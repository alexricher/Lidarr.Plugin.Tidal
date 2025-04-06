using System;
using System.Threading.Tasks;
using NzbDrone.Core.Download.Clients.Tidal;
using TidalSharp.Data;

namespace NzbDrone.Core.Download.Clients.Tidal.API
{
    /// <summary>
    /// Interface for Tidal API operations to allow for easier testing and future API changes
    /// </summary>
    public interface ITidalApiClient
    {
        /// <summary>
        /// Authenticates with the Tidal API
        /// </summary>
        /// <param name="username">Tidal username</param>
        /// <param name="password">Tidal password</param>
        /// <returns>True if authentication was successful</returns>
        Task<bool> AuthenticateAsync(string username, string password);
        
        /// <summary>
        /// Downloads a track from Tidal
        /// </summary>
        /// <param name="trackId">Tidal track ID</param>
        /// <param name="outputPath">Path to save the track</param>
        /// <param name="quality">Audio quality to download</param>
        /// <returns>Downloaded file information</returns>
        Task<byte[]> DownloadTrackAsync(string trackId, string outputPath, TidalSharp.Data.AudioQuality quality = TidalSharp.Data.AudioQuality.HIGH);
        
        /// <summary>
        /// Refreshes the authentication token
        /// </summary>
        /// <returns>True if refresh was successful</returns>
        Task<bool> RefreshTokenAsync();
        
        /// <summary>
        /// Gets the current country code from the Tidal API
        /// </summary>
        /// <returns>Two-letter country code</returns>
        Task<string> GetCountryCodeAsync();
    }
}