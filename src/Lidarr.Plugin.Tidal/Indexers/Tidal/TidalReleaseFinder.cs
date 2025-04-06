using System;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Exceptions;

namespace NzbDrone.Core.Indexers.Tidal
{
    /// <summary>
    /// Provides a service to find Tidal releases that might have fallen out of the standard cache
    /// but are still in our extended Tidal-specific cache.
    /// </summary>
    public interface ITidalReleaseFinder
    {
        /// <summary>
        /// Attempts to find a release in our extended cache by its key
        /// </summary>
        /// <param name="indexerId">The indexer ID</param>
        /// <param name="guid">The release GUID</param>
        /// <returns>A RemoteAlbum if found in our cache, null otherwise</returns>
        Task<RemoteAlbum> FindReleaseAsync(int indexerId, string guid);
    }
    
    public class TidalReleaseFinder : ITidalReleaseFinder
    {
        private readonly ITidalReleaseCache _releaseCache;
        private readonly Logger _logger;
        
        public TidalReleaseFinder(ITidalReleaseCache releaseCache, Logger logger)
        {
            _releaseCache = releaseCache;
            _logger = logger;
        }
        
        public Task<RemoteAlbum> FindReleaseAsync(int indexerId, string guid)
        {
            try
            {
                string cacheKey = $"{indexerId}_{guid}";
                _logger.Debug($"Looking for release with key: {cacheKey} in extended cache");
                
                var remoteAlbum = _releaseCache.Find(cacheKey);
                
                if (remoteAlbum == null)
                {
                    _logger.Warn($"Release with key: {cacheKey} not found in extended cache");
                    throw new NzbDroneClientException(System.Net.HttpStatusCode.NotFound, 
                        "Couldn't find requested release in extended cache, try searching again");
                }
                
                _logger.Info($"Found release in extended cache: {remoteAlbum.Release.Title}");
                return Task.FromResult(remoteAlbum);
            }
            catch (Exception ex) when (!(ex is NzbDroneClientException))
            {
                _logger.Error(ex, "Error finding release in extended cache");
                throw new NzbDroneClientException(System.Net.HttpStatusCode.InternalServerError, 
                    $"Error finding release: {ex.Message}");
            }
        }
    }
} 