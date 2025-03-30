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

    public class DownloadTaskQueue
    {
        private readonly Channel<IDownloadItem> _queue;
        private readonly List<IDownloadItem> _items;
        private readonly Dictionary<IDownloadItem, CancellationTokenSource> _cancellationSources;

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

        public DownloadTaskQueue(int capacity, TidalSettings settings, Logger logger)
        {
            BoundedChannelOptions options = new(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait
            };
            _queue = Channel.CreateBounded<IDownloadItem>(options);
            _items = new();
            _cancellationSources = new();
            _settings = settings;
            _logger = logger;
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
                    _items.Count > 0)
                {
                    SaveQueueToDisk();
                }
                
                // If queue persistence path changed, update saved queue location
                bool persistencePathChanged = oldSettings == null || 
                    oldSettings.ActualQueuePersistencePath != settings.ActualQueuePersistencePath;
                    
                if (settings.EnableQueuePersistence && persistencePathChanged && _items.Count > 0)
                {
                    SaveQueueToDisk();
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
                    // Save queue to disk before stopping if persistence is enabled
                    if (_settings.EnableQueuePersistence && _items.Count > 0)
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
            _logger.Debug("Starting queue handler task");
            
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
                            _logger.Warn($"Too many consecutive errors ({consecutiveErrors}), pausing queue processing for 1 minute to stabilize");
                            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                            consecutiveErrors = 0;
                            continue;
                        }
                        
                        // Check for prolonged error state - if no successful processing in 10 minutes, reset handler
                        if (consecutiveErrors > 0 && (DateTime.UtcNow - lastSuccessfulProcessing).TotalMinutes > 10)
                        {
                            _logger.Error($"No successful processing for over 10 minutes with {consecutiveErrors} consecutive errors. Resetting queue handler...");
                            
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
                                _logger.Error(ex, "Error resetting queue handler during error recovery");
                                await Task.Delay(5000, stoppingToken);
                                continue;
                            }
                        }
                        
                        // Adapt behavior based on queue volume
                        _behaviorSimulator.AdaptToQueueVolume(_settings, _items.Count);
                        
                        // Calculate download rate
                        UpdateDownloadRate();
                        
                        // Log statistics periodically, but only when there are items in the queue
                        if (_items.Count > 0)
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
                                _logger.Info("🎲 Random queue status check:");
                                LogQueueStatistics();
                                lastRandomStatsTime = DateTime.UtcNow;
                            }
                        }
                        
                        // First check if we should be processing the queue based on natural behavior settings
                        int queueSize = _items.Count;
                        if (!_naturalScheduler.ShouldProcessQueue(queueSize))
                        {
                            // Wait a bit before checking again
                            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                            continue;
                        }
                        
                        // Don't process if throttling is active
                        if (ShouldThrottleDownload())
                        {
                            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                            continue;
                        }
                        
                        // Get the next item from the queue
                        IDownloadItem item = null;
                        
                        try
                        {
                            // Try to use natural scheduler to get next item with context-aware selection
                            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeoutCts.Token);
                            
                            try 
                            {
                                item = await _naturalScheduler.GetNextItem(linkedCts.Token);
                            }
                            catch (OperationCanceledException)
                            {
                                if (stoppingToken.IsCancellationRequested)
                                {
                                    break;
                                }
                                
                                _logger.Debug("Timeout getting next item from scheduler, will retry");
                                await Task.Delay(1000, stoppingToken);
                                continue;
                            }
                            
                            // If we didn't get an item from the scheduler, check if there's one in the queue
                            if (item == null && _queue.Reader.TryRead(out var directItem))
                            {
                                item = directItem;
                                _logger.Debug($"Got item directly from queue: {item.Title} by {item.Artist}");
                            }
                        }
                        catch (ChannelClosedException)
                        {
                            _logger.Debug("Queue channel was closed");
                            break;
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.Debug("Queue processing was canceled");
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Error getting next item");
                            consecutiveErrors++;
                            await Task.Delay(3000, stoppingToken);
                            continue;
                        }
                        
                        // If we couldn't get an item, wait a bit before trying again
                        if (item == null)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                            continue;
                        }
                        
                        try
                        {
                            // Process the item
                            await ProcessItemAsync(item, stoppingToken);
                            
                            // Reset error counters on success
                            consecutiveErrors = 0;
                            lastSuccessfulProcessing = DateTime.UtcNow;
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, $"Error processing item: {ex.Message}");
                            consecutiveErrors++;
                            
                            // Make sure item is removed from queue even on failure
                            try
                            {
                                RemoveItem(item);
                            }
                            catch (Exception removeEx)
                            {
                                _logger.Debug(removeEx, "Error removing item after processing failure");
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
                        
                        _logger.Warn("Background task processing timed out, resetting...");
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
                        
                        _logger.Warn("Background operation cancelled, resetting...");
                        consecutiveErrors++;
                        // Wait a bit before trying again
                        await Task.Delay(5000, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Unhandled exception in download queue processor");
                        consecutiveErrors++;
                        // Wait a bit before trying again
                        await Task.Delay(5000, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
                _logger.Info("Download queue processor shutdown");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unhandled exception in download queue processor main loop");
            }
            finally
            {
                _logger.Info("Download queue background processing exited");
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
            if (_items.Count > 0)
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
            // Remove downloads older than 1 hour
            var cutoff = DateTime.UtcNow.AddHours(-1);
            _recentDownloads.RemoveAll(d => d < cutoff);
            
            // Calculate current download rate per hour
            _downloadRatePerHour = _recentDownloads.Count;
            
            // Check if we're currently rate limited
            bool wasRateLimited = _isRateLimited;
            _isRateLimited = (_settings.MaxDownloadsPerHour > 0 && _downloadRatePerHour >= _settings.MaxDownloadsPerHour);
            
            // Log when approaching or exceeding the rate limit
            if (_settings.MaxDownloadsPerHour > 0)
            {
                int percentOfLimit = (int)((_downloadRatePerHour / (float)_settings.MaxDownloadsPerHour) * 100);
                
                if (percentOfLimit >= 100)
                {
                    // If we just became rate limited or it's time for a periodic update
                    if (!wasRateLimited || (DateTime.UtcNow - _lastRateLimitLogTime) >= _rateLimitLogInterval)
                    {
                        var oldestDownload = _recentDownloads.OrderBy(d => d).FirstOrDefault();
                        string timeUntilUnlimited = "unknown";
                        
                        if (oldestDownload != default)
                        {
                            var timeUntilUnderLimit = oldestDownload.AddHours(1) - DateTime.UtcNow;
                            timeUntilUnlimited = timeUntilUnderLimit.TotalMinutes > 60 
                                ? $"{(int)timeUntilUnderLimit.TotalHours}h {timeUntilUnderLimit.Minutes}m" 
                                : $"{timeUntilUnderLimit.Minutes}m {timeUntilUnderLimit.Seconds}s";
                        }
                        
                        _logger.Warn($"🛑 RATE LIMIT REACHED: {_downloadRatePerHour}/{_settings.MaxDownloadsPerHour} downloads per hour ({percentOfLimit}% of limit)");
                        _logger.Info($"   ⏳ Downloads will be throttled until {cutoff.AddHours(1):HH:mm:ss} (in {timeUntilUnlimited})");
                        _lastRateLimitLogTime = DateTime.UtcNow;
                        
                        // Start a background task to periodically log rate limit status if we just became rate limited
                        if (!wasRateLimited)
                        {
                            Task.Run(async () => 
                            {
                                try
                                {
                                    while (ShouldThrottleDownload())
                                    {
                                        await Task.Delay(_rateLimitLogInterval);
                                        
                                        // Update rate again to ensure accurate logging
                                        lock (_lock)
                                        {
                                            // Only log if still throttled
                                            if (ShouldThrottleDownload() && 
                                                (DateTime.UtcNow - _lastRateLimitLogTime) >= _rateLimitLogInterval)
                                            {
                                                var oldestDownload = _recentDownloads.OrderBy(d => d).FirstOrDefault();
                                                if (oldestDownload != default)
                                                {
                                                    var timeUntilUnderLimit = oldestDownload.AddHours(1) - DateTime.UtcNow;
                                                    var timeUntilUnlimited = timeUntilUnderLimit.TotalMinutes > 60 
                                                        ? $"{(int)timeUntilUnderLimit.TotalHours}h {timeUntilUnderLimit.Minutes}m" 
                                                        : $"{timeUntilUnderLimit.Minutes}m {timeUntilUnderLimit.Seconds}s";
                                                    
                                                    _logger.Info($"⏱️ RATE LIMIT UPDATE at {DateTime.Now:HH:mm:ss}: {_downloadRatePerHour}/{_settings.MaxDownloadsPerHour} downloads per hour");
                                                    _logger.Info($"   ⏳ Throttling continues for {timeUntilUnlimited} (until {oldestDownload.AddHours(1).ToLocalTime():HH:mm:ss})");
                                                    _lastRateLimitLogTime = DateTime.UtcNow;
                                                }
                                            }
                                        }
                                    }
                                    
                                    // Log when throttling ends
                                    _logger.Info($"✅ RATE LIMIT LIFTED at {DateTime.Now:HH:mm:ss}: {_downloadRatePerHour}/{_settings.MaxDownloadsPerHour} downloads per hour - resuming normal operations");
                                }
                                catch (Exception ex)
                                {
                                    _logger.Debug(ex, "Error in rate limit status update task");
                                }
                            });
                        }
                    }
                }
                else if (percentOfLimit >= 80)
                {
                    _logger.Warn($"⚠️ Approaching rate limit: {_downloadRatePerHour}/{_settings.MaxDownloadsPerHour} downloads per hour ({percentOfLimit}% of limit)");
                }
            }
            
            // Update status manager with current statistics
            UpdateDownloadStatusManager();
        }
        
        private void UpdateDownloadStatusManager()
        {
            var artistStats = GetArtistStatistics();
            
            // Update general queue statistics
            _statusManager.UpdateQueueStatistics(
                _items.Count, 
                _totalItemsProcessed, 
                _failedDownloads,
                ((UserBehaviorSimulator)_behaviorSimulator).SessionsCompleted,
                ((UserBehaviorSimulator)_behaviorSimulator).IsHighVolumeMode,
                _downloadRatePerHour
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
        
        /// <summary>
        /// Logs current queue statistics and status information
        /// </summary>
        private void LogQueueStatistics()
        {
            // Skip if there's nothing in the queue
            if (_items.Count == 0)
            {
                _logger.Info("📊 Queue Status: Empty");
                return;
            }
            
            // Get basic stats
            int pendingCount = _items.Count;
            int processedCount = _totalItemsProcessed;
            int failedCount = _failedDownloads;
            bool isHighVolume = ((UserBehaviorSimulator)_behaviorSimulator).IsHighVolumeMode;
            
            // Calculate estimated time if throttling
            string throttleStatus = string.Empty;
            if (ShouldThrottleDownload() && _settings.MaxDownloadsPerHour > 0)
            {
                var oldestDownload = _recentDownloads.OrderBy(d => d).FirstOrDefault();
                if (oldestDownload != default)
                {
                    var timeUntilUnderLimit = oldestDownload.AddHours(1) - DateTime.UtcNow;
                    throttleStatus = $" ⏳ Rate limited: {_downloadRatePerHour}/{_settings.MaxDownloadsPerHour}/hr, resume in " + 
                        (timeUntilUnderLimit.TotalMinutes > 60 
                            ? $"{(int)timeUntilUnderLimit.TotalHours}h {timeUntilUnderLimit.Minutes}m" 
                            : $"{timeUntilUnderLimit.Minutes}m {timeUntilUnderLimit.Seconds}s");
                }
                else
                {
                    throttleStatus = $" ⏳ Rate limited: {_downloadRatePerHour}/{_settings.MaxDownloadsPerHour}/hr";
                }
            }
            
            // Get info about processing times
            string processingStatus = _naturalScheduler.ShouldProcessQueue(pendingCount) 
                ? "▶️ Active" 
                : "⏸️ Paused (outside active hours)";
                
            // Get unique artists and albums
            var artistCount = _items.Select(i => i.Artist).Distinct().Count();
            var albumCount = _items.Select(i => i.Album).Distinct().Count();
            
            // Calculate average processing rate (per hour)
            int averageRate = _downloadRatePerHour;
            
            // Log main queue stats
            _logger.Info($"📊 Queue Status at {DateTime.Now:HH:mm:ss}: {processingStatus}{throttleStatus}");
            _logger.Info($"   📈 Items: {pendingCount} pending, {processedCount} completed, {failedCount} failed");
            _logger.Info($"   👤 Content: {artistCount} artists, {albumCount} albums, mode: {(isHighVolume ? "high volume" : "normal")}");
            
            if (pendingCount > 0 && averageRate > 0)
            {
                // Estimate completion time (very rough)
                double estimatedHours = (double)pendingCount / averageRate;
                string estimatedTime = estimatedHours < 1 
                    ? $"{(int)(estimatedHours * 60)}m" 
                    : $"{(int)estimatedHours}h {(int)((estimatedHours - Math.Floor(estimatedHours)) * 60)}m";
                
                _logger.Info($"   ⏱️ Processing: Current rate ~{averageRate}/hr, est. completion in {estimatedTime}");
            }
        }
        
        private Dictionary<string, Tuple<int, int, int, List<string>>> GetArtistStatistics()
        {
            var result = new Dictionary<string, Tuple<int, int, int, List<string>>>();
            
            // Group by artist
            var byArtist = _items.GroupBy(i => i.Artist).ToDictionary(g => g.Key, g => g.ToList());
            
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
                double artistProportion = (double)pending / Math.Max(1, _items.Count);
                int estimated_completed = (int)(artistProportion * _totalItemsProcessed);
                int estimated_failed = (int)(artistProportion * _failedDownloads);
                
                result[artist.Key] = new Tuple<int, int, int, List<string>>(
                    pending, estimated_completed, estimated_failed, albums
                );
            }
            
            return result;
        }

        public async ValueTask QueueBackgroundWorkItemAsync(IDownloadItem workItem, CancellationToken cancellationToken = default)
        {
            const int maxRetryAttempts = 3;
            int attempt = 0;
            TimeSpan retryDelay = TimeSpan.FromMilliseconds(500);
            bool addedToQueue = false;
            
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
                    // Use TryWrite instead of WriteAsync when available for better timeout control
                    bool immediateSuccess = _queue.Writer.TryWrite(workItem);
                    
                    if (immediateSuccess)
                    {
                        _logger.Debug($"Immediately added {workItem.Title} by {workItem.Artist} to queue");
                        addedToQueue = true;
                    }
                    else
                    {
                        // If synchronous attempt failed, try asynchronous with timeout
                        _logger.Debug($"Attempting async queue add for {workItem.Title} by {workItem.Artist}, attempt {attempt}/{maxRetryAttempts}");
                        
                        // Use a task with timeout to avoid potential hanging
                        var writeTask = _queue.Writer.WriteAsync(workItem, linkedCts.Token).AsTask();
                        
                        // Wait for completion with timeout
                        if (await Task.WhenAny(writeTask, Task.Delay(timeoutCts.Token.WaitHandle.WaitOne(0) ? 0 : (int)_queueOperationTimeout.TotalMilliseconds, linkedCts.Token)) == writeTask)
                        {
                            await writeTask; // Propagate any exceptions
                            addedToQueue = true;
                        }
                        else if (timeoutCts.IsCancellationRequested)
                        {
                            throw new OperationCanceledException("Timed out while waiting for queue space", linkedCts.Token);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    if (timeoutCts.IsCancellationRequested)
                    {
                        _logger.Warn($"Timed out adding item to queue on attempt {attempt}/{maxRetryAttempts}: {workItem.Title} by {workItem.Artist}");
                        
                        // Only retry if this is not the last attempt
                        if (attempt < maxRetryAttempts)
                        {
                            _logger.Info($"Retrying queue add after a delay (attempt {attempt}/{maxRetryAttempts})");
                            
                            try
                            {
                                // Wait before retrying
                                await Task.Delay(retryDelay, cancellationToken);
                                // Increase delay for next retry (exponential backoff)
                                retryDelay = TimeSpan.FromMilliseconds(retryDelay.TotalMilliseconds * 2);
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
                lock (_lock)
                {
                    _items.Add(workItem);
                    _cancellationSources[workItem] = new CancellationTokenSource();
                    _totalItemsQueued++;
                    
                    // Log when we hit large queue sizes
                    if (_items.Count % 1000 == 0)
                    {
                        _logger.Info($"📈 Queue milestone reached: {_items.Count} items");
                        _behaviorSimulator.LogSessionStats();
                    }
                    else if (_items.Count == 1)
                    {
                        _logger.Info($"▶️ Download queue started with first item: {workItem.Title} by {workItem.Artist}");
                    }
                    else if (_items.Count % 100 == 0)
                    {
                        _logger.Debug($"📊 Queue size: {_items.Count} items");
                    }
                }
            }
            else if (!cancellationToken.IsCancellationRequested)
            {
                // All attempts failed but not due to cancellation
                _logger.Error($"Failed to add {workItem.Title} by {workItem.Artist} to queue after {maxRetryAttempts} attempts");
                throw new InvalidOperationException($"Failed to add item to queue after {maxRetryAttempts} attempts");
            }
        }

        public void RemoveItem(IDownloadItem item)
        {
            lock (_lock)
            {
                _items.Remove(item);
                if (_cancellationSources.TryGetValue(item, out var cts))
                {
                    try
                    {
                        cts.Cancel();
                        cts.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Error cancelling download task");
                    }
                    _cancellationSources.Remove(item);
                }

                if (_items.Count == 0)
                {
                    _logger.Info("✅ Download queue is now empty");

                    // If the queue is empty, remove any saved queue file
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
                else if (_settings.EnableQueuePersistence && _items.Count % 5 == 0)
                {
                    // Periodically save the queue if it changes significantly (every 5 items removed)
                    SaveQueueToDisk();
                }
            }
        }

        public IDownloadItem[] GetQueueListing()
        {
            lock (_lock)
            {
                return _items.ToArray();
            }
        }

        public int GetQueueSize()
        {
            lock (_lock)
            {
                return _items.Count;
            }
        }

        private CancellationToken GetTokenForItem(IDownloadItem item, CancellationToken linkedToken = default)
        {
            lock (_lock)
            {
                if (_cancellationSources.TryGetValue(item, out var source))
                {
                    if (linkedToken == default)
                    {
                        return source.Token;
                    }
                    
                    // Create a linked token source combining the item's token and the provided token
                    var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(source.Token, linkedToken);
                    return linkedSource.Token;
                }
                
                return linkedToken == default ? CancellationToken.None : linkedToken;
            }
        }
        
        // Add this method to allow the NaturalDownloadScheduler to check the rate
        public int GetCurrentHourlyDownloadRate()
        {
            lock (_lock)
            {
                // Force an update of the rate calculation
                UpdateDownloadRate();
                return _downloadRatePerHour;
            }
        }

        // Add this method to allow the NaturalDownloadScheduler to check the rate
        internal bool IsRateLimited() => _isRateLimited;

        /// <summary>
        /// Determines if downloads should be throttled based on current rate limits and settings
        /// </summary>
        private bool ShouldThrottleDownload()
        {
            // If max downloads per hour is not set (0), don't throttle
            if (_settings.MaxDownloadsPerHour <= 0)
            {
                return false;
            }

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

            return false;
        }

        // Save the current queue to disk
        private void SaveQueueToDisk()
        {
            // Skip saving if queue persistence is disabled
            if (!_settings.EnableQueuePersistence)
            {
                return;
            }
            
            // Get the persistence path from settings
            string persistencePath = _settings.ActualQueuePersistencePath;
            
            if (string.IsNullOrWhiteSpace(persistencePath))
            {
                _logger.Warn("Queue persistence is enabled but persistence path could not be determined. Queue will not be saved.");
                return;
            }
            
            try
            {
                var queueFilePath = Path.Combine(persistencePath, "queue_data.json");
                _logger.Info($"Saving download queue to {queueFilePath} ({_items.Count} items)");
                
                // Ensure directory exists
                Directory.CreateDirectory(persistencePath);
                
                // Create serializable records from queue items
                var records = new List<QueueItemRecord>();
                foreach (var item in _items)
                {
                    var remoteAlbumJson = "";
                    // If this is a DownloadItem, try to serialize the associated RemoteAlbum
                    if (item is DownloadItem downloadItem && downloadItem.RemoteAlbum != null)
                    {
                        try
                        {
                            // This is a simplification - full RemoteAlbum serialization would be complex
                            // Just capture the essential details instead
                            var albumInfo = new
                            {
                                Title = downloadItem.RemoteAlbum.Release?.Title,
                                Artist = downloadItem.RemoteAlbum.Artist?.Name,
                                ReleaseDate = downloadItem.RemoteAlbum.Release?.PublishDate
                            };
                            remoteAlbumJson = JsonSerializer.Serialize(albumInfo);
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug(ex, "Error serializing remote album data, will save minimal information");
                        }
                    }
                    
                    records.Add(new QueueItemRecord
                    {
                        ID = item.ID,
                        Title = item.Title,
                        Artist = item.Artist,
                        Album = item.Album,
                        Bitrate = item.Bitrate.ToString(),
                        TotalSize = item.TotalSize,
                        DownloadFolder = item.DownloadFolder,
                        Explicit = item.Explicit,
                        RemoteAlbumJson = remoteAlbumJson,
                        QueuedTime = DateTime.UtcNow
                    });
                }
                
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(records, options);
                File.WriteAllText(queueFilePath, json);
                
                _logger.Info($"Successfully saved download queue with {records.Count} items");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error saving download queue to disk");
            }
        }
        
        // Restore the queue from disk
        private void RestoreQueueFromDisk()
        {
            // Skip restoration if queue persistence is disabled
            if (!_settings.EnableQueuePersistence)
            {
                return;
            }
            
            // Get the persistence path from settings
            string persistencePath = _settings.ActualQueuePersistencePath;
            
            if (string.IsNullOrWhiteSpace(persistencePath))
            {
                _logger.Debug("Queue persistence is enabled but persistence path could not be determined. Starting with empty queue.");
                return;
            }
            
            var queueFilePath = Path.Combine(persistencePath, "queue_data.json");
            if (!File.Exists(queueFilePath))
            {
                _logger.Debug("No saved queue file found, starting with empty queue");
                return;
            }
            
            try
            {
                _logger.Info($"Restoring download queue from {queueFilePath}");
                
                var json = File.ReadAllText(queueFilePath);
                var records = JsonSerializer.Deserialize<List<QueueItemRecord>>(json);
                
                if (records == null || records.Count == 0)
                {
                    _logger.Info("No items found in saved queue file");
                    return;
                }
                
                _logger.Info($"Found {records.Count} items in saved queue file");
                
                // Create placeholder items from the records
                int restoredCount = 0;
                foreach (var record in records)
                {
                    try
                    {
                        // Create a placeholder item that implements IDownloadItem interface
                        var downloadItem = new PlaceholderDownloadItem(
                            record.ID,
                            record.Title,
                            record.Artist,
                            record.Album,
                            ParseAudioQuality(record.Bitrate),
                            record.TotalSize,
                            record.DownloadFolder,
                            record.Explicit
                        );
                        
                        // Add to internal items list
                        lock (_lock)
                        {
                            _items.Add(downloadItem);
                            _cancellationSources[downloadItem] = new CancellationTokenSource();
                            restoredCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Error restoring queue item: {record.Title} by {record.Artist}");
                    }
                }
                
                _logger.Info($"Successfully restored {restoredCount} of {records.Count} items to download queue");
                
                if (restoredCount > 0)
                {
                    _logger.Warn("⚠️ Note: Restored items are placeholders and will need to be re-queued for download if processing fails");
                }
                
                // Delete the queue file after successful restore to prevent duplication
                try
                {
                    File.Delete(queueFilePath);
                    _logger.Debug("Deleted saved queue file after restoration");
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to delete saved queue file after restoration");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error restoring download queue from disk");
            }
        }
        
        // Simple placeholder implementation of IDownloadItem for restored queue items
        private class PlaceholderDownloadItem : IDownloadItem
        {
            public string ID { get; }
            public string Title { get; }
            public string Artist { get; }
            public string Album { get; }
            public bool Explicit { get; }
            public RemoteAlbum RemoteAlbum { get; } = null;
            public string DownloadFolder { get; }
            public AudioQuality Bitrate { get; }
            public DownloadItemStatus Status { get; set; } = DownloadItemStatus.Queued;
            public float Progress => 0;
            public long DownloadedSize => 0;
            public long TotalSize { get; }
            public int FailedTracks => 0;
            
            public PlaceholderDownloadItem(
                string id, string title, string artist, string album, 
                AudioQuality bitrate, long totalSize, string downloadFolder, bool isExplicit)
            {
                ID = id;
                Title = title;
                Artist = artist;
                Album = album;
                Bitrate = bitrate;
                TotalSize = totalSize;
                DownloadFolder = downloadFolder;
                Explicit = isExplicit;
            }
            
            public async Task DoDownload(TidalSettings settings, Logger logger, CancellationToken cancellation = default)
            {
                logger.Warn($"⚠️ Attempting to download a placeholder item: {Title} by {Artist}");
                // Cannot directly download a placeholder item - mark as failed
                Status = DownloadItemStatus.Failed;
                logger.Error($"❌ Download failed - this is a placeholder item restored from a previous session");
                logger.Error($"   Please re-search for this album to queue a proper download");
                
                // Add a small delay to avoid warning about no await operators
                await Task.Delay(100, cancellation);
                
                throw new NotImplementedException("Cannot download placeholder item - please re-queue this album");
            }
        }
        
        private TidalSharp.Data.AudioQuality ParseAudioQuality(string bitrateString)
        {
            if (Enum.TryParse<TidalSharp.Data.AudioQuality>(bitrateString, out var result))
            {
                return result;
            }
            
            // Default to HIGH if parsing fails
            return TidalSharp.Data.AudioQuality.HIGH;
        }

        /// <summary>
        /// Processes a single download item
        /// </summary>
        private async Task ProcessItemAsync(IDownloadItem item, CancellationToken stoppingToken)
        {
            try
            {
                _logger.Info($"Starting download for {item.Title} by {item.Artist}");
                
                // Create a token for this specific item
                var token = GetTokenForItem(item, stoppingToken);
                
                // Execute the download
                await item.DoDownload(_settings, _logger, token);
                
                _logger.Info($"Completed download for {item.Title} by {item.Artist}");
                _totalItemsProcessed++;
                
                // Track each track download individually for rate limiting
                // Get the number of tracks in the album - if we can't determine it, assume at least 1
                int trackCount = 1;
                try 
                {
                    // Try to determine the number of tracks based on the item 
                    trackCount = Math.Max(1, item.FailedTracks == 0 ? 
                        10 : item.FailedTracks); // Assume an album with 10 tracks if all succeeded
                    
                    _logger.Debug($"Tracking {trackCount} track downloads for rate limiting");
                } 
                catch 
                {
                    // Fallback to single track if we can't determine
                    trackCount = 1;
                }

                // Add each track as a separate download for rate limiting
                lock (_lock) 
                {
                    for (int i = 0; i < trackCount; i++) 
                    {
                        _recentDownloads.Add(DateTime.UtcNow);
                    }
                    
                    // Update the download rate immediately
                    UpdateDownloadRate();
                }
                
                // Calculate quality level based on bitrate
                string quality = item.Bitrate.ToString();
                string format = quality switch
                {
                    "LOW" => "AAC 96kbps",
                    "HIGH" => "AAC 320kbps",
                    "LOSSLESS" => "FLAC Lossless",
                    "HI_RES_LOSSLESS" => "FLAC 24bit Lossless",
                    _ => "Unknown"
                };
                
                // Add the completed track with size information
                _statusManager.AddCompletedTrackWithDetails(
                    Guid.NewGuid().ToString(),
                    item.Title,
                    item.Artist,
                    item.Album,
                    quality,
                    format,
                    item.TotalSize,
                    item.DownloadFolder,
                    false, // We don't know if it has lyrics
                    item.Explicit,
                    0 // We don't know the track number
                );
                
                // Log queue statistics after album download completes if there are still items in the queue
                if (_items.Count > 0)
                {
                    LogQueueStatistics();
                }
            }
            catch (TaskCanceledException)
            {
                _logger.Info($"Download cancelled for {item.Title} by {item.Artist}");
            }
            catch (OperationCanceledException)
            {
                _logger.Info($"Download operation cancelled for {item.Title} by {item.Artist}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error downloading {item.Title} by {item.Artist}");
                _failedDownloads++;
                
                // Track failed download with any size information that might be available
                string quality = item.Bitrate.ToString();
                string format = quality switch
                {
                    "LOW" => "AAC 96kbps",
                    "HIGH" => "AAC 320kbps",
                    "LOSSLESS" => "FLAC Lossless",
                    "HI_RES_LOSSLESS" => "FLAC 24bit Lossless",
                    _ => "Unknown"
                };
                
                // Add the failed track with any size info we might have
                _statusManager.AddFailedTrackWithDetails(
                    Guid.NewGuid().ToString(),
                    item.Title,
                    item.Artist,
                    item.Album,
                    quality,
                    format,
                    item.TotalSize,
                    item.DownloadFolder, 
                    false,
                    item.Explicit,
                    0
                );
            }
            finally
            {
                // Remove the item from the queue
                RemoveItem(item);
            }
        }
    }
}
