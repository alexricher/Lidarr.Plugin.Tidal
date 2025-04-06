using NLog;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.Download.Clients.Tidal;

namespace Lidarr.Plugin.Tidal.Services.FileSystem
{
    /// <summary>
    /// Interface for services that validate audio files for corruption, 
    /// track download retries, and manage file operations related to validation.
    /// </summary>
    /// <remarks>
    /// This service provides functionality to validate downloaded audio files
    /// for potential corruption issues, manages retry attempts for failed tracks,
    /// and handles temporary file management during the validation process.
    /// </remarks>
    public interface IFileValidationService
    {
        /// <summary>
        /// Validates an audio file to ensure it is not corrupted and meets quality expectations.
        /// </summary>
        /// <param name="filePath">Path to the audio file to validate</param>
        /// <param name="trackId">Tidal track ID for tracking retry attempts</param>
        /// <param name="quality">Audio quality that was requested</param>
        /// <param name="settings">Tidal settings with validation configuration</param>
        /// <param name="logger">Logger for detailed validation feedback</param>
        /// <param name="cancellationToken">Cancellation token to cancel validation if needed</param>
        /// <returns>
        /// A <see cref="FileValidationResult"/> indicating success or failure,
        /// with details about why a file failed validation and if it should be requeued
        /// </returns>
        Task<FileValidationResult> ValidateAudioFileAsync(
            string filePath, 
            string trackId, 
            TidalSharp.Data.AudioQuality quality,
            TidalSettings settings, 
            Logger logger, 
            CancellationToken cancellationToken);
            
        /// <summary>
        /// Cleans up any temporary files created during the validation process.
        /// </summary>
        /// <param name="filePath">The file path associated with the temporary files</param>
        /// <param name="logger">Logger for cleanup feedback</param>
        /// <remarks>
        /// This method typically removes temporary files like .tmp files 
        /// created during the validation process. It should be called after
        /// validation completes, regardless of success or failure.
        /// </remarks>
        void CleanupTempFiles(string filePath, Logger logger);
        
        /// <summary>
        /// Creates a backup of a file before replacing it with a new version.
        /// </summary>
        /// <param name="filePath">The file path to create a backup for</param>
        /// <param name="logger">Logger for backup operation feedback</param>
        /// <returns>The path to the created backup file, or null if backup failed</returns>
        /// <remarks>
        /// This method creates backups with a .bak extension by default. The backup
        /// can be used to restore the file if the replacement operation fails or
        /// if the user wants to revert to the previous version.
        /// </remarks>
        string CreateBackup(string filePath, Logger logger);
        
        /// <summary>
        /// Checks if a track has exceeded the maximum number of retry attempts.
        /// </summary>
        /// <param name="trackId">The track ID to check</param>
        /// <param name="settings">Tidal settings containing max retry configuration</param>
        /// <returns>True if the track has exceeded the maximum retry attempts, false otherwise</returns>
        /// <remarks>
        /// This prevents infinite retry loops for tracks that consistently fail validation,
        /// which could be due to issues with the source rather than download problems.
        /// </remarks>
        bool HasExceededMaxRetries(string trackId, TidalSettings settings);
        
        /// <summary>
        /// Records a retry attempt for a track that failed validation.
        /// </summary>
        /// <param name="trackId">The track ID that failed validation</param>
        /// <param name="reason">The reason for the validation failure</param>
        /// <param name="settings">Tidal settings containing retry configuration</param>
        /// <param name="logger">Logger for tracking retry attempts</param>
        /// <remarks>
        /// This method increments the retry counter for the track and records the
        /// reason for the failure. This information is used to determine if the track
        /// should be requeued or abandoned after repeated failures.
        /// </remarks>
        void RecordRetryAttempt(string trackId, string reason, TidalSettings settings, Logger logger);
        
        /// <summary>
        /// Resets all retry counters for all tracks.
        /// </summary>
        /// <remarks>
        /// This can be called periodically to clear historic retry statistics
        /// and allow previously failed tracks to be tried again. Useful after
        /// application restarts or when user triggers a manual reset.
        /// </remarks>
        void ResetRetryCounters();
    }
    
    /// <summary>
    /// Represents the result of a file validation operation.
    /// </summary>
    /// <remarks>
    /// This class encapsulates all validation information in a single object,
    /// including success/failure status, reasons for failure, retry information,
    /// and whether the track should be requeued for another download attempt.
    /// </remarks>
    public class FileValidationResult
    {
        /// <summary>
        /// Gets a value indicating whether the file passed validation.
        /// </summary>
        public bool IsValid { get; private set; }
        
        /// <summary>
        /// Gets the general reason for validation failure (null if validation passed).
        /// </summary>
        /// <remarks>
        /// Examples include "File Too Small", "Validation Failed", "Invalid Signature", etc.
        /// Useful for categorizing validation failures in logs and user interfaces.
        /// </remarks>
        public string FailureReason { get; private set; }
        
        /// <summary>
        /// Gets the detailed information about the validation failure (null if validation passed).
        /// </summary>
        /// <remarks>
        /// Contains specific details about why validation failed, such as 
        /// "File size (256 KB) is smaller than minimum size (1024 KB)",
        /// "FLAC signature not detected", etc.
        /// </remarks>
        public string Details { get; private set; }
        
        /// <summary>
        /// Gets the number of retry attempts that have been made for this track.
        /// </summary>
        public int RetryCount { get; private set; }
        
        /// <summary>
        /// Gets a value indicating whether the track should be requeued for another download attempt.
        /// </summary>
        /// <remarks>
        /// False when max retries have been exceeded or when the failure is not retryable
        /// (e.g., track doesn't exist or permission issues).
        /// </remarks>
        public bool ShouldRequeue { get; private set; }
        
        /// <summary>
        /// A convenience property that returns a successful validation result.
        /// </summary>
        public static FileValidationResult Success => new FileValidationResult { IsValid = true };
        
        /// <summary>
        /// Creates a validation failure result with the specified details.
        /// </summary>
        /// <param name="reason">General reason for validation failure</param>
        /// <param name="details">Detailed information about the validation failure</param>
        /// <param name="retryCount">Number of retry attempts made so far</param>
        /// <param name="shouldRequeue">Whether the track should be requeued</param>
        /// <returns>A FileValidationResult representing a validation failure</returns>
        public static FileValidationResult Failure(string reason, string details, int retryCount, bool shouldRequeue)
        {
            return new FileValidationResult
            {
                IsValid = false,
                FailureReason = reason,
                Details = details,
                RetryCount = retryCount,
                ShouldRequeue = shouldRequeue
            };
        }
    }
} 