namespace Lidarr.Plugin.Tidal.Services.CircuitBreaker
{
    /// <summary>
    /// Represents the possible states of a circuit breaker.
    /// </summary>
    public enum CircuitBreakerState
    {
        /// <summary>
        /// The circuit breaker is closed and allowing operations to proceed normally.
        /// </summary>
        Closed,

        /// <summary>
        /// The circuit breaker is open and preventing operations from proceeding.
        /// </summary>
        Open,

        /// <summary>
        /// The circuit breaker is in a half-open state, allowing a limited number of operations to test if the system has recovered.
        /// </summary>
        HalfOpen
    }
}
