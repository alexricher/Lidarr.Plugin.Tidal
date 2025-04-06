# XML Documentation for DownloadItem

This file contains XML documentation snippets that can be applied to the `DownloadItem` class. Copy and paste these documentation comments above the corresponding methods in the class.

## Class Documentation

```csharp
/// <summary>
/// Represents a single download item from Tidal.
/// This consolidated class includes functionality from all DownloadItem implementations.
/// Provides thread-safe access to download state information.
/// </summary>
public class DownloadItem : IDownloadItem
```

## Constructor

```csharp
/// <summary>
/// Initializes a new instance of the DownloadItem class.
/// </summary>
/// <param name="logger">The logger to use for logging.</param>
public DownloadItem(Logger logger)
```

## Factory Methods

```csharp
/// <summary>
/// Creates a new download item from a RemoteAlbum.
/// </summary>
/// <param name="remoteAlbum">The remote album information.</param>
/// <param name="logger">The logger to use for logging.</param>
/// <returns>A new download item.</returns>
/// <exception cref="ArgumentNullException">Thrown when remoteAlbum is null.</exception>
/// <exception cref="ArgumentException">Thrown when release is null or download URL is empty.</exception>
public static DownloadItem From(RemoteAlbum remoteAlbum, Logger logger = null)

/// <summary>
/// Creates a mock download item for testing.
/// </summary>
/// <param name="id">The unique identifier for the item.</param>
/// <param name="title">The title of the item.</param>
/// <param name="artist">The artist name.</param>
/// <param name="album">The album name.</param>
/// <param name="status">The initial download status.</param>
/// <param name="logger">The logger to use for logging.</param>
/// <returns>A mock download item.</returns>
public static DownloadItem CreateMock(string id, string title, string artist, string album, 
    Interfaces.DownloadItemStatus status, Logger logger)
```

## IDownloadItem Implementation - Properties

```csharp
/// <summary>
/// Gets or sets the unique identifier for this download item.
/// This property is thread-safe.
/// </summary>
public string Id { get; set; }

/// <summary>
/// Gets the unique identifier for this download item. (Compatibility property)
/// </summary>
public string ID => Id;

/// <summary>
/// Gets or sets the title of the download item.
/// </summary>
public string Title { get; set; }

/// <summary>
/// Gets or sets the artist name for the download item.
/// </summary>
public string Artist { get; set; }

/// <summary>
/// Gets or sets the album name for the download item.
/// </summary>
public string Album { get; set; }

/// <summary>
/// Gets or sets whether the item contains explicit content.
/// </summary>
public bool Explicit { get; set; }

/// <summary>
/// Gets or sets the remote album information.
/// </summary>
public RemoteAlbum RemoteAlbum { get; set; }

/// <summary>
/// Gets or sets the serialized remote album information.
/// </summary>
public string RemoteAlbumJson { get; set; }

/// <summary>
/// Gets or sets the destination folder for downloaded files.
/// </summary>
public string DownloadFolder { get; set; }

/// <summary>
/// Gets or sets whether this download can be resumed.
/// This property is thread-safe.
/// </summary>
public bool CanBeResumed { get; set; }

/// <summary>
/// Gets the audio quality for this download.
/// </summary>
public AudioQuality Bitrate => _state.TidalBitrate;

/// <summary>
/// Gets or sets the audio quality for this download as an integer.
/// This property is thread-safe.
/// </summary>
public int BitrateInt { get; set; }

/// <summary>
/// Gets or sets the download status.
/// This property is thread-safe.
/// </summary>
public Interfaces.DownloadItemStatus Status { get; set; }

/// <summary>
/// Gets or sets the download progress percentage (0-100).
/// This property is thread-safe.
/// </summary>
public double Progress { get; set; }

/// <summary>
/// Gets or sets the number of bytes downloaded so far.
/// This property is thread-safe.
/// </summary>
public long DownloadedSize { get; set; }

/// <summary>
/// Gets or sets the total size of the download in bytes.
/// This property is thread-safe.
/// </summary>
public long TotalSize { get; set; }

/// <summary>
/// Gets or sets the array of failed track indexes.
/// This property is thread-safe.
/// </summary>
public int[] FailedTracks { get; set; }

/// <summary>
/// Gets or sets the time when this item was queued.
/// </summary>
public DateTime QueuedTime { get; set; }

/// <summary>
/// Gets or sets the time when the download started.
/// This property is thread-safe.
/// </summary>
public DateTime StartTime { get; set; }

/// <summary>
/// Gets or sets the time when the download completed or failed.
/// This property is thread-safe.
/// </summary>
public DateTime? EndTime { get; set; }

/// <summary>
/// Gets or sets the estimated time remaining for the download.
/// This property is thread-safe.
/// </summary>
public TimeSpan? EstimatedTimeRemaining { get; set; }

/// <summary>
/// Gets or sets the total number of tracks in this download.
/// This property is thread-safe.
/// </summary>
public int TotalTracks { get; set; }

/// <summary>
/// Gets or sets the number of completed tracks.
/// This property is thread-safe.
/// </summary>
public int CompletedTracks { get; set; }

/// <summary>
/// Gets or sets the last error message.
/// This property is thread-safe.
/// </summary>
public string LastErrorMessage { get; set; }
```

## State Management Methods

```csharp
/// <summary>
/// Updates the download progress information.
/// </summary>
/// <param name="downloadedSize">The current downloaded size in bytes.</param>
/// <param name="totalSize">The total size in bytes.</param>
/// <param name="completedTracks">The number of completed tracks.</param>
/// <param name="totalTracks">The total number of tracks.</param>
/// <param name="estimatedTime">The estimated time remaining.</param>
public void UpdateProgress(long downloadedSize, long totalSize, int completedTracks, int totalTracks, TimeSpan? estimatedTime = null)

/// <summary>
/// Sets the download as completed.
/// </summary>
public void SetCompleted()

/// <summary>
/// Sets the download status to failed with an error message.
/// </summary>
/// <param name="errorMessage">The error message describing the failure.</param>
public void SetFailed(string errorMessage)

/// <summary>
/// Sets the download status to warning with a message.
/// </summary>
/// <param name="warningMessage">The warning message.</param>
public void SetWarning(string warningMessage)

/// <summary>
/// Adds track indexes to the list of failed tracks.
/// </summary>
/// <param name="trackIndexes">The track indexes that failed.</param>
public void AddFailedTracks(IEnumerable<int> trackIndexes)

/// <summary>
/// Adds a single track index to the list of failed tracks.
/// </summary>
/// <param name="trackIndex">The track index that failed.</param>
public void AddFailedTrack(int trackIndex)
```

## Download Control Methods

```csharp
/// <summary>
/// Pauses the download.
/// </summary>
public void Pause()

/// <summary>
/// Resumes the download.
/// </summary>
public void Resume()

/// <summary>
/// Cancels the download.
/// </summary>
public void Cancel()

/// <summary>
/// Pauses the download. (Alias for Pause)
/// </summary>
public void PauseDownload()

/// <summary>
/// Resumes the download. (Alias for Resume)
/// </summary>
public void ResumeDownload()

/// <summary>
/// Cancels the download. (Alias for Cancel)
/// </summary>
public void CancelDownload()
```

## Download Execution

```csharp
/// <summary>
/// Performs the actual download operation.
/// </summary>
/// <param name="settings">The Tidal settings to use.</param>
/// <param name="logger">The logger to use.</param>
/// <param name="cancellation">A token to monitor for cancellation requests.</param>
/// <returns>A task representing the asynchronous download operation.</returns>
public async Task DoDownload(TidalSettings settings, Logger logger, CancellationToken cancellation = default)
```

## Utility Methods

```csharp
/// <summary>
/// Extracts the album ID from a Tidal URL.
/// </summary>
/// <param name="url">The Tidal URL.</param>
/// <returns>The extracted album ID, or null if not found.</returns>
private string ExtractAlbumIdFromUrl(string url)

/// <summary>
/// Returns a string representation of this download item.
/// </summary>
/// <returns>A string representation.</returns>
public override string ToString()
```

## DownloadState Class

```csharp
/// <summary>
/// Encapsulates the mutable state of a download item with thread-safe access.
/// </summary>
private class DownloadState
{
    /// <summary>
    /// Gets or sets the unique identifier of the download.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// Gets or sets the current status of the download.
    /// </summary>
    public Interfaces.DownloadItemStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the progress percentage (0-100).
    /// </summary>
    public double Progress { get; set; }

    /// <summary>
    /// Gets or sets the number of bytes downloaded so far.
    /// </summary>
    public long DownloadedSize { get; set; }

    /// <summary>
    /// Gets or sets the total size of the download in bytes.
    /// </summary>
    public long TotalSize { get; set; }

    /// <summary>
    /// Gets or sets whether this download can be resumed.
    /// </summary>
    public bool CanBeResumed { get; set; }

    /// <summary>
    /// Gets or sets the audio quality for this download.
    /// </summary>
    public AudioQuality TidalBitrate { get; set; }

    /// <summary>
    /// Gets or sets the array of failed track indexes.
    /// </summary>
    public int[] FailedTracks { get; set; }

    /// <summary>
    /// Gets or sets the time when the download started.
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Gets or sets the time when the download completed or failed.
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// Gets or sets the estimated time remaining for the download.
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; set; }

    /// <summary>
    /// Gets or sets the total number of tracks in this download.
    /// </summary>
    public int TotalTracks { get; set; }

    /// <summary>
    /// Gets or sets the number of completed tracks.
    /// </summary>
    public int CompletedTracks { get; set; }

    /// <summary>
    /// Gets or sets the last error message.
    /// </summary>
    public string LastErrorMessage { get; set; }
}
``` 