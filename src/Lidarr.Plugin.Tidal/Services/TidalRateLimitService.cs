using System;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Tidal.Services;
using Lidarr.Plugin.Tidal.Services.Logging;
using NLog;
using NzbDrone.Core.Download.Clients.Tidal;
using NzbDrone.Core.Download.Clients.Tidal.Services;

namespace NzbDrone.Core.Indexers.Tidal
{
    /// <summary>
    /// Interface for the Tidal rate limiting service.
    /// Provides methods to control access to Tidal API based on rate limits.
    /// </summary>
    public interface ITidalRateLimitService
    {
        /// <summary>
        /// Waits for a slot to become available before accessing Tidal API.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>Task representing the async operation</returns>
        Task WaitForSlot(CancellationToken cancellationToken = default);

        /// <summary>
        /// Releases a slot after an operation is complete.
        /// </summary>
        void Release();

        /// <summary>
        /// Gets the current number of requests being processed.
        /// </summary>
        int CurrentRequestCount { get; }

        /// <summary>
        /// Gets the current count of available slots.
        /// </summary>
        int CurrentCount { get; }

        /// <summary>
        /// Initializes the rate limiting service with specific limits.
        /// </summary>
        /// <param name="maxConcurrentSearches">Maximum number of concurrent searches</param>
        /// <param name="maxRequestsPerMinute">Maximum requests per minute</param>
        void Initialize(int maxConcurrentSearches, int maxRequestsPerMinute);
    }

    /// <summary>
    /// Implements rate limiting for Tidal API requests to prevent exceeding service limits.
    /// </summary>
    public class TidalRateLimitService : ITidalRateLimitService, IDisposable
    {
        private SemaphoreSlim _searchSemaphore;
        private IRateLimiter _rateLimiter;
        private readonly Logger _logger;
        private readonly TidalSettings _downloadSettings;
        private readonly TidalIndexerSettings _indexerSettings;
        private bool _isInitialized;
        private readonly object _initLock = new object();
        private bool _isDisposed;
        private int _maxConcurrentSearches; // Track the maximum count for logging

        // Default values to use if not explicitly initialized
        private const int DEFAULT_MAX_CONCURRENT_SEARCHES = 5;
        private const int DEFAULT_MAX_REQUESTS_PER_MINUTE = 30;

        /// <summary>
        /// Creates a new instance of the TidalRateLimitService.
        /// </summary>
        /// <param name="logger">Logger for recording activity</param>
        /// <param name="downloadSettings">Tidal download settings</param>
        /// <param name="indexerSettings">Tidal indexer settings</param>
        /// <param name="rateLimiter">Rate limiter implementation</param>
        public TidalRateLimitService(Logger logger, TidalSettings downloadSettings, TidalIndexerSettings indexerSettings, IRateLimiter rateLimiter)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _downloadSettings = downloadSettings ?? throw new ArgumentNullException(nameof(downloadSettings));
            _indexerSettings = indexerSettings ?? throw new ArgumentNullException(nameof(indexerSettings));
            _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));

            // Initialize with default values at construction to avoid null reference exceptions
            InitializeInternal(DEFAULT_MAX_CONCURRENT_SEARCHES, DEFAULT_MAX_REQUESTS_PER_MINUTE);
            _logger.Debug($"TidalRateLimitService constructed with default values: MaxConcurrentSearches={DEFAULT_MAX_CONCURRENT_SEARCHES}, MaxRequestsPerMinute={DEFAULT_MAX_REQUESTS_PER_MINUTE}");
        }

        /// <summary>
        /// Initializes the rate limiter with specified parameters.
        /// </summary>
        /// <param name="maxConcurrentSearches">Maximum number of concurrent searches</param>
        /// <param name="maxRequestsPerMinute">Maximum requests per minute</param>
        public void Initialize(int maxConcurrentSearches, int maxRequestsPerMinute)
        {
            try
            {
                // Validate parameters
                if (maxConcurrentSearches <= 0)
                {
                    _logger.Warn($"Invalid maxConcurrentSearches value: {maxConcurrentSearches}, using default {DEFAULT_MAX_CONCURRENT_SEARCHES}");
                    maxConcurrentSearches = DEFAULT_MAX_CONCURRENT_SEARCHES;
                }

                if (maxRequestsPerMinute <= 0)
                {
                    _logger.Warn($"Invalid maxRequestsPerMinute value: {maxRequestsPerMinute}, using default {DEFAULT_MAX_REQUESTS_PER_MINUTE}");
                    maxRequestsPerMinute = DEFAULT_MAX_REQUESTS_PER_MINUTE;
                }

                lock (_initLock)
                {
                    InitializeInternal(maxConcurrentSearches, maxRequestsPerMinute);
                }

                _logger.Debug($"TidalRateLimitService initialized with: MaxConcurrentSearches={maxConcurrentSearches}, MaxRequestsPerMinute={maxRequestsPerMinute}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error initializing TidalRateLimitService");
                // Ensure we have valid settings even if initialization fails
                lock (_initLock)
                {
                    if (!_isInitialized)
                    {
                        InitializeInternal(DEFAULT_MAX_CONCURRENT_SEARCHES, DEFAULT_MAX_REQUESTS_PER_MINUTE);
                    }
                }
            }
        }

        /// <summary>
        /// Internal initialization method that handles the actual setup.
        /// </summary>
        /// <param name="maxConcurrentSearches">Maximum concurrent searches</param>
        /// <param name="maxRequestsPerMinute">Maximum requests per minute</param>
        private void InitializeInternal(int maxConcurrentSearches, int maxRequestsPerMinute)
        {
            ThrowIfDisposed();

            // Clean up existing resources first
            if (_searchSemaphore != null)
            {
                try
                {
                    _searchSemaphore.Dispose();
                }
                catch
                {
                    // Ignore errors during disposal
                }
            }

            _searchSemaphore = new SemaphoreSlim(maxConcurrentSearches, maxConcurrentSearches);
            _maxConcurrentSearches = maxConcurrentSearches; // Store for later use
            _isInitialized = true;
        }

        /// <summary>
        /// Waits for a slot to become available before accessing Tidal API.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>Task representing the async operation</returns>
        public async Task WaitForSlot(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var startWait = DateTime.UtcNow;

            // Guard against semaphore leaks - if current count is negative, something went wrong
            // This should never happen with proper usage, but we'll guard against it anyway
            try
            {
                GuardAgainstSemaphoreLeaks();
            }
            catch (Exception ex)
            {
                _logger.ErrorWithEmoji(LogEmojis.Error, ex, "Error checking for semaphore leaks");
                // Continue despite errors - we'll try to reinitialize if needed
            }

            try
            {
                // Ensure we're initialized
                EnsureInitialized();

                int currentActiveSearches = Math.Max(0, _maxConcurrentSearches - _searchSemaphore.CurrentCount);
                _logger.Debug($"Waiting for search semaphore (active searches: {currentActiveSearches}/{_maxConcurrentSearches}, available slots: {_searchSemaphore.CurrentCount})");

                // IMPROVED: Use 90-second timeout to match the request generator
                bool acquired = false;

                // Try to acquire the semaphore first without a timeout to see if it's immediately available
                try
                {
                    acquired = _searchSemaphore.Wait(0, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw; // Propagate cancellation
                }

                if (!acquired)
                {
                    // IMPROVED: Log more user-friendly information about wait times
                    int waitingActiveSearches = Math.Max(0, _maxConcurrentSearches - _searchSemaphore.CurrentCount);
                    _logger.Info($"🔄 Search rate limit active - waiting for a slot to become available (active searches: {waitingActiveSearches}/{_maxConcurrentSearches})");

                    // IMPROVED: Longer timeout and more informative error message
                    acquired = await _searchSemaphore.WaitAsync(TimeSpan.FromSeconds(90), cancellationToken);

                    if (!acquired)
                    {
                        throw new TimeoutException("Failed to acquire search semaphore after 90 seconds - system may be overloaded");
                    }
                }

                int acquiredActiveSearches = Math.Max(0, _maxConcurrentSearches - _searchSemaphore.CurrentCount);
                _logger.Debug($"Search semaphore acquired successfully (active searches: {acquiredActiveSearches}/{_maxConcurrentSearches})");

                // Ensure rate limiter is not null before using it
                if (_rateLimiter == null)
                {
                    _logger.Warn("Rate limiter was null when trying to wait for slot, will skip rate limiting");
                    return;
                }

                try
                {
                    // IMPROVED: Use a shorter timeout for the token bucket rate limiter
                    var rateLimiterTask = _rateLimiter.WaitForSlot(TidalRequestType.Search, cancellationToken);
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

                    var completedTask = await Task.WhenAny(rateLimiterTask, timeoutTask);
                    if (completedTask == timeoutTask)
                    {
                        _logger.Warn("⏱️ Rate limiter wait timed out after 30 seconds, continuing anyway");
                        // We continue even when token bucket times out as semaphore already provides basic rate limiting
                    }
                }
                catch (Exception ex)
                {
                    _logger.WarnWithEmoji(LogEmojis.Warning, $"Error waiting for rate limiter slot, continuing anyway: {ex.Message}");
                    // We continue even if rate limiting fails, as semaphore already limits concurrency
                }
            }
            catch (TimeoutException ex)
            {
                _logger.ErrorWithEmoji(LogEmojis.Error, ex, "Timeout waiting for search semaphore");
                throw;
            }
            catch (OperationCanceledException ex)
            {
                _logger.DebugWithEmoji(LogEmojis.Cancel, $"Operation canceled while waiting for search semaphore: {ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                _logger.ErrorWithEmoji(LogEmojis.Error, ex, "Unexpected error in WaitForSlot");
                throw;
            }
            finally
            {
                var waitTime = DateTime.UtcNow - startWait;
                if (waitTime.TotalMilliseconds > 500)
                {
                    _logger.DebugWithEmoji(LogEmojis.Time, $"Rate limiter delayed request for {waitTime.TotalSeconds:F1}s");
                }
            }
        }

        /// <summary>
        /// Releases a slot after an operation is complete.
        /// </summary>
        public void Release()
        {
            try
            {
                if (_isDisposed)
                {
                    return;
                }

                // Only release if semaphore exists and has been initialized properly
                if (_searchSemaphore != null)
                {
                    // Avoid releasing more slots than were acquired which would cause semaphore corruption
                    if (_searchSemaphore.CurrentCount < _maxConcurrentSearches)
                    {
                        _searchSemaphore.Release();
                        int releasedActiveSearches = Math.Max(0, _maxConcurrentSearches - _searchSemaphore.CurrentCount);
                        _logger.DebugWithEmoji(LogEmojis.Process, $"Search semaphore released (active searches: {releasedActiveSearches}/{_maxConcurrentSearches})");
                    }
                    else
                    {
                        _logger.Warn("Attempted to release semaphore when all slots are already available - skipping release");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorWithEmoji(LogEmojis.Error, ex, "Error releasing search semaphore");
            }
        }

        /// <summary>
        /// Gets the current number of requests being processed through the rate limiter.
        /// </summary>
        public int CurrentRequestCount => _rateLimiter?.GetCurrentRequestCount(TidalRequestType.Search) ?? 0;

        /// <summary>
        /// Gets the current count of available slots in the semaphore.
        /// </summary>
        public int CurrentCount => _searchSemaphore?.CurrentCount ?? 0;

        /// <summary>
        /// Ensures the service is initialized properly.
        /// </summary>
        private void EnsureInitialized()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(TidalRateLimitService));
            }

            if (!_isInitialized || _searchSemaphore == null)
            {
                lock (_initLock)
                {
                    if (!_isInitialized || _searchSemaphore == null)
                    {
                        _logger.WarnWithEmoji(LogEmojis.Warning, "TidalRateLimitService was not properly initialized, initializing with default values");
                        InitializeInternal(DEFAULT_MAX_CONCURRENT_SEARCHES, DEFAULT_MAX_REQUESTS_PER_MINUTE);
                    }
                }
            }
        }

        /// <summary>
        /// Throws an ObjectDisposedException if the service is disposed.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(TidalRateLimitService));
            }
        }

        /// <summary>
        /// Checks for and attempts to fix corrupted semaphore state
        /// </summary>
        private void GuardAgainstSemaphoreLeaks()
        {
            if (_searchSemaphore == null)
            {
                return; // Nothing to check
            }

            // If currentCount > maxCount, the semaphore is in a bad state
            if (_searchSemaphore.CurrentCount > _maxConcurrentSearches)
            {
                _logger.Warn("Semaphore in invalid state! CurrentCount exceeds max value - reinitializing");

                lock (_initLock)
                {
                    try
                    {
                        // Reinitialize to fix the bad state
                        InitializeInternal(_maxConcurrentSearches, DEFAULT_MAX_REQUESTS_PER_MINUTE);
                        _logger.Info("Semaphore reinitialized to fix corrupted state");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to reinitialize semaphore after detecting corrupted state");
                    }
                }
            }
        }

        /// <summary>
        /// Disposes all resources used by the service.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            try
            {
                _searchSemaphore?.Dispose();
                _searchSemaphore = null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error disposing TidalRateLimitService");
            }

            _logger.Debug("TidalRateLimitService disposed");
        }
    }
}
