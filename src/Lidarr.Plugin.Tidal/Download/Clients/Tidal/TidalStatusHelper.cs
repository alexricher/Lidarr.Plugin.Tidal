using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Tidal.Services.Logging;
using NLog;

namespace NzbDrone.Core.Download.Clients.Tidal
{
    public class TidalStatusHelper
    {
        private readonly string _statusFilesPath;
        private readonly Logger _logger;
        private readonly bool _enabled;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks;
        private const int DEFAULT_TIMEOUT_MS = 5000; // 5 seconds timeout for file operations

        public TidalStatusHelper(string statusFilesPath, Logger logger)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));

            _statusFilesPath = statusFilesPath;
            _logger = logger;
            _enabled = !string.IsNullOrWhiteSpace(statusFilesPath);
            _fileLocks = new ConcurrentDictionary<string, SemaphoreSlim>();

            if (_enabled)
            {
                EnsureDirectory();
            }
            else
            {
                _logger.WarnWithEmoji(LogEmojis.Warning, "TidalStatusHelper initialized with empty statusFilesPath - status tracking is disabled");
            }
        }

        public bool IsEnabled => _enabled;

        public string StatusFilesPath => _statusFilesPath;

        // Gets a semaphore for a specific file to handle concurrent access
        private SemaphoreSlim GetFileLock(string filePath)
        {
            return _fileLocks.GetOrAdd(filePath, new SemaphoreSlim(1, 1));
        }

        private void EnsureDirectory()
        {
            if (!_enabled) return;

            try
            {
                if (!Directory.Exists(_statusFilesPath))
                {
                    _logger.InfoWithEmoji(LogEmojis.Folder, $"Creating status directory at: {_statusFilesPath}");
                    Directory.CreateDirectory(_statusFilesPath);
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorWithEmoji(LogEmojis.Error, ex, $"Failed to create status directory: {_statusFilesPath}");
            }
        }

        // New async version of WriteJsonFile
        public async Task<bool> WriteJsonFileAsync<T>(string fileName, T data, CancellationToken cancellationToken = default)
        {
            if (!_enabled) return false;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                _logger.WarnWithEmoji(LogEmojis.Warning, "Cannot write JSON file with empty filename");
                return false;
            }

            try
            {
                // Always check if directory exists before writing
                EnsureDirectory();

                var filePath = Path.Combine(_statusFilesPath, fileName);
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(data, options);

                _logger.DebugWithEmoji(LogEmojis.File, $"Writing JSON file: {filePath}");

                // Use file lock to prevent concurrent writes
                var fileLock = GetFileLock(filePath);
                bool acquired = false;

                try
                {
                    // Try to acquire lock with timeout
                    acquired = await fileLock.WaitAsync(DEFAULT_TIMEOUT_MS, cancellationToken);
                    if (!acquired)
                    {
                        _logger.WarnWithEmoji(LogEmojis.Warning, $"Timeout waiting for file lock on {filePath}");
                        return false;
                    }

                    // Use retries for file operations
                    int maxRetries = 3;
                    int retryDelay = 500; // ms

                    for (int i = 0; i < maxRetries; i++)
                    {
                        try
                        {
                            // Ensure directory exists again before write
                            if (!Directory.Exists(Path.GetDirectoryName(filePath)))
                            {
                                _logger.Warn($"Directory not found when writing file, recreating: {_statusFilesPath}");
                                EnsureDirectory();
                            }

                            await File.WriteAllTextAsync(filePath, json, cancellationToken);
                            _logger.Debug($"Successfully wrote JSON file: {filePath} (size: {json.Length} bytes)");
                            return true;
                        }
                        catch (DirectoryNotFoundException)
                        {
                            _logger.Warn($"Directory not found when writing file, recreating: {_statusFilesPath}");
                            EnsureDirectory();

                            if (i < maxRetries - 1)
                            {
                                await Task.Delay(retryDelay, cancellationToken);
                                retryDelay *= 2; // Exponential backoff
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug($"Failed to write JSON file (attempt {i+1}/{maxRetries}): {ex.Message}");

                            if (i < maxRetries - 1)
                            {
                                await Task.Delay(retryDelay, cancellationToken);
                                retryDelay *= 2; // Exponential backoff
                            }
                        }
                    }

                    return false;
                }
                finally
                {
                    // Release the lock if we acquired it
                    if (acquired)
                    {
                        fileLock.Release();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to write JSON file {fileName}");
                return false;
            }
        }

        // Synchronous version for backward compatibility
        public bool WriteJsonFile<T>(string fileName, T data)
        {
            if (!_enabled) return false;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                _logger.Warn("Cannot write JSON file with empty filename");
                return false;
            }

            try
            {
                // Always check if directory exists before writing
                EnsureDirectory();

                var filePath = Path.Combine(_statusFilesPath, fileName);
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(data, options);

                _logger.Debug($"Writing JSON file: {filePath}");

                // Use file lock to prevent concurrent writes
                var fileLock = GetFileLock(filePath);
                bool acquired = false;

                try
                {
                    // Try to acquire lock with timeout
                    acquired = fileLock.Wait(DEFAULT_TIMEOUT_MS);
                    if (!acquired)
                    {
                        _logger.Warn($"Timeout waiting for file lock on {filePath}");
                        return false;
                    }

                    // Use retries for file operations
                    int maxRetries = 3;
                    int retryDelay = 500; // ms

                    for (int i = 0; i < maxRetries; i++)
                    {
                        try
                        {
                            // Ensure directory exists again before write
                            if (!Directory.Exists(Path.GetDirectoryName(filePath)))
                            {
                                _logger.Warn($"Directory not found when writing file, recreating: {_statusFilesPath}");
                                EnsureDirectory();
                            }

                            File.WriteAllText(filePath, json);
                            _logger.Debug($"Successfully wrote JSON file: {filePath} (size: {json.Length} bytes)");
                            return true;
                        }
                        catch (DirectoryNotFoundException)
                        {
                            _logger.Warn($"Directory not found when writing file, recreating: {_statusFilesPath}");
                            EnsureDirectory();

                            if (i < maxRetries - 1)
                            {
                                Thread.Sleep(retryDelay);
                                retryDelay *= 2; // Exponential backoff
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug($"Failed to write JSON file (attempt {i+1}/{maxRetries}): {ex.Message}");

                            if (i < maxRetries - 1)
                            {
                                Thread.Sleep(retryDelay);
                                retryDelay *= 2; // Exponential backoff
                            }
                        }
                    }

                    return false;
                }
                finally
                {
                    // Release the lock if we acquired it
                    if (acquired)
                    {
                        fileLock.Release();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to write JSON file {fileName}");
                return false;
            }
        }

        // Alternative non-generic version for backward compatibility
        public bool WriteJsonFile(string fileName, string content)
        {
            if (!_enabled) return false;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                _logger.Warn("Cannot write JSON file with empty filename");
                return false;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.Warn("Cannot write empty content to JSON file");
                return false;
            }

            try
            {
                // Always check if directory exists before writing
                EnsureDirectory();

                var filePath = Path.Combine(_statusFilesPath, fileName);

                _logger.Debug($"Writing JSON content to file: {filePath}");

                // Use file lock to prevent concurrent writes
                var fileLock = GetFileLock(filePath);
                bool acquired = false;

                try
                {
                    // Try to acquire lock with timeout
                    acquired = fileLock.Wait(DEFAULT_TIMEOUT_MS);
                    if (!acquired)
                    {
                        _logger.Warn($"Timeout waiting for file lock on {filePath}");
                        return false;
                    }

                    // Validate that the content is valid JSON
                    try
                    {
                        JsonDocument.Parse(content);
                    }
                    catch (JsonException)
                    {
                        _logger.Warn($"Attempted to write invalid JSON to file: {filePath}");
                        return false;
                    }

                    // Use retries for file operations
                    int maxRetries = 3;
                    int retryDelay = 500; // ms

                    for (int i = 0; i < maxRetries; i++)
                    {
                        try
                        {
                            File.WriteAllText(filePath, content);
                            _logger.Debug($"Successfully wrote JSON file: {filePath} (size: {content.Length} bytes)");
                            return true;
                        }
                        catch (DirectoryNotFoundException)
                        {
                            _logger.Warn($"Directory not found when writing file, recreating: {_statusFilesPath}");
                            EnsureDirectory();

                            if (i < maxRetries - 1)
                            {
                                Thread.Sleep(retryDelay);
                                retryDelay *= 2; // Exponential backoff
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug($"Failed to write JSON file (attempt {i+1}/{maxRetries}): {ex.Message}");

                            if (i < maxRetries - 1)
                            {
                                Thread.Sleep(retryDelay);
                                retryDelay *= 2; // Exponential backoff
                            }
                        }
                    }

                    return false;
                }
                finally
                {
                    // Release the lock if we acquired it
                    if (acquired)
                    {
                        fileLock.Release();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to write JSON file {fileName}");
                return false;
            }
        }

        // New async version of ReadJsonFile
        public async Task<T> ReadJsonFileAsync<T>(string fileName, CancellationToken cancellationToken = default) where T : class
        {
            if (!_enabled) return default;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                _logger.Warn("Cannot read JSON file with empty filename");
                return default;
            }

            try
            {
                var filePath = Path.Combine(_statusFilesPath, fileName);
                if (!File.Exists(filePath))
                {
                    _logger.Debug($"JSON file does not exist: {filePath}");
                    return default;
                }

                _logger.Debug($"Reading JSON file: {filePath}");

                // Use file lock to prevent reading while file is being written
                var fileLock = GetFileLock(filePath);
                bool acquired = false;

                try
                {
                    // Try to acquire lock with timeout
                    acquired = await fileLock.WaitAsync(DEFAULT_TIMEOUT_MS, cancellationToken);
                    if (!acquired)
                    {
                        _logger.Warn($"Timeout waiting for file lock on {filePath}");
                        return default;
                    }

                    // Use retries for file operations
                    int maxRetries = 3;
                    int retryDelay = 500; // ms

                    for (int i = 0; i < maxRetries; i++)
                    {
                        try
                        {
                            // Check if file still exists
                            if (!File.Exists(filePath))
                            {
                                _logger.Debug($"File no longer exists: {filePath}");
                                return default;
                            }

                            string json = await File.ReadAllTextAsync(filePath, cancellationToken);

                            // Validate JSON content
                            if (string.IsNullOrWhiteSpace(json))
                            {
                                _logger.Warn($"JSON file is empty: {filePath}");
                                return default;
                            }

                            try
                            {
                                var options = new JsonSerializerOptions
                                {
                                    PropertyNameCaseInsensitive = true,
                                    AllowTrailingCommas = true, // More lenient parsing
                                    ReadCommentHandling = JsonCommentHandling.Skip
                                };

                                T result = JsonSerializer.Deserialize<T>(json, options);
                                return result;
                            }
                            catch (JsonException jsonEx)
                            {
                                _logger.Warn(jsonEx, $"Failed to deserialize JSON from {filePath}");
                                return default;
                            }
                        }
                        catch (FileNotFoundException)
                        {
                            _logger.Debug($"File not found: {filePath}");
                            return default;
                        }
                        catch (Exception ex)
                        {
                            _logger.Warn($"Failed to read JSON file (attempt {i+1}/{maxRetries}): {ex.Message}");

                            if (i < maxRetries - 1)
                            {
                                await Task.Delay(retryDelay, cancellationToken);
                                retryDelay *= 2; // Exponential backoff
                            }
                        }
                    }

                    return default;
                }
                finally
                {
                    // Release the lock if we acquired it
                    if (acquired)
                    {
                        fileLock.Release();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to read JSON file {fileName}");
                return default;
            }
        }

        // Synchronous version for backward compatibility
        public T ReadJsonFile<T>(string fileName) where T : class
        {
            if (!_enabled) return null;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                _logger.Warn("Cannot read JSON file with empty filename");
                return null;
            }

            try
            {
                // Check if directory exists before reading
                if (!Directory.Exists(_statusFilesPath))
                {
                    _logger.Warn($"Status directory not found when reading file, recreating: {_statusFilesPath}");
                    EnsureDirectory();
                }

                var filePath = Path.Combine(_statusFilesPath, fileName);
                if (!File.Exists(filePath))
                {
                    _logger.Debug($"JSON file does not exist: {filePath}");
                    return null;
                }

                // Use file lock to prevent reading during writes
                var fileLock = GetFileLock(filePath);
                bool acquired = false;

                try
                {
                    // Try to acquire lock with timeout
                    acquired = fileLock.Wait(DEFAULT_TIMEOUT_MS);
                    if (!acquired)
                    {
                        _logger.Warn($"Timeout waiting for file lock on {filePath}");
                        return null;
                    }

                    // Use retries for file operations
                    int maxRetries = 3;
                    int retryDelay = 500; // ms
                    Exception lastException = null;

                    for (int i = 0; i < maxRetries; i++)
                    {
                        try
                        {
                            _logger.Debug($"Reading JSON file: {filePath} (attempt {i+1}/{maxRetries})");
                            var content = File.ReadAllText(filePath);
                            var result = JsonSerializer.Deserialize<T>(content);
                            _logger.Debug($"Successfully read and parsed JSON file: {filePath}");
                            return result;
                        }
                        catch (DirectoryNotFoundException)
                        {
                            _logger.Warn($"Directory not found when reading file, recreating: {_statusFilesPath}");
                            EnsureDirectory();

                            if (i < maxRetries - 1)
                            {
                                Thread.Sleep(retryDelay);
                                retryDelay *= 2; // Exponential backoff
                            }
                        }
                        catch (JsonException jex)
                        {
                            // If JSON parsing fails, this is likely a corrupted file
                            _logger.Warn(jex, $"Failed to parse JSON file (corrupted?): {fileName}");

                            // Backup the corrupted file
                            try
                            {
                                var backupPath = filePath + $".corrupted.{DateTime.Now:yyyyMMdd_HHmmss}.bak";
                                File.Copy(filePath, backupPath);
                                _logger.Info($"Backed up corrupted JSON file to: {backupPath}");
                            }
                            catch (Exception bex)
                            {
                                _logger.Debug($"Failed to back up corrupted JSON file: {bex.Message}");
                            }

                            return null;
                        }
                        catch (Exception ex)
                        {
                            lastException = ex;
                            _logger.Debug($"Failed to read JSON file (attempt {i+1}/{maxRetries}): {ex.Message}");

                            if (i < maxRetries - 1)
                            {
                                Thread.Sleep(retryDelay);
                                retryDelay *= 2; // Exponential backoff
                            }
                        }
                    }

                    // If we get here, all retries failed
                    _logger.Error(lastException, $"Failed to read JSON file {fileName} after {maxRetries} attempts");
                    return null;
                }
                finally
                {
                    // Release the lock if we acquired it
                    if (acquired)
                    {
                        fileLock.Release();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to read JSON file {fileName}");
                return null;
            }
        }

        // Async version of DeleteFile
        public async Task<bool> DeleteFileAsync(string fileName, CancellationToken cancellationToken = default)
        {
            if (!_enabled) return false;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                _logger.Warn("Cannot delete file with empty filename");
                return false;
            }

            try
            {
                var filePath = Path.Combine(_statusFilesPath, fileName);
                if (!File.Exists(filePath))
                {
                    _logger.Debug($"File does not exist (cannot delete): {filePath}");
                    return false;
                }

                // Use file lock to ensure no one is reading/writing
                var fileLock = GetFileLock(filePath);
                bool acquired = false;

                try
                {
                    acquired = await fileLock.WaitAsync(DEFAULT_TIMEOUT_MS, cancellationToken);
                    if (!acquired)
                    {
                        _logger.Warn($"Timeout waiting for file lock to delete {filePath}");
                        return false;
                    }

                    // Use asynchronous file I/O
                    await Task.Run(() => {
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                        }
                    }, cancellationToken);

                    _logger.Debug($"Deleted file: {filePath}");
                    return true;
                }
                finally
                {
                    if (acquired)
                    {
                        fileLock.Release();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to delete file {fileName}");
                return false;
            }
        }

        // The rest of the methods (FileExists, DeleteFile, ListFiles, etc.) would follow the same pattern
        // of adding input validation, file locking, and async versions

        // I'll add just a couple more to demonstrate the pattern

        public bool FileExists(string fileName)
        {
            if (!_enabled) return false;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                _logger.Warn("Cannot check if file exists with empty filename");
                return false;
            }

            try
            {
                // Check if directory exists before checking file
                if (!Directory.Exists(_statusFilesPath))
                {
                    _logger.Warn($"Status directory not found when checking file, recreating: {_statusFilesPath}");
                    EnsureDirectory();
                    return false; // If directory was recreated, file doesn't exist
                }

                var filePath = Path.Combine(_statusFilesPath, fileName);
                var exists = File.Exists(filePath);

                if (exists)
                {
                    try
                    {
                        // Check if file is readable
                        var fileInfo = new FileInfo(filePath);
                        _logger.Debug($"File check: {filePath} exists (size: {fileInfo.Length} bytes, last modified: {fileInfo.LastWriteTime})");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"File exists but cannot read details: {ex.Message}");
                    }
                }

                return exists;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to check if file {fileName} exists");
                return false;
            }
        }

        public async Task<bool> FileExistsAsync(string fileName, CancellationToken cancellationToken = default)
        {
            if (!_enabled) return false;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                _logger.Warn("Cannot check if file exists with empty filename");
                return false;
            }

            try
            {
                // Check if directory exists before checking file
                if (!Directory.Exists(_statusFilesPath))
                {
                    _logger.Warn($"Status directory not found when checking file, recreating: {_statusFilesPath}");
                    EnsureDirectory();
                    return false; // If directory was recreated, file doesn't exist
                }

                var filePath = Path.Combine(_statusFilesPath, fileName);
                bool exists = File.Exists(filePath);

                if (exists)
                {
                    try
                    {
                        // Check if file is readable using async file operations
                        var fileInfo = new FileInfo(filePath);

                        // Actually do something async
                        using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                                                              bufferSize: 4096, useAsync: true))
                        {
                            var buffer = new byte[1]; // Just read one byte to check it's accessible
                            await fileStream.ReadAsync(buffer, 0, 1, cancellationToken);
                        }

                        _logger.Debug($"File check: {filePath} exists (size: {fileInfo.Length} bytes, last modified: {fileInfo.LastWriteTime})");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"File exists but cannot read details: {ex.Message}");
                    }
                }

                return exists;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to check if file {fileName} exists");
                return false;
            }
        }

        // Method to clean up temporary test files
        public void CleanupTempFiles()
        {
            if (!_enabled) return;
            if (string.IsNullOrWhiteSpace(_statusFilesPath))
            {
                _logger.Debug("Cannot clean up temp files - status files path is null or empty");
                return;
            }

            try
            {
                if (!Directory.Exists(_statusFilesPath))
                {
                    _logger.Debug($"Status directory does not exist: {_statusFilesPath}. Nothing to clean up.");
                    return;
                }

                _logger.Debug("Cleaning up temporary test files");

                // Patterns for temporary files
                var tempPatterns = new[]
                {
                    "write_test_*.tmp",
                    "status_test.json"
                };

                foreach (var pattern in tempPatterns)
                {
                    try
                    {
                        var files = Directory.GetFiles(_statusFilesPath, pattern);
                        foreach (var file in files)
                        {
                            try
                            {
                                // Get a lock on the file before deleting
                                var fileLock = GetFileLock(file);
                                bool acquired = fileLock.Wait(1000); // 1 second timeout

                                if (acquired)
                                {
                                    try
                                    {
                                        if (File.Exists(file))
                                        {
                                            File.Delete(file);
                                            _logger.Debug($"Deleted temporary file: {file}");
                                        }
                                    }
                                    finally
                                    {
                                        fileLock.Release();
                                    }
                                }
                                else
                                {
                                    _logger.Debug($"Could not acquire lock to delete temporary file: {file}");
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Debug($"Failed to delete temporary file {file}: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Failed to search for temporary files with pattern {pattern}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to clean up temporary files");
            }
        }

        public string[] ListFiles(string pattern = "*.*")
        {
            if (!_enabled) return Array.Empty<string>();
            if (string.IsNullOrWhiteSpace(pattern))
            {
                pattern = "*.*";
            }

            try
            {
                // Check if directory exists
                if (!Directory.Exists(_statusFilesPath))
                {
                    _logger.Warn($"Status directory not found when listing files, recreating: {_statusFilesPath}");
                    EnsureDirectory();
                    return Array.Empty<string>(); // Return empty array for newly created directory
                }

                return Directory.GetFiles(_statusFilesPath, pattern)
                    .Select(Path.GetFileName)
                    .ToArray();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to list files with pattern {pattern}");
                return Array.Empty<string>();
            }
        }

        public bool DeleteFile(string fileName)
        {
            if (!_enabled) return false;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                _logger.Warn("Cannot delete file with empty filename");
                return false;
            }

            try
            {
                var filePath = Path.Combine(_statusFilesPath, fileName);
                if (!File.Exists(filePath))
                {
                    _logger.Debug($"File does not exist (cannot delete): {filePath}");
                    return false;
                }

                // Use file lock to ensure no one is reading/writing
                var fileLock = GetFileLock(filePath);
                bool acquired = false;

                try
                {
                    acquired = fileLock.Wait(DEFAULT_TIMEOUT_MS);
                    if (!acquired)
                    {
                        _logger.Warn($"Timeout waiting for file lock to delete {filePath}");
                        return false;
                    }

                    File.Delete(filePath);
                    _logger.Debug($"Deleted file: {filePath}");
                    return true;
                }
                finally
                {
                    if (acquired)
                    {
                        fileLock.Release();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to delete file {fileName}");
                return false;
            }
        }
    }
}