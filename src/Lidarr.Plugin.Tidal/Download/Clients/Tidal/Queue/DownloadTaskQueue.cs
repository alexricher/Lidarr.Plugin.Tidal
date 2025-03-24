using System;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using NLog;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Plugins;
using NzbDrone.Core.Download.Clients.Tidal.Viewer;

namespace NzbDrone.Core.Download.Clients.Tidal.Queue
{
    public class DownloadTaskQueue
    {
        private readonly Channel<IDownloadItem> _queue;
        private readonly List<IDownloadItem> _items;
        private readonly Dictionary<IDownloadItem, CancellationTokenSource> _cancellationSources;

        private readonly List<Task> _runningTasks = new();
        private readonly object _lock = new();

        private TidalSettings _settings;
        private readonly Logger _logger;
        private NaturalDownloadScheduler _naturalScheduler;
        private IUserBehaviorSimulator _behaviorSimulator;
        
        // Statistics tracking
        private int _totalItemsProcessed = 0;
        private int _totalItemsQueued = 0;
        private int _failedDownloads = 0;
        private DateTime _lastStatsLogTime = DateTime.UtcNow;
        private readonly TimeSpan _statsLogInterval = TimeSpan.FromHours(1);
        
        // Download status manager for the viewer
        private readonly DownloadStatusManager _statusManager;
        private int _downloadRatePerHour = 0;
        private readonly List<DateTime> _recentDownloads = new List<DateTime>();

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
            
            // Initialize download status manager for the viewer
            _statusManager = new DownloadStatusManager(
                AppDomain.CurrentDomain.BaseDirectory, 
                settings.StatusFilesPath,
                logger);
            
            _logger.Info($"Tidal Download Queue initialized - Plugin Version {typeof(NzbDrone.Core.Plugins.TidalPlugin).Assembly.GetName().Version}");
        }

        public void SetSettings(TidalSettings settings)
        {
            _settings = settings;
            // Update the scheduler with new settings
            _naturalScheduler = new NaturalDownloadScheduler(this, _behaviorSimulator, settings, _logger);
        }

        public void StartQueueHandler()
        {
            Task.Run(() => BackgroundProcessing());
        }

        private async Task BackgroundProcessing(CancellationToken stoppingToken = default)
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    // Adapt behavior based on queue volume
                    _behaviorSimulator.AdaptToQueueVolume(_settings, _items.Count);
                    
                    // Calculate download rate
                    UpdateDownloadRate();
                    
                    // Log statistics periodically
                    if ((DateTime.UtcNow - _lastStatsLogTime) > _statsLogInterval)
                    {
                        LogQueueStatistics();
                        _lastStatsLogTime = DateTime.UtcNow;
                    }
                    
                    var nextItem = await _naturalScheduler.GetNextItem(stoppingToken);
                    if (nextItem == null)
                    {
                        // No items in queue, wait a bit before checking again
                        await Task.Delay(1000, stoppingToken);
                        continue;
                    }

                    var token = GetTokenForItem(nextItem);
                    try
                    {
                        _logger.Info($"Starting download for {nextItem.Title} by {nextItem.Artist}");
                        await nextItem.DoDownload(_settings, _logger, token);
                        _logger.Info($"Completed download for {nextItem.Title} by {nextItem.Artist}");
                        _totalItemsProcessed++;
                        
                        // Track successful download
                        _recentDownloads.Add(DateTime.UtcNow);
                        _statusManager.AddCompletedTrack(nextItem.Title, nextItem.Artist, nextItem.Album);
                    }
                    catch (TaskCanceledException)
                    {
                        _logger.Info($"Download cancelled for {nextItem.Title} by {nextItem.Artist}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Error downloading {nextItem.Title} by {nextItem.Artist}");
                        _failedDownloads++;
                        
                        // Track failed download
                        _statusManager.AddFailedTrack(nextItem.Title, nextItem.Artist, nextItem.Album);
                    }
                    finally
                    {
                        // Remove the item from the queue
                        RemoveItem(nextItem);
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
                _logger.Error(ex, "Unhandled exception in download queue processor");
            }
        }

        private void UpdateDownloadRate()
        {
            // Remove downloads older than 1 hour
            var cutoff = DateTime.UtcNow.AddHours(-1);
            _recentDownloads.RemoveAll(d => d < cutoff);
            
            // Calculate current download rate per hour
            _downloadRatePerHour = _recentDownloads.Count;
            
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

        public async ValueTask QueueBackgroundWorkItemAsync(IDownloadItem workItem)
        {
            await _queue.Writer.WriteAsync(workItem);
            
            lock (_lock)
            {
                _items.Add(workItem);
                _cancellationSources[workItem] = new CancellationTokenSource();
                _totalItemsQueued++;
                
                // Log when we hit large queue sizes
                if (_items.Count % 1000 == 0)
                {
                    _logger.Info($"Queue size has reached {_items.Count} items");
                    _behaviorSimulator.LogSessionStats();
                }
            }
        }

        public void RemoveItem(IDownloadItem item)
        {
            lock (_lock)
            {
                _items.Remove(item);
                if (_cancellationSources.ContainsKey(item))
                {
                    _cancellationSources.Remove(item);
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

        private CancellationToken GetTokenForItem(IDownloadItem item)
        {
            lock (_lock)
            {
                if (_cancellationSources.TryGetValue(item, out var source))
                {
                    return source.Token;
                }
                
                return CancellationToken.None;
            }
        }
        
        private void LogQueueStatistics()
        {
            _logger.Info($"Queue statistics: {_items.Count} items in queue, {_totalItemsProcessed} processed, {_totalItemsQueued} total queued, {_failedDownloads} failed, Rate: {_downloadRatePerHour}/hour");
            
            // Group items by artist to see distribution
            var artistGroups = _items
                .GroupBy(i => i.Artist)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .ToList();
                
            if (artistGroups.Any())
            {
                _logger.Info("Top 5 artists in queue:");
                foreach (var group in artistGroups)
                {
                    _logger.Info($"  - {group.Key}: {group.Count()} items");
                }
            }
            
            // Log behavior simulator stats
            _behaviorSimulator.LogSessionStats();
        }
    }
}

