using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.Tidal
{
    /// <summary>
    /// A specialized cache for Tidal releases that has a longer expiration time
    /// than the default Lidarr release cache to avoid "Couldn't find requested release in cache" errors.
    /// </summary>
    public interface ITidalReleaseCache
    {
        /// <summary>
        /// Adds a release to the cache with the specified key
        /// </summary>
        /// <param name="key">The unique key for the release</param>
        /// <param name="remoteAlbum">The RemoteAlbum to cache</param>
        void Set(string key, RemoteAlbum remoteAlbum);

        /// <summary>
        /// Finds a release in the cache by its key
        /// </summary>
        /// <param name="key">The unique key for the release</param>
        /// <returns>The RemoteAlbum if found, null otherwise</returns>
        RemoteAlbum Find(string key);

        /// <summary>
        /// Checks if a release exists in the cache
        /// </summary>
        /// <param name="key">The unique key for the release</param>
        /// <returns>True if the release exists in the cache, false otherwise</returns>
        bool Contains(string key);

        /// <summary>
        /// Removes a release from the cache
        /// </summary>
        /// <param name="key">The unique key for the release to remove</param>
        void Remove(string key);

        /// <summary>
        /// Clears all releases from the cache
        /// </summary>
        void Clear();

        /// <summary>
        /// Gets a count of releases in the cache
        /// </summary>
        /// <returns>The number of releases in the cache</returns>
        int Count { get; }
        
        /// <summary>
        /// Attempts to hook into the standard cache lookup by trying to find a matching release
        /// in our extended cache when the standard cache misses
        /// </summary>
        /// <param name="indexerId">The ID of the indexer</param>
        /// <param name="guid">The GUID of the release</param>
        /// <returns>The RemoteAlbum if found, null otherwise</returns>
        RemoteAlbum FindByIndexerIdAndGuid(int indexerId, string guid);
    }

    public class TidalReleaseCache : ITidalReleaseCache
    {
        private readonly ICached<RemoteAlbum> _cache;
        private readonly Logger _logger;
        // Set a longer cache timeout (4 hours) to avoid the "Couldn't find requested release in cache" error
        private readonly TimeSpan _cacheTimeout = TimeSpan.FromHours(4);

        public TidalReleaseCache(ICacheManager cacheManager, Logger logger)
        {
            _logger = logger;
            _cache = cacheManager.GetCache<RemoteAlbum>(GetType(), "tidalReleases");
        }

        public void Set(string key, RemoteAlbum remoteAlbum)
        {
            _logger.Debug("Caching Tidal release with key: {0} for {1}", key, _cacheTimeout);
            _cache.Set(key, remoteAlbum, _cacheTimeout);
        }

        public RemoteAlbum Find(string key)
        {
            var result = _cache.Find(key);
            if (result == null)
            {
                _logger.Debug("Tidal release with key: {0} not found in cache", key);
            }
            else
            {
                _logger.Debug("Found Tidal release with key: {0} in cache", key);
            }
            return result;
        }

        public bool Contains(string key)
        {
            return _cache.Find(key) != null;
        }

        public void Remove(string key)
        {
            _logger.Debug("Removing Tidal release with key: {0} from cache", key);
            _cache.Remove(key);
        }

        public void Clear()
        {
            _logger.Debug("Clearing all Tidal releases from cache");
            _cache.Clear();
        }

        public int Count => _cache.Count;
        
        public RemoteAlbum FindByIndexerIdAndGuid(int indexerId, string guid)
        {
            string cacheKey = $"{indexerId}_{guid}";
            _logger.Debug($"Looking for release with indexerId: {indexerId}, guid: {guid} in extended cache using key: {cacheKey}");
            return Find(cacheKey);
        }
    }
} 