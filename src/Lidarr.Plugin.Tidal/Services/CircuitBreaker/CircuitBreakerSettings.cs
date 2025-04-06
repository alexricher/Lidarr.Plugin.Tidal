using System;

namespace Lidarr.Plugin.Tidal.Services.CircuitBreaker
{
    /// <summary>
    /// Settings for configuring a circuit breaker.
    /// </summary>
    public class CircuitBreakerSettings
    {
        /// <summary>
        /// Gets or sets the duration to keep the circuit breaker open after a trip.
        /// </summary>
        public TimeSpan BreakDuration { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Gets or sets the interval between status update log messages.
        /// </summary>
        public TimeSpan StatusUpdateInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets the maximum number of consecutive failures before tripping the circuit breaker.
        /// </summary>
        public int FailureThreshold { get; set; } = 3;

        /// <summary>
        /// Gets or sets the time window for counting failures.
        /// </summary>
        public TimeSpan FailureTimeWindow { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Gets or sets the name of the circuit breaker for logging purposes.
        /// </summary>
        public string Name { get; set; } = "Default";

        /// <summary>
        /// Creates a new instance of the CircuitBreakerSettings class with default values.
        /// </summary>
        public static CircuitBreakerSettings Default => new CircuitBreakerSettings();

        /// <summary>
        /// Creates a new instance of the CircuitBreakerSettings class with values for a sensitive service.
        /// </summary>
        public static CircuitBreakerSettings Sensitive => new CircuitBreakerSettings
        {
            BreakDuration = TimeSpan.FromMinutes(5),
            FailureThreshold = 2,
            FailureTimeWindow = TimeSpan.FromMinutes(10)
        };

        /// <summary>
        /// Creates a new instance of the CircuitBreakerSettings class with values for a resilient service.
        /// </summary>
        public static CircuitBreakerSettings Resilient => new CircuitBreakerSettings
        {
            BreakDuration = TimeSpan.FromMinutes(1),
            FailureThreshold = 5,
            FailureTimeWindow = TimeSpan.FromMinutes(3)
        };
    }
}
