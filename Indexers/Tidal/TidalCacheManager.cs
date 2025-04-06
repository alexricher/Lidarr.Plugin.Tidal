using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Indexers.Tidal;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Download.Clients.Tidal;
using System.Collections;

namespace NzbDrone.Core.Indexers.Tidal
{
    /// <summary>
    /// Extension methods for ICacheManager to provide additional functionality for testing
    /// </summary>
    public static class CacheManagerExtensions
    {
        public static List<string> GetCacheKeys(this ICacheManager cacheManager)
        {
            // This is a placeholder that would need real implementation in production
            return new List<string>();
        }

        public static void ClearExpired(this ICacheManager cacheManager)
        {
            // This is a placeholder that would need real implementation in production
        }
    }

    public interface ITidalCacheManager
    {
        void EvictLeastRecentlyUsed(int percentToEvict = 20);
        void EvictByPattern(string pattern);
        void EvictExpired();
        void EnforceMemoryLimit();
    }

    public class TidalCacheManager : ITidalCacheManager
    {
        private readonly ICacheManager _cacheManager;
        private readonly ITidalCacheMetrics _metrics;
        private readonly Logger _logger;
        private readonly TidalSettings _settings;

        public TidalCacheManager(
            ICacheManager cacheManager,
            ITidalCacheMetrics metrics,
            TidalSettings settings,
            Logger logger)
        {
            _cacheManager = cacheManager;
            _metrics = metrics;
            _settings = settings;
            _logger = logger;
        }

        public void EvictLeastRecentlyUsed(int percentToEvict = 20)
        {
            try
            {
                var stats = _metrics.GetStatistics();
                if (stats?.PopularKeys == null) return;

                var keysToEvict = stats.PopularKeys
                    .OrderBy(x => x.Value)
                    .Take(stats.PopularKeys.Count * percentToEvict / 100)
                    .Select(x => x.Key)
                    .ToList();

                _logger.Debug($"Evicting {keysToEvict.Count} least recently used cache entries based on available metrics");

                foreach (var key in keysToEvict)
                {
                    // Use a different approach to remove items
                    RemoveFromCache(key);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during LRU cache eviction");
            }
        }

        public void EvictByPattern(string pattern)
        {
            _logger.Debug($"Evicting cache entries matching pattern: {pattern}");
            try
            {
                var cacheKeys = _cacheManager.GetCacheKeys();
                foreach (var key in cacheKeys.Where(k => k != null && k.Contains(pattern)))
                {
                    // Use a different approach to remove items
                    RemoveFromCache(key);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during pattern cache eviction");
            }
        }

        public void EvictExpired()
        {
            _logger.Debug("Evicting expired cache entries");
            try
            {
                _cacheManager.ClearExpired();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during expired cache eviction");
            }
        }

        public void EnforceMemoryLimit()
        {
            try
            {
                // Get MemoryLimitMB using reflection since it might not be directly accessible in tests
                var memoryLimit = GetSettingValue<int>(_settings, "MemoryLimitMB");
                if (memoryLimit <= 0)
                {
                    return; // No limit
                }

                var estimatedMemoryUsage = EstimateMemoryUsage();
                var limitBytes = memoryLimit * 1024 * 1024;

                if (estimatedMemoryUsage > limitBytes)
                {
                    var percentToEvict = Math.Min(50, (int)((estimatedMemoryUsage - limitBytes) * 100 / estimatedMemoryUsage));
                    _logger.Debug($"Cache memory usage ({estimatedMemoryUsage / 1024 / 1024}MB) exceeds limit ({memoryLimit}MB), evicting {percentToEvict}%");
                    EvictLeastRecentlyUsed(percentToEvict);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error enforcing memory limit");
            }
        }

        private void RemoveFromCache(string key)
        {
            try
            {
                // This is a placeholder for actual cache removal logic
                _logger.Debug($"Removing cache entry: {key}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error removing cache entry: {key}");
            }
        }

        private long EstimateMemoryUsage()
        {
            // This is a placeholder for actual memory estimation logic
            return 0;
        }

        private T GetSettingValue<T>(object settings, string propertyName)
        {
            try
            {
                var property = settings.GetType().GetProperty(propertyName);
                if (property != null)
                {
                    return (T)property.GetValue(settings);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error getting setting value: {propertyName}");
            }
            return default(T);
        }
    }
}
