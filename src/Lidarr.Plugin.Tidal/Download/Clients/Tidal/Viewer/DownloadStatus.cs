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
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public string Status { get; set; }
        public DateTime Timestamp { get; set; }
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

        private readonly string _statusFilePath;
        private readonly Logger _logger;
        private readonly Timer _updateTimer;
        private readonly object _lock = new object();
        private DownloadStatus _status;

        public DownloadStatusManager(string baseDirectory, string statusFilesPath, Logger logger)
        {
            _logger = logger;
            _logger.Info($"Initializing DownloadStatusManager with baseDir={baseDirectory}, statusFilesPath={statusFilesPath}");
            
            string dataDirectory;
            
            // Use the user-specified status files path if provided
            if (!string.IsNullOrWhiteSpace(statusFilesPath))
            {
                dataDirectory = statusFilesPath;
                _logger.Info($"Using custom status file path: {dataDirectory}");
            }
            else
            {
                // Fall back to the default location
                dataDirectory = Path.Combine(baseDirectory, "TidalDownloadViewer");
                _logger.Info($"Using default status file path: {dataDirectory}");
            }
            
            try
            {
                // Handle Docker path conversion if needed
                if (Path.DirectorySeparatorChar == '\\' && dataDirectory.StartsWith("/"))
                {
                    _logger.Info("Detected Windows system with Linux-style paths (Docker scenario)");
                    dataDirectory = dataDirectory.TrimStart('/').Replace('/', '\\');
                    _logger.Info($"Converted path to: {dataDirectory}");
                }

                _logger.Info($"Checking if directory exists: {dataDirectory}");
                if (!Directory.Exists(dataDirectory))
                {
                    _logger.Info($"Creating directory: {dataDirectory}");
                    Directory.CreateDirectory(dataDirectory);
                    
                    // Test file creation
                    var testPath = Path.Combine(dataDirectory, "directory_test.txt");
                    File.WriteAllText(testPath, "Directory creation test");
                    _logger.Info($"Test file created at: {testPath}");
                    
                    // Clean up the test file
                    try
                    {
                        if (File.Exists(testPath))
                        {
                            File.Delete(testPath);
                            _logger.Debug($"Deleted temporary test file: {testPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"Failed to delete test file (non-critical): {ex.Message}");
                    }
                }
                
                _statusFilePath = Path.Combine(dataDirectory, "status.json");
                _logger.Info($"Status file path set to: {_statusFilePath}");
                _status = LoadOrCreateStatus();
                
                // Update the status file every 15 seconds
                _updateTimer = new Timer(UpdateStatusFile, null, TimeSpan.Zero, TimeSpan.FromSeconds(15));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to initialize download status manager with path {dataDirectory}. Status tracking will be disabled.");
                // Initialize with defaults to prevent null reference exceptions
                _statusFilePath = null;
                _status = new DownloadStatus();
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
                    _logger.Warn($"Status directory was deleted, recreating: {directory}");
                    Directory.CreateDirectory(directory);
                }
                
                if (File.Exists(_statusFilePath))
                {
                    string json = File.ReadAllText(_statusFilePath);
                    return JsonSerializer.Deserialize<DownloadStatus>(json) ?? new DownloadStatus 
                    { 
                        LastUpdated = DateTime.UtcNow 
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading download status file");
            }
            
            return new DownloadStatus
            {
                LastUpdated = DateTime.UtcNow,
                PluginVersion = typeof(NzbDrone.Core.Plugins.TidalPlugin).Assembly.GetName().Version.ToString()
            };
        }

        private void UpdateStatusFile(object state)
        {
            try
            {
                lock (_lock)
                {
                    _status.LastUpdated = DateTime.UtcNow;
                    string json = JsonSerializer.Serialize(_status, _jsonOptions);
                    
                    // Check if directory exists, create if not
                    string directory = Path.GetDirectoryName(_statusFilePath);
                    if (!Directory.Exists(directory))
                    {
                        _logger.Warn($"Status directory was deleted, recreating: {directory}");
                        Directory.CreateDirectory(directory);
                    }
                    
                    File.WriteAllText(_statusFilePath, json);
                    _logger.Debug($"Status file updated successfully: {_statusFilePath}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating download status file");
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

        public void AddCompletedTrack(string title, string artist, string album)
        {
            lock (_lock)
            {
                // Keep only the most recent 100 downloads
                if (_status.RecentDownloads.Count >= 100)
                {
                    _status.RecentDownloads.RemoveAt(0);
                }

                _status.RecentDownloads.Add(new DownloadTrackInfo
                {
                    Title = title,
                    Artist = artist,
                    Album = album,
                    Status = "Completed",
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        public void AddFailedTrack(string title, string artist, string album)
        {
            lock (_lock)
            {
                // Keep only the most recent 100 downloads
                if (_status.RecentDownloads.Count >= 100)
                {
                    _status.RecentDownloads.RemoveAt(0);
                }

                _status.RecentDownloads.Add(new DownloadTrackInfo
                {
                    Title = title,
                    Artist = artist,
                    Album = album,
                    Status = "Failed",
                    Timestamp = DateTime.UtcNow
                });
            }
        }
    }
}

