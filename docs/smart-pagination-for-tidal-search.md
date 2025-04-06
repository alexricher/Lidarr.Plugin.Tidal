# Smart Pagination Strategy for Tidal Search

## Problem Statement

The Tidal plugin currently limits search results to 3 pages (approximately 300 results) per search. While this is efficient for API usage and sufficient for most common searches, it can potentially miss relevant content when:

1. Searching for artists with extensive catalogs
2. Using common search terms that yield many results
3. Looking for obscure or rare releases that might be ranked lower in results
4. Attempting to build a comprehensive music library

## Proposed Solution: Smart Pagination

Rather than using a fixed page limit or retrieving all possible results, we can implement an adaptive approach that continues pagination only when necessary, using intelligent heuristics to determine when to stop searching.

## Core Principles

1. **Start conservatively** - Begin with 3 pages to avoid unnecessary API calls
2. **Analyze result quality** - Evaluate whether the initial results satisfy the search intent
3. **Progressive fetching** - Continue fetching only when there's a high probability of finding valuable additional content
4. **Contextual awareness** - Adapt search depth based on the type of search and artist profile

## Implementation Strategy

### 1. Search Context Classification

First, classify the search context to determine appropriate pagination strategy:

```csharp
public enum SearchIntent
{
    SpecificAlbum,         // Looking for a specific album
    ArtistDiscography,     // Looking for an artist's complete works
    DiscoveryMode,         // Open-ended browsing
    TrackSearch,           // Looking for specific tracks
    GenreExploration       // Exploring a genre
}
```

Determine search intent through:
- Explicit user selection
- Analysis of search query structure (e.g., "Artist - Album" format indicates SpecificAlbum)
- Presence of qualifiers (year, album type, etc.)

### 2. Result Quality Assessment

After the initial 3 pages, evaluate result quality:

```csharp
public class SearchResultMetrics
{
    public int TotalResultsFound { get; set; }
    public int ExactArtistMatches { get; set; }
    public int ExactAlbumMatches { get; set; }
    public int HighConfidenceMatches { get; set; } // >90% match score
    public int RecentReleases { get; set; } // Last 5 years
    public int AlbumCount { get; set; }
    public int SingleCount { get; set; }
    public int CompilationCount { get; set; }
    public int AppearancesCount { get; set; }
    public bool FoundTargetItem { get; set; } // For specific searches
    public Dictionary<int, int> ResultsPerPage { get; set; } // Track diminishing returns
    public Dictionary<string, int> AlbumTypes { get; set; } // Distribution of album types
}
```

### 3. Pagination Decision Logic

Based on search intent and result metrics, determine whether to continue pagination:

```csharp
public bool ShouldContinuePagination(
    SearchIntent intent, 
    SearchResultMetrics metrics,
    int currentPage,
    TidalSearchOptions options)
{
    // Base case: Never exceed absolute maximum set by user or system
    if (currentPage >= options.AbsoluteMaxPages)
        return false;
        
    switch (intent)
    {
        case SearchIntent.SpecificAlbum:
            // Continue if we haven't found the target album yet,
            // but with diminishing persistence
            return !metrics.FoundTargetItem && 
                   currentPage < Math.Min(7, options.MaxPages);
                   
        case SearchIntent.ArtistDiscography:
            // For artists, consider their profile
            if (IsHighProfileArtist(metrics.ExactArtistMatches))
            {
                // For major artists, get more comprehensive results
                // Stop when we see diminishing returns
                var currentPageResults = metrics.ResultsPerPage[currentPage];
                var previousPageResults = metrics.ResultsPerPage[currentPage - 1];
                var twoPagesBefore = currentPage > 1 ? metrics.ResultsPerPage[currentPage - 2] : 100;
                
                // Continue if we're still finding significant content (>25% of first page yield)
                return currentPageResults > (metrics.ResultsPerPage[0] * 0.25) &&
                       currentPage < Math.Min(8, options.MaxPages);
            }
            else
            {
                // For lesser-known artists, be less aggressive
                return currentPage < Math.Min(5, options.MaxPages) && 
                       metrics.ResultsPerPage[currentPage] > 10;
            }
            
        case SearchIntent.DiscoveryMode:
            // In discovery mode, go deeper but respect diminishing returns
            return metrics.ResultsPerPage[currentPage] > 15 && 
                   currentPage < Math.Min(10, options.MaxPages);
                   
        // etc. for other intents
    }
    
    return false;
}
```

### 4. Artist Profile Analysis

For artist-based searches, analyze the artist's profile to determine appropriate search depth:

```csharp
public class ArtistProfile
{
    public string Name { get; set; }
    public int EstimatedCatalogSize { get; set; }
    public DateTime FirstReleaseYear { get; set; }
    public DateTime LastReleaseYear { get; set; }
    public bool IsProlific => EstimatedCatalogSize > 25;
    public int CareerSpan => LastReleaseYear.Year - FirstReleaseYear.Year;
    public bool HasLongCareer => CareerSpan > 15;
    
    public int RecommendedSearchDepth 
    { 
        get
        {
            if (IsProlific && HasLongCareer)
                return 8; // Deep search for prolific artists with long careers
            else if (IsProlific)
                return 6; // Moderately deep for prolific artists
            else if (HasLongCareer)
                return 5; // Moderately deep for long-career artists
            else
                return 4; // Standard depth for most artists
        }
    }
}
```

### 5. Genre and Catalog Intelligence

Utilize knowledge about music genres and typical catalog structures:

```csharp
// Some genres have more prolific release patterns
private readonly Dictionary<string, int> _genreSearchDepthModifiers = new()
{
    { "classical", 3 },     // Classical composers often have massive catalogs
    { "jazz", 2 },          // Jazz artists often have many live albums and sessions
    { "electronic", 1 },    // Electronic artists often have many singles and EPs
    { "ambient", 1 },       // Ambient artists often have many similar releases
    { "soundtrack", 1 }     // Soundtrack composers often have many credits
};

// Artist archetypes
private readonly Dictionary<string, ArtistArchetype> _knownArtistArchetypes = new()
{
    // Extremely prolific artists known for massive catalogs
    { "john zorn", new ArtistArchetype { SearchDepthMultiplier = 3.0 } },
    { "buckethead", new ArtistArchetype { SearchDepthMultiplier = 3.0 } },
    { "merzbow", new ArtistArchetype { SearchDepthMultiplier = 3.0 } },
    
    // Artists with many side projects and collaborations
    { "mike patton", new ArtistArchetype { SearchDepthMultiplier = 2.0 } },
    { "robert fripp", new ArtistArchetype { SearchDepthMultiplier = 2.0 } },
    
    // Artists commonly featured as guests
    { "nile rodgers", new ArtistArchetype { SearchDepthMultiplier = 1.5, ConsiderAppearances = true } }
};
```

### 6. Implementation in Search Flow

Integrate into the search flow:

```csharp
public async Task<List<Release>> SearchTidalWithSmartPagination(
    string query, 
    SearchIntent intent,
    TidalSearchOptions options)
{
    int currentPage = 1;
    var allResults = new List<Release>();
    var metrics = new SearchResultMetrics();
    metrics.ResultsPerPage = new Dictionary<int, int>();
    
    // Determine context for search
    var artistProfile = await AnalyzeArtistProfileFromQuery(query);
    var adjustedMaxPages = DetermineInitialMaxPages(intent, artistProfile, options);
    
    // Initial search (always do at least 3 pages)
    while (currentPage <= Math.Min(3, adjustedMaxPages))
    {
        var pageResults = await SearchTidalPage(query, currentPage, options);
        allResults.AddRange(pageResults);
        
        // Update metrics
        metrics.ResultsPerPage[currentPage] = pageResults.Count;
        UpdateSearchMetrics(metrics, pageResults, intent);
        
        if (pageResults.Count == 0)
            break; // No more results
            
        currentPage++;
    }
    
    // Evaluate if we should continue searching
    while (ShouldContinuePagination(intent, metrics, currentPage, options))
    {
        // Log that we're doing extended searching
        _logger.Debug($"Smart pagination: Fetching additional page {currentPage} for '{query}' (intent: {intent})");
        
        var pageResults = await SearchTidalPage(query, currentPage, options);
        allResults.AddRange(pageResults);
        
        // Update metrics
        metrics.ResultsPerPage[currentPage] = pageResults.Count;
        UpdateSearchMetrics(metrics, pageResults, intent);
        
        if (pageResults.Count == 0)
            break; // No more results
            
        currentPage++;
        
        // Apply small delay to avoid API rate limits
        await Task.Delay(200);
    }
    
    // Log search effectiveness
    _logger.Debug($"Smart pagination: Retrieved {currentPage} pages for '{query}', found {allResults.Count} total results");
    
    return allResults;
}
```

### 7. Learning from Behavior

Implement a feedback mechanism to learn from user behavior:

```csharp
public class SmartPaginationLearningService
{
    private readonly Dictionary<string, SearchEffectivenessMetrics> _artistSearchEffectiveness = 
        new Dictionary<string, SearchEffectivenessMetrics>();
    
    // Record when a user manually searches for an artist after an 
    // automatic search didn't find what they wanted
    public void RecordMissedResult(string originalQuery, Release selectedRelease, int originalPageCount)
    {
        var artistName = selectedRelease.ArtistName.ToLowerInvariant();
        
        if (!_artistSearchEffectiveness.ContainsKey(artistName))
            _artistSearchEffectiveness[artistName] = new SearchEffectivenessMetrics();
            
        _artistSearchEffectiveness[artistName].MissedResults++;
        _artistSearchEffectiveness[artistName].LastMissedDate = DateTime.UtcNow;
        
        // If we're consistently missing results for this artist, recommend deeper searches
        if (_artistSearchEffectiveness[artistName].MissedResults > 3)
            _artistSearchEffectiveness[artistName].RecommendedExtraPages = 
                Math.Min(10, _artistSearchEffectiveness[artistName].RecommendedExtraPages + 1);
    }
    
    public int GetRecommendedExtraPagesForArtist(string artistName)
    {
        artistName = artistName.ToLowerInvariant();
        if (_artistSearchEffectiveness.ContainsKey(artistName))
            return _artistSearchEffectiveness[artistName].RecommendedExtraPages;
            
        return 0;
    }
}
```

## Performance Considerations

1. **API call efficiency** - Implement proper delays between extra page requests to avoid rate limiting
2. **Result caching** - Cache search results to reduce duplicate API calls for common searches
3. **Asynchronous loading** - Load initial results quickly, then fetch additional pages asynchronously
4. **Metadata enrichment** - Use available metadata to make smarter decisions about when to stop searching

## User Interface

Enable user configuration via TidalSettings.cs:

```csharp
// In TidalSettings.cs

[FieldDefinition(100, Label = "Smart Pagination", HelpText = "Automatically determine how many search pages to retrieve based on search context", Type = FieldType.Checkbox, Section = "Advanced Settings", Advanced = true)]
public bool EnableSmartPagination { get; set; } = true;

[FieldDefinition(101, Label = "Max Search Pages", HelpText = "Maximum number of pages to retrieve, even with smart pagination", Type = FieldType.Number, Section = "Advanced Settings", Advanced = true)]
public int MaxSearchPages { get; set; } = 10;

[FieldDefinition(102, Label = "Search Thoroughness", HelpText = "How thorough searches should be", Type = FieldType.Select, SelectOptions = typeof(SearchThoroughness), Section = "Advanced Settings", Advanced = true)]
public int SearchThoroughness { get; set; } = (int)SearchThoroughness.Balanced;

// Define thoroughness levels
public enum SearchThoroughness
{
    Efficient = 0,    // Minimize API calls, good for most users (max 5 pages)
    Balanced = 1,     // Default option, good balance (max 8 pages)
    Thorough = 2,     // More thorough for enthusiasts (max 12 pages)
    Completionist = 3 // Maximum thoroughness for collectors (max 20 pages)
}
```

## Curator's Perspective

As a music enthusiast with experience curating over 1 million albums, I've found that search thoroughness is essential for several scenarios:

1. **Obscure recordings and releases** - Many important releases appear far down in search results, especially:
   - Limited editions
   - Regional releases
   - Live recordings
   - Early demos and rarities
   - Guest appearances and collaborations

2. **Prolific artists** - Artists like John Zorn, Buckethead, or Merzbow have hundreds of releases that would never fit in 3 pages of results

3. **Common names** - Artists with common names (like "Lamb") require deeper searching to find the specific artist among many similarly named ones

4. **Comprehensive collections** - Building a complete discography often requires finding releases that aren't prioritized by popularity algorithms

5. **Classical music** - Classical composers often have thousands of recordings of their works by different performers

The smart pagination approach mimics how an experienced curator searches: start with basic results, then dig deeper strategically based on what's missing. This ensures thoroughness while respecting API limits and performance.

## Future Enhancements

1. **User-driven learning** - Track which artists/genres typically need deeper searching based on user behavior
2. **Discography completion analysis** - Analyze gaps in artist discographies to target deeper searching
3. **Search term refinement** - Suggest better search terms when smart pagination isn't finding expected results
4. **Release timeline visualization** - Show a timeline of found releases to help identify missing periods
5. **Artist relationship mapping** - Use knowledge of collaborators and projects to improve search depth decisions

## Conclusion

Smart pagination balances efficiency with thoroughness, ensuring that users find what they're looking for without unnecessarily taxing the Tidal API. By adapting search depth based on context, user needs, and result quality, we can provide a superior experience for casual users and serious collectors alike.

This approach mirrors how an experienced music curator would search: start with the basics, then strategically deepen the search only where needed. The result is a more complete, yet efficient, music discovery experience. 