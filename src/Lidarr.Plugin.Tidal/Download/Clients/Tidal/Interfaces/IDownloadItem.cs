using System;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Music;
using TidalSharp.Data;

namespace NzbDrone.Core.Download.Clients.Tidal.Interfaces
{
    /// <summary>
    /// Defines the interface for a Tidal download item.
    /// This interface represents a single download task in the Tidal download system.
    /// </summary>
    public interface IDownloadItem
    {
        #region Basic Metadata
        /// <summary>
        /// Gets or sets the unique identifier for the download item.
        /// This is the primary identifier used by the system.
        /// </summary>
        string Id { get; set; }

        /// <summary>
        /// Gets the unique identifier for the download item.
        /// This is an alias for Id to maintain compatibility with existing code.
        /// </summary>
        string ID { get; }

        /// <summary>
        /// Gets or sets the title of the download item.
        /// </summary>
        string Title { get; set; }

        /// <summary>
        /// Gets or sets the artist name.
        /// </summary>
        string Artist { get; set; }

        /// <summary>
        /// Gets or sets the album name.
        /// </summary>
        string Album { get; set; }

        /// <summary>
        /// Gets or sets whether the content is explicit.
        /// </summary>
        bool Explicit { get; set; }
        #endregion

        #region Timing Information
        /// <summary>
        /// Gets or sets when the item was queued for download.
        /// </summary>
        DateTime QueuedTime { get; set; }

        /// <summary>
        /// Gets or sets when the download started.
        /// </summary>
        DateTime StartTime { get; set; }

        /// <summary>
        /// Gets or sets when the download completed.
        /// </summary>
        DateTime? EndTime { get; set; }
        
        /// <summary>
        /// Gets or sets the estimated time remaining for the download.
        /// </summary>
        TimeSpan? EstimatedTimeRemaining { get; set; }
        #endregion

        #region Progress Tracking
        /// <summary>
        /// Gets or sets the current download progress as a percentage (0-100).
        /// </summary>
        double Progress { get; set; }

        /// <summary>
        /// Gets or sets the total size of the download in bytes.
        /// </summary>
        long TotalSize { get; set; }

        /// <summary>
        /// Gets or sets the amount of data downloaded in bytes.
        /// </summary>
        long DownloadedSize { get; set; }
        
        /// <summary>
        /// Gets or sets whether the download can be resumed if paused or interrupted.
        /// </summary>
        bool CanBeResumed { get; set; }
        #endregion

        #region Track Information
        /// <summary>
        /// Gets or sets the total number of tracks to download.
        /// </summary>
        int TotalTracks { get; set; }

        /// <summary>
        /// Gets or sets the number of tracks completed.
        /// </summary>
        int CompletedTracks { get; set; }

        /// <summary>
        /// Gets or sets the array of track numbers that failed to download.
        /// </summary>
        int[] FailedTracks { get; set; }
        #endregion

        #region Status and Control
        /// <summary>
        /// Gets or sets the current status of the download.
        /// </summary>
        DownloadItemStatus Status { get; set; }

        /// <summary>
        /// Gets or sets the last error message if the download failed.
        /// </summary>
        string LastErrorMessage { get; set; }

        /// <summary>
        /// Gets or sets the folder where the download will be saved.
        /// </summary>
        string DownloadFolder { get; set; }

        /// <summary>
        /// Gets or sets the remote album information.
        /// </summary>
        RemoteAlbum RemoteAlbum { get; set; }
        
        /// <summary>
        /// Gets or sets the JSON representation of RemoteAlbum for serialization.
        /// </summary>
        string RemoteAlbumJson { get; set; }

        /// <summary>
        /// Gets the audio quality of the download.
        /// </summary>
        AudioQuality Bitrate { get; }

        /// <summary>
        /// Gets or sets the audio quality as an integer value (for compatibility).
        /// </summary>
        int BitrateInt { get; set; }
        #endregion

        #region Control Methods
        /// <summary>
        /// Pauses the download.
        /// </summary>
        void Pause();

        /// <summary>
        /// Resumes a paused download.
        /// </summary>
        void Resume();

        /// <summary>
        /// Cancels the download.
        /// </summary>
        void Cancel();
        
        // Add aliases for backward compatibility. These will be removed in future versions.
        
        /// <summary>
        /// Pauses the download. Alias for <see cref="Pause"/> for backward compatibility.
        /// </summary>
        [Obsolete("Use Pause() instead. This method will be removed in a future version.")]
        void PauseDownload();

        /// <summary>
        /// Resumes a paused download. Alias for <see cref="Resume"/> for backward compatibility.
        /// </summary>
        [Obsolete("Use Resume() instead. This method will be removed in a future version.")]
        void ResumeDownload();

        /// <summary>
        /// Cancels the download. Alias for <see cref="Cancel"/> for backward compatibility.
        /// </summary>
        [Obsolete("Use Cancel() instead. This method will be removed in a future version.")]
        void CancelDownload();
        #endregion
    }

    /// <summary>
    /// Defines the possible states of a download item.
    /// </summary>
    public enum DownloadItemStatus
    {
        /// <summary>
        /// The download is waiting to be processed.
        /// </summary>
        Queued = 0,

        /// <summary>
        /// The download has been paused.
        /// </summary>
        Paused = 1,

        /// <summary>
        /// The download is currently in progress.
        /// </summary>
        Downloading = 2,

        /// <summary>
        /// The download has completed successfully.
        /// </summary>
        Completed = 3,

        /// <summary>
        /// The download has failed.
        /// </summary>
        Failed = 4,

        /// <summary>
        /// The download has a warning.
        /// </summary>
        Warning = 5,
        
        /// <summary>
        /// The system is preparing for download by parsing metadata.
        /// </summary>
        Preparing = 6,
        
        /// <summary>
        /// The download has been cancelled by the user.
        /// </summary>
        Cancelled = 7
    }
} 