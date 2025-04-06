# Refactoring Documentation

## Overview

This document outlines the refactoring work done to improve the codebase structure, eliminate duplication, enhance error handling, and improve resource management in the Lidarr.Plugin.Tidal project.

## Table of Contents

- [Token Bucket Rate Limiting](#token-bucket-rate-limiting)
- [Download Item Consolidation](#download-item-consolidation)
- [Error Handling Improvements](#error-handling-improvements)
- [Resource Management](#resource-management)
- [Documentation Enhancements](#documentation-enhancements)

## Token Bucket Rate Limiting

### Problem

The codebase contained multiple implementations of the token bucket algorithm for rate limiting in different classes:

1. `RateLimiter.cs` - Used for API rate limiting
2. `DownloadTaskQueue.cs` - Used for download rate limiting
3. `TokenBucketUtil.cs` - Utility methods for token bucket operations

This duplication made it difficult to maintain consistent behavior across different parts of the application and increased the risk of bugs.

### Solution

We transformed the existing `TokenBucketUtil` static utility class into a proper `TokenBucketRateLimiter` class that implements the `ITokenBucketRateLimiter` interface:

```csharp
public interface ITokenBucketRateLimiter : IDisposable
{
    Task WaitForTokenAsync(CancellationToken token = default);
    bool TryConsumeToken();
    TimeSpan GetEstimatedWaitTime();
    double GetCurrentTokenCount();
    double GetMaxTokenCount();
    int GetCurrentRateLimit();
    void UpdateSettings(int maxOperationsPerHour);
}
```

We maintained backward compatibility by keeping the static utility methods as legacy methods, while adding the new instance-based implementation. This approach allowed us to consolidate the token bucket logic without breaking existing code.

The new `TokenBucketRateLimiter` class is now used by both the API rate limiter and the download task queue, ensuring consistent behavior and eliminating code duplication.

### Benefits

1. **Consistent Behavior**: All rate limiting now uses the same algorithm and behavior
2. **Improved Testability**: The interface makes it easy to mock for testing
3. **Enhanced Error Handling**: Centralized error handling for rate limiting operations
4. **Better Resource Management**: Proper disposal of resources
5. **Simplified Maintenance**: Changes to the rate limiting algorithm only need to be made in one place

## Download Item Consolidation

### Problem

The codebase had multiple implementations of the `DownloadItem` class:

1. `src/Lidarr.Plugin.Tidal/Download/Clients/Tidal/DownloadItem.cs`
2. `src/Lidarr.Plugin.Tidal/Download/Clients/Tidal/Queue/DownloadItem.cs`

This duplication led to inconsistent behavior and made it difficult to maintain.

### Solution

We consolidated these implementations into a single `DownloadItem` class in `src/Lidarr.Plugin.Tidal/Download/Clients/Tidal/Models/DownloadItem.cs` that implements the `IDownloadItem` interface.

The new class provides factory methods for creating download items in different scenarios:

```csharp
// Create for album download
public static async Task<IDownloadItem> CreateForAlbumAsync(
    RemoteAlbum remoteAlbum,
    AudioQuality quality,
    string tidalUrl,
    Logger logger)

// Create mock for testing
public static IDownloadItem CreateMock(
    string id,
    string title,
    string artist,
    string album,
    DownloadItemStatus status,
    Logger logger)

// Create from serialized record
public static IDownloadItem FromRecord(QueueItemRecord record, Logger logger)
```

### Benefits

1. **Consistent Behavior**: All download items now use the same implementation
2. **Improved Testability**: Factory methods make it easy to create test instances
3. **Enhanced Error Handling**: Centralized error handling for download item operations
4. **Better Resource Management**: Proper handling of resources
5. **Simplified Maintenance**: Changes to the download item only need to be made in one place

## Error Handling Improvements

### Problem

Error handling was inconsistent across the codebase, with some methods having thorough error handling while others had minimal or no error handling.

### Solution

We improved error handling throughout the codebase by:

1. **Adding try-catch blocks** to critical operations
2. **Logging detailed error messages** with context information
3. **Implementing graceful fallbacks** for error conditions
4. **Using CancellationToken properly** to handle cancellation
5. **Adding parameter validation** to prevent null reference exceptions

Example of improved error handling:

```csharp
public async Task WaitForSlot(TidalRequestType requestType, CancellationToken token = default)
{
    if (_disposed) throw new ObjectDisposedException(nameof(RateLimiter));

    // Get the rate limiter for the request type
    if (!_rateLimiters.TryGetValue(requestType, out var rateLimiter))
    {
        throw new ArgumentException($"Unknown request type: {requestType}");
    }

    try
    {
        _logger.Debug($"[{requestType}] Waiting for rate limit slot");
        await rateLimiter.WaitForTokenAsync(token).ConfigureAwait(false);
        _logger.Debug($"[{requestType}] Rate limit slot acquired");
    }
    catch (OperationCanceledException)
    {
        _logger.Debug($"[{requestType}] Wait for rate limit slot was cancelled");
        throw;
    }
    catch (Exception ex)
    {
        _logger.Error(ex, $"[{requestType}] Error waiting for rate limit slot: {ex.Message}");
        throw;
    }
}
```

### Benefits

1. **Improved Stability**: Better handling of error conditions
2. **Enhanced Diagnostics**: More detailed error messages for troubleshooting
3. **Graceful Degradation**: Fallback behavior when errors occur
4. **Proper Resource Cleanup**: Resources are properly disposed even in error conditions

## Resource Management

### Problem

Some classes implemented `IDisposable` but there were instances where resources might not be properly disposed, especially in error conditions.

### Solution

We improved resource management by:

1. **Implementing IDisposable properly** in all classes that manage resources
2. **Using using statements** for disposable resources
3. **Adding try-finally blocks** to ensure resources are released
4. **Checking for disposed state** before operations
5. **Adding null checks** for resources

Example of improved resource management:

```csharp
public void Dispose()
{
    if (_disposed) return;
    _disposed = true;

    foreach (var rateLimiter in _rateLimiters.Values)
    {
        try
        {
            rateLimiter.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Error disposing rate limiter: {ex.Message}");
        }
    }

    _rateLimiters.Clear();
    _logger.Debug("Rate limiter disposed");
}
```

### Benefits

1. **Prevented Resource Leaks**: Resources are properly disposed
2. **Improved Stability**: Better handling of resource cleanup
3. **Enhanced Diagnostics**: Logging of resource disposal issues
4. **Proper Error Handling**: Resources are released even in error conditions

## Documentation Enhancements

### Problem

While many classes had good XML documentation, some methods lacked detailed comments, making it difficult to understand their purpose and usage.

### Solution

We improved documentation by:

1. **Adding XML documentation** to all public classes and methods
2. **Enhancing existing documentation** with more details
3. **Adding code examples** where appropriate
4. **Documenting error conditions** and exceptions
5. **Adding class-level documentation** to explain the purpose of each class

Example of improved documentation:

```csharp
/// <summary>
/// Implements rate limiting for Tidal API requests.
/// This class uses the TokenBucketRateLimiter to enforce rate limits.
/// </summary>
public class RateLimiter : IRateLimiter
{
    /// <summary>
    /// Initializes a new instance of the RateLimiter class.
    /// </summary>
    /// <param name="downloadSettings">The download settings to use.</param>
    /// <param name="indexerSettings">The indexer settings to use.</param>
    /// <param name="logger">The logger to use for logging.</param>
    public RateLimiter(TidalSettings downloadSettings, TidalIndexerSettings indexerSettings, Logger logger)
    {
        // Implementation
    }

    /// <inheritdoc/>
    public async Task WaitForSlot(TidalRequestType requestType, CancellationToken token = default)
    {
        // Implementation
    }
}
```

### Benefits

1. **Improved Maintainability**: Easier to understand the code
2. **Enhanced Collaboration**: Easier for new developers to contribute
3. **Better IDE Support**: Better IntelliSense and documentation tooltips
4. **Clearer Intent**: Explicit documentation of method purpose and behavior

## Conclusion

The refactoring work has significantly improved the codebase structure, eliminated duplication, enhanced error handling, and improved resource management. These changes make the codebase more maintainable, more robust, and easier to extend in the future.

The use of composition and interfaces has made the code more testable and has reduced coupling between components. The improved error handling and resource management have made the code more resilient to failures and less prone to resource leaks.

Overall, these changes have improved the quality of the codebase while preserving all existing functionality.
