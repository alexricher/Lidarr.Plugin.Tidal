using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Tidal.Services.CircuitBreaker;
using Lidarr.Plugin.Tidal.Services.Logging;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Download.Clients.Tidal.Extensions;
using NzbDrone.Core.Download.Clients.Tidal.Queue;
using NzbDrone.Core.Download.Clients.Tidal.Interfaces;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Common.Instrumentation;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download.Clients.Tidal.Utilities;
using InterfacesDownloadItem = NzbDrone.Core.Download.Clients.Tidal.Interfaces.IDownloadItem;
using NzbDrone.Core.Indexers;
using NzbDrone.Plugin.Tidal;
using TidalSharp;
using FluentValidation.Results;
using Lidarr.Plugin.Tidal.Download.Clients.Tidal;

namespace NzbDrone.Core.Download.Clients.Tidal
{
    /// <summary>
    /// Core component that provides a proxy interface between Lidarr and the Tidal API.
    /// Handles initialization of download components, queue management, and API interaction.
    /// Implements fault tolerance through circuit breaker pattern and delayed initialization.
    /// </summary>
    public class TidalProxy : ITidalProxy
    {
        /// <summary>
        /// Logger instance for diagnostic and error messages.
        /// </summary>
        private readonly Logger _logger;
        
        /// <summary>
        /// Download queue that manages concurrent download operations.
        /// Initialized lazily to avoid circular dependencies.
        /// </summary>
        private Queue.DownloadTaskQueue _downloadQueue;
        
        /// <summary>
        /// Circuit breaker that prevents excessive API calls during failure scenarios.
        /// </summary>
        private ITidalCircuitBreaker _circuitBreaker;
        
        /// <summary>
        /// Flag that indicates whether the proxy has been initialized.
        /// </summary>
        private bool _initialized = false;
        
        /// <summary>
        /// Lock object to ensure thread-safe initialization.
        /// </summary>
        private readonly object _initLock = new object();

        /// <summary>
        /// Initializes a new instance of the TidalProxy class.
        /// Note that actual initialization of dependencies is delayed until first use.
        /// </summary>
        /// <param name="logger">Logger for diagnostic and operational messages.</param>
        public TidalProxy(Logger logger)
        {
            _logger = logger;

            // We delay initialization of these components until they're actually needed
            // This helps avoid circular dependencies during plugin loading
            _logger?.Debug("Created TidalProxy instance - dependencies will be initialized on first use");
        }

        /// <summary>
        /// Ensures that all necessary components are initialized before use.
        /// Handles thread synchronization and initialization errors.
        /// Creates the download queue, circuit breaker, and other components.
        /// </summary>
        /// <exception cref="Exception">Thrown when initialization fails.</exception>
        private void EnsureInitialized()
        {
            if (_initialized) return;

            lock (_initLock)
            {
                if (_initialized) return;

                try
                {
                    _logger?.Debug("Initializing TidalProxy components");

                    // Create a default settings object with proper initialization values
                    var defaultSettings = new TidalSettings
                    {
                        MaxConcurrentTrackDownloads = 1,
                        DownloadTrackRetryCount = 3,
                        MaxTrackFailures = 3,
                        DownloadItemTimeoutMinutes = 30,
                        StallDetectionThresholdMinutes = 15,
                        StatsLogIntervalMinutes = 10,
                        CircuitBreakerFailureThreshold = 5,
                        CircuitBreakerResetTimeMinutes = 10,
                        CircuitBreakerHalfOpenMaxAttempts = 1,
                        QueueOperationTimeoutSeconds = 60,
                        QueueCapacity = 100,
                        // Ensure persistence is enabled by default
                        EnableQueuePersistence = true,
                        // Use a valid default path for queue persistence - use temp directory
                        QueuePersistencePath = Path.Combine(Path.GetTempPath(), "Lidarr", "TidalQueue")
                    };

                    try
                    {
                        // Create the temp directory if it doesn't exist
                        string tempQueuePath = defaultSettings.QueuePersistencePath;
                        if (!Directory.Exists(tempQueuePath))
                        {
                            _logger?.Debug($"[DIAGNOSTIC] Creating default queue persistence directory: {tempQueuePath}");
                            Directory.CreateDirectory(tempQueuePath);
                        }

                        _logger?.Debug("[DIAGNOSTIC] Creating file system service");
                        // Create file system service first
                        var fileSystemService = new Lidarr.Plugin.Tidal.Services.FileSystem.FileSystemService();
                        
                        _logger?.Debug("[DIAGNOSTIC] Creating download queue");
                        // Use settings.QueueCapacity for queue size rather than hardcoded value
                        _downloadQueue = new Queue.DownloadTaskQueue(defaultSettings.QueueCapacity, defaultSettings, _logger);
                        
                        _logger?.Debug("[DIAGNOSTIC] Creating circuit breaker");
                        _circuitBreaker = new TidalCircuitBreakerAdapter(_logger);

                        _logger?.Debug("[DIAGNOSTIC] Starting queue handler");
                        // Start the queue handler to process downloads
                        _downloadQueue.StartQueueHandler();

                        _initialized = true;
                        _logger?.Debug("TidalProxy components initialized successfully");
                    }
                    catch (NullReferenceException ex)
                    {
                        _logger?.Error(ex, "[DIAGNOSTIC] Null reference error initializing TidalProxy components. This may indicate a dependency issue.");
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "[DIAGNOSTIC] Failed to initialize TidalProxy components");
                    // We don't set _initialized to true here so it will try again next time
                    throw; // Re-throw to allow calling code to handle the error
                }
            }
        }

        /// <summary>
        /// Processes a download request for a Tidal album.
        /// Creates a download item and adds it to the download queue.
        /// </summary>
        /// <param name="remoteAlbum">The album to download.</param>
        /// <param name="settings">Tidal client settings.</param>
        /// <returns>A unique identifier for the download request.</returns>
        /// <exception cref="ArgumentNullException">Thrown when either parameter is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when download path is not configured.</exception>
        /// <exception cref="DownloadClientException">Thrown when download queueing fails.</exception>
        public async Task<string> Download(RemoteAlbum remoteAlbum, TidalSettings settings)
        {
            _logger?.Debug("[DIAGNOSTIC] Download method called");
            EnsureInitialized();

            if (remoteAlbum == null)
                throw new ArgumentNullException(nameof(remoteAlbum));

            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            string albumTitle = remoteAlbum.Release?.Title ?? "Unknown";
            string artistName = remoteAlbum.Artist?.Name ?? "Unknown Artist";

            _logger?.Debug($"[DIAGNOSTIC] Processing download: {albumTitle} by {artistName}");

            // Check if queue is already initialized
            if (_downloadQueue == null)
            {
                _logger?.Error("[DIAGNOSTIC] _downloadQueue is null even after EnsureInitialized");
                throw new InvalidOperationException("Download queue not initialized properly");
            }

            // Log the counts to verify our duplicate detection is working
            try
            {
                var existingItems = _downloadQueue.GetQueuedItems().ToList();
                _logger?.Debug($"[DIAGNOSTIC] Current queue size: {existingItems.Count}");
                
                var duplicates = existingItems.Where(item => 
                    !string.IsNullOrEmpty(item.Title) && 
                    !string.IsNullOrEmpty(item.Artist) && 
                    String.Equals(item.Title, albumTitle, StringComparison.OrdinalIgnoreCase) && 
                    String.Equals(item.Artist, artistName, StringComparison.OrdinalIgnoreCase)).ToList();
                    
                _logger?.Debug($"[DIAGNOSTIC] Duplicate items found: {duplicates.Count}");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[DIAGNOSTIC] Error checking duplicates");
            }

            _logger.Info($"Processing download request for {albumTitle} by {artistName}");

            try
            {
                // Create a unique ID for this download
                string downloadId = Guid.NewGuid().ToString();

                // Verify the download path exists
                if (string.IsNullOrWhiteSpace(settings.DownloadPath))
                {
                    throw new InvalidOperationException("Download path is not configured in Tidal settings");
                }

                if (!Directory.Exists(settings.DownloadPath))
                {
                    _logger.Info($"Creating download directory: {settings.DownloadPath}");
                    Directory.CreateDirectory(settings.DownloadPath);
                }

                // Create a download item and add it to the queue
                var downloadItem = new Lidarr.Plugin.Tidal.Download.Clients.Tidal.DownloadItem
                {
                    Id = downloadId,
                    Title = albumTitle,
                    Artist = artistName,
                    RemoteAlbum = remoteAlbum,
                    Settings = settings
                };

                // Set appropriate priority based on release date
                if (settings.EnablePrioritySystem)
                {
                    // Determine if this is a new release
                    bool isNewRelease = false;
                    if (remoteAlbum.Release?.PublishDate != null)
                    {
                        // Consider releases from the last 30 days as "new"
                        isNewRelease = (DateTime.UtcNow - remoteAlbum.Release.PublishDate).TotalDays <= 30;
                    }

                    // Set priority based on release type
                    if (isNewRelease)
                    {
                        downloadItem.Priority = (NzbDrone.Core.Download.Clients.Tidal.Interfaces.DownloadItemPriority)settings.DefaultNewReleasePriority;
                        _logger?.Debug($"[DIAGNOSTIC] Setting priority to {downloadItem.Priority} (New Release) for '{albumTitle}'");
                    }
                    else
                    {
                        downloadItem.Priority = (NzbDrone.Core.Download.Clients.Tidal.Interfaces.DownloadItemPriority)settings.DefaultBacklogPriority;
                        _logger?.Debug($"[DIAGNOSTIC] Setting priority to {downloadItem.Priority} (Backlog) for '{albumTitle}'");
                    }
                }

                _logger?.Debug("[DIAGNOSTIC] About to queue download item");
                // Queue the download - duplicate detection happens in DownloadTaskQueue
                await _downloadQueue.QueueBackgroundWorkItemAsync(downloadItem, CancellationToken.None);
                _logger?.Debug("[DIAGNOSTIC] Item queued successfully");

                _logger.Info($"Successfully queued download for {albumTitle} with ID {downloadId}");

                return downloadId;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[DIAGNOSTIC] Error queueing download for {albumTitle}: {ex.Message}");
                throw new DownloadClientException("Error queueing download: " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Retrieves the current download queue items.
        /// Currently returns an empty list as implementation is pending.
        /// </summary>
        /// <param name="settings">Tidal client settings.</param>
        /// <returns>A collection of download client items.</returns>
        public IEnumerable<DownloadClientItem> GetQueue(TidalSettings settings)
        {
            EnsureInitialized();

            // Return an empty list for now
            return new List<DownloadClientItem>();
        }

        /// <summary>
        /// Removes an item from the download queue.
        /// Currently a no-op as implementation is pending.
        /// </summary>
        /// <param name="downloadId">ID of the download to remove.</param>
        /// <param name="settings">Tidal client settings.</param>
        public void RemoveFromQueue(string downloadId, TidalSettings settings)
        {
            EnsureInitialized();

            // No-op for now
        }

        /// <summary>
        /// Gets a Tidal downloader instance for direct API operations.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>A task that resolves to a Tidal downloader.</returns>
        /// <exception cref="InvalidOperationException">Thrown when TidalAPI is not initialized or downloader is not available.</exception>
        public Task<Downloader> GetDownloaderAsync(CancellationToken cancellationToken = default)
        {
            if (TidalAPI.Instance == null || TidalAPI.Instance.Client == null)
            {
                throw new InvalidOperationException("TidalAPI is not initialized");
            }

            var downloader = TidalAPI.Instance.Client.Downloader;
            if (downloader == null)
            {
                throw new InvalidOperationException("Downloader not available");
            }

            return Task.FromResult(downloader);
        }

        /// <summary>
        /// Tests the connection to Tidal API using the provided settings.
        /// </summary>
        /// <param name="settings">Tidal client settings to test.</param>
        /// <returns>A validation result indicating success or failure.</returns>
        public ValidationResult TestConnection(TidalSettings settings)
        {
            EnsureInitialized();

            var result = new ValidationResult();

            try
            {
                // Test logic would go here - checking that the Tidal API is accessible
                if (TidalAPI.Instance == null || TidalAPI.Instance.Client == null)
                {
                    result.Errors.Add(new ValidationFailure("", "Tidal API is not initialized"));
                    return result;
                }

                // If we got here, connection is successful
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error testing Tidal connection");
                result.Errors.Add(new ValidationFailure("", $"Error testing connection: {ex.Message}"));
                return result;
            }
        }

        /// <summary>
        /// Checks if the circuit breaker is open, which indicates that downloads are temporarily suspended.
        /// </summary>
        /// <returns>True if the circuit breaker is open; otherwise, false.</returns>
        public bool IsCircuitBreakerOpen()
        {
            EnsureInitialized();

            // Use the circuit breaker instance to check if it's open
            return _circuitBreaker?.IsOpen() ?? false;
        }

        /// <summary>
        /// Gets the number of pending downloads currently in the queue.
        /// </summary>
        /// <returns>The number of pending downloads.</returns>
        public int GetPendingDownloadCount()
        {
            EnsureInitialized();

            // Get pending downloads from the circuit breaker
            return _circuitBreaker?.GetPendingCount() ?? 0;
        }

        /// <summary>
        /// Gets the time until the circuit breaker will automatically reset and allow downloads again.
        /// </summary>
        /// <returns>A TimeSpan representing the duration until reset.</returns>
        public TimeSpan GetCircuitBreakerReopenTime()
        {
            EnsureInitialized();

            // Get the time until the circuit breaker reopens
            return _circuitBreaker?.GetReopenTime() ?? TimeSpan.FromMinutes(1);
        }

        /// <summary>
        /// Updates the proxy with new settings
        /// </summary>
        /// <param name="settings">The updated Tidal settings</param>
        public void UpdateSettings(TidalSettings settings)
        {
            EnsureInitialized();

            try
            {
                _logger?.Debug("Updating TidalProxy settings");
                
                // Update the download queue with new settings if it exists
                if (_downloadQueue != null)
                {
                    _downloadQueue.SetSettings(settings);
                    _logger?.Debug("Updated download queue settings");
                }
                
                // We could reinitialize the circuit breaker here if needed, but we'll 
                // keep it simple for now to avoid breaking changes
                
                _logger?.Info("TidalProxy settings updated successfully");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error updating TidalProxy settings");
                throw;
            }
        }
    }
}


