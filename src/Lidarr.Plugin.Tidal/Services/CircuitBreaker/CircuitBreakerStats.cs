using System;

namespace Lidarr.Plugin.Tidal.Services.CircuitBreaker
{
    /// <summary>
    /// Contains statistics about a circuit breaker's operation.
    /// </summary>
    public class CircuitBreakerStats
    {
        /// <summary>
        /// Gets or sets the name of the circuit breaker.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the current state of the circuit breaker.
        /// </summary>
        public CircuitBreakerState State { get; set; }

        /// <summary>
        /// Gets or sets the time when the circuit breaker was last tripped.
        /// </summary>
        public DateTime? LastTrippedTime { get; set; }

        /// <summary>
        /// Gets or sets the time when the circuit breaker will automatically reset.
        /// </summary>
        public DateTime? ResetTime { get; set; }

        /// <summary>
        /// Gets or sets the number of consecutive successful operations since the last failure.
        /// </summary>
        public int ConsecutiveSuccesses { get; set; }

        /// <summary>
        /// Gets or sets the number of recent failures within the failure time window.
        /// </summary>
        public int RecentFailures { get; set; }

        /// <summary>
        /// Gets or sets the total number of operations that have been executed through this circuit breaker.
        /// </summary>
        public long TotalOperations { get; set; }

        /// <summary>
        /// Gets or sets the total number of successful operations.
        /// </summary>
        public long SuccessfulOperations { get; set; }

        /// <summary>
        /// Gets or sets the total number of failed operations.
        /// </summary>
        public long FailedOperations { get; set; }

        /// <summary>
        /// Gets or sets the total number of times the circuit breaker has been tripped.
        /// </summary>
        public long TripCount { get; set; }
    }
}
