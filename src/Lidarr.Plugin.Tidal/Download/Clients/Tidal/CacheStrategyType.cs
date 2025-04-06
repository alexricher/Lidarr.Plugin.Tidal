using System;

namespace Lidarr.Plugin.Tidal.Indexers.Tidal
{
    /// <summary>
    /// Enum representing different cache eviction strategies.
    /// </summary>
    public enum CacheStrategyType
    {
        LeastRecentlyUsed = 0,
        MostFrequentlyUsed = 1,
        TimeToLive = 2,
        Adaptive = 3
        }

}