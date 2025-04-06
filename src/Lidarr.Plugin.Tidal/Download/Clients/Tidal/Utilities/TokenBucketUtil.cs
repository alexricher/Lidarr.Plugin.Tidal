using System;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Tidal.Services.Logging;
using NLog;
using NzbDrone.Core.Download.Clients.Tidal;
using NzbDrone.Core.Indexers.Tidal;

namespace Lidarr.Plugin.Tidal.Download.Clients.Tidal.Utilities
{
    /// <summary>
    /// Specifies the level to reset the token bucket to.
    /// </summary>
    public enum TokenBucketLevel
    {
        /// <summary>
        /// Reset to empty (0 tokens).
        /// </summary>
        Empty,
        
        /// <summary>
        /// Reset to half capacity.
        /// </summary>
        Half,
        
        /// <summary>
        /// Reset to full capacity.
        /// </summary>
        Full
    }

    /// <summary>
    /// Interface for token bucket rate limiting
    /// </summary>
    public interface ITokenBucketRateLimiter : IDisposable
    {
        /// <summary>
        /// Waits until a token is available and then consumes it.
        /// </summary>
        /// <param name="token">Cancellation token to cancel the wait operation.</param>
        /// <returns>A task that completes when a token has been consumed.</returns>
        Task WaitForTokenAsync(CancellationToken token = default);

        /// <summary>
        /// Tries to consume a token without waiting.
        /// </summary>
        /// <returns>True if a token was consumed, false otherwise.</returns>
        bool TryConsumeToken();

        /// <summary>
        /// Gets the estimated wait time until the next token will be available.
        /// </summary>
        /// <returns>The estimated wait time.</returns>
        TimeSpan GetEstimatedWaitTime();

        /// <summary>
        /// Gets the current number of tokens in the bucket.
        /// </summary>
        /// <returns>The current token count.</returns>
        double GetCurrentTokenCount();

        /// <summary>
        /// Gets the maximum number of tokens the bucket can hold.
        /// </summary>
        /// <returns>The maximum token count.</returns>
        double GetMaxTokenCount();

        /// <summary>
        /// Gets the current rate limit in operations per hour.
        /// </summary>
        /// <returns>The current rate limit.</returns>
        int GetCurrentRateLimit();

        /// <summary>
        /// Updates the rate limit settings.
        /// </summary>
        /// <param name="maxOperationsPerHour">The new maximum operations per hour.</param>
        void UpdateSettings(int maxOperationsPerHour);
    }

    /// <summary>
    /// Implements a token bucket algorithm for rate limiting.
    /// This class provides a thread-safe way to limit the rate of operations
    /// such as API calls or downloads.
    /// </summary>
    public class TokenBucketRateLimiter : ITokenBucketRateLimiter
    {
        /// <summary>
        /// Default downloads per hour if not specified
        /// </summary>
        public const int DEFAULT_DOWNLOADS_PER_HOUR = 10;

        /// <summary>
        /// Maximum allowed downloads per hour - can be overridden by settings but values above
        /// this limit may increase the risk of rate limiting or detection by Tidal's systems
        /// </summary>
        public const int MAX_DOWNLOADS_PER_HOUR = 200;

        /// <summary>
        /// Maximum allowed requests per hour for indexer operations
        /// </summary>
        public const int MAX_INDEXER_REQUESTS_PER_HOUR = 500;

        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly Logger _logger;
        private double _tokenBucket;
        private double _maxTokens;
        private DateTime _lastTokenUpdate = DateTime.MinValue;
        private int _maxOperationsPerHour;
        private bool _disposed;
        private bool _enableThrottling = true;

        /// <summary>
        /// Initializes a new instance of the TokenBucketRateLimiter class.
        /// </summary>
        /// <param name="maxOperationsPerHour">The maximum number of operations per hour.</param>
        /// <param name="logger">The logger to use for logging.</param>
        /// <param name="startWithFullBucket">Whether to start with a full token bucket.</param>
        public TokenBucketRateLimiter(int maxOperationsPerHour, Logger logger, bool startWithFullBucket = false)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _maxOperationsPerHour = ValidateRateLimit(maxOperationsPerHour);
            _maxTokens = _maxOperationsPerHour;

            // Start with either a full bucket or half full bucket
            _tokenBucket = startWithFullBucket ? _maxTokens : _maxTokens / 2.0;
            _lastTokenUpdate = DateTime.UtcNow;

            _logger.DebugWithEmoji(LogEmojis.Debug, $"Token bucket initialized: {_tokenBucket:F2} tokens, max: {_maxTokens:F2}, rate: {_maxOperationsPerHour}/hour");
        }

        /// <summary>
        /// Creates a new TokenBucketRateLimiter from TidalSettings.
        /// </summary>
        /// <param name="settings">The Tidal settings to use.</param>
        /// <param name="logger">The logger to use for logging.</param>
        /// <returns>A new TokenBucketRateLimiter instance.</returns>
        public static TokenBucketRateLimiter FromTidalSettings(TidalSettings settings, Logger logger)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            return new TokenBucketRateLimiter(settings.MaxDownloadsPerHour, logger);
        }

        /// <summary>
        /// Creates a new TokenBucketRateLimiter from TidalIndexerSettings.
        /// </summary>
        /// <param name="settings">The Tidal indexer settings to use.</param>
        /// <param name="logger">The logger to use for logging.</param>
        /// <returns>A new TokenBucketRateLimiter instance.</returns>
        public static TokenBucketRateLimiter FromTidalIndexerSettings(TidalIndexerSettings settings, Logger logger)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            // Convert requests per minute to requests per hour
            int requestsPerHour = settings.MaxRequestsPerMinute * 60;
            return new TokenBucketRateLimiter(requestsPerHour, logger);
        }

        /// <summary>
        /// Validates and normalizes the rate limit.
        /// </summary>
        /// <param name="rateLimit">The rate limit to validate.</param>
        /// <returns>The validated rate limit.</returns>
        private int ValidateRateLimit(int rateLimit)
        {
            // If rate limiting is disabled (0 or negative), use a high value
            if (rateLimit <= 0)
            {
                _logger?.DebugWithEmoji(LogEmojis.Debug, $"Rate limiting disabled (value: {rateLimit}), using unlimited rate");
                return int.MaxValue;
            }

            // If rate limit exceeds maximum allowed, cap it
            if (rateLimit > MAX_DOWNLOADS_PER_HOUR)
            {
                _logger?.WarnWithEmoji(LogEmojis.Warning, $"Rate limit ({rateLimit}) exceeds maximum recommended ({MAX_DOWNLOADS_PER_HOUR}), capping to maximum");
                return MAX_DOWNLOADS_PER_HOUR;
            }

            return rateLimit;
        }

        /// <inheritdoc/>
        public async Task WaitForTokenAsync(CancellationToken token = default)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(TokenBucketRateLimiter));
            }

            // If rate limiting is effectively disabled, return immediately
            if (_maxOperationsPerHour == int.MaxValue)
            {
                return;
            }

            try
            {
                await _semaphore.WaitAsync(token).ConfigureAwait(false);

                // Refill tokens based on elapsed time
                RefillTokens();

                // Wait until we have at least one token
                while (_tokenBucket < 1.0 && !token.IsCancellationRequested)
                {
                    // Calculate wait time until next token
                    double tokensPerSecond = _maxOperationsPerHour / 3600.0;
                    double tokensNeeded = 1.0 - _tokenBucket;
                    TimeSpan waitTime = TimeSpan.FromSeconds(tokensNeeded / tokensPerSecond);

                    // Add a small buffer to avoid busy waiting
                    waitTime = waitTime.Add(TimeSpan.FromMilliseconds(50));

                    _logger.DebugWithEmoji(LogEmojis.Wait, $"Waiting {waitTime.TotalSeconds:F2}s for token (current: {_tokenBucket:F2})");

                    // Release the semaphore while waiting to allow other threads to check
                    _semaphore.Release();

                    try
                    {
                        await Task.Delay(waitTime, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Propagate cancellation
                        throw;
                    }

                    // Reacquire the semaphore
                    await _semaphore.WaitAsync(token).ConfigureAwait(false);

                    // Refill tokens again after waiting
                    RefillTokens();
                }

                // Consume a token
                token.ThrowIfCancellationRequested();
                _tokenBucket -= 1.0;
                _logger.DebugWithEmoji(LogEmojis.Debug, $"Token consumed, {_tokenBucket:F2} remaining");
            }
            finally
            {
                if (!_disposed)
                {
                    _semaphore.Release();
                }
            }
        }

        /// <inheritdoc/>
        public bool TryConsumeToken()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(TokenBucketRateLimiter));
            }

            // If rate limiting is effectively disabled, always succeed
            if (_maxOperationsPerHour == int.MaxValue)
            {
                return true;
            }

            try
            {
                _semaphore.Wait();

                // Refill tokens based on elapsed time
                RefillTokens();

                // Check if we have enough tokens
                if (_tokenBucket < 1.0)
                {
                    _logger?.DebugWithEmoji(LogEmojis.Wait, $"No tokens available ({_tokenBucket:F2}/{_maxTokens:F2})");
                    return false;
                }

                // Consume a token
                _tokenBucket -= 1.0;
                _logger?.DebugWithEmoji(LogEmojis.Debug, $"Consumed token, {_tokenBucket:F2}/{_maxTokens:F2} tokens remaining");
                return true;
            }
            finally
            {
                if (!_disposed)
                {
                    _semaphore.Release();
                }
            }
        }

        /// <inheritdoc/>
        public TimeSpan GetEstimatedWaitTime()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(TokenBucketRateLimiter));
            }

            // If rate limiting is effectively disabled, return zero wait time
            if (_maxOperationsPerHour == int.MaxValue)
            {
                return TimeSpan.Zero;
            }

            try
            {
                _semaphore.Wait();

                // Refill tokens based on elapsed time
                RefillTokens();

                // If we have enough tokens, no wait time
                if (_tokenBucket >= 1.0)
                {
                    return TimeSpan.Zero;
                }

                // Calculate wait time until next token
                double tokensPerSecond = _maxOperationsPerHour / 3600.0;
                double tokensNeeded = 1.0 - _tokenBucket;
                return TimeSpan.FromSeconds(tokensNeeded / tokensPerSecond);
            }
            finally
            {
                if (!_disposed)
                {
                    _semaphore.Release();
                }
            }
        }

        /// <inheritdoc/>
        public double GetCurrentTokenCount()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(TokenBucketRateLimiter));
            }

            try
            {
                _semaphore.Wait();
                RefillTokens();
                return _tokenBucket;
            }
            finally
            {
                if (!_disposed)
                {
                    _semaphore.Release();
                }
            }
        }

        /// <inheritdoc/>
        public double GetMaxTokenCount()
        {
            return _maxTokens;
        }

        /// <inheritdoc/>
        public int GetCurrentRateLimit()
        {
            return _maxOperationsPerHour == int.MaxValue ? 0 : _maxOperationsPerHour; // 0 indicates unlimited
        }

        /// <inheritdoc/>
        public void UpdateSettings(int maxOperationsPerHour)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(TokenBucketRateLimiter));
            }

            try
            {
                _semaphore.Wait();

                int oldLimit = _maxOperationsPerHour;
                _maxOperationsPerHour = ValidateRateLimit(maxOperationsPerHour);
                _maxTokens = _maxOperationsPerHour == int.MaxValue ? double.MaxValue : _maxOperationsPerHour;

                // Scale the current tokens proportionally to the new limit
                if (oldLimit != int.MaxValue && _maxOperationsPerHour != int.MaxValue)
                {
                    double ratio = (double)_maxOperationsPerHour / oldLimit;
                    _tokenBucket = Math.Min(_maxTokens, _tokenBucket * ratio);
                }
                else if (_maxOperationsPerHour == int.MaxValue)
                {
                    // If switching to unlimited, fill the bucket
                    _tokenBucket = _maxTokens;
                }
                else if (oldLimit == int.MaxValue)
                {
                    // If switching from unlimited, start with half full bucket
                    _tokenBucket = _maxTokens / 2.0;
                }

                _logger.InfoWithEmoji(LogEmojis.Settings, $"Rate limit updated: {oldLimit}/hour -> {_maxOperationsPerHour}/hour, current tokens: {_tokenBucket:F2}");
            }
            finally
            {
                if (!_disposed)
                {
                    _semaphore.Release();
                }
            }
        }

        /// <summary>
        /// Refills the token bucket based on elapsed time.
        /// </summary>
        private void RefillTokens()
        {
            DateTime now = DateTime.UtcNow;
            double elapsedHours = (now - _lastTokenUpdate).TotalHours;
            
            if (elapsedHours > 0)
            {
                // Calculate tokens to add based on rate
                double tokensToAdd = elapsedHours * _maxOperationsPerHour;
                
                // Add tokens up to max capacity
                _tokenBucket = Math.Min(_maxTokens, _tokenBucket + tokensToAdd);
                
                // Update timestamp
                _lastTokenUpdate = now;
                
                _logger?.DebugWithEmoji(LogEmojis.Debug, $"Refilled tokens: added {tokensToAdd:F2}, now have {_tokenBucket:F2}/{_maxTokens:F2}");
            }
        }

        /// <summary>
        /// Calculates tokens per second from max downloads per hour
        /// </summary>
        public static double GetTokensPerSecond(int maxDownloadsPerHour)
        {
            // Ensure a valid rate
            if (maxDownloadsPerHour <= 0)
                return DEFAULT_DOWNLOADS_PER_HOUR / 3600.0;

            if (maxDownloadsPerHour > MAX_DOWNLOADS_PER_HOUR)
                return MAX_DOWNLOADS_PER_HOUR / 3600.0;

            return maxDownloadsPerHour / 3600.0;
        }

        /// <summary>
        /// Calculates tokens per second from max downloads per hour (double version)
        /// </summary>
        public static double GetTokensPerSecond(double maxDownloadsPerHour)
        {
            return GetTokensPerSecond((int)maxDownloadsPerHour);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _semaphore.Dispose();
            _logger?.DebugWithEmoji(LogEmojis.Debug, $"Token bucket disposed");
        }

        /// <summary>
        /// Gets or sets whether throttling is enabled.
        /// </summary>
        public bool EnableThrottling
        {
            get => _enableThrottling;
            set
            {
                if (_enableThrottling != value)
                {
                    _enableThrottling = value;
                    _logger?.InfoWithEmoji(_enableThrottling ? LogEmojis.Resume : LogEmojis.Pause, 
                        $"Rate limiting {(_enableThrottling ? "enabled" : "disabled")}");
                }
            }
        }

        /// <summary>
        /// Resets the token bucket to the specified level.
        /// </summary>
        /// <param name="level">The level to reset to (default is full).</param>
        public void ResetBucket(TokenBucketLevel level = TokenBucketLevel.Full)
        {
            try
            {
                _semaphore.Wait();
                
                switch (level)
                {
                    case TokenBucketLevel.Empty:
                        _tokenBucket = 0;
                        _logger?.DebugWithEmoji(LogEmojis.Debug, $"Token bucket reset to empty");
                        break;
                    case TokenBucketLevel.Half:
                        _tokenBucket = _maxTokens / 2.0;
                        _logger?.DebugWithEmoji(LogEmojis.Debug, $"Token bucket reset to half ({_tokenBucket:F2} tokens)");
                        break;
                    case TokenBucketLevel.Full:
                    default:
                        _tokenBucket = _maxTokens;
                        _logger?.DebugWithEmoji(LogEmojis.Debug, $"Token bucket reset to full ({_tokenBucket:F2} tokens)");
                        break;
                }
                
                _lastTokenUpdate = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger?.ErrorWithEmoji(LogEmojis.Error, ex, $"Error resetting token bucket");
                throw;
            }
            finally
            {
                if (!_disposed)
                {
                    _semaphore.Release();
                }
            }
        }

        private void LogStatistics()
        {
            try
            {
                _logger?.DebugWithEmoji(LogEmojis.Stats, $"Token bucket stats: {_tokenBucket:F2}/{_maxTokens:F2} tokens, rate: {_maxOperationsPerHour}/hour");
                
                if (_tokenBucket < 1.0)
                {
                    double tokensPerSecond = _maxOperationsPerHour / 3600.0;
                    double tokensNeeded = 1.0 - _tokenBucket;
                    TimeSpan waitTime = TimeSpan.FromSeconds(tokensNeeded / tokensPerSecond);
                    
                    _logger?.WarnWithEmoji(LogEmojis.Wait, $"Token bucket low, next token available in {waitTime.TotalSeconds:F1} seconds");
                }
            }
            catch (Exception ex)
            {
                _logger?.ErrorWithEmoji(LogEmojis.Error, ex, $"Error logging token bucket statistics");
            }
        }

        #region Legacy Static Methods for Backward Compatibility

        /// <summary>
        /// Initializes token bucket values for rate limiting (legacy method for backward compatibility)
        /// </summary>
        /// <param name="settings">Tidal settings containing rate limit values</param>
        /// <param name="logger">Logger for diagnostic messages</param>
        /// <param name="tokenBucket">Reference to the token bucket value to initialize</param>
        /// <param name="lastUpdate">Reference to the last update timestamp</param>
        public static void InitializeTokenBucket(
            TidalSettings settings,
            Logger logger,
            ref double tokenBucket,
            ref DateTime lastTokenUpdate)
        {
            try
            {
                // Get rate limit from settings
                var tokensPerHour = settings?.MaxDownloadsPerHour ?? DEFAULT_DOWNLOADS_PER_HOUR;
                logger?.Info($"Initializing token bucket with {tokensPerHour} downloads per hour");

                // Ensure we have a sane value for tokens per hour
                if (tokensPerHour <= 0)
                {
                    logger?.Warn("Invalid downloads per hour setting (<=0), using default value");
                    tokensPerHour = DEFAULT_DOWNLOADS_PER_HOUR;
                }
                else if (tokensPerHour > MAX_DOWNLOADS_PER_HOUR)
                {
                    logger?.Warn($"Downloads per hour ({tokensPerHour}) exceeds recommended maximum ({MAX_DOWNLOADS_PER_HOUR}). " +
                                $"Higher values may increase the risk of rate limiting.");
                }

                // Start with half-full token bucket
                tokenBucket = tokensPerHour / 2.0;

                // Reset the last update timestamp
                lastTokenUpdate = DateTime.UtcNow;

                logger?.DebugWithEmoji(LogEmojis.Debug, $"Token bucket initialized: {tokenBucket:F2} tokens, refill rate: {tokensPerHour}/hour");
            }
            catch (Exception ex)
            {
                logger?.ErrorWithEmoji(LogEmojis.Error, ex, "Error initializing token bucket: {0}", ex.Message);

                // Fallback to conservative defaults if initialization fails
                tokenBucket = 2.0; // Start with 2 tokens
                lastTokenUpdate = DateTime.UtcNow;

                logger?.WarnWithEmoji(LogEmojis.Warning, "Using conservative fallback token bucket due to initialization error");
            }
        }

        /// <summary>
        /// Helper method to initialize token bucket with proper logger type handling (legacy method for backward compatibility)
        /// </summary>
        public static void SafeInitializeTokenBucket(
            object settings,
            Logger logger,
            ref double tokenBucket,
            ref DateTime lastTokenUpdate)
        {
            try
            {
                lastTokenUpdate = DateTime.UtcNow;

                if (settings is TidalSettings tidalSettings)
                {
                    InitializeTokenBucket(tidalSettings, logger, ref tokenBucket, ref lastTokenUpdate);
                }
                else if (settings is TidalIndexerSettings indexerSettings)
                {
                    tokenBucket = indexerSettings.MaxRequestsPerMinute * 60; // Convert to hourly rate
                    logger?.DebugWithEmoji(LogEmojis.Debug, $"Initialized search token bucket with {tokenBucket} tokens (from {indexerSettings.MaxRequestsPerMinute} requests/minute)");
                }
                else if (settings is Logger loggerOnly)
                {
                    // For backward compatibility with old method signature
                    double tempBucket = 0;
                    DateTime tempTime = DateTime.MinValue;
                    InitializeTokenBucket(null, logger, ref tempBucket, ref tempTime);
                    tokenBucket = tempBucket;
                    lastTokenUpdate = tempTime;
                }
                else
                {
                    tokenBucket = DEFAULT_DOWNLOADS_PER_HOUR;
                    logger?.WarnWithEmoji(LogEmojis.Warning, $"Unknown settings type {settings?.GetType()?.Name ?? "null"}, using default token bucket value of {tokenBucket}");
                }
            }
            catch (Exception ex)
            {
                logger?.ErrorWithEmoji(LogEmojis.Error, ex, "Error initializing token bucket");
                tokenBucket = 50; // Conservative fallback value
                lastTokenUpdate = DateTime.UtcNow;
            }
        }

        #endregion
    }
}