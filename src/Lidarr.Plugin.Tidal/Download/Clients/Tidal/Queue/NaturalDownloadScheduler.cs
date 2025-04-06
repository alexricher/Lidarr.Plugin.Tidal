using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Download.Clients.Tidal.Interfaces;

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
        private DateTime? _lastInactiveHoursLogTime = null;
        private DateTime _lastSessionStart = DateTime.MinValue;
        private DateTime _lastBreakStart = DateTime.MinValue;
        private DateTime _lastBreakLogTime = DateTime.MinValue;

        public NaturalDownloadScheduler(DownloadTaskQueue queue, IUserBehaviorSimulator behaviorSimulator, TidalSettings settings, Logger logger)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue), "Download queue cannot be null");
            _behaviorSimulator = behaviorSimulator ?? new UserBehaviorSimulator(logger);
            _settings = settings ?? new TidalSettings 
            {
                // Default values for critical settings
                SessionDurationMinutes = 60,
                BreakDurationMinutes = 15,
                EnableNaturalBehavior = false
            };
            _logger = logger ?? LogManager.GetCurrentClassLogger();
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

            // Check current download rate against MaxDownloadsPerHour
            var downloadRate = GetCurrentDownloadRate();
            if (_settings.MaxDownloadsPerHour > 0 && downloadRate >= _settings.MaxDownloadsPerHour)
            {
                _logger.Info($"🛑 Max download rate reached: {downloadRate}/{_settings.MaxDownloadsPerHour} per hour. Delaying next download.");

                // Calculate delay based on 1-hour sliding window
                // Wait for the oldest download to fall out of the 1-hour window, plus a small random delay
                var delayMinutes = Math.Max(1, _random.Next(3, 10));
                await Task.Delay(TimeSpan.FromMinutes(delayMinutes), token);

                // Try again after the delay
                return await GetNextItem(token);
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
            UpdateArtistGroups(items.ToList());

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
        private void UpdateArtistGroups(IList<IDownloadItem> items)
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

        /// <summary>
        /// Gets the current download rate per hour
        /// </summary>
        private int GetCurrentDownloadRate()
        {
            // Use the queue's tracking method which maintains a sliding 1-hour window
            return _queue.GetCurrentHourlyDownloadRate();
        }

        public bool ShouldProcessQueue(int queueSize)
        {
            // If natural behavior is disabled, always process
            if (!_settings.EnableNaturalBehavior)
                return true;

            // Check for time-of-day restrictions
            if (_settings.TimeOfDayAdaptation)
            {
                DateTime now = DateTime.Now;
                int currentHour = now.Hour;

                bool isWithinActiveHours = false;

                // Handle case where active hours span midnight
                if (_settings.ActiveHoursStart > _settings.ActiveHoursEnd)
                {
                    // Example: ActiveHoursStart=22, ActiveHoursEnd=6
                    // This means active from 10PM to 6AM
                    isWithinActiveHours = currentHour >= _settings.ActiveHoursStart || currentHour < _settings.ActiveHoursEnd;
                }
                else
                {
                    // Standard case: ActiveHoursStart < ActiveHoursEnd
                    // Example: ActiveHoursStart=8, ActiveHoursEnd=22
                    // This means active from 8AM to 10PM
                    isWithinActiveHours = currentHour >= _settings.ActiveHoursStart && currentHour < _settings.ActiveHoursEnd;
                }

                if (!isWithinActiveHours)
                {
                    // Log this only occasionally to avoid log spam
                    if (!_lastInactiveHoursLogTime.HasValue || (DateTime.UtcNow - _lastInactiveHoursLogTime.Value).TotalMinutes >= 5)
                    {
                        var nextActiveTime = GetNextActiveTime();
                        _logger.Info($"⏰ Outside active hours ({_settings.ActiveHoursStart:00}:00 - {_settings.ActiveHoursEnd:00}:00). Queue processing paused until {nextActiveTime:HH:mm}");
                        _lastInactiveHoursLogTime = DateTime.UtcNow;
                    }

                    return false;
                }
            }

            // Check if queue is in high volume mode
            if (_settings.EnableHighVolumeHandling && queueSize >= _settings.HighVolumeThreshold)
            {
                return IsWithinHighVolumeSession();
            }

            // Check if we're within a standard session
            return IsWithinActiveSession();
        }

        /// <summary>
        /// Gets the next time when active hours will begin
        /// </summary>
        private DateTime GetNextActiveTime()
        {
            DateTime now = DateTime.Now;
            int currentHour = now.Hour;
            DateTime today = now.Date;

            // Create a starting point at today's active start hour
            DateTime activeStartToday = today.AddHours(_settings.ActiveHoursStart);

            // Case where active hours span midnight
            if (_settings.ActiveHoursStart > _settings.ActiveHoursEnd)
            {
                // If we're after today's start time, it starts tomorrow
                if (currentHour >= _settings.ActiveHoursStart)
                {
                    return activeStartToday.AddDays(1);
                }
                // If we're before today's end time, it starts today
                else if (currentHour < _settings.ActiveHoursEnd)
                {
                    return activeStartToday;
                }
                // If we're between end and start, it starts today
                else
                {
                    return activeStartToday;
                }
            }
            else
            {
                // Standard case
                // If we're before today's start time, it starts today
                if (currentHour < _settings.ActiveHoursStart)
                {
                    return activeStartToday;
                }
                // If we're after/equal to today's end time, it starts tomorrow
                else
                {
                    return activeStartToday.AddDays(1);
                }
            }
        }

        /// <summary>
        /// Checks if we are currently within an active high volume session
        /// </summary>
        private bool IsWithinHighVolumeSession()
        {
            // If high volume mode is not enabled, return false
            if (!_settings.EnableHighVolumeHandling)
                return false;

            // Get the current time
            DateTime now = DateTime.UtcNow;

            // Check if we're in a session or break period
            if (_lastSessionStart == DateTime.MinValue)
            {
                // First session - always start with a session
                _lastSessionStart = now;
                _lastBreakStart = DateTime.MinValue;
                return true;
            }

            TimeSpan sessionDuration = TimeSpan.FromMinutes(_settings.HighVolumeSessionMinutes);
            TimeSpan breakDuration = TimeSpan.FromMinutes(_settings.HighVolumeBreakMinutes);

            // Check if we're still in the session
            if (_lastSessionStart != DateTime.MinValue && (now - _lastSessionStart) <= sessionDuration)
            {
                return true;
            }

            // If we've exceeded session duration, check if we need to start a break
            if (_lastBreakStart == DateTime.MinValue || (now - _lastBreakStart) >= breakDuration)
            {
                // Break is over (or we haven't started one yet), start a new session
                _lastSessionStart = now;
                _lastBreakStart = DateTime.MinValue;
                _logger.Info($"Starting new high volume session at {now:HH:mm:ss}");
                return true;
            }

            // We're in a break period
            var remainingBreak = breakDuration - (now - _lastBreakStart);
            if (_lastBreakLogTime == DateTime.MinValue || (now - _lastBreakLogTime).TotalMinutes >= 5)
            {
                _logger.Info($"⏸️ On high volume break ({_settings.HighVolumeBreakMinutes} min). Resuming in {remainingBreak.TotalMinutes:F1} minutes");
                _lastBreakLogTime = now;
            }

            return false;
        }

        /// <summary>
        /// Checks if we are currently within an active standard session
        /// </summary>
        private bool IsWithinActiveSession()
        {
            // If natural behavior is disabled, always return true
            if (!_settings.EnableNaturalBehavior)
                return true;

            // Get the current time
            DateTime now = DateTime.UtcNow;

            // Check if we're in a session or break period
            if (_lastSessionStart == DateTime.MinValue)
            {
                // First session - always start with a session
                _lastSessionStart = now;
                _lastBreakStart = DateTime.MinValue;
                return true;
            }

            TimeSpan sessionDuration = TimeSpan.FromMinutes(_settings.SessionDurationMinutes);
            TimeSpan breakDuration = TimeSpan.FromMinutes(_settings.BreakDurationMinutes);

            // Check if we're still in the session
            if (_lastSessionStart != DateTime.MinValue && (now - _lastSessionStart) <= sessionDuration)
            {
                return true;
            }

            // If we've exceeded session duration, check if we need to start a break
            if (_lastBreakStart == DateTime.MinValue)
            {
                // Start a break
                _lastBreakStart = now;
                _lastSessionStart = DateTime.MinValue;
                _logger.Info($"🛑 Session ended after {sessionDuration.TotalMinutes:F1} minutes. Taking a break for {breakDuration.TotalMinutes:F1} minutes");
                return false;
            }

            // Check if break is over
            if ((now - _lastBreakStart) >= breakDuration)
            {
                // Break is over, start a new session
                _lastSessionStart = now;
                _lastBreakStart = DateTime.MinValue;
                _logger.Info($"▶️ Break complete. Starting new download session at {now:HH:mm:ss}");
                return true;
            }

            // We're in a break period
            var remainingBreak = breakDuration - (now - _lastBreakStart);
            if (_lastBreakLogTime == DateTime.MinValue || (now - _lastBreakLogTime).TotalMinutes >= 5)
            {
                _logger.Info($"⏸️ On break ({_settings.BreakDurationMinutes} min). Resuming in {remainingBreak.TotalMinutes:F1} minutes");
                _lastBreakLogTime = now;
            }

            return false;
        }
    }
}