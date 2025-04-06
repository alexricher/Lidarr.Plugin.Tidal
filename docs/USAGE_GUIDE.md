# Usage Guide for Refactored Components

This guide provides examples of how to use the newly refactored components in your code.

## Circuit Breaker Pattern

The circuit breaker pattern helps prevent cascading failures by temporarily stopping operations when too many failures occur.

### Basic Usage

```csharp
// Create a circuit breaker using the factory
var logger = LogManager.GetCurrentClassLogger();
var circuitBreaker = CircuitBreakerFactory.CreateDefault(logger, "MyService");

// Use the circuit breaker to protect an operation
try
{
    await circuitBreaker.ExecuteAsync(async token =>
    {
        // Your operation here
        await SomeOperationAsync(token);
    });
}
catch (CircuitBreakerOpenException ex)
{
    // Handle the case where the circuit breaker is open
    logger.Error($"Circuit breaker is open: {ex.Message}");
}
catch (Exception ex)
{
    // Handle other exceptions
    logger.Error(ex, "Operation failed");
}
```

### Creating Different Types of Circuit Breakers

```csharp
// Create a sensitive circuit breaker (more likely to trip)
var sensitiveBreaker = CircuitBreakerFactory.CreateSensitive(logger, "SensitiveService");

// Create a resilient circuit breaker (less likely to trip)
var resilientBreaker = CircuitBreakerFactory.CreateResilient(logger, "ResilientService");

// Create a custom circuit breaker
var customBreaker = CircuitBreakerFactory.Create(
    logger,
    "CustomService",
    breakDuration: TimeSpan.FromMinutes(3),
    failureThreshold: 4);
```

## Enhanced Logging with Emojis

The enhanced logging extensions make your logs more readable with emojis.

### Basic Usage

```csharp
var logger = LogManager.GetCurrentClassLogger();

// Info logging with emoji
logger.InfoWithEmoji(LogEmojis.Info, "This is an informational message");

// Warning logging with emoji
logger.WarnWithEmoji(LogEmojis.Warning, "This is a warning message");

// Error logging with emoji
logger.ErrorWithEmoji(LogEmojis.Error, "This is an error message");

// Error logging with exception and emoji
try
{
    // Some operation that might throw
}
catch (Exception ex)
{
    logger.ErrorWithEmoji(LogEmojis.Error, ex, "Operation failed: {0}", ex.Message);
}
```

### Common Emojis

```csharp
// Status emojis
logger.InfoWithEmoji(LogEmojis.Success, "Operation completed successfully");
logger.InfoWithEmoji(LogEmojis.Error, "Operation failed");
logger.InfoWithEmoji(LogEmojis.Warning, "Something might be wrong");

// Operation emojis
logger.InfoWithEmoji(LogEmojis.Start, "Starting operation");
logger.InfoWithEmoji(LogEmojis.Stop, "Stopping operation");
logger.InfoWithEmoji(LogEmojis.Retry, "Retrying operation");

// Data-related emojis
logger.InfoWithEmoji(LogEmojis.Download, "Downloading file");
logger.InfoWithEmoji(LogEmojis.Save, "Saving data");
logger.InfoWithEmoji(LogEmojis.Delete, "Deleting file");

// Music-related emojis
logger.InfoWithEmoji(LogEmojis.Music, "Processing music");
logger.InfoWithEmoji(LogEmojis.Album, "Processing album");
logger.InfoWithEmoji(LogEmojis.Artist, "Processing artist");
```

## Retry Policy

The retry policy helps you implement consistent retry logic with exponential backoff.

### Basic Usage

```csharp
var logger = LogManager.GetCurrentClassLogger();
var retryPolicy = RetryPolicyFactory.CreateDefault(logger);

// Use the retry policy to retry an operation
try
{
    var result = await retryPolicy.ExecuteAsync(async token =>
    {
        // Your operation here
        return await SomeOperationAsync(token);
    }, "MyOperation");
}
catch (Exception ex)
{
    // Handle the case where all retries failed
    logger.Error(ex, "All retry attempts failed");
}
```

### Creating Different Types of Retry Policies

```csharp
// Create an aggressive retry policy (more retries, shorter delays)
var aggressivePolicy = RetryPolicyFactory.CreateAggressive(logger);

// Create a conservative retry policy (fewer retries, longer delays)
var conservativePolicy = RetryPolicyFactory.CreateConservative(logger);

// Create a custom retry policy
var customPolicy = RetryPolicyFactory.Create(
    logger,
    maxRetries: 4,
    initialDelay: TimeSpan.FromSeconds(2),
    backoffFactor: 2.5,
    shouldRetry: ex => ex is HttpRequestException || ex is TimeoutException,
    useJitter: true);
```

### Using the RetryableHttpClient

```csharp
var logger = LogManager.GetCurrentClassLogger();
var httpClient = new HttpClient(); // Your HTTP client
var retryPolicy = RetryPolicyFactory.CreateDefault(logger);
var retryableClient = new RetryableHttpClient(httpClient, logger, retryPolicy);

// Use the retryable HTTP client
try
{
    var request = new HttpRequest("https://api.example.com/data");
    var response = await retryableClient.GetAsync(request);
    
    // Process the response
    if (response.IsSuccessful)
    {
        // Handle successful response
    }
    else
    {
        // Handle error response
    }
}
catch (Exception ex)
{
    // Handle the case where all retries failed
    logger.Error(ex, "All HTTP retry attempts failed");
}
```

## Token Bucket Rate Limiting

The token bucket rate limiter helps you implement consistent rate limiting across your application.

### Basic Usage

```csharp
var logger = LogManager.GetCurrentClassLogger();
var rateLimiter = new TokenBucketRateLimiter(
    maxOperationsPerHour: 100,
    logger: logger,
    name: "MyRateLimiter");

// Use the rate limiter to limit operations
try
{
    // Wait for a token to become available
    await rateLimiter.WaitForTokenAsync();
    
    // Perform the operation
    await SomeOperationAsync();
}
catch (Exception ex)
{
    logger.Error(ex, "Operation failed");
}

// Or check if a token is available without waiting
if (rateLimiter.TryConsumeToken())
{
    // Perform the operation
    await SomeOperationAsync();
}
else
{
    // Handle the case where no token is available
    logger.Warn("Rate limit reached, operation skipped");
}
```

### Creating Rate Limiters from Settings

```csharp
var logger = LogManager.GetCurrentClassLogger();

// Create a rate limiter from Tidal settings
var tidalSettings = new TidalSettings { MaxDownloadsPerHour = 100 };
var downloadRateLimiter = TokenBucketRateLimiter.FromTidalSettings(tidalSettings, logger);

// Create a rate limiter from Tidal indexer settings
var indexerSettings = new TidalIndexerSettings { MaxRequestsPerMinute = 30 };
var searchRateLimiter = TokenBucketRateLimiter.FromTidalIndexerSettings(indexerSettings, logger);
```

### Updating Rate Limiter Settings

```csharp
var logger = LogManager.GetCurrentClassLogger();
var rateLimiter = new TokenBucketRateLimiter(
    maxOperationsPerHour: 100,
    logger: logger,
    name: "MyRateLimiter");

// Update the rate limiter settings
rateLimiter.UpdateSettings(200); // Change to 200 operations per hour
```
