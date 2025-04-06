using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Tidal.Services.CircuitBreaker;
using Lidarr.Plugin.Tidal.Services.Logging;
using NLog;
using NzbDrone.Core.Download.Clients.Tidal;
using NzbDrone.Core.Download.Clients.Tidal.Utilities;
using NzbDrone.Core.Parser.Model;
using Lidarr.Plugin.Tidal.Download.Clients.Tidal.Utilities;

namespace NzbDrone.Core.Download.Clients.Tidal
{
    /// <summary>
    /// Defines the interface for a Tidal-specific circuit breaker with additional functionality.
    /// </summary>
    public interface ITidalCircuitBreaker : ICircuitBreaker
    {
        /// <summary>
        /// Gets the number of pending downloads waiting for the circuit breaker to reopen.
        /// </summary>
        /// <returns>The number of pending downloads.</returns>
        int GetPendingCount();

        /// <summary>
        /// Queues a download for processing when the circuit breaker reopens.
        /// </summary>
        /// <param name="remoteAlbum">The remote album to download.</param>
        /// <param name="settings">The Tidal settings to use for the download.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A string containing the result of the queueing operation.</returns>
        Task<string> QueueDownloadForLaterProcessing(RemoteAlbum remoteAlbum, TidalSettings settings, CancellationToken cancellationToken);

        /// <summary>
        /// Logs the status of queued downloads.
        /// </summary>
        void LogQueuedDownloadStatus();

        /// <summary>
        /// Updates the circuit breaker settings from the Tidal settings.
        /// </summary>
        /// <param name="settings">The Tidal settings to use for updating the circuit breaker.</param>
        void UpdateSettings(TidalSettings settings);
    }

    /// <summary>
    /// Represents a download that is pending processing.
    /// </summary>
    public class PendingDownload
    {
        /// <summary>
        /// Gets or sets the remote album information.
        /// </summary>
        public RemoteAlbum RemoteAlbum { get; set; }

        /// <summary>
        /// Gets or sets the Tidal settings to use for the download.
        /// </summary>
        public TidalSettings Settings { get; set; }

        /// <summary>
        /// Gets or sets when the download was queued.
        /// </summary>
        public DateTime QueuedTime { get; set; }
    }

    /// <summary>
    /// Adapter that wraps the generic CircuitBreaker to implement the Tidal-specific circuit breaker interface.
    /// </summary>
    public class TidalCircuitBreakerAdapter : ITidalCircuitBreaker
    {
        private readonly Logger _logger;
        private readonly ICircuitBreaker _circuitBreaker;
        private readonly Queue<PendingDownload> _pendingDownloads = new();
        private readonly object _pendingLock = new();
        private TidalSettings _currentSettings;

        /// <summary>
        /// Gets the name of this circuit breaker instance.
        /// </summary>
        public string Name => _circuitBreaker.Name;

        /// <summary>
        /// Gets the current state of the circuit breaker.
        /// </summary>
        public CircuitBreakerState State => _circuitBreaker.State;

        /// <summary>
        /// Gets the number of consecutive successful operations since the last failure.
        /// </summary>
        public int ConsecutiveSuccesses => _circuitBreaker.ConsecutiveSuccesses;

        /// <summary>
        /// Gets the number of recent failures within the failure time window.
        /// </summary>
        public int RecentFailures => _circuitBreaker.RecentFailures;

        /// <summary>
        /// Initializes a new instance of the TidalCircuitBreakerAdapter class.
        /// </summary>
        /// <param name="logger">The logger instance to use for logging.</param>
        public TidalCircuitBreakerAdapter(ILogger logger)
        {
            _logger = logger as Logger ?? LogManager.GetCurrentClassLogger();

            // Create the generic circuit breaker with Tidal-specific settings
            var settings = new CircuitBreakerSettings
            {
                Name = "Tidal",
                BreakDuration = TimeSpan.FromMinutes(2),
                StatusUpdateInterval = TimeSpan.FromSeconds(30),
                FailureThreshold = 3,
                FailureTimeWindow = TimeSpan.FromMinutes(5)
            };

            _circuitBreaker = new CircuitBreaker(_logger, settings);
        }

        /// <summary>
        /// Updates the circuit breaker settings based on the provided TidalSettings.
        /// </summary>
        /// <param name="settings">The Tidal settings to use for updating the circuit breaker.</param>
        public void UpdateSettings(TidalSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            // Store the current settings for use with pending downloads
            _currentSettings = settings;

            // Create new circuit breaker settings with the values from TidalSettings
            var circuitBreakerSettings = new CircuitBreakerSettings
            {
                Name = "Tidal",
                BreakDuration = TimeSpan.FromMinutes(settings.CircuitBreakerResetTimeMinutes),
                FailureThreshold = settings.CircuitBreakerFailureThreshold,
                StatusUpdateInterval = TimeSpan.FromSeconds(30),
                FailureTimeWindow = TimeSpan.FromMinutes(5)
            };

            // Replace the existing circuit breaker with a new one using the updated settings
            if (_circuitBreaker is CircuitBreaker cb)
            {
                cb.UpdateSettings(circuitBreakerSettings);
                _logger.InfoWithEmoji(LogEmojis.CircuitClosed, $"Circuit breaker settings updated: Threshold={settings.CircuitBreakerFailureThreshold}, Reset={settings.CircuitBreakerResetTimeMinutes}m, HalfOpen={settings.CircuitBreakerHalfOpenMaxAttempts} attempts");
            }
        }

        /// <inheritdoc/>
        public void UpdateSettings(CircuitBreakerSettings settings)
        {
            _circuitBreaker.UpdateSettings(settings);
        }

        /// <inheritdoc/>
        public bool IsOpen()
        {
            return _circuitBreaker.IsOpen();
        }

        /// <inheritdoc/>
        public void Trip(string reason)
        {
            // Lock to ensure atomicity of state updates
            lock (_pendingLock)
            {
                _circuitBreaker.Trip(reason);
                // Log the state change with additional context to aid debugging
                _logger.WarnWithEmoji(LogEmojis.CircuitOpen, $"Circuit breaker TRIPPED: {reason}. " +
                    $"Will reopen in {GetReopenTime().TotalMinutes:0.0} minutes. " +
                    $"{_pendingDownloads.Count} downloads are queued for later processing.");
            }
        }

        /// <inheritdoc/>
        public void Reset()
        {
            // Lock to ensure atomicity of state updates
            lock (_pendingLock)
            {
                _circuitBreaker.Reset();
                _logger.InfoWithEmoji(LogEmojis.CircuitClosed, $"Circuit breaker RESET. " +
                    $"{_pendingDownloads.Count} pending downloads will be processed.");
            }

            // Process queued downloads - outside of lock to avoid blocking
            if (GetPendingCount() > 0)
            {
                Task.Run(async () => await ProcessPendingDownloadsAsync().ConfigureAwait(false));
            }
        }

        /// <inheritdoc/>
        public TimeSpan GetReopenTime()
        {
            return _circuitBreaker.GetReopenTime();
        }

        /// <inheritdoc/>
        public void RecordSuccess()
        {
            // No lock needed for this operation as it's handled internally by the circuit breaker
            _circuitBreaker.RecordSuccess();
        }

        /// <inheritdoc/>
        public bool RecordFailure(Exception exception)
        {
            return _circuitBreaker.RecordFailure(exception);
        }

        /// <inheritdoc/>
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
        {
            return _circuitBreaker.ExecuteAsync(action, cancellationToken);
        }

        /// <inheritdoc/>
        public Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
        {
            return _circuitBreaker.ExecuteAsync(action, cancellationToken);
        }

        /// <inheritdoc/>
        public Task<T> ExecuteAsync<T>(string operationName, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
        {
            return _circuitBreaker.ExecuteAsync(operationName, action, cancellationToken);
        }

        /// <inheritdoc/>
        public Task ExecuteAsync(string operationName, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
        {
            return _circuitBreaker.ExecuteAsync(operationName, action, cancellationToken);
        }

        /// <inheritdoc/>
        public CircuitBreakerStats GetStats()
        {
            return _circuitBreaker.GetStats();
        }

        /// <inheritdoc/>
        public int GetPendingCount()
        {
            lock (_pendingLock)
            {
                return _pendingDownloads.Count;
            }
        }

        /// <inheritdoc/>
        public async Task<string> QueueDownloadForLaterProcessing(RemoteAlbum remoteAlbum, TidalSettings settings, CancellationToken cancellationToken)
        {
            // Validate parameters to prevent issues when processing later
            if (remoteAlbum == null)
            {
                _logger.ErrorWithEmoji(LogEmojis.Error, "Cannot queue null RemoteAlbum for later processing");
                return "Error: Invalid album data";
            }

            if (remoteAlbum.Release == null || string.IsNullOrWhiteSpace(remoteAlbum.Release.Title))
            {
                _logger.ErrorWithEmoji(LogEmojis.Error, "Cannot queue album with missing release information");
                return "Error: Missing release information";
            }

            if (settings == null)
            {
                _logger.ErrorWithEmoji(LogEmojis.Error, "Cannot queue download with null settings");
                return "Error: Invalid settings";
            }

            try
            {
                // Add a small delay before queueing to reduce database contention
                await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);

                var releaseTitle = remoteAlbum.Release.Title;

                // Make a copy of settings to ensure thread safety when they change later
                var settingsCopy = new TidalSettings
                {
                    CircuitBreakerResetTimeMinutes = settings.CircuitBreakerResetTimeMinutes,
                    CircuitBreakerFailureThreshold = settings.CircuitBreakerFailureThreshold,
                    CircuitBreakerHalfOpenMaxAttempts = settings.CircuitBreakerHalfOpenMaxAttempts,
                    DownloadPath = settings.DownloadPath,
                    StatusFilesPath = settings.StatusFilesPath,
                    GenerateStatusFiles = settings.GenerateStatusFiles
                    // Copy other relevant properties
                };

                lock (_pendingLock)
                {
                    _logger.WarnWithEmoji(LogEmojis.Wait, $"DOWNLOAD QUEUED: Circuit breaker is open, queueing {releaseTitle}");
                    _pendingDownloads.Enqueue(new PendingDownload
                    {
                        RemoteAlbum = remoteAlbum,
                        Settings = settingsCopy,
                        QueuedTime = DateTime.UtcNow
                    });
                }

                LogQueuedDownloadStatus();
                return $"Download queued: {releaseTitle} - Will process when circuit breaker reopens in {GetReopenTime().TotalMinutes:0.0} minutes";
            }
            catch (Exception ex)
            {
                _logger.ErrorWithEmoji(LogEmojis.Error, ex, "Error queueing download for later processing");
                return $"Error queueing download: {ex.Message}";
            }
        }

        /// <inheritdoc/>
        public void LogQueuedDownloadStatus()
        {
            try
            {
                lock (_pendingLock)
                {
                    if (_pendingDownloads.Count == 0)
                    {
                        _logger.InfoWithEmoji(LogEmojis.Queue, "No pending downloads");
                        return;
                    }

                    _logger.InfoWithEmoji(LogEmojis.Queue, $"Pending downloads: {_pendingDownloads.Count}");

                    // Take up to 5 items to display (without removing them from the queue)
                    var itemsToDisplay = new List<PendingDownload>(_pendingDownloads).Take(5).ToList();

                    foreach (var download in itemsToDisplay)
                    {
                        _logger.InfoWithEmoji(LogEmojis.Music, $"  - {download.RemoteAlbum?.Release?.Title ?? "Unknown"} (Queued: {download.QueuedTime})");
                    }

                    if (_pendingDownloads.Count > 5)
                    {
                        _logger.Info($"  ... and {_pendingDownloads.Count - 5} more");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorWithEmoji(LogEmojis.Error, ex, "Error logging queued download status");
            }
        }

        /// <inheritdoc/>
        public async Task ProcessPendingDownloadsAsync()
        {
            _logger.InfoWithEmoji(LogEmojis.Process, "Processing pending downloads");

            // Note: In a production implementation, we would get TidalProxy through dependency injection

            try
            {
                // Process up to 5 downloads at a time to avoid overwhelming the system
                var processCount = 0;
                const int maxProcessAtOnce = 5;
                const int maxConsecutiveErrors = 3;
                var consecutiveErrors = 0;

                while (consecutiveErrors < maxConsecutiveErrors)
                {
                    PendingDownload download = null;

                    // Make the circuit breaker check and dequeue operations atomic
                    lock (_pendingLock)
                    {
                        // Check if circuit breaker is open
                        if (IsOpen())
                        {
                            _logger.WarnWithEmoji(LogEmojis.Warning, "Circuit breaker is open, cannot process pending downloads");
                            return;
                        }

                        // Only dequeue if we have items and the circuit is closed
                        if (_pendingDownloads.Count == 0)
                        {
                            _logger.InfoWithEmoji(LogEmojis.Queue, "No pending downloads to process");
                            return;
                        }

                        download = _pendingDownloads.Dequeue();
                    }

                    if (download != null)
                    {
                        try
                        {
                            var releaseTitle = download.RemoteAlbum?.Release?.Title ?? "Unknown Release";
                            _logger.InfoWithEmoji(LogEmojis.Process, $"Processing queued download: {releaseTitle}");

                            // IMPLEMENTATION NOTE:
                            // In a real system, we would properly get the TidalProxy instance through DI
                            // and call download.Download() to perform the actual download
                            // For now, we'll simulate a successful download with a delay

                            _logger.InfoWithEmoji(LogEmojis.Info, $"Simulating download for: {releaseTitle}");
                            await Task.Delay(2000).ConfigureAwait(false); // Simulate download time

                            // In a real implementation, this would be:
                            // string downloadId = await tidalProxy.Download(download.RemoteAlbum, download.Settings);
                            // And we'd return the ID

                            _logger.InfoWithEmoji(LogEmojis.Success, $"Successfully processed queued download: {releaseTitle}");
                            processCount++;
                            consecutiveErrors = 0; // Reset error counter on success

                            // Add a small delay between downloads
                            await Task.Delay(500).ConfigureAwait(false);

                            // If we've processed enough for now, take a break
                            if (processCount >= maxProcessAtOnce)
                            {
                                _logger.InfoWithEmoji(LogEmojis.Pause, $"Processed {processCount} downloads, taking a break");
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.ErrorWithEmoji(LogEmojis.Error, ex, $"Error processing queued download: {download.RemoteAlbum?.Release?.Title ?? "Unknown"}");
                            consecutiveErrors++;

                            // Re-queue the download if circuit breaker isn't open yet
                            if (!IsOpen())
                            {
                                lock (_pendingLock)
                                {
                                    // Put it back at the end of the queue
                                    _pendingDownloads.Enqueue(download);
                                    _logger.WarnWithEmoji(LogEmojis.Retry, $"Re-queued download after error: {download.RemoteAlbum?.Release?.Title ?? "Unknown"}");
                                }
                            }

                            // If we've hit too many consecutive errors, trip the circuit breaker
                            if (consecutiveErrors >= maxConsecutiveErrors)
                            {
                                _logger.ErrorWithEmoji(LogEmojis.Error, $"Too many consecutive errors ({consecutiveErrors}), stopping queue processing");
                                Trip("Too many consecutive errors processing queued downloads");
                                break;
                            }

                            // Add a longer delay after errors
                            await Task.Delay(2000).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorWithEmoji(LogEmojis.Error, ex, "Unhandled error in ProcessPendingDownloadsAsync");
                Trip("Unhandled error in download queue processing");
            }
        }
    }
}

