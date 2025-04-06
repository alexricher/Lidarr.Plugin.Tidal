using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Tidal.Services.Logging;
using NLog;
using NzbDrone.Plugin.Tidal.Constants;

namespace Lidarr.Plugin.Tidal.Services.CircuitBreaker
{
    /// <summary>
    /// Defines severity levels for the circuit breaker.
    /// </summary>
    public enum CircuitBreakerSeverity
    {
        /// <summary>
        /// Low severity, indicating a minor issue.
        /// </summary>
        Low,
        
        /// <summary>
        /// Medium severity, indicating a moderate issue.
        /// </summary>
        Medium,
        
        /// <summary>
        /// High severity, indicating a severe issue.
        /// </summary>
        High
    }

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
        private CircuitBreakerSeverity _currentSeverity = CircuitBreakerSeverity.Low;
        private TimeSpan _failureWindow;

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
        /// Gets the current severity level of the circuit breaker.
        /// </summary>
        /// <returns>The current severity level</returns>
        public CircuitBreakerSeverity GetSeverity()
        {
            lock (_circuitLock)
            {
                return _currentSeverity;
            }
        }

        /// <summary>
        /// Initializes a new instance of the CircuitBreaker class.
        /// </summary>
        /// <param name="logger">The logger instance to use for logging.</param>
        /// <param name="settings">The settings for the circuit breaker.</param>
        public CircuitBreaker(Logger logger, CircuitBreakerSettings settings)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            
            // Validate and apply default settings if needed
            if (string.IsNullOrWhiteSpace(_settings.Name))
            {
                _settings.Name = "Default";
            }
            
            if (_settings.FailureThreshold <= 0)
            {
                _settings.FailureThreshold = TidalConstants.DefaultCircuitBreakerFailureThreshold;
            }
            
            if (_settings.BreakDuration <= TimeSpan.Zero)
            {
                _settings.BreakDuration = TidalConstants.DefaultCircuitBreakerBreakDuration;
            }
            
            if (_settings.FailureTimeWindow <= TimeSpan.Zero)
            {
                _settings.FailureTimeWindow = TidalConstants.DefaultCircuitBreakerFailureTimeWindow;
            }
            
            if (_settings.StatusUpdateInterval <= TimeSpan.Zero)
            {
                _settings.StatusUpdateInterval = TidalConstants.DefaultCircuitBreakerStatusUpdateInterval;
            }

            _failureWindow = _settings.FailureTimeWindow;
            
            _logger.Debug($"CircuitBreaker '{_settings.Name}' initialized with failure threshold: {_settings.FailureThreshold}, break duration: {_settings.BreakDuration.TotalMinutes:F1}m");
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
            lock (_circuitLock)
            {
                if (_isOpen)
                {
                    _logger.Debug($"Circuit breaker {Name} is already open");
                    return;
                }

                _isOpen = true;
                _resetTime = DateTime.UtcNow.Add(_settings.BreakDuration);
                _lastTrippedTime = DateTime.UtcNow;
                _consecutiveSuccesses = 0;
                _tripCount++;
                
                // Set severity based on trip count and failure rate
                UpdateSeverity();
                
                _logger.Warn($"Circuit breaker {Name} tripped: {reason}. Will remain open until {_resetTime:g}. Severity: {_currentSeverity}");
                _lastStatusUpdate = DateTime.UtcNow;
            }
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
                    // Update the _failureWindow field to stay in sync with settings
                    _failureWindow = settings.FailureTimeWindow;
                    
                    _logger.InfoWithEmoji(LogEmojis.CircuitClosed, 
                        $"CIRCUIT BREAKER [{settings.Name}] SETTINGS UPDATED: " +
                        $"FailureThreshold: {oldSettings.FailureThreshold} → {settings.FailureThreshold}, " +
                        $"BreakDuration: {oldSettings.BreakDuration.TotalMinutes}m → {settings.BreakDuration.TotalMinutes}m, " +
                        $"FailureTimeWindow: {oldSettings.FailureTimeWindow.TotalMinutes}m → {settings.FailureTimeWindow.TotalMinutes}m");
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

        /// <summary>
        /// Called for successful operations to reset the circuit breaker and record success metrics.
        /// </summary>
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
                
                // Clean up outdated failures when we record success too
                var now = DateTime.UtcNow;
                while (_recentFailures.Count > 0 && 
                       (now - _recentFailures.Peek()) > _failureWindow)
                {
                    _recentFailures.Dequeue();
                }

                // If we have enough consecutive successes, consider closing the circuit
                if (_isOpen && _consecutiveSuccesses >= 3)
                {
                    _isOpen = false;
                    _consecutiveSuccesses = 0;
                    shouldLogReset = true;
                    // Update severity since state is changing
                    UpdateSeverity();
                }
            }

            // Log outside the lock
            if (shouldLogReset)
            {
                _logger.InfoWithEmoji(LogEmojis.CircuitClosed, $"CIRCUIT BREAKER [{_settings.Name}] RESET due to {successCount} consecutive successes");
            }
        }

        /// <summary>
        /// Records a failed operation, which can be used for circuit breaker state management.
        /// </summary>
        /// <param name="exception">The exception that caused the failure.</param>
        /// <returns>True if the circuit breaker was tripped as a result of this failure.</returns>
        public bool RecordFailure(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            lock (_circuitLock)
            {
                _consecutiveSuccesses = 0;
                _failedOperations++;
                _totalOperations++;
                
                // Add the current time to the failure queue
                var now = DateTime.UtcNow;
                _recentFailures.Enqueue(now);
                
                // Limit the queue size to prevent unbounded growth
                if (_recentFailures.Count > 100) // Arbitrary limit to prevent memory issues
                {
                    _recentFailures.Dequeue();
                }
                
                // Remove failures outside the time window
                while (_recentFailures.Count > 0 &&
                       (now - _recentFailures.Peek()) > _failureWindow)
                {
                    _recentFailures.Dequeue();
                }
                
                var failureCount = _recentFailures.Count;
                var wasTripped = false;
                
                // If we have enough failures in the time window, trip the circuit breaker
                if (!_isOpen && failureCount >= _settings.FailureThreshold)
                {
                    Trip($"Failure threshold reached: {failureCount} failures in {_failureWindow.TotalMinutes} minutes - Last error: {exception.Message}");
                    wasTripped = true;
                }
                
                // Update the severity level using the improved algorithm
                UpdateSeverity();
                
                // Check for transient exceptions if not already tripped
                if (!wasTripped && !_isOpen && IsTransientException(exception))
                {
                    Trip($"Transient exception: {exception.GetType().Name} - {exception.Message}");
                    return true;
                }
                
                // Log less frequently to avoid excessive logging
                if (failureCount % 5 == 0 || failureCount == _settings.FailureThreshold - 1)
                {
                    _logger.Warn($"Circuit breaker {Name} recorded failure {failureCount}/{_settings.FailureThreshold}: {exception.Message}");
                }
                
                return wasTripped || _isOpen;
            }
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
                RecordFailure(ex);
                throw;
            }
            catch (TimeoutException ex)
            {
                // Handle timeout exceptions
                _logger.WarnWithEmoji(LogEmojis.Time, $"Operation timed out in circuit breaker [{_settings.Name}]: {ex.Message}");
                RecordFailure(ex);
                throw;
            }
            catch (IOException ex)
            {
                // Handle I/O exceptions
                _logger.WarnWithEmoji(LogEmojis.File, $"I/O error in circuit breaker [{_settings.Name}]: {ex.Message}");
                RecordFailure(ex);
                throw;
            }
            catch (Exception ex)
            {
                // Handle other exceptions
                _logger.ErrorWithEmoji(LogEmojis.Error, ex, $"Unexpected error in circuit breaker [{_settings.Name}]");
                RecordFailure(ex);
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
                RecordFailure(ex);
                throw;
            }
            catch (TimeoutException ex)
            {
                // Handle timeout exceptions
                _logger.Warn($"Operation timed out in circuit breaker [{_settings.Name}]: {ex.Message}");
                RecordFailure(ex);
                throw;
            }
            catch (IOException ex)
            {
                // Handle I/O exceptions
                _logger.Warn($"I/O error in circuit breaker [{_settings.Name}]: {ex.Message}");
                RecordFailure(ex);
                throw;
            }
            catch (Exception ex)
            {
                // Handle other exceptions
                _logger.ErrorWithEmoji(LogEmojis.Error, ex, $"Unexpected error in circuit breaker [{_settings.Name}]");
                RecordFailure(ex);
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

        /// <summary>
        /// Updates the severity level based on recent failure history.
        /// </summary>
        private void UpdateSeverity()
        {
            lock (_circuitLock)
            {
                // Calculate severity based on multiple factors with weighted scoring
                double severityScore = 0;
                
                // Factor 1: Trip count - How many times the circuit breaker has been tripped
                // Higher trip count indicates more recurring issues
                if (_tripCount > 15)
                {
                    severityScore += 3.0; // High contribution to severity
                }
                else if (_tripCount > 8)
                {
                    severityScore += 2.0; // Medium contribution
                }
                else if (_tripCount > 3)
                {
                    severityScore += 1.0; // Low contribution
                }
                else
                {
                    severityScore += 0.5; // Minimal contribution
                }
                
                // Factor 2: Failure density - Number of failures relative to threshold
                // A large number of failures in a short time indicates acute issues
                double failureRatio = (double)_recentFailures.Count / _settings.FailureThreshold;
                if (failureRatio >= 3.0)
                {
                    severityScore += 3.0; // Severe failure density
                }
                else if (failureRatio >= 2.0)
                {
                    severityScore += 2.0; // High failure density
                }
                else if (failureRatio >= 1.0)
                {
                    severityScore += 1.0; // Moderate failure density
                }
                
                // Factor 3: Failure rate - How quickly failures are occurring
                // Rapid succession of failures indicates more severe problems
                if (_recentFailures.Count >= 2)
                {
                    var oldestFailure = _recentFailures.Peek();
                    var newestFailure = DateTime.UtcNow;
                    var timeSpan = newestFailure - oldestFailure;
                    
                    if (timeSpan.TotalSeconds > 0)
                    {
                        var failuresPerSecond = _recentFailures.Count / timeSpan.TotalSeconds;
                        
                        if (failuresPerSecond >= 0.5) // More than one failure every 2 seconds
                        {
                            severityScore += 3.0; // Very rapid failure rate
                        }
                        else if (failuresPerSecond >= 0.2) // More than one failure every 5 seconds
                        {
                            severityScore += 2.0; // High failure rate
                        }
                        else if (failuresPerSecond >= 0.05) // More than one failure every 20 seconds
                        {
                            severityScore += 1.0; // Moderate failure rate
                        }
                    }
                }
                
                // Factor 4: Success ratio - Successful operations relative to total
                if (_totalOperations > 0)
                {
                    double successRatio = (double)_successfulOperations / _totalOperations;
                    
                    if (successRatio < 0.3) // Less than 30% successful
                    {
                        severityScore += 2.0; // Very poor success rate
                    }
                    else if (successRatio < 0.6) // Less than 60% successful
                    {
                        severityScore += 1.0; // Poor success rate
                    }
                }
                
                // Determine severity level based on final score
                if (severityScore >= 5.0)
                {
                    _currentSeverity = CircuitBreakerSeverity.High;
                }
                else if (severityScore >= 2.5)
                {
                    _currentSeverity = CircuitBreakerSeverity.Medium;
                }
                else
                {
                    _currentSeverity = CircuitBreakerSeverity.Low;
                }
                
                _logger.Debug($"Circuit breaker {Name} severity set to {_currentSeverity} (score: {severityScore:F1}) " +
                               $"[trips: {_tripCount}, failures: {_recentFailures.Count}, ratio: {failureRatio:F2}]");
            }
        }

        /// <summary>
        /// Checks if the circuit breaker is open with at least the specified severity level.
        /// </summary>
        /// <param name="minimumSeverity">The minimum severity level to check for.</param>
        /// <returns>True if the circuit is open with at least the specified severity, false otherwise.</returns>
        public bool IsOpenWithSeverity(CircuitBreakerSeverity minimumSeverity)
        {
            lock (_circuitLock)
            {
                if (!_isOpen)
                {
                    return false;
                }
                
                // If open, check if the current severity meets or exceeds the minimum
                return _currentSeverity >= minimumSeverity;
            }
        }

        /// <summary>
        /// Gets the current severity level and state of the circuit breaker.
        /// </summary>
        /// <returns>A tuple containing the current state and severity level.</returns>
        public (CircuitBreakerState State, CircuitBreakerSeverity Severity) GetStateAndSeverity()
        {
            lock (_circuitLock)
            {
                var state = CircuitBreakerState.Closed;
                if (_isOpen)
                {
                    state = _consecutiveSuccesses > 0 ? CircuitBreakerState.HalfOpen : CircuitBreakerState.Open;
                }
                
                return (state, _currentSeverity);
            }
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








