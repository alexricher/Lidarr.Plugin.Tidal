using System.Collections.Generic;

namespace Lidarr.Plugin.Tidal.Indexers.Tidal.Interfaces
{
    /// <summary>
    /// Defines statistics returned by the cache metrics provider.
    /// </summary>
    public class TidalCacheStats
    {
        /// <summary>
        /// Gets or sets a collection of cache keys mapped to a metric (e.g., access count or timestamp).
        /// The interpretation depends on the eviction strategy (LRU, LFU).
        /// </summary>
        public Dictionary<string, long> PopularKeys { get; set; } = new Dictionary<string, long>();

        // Add other relevant statistics if needed, e.g.:
        // public long TotalSizeInBytes { get; set; }
        // public int TotalItemCount { get; set; }
        // public long HitCount { get; set; }
        // public long MissCount { get; set; }
    }

    /// <summary>
    /// Interface for providing metrics about the Tidal indexer cache.
    /// </summary>
    public interface ITidalCacheMetrics
    {
        /// <summary>
        /// Gets the current statistics of the cache.
        /// </summary>
        /// <returns>An object containing cache statistics.</returns>
        TidalCacheStats GetStatistics();
    }
}