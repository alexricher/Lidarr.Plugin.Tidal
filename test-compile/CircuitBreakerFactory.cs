using System;
using NLog;

namespace Lidarr.Plugin.Tidal.Services.CircuitBreaker
{
    /// <summary>
    /// Factory for creating circuit breakers.
    /// </summary>
    public static class CircuitBreakerFactory
    {
        /// <summary>
        /// Creates a new circuit breaker with the specified settings.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="name">The name of the circuit breaker.</param>
        /// <param name="breakDuration">The duration to keep the circuit breaker open after a trip.</param>
        /// <param name="failureThreshold">The number of failures required to trip the circuit breaker.</param>
        /// <returns>A new circuit breaker.</returns>
        public static ICircuitBreaker Create(Logger logger, string name, TimeSpan? breakDuration = null, int? failureThreshold = null)
        {
            var settings = new CircuitBreakerSettings
            {
                Name = name,
                BreakDuration = breakDuration ?? TimeSpan.FromMinutes(2),
                FailureThreshold = failureThreshold ?? 3
            };
            
            return new CircuitBreaker(logger, settings);
        }

        /// <summary>
        /// Creates a new circuit breaker with default settings.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="name">The name of the circuit breaker.</param>
        /// <returns>A new circuit breaker with default settings.</returns>
        public static ICircuitBreaker CreateDefault(Logger logger, string name)
        {
            return Create(logger, name);
        }

        /// <summary>
        /// Creates a new circuit breaker with sensitive settings (more likely to trip).
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="name">The name of the circuit breaker.</param>
        /// <returns>A new circuit breaker with sensitive settings.</returns>
        public static ICircuitBreaker CreateSensitive(Logger logger, string name)
        {
            return Create(logger, name, TimeSpan.FromMinutes(5), 2);
        }

        /// <summary>
        /// Creates a new circuit breaker with resilient settings (less likely to trip).
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="name">The name of the circuit breaker.</param>
        /// <returns>A new circuit breaker with resilient settings.</returns>
        public static ICircuitBreaker CreateResilient(Logger logger, string name)
        {
            return Create(logger, name, TimeSpan.FromMinutes(1), 5);
        }
    }
}
