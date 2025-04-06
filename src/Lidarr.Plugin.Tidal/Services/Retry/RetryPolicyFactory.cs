using System;
using NLog;

namespace Lidarr.Plugin.Tidal.Services.Retry
{
    /// <summary>
    /// Factory for creating retry policies.
    /// </summary>
    public static class RetryPolicyFactory
    {
        /// <summary>
        /// Creates a default retry policy.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <returns>A default retry policy.</returns>
        public static RetryPolicy CreateDefault(Logger logger)
        {
            return new RetryPolicy(
                logger,
                maxRetries: 3,
                initialDelay: TimeSpan.FromSeconds(1),
                backoffFactor: 2.0,
                useJitter: true);
        }

        /// <summary>
        /// Creates an aggressive retry policy with more retries and shorter delays.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <returns>An aggressive retry policy.</returns>
        public static RetryPolicy CreateAggressive(Logger logger)
        {
            return new RetryPolicy(
                logger,
                maxRetries: 5,
                initialDelay: TimeSpan.FromMilliseconds(500),
                backoffFactor: 1.5,
                useJitter: true);
        }

        /// <summary>
        /// Creates a conservative retry policy with fewer retries and longer delays.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <returns>A conservative retry policy.</returns>
        public static RetryPolicy CreateConservative(Logger logger)
        {
            return new RetryPolicy(
                logger,
                maxRetries: 2,
                initialDelay: TimeSpan.FromSeconds(2),
                backoffFactor: 3.0,
                useJitter: true);
        }

        /// <summary>
        /// Creates a custom retry policy.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="maxRetries">The maximum number of retries.</param>
        /// <param name="initialDelay">The initial delay between retries.</param>
        /// <param name="backoffFactor">The factor to multiply the delay by after each retry.</param>
        /// <param name="shouldRetry">A function that determines whether an exception should be retried.</param>
        /// <param name="useJitter">Whether to add jitter to the delay to prevent thundering herd problems.</param>
        /// <returns>A custom retry policy.</returns>
        public static RetryPolicy Create(
            Logger logger,
            int maxRetries,
            TimeSpan initialDelay,
            double backoffFactor = 2.0,
            Func<Exception, bool> shouldRetry = null,
            bool useJitter = true)
        {
            return new RetryPolicy(
                logger,
                maxRetries,
                initialDelay,
                backoffFactor,
                shouldRetry,
                useJitter);
        }
    }
}
