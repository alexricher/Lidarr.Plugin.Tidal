using System;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Tidal.Services.Logging;
using NLog;

namespace Lidarr.Plugin.Tidal.Services.Retry
{
    /// <summary>
    /// Represents a policy that can be used to retry operations.
    /// </summary>
    public class RetryPolicy
    {
        private readonly Logger _logger;
        private readonly int _maxRetries;
        private readonly TimeSpan _initialDelay;
        private readonly double _backoffFactor;
        private readonly Func<Exception, bool> _shouldRetry;
        private readonly bool _useJitter;

        /// <summary>
        /// Initializes a new instance of the RetryPolicy class.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="maxRetries">The maximum number of retries.</param>
        /// <param name="initialDelay">The initial delay between retries.</param>
        /// <param name="backoffFactor">The factor to multiply the delay by after each retry.</param>
        /// <param name="shouldRetry">A function that determines whether an exception should be retried.</param>
        /// <param name="useJitter">Whether to add jitter to the delay to prevent thundering herd problems.</param>
        public RetryPolicy(
            Logger logger,
            int maxRetries = 3,
            TimeSpan? initialDelay = null,
            double backoffFactor = 2.0,
            Func<Exception, bool> shouldRetry = null,
            bool useJitter = true)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _maxRetries = maxRetries;
            _initialDelay = initialDelay ?? TimeSpan.FromSeconds(1);
            _backoffFactor = backoffFactor;
            _shouldRetry = shouldRetry ?? DefaultShouldRetry;
            _useJitter = useJitter;
        }

        /// <summary>
        /// Executes an asynchronous function with retry logic.
        /// </summary>
        /// <typeparam name="T">The return type of the function.</typeparam>
        /// <param name="func">The function to execute.</param>
        /// <param name="operationName">The name of the operation for logging purposes.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>The result of the function.</returns>
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> func, string operationName = null, CancellationToken cancellationToken = default)
        {
            operationName = operationName ?? "operation";
            int attempt = 0;
            TimeSpan delay = _initialDelay;
            
            while (true)
            {
                attempt++;
                try
                {
                    if (attempt > 1)
                    {
                        _logger.InfoWithEmoji(LogEmojis.Retry, "Retry attempt {0}/{1} for {2}", attempt - 1, _maxRetries, operationName);
                    }
                    
                    return await func(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested && attempt <= _maxRetries && _shouldRetry(ex))
                {
                    _logger.WarnWithEmoji(LogEmojis.Retry, "Attempt {0}/{1} failed for {2}: {3}", attempt, _maxRetries + 1, operationName, ex.Message);
                    
                    if (attempt > _maxRetries)
                    {
                        _logger.ErrorWithEmoji(LogEmojis.Error, "All {0} retry attempts failed for {1}", _maxRetries, operationName);
                        throw;
                    }
                    
                    // Calculate the next delay with optional jitter
                    TimeSpan nextDelay = delay;
                    if (_useJitter)
                    {
                        // Add jitter of +/- 20%
                        var jitter = new Random().NextDouble() * 0.4 - 0.2; // -0.2 to 0.2
                        nextDelay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * (1 + jitter));
                    }
                    
                    _logger.DebugWithEmoji(LogEmojis.Wait, "Waiting {0:0.00} seconds before retry attempt {1} for {2}", 
                        nextDelay.TotalSeconds, attempt + 1, operationName);
                    
                    await Task.Delay(nextDelay, cancellationToken).ConfigureAwait(false);
                    
                    // Increase the delay for the next attempt
                    delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * _backoffFactor);
                }
            }
        }

        /// <summary>
        /// Executes an asynchronous action with retry logic.
        /// </summary>
        /// <param name="action">The action to execute.</param>
        /// <param name="operationName">The name of the operation for logging purposes.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        public async Task ExecuteAsync(Func<CancellationToken, Task> action, string operationName = null, CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(async token =>
            {
                await action(token).ConfigureAwait(false);
                return true;
            }, operationName, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// The default function to determine whether an exception should be retried.
        /// </summary>
        /// <param name="ex">The exception to check.</param>
        /// <returns>True if the exception should be retried, false otherwise.</returns>
        private static bool DefaultShouldRetry(Exception ex)
        {
            // Retry on transient exceptions
            return ex is TimeoutException ||
                   ex is System.Net.Http.HttpRequestException ||
                   ex is System.Net.WebException ||
                   ex is System.IO.IOException ||
                   (ex.Message?.Contains("timeout", StringComparison.OrdinalIgnoreCase) ?? false) ||
                   (ex.Message?.Contains("connection", StringComparison.OrdinalIgnoreCase) ?? false);
        }
    }
}
