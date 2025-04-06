using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Tidal.Services.Logging;
using NLog;

namespace NzbDrone.Core.Download.Clients.Tidal
{


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
        private readonly ILogger _logger;
        private DateTime _sessionStart;
        private DateTime _lastActionTime;
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

        // Delay tracking fields
        private DateTime _lastDelayTime;
        private double _lastDelayValue;
        private bool _antiPatternJitter = true;

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

        public UserBehaviorSimulator(ILogger logger)
        {
            _logger = logger ?? LogManager.GetLogger("UserBehaviorSimulator");
            ResetSession();

            _logger.DebugWithEmoji(LogEmojis.Start, $"UserBehaviorSimulator initialized - Version {typeof(NzbDrone.Core.Plugins.TidalPlugin).Assembly.GetName().Version}");
        }

        /// <summary>
        /// Logs detailed version and settings information once, for diagnostics
        /// </summary>
        public void LogVersionInfo()
        {
            _logger.InfoWithEmoji(LogEmojis.Retry, $"Tidal Behavior Simulator Version {typeof(NzbDrone.Core.Plugins.TidalPlugin).Assembly.GetName().Version}");
            _logger.Info($"   Mode: {(_isHighVolumeMode ? "High Volume" : "Standard")}, Sessions: {_totalSessionsCompleted}");
        }

        /// <summary>
        /// Resets the current session timing data
        /// </summary>
        private void ResetSession()
        {
            _sessionStart = DateTime.UtcNow;
            _lastActionTime = DateTime.UtcNow;
            _consecutiveDownloads = 0;
            _totalSessionsCompleted++;

            // Rotate user agent on new session
            if (_random.Next(100) < 70) // 70% chance to rotate
            {
                _currentUserAgentIndex = (_random.Next(_userAgents.Length)); // Truly random instead of sequential
            }

            _logger.DebugWithEmoji(LogEmojis.Retry, $"Session reset. Total sessions: {_totalSessionsCompleted}. Total downloads: {_totalDownloadsInSession}");
        }

        /// <summary>
        /// Starts a break between download sessions
        /// </summary>
        private void StartBreak()
        {
            _lastActionTime = DateTime.UtcNow;
            _logger.DebugWithEmoji(LogEmojis.Pause, "Starting a download session break");
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
        /// Checks if the current time is within the active hours defined in settings
        /// </summary>
        private bool IsWithinActiveHours(TidalSettings settings)
        {
            // If time-of-day adaptation is not enabled, always return true
            if (!settings.TimeOfDayAdaptation)
                return true;

            // Get current hour in 24-hour format (0-23)
            int currentHour = DateTime.Now.Hour;

            // Handle case where active hours span midnight
            if (settings.ActiveHoursStart > settings.ActiveHoursEnd)
            {
                // Example: ActiveHoursStart=22, ActiveHoursEnd=6
                // This means active from 10PM to 6AM
                return currentHour >= settings.ActiveHoursStart || currentHour < settings.ActiveHoursEnd;
            }
            else
            {
                // Standard case: ActiveHoursStart < ActiveHoursEnd
                // Example: ActiveHoursStart=8, ActiveHoursEnd=22
                // This means active from 8AM to 10PM
                return currentHour >= settings.ActiveHoursStart && currentHour < settings.ActiveHoursEnd;
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
                // Skip delay generation if settings is null or no delay simulation is enabled
                if (settings == null || (!settings.EnableNaturalBehavior && !settings.SimulateDelays && !settings.DownloadDelay))
                {
                    return 0.0;
                }

                double min = 0.5, max = 3.0; // Default values if we can't determine from settings

                switch (delayType)
                {
                    case DelayType.TrackToTrack when settings.SimulateListeningPatterns:
                        // Ensure min/max are valid and min < max
                        min = Math.Max(0.1, settings.TrackToTrackDelayMin);
                        max = Math.Max(min + 0.1, settings.TrackToTrackDelayMax);
                        break;

                    case DelayType.AlbumToAlbum when settings.SimulateListeningPatterns:
                        // Ensure min/max are valid and min < max
                        min = Math.Max(1.0, settings.AlbumToAlbumDelayMin);
                        max = Math.Max(min + 1.0, settings.AlbumToAlbumDelayMax);
                        break;

                    case DelayType.ArtistToArtist when settings.SimulateListeningPatterns:
                        // Artist to artist transitions get a slightly longer delay than album to album
                        min = Math.Max(1.0, settings.AlbumToAlbumDelayMin * 1.5);
                        max = Math.Max(min + 1.0, settings.AlbumToAlbumDelayMax * 1.5);
                        break;

                    case DelayType.SessionBreak:
                        // Short delay for session breaks (actual break is handled separately)
                        min = 10;
                        max = 30;
                        break;

                    case DelayType.HighVolumeDelay:
                        // Extended delay for high volume periods
                        min = Math.Max(1.0, settings.DownloadDelayMin > 0 ? settings.DownloadDelayMin * 2 : 2.0);
                        max = Math.Max(min + 1.0, settings.DownloadDelayMax > 0 ? settings.DownloadDelayMax * 3 : 6.0);
                        break;

                    default:
                        // Check if we should use MS-based settings instead of seconds-based
                        if (settings.SimulateDelays)
                        {
                            // Convert MS settings to seconds for consistency
                            if (delayType == DelayType.TrackToTrack || delayType == DelayType.Standard)
                            {
                                min = Math.Max(0.1, settings.MinDelayBetweenTracksMs / 1000.0);
                                max = Math.Max(min + 0.1, settings.MaxDelayBetweenTracksMs / 1000.0);
                            }
                            else if (delayType == DelayType.AlbumToAlbum || delayType == DelayType.ArtistToArtist)
                            {
                                min = Math.Max(0.5, settings.MinDelayBetweenAlbumsMs / 1000.0);
                                max = Math.Max(min + 0.5, settings.MaxDelayBetweenAlbumsMs / 1000.0);
                            }
                            else
                            {
                                // Fall back to legacy delay settings
                                min = Math.Max(0.5, settings.DownloadDelayMin);
                                max = Math.Max(min + 0.5, settings.DownloadDelayMax);
                            }
                        }
                        else
                        {
                            // Fall back to standard delay settings
                            min = Math.Max(0.5, settings.DownloadDelayMin);
                            max = Math.Max(min + 0.5, settings.DownloadDelayMax);
                        }
                        break;
                }

                // Generate a random delay between min and max
                double delay;

                // Apply weighted randomness based on the configured type
                if (settings.BehaviorProfileType == (int)BehaviorProfile.Automatic ||
                    settings.BehaviorProfileType == (int)BehaviorProfile.MusicEnthusiast)
                {
                    // Use exponential-like distribution - more shorter delays, fewer longer ones
                    double randomVal = Math.Pow(_random.NextDouble(), 2.0); // Square to bias toward shorter delays
                    delay = min + randomVal * (max - min);
                }
                else if (settings.BehaviorProfileType == (int)BehaviorProfile.CasualListener)
                {
                    // Use a normal-like distribution - more delays in the middle of the range
                    double u1 = 1.0 - _random.NextDouble();
                    double u2 = 1.0 - _random.NextDouble();
                    double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);

                    // Scale to our range, truncated to min-max
                    double mean = (min + max) / 2.0;
                    double stdDev = (max - min) / 4.0;
                    delay = Math.Min(Math.Max(mean + stdDev * randStdNormal, min), max);
                }
                else
                {
                    // Default uniform distribution
                    delay = min + _random.NextDouble() * (max - min);
                }

                // Apply anti-pattern detection avoidance - add jitter to ensure delays aren't too regular
                if (_antiPatternJitter && _random.NextDouble() < 0.7)
                {
                    // 70% chance to add a small amount of jitter to the delay
                    double jitterFactor = 0.8 + (_random.NextDouble() * 0.4); // 0.8 to 1.2
                    delay *= jitterFactor;
                }

                // Record the delay
                _lastDelayTime = DateTime.UtcNow;
                _lastDelayValue = delay;

                return delay;
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
        /// Applies natural behavior delay based on context and settings
        /// </summary>
        public async Task ApplyNaturalBehaviorDelay(TidalSettings settings, DownloadContext context, CancellationToken cancellation)
        {
            if (settings == null || cancellation.IsCancellationRequested)
            {
                return;
            }

            // Record activity for this download
            lock (_lock)
            {
                _lastActionTime = DateTime.UtcNow;
                _consecutiveDownloads++;
                _totalDownloadsInSession++;
            }

            try
            {
                // First check if we're configured for natural behavior - early exit if disabled
                if (!settings.EnableNaturalBehavior && !settings.SimulateDelays && !settings.DownloadDelay)
                {
                    return;
                }

                // Determine the type of delay to apply based on context
                DelayType delayType = DelayType.Standard;

                if (context != null)
                {
                    // Use context-provided delay type if available
                    delayType = context.DelayType;

                    // Track context for future natural ordering
                    UpdateContextTracking(context);
                }

                // Apply appropriate delay based on type
                double delaySeconds = 0;
                string delayName = "standard";

                // Check if we're in high volume mode
                if (_isHighVolumeMode && settings.EnableHighVolumeHandling)
                {
                    delaySeconds = GenerateDelay(settings, DelayType.HighVolumeDelay);
                    delayName = "high volume";
                    _logger?.DebugWithEmoji(LogEmojis.Performance, $"High volume mode active: Adding {delaySeconds:F1}s delay between tracks");
                }
                else
                {
                    // Apply context-specific delay
                    switch (delayType)
                    {
                        case DelayType.TrackToTrack:
                            if (settings.SimulateListeningPatterns)
                            {
                                delaySeconds = GenerateDelay(settings, DelayType.TrackToTrack);
                                delayName = "track-to-track";
                            }
                            break;

                        case DelayType.AlbumToAlbum:
                            if (settings.SimulateListeningPatterns)
                            {
                                delaySeconds = GenerateDelay(settings, DelayType.AlbumToAlbum);
                                delayName = "album-to-album";
                            }
                            break;

                        case DelayType.ArtistToArtist:
                            if (settings.SimulateListeningPatterns)
                            {
                                delaySeconds = GenerateDelay(settings, DelayType.ArtistToArtist);
                                delayName = "artist-to-artist";
                            }
                            break;

                        default:
                            // Standard delay - use the most appropriate setting
                            if (settings.DownloadDelay)
                            {
                                // Legacy delay option
                                float min = Math.Max(0.1f, settings.DownloadDelayMin);
                                float max = Math.Max(min, settings.DownloadDelayMax);
                                delaySeconds = min + ((float)_random.NextDouble() * (max - min));
                                delayName = "legacy";
                            }
                            else if (settings.SimulateDelays)
                            {
                                // MS-based delay settings in seconds
                                delaySeconds = GenerateDelay(settings, DelayType.Standard);
                                delayName = "simulated";
                            }
                            break;
                    }
                }

                // Ensure we have a reasonable delay (clamp values)
                delaySeconds = Math.Max(0.1, Math.Min(delaySeconds, 300)); // Between 0.1s and 5 minutes

                // Apply the delay if needed
                if (delaySeconds > 0 && !cancellation.IsCancellationRequested)
                {
                    // Only log if delay is significant
                    if (delaySeconds >= 0.5)
                    {
                        _logger?.Debug($"⏱️ Applying {delayName} delay of {delaySeconds:F1} seconds between downloads");
                    }

                    // Convert to milliseconds and wait
                    int delayMs = (int)(delaySeconds * 1000);
                    await Task.Delay(delayMs, cancellation);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
                _logger?.Debug("Delay was cancelled");
            }
            catch (Exception ex)
            {
                // Log any unexpected errors but don't block the download
                _logger?.Debug(ex, "Error applying natural behavior delay");
            }
        }

        /// <summary>
        /// Calculate how many minutes until the next active hour period begins
        /// </summary>
        private int GetMinutesToActiveHours(TidalSettings settings)
        {
            DateTime now = DateTime.Now;
            int currentHour = now.Hour;
            int currentMinute = now.Minute;

            // If already in active hours, return 0
            if (IsWithinActiveHours(settings))
                return 0;

            // Case where active hours span midnight (e.g., ActiveHoursStart=22, ActiveHoursEnd=6)
            if (settings.ActiveHoursStart > settings.ActiveHoursEnd)
            {
                // If current time is before end time, active hours start today at ActiveHoursStart
                if (currentHour < settings.ActiveHoursEnd)
                {
                    // Calculate minutes from now until ActiveHoursStart today
                    return (settings.ActiveHoursStart - currentHour) * 60 - currentMinute;
                }
                else
                {
                    // Calculate minutes from now until ActiveHoursStart tomorrow
                    return ((24 - currentHour) + settings.ActiveHoursStart) * 60 - currentMinute;
                }
            }
            else
            {
                // Standard case (e.g., ActiveHoursStart=8, ActiveHoursEnd=22)
                if (currentHour < settings.ActiveHoursStart)
                {
                    // Calculate minutes until ActiveHoursStart today
                    return (settings.ActiveHoursStart - currentHour) * 60 - currentMinute;
                }
                else
                {
                    // Calculate minutes until ActiveHoursStart tomorrow
                    return ((24 - currentHour) + settings.ActiveHoursStart) * 60 - currentMinute;
                }
            }
        }

        /// <summary>
        /// Get the DateTime when the next active hour period begins
        /// </summary>
        private DateTime GetNextActiveHourStart(TidalSettings settings)
        {
            DateTime now = DateTime.Now;
            int currentHour = now.Hour;

            // If already in active hours, return current time
            if (IsWithinActiveHours(settings))
                return now;

            // Create a starting point at today's active start hour
            DateTime activeStartToday = now.Date.AddHours(settings.ActiveHoursStart);

            // Case where active hours span midnight (e.g., ActiveHoursStart=22, ActiveHoursEnd=6)
            if (settings.ActiveHoursStart > settings.ActiveHoursEnd)
            {
                // If we're after today's start time, it starts tomorrow
                if (currentHour >= settings.ActiveHoursStart)
                {
                    return activeStartToday.AddDays(1);
                }
                // If we're before today's end time, it starts today
                else if (currentHour < settings.ActiveHoursEnd)
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
                // Standard case (e.g., ActiveHoursStart=8, ActiveHoursEnd=22)
                // If we're before today's start time, it starts today
                if (currentHour < settings.ActiveHoursStart)
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

            // Updated to work directly with float values instead of casting to int
            float probability = Math.Clamp(settings.SkipProbability, 0.0f, 100.0f);
            return _random.NextDouble() * 100 < probability;
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
            // Update high volume session and break durations from settings
            _highVolumeSessionDuration = TimeSpan.FromMinutes(settings.HighVolumeSessionMinutes);
            _highVolumeBreakDuration = TimeSpan.FromMinutes(settings.HighVolumeBreakMinutes);

            if (itemCount > settings.HighVolumeThreshold && settings.EnableHighVolumeHandling)
            {
                if (!_isHighVolumeMode)
                {
                    _isHighVolumeMode = true;
                    _logger.Debug($"Entering high volume mode due to {itemCount} items in the queue");
                }
            }
            else
            {
                if (_isHighVolumeMode)
                {
                    _isHighVolumeMode = false;
                    _logger.Debug("Exiting high volume mode");
                }
            }
        }

        /// <summary>
        /// Logs current session statistics
        /// </summary>
        public void LogSessionStats()
        {
            var stats = GetSessionStats();

            TimeSpan sessionDuration = DateTime.UtcNow - _sessionStart;
            string durationStr = sessionDuration.TotalHours >= 1
                ? $"{(int)sessionDuration.TotalHours}h {sessionDuration.Minutes}m"
                : $"{sessionDuration.Minutes}m {sessionDuration.Seconds}s";

            _logger.InfoWithEmoji(LogEmojis.Track, $"SESSION STATS: {stats.TotalDownloads} downloads, {stats.CompletedSessions} sessions");
            _logger.Info($"   {LogEmojis.Time} Duration: {durationStr} | {LogEmojis.Retry} Consecutive: {stats.ConsecutiveDownloads} | {LogEmojis.Skip} Skipped: {stats.SkippedTracks}");
        }

        /// <summary>
        /// Returns session statistics as a structured object
        /// </summary>
        public SessionStatistics GetSessionStats()
        {
            return new SessionStatistics
            {
                TotalDownloads = _totalDownloadsInSession,
                CompletedSessions = _totalSessionsCompleted,
                ConsecutiveDownloads = _consecutiveDownloads,
                SkippedTracks = _skippedTracks,
                SessionStart = _sessionStart,
                IsHighVolumeMode = _isHighVolumeMode
            };
        }

        // Public statistics for the download viewer
        public int SessionsCompleted => _totalSessionsCompleted;
        public bool IsHighVolumeMode => _isHighVolumeMode;
        public int DownloadsInCurrentSession => _totalDownloadsInSession;
        public int TotalDownloads => _totalDownloadsInSession;
        public TimeSpan CurrentSessionDuration => DateTime.UtcNow - _sessionStart;

        /// <summary>
        /// Gets the break duration based on settings and mode
        /// </summary>
        private TimeSpan GetBreakDuration(TidalSettings settings)
        {
            if (_isHighVolumeMode && settings.EnableHighVolumeHandling)
            {
                return TimeSpan.FromMinutes(settings.HighVolumeBreakMinutes);
            }

            return TimeSpan.FromMinutes(settings.BreakDurationMinutes);
        }
    }

    /// <summary>
    /// Contains session statistics for the behavior simulator
    /// </summary>
    public class SessionStatistics
    {
        /// <summary>
        /// Total downloads in the current session
        /// </summary>
        public int TotalDownloads { get; set; }

        /// <summary>
        /// Number of completed sessions
        /// </summary>
        public int CompletedSessions { get; set; }

        /// <summary>
        /// Number of consecutive downloads in the current session
        /// </summary>
        public int ConsecutiveDownloads { get; set; }

        /// <summary>
        /// Number of tracks skipped in the current session
        /// </summary>
        public int SkippedTracks { get; set; }

        /// <summary>
        /// Time when the current session started
        /// </summary>
        public DateTime SessionStart { get; set; }

        /// <summary>
        /// Whether high volume mode is currently active
        /// </summary>
        public bool IsHighVolumeMode { get; set; }
    }
}


