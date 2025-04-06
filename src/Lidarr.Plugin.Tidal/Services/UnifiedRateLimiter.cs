using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Download.Clients.Tidal;
using NzbDrone.Core.Indexers.Tidal;
using NzbDrone.Core.Download.Clients.Tidal.Services;
using Lidarr.Plugin.Tidal.Download.Clients.Tidal.Utilities;
using Lidarr.Plugin.Tidal.Services.Logging;
using NzbDrone.Plugin.Tidal.Constants;
using NzbDrone.Plugin.Tidal.Indexers.Tidal;

namespace Lidarr.Plugin.Tidal.Services
{
    /// <summary>
    /// Unified rate limiter that handles both download and search rate limiting.
    /// This is the definitive implementation of IRateLimiter, consolidating all rate limiting functionality.
    /// All other rate limiting implementations should be phased out in favor of this one.
    /// </summary>
    public class UnifiedRateLimiter : IRateLimiter
    {
        private readonly Dictionary<TidalRequestType, ITokenBucketRateLimiter> _rateLimiters;
        private readonly Logger _logger;
        private SemaphoreSlim _searchSemaphore;
        private readonly object _lockObject = new();
        private volatile bool _disposed;
        
        // Metrics tracking
        private long _totalRequests;
        private long _throttledRequests;
        private DateTime _lastMetricsLog = DateTime.MinValue;
        private readonly TimeSpan _metricsLogInterval = TimeSpan.FromHours(1);

        // Default values
        private const int DEFAULT_MAX_CONCURRENT_SEARCHES = TidalConstants.DefaultMaxConcurrentSearches;
        private const int DEFAULT_MAX_REQUESTS_PER_MINUTE = TidalConstants.DefaultMaxRequestsPerMinute;
        private const int DEFAULT_MAX_DOWNLOADS_PER_HOUR = TidalConstants.DefaultDownloadsPerHour;

        // Lock for thread-safe access to _searchSemaphore
        private readonly object _semaphoreLock = new();

        /// <summary>
        /// Gets the current count of available search slots
        /// </summary>
        public int CurrentCount
        {
            get
            {
                lock (_semaphoreLock)
                {
                    return _searchSemaphore?.CurrentCount ?? 0;
                }
            }
        }

        /// <summary>
        /// Gets the current number of active search requests being processed
        /// </summary>
        public int ActiveSearchCount
        {
            get
            {
                lock (_semaphoreLock)
                {
                    if (_searchSemaphore == null)
                    {
                        return 0;
                    }

                    int maxCount = 0;
                    
                    // Get the max count from the semaphore if available
                    try
                    {
                        // Use reflection to get the maximum count if available
                        var field = _searchSemaphore.GetType().GetField("m_maxCount", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (field != null)
                        {
                            maxCount = (int)field.GetValue(_searchSemaphore);
                        }
                        else
                        {
                            maxCount = DEFAULT_MAX_CONCURRENT_SEARCHES;
                        }
                    }
                    catch
                    {
                        maxCount = DEFAULT_MAX_CONCURRENT_SEARCHES;
                    }

                    return maxCount - _searchSemaphore.CurrentCount;
                }
            }
        }
        
        /// <summary>
        /// Gets the total number of requests processed since initialization
        /// </summary>
        public long TotalRequests => Interlocked.Read(ref _totalRequests);
        
        /// <summary>
        /// Gets the total number of requests that were throttled
        /// </summary>
        public long ThrottledRequests => Interlocked.Read(ref _throttledRequests);
        
        /// <summary>
        /// Gets the throttling percentage (throttled requests / total requests)
        /// </summary>
        public double ThrottlingPercentage
        {
            get
            {
                var total = TotalRequests;
                if (total == 0) return 0;
                return (double)ThrottledRequests / total * 100.0;
            }
        }

        /// <summary>
        /// Initializes a new instance of the UnifiedRateLimiter class
        /// </summary>
        /// <param name="downloadSettings">Tidal download settings</param>
        /// <param name="indexerSettings">Tidal indexer settings</param>
        /// <param name="logger">Logger for diagnostic messages</param>
        public UnifiedRateLimiter(TidalSettings downloadSettings, TidalIndexerSettings indexerSettings, Logger logger)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _rateLimiters = new Dictionary<TidalRequestType, ITokenBucketRateLimiter>();

            try
            {
                _logger.InfoWithEmoji(LogEmojis.Settings, "Initializing unified rate limiter with current settings");
                InitializeRateLimiters(downloadSettings, indexerSettings);
            }
            catch (ArgumentException ex)
            {
                _logger?.ErrorWithEmoji(LogEmojis.Error, ex, "Invalid argument when initializing rate limiter: {0}", ex.Message);
                // Create default limiters as fallback
                InitializeDefaultRateLimiters();
            }
            catch (InvalidOperationException ex)
            {
                _logger?.ErrorWithEmoji(LogEmojis.Error, ex, "Invalid operation when initializing rate limiter: {0}", ex.Message);
                // Create default limiters as fallback
                InitializeDefaultRateLimiters();
            }
        }

        /// <summary>
        /// Initializes default rate limiters if the main initialization fails
        /// </summary>
        private void InitializeDefaultRateLimiters()
        {
            try
            {
                _logger?.InfoWithEmoji(LogEmojis.Warning, "Using safe default rate limits");

                // Initialize search semaphore with default values
                lock (_semaphoreLock)
                {
                    _searchSemaphore = new SemaphoreSlim(DEFAULT_MAX_CONCURRENT_SEARCHES, DEFAULT_MAX_CONCURRENT_SEARCHES);
                }

                // Initialize token bucket rate limiters for each request type
                if (!_rateLimiters.ContainsKey(TidalRequestType.Search))
                {
                    _rateLimiters[TidalRequestType.Search] = new TokenBucketRateLimiter(DEFAULT_MAX_REQUESTS_PER_MINUTE * 60, _logger);
                }

                if (!_rateLimiters.ContainsKey(TidalRequestType.Download))
                {
                    _rateLimiters[TidalRequestType.Download] = new TokenBucketRateLimiter(DEFAULT_MAX_DOWNLOADS_PER_HOUR, _logger); 
                }
            }
            catch (ArgumentException ex)
            {
                _logger?.ErrorWithEmoji(LogEmojis.Error, ex, "Invalid argument when initializing default rate limiters: {0}", ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger?.ErrorWithEmoji(LogEmojis.Error, ex, "Invalid operation when initializing default rate limiters: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Initializes the rate limiters for each request type
        /// </summary>
        private void InitializeRateLimiters(TidalSettings downloadSettings, TidalIndexerSettings indexerSettings)
        {
            _logger?.Debug("Initializing rate limiters with custom settings");

            // Initialize search semaphore
            var maxConcurrentSearches = indexerSettings?.MaxConcurrentSearches ?? DEFAULT_MAX_CONCURRENT_SEARCHES;
            lock (_semaphoreLock)
            {
                _searchSemaphore = new SemaphoreSlim(maxConcurrentSearches, maxConcurrentSearches);
                _logger?.DebugWithEmoji(LogEmojis.Debug, $"Initialized search semaphore with {maxConcurrentSearches} concurrent slots");
            }

            // Initialize search rate limiter
            var searchRateLimiter = TokenBucketRateLimiter.FromTidalIndexerSettings(indexerSettings, _logger);
            _rateLimiters[TidalRequestType.Search] = searchRateLimiter;
            _logger?.Debug($"Initialized search rate limiter: {indexerSettings?.MaxRequestsPerMinute ?? DEFAULT_MAX_REQUESTS_PER_MINUTE} requests/minute");

            // Initialize download rate limiter
            var downloadRateLimiter = TokenBucketRateLimiter.FromTidalSettings(downloadSettings, _logger);
            _rateLimiters[TidalRequestType.Download] = downloadRateLimiter;
            _logger?.Debug($"Initialized download rate limiter: {downloadSettings?.MaxDownloadsPerHour ?? TidalConstants.DefaultDownloadsPerHour} downloads/hour");
            
            // Log summary
            _logger?.InfoWithEmoji(LogEmojis.Settings, 
                $"Rate limiting configured: {maxConcurrentSearches} concurrent searches, " +
                $"{indexerSettings?.MaxRequestsPerMinute ?? DEFAULT_MAX_REQUESTS_PER_MINUTE} searches/minute, " +
                $"{downloadSettings?.MaxDownloadsPerHour ?? TidalConstants.DefaultDownloadsPerHour} downloads/hour");
        }

        /// <summary>
        /// Logs current rate limiting metrics if sufficient time has passed since the last log
        /// </summary>
        private void LogMetricsIfNeeded()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastMetricsLog) >= _metricsLogInterval)
            {
                _lastMetricsLog = now;
                _logger?.InfoWithEmoji(LogEmojis.Stats, 
                    $"Rate limiting metrics: {TotalRequests} total requests, " +
                    $"{ThrottledRequests} throttled ({ThrottlingPercentage:F1}%)");
                
                // Log detailed stats for each rate limiter
                foreach (var kvp in _rateLimiters)
                {
                    _logger?.Debug($"Rate limiter for {kvp.Key}: {kvp.Value.GetCurrentTokenCount():F1}/{kvp.Value.GetMaxTokenCount():F1} tokens, " +
                        $"{kvp.Value.GetCurrentRateLimit()} ops/hour");
                }
            }
        }

        /// <summary>
        /// Initializes the rate limiter with specific limits
        /// </summary>
        /// <param name="maxConcurrentSearches">Maximum number of concurrent searches</param>
        /// <param name="maxRequestsPerMinute">Maximum requests per minute</param>
        public void Initialize(int maxConcurrentSearches, int maxRequestsPerMinute)
        {
            lock (_lockObject)
            {
                try
                {
                    _logger?.Debug($"Reinitializing rate limiters: MaxConcurrentSearches={maxConcurrentSearches}, MaxRequestsPerMinute={maxRequestsPerMinute}");

                    // Dispose existing semaphore if any and initialize new one
                    lock (_semaphoreLock)
                    {
                        _searchSemaphore?.Dispose();

                        // Initialize new semaphore
                        _searchSemaphore = new SemaphoreSlim(maxConcurrentSearches, maxConcurrentSearches);
                    }

                    // Update search rate limiter
                    if (_rateLimiters.TryGetValue(TidalRequestType.Search, out var searchLimiter))
                    {
                        searchLimiter.UpdateSettings(maxRequestsPerMinute * 60); // Convert to per hour
                    }
                    else
                    {
                        _rateLimiters[TidalRequestType.Search] = new TokenBucketRateLimiter(maxRequestsPerMinute * 60, _logger);
                    }

                    _logger?.Info($"Rate limiters reinitialized: MaxConcurrentSearches={maxConcurrentSearches}, MaxRequestsPerMinute={maxRequestsPerMinute}");
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "Error initializing rate limiters");
                    throw;
                }
            }
        }

        /// <inheritdoc/>
        public async Task WaitForSlot(TidalRequestType requestType, CancellationToken token = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UnifiedRateLimiter));
            
            // Increment total requests counter
            Interlocked.Increment(ref _totalRequests);
            bool wasThrottled = false;
            DateTime startTime = DateTime.UtcNow;

            try
            {
                // First, acquire semaphore slot for concurrent requests (only for Search)
                SemaphoreSlim semaphore = null;
                lock (_semaphoreLock)
                {
                    if (requestType == TidalRequestType.Search && _searchSemaphore != null)
                    {
                        semaphore = _searchSemaphore;
                        _logger?.DebugWithEmoji(LogEmojis.Wait, $"Waiting for search semaphore slot (available: {_searchSemaphore.CurrentCount})");
                    }
                }

                if (semaphore != null)
                {
                    try
                    {
                        await semaphore.WaitAsync(token).ConfigureAwait(false);
                        _logger?.DebugWithEmoji(LogEmojis.Debug, "Search semaphore slot acquired");
                    }
                    catch (OperationCanceledException)
                    {
                        _logger?.DebugWithEmoji(LogEmojis.Cancel, "Search semaphore wait was canceled");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger?.ErrorWithEmoji(LogEmojis.Error, ex, "Error waiting for search semaphore");
                        throw;
                    }
                }

                try
                {
                    // Get the rate limiter for the request type
                    if (!_rateLimiters.TryGetValue(requestType, out var rateLimiter))
                    {
                        _logger?.Warn($"Unknown request type: {requestType}, using default wait logic");
                        await Task.Delay(100, token).ConfigureAwait(false);
                        return;
                    }

                    // Check if wait is needed
                    var waitTime = rateLimiter.GetEstimatedWaitTime();
                    if (waitTime > TimeSpan.Zero)
                    {
                        wasThrottled = true;
                        if (waitTime.TotalSeconds > 1)
                        {
                            _logger?.DebugWithEmoji(LogEmojis.Wait, 
                                $"Rate limit hit for {requestType}, waiting {waitTime.TotalSeconds:F1}s (current: {rateLimiter.GetCurrentTokenCount():F1}, max: {rateLimiter.GetMaxTokenCount():F1})");
                        }
                        
                        // Track throttled requests
                        Interlocked.Increment(ref _throttledRequests);
                    }

                    // Wait for a token to become available
                    await rateLimiter.WaitForTokenAsync(token).ConfigureAwait(false);
                    
                    // Log metrics periodically
                    LogMetricsIfNeeded();
                }
                catch (OperationCanceledException)
                {
                    _logger?.DebugWithEmoji(LogEmojis.Cancel, $"Rate limiter wait was canceled for {requestType}");
                    throw;
                }
                catch (Exception ex)
                {
                    _logger?.ErrorWithEmoji(LogEmojis.Error, ex, $"Error waiting for rate limiter for {requestType}");
                    
                    // Try to release the semaphore if we had acquired it
                    if (semaphore != null)
                    {
                        try { semaphore.Release(); } catch { /* Ignore errors during cleanup */ }
                    }
                    
                    throw;
                }
            }
            finally
            {
                var elapsed = DateTime.UtcNow - startTime;
                if (wasThrottled && elapsed.TotalSeconds > 5)
                {
                    _logger?.InfoWithEmoji(LogEmojis.Time, 
                        $"Rate limiting for {requestType} took {elapsed.TotalSeconds:F1}s");
                }
            }
        }

        /// <inheritdoc/>
        public bool TryConsumeToken(TidalRequestType requestType)
        {
            if (_disposed) return false;

            // Get the rate limiter for the request type
            if (!_rateLimiters.TryGetValue(requestType, out var rateLimiter))
            {
                return false;
            }

            try
            {
                return rateLimiter.TryConsumeToken();
            }
            catch (ObjectDisposedException ex)
            {
                _logger?.ErrorWithEmoji(LogEmojis.Error, ex, $"Rate limiter was disposed while consuming token for {requestType}: {ex.Message}");
                return false;
            }
            catch (InvalidOperationException ex)
            {
                _logger?.ErrorWithEmoji(LogEmojis.Error, ex, $"Rate limiter operation error for {requestType}: {ex.Message}");
                return false;
            }
        }

        /// <inheritdoc/>
        public TimeSpan GetEstimatedWaitTime(TidalRequestType requestType)
        {
            if (_disposed) return TimeSpan.Zero;

            // Get the rate limiter for the request type
            if (!_rateLimiters.TryGetValue(requestType, out var rateLimiter))
            {
                return TimeSpan.Zero;
            }

            try
            {
                return rateLimiter.GetEstimatedWaitTime();
            }
            catch (ObjectDisposedException ex)
            {
                _logger?.ErrorWithEmoji(LogEmojis.Error, ex, $"Rate limiter was disposed while getting wait time for {requestType}: {ex.Message}");
                return TimeSpan.Zero;
            }
            catch (InvalidOperationException ex)
            {
                _logger?.ErrorWithEmoji(LogEmojis.Error, ex, $"Rate limiter operation error for {requestType}: {ex.Message}");
                return TimeSpan.Zero;
            }
        }

        /// <inheritdoc/>
        public int GetCurrentRequestCount(TidalRequestType requestType)
        {
            if (_disposed) return 0;

            // Get the rate limiter for the request type
            if (!_rateLimiters.TryGetValue(requestType, out var rateLimiter))
            {
                return 0;
            }

            try
            {
                // Get the current rate limit
                var rateLimit = rateLimiter.GetCurrentRateLimit();

                // If rate limiting is disabled (0), return 0
                if (rateLimit <= 0)
                {
                    return 0;
                }

                // Calculate the number of requests made based on tokens consumed
                var maxTokens = rateLimiter.GetMaxTokenCount();
                var currentTokens = rateLimiter.GetCurrentTokenCount();

                return Math.Max(0, (int)Math.Ceiling(maxTokens - currentTokens));
            }
            catch (ObjectDisposedException ex)
            {
                _logger?.ErrorWithEmoji(LogEmojis.Error, ex, $"Rate limiter was disposed while getting request count for {requestType}: {ex.Message}");
                return 0;
            }
            catch (InvalidOperationException ex)
            {
                _logger?.ErrorWithEmoji(LogEmojis.Error, ex, $"Rate limiter operation error for {requestType}: {ex.Message}");
                return 0;
            }
        }

        /// <inheritdoc/>
        public void UpdateSettings(TidalSettings downloadSettings, TidalIndexerSettings indexerSettings)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UnifiedRateLimiter));

            try
            {
                lock (_lockObject)
                {
                    // Update search rate limiter
                    if (_rateLimiters.TryGetValue(TidalRequestType.Search, out var searchLimiter))
                    {
                        var maxRequestsPerMinute = indexerSettings?.MaxRequestsPerMinute ?? DEFAULT_MAX_REQUESTS_PER_MINUTE;
                        searchLimiter.UpdateSettings(maxRequestsPerMinute * 60); // Convert to per hour
                        _logger?.Debug($"Updated search rate limiter: {maxRequestsPerMinute} requests/minute");
                    }

                    // Update download rate limiter
                    if (_rateLimiters.TryGetValue(TidalRequestType.Download, out var downloadLimiter))
                    {
                        var maxDownloadsPerHour = downloadSettings?.MaxDownloadsPerHour ?? TidalConstants.DefaultDownloadsPerHour;
                        downloadLimiter.UpdateSettings(maxDownloadsPerHour);
                        _logger?.Debug($"Updated download rate limiter: {maxDownloadsPerHour} downloads/hour");
                    }

                    // Update semaphore if MaxConcurrentSearches changed
                    var maxConcurrentSearches = indexerSettings?.MaxConcurrentSearches ?? DEFAULT_MAX_CONCURRENT_SEARCHES;
                    lock (_semaphoreLock)
                    {
                        if (_searchSemaphore != null && _searchSemaphore.CurrentCount != maxConcurrentSearches)
                        {
                            _searchSemaphore.Dispose();
                            _searchSemaphore = new SemaphoreSlim(maxConcurrentSearches, maxConcurrentSearches);
                            _logger?.DebugWithEmoji(LogEmojis.Settings, $"Updated search semaphore: {maxConcurrentSearches} concurrent searches");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error updating rate limiter settings");
                throw;
            }
        }

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException">Thrown when newSettings is null.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the rate limiter has been disposed.</exception>
        public void OnSettingsChanged(TidalSettings newSettings, TidalRequestType type)
        {
            if (newSettings == null) throw new ArgumentNullException(nameof(newSettings));
            if (_disposed) throw new ObjectDisposedException(nameof(UnifiedRateLimiter));

            try
            {
                // Get the rate limiter for the request type
                if (!_rateLimiters.TryGetValue(type, out var rateLimiter))
                {
                    _logger?.Warn($"Cannot update settings for unknown request type: {type}");
                    return;
                }

                // Update the rate limiter settings
                if (type == TidalRequestType.Download)
                {
                    rateLimiter.UpdateSettings(newSettings.MaxDownloadsPerHour);
                    _logger?.DebugWithEmoji(LogEmojis.Settings, $"Updated {type} rate limiter: {newSettings.MaxDownloadsPerHour} downloads/hour");
                }
            }
            catch (Exception ex)
            {
                _logger?.ErrorWithEmoji(LogEmojis.Error, ex, $"Error updating settings for {type}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Releases a slot in the search semaphore
        /// </summary>
        public void Release()
        {
            if (_disposed) return;

            SemaphoreSlim semaphore = null;
            lock (_semaphoreLock)
            {
                semaphore = _searchSemaphore;
            }

            if (semaphore != null)
            {
                try
                {
                    semaphore.Release();
                    _logger?.DebugWithEmoji(LogEmojis.Debug, "Released search semaphore slot");
                }
                catch (SemaphoreFullException)
                {
                    _logger?.DebugWithEmoji(LogEmojis.Debug, "Semaphore already at maximum count, ignoring release");
                }
                catch (ObjectDisposedException ex)
                {
                    _logger?.ErrorWithEmoji(LogEmojis.Error, ex, "Semaphore was disposed while releasing: {0}", ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    _logger?.ErrorWithEmoji(LogEmojis.Error, ex, "Invalid semaphore operation: {0}", ex.Message);
                }
            }
        }

        /// <summary>
        /// Releases a semaphore slot if one is being held
        /// </summary>
        private void ReleaseSemaphore()
        {
            SemaphoreSlim semaphore = null;
            lock (_semaphoreLock)
            {
                semaphore = _searchSemaphore;
            }

            if (semaphore == null) return;

            try
            {
                semaphore.Release();
                _logger?.DebugWithEmoji(LogEmojis.Debug, "Released search semaphore slot");
            }
            catch (SemaphoreFullException)
            {
                _logger?.DebugWithEmoji(LogEmojis.Debug, "Semaphore already at maximum count, ignoring release");
            }
            catch (ObjectDisposedException ex)
            {
                _logger?.ErrorWithEmoji(LogEmojis.Error, ex, "Semaphore was disposed while releasing: {0}", ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger?.ErrorWithEmoji(LogEmojis.Error, ex, "Invalid semaphore operation: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Disposes the rate limiter and releases all resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes the rate limiter resources.
        /// </summary>
        /// <param name="disposing">True if called from Dispose(), false if called from finalizer.</param>
        private void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                try
                {
                    // Dispose semaphore
                    _searchSemaphore?.Dispose();
                    _searchSemaphore = null;

                    // Dispose all rate limiters
                    foreach (var limiter in _rateLimiters.Values)
                    {
                        limiter?.Dispose();
                    }

                    _rateLimiters.Clear();

                    _logger?.DebugWithEmoji(LogEmojis.Debug, "UnifiedRateLimiter disposed");
                }
                catch (ObjectDisposedException ex)
                {
                    _logger?.ErrorWithEmoji(LogEmojis.Error, ex, "Component already disposed: {0}", ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    _logger?.ErrorWithEmoji(LogEmojis.Error, ex, "Invalid operation during disposal: {0}", ex.Message);
                }
            }
        }
    }
}



