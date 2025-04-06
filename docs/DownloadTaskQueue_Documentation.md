# XML Documentation for DownloadTaskQueue

This file contains XML documentation snippets that can be applied to the `DownloadTaskQueue` class. Copy and paste these documentation comments above the corresponding methods in the class.

## Class Documentation

```csharp
/// <summary>
/// Manages a thread-safe queue of download tasks for Tidal content.
/// Provides functionality for adding, processing, and tracking download tasks.
/// </summary>
public class DownloadTaskQueue : IDisposable
```

## Constructor

```csharp
/// <summary>
/// Initializes a new instance of the DownloadTaskQueue class.
/// </summary>
/// <param name="tidalApi">The Tidal API client to use for downloads.</param>
/// <param name="settings">The Tidal settings.</param>
/// <param name="logger">The logger to use.</param>
public DownloadTaskQueue(TidalAPI tidalApi, TidalSettings settings, Logger logger)
```

## Properties

```csharp
/// <summary>
/// Gets the count of items currently in the queue.
/// </summary>
public int QueueCount => _itemManager.TotalCount;

/// <summary>
/// Gets the count of items currently being downloaded.
/// </summary>
public int DownloadingCount => _itemManager.DownloadingCount;
```

## Methods

```csharp
/// <summary>
/// Gets a list of all items currently in the queue.
/// </summary>
/// <returns>A list of download items.</returns>
public List<IDownloadItem> GetItems()

/// <summary>
/// Adds an album download task to the queue.
/// </summary>
/// <param name="album">The Tidal album to download.</param>
/// <param name="destinationPath">The path where the downloaded files should be saved.</param>
/// <param name="priority">The priority of the download task.</param>
/// <returns>A unique identifier for the download task.</returns>
public string AddAlbum(TidalAlbum album, string destinationPath, QueuePriority priority)

/// <summary>
/// Adds a track download task to the queue.
/// </summary>
/// <param name="track">The Tidal track to download.</param>
/// <param name="destinationPath">The path where the downloaded file should be saved.</param>
/// <param name="priority">The priority of the download task.</param>
/// <returns>A unique identifier for the download task.</returns>
public string AddTrack(TidalTrack track, string destinationPath, QueuePriority priority)

/// <summary>
/// Creates and adds a download item to the queue.
/// </summary>
/// <param name="item">The download item to add to the queue.</param>
/// <param name="priority">The priority of the download task.</param>
/// <returns>A unique identifier for the download task.</returns>
public string AddItem(IDownloadItem item, QueuePriority priority)

/// <summary>
/// Removes a download item from the queue by its identifier.
/// </summary>
/// <param name="id">The unique identifier of the download item to remove.</param>
/// <returns>True if the item was found and removed, false otherwise.</returns>
public bool RemoveItem(string id)

/// <summary>
/// Removes all download items from the queue.
/// </summary>
public void RemoveAll()

/// <summary>
/// Gets a download item by its identifier.
/// </summary>
/// <param name="id">The unique identifier of the download item to retrieve.</param>
/// <returns>The download item if found, null otherwise.</returns>
public IDownloadItem GetItem(string id)

/// <summary>
/// Updates the priority of a download item in the queue.
/// </summary>
/// <param name="id">The unique identifier of the download item to update.</param>
/// <param name="priority">The new priority to set.</param>
/// <returns>True if the item was found and its priority was updated, false otherwise.</returns>
public bool UpdatePriority(string id, QueuePriority priority)

/// <summary>
/// Starts processing the download queue in a background thread.
/// </summary>
public void Start()

/// <summary>
/// Stops the download queue processing.
/// </summary>
public void Stop()

/// <summary>
/// Sets the settings for the download queue.
/// </summary>
/// <param name="settings">The Tidal settings to use.</param>
public void SetSettings(TidalSettings settings)

/// <summary>
/// Disposes resources used by the download task queue.
/// </summary>
public void Dispose()
```

## Internal Methods

```csharp
/// <summary>
/// Starts the queue handler in a background thread.
/// </summary>
public void StartQueueHandler()

/// <summary>
/// Stops the queue handler.
/// </summary>
public void StopQueueHandler()

/// <summary>
/// Processes items in the queue in a background thread.
/// </summary>
/// <param name="stoppingToken">A token to monitor for cancellation requests.</param>
private async Task BackgroundProcessing(CancellationToken stoppingToken)

/// <summary>
/// Processes a single download item.
/// </summary>
/// <param name="item">The item to process.</param>
/// <param name="stoppingToken">A token to monitor for cancellation requests.</param>
private async Task ProcessItemAsync(IDownloadItem item, CancellationToken stoppingToken)

/// <summary>
/// Gets a cancellation token for the specified item.
/// </summary>
/// <param name="item">The download item to get a token for.</param>
/// <param name="linkedToken">An optional token to link with the item's token.</param>
/// <returns>A cancellation token.</returns>
private CancellationToken GetTokenForItem(IDownloadItem item, CancellationToken linkedToken = default)

/// <summary>
/// Saves the current queue to disk.
/// </summary>
private void SaveQueueToDisk()

/// <summary>
/// Restores the queue from disk.
/// </summary>
private void RestoreQueueFromDisk()

/// <summary>
/// Updates the download status manager with current queue information.
/// </summary>
private void UpdateDownloadStatusManager()

/// <summary>
/// Logs the current queue statistics.
/// </summary>
private void LogQueueStatistics()

/// <summary>
/// Updates the last processed time for stall detection.
/// </summary>
private void UpdateLastProcessedTime()

/// <summary>
/// Checks for stalls in queue processing.
/// </summary>
private void CheckForStall()

/// <summary>
/// Parses an audio quality string to an AudioQuality enum value.
/// </summary>
/// <param name="bitrateString">The bitrate string to parse.</param>
/// <returns>The audio quality enum value.</returns>
private TidalSharp.Data.AudioQuality ParseAudioQuality(string bitrateString)
```

## StatisticsManager Class

```csharp
/// <summary>
/// Thread-safe statistics manager for tracking download rates, processed items, and failures.
/// </summary>
private class StatisticsManager
{
    /// <summary>
    /// Initializes a new instance of the StatisticsManager class.
    /// </summary>
    /// <param name="settings">The Tidal settings.</param>
    /// <param name="logger">The logger to use.</param>
    public StatisticsManager(TidalSettings settings, Logger logger)

    /// <summary>
    /// Increments the count of processed items in a thread-safe manner.
    /// </summary>
    public void IncrementProcessedItems()

    /// <summary>
    /// Increments the count of failed downloads in a thread-safe manner.
    /// </summary>
    public void IncrementFailedDownloads()

    /// <summary>
    /// Gets the total number of processed items.
    /// </summary>
    public int TotalItemsProcessed => Interlocked.CompareExchange(ref _totalItemsProcessed, 0, 0);

    /// <summary>
    /// Gets the total number of failed downloads.
    /// </summary>
    public int FailedDownloads => Interlocked.CompareExchange(ref _failedDownloads, 0, 0);

    /// <summary>
    /// Gets the current download rate per hour.
    /// </summary>
    public int DownloadRatePerHour

    /// <summary>
    /// Checks whether download operations should be throttled based on rate limits.
    /// </summary>
    /// <returns>True if downloads should be throttled, false otherwise.</returns>
    public bool ShouldThrottleDownload()

    /// <summary>
    /// Records a download for rate limiting purposes.
    /// </summary>
    /// <param name="trackCount">Number of tracks downloaded.</param>
    public void RecordDownload(int trackCount = 1)

    /// <summary>
    /// Updates the current download rate based on recent downloads.
    /// </summary>
    /// <returns>True if the rate limit status has changed, false otherwise.</returns>
    public bool UpdateDownloadRate()

    /// <summary>
    /// Gets the estimated time until the next download slot is available.
    /// </summary>
    /// <returns>The estimated time span, or null if unknown or not rate limited.</returns>
    public TimeSpan? GetEstimatedTimeUntilNextSlot()

    /// <summary>
    /// Gets whether downloads are currently rate limited.
    /// </summary>
    public bool IsRateLimited => _isRateLimited;
}
```

## ItemCollectionManager Class

```csharp
/// <summary>
/// Encapsulates thread-safe operations on the download item collections.
/// </summary>
private class ItemCollectionManager
{
    /// <summary>
    /// Initializes a new instance of the ItemCollectionManager class.
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    public ItemCollectionManager(Logger logger)

    /// <summary>
    /// Adds an item to the collections in a thread-safe manner.
    /// </summary>
    /// <param name="item">The download item to add.</param>
    public void AddItem(IDownloadItem item)

    /// <summary>
    /// Removes an item from the collections in a thread-safe manner.
    /// </summary>
    /// <param name="item">The download item to remove.</param>
    /// <returns>True if the item was removed, false otherwise.</returns>
    public bool RemoveItem(IDownloadItem item)

    /// <summary>
    /// Gets a copy of all items in a thread-safe manner.
    /// </summary>
    /// <returns>An array of all download items.</returns>
    public IDownloadItem[] GetAllItems()

    /// <summary>
    /// Gets an item at the specified index in a thread-safe manner.
    /// </summary>
    /// <param name="index">The index of the item to get.</param>
    /// <returns>The download item, or null if the index is out of range.</returns>
    public IDownloadItem GetItemAt(int index)

    /// <summary>
    /// Gets the current count of items in a thread-safe manner.
    /// </summary>
    public int Count

    /// <summary>
    /// Gets a cancellation token for the specified item in a thread-safe manner.
    /// </summary>
    /// <param name="item">The download item to get a token for.</param>
    /// <param name="linkedToken">An optional token to link with the item's token.</param>
    /// <returns>A cancellation token.</returns>
    public CancellationToken GetTokenForItem(IDownloadItem item, CancellationToken linkedToken = default)

    /// <summary>
    /// Cancels all items in a thread-safe manner.
    /// </summary>
    public void CancelAll()

    /// <summary>
    /// Disposes all cancellation tokens in a thread-safe manner.
    /// </summary>
    public void Dispose()
}
```

## PlaceholderDownloadItem Class

```csharp
/// <summary>
/// Simple placeholder implementation of IDownloadItem for restored queue items.
/// </summary>
private class PlaceholderDownloadItem : IDownloadItem
{
    /// <summary>
    /// Initializes a new instance of the PlaceholderDownloadItem class.
    /// </summary>
    /// <param name="id">The unique identifier of the item.</param>
    /// <param name="title">The title of the item.</param>
    /// <param name="artist">The artist of the item.</param>
    /// <param name="album">The album of the item.</param>
    /// <param name="bitrate">The audio quality of the item.</param>
    /// <param name="totalSize">The total size of the item in bytes.</param>
    /// <param name="downloadFolder">The folder where the item should be downloaded to.</param>
    /// <param name="isExplicit">Whether the item contains explicit content.</param>
    public PlaceholderDownloadItem(string id, string title, string artist, string album, AudioQuality bitrate, long totalSize, string downloadFolder, bool isExplicit)
}
``` 