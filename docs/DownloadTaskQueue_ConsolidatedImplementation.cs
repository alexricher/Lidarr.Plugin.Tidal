using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Download.Clients.Tidal.Queue;
using NzbDrone.Core.Download.Clients.Tidal.Viewer;
using NzbDrone.Core.Download.Clients.Tidal.Interfaces;
using System.Text;
using Lidarr.Plugin.Tidal.Services.Logging;
using NzbDrone.Plugin.Tidal;

namespace NzbDrone.Core.Download.Clients.Tidal.Queue
{
    // Serializable class for storing queue items
    public class QueueItemRecord
    {
        public string ID { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public string Bitrate { get; set; }
        public long TotalSize { get; set; }
        public string DownloadFolder { get; set; }
        public bool Explicit { get; set; }
        public string RemoteAlbumJson { get; set; }
        public DateTime QueuedTime { get; set; }
    }

    /// <summary>
    /// Manages a thread-safe queue of download tasks for Tidal content.
    /// Provides functionality for adding, processing, and tracking download tasks.
    /// </summary>
    public class DownloadTaskQueue : IDisposable
    {
        #region Thread-Safe Collection Manager

        /// <summary>
        /// Encapsulates thread-safe operations on the download item collections.
        /// </summary>
        private class ItemCollectionManager : IDisposable
        {
            private readonly object _lock = new object();
            private readonly List<IDownloadItem> _items = new List<IDownloadItem>();
            private readonly Dictionary<IDownloadItem, CancellationTokenSource> _cancellationSources = 
                new Dictionary<IDownloadItem, CancellationTokenSource>();
            private readonly Logger _logger;

            public ItemCollectionManager(Logger logger)
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
                        try
                        {
                            cts.Cancel();
                            cts.Dispose();
                        }
                        catch (Exception ex)
                        {
                            _logger?.Debug(ex, "Error cancelling/disposing cancellation token source");
                        }
                        _cancellationSources.Remove(item);
                    }
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
            /// Gets an item at the specified index in a thread-safe manner.
            /// </summary>
            /// <param name="index">The index of the item to get.</param>
            /// <returns>The download item, or null if the index is out of range.</returns>
            public IDownloadItem GetItemAt(int index)
            {
                lock (_lock)
                {
                    if (index >= 0 && index < _items.Count)
                    {
                        return _items[index];
                    }
                    return null;
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
            /// Gets a cancellation token for the specified item in a thread-safe manner.
            /// </summary>
            /// <param name="item">The download item to get a token for.</param>
            /// <param name="linkedToken">An optional token to link with the item's token.</param>
            /// <returns>A cancellation token.</returns>
            public CancellationToken GetTokenForItem(IDownloadItem item, CancellationToken linkedToken = default)
            {
                if (item == null)
                {
                    return linkedToken == default ? CancellationToken.None : linkedToken;
                }

                lock (_lock)
                {
                    if (_cancellationSources.TryGetValue(item, out var source))
                    {
                        if (linkedToken == default)
                        {
                            return source.Token;
                        }
                        
                        var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token, linkedToken);
                        return linkedSource.Token;
                    }
                    
                    return linkedToken == default ? CancellationToken.None : linkedToken;
                }
            }

            /// <summary>
            /// Cancels all items in a thread-safe manner.
            /// </summary>
            public void CancelAll()
            {
                lock (_lock)
                {
                    foreach (var cts in _cancellationSources.Values)
                    {
                        try
                        {
                            cts.Cancel();
                        }
                        catch (Exception ex)
                        {
                            _logger?.Debug(ex, "Error cancelling token source during CancelAll");
                        }
                    }
                }
            }

            /// <summary>
            /// Disposes all cancellation tokens in a thread-safe manner.
            /// </summary>
            public void Dispose()
            {
                lock (_lock)
                {
                    foreach (var cts in _cancellationSources.Values)
                    {
                        try
                        {
                            cts.Dispose();
                        }
                        catch (Exception ex)
                        {
                            _logger?.Debug(ex, "Error disposing token source");
                        }
                    }
                    _cancellationSources.Clear();
                    _items.Clear();
                }
            }
        }

        #endregion

        private readonly Channel<IDownloadItem> _queue;
        private readonly ItemCollectionManager _collectionManager; // Collection manager
        private readonly List<Task> _runningTasks = new List<Task>();
        private readonly object _lock = new object();
        private CancellationTokenSource _processingCancellationSource;
        private Task _processingTask;
        private readonly TimeSpan _itemProcessTimeout = TimeSpan.FromMinutes(30);
        private readonly TimeSpan _queueOperationTimeout = TimeSpan.FromSeconds(60);

        private TidalSettings _settings;
        private readonly Logger _logger;
        private NaturalDownloadScheduler _naturalScheduler;
        private IUserBehaviorSimulator _behaviorSimulator;

        // Statistics tracking
        private int _totalItemsProcessed = 0;
        private int _totalItemsQueued = 0;
        private int _failedDownloads = 0;
        private DateTime _lastStatsLogTime = DateTime.UtcNow;
        private readonly TimeSpan _statsLogInterval = TimeSpan.FromMinutes(10);

        // Download status manager for the viewer
        private DownloadStatusManager _statusManager;
        private int _downloadRatePerHour = 0;
        private readonly List<DateTime> _recentDownloads = new List<DateTime>();
        private DateTime _lastRateLimitLogTime = DateTime.MinValue;
        private readonly TimeSpan _rateLimitLogInterval = TimeSpan.FromSeconds(30);
        private bool _isRateLimited = false;

        // Stall detection
        private DateTime _lastProcessedTime = DateTime.MinValue;
        private readonly TimeSpan _stallDetectionThreshold = TimeSpan.FromMinutes(15);
        private int _stallDetectionCount = 0;
        private readonly object _stallDetectionLock = new object();

        private StatisticsManager _stats;

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
            private bool _isRateLimited = false;
            private int _downloadRatePerHour = 0;

            /// <summary>
            /// Initializes a new instance of the StatisticsManager class.
            /// </summary>
            /// <param name="settings">The Tidal settings.</param>
            /// <param name="logger">The logger to use.</param>
            public StatisticsManager(TidalSettings settings, Logger logger)
            {
                _settings = settings;
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
                        if ((DateTime.UtcNow - _lastRateLimitLogTime) > _rateLimitLogInterval)
                        {
                            _logger.Debug($"Download throttling active: {_downloadRatePerHour}/{_settings.MaxDownloadsPerHour} downloads per hour limit reached");
                            _lastRateLimitLogTime = DateTime.UtcNow;
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

                    if (percentOfLimit >= 100 && (isNowRateLimited != wasRateLimited || 
                                                 (DateTime.UtcNow - _lastRateLimitLogTime) >= _rateLimitLogInterval))
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
                        
                        lock (_lock)
                        {
                            _lastRateLimitLogTime = DateTime.UtcNow;
                        }
                    }
                    else if (percentOfLimit >= 80 && percentOfLimit < 100 && 
                             (DateTime.UtcNow - _lastRateLimitLogTime) >= _rateLimitLogInterval)
                    {
                        _logger.Warn($"{LogEmojis.Warning} Approaching rate limit: {_downloadRatePerHour}/{_settings.MaxDownloadsPerHour} downloads per hour ({percentOfLimit}% of limit)");
                        
                        lock (_lock)
                        {
                            _lastRateLimitLogTime = DateTime.UtcNow;
                        }
                    }
                }

                return isNowRateLimited != wasRateLimited;
            }

            /// <summary>
            /// Gets the estimated time until the next download slot is available.
            /// </summary>
            /// <returns>The estimated time span, or null if unknown or not rate limited.</returns>
            public TimeSpan? GetEstimatedTimeUntilNextSlot()
            {
                if (!_isRateLimited || _settings.MaxDownloadsPerHour <= 0)
                {
                    return null;
                }

                lock (_lock)
                {
                    var oldestDownload = _recentDownloads.OrderBy(d => d).FirstOrDefault();
                    if (oldestDownload != default)
                    {
                        return oldestDownload.AddHours(1) - DateTime.UtcNow;
                    }
                }

                return null;
            }

            /// <summary>
            /// Gets whether downloads are currently rate limited.
            /// </summary>
            public bool IsRateLimited => _isRateLimited;
        }

        /// <summary>
        /// Initializes a new instance of the DownloadTaskQueue class.
        /// </summary>
        /// <param name="tidalApi">The Tidal API client to use for downloads.</param>
        /// <param name="settings">The Tidal settings.</param>
        /// <param name="logger">The logger to use.</param>
        public DownloadTaskQueue(TidalAPI tidalApi, TidalSettings settings, Logger logger)
        {
            BoundedChannelOptions options = new(1000)
            {
                FullMode = BoundedChannelFullMode.Wait
            };
            _queue = Channel.CreateBounded<IDownloadItem>(options);
            _logger = logger;
            _collectionManager = new ItemCollectionManager(logger); // Initialize collection manager
            _settings = settings;
            _stats = new StatisticsManager(settings, logger); // Properly initialize with settings
            _behaviorSimulator = new UserBehaviorSimulator(logger);
            _naturalScheduler = new NaturalDownloadScheduler(this, _behaviorSimulator, settings, logger);
            _processingCancellationSource = new CancellationTokenSource();

            // Initialize download status manager for the viewer
            _statusManager = new DownloadStatusManager(
                AppDomain.CurrentDomain.BaseDirectory,
                settings.StatusFilesPath,
                logger);

            try
            {
                _logger.Info($"Tidal Download Queue initialized - Plugin Version {typeof(NzbDrone.Core.Plugins.TidalPlugin).Assembly.GetName().Version}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error initializing DownloadTaskQueue");
            }

            // Restore queue if persistence is enabled
            if (settings.EnableQueuePersistence)
            {
                RestoreQueueFromDisk();
            }
        }

        /// <summary>
        /// Saves the current queue to disk.
        /// </summary>
        private void SaveQueueToDisk()
        {
            // Implementation goes here
        }

        /// <summary>
        /// Restores the queue from disk.
        /// </summary>
        private void RestoreQueueFromDisk()
        {
            // Implementation goes here
        }

        /// <summary>
        /// Processes a single download item.
        /// </summary>
        /// <param name="item">The item to process.</param>
        /// <param name="stoppingToken">A token to monitor for cancellation requests.</param>
        private async Task ProcessItemAsync(IDownloadItem item, CancellationToken stoppingToken)
        {
            // Implementation goes here
            await Task.CompletedTask;
        }

        /// <summary>
        /// Implements IDisposable.Dispose() to clean up resources
        /// </summary>
        public void Dispose()
        {
            try
            {
                // Stop the queue handler
                StopQueueHandler();

                // Dispose the collection manager
                _collectionManager?.Dispose();

                // Dispose any other resources
                _processingCancellationSource?.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error disposing DownloadTaskQueue");
            }
        }

        /// <summary>
        /// Starts the queue handler.
        /// </summary>
        public void StartQueueHandler()
        {
            // Implementation goes here
        }

        /// <summary>
        /// Stops the queue handler.
        /// </summary>
        public void StopQueueHandler()
        {
            // Implementation goes here
        }

        /// <summary>
        /// Gets the queue listing.
        /// </summary>
        /// <returns>An array of download items in the queue.</returns>
        public IDownloadItem[] GetQueueListing()
        {
            return _collectionManager.GetAllItems();
        }

        /// <summary>
        /// Gets the queue size.
        /// </summary>
        /// <returns>The number of items in the queue.</returns>
        public int GetQueueSize()
        {
            return _collectionManager.Count;
        }

        /// <summary>
        /// Removes an item from the queue.
        /// </summary>
        /// <param name="item">The item to remove.</param>
        public void RemoveItem(IDownloadItem item)
        {
            bool itemRemoved = _collectionManager.RemoveItem(item);

            if (itemRemoved)
            {
                // If the queue is empty, remove any saved queue file
                if (_collectionManager.Count == 0)
                {
                    _logger.Info($"{LogEmojis.Success} Download queue is now empty");

                    if (_settings.EnableQueuePersistence)
                    {
                        string persistencePath = _settings.ActualQueuePersistencePath;
                        if (!string.IsNullOrWhiteSpace(persistencePath))
                        {
                            try
                            {
                                var queueFilePath = Path.Combine(persistencePath, "queue_data.json");
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
    }
}
