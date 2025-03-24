using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Download.Clients.Tidal
{
    /// <summary>
    /// Interface for user behavior simulation
    /// </summary>
    public interface IUserBehaviorSimulator
    {
        /// <summary>
        /// Applies a natural behavior delay before the next download
        /// </summary>
        /// <param name="settings">Tidal settings containing behavior parameters</param>
        /// <param name="context">Optional context information about the current download</param>
        /// <param name="cancellationToken">Cancellation token to abort delays</param>
        /// <returns>Task representing the delay operation</returns>
        Task ApplyNaturalBehaviorDelay(TidalSettings settings, DownloadContext context = null, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Reorders the download queue to conform to natural listening patterns
        /// </summary>
        /// <param name="settings">Tidal settings containing behavior parameters</param>
        /// <param name="items">The current download queue items</param>
        /// <returns>The reordered queue items</returns>
        IEnumerable<DownloadQueueItem> ReorderQueue(TidalSettings settings, IEnumerable<DownloadQueueItem> items);
        
        /// <summary>
        /// Determines if a track should be skipped based on configured skip probability
        /// </summary>
        /// <param name="settings">Tidal settings containing behavior parameters</param>
        /// <returns>True if the track should be skipped</returns>
        bool ShouldSkipTrack(TidalSettings settings);
        
        /// <summary>
        /// Gets connection parameters for the current session that may vary to avoid fingerprinting
        /// </summary>
        /// <param name="settings">Tidal settings containing behavior parameters</param>
        /// <returns>Dictionary of connection parameters</returns>
        Dictionary<string, string> GetConnectionParameters(TidalSettings settings);

        /// <summary>
        /// Adapts behavior based on the volume of pending downloads
        /// </summary>
        /// <param name="settings">Tidal settings containing behavior parameters</param>
        /// <param name="itemCount">Number of items in the download queue</param>
        void AdaptToQueueVolume(TidalSettings settings, int itemCount);
        
        /// <summary>
        /// Logs current session statistics
        /// </summary>
        void LogSessionStats();
    }

    /// <summary>
    /// Model class to provide context for the current download
    /// </summary>
    public class DownloadContext
    {
        /// <summary>
        /// Type of delay to apply
        /// </summary>
        public DelayType DelayType { get; set; } = DelayType.Standard;
        
        /// <summary>
        /// Artist ID of the current track
        /// </summary>
        public string ArtistId { get; set; }
        
        /// <summary>
        /// Album ID of the current track
        /// </summary>
        public string AlbumId { get; set; }
        
        /// <summary>
        /// Track number within the album
        /// </summary>
        public int? TrackNumber { get; set; }
        
        /// <summary>
        /// Total number of tracks in the album
        /// </summary>
        public int? TotalTracks { get; set; }
        
        /// <summary>
        /// True if this is the last track in the album
        /// </summary>
        public bool IsLastTrackInAlbum => TrackNumber.HasValue && TotalTracks.HasValue && TrackNumber.Value == TotalTracks.Value;

        /// <summary>
        /// Priority of the download (higher numbers = higher priority)
        /// </summary>
        public int Priority { get; set; } = 0;
    }
    
    /// <summary>
    /// Model class representing an item in the download queue
    /// </summary>
    public class DownloadQueueItem
    {
        /// <summary>
        /// Unique identifier for the item
        /// </summary>
        public string Id { get; set; }
        
        /// <summary>
        /// Artist ID of the track
        /// </summary>
        public string ArtistId { get; set; }
        
        /// <summary>
        /// Album ID of the track
        /// </summary>
        public string AlbumId { get; set; }
        
        /// <summary>
        /// Track number within the album
        /// </summary>
        public int? TrackNumber { get; set; }
        
        /// <summary>
        /// Total number of tracks in the album
        /// </summary>
        public int? TotalTracks { get; set; }
        
        /// <summary>
        /// Artist name
        /// </summary>
        public string ArtistName { get; set; }
        
        /// <summary>
        /// Album name
        /// </summary>
        public string AlbumName { get; set; }
        
        /// <summary>
        /// Track name
        /// </summary>
        public string TrackName { get; set; }

        /// <summary>
        /// Priority of the download (higher numbers = higher priority)
        /// </summary>
        public int Priority { get; set; } = 0;
    }
    
    /// <summary>
    /// Enumeration of delay types
    /// </summary>
    public enum DelayType
    {
        /// <summary>
        /// Standard delay between any tracks
        /// </summary>
        Standard,
        
        /// <summary>
        /// Delay between tracks in the same album
        /// </summary>
        TrackToTrack,
        
        /// <summary>
        /// Delay between different albums
        /// </summary>
        AlbumToAlbum,
        
        /// <summary>
        /// Delay between different artists
        /// </summary>
        ArtistToArtist,
        
        /// <summary>
        /// Delay for a session break
        /// </summary>
        SessionBreak,
        
        /// <summary>
        /// Extended delay for high volume periods
        /// </summary>
        HighVolumeDelay
    }

    /// <summary>
    /// Simulates natural user behavior for downloads to help avoid detection systems.
    /// </summary>
    public class UserBehaviorSimulator : IUserBehaviorSimulator
    {
        private readonly Random _random = new();
        private readonly Logger _logger;
        private DateTime _sessionStart;
        private DateTime _lastActionTime;
        private bool _onBreak = false;
        private readonly object _lock = new();
        
        // Track the last downloaded item to determine context
        private string _lastArtistId;
        private string _lastAlbumId;
        private int? _lastTrackNumber;
        
        // Session statistics
        private int _totalDownloadsInSession = 0;
        private int _totalSessionsCompleted = 0;
        private int _consecutiveDownloads = 0;
        private int _skippedTracks = 0;
        private DateTime _pluginStartTime = DateTime.UtcNow;
        
        // High volume handling
        private bool _isHighVolumeMode = false;
        private TimeSpan _highVolumeSessionDuration = TimeSpan.FromMinutes(60);
        private TimeSpan _highVolumeBreakDuration = TimeSpan.FromMinutes(30);
        
        // Connection parameter rotation
        private readonly string[] _userAgents = new[]
        {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/15.0 Safari/605.1.15",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:89.0) Gecko/20100101 Firefox/89.0",
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/90.0.4430.212 Safari/537.36",
            "Mozilla/5.0 (iPhone; CPU iPhone OS 14_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/14.0 Mobile/15E148 Safari/604.1",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/92.0.4515.159 Safari/537.36 Edg/92.0.902.84",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/93.0.4577.63 Safari/537.36 OPR/79.0.4143.50"
        };
        
        private int _currentUserAgentIndex = 0;

        public UserBehaviorSimulator(Logger logger)
        {
            _logger = logger;
            ResetSession();
            
            _logger.Info($"UserBehaviorSimulator initialized - Version {typeof(NzbDrone.Core.Plugins.TidalPlugin).Assembly.GetName().Version}");
        }

        /// <summary>
        /// Resets the current session timing data
        /// </summary>
        private void ResetSession()
        {
            _sessionStart = DateTime.UtcNow;
            _lastActionTime = DateTime.UtcNow;
            _onBreak = false;
            _consecutiveDownloads = 0;
            _totalSessionsCompleted++;
            
            // Rotate user agent on new session
            if (_random.Next(100) < 70) // 70% chance to rotate 
            {
                _currentUserAgentIndex = (_random.Next(_userAgents.Length)); // Truly random instead of sequential
            }
            
            _logger.Debug($"Session reset. Total sessions: {_totalSessionsCompleted}. Total downloads: {_totalDownloadsInSession}");
        }

        /// <summary>
        /// Starts a break between download sessions
        /// </summary>
        private void StartBreak()
        {
            _onBreak = true;
            _lastActionTime = DateTime.UtcNow;
            _logger.Debug("Starting a download session break");
        }

        /// <summary>
        /// Determines if the session should take a break based on session duration
        /// </summary>
        /// <param name="settings">Tidal settings containing behavior parameters</param>
        /// <returns>True if the session should take a break</returns>
        private bool ShouldTakeBreak(TidalSettings settings)
        {
            // Skip if natural behavior is disabled
            if (!settings.EnableNaturalBehavior)
            {
                return false;
            }
            
            // If session duration exceeds the configured limit, take a break
            var sessionDuration = DateTime.UtcNow - _sessionStart;
            
            // Use high volume session duration if in high volume mode
            if (_isHighVolumeMode)
            {
                return sessionDuration >= _highVolumeSessionDuration;
            }
            
            return sessionDuration.TotalMinutes >= settings.SessionDurationMinutes;
        }

        /// <summary>
        /// Determines if the break should end based on break duration
        /// </summary>
        /// <param name="settings">Tidal settings containing behavior parameters</param>
        /// <returns>True if the break should end</returns>
        private bool ShouldEndBreak(TidalSettings settings)
        {
            // If break duration exceeds the configured limit, end the break
            var breakDuration = DateTime.UtcNow - _lastActionTime;
            
            // Use high volume break duration if in high volume mode
            if (_isHighVolumeMode)
            {
                return breakDuration >= _highVolumeBreakDuration;
            }
            
            return breakDuration.TotalMinutes >= settings.BreakDurationMinutes;
        }
        
        /// <summary>
        /// Checks if the download should be active based on time of day
        /// </summary>
        /// <param name="settings">Tidal settings containing behavior parameters</param>
        /// <returns>True if the current time is within active hours</returns>
        private bool IsWithinActiveHours(TidalSettings settings)
        {
            if (!settings.TimeOfDayAdaptation)
            {
                return true; // Always active if adaptation is disabled
            }
            
            int currentHour = DateTime.Now.Hour;
            int start = settings.ActiveHoursStart;
            int end = settings.ActiveHoursEnd;
            
            // Handle wrap around (e.g., 22 - 6)
            if (start <= end)
            {
                return currentHour >= start && currentHour < end;
            }
            else
            {
                return currentHour >= start || currentHour < end;
            }
        }

        /// <summary>
        /// Generates a random delay based on the delay type and settings
        /// </summary>
        /// <param name="settings">Tidal settings containing delay parameters</param>
        /// <param name="delayType">Type of delay to generate</param>
        /// <returns>Delay duration in seconds</returns>
        private double GenerateDelay(TidalSettings settings, DelayType delayType)
        {
            lock (_lock)
            {
                double min, max;
                
                switch (delayType)
                {
                    case DelayType.TrackToTrack when settings.SimulateListeningPatterns:
                        min = settings.TrackToTrackDelayMin;
                        max = settings.TrackToTrackDelayMax;
                        break;
                        
                    case DelayType.AlbumToAlbum when settings.SimulateListeningPatterns:
                        min = settings.AlbumToAlbumDelayMin;
                        max = settings.AlbumToAlbumDelayMax;
                        break;
                        
                    case DelayType.ArtistToArtist when settings.SimulateListeningPatterns:
                        // Artist to artist transitions get a slightly longer delay than album to album
                        min = settings.AlbumToAlbumDelayMin * 1.5;
                        max = settings.AlbumToAlbumDelayMax * 1.5;
                        break;
                        
                    case DelayType.SessionBreak:
                        // Short delay for session breaks (actual break is handled separately)
                        min = 10;
                        max = 30;
                        break;
                        
                    case DelayType.HighVolumeDelay:
                        // Extended delay for high volume periods
                        min = settings.DownloadDelayMin * 2;
                        max = settings.DownloadDelayMax * 3;
                        break;
                        
                    default:
                        // Fall back to standard delay settings
                        min = settings.DownloadDelayMin;
                        max = settings.DownloadDelayMax;
                        break;
                }
                
                // Ensure min doesn't exceed max
                if (min > max)
                {
                    (min, max) = (max, min);
                }
                
                // Add some randomization to non-active hours if time adaptation is enabled
                if (settings.TimeOfDayAdaptation && !IsWithinActiveHours(settings))
                {
                    // During non-active hours, increase delays
                    min *= 2;
                    max *= 3;
                }

                // Calculate a value between min and max with a natural distribution
                // Use a triangular-like distribution that favors values closer to the min
                double u = _random.NextDouble();
                double v = _random.NextDouble();
                return min + (max - min) * Math.Min(u, v);
            }
        }
        
        /// <summary>
        /// Determines the appropriate delay type based on the download context
        /// </summary>
        /// <param name="context">The download context</param>
        /// <returns>The appropriate delay type</returns>
        private DelayType DetermineDelayType(DownloadContext context)
        {
            if (context == null)
            {
                return DelayType.Standard;
            }
            
            if (context.DelayType != DelayType.Standard)
            {
                return context.DelayType;
            }
            
            // If we have context information, determine the delay type
            if (_lastAlbumId != null && context.AlbumId != null)
            {
                if (_lastAlbumId != context.AlbumId)
                {
                    // Different album
                    if (_lastArtistId != null && context.ArtistId != null && _lastArtistId != context.ArtistId)
                    {
                        // Different artist
                        return DelayType.ArtistToArtist;
                    }
                    
                    return DelayType.AlbumToAlbum;
                }
                else
                {
                    // Same album, track-to-track delay
                    return DelayType.TrackToTrack;
                }
            }
            
            return DelayType.Standard;
        }
        
        /// <summary>
        /// Updates the context tracking after a download
        /// </summary>
        /// <param name="context">The download context</param>
        private void UpdateContextTracking(DownloadContext context)
        {
            if (context == null)
            {
                return;
            }
            
            _lastArtistId = context.ArtistId;
            _lastAlbumId = context.AlbumId;
            _lastTrackNumber = context.TrackNumber;
        }

        /// <summary>
        /// Applies a natural behavior delay before the next download
        /// </summary>
        /// <param name="settings">Tidal settings containing behavior parameters</param>
        /// <param name="context">Optional context information about the current download</param>
        /// <param name="cancellationToken">Cancellation token to abort delays</param>
        /// <returns>Task representing the delay operation</returns>
        public virtual async Task ApplyNaturalBehaviorDelay(TidalSettings settings, DownloadContext context = null, CancellationToken cancellationToken = default)
        {
            // Skip if neither natural behavior nor download delay is enabled
            if (!settings.EnableNaturalBehavior && !settings.DownloadDelay)
            {
                return;
            }
            
            if (settings.EnableNaturalBehavior)
            {
                if (_onBreak)
                {
                    // Check if we should end the break
                    if (ShouldEndBreak(settings))
                    {
                        _logger.Debug("Ending download session break");
                        ResetSession();
                    }
                    else
                    {
                        // Still on break, wait a bit before checking again
                        _logger.Debug("Still on download break");
                        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                        return;
                    }
                }
                else if (ShouldTakeBreak(settings))
                {
                    StartBreak();
                    // Apply initial break delay
                    _logger.Debug($"Beginning a {settings.BreakDurationMinutes} minute download break");
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                    return;
                }
                
                // If time adaptation is enabled and we're outside active hours
                if (settings.TimeOfDayAdaptation && !IsWithinActiveHours(settings))
                {
                    // 80% chance to pause for a longer period during non-active hours
                    if (_random.NextDouble() < 0.8)
                    {
                        _logger.Debug("Outside active hours, applying extended delay");
                        double inactiveDelay = _random.Next(60, 300); // 1-5 minute delay
                        await Task.Delay(TimeSpan.FromSeconds(inactiveDelay), cancellationToken);
                        return;
                    }
                }
            }

            // Determine the appropriate delay type
            DelayType delayType = settings.EnableNaturalBehavior ? 
                DetermineDelayType(context) : 
                DelayType.Standard;
                
            // Apply a delay based on the context
            double delayDuration = GenerateDelay(settings, delayType);
            
            _logger.Debug($"{delayType} delay: {delayDuration:F2} seconds");
            await Task.Delay(TimeSpan.FromSeconds(delayDuration), cancellationToken);
            
            // Update the last action time and context tracking
            _lastActionTime = DateTime.UtcNow;
            if (settings.EnableNaturalBehavior)
            {
                UpdateContextTracking(context);
            }
        }
        
        /// <summary>
        /// Reorders the download queue to conform to natural listening patterns
        /// </summary>
        /// <param name="settings">Tidal settings containing behavior parameters</param>
        /// <param name="items">The current download queue items</param>
        /// <returns>The reordered queue items</returns>
        public virtual IEnumerable<DownloadQueueItem> ReorderQueue(TidalSettings settings, IEnumerable<DownloadQueueItem> items)
        {
            // Early return if natural behavior is disabled
            if (!settings.EnableNaturalBehavior)
            {
                return items;
            }
            
            var itemsList = items.ToList();
            if (!itemsList.Any())
            {
                return itemsList;
            }
            
            _logger.Debug($"Reordering queue with {itemsList.Count} items using natural behavior patterns");
            
            // Group items by artist and album
            var byAlbum = itemsList.GroupBy(i => i.AlbumId).ToDictionary(g => g.Key, g => g.ToList());
            var byArtist = itemsList.GroupBy(i => i.ArtistId).ToDictionary(g => g.Key, g => g.ToList());
            
            var result = new List<DownloadQueueItem>();
            var processedAlbums = new HashSet<string>();
            
            // Start with the last album/artist we were working on if possible
            string startingAlbumId = _lastAlbumId;
            string startingArtistId = _lastArtistId;
            
            // If we don't have a starting point or it's not in the current queue, pick a random one
            if (startingAlbumId == null || !byAlbum.ContainsKey(startingAlbumId))
            {
                // Get list of album IDs
                var albumIds = byAlbum.Keys.ToList();
                
                if (settings.RandomizeAlbumOrder)
                {
                    // Randomize album order
                    ShuffleList(albumIds);
                }
                
                startingAlbumId = albumIds.FirstOrDefault();
                if (startingAlbumId != null)
                {
                    var startingItems = byAlbum[startingAlbumId];
                    startingArtistId = startingItems.FirstOrDefault()?.ArtistId;
                }
            }
            
            // Process the starting album first
            if (startingAlbumId != null && byAlbum.ContainsKey(startingAlbumId))
            {
                ProcessAlbum(startingAlbumId, byAlbum, processedAlbums, result, settings);
            }
            
            // Then process the rest of the albums from the same artist if artist grouping is enabled
            if (settings.PreferArtistGrouping && startingArtistId != null && byArtist.ContainsKey(startingArtistId))
            {
                ProcessArtistAlbums(startingArtistId, byArtist, byAlbum, processedAlbums, result, settings);
            }
            
            // Process remaining artists/albums
            // Create a list of artists to process
            var artistIds = byArtist.Keys.ToList();
            if (settings.RandomizeAlbumOrder)
            {
                ShuffleList(artistIds);
            }
            
            foreach (var artistId in artistIds)
            {
                // Skip the already processed starting artist
                if (artistId == startingArtistId)
                {
                    continue;
                }
                
                ProcessArtistAlbums(artistId, byArtist, byAlbum, processedAlbums, result, settings);
            }
            
            // Add any remaining items that weren't processed (shouldn't happen, but just in case)
            foreach (var item in itemsList)
            {
                if (!result.Contains(item))
                {
                    result.Add(item);
                }
            }
            
            _logger.Debug($"Queue reordering complete, resulting in {result.Count} ordered items");
            return result;
        }
        
        /// <summary>
        /// Processes all albums from a specific artist
        /// </summary>
        private void ProcessArtistAlbums(
            string artistId, 
            Dictionary<string, List<DownloadQueueItem>> byArtist,
            Dictionary<string, List<DownloadQueueItem>> byAlbum,
            HashSet<string> processedAlbums,
            List<DownloadQueueItem> result,
            TidalSettings settings)
        {
            var artistItems = byArtist[artistId];
            
            // Get all album IDs for this artist that haven't been processed yet
            var albumIds = artistItems
                .Select(i => i.AlbumId)
                .Distinct()
                .Where(id => !processedAlbums.Contains(id))
                .ToList();
                
            if (settings.RandomizeAlbumOrder)
            {
                ShuffleList(albumIds);
            }
            
            // Process each album
            foreach (var albumId in albumIds)
            {
                if (albumId != null && byAlbum.ContainsKey(albumId))
                {
                    ProcessAlbum(albumId, byAlbum, processedAlbums, result, settings);
                }
            }
        }
        
        /// <summary>
        /// Processes all tracks from a specific album
        /// </summary>
        private void ProcessAlbum(
            string albumId,
            Dictionary<string, List<DownloadQueueItem>> byAlbum,
            HashSet<string> processedAlbums,
            List<DownloadQueueItem> result,
            TidalSettings settings)
        {
            if (processedAlbums.Contains(albumId))
            {
                return;
            }
            
            var albumItems = byAlbum[albumId];
            
            if (settings.SequentialTrackOrder && albumItems.All(i => i.TrackNumber.HasValue))
            {
                // Sort by track number
                albumItems = albumItems.OrderBy(i => i.TrackNumber.Value).ToList();
            }
            else if (!settings.SequentialTrackOrder)
            {
                // Randomize tracks within the album
                ShuffleList(albumItems);
            }
            
            // Add all tracks from this album to the result
            result.AddRange(albumItems);
            
            // Mark this album as processed
            processedAlbums.Add(albumId);
        }
        
        /// <summary>
        /// Shuffles a list in place
        /// </summary>
        private void ShuffleList<T>(List<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = _random.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }
        
        /// <summary>
        /// Determines if a track should be skipped based on configured skip probability
        /// </summary>
        /// <param name="settings">Tidal settings containing behavior parameters</param>
        /// <returns>True if the track should be skipped</returns>
        public virtual bool ShouldSkipTrack(TidalSettings settings)
        {
            if (!settings.EnableNaturalBehavior || !settings.SimulateSkips)
            {
                return false;
            }
            
            int probability = Math.Clamp((int)(settings.SkipProbability * 100), 0, 100);
            return _random.Next(100) < probability;
        }
        
        /// <summary>
        /// Gets connection parameters for the current session that may vary to avoid fingerprinting
        /// </summary>
        /// <param name="settings">Tidal settings containing behavior parameters</param>
        /// <returns>Dictionary of connection parameters</returns>
        public virtual Dictionary<string, string> GetConnectionParameters(TidalSettings settings)
        {
            var parameters = new Dictionary<string, string>();
            
            if (!settings.EnableNaturalBehavior)
            {
                return parameters;
            }
            
            // Add user agent rotation if enabled
            if (settings.RotateUserAgent)
            {
                parameters["User-Agent"] = _userAgents[_currentUserAgentIndex];
            }
            
            // Add connection parameter variation if enabled
            if (settings.VaryConnectionParameters)
            {
                // Add random accept-language values
                string[] languages = { "en-US", "en-GB", "en", "en-CA", "en-AU" };
                parameters["Accept-Language"] = languages[_random.Next(languages.Length)];
                
                // Randomly include or exclude certain headers
                if (_random.Next(100) < 70) // 70% chance
                {
                    parameters["Accept-Encoding"] = "gzip, deflate, br";
                }
                
                if (_random.Next(100) < 60) // 60% chance
                {
                    parameters["DNT"] = "1";
                }
            }
            
            return parameters;
        }

        /// <summary>
        /// Adapts behavior based on the volume of pending downloads
        /// </summary>
        /// <param name="settings">Tidal settings containing behavior parameters</param>
        /// <param name="itemCount">Number of items in the download queue</param>
        public void AdaptToQueueVolume(TidalSettings settings, int itemCount)
        {
            if (itemCount > settings.HighVolumeThreshold)
            {
                _isHighVolumeMode = true;
                _logger.Debug($"Entering high volume mode due to {itemCount} items in the queue");
            }
            else
            {
                _isHighVolumeMode = false;
            }
        }

        /// <summary>
        /// Logs current session statistics
        /// </summary>
        public void LogSessionStats()
        {
            _logger.Info($"Session statistics: Total downloads {_totalDownloadsInSession}, Total sessions {_totalSessionsCompleted}, Consecutive downloads {_consecutiveDownloads}, Skipped tracks {_skippedTracks}");
        }

        // Public statistics for the download viewer
        public int SessionsCompleted => _totalSessionsCompleted;
        public bool IsHighVolumeMode => _isHighVolumeMode;
        public int DownloadsInCurrentSession => _totalDownloadsInSession;
        public int TotalDownloads => _totalDownloadsInSession;
        public TimeSpan CurrentSessionDuration => DateTime.UtcNow - _sessionStart;
    }
} 