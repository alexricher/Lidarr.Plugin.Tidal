using System;

namespace NzbDrone.Core.Download.Clients.Tidal.Interfaces
{
    /// <summary>
    /// Represents the priority level of a download item in the queue.
    /// </summary>
    public enum DownloadItemPriority
    {
        /// <summary>
        /// Low priority downloads will be processed after all other priorities.
        /// Suitable for non-urgent background downloads.
        /// </summary>
        Low = -1,
        
        /// <summary>
        /// Normal priority is the default for most downloads.
        /// </summary>
        Normal = 0,
        
        /// <summary>
        /// High priority downloads will be processed before normal and low priority.
        /// Use for important downloads that should be completed quickly.
        /// </summary>
        High = 1,
        
        /// <summary>
        /// Urgent priority downloads are processed immediately, potentially interrupting other downloads.
        /// Use sparingly for critical downloads that cannot wait.
        /// </summary>
        Urgent = 2
    }
} 