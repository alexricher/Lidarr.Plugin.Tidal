using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Download.Clients.Tidal;
using NzbDrone.Core.Download.Clients.Tidal.Utilities;
using NzbDrone.Core.Indexers.Tidal;
using NzbDrone.Core.Download.Clients.Tidal.Services;
using Lidarr.Plugin.Tidal.Download.Clients.Tidal.Utilities;

namespace Lidarr.Plugin.Tidal.Services
{
    /// <summary>
    /// Implements rate limiting for Tidal API requests.
    /// This class uses the TokenBucketRateLimiter to enforce rate limits.
    /// </summary>
    public class RateLimiter : IRateLimiter
    {
        private readonly ConcurrentDictionary<TidalRequestType, ITokenBucketRateLimiter> _rateLimiters;
        private readonly TidalSettings _downloadSettings;
        private readonly TidalIndexerSettings _indexerSettings;
        private readonly Logger _logger;
        private volatile bool _disposed;

        /// <summary>
        /// Initializes a new instance of the RateLimiter class.
        /// </summary>
        /// <param name="downloadSettings">The download settings to use.</param>
        /// <param name="indexerSettings">The indexer settings to use.</param>
        /// <param name="logger">The logger to use for logging.</param>
        public RateLimiter(TidalSettings downloadSettings, TidalIndexerSettings indexerSettings, Logger logger)
        {
            _downloadSettings = downloadSettings ?? throw new ArgumentNullException(nameof(downloadSettings));
            _indexerSettings = indexerSettings ?? throw new ArgumentNullException(nameof(indexerSettings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _rateLimiters = new ConcurrentDictionary<TidalRequestType, ITokenBucketRateLimiter>();

            InitializeRateLimiters();
        }

        /// <summary>
        /// Initializes the rate limiters for each request type.
        /// </summary>
        private void InitializeRateLimiters()
        {
            try
            {
                // Create rate limiter for search requests
                var searchRateLimiter = TokenBucketRateLimiter.FromTidalIndexerSettings(_indexerSettings, _logger);
                _rateLimiters[TidalRequestType.Search] = searchRateLimiter;
                _logger.Debug($"Initialized search rate limiter: {_indexerSettings.MaxRequestsPerMinute} requests/minute");

                // Create rate limiter for download requests
                var downloadRateLimiter = TokenBucketRateLimiter.FromTidalSettings(_downloadSettings, _logger);
                _rateLimiters[TidalRequestType.Download] = downloadRateLimiter;
                _logger.Debug($"Initialized download rate limiter: {_downloadSettings.MaxDownloadsPerHour} downloads/hour");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error initializing rate limiters");
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task WaitForSlot(TidalRequestType requestType, CancellationToken token = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RateLimiter));

            // Get the rate limiter for the request type
            if (!_rateLimiters.TryGetValue(requestType, out var rateLimiter))
            {
                throw new ArgumentException($"Unknown request type: {requestType}");
            }

            try
            {
                _logger.Debug($"[{requestType}] Waiting for rate limit slot");
                await rateLimiter.WaitForTokenAsync(token).ConfigureAwait(false);
                _logger.Debug($"[{requestType}] Rate limit slot acquired");
            }
            catch (OperationCanceledException)
            {
                _logger.Debug($"[{requestType}] Wait for rate limit slot was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[{requestType}] Error waiting for rate limit slot: {ex.Message}");
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
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error consuming token for {requestType}: {ex.Message}");
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
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error getting estimated wait time for {requestType}: {ex.Message}");
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
                int rateLimit = rateLimiter.GetCurrentRateLimit();

                // If rate limiting is disabled (0), return 0
                if (rateLimit <= 0)
                {
                    return 0;
                }

                // Calculate the number of requests made based on tokens consumed
                double maxTokens = rateLimiter.GetMaxTokenCount();
                double currentTokens = rateLimiter.GetCurrentTokenCount();

                return Math.Max(0, (int)Math.Ceiling(maxTokens - currentTokens));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error getting current request count for {requestType}: {ex.Message}");
                return 0;
            }
        }

        /// <inheritdoc/>
        public void OnSettingsChanged(TidalSettings newSettings, TidalRequestType type)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RateLimiter));

            try
            {
                if (_rateLimiters.TryGetValue(type, out var rateLimiter))
                {
                    // Update the rate limiter settings
                    int newRateLimit = type switch
                    {
                        TidalRequestType.Search => newSettings.MaxDownloadsPerHour, // This should be from indexer settings
                        TidalRequestType.Download => newSettings.MaxDownloadsPerHour,
                        _ => throw new ArgumentException($"Unknown request type: {type}")
                    };

                    rateLimiter.UpdateSettings(newRateLimit);
                    _logger.Info($"[{type}] Rate limiter settings updated to {newRateLimit}/hour");
                }
                else
                {
                    _logger.Warn($"[{type}] Rate limiter not found for settings update");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error updating rate limiter settings for {type}: {ex.Message}");
            }
        }

        /// <inheritdoc/>
        public void UpdateSettings(TidalSettings downloadSettings, TidalIndexerSettings indexerSettings)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RateLimiter));

            try
            {
                // Update search rate limiter settings
                OnSettingsChanged(downloadSettings, TidalRequestType.Search);
                
                // Update download rate limiter settings
                OnSettingsChanged(downloadSettings, TidalRequestType.Download);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating rate limiter settings");
            }
        }

        /// <summary>
        /// Disposes the rate limiter and all token buckets.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                _disposed = true;

                // Dispose all rate limiters
                foreach (var kvp in _rateLimiters)
                {
                    try
                    {
                        kvp.Value?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Error disposing rate limiter for {kvp.Key}");
                    }
                }

                _rateLimiters.Clear();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error disposing rate limiter");
            }
        }
    }
}
