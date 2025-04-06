using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Download.Clients.Tidal.Queue;
using NzbDrone.Core.Download.Clients.Tidal.Viewer;
using TidalSharp.Data;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Plugins;
using NzbDrone.Core.Download.Clients.Tidal.Interfaces;
using System.Text;
using Lidarr.Plugin.Tidal.Services.Logging;
using NzbDrone.Plugin.Tidal;
using NzbDrone.Core.Download.Clients.Tidal;
using Lidarr.Plugin.Tidal.Download.Clients.Tidal.Interfaces;
using Lidarr.Plugin.Tidal.Download.Clients.Tidal.Utilities;

namespace NzbDrone.Core.Download.Clients.Tidal.Queue
{
    // Serializable class for storing queue items
    /// <summary>
    /// Serializable record for persisting queue items between application restarts.
    /// Contains essential metadata about downloads without full object references.
    /// </summary>
    public class QueueItemRecord
    {
        /// <summary>
        /// Gets or sets the unique identifier for the download.
        /// </summary>
        public string ID { get; set; }

        /// <summary>
        /// Gets or sets the title of the album being downloaded.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets the artist name.
        /// </summary>
        public string Artist { get; set; }

        /// <summary>
        /// Gets or sets the album name.
        /// </summary>
        public string Album { get; set; }

        /// <summary>
        /// Gets or sets the audio quality/bitrate for the download.
        /// </summary>
        public string Bitrate { get; set; }

        /// <summary>
        /// Gets or sets the total size of the download in bytes.
        /// </summary>
        public long TotalSize { get; set; }

        /// <summary>
        /// Gets or sets the destination folder for downloaded files.
        /// </summary>
        public string DownloadFolder { get; set; }

        /// <summary>
        /// Gets or sets whether the content contains explicit material.
        /// </summary>
        public bool Explicit { get; set; }

        /// <summary>
        /// Gets or sets serialized JSON data for the remote album information.
        /// </summary>
        public string RemoteAlbumJson { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when the item was added to the queue.
        /// </summary>
        public DateTime QueuedTime { get; set; }
        
        /// <summary>
        /// Gets or sets the priority of the download in the queue.
        /// </summary>
        public int Priority { get; set; } = 0; // Default to Normal priority (0)
    }

    /// <summary>
    /// Core component that manages download operations for Tidal content.
    /// <para>
    /// The DownloadTaskQueue performs several key functions:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Maintains a thread-safe queue of download items</description></item>
    /// <item><description>Processes downloads with configurable concurrency</description></item>
    /// <item><description>Handles rate limiting and throttling</description></item>
    /// <item><description>Implements retry logic for failed downloads</description></item>
    /// <item><description>Tracks statistics for download operations</description></item>
    /// <item><description>Provides persistence for the download queue across restarts</description></item>
    /// <item><description>Simulates natural user behavior to avoid detection</description></item>
    /// </list>
    /// <para>
    /// This class is designed to be thread-safe and handles multiple concurrent downloads
    /// while respecting rate limits and providing detailed status information.
    /// </para>
    /// </summary>
    public class DownloadTaskQueue : IDisposable
    {
        #region Thread-Safe Collection Manager

        /// <summary>
        /// Encapsulates thread-safe operations on the download item collections with async lock support.
        /// </summary>
        private class ConcurrentItemCollectionManager : IDisposable
        {
            private readonly object _lock = new object();
            private readonly List<IDownloadItem> _items = new List<IDownloadItem>();
            private readonly Dictionary<IDownloadItem, CancellationTokenSource> _cancellationSources = 
                new Dictionary<IDownloadItem, CancellationTokenSource>();
            private readonly Dictionary<string, SemaphoreSlim> _itemLocks = new Dictionary<string, SemaphoreSlim>();
            private readonly Logger _logger;

            public ConcurrentItemCollectionManager(Logger logger)
            {
                _logger = logger;
            }

            /// <summary>
            /// Adds an item to the collections in a thread-safe manner.
            /// </summary>
            /// <param name="item">The download item to add.</param>
            public void AddItem(IDownloadItem item)
            {
                if (item == null)
                {
                    _logger?.Warn("Attempted to add null item to collection");
                    return;
                }

                lock (_lock)
                {
                    _items.Add(item);
                    _cancellationSources[item] = new CancellationTokenSource();
                    
                    // Create a semaphore for this item if it doesn't exist
                    if (!string.IsNullOrEmpty(item.Id) && !_itemLocks.ContainsKey(item.Id))
                    {
                        _itemLocks[item.Id] = new SemaphoreSlim(1, 1);
                    }
                }
            }

            /// <summary>
            /// Removes an item from the collections in a thread-safe manner.
            /// </summary>
            /// <param name="item">The download item to remove.</param>
            /// <returns>True if the item was removed, false otherwise.</returns>
            public bool RemoveItem(IDownloadItem item)
            {
                if (item == null)
                {
                    _logger?.Warn("Attempted to remove null item from collection");
                    return false;
                }

                lock (_lock)
                {
                    bool removed = _items.Remove(item);
                    
                    if (_cancellationSources.TryGetValue(item, out var cts))
                    {
                        cts.Dispose();
                        _cancellationSources.Remove(item);
                    }
                    
                    // We don't remove the semaphore immediately in case other code is using it
                    // It will be cleaned up periodically or when the manager is disposed
                    
                    return removed;
                }
            }

            /// <summary>
            /// Gets a copy of all items in a thread-safe manner.
            /// </summary>
            /// <returns>An array of all download items.</returns>
            public IDownloadItem[] GetAllItems()
            {
                lock (_lock)
                {
                    return _items.ToArray();
                }
            }

            /// <summary>
            /// Gets the current count of items in a thread-safe manner.
            /// </summary>
            public int Count
            {
                get
                {
                    lock (_lock)
                    {
                        return _items.Count;
                    }
                }
            }

            /// <summary>
            /// Gets a cancellation token specifically for the given item.
            /// </summary>
            /// <param name="item">The download item.</param>
            /// <param name="linkedToken">Optional token to link with.</param>
            /// <returns>A cancellation token for the item.</returns>
            public CancellationToken GetTokenForItem(IDownloadItem item, CancellationToken linkedToken = default)
            {
                lock (_lock)
                {
                    if (item != null && _cancellationSources.TryGetValue(item, out var cts))
                    {
                        if (linkedToken == default)
                        {
                            return cts.Token;
                        }
                        
                        return CancellationTokenSource.CreateLinkedTokenSource(cts.Token, linkedToken).Token;
                    }
                    
                    return linkedToken == default ? CancellationToken.None : linkedToken;
                }
            }
            
            /// <summary>
            /// Acquires a lock for the specified item ID asynchronously.
            /// </summary>
            /// <param name="itemId">The ID of the item to lock.</param>
            /// <param name="timeout">Maximum time to wait for the lock.</param>
            /// <param name="cancellationToken">Cancellation token.</param>
            /// <returns>An IDisposable that releases the lock when disposed.</returns>
            public async Task<IDisposable> AcquireLockAsync(string itemId, TimeSpan timeout, CancellationToken cancellationToken)
            {
                if (string.IsNullOrEmpty(itemId))
                {
                    throw new ArgumentNullException(nameof(itemId));
                }
                
                SemaphoreSlim semaphore;
                
                // Get or create the semaphore for this item
                lock (_lock)
                {
                    if (!_itemLocks.TryGetValue(itemId, out semaphore))
                    {
                        semaphore = new SemaphoreSlim(1, 1);
                        _itemLocks[itemId] = semaphore;
                    }
                }
                
                // Wait for the semaphore with timeout
                var entered = await semaphore.WaitAsync(timeout, cancellationToken);
                
                if (!entered)
                {
                    throw new TimeoutException($"Timed out waiting for lock on item {itemId}");
                }
                
                // Return a disposable that releases the semaphore
                return new SemaphoreLockRelease(semaphore);
            }
            
            /// <summary>
            /// Clean up unused item locks to prevent memory leaks
            /// </summary>
            public void CleanupUnusedLocks()
            {
                lock (_lock)
                {
                    // Find all item IDs that are no longer in the collection
                    var unusedLockIds = _itemLocks.Keys
                        .Where(id => !_items.Any(item => item.Id == id))
                        .ToList();
                        
                    foreach (var id in unusedLockIds)
                    {
                        if (_itemLocks.TryGetValue(id, out var semaphore))
                        {
                            // Only dispose if no one is waiting on it
                            if (semaphore.CurrentCount == 1)
                            {
                                semaphore.Dispose();
                                _itemLocks.Remove(id);
                                _logger?.Debug($"Cleaned up unused lock for item ID: {id}");
                            }
                        }
                    }
                }
            }
            
            /// <summary>
            /// Dispose all resources
            /// </summary>
            public void Dispose()
            {
                lock (_lock)
                {
                    foreach (var cts in _cancellationSources.Values)
                    {
                        cts.Dispose();
                    }
                    
                    foreach (var semaphore in _itemLocks.Values)
                    {
                        semaphore.Dispose();
                    }
                    
                    _cancellationSources.Clear();
                    _itemLocks.Clear();
                    _items.Clear();
                }
            }
            
            /// <summary>
            /// Helper class to release a semaphore when disposed
            /// </summary>
            private class SemaphoreLockRelease : IDisposable
            {
                private readonly SemaphoreSlim _semaphore;
                
                public SemaphoreLockRelease(SemaphoreSlim semaphore)
                {
                    _semaphore = semaphore;
                }
                
                public void Dispose()
                {
                    _semaphore.Release();
                }
            }
        }

        #endregion

        private readonly Channel<IDownloadItem> _queue;
        private readonly ConcurrentItemCollectionManager _collectionManager;
        private readonly List<Task> _runningTasks = new();
        private readonly object _lock = new();
        private CancellationTokenSource _processingCancellationSource;
        private Task _processingTask;
        private readonly TimeSpan _itemProcessTimeout = TimeSpan.FromMinutes(30);
        private readonly TimeSpan _queueOperationTimeout = TimeSpan.FromSeconds(60);

        private TidalSettings _settings;
        private readonly Logger _logger;
        private NaturalDownloadScheduler _naturalScheduler;
        private IUserBehaviorSimulator _behaviorSimulator;
        private readonly StatisticsManager _stats;

        // Statistics tracking
        private int _totalItemsProcessed = 0;
        private int _totalItemsQueued = 0;
        private int _failedDownloads = 0;
        private DateTime _lastStatsLogTime = DateTime.UtcNow;
        private readonly TimeSpan _statsLogInterval = TimeSpan.FromMinutes(15); // Log stats every 15 minutes
        private DateTime _lastRegularUpdateTime = DateTime.MinValue;
        private readonly TimeSpan _regularUpdateInterval = TimeSpan.FromMinutes(3); // Status update every 3 minutes

        // Queue auto-save
        private System.Timers.Timer _autoSaveTimer;
        private readonly TimeSpan _autoSaveInterval = TimeSpan.FromMinutes(2); // Auto-save every 2 minutes
        private DateTime _lastAutoSaveTime = DateTime.MinValue;
        private readonly object _autoSaveLock = new object();

        // Download status manager for the viewer
        private DownloadStatusManager _statusManager;
        
        // Stall detection
        private DateTime _lastProcessedTime = DateTime.MinValue;
        private readonly TimeSpan _stallDetectionThreshold = TimeSpan.FromMinutes(30);
        private int _stallDetectionCount = 0;
        private readonly object _stallDetectionLock = new object();

        private QueuePersistenceManager _persistenceManager;

        /// <summary>
        /// Thread-safe statistics manager for tracking download rates, processed items, and failures.
        /// </summary>
        private class StatisticsManager
        {
            private readonly object _lock = new object();
            private readonly Logger _logger;
            private readonly TidalSettings _settings;
            private int _totalItemsProcessed;
            private int _failedDownloads;
            private readonly List<DateTime> _recentDownloads = new List<DateTime>();
            private DateTime _lastRateLimitLogTime = DateTime.MinValue;
            private readonly TimeSpan _rateLimitLogInterval = TimeSpan.FromSeconds(30);
            private readonly TimeSpan _rateLimitStatusInterval = TimeSpan.FromMinutes(2); // New: Regular status update interval
            private DateTime _lastRateLimitStatusTime = DateTime.MinValue; // New: Track last status update time
            private bool _isRateLimited = false;
            private int _downloadRatePerHour = 0;

            /// <summary>
            /// Initializes a new instance of the StatisticsManager class.
            /// </summary>
            /// <param name="settings">The Tidal settings.</param>
            /// <param name="logger">The logger to use.</param>
            public StatisticsManager(TidalSettings settings, Logger logger)
            {
                _settings = settings ?? new TidalSettings
                {
                    MaxDownloadsPerHour = 0  // No rate limiting by default
                };
                _logger = logger;
            }

            /// <summary>
            /// Increments the count of processed items in a thread-safe manner.
            /// </summary>
            public void IncrementProcessedItems()
            {
                Interlocked.Increment(ref _totalItemsProcessed);
            }

            /// <summary>
            /// Increments the count of failed downloads in a thread-safe manner.
            /// </summary>
            public void IncrementFailedDownloads()
            {
                Interlocked.Increment(ref _failedDownloads);
            }

            /// <summary>
            /// Gets the total number of processed items.
            /// </summary>
            public int TotalItemsProcessed => Interlocked.CompareExchange(ref _totalItemsProcessed, 0, 0);

            /// <summary>
            /// Gets the total number of failed downloads.
            /// </summary>
            public int FailedDownloads => Interlocked.CompareExchange(ref _failedDownloads, 0, 0);

            /// <summary>
            /// Gets the current download rate per hour.
            /// </summary>
            public int DownloadRatePerHour
            {
                get { lock (_lock) { return _downloadRatePerHour; } }
            }
            
            /// <summary>
            /// Gets a value indicating whether the rate is currently limited.
            /// </summary>
            public bool IsRateLimited
            {
                get { lock (_lock) { return _isRateLimited; } }
            }

            /// <summary>
            /// Gets the oldest download in the tracking window.
            /// </summary>
            public DateTime? OldestDownload
            {
                get 
                { 
                    lock (_lock) 
                    { 
                        return _recentDownloads.OrderBy(d => d).FirstOrDefault(); 
                    } 
                }
            }

            /// <summary>
            /// Checks whether download operations should be throttled based on rate limits.
            /// </summary>
            /// <returns>True if downloads should be throttled, false otherwise.</returns>
            public bool ShouldThrottleDownload()
            {
                // If max downloads per hour is not set (0), don't throttle
                if (_settings.MaxDownloadsPerHour <= 0)
                {
                    return false;
                }

                lock (_lock)
                {
                    // If we've reached the max downloads per hour, throttle
                    if (_downloadRatePerHour >= _settings.MaxDownloadsPerHour)
                    {
                        // Only log the throttling message when we haven't already logged it recently
                        if ((DateTime.UtcNow - _lastRateLimitStatusTime) >= _rateLimitStatusInterval)
                        {
                            _logger.Debug($"Download throttling active: {_downloadRatePerHour}/{_settings.MaxDownloadsPerHour} downloads per hour limit reached");
                            _lastRateLimitStatusTime = DateTime.UtcNow;
                        }
                        return true;
                    }
                }

                return false;
            }

            /// <summary>
            /// Records a download for rate limiting purposes.
            /// </summary>
            /// <param name="trackCount">Number of tracks downloaded.</param>
            public void RecordDownload(int trackCount = 1)
            {
                lock (_lock)
                {
                    for (int i = 0; i < trackCount; i++)
                    {
                        _recentDownloads.Add(DateTime.UtcNow);
                    }
                    UpdateDownloadRate();
                }
            }

            /// <summary>
            /// Updates the current download rate based on recent downloads.
            /// </summary>
            /// <returns>True if the rate limit status has changed, false otherwise.</returns>
            public bool UpdateDownloadRate()
            {
                bool wasRateLimited;
                bool isNowRateLimited;

                lock (_lock)
                {
                    // Save previous state to detect changes
                    wasRateLimited = _isRateLimited;

                    // Remove downloads older than 1 hour
                    var cutoff = DateTime.UtcNow.AddHours(-1);
                    _recentDownloads.RemoveAll(d => d < cutoff);

                    // Calculate current download rate per hour
                    _downloadRatePerHour = _recentDownloads.Count;

                    // Check if we're currently rate limited
                    isNowRateLimited = (_settings.MaxDownloadsPerHour > 0 && _downloadRatePerHour >= _settings.MaxDownloadsPerHour);

                    // Update state
                    _isRateLimited = isNowRateLimited;
                }

                // Log when approaching or exceeding the rate limit
                if (_settings.MaxDownloadsPerHour > 0)
                {
                    int percentOfLimit = (int)((_downloadRatePerHour / (float)_settings.MaxDownloadsPerHour) * 100);

                    // Instead of using a separate periodic condition, always log status when updating
                    bool rateChanged = isNowRateLimited != wasRateLimited;
                    bool timeForRegularUpdate = (DateTime.UtcNow - _lastRateLimitStatusTime) >= _rateLimitStatusInterval;

                    if (percentOfLimit >= 100 && (rateChanged || (DateTime.UtcNow - _lastRateLimitLogTime) >= _rateLimitLogInterval))
                    {
                        DateTime oldestDownload;
                        string timeUntilUnlimited = "unknown";

                        lock (_lock)
                        {
                            oldestDownload = _recentDownloads.OrderBy(d => d).FirstOrDefault();
                        }

                        if (oldestDownload != default)
                        {
                            var timeUntilUnderLimit = oldestDownload.AddHours(1) - DateTime.UtcNow;
                            timeUntilUnlimited = timeUntilUnderLimit.TotalMinutes > 60
                                ? $"{(int)timeUntilUnderLimit.TotalHours}h {timeUntilUnderLimit.Minutes}m"
                                : $"{timeUntilUnderLimit.Minutes}m {timeUntilUnderLimit.Seconds}s";
                        }

                        _logger.Warn($"{LogEmojis.Stop} RATE LIMIT REACHED: {_downloadRatePerHour}/{_settings.MaxDownloadsPerHour} downloads per hour ({percentOfLimit}% of limit)");
                        _logger.Info($"   {LogEmojis.Wait} Downloads will be throttled until rate falls below limit (in {timeUntilUnlimited})");
                        _logger.Info($"   {LogEmojis.Info} This is normal behavior to prevent Tidal service limitations - not a bug or error");

                        lock (_lock)
                        {
                            _lastRateLimitLogTime = DateTime.UtcNow;
                            _lastRateLimitStatusTime = DateTime.UtcNow;
                        }
                    }
                    else if (percentOfLimit >= 80 && percentOfLimit < 100 &&
                             (DateTime.UtcNow - _lastRateLimitLogTime) >= _rateLimitLogInterval)
                    {
                        _logger.Warn($"{LogEmojis.Warning} Approaching rate limit: {_downloadRatePerHour}/{_settings.MaxDownloadsPerHour} downloads per hour ({percentOfLimit}% of limit)");
                        _logger.Info($"   {LogEmojis.Info} Download speed will be temporarily reduced soon to stay within Tidal limits");

                        lock (_lock)
                        {
                            _lastRateLimitLogTime = DateTime.UtcNow;
                            _lastRateLimitStatusTime = DateTime.UtcNow;
                        }
                    }
                    // Always log a regular status update if it's time
                    else if (timeForRegularUpdate)
                    {
                        // Don't log for very low usage (less than 10%)
                        if (percentOfLimit >= 10)
                        {
                            string statusEmoji;
                            string statusText;

                            if (percentOfLimit >= 50)
                            {
                                statusEmoji = LogEmojis.Info;
                                statusText = "MODERATE";
                            }
                            else
                            {
                                statusEmoji = LogEmojis.Success;
                                statusText = "NORMAL";
                            }

                            _logger.Info($"{statusEmoji} DOWNLOAD RATE STATUS: {statusText} - Current rate: {_downloadRatePerHour}/{_settings.MaxDownloadsPerHour} downloads per hour ({percentOfLimit}%)");

                            if (_downloadRatePerHour > 0)
                            {
                                // Calculate time average between downloads
                                var averageTimeBetweenDownloads = 60.0 / (_downloadRatePerHour / 60.0);
                                _logger.Debug($"   {LogEmojis.Time} Average time between downloads: ~{averageTimeBetweenDownloads:F1} seconds");
                            }
                        }

                        lock (_lock)
                        {
                            _lastRateLimitStatusTime = DateTime.UtcNow;
                        }
                    }
                }

                return isNowRateLimited != wasRateLimited;
            }
            
            /// <summary>
            /// Gets the time until the next download slot is available as a TimeSpan.
            /// </summary>
            /// <returns>A TimeSpan until the rate limit reset, or null if not rate limited or unknown.</returns>
            public TimeSpan? GetTimeUntilRateLimitResetTimeSpan()
            {
                if (!_isRateLimited || _settings.MaxDownloadsPerHour <= 0)
                {
                    return null;
                }
                
                var oldestDownload = OldestDownload;
                if (oldestDownload.HasValue)
                {
                    return oldestDownload.Value.AddHours(1) - DateTime.UtcNow;
                }
                
                return null;
            }
            
            /// <summary>
            /// Gets a formatted string describing the time until the rate limit will be reset.
            /// </summary>
            /// <returns>A formatted string with the time until reset, or empty if not rate limited.</returns>
            public string GetTimeUntilRateLimitReset()
            {
                var timeSpan = GetTimeUntilRateLimitResetTimeSpan();
                if (!timeSpan.HasValue)
                {
                    return "unknown";
                }
                
                return timeSpan.Value.TotalMinutes > 60
                    ? $"{(int)timeSpan.Value.TotalHours}h {timeSpan.Value.Minutes}m"
                    : $"{timeSpan.Value.Minutes}m {timeSpan.Value.Seconds}s";
            }
            
            /// <summary>
            /// Gets a formatted string describing the current rate limit status.
            /// </summary>
            /// <returns>A formatted string with the rate limit status.</returns>
            public string GetRateLimitStatusString()
            {
                if (_settings.MaxDownloadsPerHour <= 0)
                {
                    return $"{LogEmojis.Info} No rate limit configured";
                }
                
                int percentOfLimit = _settings.MaxDownloadsPerHour > 0 
                    ? (int)((_downloadRatePerHour / (float)_settings.MaxDownloadsPerHour) * 100) 
                    : 0;
                    
                if (_isRateLimited)
                {
                    return $"{LogEmojis.Stop} Rate limited: {_downloadRatePerHour}/{_settings.MaxDownloadsPerHour}/hr ({percentOfLimit}%), reset in {GetTimeUntilRateLimitReset()}";
                }
                else if (percentOfLimit >= 80)
                {
                    return $"{LogEmojis.Warning} Approaching limit: {_downloadRatePerHour}/{_settings.MaxDownloadsPerHour}/hr ({percentOfLimit}%)";
                }
                else if (percentOfLimit >= 50)
                {
                    return $"{LogEmojis.Info} Moderate usage: {_downloadRatePerHour}/{_settings.MaxDownloadsPerHour}/hr ({percentOfLimit}%)";
                }
                else if (percentOfLimit > 0)
                {
                    return $"{LogEmojis.Success} Normal usage: {_downloadRatePerHour}/{_settings.MaxDownloadsPerHour}/hr ({percentOfLimit}%)";
                }
                
                return $"{LogEmojis.Success} No recent downloads";
            }
        }

        /// <summary>
        /// Initializes a new instance of the DownloadTaskQueue class.
        /// </summary>
        /// <param name="capacity">The capacity of the download queue.</param>
        /// <param name="settings">The Tidal settings.</param>
        /// <param name="logger">The logger to use.</param>
        public DownloadTaskQueue(int capacity, TidalSettings settings, Logger logger)
        {
            BoundedChannelOptions options = new(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait
            };
            _queue = Channel.CreateBounded<IDownloadItem>(options);
            _logger = logger;
            _collectionManager = new ConcurrentItemCollectionManager(logger);

            // Create a default settings object if none is provided
            _settings = settings ?? new TidalSettings
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
                QueueOperationTimeoutSeconds = 60
            };

            _stats = new StatisticsManager(_settings, logger); // Use the non-null settings
            _behaviorSimulator = new UserBehaviorSimulator(logger);
            _naturalScheduler = new NaturalDownloadScheduler(this, _behaviorSimulator, _settings, logger);
            _processingCancellationSource = new CancellationTokenSource();

            // Initialize download status manager for the viewer
            if (settings.GenerateStatusFiles)
            {
                try
                {
            _statusManager = new DownloadStatusManager(
                AppDomain.CurrentDomain.BaseDirectory,
                        settings.StatusFilesPath,
                logger);

                    _logger.Info($"Download status manager initialized successfully");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Error initializing download status manager. Status tracking will be disabled.");
                    _statusManager = null; // Ensure it's null so we don't try to use it later
                }
            }
            else
            {
                _logger.Info("Status file generation is disabled in settings. Status tracking will not be active.");
                _statusManager = null;
            }

            // Initialize queue persistence manager
            if (settings.EnableQueuePersistence)
            {
                try
                {
                    string persistencePath = settings.ActualQueuePersistencePath;
                    if (!string.IsNullOrWhiteSpace(persistencePath))
                    {
                        try
                        {
                            // Ensure the directory exists
                            if (!Directory.Exists(persistencePath))
                            {
                                _logger.Info($"Creating queue persistence directory: {persistencePath}");
                                Directory.CreateDirectory(persistencePath);
                            }

                            // Test if the directory is writable
                            string testFilePath = Path.Combine(persistencePath, ".write_test");
                            File.WriteAllText(testFilePath, "Test");
                            File.Delete(testFilePath);

                            _persistenceManager = new QueuePersistenceManager(persistencePath, logger);
                            _logger.Info($"Queue persistence manager initialized successfully with path: {persistencePath}");
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            _logger.Error(ex, $"Permission denied when accessing queue persistence path: {persistencePath}");
                            TryFallbackPersistencePath(settings, logger);
                        }
                        catch (IOException ex)
                        {
                            _logger.Error(ex, $"IO error when accessing queue persistence path: {persistencePath}");
                            TryFallbackPersistencePath(settings, logger);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, $"Error initializing queue persistence manager with path: {persistencePath}");
                            TryFallbackPersistencePath(settings, logger);
                        }
                    }
                    else
                    {
                        _logger.Warn("Queue persistence path is not configured. Trying fallback paths...");
                        TryFallbackPersistencePath(settings, logger);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Error initializing queue persistence manager. Queue persistence will be disabled.");
                    _persistenceManager = null;
                }
            }
            else
            {
                _logger.Info("Queue persistence is disabled in settings.");
                _persistenceManager = null;
            }

            try
            {
                _logger.Info($"Tidal Download Queue initialized - Plugin Version {typeof(NzbDrone.Core.Plugins.TidalPlugin).Assembly.GetName().Version}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error initializing DownloadTaskQueue");
            }

            // Restore queue if persistence is enabled
            if (_settings.EnableQueuePersistence)
            {
                RestoreQueueFromDisk();
            }
            
            // Initialize auto-save timer
            if (_settings.EnableQueuePersistence && _persistenceManager != null)
            {
                _autoSaveTimer = new System.Timers.Timer
                {
                    Interval = _autoSaveInterval.TotalMilliseconds,
                    AutoReset = true
                };
                _autoSaveTimer.Elapsed += (sender, args) => AutoSaveQueue();
                _logger.Debug($"Auto-save timer initialized with interval of {_autoSaveInterval.TotalMinutes} minutes");
            }
        }

        /// <summary>
        /// Tries to create a persistence manager with a fallback path if the primary path fails
        /// </summary>
        private void TryFallbackPersistencePath(TidalSettings settings, Logger logger)
        {
            try
            {
                // Try using temp directory as fallback
                string tempPath = Path.Combine(Path.GetTempPath(), "Lidarr", "TidalQueue");
                _logger.Warn($"Trying fallback persistence path: {tempPath}");
                
                // Ensure the directory exists
                if (!Directory.Exists(tempPath))
                {
                    Directory.CreateDirectory(tempPath);
                }
                
                // Test if the directory is writable
                string testFilePath = Path.Combine(tempPath, ".write_test");
                File.WriteAllText(testFilePath, "Test");
                File.Delete(testFilePath);
                
                _persistenceManager = new QueuePersistenceManager(tempPath, logger);
                _logger.Info($"Queue persistence manager initialized with fallback path: {tempPath}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize queue persistence manager with fallback path. Queue persistence will be disabled.");
                _persistenceManager = null;
            }
        }

        public void SetSettings(TidalSettings settings)
        {
            if (settings == null)
            {
                _logger?.Warn("Received null settings in SetSettings");
                return;
            }

            lock (_lock)
            {
                var oldSettings = _settings;
                _settings = settings;

                // Update the scheduler with new settings
                _naturalScheduler = new NaturalDownloadScheduler(this, _behaviorSimulator, settings, _logger);

                // Check if the status files path has changed
                bool statusPathChanged = oldSettings == null ||
                    oldSettings.StatusFilesPath != settings.StatusFilesPath ||
                    oldSettings.GenerateStatusFiles != settings.GenerateStatusFiles;

                // Handle status file path changes
                if (statusPathChanged)
                {
                    try
                    {
                        if (settings.GenerateStatusFiles)
                        {
                            _logger?.Info($"Status files path changed from '{oldSettings?.StatusFilesPath}' to '{settings.StatusFilesPath}'. Updating status manager.");

                            if (_statusManager != null)
                            {
                                // If we already have a status manager, reinitialize it with the new path
                                _statusManager.ReinitializeWithPath(settings.StatusFilesPath);

                                // Force a status update with current queue data
                                UpdateDownloadStatusManager();

                                _logger?.Info("Status manager updated successfully with new path.");
                            }
                            else
                            {
                                // If we don't have a status manager yet (perhaps it was disabled before), create a new one
                                var newStatusManager = new DownloadStatusManager(
                                    AppDomain.CurrentDomain.BaseDirectory,
                                    settings.StatusFilesPath,
                                    _logger);

                                // Initialize with current data
                                _statusManager = newStatusManager;
                                UpdateDownloadStatusManager();

                                _logger?.Info("Created new status manager with current path.");
                            }
                        }
                        else if (_statusManager != null)
                        {
                            _logger?.Info("Status file generation has been disabled. Status manager will be kept but files won't be updated.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Error(ex, "Error updating status manager with new path");
                    }
                }

                // If persistence setting changed from disabled to enabled, save queue
                if ((oldSettings == null || !oldSettings.EnableQueuePersistence) &&
                    settings.EnableQueuePersistence &&
                    _collectionManager.Count > 0)
                {
                    SaveQueueToDisk();
                }

                // If queue persistence path changed, update saved queue location
                bool persistencePathChanged = oldSettings == null ||
                    oldSettings.ActualQueuePersistencePath != settings.ActualQueuePersistencePath;

                bool persistenceSettingChanged = oldSettings == null || 
                    oldSettings.EnableQueuePersistence != settings.EnableQueuePersistence;

                // If persistence setting was enabled or path changed while enabled, reinitialize
                if ((persistenceSettingChanged && settings.EnableQueuePersistence) || 
                    (settings.EnableQueuePersistence && persistencePathChanged))
                {
                    try
                    {
                        _logger?.Info("Queue persistence settings changed. Reinitializing persistence manager...");
                        
                        // Save current items if we had a persistence manager before
                        IDownloadItem[] currentItems = _collectionManager.GetAllItems();
                        
                        // Stop auto-save timer
                        if (_autoSaveTimer != null)
                        {
                            _autoSaveTimer.Stop();
                        }
                        
                        // Reinitialize persistence manager
                        if (settings.EnableQueuePersistence)
                        {
                            string persistencePath = settings.ActualQueuePersistencePath;
                            if (!string.IsNullOrWhiteSpace(persistencePath))
                            {
                                try
                                {
                                    // Ensure the directory exists
                                    if (!Directory.Exists(persistencePath))
                                    {
                                        _logger.Info($"Creating queue persistence directory: {persistencePath}");
                                        Directory.CreateDirectory(persistencePath);
                                    }
                                    
                                    _persistenceManager = new QueuePersistenceManager(persistencePath, _logger);
                                    _logger.Info($"Queue persistence manager reinitialized with path: {persistencePath}");
                                    
                                    // Initialize auto-save timer if needed
                                    if (_autoSaveTimer == null)
                                    {
                                        _autoSaveTimer = new System.Timers.Timer
                                        {
                                            Interval = _autoSaveInterval.TotalMilliseconds,
                                            AutoReset = true
                                        };
                                        _autoSaveTimer.Elapsed += (sender, args) => AutoSaveQueue();
                                    }
                                    
                                    // Start the timer
                                    _autoSaveTimer.Start();
                                    _logger.Debug("Started queue auto-save timer after reinitializing persistence manager");
                                    
                                    // Save the current items to the new location
                                    if (currentItems.Length > 0)
                                    {
                                        _persistenceManager.SaveQueue(currentItems);
                                        _logger.Info($"Migrated {currentItems.Length} queue items to new persistence location");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.Error(ex, $"Error reinitializing queue persistence manager. Falling back to previous configuration.");
                                    TryFallbackPersistencePath(settings, _logger);
                                }
                            }
                            else
                            {
                                _logger.Warn("Queue persistence path is not configured after settings change. Trying fallback paths...");
                                TryFallbackPersistencePath(settings, _logger);
                            }
                        }
                        else
                        {
                            _persistenceManager = null;
                            _logger.Info("Queue persistence disabled in updated settings.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Error(ex, "Error updating queue persistence settings");
                    }
                }
            }
        }

        public void StartQueueHandler()
        {
            lock (_lock)
            {
                if (_processingTask == null || _processingTask.IsCompleted)
                {
                    _processingCancellationSource = new CancellationTokenSource();
                    _processingTask = Task.Run(() => BackgroundProcessing(_processingCancellationSource.Token));
                    _logger.Debug("Started queue handler task");
                    
                    // Start auto-save timer if enabled
                    if (_settings.EnableQueuePersistence && _autoSaveTimer != null && _persistenceManager != null)
                    {
                        _autoSaveTimer.Start();
                        _logger.Debug("Started queue auto-save timer");
                    }
                }
                else
                {
                    _logger.Debug("Queue handler is already running");
                }
            }
        }

        public void StopQueueHandler()
        {
            lock (_lock)
            {
                _logger.Info("Stopping queue handler...");

                try
                {
                    // Stop auto-save timer
                    if (_autoSaveTimer != null)
                    {
                        _autoSaveTimer.Stop();
                        _logger.Debug("Stopped queue auto-save timer");
                    }
                    
                    // Save queue to disk before stopping if persistence is enabled
                    if (_settings.EnableQueuePersistence && _collectionManager.Count > 0)
                    {
                        SaveQueueToDisk();
                    }

                    _processingCancellationSource?.Cancel();
                    _processingTask?.Wait(TimeSpan.FromSeconds(10)); // Give it 10 seconds to shut down gracefully
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error stopping queue handler");
                }
                finally
                {
                    _processingCancellationSource?.Dispose();
                    _processingCancellationSource = new CancellationTokenSource();
                }

                _logger.Info("Queue handler stopped");
            }
        }

        private async Task BackgroundProcessing(CancellationToken stoppingToken)
        {
            _logger.Debug("[DIAGNOSTIC] Starting queue handler task");

            try
            {
                Random random = new Random();
                DateTime lastRandomStatsTime = DateTime.UtcNow;
                int consecutiveErrors = 0;
                DateTime lastSuccessfulProcessing = DateTime.UtcNow;

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        // Reset the last processed time for stall detection
                        UpdateLastProcessedTime();

                        // Check for queue stalls
                        CheckForStall();

                        // Check for excessive consecutive errors, take a longer break if needed
                        if (consecutiveErrors > 5)
                        {
                            _logger.Warn($"[DIAGNOSTIC] Too many consecutive errors ({consecutiveErrors}), pausing queue processing for 1 minute to stabilize");
                            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                            consecutiveErrors = 0;
                            continue;
                        }

                        // Check for prolonged error state - if no successful processing in 10 minutes, reset handler
                        if (consecutiveErrors > 0 && (DateTime.UtcNow - lastSuccessfulProcessing).TotalMinutes > 10)
                        {
                            _logger.Error($"[DIAGNOSTIC] No successful processing for over 10 minutes with {consecutiveErrors} consecutive errors. Resetting queue handler...");

                            try
                            {
                                StopQueueHandler();
                                // Wait a second for things to clean up
                                await Task.Delay(1000, CancellationToken.None);
                                StartQueueHandler();
                                consecutiveErrors = 0;
                                continue;
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "[DIAGNOSTIC] Error resetting queue handler during error recovery");
                                await Task.Delay(5000, stoppingToken);
                                continue;
                            }
                        }

                        // Adapt behavior based on queue volume
                        _behaviorSimulator.AdaptToQueueVolume(_settings, _collectionManager.Count);

                        // Calculate download rate
                        UpdateDownloadRate();

                        // ENHANCED: Log status updates on a regular interval regardless of other conditions
                        if ((DateTime.UtcNow - _lastRegularUpdateTime) > _regularUpdateInterval)
                        {
                            // Log differently based on whether there are items in the queue
                            if (_collectionManager.Count > 0)
                            {
                                LogQueueContentStatusUpdate();
                            }
                            else
                            {
                                LogEmptyQueueStatus();
                            }
                            _lastRegularUpdateTime = DateTime.UtcNow;
                        }

                        // More detailed logging at larger intervals
                        if (_collectionManager.Count > 0)
                        {
                            // Regular scheduled logging
                            if ((DateTime.UtcNow - _lastStatsLogTime) > _statsLogInterval)
                            {
                                LogQueueStatistics();
                                _lastStatsLogTime = DateTime.UtcNow;
                            }

                            // Random interval logging (approximately every 30-90 minutes)
                            TimeSpan randomInterval = TimeSpan.FromMinutes(random.Next(30, 90));
                            if ((DateTime.UtcNow - lastRandomStatsTime) > randomInterval)
                            {
                                _logger.Info($"{LogEmojis.Info} Random queue status check:");
                                LogQueueStatistics();
                                lastRandomStatsTime = DateTime.UtcNow;
                            }
                        }

                        // First check if we should be processing the queue based on natural behavior settings
                        int queueSize = _collectionManager.Count;
                        _logger?.Debug($"[DIAGNOSTIC] Queue size: {queueSize}, checking if we should process queue");
                        if (!_naturalScheduler.ShouldProcessQueue(queueSize))
                        {
                            _logger?.Debug("[DIAGNOSTIC] Natural scheduler says not to process queue now, waiting 30 seconds");
                            // Wait a bit before checking again
                            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                            continue;
                        }

                        // Don't process if throttling is active
                        if (ShouldThrottleDownload())
                        {
                            _logger?.Debug("[DIAGNOSTIC] Download throttling is active, waiting 10 seconds");
                            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                            continue;
                        }

                        // Get the next item from the queue
                        IDownloadItem item = null;

                        try
                        {
                            _logger?.Debug("[DIAGNOSTIC] Trying to get next item from queue");
                            // Try to use natural scheduler to get next item with context-aware selection
                            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeoutCts.Token);

                            try
                            {
                                item = await _naturalScheduler.GetNextItem(linkedCts.Token);
                                _logger?.Debug(item != null
                                    ? "[DIAGNOSTIC] Got item from scheduler: {item.Title} by {item.Artist}"
                                    : "[DIAGNOSTIC] No item returned from scheduler");
                            }
                            catch (OperationCanceledException)
                            {
                                if (stoppingToken.IsCancellationRequested)
                                {
                                    break;
                                }

                                _logger.Debug("[DIAGNOSTIC] Timeout getting next item from scheduler, will retry");
                                await Task.Delay(1000, stoppingToken);
                                continue;
                            }

                            // If we didn't get an item from the scheduler, check if there's one in the queue
                            if (item == null && _queue.Reader.TryRead(out var directItem))
                            {
                                item = directItem;
                                _logger.Debug($"[DIAGNOSTIC] Got item directly from queue: {item.Title} by {item.Artist}");
                            }
                        }
                        catch (ChannelClosedException)
                        {
                            _logger.Debug("[DIAGNOSTIC] Queue channel was closed");
                            break;
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.Debug("[DIAGNOSTIC] Queue processing was canceled");
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "[DIAGNOSTIC] Error getting next item");
                            consecutiveErrors++;
                            await Task.Delay(3000, stoppingToken);
                            continue;
                        }

                        // If we couldn't get an item, wait a bit before trying again
                        if (item == null)
                        {
                            _logger?.Debug("[DIAGNOSTIC] No item available, waiting 1 second");
                            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                            continue;
                        }

                        try
                        {
                            // Process the item
                            _logger?.Debug($"[DIAGNOSTIC] Processing item: {item.Title} by {item.Artist}");
                            await ProcessItemAsync(item, stoppingToken);
                            _logger?.Debug($"[DIAGNOSTIC] Successfully processed item: {item.Title} by {item.Artist}");

                            // Reset error counters on success
                            consecutiveErrors = 0;
                            lastSuccessfulProcessing = DateTime.UtcNow;
                            
                            // Add a small delay between item processing to reduce contention with search operations
                            // Only apply the delay if it's set to a positive value
                            if (_settings.ItemProcessingDelayMs > 0)
                            {
                                await Task.Delay(TimeSpan.FromMilliseconds(_settings.ItemProcessingDelayMs), stoppingToken);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, $"[DIAGNOSTIC] Error processing item: {ex.Message}");
                            consecutiveErrors++;

                            // Make sure item is removed from queue even on failure
                            try
                            {
                                _collectionManager.RemoveItem(item);
                                _logger?.Debug("[DIAGNOSTIC] Removed failed item from queue");
                            }
                            catch (Exception removeEx)
                            {
                                _logger.Debug(removeEx, "[DIAGNOSTIC] Error removing item after processing failure");
                            }

                            // Add a delay after failure to avoid retrying too quickly
                            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        // Expected when cancellation is requested
                        if (stoppingToken.IsCancellationRequested)
                        {
                            break;
                        }

                        _logger.Warn("[DIAGNOSTIC] Background task processing timed out, resetting...");
                        consecutiveErrors++;
                        // Wait a bit before trying again
                        await Task.Delay(5000, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when cancellation is requested
                        if (stoppingToken.IsCancellationRequested)
                        {
                            break;
                        }

                        _logger.Warn("[DIAGNOSTIC] Background operation cancelled, resetting...");
                        consecutiveErrors++;
                        // Wait a bit before trying again
                        await Task.Delay(5000, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "[DIAGNOSTIC] Unhandled exception in download queue processor");
                        consecutiveErrors++;
                        // Wait a bit before trying again
                        await Task.Delay(5000, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
                _logger.Info("[DIAGNOSTIC] Download queue processor shutdown");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[DIAGNOSTIC] Unhandled exception in download queue processor main loop");
            }
            finally
            {
                _logger.Info("[DIAGNOSTIC] Download queue background processing exited");
            }
        }

        private void UpdateLastProcessedTime()
        {
            lock (_stallDetectionLock)
            {
                _lastProcessedTime = DateTime.UtcNow;
                _stallDetectionCount = 0;
            }
        }

        private void CheckForStall()
        {
            // Only check for stalls if we have items to process
            if (_collectionManager.Count > 0)
            {
                lock (_stallDetectionLock)
                {
                    // If last processed time is set and it's been too long
                    if (_lastProcessedTime != DateTime.MinValue &&
                        (DateTime.UtcNow - _lastProcessedTime) > _stallDetectionThreshold)
                    {
                        _stallDetectionCount++;

                        _logger.Warn($"Queue stall detected! No activity for {_stallDetectionThreshold.TotalMinutes} minutes. " +
                                     $"Stall count: {_stallDetectionCount}");

                        // If we've detected multiple stalls, reset the queue handler
                        if (_stallDetectionCount >= 3)
                        {
                            _logger.Error("Multiple queue stalls detected, resetting queue handler...");

                            try
                            {
                                StopQueueHandler();
                                // Wait a second for things to clean up
                                Thread.Sleep(1000);
                                StartQueueHandler();

                                // Reset the stall counter
                                _stallDetectionCount = 0;
                                _lastProcessedTime = DateTime.UtcNow;
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "Error resetting queue handler after stall detection");
                            }
                        }
                    }
                }
            }
        }

        private void UpdateDownloadRate()
        {
            // Check if rate limiting is enabled
            if (_settings.MaxDownloadsPerHour <= 0)
            {
                return;
            }

            // Calculate current rate
            var downloadRate = _stats.DownloadRatePerHour;
            bool isRateLimited = _stats.IsRateLimited;

            // Update the status manager with the current rate
            if (_statusManager != null)
            {
                try
                {
                    _statusManager.UpdateRateInformation(
                        downloadRate,
                        _settings.MaxDownloadsPerHour,
                        isRateLimited,
                        _stats.GetTimeUntilRateLimitResetTimeSpan());
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "Error updating rate information in status manager");
                }
            }

            // Let the statistics manager handle the logging
            _stats.UpdateDownloadRate();
        }

        private void UpdateDownloadStatusManager()
        {
            // Skip if status manager is disabled
            if (_statusManager == null)
            {
                return;
            }

            try
        {
            var artistStats = GetArtistStatistics();

            // Update general queue statistics
            _statusManager.UpdateQueueStatistics(
                _collectionManager.Count,
                _stats.TotalItemsProcessed,
                _stats.FailedDownloads,
                ((UserBehaviorSimulator)_behaviorSimulator).SessionsCompleted,
                ((UserBehaviorSimulator)_behaviorSimulator).IsHighVolumeMode,
                _stats.DownloadRatePerHour
            );

            // Update artist statistics
            foreach (var artist in artistStats)
            {
                _statusManager.AddOrUpdateArtist(
                    artist.Key,
                    artist.Value.Item1, // pending
                    artist.Value.Item2, // completed (estimate)
                    artist.Value.Item3, // failed (estimate)
                    artist.Value.Item4  // albums
                );
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error updating download status manager");
            }
        }

        /// <summary>
        /// Logs current queue statistics and status information
        /// </summary>
        private void LogQueueStatistics()
        {
            // Skip if there's nothing in the queue
            if (_collectionManager.Count == 0)
            {
                LogEmptyQueueStatus();
                return;
            }

            // Get basic stats
            int pendingCount = _collectionManager.Count;
            int processedCount = _stats.TotalItemsProcessed;
            int failedCount = _stats.FailedDownloads;
            bool isHighVolume = ((UserBehaviorSimulator)_behaviorSimulator).IsHighVolumeMode;

            // Calculate estimated time if throttling
            string throttleStatus = string.Empty;
            if (_stats.ShouldThrottleDownload() && _settings.MaxDownloadsPerHour > 0)
            {
                throttleStatus = $" {LogEmojis.Wait} " + _stats.GetRateLimitStatusString();
            }

            // Get info about processing times
            string processingStatus = _naturalScheduler.ShouldProcessQueue(pendingCount)
                ? $"{LogEmojis.Resume} Active"
                : $"{LogEmojis.Pause} Paused (outside active hours)";

            // Get unique artists and albums
            var artistCount = _collectionManager.GetAllItems().Select(i => i.Artist).Distinct().Count();
            var albumCount = _collectionManager.GetAllItems().Select(i => i.Album).Distinct().Count();

            // Calculate average processing rate (per hour)
            int averageRate = _stats.DownloadRatePerHour;

            // Log main queue stats
            _logger.Info($"{LogEmojis.Stats} Queue Status at {DateTime.Now:HH:mm:ss}: {processingStatus}{throttleStatus}");
            _logger.Info($"   {LogEmojis.Data} Items: {pendingCount} pending, {processedCount} completed, {failedCount} failed");
            _logger.Info($"   {LogEmojis.User} Content: {artistCount} artists, {albumCount} albums, mode: {(isHighVolume ? "high volume" : "normal")}");

            if (pendingCount > 0 && averageRate > 0)
            {
                // Estimate completion time (very rough)
                double estimatedHours = (double)pendingCount / averageRate;
                string estimatedTime = estimatedHours < 1
                    ? $"{(int)(estimatedHours * 60)}m"
                    : $"{(int)estimatedHours}h {(int)((estimatedHours - Math.Floor(estimatedHours)) * 60)}m";

                _logger.Info($"   {LogEmojis.Time} Processing: Current rate ~{averageRate}/hr, est. completion in {estimatedTime}");
            }

            // Log next few download items if available
            if (pendingCount > 0)
            {
                _logger.Info($"   {LogEmojis.Queue} Next in queue:");
                var nextItems = _collectionManager.GetAllItems().OrderBy(i => i.QueuedTime).Take(Math.Min(3, pendingCount)).ToList();
                foreach (var item in nextItems)
                {
                    _logger.Info($"     {LogEmojis.Music} {item.Artist} - {item.Album}");
                }

                if (pendingCount > 3)
                {
                    _logger.Info($"     {LogEmojis.Info} ...and {pendingCount - 3} more items");
                }
            }

            // Log rate limit status if throttled
            if (_stats.IsRateLimited)
            {
                _logger.Info($"   {LogEmojis.Wait} {_stats.GetRateLimitStatusString()}");
            }
        }

        /// <summary>
        /// Logs the status when the queue is empty, explaining why there are no downloads
        /// </summary>
        private void LogEmptyQueueStatus()
        {
            _logger.Info($"{LogEmojis.Status} ━━━━━━ QUEUE STATUS ━━━━━━");
            _logger.Info($"{LogEmojis.Queue}  •  Status: Empty  •  Time: {DateTime.Now:HH:mm:ss}");

            // Check if downloads are enabled
            if (_settings != null && _settings.GetType().GetProperty("Enable")?.GetValue(_settings) is bool enabled && !enabled)
            {
                _logger.Info($"{LogEmojis.Stop}  •  Downloads are disabled in settings");
                _logger.Info($"{LogEmojis.Status} ━━━━━━━━━━━━━━━━━━━━━━━━━");
                return;
            }

            // Check if we're within active hours
            bool isActive = _naturalScheduler.ShouldProcessQueue(0);
            if (!isActive)
            {
                _logger.Info($"{LogEmojis.Schedule}  •  Outside active hours");
                _logger.Info($"{LogEmojis.Wait}  •  Downloads will resume during the next active period");
                _logger.Info($"{LogEmojis.Status} ━━━━━━━━━━━━━━━━━━━━━━━━━");
                return;
            }

            // Check if rate limited
            if (_stats.IsRateLimited && _settings.MaxDownloadsPerHour > 0)
            {
                string limitStatus = _stats.GetRateLimitStatusString().Replace($"{LogEmojis.Stop} ", "").Replace($"{LogEmojis.Warning} ", "");
                _logger.Info($"{LogEmojis.Wait}  •  Rate limit active  •  {limitStatus}");
                _logger.Info($"{LogEmojis.Time}  •  Reset in {_stats.GetTimeUntilRateLimitReset()}");
                _logger.Info($"{LogEmojis.Status} ━━━━━━━━━━━━━━━━━━━━━━━━━");
                return;
            }

            // If we get here, the queue is empty but the system is ready to download
            _logger.Info($"{LogEmojis.Success}  •  System ready  •  Waiting for new items from Lidarr");
            if (_stats.TotalItemsProcessed > 0)
            {
                _logger.Info($"{LogEmojis.Stats}  •  Session stats  •  {_stats.TotalItemsProcessed} processed  •  {_stats.FailedDownloads} failed");
            }
            _logger.Info($"{LogEmojis.Status} ━━━━━━━━━━━━━━━━━━━━━━━━━");
        }

        /// <summary>
        /// Formats a TimeSpan into a human-readable string
        /// </summary>
        private string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalHours >= 1)
            {
                return $"{(int)timeSpan.TotalHours}h {timeSpan.Minutes}m";
            }
            else if (timeSpan.TotalMinutes >= 1)
            {
                return $"{timeSpan.Minutes}m {timeSpan.Seconds}s";
            }
            else
            {
                return $"{timeSpan.Seconds}s";
            }
        }

        private Dictionary<string, Tuple<int, int, int, List<string>>> GetArtistStatistics()
        {
            var result = new Dictionary<string, Tuple<int, int, int, List<string>>>();
            var items = _collectionManager.GetAllItems();

            // Group by artist
            var byArtist = items.GroupBy(i => i.Artist).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var artist in byArtist)
            {
                int pending = artist.Value.Count;

                // Get unique albums
                var albums = artist.Value
                    .Select(i => i.Album)
                    .Distinct()
                    .ToList();

                // Estimate completed and failed tracks - we don't have actual per-artist tracking
                // so we'll just distribute completed/failed proportionally to queue size
                double artistProportion = (double)pending / Math.Max(1, items.Length);
                int estimated_completed = (int)(artistProportion * _stats.TotalItemsProcessed);
                int estimated_failed = (int)(artistProportion * _stats.FailedDownloads);

                result[artist.Key] = new Tuple<int, int, int, List<string>>(
                    pending, estimated_completed, estimated_failed, albums
                );
            }

            return result;
        }

        public async ValueTask QueueBackgroundWorkItemAsync(IDownloadItem workItem, CancellationToken cancellationToken = default)
        {
            _logger?.Debug($"[DIAGNOSTIC] QueueBackgroundWorkItemAsync called for {workItem.Title} by {workItem.Artist}");

            // Check for duplicate items first - don't add if it's already in the queue
            if (IsDuplicateItem(workItem))
            {
                _logger?.Info($"{LogEmojis.Info} [DIAGNOSTIC] Skipping duplicate download: {workItem.Title} by {workItem.Artist} (already in queue)");
                return;
            }

            const int maxRetryAttempts = 5; // Increased from 3 to 5
            int attempt = 0;
            TimeSpan initialRetryDelay = TimeSpan.FromMilliseconds(500);
            TimeSpan retryDelay = initialRetryDelay;
            bool addedToQueue = false;
            bool queueMightBeFull = false;

            // Check queue capacity before attempting to add
            var queueStats = GetQueueStats();
            if (queueStats.CurrentCount >= queueStats.Capacity * 0.9)
            {
                _logger?.Warn($"{LogEmojis.Warning} Queue is nearly full ({queueStats.CurrentCount}/{queueStats.Capacity}). Download of '{workItem.Title}' by {workItem.Artist} may be delayed.");
                queueMightBeFull = true;
            }

            // Attempt to add to queue with retries
            while (attempt < maxRetryAttempts && !addedToQueue && !cancellationToken.IsCancellationRequested)
            {
                attempt++;

                // Use a timeout to prevent writer from blocking indefinitely
                // Scale timeout with attempt number to give later attempts more time
                using var timeoutCts = new CancellationTokenSource(_queueOperationTimeout.Add(TimeSpan.FromSeconds(attempt * 5)));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                try
                {
                    if (attempt > 1)
                    {
                        _logger?.Debug($"{LogEmojis.Time} Retrying queue add (attempt {attempt}/{maxRetryAttempts}) for '{workItem.Title}' after {retryDelay.TotalMilliseconds}ms delay");
                    }
                    
                    // Use TryWrite instead of WriteAsync when available for better timeout control
                    bool immediateSuccess = _queue.Writer.TryWrite(workItem);

                    if (immediateSuccess)
                    {
                        _logger.Debug($"{LogEmojis.Success} Immediately added '{workItem.Title}' by {workItem.Artist} to queue");
                        addedToQueue = true;
                    }
                    else
                    {
                        // If synchronous attempt failed, try asynchronous with timeout
                        _logger.Debug($"{LogEmojis.Wait} Attempting async queue add for '{workItem.Title}', attempt {attempt}/{maxRetryAttempts}");

                        // Use a task with timeout to avoid potential hanging
                        var writeTask = _queue.Writer.WriteAsync(workItem, linkedCts.Token).AsTask();

                        // Wait for completion with timeout
                        if (await Task.WhenAny(writeTask, Task.Delay(timeoutCts.Token.WaitHandle.WaitOne(0) ? 0 : (int)_queueOperationTimeout.TotalMilliseconds, linkedCts.Token)) == writeTask)
                        {
                            await writeTask; // Propagate any exceptions
                            addedToQueue = true;
                            _logger?.Debug($"{LogEmojis.Success} Successfully added to queue via async call");
                        }
                        else if (timeoutCts.IsCancellationRequested)
                        {
                            if (queueMightBeFull)
                            {
                                throw new OperationCanceledException($"Timed out while waiting for queue space. Queue appears to be full ({queueStats.CurrentCount}/{queueStats.Capacity})", linkedCts.Token);
                            }
                            else
                            {
                                throw new OperationCanceledException("Timed out while waiting for queue space", linkedCts.Token);
                            }
                        }
                    }
                }
                catch (OperationCanceledException ex)
                {
                    if (timeoutCts.IsCancellationRequested)
                    {
                        if (queueMightBeFull)
                        {
                            _logger.Warn($"{LogEmojis.Warning} Queue appears to be full. Timed out adding '{workItem.Title}' to queue on attempt {attempt}/{maxRetryAttempts}");
                            
                            // Check if we've reached capacity limit and provide helpful message
                            if (queueStats.CurrentCount >= queueStats.Capacity)
                            {
                                _logger.Error($"{LogEmojis.Error} Queue capacity reached ({queueStats.Capacity} items). Consider increasing Queue Capacity in settings.");
                                
                                if (attempt == maxRetryAttempts)
                                {
                                    throw new InvalidOperationException($"Queue capacity limit reached ({queueStats.Capacity}). Increase Queue Capacity in Tidal settings.", ex);
                                }
                            }
                        }
                        else
                        {
                            _logger.Warn($"{LogEmojis.Warning} Timed out adding item to queue on attempt {attempt}/{maxRetryAttempts}: '{workItem.Title}' by {workItem.Artist}");
                        }

                        // Only retry if this is not the last attempt
                        if (attempt < maxRetryAttempts)
                        {
                            _logger.Info($"{LogEmojis.Retry} Retrying queue add after a delay (attempt {attempt}/{maxRetryAttempts})");

                            try
                            {
                                // Wait before retrying with exponential backoff
                                await Task.Delay(retryDelay, cancellationToken);
                                
                                // Exponential backoff with jitter for next retry
                                var jitter = new Random().Next(-200, 200); // Add randomness (-200ms to +200ms)
                                retryDelay = TimeSpan.FromMilliseconds(
                                    Math.Min(30000, initialRetryDelay.TotalMilliseconds * Math.Pow(2, attempt - 1) + jitter)
                                );
                                
                                _logger.Debug($"{LogEmojis.Wait} Next retry in {retryDelay.TotalMilliseconds}ms");
                            }
                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                            {
                                // User cancelled during retry delay
                                throw;
                            }
                        }
                        else
                        {
                            // This was the last attempt - rethrow
                            throw;
                        }
                    }
                    else if (cancellationToken.IsCancellationRequested)
                    {
                        // User cancelled the operation
                        throw;
                    }
                }
            }

            // If successfully added to queue, update tracking
            if (addedToQueue)
            {
                _logger?.Debug($"[DIAGNOSTIC] Adding item to collection manager");
                _collectionManager.AddItem(workItem);
                Interlocked.Increment(ref _totalItemsQueued);

                // Log when we hit large queue sizes
                int currentCount = _collectionManager.Count;
                _logger?.Debug($"[DIAGNOSTIC] Current queue count after adding: {currentCount}");

                if (currentCount % 1000 == 0)
                {
                    _logger.Info($"{LogEmojis.Stats} Queue milestone reached: {currentCount} items");
                    _behaviorSimulator.LogSessionStats();
                }
                else if (currentCount == 1)
                {
                    _logger.Info($"{LogEmojis.Start}  •  Queue started  •  First item: \"{workItem.Title}\"  •  Artist: {workItem.Artist}");
                }
                else if (currentCount % 100 == 0)
                {
                    _logger.Debug($"{LogEmojis.Stats} Queue size: {currentCount} items");
                }

                // Save the queue to disk after adding a new item
                if (_settings.EnableQueuePersistence)
                {
                    SaveQueueToDisk();
                }
            }
            else if (!cancellationToken.IsCancellationRequested)
            {
                // All attempts failed but not due to cancellation
                _logger.Error($"{LogEmojis.Error} Failed to add '{workItem.Title}' by {workItem.Artist} to queue after {maxRetryAttempts} attempts");
                throw new InvalidOperationException($"Failed to add item to queue after {maxRetryAttempts} attempts");
            }
        }

        /// <summary>
        /// Gets current queue statistics, including capacity and usage
        /// </summary>
        private (int Capacity, int CurrentCount) GetQueueStats()
        {
            // Get capacity from bounded channel options or from settings
            var capacity = _settings?.QueueCapacity ?? 100;
            var currentCount = _collectionManager.Count;
            return (capacity, currentCount);
        }

        /// <summary>
        /// Checks if an item already exists in the queue (to prevent duplicates)
        /// </summary>
        /// <param name="item">The item to check</param>
        /// <returns>True if a duplicate exists, false otherwise</returns>
        private bool IsDuplicateItem(IDownloadItem item)
        {
            if (item == null) return false;

            try
            {
                var existingItems = _collectionManager.GetAllItems();
                _logger?.Debug($"[DIAGNOSTIC] IsDuplicateItem checking against {existingItems.Length} existing items");

                // Extract album ID from the new item for comparison
                string newItemAlbumId = null;
                string numericAlbumId = null;

                if (item is Lidarr.Plugin.Tidal.Download.Clients.Tidal.DownloadItem downloadItem && 
                    downloadItem.RemoteAlbum?.Release?.Guid != null)
                {
                    newItemAlbumId = downloadItem.RemoteAlbum.Release.Guid.Replace("tidal:", "");
                    
                    // Extract numeric part for comparison
                    if (newItemAlbumId.StartsWith("Tidal-", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = newItemAlbumId.Split('-');
                        if (parts.Length >= 2)
                        {
                            numericAlbumId = parts[1];
                            _logger?.Debug($"[DIAGNOSTIC] Extracted numeric album ID: {numericAlbumId} from {newItemAlbumId}");
                        }
                    }
                }

                // First check if we have the exact same album ID in the queue
                if (!string.IsNullOrEmpty(numericAlbumId))
                {
                    foreach (var existing in existingItems)
                    {
                        if (existing is Lidarr.Plugin.Tidal.Download.Clients.Tidal.DownloadItem existingDownloadItem && 
                            existingDownloadItem.RemoteAlbum?.Release?.Guid != null)
                        {
                            string existingAlbumId = existingDownloadItem.RemoteAlbum.Release.Guid.Replace("tidal:", "");
                            
                            // Extract numeric part for comparison
                            if (existingAlbumId.StartsWith("Tidal-", StringComparison.OrdinalIgnoreCase))
                            {
                                var parts = existingAlbumId.Split('-');
                                if (parts.Length >= 2 && parts[1] == numericAlbumId)
                                {
                                    _logger?.Info($"[DIAGNOSTIC] Found duplicate by album ID {numericAlbumId} for {item.Title} by {item.Artist}");
                                    return true;
                                }
                            }
                        }
                    }
                }

                // Fall back to title/artist matching if no album ID match
                var duplicates = existingItems.Where(existing =>
                    !string.IsNullOrEmpty(existing.Title) &&
                    !string.IsNullOrEmpty(existing.Artist) &&
                    !string.IsNullOrEmpty(item.Title) &&
                    !string.IsNullOrEmpty(item.Artist) &&
                    String.Equals(existing.Title, item.Title, StringComparison.OrdinalIgnoreCase) &&
                    String.Equals(existing.Artist, item.Artist, StringComparison.OrdinalIgnoreCase)).ToList();

                if (duplicates.Any())
                {
                    _logger?.Info($"[DIAGNOSTIC] Found {duplicates.Count} duplicate items by title/artist for {item.Title} by {item.Artist}");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"[DIAGNOSTIC] Error in IsDuplicateItem check for {item.Title} by {item.Artist}");
                // If there's an error checking for duplicates, we should continue with adding the item
                return false;
            }
        }

        public void RemoveItem(IDownloadItem item)
        {
            bool itemRemoved = _collectionManager.RemoveItem(item);

            if (itemRemoved)
            {
                // If the queue is empty, remove any saved queue file
                if (_collectionManager.Count == 0)
                {
                    _logger.Info($"{LogEmojis.Success}  •  Queue completed  •  No more items in queue");

                    if (_settings.EnableQueuePersistence)
                    {
                        string persistencePath = _settings.ActualQueuePersistencePath;
                        if (!string.IsNullOrWhiteSpace(persistencePath))
                        {
                            try
                            {
                                var queueFilePath = Path.Combine(persistencePath, "queue", "tidal_queue.json");
                                if (File.Exists(queueFilePath))
                                {
                                    File.Delete(queueFilePath);
                                    _logger.Debug("Deleted queue persistence file after queue emptied");
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Debug(ex, "Error deleting queue persistence file");
                            }
                        }
                    }
                }
                else if (_settings.EnableQueuePersistence && _collectionManager.Count % 5 == 0)
                {
                    // Periodically save the queue if it changes significantly (every 5 items removed)
                    SaveQueueToDisk();
                }
            }
        }

        /// <summary>
        /// Automatically saves the queue to disk on a timer.
        /// </summary>
        private void AutoSaveQueue()
        {
            try
            {
                if (_settings == null || !_settings.EnableQueuePersistence)
                {
                    return;
                }

                var now = DateTime.UtcNow;
                
                // Get current queue size for adaptive saving
                int queueSize = _collectionManager.Count;
                bool isHighVolumeMode = queueSize > (_settings.HighVolumeThreshold > 0 ? _settings.HighVolumeThreshold : 500);
                
                // Determine if we should save now based on time since last save and queue size
                bool shouldSaveNow = false;
                
                if ((now - _lastAutoSaveTime).TotalMinutes >= 2) // Regular 2-minute interval
                {
                    shouldSaveNow = true;
                    _logger?.Debug($"{LogEmojis.Save} Regular 2-minute queue persistence interval triggered");
                }
                else if (isHighVolumeMode && (now - _lastAutoSaveTime).TotalSeconds >= 30) // More frequent saves for high volume
                {
                    shouldSaveNow = true;
                    _logger?.Debug($"{LogEmojis.Save} High-volume rapid persistence interval triggered ({queueSize} items in queue)");
                }
                
                if (shouldSaveNow)
                {
                    _logger?.Debug("Auto-saving queue to disk...");
                    SaveQueueToDisk();
                    _lastAutoSaveTime = now;
                }
            }
            catch (Exception ex)
            {
                // Log but don't throw - we don't want auto-save failures to affect the queue operation
                _logger?.Error(ex, $"{LogEmojis.Error} Error during queue auto-save: {ex.Message}");
            }
        }

        private void SaveQueueToDisk()
        {
            if (_persistenceManager == null)
            {
                _logger?.Debug("Queue persistence manager is null - can't save queue");
                return;
            }

            lock (_autoSaveLock)
            {
                try
                {
                    // Get a snapshot of queue items
                    var items = _collectionManager.GetAllItems();
                    
                    // Don't save if queue is empty
                    if (items.Length == 0)
                    {
                        _logger?.Debug("Queue is empty - nothing to save");
                        return;
                    }

                    _logger?.Debug($"Saving queue to disk ({items.Length} items)...");
                    
                    bool success = false;
                    Exception lastException = null;
                    string queueFilePath = string.Empty;
                    
                    try
                    {
                        // Get the queue file path for backup purposes
                        if (_settings?.ActualQueuePersistencePath != null)
                        {
                            queueFilePath = Path.Combine(_settings.ActualQueuePersistencePath, "queue", "tidal_queue.json");
                            
                            // Create backup of existing queue file if it exists
                            if (File.Exists(queueFilePath))
                            {
                                string backupPath = queueFilePath + ".bak";
                                try
                                {
                                    if (File.Exists(backupPath))
                                    {
                                        File.Delete(backupPath);
                                    }
                                    File.Copy(queueFilePath, backupPath);
                                    _logger?.Debug($"Created backup of queue file at {backupPath}");
                                }
                                catch (Exception ex)
                                {
                                    _logger?.Warn($"Failed to create backup of queue file: {ex.Message}");
                                    // Continue - this is just a backup failure, not critical
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warn($"Error preparing for queue save: {ex.Message}");
                        // Continue with save attempt despite preparation error
                    }
                    
                    // Try up to 3 times with exponential backoff
                    for (int attempt = 1; attempt <= 3; attempt++)
                    {
                        try
                        {
                            // Save queue using persistence manager
                            _persistenceManager.SaveQueue(items);
                            
                            // Verify file was written correctly
                            if (!string.IsNullOrEmpty(queueFilePath) && File.Exists(queueFilePath))
                            {
                                var fileInfo = new FileInfo(queueFilePath);
                                if (fileInfo.Length > 0)
                                {
                                    // Additional check: try to parse the JSON to verify it's valid
                                    try
                                    {
                                        string json = File.ReadAllText(queueFilePath);
                                        // If we can parse it as JSON, it's probably valid
                                        if (!string.IsNullOrWhiteSpace(json) && 
                                            (json.TrimStart().StartsWith("[") || json.TrimStart().StartsWith("{")))
                                        {
                                            success = true;
                                            _logger?.Debug($"Queue file verified: {fileInfo.Length} bytes");
                                            break;
                                        }
                                        else
                                        {
                                            _logger?.Warn("Queue file doesn't appear to contain valid JSON");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger?.Warn($"Error verifying queue file: {ex.Message}");
                                        // Continue to retry
                                    }
                                }
                                else
                                {
                                    _logger?.Warn("Queue file was created but is empty");
                                }
                            }
                            else
                            {
                                // If we reach here and queueFilePath was set, it means the file wasn't created
                                if (!string.IsNullOrEmpty(queueFilePath))
                                {
                                    _logger?.Warn("Queue file was not created at the expected location");
                                }
                                else
                                {
                                    // If queueFilePath wasn't available, assume success since we didn't get an exception
                                    success = true;
                                    break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            lastException = ex;
                            _logger?.Warn(ex, $"{LogEmojis.Warning} Error saving queue to disk (attempt {attempt}/3): {ex.Message}");
                            
                            // Don't sleep on the last attempt
                            if (attempt < 3)
                            {
                                Thread.Sleep(100 * attempt * attempt); // 100ms, 400ms, 900ms
                            }
                        }
                    }
                    
                    if (success)
                    {
                        _logger?.Debug($"{LogEmojis.Success} Queue saved to disk successfully");
                        _lastAutoSaveTime = DateTime.UtcNow; // Update save time even if auto-triggered
                    }
                    else if (lastException != null)
                    {
                        _logger?.Error(lastException, $"{LogEmojis.Error} Failed to save queue to disk after 3 attempts: {lastException.Message}");
                        
                        // Try to restore from backup if we failed to save
                        if (!string.IsNullOrEmpty(queueFilePath))
                        {
                            string backupPath = queueFilePath + ".bak";
                            if (File.Exists(backupPath))
                            {
                                try
                                {
                                    _logger?.Info($"Attempting to restore queue from backup after failed save");
                                    File.Copy(backupPath, queueFilePath, true);
                                    _logger?.Info($"Successfully restored queue from backup");
                                }
                                catch (Exception ex)
                                {
                                    _logger?.Error(ex, $"Failed to restore queue from backup: {ex.Message}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, $"{LogEmojis.Error} Error saving queue to disk: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Restores the queue from disk.
        /// </summary>
        private void RestoreQueueFromDisk()
        {
            if (!_settings.EnableQueuePersistence || _persistenceManager == null)
            {
                return;
            }

            try
            {
                // Use the persistence manager to load the queue
                var records = _persistenceManager.LoadQueue();

                if (records.Count == 0)
                {
                    _logger.Debug("No queue items to restore");
                return;
            }

                _logger.Info($"Restoring {records.Count} items from queue file");

                // Convert records back to download items and add them to the queue
                foreach (var record in records)
                {
                    try
                    {
                        // Create a new download item from the record
                        var downloadItem = new Lidarr.Plugin.Tidal.Download.Clients.Tidal.DownloadItem
                        {
                            Id = record.ID,
                            Title = record.Title,
                            Artist = record.Artist,
                            Album = record.Album,
                            BitrateInt = (int)Enum.Parse(typeof(AudioQuality), record.Bitrate),
                            TotalSize = record.TotalSize,
                            DownloadFolder = record.DownloadFolder,
                            Explicit = record.Explicit,
                            RemoteAlbumJson = record.RemoteAlbumJson,
                            QueuedTime = record.QueuedTime,
                            Status = NzbDrone.Core.Download.Clients.Tidal.Interfaces.DownloadItemStatus.Queued,
                            Priority = (NzbDrone.Core.Download.Clients.Tidal.Interfaces.DownloadItemPriority)record.Priority
                        };

                        // Add the item to the collection manager
                        _collectionManager.AddItem(downloadItem);

                        // Queue the item for processing
                        _queue.Writer.TryWrite(downloadItem);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Error restoring queue item {record.Title}");
                    }
                }

                _logger.Info($"Successfully restored {records.Count} items to the queue");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error restoring queue from disk");
            }
        }

        /// <summary>
        /// Processes a single download item.
        /// </summary>
        /// <param name="item">The item to process.</param>
        /// <param name="stoppingToken">A token to monitor for cancellation requests.</param>
        private async Task ProcessItemAsync(IDownloadItem item, CancellationToken stoppingToken)
        {
            if (item == null)
            {
                return;
            }

            try
            {
                _logger.Info($"Processing download item: {item.Title} by {item.Artist}");

                // Cast to our concrete implementation if possible
                if (item is Lidarr.Plugin.Tidal.Download.Clients.Tidal.DownloadItem downloadItem)
                {
                    // Use a lock to prevent race conditions when checking and removing items
                    // We use item.Id as the lock key to allow different items to process in parallel
                    using var itemLock = await _collectionManager.AcquireLockAsync(item.Id, TimeSpan.FromMinutes(1), stoppingToken);
                    
                    // Check if the item is already removed from the queue
                    // This prevents processing the same item multiple times
                    if (!_collectionManager.GetAllItems().Contains(item))
                    {
                        _logger.Debug($"[TIDAL] Item {item.Title} by {item.Artist} appears to be already removed from queue. Skipping processing.");
                        return;
                    }

                    // Get the settings from the item if available
                    var settings = downloadItem.Settings ?? _settings;

                    // Verify download path exists
                    if (string.IsNullOrWhiteSpace(settings.DownloadPath))
                    {
                        throw new InvalidOperationException("Download path is not configured in Tidal settings");
                    }

                    if (!Directory.Exists(settings.DownloadPath))
                    {
                        _logger.Info($"Creating download directory: {settings.DownloadPath}");
                        Directory.CreateDirectory(settings.DownloadPath);
                    }

                    // Remove the item from the queue BEFORE processing to prevent duplicate processing
                    bool itemRemoved = _collectionManager.RemoveItem(item);
                    _logger.Debug($"[TIDAL] Removed item from queue before processing: {itemRemoved}. Current queue size: {_collectionManager.Count}");

                    // Start the actual download using the extension method properly
                    await DownloadItemExtensions.DoDownload(downloadItem, settings, _logger, stoppingToken);

                    _logger.Info($"Download completed for: {item.Title} by {item.Artist}");
                    
                    // Update statistics
                    Interlocked.Increment(ref _totalItemsProcessed);
                    
                    // Note: We already removed the item from the collection manager above
                }
                else
                {
                    _logger.Warn($"Item is not a Tidal DownloadItem: {item.GetType().Name}");
                    // Still remove non-Tidal items to prevent queue clogging
                    _collectionManager.RemoveItem(item);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.Error(ex, $"Error processing download item: {item.Title}");
                Interlocked.Increment(ref _failedDownloads);
                
                // Remove failed items from the queue as well
                try
                {
                    _logger.Debug($"[TIDAL] Removing failed item {item.Title} by {item.Artist} from queue");
                    _collectionManager.RemoveItem(item);
                }
                catch (Exception removeEx)
                {
                    _logger.Debug(removeEx, $"[TIDAL] Error removing failed item from queue: {removeEx.Message}");
                }
            }
        }

        /// <summary>
        /// Implements IDisposable.Dispose() to clean up resources
        /// </summary>
        public void Dispose()
        {
            try
            {
                _logger.Debug("[DIAGNOSTICS] Disposing DownloadTaskQueue");
                StopQueueHandler();
                
                // Dispose auto-save timer
                if (_autoSaveTimer != null)
                {
                    _autoSaveTimer.Elapsed -= (sender, args) => AutoSaveQueue();
                    _autoSaveTimer.Dispose();
                    _autoSaveTimer = null;
                }
                
                _processingCancellationSource?.Dispose();
                _processingCancellationSource = null;
                _collectionManager?.Dispose();
                _logger.Debug("[DIAGNOSTICS] DownloadTaskQueue disposed");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[DIAGNOSTICS] Error disposing DownloadTaskQueue");
            }
        }

        public IDownloadItem[] GetQueueListing()
        {
            return _collectionManager.GetAllItems();
        }

        public int GetQueueSize()
        {
            return _collectionManager.Count;
        }

        private CancellationToken GetTokenForItem(IDownloadItem item, CancellationToken linkedToken = default)
        {
            return _collectionManager.GetTokenForItem(item, linkedToken);
        }

        /// <summary>
        /// Add this method to allow the NaturalDownloadScheduler to check the rate
        /// </summary>
        public int GetCurrentHourlyDownloadRate()
        {
            return _stats.DownloadRatePerHour;
        }

        // Add this method to allow the NaturalDownloadScheduler to check the rate
        internal bool IsRateLimited() => _stats.IsRateLimited;

        /// <summary>
        /// Determines if downloads should be throttled based on current rate limits and settings
        /// </summary>
        private bool ShouldThrottleDownload()
        {
            return _stats.ShouldThrottleDownload();
        }

        public IEnumerable<IDownloadItem> GetQueuedItems()
        {
            // Return a copy of all items currently in the queue
            return _collectionManager.GetAllItems();
        }

        /// <summary>
        /// Logs a brief status update about the queue content for frequent updates
        /// </summary>
        private void LogQueueContentStatusUpdate()
        {
            // Get basic stats
            int pendingCount = _collectionManager.Count;
            
            // Skip if there's nothing to report
            if (pendingCount == 0)
            {
                return;
            }
            
            // Get throttling status
            bool isThrottled = _stats.ShouldThrottleDownload();
            string throttleStr = isThrottled ? $" {LogEmojis.Wait} RATE LIMITED" : "";
            
            // Get processing status
            bool isProcessing = _naturalScheduler.ShouldProcessQueue(pendingCount);
            string processingStr = isProcessing ? $"{LogEmojis.Resume} ACTIVE" : $"{LogEmojis.Pause} PAUSED";
            
            // Get the next item (if we have any)
            var nextItem = _collectionManager.GetAllItems().OrderBy(i => i.QueuedTime).FirstOrDefault();
            string nextItemStr = nextItem != null 
                ? $"{LogEmojis.Next} Next: {nextItem.Artist} - {nextItem.Album}"
                : "";
                
            // Show rate information if available
            string rateInfo = "";
            if (_stats.DownloadRatePerHour > 0)
            {
                double estimatedHours = (double)pendingCount / _stats.DownloadRatePerHour;
                string estimatedTime = estimatedHours < 1
                    ? $"{(int)(estimatedHours * 60)}m"
                    : $"{(int)estimatedHours}h {(int)((estimatedHours - Math.Floor(estimatedHours)) * 60)}m";
                    
                rateInfo = $"{LogEmojis.Time} Rate: ~{_stats.DownloadRatePerHour}/hr, est. completion: {estimatedTime}";
            }
            
            // Log compact status update with enhanced format
            _logger.Info($"{LogEmojis.Status} ━━━━━━ QUEUE STATUS ━━━━━━");
            _logger.Info($"{LogEmojis.Queue}  •  Items: {pendingCount}  •  Status: {processingStr.Trim()}");

            if (!string.IsNullOrEmpty(nextItemStr))
            {
                string artist = nextItem.Artist;
                string album = nextItem.Album;
                _logger.Info($"{LogEmojis.Next}  •  Coming up  •  \"{album}\"  •  Artist: {artist}");
            }

            if (!string.IsNullOrEmpty(rateInfo))
            {
                // Extract the estimated time from rateInfo
                double estimatedHours = (double)pendingCount / _stats.DownloadRatePerHour;
                string estimatedTime = estimatedHours < 1
                    ? $"{(int)(estimatedHours * 60)}m"
                    : $"{(int)estimatedHours}h {(int)((estimatedHours - Math.Floor(estimatedHours)) * 60)}m";
                    
                _logger.Info($"{LogEmojis.Time}  •  Rate: ~{_stats.DownloadRatePerHour}/hr  •  Est. completion: {estimatedTime}");
            }

            // Add throttle details if throttled
            if (isThrottled)
            {
                var limitStatus = _stats.GetRateLimitStatusString().Replace($"{LogEmojis.Stop} ", "").Replace($"{LogEmojis.Warning} ", "");
                _logger.Info($"{LogEmojis.Wait}  •  Rate limit  •  {limitStatus}");
            }
            _logger.Info($"{LogEmojis.Status} ━━━━━━━━━━━━━━━━━━━━━━━━━");
        }

        /// <summary>
        /// Gets the next item from the queue to download.
        /// </summary>
        /// <param name="settings">The Tidal settings.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>The next item to download, or null if no more items are available.</returns>
        private Task<IDownloadItem> GetNextItemFromQueue(TidalSettings settings, CancellationToken cancellationToken)
        {
            try
            {
                // Get all items from the queue
                var items = _collectionManager.GetAllItems();
                _logger?.Debug($"[DIAGNOSTIC] CheckForNextDownload - Got {items.Length} items from queue");
                
                // First check if we have any items ready to download
                var queuedItems = items.Where(item => item.Status.Equals(NzbDrone.Core.Download.Clients.Tidal.Interfaces.DownloadItemStatus.Queued))
                    .OrderByDescending(item => item.Priority) // Order by priority highest first
                    .ThenBy(item => item.QueuedTime)  // Then by queue time (FIFO)
                    .ToArray();
                
                // Log priority distribution for debugging
                if (queuedItems.Length > 0)
                {
                    var priorityGroups = queuedItems.GroupBy(item => item.Priority)
                        .Select(g => $"{g.Key}: {g.Count()}")
                        .ToArray();
                        
                    _logger?.Debug($"[DIAGNOSTIC] Priority distribution: {string.Join(", ", priorityGroups)}");
                }

                if (queuedItems.Length == 0)
                {
                    _logger?.Debug("[DIAGNOSTIC] No queued items available");
                    return Task.FromResult<IDownloadItem>(null);
                }

                // Apply natural behavior limits if enabled
                if (settings.EnableNaturalBehavior)
                {
                    // Natural behavior settings may prevent downloads even if items are available
                    bool allowDownload = _naturalScheduler.ShouldProcessQueue(queuedItems.Length);
                    if (!allowDownload)
                    {
                        _logger?.Debug("[DIAGNOSTIC] Natural behavior preventing download at this time");
                        return Task.FromResult<IDownloadItem>(null);
                    }
                }

                // Get the next item from the queue based on priority
                var nextItem = queuedItems.FirstOrDefault();
                
                if (nextItem != null)
                {
                    // Log priority information
                    string priorityText = nextItem.Priority.ToString();
                    _logger?.Debug($"[DIAGNOSTIC] Next download: {nextItem.Title} by {nextItem.Artist} (Priority: {priorityText})");
                }
                else
                {
                    _logger?.Debug("[DIAGNOSTIC] No item found after filtering");
                }

                return Task.FromResult(nextItem);
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "[DIAGNOSTIC] Error getting next item from queue");
                return Task.FromResult<IDownloadItem>(null);
            }
        }

        /// <summary>
        /// Sets the priority of a queued download item.
        /// </summary>
        /// <param name="itemId">The ID of the download item.</param>
        /// <param name="priority">The new priority to set.</param>
        /// <returns>True if the priority was successfully set, false otherwise.</returns>
        public bool SetItemPriority(string itemId, NzbDrone.Core.Download.Clients.Tidal.Interfaces.DownloadItemPriority priority)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                _logger?.Warn($"{LogEmojis.Warning} Cannot set priority: Invalid item ID");
                return false;
            }

            try
            {
                // Get all items from the queue
                var items = _collectionManager.GetAllItems();
                
                // Find the item with the specified ID
                var item = items.FirstOrDefault(i => i.Id == itemId);
                
                if (item == null)
                {
                    _logger?.Warn($"{LogEmojis.Warning} Cannot set priority: Item with ID {itemId} not found in queue");
                    return false;
                }
                
                // Only allow changing priority for items that are queued
                if (!item.Status.Equals(NzbDrone.Core.Download.Clients.Tidal.Interfaces.DownloadItemStatus.Queued))
                {
                    _logger?.Warn($"{LogEmojis.Warning} Cannot change priority for item '{item.Title}' because it is not in queued state");
                    return false;
                }
                
                // Set the new priority
                item.Priority = priority;
                
                _logger?.Info($"{LogEmojis.Info} Set priority of '{item.Title}' by {item.Artist} to {priority}");
                
                // Save the queue to disk after changing priority
                if (_settings.EnableQueuePersistence)
                {
                    SaveQueueToDisk();
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"{LogEmojis.Error} Error setting priority for item {itemId}: {ex.Message}");
                return false;
            }
        }
    }
}
