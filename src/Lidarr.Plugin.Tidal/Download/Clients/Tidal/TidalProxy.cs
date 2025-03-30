using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        Task<List<DownloadClientItem>> GetQueueAsync(TidalSettings settings, CancellationToken cancellationToken = default);
        Task<string> Download(RemoteAlbum remoteAlbum, TidalSettings settings, CancellationToken cancellationToken = default);
        Task RemoveFromQueueAsync(string downloadId, TidalSettings settings, CancellationToken cancellationToken = default);
        Task TestConnectionAsync(TidalSettings settings, CancellationToken cancellationToken = default);
        List<DownloadClientItem> GetQueue(TidalSettings settings);
        void RemoveFromQueue(string downloadId, TidalSettings settings);
        void TestConnection(TidalSettings settings);
        bool IsCircuitBreakerOpen();
        int GetPendingDownloadCount();
        TimeSpan GetCircuitBreakerReopenTime();
    }

    public class TidalProxy : ITidalProxy
    {
        private readonly ICached<DateTime?> _startTimeCache;
        private readonly ICached<DownloadTaskQueue> _taskQueueCache;
        private readonly Logger _logger;
        private readonly SemaphoreSlim _initSemaphore = new SemaphoreSlim(1, 1);
        private readonly TimeSpan _operationTimeout = TimeSpan.FromMinutes(10);
        private readonly TimeSpan _initTimeout = TimeSpan.FromSeconds(60);
        private static readonly SemaphoreSlim _globalThrottleSemaphore = new SemaphoreSlim(4, 4);
        // Track operations currently using the database
        private static int _activeOperations = 0;
        private static readonly object _operationsLock = new object();
        private static readonly List<CancellationTokenSource> _pendingOperationCts = new List<CancellationTokenSource>();
        // Maximum number of concurrent operations to avoid database contention
        private static readonly int _maxConcurrentOperations = 2;
        private static int _consecutiveFailures = 0;
        private static bool _circuitBreakerOpen = false;
        private static readonly object _circuitBreakerLock = new object();
        private static readonly TimeSpan _circuitBreakDuration = TimeSpan.FromMinutes(2);
        private static DateTime _circuitBreakerResetTime = DateTime.MinValue;
        // Queue for storing download operations that arrive during circuit breaker open state
        private static readonly Queue<PendingDownload> _pendingDownloads = new Queue<PendingDownload>();
        private static readonly SemaphoreSlim _pendingDownloadLock = new SemaphoreSlim(1, 1);
        private static bool _processingPendingDownloads = false;
        // Health monitoring
        private static DateTime _lastSuccessfulOperation = DateTime.UtcNow;
        private static readonly TimeSpan _healthCheckInterval = TimeSpan.FromMinutes(5);
        private static DateTime _lastHealthCheck = DateTime.MinValue;
        private static readonly TimeSpan _stalledOperationThreshold = TimeSpan.FromMinutes(15);
        private static readonly object _healthMonitorLock = new object();
        // Circuit breaker status update timer
        private static readonly TimeSpan _statusUpdateInterval = TimeSpan.FromSeconds(30);
        private static DateTime _lastStatusUpdate = DateTime.MinValue;

        // Structure to store pending downloads
        private class PendingDownload
        {
            public RemoteAlbum RemoteAlbum { get; set; }
            public TidalSettings Settings { get; set; }
            public DateTime QueuedTime { get; set; }
        }

        public TidalProxy(ICacheManager cacheManager, Logger logger)
        {
            _startTimeCache = cacheManager.GetCache<DateTime?>(GetType(), "startTime");
            _taskQueueCache = cacheManager.GetCache<DownloadTaskQueue>(GetType(), "taskQueue");
            _logger = logger;
        }

        // Health check method to detect and recover from stalled operations
        private void CheckSystemHealth()
        {
            lock (_healthMonitorLock)
            {
                // Only run health check periodically
                if (DateTime.UtcNow - _lastHealthCheck < _healthCheckInterval)
                {
                    return;
                }
                
                _lastHealthCheck = DateTime.UtcNow;
                
                // Check if there has been any successful operation in threshold period
                if (DateTime.UtcNow - _lastSuccessfulOperation > _stalledOperationThreshold)
                {
                    _logger.Warn($"System health check: No successful operations for {_stalledOperationThreshold.TotalMinutes} minutes. Performing recovery actions.");
                    
                    // Recovery actions:
                    
                    // 1. Reset database operations tracking
                    try
                    {
                        // First check if operations appear stuck
                        lock (_operationsLock)
                        {
                            if (_activeOperations > 0 || _pendingOperationCts.Count > 0)
                            {
                                _logger.Warn($"Database operations appear stuck: Active={_activeOperations}, Pending={_pendingOperationCts.Count}");
                                // Perform outside lock
                            }
                        }
                        
                        // Reset database operations tracking
                        ResetDatabaseOperations();
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error resetting database operations during health check");
                    }
                    
                    // 2. Reset throttle semaphore if locked
                    try 
                    {
                        // Check if semaphore is likely depleted
                        if (_globalThrottleSemaphore.CurrentCount == 0)
                        {
                            _logger.Warn("Global throttle semaphore appears to be depleted, releasing locks");
                            
                            // Try to reset to original count (4)
                            for (int i = 0; i < 4; i++)
                            {
                                try 
                                {
                                    _globalThrottleSemaphore.Release();
                                }
                                catch 
                                {
                                    // Ignore exceptions when releasing already fully released semaphore
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error resetting throttle semaphore during health check");
                    }
                    
                    // 3. Reset circuit breaker if needed
                    try
                    {
                        lock (_circuitBreakerLock)
                        {
                            _logger.Warn("Performing emergency circuit breaker reset due to stalled operations");
                            _circuitBreakerOpen = false;
                            _consecutiveFailures = 0;
                            
                            // Process any pending downloads after circuit breaker reset
                            Task.Run(async () => await ProcessPendingDownloads()).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error resetting circuit breaker during health check");
                    }
                    
                    // Update the last successful operation time to prevent continuous resets
                    _lastSuccessfulOperation = DateTime.UtcNow.AddMinutes(-(_stalledOperationThreshold.TotalMinutes / 2));
                }
            }
        }

        private void RecordSuccessfulOperation()
        {
            _lastSuccessfulOperation = DateTime.UtcNow;
        }

        // Add method to log queue status periodically
        private void LogQueueStatus()
        {
            var pendingCount = _pendingDownloads.Count;
            var isCircuitBreakerOpen = IsCircuitBreakerOpen();
            var timeRemaining = GetCircuitBreakerReopenTime();
            
            if (isCircuitBreakerOpen)
            {
                // More detailed message when circuit breaker is open
                var timeRemainingStr = timeRemaining.TotalHours >= 1
                    ? $"{(int)timeRemaining.TotalHours}h {timeRemaining.Minutes}m"
                    : $"{timeRemaining.Minutes}m {timeRemaining.Seconds}s";
                
                _logger.Info($"📊 QUEUE STATUS at {DateTime.Now:HH:mm:ss}: Circuit breaker OPEN, {timeRemainingStr} remaining until processing resumes, {pendingCount} downloads queued");
                
                // Add more detail about why processing is paused
                _logger.Info($"   ⏸️ Downloads are currently paused due to consecutive errors or rate limits");
                _logger.Info($"   ⏱️ Will automatically resume at {_circuitBreakerResetTime.ToLocalTime():HH:mm:ss}");
            }
            else if (pendingCount > 0)
            {
                _logger.Info($"📊 QUEUE STATUS at {DateTime.Now:HH:mm:ss}: Circuit breaker CLOSED, {pendingCount} downloads queued for processing");
            }
        }

        private async Task<DownloadTaskQueue> GetTaskQueueAsync(TidalSettings settings, CancellationToken cancellationToken = default)
        {
            var cacheKey = $"{settings.DownloadPath}@{settings.StatusFilesPath}";
            
            return await Task.Run(() => _taskQueueCache.Get(cacheKey, () => {
                _logger.Info($"Creating new DownloadTaskQueue with status files path: {settings.StatusFilesPath}");
                var queue = new DownloadTaskQueue(10, settings, _logger);
                queue.StartQueueHandler();
                return queue;
            }), cancellationToken);
        }

        private bool CheckCircuitBreaker()
        {
            lock (_circuitBreakerLock)
            {
                if (!_circuitBreakerOpen)
                {
                    return false;
                }
                
                // Check if it's time to reset
                if (DateTime.UtcNow >= _circuitBreakerResetTime)
                {
                    _circuitBreakerOpen = false;
                    var pendingCount = _pendingDownloads.Count;
                    _logger.Info($"🟢 CIRCUIT BREAKER RESET at {DateTime.Now:HH:mm:ss} - Resuming download processing of {pendingCount} queued items");
                    return false;
                }
                
                return true;
            }
        }

        private async Task ProcessPendingDownloads()
        {
            if (_processingPendingDownloads)
            {
                return;
            }
            
            _processingPendingDownloads = true;
            _logger.Info($"🔄 Starting pending download processing at {DateTime.Now:HH:mm:ss} with {_pendingDownloads.Count} items in queue");
            
            try
            {
                int batchSize = 3; // Process in small batches
                int processedCount = 0;
                DateTime lastStatusLog = DateTime.MinValue;
                
                bool shouldPause = false;
                
                while (_pendingDownloads.Count > 0)
                {
                    // Log queue status every minute during processing
                    if ((DateTime.UtcNow - lastStatusLog).TotalMinutes >= 1)
                    {
                        LogQueueStatus();
                        lastStatusLog = DateTime.UtcNow;
                    }
                    
                    // Check if circuit breaker has tripped again before processing
                    if (CheckCircuitBreaker())
                    {
                        _logger.Warn($"❌ Circuit breaker active at {DateTime.Now:HH:mm:ss}, pausing pending download processing");
                        break;
                    }
                    
                    // Check if we need to pause to allow higher priority operations
                    lock (_operationsLock)
                    {
                        // If we have a significant number of pending high-priority operations, pause
                        if (_pendingOperationCts.Count > 1)
                        {
                            shouldPause = true;
                            _logger.Info($"⏸️ Pausing pending download processing at {DateTime.Now:HH:mm:ss} to allow {_pendingOperationCts.Count} higher priority operations to complete");
                        }
                        else
                        {
                            shouldPause = false;
                        }
                    }
                    
                    if (shouldPause)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None);
                        continue;
                    }
                    
                    // Process only a small batch at a time
                    int currentBatchCount = Math.Min(batchSize, _pendingDownloads.Count);
                    var currentBatch = new List<PendingDownload>(currentBatchCount);
                    
                    // Extract batch from queue
                    lock (_pendingDownloads)
                    {
                        for (int i = 0; i < currentBatchCount; i++)
                        {
                            if (_pendingDownloads.Count > 0)
                            {
                                currentBatch.Add(_pendingDownloads.Dequeue());
                            }
                        }
                    }
                    
                    // Process the batch
                    _logger.Debug($"📦 Processing batch of {currentBatch.Count} pending downloads at {DateTime.Now:HH:mm:ss}");
                    
                    foreach (var pendingDownload in currentBatch)
                    {
                        try
                        {
                            // Log when items have been queued for a long time
                            var queueTime = DateTime.UtcNow - pendingDownload.QueuedTime;
                            if (queueTime.TotalMinutes > 5)
                            {
                                _logger.Info($"⏱️ Download of {pendingDownload.RemoteAlbum.Release.Title} has been queued for {queueTime.TotalMinutes:F1} minutes");
                            }
                            
                            // Process the download
                            await Download(pendingDownload.RemoteAlbum, pendingDownload.Settings, CancellationToken.None);
                            processedCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, $"Failed to process queued download: {pendingDownload.RemoteAlbum.Release.Title}");
                            
                            _consecutiveFailures++;
                            if (_consecutiveFailures >= 3)
                            {
                                TripCircuitBreaker();
                                _logger.Error($"Three consecutive download failures, circuit breaker tripped at {DateTime.Now:HH:mm:ss}");
                                break;
                            }
                        }
                    }
                    
                    // After each batch, add a small delay to avoid overwhelming the system
                    await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
                }
                
                _logger.Info($"✅ Completed pending download processing at {DateTime.Now:HH:mm:ss}. Processed {processedCount} items, {_pendingDownloads.Count} remaining in queue");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing pending downloads");
            }
            finally
            {
                _processingPendingDownloads = false;
            }
        }

        private void TripCircuitBreaker()
        {
            lock (_circuitBreakerLock)
            {
                // If already open, update reset time but don't log again
                if (_circuitBreakerOpen)
                {
                    _circuitBreakerResetTime = DateTime.UtcNow.Add(_circuitBreakDuration);
                    return;
                }
                
                _circuitBreakerOpen = true;
                _circuitBreakerResetTime = DateTime.UtcNow.Add(_circuitBreakDuration);
                
                var pendingCount = _pendingDownloads.Count;
                _logger.Warn($"🚨 CIRCUIT BREAKER ACTIVATED at {DateTime.Now:HH:mm:ss} - Downloads will be queued for {_circuitBreakDuration.TotalMinutes:F1} minutes ({pendingCount} items already in queue)");
                _logger.Info($"   ⚠️ Downloads will be queued and processed when circuit reopens");
                
                // Start sending periodic updates
                _lastStatusUpdate = DateTime.MinValue; // Force immediate status update
                LogQueueStatus();
                
                // Start a task to periodically report status
                Task.Run(async () => 
                {
                    try 
                    {
                        while (IsCircuitBreakerOpen()) 
                        {
                            await Task.Delay(_statusUpdateInterval);
                            if ((DateTime.UtcNow - _lastStatusUpdate) >= _statusUpdateInterval)
                            {
                                LogQueueStatus();
                                _lastStatusUpdate = DateTime.UtcNow;
                            }
                        }
                    }
                    catch (Exception ex) 
                    {
                        _logger.Debug(ex, "Error in status update task");
                    }
                });
            }
        }

        private void RecordSuccess()
        {
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            _lastSuccessfulOperation = DateTime.UtcNow;
            LogQueueStatus();
            _lastStatusUpdate = DateTime.UtcNow;
        }

        public async Task<List<DownloadClientItem>> GetQueueAsync(TidalSettings settings, CancellationToken cancellationToken = default)
        {
            if (CheckCircuitBreaker())
            {
                _logger.Warn("Circuit breaker open, returning empty queue");
                return new List<DownloadClientItem>();
            }

            try
            {
                bool throttleSemaphoreAcquired = false;
                bool operationSlotAcquired = false;
                
                try
                {
                    // GetQueue is called by UI, so it gets high priority
                    operationSlotAcquired = await AcquireDatabaseOperationAsync(
                        isHighPriority: true,
                        timeoutMs: 20000, // 20 seconds
                        cancellationToken);
                        
                    if (!operationSlotAcquired)
                    {
                        _logger.Warn("Could not acquire database operation slot for queue listing");
                        return new List<DownloadClientItem>();
                    }
                
                    throttleSemaphoreAcquired = await _globalThrottleSemaphore.WaitAsync(_operationTimeout, cancellationToken);
                    if (!throttleSemaphoreAcquired)
                    {
                        _logger.Warn("Global throttle timeout exceeded, returning empty queue");
                        return new List<DownloadClientItem>();
                    }

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(_operationTimeout);
                    
                    var taskQueue = await GetTaskQueueAsync(settings, cts.Token);
                    taskQueue.SetSettings(settings);

                    var items = taskQueue.GetQueueListing();
                    var result = new List<DownloadClientItem>();

                    foreach (var item in items)
                    {
                        result.Add(ToDownloadClientItem(item));
                    }

                    RecordSuccess();
                    return result;
                }
                finally
                {
                    if (throttleSemaphoreAcquired)
                    {
                        _globalThrottleSemaphore.Release();
                    }
                    
                    if (operationSlotAcquired)
                    {
                        ReleaseDatabaseOperation();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Error("GetQueue operation timed out after {0} minutes", _operationTimeout.TotalMinutes);
                TripCircuitBreaker();
                return new List<DownloadClientItem>();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting queue: {0}", ex.Message);
                TripCircuitBreaker();
                return new List<DownloadClientItem>();
            }
        }

        public async Task RemoveFromQueueAsync(string downloadId, TidalSettings settings, CancellationToken cancellationToken = default)
        {
            if (CheckCircuitBreaker())
            {
                _logger.Warn("Circuit breaker open, skipping remove from queue");
                return;
            }

            try
            {
                bool throttleSemaphoreAcquired = false;
                bool operationSlotAcquired = false;
                
                try
                {
                    // Removal is a UI operation, so gets high priority
                    operationSlotAcquired = await AcquireDatabaseOperationAsync(
                        isHighPriority: true,
                        timeoutMs: 15000, // 15 seconds
                        cancellationToken);
                        
                    if (!operationSlotAcquired)
                    {
                        _logger.Warn("Could not acquire database operation slot for queue item removal");
                        return;
                    }
                    
                    throttleSemaphoreAcquired = await _globalThrottleSemaphore.WaitAsync(_operationTimeout, cancellationToken);
                    if (!throttleSemaphoreAcquired)
                    {
                        _logger.Warn("Global throttle timeout exceeded while removing from queue");
                        return;
                    }

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(_operationTimeout);
                    
                    var taskQueue = await GetTaskQueueAsync(settings, cts.Token);
                    taskQueue.SetSettings(settings);

                    var item = taskQueue.GetQueueListing().FirstOrDefault(a => a.ID == downloadId);
                    if (item != null)
                        taskQueue.RemoveItem(item);
                    
                    RecordSuccess();
                }
                finally
                {
                    if (throttleSemaphoreAcquired)
                    {
                        _globalThrottleSemaphore.Release();
                    }
                    
                    if (operationSlotAcquired)
                    {
                        ReleaseDatabaseOperation();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Error("RemoveFromQueue operation timed out after {0} minutes", _operationTimeout.TotalMinutes);
                TripCircuitBreaker();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error removing from queue: {0}", ex.Message);
                TripCircuitBreaker();
            }
        }

        public async Task<string> Download(RemoteAlbum remoteAlbum, TidalSettings settings, CancellationToken cancellationToken = default)
        {
            // Check circuit breaker status
            if (CheckCircuitBreaker())
            {
                // Since we know the circuit breaker is open, we'll queue the download but add a delay
                // between consecutive queue additions to avoid database contention
                
                // Add a small delay before queueing to reduce database contention
                await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);
                
                lock (_pendingDownloads)
                {
                    _logger.Warn($"⏳ DOWNLOAD QUEUED: Circuit breaker is open, queueing {remoteAlbum?.Release?.Title}");
                    _pendingDownloads.Enqueue(new PendingDownload
                    {
                        RemoteAlbum = remoteAlbum,
                        Settings = settings,
                        QueuedTime = DateTime.UtcNow
                    });
                }
                
                var pendingCount = GetPendingDownloadCount();
                var timeRemaining = GetCircuitBreakerReopenTime();
                
                // Format the message for better readability
                var timeRemainingStr = timeRemaining.TotalHours >= 1
                    ? $"{(int)timeRemaining.TotalHours}h {timeRemaining.Minutes}m"
                    : $"{timeRemaining.Minutes}m {timeRemaining.Seconds}s";
                
                var message = $"   ℹ️ Download queued. Circuit breaker will reset in {timeRemainingStr}";
                message += $"\n   📋 Queue position: #{pendingCount} of {pendingCount} items";
                _logger.Info(message);
                
                // Ensure we're respecting cancellation even in circuit breaker state
                cancellationToken.ThrowIfCancellationRequested();
                
                return null;
            }
            
            // Create a linked token source that combines the incoming token with our operation timeout
            using var timeoutCts = new CancellationTokenSource(_operationTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var combinedToken = linkedCts.Token;
            
            bool semaphoreAcquired = false;
            bool operationSlotAcquired = false;
            DownloadTaskQueue queue = null;
            
            try
            {
                _logger.Info($"Attempting to download album: {remoteAlbum?.Release?.Title}");
                
                // Try to acquire database operation slot - downloads are low priority
                operationSlotAcquired = await AcquireDatabaseOperationAsync(
                    isHighPriority: false,
                    timeoutMs: 30000, // 30 seconds
                    combinedToken);
                    
                if (!operationSlotAcquired)
                {
                    _logger.Warn($"Could not acquire database operation slot for download: {remoteAlbum?.Release?.Title}");
                    throw new TimeoutException("Timed out waiting for database access. System is busy with other operations.");
                }
                
                // Try to acquire semaphore with timeout
                semaphoreAcquired = await _globalThrottleSemaphore.WaitAsync(TimeSpan.FromSeconds(30), combinedToken);
                if (!semaphoreAcquired)
                {
                    throw new TimeoutException("Timed out waiting for download slot. Too many concurrent downloads.");
                }
                
                combinedToken.ThrowIfCancellationRequested();
                
                // Get the queue first, with a potential retry if initial fetch fails
                try 
                {
                    queue = await GetTaskQueueAsync(settings, combinedToken);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error getting download queue, retrying once");
                    // Small delay before retry
                    await Task.Delay(500, combinedToken);
                    queue = await GetTaskQueueAsync(settings, combinedToken);
                }
                
                await EnsureInitializedAsync(settings, combinedToken);
                
                string downloadId;
                try
                {
                    // Create the download item first, which is lighter weight
                    var downloadItem = await DownloadItem.From(remoteAlbum);
                    
                    if (downloadItem == null)
                    {
                        _logger.Error($"Failed to create download item for {remoteAlbum?.Release?.Title}");
                        throw new InvalidOperationException($"Could not create download item for {remoteAlbum?.Release?.Title}");
                    }
                    
                    // Set a separate timeout for the queue operation specifically
                    using var queueCts = CancellationTokenSource.CreateLinkedTokenSource(combinedToken);
                    queueCts.CancelAfter(TimeSpan.FromSeconds(45)); // 45-second timeout specifically for queue operation
                    
                    try 
                    {
                        // This is where most timeouts occur
                        await queue.QueueBackgroundWorkItemAsync(downloadItem, queueCts.Token);
                        downloadId = downloadItem.ID;
                        
                        RecordSuccess();
                        _logger.Info($"Successfully queued download for {remoteAlbum?.Release?.Title}");
                        return downloadId;
                    }
                    catch (OperationCanceledException ex) when (queueCts.IsCancellationRequested && !combinedToken.IsCancellationRequested)
                    {
                        _logger.Error($"Timed out adding item to queue: {remoteAlbum?.Release?.Title}");
                        TripCircuitBreaker();
                        throw new TimeoutException($"Timed out adding {remoteAlbum?.Release?.Title} to download queue", ex);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.Error(ex, $"Error during download of {remoteAlbum?.Release?.Title}");
                    TripCircuitBreaker();
                    throw;
                }
            }
            catch (OperationCanceledException ex)
            {
                if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    _logger.Error($"Download operation for {remoteAlbum?.Release?.Title} timed out after {_operationTimeout.TotalMinutes} minutes");
                    TripCircuitBreaker();
                    throw new TimeoutException($"Tidal download operation timed out after {_operationTimeout.TotalMinutes} minutes", ex);
                }
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Unexpected error during download of {remoteAlbum?.Release?.Title}: {ex.Message}");
                TripCircuitBreaker();
                throw;
            }
            finally
            {
                if (semaphoreAcquired)
                {
                    _globalThrottleSemaphore.Release();
                    _logger.Debug("Released download semaphore");
                }
                
                if (operationSlotAcquired)
                {
                    ReleaseDatabaseOperation();
                    _logger.Debug("Released database operation slot");
                }
            }
        }

        public async Task TestConnectionAsync(TidalSettings settings, CancellationToken cancellationToken = default)
        {
            _logger.Debug("Testing Tidal connection and settings...");
            
            if (string.IsNullOrWhiteSpace(settings.DownloadPath))
            {
                throw new ArgumentException("Download path must be configured");
            }
            
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
            
            try
            {
                await EnsureInitializedAsync(settings, cancellationToken);
                _logger.Debug("Successfully initialized Tidal proxy components");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to initialize Tidal components: {ex.Message}");
            }
            
            _logger.Info("Tidal connection test successful");
        }

        private async Task EnsureInitializedAsync(TidalSettings settings, CancellationToken cancellationToken = default)
        {
            if (!await _initSemaphore.WaitAsync(_initTimeout, cancellationToken))
            {
                throw new TimeoutException($"Timed out waiting for initialization after {_initTimeout.TotalSeconds} seconds");
            }
            
            bool operationSlotAcquired = false;
            
            try
            {
                // This is initialization, so it's high priority
                operationSlotAcquired = await AcquireDatabaseOperationAsync(
                    isHighPriority: true,
                    timeoutMs: (int)_initTimeout.TotalMilliseconds, 
                    cancellationToken);
                    
                if (!operationSlotAcquired)
                {
                    _logger.Warn("Could not acquire database operation slot for initialization");
                    throw new TimeoutException("Timed out waiting for database access during initialization");
                }
                
                var queue = await GetTaskQueueAsync(settings, cancellationToken);
                
                try
                {
                    // Set the settings on the queue
                    queue.SetSettings(settings);
                    
                    // Check if the queue is initialized and ready
                    if (queue == null)
                    {
                        throw new InvalidOperationException("Failed to initialize download task queue");
                    }
                    
                    _logger.Debug("Tidal components initialized successfully");
                    RecordSuccessfulOperation();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error initializing Tidal components: {0}", ex.Message);
                    TripCircuitBreaker();
                    throw;
                }
            }
            finally
            {
                _initSemaphore.Release();
                
                if (operationSlotAcquired)
                {
                    ReleaseDatabaseOperation();
                }
            }
        }

        public List<DownloadClientItem> GetQueue(TidalSettings settings)
        {
            try
            {
                using var cts = new CancellationTokenSource(_operationTimeout);
                return GetQueueAsync(settings, cts.Token).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in synchronous GetQueue: {0}", ex.Message);
                return new List<DownloadClientItem>();
            }
        }

        public void RemoveFromQueue(string downloadId, TidalSettings settings)
        {
            try
            {
                using var cts = new CancellationTokenSource(_operationTimeout);
                RemoveFromQueueAsync(downloadId, settings, cts.Token).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in synchronous RemoveFromQueue: {0}", ex.Message);
            }
        }

        public void TestConnection(TidalSettings settings)
        {
            try
            {
                using var cts = new CancellationTokenSource(_operationTimeout);
                TestConnectionAsync(settings, cts.Token).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in synchronous TestConnection: {0}", ex.Message);
                throw;
            }
        }

        private DownloadClientItem ToDownloadClientItem(IDownloadItem downloadItem)
        {
            // Format title depending on audio quality
            var title = $"{downloadItem.Artist} - {downloadItem.Title}";
            
            // Determine format from bitrate if available
            string format = null;
            var bitrate = downloadItem.Bitrate;
            format = bitrate switch
            {
                AudioQuality.LOW => "AAC (M4A) 96kbps",
                AudioQuality.HIGH => "AAC (M4A) 320kbps",
                AudioQuality.LOSSLESS => "FLAC (M4A) Lossless",
                AudioQuality.HI_RES_LOSSLESS => "FLAC (M4A) 24bit Lossless",
                _ => null,
            };
            
            if (format != null)
            {
                title += $" [WEB] [{format}]";
            }
            
            // Add a warning to the title if the circuit breaker is active
            if (CheckCircuitBreaker())
            {
                title += " [THROTTLED: Download operations limited]";
            }
            
            var item = new DownloadClientItem
            {
                DownloadId = downloadItem.ID,
                Title = title,
                TotalSize = downloadItem.TotalSize,
                RemainingSize = downloadItem.TotalSize - downloadItem.DownloadedSize,
                Status = downloadItem.Status == DownloadItemStatus.Completed ? DownloadItemStatus.Completed :
                        downloadItem.Status == DownloadItemStatus.Failed ? DownloadItemStatus.Failed :
                        DownloadItemStatus.Downloading,
                RemainingTime = GetRemainingTime(downloadItem),
                CanBeRemoved = true,
                CanMoveFiles = false
            };
            
            if (downloadItem.DownloadFolder.IsNotNullOrWhiteSpace())
            {
                item.OutputPath = new OsPath(downloadItem.DownloadFolder);
            }
            
            return item;
        }

        private TimeSpan GetRemainingTime(IDownloadItem downloadItem)
        {
            if (downloadItem.Status == DownloadItemStatus.Paused || downloadItem.Status == DownloadItemStatus.Failed)
            {
                return TimeSpan.Zero;
            }
            
            if (downloadItem.Status == DownloadItemStatus.Completed)
            {
                return TimeSpan.Zero;
            }
            
            if (downloadItem.DownloadedSize <= 0 || downloadItem.TotalSize <= 0)
            {
                return TimeSpan.FromHours(1); // Default estimate when no data is available
            }
            
            var bytesRemaining = downloadItem.TotalSize - downloadItem.DownloadedSize;
            
            if (bytesRemaining <= 0)
            {
                return TimeSpan.Zero;
            }
            
            // Fall back to estimating based on progress so far
            var elapsedTime = DateTime.UtcNow - _startTimeCache.Get("download", () => DateTime.UtcNow);
            var progress = downloadItem.DownloadedSize / (float)downloadItem.TotalSize;
            
            if (progress <= 0 || elapsedTime == null)
            {
                return TimeSpan.FromHours(1); // Default estimate when rate is unknown
            }
            
            var estimatedTotalTime = TimeSpan.FromTicks((long)(elapsedTime.Value.Ticks / progress));
            var remainingTime = estimatedTotalTime - elapsedTime.Value;
            
            return remainingTime > TimeSpan.Zero ? remainingTime : TimeSpan.Zero;
        }

        // Add public methods to support circuit breaker and pending download coordination
        
        /// <summary>
        /// Public method to check if the circuit breaker is currently open
        /// </summary>
        /// <returns>True if the circuit breaker is open</returns>
        public bool IsCircuitBreakerOpen()
        {
            return CheckCircuitBreaker();
        }
        
        /// <summary>
        /// Gets the count of pending downloads waiting for circuit breaker to reopen
        /// </summary>
        /// <returns>Number of pending downloads</returns>
        public int GetPendingDownloadCount()
        {
            return _pendingDownloads.Count;
        }
        
        /// <summary>
        /// Gets the estimated time until the circuit breaker reopens
        /// </summary>
        /// <returns>TimeSpan until circuit breaker reopens, or TimeSpan.Zero if already open</returns>
        public TimeSpan GetCircuitBreakerReopenTime()
        {
            if (_circuitBreakerOpen && _circuitBreakerResetTime > DateTime.UtcNow)
            {
                return _circuitBreakerResetTime - DateTime.UtcNow;
            }
            
            return TimeSpan.Zero;
        }

        /// <summary>
        /// Asynchronously acquires a database operation slot with priority awareness
        /// </summary>
        /// <param name="isHighPriority">True for search/UI operations, false for download operations</param>
        /// <param name="timeoutMs">Timeout in milliseconds</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if operation slot acquired, false otherwise</returns>
        private async Task<bool> AcquireDatabaseOperationAsync(bool isHighPriority, int timeoutMs, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            
            // High priority operations get to bypass the queue if there's capacity
            if (isHighPriority)
            {
                lock (_operationsLock)
                {
                    if (_activeOperations < _maxConcurrentOperations)
                    {
                        _activeOperations++;
                        return true;
                    }
                }
            }
            
            // For both regular and high priority that couldn't bypass, we need to wait
            // Create a wait handle that will be signaled when a slot becomes available or timeout occurs
            using var waitCts = new CancellationTokenSource();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(waitCts.Token, cancellationToken);
            var registeredWait = false;
            
            try
            {
                lock (_operationsLock)
                {
                    // One more check before we add to the wait queue
                    if (_activeOperations < _maxConcurrentOperations)
                    {
                        _activeOperations++;
                        return true;
                    }
                    
                    // Add to the pending operations queue with priority
                    // High priority operations go to front, low priority to back
                    if (isHighPriority)
                    {
                        _pendingOperationCts.Insert(0, waitCts);
                    }
                    else
                    {
                        _pendingOperationCts.Add(waitCts);
                    }
                    registeredWait = true;
                }
                
                // Set the timeout
                var remainingTime = deadline - DateTime.UtcNow;
                if (remainingTime > TimeSpan.Zero)
                {
                    waitCts.CancelAfter(remainingTime);
                }
                else
                {
                    // Already timed out
                    return false;
                }
                
                try
                {
                    // Wait for signal that we can proceed, or timeout
                    await Task.Delay(-1, linkedCts.Token);
                    // If we get here, the token was canceled but not due to timeout
                    return !waitCts.IsCancellationRequested || !cancellationToken.IsCancellationRequested;
                }
                catch (OperationCanceledException)
                {
                    // Check if we were granted access before cancelation
                    lock (_operationsLock)
                    {
                        if (registeredWait && !_pendingOperationCts.Contains(waitCts))
                        {
                            // We were removed from the queue by another thread, likely granted access
                            return true;
                        }
                        
                        // Token was canceled due to timeout or user canceled
                        return false;
                    }
                }
            }
            finally
            {
                // If still registered but failed, remove from queue
                if (registeredWait)
                {
                    lock (_operationsLock)
                    {
                        _pendingOperationCts.Remove(waitCts);
                    }
                }
            }
        }
        
        /// <summary>
        /// Releases a database operation slot and signals the next waiting task if any
        /// </summary>
        private void ReleaseDatabaseOperation()
        {
            CancellationTokenSource nextCts = null;
            
            lock (_operationsLock)
            {
                // Decrement active operations
                if (_activeOperations > 0)
                {
                    _activeOperations--;
                }
                
                // Signal the next waiting operation if capacity allows
                if (_activeOperations < _maxConcurrentOperations && _pendingOperationCts.Count > 0)
                {
                    nextCts = _pendingOperationCts[0];
                    _pendingOperationCts.RemoveAt(0);
                    _activeOperations++; // Pre-increment for the operation we're about to signal
                }
            }
            
            // Signal outside the lock to avoid deadlocks
            nextCts?.Cancel();
        }
        
        /// <summary>
        /// Emergency reset of database operation tracking in case of deadlocks
        /// </summary>
        private void ResetDatabaseOperations()
        {
            List<CancellationTokenSource> allPendingCts = null;
            
            lock (_operationsLock)
            {
                _logger.Warn("Emergency reset of database operations tracking");
                _activeOperations = 0;
                
                // Get all pending operations so we can signal them outside the lock
                allPendingCts = new List<CancellationTokenSource>(_pendingOperationCts);
                _pendingOperationCts.Clear();
            }
            
            // Signal all pending operations to retry
            if (allPendingCts != null)
            {
                foreach (var cts in allPendingCts)
                {
                    try
                    {
                        cts.Cancel();
                    }
                    catch
                    {
                        // Ignore errors when signaling
                    }
                }
            }
        }
    }
}
