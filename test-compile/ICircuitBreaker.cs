using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lidarr.Plugin.Tidal.Services.CircuitBreaker
{
    // CircuitBreakerState enum is now defined in CircuitBreaker.cs to avoid duplication
    
    /// <summary>
    /// Defines the interface for a generic circuit breaker pattern implementation.
    /// This interface provides methods to control and monitor the state of a circuit breaker
    /// used to prevent cascading failures in the system.
    /// </summary>
    public interface ICircuitBreaker
    {
        /// <summary>
        /// Gets the name of this circuit breaker instance.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the current state of the circuit breaker.
        /// </summary>
        CircuitBreakerState State { get; }

        /// <summary>
        /// Gets the number of consecutive successful operations since the last failure.
        /// </summary>
        int ConsecutiveSuccesses { get; }

        /// <summary>
        /// Gets the number of recent failures within the failure time window.
        /// </summary>
        int RecentFailures { get; }

        /// <summary>
        /// Gets the current severity level of the circuit breaker.
        /// </summary>
        /// <returns>The current severity level</returns>
        CircuitBreakerSeverity GetSeverity();

        /// <summary>
        /// Checks if the circuit breaker is currently open (preventing operations).
        /// </summary>
        /// <returns>True if the circuit breaker is open, false otherwise.</returns>
        bool IsOpen();
        
        /// <summary>
        /// Checks if the circuit breaker is open with at least the specified severity level.
        /// </summary>
        /// <param name="minimumSeverity">The minimum severity level to check for.</param>
        /// <returns>True if the circuit is open with at least the specified severity, false otherwise.</returns>
        bool IsOpenWithSeverity(CircuitBreakerSeverity minimumSeverity);
        
        /// <summary>
        /// Gets the current severity level and state of the circuit breaker.
        /// </summary>
        /// <returns>A tuple containing the current state and severity level.</returns>
        (CircuitBreakerState State, CircuitBreakerSeverity Severity) GetStateAndSeverity();

        /// <summary>
        /// Trips the circuit breaker, preventing further operations until reset.
        /// </summary>
        /// <param name="reason">The reason for tripping the circuit breaker.</param>
        void Trip(string reason);

        /// <summary>
        /// Resets the circuit breaker to its closed state, allowing operations to resume.
        /// </summary>
        void Reset();

        /// <summary>
        /// Updates the circuit breaker settings.
        /// </summary>
        /// <param name="settings">The new settings to apply.</param>
        void UpdateSettings(CircuitBreakerSettings settings);

        /// <summary>
        /// Gets the time remaining before the circuit breaker will automatically reset.
        /// </summary>
        /// <returns>A TimeSpan representing the time until reset.</returns>
        TimeSpan GetReopenTime();

        /// <summary>
        /// Records a successful operation, which can be used for circuit breaker state management.
        /// </summary>
        void RecordSuccess();

        /// <summary>
        /// Records a failed operation, which can be used for circuit breaker state management.
        /// </summary>
        /// <param name="exception">The exception that caused the failure.</param>
        /// <returns>True if the circuit breaker was tripped as a result of this failure.</returns>
        bool RecordFailure(Exception exception);

        /// <summary>
        /// Executes an action with circuit breaker protection.
        /// </summary>
        /// <typeparam name="T">The return type of the action.</typeparam>
        /// <param name="action">The action to execute.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>The result of the action.</returns>
        Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes an action with circuit breaker protection.
        /// </summary>
        /// <param name="action">The action to execute.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes an action with circuit breaker protection and a specific operation name for logging.
        /// </summary>
        /// <typeparam name="T">The return type of the action.</typeparam>
        /// <param name="operationName">The name of the operation for logging purposes.</param>
        /// <param name="action">The action to execute.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>The result of the action.</returns>
        Task<T> ExecuteAsync<T>(string operationName, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes an action with circuit breaker protection and a specific operation name for logging.
        /// </summary>
        /// <param name="operationName">The name of the operation for logging purposes.</param>
        /// <param name="action">The action to execute.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task ExecuteAsync(string operationName, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets statistics about the circuit breaker's operation.
        /// </summary>
        /// <returns>A CircuitBreakerStats object containing statistics about the circuit breaker.</returns>
        CircuitBreakerStats GetStats();
    }
}
