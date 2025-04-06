# Tidal Indexer Improvements Roadmap

## Overview

This technical document outlines a comprehensive roadmap for improving the Tidal indexer and download components to address several critical issues identified during testing. The goal is to create a more robust, reliable, and performant implementation that can handle concurrent operations without failures.

## Identified Issues

1. **Duplicate Download Processing**
   - Items are processed multiple times, causing redundant downloads and log entries
   - Resources are wasted on repeated processing

2. **Search Queue Stalling**
   - Subsequent searches get stuck while initial searches work properly
   - Semaphore management issues cause deadlocks

3. **NLog Dependency Management**
   - Linter errors related to Logger type references
   - Potential silent failures when logging

4. **Race Conditions in Search & Download**
   - Concurrency issues with thread synchronization
   - Lock management issues during parallel operations

5. **Memory Management**
   - Potential memory leaks with unmanaged resources
   - Especially with CancellationTokenSource objects

6. **TidalAPI Initialization & State Management**
   - Inconsistent state between searches
   - Re-initialization problems

7. **Authentication Token Handling**
   - Expiration of tokens during operations
   - Insufficient refresh mechanisms

8. **Collection Management Synchronization**
   - Potential deadlocks in collection operations
   - Lack of thread-safe implementation in key areas

## Implementation Plan

### Phase 1: Core Architecture Improvements

#### 1.1. Redesign Thread Synchronization

```csharp
// Current approach - prone to deadlocks
private static readonly object _lock = new object();
private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(0);

// Improved approach - consistent, nested locking order
private static readonly AsyncReaderWriterLock _queueLock = new AsyncReaderWriterLock();
private static readonly SemaphoreSlim _throttleSemaphore = new SemaphoreSlim(4, 4);
```

**Technical Details:**
- Replace all object locks with AsyncReaderWriterLock for more granular control
- Implement consistent lock acquisition order to prevent deadlocks
- Add timeouts to all wait operations to prevent indefinite blocking
- Create lock hierarchy documentation to prevent nesting violations

#### 1.2. Memory Management Framework

```csharp
// Disposable tracker for managing resources
public class DisposableTracker : IDisposable
{
    private readonly ConcurrentBag<IDisposable> _resources = new ConcurrentBag<IDisposable>();
    
    public T Track<T>(T resource) where T : IDisposable
    {
        _resources.Add(resource);
        return resource;
    }
    
    public void Dispose()
    {
        foreach (var resource in _resources)
        {
            try
            {
                resource.Dispose();
            }
            catch (Exception ex)
            {
                // Log but continue
            }
        }
    }
}
```

**Technical Details:**
- Add explicit resource tracking for all disposable objects
- Implement cleanup mechanism for abandoned search operations
- Add reference counting for shared resources
- Implement periodic garbage collection triggers for long-running operations

### Phase 2: Search System Overhaul

#### 2.1. Search Queue Rewrite

```csharp
public class SearchQueueManager : IDisposable
{
    private readonly Channel<SearchRequest> _requestChannel;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _shutdownTokenSource = new CancellationTokenSource();
    private Task _processingTask;
    private readonly TimeSpan _processingTimeout = TimeSpan.FromSeconds(30);
    
    public SearchQueueManager(ILogger logger)
    {
        _logger = logger;
        _requestChannel = Channel.CreateBounded<SearchRequest>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        
        _processingTask = ProcessQueueAsync(_shutdownTokenSource.Token);
    }
    
    public async Task<IList<ReleaseInfo>> EnqueueSearchAsync(string searchTerm, CancellationToken cancellationToken)
    {
        var request = new SearchRequest(searchTerm);
        
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, 
            _shutdownTokenSource.Token);
        
        try
        {
            await _requestChannel.Writer.WriteAsync(request, linkedCts.Token);
            return await request.CompletionTask.WaitAsync(_processingTimeout, linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            request.SetCanceled();
            throw;
        }
        catch (TimeoutException)
        {
            request.SetResult(Array.Empty<ReleaseInfo>());
            _logger.Warn($"Search request for '{searchTerm}' timed out after {_processingTimeout.TotalSeconds}s");
            return Array.Empty<ReleaseInfo>();
        }
    }
    
    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        // Implementation details
    }
    
    public void Dispose()
    {
        _shutdownTokenSource.Cancel();
        _shutdownTokenSource.Dispose();
        // Wait for processing to complete with timeout
    }
}
```

**Technical Details:**
- Replace concurrent queue and semaphores with System.Threading.Channels
- Implement bounded channel with backpressure to prevent memory exhaustion
- Add explicit timeouts for all operations
- Clear separation between request queuing and processing
- Ensure search operations can be properly canceled

#### 2.2. Robust Tidal API Initialization

```csharp
public class TidalAPIManager
{
    private readonly object _initLock = new object();
    private readonly SemaphoreSlim _initSemaphore = new SemaphoreSlim(1, 1);
    private bool _isInitialized = false;
    private DateTime _lastInitTime = DateTime.MinValue;
    private readonly TimeSpan _reinitThreshold = TimeSpan.FromMinutes(30);
    
    public async Task<TidalAPI> GetInitializedAPIAsync(TidalSettings settings, ILogger logger, CancellationToken token)
    {
        if (_isInitialized && TidalAPI.Instance != null && 
            (DateTime.UtcNow - _lastInitTime) < _reinitThreshold)
        {
            return TidalAPI.Instance;
        }
        
        await _initSemaphore.WaitAsync(token);
        try
        {
            // Double-check pattern after acquiring lock
            if (_isInitialized && TidalAPI.Instance != null && 
                (DateTime.UtcNow - _lastInitTime) < _reinitThreshold)
            {
                return TidalAPI.Instance;
            }
            
            // Initialize API here
            // ...
            
            _isInitialized = true;
            _lastInitTime = DateTime.UtcNow;
            return TidalAPI.Instance;
        }
        finally
        {
            _initSemaphore.Release();
        }
    }
}
```

**Technical Details:**
- Implement proper double-check pattern for initialization
- Add periodic re-initialization to refresh state
- Use SemaphoreSlim for async-friendly locking
- Track initialization state with timestamps
- Add explicit timeouts to avoid deadlocks

### Phase 3: Download System Improvements

#### 3.1. Deduplication Framework

```csharp
public class DownloadDeduplicator
{
    private readonly ConcurrentDictionary<string, bool> _activeDownloads = new ConcurrentDictionary<string, bool>();
    private readonly TimeSpan _deduplicationWindow = TimeSpan.FromMinutes(30);
    private readonly ILogger _logger;
    
    public DownloadDeduplicator(ILogger logger)
    {
        _logger = logger;
        // Start cleanup timer
        StartCleanupTimer();
    }
    
    public bool TryRegisterDownload(IDownloadItem item, out string reason)
    {
        if (item == null)
        {
            reason = "Null download item";
            return false;
        }
        
        // Generate a unique key based on metadata
        string key = GenerateDeduplicationKey(item);
        
        // Check if already downloading or recently downloaded
        if (!_activeDownloads.TryAdd(key, true))
        {
            reason = $"Duplicate download detected: {item.Title} by {item.Artist}";
            _logger.Debug(reason);
            return false;
        }
        
        reason = null;
        return true;
    }
    
    public void UnregisterDownload(IDownloadItem item)
    {
        if (item == null) return;
        
        string key = GenerateDeduplicationKey(item);
        _activeDownloads.TryRemove(key, out _);
    }
    
    private string GenerateDeduplicationKey(IDownloadItem item)
    {
        // Generate a robust key that can identify duplicates
        // including album ID, artist, title, quality, etc.
    }
    
    private void StartCleanupTimer()
    {
        // Periodically clean up old entries
    }
}
```

**Technical Details:**
- Implementation for checking duplicates before processing
- Robust key generation to detect same content at different quality levels
- Automatic cleanup of expired dedupe records
- Thread-safe implementation with ConcurrentDictionary
- Detailed logging of duplicate detection

#### 3.2. Resilient Download Task Queue

```csharp
public class ResilientDownloadTaskQueue : IDownloadTaskQueue
{
    private readonly Channel<IDownloadItem> _downloadChannel;
    private readonly DownloadDeduplicator _deduplicator;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _shutdownTokenSource;
    private readonly ICollection<Task> _processingTasks;
    private readonly SemaphoreSlim _throttleSemaphore;
    
    public ResilientDownloadTaskQueue(int concurrency, ILogger logger)
    {
        _logger = logger;
        _deduplicator = new DownloadDeduplicator(logger);
        _shutdownTokenSource = new CancellationTokenSource();
        _processingTasks = new List<Task>(concurrency);
        _throttleSemaphore = new SemaphoreSlim(concurrency, concurrency);
        
        _downloadChannel = Channel.CreateBounded<IDownloadItem>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,  // Multiple processors
            SingleWriter = false   // Multiple enqueuers
        });
        
        // Start processing tasks
        for (int i = 0; i < concurrency; i++)
        {
            _processingTasks.Add(ProcessDownloadsAsync(_shutdownTokenSource.Token));
        }
    }
    
    public async Task EnqueueDownloadAsync(IDownloadItem item, CancellationToken cancellationToken)
    {
        // Check deduplication first
        if (!_deduplicator.TryRegisterDownload(item, out string reason))
        {
            _logger.Info($"Skipping download: {reason}");
            return;
        }
        
        try
        {
            await _downloadChannel.Writer.WriteAsync(item, cancellationToken);
            _logger.Debug($"Download queued: {item.Title} by {item.Artist}");
        }
        catch (Exception)
        {
            // On exception, make sure to unregister from deduplicator
            _deduplicator.UnregisterDownload(item);
            throw;
        }
    }
    
    private async Task ProcessDownloadsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            IDownloadItem item = null;
            bool semaphoreAcquired = false;
            
            try
            {
                item = await _downloadChannel.Reader.ReadAsync(cancellationToken);
                
                // Apply throttling
                semaphoreAcquired = await _throttleSemaphore.WaitAsync(TimeSpan.FromMinutes(5), cancellationToken);
                if (!semaphoreAcquired)
                {
                    _logger.Warn("Download throttling timeout, will retry");
                    // Re-queue the item
                    await _downloadChannel.Writer.WriteAsync(item, cancellationToken);
                    continue;
                }
                
                // Process the download
                await ProcessDownloadItem(item, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error processing download: {item?.Title}");
            }
            finally
            {
                if (semaphoreAcquired)
                {
                    _throttleSemaphore.Release();
                }
                
                if (item != null)
                {
                    // Always unregister from deduplicator when done
                    _deduplicator.UnregisterDownload(item);
                }
            }
        }
    }
    
    private async Task ProcessDownloadItem(IDownloadItem item, CancellationToken cancellationToken)
    {
        // Implementation of the download logic
    }
}
```

**Technical Details:**
- Channel-based queue implementation for better backpressure
- Multi-threaded processing with controlled concurrency
- Semaphore-based throttling with timeouts
- Integration with deduplication framework
- Proper cleanup of resources in all cases

### Phase 4: Logging and Diagnostics

#### 4.1. Structured Logging Implementation

```csharp
public static class StructuredLoggingExtensions
{
    private static readonly ConcurrentDictionary<string, long> _errorCounters = new ConcurrentDictionary<string, long>();
    private static readonly ConcurrentDictionary<string, DateTime> _lastLogTimes = new ConcurrentDictionary<string, DateTime>();
    
    public static void LogOperationStart(this ILogger logger, string operation, string context)
    {
        logger.Debug($"[{operation}] [START] {context}");
    }
    
    public static void LogOperationEnd(this ILogger logger, string operation, string context, TimeSpan duration)
    {
        logger.Debug($"[{operation}] [END] {context} - Duration: {duration.TotalMilliseconds}ms");
    }
    
    public static void LogThrottled(this ILogger logger, LogLevel level, string key, TimeSpan throttlePeriod, string message)
    {
        if (!_lastLogTimes.TryGetValue(key, out var lastTime) || 
            (DateTime.UtcNow - lastTime) > throttlePeriod)
        {
            // Log with count of suppressed messages if any
            if (_errorCounters.TryRemove(key, out var count) && count > 0)
            {
                logger.Log(level, $"{message} (+ {count} similar messages suppressed)");
            }
            else
            {
                logger.Log(level, message);
            }
            
            _lastLogTimes[key] = DateTime.UtcNow;
        }
        else
        {
            // Increment counter for suppressed messages
            _errorCounters.AddOrUpdate(key, 1, (_, count) => count + 1);
        }
    }
}
```

**Technical Details:**
- Consistent, structured logging format
- Throttled logging to prevent log flooding
- Operation tracking with start/end markers
- Message suppression with counters for repeated messages
- Integration with existing Logger infrastructure

#### 4.2. Diagnostic Telemetry System

```csharp
public class TidalDiagnostics
{
    private readonly ConcurrentDictionary<string, Metrics> _operationMetrics = new ConcurrentDictionary<string, Metrics>();
    private readonly ILogger _logger;
    private readonly TimeSpan _reportingInterval;
    private readonly Timer _reportingTimer;
    
    private class Metrics
    {
        public long SuccessCount;
        public long FailureCount;
        public long TotalElapsedMs;
        public long MaxElapsedMs;
        public long Count => SuccessCount + FailureCount;
        public double AvgElapsedMs => Count > 0 ? (double)TotalElapsedMs / Count : 0;
        public double SuccessRate => Count > 0 ? (double)SuccessCount / Count : 0;
    }
    
    public TidalDiagnostics(ILogger logger, TimeSpan reportingInterval)
    {
        _logger = logger;
        _reportingInterval = reportingInterval;
        _reportingTimer = new Timer(ReportMetrics, null, reportingInterval, reportingInterval);
    }
    
    public IDisposable TrackOperation(string operation)
    {
        return new OperationTracker(this, operation);
    }
    
    private void RecordOperation(string operation, bool success, long elapsedMs)
    {
        _operationMetrics.AddOrUpdate(
            operation,
            _ => new Metrics 
            { 
                SuccessCount = success ? 1 : 0,
                FailureCount = success ? 0 : 1,
                TotalElapsedMs = elapsedMs,
                MaxElapsedMs = elapsedMs
            },
            (_, metrics) => 
            {
                if (success) Interlocked.Increment(ref metrics.SuccessCount);
                else Interlocked.Increment(ref metrics.FailureCount);
                Interlocked.Add(ref metrics.TotalElapsedMs, elapsedMs);
                InterlockedMax(ref metrics.MaxElapsedMs, elapsedMs);
                return metrics;
            });
    }
    
    private void ReportMetrics(object _)
    {
        // Generate metrics report
    }
    
    private class OperationTracker : IDisposable
    {
        // Implementation details
    }
    
    private static void InterlockedMax(ref long target, long value)
    {
        long current = target;
        while (value > current)
        {
            long original = Interlocked.CompareExchange(ref target, value, current);
            if (original == current) break;
            current = original;
        }
    }
}
```

**Technical Details:**
- Detailed metrics collection for all operations
- Thread-safe counters for concurrent usage
- Regular reporting intervals
- Success rate tracking
- Performance measurements (avg, max times)
- Low overhead implementation

### Phase 5: Token and Authentication Management

#### 5.1. Token Refresh System

```csharp
public class TokenManager
{
    private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);
    private string _currentToken;
    private DateTime _tokenExpiry;
    private readonly TimeSpan _refreshBuffer = TimeSpan.FromMinutes(5);
    private readonly ILogger _logger;
    
    public TokenManager(ILogger logger)
    {
        _logger = logger;
    }
    
    public async Task<string> GetValidTokenAsync(Func<Task<(string token, DateTime expiry)>> tokenProvider, CancellationToken cancellationToken)
    {
        // Fast path - token is still valid
        if (!string.IsNullOrEmpty(_currentToken) && DateTime.UtcNow < _tokenExpiry.Subtract(_refreshBuffer))
        {
            return _currentToken;
        }
        
        // Need to refresh - acquire lock
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (!string.IsNullOrEmpty(_currentToken) && DateTime.UtcNow < _tokenExpiry.Subtract(_refreshBuffer))
            {
                return _currentToken;
            }
            
            // Get new token
            _logger.Debug("Refreshing Tidal authentication token");
            var (token, expiry) = await tokenProvider();
            
            _currentToken = token;
            _tokenExpiry = expiry;
            
            _logger.Debug($"Token refreshed, valid until {expiry}");
            return _currentToken;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error refreshing Tidal token");
            
            // If we have an existing token, return it even if close to expiry
            if (!string.IsNullOrEmpty(_currentToken) && DateTime.UtcNow < _tokenExpiry)
            {
                _logger.Warn($"Using existing token that expires in {(_tokenExpiry - DateTime.UtcNow).TotalMinutes:F1} minutes");
                return _currentToken;
            }
            
            throw;
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
```

**Technical Details:**
- Proactive token refreshing before expiration
- Thread-safe implementation with proper locking
- Fallback to existing token when refresh fails
- Configurable refresh buffer window
- Detailed logging of token state

#### 5.2. API Circuit Breaker Implementation

```csharp
public class ApiCircuitBreaker
{
    private enum CircuitState { Closed, Open, HalfOpen }
    
    private CircuitState _currentState = CircuitState.Closed;
    private int _failureCount;
    private DateTime _openTime;
    private readonly object _stateLock = new object();
    private readonly int _failureThreshold;
    private readonly TimeSpan _resetTimeout;
    private readonly ILogger _logger;
    
    public ApiCircuitBreaker(int failureThreshold, TimeSpan resetTimeout, ILogger logger)
    {
        _failureThreshold = failureThreshold;
        _resetTimeout = resetTimeout;
        _logger = logger;
    }
    
    public bool AllowRequest()
    {
        lock (_stateLock)
        {
            switch (_currentState)
            {
                case CircuitState.Closed:
                    return true;
                    
                case CircuitState.Open:
                    // Check if reset timeout has elapsed
                    if (DateTime.UtcNow - _openTime > _resetTimeout)
                    {
                        _logger.Info("Circuit breaker transitioning from Open to Half-Open");
                        _currentState = CircuitState.HalfOpen;
                        return true;
                    }
                    return false;
                    
                case CircuitState.HalfOpen:
                    // Only let one request through
                    return true;
                    
                default:
                    return false;
            }
        }
    }
    
    public void RecordSuccess()
    {
        lock (_stateLock)
        {
            switch (_currentState)
            {
                case CircuitState.HalfOpen:
                    _logger.Info("Circuit breaker reset after successful request");
                    _currentState = CircuitState.Closed;
                    _failureCount = 0;
                    break;
                    
                case CircuitState.Closed:
                    _failureCount = 0;
                    break;
            }
        }
    }
    
    public void RecordFailure()
    {
        lock (_stateLock)
        {
            switch (_currentState)
            {
                case CircuitState.HalfOpen:
                    _logger.Warn("Circuit breaker remaining open after test request failure");
                    _currentState = CircuitState.Open;
                    _openTime = DateTime.UtcNow;
                    break;
                    
                case CircuitState.Closed:
                    _failureCount++;
                    if (_failureCount >= _failureThreshold)
                    {
                        _logger.Warn($"Circuit breaker tripped after {_failureCount} consecutive failures");
                        _currentState = CircuitState.Open;
                        _openTime = DateTime.UtcNow;
                    }
                    break;
            }
        }
    }
}
```

**Technical Details:**
- Standard Circuit Breaker pattern implementation
- State transitions: Closed -> Open -> Half-Open -> Closed
- Configurable failure threshold and reset timeout
- Thread-safe implementation with locking
- Detailed logging of circuit state changes

## Implementation Timeline

### Immediate Fixes (1-2 Weeks)

1. **Implement core duplicate detection fixes**
   - Apply the approach used in our initial fix to ensure items are removed from queue before processing
   - Add more robust equality comparison for download items
   - Add diagnostic logging to trace item lifecycle

2. **Search queue robustness improvements**
   - Enhance the queue clearing mechanism
   - Fix semaphore management
   - Add proper cancellation token usage

### Short-term Improvements (2-4 Weeks)

1. **Overhaul thread synchronization**
   - Replace basic locking with more sophisticated mechanisms
   - Document and enforce lock hierarchy
   - Add timeouts to all blocking calls

2. **Implement structured logging**
   - Add correlation IDs for tracing requests
   - Implement throttled logging
   - Add operation context to all log messages

3. **Update authentication management**
   - Implement token refresh mechanism
   - Add circuit breaker for API calls
   - Handle connection failures gracefully

### Medium-term Development (1-3 Months)

1. **Complete rewrite of search system**
   - Migrate to Channels-based implementation
   - Implement proper backpressure
   - Add comprehensive diagnostics

2. **Memory management framework**
   - Implement resource tracking
   - Add memory usage monitoring
   - Fix potential memory leaks

3. **Collection synchronization improvements**
   - Rewrite thread-safety mechanisms
   - Add explicit deadlock detection
   - Improve performance with reader-writer locks

### Long-term Architecture (3-6 Months)

1. **API resilience layer**
   - Full circuit breaker implementation
   - Retry policies with exponential backoff
   - Request coalescing for high-load scenarios

2. **Complete diagnostics system**
   - Performance metrics collection
   - Operational health monitoring
   - Automated anomaly detection

3. **Test suite expansion**
   - Comprehensive unit tests for concurrency scenarios
   - Integration tests for full-system behavior
   - Load tests to verify scalability

## Success Criteria

1. **Reliability Metrics**
   - Zero duplicated downloads
   - No search queue stalls
   - Graceful handling of API errors
   - Proper cleanup of resources

2. **Performance Goals**
   - Support for concurrent search operations
   - Minimal memory usage growth over time
   - Fast response times for search operations
   - Efficient use of Tidal API rate limits

3. **Maintainability Standards**
   - Comprehensive logging
   - Clear diagnostic messages
   - Documented thread-safety requirements
   - Consistent error handling patterns

## Conclusion

This roadmap outlines a comprehensive approach to addressing the current issues with the Tidal indexer component. By implementing the proposed changes in a phased manner, we can systematically resolve the problems while improving the overall architecture and reliability of the system.

The most critical fixes for duplicate processing and search queue stalling should be implemented immediately, followed by the more extensive architectural improvements over time. Each phase builds on the previous one, creating a progressively more robust and maintainable system. 