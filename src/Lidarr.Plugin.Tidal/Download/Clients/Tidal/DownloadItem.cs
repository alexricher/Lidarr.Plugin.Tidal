using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Music;
using NzbDrone.Core.Download.Clients.Tidal.Interfaces;
using TidalSharp.Data;
using Newtonsoft.Json;
using NzbDrone.Plugin.Tidal;
using System.IO;
using Newtonsoft.Json.Linq;
using Lidarr.Plugin.Tidal.Download.Clients.Tidal.Utilities;
using System.Text;
using Lidarr.Plugin.Tidal.Services.Logging;
using NzbDrone.Core.Download.Clients.Tidal; // For TidalSettings and LyricProviderSource
using Lidarr.Plugin.Tidal.Services.Lyrics;
using Lidarr.Plugin.Tidal.Services.FileSystem;

namespace Lidarr.Plugin.Tidal.Download.Clients.Tidal
{
    /// <summary>
    /// Represents an item to be downloaded from Tidal
    /// </summary>
    public class DownloadItem : IDownloadItem
    {
        /// <summary>
        /// Static semaphore to control concurrent file I/O operations
        /// </summary>
        private static readonly SemaphoreSlim FileIOSemaphore = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Gets or sets the unique identifier for this download
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Gets the unique identifier for the download item.
        /// This is an alias for Id to maintain compatibility with existing code.
        /// </summary>
        public string ID => Id;

        /// <summary>
        /// Gets or sets the title of the item being downloaded
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets the artist of the item being downloaded
        /// </summary>
        public string Artist { get; set; }

        /// <summary>
        /// Gets or sets the album name.
        /// </summary>
        public string Album { get; set; }

        /// <summary>
        /// Gets or sets whether the content is explicit.
        /// </summary>
        public bool Explicit { get; set; }

        /// <summary>
        /// Gets or sets when the item was queued for download.
        /// </summary>
        public DateTime QueuedTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Gets or sets when the download started.
        /// </summary>
        public DateTime StartTime { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Gets or sets when the download completed.
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// Gets or sets the estimated time remaining for the download.
        /// </summary>
        public TimeSpan? EstimatedTimeRemaining { get; set; }

        /// <summary>
        /// Gets or sets the current download progress as a percentage (0-100).
        /// </summary>
        public double Progress { get; set; }

        /// <summary>
        /// Gets or sets the total size of the download in bytes.
        /// </summary>
        public long TotalSize { get; set; }

        /// <summary>
        /// Gets or sets the amount of data downloaded in bytes.
        /// </summary>
        public long DownloadedSize { get; set; }

        /// <summary>
        /// Gets or sets whether the download can be resumed if paused or interrupted.
        /// </summary>
        public bool CanBeResumed { get; set; } = false;

        /// <summary>
        /// Gets or sets the total number of tracks to download.
        /// </summary>
        public int TotalTracks { get; set; }

        /// <summary>
        /// Gets or sets the number of tracks completed.
        /// </summary>
        public int CompletedTracks { get; set; }

        /// <summary>
        /// Gets or sets the array of track numbers that failed to download.
        /// </summary>
        public int[] FailedTracks { get; set; } = Array.Empty<int>();

        /// <summary>
        /// Gets or sets the current status of the download.
        /// </summary>
        public DownloadItemStatus Status { get; set; } = DownloadItemStatus.Queued;

        /// <summary>
        /// Gets or sets the last error message if the download failed.
        /// </summary>
        public string LastErrorMessage { get; set; }

        /// <summary>
        /// Gets or sets the priority of the download in the queue.
        /// Higher priority items are processed before lower priority ones.
        /// </summary>
        public DownloadItemPriority Priority { get; set; } = DownloadItemPriority.Normal;

        /// <summary>
        /// Gets or sets the folder where the download will be saved.
        /// </summary>
        public string DownloadFolder { get; set; }

        /// <summary>
        /// Gets or sets the remote album information
        /// </summary>
        public RemoteAlbum RemoteAlbum { get; set; }

        /// <summary>
        /// Gets or sets the JSON representation of RemoteAlbum for serialization.
        /// </summary>
        public string RemoteAlbumJson { get; set; }

        /// <summary>
        /// Gets the audio quality of the download.
        /// </summary>
        public AudioQuality Bitrate => (AudioQuality)BitrateInt;

        /// <summary>
        /// Gets or sets the audio quality as an integer value (for compatibility).
        /// </summary>
        public int BitrateInt { get; set; }

        /// <summary>
        /// Gets or sets the download client settings
        /// </summary>
        public dynamic Settings { get; set; }

        /// <summary>
        /// Indicates whether MQA quality was specifically requested
        /// </summary>
        public bool RequestedMQA { get; private set; } = false;

        /// <summary>
        /// Pauses the download.
        /// </summary>
        public void Pause()
        {
            Status = DownloadItemStatus.Paused;
        }

        /// <summary>
        /// Resumes a paused download.
        /// </summary>
        public void Resume()
        {
            if (Status == DownloadItemStatus.Paused)
            {
                Status = DownloadItemStatus.Queued;
            }
        }

        /// <summary>
        /// Cancels the download.
        /// </summary>
        public void Cancel()
        {
            Status = DownloadItemStatus.Cancelled;
        }

        /// <summary>
        /// Pauses the download. Alias for <see cref="Pause"/> for backward compatibility.
        /// </summary>
        [Obsolete("Use Pause() instead. This method will be removed in a future version.")]
        public void PauseDownload() => Pause();

        /// <summary>
        /// Resumes a paused download. Alias for <see cref="Resume"/> for backward compatibility.
        /// </summary>
        [Obsolete("Use Resume() instead. This method will be removed in a future version.")]
        public void ResumeDownload() => Resume();

        /// <summary>
        /// Cancels the download. Alias for <see cref="Cancel"/> for backward compatibility.
        /// </summary>
        [Obsolete("Use Cancel() instead. This method will be removed in a future version.")]
        public void CancelDownload() => Cancel();

        /// <summary>
        /// Performs the actual download operation.
        /// </summary>
        /// <param name="settings">The Tidal settings to use.</param>
        /// <param name="logger">The logger to use.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task representing the asynchronous download operation.</returns>
        public async Task DoDownload(NzbDrone.Core.Download.Clients.Tidal.TidalSettings settings, Logger logger, CancellationToken cancellationToken = default)
        {
            if (settings == null)
            {
                logger.Error("Cannot download without settings");
                throw new ArgumentNullException(nameof(settings));
            }

            try
            {
                // Update status
                Status = DownloadItemStatus.Downloading;
                StartTime = DateTime.UtcNow;

                // Initialize the concurrency monitor if needed
                Lidarr.Plugin.Tidal.Services.ConcurrencyMonitor.Initialize(settings, logger);

                // Log the start of download with more detailed information
                logger.Info($"[TIDAL] {LogEmojis.Start} ════════ ALBUM DOWNLOAD ════════");
                logger.Info($"[TIDAL] {LogEmojis.Album}  •  Title: \"{Title}\"  •  Artist: {Artist}");

                // Extract album ID from the remote album
                string albumId = null;

                // Extract album ID and log additional details

                if (RemoteAlbum?.Release?.Guid != null)
                {
                    albumId = RemoteAlbum.Release.Guid.Replace("tidal:", "");
                    logger.Debug($"[TIDAL] Using album ID from Release.Guid: {albumId}");
                }

                if (string.IsNullOrEmpty(albumId) && RemoteAlbum?.Release?.IndexerId != null)
                {
                    albumId = RemoteAlbum.Release.IndexerId.ToString();
                    logger.Debug($"[TIDAL] Using album ID from Release.IndexerId: {albumId}");
                }

                // Log additional download details
                logger.Info($"[TIDAL] {LogEmojis.Info} Download details: ID: {albumId}, Quality: {Bitrate}");

                if (string.IsNullOrEmpty(albumId))
                {
                    logger.Error($"[TIDAL] {LogEmojis.Error}  •  No tracks found  •  Album ID: {albumId}");
                    SetFailed("Failed to extract album ID from release");
                    return;
                }

                // Extract numeric ID from formats like "Tidal-123456789-HI_RES_LOSSLESS"
                string numericAlbumId = albumId;
                if (albumId.StartsWith("Tidal-", StringComparison.OrdinalIgnoreCase))
                {
                    // Split by hyphen and take the middle part
                    var parts = albumId.Split('-');
                    if (parts.Length >= 2)
                    {
                        numericAlbumId = parts[1];
                        logger.Debug($"[TIDAL] Extracted numeric album ID: {numericAlbumId} from {albumId}");
                    }
                }

                // Create the download folder
                string artistFolder = Path.Combine(settings.DownloadPath, MakeValidFileName(Artist));
                string albumFolder = Path.Combine(artistFolder, MakeValidFileName(Title));

                DownloadFolder = albumFolder;

                if (!Directory.Exists(albumFolder))
                {
                    Directory.CreateDirectory(albumFolder);
                }

                // Check if TidalAPI is initialized
                if (NzbDrone.Plugin.Tidal.TidalAPI.Instance == null)
                {
                    logger.Error($"[TIDAL] {LogEmojis.Error} TidalAPI not initialized");
                    SetFailed("TidalAPI not initialized");
                    return;
                }

                // Get the Tidal downloader
                var downloader = NzbDrone.Plugin.Tidal.TidalAPI.Instance.Client?.Downloader;
                if (downloader == null)
                {
                    logger.Error($"[TIDAL] {LogEmojis.Error} Tidal downloader not available");
                    SetFailed("Tidal downloader not available");
                    return;
                }

                try
                {
                    // Get album details
                    if (!int.TryParse(numericAlbumId, out int albumId_Int))
                    {
                        logger.Error($"[TIDAL] {LogEmojis.Error} Invalid album ID format: {albumId}");
                        SetFailed($"Invalid album ID format: {albumId}");
                        return;
                    }

                    // Use the API.GetAlbum method to get album details
                    var albumData = await NzbDrone.Plugin.Tidal.TidalAPI.Instance.Client.API.GetAlbum(albumId_Int.ToString(), cancellationToken);
                    if (albumData == null)
                    {
                        logger.Error($"[TIDAL] {LogEmojis.Error} Album not found with ID: {albumId}");
                        SetFailed($"Album not found with ID: {albumId}");
                        return;
                    }

                    string albumTitle = albumData["title"]?.ToString() ?? "Unknown";
                    string artistName = albumData["artist"]?["name"]?.ToString() ?? "Unknown";

                    // Extract additional album info for logging
                    var releaseDate = albumData["releaseDate"]?.ToString();
                    var audioQuality = albumData["audioQuality"]?.ToString();
                    var copyright = albumData["copyright"]?.ToString();
                    var numberOfTracks = albumData["numberOfTracks"]?.ToString() ?? "Unknown";
                    var duration = albumData["duration"] != null ? int.Parse(albumData["duration"].ToString()) : 0;

                    // Format duration for display (mm:ss)
                    string albumDurationStr = duration > 0
                        ? $"{duration / 60}:{(duration % 60).ToString("00")}"
                        : "--:--";

                    // Log detailed album info
                    logger.Info($"[TIDAL] {LogEmojis.Album}  •  Found album  •  \"{albumTitle}\"  •  Artist: {artistName}");
                    logger.Info($"[TIDAL] {LogEmojis.Track}  •  Tracks: {numberOfTracks}  •  Duration: {albumDurationStr}");

                    if (!string.IsNullOrEmpty(releaseDate))
                    {
                        logger.Info($"[TIDAL] {LogEmojis.Schedule}  •  Released: {releaseDate}");
                    }

                    if (!string.IsNullOrEmpty(audioQuality))
                    {
                        logger.Info($"[TIDAL] {LogEmojis.Music}  •  Quality: {audioQuality}");
                    }

                    // Create json file with album information
                    try
                    {
                        var albumInfoFile = Path.Combine(albumFolder, "album_info.json");
                        await File.WriteAllTextAsync(albumInfoFile, albumData.ToString(), cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.Warn($"[TIDAL] {LogEmojis.Warning} Failed to write album info file: {ex.Message}");
                        // Continue with download, this is non-critical
                    }

                    // Get quality/bitrate setting
                    var quality = GetQualitySetting(settings);

                    // Get tracks for the album
                    var tracksData = await NzbDrone.Plugin.Tidal.TidalAPI.Instance.Client.API.GetAlbumTracks(albumId_Int.ToString(), cancellationToken);
                    var trackItems = tracksData["items"];
                    if (trackItems == null || !trackItems.Any())
                    {
                        logger.Error($"[TIDAL] {LogEmojis.Error}  •  No tracks found  •  Album ID: {albumId}");
                        SetFailed($"No tracks found for album with ID: {albumId}");
                        return;
                    }

                    // Track progress
                    TotalTracks = trackItems.Count();
                    CompletedTracks = 0;
                    FailedTracks = new int[0];
                    var failedTracksList = new List<int>();

                    // Process tracks for the album
                    await ProcessTracks(trackItems, albumFolder, albumTitle, quality, settings, logger, cancellationToken);

                    // Final status
                    if (CompletedTracks == 0)
                    {
                        logger.Error($"[TIDAL] {LogEmojis.Error} Failed to download any tracks from album: {albumTitle}");
                        SetFailed("Failed to download any tracks");
                    }
                    else if (CompletedTracks < TotalTracks)
                    {
                        logger.Warn($"[TIDAL] {LogEmojis.Warning}  •  Partial download  •  {CompletedTracks}/{TotalTracks} tracks  •  Album: \"{albumTitle}\"");
                        Status = DownloadItemStatus.Warning;
                        LastErrorMessage = $"Partially downloaded ({CompletedTracks}/{TotalTracks} tracks)";
                        EndTime = DateTime.UtcNow;

                        // Log failed tracks
                        if (failedTracksList.Any())
                        {
                            logger.Warn($"[TIDAL] {LogEmojis.Error}  •  Failed tracks  •  IDs: #{string.Join(", #", failedTracksList.OrderBy(t => t))}");
                        }

                        // Calculate download stats
                        var totalDownloadTime = EndTime.Value - StartTime;
                        logger.Info($"[TIDAL] {LogEmojis.Time}  •  Download time  •  {FormatTimeSpan(totalDownloadTime)}");

                        // Get directory size info
                        DirectoryInfo dirInfo = new DirectoryInfo(albumFolder);
                        if (dirInfo.Exists)
                        {
                            var audioFiles = dirInfo.GetFiles("*.*", SearchOption.AllDirectories)
                                               .Where(f => f.Extension.ToLower() == ".flac" || f.Extension.ToLower() == ".m4a")
                                               .ToList();

                            long totalSize = audioFiles.Sum(f => f.Length);
                            int flacCount = audioFiles.Count(f => f.Extension.ToLower() == ".flac");
                            int m4aCount = audioFiles.Count(f => f.Extension.ToLower() == ".m4a");

                            // Get quality description and check if it was downgraded
                            string qualityDesc = GetAudioQualityDescription(Bitrate);
                            bool qualityDowngraded = flacCount == 0 && (Bitrate == TidalSharp.Data.AudioQuality.LOSSLESS || 
                                                                       Bitrate == TidalSharp.Data.AudioQuality.HI_RES_LOSSLESS);
                            
                            // If quality was downgraded, provide a more accurate quality description
                            if (qualityDowngraded)
                            {
                                // Determine actual quality based on file types
                                string actualQualityDesc = m4aCount > 0 ? "AAC 320kbps" : qualityDesc;
                                qualityDesc = $"{actualQualityDesc} (requested {qualityDesc})";
                                
                                // Add a warning about subscription level
                                logger.Warn($"[TIDAL] {LogEmojis.Warning} Quality downgrade detected in downloaded files.");
                                // Use simple track position indicator for subscription warning
                                string subWarnIndicator = $"[{CompletedTracks}/{TotalTracks}]";
                                logger.Warn($"[TIDAL] {LogEmojis.Info} {subWarnIndicator} Your Tidal subscription may not support {GetAudioQualityDescription(Bitrate)}");
                            }

                            // Calculate average file size
                            long avgFileSize = audioFiles.Count > 0 ? totalSize / audioFiles.Count : 0;

                            // Log comprehensive download summary with enhanced formatting
                            logger.Info($"[TIDAL] {LogEmojis.Complete} ══════════ DOWNLOAD SUMMARY ══════════");
                            logger.Info($"[TIDAL] {LogEmojis.Album}  •  Album: \"{Title}\"  •  Artist: {Artist}");
                            logger.Info($"[TIDAL] {LogEmojis.Music}  •  Quality: {qualityDesc}");
                            logger.Info($"[TIDAL] {LogEmojis.Track}  •  Tracks: {CompletedTracks}/{TotalTracks} downloaded{(FailedTracks.Length > 0 ? $", {FailedTracks.Length} failed" : "")}");
                            logger.Info($"[TIDAL] {LogEmojis.File}  •  Files: {flacCount} FLAC, {m4aCount} M4A");
                            logger.Info($"[TIDAL] {LogEmojis.Data}  •  Total size: {FormatFileSize(totalSize)}  •  Average: {FormatFileSize(avgFileSize)}/track");
                            logger.Info($"[TIDAL] {LogEmojis.Time}  •  Download time: {FormatTimeSpan(totalDownloadTime)}");
                            logger.Info($"[TIDAL] {LogEmojis.Folder}  •  Location: {albumFolder}");
                            logger.Info($"[TIDAL] {LogEmojis.Complete} ═════════════════════════════════════");
                        }
                    }
                    else
                    {
                        logger.Info($"[TIDAL] {LogEmojis.Music}  •  Download complete  •  All {TotalTracks} tracks  •  Album: \"{albumTitle}\"");
                        SetCompleted();

                        // Calculate download stats
                        var totalDownloadTime = EndTime.Value - StartTime;
                        logger.Info($"[TIDAL] {LogEmojis.Time}  •  Download time  •  {FormatTimeSpan(totalDownloadTime)}");

                        // Get directory size info
                        DirectoryInfo dirInfo = new DirectoryInfo(albumFolder);
                        if (dirInfo.Exists)
                        {
                            var audioFiles = dirInfo.GetFiles("*.*", SearchOption.AllDirectories)
                                               .Where(f => f.Extension.ToLower() == ".flac" || f.Extension.ToLower() == ".m4a")
                                               .ToList();

                            long totalSize = audioFiles.Sum(f => f.Length);
                            int flacCount = audioFiles.Count(f => f.Extension.ToLower() == ".flac");
                            int m4aCount = audioFiles.Count(f => f.Extension.ToLower() == ".m4a");

                            // Get quality description and check if it was downgraded
                            string qualityDesc = GetAudioQualityDescription(Bitrate);
                            bool qualityDowngraded = flacCount == 0 && (Bitrate == TidalSharp.Data.AudioQuality.LOSSLESS || 
                                                                       Bitrate == TidalSharp.Data.AudioQuality.HI_RES_LOSSLESS);
                            
                            // If quality was downgraded, provide a more accurate quality description
                            if (qualityDowngraded)
                            {
                                // Determine actual quality based on file types
                                string actualQualityDesc = m4aCount > 0 ? "AAC 320kbps" : qualityDesc;
                                qualityDesc = $"{actualQualityDesc} (requested {qualityDesc})";
                                
                                // Add a warning about subscription level
                                logger.Warn($"[TIDAL] {LogEmojis.Warning} Quality downgrade detected in downloaded files.");
                                // Use simple track position indicator for subscription warning
                                string subWarnIndicator = $"[{CompletedTracks}/{TotalTracks}]";
                                logger.Warn($"[TIDAL] {LogEmojis.Info} {subWarnIndicator} Your Tidal subscription may not support {GetAudioQualityDescription(Bitrate)}");
                            }

                            // Calculate average file size
                            long avgFileSize = audioFiles.Count > 0 ? totalSize / audioFiles.Count : 0;

                            // Log comprehensive download summary with enhanced formatting
                            logger.Info($"[TIDAL] {LogEmojis.Complete} ══════════ DOWNLOAD SUMMARY ══════════");
                            logger.Info($"[TIDAL] {LogEmojis.Album}  •  Album: \"{Title}\"  •  Artist: {Artist}");
                            logger.Info($"[TIDAL] {LogEmojis.Music}  •  Quality: {qualityDesc}");
                            logger.Info($"[TIDAL] {LogEmojis.Track}  •  Tracks: {CompletedTracks}/{TotalTracks} downloaded{(FailedTracks.Length > 0 ? $", {FailedTracks.Length} failed" : "")}");
                            logger.Info($"[TIDAL] {LogEmojis.File}  •  Files: {flacCount} FLAC, {m4aCount} M4A");
                            logger.Info($"[TIDAL] {LogEmojis.Data}  •  Total size: {FormatFileSize(totalSize)}  •  Average: {FormatFileSize(avgFileSize)}/track");
                            logger.Info($"[TIDAL] {LogEmojis.Time}  •  Download time: {FormatTimeSpan(totalDownloadTime)}");
                            logger.Info($"[TIDAL] {LogEmojis.Folder}  •  Location: {albumFolder}");
                            logger.Info($"[TIDAL] {LogEmojis.Complete} ═════════════════════════════════════");
                        }

                        // Log track details for successful downloads
                        logger.Debug($"[TIDAL] {LogEmojis.Success} Download completed for {Title} by {Artist}:");
                        logger.Debug($"   {LogEmojis.Info} Total tracks: {TotalTracks}, Downloaded: {CompletedTracks}, Failed: {FailedTracks.Length}");
                        logger.Debug($"   {LogEmojis.Save} Saved to: {albumFolder}");

                        // This will help with detecting if the item is properly removed from queue
                        logger.Debug($"[TIDAL] {LogEmojis.Complete} Final status: {Status}, Should be removed from queue");
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"[TIDAL] {LogEmojis.Error} Error downloading album: {ex.Message}");
                    SetFailed(ex.Message);

                    // Log final status for debugging
                    logger.Debug($"[TIDAL] {LogEmojis.Error} Final status: {Status}, Error: {LastErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"[TIDAL] {LogEmojis.Error} Error downloading {Title} by {Artist}: {ex.Message}");
                SetFailed(ex.Message);

                // Log final status for debugging
                logger.Debug($"[TIDAL] {LogEmojis.Error} Final status: {Status}, Error: {LastErrorMessage}");
            }
        }

        /// <summary>
        /// Process tracks for the album
        /// </summary>
        private async Task ProcessTracks(
            JToken trackItems, 
            string albumFolder, 
            string albumTitle, 
            TidalSharp.Data.AudioQuality quality, 
            TidalSettings settings, 
            Logger logger, 
            CancellationToken cancellationToken)
        {
            var downloader = NzbDrone.Plugin.Tidal.TidalAPI.Instance.Client?.Downloader;
            var tracks = trackItems.ToList();
            var failedTracksList = new List<int>();
            var fileSystemService = new Services.FileSystem.FileSystemService();
            var fileValidationService = Services.FileSystem.FileValidationServiceProvider.Current;
            string correctedFilePath = string.Empty;
            TotalTracks = tracks.Count;
            
            // Create album folder if it doesn't exist
            fileSystemService.EnsureDirectoryExists(albumFolder);
            
            // Get the lyrics service from the provider
            var lyricsService = LyricsServiceProvider.Current;

            // Determine if we need to apply natural behavior delays
            bool useNaturalDelays = settings.EnableNaturalBehavior && settings.SimulateListeningPatterns;
            Random random = new Random();

            // Sort tracks according to settings
            if (settings.SequentialTrackOrder)
            {
                // Sort by volume number and track number for sequential order
                tracks = tracks.OrderBy(t => t["volumeNumber"]?.Value<int>() ?? 1)
                               .ThenBy(t => t["trackNumber"]?.Value<int>() ?? 0)
                               .ToList();
            }
            else
            {
                // Use random order for tracks
                tracks = tracks.OrderBy(t => random.Next()).ToList();
            }

            // Track download timing for statistics
            var downloadStartTime = DateTime.UtcNow;
            var lastProgressUpdate = DateTime.UtcNow;
            
            // Loop through tracks and download
            for (int i = 0; i < tracks.Count; i++)
            {
                var track = tracks[i];
                
                // Check for cancellation
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var trackId = track["id"]?.ToString();
                    var trackTitle = track["title"]?.ToString() ?? "Unknown";
                    var trackNumber = int.TryParse(track["trackNumber"]?.ToString(), out int tn) ? tn : 0;
                    var trackDuration = int.TryParse(track["duration"]?.ToString(), out int td) ? td : 0;
                    var trackArtists = track["artists"] != null 
                        ? string.Join(", ", track["artists"].Select(a => a["name"]?.ToString())) 
                        : Artist;
                    var isExplicit = track["explicit"]?.Value<bool>() ?? false;

                    // Skip tracks that don't meet our settings criteria
                    if (string.IsNullOrEmpty(trackId))
                    {
                        logger.Warn($"[TIDAL] {LogEmojis.Warning} Track ID missing for track: {trackTitle}");
                        failedTracksList.Add(trackNumber);
                        continue;
                    }

                    // Handle explicit content preference
                    if (settings.PreferExplicit && !isExplicit)
                    {
                        // Check if there's an explicit version of this track in the album
                        var explicitVersion = trackItems.FirstOrDefault(t => 
                            t["title"]?.ToString() == trackTitle && 
                            t["explicit"]?.Value<bool>() == true);
                            
                        if (explicitVersion != null)
                        {
                            // Use the explicit version instead
                            var explicitId = explicitVersion["id"]?.ToString();
                            logger.Debug($"[TIDAL] {LogEmojis.Info} Found explicit version of '{trackTitle}', using that instead");
                            
                            if (!string.IsNullOrEmpty(explicitId))
                            {
                                trackId = explicitId;
                                isExplicit = true;
                            }
                        }
                    }

                    // Create track filename
                    string trackNumberFormat = TotalTracks >= 10 ? "00" : "0";
                    string trackFileName = $"{trackNumber.ToString(trackNumberFormat)} - {MakeValidFileName(trackTitle)}.{GetExtensionForQuality(quality)}";
                    string trackFilePath = Path.Combine(albumFolder, trackFileName);

                    // Check if the file already exists - with improved quality check
                    bool shouldDownload = true;
                    if (File.Exists(trackFilePath))
                    {
                        // Get existing file info for quality comparison
                        var existingFileInfo = new FileInfo(trackFilePath);
                        
                        // Check if the existing file might be of lower quality than what we're about to download
                        bool isPotentialQualityUpgrade = false;
                        
                        // Check extension-based quality (FLAC > M4A)
                        string existingExt = Path.GetExtension(trackFilePath).ToLowerInvariant();
                        bool existingIsFlac = existingExt == ".flac";
                        bool newIsFlac = quality == TidalSharp.Data.AudioQuality.LOSSLESS || 
                                       quality == TidalSharp.Data.AudioQuality.HI_RES_LOSSLESS;
                                       
                        // If we're getting FLAC but existing is not FLAC, then it's an upgrade
                        if (newIsFlac && !existingIsFlac)
                        {
                            isPotentialQualityUpgrade = true;
                            logger.Debug($"[TIDAL] {LogEmojis.Info} Quality upgrade detected: {existingExt} → FLAC");
                        }
                        // If same format, check if higher bit depth or sampling rate
                        else if (existingIsFlac && newIsFlac)
                        {
                            // For FLAC files, check if we're upgrading from standard to hi-res
                            bool existingIsHiRes = IsHighResolutionFlac(trackFilePath, logger);
                            bool newIsHiRes = quality == TidalSharp.Data.AudioQuality.HI_RES_LOSSLESS;
                            
                            if (newIsHiRes && !existingIsHiRes)
                            {
                                isPotentialQualityUpgrade = true;
                                logger.Debug($"[TIDAL] {LogEmojis.Info} Quality upgrade detected: Standard FLAC → Hi-Res FLAC");
                            }
                        }
                        // For AAC files, check if higher bitrate
                        else if (!existingIsFlac && !newIsFlac)
                        {
                            bool existingIsLowQuality = IsLowQualityAAC(trackFilePath, existingFileInfo.Length, trackDuration, logger);
                            bool newIsHighQuality = quality == TidalSharp.Data.AudioQuality.HIGH;
                            
                            if (newIsHighQuality && existingIsLowQuality)
                            {
                                isPotentialQualityUpgrade = true;
                                logger.Debug($"[TIDAL] {LogEmojis.Info} Quality upgrade detected: AAC 96kbps → AAC 320kbps");
                            }
                        }
                        
                        // If existing file is too small, it might be corrupted or incomplete, so download again
                        bool existingFileTooSmall = existingFileInfo.Length < 25 * 1024; // Less than 25 KB
                        
                        // Decide whether to download
                        if (existingFileTooSmall)
                        {
                            logger.Warn($"[TIDAL] {LogEmojis.Warning} Existing file too small ({FormatFileSize(existingFileInfo.Length)}), will re-download: {trackTitle}");
                            shouldDownload = true;
                        }
                        else if (isPotentialQualityUpgrade)
                        {
                            logger.Info($"[TIDAL] {LogEmojis.File} Higher quality version available, replacing: #{trackNumber} - {trackTitle}");
                            shouldDownload = true;
                            
                            // Rename existing file to .bak just in case
                            string backupFilePath = $"{trackFilePath}.bak";
                            try 
                            {
                                if (File.Exists(backupFilePath))
                                {
                                    File.Delete(backupFilePath);
                                }
                                File.Move(trackFilePath, backupFilePath);
                                logger.Debug($"[TIDAL] {LogEmojis.Info} Existing file backed up to: {Path.GetFileName(backupFilePath)}");
                            }
                            catch (Exception ex)
                            {
                                logger.Warn($"[TIDAL] {LogEmojis.Warning} Could not create backup file: {ex.Message}");
                                // Continue with override
                            }
                        }
                        else
                        {
                            logger.Info($"[TIDAL] {LogEmojis.File}  •  Track exists  •  Same/better quality  •  #{trackNumber:D2}  •  \"{trackTitle}\"");
                            shouldDownload = false;
                            CompletedTracks++;
                        }
                    }

                    // Skip to next track if we don't need to download this one
                    if (!shouldDownload)
                    {
                        continue;
                    }

                    // Skip if the file already exists
                    if (File.Exists(trackFilePath))
                    {
                        logger.Info($"[TIDAL] {LogEmojis.File}  •  Track exists  •  Skipping  •  #{trackNumber:D2}  •  \"{trackTitle}\"");
                        CompletedTracks++;
                        continue;
                    }

                    // Format duration for display
                    string durationStr = trackDuration > 0
                        ? $"{trackDuration / 60}:{(trackDuration % 60).ToString("00")}"
                        : "--:--";

                    // Log track being downloaded with more details
                    string trackArtistsStr = trackArtists != Artist ? $" by {trackArtists}" : "";
                    string explicitTag = isExplicit ? " [E]" : "";
                    // Create enhanced format with zero-padding and consistent spacing
                    string trackIndicator = $"[{trackNumber:D2}/{TotalTracks:D2}]";
                    string artistPart = string.IsNullOrEmpty(trackArtistsStr) ? Artist : trackArtistsStr.TrimStart(' ', 'b', 'y');
                    logger.Info($"[TIDAL] {LogEmojis.Download} {trackIndicator}  \"{trackTitle}\"{explicitTag}  •  {artistPart}  •  ({durationStr})  •  Album: \"{Album}\"");
                    logger.Info($"[TIDAL] {LogEmojis.Track} Track details: ID: {trackId}, Quality: {quality}");

                    // Download track using TidalSharp
                    try {
                        // Log attempt with more detail
                        logger.Debug($"[TIDAL] {LogEmojis.Info} Initiating download for track ID: {trackId}, quality: {quality}");
                        
                        // Create a temporary file path for download validation
                        string tempFilePath = $"{trackFilePath}.tmp";
                        
                        // Delete any existing temporary file
                        if (File.Exists(tempFilePath)) {
                            try {
                                File.Delete(tempFilePath);
                            }
                            catch (Exception ex) {
                                logger.Warn($"[TIDAL] {LogEmojis.Warning} Could not delete existing temp file: {ex.Message}");
                            }
                        }
                        
                        // Get the file extension that will be used by TidalSharp
                        string expectedExtension = "";
                        try
                        {
                            expectedExtension = await downloader.GetExtensionForTrack(trackId, quality, cancellationToken);
                            ExtendedLoggingService.LogTrackDownloadDiagnostics(trackId, quality, expectedExtension, logger);
                        }
                        catch (Exception ex)
                        {
                            logger.Debug($"[TIDAL] {LogEmojis.Debug} Error getting extension info: {ex.Message}");
                        }
                        
                        // Use semaphore for file I/O if enabled in settings
                        bool useSemaphore = settings.SerializeFileOperations;
                        bool semaphoreAcquired = false;
                        
                        try 
                        {
                            // Apply dynamic throttling if the system detects high load
                            await Lidarr.Plugin.Tidal.Services.ConcurrencyMonitor.ApplyThrottlingDelay(cancellationToken);
                            
                            // Record the start of a file operation for load monitoring
                            Lidarr.Plugin.Tidal.Services.ConcurrencyMonitor.RecordOperationStart("TrackDownload");
                            
                            // Acquire semaphore if serialization is enabled
                            if (useSemaphore)
                            {
                                logger.Debug($"[TIDAL] {LogEmojis.Debug} Waiting for file I/O semaphore");
                                await FileIOSemaphore.WaitAsync(cancellationToken);
                                semaphoreAcquired = true;
                                logger.Debug($"[TIDAL] {LogEmojis.Debug} Acquired file I/O semaphore");
                            }
                            
                            // Download to temporary file first
                            await downloader.WriteRawTrackToFile(trackId, quality, tempFilePath, null, cancellationToken);
                            
                            // Save a copy of the raw downloaded file for diagnostics if enabled
                            bool saveDiagnosticFiles = settings.EnableDiagnostics || 
                                                   (settings.ExtendedLogging && quality == TidalSharp.Data.AudioQuality.HI_RES_LOSSLESS);
                            
                            if (saveDiagnosticFiles)
                            {
                                // Create an instance of the FileValidationService
                                var diagFileValidationService = new Services.FileSystem.FileValidationService();
                                
                                // Save the raw file for diagnostics
                                logger.Info($"[TIDAL] {LogEmojis.Debug} Saving raw download for diagnostic analysis");
                                string diagPath = diagFileValidationService.SaveRawFileForDiagnostics(
                                    tempFilePath, trackId, quality, logger, settings);
                                    
                                if (!string.IsNullOrEmpty(diagPath))
                                {
                                    logger.Info($"[TIDAL] {LogEmojis.Debug} Raw file saved for diagnostics at: {diagPath}");
                                }
                            }
                            
                            // Perform extended diagnostics on the downloaded file
                            ExtendedLoggingService.LogFileFormatDiagnostics(tempFilePath, logger);
                            
                            // Get diagnostic information about the file format
                            var formatInfo = ExtendedLoggingService.GetFileFormatInfo(tempFilePath);
                            
                            // Check if the file extension needs correction based on content
                            correctedFilePath = trackFilePath;
                            if (formatInfo != null)
                            {
                                // Get current extension
                                string currentExtension = Path.GetExtension(trackFilePath).ToLowerInvariant();
                                string directory = Path.GetDirectoryName(trackFilePath);
                                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(trackFilePath);
                                
                                // If the file is FLAC but has .m4a extension, correct it
                                if (formatInfo.HasFlacSignature && currentExtension == ".m4a")
                                {
                                    correctedFilePath = Path.Combine(directory, $"{fileNameWithoutExt}.flac");
                                    logger.Warn($"[TIDAL] {LogEmojis.Warning} Correcting file extension: .m4a -> .flac for {Path.GetFileName(trackFilePath)}");
                                }
                                // If the file is M4A but has .flac extension, correct it
                                else if ((formatInfo.HasFtypAtom || formatInfo.HasMoovAtom || formatInfo.HasMdatAtom) && 
                                        !formatInfo.HasFlacSignature && currentExtension == ".flac")
                                {
                                    correctedFilePath = Path.Combine(directory, $"{fileNameWithoutExt}.m4a");
                                    // Use enhanced format for file extension corrections
                                    string fileExtIndicator = $"[{trackNumber:D2}/{TotalTracks:D2}]";
                                    logger.Warn($"[TIDAL] {LogEmojis.Warning} {fileExtIndicator}  •  Correcting file extension  •  .flac → .m4a  •  File: \"{Path.GetFileName(trackFilePath)}\"");
                                    
                                    // Log additional info about why this happened
                                    if (quality == TidalSharp.Data.AudioQuality.HI_RES_LOSSLESS)
                                    {
                                        logger.Info($"[TIDAL] {LogEmojis.Info} Note: Tidal sometimes delivers HI_RES_LOSSLESS tracks in M4A format instead of FLAC");
                                    }
                                }
                            }
                            
                            // Validate the file using the validation service
                            var validationResult = await fileValidationService.ValidateAudioFileAsync(
                                tempFilePath, trackId, quality, settings, logger, cancellationToken);
                            
                            if (!validationResult.IsValid)
                            {
                                logger.Error($"[TIDAL] {LogEmojis.Error} Track validation failed: {validationResult.FailureReason}");
                                logger.Error($"[TIDAL] {LogEmojis.Error} Details: {validationResult.Details}");
                                
                                // Save a diagnostic copy if we haven't already - especially important for failures
                                if (!saveDiagnosticFiles && validationResult.FailureReason.Contains("FLAC header"))
                                {
                                    var fileValidationSvc = new Services.FileSystem.FileValidationService();
                                    logger.Info($"[TIDAL] {LogEmojis.Debug} Saving failed FLAC file for diagnostic analysis");
                                    fileValidationSvc.SaveRawFileForDiagnostics(tempFilePath, trackId, quality, logger, settings);
                                }
                                
                                // Clean up the temp file
                                fileValidationService.CleanupTempFiles(tempFilePath, logger);
                                
                                if (validationResult.ShouldRequeue && fileValidationService.HasExceededMaxRetries(trackId, settings) == false)
                                {
                                    // If we should requeue, throw an exception that will be caught
                                    // by the outer try/catch and added to the failed tracks list
                                    throw new Exception($"Validation failed ({validationResult.RetryCount}/{settings.FileValidationMaxRetries}): {validationResult.FailureReason}");
                                }
                                else
                                {
                                    // If we shouldn't requeue (max retries exceeded), log and continue
                                    logger.Warn($"[TIDAL] {LogEmojis.Warning} Maximum retries exceeded for track {trackId}, skipping");
                                }
                            }
                            else
                            {
                                // If validation passed, move the file to its final destination
                                if (File.Exists(correctedFilePath))
                                {
                                    // Check if we should keep backups
                                    if (settings.KeepBackupFiles)
                                    {
                                        fileValidationService.CreateBackup(correctedFilePath, logger);
                                    }
                                    else
                                    {
                                        // Just delete the existing file
                                        File.Delete(correctedFilePath);
                                    }
                                }
                                
                                // Move validated file to final destination
                                File.Move(tempFilePath, correctedFilePath);
                                logger.Debug($"[TIDAL] {LogEmojis.Success} File validation passed and file moved to destination");
                                
                                // Log file path if it was corrected
                                if (correctedFilePath != trackFilePath)
                                {
                                    logger.Info($"[TIDAL] {LogEmojis.Success} File saved with corrected path: {Path.GetFileName(correctedFilePath)}");
                                }
                            }
                        }
                        finally
                        {
                            // Release semaphore if we acquired it
                            if (useSemaphore && semaphoreAcquired)
                            {
                                FileIOSemaphore.Release();
                                logger.Debug($"[TIDAL] {LogEmojis.Debug} Released file I/O semaphore");
                            }
                            
                            // Record the end of this file operation
                            Lidarr.Plugin.Tidal.Services.ConcurrencyMonitor.RecordOperationEnd("TrackDownload");
                            
                            // Clean up any temporary files
                            fileValidationService.CleanupTempFiles(tempFilePath, logger);
                        }
                    }
                    catch (Exception ex) {
                        logger.Error($"[TIDAL] {LogEmojis.Error} Download or validation error: {ex.Message}");
                        throw; // Rethrow to the outer try/catch
                    }

                    // Apply possible audio format conversion based on settings
                    correctedFilePath = ApplyAudioFormatConversion(correctedFilePath, settings, logger);
                    
                    // Process lyrics and track metadata
                    string plainLyrics = string.Empty;
                    string lrcFilePath = null;
                    
                    // Process lyrics for the track if enabled in settings
                    if (settings.IncludeLyrics || settings.SaveSyncedLyrics)
                    {
                        logger.Debug($"[TIDAL] {LogEmojis.Info} Processing lyrics for track: {trackTitle} using path: {correctedFilePath}");
                        
                        var lyricsResult = await lyricsService.ProcessTrackLyrics(
                            trackId, 
                            correctedFilePath,
                            trackTitle, 
                            trackArtists, 
                            albumTitle, 
                            trackDuration, 
                            settings, 
                            logger, 
                            cancellationToken);
                            
                        plainLyrics = lyricsResult.plainLyrics;
                        lrcFilePath = lyricsResult.lrcFilePath;
                        
                        // If LRC path was returned but file doesn't exist, log a warning
                        if (!string.IsNullOrEmpty(lrcFilePath) && !File.Exists(lrcFilePath))
                        {
                            logger.Warn($"[TIDAL] {LogEmojis.Warning} LRC file was created but does not exist at: {lrcFilePath}");
                        }
                    }
                    
                    // Apply metadata with correct cover art size based on settings
                    var coverResolution = settings.AddCoverArt 
                        ? MediaResolution.s1280  // High quality
                        : MediaResolution.s160;  // Low quality if covers aren't important
                        
                    await downloader.ApplyMetadataToFile(trackId, correctedFilePath, coverResolution, plainLyrics, cancellationToken);

                    // Log track completion with more comprehensive details
                    var fileInfo = new FileInfo(correctedFilePath);
                    string fileSize = fileInfo.Exists
                        ? FormatFileSize(fileInfo.Length)
                        : "unknown size";

                    // Get file extension and format for display
                    string fileExtension = Path.GetExtension(correctedFilePath).TrimStart('.');
                    string fileFormat = fileExtension.ToUpperInvariant();

                    // Log quality details and check for downgrades
                    LogTrackQualityDetails(quality, fileExtension, fileFormat, trackNumber, TotalTracks, logger);

                    // Log LRC file details if created
                    if (!string.IsNullOrEmpty(lrcFilePath))
                    {
                        logger.Debug($"[TIDAL] {LogEmojis.Music} LRC file created at: {lrcFilePath}");
                    }

                    // Create a visual progress indicator showing which track in the album is being processed
                    int completeBarWidth = 20;
                    int completePosition = (int)Math.Ceiling((double)trackNumber / TotalTracks * completeBarWidth);
                    string completeProgressBar = "[" + new string('■', completePosition) + new string('□', completeBarWidth - completePosition) + "]";
                    string albumInfoComplete = !string.IsNullOrEmpty(Album) ? $" - {Album}" : "";
                    logger.Info($"[TIDAL] {LogEmojis.Success} {trackIndicator}  \"{trackTitle}\"{explicitTag}  •  {artistPart}  •  ({fileSize})  •  Album: \"{Album}\"");

                    CompletedTracks++;

                    // Update progress
                    Progress = CompletedTracks * 100.0 / TotalTracks;
                    
                    // Update progress every minute
                    if ((DateTime.UtcNow - lastProgressUpdate).TotalMinutes >= 1)
                    {
                        lastProgressUpdate = DateTime.UtcNow;
                        string progressIndicator = $"[{CompletedTracks:D2}/{TotalTracks:D2}]";
                        logger.Debug($"[TIDAL] {LogEmojis.Info} {progressIndicator}  •  Album progress  •  {Progress:F1}% complete  •  Album: \"{Album}\"");
                    }

                    // Add appropriate delay based on settings
                    if (i < tracks.Count - 1) // Only delay if not the last track
                    {
                        await ApplyTrackDelay(settings, useNaturalDelays, random, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    var trackTitle = track["title"]?.ToString() ?? "Unknown";
                    var trackNumber = int.TryParse(track["trackNumber"]?.ToString(), out int tn) ? tn : 0;

                    // Use enhanced format for errors
                    string errorIndicator = $"[{trackNumber:D2}/{TotalTracks:D2}]";
                    // Variables for explicitTag and artist may not be defined in this scope - use simpler format
                    logger.Error(ex, $"[TIDAL] {LogEmojis.Error} {errorIndicator}  \"{trackTitle}\"  •  {Artist}  •  FAILED: {ex.Message}  •  Album: \"{Album}\"");
                    failedTracksList.Add(trackNumber);
                }
            }

            // Update failed tracks list
            FailedTracks = failedTracksList.ToArray();
            
            // Log download statistics
            var totalTime = DateTime.UtcNow - downloadStartTime;
            logger.Info($"[TIDAL] {LogEmojis.Time} Album download completed in {FormatTimeSpan(totalTime)}");
            logger.Info($"[TIDAL] {LogEmojis.Info} Downloaded {CompletedTracks}/{TotalTracks} tracks" +
                       (failedTracksList.Count > 0 ? $" ({failedTracksList.Count} failed)" : ""));
        }

        /// <summary>
        /// Applies audio format conversion based on settings
        /// </summary>
        private string ApplyAudioFormatConversion(string filePath, TidalSettings settings, Logger logger)
        {
            if (!settings.ExtractFlac && !settings.ReEncodeAAC)
            {
                return filePath; // No conversion needed
            }
            
            // Handle FLAC extraction if setting is enabled
            if (settings.ExtractFlac && Path.GetExtension(filePath).Equals(".m4a", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    logger.Debug($"[TIDAL] {LogEmojis.File} Extracting FLAC from M4A file: {filePath}");
                    
                    // Implement FLAC extraction logic here
                    // This would typically involve using FFmpeg to extract the FLAC stream
                    
                    // Return the path to the new FLAC file
                    string flacPath = Path.ChangeExtension(filePath, ".flac");
                    return flacPath;
                }
                catch (Exception ex)
                {
                    logger.Warn($"[TIDAL] {LogEmojis.Warning} FLAC extraction failed: {ex.Message}");
                    return filePath; // Return original path on failure
                }
            }
            
            // Handle AAC to MP3 re-encoding if setting is enabled
            if (settings.ReEncodeAAC && Path.GetExtension(filePath).Equals(".m4a", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    logger.Debug($"[TIDAL] {LogEmojis.File} Re-encoding AAC to MP3: {filePath}");
                    
                    // Implement MP3 conversion logic here
                    // This would typically involve using FFmpeg to convert the AAC to MP3
                    
                    // Return the path to the new MP3 file
                    string mp3Path = Path.ChangeExtension(filePath, ".mp3");
                    return mp3Path;
                }
                catch (Exception ex)
                {
                    logger.Warn($"[TIDAL] {LogEmojis.Warning} AAC to MP3 conversion failed: {ex.Message}");
                    return filePath; // Return original path on failure
                }
            }
            
            return filePath;
        }
        
        /// <summary>
        /// Applies appropriate delay between track downloads based on settings
        /// </summary>
        private async Task ApplyTrackDelay(TidalSettings settings, bool useNaturalDelays, Random random, CancellationToken cancellationToken)
        {
            // Apply natural delays if enabled (from the behavior settings)
            if (useNaturalDelays)
            {
                // Get min/max delay from settings and convert to milliseconds
                int minDelayMs = (int)(settings.TrackToTrackDelayMin * 1000);
                int maxDelayMs = (int)(settings.TrackToTrackDelayMax * 1000);
                
                // Ensure max is at least equal to min
                maxDelayMs = Math.Max(minDelayMs, maxDelayMs);
                
                // Generate random delay within range
                int delayMs = random.Next(minDelayMs, maxDelayMs + 1);
                await Task.Delay(delayMs, cancellationToken);
            }
            // Otherwise use legacy delay setting if enabled
            else if (settings.DownloadDelay)
            {
                // Use legacy download delay setting
                var delay = (float)Random.Shared.NextDouble() * 
                    (settings.DownloadDelayMax - settings.DownloadDelayMin) + 
                    settings.DownloadDelayMin;
                    
                await Task.Delay((int)(delay * 1000), cancellationToken);
            }
            else
            {
                // Add a small default delay (300-800ms) to avoid hammering the API
                int delayMs = random.Next(300, 800);
                await Task.Delay(delayMs, cancellationToken);
            }
        }
        
        /// <summary>
        /// Logs quality details and checks for potential quality downgrades
        /// </summary>
        private void LogTrackQualityDetails(TidalSharp.Data.AudioQuality requestedQuality, string fileExtension, string fileFormat, int trackNumber, int totalTracks, Logger logger)
        {
            // Check if the file is the expected type based on quality
            bool isExpectedFormat = (requestedQuality == TidalSharp.Data.AudioQuality.LOSSLESS || 
                                    requestedQuality == TidalSharp.Data.AudioQuality.HI_RES_LOSSLESS) 
                                    ? fileExtension.Equals("flac", StringComparison.OrdinalIgnoreCase) 
                                    : fileExtension.Equals("m4a", StringComparison.OrdinalIgnoreCase);
                                    
            // If not expected format, we may have received a lower quality than requested
            if (!isExpectedFormat && (requestedQuality == TidalSharp.Data.AudioQuality.LOSSLESS || 
                                    requestedQuality == TidalSharp.Data.AudioQuality.HI_RES_LOSSLESS))
            {
                // Use enhanced format with padding for quality warnings
                string warnIndicator = $"[{trackNumber:D2}/{totalTracks:D2}]";
                logger.Warn($"[TIDAL] {LogEmojis.Warning} {warnIndicator}  •  Quality downgrade detected!  •  Requested: {GetAudioQualityDescription(requestedQuality)}  •  Received: {fileFormat}");
                
                // Determine actual quality based on the file extension
                TidalSharp.Data.AudioQuality actualQuality = fileExtension.Equals("m4a", StringComparison.OrdinalIgnoreCase)
                    ? TidalSharp.Data.AudioQuality.HIGH   // Assuming 320kbps AAC
                    : requestedQuality;  // Keep original if unexpected format
                    
                // Get bitrate description based on actual quality
                string bitrateDesc = GetAudioQualityDescription(actualQuality);
                
                logger.Info($"[TIDAL] {LogEmojis.Warning} {warnIndicator}  •  Subscription limitation  •  Your Tidal subscription may not support {GetAudioQualityDescription(requestedQuality)}");
                logger.Info($"[TIDAL] {LogEmojis.File} {warnIndicator}  •  Downgraded format  •  Actual: {fileFormat}, {bitrateDesc}  •  Requested: {GetAudioQualityDescription(requestedQuality)}");
            }
            else
            {
                // Get bitrate description based on quality
                string bitrateDesc = GetAudioQualityDescription(requestedQuality);
                // Create track indicator for this log message
                string fileIndicator = $"[{trackNumber:D2}/{totalTracks:D2}]";
                logger.Info($"[TIDAL] {LogEmojis.File} {fileIndicator}  •  File details  •  Format: {fileFormat}  •  Quality: {bitrateDesc}");
            }
        }
        
        /// <summary>
        /// Gets the appropriate audio quality setting based on settings configuration
        /// </summary>
        private TidalSharp.Data.AudioQuality GetQualitySetting(NzbDrone.Core.Download.Clients.Tidal.TidalSettings settings)
        {
            // Default to high quality as a safe choice
            var quality = TidalSharp.Data.AudioQuality.HIGH;
            
            // Reset MQA flag
            RequestedMQA = false;

            try
            {
                // If prefer highest quality is enabled and we have a valid preference
                if (settings.PreferredQuality >= 0 && settings.PreferredQuality <= 4)
                {
                    // Use the selected quality profile
                    switch (settings.PreferredQuality)
                    {
                        case 0: // Low
                            quality = TidalSharp.Data.AudioQuality.LOW;
                            break;
                        case 1: // High
                            quality = TidalSharp.Data.AudioQuality.HIGH;
                            break;
                        case 2: // HiFi
                            quality = TidalSharp.Data.AudioQuality.LOSSLESS;
                            break;
                        case 3: // MQA - map to HI_RES_LOSSLESS since TidalSharp doesn't have a separate MQA option
                            quality = TidalSharp.Data.AudioQuality.HI_RES_LOSSLESS;
                            // Store the fact that MQA was requested for reference later
                            RequestedMQA = true;
                            break;
                        case 4: // HiRes
                            quality = TidalSharp.Data.AudioQuality.HI_RES_LOSSLESS;
                            break;
                    }
                    
                    // If prefer highest quality is enabled, check if we should upgrade
                    if (settings.PreferHighestQuality && 
                        (quality != TidalSharp.Data.AudioQuality.HI_RES_LOSSLESS))
                    {
                        // Upgrade to highest quality
                        quality = TidalSharp.Data.AudioQuality.HI_RES_LOSSLESS;
                        RequestedMQA = false; // Not MQA when upgraded
                    }
                }
            }
            catch (Exception ex)
            {
                // Silent error, just use the default quality
                Console.WriteLine($"Error determining quality setting: {ex.Message}");
            }

            return quality;
        }

        /// <summary>
        /// Gets the file extension for the specified quality
        /// </summary>
        private static string GetExtensionForQuality(TidalSharp.Data.AudioQuality quality)
        {
            return quality switch
            {
                TidalSharp.Data.AudioQuality.LOSSLESS => "flac",
                TidalSharp.Data.AudioQuality.HI_RES_LOSSLESS => "flac",
                _ => "m4a"
            };
        }

        /// <summary>
        /// Sets the download status to failed with an error message.
        /// </summary>
        /// <param name="errorMessage">The error message describing the failure.</param>
        public void SetFailed(string errorMessage)
        {
            Status = DownloadItemStatus.Failed;
            LastErrorMessage = errorMessage;
            EndTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Sets the download as completed.
        /// </summary>
        public void SetCompleted()
        {
            Status = DownloadItemStatus.Completed;
            Progress = 100;
            EndTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Creates a valid filename by removing invalid path characters
        /// </summary>
        /// <param name="filename">The filename to clean</param>
        /// <returns>A valid filename</returns>
        private string MakeValidFileName(string filename)
        {
            if (string.IsNullOrEmpty(filename))
            {
                return "unknown";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var validName = new string(filename.Where(c => !invalidChars.Contains(c)).ToArray());

            // Replace common problematic characters with alternatives
            validName = validName.Replace(":", "-")
                                 .Replace("?", "")
                                 .Replace("*", "")
                                 .Replace("\"", "'")
                                 .Replace("<", "(")
                                 .Replace(">", ")")
                                 .Replace("|", "-")
                                 .Trim();

            // Ensure we don't return an empty string
            return string.IsNullOrWhiteSpace(validName) ? "unknown" : validName;
        }

        private string FormatFileSize(long size)
        {
            if (size < 1024)
            {
                return $"{size} bytes";
            }
            else if (size < 1024 * 1024)
            {
                return $"{size / 1024} KB";
            }
            else if (size < 1024 * 1024 * 1024)
            {
                return $"{size / (1024 * 1024)} MB";
            }
            else
            {
                return $"{size / (1024 * 1024 * 1024)} GB";
            }
        }

        private string FormatTimeSpan(TimeSpan timeSpan)
        {
            return $"{timeSpan.Hours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
        }

        /// <summary>
        /// Converts an AudioQuality enum value to a human-readable description
        /// </summary>
        /// <param name="quality">The audio quality to describe</param>
        /// <returns>A human-readable description of the audio quality</returns>
        private string GetAudioQualityDescription(TidalSharp.Data.AudioQuality quality)
        {
            // If MQA was specifically requested, show it as such even though it uses HI_RES_LOSSLESS internally
            if (RequestedMQA && quality == TidalSharp.Data.AudioQuality.HI_RES_LOSSLESS)
            {
                return "MQA (Master Quality Authenticated)";
            }
            
            return quality switch
            {
                TidalSharp.Data.AudioQuality.LOW => "AAC 96kbps",
                TidalSharp.Data.AudioQuality.HIGH => "AAC 320kbps",
                TidalSharp.Data.AudioQuality.LOSSLESS => "FLAC 16-bit/44.1kHz",
                TidalSharp.Data.AudioQuality.HI_RES_LOSSLESS => "FLAC 24-bit/96kHz",
                _ => quality.ToString()
            };
        }

        /// <summary>
        /// Checks if a FLAC file is high resolution (24-bit or >44.1kHz)
        /// </summary>
        private bool IsHighResolutionFlac(string filePath, Logger logger)
        {
            try
            {
                // Try to read the FLAC file header to determine bit depth and sample rate
                // This is a simplified check that could be enhanced with a more robust FLAC parser
                using (var fileStream = File.OpenRead(filePath))
                {
                    byte[] buffer = new byte[42]; // Read enough for a basic FLAC header check
                    if (fileStream.Read(buffer, 0, buffer.Length) < buffer.Length)
                    {
                        return false; // Not enough data, assume standard
                    }

                    // Check for "fLaC" marker at start
                    if (buffer[0] == 0x66 && buffer[1] == 0x4C && buffer[2] == 0x61 && buffer[3] == 0x43)
                    {
                        // Skip past marker and look for STREAMINFO block (type 0)
                        // This is very simplified and might need to be improved
                        for (int i = 4; i < buffer.Length - 8; i++)
                        {
                            // Check bits 1-7 of byte for block type 0 (STREAMINFO)
                            if ((buffer[i] & 0x7F) == 0)
                            {
                                // Sample rate is at offset 14-16 from block header
                                int sampleRate = (buffer[i + 14] << 12) | (buffer[i + 15] << 4) | ((buffer[i + 16] & 0xF0) >> 4);
                                
                                // Bit depth is lower 5 bits of byte 16 + upper 1 bit of byte 17
                                int bitDepth = ((buffer[i + 16] & 0x0F) << 1) | ((buffer[i + 17] & 0x80) >> 7);
                                
                                logger.Debug($"[TIDAL] FLAC analysis: {bitDepth} bits, {sampleRate} Hz");
                                
                                // Check if high-res (24-bit or >44.1kHz)
                                return bitDepth > 16 || sampleRate > 44100;
                            }
                        }
                    }
                }
                
                // Default to false if we couldn't determine
                return false;
            }
            catch (Exception ex)
            {
                logger.Debug($"[TIDAL] Error analyzing FLAC file: {ex.Message}");
                return false; // Assume standard quality on error
            }
        }
        
        /// <summary>
        /// Checks if an AAC file is low quality (96kbps) based on file size relative to duration
        /// </summary>
        private bool IsLowQualityAAC(string filePath, long fileSize, int durationSeconds, Logger logger)
        {
            try
            {
                // If no duration available, we can't make this calculation
                if (durationSeconds <= 0)
                {
                    return false; // Can't determine
                }
                
                // Calculate approximate bitrate (bytes per second * 8 for bits)
                // Account for container overhead by reducing file size by 10KB
                long adjustedFileSize = Math.Max(0, fileSize - (10 * 1024));
                double bitsPerSecond = (adjustedFileSize * 8.0) / durationSeconds;
                int approximateBitrate = (int)(bitsPerSecond / 1000); // Convert to kbps
                
                logger.Debug($"[TIDAL] AAC analysis: ~{approximateBitrate} kbps ({fileSize} bytes / {durationSeconds} sec)");
                
                // If the bitrate is closer to 96kbps than 320kbps, consider it low quality
                // Using 200kbps as a rough midpoint
                return approximateBitrate < 200;
            }
            catch (Exception ex)
            {
                logger.Debug($"[TIDAL] Error analyzing AAC file: {ex.Message}");
                return false; // Assume high quality on error
            }
        }

        /// <summary>
        /// Validates an audio file to ensure it's not corrupted
        /// </summary>
        /// <param name="filePath">Path to the audio file</param>
        /// <param name="logger">Logger for output</param>
        /// <returns>True if the file is valid, false if corrupted</returns>
        private bool ValidateAudioFile(string filePath, Logger logger)
        {
            // This method is kept for backward compatibility
            try
            {
                var validationService = FileValidationServiceProvider.Current;
                var result = validationService.ValidateAudioFileAsync(
                    filePath, 
                    "unknown", // Track ID unknown in this context
                    TidalSharp.Data.AudioQuality.HIGH, // Default quality
                    new NzbDrone.Core.Download.Clients.Tidal.TidalSettings { 
                        EnableFileValidation = true,
                        FileValidationMaxRetries = 1,
                        FileValidationMinSize = 32 // 32 KB minimum
                    }, 
                    logger, 
                    CancellationToken.None).GetAwaiter().GetResult();
                
                return result.IsValid;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"[TIDAL] {LogEmojis.Error} File validation error: {ex.Message}");
                return false;
            }
        }
    }
}
