using System;
using NzbDrone.Core.Download.Clients.Tidal.Interfaces;

namespace NzbDrone.Core.Download.Clients.Tidal.Utilities
{
    /// <summary>
    /// Extension methods for converting between different status enums
    /// </summary>
    public static class StatusExtensions
    {
        /// <summary>
        /// Converts a Tidal download item status to the core NzbDrone status
        /// </summary>
        /// <param name="status">The Tidal status to convert</param>
        /// <returns>The equivalent NzbDrone core status</returns>
        public static NzbDrone.Core.Download.DownloadItemStatus ToDownloadItemStatus(this Interfaces.DownloadItemStatus status)
        {
            return status switch
            {
                Interfaces.DownloadItemStatus.Queued => NzbDrone.Core.Download.DownloadItemStatus.Queued,
                Interfaces.DownloadItemStatus.Downloading => NzbDrone.Core.Download.DownloadItemStatus.Downloading,
                Interfaces.DownloadItemStatus.Paused => NzbDrone.Core.Download.DownloadItemStatus.Paused,
                Interfaces.DownloadItemStatus.Failed => NzbDrone.Core.Download.DownloadItemStatus.Failed,
                Interfaces.DownloadItemStatus.Completed => NzbDrone.Core.Download.DownloadItemStatus.Completed,
                Interfaces.DownloadItemStatus.Cancelled => NzbDrone.Core.Download.DownloadItemStatus.Warning,
                Interfaces.DownloadItemStatus.Preparing => NzbDrone.Core.Download.DownloadItemStatus.Queued,
                _ => NzbDrone.Core.Download.DownloadItemStatus.Downloading
            };
        }

        /// <summary>
        /// Converts a core NzbDrone status to the Tidal download item status
        /// </summary>
        /// <param name="status">The NzbDrone core status to convert</param>
        /// <returns>The equivalent Tidal status</returns>
        public static Interfaces.DownloadItemStatus ToTidalStatus(this NzbDrone.Core.Download.DownloadItemStatus status)
        {
            return status switch
            {
                NzbDrone.Core.Download.DownloadItemStatus.Queued => Interfaces.DownloadItemStatus.Queued,
                NzbDrone.Core.Download.DownloadItemStatus.Downloading => Interfaces.DownloadItemStatus.Downloading,
                NzbDrone.Core.Download.DownloadItemStatus.Paused => Interfaces.DownloadItemStatus.Paused,
                NzbDrone.Core.Download.DownloadItemStatus.Failed => Interfaces.DownloadItemStatus.Failed,
                NzbDrone.Core.Download.DownloadItemStatus.Completed => Interfaces.DownloadItemStatus.Completed,
                NzbDrone.Core.Download.DownloadItemStatus.Warning => Interfaces.DownloadItemStatus.Paused,
                _ => Interfaces.DownloadItemStatus.Queued
            };
        }
    }
} 