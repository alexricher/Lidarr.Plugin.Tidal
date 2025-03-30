using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Download.Clients.Tidal.Viewer
{
    public class DownloadArtistInfo
    {
        public string ArtistName { get; set; }
        public int PendingTracks { get; set; }
        public int CompletedTracks { get; set; }
        public int FailedTracks { get; set; }
        public List<string> Albums { get; set; } = new List<string>();
    }

    public class DownloadTrackInfo
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public string Status { get; set; }
        public DateTime Timestamp { get; set; }
        public string Bitrate { get; set; }
        public string Format { get; set; }
        public long Size { get; set; }
        public string OutputPath { get; set; }
        public bool HasLyrics { get; set; }
        public bool IsExplicit { get; set; }
        public int TrackNumber { get; set; }
    }

    public class DownloadStatus
    {
        public string PluginVersion { get; set; }
        public DateTime LastUpdated { get; set; }
        public int TotalPendingDownloads { get; set; }
        public int TotalCompletedDownloads { get; set; }
        public int TotalFailedDownloads { get; set; }
        public int DownloadRate { get; set; } // Downloads per hour
        public int SessionsCompleted { get; set; }
        public bool IsHighVolumeMode { get; set; }
        public Dictionary<string, DownloadArtistInfo> ArtistStats { get; set; } = new Dictionary<string, DownloadArtistInfo>();
        public List<DownloadTrackInfo> RecentDownloads { get; set; } = new List<DownloadTrackInfo>();
    }

    public class DownloadStatusManager
    {
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private string _statusFilePath;
        private readonly Logger _logger;
        private readonly Timer _updateTimer;
        private readonly object _lock = new object();
        private DownloadStatus _status;

        // Maximum number of recent downloads to keep in the status file
        private const int MaxRecentDownloads = 500;

        public DownloadStatusManager(string baseDirectory, string statusFilesPath, Logger logger)
        {
            _logger = logger;
            _logger?.Info($"Initializing DownloadStatusManager with baseDir={baseDirectory}, statusFilesPath={statusFilesPath}");
            
            string dataDirectory;
            
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
                    _logger?.Info("Detected Windows system with Linux-style paths (Docker scenario)");
                    dataDirectory = dataDirectory.TrimStart('/').Replace('/', '\\');
                    _logger?.Info($"Converted path to: {dataDirectory}");
                }

                // Ensure directory exists with retry mechanism
                int retryCount = 0;
                bool directoryCreated = false;
                Exception lastException = null;
                
                while (retryCount < 3 && !directoryCreated)
                {
                    try
                    {
                        _logger?.Info($"Checking if directory exists (attempt {retryCount+1}/3): {dataDirectory}");
                        if (!Directory.Exists(dataDirectory))
                        {
                            _logger?.Info($"Creating directory: {dataDirectory}");
                            Directory.CreateDirectory(dataDirectory);
                        }
                        
                        // Test file creation to verify write permissions
                        var testPath = Path.Combine(dataDirectory, $"directory_test_{Guid.NewGuid()}.tmp");
                        File.WriteAllText(testPath, "Directory creation test");
                        _logger?.Info($"Test file created at: {testPath}");
                        
                        // Clean up the test file
                        try
                        {
                            if (File.Exists(testPath))
                            {
                                File.Delete(testPath);
                                _logger?.Debug($"Deleted temporary test file: {testPath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.Debug($"Failed to delete test file (non-critical): {ex.Message}");
                        }
                        
                        directoryCreated = true;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        retryCount++;
                        _logger?.Warn(ex, $"Failed to create/verify directory (attempt {retryCount}/3): {ex.Message}");
                        
                        if (retryCount < 3)
                        {
                            // Wait before retrying
                            Thread.Sleep(500);
                        }
                    }
                }
                
                if (!directoryCreated)
                {
                    throw new IOException($"Failed to create or verify directory after multiple attempts: {dataDirectory}", lastException);
                }
                
                _statusFilePath = Path.Combine(dataDirectory, "status.json");
                _logger?.Info($"Status file path set to: {_statusFilePath}");
                _status = LoadOrCreateStatus();
                
                // Update the status file every 15 seconds
                _updateTimer = new Timer(UpdateStatusFile, null, TimeSpan.Zero, TimeSpan.FromSeconds(15));
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to initialize download status manager with path {dataDirectory}. Status tracking will be disabled.");
                // Initialize with defaults to prevent null reference exceptions
                _statusFilePath = null;
                _status = new DownloadStatus
                {
                    LastUpdated = DateTime.UtcNow,
                    PluginVersion = typeof(NzbDrone.Core.Plugins.TidalPlugin).Assembly.GetName().Version.ToString()
                };
            }
        }

        private DownloadStatus LoadOrCreateStatus()
        {
            try
            {
                // Check if directory exists, create if not
                string directory = Path.GetDirectoryName(_statusFilePath);
                if (!Directory.Exists(directory))
                {
                    _logger?.Warn($"Status directory was deleted, recreating: {directory}");
                    Directory.CreateDirectory(directory);
                }
                
                if (File.Exists(_statusFilePath))
                {
                    try
                    {
                        string json = File.ReadAllText(_statusFilePath);
                        var result = JsonSerializer.Deserialize<DownloadStatus>(json);
                        
                        if (result != null)
                        {
                            _logger?.Info($"🔄 Loaded download status file with {result.RecentDownloads?.Count ?? 0} recent downloads");
                            return result;
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        // Handle corrupted JSON file - create backup and start fresh
                        string backupPath = _statusFilePath + $".corrupted.{DateTime.Now:yyyyMMdd-HHmmss}.bak";
                        _logger?.Error(jsonEx, $"💥 Error parsing download status file: {jsonEx.Message}");
                        _logger?.Warn($"⚠️ Creating backup of corrupted status file at: {backupPath}");
                        
                        try
                        {
                            // Copy corrupted file for debugging
                            File.Copy(_statusFilePath, backupPath);
                            
                            // Delete corrupted file
                            File.Delete(_statusFilePath);
                            
                            _logger?.Info("🔄 Starting with fresh status file after backup");
                        }
                        catch (Exception copyEx)
                        {
                            _logger?.Error(copyEx, "Failed to backup corrupted status file");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Error(ex, $"Unexpected error loading status file: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "💥 Error accessing download status file");
            }
            
            // Create a new status object with the current timestamp and plugin version
            var newStatus = new DownloadStatus
            {
                LastUpdated = DateTime.UtcNow,
                PluginVersion = typeof(NzbDrone.Core.Plugins.TidalPlugin).Assembly.GetName().Version.ToString()
            };
            
            _logger?.Info("📄 Creating new download status tracking file");
            return newStatus;
        }

        private void UpdateStatusFile(object state)
        {
            if (string.IsNullOrEmpty(_statusFilePath))
            {
                return; // Skip if status file path is not available
            }
            
            try
            {
                lock (_lock)
                {
                    // Update timestamp
                    _status.LastUpdated = DateTime.UtcNow;
                    
                    // Ensure plugin version is set
                    if (string.IsNullOrEmpty(_status.PluginVersion))
                    {
                        _status.PluginVersion = typeof(NzbDrone.Core.Plugins.TidalPlugin).Assembly.GetName().Version.ToString();
                    }

                    // Check if directory exists, create if not
                    string directory = Path.GetDirectoryName(_statusFilePath);
                    if (!Directory.Exists(directory))
                    {
                        _logger?.Warn($"⚠️ Status directory was deleted, recreating: {directory}");
                        Directory.CreateDirectory(directory);
                    }
                    
                    // First, create a temp file
                    string tempFile = _statusFilePath + ".tmp";
                    string backupFile = _statusFilePath + ".bak";

                    try
                    {
                        // Serialize to JSON
                        string json = JsonSerializer.Serialize(_status, _jsonOptions);
                        
                        // Write to temp file first
                        File.WriteAllText(tempFile, json);
                        
                        // If original file exists, create backup (overwrite any existing backup)
                        if (File.Exists(_statusFilePath))
                        {
                            try
                            {
                                // Use replace to overwrite any existing backup
                                File.Copy(_statusFilePath, backupFile, true);
                            }
                            catch (Exception backupEx)
                            {
                                // Log but continue - backup is nice to have, not critical
                                _logger?.Debug($"Failed to create backup (non-critical): {backupEx.Message}");
                            }
                        }
                        
                        // Replace original file with temp file - this is atomic on most file systems
                        File.Move(tempFile, _statusFilePath, true);
                        
                        _logger?.Debug($"💾 Status file updated: {_statusFilePath}");
                    }
                    finally
                    {
                        // Clean up temp file if it still exists
                        try
                        {
                            if (File.Exists(tempFile))
                            {
                                File.Delete(tempFile);
                            }
                        }
                        catch { /* Ignore cleanup errors */ }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"❌ Error updating download status file: {ex.Message}");
            }
        }

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

        // Simple version called from DownloadTaskQueue
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

        // Simple version called from DownloadTaskQueue
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
        /// Reinitializes the status manager with a new path, preserving current status data
        /// </summary>
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
            try
            {
                if (string.IsNullOrEmpty(_statusFilePath))
                {
                    _logger?.Warn("Cannot test write with empty status file path");
                    return false;
                }

                string directory = Path.GetDirectoryName(_statusFilePath);
                if (string.IsNullOrEmpty(directory))
                {
                    _logger?.Warn("Cannot determine directory from status file path: {0}", _statusFilePath);
                    return false;
                }

                string testFilePath = Path.Combine(directory, $"test_write_{DateTime.Now.Ticks}.tmp");
                
                _logger?.Debug("Testing write access by creating file: {0}", testFilePath);
                
                // Try to create and write to a test file
                using (var fs = new FileStream(testFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(fs))
                {
                    writer.WriteLine("Test write access");
                    writer.Flush();
                }
                
                // If successful, delete the test file
                if (File.Exists(testFilePath))
                {
                    _logger?.Debug("Test write succeeded, cleaning up test file");
                    File.Delete(testFilePath);
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error testing write access to status file path: {0}", _statusFilePath);
                return false;
            }
        }

        /// <summary>
        /// Gets the current directory where status files are stored.
        /// </summary>
        /// <returns>The directory path or null if not set.</returns>
        public string GetStatusDirectory()
        {
            if (string.IsNullOrEmpty(_statusFilePath))
            {
                return null;
            }
            
            return Path.GetDirectoryName(_statusFilePath);
        }
    }
}







