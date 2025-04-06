using NLog;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Tidal.Services.Logging;
using NzbDrone.Core.Download.Clients.Tidal;
using NzbDrone.Common.Extensions;

namespace Lidarr.Plugin.Tidal.Services.FileSystem
{
    /// <summary>
    /// Implementation of <see cref="IFileValidationService"/> that validates audio files,
    /// tracks retry attempts, and manages file operations for validation.
    /// </summary>
    /// <remarks>
    /// This service provides robust validation for audio files, particularly FLAC and M4A formats,
    /// to ensure they are not corrupted before being added to the music library. It implements
    /// format-specific validation logic for each supported audio format, tracks retry attempts
    /// for failed downloads, and provides detailed diagnostics about validation failures.
    /// </remarks>
    public class FileValidationService : IFileValidationService
    {
        /// <summary>
        /// Dictionary to track retry attempts for each track by ID
        /// </summary>
        private readonly ConcurrentDictionary<string, RetryInfo> _retryAttempts = new ConcurrentDictionary<string, RetryInfo>();
        
        /// <summary>
        /// Dictionary to track temporary files created during validation
        /// </summary>
        private readonly ConcurrentDictionary<string, string> _tempFiles = new ConcurrentDictionary<string, string>();
        
        /// <summary>
        /// Directory to save raw diagnostic files
        /// </summary>
        private readonly string _diagnosticDir = Path.Combine(Path.GetTempPath(), "LidarrTidalDiagnostics");
        
        /// <inheritdoc />
        /// <remarks>
        /// This implementation performs several checks:
        /// 1. Verifies the file exists and has reasonable size
        /// 2. Format-specific validation (FLAC/M4A signatures and structure)
        /// 3. Tracks validation failures for retry management
        /// 4. Respects the configured validation settings from TidalSettings
        /// </remarks>
        public async Task<FileValidationResult> ValidateAudioFileAsync(
            string filePath, 
            string trackId, 
            TidalSharp.Data.AudioQuality quality,
            TidalSettings settings, 
            Logger logger, 
            CancellationToken cancellationToken)
        {
            // Skip validation if disabled in settings
            if (!settings.EnableFileValidation)
            {
                // Validation disabled in settings, return success
                return FileValidationResult.Success;
            }
            
            // Validate parameters
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(trackId))
            {
                logger.Error($"[TIDAL] {LogEmojis.Error} Invalid parameters for file validation");
                return FileValidationResult.Failure("Invalid Parameters", "File path or track ID is null or empty", 0, false);
            }
            
            try
            {
                logger.Debug($"[TIDAL] {LogEmojis.Info} Validating file: {Path.GetFileName(filePath)}");
                
                // Check file existence
                if (!File.Exists(filePath))
                {
                    logger.Error($"[TIDAL] {LogEmojis.Error} File validation failed: File not found at {filePath}");
                    return FileValidationResult.Failure("File Not Found", $"File does not exist at path: {filePath}", 0, true);
                }
                
                // Get retry count
                int retryCount = GetRetryCount(trackId);
                
                // Check max retries - skip validation if exceeded
                if (HasExceededMaxRetries(trackId, settings))
                {
                    logger.Warn($"[TIDAL] {LogEmojis.Warning} Track {trackId} has exceeded max retries ({settings.FileValidationMaxRetries}), skipping validation");
                    // Return valid to avoid further retries
                    return FileValidationResult.Success;
                }
                
                // Check if the file has a reasonable size
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length < settings.FileValidationMinSize * 1024) // Convert KB to bytes
                {
                    string details = $"File size ({FormatFileSize(fileInfo.Length)}) is smaller than minimum size ({settings.FileValidationMinSize} KB)";
                    logger.Error($"[TIDAL] {LogEmojis.Error} File validation failed: {details}");
                    return FileValidationResult.Failure("File Too Small", details, retryCount, true);
                }
                
                // Determine validation strategy based on file extension
                string extension = Path.GetExtension(filePath).ToLowerInvariant();
                bool isValid = false;
                string detailedInfo = string.Empty;
                
                // Call the appropriate validator based on file extension
                if (extension == ".flac")
                {
                    (isValid, detailedInfo) = await ValidateFlacFileAsync(filePath, logger, cancellationToken);
                }
                else if (extension == ".m4a")
                {
                    (isValid, detailedInfo) = await ValidateM4aFileAsync(filePath, logger, cancellationToken);
                }
                else
                {
                    // Unknown file type, less strict validation
                    (isValid, detailedInfo) = await ValidateGenericFileAsync(filePath, logger, cancellationToken);
                }
                
                // Handle validation failure
                if (!isValid)
                {
                    RecordRetryAttempt(trackId, detailedInfo, settings, logger);
                    retryCount = GetRetryCount(trackId);
                    
                    bool shouldRequeue = retryCount < settings.FileValidationMaxRetries;
                    string maxRetriesInfo = shouldRequeue ? "" : " (max retries exceeded)";
                    
                    logger.Error($"[TIDAL] {LogEmojis.Error} File validation failed{maxRetriesInfo}: {detailedInfo} - Retry count: {retryCount}/{settings.FileValidationMaxRetries}");
                    return FileValidationResult.Failure("Validation Failed", detailedInfo, retryCount, shouldRequeue);
                }
                
                // Log success and return
                logger.Debug($"[TIDAL] {LogEmojis.Success} File validation passed for {Path.GetFileName(filePath)}");
                return FileValidationResult.Success;
            }
            catch (Exception ex)
            {
                // Record this as a retry attempt
                RecordRetryAttempt(trackId, $"Exception during validation: {ex.Message}", settings, logger);
                int currentRetries = GetRetryCount(trackId);
                bool shouldRequeue = currentRetries < settings.FileValidationMaxRetries;
                
                logger.Error(ex, $"[TIDAL] {LogEmojis.Error} Exception during file validation: {ex.Message}");
                return FileValidationResult.Failure("Validation Exception", ex.Message, currentRetries, shouldRequeue);
            }
        }
        
        /// <inheritdoc />
        /// <remarks>
        /// This method cleans up both explicitly tracked temporary files and 
        /// standard pattern-based temporary files (*.tmp) associated with a download.
        /// </remarks>
        public void CleanupTempFiles(string filePath, Logger logger)
        {
            try
            {
                // Clean up any recorded temporary files
                if (_tempFiles.TryRemove(filePath, out string tempFile) && File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                    logger.Debug($"[TIDAL] {LogEmojis.File} Deleted temporary file: {Path.GetFileName(tempFile)}");
                }
                
                // Also try to clean up standard temp files
                string tempFilePath = $"{filePath}.tmp";
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                    logger.Debug($"[TIDAL] {LogEmojis.File} Deleted temporary file: {Path.GetFileName(tempFilePath)}");
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"[TIDAL] {LogEmojis.Warning} Error cleaning up temporary files: {ex.Message}");
            }
        }
        
        /// <inheritdoc />
        /// <remarks>
        /// Creates backups with a .bak extension and ensures any existing backups are removed
        /// before creating a new one to avoid old backups persisting.
        /// </remarks>
        public string CreateBackup(string filePath, Logger logger)
        {
            // Skip if file doesn't exist
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return null;
            }
            
            try
            {
                string backupPath = $"{filePath}.bak";
                
                // Remove existing backup if it exists
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
                
                // Create the backup
                File.Copy(filePath, backupPath);
                logger.Debug($"[TIDAL] {LogEmojis.File} Created backup file: {Path.GetFileName(backupPath)}");
                
                return backupPath;
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"[TIDAL] {LogEmojis.Warning} Failed to create backup file: {ex.Message}");
                return null;
            }
        }
        
        /// <inheritdoc />
        /// <remarks>
        /// Uses the internal retry tracking dictionary to determine if a track
        /// has been retried too many times and should be abandoned.
        /// </remarks>
        public bool HasExceededMaxRetries(string trackId, TidalSettings settings)
        {
            // Handle null track ID
            if (string.IsNullOrEmpty(trackId))
            {
                return false;
            }
            
            // Get the retry info, defaulting to null if not found
            _retryAttempts.TryGetValue(trackId, out RetryInfo retryInfo);
            
            // Compare retry count to max retry setting
            return retryInfo?.RetryCount >= settings.FileValidationMaxRetries;
        }
        
        /// <inheritdoc />
        /// <remarks>
        /// Tracks both the retry count and the reason for each retry attempt,
        /// allowing for detailed diagnostics about persistent download issues.
        /// </remarks>
        public void RecordRetryAttempt(string trackId, string reason, TidalSettings settings, Logger logger)
        {
            // Skip for null track ID
            if (string.IsNullOrEmpty(trackId))
            {
                return;
            }
            
            // Get or create retry info
            var retryInfo = _retryAttempts.GetOrAdd(trackId, new RetryInfo());
            
            // Increment retry count
            retryInfo.RetryCount++;
            retryInfo.LastRetryReason = reason;
            retryInfo.LastRetryTime = DateTime.UtcNow;
            
            // Log retry statistics
            logger.Warn($"[TIDAL] {LogEmojis.Warning} Track {trackId} retry {retryInfo.RetryCount}/{settings.FileValidationMaxRetries}: {reason}");
        }
        
        /// <inheritdoc />
        /// <remarks>
        /// Completely clears all retry tracking information, allowing previously
        /// failed tracks to be retried from scratch.
        /// </remarks>
        public void ResetRetryCounters()
        {
            _retryAttempts.Clear();
        }
        
        /// <summary>
        /// Validates a FLAC file by checking its header and structure
        /// </summary>
        /// <param name="filePath">Path to the FLAC file</param>
        /// <param name="logger">Logger for validation feedback</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A tuple with validation result and details</returns>
        /// <remarks>
        /// This method performs multiple validations:
        /// 1. Checks for "fLaC" signature at the file start
        /// 2. Verifies presence of STREAMINFO block which must exist in valid FLAC files
        /// 3. Ensures the file has sufficient data beyond the header
        /// </remarks>
        private async Task<(bool isValid, string details)> ValidateFlacFileAsync(string filePath, Logger logger, CancellationToken cancellationToken)
        {
            try
            {
                // Retry loop for high-concurrency situations where file might be temporarily locked
                for (int retry = 0; retry < 3; retry++)
                {
                    try
                    {
                        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        
                        // FLAC files should start with "fLaC" signature
                        byte[] headerBuffer = new byte[8]; // Read 8 bytes to also check for possible M4A header
                        int bytesRead = await fileStream.ReadAsync(headerBuffer, 0, 8, cancellationToken);
                        
                        if (bytesRead < 4)
                        {
                            // In high-concurrency situations, the file might not be completely written
                            // Wait a moment and retry instead of immediately failing
                            if (retry < 2)
                            {
                                logger.Debug($"FLAC file too small to contain a valid header, waiting and retrying ({retry + 1}/3)");
                                await Task.Delay(500, cancellationToken);
                                continue;
                            }
                            return (false, "FLAC file too small to contain a valid header");
                        }
                        
                        // Check for "fLaC" signature (0x66, 0x4C, 0x61, 0x43)
                        bool isFlac = headerBuffer[0] == 0x66 && headerBuffer[1] == 0x4C && 
                                      headerBuffer[2] == 0x61 && headerBuffer[3] == 0x43;
                        
                        // More detailed logging for debugging format issues
                        string hexSignature = BitConverter.ToString(headerBuffer, 0, Math.Min(8, bytesRead));
                        logger.Debug($"File signature: {hexSignature}, IsFlac: {isFlac}");
                        
                        // Check for M4A/ISO/MP4 signature ('ftyp')
                        bool isFtyp = false;
                        if (bytesRead >= 8 && !isFlac)
                        {
                            isFtyp = headerBuffer[4] == 0x66 && headerBuffer[5] == 0x74 && 
                                     headerBuffer[6] == 0x79 && headerBuffer[7] == 0x70;
                            
                            if (isFtyp)
                            {
                                logger.Warn($"[TIDAL] {LogEmojis.Warning} File has .flac extension but contains M4A/MP4 content (found 'ftyp' marker)");
                                
                                // If this is actually an M4A file, delegate to the M4A validator to confirm it's valid
                                return await ValidateM4aFileAsync(filePath, logger, cancellationToken);
                            }
                        }
                        
                        // Rest of the original validation logic...
                        // If not FLAC or known M4A, search for other common M4A markers in first 32 bytes
                        if (!isFlac)
                        {
                            fileStream.Seek(0, SeekOrigin.Begin);
                            byte[] searchBuffer = new byte[32];
                            bytesRead = await fileStream.ReadAsync(searchBuffer, 0, Math.Min(searchBuffer.Length, (int)fileStream.Length), cancellationToken);
                            
                            for (int i = 0; i < bytesRead - 4; i++)
                            {
                                // Check for 'ftyp' marker
                                if (searchBuffer[i] == 0x66 && searchBuffer[i+1] == 0x74 && 
                                    searchBuffer[i+2] == 0x79 && searchBuffer[i+3] == 0x70)
                                {
                                    logger.Warn($"[TIDAL] {LogEmojis.Warning} File has .flac extension but contains M4A/MP4 content (found 'ftyp' marker at offset {i})");
                                    return await ValidateM4aFileAsync(filePath, logger, cancellationToken);
                                }
                                
                                // Check for 'moov' or 'mdat' markers which indicate MP4 container
                                if ((searchBuffer[i] == 0x6D && searchBuffer[i+1] == 0x6F && 
                                     searchBuffer[i+2] == 0x6F && searchBuffer[i+3] == 0x76) ||
                                    (searchBuffer[i] == 0x6D && searchBuffer[i+1] == 0x64 && 
                                     searchBuffer[i+2] == 0x61 && searchBuffer[i+3] == 0x74))
                                {
                                    logger.Warn($"[TIDAL] {LogEmojis.Warning} File has .flac extension but contains M4A/MP4 content (found MP4 marker at offset {i})");
                                    return await ValidateM4aFileAsync(filePath, logger, cancellationToken);
                                }
                            }
                            
                            // At this point, if it's not a FLAC file and we didn't identify it as an MP4/M4A file,
                            // then it's truly invalid
                            return (false, "Invalid FLAC signature and no M4A/MP4 markers found");
                        }

                        // Continue with standard FLAC validation if the file has a FLAC signature
                        // Try to read basic metadata
                        byte[] metadataBuffer = new byte[42]; // Enough for basic FLAC header checking
                        fileStream.Seek(0, SeekOrigin.Begin);
                        
                        bytesRead = await fileStream.ReadAsync(metadataBuffer, 0, metadataBuffer.Length, cancellationToken);
                        if (bytesRead < 42)
                        {
                            if (retry < 2)
                            {
                                logger.Debug($"FLAC file too small to contain complete metadata, waiting and retrying ({retry + 1}/3)");
                                await Task.Delay(500, cancellationToken);
                                continue;
                            }
                            return (false, "FLAC file too small to contain complete metadata");
                        }
                        
                        // Look for STREAMINFO block (type 0) which should be present in all valid FLAC files
                        bool foundStreamInfo = false;
                        for (int i = 4; i < metadataBuffer.Length - 4; i++)
                        {
                            // Check for metadata block header
                            if ((metadataBuffer[i] & 0x7F) == 0)
                            {
                                foundStreamInfo = true;
                                break;
                            }
                        }
                        
                        if (!foundStreamInfo)
                        {
                            if (retry < 2 && await IsFileStillBeingWrittenAsync(filePath, logger, cancellationToken))
                            {
                                logger.Debug($"FLAC file appears to be incomplete, waiting and retrying ({retry + 1}/3)");
                                await Task.Delay(500, cancellationToken);
                                continue;
                            }
                            return (false, "FLAC file missing STREAMINFO block");
                        }
                        
                        // Sample additional check - ensure file has enough data beyond the header
                        var fileInfo = new FileInfo(filePath);
                        if (fileInfo.Length < 1024 * 16) // At least 16KB for a minimal FLAC
                        {
                            return (false, $"FLAC file too small ({FormatFileSize(fileInfo.Length)}) to contain audio data");
                        }
                        
                        // If we've got this far in the current attempt, the file is valid
                        return (true, "FLAC validation passed");
                    }
                    catch (IOException ex) when (IsFileLockException(ex) && retry < 2)
                    {
                        // If the file is locked (might be still being written by another thread),
                        // wait and retry instead of failing immediately
                        logger.Debug($"File locked during validation, waiting and retrying ({retry + 1}/3): {ex.Message}");
                        await Task.Delay(500, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        // On other exceptions, just return the error
                        return (false, $"FLAC validation exception: {ex.Message}");
                    }
                }
                
                // If we've tried 3 times and still have issues, fail with a generic message
                return (false, "FLAC validation failed after multiple attempts");
            }
            catch (Exception ex)
            {
                return (false, $"FLAC validation exception: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Checks if a file appears to still be in the process of being written
        /// </summary>
        private async Task<bool> IsFileStillBeingWrittenAsync(string filePath, Logger logger, CancellationToken cancellationToken)
        {
            try
            {
                // Get initial file size
                var initialInfo = new FileInfo(filePath);
                if (!initialInfo.Exists)
                {
                    return false;
                }
                
                long initialSize = initialInfo.Length;
                
                // Wait a moment
                await Task.Delay(200, cancellationToken);
                
                // Check size again
                var currentInfo = new FileInfo(filePath);
                if (!currentInfo.Exists)
                {
                    return false;
                }
                
                long currentSize = currentInfo.Length;
                
                // If size changed, the file is likely still being written
                bool isStillWriting = currentSize > initialSize;
                if (isStillWriting)
                {
                    logger.Debug($"File size changed from {initialSize} to {currentSize} bytes during check, file is still being written");
                }
                
                return isStillWriting;
            }
            catch
            {
                // In case of exceptions, assume the file is not still being written
                return false;
            }
        }
        
        /// <summary>
        /// Determines if an exception is related to file locking
        /// </summary>
        private bool IsFileLockException(Exception ex)
        {
            // Different error codes or messages that indicate the file is locked
            return ex.Message.Contains("being used by another process") || 
                   ex.Message.Contains("access is denied") ||
                   ex.Message.Contains("locked");
        }
        
        /// <summary>
        /// Validates an M4A file by checking its atom structure
        /// </summary>
        /// <param name="filePath">Path to the M4A file</param>
        /// <param name="logger">Logger for validation feedback</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A tuple with validation result and details</returns>
        /// <remarks>
        /// This method validates M4A files by:
        /// 1. Checking for 'ftyp' atom which defines the file type
        /// 2. Looking for 'moov' atom which contains metadata (optional but common)
        /// 3. Verifying the file is large enough to contain audio data
        /// M4A validation is more lenient than FLAC since the format is more variable.
        /// </remarks>
        private async Task<(bool isValid, string details)> ValidateM4aFileAsync(string filePath, Logger logger, CancellationToken cancellationToken)
        {
            try
            {
                // Retry loop for high-concurrency situations
                for (int retry = 0; retry < 3; retry++)
                {
                    try
                    {
                        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        
                        // Check the first 64 bytes for inspection and logging
                        byte[] signatureBytes = new byte[64];
                        int signatureBytesRead = await fileStream.ReadAsync(signatureBytes, 0, Math.Min(64, (int)fileStream.Length), cancellationToken);
                        
                        if (signatureBytesRead < 8)
                        {
                            // File might still be being written
                            if (retry < 2)
                            {
                                logger.Debug($"M4A file too small to analyze signature, waiting and retrying ({retry + 1}/3)");
                                await Task.Delay(500, cancellationToken);
                                continue;
                            }
                            return (false, "File too small to be a valid M4A file");
                        }
                        
                        string hexDump = BitConverter.ToString(signatureBytes, 0, signatureBytesRead);
                        logger.Debug($"[TIDAL] {LogEmojis.Debug} M4A validation - File signature first 64 bytes: {hexDump}");
                        
                        // Check for common FLAC signature in case TidalSharp mislabeled the file
                        if (signatureBytesRead >= 4 && 
                            signatureBytes[0] == 0x66 && signatureBytes[1] == 0x4C && 
                            signatureBytes[2] == 0x61 && signatureBytes[3] == 0x43)
                        {
                            logger.Warn($"[TIDAL] {LogEmojis.Warning} File extension is .m4a but file has FLAC signature!");
                            // Return true for FLAC files with wrong extension - we'll handle them properly
                            return (true, "File appears to be FLAC despite .m4a extension");
                        }
                        
                        // M4A files start with ftyp atom
                        fileStream.Seek(0, SeekOrigin.Begin);
                        byte[] atomHeader = new byte[8]; // 4 bytes size, 4 bytes type
                        int bytesRead = await fileStream.ReadAsync(atomHeader, 0, Math.Min(8, (int)fileStream.Length), cancellationToken);
                        
                        if (bytesRead < 8)
                        {
                            if (retry < 2 && await IsFileStillBeingWrittenAsync(filePath, logger, cancellationToken))
                            {
                                logger.Debug($"M4A file too small, waiting and retrying ({retry + 1}/3)");
                                await Task.Delay(500, cancellationToken);
                                continue;
                            }
                            return (false, "M4A file too small to contain a valid header");
                        }
                        
                        // Rest of the original validation logic
                        // Check for 'ftyp' atom which should be at the start
                        bool hasValidHeader = false;
                        
                        // Skip first 4 bytes (size) and check next 4 bytes for "ftyp"
                        if (atomHeader[4] == 0x66 && atomHeader[5] == 0x74 && 
                            atomHeader[6] == 0x79 && atomHeader[7] == 0x70)
                        {
                            hasValidHeader = true;
                        }
                        
                        // If no ftyp at start, check for other common atoms
                        if (!hasValidHeader)
                        {
                            // Read more of the file to look for common markers
                            fileStream.Seek(0, SeekOrigin.Begin);
                            byte[] searchBuffer = new byte[64];
                            int searchBytesRead = await fileStream.ReadAsync(searchBuffer, 0, Math.Min(64, (int)fileStream.Length), cancellationToken);
                            
                            for (int i = 0; i < searchBytesRead - 4; i++)
                            {
                                // Check for common MP4 container markers
                                if ((searchBuffer[i] == 0x6D && searchBuffer[i+1] == 0x6F && 
                                     searchBuffer[i+2] == 0x6F && searchBuffer[i+3] == 0x76) || // 'moov'
                                    (searchBuffer[i] == 0x6D && searchBuffer[i+1] == 0x64 && 
                                     searchBuffer[i+2] == 0x61 && searchBuffer[i+3] == 0x74) || // 'mdat'
                                    (searchBuffer[i] == 0x66 && searchBuffer[i+1] == 0x74 && 
                                     searchBuffer[i+2] == 0x79 && searchBuffer[i+3] == 0x70))   // 'ftyp'
                                {
                                    hasValidHeader = true;
                                    logger.Debug($"[TIDAL] {LogEmojis.Debug} Found valid M4A/MP4 marker at offset {i}");
                                    break;
                                }
                            }
                        }
                        
                        if (!hasValidHeader)
                        {
                            if (retry < 2 && await IsFileStillBeingWrittenAsync(filePath, logger, cancellationToken))
                            {
                                logger.Debug($"M4A file appears incomplete, waiting and retrying ({retry + 1}/3)");
                                await Task.Delay(500, cancellationToken);
                                continue;
                            }
                            return (false, "M4A file missing valid MP4 container markers");
                        }
                        
                        // File must have reasonable size - M4A files under 16KB are likely invalid
                        var fileInfo = new FileInfo(filePath);
                        if (fileInfo.Length < 1024 * 16)
                        {
                            if (retry < 2 && await IsFileStillBeingWrittenAsync(filePath, logger, cancellationToken))
                            {
                                logger.Debug($"M4A file too small, waiting and retrying ({retry + 1}/3)");
                                await Task.Delay(500, cancellationToken);
                                continue;
                            }
                            return (false, $"M4A file too small ({FormatFileSize(fileInfo.Length)}) to contain audio data");
                        }
                        
                        // If we get here, the file passed validation
                        return (true, "M4A validation passed");
                    }
                    catch (IOException ex) when (IsFileLockException(ex) && retry < 2)
                    {
                        // If the file is locked (might be still being written by another thread),
                        // wait and retry instead of failing immediately
                        logger.Debug($"File locked during M4A validation, waiting and retrying ({retry + 1}/3): {ex.Message}");
                        await Task.Delay(500, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        // On other exceptions, just return the error
                        return (false, $"M4A validation exception: {ex.Message}");
                    }
                }
                
                // If we've tried 3 times and still have issues, fail with a generic message
                return (false, "M4A validation failed after multiple attempts");
            }
            catch (Exception ex)
            {
                return (false, $"M4A validation exception: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Basic validation for files with unknown format - just checks size and readability
        /// </summary>
        /// <param name="filePath">Path to the file</param>
        /// <param name="logger">Logger for validation feedback</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A tuple with validation result and details</returns>
        /// <remarks>
        /// For unknown formats, we perform minimal validation:
        /// 1. Verify that the file is readable (I/O operations succeed)
        /// 2. Check that the file has a minimum size (16KB)
        /// This is a fallback for formats we don't explicitly support.
        /// </remarks>
        private async Task<(bool isValid, string details)> ValidateGenericFileAsync(string filePath, Logger logger, CancellationToken cancellationToken)
        {
            try
            {
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                
                // Check if file is readable - try to read first 1KB
                byte[] buffer = new byte[1024];
                int bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                
                if (bytesRead <= 0)
                {
                    return (false, "File is not readable");
                }
                
                // Check file size
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length < 1024 * 16) // At least 16KB for any audio file
                {
                    return (false, $"File too small ({FormatFileSize(fileInfo.Length)}) to be a valid audio file");
                }
                
                return (true, "Basic file validation passed");
            }
            catch (Exception ex)
            {
                return (false, $"File validation exception: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Gets the current retry count for a track
        /// </summary>
        /// <param name="trackId">Tidal track ID</param>
        /// <returns>Number of retry attempts for the track, or 0 if none recorded</returns>
        private int GetRetryCount(string trackId)
        {
            if (string.IsNullOrEmpty(trackId) || !_retryAttempts.TryGetValue(trackId, out RetryInfo retryInfo))
            {
                return 0;
            }
            
            return retryInfo.RetryCount;
        }
        
        /// <summary>
        /// Formats a file size in bytes to a human-readable string
        /// </summary>
        /// <param name="bytes">Size in bytes</param>
        /// <returns>Formatted string with appropriate units (B, KB, MB, etc.)</returns>
        private string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int suffixIndex = 0;
            double size = bytes;
            
            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }
            
            return $"{size:0.##} {suffixes[suffixIndex]}";
        }
        
        /// <summary>
        /// Class to track retry information for a track
        /// </summary>
        private class RetryInfo
        {
            /// <summary>
            /// Number of retry attempts
            /// </summary>
            public int RetryCount { get; set; }
            
            /// <summary>
            /// Reason for the last retry
            /// </summary>
            public string LastRetryReason { get; set; }
            
            /// <summary>
            /// Time of the last retry
            /// </summary>
            public DateTime LastRetryTime { get; set; }
        }

        /// <summary>
        /// Saves a copy of the raw file data for diagnostics
        /// </summary>
        /// <param name="filePath">Path to the file to analyze</param>
        /// <param name="trackId">ID of the track</param>
        /// <param name="quality">Quality setting used for download</param>
        /// <param name="logger">Logger for feedback</param>
        /// <param name="settings">Tidal settings containing download path</param>
        /// <returns>Path to the saved diagnostic file</returns>
        public string SaveRawFileForDiagnostics(string filePath, string trackId, TidalSharp.Data.AudioQuality quality, 
                                              Logger logger, NzbDrone.Core.Download.Clients.Tidal.TidalSettings settings)
        {
            try
            {
                // Create diagnostics directory in the download path instead of system temp
                string downloadPath = settings?.DownloadPath ?? Path.GetDirectoryName(filePath);
                string diagnosticDir = Path.Combine(downloadPath, "diagnostics");
                
                if (!Directory.Exists(diagnosticDir))
                {
                    Directory.CreateDirectory(diagnosticDir);
                }

                // Create a unique name for the diagnostic file
                string diagnosticFileName = $"tidal_raw_{DateTime.Now:yyyyMMdd_HHmmss}_{trackId}_{quality}.bin";
                string diagnosticFilePath = Path.Combine(diagnosticDir, diagnosticFileName);
                
                // Copy the first 1MB of the file (or less if the file is smaller)
                using (var sourceStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var destStream = new FileStream(diagnosticFilePath, FileMode.Create))
                {
                    int bufferSize = 1024;
                    byte[] buffer = new byte[bufferSize];
                    int bytesRead;
                    long totalBytesRead = 0;
                    long maxBytes = 1024 * 1024; // 1MB max for diagnostic files
                    
                    while ((bytesRead = sourceStream.Read(buffer, 0, bufferSize)) > 0 && totalBytesRead < maxBytes)
                    {
                        destStream.Write(buffer, 0, bytesRead);
                        totalBytesRead += bytesRead;
                    }
                }
                
                // Create a text file with diagnostic information
                string infoFilePath = Path.Combine(diagnosticDir, Path.GetFileNameWithoutExtension(diagnosticFileName) + "_info.txt");
                using (var writer = new StreamWriter(infoFilePath))
                {
                    writer.WriteLine($"Tidal Diagnostic Information");
                    writer.WriteLine($"----------------------------");
                    writer.WriteLine($"Date: {DateTime.Now}");
                    writer.WriteLine($"Track ID: {trackId}");
                    writer.WriteLine($"Quality: {quality}");
                    writer.WriteLine($"Original file: {filePath}");
                    writer.WriteLine($"Original file size: {new FileInfo(filePath).Length} bytes");
                    
                    // Add hex dump of first 256 bytes
                    writer.WriteLine();
                    writer.WriteLine("First 256 bytes (hex dump):");
                    writer.WriteLine("----------------------------");
                    
                    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        byte[] headerBytes = new byte[256];
                        int headerBytesRead = stream.Read(headerBytes, 0, headerBytes.Length);
                        
                        for (int i = 0; i < headerBytesRead; i += 16)
                        {
                            // Print offset
                            writer.Write($"{i:X4}: ");
                            
                            // Print hex values
                            for (int j = 0; j < 16; j++)
                            {
                                if (i + j < headerBytesRead)
                                    writer.Write($"{headerBytes[i + j]:X2} ");
                                else
                                    writer.Write("   ");
                                
                                // Add extra space in the middle
                                if (j == 7)
                                    writer.Write(" ");
                            }
                            
                            writer.Write(" | ");
                            
                            // Print ASCII representation
                            for (int j = 0; j < 16; j++)
                            {
                                if (i + j < headerBytesRead)
                                {
                                    byte b = headerBytes[i + j];
                                    char c = (b >= 32 && b <= 126) ? (char)b : '.';
                                    writer.Write(c);
                                }
                            }
                            
                            writer.WriteLine();
                        }
                    }
                }
                
                logger.Info($"[TIDAL] {LogEmojis.Debug} Created diagnostic files in download folder:");
                logger.Info($"[TIDAL] {LogEmojis.Debug} Raw data: {diagnosticFilePath}");
                logger.Info($"[TIDAL] {LogEmojis.Debug} Info file: {infoFilePath}");
                
                return diagnosticFilePath;
            }
            catch (Exception ex)
            {
                logger.Error($"[TIDAL] {LogEmojis.Error} Failed to save diagnostic file: {ex.Message}");
                return null;
            }
        }
    }
} 