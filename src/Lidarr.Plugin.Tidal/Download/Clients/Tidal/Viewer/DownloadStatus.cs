using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Tidal.Services.Logging;
using NLog;
using Lidarr.Plugin.Tidal.Services.FileSystem;
using NzbDrone.Core.Download.Clients.Tidal;
using NzbDrone.Core.Download.Clients.Tidal.API;
using NzbDrone.Plugin.Tidal;

namespace NzbDrone.Core.Download.Clients.Tidal.Viewer
{
    /// <summary>
    /// Represents information about an artist's download statistics.
    /// Used to track and display download information for a specific artist.
    /// </summary>
    public class DownloadArtistInfo
    {
        /// <summary>
        /// Gets or sets the name of the artist.
        /// </summary>
        public string ArtistName { get; set; }

        /// <summary>
        /// Gets or sets the number of pending track downloads for this artist.
        /// </summary>
        public int PendingTracks { get; set; }

        /// <summary>
        /// Gets or sets the number of successfully completed track downloads for this artist.
        /// </summary>
        public int CompletedTracks { get; set; }

        /// <summary>
        /// Gets or sets the number of failed track downloads for this artist.
        /// </summary>
        public int FailedTracks { get; set; }

        /// <summary>
        /// Gets or sets the list of albums being downloaded for this artist.
        /// </summary>
        public List<string> Albums { get; set; } = new List<string>();
    }

    /// <summary>
    /// Represents detailed information about a track download.
    /// Contains metadata about the track and its download status.
    /// </summary>
    public class DownloadTrackInfo
    {
        /// <summary>
        /// Gets or sets the unique identifier for this track download.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the title of the track.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets the name of the artist who performed the track.
        /// </summary>
        public string Artist { get; set; }

        /// <summary>
        /// Gets or sets the name of the album the track belongs to.
        /// </summary>
        public string Album { get; set; }

        /// <summary>
        /// Gets or sets the current status of the download (e.g., "Completed", "Failed", "Downloading").
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when this track was processed.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Gets or sets the bitrate of the downloaded track (e.g., "320kbps", "FLAC").
        /// </summary>
        public string Bitrate { get; set; }

        /// <summary>
        /// Gets or sets the audio format of the downloaded track (e.g., "MP3", "FLAC").
        /// </summary>
        public string Format { get; set; }

        /// <summary>
        /// Gets or sets the file size of the downloaded track in bytes.
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// Gets or sets the output file path where the track was saved.
        /// </summary>
        public string OutputPath { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the track includes synchronized lyrics.
        /// </summary>
        public bool HasLyrics { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the track has explicit content.
        /// </summary>
        public bool IsExplicit { get; set; }

        /// <summary>
        /// Gets or sets the track number within its album.
        /// </summary>
        public int TrackNumber { get; set; }
    }

    /// <summary>
    /// Represents information about a currently active download.
    /// Used to track and monitor downloads that are currently in progress.
    /// </summary>
    public class ActiveDownload
    {
        /// <summary>
        /// Gets or sets the unique identifier for this download.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets a detailed status message about the current state of the download.
        /// </summary>
        public string DetailedStatus { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when this download status was last updated.
        /// </summary>
        public DateTime LastUpdated { get; set; }
    }

    /// <summary>
    /// Represents the overall download status for the Tidal client.
    /// Contains statistics and information about all downloads, both past and present.
    /// This is the main data structure saved to the status JSON file.
    /// </summary>
    public class DownloadStatus
    {
        /// <summary>
        /// Gets or sets the plugin version.
        /// </summary>
        public string PluginVersion { get; set; }

        /// <summary>
        /// Gets or sets the last updated timestamp.
        /// </summary>
        public DateTime LastUpdated { get; set; }

        /// <summary>
        /// Gets or sets the total pending downloads.
        /// </summary>
        public int TotalPendingDownloads { get; set; }

        /// <summary>
        /// Gets or sets the total completed downloads.
        /// </summary>
        public int TotalCompletedDownloads { get; set; }

        /// <summary>
        /// Gets or sets the total failed downloads.
        /// </summary>
        public int TotalFailedDownloads { get; set; }

        /// <summary>
        /// Gets or sets the download rate per hour.
        /// </summary>
        public int DownloadRate { get; set; } // Downloads per hour

        /// <summary>
        /// Gets or sets the maximum allowed download rate per hour.
        /// </summary>
        public int MaxDownloadRate { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether downloads are currently rate limited.
        /// </summary>
        public bool IsRateLimited { get; set; }

        /// <summary>
        /// Gets or sets the timestamp until which the rate limit will be in effect.
        /// </summary>
        public DateTime? RateLimitedUntil { get; set; }

        /// <summary>
        /// Gets or sets a human-readable time string for when the rate limit will be lifted.
        /// </summary>
        public string TimeUntilRateLimitLifted { get; set; }

        /// <summary>
        /// Gets or sets the number of completed download sessions.
        /// </summary>
        public int SessionsCompleted { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether high volume mode is active.
        /// </summary>
        public bool IsHighVolumeMode { get; set; }

        /// <summary>
        /// Gets or sets the artist statistics.
        /// </summary>
        public Dictionary<string, DownloadArtistInfo> ArtistStats { get; set; } = new Dictionary<string, DownloadArtistInfo>();

        /// <summary>
        /// Gets or sets the recent downloads.
        /// </summary>
        public List<DownloadTrackInfo> RecentDownloads { get; set; } = new List<DownloadTrackInfo>();

        /// <summary>
        /// Gets or sets a list of currently active downloads.
        /// </summary>
        public List<ActiveDownload> ActiveDownloads { get; set; } = new List<ActiveDownload>();
    }

    /// <summary>
    /// Manages the creation, updating, and persistence of download status information.
    /// Provides methods to track downloads, update statistics, and write status to disk.
    /// Implements retry logic and handles Docker environments intelligently.
    /// </summary>
    public class DownloadStatusManager : IDisposable
    {
        /// <summary>
        /// JSON serialization options for writing status files, with pretty-printing enabled.
        /// </summary>
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        /// <summary>
        /// The full path to the status JSON file.
        /// </summary>
        private string _statusFilePath;

        /// <summary>
        /// Logger instance for diagnostic and operational logging.
        /// </summary>
        private readonly Logger _logger;

        /// <summary>
        /// Timer that triggers periodic writing of status information to disk.
        /// </summary>
        private readonly Timer _updateTimer;

        /// <summary>
        /// Lock object to ensure thread-safe access to the status data.
        /// </summary>
        private readonly object _lock = new object();

        /// <summary>
        /// The current download status data structure.
        /// </summary>
        private DownloadStatus _status;

        /// <summary>
        /// Maximum number of recent downloads to keep in the status file to prevent excessive growth.
        /// </summary>
        private const int MaxRecentDownloads = 500;

        private readonly IFileSystemService _fileSystemService;

        /// <summary>
        /// Initializes a new instance of the DownloadStatusManager class.
        /// Sets up status file paths, creates necessary directories, and initializes the status update timer.
        /// Includes Docker environment detection and fallback paths for containerized environments.
        /// </summary>
        /// <param name="baseDirectory">The base directory where Lidarr is installed.</param>
        /// <param name="statusFilesPath">Custom path for status files, if specified in settings.</param>
        /// <param name="logger">Logger for diagnostic and operational messages.</param>
        public DownloadStatusManager(string baseDirectory, string statusFilesPath, Logger logger)
        {
            _logger = logger ?? LogManager.GetLogger("DownloadStatusManager");
            _logger?.Info($"Initializing DownloadStatusManager with baseDir={baseDirectory}, statusFilesPath={statusFilesPath}");
            
            // Initialize the file system service first before using it
            _fileSystemService = new FileSystemService();

            string dataDirectory;
            string originalPath = statusFilesPath;
            bool isInDocker = IsRunningInDocker();

            // Use the user-specified status files path if provided
            if (!string.IsNullOrWhiteSpace(statusFilesPath))
            {
                dataDirectory = statusFilesPath;
                _logger?.Info($"Using custom status file path: {dataDirectory}");
            }
            else
            {
                // Fall back to the default location
                dataDirectory = Path.Combine(baseDirectory, "TidalDownloadViewer");
                _logger?.Info($"Using default status file path: {dataDirectory}");
            }

            try
            {
                // Handle Docker path conversion if needed
                if (Path.DirectorySeparatorChar == '\\' && dataDirectory.StartsWith("/"))
                {
                    _logger?.InfoWithEmoji(LogEmojis.System, "Detected Windows system with Linux-style paths (Docker scenario)");
                    dataDirectory = dataDirectory.TrimStart('/').Replace('/', '\\');
                    _logger?.InfoWithEmoji(LogEmojis.Folder, $"Converted path to: {dataDirectory}");
                }

                // Try to create/verify the directory
                bool directoryReady = TryEnsureDirectoryExists(dataDirectory);
                
                // If the directory couldn't be created and we're likely in Docker, try alternative paths
                if (!directoryReady && isInDocker)
                {
                    _logger?.InfoWithEmoji(LogEmojis.Warning, "Primary path failed and Docker environment detected. Trying common Docker writable paths.");
                    
                    // Try common Docker writable locations
                    var alternativePaths = new List<string>
                    {
                        "/config/tidal",            // Common Lidarr Docker config location
                        "/config/plugins/tidal",    // Nested in config
                        "/config/cache/tidal",      // Cache dir in config
                        "/downloads/tidal",         // Common downloads location
                        "/downloads/.tidal-status", // Hidden directory in downloads
                        "/tmp/lidarr/tidal",        // Temp directory (works in most containers)
                        "/var/tmp/lidarr/tidal"     // Alternative temp location
                    };
                    
                    foreach (var path in alternativePaths)
                    {
                        _logger?.InfoWithEmoji(LogEmojis.Folder, $"Trying alternative path: {path}");
                        if (TryEnsureDirectoryExists(path))
                        {
                            dataDirectory = path;
                            _logger?.InfoWithEmoji(LogEmojis.Success, $"Using alternative path: {dataDirectory} (original path {originalPath} was not writable)");
                            directoryReady = true;
                            break;
                        }
                    }
                }
                
                // If we still don't have a valid directory, use a fallback in temp
                if (!directoryReady)
                {
                    var tempPath = Path.Combine(Path.GetTempPath(), "Lidarr", "TidalStatus");
                    _logger?.InfoWithEmoji(LogEmojis.Warning, $"All alternative paths failed. Using temp directory as last resort: {tempPath}");
                    
                    if (TryEnsureDirectoryExists(tempPath))
                    {
                        dataDirectory = tempPath;
                        directoryReady = true;
                    }
                }
                
                // If all attempts failed, throw exception that will be caught in the outer catch block
                if (!directoryReady)
                {
                    throw new IOException($"Failed to create or find a writable directory for status files");
                }

                _statusFilePath = Path.Combine(dataDirectory, "status.json");
                _logger?.InfoWithEmoji(LogEmojis.File, $"Status file path set to: {_statusFilePath}");
                _status = LoadOrCreateStatus();

                // Update the status file every 15 seconds
                _updateTimer = new Timer(UpdateStatusFile, this, TimeSpan.Zero, TimeSpan.FromSeconds(15));
            }
            catch (Exception ex)
            {
                _logger?.ErrorWithEmoji(LogEmojis.Error, ex, $"Failed to initialize download status manager with path {dataDirectory}. Status tracking will be disabled.");
                // Initialize with defaults to prevent null reference exceptions
                _statusFilePath = null;
                _status = new DownloadStatus
                {
                    LastUpdated = DateTime.UtcNow,
                    PluginVersion = typeof(NzbDrone.Core.Plugins.TidalPlugin).Assembly.GetName().Version.ToString()
                };
            }
        }

        /// <summary>
        /// Loads the status from disk or creates a new status object if none exists.
        /// Handles corrupted files by creating backups and starting fresh.
        /// </summary>
        /// <returns>A DownloadStatus object, either loaded from disk or newly created.</returns>
        private DownloadStatus LoadOrCreateStatus()
        {
            try
            {
                // Ensure the data directory exists
                string dataDirectory = _statusFilePath;
                bool directoryReady = _fileSystemService.EnsureDirectoryExists(dataDirectory, 3, _logger);

                if (!directoryReady)
                {
                    _logger?.ErrorWithEmoji(LogEmojis.Error, $"Failed to create or access directory: {dataDirectory}");
                    return CreateNewStatus();
                }

                string statusFile = Path.Combine(dataDirectory, "status.json");
                string backupFile = Path.Combine(dataDirectory, "status_backup.json");

                if (!File.Exists(statusFile) && !File.Exists(backupFile))
                {
                    _logger?.InfoWithEmoji(LogEmojis.Info, "No existing status file found, creating new one");
                    return CreateNewStatus();
                }

                string fileToLoad = File.Exists(statusFile) ? statusFile : backupFile;
                _logger?.InfoWithEmoji(LogEmojis.Download, $"Loading status from: {fileToLoad}");

                try
                {
                    string json = File.ReadAllText(fileToLoad);
                    var status = JsonSerializer.Deserialize<DownloadStatus>(json, _jsonOptions);

                    if (status != null)
                    {
                        _logger?.InfoWithEmoji(LogEmojis.Success, $"Successfully loaded status with {status.RecentDownloads.Count} recent downloads");
                        status.PluginVersion = GetPluginVersion();
                        return status;
                    }
                    else
                    {
                        _logger?.ErrorWithEmoji(LogEmojis.Error, "Loaded status was null");
                        return CreateNewStatus();
                    }
                }
                catch (Exception ex)
                {
                    _logger?.ErrorWithEmoji(LogEmojis.Error, ex, $"Error loading status file: {fileToLoad}");
                    return CreateNewStatus();
                }
            }
            catch (Exception ex)
            {
                _logger?.ErrorWithEmoji(LogEmojis.Error, ex, "Error in LoadOrCreateStatus");
                return CreateNewStatus();
            }
        }

        /// <summary>
        /// Creates a new DownloadStatus object with default values
        /// </summary>
        private DownloadStatus CreateNewStatus()
        {
            var newStatus = new DownloadStatus
            {
                LastUpdated = DateTime.UtcNow,
                PluginVersion = GetPluginVersion()
            };

            _logger?.InfoWithEmoji(LogEmojis.File, "Creating new download status tracking file");
            return newStatus;
        }

        /// <summary>
        /// Gets the current plugin version
        /// </summary>
        private string GetPluginVersion()
        {
            try
            {
                // Try to get the assembly version
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name?.Contains("Tidal") == true);

                if (assembly != null)
                {
                    return assembly.GetName().Version.ToString();
                }
            }
            catch (Exception ex)
            {
                _logger?.DebugWithEmoji(LogEmojis.Error, "Error getting plugin version: " + ex.Message);
            }

            return "Unknown"; // Fallback version if we can't determine the actual version
        }

        /// <summary>
        /// Periodically saves the current status to the status file.
        /// Uses a safe write pattern with temporary files to prevent corruption.
        /// Creates backups of existing files before overwriting.
        /// </summary>
        /// <param name="state">State object passed by the Timer (not used).</param>
        private void UpdateStatusFile(object state)
        {
            if (_status == null)
            {
                _logger?.ErrorWithEmoji(LogEmojis.Error, "Status is null, cannot update status file");
                return;
            }

            try
            {
                lock (_lock)
                {
                    string dataDirectory = _statusFilePath;
                    bool directoryReady = _fileSystemService.EnsureDirectoryExists(dataDirectory, 3, _logger);

                    if (!directoryReady)
                    {
                        _logger?.ErrorWithEmoji(LogEmojis.Error, $"Failed to create or access directory: {dataDirectory}");
                        return;
                    }

                    // Update timestamp
                    _status.LastUpdated = DateTime.UtcNow;
                    
                    // Main status file
                    string statusFile = Path.Combine(dataDirectory, "status.json");
                    string tempFile = Path.Combine(dataDirectory, $"status_temp_{Guid.NewGuid()}.json");
                    string backupFile = Path.Combine(dataDirectory, "status_backup.json");

                    try
                    {
                        // Write to a temporary file first
                        string json = JsonSerializer.Serialize(_status, _jsonOptions);
                        File.WriteAllText(tempFile, json);

                        // If we have an existing status file, back it up before replacing
                        if (File.Exists(statusFile))
                        {
                            try
                            {
                                if (File.Exists(backupFile))
                                {
                                    File.Delete(backupFile);
                                }
                                File.Move(statusFile, backupFile);
                            }
                            catch (Exception ex)
                            {
                                _logger?.ErrorWithEmoji(LogEmojis.Warning, ex, "Failed to create backup of status file (non-critical)");
                                // Continue anyway - better to have a new file than no file
                            }
                        }

                        // Replace the real status file with our temporary one
                        File.Move(tempFile, statusFile);
                        _logger?.DebugWithEmoji(LogEmojis.Download, "Updated status file.");
                    }
                    catch (Exception ex)
                    {
                        _logger?.ErrorWithEmoji(LogEmojis.Error, ex, "Error writing to status file");
                    }
                    finally
                    {
                        // Clean up the temp file if it still exists
                        try
                        {
                            if (File.Exists(tempFile))
                            {
                                File.Delete(tempFile);
                            }
                        }
                        catch { /* ignore cleanup errors */ }
                    }

                    // Also save recent downloads list separately - this speeds up UI by avoiding
                    // loading entire status object when just the recent downloads need updating
                    try
                    {
                        string recentPath = Path.Combine(dataDirectory, "recent");
                        if (_fileSystemService.EnsureDirectoryExists(recentPath, 3, _logger))
                        {
                            string recentFile = Path.Combine(recentPath, "recent_downloads.json");
                            string tempRecentFile = Path.Combine(recentPath, $"recent_temp_{Guid.NewGuid()}.json");

                            string recentJson = JsonSerializer.Serialize(_status.RecentDownloads, _jsonOptions);
                            File.WriteAllText(tempRecentFile, recentJson);

                            if (File.Exists(recentFile))
                            {
                                File.Delete(recentFile);
                            }

                            File.Move(tempRecentFile, recentFile);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.ErrorWithEmoji(LogEmojis.Warning, ex, "Error writing recent downloads file (non-critical)");
                    }

                    // Similarly, save active downloads separately
                    try
                    {
                        string activePath = Path.Combine(dataDirectory, "active");
                        if (_fileSystemService.EnsureDirectoryExists(activePath, 3, _logger))
                        {
                            string activeFile = Path.Combine(activePath, "active_downloads.json");
                            string tempActiveFile = Path.Combine(activePath, $"active_temp_{Guid.NewGuid()}.json");

                            string activeJson = JsonSerializer.Serialize(_status.ActiveDownloads, _jsonOptions);
                            File.WriteAllText(tempActiveFile, activeJson);

                            if (File.Exists(activeFile))
                            {
                                File.Delete(activeFile);
                            }

                            File.Move(tempActiveFile, activeFile);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.ErrorWithEmoji(LogEmojis.Warning, ex, "Error writing active downloads file (non-critical)");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.ErrorWithEmoji(LogEmojis.Error, ex, "Error updating status file");
            }
        }

        /// <summary>
        /// Updates global queue statistics in the status object.
        /// </summary>
        /// <param name="pending">Number of pending downloads.</param>
        /// <param name="completed">Number of completed downloads.</param>
        /// <param name="failed">Number of failed downloads.</param>
        /// <param name="sessionsCompleted">Number of completed download sessions.</param>
        /// <param name="isHighVolumeMode">Whether high volume mode is active.</param>
        /// <param name="downloadRate">Current download rate (items/hour).</param>
        public void UpdateQueueStatistics(int pending, int completed, int failed, int sessionsCompleted, bool isHighVolumeMode, int downloadRate)
        {
            lock (_lock)
            {
                _status.TotalPendingDownloads = pending;
                _status.TotalCompletedDownloads = completed;
                _status.TotalFailedDownloads = failed;
                _status.SessionsCompleted = sessionsCompleted;
                _status.IsHighVolumeMode = isHighVolumeMode;
                _status.DownloadRate = downloadRate;
            }
        }

        /// <summary>
        /// Adds or updates an artist's download statistics in the status object.
        /// </summary>
        /// <param name="artistName">Name of the artist.</param>
        /// <param name="pending">Number of pending tracks for this artist.</param>
        /// <param name="completed">Number of completed tracks for this artist.</param>
        /// <param name="failed">Number of failed tracks for this artist.</param>
        /// <param name="albums">List of album names being downloaded for this artist.</param>
        public void AddOrUpdateArtist(string artistName, int pending, int completed, int failed, List<string> albums)
        {
            lock (_lock)
            {
                if (!_status.ArtistStats.TryGetValue(artistName, out var artist))
                {
                    artist = new DownloadArtistInfo
                    {
                        ArtistName = artistName
                    };
                    _status.ArtistStats[artistName] = artist;
                }

                artist.PendingTracks = pending;
                artist.CompletedTracks = completed;
                artist.FailedTracks = failed;
                artist.Albums = albums;
            }
        }

        /// <summary>
        /// Adds a completed track to the recent downloads list with minimal information.
        /// Uses a generated unique ID and default values for missing fields.
        /// </summary>
        /// <param name="title">Title of the track.</param>
        /// <param name="artist">Artist name.</param>
        /// <param name="album">Album name.</param>
        public void AddCompletedTrack(string title, string artist, string album)
        {
            AddCompletedTrackWithDetails(
                Guid.NewGuid().ToString(),  // Generate a unique ID
                title,
                artist,
                album,
                "Unknown",    // bitrate
                "Unknown",    // format
                0,            // size
                string.Empty, // outputPath
                false,        // hasLyrics
                false,        // isExplicit
                0);           // trackNumber
        }

        /// <summary>
        /// Adds a failed track to the recent downloads list with minimal information.
        /// Uses a generated unique ID and default values for missing fields.
        /// </summary>
        /// <param name="title">Title of the track.</param>
        /// <param name="artist">Artist name.</param>
        /// <param name="album">Album name.</param>
        public void AddFailedTrack(string title, string artist, string album)
        {
            AddFailedTrackWithDetails(
                Guid.NewGuid().ToString(),  // Generate a unique ID
                title,
                artist,
                album,
                "Unknown",    // bitrate
                "Unknown",    // format
                0,            // size
                string.Empty, // outputPath
                false,        // hasLyrics
                false,        // isExplicit
                0);           // trackNumber
        }

        public void AddCompletedTrackWithDetails(
            string id,
            string title,
            string artist,
            string album,
            string bitrate,
            string format,
            long size,
            string outputPath,
            bool hasLyrics,
            bool isExplicit,
            int trackNumber)
        {
            var track = new DownloadTrackInfo
            {
                Id = id,
                Title = title,
                Artist = artist,
                Album = album,
                Status = "Completed",
                Timestamp = DateTime.UtcNow,
                Bitrate = bitrate,
                Format = format,
                Size = size,
                OutputPath = outputPath,
                HasLyrics = hasLyrics,
                IsExplicit = isExplicit,
                TrackNumber = trackNumber
            };

            // Use the common method to add the track
            AddDownloadTrack(track);
        }

        public void AddFailedTrackWithDetails(
            string id,
            string title,
            string artist,
            string album,
            string bitrate,
            string format,
            long size,
            string outputPath,
            bool hasLyrics,
            bool isExplicit,
            int trackNumber)
        {
            var track = new DownloadTrackInfo
            {
                Id = id,
                Title = title,
                Artist = artist,
                Album = album,
                Status = "Failed",
                Timestamp = DateTime.UtcNow,
                Bitrate = bitrate,
                Format = format,
                Size = size,
                OutputPath = outputPath,
                HasLyrics = hasLyrics,
                IsExplicit = isExplicit,
                TrackNumber = trackNumber
            };

            // Use the common method to add the track
            AddDownloadTrack(track);
        }

        private void AddDownloadTrack(DownloadTrackInfo track)
        {
            lock (_lock)
            {
                // Add at the beginning (most recent first)
                _status.RecentDownloads.Insert(0, track);

                // Trim the list if it exceeds maximum size
                if (_status.RecentDownloads.Count > MaxRecentDownloads)
                {
                    _logger?.Debug($"📊 Trimming recent downloads list from {_status.RecentDownloads.Count} to {MaxRecentDownloads} items");
                    _status.RecentDownloads.RemoveRange(MaxRecentDownloads, _status.RecentDownloads.Count - MaxRecentDownloads);
                }
            }
        }

        /// <summary>
        /// Reinitializes the status manager with a new path, preserving current status data.
        /// Copies existing status file to the new location if possible.
        /// </summary>
        /// <param name="newStatusFilesPath">New path for status files.</param>
        public void ReinitializeWithPath(string newStatusFilesPath)
        {
            if (string.IsNullOrWhiteSpace(newStatusFilesPath))
            {
                _logger?.Warn("Cannot reinitialize status manager with empty path");
                return;
            }

            try
            {
                _logger?.Info($"Reinitializing status manager with new path: {newStatusFilesPath}");

                // Save a copy of current status
                var currentStatus = _status;

                // First try to create and validate the new directory
                string dataDirectory = newStatusFilesPath;

                // Handle Docker path conversion if needed
                if (Path.DirectorySeparatorChar == '\\' && dataDirectory.StartsWith("/"))
                {
                    _logger?.Info("Detected Windows system with Linux-style paths during reinitialization");
                    dataDirectory = dataDirectory.TrimStart('/').Replace('/', '\\');
                    _logger?.Info($"Converted path to: {dataDirectory}");
                }

                // Ensure directory exists and is writable
                if (!Directory.Exists(dataDirectory))
                {
                    _logger?.Info($"Creating directory during reinitialization: {dataDirectory}");
                    Directory.CreateDirectory(dataDirectory);
                }

                // Test write permission with a test file
                var testPath = Path.Combine(dataDirectory, $"write_test_{Guid.NewGuid()}.tmp");
                try
                {
                    File.WriteAllText(testPath, "Write permission test");
                    _logger?.Debug($"Successfully wrote test file to {testPath}");

                    // Clean up
                    if (File.Exists(testPath))
                    {
                        File.Delete(testPath);
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Directory {dataDirectory} is not writable: {ex.Message}", ex);
                }

                // Update status file path with the new path
                string oldPath = _statusFilePath;
                string newPath = Path.Combine(dataDirectory, "status.json");

                // Copy existing status data if possible
                if (!string.IsNullOrEmpty(oldPath) && File.Exists(oldPath) && oldPath != newPath)
                {
                    try
                    {
                        _logger?.Info($"Copying status data from {oldPath} to {newPath}");
                        File.Copy(oldPath, newPath, true);
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warn(ex, $"Failed to copy existing status file, will create new one: {ex.Message}");
                        // Continue with initialization - we'll save the in-memory status to the new path
                    }
                }

                // Update path and save current status
                lock (_lock)
                {
                    _statusFilePath = newPath;
                    _status = currentStatus;

                    // Force an immediate update to test the new path
                    UpdateStatusFile(null);
                }

                _logger?.Info($"Successfully reinitialized status manager with new path: {newPath}");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to reinitialize status manager with path {newStatusFilesPath}");
                // Don't change existing path if reinitialization fails
            }
        }

        /// <summary>
        /// Tests if the current status file path is writable by creating a test file.
        /// </summary>
        /// <returns>True if the path is writable, false otherwise.</returns>
        public bool TestWrite()
        {
            if (_logger != null)
            {
                _logger.DebugWithEmoji(LogEmojis.File, "Testing write access to status directory...");
            }

            if (!_fileSystemService.TestPathWritable(_statusFilePath, _logger))
            {
                _logger?.ErrorWithEmoji(LogEmojis.Error, $"Cannot write to status files directory: {_statusFilePath}");
                return false;
            }

            try
            {
                string testFile = Path.Combine(_statusFilePath, $"test_{Guid.NewGuid()}.json");
                var testData = new
                {
                    timestamp = DateTime.UtcNow,
                    test = true,
                    message = "Test write successful"
                };

                string json = JsonSerializer.Serialize(testData, _jsonOptions);
                File.WriteAllText(testFile, json);

                // Verify we can read it back
                string readBack = File.ReadAllText(testFile);
                var readData = JsonSerializer.Deserialize<JsonElement>(readBack);

                // Clean up
                File.Delete(testFile);

                _logger?.InfoWithEmoji(LogEmojis.Success, $"Test write successful to {_statusFilePath}");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.ErrorWithEmoji(LogEmojis.Error, ex, $"Failed to write test file to {_statusFilePath}");
                return false;
            }
        }

        /// <summary>
        /// Gets the current directory where status files are stored.
        /// </summary>
        /// <returns>The directory path or null if not set.</returns>
        public string GetStatusDirectory()
        {
            try
            {
                // Ensure the status directory exists and is writable
                _statusFilePath = _fileSystemService.ValidatePath(_statusFilePath, _logger);
                
                // Return the validated path
                return _statusFilePath;
            }
            catch (Exception ex)
            {
                _logger?.ErrorWithEmoji(LogEmojis.Error, ex, "Failed to get status directory");
                return _statusFilePath; // Return the existing path, even if it's not valid
            }
        }

        /// <summary>
        /// Adds or updates a detailed status report for a specific download.
        /// </summary>
        /// <param name="downloadId">Identifier of the download to update.</param>
        /// <param name="statusReport">Detailed status information to add.</param>
        public void AddDetailedStatusReport(string downloadId, string statusReport)
        {
            lock (_lock)
            {
                // Find the download in the active downloads
                var download = _status.ActiveDownloads.FirstOrDefault(d => d.Id == downloadId);
                if (download != null)
                {
                    download.DetailedStatus = statusReport;
                    download.LastUpdated = DateTime.UtcNow;
                }
            }
        }

        /// <summary>
        /// Updates the rate limiting information in the status file.
        /// This method provides visibility into the current download rate limits,
        /// which helps users understand why downloads might be throttled.
        /// </summary>
        /// <param name="currentRate">The current download rate per hour.</param>
        /// <param name="maxRate">The maximum allowed download rate per hour.</param>
        /// <param name="isRateLimited">Whether rate limiting is currently active.</param>
        /// <param name="timeUntilNextSlot">The time remaining until rate limiting is lifted, or null if not limited.</param>
        public void UpdateRateInformation(int currentRate, int maxRate, bool isRateLimited, TimeSpan? timeUntilNextSlot)
        {
            try
            {
                lock (_lock)
                {
                    _status.DownloadRate = currentRate;
                    _status.MaxDownloadRate = maxRate;
                    _status.IsRateLimited = isRateLimited;
                    _status.LastUpdated = DateTime.UtcNow;
                    
                    if (isRateLimited && timeUntilNextSlot.HasValue)
                    {
                        _status.RateLimitedUntil = DateTime.UtcNow.Add(timeUntilNextSlot.Value);
                        _status.TimeUntilRateLimitLifted = FormatTimeSpan(timeUntilNextSlot.Value);
                    }
                    else
                    {
                        _status.RateLimitedUntil = null;
                        _status.TimeUntilRateLimitLifted = null;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.ErrorWithEmoji(LogEmojis.Error, ex, "Failed to update rate information");
            }
        }

        // Helper method to format TimeSpan in a human-readable way
        private string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalMinutes < 1)
            {
                return $"{timeSpan.Seconds}s";
            }
            else if (timeSpan.TotalHours < 1)
            {
                return $"{timeSpan.Minutes}m {timeSpan.Seconds}s";
            }
            else
            {
                return $"{(int)timeSpan.TotalHours}h {timeSpan.Minutes}m";
            }
        }

        /// <summary>
        /// Disposes resources used by the download status manager.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes resources used by the download status manager.
        /// </summary>
        /// <param name="disposing">True if called from Dispose(), false if called from finalizer.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    // Dispose the update timer
                    _updateTimer?.Dispose();

                    _logger?.Debug("Download status manager disposed");
                }
                catch (ObjectDisposedException ex)
                {
                    _logger?.Debug(ex, "Timer was already disposed");
                }
                catch (InvalidOperationException ex)
                {
                    _logger?.Error(ex, "Error disposing timer");
                }
            }
        }

        /// <summary>
        /// Detects if the application is running in a Docker container.
        /// Checks for Docker-specific files and filesystem characteristics.
        /// </summary>
        /// <returns>True if running in Docker, false otherwise.</returns>
        private bool IsRunningInDocker()
        {
            return _fileSystemService.IsRunningInDocker(_logger);
        }

        /// <summary>
        /// Ensures a directory exists and is writable with retry logic.
        /// Creates the directory if it doesn't exist and tests write access.
        /// </summary>
        /// <param name="path">Path to the directory to create/verify.</param>
        /// <returns>True if the directory exists and is writable, false otherwise.</returns>
        private bool TryEnsureDirectoryExists(string path)
        {
            // Delegate to the file system service
            return _fileSystemService.EnsureDirectoryExists(path, 3, _logger);
        }
    }
}







