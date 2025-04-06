using System;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Plugin.Tidal.Indexers.Tidal.Extensions
{
    /// <summary>
    /// Extension methods for search criteria classes to standardize property access
    /// and work around interface limitations.
    /// </summary>
    public static class SearchCriteriaExtensions
    {
        /// <summary>
        /// Gets a standardized search query from album search criteria.
        /// </summary>
        /// <param name="criteria">The album search criteria.</param>
        /// <returns>The search query string.</returns>
        public static string GetSearchQuery(this AlbumSearchCriteria criteria)
        {
            if (criteria == null)
            {
                throw new ArgumentNullException(nameof(criteria));
            }

            // First try to use album query as the search term
            if (!string.IsNullOrWhiteSpace(criteria.AlbumQuery))
            {
                return criteria.AlbumQuery;
            }

            // If not available, create a query from artist and album
            if (criteria.Artist != null && !string.IsNullOrWhiteSpace(criteria.Artist.Name))
            {
                var query = criteria.Artist.Name;
                
                if (criteria.Albums != null && criteria.Albums.Count > 0 && !string.IsNullOrWhiteSpace(criteria.Albums[0].Title))
                {
                    query += " " + criteria.Albums[0].Title;
                }
                
                return query;
            }

            // Last resort, use album title if available
            if (criteria.Albums != null && criteria.Albums.Count > 0 && !string.IsNullOrWhiteSpace(criteria.Albums[0].Title))
            {
                return criteria.Albums[0].Title;
            }
            
            // Fallback to empty string if no search terms are available
            return string.Empty;
        }
        
        /// <summary>
        /// Gets a standardized search query from artist search criteria.
        /// </summary>
        /// <param name="criteria">The artist search criteria.</param>
        /// <returns>The search query string.</returns>
        public static string GetSearchQuery(this ArtistSearchCriteria criteria)
        {
            if (criteria == null)
            {
                throw new ArgumentNullException(nameof(criteria));
            }

            // First try to use artist query as the search term
            if (!string.IsNullOrWhiteSpace(criteria.ArtistQuery))
            {
                return criteria.ArtistQuery;
            }

            // If not available, use artist name
            if (criteria.Artist != null && !string.IsNullOrWhiteSpace(criteria.Artist.Name))
            {
                return criteria.Artist.Name;
            }
            
            // Fallback to empty string if no search terms are available
            return string.Empty;
        }
        
        /// <summary>
        /// Gets the artist name from album search criteria if available.
        /// </summary>
        /// <param name="criteria">The album search criteria.</param>
        /// <returns>The artist name, or null if not available.</returns>
        public static string GetArtistName(this AlbumSearchCriteria criteria)
        {
            return criteria?.Artist?.Name;
        }
        
        /// <summary>
        /// Gets the artist name from artist search criteria if available.
        /// </summary>
        /// <param name="criteria">The artist search criteria.</param>
        /// <returns>The artist name, or null if not available.</returns>
        public static string GetArtistName(this ArtistSearchCriteria criteria)
        {
            return criteria?.Artist?.Name;
        }
        
        /// <summary>
        /// Gets the album title from album search criteria if available.
        /// </summary>
        /// <param name="criteria">The album search criteria.</param>
        /// <returns>The album title, or null if not available.</returns>
        public static string GetAlbumTitle(this AlbumSearchCriteria criteria)
        {
            if (criteria?.Albums == null || criteria.Albums.Count == 0)
            {
                return null;
            }
            
            return criteria.Albums[0].Title;
        }
        
        /// <summary>
        /// Checks if this search criteria has an artist specified.
        /// </summary>
        /// <param name="criteria">The search criteria.</param>
        /// <returns>True if an artist is specified, false otherwise.</returns>
        public static bool HasArtist(this AlbumSearchCriteria criteria)
        {
            return criteria?.Artist != null && !string.IsNullOrWhiteSpace(criteria.Artist.Name);
        }
        
        /// <summary>
        /// Checks if this search criteria has an artist specified.
        /// </summary>
        /// <param name="criteria">The search criteria.</param>
        /// <returns>True if an artist is specified, false otherwise.</returns>
        public static bool HasArtist(this ArtistSearchCriteria criteria)
        {
            return criteria?.Artist != null && !string.IsNullOrWhiteSpace(criteria.Artist.Name);
        }
        
        /// <summary>
        /// Checks if this search criteria has an album specified.
        /// </summary>
        /// <param name="criteria">The search criteria.</param>
        /// <returns>True if an album is specified, false otherwise.</returns>
        public static bool HasAlbum(this AlbumSearchCriteria criteria)
        {
            return criteria?.Albums != null && criteria.Albums.Count > 0 && 
                   !string.IsNullOrWhiteSpace(criteria.Albums[0].Title);
        }
    }
} 