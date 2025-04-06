using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLog;
using Lidarr.Plugin.Tidal.Services.Logging;
using NzbDrone.Core.Download.Clients.Tidal.Interfaces;

namespace Lidarr.Plugin.Tidal.Download.Clients.Tidal.Utilities
{
    /// <summary>
    /// Generates detailed album information files for downloaded albums
    /// </summary>
    public class AlbumInfoGenerator
    {
        private readonly Logger _logger;
        private const string DEFAULT_FILENAME = "album-info.txt";

        /// <summary>
        /// Initializes a new instance of the AlbumInfoGenerator class
        /// </summary>
        /// <param name="logger">Logger for diagnostic messages</param>
        public AlbumInfoGenerator(Logger logger)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
        }

        /// <summary>
        /// Creates a detailed album information file in the specified folder
        /// </summary>
        /// <param name="downloadItem">The download item containing album information</param>
        /// <param name="tracks">List of track information</param>
        /// <param name="albumId">The Tidal album ID</param>
        /// <param name="albumMetadata">Additional album metadata (optional)</param>
        /// <param name="customFilename">Custom filename (optional, defaults to album-info.txt)</param>
        /// <returns>True if the file was created successfully, false otherwise</returns>
        public bool CreateAlbumInfoFile(
            IDownloadItem downloadItem,
            List<JObject> tracks,
            string albumId,
            JObject albumMetadata = null,
            string customFilename = null)
        {
            if (downloadItem == null)
            {
                _logger.Error("Cannot create album info file: download item is null");
                return false;
            }

            if (string.IsNullOrWhiteSpace(downloadItem.DownloadFolder))
            {
                _logger.Warn("Cannot create album info file: download folder is not specified");
                return false;
            }

            try
            {
                string content = GenerateAlbumInfoContent(downloadItem, tracks, albumId, albumMetadata);
                string filename = customFilename ?? DEFAULT_FILENAME;
                string filePath = Path.Combine(downloadItem.DownloadFolder, filename);

                File.WriteAllText(filePath, content);
                _logger.Info($"Created album info file: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn($"Error creating album info file: {ex.Message}");
                _logger.Debug(ex, "Exception details");
                return false;
            }
        }

        /// <summary>
        /// Asynchronously creates a detailed album information file in the specified folder
        /// </summary>
        /// <param name="downloadItem">The download item containing album information</param>
        /// <param name="tracks">List of track information</param>
        /// <param name="albumId">The Tidal album ID</param>
        /// <param name="albumMetadata">Additional album metadata (optional)</param>
        /// <param name="customFilename">Custom filename (optional, defaults to album-info.txt)</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a boolean indicating success</returns>
        public async Task<bool> CreateAlbumInfoFileAsync(
            IDownloadItem downloadItem,
            List<JObject> tracks,
            string albumId,
            JObject albumMetadata = null,
            string customFilename = null)
        {
            if (downloadItem == null)
            {
                _logger.Error("Cannot create album info file: download item is null");
                return false;
            }

            if (string.IsNullOrWhiteSpace(downloadItem.DownloadFolder))
            {
                _logger.Warn("Cannot create album info file: download folder is not specified");
                return false;
            }

            try
            {
                string content = GenerateAlbumInfoContent(downloadItem, tracks, albumId, albumMetadata);
                string filename = customFilename ?? DEFAULT_FILENAME;
                string filePath = Path.Combine(downloadItem.DownloadFolder, filename);

                await File.WriteAllTextAsync(filePath, content);
                _logger.Info($"Created album info file: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn($"Error creating album info file: {ex.Message}");
                _logger.Debug(ex, "Exception details");
                return false;
            }
        }

        /// <summary>
        /// Generates the content for the album information file
        /// </summary>
        /// <param name="downloadItem">The download item containing album information</param>
        /// <param name="tracks">List of track information</param>
        /// <param name="albumId">The Tidal album ID</param>
        /// <param name="albumMetadata">Additional album metadata (optional)</param>
        /// <returns>The generated content as a string</returns>
        private string GenerateAlbumInfoContent(
            IDownloadItem downloadItem,
            List<JObject> tracks,
            string albumId,
            JObject albumMetadata)
        {
            var sb = new StringBuilder();

            // Add header with album info
            sb.AppendLine($"ALBUM INFORMATION");
            sb.AppendLine($"=================");
            sb.AppendLine();
            sb.AppendLine($"Title: {downloadItem.Album}");
            sb.AppendLine($"Artist: {downloadItem.Artist}");
            sb.AppendLine($"Downloaded: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Quality: {downloadItem.Bitrate}");
            sb.AppendLine($"Tidal Album ID: {albumId}");

            // Add Tidal URL if available
            string tidalUrl = GetTidalAlbumUrl(albumId);
            if (!string.IsNullOrEmpty(tidalUrl))
            {
                sb.AppendLine($"Tidal URL: {tidalUrl}");
            }

            sb.AppendLine();

            // Add album metadata if available
            if (albumMetadata != null && albumMetadata.Count > 0)
            {
                sb.AppendLine("ALBUM METADATA");
                sb.AppendLine("==============");
                sb.AppendLine();

                // Try to extract and add common album metadata
                try
                {
                    AddMetadataIfExists(sb, albumMetadata, "releaseDate", "Release Date");
                    AddMetadataIfExists(sb, albumMetadata, "copyright", "Copyright");
                    AddMetadataIfExists(sb, albumMetadata, "numberOfTracks", "Number of Tracks");

                    // Format duration if available
                    if (albumMetadata["duration"] != null)
                    {
                        try
                        {
                            int seconds = albumMetadata["duration"].Value<int>();
                            TimeSpan duration = TimeSpan.FromSeconds(seconds);
                            sb.AppendLine($"Duration: {duration:hh\\:mm\\:ss}");
                        }
                        catch
                        {
                            // If parsing fails, just add the raw value
                            sb.AppendLine($"Duration: {albumMetadata["duration"]}");
                        }
                    }

                    AddMetadataIfExists(sb, albumMetadata, "audioQuality", "Audio Quality");
                    AddMetadataIfExists(sb, albumMetadata, "audioModes", "Audio Modes");
                    AddMetadataIfExists(sb, albumMetadata, "explicit", "Explicit");
                    AddMetadataIfExists(sb, albumMetadata, "popularity", "Popularity");
                    AddMetadataIfExists(sb, albumMetadata, "label", "Label");
                    AddMetadataIfExists(sb, albumMetadata, "upc", "UPC");

                    // Add genres if available
                    if (albumMetadata["genres"] != null && albumMetadata["genres"].Type == JTokenType.Array)
                    {
                        var genres = albumMetadata["genres"].ToObject<string[]>();
                        if (genres != null && genres.Length > 0)
                        {
                            sb.AppendLine($"Genres: {string.Join(", ", genres)}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Error extracting album metadata: {ex.Message}");
                }

                sb.AppendLine();
            }

            // Add track list
            sb.AppendLine("TRACK LIST");
            sb.AppendLine("===========");
            sb.AppendLine();

            if (tracks != null && tracks.Count > 0)
            {
                int trackNum = 1;
                foreach (var track in tracks)
                {
                    string trackTitle = track["title"]?.ToString() ?? $"Track {trackNum}";
                    string trackArtist = GetTrackArtist(track, downloadItem.Artist);
                    string trackDuration = GetFormattedTrackDuration(track);

                    // Add track info
                    sb.AppendLine($"{trackNum}. {trackTitle}{trackDuration}");

                    // Add featured artists if different from main artist
                    if (trackArtist != downloadItem.Artist && !string.IsNullOrEmpty(trackArtist))
                    {
                        sb.AppendLine($"   Artist: {trackArtist}");
                    }

                    // Add track-specific metadata
                    AddTrackMetadata(sb, track);

                    trackNum++;
                }
            }
            else
            {
                sb.AppendLine("No track information available");
            }

            sb.AppendLine();
            sb.AppendLine("DOWNLOAD INFORMATION");
            sb.AppendLine("====================");
            sb.AppendLine();
            sb.AppendLine($"Downloaded by: Lidarr Tidal Plugin");

            // Add download time if available
            if (downloadItem.StartTime != DateTime.MinValue && downloadItem.EndTime != DateTime.MinValue)
            {
                TimeSpan downloadTime = downloadItem.EndTime.HasValue ? (downloadItem.EndTime.Value - downloadItem.StartTime) : TimeSpan.Zero;
                sb.AppendLine($"Download Started: {downloadItem.StartTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Download Completed: {downloadItem.EndTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Download Time: {downloadTime.TotalMinutes:F1} minutes");
            }

            sb.AppendLine($"Download Status: {downloadItem.Status}");
            sb.AppendLine($"Completed Tracks: {downloadItem.CompletedTracks}/{downloadItem.TotalTracks}");

            // Add failed tracks information if any
            if (downloadItem.FailedTracks != null && downloadItem.FailedTracks.Length > 0)
            {
                sb.AppendLine($"Failed Tracks: {downloadItem.FailedTracks.Length}");
                sb.AppendLine($"Failed Track Numbers: {string.Join(", ", downloadItem.FailedTracks)}");
            }

            // Add validation information (placeholder for future expansion)
            sb.AppendLine();
            sb.AppendLine("VALIDATION INFORMATION");
            sb.AppendLine("=====================");
            sb.AppendLine();
            sb.AppendLine("No validation information available");

            return sb.ToString();
        }

        /// <summary>
        /// Adds metadata from a JObject to the StringBuilder if the property exists
        /// </summary>
        /// <param name="sb">The StringBuilder to append to</param>
        /// <param name="metadata">The metadata JObject</param>
        /// <param name="propertyName">The property name to look for</param>
        /// <param name="displayName">The display name for the property</param>
        private void AddMetadataIfExists(StringBuilder sb, JObject metadata, string propertyName, string displayName)
        {
            if (metadata[propertyName] != null)
            {
                sb.AppendLine($"{displayName}: {metadata[propertyName]}");
            }
        }

        /// <summary>
        /// Gets the artist name from a track JObject
        /// </summary>
        /// <param name="track">The track JObject</param>
        /// <param name="defaultArtist">The default artist name to use if not found</param>
        /// <returns>The artist name</returns>
        private string GetTrackArtist(JObject track, string defaultArtist)
        {
            try
            {
                // Try to get artist from different possible locations in the JSON
                if (track["artist"]?["name"] != null)
                {
                    return track["artist"]["name"].ToString();
                }

                if (track["artists"] != null && track["artists"].Type == JTokenType.Array)
                {
                    var artists = track["artists"].ToObject<JArray>();
                    if (artists != null && artists.Count > 0 && artists[0]["name"] != null)
                    {
                        return artists[0]["name"].ToString();
                    }
                }
            }
            catch
            {
                // Ignore errors and return default
            }

            return defaultArtist;
        }

        /// <summary>
        /// Gets a formatted duration string from a track JObject
        /// </summary>
        /// <param name="track">The track JObject</param>
        /// <returns>The formatted duration string</returns>
        private string GetFormattedTrackDuration(JObject track)
        {
            try
            {
                if (track["duration"] != null)
                {
                    int seconds = track["duration"].Value<int>();
                    TimeSpan duration = TimeSpan.FromSeconds(seconds);
                    return $" ({duration:mm\\:ss})";
                }
            }
            catch
            {
                // Ignore duration parsing errors
            }

            return "";
        }

        /// <summary>
        /// Adds track-specific metadata to the StringBuilder
        /// </summary>
        /// <param name="sb">The StringBuilder to append to</param>
        /// <param name="track">The track JObject</param>
        private void AddTrackMetadata(StringBuilder sb, JObject track)
        {
            try
            {
                // Add track number if available
                if (track["trackNumber"] != null)
                {
                    int trackNumber = track["trackNumber"].Value<int>();
                    if (trackNumber > 0)
                    {
                        sb.AppendLine($"   Track Number: {trackNumber}");
                    }
                }

                // Add volume/disc number if available
                if (track["volumeNumber"] != null)
                {
                    int volumeNumber = track["volumeNumber"].Value<int>();
                    if (volumeNumber > 1) // Only show if it's not disc 1
                    {
                        sb.AppendLine($"   Disc: {volumeNumber}");
                    }
                }

                // Add explicit flag if available and true
                if (track["explicit"] != null && track["explicit"].Value<bool>())
                {
                    sb.AppendLine($"   Explicit: Yes");
                }

                // Add ISRC if available
                if (track["isrc"] != null)
                {
                    sb.AppendLine($"   ISRC: {track["isrc"]}");
                }

                // Add copyright if available and different from album copyright
                if (track["copyright"] != null)
                {
                    sb.AppendLine($"   Copyright: {track["copyright"]}");
                }
            }
            catch
            {
                // Ignore errors in track metadata extraction
            }
        }

        /// <summary>
        /// Gets the Tidal album URL from an album ID
        /// </summary>
        /// <param name="albumId">The Tidal album ID</param>
        /// <returns>The Tidal album URL</returns>
        private string GetTidalAlbumUrl(string albumId)
        {
            if (string.IsNullOrWhiteSpace(albumId))
            {
                return null;
            }

            return $"https://listen.tidal.com/album/{albumId}";
        }
    }
}
