using NLog;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.Download.Clients.Tidal;
using NzbDrone.Plugin.Tidal;
using Lidarr.Plugin.Tidal.Services.FileSystem;
using Lidarr.Plugin.Tidal.Services.Logging;

namespace Lidarr.Plugin.Tidal.Services.Lyrics
{
    /// <summary>
    /// Implementation of the ILyricsService interface for handling lyrics operations.
    /// </summary>
    public class LyricsService : ILyricsService
    {
        private readonly IFileSystemService _fileSystemService;
        private static readonly NLog.Logger _logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Initializes a new instance of the LyricsService class.
        /// </summary>
        /// <param name="fileSystemService">File system service for file operations</param>
        public LyricsService(IFileSystemService fileSystemService)
        {
            _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        }

        /// <summary>
        /// Default constructor that creates a default file system service.
        /// </summary>
        public LyricsService() : this(new FileSystemService())
        {
        }

        /// <summary>
        /// Fetches lyrics from Tidal and backup providers if configured.
        /// </summary>
        /// <param name="trackId">The Tidal track ID</param>
        /// <param name="trackTitle">The track title</param>
        /// <param name="artistName">The artist name</param>
        /// <param name="albumTitle">The album title</param>
        /// <param name="duration">Track duration in seconds</param>
        /// <param name="settings">Tidal settings</param>
        /// <param name="logger">Logger instance</param>
        /// <param name="cancellation">Cancellation token</param>
        /// <returns>A tuple containing plain lyrics text and synchronized lyrics text (or null if not available)</returns>
        public async Task<(string plainLyrics, string syncLyrics)> FetchLyrics(
            string trackId,
            string trackTitle,
            string artistName,
            string albumTitle,
            int duration,
            TidalSettings settings,
            Logger logger,
            CancellationToken cancellation)
        {
            string plainLyrics = string.Empty;
            string syncLyrics = string.Empty;

            // Don't proceed if lyrics features are disabled
            if (!settings.IncludeLyrics && !settings.SaveSyncedLyrics)
            {
                logger?.Debug($"[TIDAL] {LogEmojis.Info} Lyrics features are disabled in settings");
                return (plainLyrics, syncLyrics);
            }

            try
            {
                // Get downloader reference for API access
                var downloader = NzbDrone.Plugin.Tidal.TidalAPI.Instance?.Client?.Downloader;
                if (downloader == null)
                {
                    logger?.Warn($"[TIDAL] {LogEmojis.Warning} Downloader not available for lyrics fetching");
                    return (plainLyrics, syncLyrics);
                }

                // First try to get lyrics from Tidal
                logger?.Debug($"[TIDAL] {LogEmojis.Search} Fetching lyrics from Tidal for \"{trackTitle}\" by {artistName}");
                var lyrics = await downloader.FetchLyricsFromTidal(trackId, cancellation);
                if (lyrics.HasValue)
                {
                    plainLyrics = lyrics.Value.plainLyrics ?? string.Empty;
                    
                    if (settings.SaveSyncedLyrics)
                        syncLyrics = lyrics.Value.syncLyrics ?? string.Empty;

                    logger?.Debug($"[TIDAL] {LogEmojis.Music} Lyrics found on Tidal for \"{trackTitle}\" by {artistName}");
                }
                else
                {
                    logger?.Debug($"[TIDAL] {LogEmojis.Info} No lyrics found on Tidal for \"{trackTitle}\" by {artistName}");
                }

                // If lyrics are still missing and backup provider is enabled, try that
                if (settings.UseLRCLIB && (
                    (string.IsNullOrWhiteSpace(plainLyrics) && settings.IncludeLyrics) || 
                    (settings.SaveSyncedLyrics && string.IsNullOrWhiteSpace(syncLyrics))))
                {
                    // Only proceed if the backup provider is set to LRCLIB
                    if (settings.BackupLyricProvider == (int)LyricProviderSource.LRCLIB)
                    {
                        logger?.Debug($"[TIDAL] {LogEmojis.Search} Searching backup lyrics provider for \"{trackTitle}\" by {artistName}");
                        
                        // Make sure the provider URL is set
                        string providerUrl = !string.IsNullOrWhiteSpace(settings.LyricProviderUrl) 
                            ? settings.LyricProviderUrl 
                            : "lrclib.net";
                        
                        // Use the configured URL with LRCLIB
                        lyrics = await downloader.FetchLyricsFromLRCLIB(
                            providerUrl, 
                            trackTitle, 
                            artistName, 
                            albumTitle, 
                            duration, 
                            cancellation);
                        
                        if (lyrics.HasValue)
                        {
                            if (settings.IncludeLyrics && string.IsNullOrWhiteSpace(plainLyrics) && 
                                !string.IsNullOrWhiteSpace(lyrics.Value.plainLyrics))
                            {
                                plainLyrics = lyrics.Value.plainLyrics;
                                logger?.Debug($"[TIDAL] {LogEmojis.Music} Plain lyrics found from backup provider");
                            }
                            
                            if (settings.SaveSyncedLyrics && string.IsNullOrWhiteSpace(syncLyrics) && 
                                !string.IsNullOrWhiteSpace(lyrics.Value.syncLyrics))
                            {
                                syncLyrics = lyrics.Value.syncLyrics;
                                logger?.Debug($"[TIDAL] {LogEmojis.Music} Synced lyrics found from backup provider");
                            }
                        }
                        else
                        {
                            logger?.Debug($"[TIDAL] {LogEmojis.Info} No lyrics found from backup provider");
                        }
                    }
                    else
                    {
                        logger?.Debug($"[TIDAL] {LogEmojis.Info} Backup lyrics provider is not set to LRCLIB, skipping backup fetch");
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the error but continue with the download
                logger?.Warn($"[TIDAL] {LogEmojis.Warning} Error fetching lyrics for \"{trackTitle}\" by {artistName}: {ex.Message}");
            }

            return (plainLyrics, syncLyrics);
        }

        /// <summary>
        /// Creates an LRC file for synchronized lyrics and returns the file path.
        /// </summary>
        /// <param name="baseAudioFilePath">The audio file path (without extension)</param>
        /// <param name="syncLyrics">The synchronized lyrics content</param>
        /// <param name="logger">Logger instance</param>
        /// <param name="cancellation">Cancellation token</param>
        /// <returns>Path to the created LRC file, or null if creation failed</returns>
        public async Task<string> CreateLrcFile(
            string baseAudioFilePath,
            string syncLyrics,
            Logger logger,
            CancellationToken cancellation)
        {
            // Ensure the syncLyrics are not null or empty
            if (string.IsNullOrEmpty(syncLyrics))
            {
                logger?.Debug($"[TIDAL] {LogEmojis.Info} No synced lyrics provided, skipping LRC file creation");
                return null;
            }

            // Generate LRC file path
            string lrcFilePath = Path.ChangeExtension(baseAudioFilePath, "lrc");
            string directory = Path.GetDirectoryName(lrcFilePath);
            
            logger?.Debug($"[TIDAL] {LogEmojis.Debug} Attempting to create LRC file at: {lrcFilePath}");
            logger?.Debug($"[TIDAL] {LogEmojis.Debug} Based on audio file: {baseAudioFilePath}");
            logger?.Debug($"[TIDAL] {LogEmojis.Debug} Directory exists: {Directory.Exists(directory)}, Path exists: {File.Exists(baseAudioFilePath)}");

            try
            {
                cancellation.ThrowIfCancellationRequested();

                // Ensure the directory exists
                if (!_fileSystemService.EnsureDirectoryExists(directory, 3, logger))
                {
                    logger?.Warn($"[TIDAL] {LogEmojis.Warning} Failed to create/verify directory for LRC file: {directory}");
                    return null;
                }

                // Write the LRC file with retry logic
                const int maxRetries = 3;
                int attempt = 0;
                bool success = false;

                while (!success && attempt < maxRetries)
                {
                    try
                    {
                        attempt++;
                        await File.WriteAllTextAsync(lrcFilePath, syncLyrics, cancellation);
                        success = true;
                        logger?.Debug($"[TIDAL] {LogEmojis.Music} Created LRC file: {lrcFilePath}");
                    }
                    catch (Exception ex) when (attempt < maxRetries && 
                              !(ex is OperationCanceledException || ex is TaskCanceledException))
                    {
                        logger?.Warn($"[TIDAL] {LogEmojis.Warning} Attempt {attempt}/{maxRetries} to create LRC file failed: {ex.Message}");
                        await Task.Delay(500 * attempt, cancellation); // Increasing delay between retries
                    }
                }

                return success ? lrcFilePath : null;
            }
            catch (OperationCanceledException)
            {
                logger?.Debug($"[TIDAL] {LogEmojis.Cancel} LRC file creation cancelled");
                throw; // Re-throw cancellation exceptions
            }
            catch (Exception ex)
            {
                logger?.Warn($"[TIDAL] {LogEmojis.Error} Failed to create LRC file: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Processes lyrics for a track - gets lyrics and creates LRC file if enabled.
        /// </summary>
        /// <param name="trackId">The Tidal track ID</param>
        /// <param name="trackFilePath">The track file path</param>
        /// <param name="trackTitle">The track title</param>
        /// <param name="artistName">The artist name</param>
        /// <param name="albumTitle">The album title</param>
        /// <param name="duration">Track duration in seconds</param>
        /// <param name="settings">Tidal settings</param>
        /// <param name="logger">Logger instance</param>
        /// <param name="cancellation">Cancellation token</param>
        /// <returns>A tuple containing plain lyrics text and path to LRC file (or null if not created)</returns>
        public async Task<(string plainLyrics, string lrcFilePath)> ProcessTrackLyrics(
            string trackId,
            string trackFilePath,
            string trackTitle,
            string artistName,
            string albumTitle,
            int duration,
            TidalSettings settings,
            Logger logger,
            CancellationToken cancellation)
        {
            string lrcFilePath = null;

            // Check if lyric features are enabled
            if (!settings.IncludeLyrics && !settings.SaveSyncedLyrics)
            {
                logger?.Debug($"[TIDAL] {LogEmojis.Info} Lyrics features are disabled, skipping lyrics processing for: {trackTitle}");
                return (string.Empty, null);
            }

            try
            {
                logger?.Debug($"[TIDAL] {LogEmojis.Music} Processing lyrics for: {trackTitle} by {artistName}");

                // Fetch lyrics from Tidal and backup provider if configured
                var (plainLyrics, syncLyrics) = await FetchLyrics(
                    trackId,
                    trackTitle,
                    artistName,
                    albumTitle,
                    duration,
                    settings,
                    logger,
                    cancellation);

                // Create LRC file if synced lyrics are available and enabled in settings
                if (settings.SaveSyncedLyrics && !string.IsNullOrWhiteSpace(syncLyrics))
                {
                    logger?.Debug($"[TIDAL] {LogEmojis.Music} Creating LRC file for: {trackTitle}");
                    
                    lrcFilePath = await CreateLrcFile(
                        trackFilePath,
                        syncLyrics,
                        logger,
                        cancellation);

                    if (!string.IsNullOrEmpty(lrcFilePath))
                    {
                        logger?.Debug($"[TIDAL] {LogEmojis.Music} Created LRC file at: {lrcFilePath}");
                    }
                    else
                    {
                        logger?.Warn($"[TIDAL] {LogEmojis.Warning} Failed to create LRC file for: {trackTitle}");
                    }
                }
                else if (settings.SaveSyncedLyrics)
                {
                    logger?.Debug($"[TIDAL] {LogEmojis.Info} No synced lyrics available for LRC file: {trackTitle}");
                }

                return (plainLyrics, lrcFilePath);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException || ex is TaskCanceledException))
            {
                logger?.Error(ex, $"[TIDAL] {LogEmojis.Error} Error processing lyrics for: {trackTitle}");
                return (string.Empty, null);
            }
        }
    }
} 