using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Indexers.Tidal.Models
{
    /// <summary>
    /// Enum representing the detected intent of a search query
    /// </summary>
    public enum SearchIntent
    {
        /// <summary>
        /// Unknown search intent, default fallback
        /// </summary>
        Unknown = 0,
        
        /// <summary>
        /// Searching for a specific album by a specific artist
        /// </summary>
        SpecificAlbum = 1,
        
        /// <summary>
        /// Searching for all albums by a specific artist
        /// </summary>
        ArtistDiscography = 2,
        
        /// <summary>
        /// Exploration search within a genre or style
        /// </summary>
        GenreExploration = 3,
        
        /// <summary>
        /// Discovery mode searching for general music
        /// </summary>
        DiscoveryMode = 4,
        
        /// <summary>
        /// Searching for a specific album without artist context
        /// </summary>
        AlbumSearch = 5
    }

    /// <summary>
    /// Contains metrics about search results to help make smart pagination decisions
    /// </summary>
    public class SearchResultMetrics
    {
        /// <summary>
        /// Total number of results found across all pages
        /// </summary>
        public int TotalResultsFound { get; set; }
        
        /// <summary>
        /// Number of exact artist matches found
        /// </summary>
        public int ExactArtistMatches { get; set; }
        
        /// <summary>
        /// Number of exact album matches found
        /// </summary>
        public int ExactAlbumMatches { get; set; }
        
        /// <summary>
        /// Number of results with high confidence match scores (>90%)
        /// </summary>
        public int HighConfidenceMatches { get; set; }
        
        /// <summary>
        /// Number of recent releases found (last 5 years)
        /// </summary>
        public int RecentReleases { get; set; }
        
        /// <summary>
        /// Number of albums found
        /// </summary>
        public int AlbumCount { get; set; }
        
        /// <summary>
        /// Number of singles found
        /// </summary>
        public int SingleCount { get; set; }
        
        /// <summary>
        /// Number of compilations found
        /// </summary>
        public int CompilationCount { get; set; }
        
        /// <summary>
        /// Number of guest appearances found
        /// </summary>
        public int AppearancesCount { get; set; }
        
        /// <summary>
        /// Whether the target item was found (for specific searches)
        /// </summary>
        public bool FoundTargetItem { get; set; }
        
        /// <summary>
        /// Number of results per page to track diminishing returns
        /// </summary>
        public Dictionary<int, int> ResultsPerPage { get; set; } = new Dictionary<int, int>();
        
        /// <summary>
        /// Distribution of album types found
        /// </summary>
        public Dictionary<string, int> AlbumTypes { get; set; } = new Dictionary<string, int>();
    }
} 