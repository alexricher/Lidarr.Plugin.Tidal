using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Download.Clients.Tidal;
using NzbDrone.Core.Download.Clients.Tidal.Interfaces;

namespace Lidarr.Plugin.Tidal.Download.Clients.Tidal.Interfaces
{
    /// <summary>
    /// Extension methods for IDownloadItem to facilitate download operations.
    /// These methods provide a bridge between the abstract download interfaces and the concrete implementations.
    /// </summary>
    public static class DownloadItemExtensions
    {
        /// <summary>
        /// Downloads content from Tidal based on the information in the download item.
        /// </summary>
        /// <remarks>
        /// This method serves as a dispatcher that routes the download request to the appropriate implementation.
        /// It provides several key functions:
        /// <list type="bullet">
        ///   <item><description>Validates input parameters to ensure they're non-null</description></item>
        ///   <item><description>Determines the concrete type of the download item</description></item>
        ///   <item><description>Calls the appropriate implementation of DoDownload based on the item type</description></item>
        ///   <item><description>Provides consistent error handling regardless of the underlying implementation</description></item>
        /// </list>
        /// </remarks>
        /// <param name="item">The download item containing metadata about what to download</param>
        /// <param name="settings">The Tidal settings containing authentication and configuration information</param>
        /// <param name="logger">The logger for diagnostic and operational messages</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests</param>
        /// <returns>A task representing the asynchronous download operation</returns>
        /// <exception cref="ArgumentNullException">Thrown when item or settings parameter is null</exception>
        /// <exception cref="NotImplementedException">Thrown when the provided item type doesn't implement DoDownload</exception>
        public static async Task DoDownload(this IDownloadItem item, NzbDrone.Core.Download.Clients.Tidal.TidalSettings settings, Logger logger, CancellationToken cancellationToken = default)
        {
            if (item == null)
            {
                logger.Error("Cannot download a null item");
                throw new ArgumentNullException(nameof(item));
            }

            if (settings == null)
            {
                logger.Error("Cannot download without settings");
                throw new ArgumentNullException(nameof(settings));
            }

            // Thread-safe null check for logger
            var log = logger ?? LogManager.GetCurrentClassLogger();
            
            // Check if we have a DownloadItem
            if (item is Lidarr.Plugin.Tidal.Download.Clients.Tidal.DownloadItem downloadItem)
            {
                log.Info($"Downloading {item.Title} by {item.Artist}");
                await downloadItem.DoDownload(settings, log, cancellationToken);
                return;
            }
            
            // Handle any other implementation of IDownloadItem
            log.Error($"DoDownload not implemented for the provided IDownloadItem type: {item.GetType().FullName}");
            throw new NotImplementedException($"DoDownload not implemented for the provided IDownloadItem type: {item.GetType().FullName}");
        }
    }
}
