using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Core.Indexers.Tidal.Models;
using Lidarr.Plugin.Tidal.Services.Logging;
using NzbDrone.Plugin.Tidal.Indexers.Tidal;

namespace NzbDrone.Core.Indexers.Tidal.Services
{
    /// <summary>
    /// Service that implements smart pagination for Tidal searches
    /// </summary>
    public interface ISmartPaginationService
    {
        /// <summary>
        /// Determines the search intent based on the query
        /// </summary>
        /// <param name="query">The search query</param>
        /// <returns>The detected search intent</returns>
        SearchIntent DetectSearchIntent(string query);
        
        /// <summary>
        /// Determines if pagination should continue based on metrics and settings
        /// </summary>
        /// <param name="intent">The search intent</param>
        /// <param name="metrics">Metrics collected from results so far</param>
        /// <param name="currentPage">The current page number</param>
        /// <param name="settings">Tidal indexer settings</param>
        /// <returns>True if pagination should continue, false otherwise</returns>
        bool ShouldContinuePagination(SearchIntent intent, SearchResultMetrics metrics, int currentPage, TidalIndexerSettings settings);
        
        /// <summary>
        /// Calculates the absolute maximum pages to retrieve based on search intent and settings
        /// </summary>
        /// <param name="intent">The search intent</param>
        /// <param name="settings">Tidal indexer settings</param>
        /// <returns>The maximum pages to retrieve</returns>
        int CalculateMaxPages(SearchIntent intent, TidalIndexerSettings settings);
        
        /// <summary>
        /// Gets the maximum number of pages to retrieve based on search intent
        /// </summary>
        /// <param name="intent">The search intent</param>
        /// <returns>The maximum number of pages to retrieve</returns>
        int GetMaxPagesForIntent(SearchIntent intent);
        
        /// <summary>
        /// Determines if pagination should continue based on result metrics
        /// </summary>
        /// <param name="metrics">The search result metrics</param>
        /// <param name="currentPage">The current page number (0-based)</param>
        /// <returns>True if pagination should continue, false otherwise</returns>
        bool ShouldContinuePagination(SearchResultMetrics metrics, int currentPage);
    }
    
    /// <summary>
    /// Implements smart pagination logic for Tidal searches
    /// </summary>
    public class SmartPaginationService : ISmartPaginationService
    {
        private readonly Logger _logger;
        
        // Known prolific artists that often need deeper searches
        private static readonly Dictionary<string, double> _prolificArtists = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            // Extremely prolific artists known for massive catalogs
            { "john zorn", 3.0 },
            { "buckethead", 3.0 },
            { "merzbow", 3.0 },
            { "sun ra", 3.0 },
            
            // Artists with many side projects and collaborations
            { "mike patton", 2.0 },
            { "robert fripp", 2.0 },
            { "john mclaughlin", 2.0 },
            { "herbie hancock", 2.0 },
            { "miles davis", 2.0 },
            { "prince", 2.0 },
            
            // Artists commonly featured as guests
            { "nile rodgers", 1.5 }
        };
        
        // Genres that typically have more releases to consider
        private static readonly Dictionary<string, double> _prolificGenres = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "classical", 2.0 },
            { "jazz", 1.5 },
            { "electronic", 1.25 },
            { "ambient", 1.25 },
            { "soundtrack", 1.25 }
        };
        
        // Regex for detecting artist-album patterns
        private static readonly Regex _artistAlbumPattern = new Regex(@"^(.+?)\s+[-–—]\s+(.+)$", RegexOptions.Compiled);
        
        /// <summary>
        /// Initializes a new instance of the SmartPaginationService
        /// </summary>
        /// <param name="logger">The logger to use</param>
        public SmartPaginationService(Logger logger)
        {
            _logger = logger;
        }
        
        /// <summary>
        /// Detects the search intent based on the query
        /// </summary>
        public SearchIntent DetectSearchIntent(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return SearchIntent.DiscoveryMode;
            }
            
            // Check for "Artist - Album" format (indicates specific album search)
            var match = _artistAlbumPattern.Match(query);
            if (match.Success)
            {
                _logger.Debug($"Detected specific album search: '{query}'");
                return SearchIntent.SpecificAlbum;
            }
            
            // Check for genre exploration
            foreach (var genre in _prolificGenres.Keys)
            {
                if (query.Contains(genre, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Debug($"Detected genre exploration: '{query}' (genre: {genre})");
                    return SearchIntent.GenreExploration;
                }
            }
            
            // If only a single word, likely an artist search
            if (!query.Contains(" "))
            {
                _logger.Debug($"Detected artist discography search: '{query}' (single term)");
                return SearchIntent.ArtistDiscography;
            }
            
            // Default to artist discography if we can't determine more specifically
            _logger.Debug($"Defaulting to artist discography for search: '{query}'");
            return SearchIntent.ArtistDiscography;
        }
        
        /// <summary>
        /// Gets the maximum number of pages to retrieve based on search intent
        /// </summary>
        public int GetMaxPagesForIntent(SearchIntent intent)
        {
            // Default to 3 pages if we can't determine
            if (intent == SearchIntent.Unknown)
            {
                return 3;
            }
            
            switch (intent)
            {
                case SearchIntent.SpecificAlbum:
                    return 5; // For specific albums, be thorough but not excessive
                
                case SearchIntent.ArtistDiscography:
                    return 7; // For artist discographies, be more thorough
                
                case SearchIntent.GenreExploration:
                    return 4; // For genre exploration, moderate depth
                
                case SearchIntent.DiscoveryMode:
                    return 2; // For discovery mode, minimal depth
                
                default:
                    return 3; // Default to 3 pages
            }
        }
        
        /// <summary>
        /// Determines if pagination should continue based on result metrics
        /// </summary>
        public bool ShouldContinuePagination(SearchResultMetrics metrics, int currentPage)
        {
            // Check for diminishing returns
            if (metrics.ResultsPerPage.ContainsKey(currentPage) && 
                metrics.ResultsPerPage.ContainsKey(0) && 
                metrics.ResultsPerPage[0] > 0)
            {
                double firstPageCount = metrics.ResultsPerPage[0];
                double currentPageCount = metrics.ResultsPerPage[currentPage];
                double ratio = currentPageCount / firstPageCount;
                
                // If we're getting less than 10% of the first page's results, stop pagination
                if (ratio < 0.1)
                {
                    _logger.Debug($"Diminishing returns detected: page {currentPage+1} has {ratio:P0} of first page's results");
                    return false;
                }
            }
            
            // If no results on current page, stop pagination
            if (metrics.ResultsPerPage.ContainsKey(currentPage) && 
                metrics.ResultsPerPage[currentPage] == 0)
            {
                _logger.Debug($"No results on page {currentPage+1}, stopping pagination");
                return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// Determines if pagination should continue based on metrics and settings
        /// </summary>
        public bool ShouldContinuePagination(SearchIntent intent, SearchResultMetrics metrics, int currentPage, TidalIndexerSettings settings)
        {
            // Check if smart pagination is enabled
            if (!settings.EnableSmartPagination)
            {
                return currentPage < settings.MaxPages;
            }
            
            // Calculate the absolute maximum pages based on settings
            int absoluteMaxPages = CalculateMaxPages(intent, settings);
            
            // Never exceed the absolute maximum
            if (currentPage >= absoluteMaxPages)
            {
                _logger.Debug($"Maximum page limit reached ({currentPage} >= {absoluteMaxPages})");
                return false;
            }
            
            // Check if we've found the target item for specific searches
            if (intent == SearchIntent.SpecificAlbum && metrics.FoundTargetItem)
            {
                _logger.Debug("Target album found, stopping pagination");
                return false;
            }
            
            // Check for diminishing returns
            if (metrics.ResultsPerPage.ContainsKey(currentPage) && 
                metrics.ResultsPerPage.ContainsKey(0) && 
                metrics.ResultsPerPage[0] > 0)
            {
                double firstPageCount = metrics.ResultsPerPage[0];
                double currentPageCount = metrics.ResultsPerPage[currentPage];
                double ratio = currentPageCount / firstPageCount;
                
                // If we're getting less than 10% of the first page's results, stop pagination
                if (ratio < 0.1)
                {
                    _logger.Debug($"Diminishing returns detected: page {currentPage+1} has {ratio:P0} of first page's results");
                    return false;
                }
            }
            
            // Apply intent-specific logic
            switch (intent)
            {
                case SearchIntent.SpecificAlbum:
                    // For specific album searches, be more persistent but still limit to 7 pages
                    return !metrics.FoundTargetItem && currentPage < Math.Min(7, absoluteMaxPages);
                    
                case SearchIntent.ArtistDiscography:
                    // Check for prolific artists
                    string artistName = ExtractArtistName(metrics);
                    double depthMultiplier = GetArtistDepthMultiplier(artistName);
                    
                    if (depthMultiplier > 1.0)
                    {
                        // For known prolific artists, be more thorough
                        int adjustedMax = (int)Math.Ceiling(absoluteMaxPages * depthMultiplier);
                        _logger.Debug($"Prolific artist '{artistName}' detected, using depth multiplier {depthMultiplier}x (max pages: {adjustedMax})");
                        
                        // Check if we're still finding reasonable content
                        double threshold = 0.15; // At least 15% of first page yield
                        if (metrics.ResultsPerPage.ContainsKey(currentPage) && 
                            metrics.ResultsPerPage.ContainsKey(0) && 
                            metrics.ResultsPerPage[0] > 0)
                        {
                            double ratio = (double)metrics.ResultsPerPage[currentPage] / metrics.ResultsPerPage[0];
                            return currentPage < adjustedMax && ratio > threshold;
                        }
                        
                        return currentPage < adjustedMax;
                    }
                    else
                    {
                        // For regular artists, be less aggressive
                        return currentPage < Math.Min(5, absoluteMaxPages) && 
                              (metrics.ResultsPerPage.ContainsKey(currentPage) ? metrics.ResultsPerPage[currentPage] > 10 : true);
                    }
                    
                case SearchIntent.DiscoveryMode:
                    // For discovery mode, go deeper but respect diminishing returns
                    return metrics.ResultsPerPage.ContainsKey(currentPage) ? 
                          metrics.ResultsPerPage[currentPage] > 15 && currentPage < absoluteMaxPages : 
                          currentPage < 3; // Do at least 3 pages for discovery
                    
                case SearchIntent.GenreExploration:
                    // For genre exploration, be thorough
                    string genre = ExtractGenre(metrics);
                    double genreMultiplier = GetGenreDepthMultiplier(genre);
                    
                    int genreAdjustedMax = (int)Math.Ceiling(absoluteMaxPages * genreMultiplier);
                    _logger.Debug($"Genre '{genre}' detected, using depth multiplier {genreMultiplier}x (max pages: {genreAdjustedMax})");
                    
                    // Check if we're still finding reasonable content
                    return metrics.ResultsPerPage.ContainsKey(currentPage) ? 
                          metrics.ResultsPerPage[currentPage] > 10 && currentPage < genreAdjustedMax : 
                          currentPage < 3; // Do at least 3 pages for genre exploration
                    
                case SearchIntent.AlbumSearch:
                    // For album searches, be less aggressive
                    return currentPage < Math.Min(absoluteMaxPages, 5);
                    
                default:
                    return currentPage < settings.MaxPages;
            }
        }
        
        /// <summary>
        /// Calculates the absolute maximum pages to retrieve based on search intent and settings
        /// </summary>
        public int CalculateMaxPages(SearchIntent intent, TidalIndexerSettings settings)
        {
            // If smart pagination is disabled, use the standard max pages
            if (!settings.EnableSmartPagination)
            {
                return settings.MaxPages;
            }
            
            // Get the base maximum pages from settings
            int maxPages = settings.MaxSearchPages;
            
            // Apply thoroughness adjustment based on settings
            switch (settings.SearchThoroughness)
            {
                case (int)SearchThoroughness.Efficient:
                    maxPages = Math.Min(maxPages, 5);
                    break;
                    
                case (int)SearchThoroughness.Balanced:
                    maxPages = Math.Min(maxPages, 8);
                    break;
                    
                case (int)SearchThoroughness.Thorough:
                    maxPages = Math.Min(maxPages, 12);
                    break;
                    
                case (int)SearchThoroughness.Completionist:
                    maxPages = Math.Min(maxPages, 20);
                    break;
                    
                default:
                    maxPages = Math.Min(maxPages, 8); // Default to Balanced
                    break;
            }
            
            // Apply intent-specific adjustments
            switch (intent)
            {
                case SearchIntent.SpecificAlbum:
                    // Be more focused for specific album searches
                    return Math.Min(maxPages, 7);
                    
                case SearchIntent.ArtistDiscography:
                    // Use full max for artist discography
                    return maxPages;
                    
                case SearchIntent.DiscoveryMode:
                    // Be generous for discovery mode
                    return maxPages;
                    
                case SearchIntent.GenreExploration:
                    // Be thorough for genre exploration
                    return maxPages;
                    
                case SearchIntent.AlbumSearch:
                    // Be more focused for album searches
                    return Math.Min(maxPages, 5);
                    
                default:
                    return maxPages;
            }
        }
        
        #region Helper Methods
        
        /// <summary>
        /// Extracts the artist name from metrics and search results
        /// </summary>
        private string ExtractArtistName(SearchResultMetrics metrics)
        {
            // This is a placeholder - in a real implementation, we'd extract this from search results
            // For now, let's assume we don't have this information
            return string.Empty;
        }
        
        /// <summary>
        /// Gets the depth multiplier for a given artist
        /// </summary>
        private double GetArtistDepthMultiplier(string artistName)
        {
            if (string.IsNullOrWhiteSpace(artistName))
            {
                return 1.0;
            }
            
            if (_prolificArtists.TryGetValue(artistName, out double multiplier))
            {
                return multiplier;
            }
            
            // Check for partial matches
            foreach (var artist in _prolificArtists.Keys)
            {
                if (artistName.Contains(artist, StringComparison.OrdinalIgnoreCase) ||
                    artist.Contains(artistName, StringComparison.OrdinalIgnoreCase))
                {
                    return _prolificArtists[artist];
                }
            }
            
            return 1.0;
        }
        
        /// <summary>
        /// Extracts the genre from metrics and search results
        /// </summary>
        private string ExtractGenre(SearchResultMetrics metrics)
        {
            // This is a placeholder - in a real implementation, we'd extract this from search results
            // For now, let's assume we don't have this information
            return string.Empty;
        }
        
        /// <summary>
        /// Gets the depth multiplier for a given genre
        /// </summary>
        private double GetGenreDepthMultiplier(string genre)
        {
            if (string.IsNullOrWhiteSpace(genre))
            {
                return 1.0;
            }
            
            if (_prolificGenres.TryGetValue(genre, out double multiplier))
            {
                return multiplier;
            }
            
            // Check for partial matches
            foreach (var g in _prolificGenres.Keys)
            {
                if (genre.Contains(g, StringComparison.OrdinalIgnoreCase))
                {
                    return _prolificGenres[g];
                }
            }
            
            return 1.0;
        }
        
        #endregion
    }
} 