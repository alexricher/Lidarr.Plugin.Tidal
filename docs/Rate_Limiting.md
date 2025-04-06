# Rate Limiting System

## Overview

The Rate Limiting System is a sophisticated component of the Tidal plugin for Lidarr that manages API request patterns to prevent triggering rate limits from the Tidal API. Rate limiting is crucial for ensuring the plugin can continue functioning without being temporarily or permanently blocked due to excessive requests.

This documentation covers the technical implementation details, design decisions, and usage of the Rate Limiting System.

## Key Components

### 1. Adaptive Backoff Strategy

The system implements an intelligent backoff strategy that adjusts based on rate limiting incidents:

- **Dynamic Delay Calculation**: Implements exponential backoff based on the number of rate limiting incidents
- **Retry-After Header Integration**: Parses and respects the `Retry-After` header provided by the Tidal API
- **Maximum Backoff Cap**: Limits the maximum backoff period to 5 minutes to ensure responsiveness

### 2. Circuit Breaker Pattern

A circuit breaker pattern is implemented to prevent cascading failures:

- **Failure Tracking**: Records rate limit incidents and API failures
- **Automatic Tripping**: After multiple rate limit incidents, the circuit breaker "trips" to prevent further requests
- **Self-Healing**: Automatically resets after a specified cooling period
- **Status Reporting**: Provides detailed status information about the circuit breaker state

### 3. Request Throttling

The system includes a request throttling mechanism to prevent duplicate searches:

- **Recent Search Tracking**: Maintains a dictionary of recent searches with timestamps
- **Time-Based Throttling**: Prevents identical searches from being performed within a short time period
- **Memory Management**: Automatically cleans up old entries to prevent memory leaks

## Implementation Details

### Rate Limit Detection

The system detects rate limiting through HTTP status code analysis:

```csharp
// Check for rate limiting (HTTP 429)
if (response.StatusCode == HttpStatusCode.TooManyRequests)
{
    HandleRateLimitResponse(response);
    throw new TooManyRequestsException(request, response);
}
```

### Exponential Backoff Logic

When rate limiting is detected, the system implements exponential backoff:

```csharp
// Implement exponential backoff based on the number of incidents
if (_rateLimitIncidents > 1)
{
    // Exponential backoff: double the delay each time, with a cap
    _rateLimitBackoff = TimeSpan.FromSeconds(Math.Min(
        Math.Pow(2, _rateLimitIncidents) * retryAfter.TotalSeconds,
        300)); // Cap at 5 minutes
}
else
{
    _rateLimitBackoff = retryAfter;
}
```

### Circuit Breaker Integration

After repeated rate limit incidents, the circuit breaker trips to prevent further violations:

```csharp
// If we hit too many rate limits in a short period, trip the circuit breaker
if (_rateLimitIncidents >= MAX_RATE_LIMIT_INCIDENTS)
{
    Logger.Error($"Tidal API has rate limited us {_rateLimitIncidents} times in the past {RATE_LIMIT_RESET_WINDOW.TotalMinutes} minutes. Tripping circuit breaker to prevent further rate limit violations.");
    _searchCircuitBreaker.Trip($"Hit rate limit {_rateLimitIncidents} times");
}
```

### Thread Safety

The rate limiting system implements comprehensive thread safety mechanisms:

1. **Rate Limit State Locking**: Uses `_rateLimitLock` to safely access and modify rate limit state
2. **Search History Locking**: Uses `_recentSearchLock` to safely update the record of recent searches
3. **Concurrent Access Protection**: Ensures multiple threads cannot simultaneously modify shared state

## Response Handling

### HTTP 429 (Too Many Requests)

When a 429 response is received, the system:

1. Increments the rate limit incident counter
2. Records the timestamp of the incident
3. Parses the Retry-After header if present
4. Calculates an appropriate backoff period
5. Logs detailed information about the rate limiting
6. Potentially trips the circuit breaker if incidents exceed the threshold

### Successful Responses

After successful responses, the system:

1. Records success in the circuit breaker to "heal" it
2. Resets the rate limit counter if sufficient time has passed
3. Returns to normal operation after the cooling period

## Search Throttling

To prevent duplicate searches from triggering rate limits, the system:

1. Tracks recent searches in a dictionary with timestamps
2. Refuses to repeat identical searches within a 1-minute window
3. Automatically removes old search entries to manage memory
4. Provides detailed logging about throttled searches

## Client Configuration

The system respects client-defined settings:

- **Max Concurrent Searches**: Controls how many search operations can run simultaneously
- **Max Requests Per Minute**: Defines the rate limit for API requests
- **Search Thoroughness**: Influences how deep searches go (affecting total request volume)
- **Smart Pagination**: Intelligently limits page fetching based on diminishing returns

## Performance Optimization

The rate limiting system optimizes performance through:

- **Efficient State Tracking**: Minimizes memory usage for rate limit state
- **Selective Logging**: Reduces log volume for normal operations
- **Fast Lookups**: Uses dictionary-based approach for tracking recent searches
- **Minimal Lock Contention**: Fine-grained locking to prevent thread blocking

## Error Handling

The system implements robust error handling:

- **Timeout Detection**: Recognizes when requests are taking too long
- **Recovery Logic**: Implements proper recovery after rate limiting occurs
- **Detailed Logging**: Provides comprehensive logging for debugging rate limit issues
- **Graceful Degradation**: Reduces functionality rather than failing completely

## Design Decisions

1. **Why Exponential Backoff?**: Exponential backoff provides a progressively more cautious approach as more rate limits are encountered, preventing aggressive retry patterns.

2. **Circuit Breaker Pattern**: This pattern prevents the application from continuously making requests during severe rate limiting scenarios, giving the remote API time to recover.

3. **Thread-Safe Design**: The locking mechanisms ensure that rate limit state is consistently tracked even in a multi-threaded environment.

4. **Search Throttling**: By preventing duplicate searches, the system reduces unnecessary API load while still providing responsive search capabilities.

## Future Improvements

Potential enhancements for the rate limiting system:

1. User-configurable rate limit thresholds
2. More sophisticated retry strategies based on time of day
3. Enhanced telemetry for rate limit incidents
4. Adaptive request throttling based on historical success rates
5. Integration with global request coordination across multiple plugin instances 