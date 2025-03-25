using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download.Clients.Tidal.Queue;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using TidalSharp.Data;

namespace NzbDrone.Core.Download.Clients.Tidal
{
    public interface ITidalProxy
    {
        List<DownloadClientItem> GetQueue(TidalSettings settings);
        Task<string> Download(RemoteAlbum remoteAlbum, TidalSettings settings);
        void RemoveFromQueue(string downloadId, TidalSettings settings);
        void TestConnection(TidalSettings settings);
    }

    public class TidalProxy : ITidalProxy
    {
        private readonly ICached<DateTime?> _startTimeCache;
        private DownloadTaskQueue _taskQueue;
        private readonly Logger _logger;
        private bool _isInitialized = false;
        private readonly object _initLock = new object();

        public TidalProxy(ICacheManager cacheManager, Logger logger)
        {
            _startTimeCache = cacheManager.GetCache<DateTime?>(GetType(), "startTime");
            _logger = logger;
            // We'll initialize the task queue with the user's settings in GetQueue or Download
        }

        private void EnsureInitialized(TidalSettings settings)
        {
            if (_isInitialized) return;

            lock (_initLock)
            {
                if (_isInitialized) return;

                _logger.Info($"Initializing TidalProxy with status files path: {settings.StatusFilesPath}");
                _taskQueue = new DownloadTaskQueue(10, settings, _logger);
                _taskQueue.StartQueueHandler();
                _isInitialized = true;
            }
        }

        public List<DownloadClientItem> GetQueue(TidalSettings settings)
        {
            EnsureInitialized(settings);
            _taskQueue.SetSettings(settings);

            var items = _taskQueue.GetQueueListing();
            var result = new List<DownloadClientItem>();

            foreach (var item in items)
            {
                result.Add(ToDownloadClientItem(item));
            }

            return result;
        }

        public void RemoveFromQueue(string downloadId, TidalSettings settings)
        {
            EnsureInitialized(settings);
            _taskQueue.SetSettings(settings);

            var item = _taskQueue.GetQueueListing().FirstOrDefault(a => a.ID == downloadId);
            if (item != null)
                _taskQueue.RemoveItem(item);
        }

        public async Task<string> Download(RemoteAlbum remoteAlbum, TidalSettings settings)
        {
            EnsureInitialized(settings);
            _taskQueue.SetSettings(settings);

            var downloadItem = await DownloadItem.From(remoteAlbum);
            await _taskQueue.QueueBackgroundWorkItemAsync(downloadItem);
            return downloadItem.ID;
        }

        private DownloadClientItem ToDownloadClientItem(IDownloadItem x)
        {
            var format = x.Bitrate switch
            {
                AudioQuality.LOW => "AAC (M4A) 96kbps",
                AudioQuality.HIGH => "AAC (M4A) 320kbps",
                AudioQuality.LOSSLESS => "FLAC (M4A) Lossless",
                AudioQuality.HI_RES_LOSSLESS => "FLAC (M4A) 24bit Lossless",
                _ => throw new NotImplementedException(),
            };

            var title = $"{x.Artist} - {x.Title} [WEB] [{format}]";
            if (x.Explicit)
            {
                title += " [Explicit]";
            }

            var item = new DownloadClientItem
            {
                DownloadId = x.ID,
                Title = title,
                TotalSize = x.TotalSize,
                RemainingSize = x.TotalSize - x.DownloadedSize,
                RemainingTime = GetRemainingTime(x),
                Status = x.Status,
                CanMoveFiles = true,
                CanBeRemoved = true,
            };

            if (x.DownloadFolder.IsNotNullOrWhiteSpace())
            {
                item.OutputPath = new OsPath(x.DownloadFolder);
            }

            return item;
        }

        private TimeSpan? GetRemainingTime(IDownloadItem x)
        {
            if (x.TotalSize <= 0 || x.DownloadedSize <= 0)
                return null;

            // Estimate based on progress so far
            var elapsedTime = DateTime.UtcNow - _startTimeCache.Get("download", () => DateTime.UtcNow);
            var progress = x.DownloadedSize / (float)x.TotalSize;

            if (progress <= 0)
                return null;

            // Check if elapsedTime is null before accessing Ticks
            if (elapsedTime == null)
                return null;

            var estimatedTotalTime = TimeSpan.FromTicks((long)(elapsedTime.Value.Ticks / progress));
            var remainingTime = estimatedTotalTime - elapsedTime.Value;

            return remainingTime > TimeSpan.Zero ? remainingTime : TimeSpan.Zero;
        }

        public void TestConnection(TidalSettings settings)
        {
            _logger.Debug("Testing Tidal connection and settings...");
            
            // Validate settings
            if (string.IsNullOrWhiteSpace(settings.DownloadPath))
            {
                throw new ArgumentException("Download path must be configured");
            }
            
            // Test download path exists and is writable
            if (!System.IO.Directory.Exists(settings.DownloadPath))
            {
                try
                {
                    _logger.Debug($"Creating download directory: {settings.DownloadPath}");
                    System.IO.Directory.CreateDirectory(settings.DownloadPath);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException($"Download path does not exist and cannot be created: {ex.Message}");
                }
            }
            
            // Test write permissions by creating a temporary test file
            try
            {
                var testFile = System.IO.Path.Combine(settings.DownloadPath, $"tidal_test_{Guid.NewGuid()}.tmp");
                System.IO.File.WriteAllText(testFile, "Tidal connection test");
                System.IO.File.Delete(testFile);
                _logger.Debug("Successfully verified write permissions on download path");
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Cannot write to download path: {ex.Message}");
            }
            
            // Test status files path if configured
            if (!string.IsNullOrWhiteSpace(settings.StatusFilesPath))
            {
                try
                {
                    settings.ValidateStatusFilePath(_logger);
                    _logger.Debug("Successfully validated status files path");
                }
                catch (Exception ex)
                {
                    throw new ArgumentException($"Status files path validation failed: {ex.Message}");
                }
            }
            
            // Initialize components
            try
            {
                EnsureInitialized(settings);
                _logger.Debug("Successfully initialized Tidal proxy components");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to initialize Tidal components: {ex.Message}");
            }
            
            _logger.Info("Tidal connection test successful");
        }
    }
}
