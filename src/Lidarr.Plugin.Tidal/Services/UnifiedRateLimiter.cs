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

namespace Lidarr.Plugin.Tidal.Services
{
    /// <summary>
    /// Unified rate limiter that handles both download and search rate limiting
    /// This is the primary implementation of IRateLimiter, consolidating functionality
    /// from other rate limiter implementations.
    /// </summary>
    public class UnifiedRateLimiter : IRateLimiter
    {
        private readonly Dictionary<TidalRequestType, ITokenBucketRateLimiter> _rateLimiters;
        private readonly Logger _logger;
        private SemaphoreSlim _searchSemaphore;
        private readonly object _lockObject = new();
        private volatile bool _disposed;

        // Default values
        private const int DEFAULT_MAX_CONCURRENT_SEARCHES = 5;
        private const int DEFAULT_MAX_REQUESTS_PER_MINUTE = 30;

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

                    return _searchSemaphore.AvailableWaitHandle != null ?
                        DEFAULT_MAX_CONCURRENT_SEARCHES - _searchSemaphore.CurrentCount : 0;
                }
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
                _logger?.Debug("Initializing default rate limiters");

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
                    _rateLimiters[TidalRequestType.Download] = new TokenBucketRateLimiter(30 * 60, _logger); // 30 downloads per hour
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
            _logger?.Debug("Initializing rate limiters");

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
            _logger?.Debug($"Initialized download rate limiter: {downloadSettings?.MaxDownloadsPerHour ?? 30} downloads/hour");
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

                // Wait for a token to become available
                await rateLimiter.WaitForTokenAsync(token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // If token wait fails, release the semaphore slot
                if (requestType == TidalRequestType.Search)
                {
                    ReleaseSemaphore();
                }
                throw;
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
                        var maxDownloadsPerHour = downloadSettings?.MaxDownloadsPerHour ?? 30;
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



