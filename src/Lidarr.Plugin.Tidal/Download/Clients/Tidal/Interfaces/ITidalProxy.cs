using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation.Results;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using TidalSharp;

namespace NzbDrone.Core.Download.Clients.Tidal
{
    /// <summary>
    /// Interface for the Tidal API proxy
    /// </summary>
    public interface ITidalProxy
    {
        /// <summary>
        /// Downloads a Tidal album
        /// </summary>
        /// <param name="remoteAlbum">The remote album to download</param>
        /// <param name="settings">The Tidal settings</param>
        /// <returns>A unique ID for the download task</returns>
        Task<string> Download(RemoteAlbum remoteAlbum, TidalSettings settings);

        /// <summary>
        /// Gets the current download queue
        /// </summary>
        /// <param name="settings">The Tidal settings</param>
        /// <returns>List of download client items</returns>
        IEnumerable<DownloadClientItem> GetQueue(TidalSettings settings);

        /// <summary>
        /// Removes an item from the download queue
        /// </summary>
        /// <param name="downloadId">The download ID to remove</param>
        /// <param name="settings">The Tidal settings</param>
        void RemoveFromQueue(string downloadId, TidalSettings settings);

        /// <summary>
        /// Gets a Tidal downloader instance
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A downloader instance</returns>
        Task<Downloader> GetDownloaderAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Tests the connection to the Tidal API
        /// </summary>
        /// <param name="settings">The Tidal settings</param>
        /// <returns>The validation result</returns>
        ValidationResult TestConnection(TidalSettings settings);

        /// <summary>
        /// Determines if the circuit breaker is open
        /// </summary>
        /// <returns>True if the circuit breaker is open</returns>
        bool IsCircuitBreakerOpen();

        /// <summary>
        /// Gets the count of pending downloads
        /// </summary>
        /// <returns>The number of pending downloads</returns>
        int GetPendingDownloadCount();
        
        /// <summary>
        /// Gets the time until the circuit breaker reopens
        /// </summary>
        /// <returns>The time until the circuit breaker reopens</returns>
        TimeSpan GetCircuitBreakerReopenTime();
    }
} 