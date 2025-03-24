using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Download.Clients.Tidal.Queue
{
    /// <summary>
    /// Manages the download queue with natural behavior simulation to avoid detection
    /// </summary>
    public class NaturalDownloadScheduler
    {
        private readonly DownloadTaskQueue _queue;
        private readonly IUserBehaviorSimulator _behaviorSimulator;
        private readonly Logger _logger;
        private readonly TidalSettings _settings;
        private readonly Dictionary<string, HashSet<IDownloadItem>> _artistGroups = new();
        private readonly Random _random = new();
        private readonly object _lock = new();

        public NaturalDownloadScheduler(DownloadTaskQueue queue, IUserBehaviorSimulator behaviorSimulator, TidalSettings settings, Logger logger)
        {
            _queue = queue;
            _behaviorSimulator = behaviorSimulator;
            _settings = settings;
            _logger = logger;
        }

        /// <summary>
        /// Process the next item in the queue, applying natural behavior logic
        /// </summary>
        /// <param name="token">Cancellation token</param>
        /// <returns>The next item to process, or null if none available</returns>
        public async Task<IDownloadItem> GetNextItem(CancellationToken token)
        {
            // Apply natural behavior delay
            await _behaviorSimulator.ApplyNaturalBehaviorDelay(_settings, null, token);

            // Get all items in the queue
            var allItems = _queue.GetQueueListing();
            if (allItems.Length == 0)
            {
                return null;
            }

            // Apply selection strategy based on settings
            IDownloadItem selectedItem;

            if (_settings.EnableNaturalBehavior)
            {
                if (_settings.PreferArtistGrouping)
                {
                    selectedItem = GetNextItemWithArtistGrouping(allItems);
                }
                else if (_settings.RandomizeAlbumOrder)
                {
                    selectedItem = GetRandomItem(allItems);
                }
                else
                {
                    // Default to first item in queue
                    selectedItem = allItems[0];
                }
                
                // Check if we should skip this track
                if (_behaviorSimulator.ShouldSkipTrack(_settings))
                {
                    _logger.Debug($"Natural behavior is skipping track: {selectedItem.Title}");
                    _queue.RemoveItem(selectedItem);
                    return await GetNextItem(token); // Recursively get the next item
                }
            }
            else
            {
                // Default behavior - first item in queue
                selectedItem = allItems[0];
            }

            return selectedItem;
        }

        /// <summary>
        /// Gets a random item from the queue
        /// </summary>
        private IDownloadItem GetRandomItem(IDownloadItem[] items)
        {
            lock (_lock)
            {
                int index = _random.Next(items.Length);
                return items[index];
            }
        }

        /// <summary>
        /// Gets the next item using artist grouping strategy
        /// </summary>
        private IDownloadItem GetNextItemWithArtistGrouping(IDownloadItem[] items)
        {
            // Update artist groups with current queue items
            UpdateArtistGroups(items);

            // Find the artist with the most items in the queue
            string selectedArtist = null;
            int maxItems = 0;

            foreach (var group in _artistGroups)
            {
                if (group.Value.Count > maxItems)
                {
                    maxItems = group.Value.Count;
                    selectedArtist = group.Key;
                }
            }

            // If we found an artist with multiple items, select a random item from that artist
            if (selectedArtist != null && maxItems > 1)
            {
                var artistItems = _artistGroups[selectedArtist].ToArray();
                return GetRandomItem(artistItems);
            }

            // Otherwise, just select a random item from the queue
            return GetRandomItem(items);
        }

        /// <summary>
        /// Updates the artist groups based on current queue items
        /// </summary>
        private void UpdateArtistGroups(IDownloadItem[] items)
        {
            lock (_lock)
            {
                _artistGroups.Clear();

                foreach (var item in items)
                {
                    if (!_artistGroups.ContainsKey(item.Artist))
                    {
                        _artistGroups[item.Artist] = new HashSet<IDownloadItem>();
                    }

                    _artistGroups[item.Artist].Add(item);
                }
            }
        }
    }
} 