using System;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.Download.Clients.Tidal;
using NzbDrone.Core.Indexers.Tidal;
using Lidarr.Plugin.Tidal.Services;

namespace NzbDrone.Core.Download.Clients.Tidal.Services
{
    /// <summary>
    /// Interface for rate limiters that control API request rates
    /// </summary>
    public interface IRateLimiter : IDisposable
    {
        /// <summary>
        /// Waits for a slot to become available for the specified request type
        /// </summary>
        /// <param name="requestType">Type of request (search or download)</param>
        /// <param name="token">Cancellation token</param>
        Task WaitForSlot(TidalRequestType requestType, CancellationToken token = default);
        
        /// <summary>
        /// Tries to consume a token for the specified request type without waiting
        /// </summary>
        /// <param name="requestType">Type of request (search or download)</param>
        /// <returns>True if a token was consumed, false otherwise</returns>
        bool TryConsumeToken(TidalRequestType requestType);
        
        /// <summary>
        /// Gets the estimated wait time for the specified request type
        /// </summary>
        /// <param name="requestType">Type of request (search or download)</param>
        /// <returns>TimeSpan representing the wait time</returns>
        TimeSpan GetEstimatedWaitTime(TidalRequestType requestType);
        
        /// <summary>
        /// Gets the current number of requests being processed for the specified request type
        /// </summary>
        /// <param name="requestType">Type of request (search or download)</param>
        /// <returns>Number of requests being processed</returns>
        int GetCurrentRequestCount(TidalRequestType requestType);
        
        /// <summary>
        /// Updates the settings for the rate limiters
        /// </summary>
        /// <param name="downloadSettings">New download settings</param>
        /// <param name="indexerSettings">New indexer settings</param>
        void UpdateSettings(TidalSettings downloadSettings, TidalIndexerSettings indexerSettings);
        
        /// <summary>
        /// Updates the settings for the specified request type
        /// </summary>
        /// <param name="newSettings">The new settings to use</param>
        /// <param name="type">The type of request to update settings for</param>
        void OnSettingsChanged(TidalSettings newSettings, TidalRequestType type);
    }
}
