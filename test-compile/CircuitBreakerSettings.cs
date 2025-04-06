using System;
using NzbDrone.Plugin.Tidal.Constants;

namespace Lidarr.Plugin.Tidal.Services.CircuitBreaker
{
    /// <summary>
    /// Settings for a circuit breaker.
    /// </summary>
    public class CircuitBreakerSettings
    {
        /// <summary>
        /// Gets or sets the name of the circuit breaker.
        /// </summary>
        public string Name { get; set; } = "Default";
        
        /// <summary>
        /// Gets or sets the failure threshold before the circuit breaker trips.
        /// </summary>
        public int FailureThreshold { get; set; }
        
        /// <summary>
        /// Gets or sets the duration to keep the circuit breaker open after a trip.
        /// </summary>
        public TimeSpan BreakDuration { get; set; }
        
        /// <summary>
        /// Gets or sets the time window to consider for failures.
        /// </summary>
        public TimeSpan FailureTimeWindow { get; set; }
        
        /// <summary>
        /// Gets or sets the interval between status update log messages.
        /// </summary>
        public TimeSpan StatusUpdateInterval { get; set; }
        
        /// <summary>
        /// Gets the default settings.
        /// </summary>
        public static CircuitBreakerSettings Default => new CircuitBreakerSettings
        {
            Name = "Default",
            FailureThreshold = TidalConstants.DefaultCircuitBreakerFailureThreshold,
            BreakDuration = TidalConstants.DefaultCircuitBreakerBreakDuration,
            FailureTimeWindow = TidalConstants.DefaultCircuitBreakerFailureTimeWindow,
            StatusUpdateInterval = TidalConstants.DefaultCircuitBreakerStatusUpdateInterval
        };
        
        /// <summary>
        /// Initializes a new instance of the <see cref="CircuitBreakerSettings"/> class.
        /// </summary>
        public CircuitBreakerSettings()
        {
            // Initialize with default values from TidalConstants
            FailureThreshold = TidalConstants.DefaultCircuitBreakerFailureThreshold;
            BreakDuration = TidalConstants.DefaultCircuitBreakerBreakDuration;
            FailureTimeWindow = TidalConstants.DefaultCircuitBreakerFailureTimeWindow;
            StatusUpdateInterval = TidalConstants.DefaultCircuitBreakerStatusUpdateInterval;
        }

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
