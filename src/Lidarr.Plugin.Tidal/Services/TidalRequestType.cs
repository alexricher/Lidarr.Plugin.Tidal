using System;

namespace Lidarr.Plugin.Tidal.Services
{
    /// <summary>
    /// Defines the types of requests that can be rate limited
    /// </summary>
    public enum TidalRequestType
    {
        /// <summary>
        /// Search requests to the Tidal API
        /// </summary>
        Search,
        
        /// <summary>
        /// Download requests to the Tidal API
        /// </summary>
        Download
    }
} 