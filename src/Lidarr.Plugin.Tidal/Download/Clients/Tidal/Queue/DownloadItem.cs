using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Plugins;
using NzbDrone.Plugin.Tidal;
using TidalSharp;
using TidalSharp.Data;

namespace NzbDrone.Core.Download.Clients.Tidal.Queue
{
    /// <summary>
    /// Interface for download item to make it mockable in tests
    /// </summary>
    public interface IDownloadItem
    {
        string ID { get; }
        string Title { get; }
        string Artist { get; }
        string Album { get; }
        bool Explicit { get; }
        RemoteAlbum RemoteAlbum { get; }
        string DownloadFolder { get; }
        AudioQuality Bitrate { get; }
        DownloadItemStatus Status { get; set; }
        float Progress { get; }
        long DownloadedSize { get; }
        long TotalSize { get; }
        int FailedTracks { get; }
        
        Task DoDownload(TidalSettings settings, Logger logger, CancellationToken cancellation = default);
    }

    public class DownloadItem : IDownloadItem
    {
        public static async Task<DownloadItem> From(RemoteAlbum remoteAlbum)
        {
            var url = remoteAlbum.Release.DownloadUrl.Trim();
            var quality = remoteAlbum.Release.Container switch
            {
                "96" => AudioQuality.LOW,
                "320" => AudioQuality.HIGH,
                "Lossless" => AudioQuality.LOSSLESS,
                "24bit Lossless" => AudioQuality.HI_RES_LOSSLESS,
                _ => AudioQuality.HIGH,
            };

            DownloadItem item = null;
            if (url.Contains("tidal", StringComparison.CurrentCultureIgnoreCase))
            {
                if (TidalURL.TryParse(url, out var tidalUrl))
                {
                    item = new()
                    {
                        ID = Guid.NewGuid().ToString(),
                        Status = DownloadItemStatus.Queued,
                        Bitrate = quality,
                        RemoteAlbum = remoteAlbum,
                        _tidalUrl = tidalUrl,
                    };

                    await item.SetTidalData();
                }
            }

            return item;
        }

        public string ID { get; private set; }

        public string Title { get; private set; }
        public string Artist { get; private set; }
        public string Album { get; private set; }
        public bool Explicit { get; private set; }

        public RemoteAlbum RemoteAlbum {  get; private set; }

        public string DownloadFolder { get; private set; }

        public AudioQuality Bitrate { get; private set; }
        public DownloadItemStatus Status { get; set; }

        public float Progress { get => DownloadedSize / (float)Math.Max(TotalSize, 1); }
        // Use long for DownloadedSize to prevent overflow with large files/many chunks
        public long DownloadedSize { get; private set; } 
        public long TotalSize { get; private set; }

        // Use volatile int for FailedTracks, accessed via Interlocked
        private volatile int _failedTracks;
        public int FailedTracks { get => _failedTracks; } 

        private (string id, int chunks)[] _tracks;
        private TidalURL _tidalUrl;
        private JObject _tidalAlbum;
        private Logger _logger;
        private Dictionary<string, int> _trackFailureCounts = new Dictionary<string, int>();
        private const int MaxTrackFailures = 3;

        public virtual async Task DoDownload(TidalSettings settings, Logger logger, CancellationToken cancellation = default)
        {
            _logger = logger;
            // Reset counters for this download attempt
            _failedTracks = 0; 
            DownloadedSize = 0; 

            List<Task> tasks = new();
            using SemaphoreSlim semaphore = new(settings.MaxConcurrentTrackDownloads, settings.MaxConcurrentTrackDownloads);
            
            int totalTracks = _tracks.Length;
            logger.Info($"Starting album download: '{Album}' by {Artist} ({totalTracks} tracks)");
            
            // Track start time for duration calculation
            var startTime = DateTime.Now;
            
            for (int trackIndex = 0; trackIndex < _tracks.Length; trackIndex++)
            {
                var (trackId, trackSize) = _tracks[trackIndex];
                int currentTrackNumber = trackIndex + 1;
                
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync(cancellation);
                    try
                    {
                        const int MaxRetries = 3;
                        const int RetryDelaySeconds = 5;
                        int attempt = 0;
                        bool success = false;

                        while (attempt < MaxRetries && !success && !cancellation.IsCancellationRequested)
                        {
                            // Circuit breaker logic
                            if (_trackFailureCounts.TryGetValue(trackId, out int currentFailureCount) && currentFailureCount >= MaxTrackFailures)
                            {
                                logger.Warn($"❌ Track {trackId} skipped due to exceeding maximum failure count ({MaxTrackFailures}).");
                                Interlocked.Increment(ref _failedTracks);
                                break; // Skip to the next track
                            }

                            attempt++;
                            try
                            {
                                await DoTrackDownload(trackId, settings, currentTrackNumber, totalTracks, cancellation);
                                success = true; // Mark as success if DoTrackDownload completes without exception

                                // Apply legacy delay only after successful download
                                if (settings.DownloadDelay)
                                {
                                    var delay = (float)Random.Shared.NextDouble() * (settings.DownloadDelayMax - settings.DownloadDelayMin) + settings.DownloadDelayMin;
                                    await Task.Delay((int)(delay * 1000), cancellation);
                                }
                            }
                            // Removed separate TaskCanceledException catch
                            catch (Exception ex)
                            {
                                // Check if it was a cancellation exception first
                                if (ex is TaskCanceledException || ex is OperationCanceledException)
                                {
                                    // Don't retry if cancellation was requested
                                    break;
                                }

                                // Handle other exceptions with more visible error formatting
                                logger.Warn($"❌ Attempt {attempt}/{MaxRetries} failed for track {trackId}: {ex.Message}");
                                
                                // Increment failure count
                                _trackFailureCounts.TryGetValue(trackId, out int existingFailureCount);
                                _trackFailureCounts[trackId] = existingFailureCount + 1;

                                if (attempt >= MaxRetries)
                                {
                                    logger.Error($"❌❌❌ FINAL ATTEMPT FAILED for track {trackId}. Marking as failed.");
                                    logger.Error(ex.ToString()); // Log full exception on final failure
                                    Interlocked.Increment(ref _failedTracks); // Use Interlocked on the backing field
                                }
                                else
                                {
                                    // Wait before retrying, respecting cancellation
                                    try
                                    {
                                        await Task.Delay(RetryDelaySeconds * 1000, cancellation);
                                    }
                                    catch (TaskCanceledException) 
                                    { 
                                        // Exit loop if cancelled during delay (Using standard block format)
                                        break; 
                                    } 
                                }
                            } // End of catch (Exception ex)
                        } // End of while loop - Removed stray 'b'
                        // Removed redundant check after loop as failure should be handled in the catch block
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellation));
            }

            await Task.WhenAll(tasks);
            
            // Calculate download duration
            var duration = DateTime.Now - startTime;
            var durationStr = $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
            
            // Calculate size in MB for display
            var sizeMB = TotalSize > 0 ? ((double)DownloadedSize / 1024.0 / 1024.0).ToString("F2") : "0";
            
            // Format quality string
            string qualityStr = Bitrate switch
            {
                AudioQuality.LOW => "Low (96kbps)",
                AudioQuality.HIGH => "High (320kbps)",
                AudioQuality.LOSSLESS => "Lossless (CD Quality)",
                AudioQuality.HI_RES_LOSSLESS => "Hi-Res Lossless (24bit)",
                _ => "Unknown"
            };
            
            // Use the final count from the backing field
            if (_failedTracks > 0) 
            {
                Status = DownloadItemStatus.Failed;
                logger.Error("==============================================");
                logger.Error($"❌ ALBUM DOWNLOAD INCOMPLETE: {Album} - {Artist}");
                logger.Error($"   ✓ Tracks: {totalTracks - _failedTracks}/{totalTracks} completed ({_failedTracks} failed)");
                logger.Error($"   ✓ Duration: {durationStr}");
                logger.Error($"   ✓ Size: {sizeMB}MB");
                logger.Error($"   ✓ Format: {qualityStr}");
                logger.Error("==============================================");
            }
            else
            {
                Status = DownloadItemStatus.Completed;
                logger.Info("==============================================");
                logger.Info($"📀 ALBUM DOWNLOAD COMPLETE: {Album} - {Artist}");
                logger.Info($"   ✓ Tracks: {totalTracks}/{totalTracks} completed");
                logger.Info($"   ✓ Duration: {durationStr}");
                logger.Info($"   ✓ Size: {sizeMB}MB");
                logger.Info($"   ✓ Format: {qualityStr}");
                logger.Info("==============================================");
            }
        }

        private async Task DoTrackDownload(string trackId, TidalSettings settings, int trackNumber, int totalTracks, CancellationToken cancellation)
        {
            // 1. Fetch track details
            var (page, songTitle, artistName, albumTitle, duration) = await FetchTrackDetails(trackId, cancellation);
            
            // Create consistent log prefix format
            string logPrefix = $"[{albumTitle} - {artistName} ({trackNumber}/{totalTracks})]";
            
            // Log the start of track download with clear information including track number
            _logger?.Info($"🎵 {logPrefix} \"{songTitle}\" - Starting download");

            // 2. Generate file path and ensure directory exists
            var (outPath, outDir) = await GenerateTrackFilePath(page, settings, cancellation);

            // 3. Download the raw track file
            await DownloadTrackFile(trackId, outPath, cancellation);
            _logger?.Info($"📥 {logPrefix} \"{songTitle}\" - File download complete");

            // 4. Handle audio conversion (FLAC extraction / AAC re-encoding)
            outPath = HandleAudioConversion(outPath, settings, _logger);

            // 5. Process lyrics (Tidal + Backup)
            var (plainLyrics, syncLyrics) = await ProcessLyrics(trackId, songTitle, artistName, albumTitle, duration, settings, cancellation);
            _logger?.Debug($"🎭 {logPrefix} \"{songTitle}\" - Lyrics processed (Has lyrics: {!string.IsNullOrEmpty(plainLyrics)})");

            // 6. Apply metadata and create LRC file
            await ApplyMetadataAndLrc(trackId, outPath, outDir, page, plainLyrics, syncLyrics, settings, cancellation);
            
            // Log completion of track processing
            _logger?.Info($"✅ {logPrefix} \"{songTitle}\" - Processing complete");
        }

        private async Task<(JObject page, string songTitle, string artistName, string albumTitle, int duration)> FetchTrackDetails(string trackId, CancellationToken cancellation)
        {
            var page = await TidalAPI.Instance.Client.API.GetTrack(trackId, cancellation);
            var songTitle = API.CompleteTitleFromPage(page);
            var artistName = page["artist"]!["name"]!.ToString();
            var albumTitle = page["album"]!["title"]!.ToString();
            var duration = page["duration"]!.Value<int>();
            return (page, songTitle, artistName, albumTitle, duration);
        }

        private async Task<(string outPath, string outDir)> GenerateTrackFilePath(JObject page, TidalSettings settings, CancellationToken cancellation)
        {
            var ext = (await TidalAPI.Instance.Client.Downloader.GetExtensionForTrack(page["id"]!.ToString(), Bitrate, cancellation)).TrimStart('.');
            var outPath = Path.Combine(settings.DownloadPath, MetadataUtilities.GetFilledTemplate("%albumartist%/%album%/", ext, page, _tidalAlbum), MetadataUtilities.GetFilledTemplate("%volume% - %track% - %title%.%ext%", ext, page, _tidalAlbum));
            var outDir = Path.GetDirectoryName(outPath)!;

            DownloadFolder = outDir; // Keep setting this side-effect here for now
            if (!Directory.Exists(outDir))
            {
                try
                {
                    Directory.CreateDirectory(outDir);
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, $"Failed to create directory: {outDir}");
                    // Re-throw or handle more gracefully depending on desired behavior
                    throw;
                }
            }

            return (outPath, outDir);
        }

        private async Task DownloadTrackFile(string trackId, string outPath, CancellationToken cancellation)
        {
            // Create a local variable to use with Interlocked operations
            long downloadedSize = 0;
            
            // Using Interlocked.Add which takes the value to add (i) and returns the new sum.
            // Use the local variable for the tracking and then update the property.
            await TidalAPI.Instance.Client.Downloader.WriteRawTrackToFile(trackId, Bitrate, outPath, (i) => { 
                downloadedSize = Interlocked.Add(ref downloadedSize, i);
                DownloadedSize = downloadedSize;
            }, cancellation); 
        }

        private async Task<(string plainLyrics, string syncLyrics)> ProcessLyrics(string trackId, string songTitle, string artistName, string albumTitle, int duration, TidalSettings settings, CancellationToken cancellation)
        {
            string plainLyrics = string.Empty;
            string syncLyrics = null;

            try
            {
                var lyrics = await TidalAPI.Instance.Client.Downloader.FetchLyricsFromTidal(trackId, cancellation);
                if (lyrics.HasValue)
                {
                    plainLyrics = lyrics.Value.plainLyrics;
                    if (settings.SaveSyncedLyrics)
                        syncLyrics = lyrics.Value.syncLyrics;
                    
                    _logger?.Debug($"🎭 Lyrics found on Tidal for \"{songTitle}\" by {artistName}");
                }
                else
                {
                    _logger?.Debug($"🎭 No lyrics found on Tidal for \"{songTitle}\" by {artistName}");
                }

                // Check if backup provider is enabled, LRCLIB is selected, and lyrics are still needed
                if (settings.UseLRCLIB && settings.BackupLyricProvider == (int)LyricProviderSource.LRCLIB &&
                    (string.IsNullOrWhiteSpace(plainLyrics) || (settings.SaveSyncedLyrics && !(syncLyrics?.Any() ?? false))))
                {
                    _logger?.Debug($"🔍 Searching backup lyrics provider for \"{songTitle}\" by {artistName}");
                    // Use the configured URL
                    lyrics = await TidalAPI.Instance.Client.Downloader.FetchLyricsFromLRCLIB(settings.LyricProviderUrl, songTitle, artistName, albumTitle, duration, cancellation);
                    if (lyrics.HasValue)
                    {
                        if (string.IsNullOrWhiteSpace(plainLyrics))
                        {
                            plainLyrics = lyrics.Value.plainLyrics;
                            _logger?.Debug($"🎭 Plain lyrics found from backup provider");
                        }
                        if (settings.SaveSyncedLyrics && !(syncLyrics?.Any() ?? false))
                        {
                            syncLyrics = lyrics.Value.syncLyrics;
                            _logger?.Debug($"🎭 Synced lyrics found from backup provider");
                        }
                    }
                    else
                    {
                        _logger?.Debug($"🎭 No lyrics found from backup provider");
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the error but continue with the download
                _logger?.Warn($"⚠️ Error fetching lyrics for \"{songTitle}\" by {artistName}: {ex.Message}");
            }

            return (plainLyrics, syncLyrics);
        }

        private async Task ApplyMetadataAndLrc(string trackId, string filePath, string outDir, JObject page, string plainLyrics, string syncLyrics, TidalSettings settings, CancellationToken cancellation)
        {
             try
            {
                await TidalAPI.Instance.Client.Downloader.ApplyMetadataToFile(trackId, filePath, MediaResolution.s640, plainLyrics, token: cancellation);

                if (syncLyrics != null && settings.SaveSyncedLyrics) // Check SaveSyncedLyrics again here
                {
                    var lrcFileName = MetadataUtilities.GetFilledTemplate("%volume% - %track% - %title%.%ext%", "lrc", page, _tidalAlbum);
                    await CreateLrcFile(Path.Combine(outDir, lrcFileName), syncLyrics);
                }
            }
            catch (Exception ex)
            {
                // Log the error but don't fail the download
                _logger?.Warn($"Error applying metadata or creating LRC file for track {trackId}: {ex.Message}");
            }

            // TODO: Cover art logic remains commented out
            /* try
            {
                string artOut = Path.Combine(outDir, "folder.jpg");
                if (!File.Exists(artOut))
                {
                    byte[] bigArt = await TidalAPI.Instance.Client.Downloader.GetArtBytes(page["DATA"]!["ALB_PICTURE"]!.ToString(), 1024, cancellation);
                    await File.WriteAllBytesAsync(artOut, bigArt, cancellation);
                }
            }
            catch (UnavailableArtException) { } */
        }


        private string HandleAudioConversion(string filePath, TidalSettings settings, Logger logger)
        {
            if (!settings.ExtractFlac && !settings.ReEncodeAAC)
                return filePath;

            var codecs = FFMPEG.ProbeCodecs(filePath);
            if (codecs.Contains("flac") && settings.ExtractFlac)
            {
                var newFilePath = Path.ChangeExtension(filePath, "flac");
                try
                {
                    FFMPEG.ConvertWithoutReencode(filePath, newFilePath);
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                    return newFilePath;
                }
                catch (FFMPEGException ex)
                {
                    // Log the error with details
                    logger?.Warn($"FLAC extraction failed for {Path.GetFileName(filePath)}: {ex.Message}");
                    
                    // Clean up any partial output file
                    if (File.Exists(newFilePath))
                        File.Delete(newFilePath);
                    return filePath;
                }
                catch (Exception ex)
                {
                    // Handle any other unexpected exceptions
                    logger?.Warn($"Unexpected error during FLAC extraction for {Path.GetFileName(filePath)}: {ex.Message}");
                    
                    if (File.Exists(newFilePath))
                        File.Delete(newFilePath);
                    return filePath;
                }
            }

            if (codecs.Contains("aac") && settings.ReEncodeAAC)
            {
                var newFilePath = Path.ChangeExtension(filePath, "mp3");
                try
                {
                    var tagFile = TagLib.File.Create(filePath);
                    var bitrate = tagFile.Properties.AudioBitrate;
                    tagFile.Dispose();

                    FFMPEG.Reencode(filePath, newFilePath, bitrate);
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                    return newFilePath;
                }
                catch (FFMPEGException ex)
                {
                    // Log the error with details
                    logger?.Warn($"AAC to MP3 conversion failed for {Path.GetFileName(filePath)}: {ex.Message}");
                    
                    // Clean up any partial output file
                    if (File.Exists(newFilePath))
                        File.Delete(newFilePath);
                    return filePath;
                }
                catch (Exception ex)
                {
                    // Handle any other unexpected exceptions
                    logger?.Warn($"Unexpected error during AAC to MP3 conversion for {Path.GetFileName(filePath)}: {ex.Message}");
                    
                    if (File.Exists(newFilePath))
                        File.Delete(newFilePath);
                    return filePath;
                }
            }

            return filePath;
        }

        private async Task CreateLrcFile(string lrcFilePath, string syncLyrics)
        {
            await File.WriteAllTextAsync(lrcFilePath, syncLyrics);
        }

        private async Task SetTidalData(CancellationToken cancellation = default)
        {
            if (_tidalUrl.EntityType != EntityType.Album)
                throw new InvalidOperationException();

            var album = await TidalAPI.Instance.Client.API.GetAlbum(_tidalUrl.Id, cancellation);
            var albumTracks = await TidalAPI.Instance.Client.API.GetAlbumTracks(_tidalUrl.Id, cancellation);

            var tracksTasks = albumTracks["items"]!.Select(async t =>
            {
                // Calculate total size based on chunks (assuming this represents downloadable size)
                var chunks = await TidalAPI.Instance.Client.Downloader.GetChunksInTrack(t["id"]!.ToString(), Bitrate, cancellation);
                return (t["id"]!.ToString(), chunks);
            }).ToArray();

            var tracks = await Task.WhenAll(tracksTasks);
            _tracks ??= tracks;

            _tidalAlbum = album;

            Title = album["title"]!.ToString();
            Artist = album["artist"]!["name"]!.ToString();
            Album = album["title"]!.ToString(); // Added Album property implementation
            Explicit = album["explicit"]!.Value<bool>();
            // Calculate TotalSize based on the sum of chunks from all tracks
            TotalSize = _tracks.Sum(t => (long)t.chunks); 
        }
    }
}
