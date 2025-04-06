using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Indexers.Tidal.Models
{
    /// <summary>
    /// Class that manages the state for Tidal searches to enable smarter pagination across requests
    /// </summary>
    public static class TidalSearchState
    {
        private static readonly ConcurrentDictionary<string, SearchStateInfo> _activeSearches = 
            new ConcurrentDictionary<string, SearchStateInfo>();
            
        private static readonly Logger _logger = LogManager.GetLogger("TidalSearchState");
        private static readonly Timer _cleanupTimer;
        private static readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(10);
        private static readonly object _cleanupLock = new object();
        private static bool _isCleaningUp = false;
        
        // Static constructor to initialize the cleanup timer
        static TidalSearchState()
        {
            // Start the timer to automatically clean up old searches every 10 minutes
            _cleanupTimer = new Timer(CleanupTimerCallback, null, _cleanupInterval, _cleanupInterval);
            _logger.Debug("🧹 TidalSearchState cleanup timer initialized, interval: {0} minutes", _cleanupInterval.TotalMinutes);
        }

        // Timer callback that triggers cleanup
        private static void CleanupTimerCallback(object state)
        {
            // Ensure we don't run multiple cleanups concurrently
            lock (_cleanupLock)
            {
                if (!_isCleaningUp)
                {
                    _isCleaningUp = true;
                    try
                    {
                        CleanupOldSearches();
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error during automatic search state cleanup");
                    }
                    finally
                    {
                        _isCleaningUp = false;
                    }
                }
            }
        }
        
        /// <summary>
        /// Registers a new search and returns a unique search ID
        /// </summary>
        /// <param name="query">The search query</param>
        /// <param name="intent">The detected search intent</param>
        /// <param name="maxPages">Maximum number of pages for this search</param>
        /// <returns>A unique search ID</returns>
        public static string RegisterSearch(string query, SearchIntent intent, int maxPages)
        {
            // Perform cleanup if the collection is getting large
            if (_activeSearches.Count > 200)
            {
                CleanupOldSearches(5); // More aggressive cleanup for large collections
            }
            
            var searchId = GenerateSearchId(query);
            var searchInfo = new SearchStateInfo
            {
                Query = query,
                Intent = intent,
                MaxPages = maxPages,
                StartTime = DateTime.UtcNow,
                SearchId = searchId,
                ResultMetrics = new SearchResultMetrics()
            };
            
            _activeSearches.TryAdd(searchId, searchInfo);
            _logger.Debug($"✨ Registered new search: {searchId} for '{query}' ({intent}, max {maxPages} pages) - current active: {_activeSearches.Count}");
            
            return searchId;
        }
        
        /// <summary>
        /// Updates the metrics for a search after processing a page
        /// </summary>
        /// <param name="searchId">The search ID</param>
        /// <param name="pageNumber">The page number (0-based)</param>
        /// <param name="resultCount">The number of results found on this page</param>
        /// <param name="totalResults">The total number of results reported by the API</param>
        /// <param name="foundExactMatch">Whether an exact match was found</param>
        public static void UpdateSearchMetrics(string searchId, int pageNumber, int resultCount, int totalResults = 0, bool foundExactMatch = false)
        {
            if (!_activeSearches.TryGetValue(searchId, out var searchInfo))
            {
                _logger.Warn($"⚠️ Attempted to update unknown search: {searchId}");
                return;
            }
            
            // Update page result counts
            searchInfo.ResultMetrics.ResultsPerPage[pageNumber] = resultCount;
            
            // Update page processing status
            searchInfo.ProcessedPages.Add(pageNumber);
            
            // Update total results if provided
            if (totalResults > 0)
            {
                searchInfo.ResultMetrics.TotalResultsFound = totalResults;
            }
            
            // Update exact match status
            if (foundExactMatch)
            {
                searchInfo.ResultMetrics.FoundTargetItem = true;
            }
            
            // If no results found, mark to skip remaining pages
            if (resultCount == 0)
            {
                searchInfo.SkipRemainingPages = true;
                _logger.Debug($"📉 No results found for page {pageNumber+1} of search {searchId}, will skip remaining pages");
            }
            
            // Calculate diminishing returns
            if (pageNumber > 0 && searchInfo.ResultMetrics.ResultsPerPage.ContainsKey(0) && 
                searchInfo.ResultMetrics.ResultsPerPage[0] > 0)
            {
                double firstPageCount = searchInfo.ResultMetrics.ResultsPerPage[0];
                double currentPageCount = resultCount;
                double ratio = currentPageCount / firstPageCount;
                
                // If we're getting less than 10% of the first page's results, skip remaining pages
                if (ratio < 0.1)
                {
                    searchInfo.SkipRemainingPages = true;
                    _logger.Debug($"📉 Diminishing returns detected for search {searchId}: page {pageNumber+1} has {ratio:P0} of first page's results");
                }
            }
            
            _logger.Debug($"📊 Updated metrics for search {searchId}, page {pageNumber+1}: {resultCount} results");
        }
        
        /// <summary>
        /// Checks if processing should be skipped for a given page
        /// </summary>
        /// <param name="searchId">The search ID</param>
        /// <param name="pageNumber">The page number (0-based)</param>
        /// <returns>True if processing should be skipped</returns>
        public static bool ShouldSkipProcessing(string searchId, int pageNumber)
        {
            if (!_activeSearches.TryGetValue(searchId, out var searchInfo))
            {
                _logger.Warn($"⚠️ Checked unknown search: {searchId}");
                return false;
            }
            
            // Skip if already processed
            if (searchInfo.ProcessedPages.Contains(pageNumber))
            {
                return true;
            }
            
            // Skip if all remaining pages should be skipped
            if (searchInfo.SkipRemainingPages && pageNumber > (searchInfo.ProcessedPages.Count > 0 ? searchInfo.ProcessedPages.Max() : -1))
            {
                _logger.Debug($"⏭️ Skipping page {pageNumber+1} for search {searchId} due to diminishing returns");
                return true;
            }
            
            // Skip if exact match already found and intent is SpecificAlbum
            if (searchInfo.ResultMetrics.FoundTargetItem && searchInfo.Intent == SearchIntent.SpecificAlbum)
            {
                _logger.Debug($"✅ Skipping page {pageNumber+1} for search {searchId} as exact match was already found");
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Gets the search info for a given search ID
        /// </summary>
        /// <param name="searchId">The search ID</param>
        /// <returns>The search info, or null if not found</returns>
        public static SearchStateInfo GetSearchInfo(string searchId)
        {
            _activeSearches.TryGetValue(searchId, out var searchInfo);
            return searchInfo;
        }
        
        /// <summary>
        /// Completes a search and removes it from the active searches
        /// </summary>
        /// <param name="searchId">The search ID</param>
        public static void CompleteSearch(string searchId)
        {
            if (_activeSearches.TryRemove(searchId, out var searchInfo))
            {
                var duration = DateTime.UtcNow - searchInfo.StartTime;
                _logger.Debug($"🏁 Completed search {searchId} for '{searchInfo.Query}' in {duration.TotalSeconds:F1}s - remaining active: {_activeSearches.Count}");
            }
        }
        
        /// <summary>
        /// Cleans up old searches that haven't been completed
        /// </summary>
        /// <param name="maxAgeMinutes">Maximum age in minutes before a search is removed</param>
        public static void CleanupOldSearches(int maxAgeMinutes = 30)
        {
            try
            {
                var cutoffTime = DateTime.UtcNow.AddMinutes(-maxAgeMinutes);
                var keysToRemove = _activeSearches
                    .Where(kvp => kvp.Value.StartTime < cutoffTime)
                    .Select(kvp => kvp.Key)
                    .ToList();
                
                int removedCount = 0;
                foreach (var key in keysToRemove)
                {
                    if (_activeSearches.TryRemove(key, out var searchInfo))
                    {
                        removedCount++;
                        _logger.Debug($"🧹 Removed stale search {key} for '{searchInfo.Query}' (age: {(DateTime.UtcNow - searchInfo.StartTime).TotalMinutes:F1} minutes)");
                    }
                }
                
                if (removedCount > 0)
                {
                    _logger.Info($"🧹 Cleaned up {removedCount} stale searches, remaining active: {_activeSearches.Count}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error cleaning up old searches");
            }
        }
        
        /// <summary>
        /// Performs a full cleanup of all search state
        /// </summary>
        public static void ClearAllSearches()
        {
            int count = _activeSearches.Count;
            _activeSearches.Clear();
            _logger.Info($"🧹 Cleared {count} searches from TidalSearchState");
        }
        
        /// <summary>
        /// Generates a unique search ID based on the query
        /// </summary>
        private static string GenerateSearchId(string query)
        {
            // Create a stable ID that's unique enough for our purposes
            return $"{query.GetHashCode():X8}-{DateTime.UtcNow.Ticks % 10000:X4}";
        }
        
        /// <summary>
        /// Class to hold the state info for a single search
        /// </summary>
        public class SearchStateInfo
        {
            /// <summary>
            /// The unique ID for this search
            /// </summary>
            public string SearchId { get; set; }
            
            /// <summary>
            /// The search query
            /// </summary>
            public string Query { get; set; }
            
            /// <summary>
            /// The detected search intent
            /// </summary>
            public SearchIntent Intent { get; set; }
            
            /// <summary>
            /// The maximum number of pages to retrieve
            /// </summary>
            public int MaxPages { get; set; }
            
            /// <summary>
            /// The time the search was started
            /// </summary>
            public DateTime StartTime { get; set; }
            
            /// <summary>
            /// The pages that have been processed
            /// </summary>
            public HashSet<int> ProcessedPages { get; set; } = new HashSet<int>();
            
            /// <summary>
            /// Whether to skip remaining pages
            /// </summary>
            public bool SkipRemainingPages { get; set; }
            
            /// <summary>
            /// The metrics gathered during the search
            /// </summary>
            public SearchResultMetrics ResultMetrics { get; set; }
        }
    }
} 