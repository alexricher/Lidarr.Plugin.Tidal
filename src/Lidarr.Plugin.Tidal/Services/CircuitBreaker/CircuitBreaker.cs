using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Tidal.Services.Logging;
using NLog;

namespace Lidarr.Plugin.Tidal.Services.CircuitBreaker
{
    /// <summary>
    /// Implements a generic circuit breaker pattern.
    /// This class helps prevent cascading failures by temporarily stopping operations
    /// when too many failures occur.
    /// </summary>
    public class CircuitBreaker : ICircuitBreaker
    {
        private readonly Logger _logger;
        private readonly CircuitBreakerSettings _settings;
        private readonly object _circuitLock = new();
        private bool _isOpen;
        private DateTime _resetTime = DateTime.MinValue;
        private DateTime _lastStatusUpdate = DateTime.MinValue;
        private readonly Queue<DateTime> _recentFailures = new();
        private int _consecutiveSuccesses;
        private DateTime? _lastTrippedTime;
        private long _totalOperations;
        private long _successfulOperations;
        private long _failedOperations;
        private long _tripCount;

        /// <inheritdoc/>
        public string Name => _settings.Name;

        /// <inheritdoc/>
        public CircuitBreakerState State
        {
            get
            {
                if (!_isOpen)
                {
                    return CircuitBreakerState.Closed;
                }

                // If we have some consecutive successes but the circuit is still open,
                // we're in a half-open state
                if (_consecutiveSuccesses > 0)
                {
                    return CircuitBreakerState.HalfOpen;
                }

                return CircuitBreakerState.Open;
            }
        }

        /// <inheritdoc/>
        public int ConsecutiveSuccesses => _consecutiveSuccesses;

        /// <inheritdoc/>
        public int RecentFailures
        {
            get
            {
                lock (_circuitLock)
                {
                    return _recentFailures.Count;
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the CircuitBreaker class.
        /// </summary>
        /// <param name="logger">The logger instance to use for logging.</param>
        /// <param name="settings">The settings for the circuit breaker.</param>
        public CircuitBreaker(Logger logger, CircuitBreakerSettings settings = null)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _settings = settings ?? CircuitBreakerSettings.Default;
        }

        /// <inheritdoc/>
        public bool IsOpen()
        {
            var shouldLogReset = false;
            var shouldLogStatus = false;
            var resetTime = DateTime.MinValue;
            bool isCircuitOpen;

            // Use a shorter lock duration to reduce contention
            lock (_circuitLock)
            {
                if (!_isOpen)
                {
                    return false;
                }

                // Check if it's time to reset
                var now = DateTime.UtcNow;
                if (now >= _resetTime)
                {
                    _isOpen = false;
                    shouldLogReset = true;
                    isCircuitOpen = false;
                }
                else
                {
                    // Circuit is still open
                    isCircuitOpen = true;

                    // Check if it's time to log a status update
                    if ((now - _lastStatusUpdate) >= _settings.StatusUpdateInterval)
                    {
                        _lastStatusUpdate = now;
                        shouldLogStatus = true;
                        resetTime = _resetTime;
                    }
                }
            }

            // Log outside the lock to reduce contention
            if (shouldLogReset)
            {
                _logger.InfoWithEmoji(LogEmojis.CircuitClosed, $"CIRCUIT BREAKER [{_settings.Name}] RESET at {DateTime.UtcNow:HH:mm:ss}");
            }
            else if (shouldLogStatus)
            {
                _logger.WarnWithEmoji(LogEmojis.Warning, $"CIRCUIT BREAKER [{_settings.Name}] ACTIVE: Will reset at {resetTime:HH:mm:ss}");
            }

            return isCircuitOpen;
        }

        /// <inheritdoc/>
        public void Trip(string reason)
        {
            DateTime resetTime;

            lock (_circuitLock)
            {
                var now = DateTime.UtcNow;
                _isOpen = true;
                _resetTime = now.Add(_settings.BreakDuration);
                _lastStatusUpdate = now;
                resetTime = _resetTime;

                // Record this failure within the lock
                RecordFailureInternal();
            }

            // Log outside the lock to reduce contention
            _logger.ErrorWithEmoji(LogEmojis.CircuitOpen, $"CIRCUIT BREAKER [{_settings.Name}] TRIPPED: {reason}. Will reset at {resetTime:HH:mm:ss}");
        }

        /// <inheritdoc/>
        public void Reset()
        {
            bool wasOpen;

            lock (_circuitLock)
            {
                wasOpen = _isOpen;
                _isOpen = false;
                _consecutiveSuccesses = 0;
                _recentFailures.Clear();
            }

            // Only log if the circuit was actually open
            if (wasOpen)
            {
                _logger.InfoWithEmoji(LogEmojis.CircuitClosed, $"CIRCUIT BREAKER [{_settings.Name}] MANUALLY RESET at {DateTime.UtcNow:HH:mm:ss}");
            }
        }

        /// <summary>
        /// Updates the circuit breaker settings.
        /// </summary>
        /// <param name="settings">The new settings to apply.</param>
        public void UpdateSettings(CircuitBreakerSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            lock (_circuitLock)
            {
                var oldSettings = _settings;
                var settingsField = typeof(CircuitBreaker).GetField("_settings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (settingsField != null)
                {
                    settingsField.SetValue(this, settings);
                    _logger.InfoWithEmoji(LogEmojis.CircuitClosed, 
                        $"CIRCUIT BREAKER [{settings.Name}] SETTINGS UPDATED: " +
                        $"FailureThreshold: {oldSettings.FailureThreshold} → {settings.FailureThreshold}, " +
                        $"BreakDuration: {oldSettings.BreakDuration.TotalMinutes}m → {settings.BreakDuration.TotalMinutes}m");
                }
            }
        }

        /// <inheritdoc/>
        public TimeSpan GetReopenTime()
        {
            lock (_circuitLock)
            {
                if (!_isOpen)
                {
                    return TimeSpan.Zero;
                }

                return _resetTime - DateTime.UtcNow;
            }
        }

        /// <inheritdoc/>
        public void RecordSuccess()
        {
            var shouldLogReset = false;
            var successCount = 0;

            lock (_circuitLock)
            {
                _consecutiveSuccesses++;
                successCount = _consecutiveSuccesses;
                _successfulOperations++;
                _totalOperations++;

                // If we have enough consecutive successes, consider closing the circuit
                if (_isOpen && _consecutiveSuccesses >= 3)
                {
                    _isOpen = false;
                    _consecutiveSuccesses = 0;
                    shouldLogReset = true;
                }
            }

            // Log outside the lock
            if (shouldLogReset)
            {
                _logger.InfoWithEmoji(LogEmojis.CircuitClosed, $"CIRCUIT BREAKER [{_settings.Name}] RESET due to {successCount} consecutive successes");
            }
        }

        /// <summary>
        /// Records a failure and potentially trips the circuit breaker.
        /// </summary>
        /// <returns>True if the circuit breaker was tripped, false otherwise.</returns>
        private bool RecordFailureInternal()
        {
            var resetTime = DateTime.MinValue;
            var failureCount = 0;
            var wasTripped = false;

            lock (_circuitLock)
            {
                var now = DateTime.UtcNow;
                _consecutiveSuccesses = 0;
                _failedOperations++;
                _totalOperations++;

                // Add the current time to the failure queue
                _recentFailures.Enqueue(now);

                // Limit the queue size to prevent unbounded growth
                if (_recentFailures.Count > 100) // Arbitrary limit to prevent memory issues
                {
                    _recentFailures.Dequeue();
                }

                // Remove failures outside the time window
                while (_recentFailures.Count > 0 &&
                       (now - _recentFailures.Peek()) > _settings.FailureTimeWindow)
                {
                    _recentFailures.Dequeue();
                }

                failureCount = _recentFailures.Count;

                // If we have enough failures in the time window, trip the circuit breaker
                if (!_isOpen && failureCount >= _settings.FailureThreshold)
                {
                    _isOpen = true;
                    _resetTime = now.Add(_settings.BreakDuration);
                    _lastStatusUpdate = now;
                    _lastTrippedTime = now;
                    _tripCount++;
                    resetTime = _resetTime;
                    wasTripped = true;
                }
            }

            // Log outside the lock
            if (wasTripped)
            {
                _logger.ErrorWithEmoji(LogEmojis.CircuitOpen, $"CIRCUIT BREAKER [{_settings.Name}] TRIPPED: {failureCount} failures in {_settings.FailureTimeWindow.TotalMinutes} minutes. Will reset at {resetTime:HH:mm:ss}");
            }

            return wasTripped;
        }

        /// <inheritdoc/>
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }
            // Check if the circuit is open
            if (IsOpen())
            {
                DateTime? resetTime;
                lock (_circuitLock)
                {
                    resetTime = _isOpen ? _resetTime : null;
                }

                throw new CircuitBreakerOpenException(
                    $"Circuit breaker [{_settings.Name}] is open. Operation rejected. Will reset at {resetTime:HH:mm:ss}.",
                    _settings.Name,
                    resetTime);
            }

            try
            {
                // Execute the action
                var result = await action(cancellationToken).ConfigureAwait(false);

                // Record success
                RecordSuccess();

                return result;
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                // Don't count cancellation as a failure if it was requested
                _logger.DebugWithEmoji(LogEmojis.Cancel, $"Operation cancelled in circuit breaker [{_settings.Name}]: {ex.Message}");
                throw;
            }
            catch (HttpRequestException ex)
            {
                // Handle HTTP-specific exceptions
                _logger.WarnWithEmoji(LogEmojis.Network, $"HTTP request failed in circuit breaker [{_settings.Name}]: {ex.Message}");
                if (!RecordFailureInternal())
                {
                    Trip($"HTTP request exception: {ex.Message}");
                }
                throw;
            }
            catch (TimeoutException ex)
            {
                // Handle timeout exceptions
                _logger.WarnWithEmoji(LogEmojis.Time, $"Operation timed out in circuit breaker [{_settings.Name}]: {ex.Message}");
                if (!RecordFailureInternal())
                {
                    Trip($"Timeout exception: {ex.Message}");
                }
                throw;
            }
            catch (IOException ex)
            {
                // Handle I/O exceptions
                _logger.WarnWithEmoji(LogEmojis.File, $"I/O error in circuit breaker [{_settings.Name}]: {ex.Message}");
                if (!RecordFailureInternal())
                {
                    Trip($"I/O exception: {ex.Message}");
                }
                throw;
            }
            catch (Exception ex)
            {
                // Handle other exceptions
                _logger.ErrorWithEmoji(LogEmojis.Error, ex, $"Unexpected error in circuit breaker [{_settings.Name}]");
                var wasTripped = RecordFailureInternal();

                // Only trip the circuit breaker for transient exceptions if not already tripped
                if (!wasTripped && IsTransientException(ex))
                {
                    Trip($"Transient exception: {ex.GetType().Name}");
                }

                // Rethrow the exception
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
        {
            // Check if the circuit is open
            if (IsOpen())
            {
                DateTime? resetTime;
                lock (_circuitLock)
                {
                    resetTime = _isOpen ? _resetTime : null;
                }

                throw new CircuitBreakerOpenException(
                    $"Circuit breaker [{_settings.Name}] is open. Operation rejected. Will reset at {resetTime:HH:mm:ss}.",
                    _settings.Name,
                    resetTime);
            }

            try
            {
                // Execute the action
                await action(cancellationToken).ConfigureAwait(false);

                // Record success
                RecordSuccess();
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                // Don't count cancellation as a failure if it was requested
                _logger.DebugWithEmoji(LogEmojis.Cancel, $"Operation cancelled in circuit breaker [{_settings.Name}]: {ex.Message}");
                throw;
            }
            catch (HttpRequestException ex)
            {
                // Handle HTTP-specific exceptions
                _logger.Warn($"HTTP request failed in circuit breaker [{_settings.Name}]: {ex.Message}");
                if (!RecordFailureInternal())
                {
                    Trip($"HTTP request exception: {ex.Message}");
                }
                throw;
            }
            catch (TimeoutException ex)
            {
                // Handle timeout exceptions
                _logger.Warn($"Operation timed out in circuit breaker [{_settings.Name}]: {ex.Message}");
                if (!RecordFailureInternal())
                {
                    Trip($"Timeout exception: {ex.Message}");
                }
                throw;
            }
            catch (IOException ex)
            {
                // Handle I/O exceptions
                _logger.Warn($"I/O error in circuit breaker [{_settings.Name}]: {ex.Message}");
                if (!RecordFailureInternal())
                {
                    Trip($"I/O exception: {ex.Message}");
                }
                throw;
            }
            catch (Exception ex)
            {
                // Handle other exceptions
                _logger.ErrorWithEmoji(LogEmojis.Error, ex, $"Unexpected error in circuit breaker [{_settings.Name}]");
                var wasTripped = RecordFailureInternal();

                // Only trip the circuit breaker for transient exceptions if not already tripped
                if (!wasTripped && IsTransientException(ex))
                {
                    Trip($"Transient exception: {ex.GetType().Name}");
                }

                // Rethrow the exception
                throw;
            }
        }

        /// <inheritdoc/>
        public Task<T> ExecuteAsync<T>(string operationName, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            _logger.DebugWithEmoji(LogEmojis.Debug, $"Executing operation '{operationName}' with circuit breaker [{_settings.Name}]");
            return ExecuteAsync(action, cancellationToken);
        }

        /// <inheritdoc/>
        public Task ExecuteAsync(string operationName, Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            _logger.DebugWithEmoji(LogEmojis.Debug, $"Executing operation '{operationName}' with circuit breaker [{_settings.Name}]");
            return ExecuteAsync(action, cancellationToken);
        }

        /// <inheritdoc/>
        public bool RecordFailure(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            var wasTripped = RecordFailureInternal();

            // Only trip the circuit breaker for transient exceptions if not already tripped
            if (!wasTripped && IsTransientException(exception))
            {
                Trip($"Transient exception: {exception.GetType().Name}");
                return true;
            }

            return wasTripped;
        }

        /// <inheritdoc/>
        public CircuitBreakerStats GetStats()
        {
            lock (_circuitLock)
            {
                var state = CircuitBreakerState.Closed;
                if (_isOpen)
                {
                    state = _consecutiveSuccesses > 0 ? CircuitBreakerState.HalfOpen : CircuitBreakerState.Open;
                }

                return new CircuitBreakerStats
                {
                    Name = _settings.Name,
                    State = state,
                    LastTrippedTime = _lastTrippedTime,
                    ResetTime = _isOpen ? _resetTime : null,
                    ConsecutiveSuccesses = _consecutiveSuccesses,
                    RecentFailures = _recentFailures.Count,
                    TotalOperations = _totalOperations,
                    SuccessfulOperations = _successfulOperations,
                    FailedOperations = _failedOperations,
                    TripCount = _tripCount
                };
            }
        }

        /// <summary>
        /// Determines if an exception is transient (temporary) and should trigger the circuit breaker.
        /// </summary>
        /// <param name="exception">The exception to check.</param>
        /// <returns>True if the exception is transient, false otherwise.</returns>
        private static bool IsTransientException(Exception exception)
        {
            if (exception == null)
            {
                return false;
            }

            // Check for specific exception types that are known to be transient
            if (exception is TimeoutException ||
                exception is HttpRequestException ||
                exception is System.Net.WebException ||
                exception is IOException ||
                exception is OperationCanceledException)
            {
                return true;
            }

            // Check for exception messages that indicate transient issues
            var message = exception.Message;
            if (!string.IsNullOrEmpty(message))
            {
                if (message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("temporarily", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("transient", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("retry", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // Check inner exception if present
            if (exception.InnerException != null)
            {
                return IsTransientException(exception.InnerException);
            }

            return false;
        }
    }

    /// <summary>
    /// Exception thrown when an operation is rejected because the circuit breaker is open.
    /// This exception indicates that the system is currently in a protective state to prevent cascading failures.
    /// </summary>
    public class CircuitBreakerOpenException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the CircuitBreakerOpenException class.
        /// </summary>
        public CircuitBreakerOpenException()
            : base("Circuit breaker is open. Operation rejected.")
        {
        }
        /// <summary>
        /// Gets the name of the circuit breaker that rejected the operation.
        /// </summary>
        public string CircuitBreakerName { get; }

        /// <summary>
        /// Gets the time when the circuit breaker will automatically reset, if available.
        /// </summary>
        public DateTime? ResetTime { get; }

        /// <summary>
        /// Initializes a new instance of the CircuitBreakerOpenException class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public CircuitBreakerOpenException(string message)
            : base(message)
        {
            // Extract circuit breaker name from the message if possible
            CircuitBreakerName = ExtractCircuitBreakerName(message);
        }

        /// <summary>
        /// Initializes a new instance of the CircuitBreakerOpenException class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="circuitBreakerName">The name of the circuit breaker that rejected the operation.</param>
        /// <param name="resetTime">The time when the circuit breaker will automatically reset, if available.</param>
        public CircuitBreakerOpenException(string message, string circuitBreakerName, DateTime? resetTime = null)
            : base(message)
        {
            CircuitBreakerName = circuitBreakerName;
            ResetTime = resetTime;
        }

        /// <summary>
        /// Initializes a new instance of the CircuitBreakerOpenException class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public CircuitBreakerOpenException(string message, Exception innerException)
            : base(message, innerException)
        {
            // Extract circuit breaker name from the message if possible
            CircuitBreakerName = ExtractCircuitBreakerName(message);
        }

        /// <summary>
        /// Extracts the circuit breaker name from the exception message.
        /// </summary>
        /// <param name="message">The exception message.</param>
        /// <returns>The extracted circuit breaker name, or "Unknown" if not found.</returns>
        private static string ExtractCircuitBreakerName(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return "Unknown";
            }

            // Try to extract the circuit breaker name from the message
            // Expected format: "Circuit breaker [Name] is open..."
            var startIndex = message.IndexOf('[', StringComparison.Ordinal);
            var endIndex = message.IndexOf(']', StringComparison.Ordinal);

            if (startIndex >= 0 && endIndex > startIndex)
            {
                return message.Substring(startIndex + 1, endIndex - startIndex - 1);
            }

            return "Unknown";
        }
    }
}







