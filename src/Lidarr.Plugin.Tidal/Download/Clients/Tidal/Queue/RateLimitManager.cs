using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Download.Clients.Tidal.Utilities;
using Lidarr.Plugin.Tidal.Services.Logging;
using Lidarr.Plugin.Tidal.Download.Clients.Tidal.Utilities;

namespace NzbDrone.Core.Download.Clients.Tidal.Queue
{
    /// <summary>
    /// Manages rate limiting for downloads
    /// </summary>
    public class RateLimitManager : IDisposable
    {
        private readonly ITokenBucketRateLimiter _rateLimiter;
        private readonly Logger _logger;
        private readonly Random _random = new Random();
        private DateTime _lastRateLimitLogTime = DateTime.MinValue;
        private readonly TimeSpan _rateLimitLogInterval = TimeSpan.FromMinutes(1);
        private readonly string _serviceName;

        /// <summary>
        /// Initializes a new instance of the RateLimitManager class
        /// </summary>
        /// <param name="settings">Tidal settings containing rate limit configuration</param>
        /// <param name="serviceName">Name of the service for logging</param>
        /// <param name="logger">Logger for diagnostic messages</param>
        public RateLimitManager(TidalSettings settings, string serviceName, Logger logger)
        {
            _logger = logger;
            _serviceName = serviceName;
            _rateLimiter = TokenBucketRateLimiter.FromTidalSettings(settings, logger);

            _logger?.Debug($"[{_serviceName}] Rate limit manager initialized with {settings.MaxDownloadsPerHour} downloads/hour");
        }

        /// <summary>
        /// Determines if downloads should be throttled based on current rate limits
        /// </summary>
        /// <param name="downloadRatePerHour">Current download rate per hour</param>
        /// <param name="maxDownloadsPerHour">Maximum downloads per hour from settings</param>
        /// <returns>True if downloads should be throttled, false otherwise</returns>
        public bool ShouldThrottleDownload(int downloadRatePerHour, int maxDownloadsPerHour)
        {
            // If max downloads per hour is not set (0), don't throttle
            if (maxDownloadsPerHour <= 0)
            {
                return false;
            }

            // Check if we have enough tokens for a download
            if (!_rateLimiter.TryConsumeToken())
            {
                // Calculate time until next token is available
                var waitTime = _rateLimiter.GetEstimatedWaitTime();

                if ((DateTime.UtcNow - _lastRateLimitLogTime) > _rateLimitLogInterval)
                {
                    _logger?.Debug($"[{_serviceName}] Token bucket throttling active: next token in {waitTime.TotalSeconds:F1} seconds");
                    _lastRateLimitLogTime = DateTime.UtcNow;
                }
                return true;
            }

            // Add jitter - random delay based on proximity to rate limit
            int percentOfLimit = maxDownloadsPerHour > 0 ?
                (int)((downloadRatePerHour / (float)maxDownloadsPerHour) * 100) : 0;

            if (percentOfLimit > 50)
            {
                // As we approach the rate limit, increase the chance of adding artificial delay
                int jitterChance = percentOfLimit - 30; // 20% at 50%, 70% at 100%

                if (_random.Next(100) < jitterChance)
                {
                    // Add a small delay that increases as we approach the limit
                    int delayMs = _random.Next(500, 2000 + (percentOfLimit * 30));

                    if ((DateTime.UtcNow - _lastRateLimitLogTime) > _rateLimitLogInterval)
                    {
                        _logger?.Debug($"[{_serviceName}] Adding artificial delay of {delayMs}ms to spread out downloads (at {percentOfLimit}% of rate limit)");
                        _lastRateLimitLogTime = DateTime.UtcNow;
                    }

                    Thread.Sleep(delayMs);
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the estimated wait time until the next token is available
        /// </summary>
        /// <returns>TimeSpan representing the wait time</returns>
        public TimeSpan GetEstimatedWaitTime()
        {
            return _rateLimiter.GetEstimatedWaitTime();
        }

        /// <summary>
        /// Tries to consume a token without waiting
        /// </summary>
        /// <returns>True if a token was consumed, false otherwise</returns>
        public bool TryConsumeToken()
        {
            return _rateLimiter.TryConsumeToken();
        }

        /// <summary>
        /// Waits for a token to become available and then consumes it
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to cancel the wait operation</param>
        /// <returns>A task that completes when a token has been consumed</returns>
        public async Task WaitForTokenAsync(CancellationToken cancellationToken = default)
        {
            await _rateLimiter.WaitForTokenAsync(cancellationToken);
        }

        /// <summary>
        /// Updates the rate limiter settings
        /// </summary>
        /// <param name="maxDownloadsPerHour">New maximum downloads per hour</param>
        public void UpdateSettings(int maxDownloadsPerHour)
        {
            _rateLimiter.UpdateSettings(maxDownloadsPerHour);
            _logger?.InfoWithEmoji(LogEmojis.Settings, $"[{_serviceName}] Rate limit updated to {maxDownloadsPerHour} downloads/hour");
        }

        /// <summary>
        /// Disposes resources used by the rate limit manager
        /// </summary>
        public void Dispose()
        {
            try
            {
                // Dispose the rate limiter
                _rateLimiter?.Dispose();

                _logger?.Debug($"[{_serviceName}] Rate limit manager disposed");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"[{_serviceName}] Error disposing rate limit manager");
            }
        }
    }
}
