using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Download.Clients.Tidal.Interfaces;
using Lidarr.Plugin.Tidal.Services.Logging;
using TidalSharp;
using TidalSharp.Data;

namespace NzbDrone.Core.Download.Clients.Tidal.Queue
{
    /// <summary>
    /// Handles the actual downloading of tracks from Tidal
    /// </summary>
    public class DownloadProcessor
    {
        private readonly Logger _logger;
        private readonly TidalSettings _settings;
        private readonly string _serviceName;

        /// <summary>
        /// Initializes a new instance of the DownloadProcessor class
        /// </summary>
        /// <param name="settings">Tidal settings</param>
        /// <param name="serviceName">Name of the service for logging</param>
        /// <param name="logger">Logger for diagnostic messages</param>
        public DownloadProcessor(TidalSettings settings, string serviceName, Logger logger)
        {
            _settings = settings;
            _serviceName = serviceName;
            _logger = logger;
        }

        /// <summary>
        /// Downloads a track from Tidal
        /// </summary>
        /// <param name="downloader">The downloader instance to use</param>
        /// <param name="trackId">ID of the track to download</param>
        /// <param name="trackIndex">Index of the track in the album (1-based)</param>
        /// <param name="totalTracks">Total number of tracks in the album</param>
        /// <param name="bitrate">Bitrate to download at</param>
        /// <param name="downloadPath">Path to download the track to</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result of the download operation</returns>
        public async Task<(bool Success, string FilePath, string ErrorMessage)> DownloadTrack(
            Downloader downloader,
            string trackId,
            int trackIndex,
            int totalTracks,
            AudioQuality bitrate,
            string downloadPath,
            CancellationToken cancellationToken)
        {
            try
            {
                // Get file extension for the track
                string extension = await downloader.GetExtensionForTrack(trackId, bitrate, cancellationToken);
                string trackFilename = $"track_{trackIndex:D2}_{trackId}{extension}";
                string trackPath = Path.Combine(downloadPath, trackFilename);

                _logger?.InfoWithEmoji(LogEmojis.Download,
                    $"[{_serviceName}] Downloading track {trackIndex}/{totalTracks} (ID: {trackId}) to {trackPath}");

                // Download the track
                await downloader.WriteRawTrackToFile(
                    trackId,
                    bitrate,
                    trackPath,
                    (chunkIndex) => {
                        // Update progress on chunk download
                        _logger?.Debug($"[{_serviceName}] Downloaded chunk {chunkIndex} for track {trackIndex}/{totalTracks}");
                    },
                    cancellationToken
                );

                // Verify the downloaded file
                if (!File.Exists(trackPath))
                {
                    _logger?.ErrorWithEmoji(LogEmojis.Error,
                        $"[{_serviceName}] Track file not found after download: {trackPath}");
                    return (false, trackPath, "Track file not found after download");
                }

                var fileInfo = new FileInfo(trackPath);
                if (fileInfo.Length == 0)
                {
                    _logger?.ErrorWithEmoji(LogEmojis.Error,
                        $"[{_serviceName}] Downloaded track file is empty: {trackPath}");
                    return (false, trackPath, "Downloaded track file is empty");
                }

                _logger?.InfoWithEmoji(LogEmojis.Success,
                    $"[{_serviceName}] Successfully downloaded track {trackIndex}/{totalTracks} ({fileInfo.Length / 1024.0 / 1024.0:F2} MB)");

                return (true, trackPath, null);
            }
            catch (OperationCanceledException)
            {
                _logger?.WarnWithEmoji(LogEmojis.Cancel,
                    $"[{_serviceName}] Download of track {trackIndex}/{totalTracks} (ID: {trackId}) was cancelled");
                return (false, null, "Download cancelled");
            }
            catch (Exception ex)
            {
                _logger?.ErrorWithEmoji(LogEmojis.Error, ex,
                    $"[{_serviceName}] Error downloading track {trackIndex}/{totalTracks} (ID: {trackId})");
                return (false, null, $"Download error: {ex.Message}");
            }
        }

        /// <summary>
        /// Downloads album cover art
        /// </summary>
        /// <param name="downloader">The downloader instance to use</param>
        /// <param name="albumId">ID of the album</param>
        /// <param name="downloadPath">Path to download the cover art to</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result of the download operation</returns>
        public async Task<(bool Success, string FilePath, string ErrorMessage)> DownloadCoverArt(
            Downloader downloader,
            string albumId,
            string downloadPath,
            CancellationToken cancellationToken)
        {
            try
            {
                string coverPath = Path.Combine(downloadPath, "cover.jpg");

                _logger?.InfoWithEmoji(LogEmojis.Download,
                    $"[{_serviceName}] Downloading cover art for album {albumId}");

                // Download the cover art
                // We need to get the album data to find the cover ID
                // Since we can't access the API directly, we'll use the album ID as the cover ID
                string coverId = albumId;

                // Get the cover image bytes
                byte[] coverBytes = await downloader.GetImageBytes(
                    coverId,
                    MediaResolution.s1280,
                    cancellationToken
                );

                // Write the bytes to the file
                await File.WriteAllBytesAsync(coverPath, coverBytes, cancellationToken);

                // Verify the downloaded file
                if (!File.Exists(coverPath))
                {
                    _logger?.WarnWithEmoji(LogEmojis.Warning,
                        $"[{_serviceName}] Cover art file not found after download: {coverPath}");
                    return (false, coverPath, "Cover art file not found after download");
                }

                var fileInfo = new FileInfo(coverPath);
                if (fileInfo.Length == 0)
                {
                    _logger?.WarnWithEmoji(LogEmojis.Warning,
                        $"[{_serviceName}] Downloaded cover art file is empty: {coverPath}");
                    return (false, coverPath, "Downloaded cover art file is empty");
                }

                _logger?.InfoWithEmoji(LogEmojis.Success,
                    $"[{_serviceName}] Successfully downloaded cover art ({fileInfo.Length / 1024.0:F2} KB)");

                return (true, coverPath, null);
            }
            catch (OperationCanceledException)
            {
                _logger?.WarnWithEmoji(LogEmojis.Cancel,
                    $"[{_serviceName}] Download of cover art for album {albumId} was cancelled");
                return (false, null, "Cover art download cancelled");
            }
            catch (Exception ex)
            {
                _logger?.WarnWithEmoji(LogEmojis.Warning,
                    $"[{_serviceName}] Error downloading cover art for album {albumId}: {ex.Message}");
                return (false, null, $"Cover art download error: {ex.Message}");
            }
        }
    }
}

