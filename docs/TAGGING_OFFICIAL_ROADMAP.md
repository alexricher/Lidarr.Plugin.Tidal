# Official Audio Tagging Implementation Roadmap

## Overview

This document outlines the comprehensive plan for implementing audio file tagging capabilities in the Lidarr.Plugin.Tidal project. The implementation follows a tiered, phased approach that delivers incremental value while addressing critical edge cases and technical challenges.

## Implementation Approach

The tagging system will be built using a three-tiered approach:

1. **Tier 1: Basic Tagging** - Core functionality using TagLib# with Tidal metadata
2. **Tier 2: Enhanced Metadata** - Integration with MusicBrainz for richer metadata
3. **Tier 3: Advanced Identification** - Acoustic fingerprinting for difficult-to-match tracks

This approach allows for:
- Early delivery of valuable functionality
- Progressive enhancement without external dependencies
- User control over advanced features
- Graceful fallbacks when services are unavailable

## Critical Edge Cases and Challenges

### Artist-Related Challenges
- **Various Artists Compilations**: Require special handling with compilation flags
- **Featured Artists**: Need parsing of "feat." mentions in artist strings
- **Artist Name Inconsistencies**: Same artist with different naming conventions
- **Collaborations**: Multiple primary artists with equal billing
- **Classical Music**: Complex artist roles (composer, conductor, performer, orchestra)

### Text and Character Issues
- **Non-Latin Scripts**: Japanese, Korean, Chinese, Arabic, Cyrillic texts
- **Special Characters in Filenames**: Require sanitization for file operations
- **ID3 Tag Encoding**: Must ensure consistent UTF-8 encoding
- **Normalization Forms**: Different Unicode normalization forms (NFC vs NFD)
- **Bi-directional Text**: Proper handling of RTL (right-to-left) scripts like Arabic and Hebrew

### Track Matching Challenges
- **Mismatched Track Counts**: Files vs metadata track count discrepancies
- **Fuzzy Title Matching**: Handling slight differences in track titles
- **Missing Track Numbers**: Dealing with files without embedded track numbers
- **Multi-disc Albums**: Proper disc and track number handling across multiple discs
- **Bonus and Hidden Tracks**: Tracks not listed in official metadata
- **Medleys and Mashups**: Single tracks containing multiple songs
- **Remasters and Re-releases**: Same album with different track counts/lengths

### Technical Challenges
- **API Rate Limiting**: MusicBrainz limits (1 request/second)
- **Service Availability**: Handling network issues and API outages
- **Resource Usage**: Managing performance during batch operations
- **Containerized Environments**: Docker-specific file permission issues
- **Authentication Security**: Secure handling of API keys and credentials
- **Plugin Lifecycle Integration**: Handling Lidarr startup, shutdown, and update events
- **Concurrent Operations**: Managing multiple tagging operations simultaneously

## Implementation Timeline

| Phase | Focus | Timeline | Key Deliverables |
|-------|-------|----------|-----------------|
| 1 | Foundation & Basic Tagging | Weeks 1-5 | Core interfaces, TagLib# integration, track matching |
| 2 | Enhanced Metadata | Weeks 6-9 | MusicBrainz integration, caching, conflict resolution |
| 3 | Advanced Features | Weeks 10-13 | Acoustic fingerprinting, batch processing |
| 4 | Refinement | Weeks 14-16 | Edge case handling, performance optimization |

## Detailed Implementation Plan

### Phase 1: Foundation and Basic Tagging (Weeks 1-5)

#### Milestone 1: Core Infrastructure (Weeks 1-2)

**Task 1.1: Interface Definitions**
- Create `ITaggingService`, `TaggingOptions` and `TaggingResult` classes
- Define clear contracts for all tagging operations

```csharp
public interface ITaggingService
{
    Task<TaggingResult> TagFilesAsync(string albumPath, TaggingOptions options, CancellationToken cancellationToken = default);
    bool SupportsFormat(string fileExtension);
}

public class TaggingOptions
{
    public string AlbumId { get; set; }
    public bool EmbedArtwork { get; set; } = true;
    public bool UseEnhancedMetadata { get; set; } = false;
    public bool UseFingerprinting { get; set; } = false;
    // Additional options
}

public class TaggingResult
{
    public bool Success { get; set; }
    public List<string> ProcessedFiles { get; set; } = new List<string>();
    public List<string> FailedFiles { get; set; } = new List<string>();
    public Dictionary<string, string> Errors { get; set; } = new Dictionary<string, string>();
}
```

**Task 1.2: Audio Format Detection**
- Implement utilities for handling different audio formats
- Create format detection and validation methods
- Add support for less common formats (ALAC, Opus, Ogg Vorbis)

**Task 1.3: Unicode Support**
- Implement proper encoding handling for international metadata
- Address path and filename encoding issues
- Create Unicode normalization utilities
- Add support for bi-directional text

**Task 1.4: Plugin Lifecycle Integration**
- Integrate with Lidarr's plugin lifecycle
- Implement initialization and shutdown handling
- Add event handling for Lidarr status changes
- Create persistent state management

#### Milestone 2: Basic Tidal Metadata Tagging (Weeks 3-5)

**Task 2.1: Metadata Mapping**
- Create mappers between Tidal API models and tagging models
- Implement normalization for consistent metadata
- Add support for genre mapping and normalization

**Task 2.2: Implement TagLib# Integration**
- Add NuGet package reference to TagLib#
- Create wrapper classes for testing and abstraction
- Implement format-specific tag writing strategies
- Add support for album art embedding

**Task 2.3: Robust Track Matching**
- Implement pattern-based matching for filenames
- Create fuzzy matching for titles with Levenshtein distance
- Handle track number extraction and detection
- Implement multi-disc album support

```csharp
public class TrackMatcher
{
    public TrackMetadata MatchFileToTrack(string filePath, TaggingMetadata metadata)
    {
        // Try track number matching first
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var trackNumberMatch = Regex.Match(fileName, @"^(\d+)");
        
        if (trackNumberMatch.Success && int.TryParse(trackNumberMatch.Groups[1].Value, out var trackNumber))
        {
            var track = metadata.Tracks.FirstOrDefault(t => t.TrackNumber == trackNumber);
            if (track != null)
                return track;
        }
        
        // Check for disc-track number pattern (e.g., 1-01, disc1/01)
        var discTrackMatch = Regex.Match(fileName, @"(?:^|\D)(\d+)[_\-\s.]?(\d{2})(?:\D|$)");
        if (discTrackMatch.Success)
        {
            int.TryParse(discTrackMatch.Groups[1].Value, out var discNumber);
            int.TryParse(discTrackMatch.Groups[2].Value, out var trackNum);
            
            var track = metadata.Tracks.FirstOrDefault(t => 
                t.DiscNumber == discNumber && t.TrackNumber == trackNum);
                
            if (track != null)
                return track;
        }
        
        // Fall back to fuzzy title matching if needed
        return FuzzyMatchTrack(fileName, metadata);
    }
    
    private TrackMetadata FuzzyMatchTrack(string fileName, TaggingMetadata metadata)
    {
        // Implementation that uses normalized string comparison and similarity scoring
        // Minimum threshold for match confidence
        // Returns best match above threshold or null if no good match
    }
}
```

**Task 2.4: Special Format Handling**
- Implement FLAC-specific tagging enhancements
- Add MP3 ID3v2 version handling
- Support M4A/AAC tagging specifics
- Implement Ogg Vorbis and Opus tag handling

**Task 2.5: Configuration and Settings**
- Add user-configurable options for tagging
- Implement configuration UI in Lidarr settings
- Create defaults for common scenarios
- Add import/export of configurations

**Task 2.6: Integration and Testing**
- Integrate with download client
- Implement comprehensive test suite for basic tagging
- Create end-to-end integration tests
- Add performance benchmarks

### Phase 2: Enhanced Metadata with MusicBrainz (Weeks 6-9)

#### Milestone 3: MusicBrainz Integration (Weeks 6-7)

**Task 3.1: MusicBrainz Client Implementation**
- Add MetaBrainz.MusicBrainz NuGet package
- Create client interface and implementation
- Implement proper rate limiting and error handling
- Add secure API key storage

```csharp
public class MusicBrainzRateLimiter
{
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1);
    private DateTime _lastRequestTime = DateTime.MinValue;
    private readonly TimeSpan _minimumInterval = TimeSpan.FromSeconds(1.1); // Slightly over 1 sec

    public async Task WaitForNextRequestSlotAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            var timeSinceLastRequest = DateTime.UtcNow - _lastRequestTime;
            if (timeSinceLastRequest < _minimumInterval)
            {
                var delayTime = _minimumInterval - timeSinceLastRequest;
                await Task.Delay(delayTime);
            }
            _lastRequestTime = DateTime.UtcNow;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
```

**Task 3.2: Circuit Breaker Implementation**
- Create circuit breaker for fault tolerance
- Implement exponential backoff for retries
- Add graceful fallbacks to Tidal-only metadata
- Implement health monitoring and recovery

**Task 3.3: MusicBrainz Release Matching**
- Implement album matching algorithms
- Create confidence scoring for matches
- Support search by various criteria (artist, album, tracks)
- Add support for release groups and release variants

**Task 3.4: Security and Privacy Considerations**
- Implement secure handling of API credentials
- Add data minimization for API requests
- Create audit logging for API interactions
- Implement privacy-focused API usage patterns

#### Milestone 4: Metadata Enhancement and Conflict Resolution (Weeks 8-9)

**Task 4.1: Metadata Merging System**
- Create intelligence for combining Tidal and MusicBrainz data
- Implement precedence rules for conflicting data
- Support user preferences for data sources
- Add field-level merge capabilities

```csharp
public class MetadataEnhancer
{
    public enum MetadataPrecedence
    {
        PreferTidal,      // Tidal data takes precedence
        PreferMusicBrainz, // MusicBrainz data takes precedence
        MergeWithRules,   // Use rules to decide field by field
        HighestConfidence  // Use data with highest confidence score
    }
    
    public async Task<TaggingMetadata> EnhanceMetadataAsync(
        TaggingMetadata tidalMetadata, 
        MetadataPrecedence precedence,
        CancellationToken cancellationToken = default)
    {
        // Implementation that enhances Tidal metadata with MusicBrainz data
        // Following the specified precedence rules
    }
    
    private AlbumMetadata MergeAlbumMetadata(AlbumMetadata tidalAlbum, MusicBrainzRelease mbRelease, MetadataPrecedence precedence)
    {
        // Intelligent merging based on precedence and data quality
    }
}
```

**Task 4.2: Persistent Caching System**
- Implement caching for MusicBrainz queries
- Create cache invalidation strategies
- Add cache management utilities
- Implement cache compression for large datasets

```csharp
public class MetadataCache
{
    public async Task<T> GetOrFetchAsync<T>(string key, Func<Task<T>> fetchFunc, TimeSpan cacheDuration)
    {
        // Implementation that checks cache first, then fetches if needed
        // Stores result in cache for future use
    }
    
    public void InvalidateCache(string keyPattern = null)
    {
        // Invalidate entire cache or by key pattern
    }
    
    public Task CompactCacheAsync()
    {
        // Remove expired or rarely used entries
        // Optimize storage usage
    }
}
```

**Task 4.3: Artist Handling Improvements**
- Add compilation detection and handling
- Implement featured artist parsing
- Create artist name normalization utilities
- Support classical music specific roles

**Task 4.4: Testing and Documentation**
- Create comprehensive tests for MusicBrainz integration
- Document rate limiting and caching behavior
- Add troubleshooting guide for API issues
- Implement integration tests with mock API responses

### Phase 3: Advanced Features (Weeks 10-13)

#### Milestone 5: Acoustic Fingerprinting (Weeks 10-11)

**Task 5.1: AcoustID Integration**
- Add AcoustID.NET NuGet package
- Create client interface and implementation
- Handle Chromaprint native library dependency
- Implement platform-specific native library loading

**Task 5.2: Selective Fingerprinting**
- Implement strategy for when to use fingerprinting
- Add confidence thresholds for matching
- Create fingerprint caching system
- Implement partial file fingerprinting for efficiency

**Task 5.3: MusicBrainz Recording Lookup**
- Implement lookup from fingerprint to MusicBrainz
- Create match validation system
- Add detailed fingerprint match logging
- Implement confidence scoring for acoustic matches

**Task 5.4: API Security and Rate Management**
- Implement API key rotation and security
- Create adaptive rate limiting based on service health
- Add quota management for shared resources
- Implement usage analytics and monitoring

#### Milestone 6: Advanced Workflow Optimizations (Weeks 12-13)

**Task 6.1: Batch Processing Implementation**
- Create chunked processing system for large libraries
- Implement progress reporting
- Add cancellation support
- Create parallelization strategies for multicore systems

```csharp
public async Task ProcessInBatchesAsync<T>(
    IEnumerable<T> items,
    Func<T, Task> processor,
    int batchSize = 10,
    int maxParallelism = 4,
    IProgress<double> progress = null,
    CancellationToken cancellationToken = default)
{
    // Implementation that processes items in batches with controlled parallelism
    // Reports progress and supports cancellation
    
    var itemsList = items.ToList();
    var totalItems = itemsList.Count;
    var processedItems = 0;
    
    for (int i = 0; i < totalItems; i += batchSize)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        var batch = itemsList.Skip(i).Take(batchSize).ToList();
        
        // Process batch with limited parallelism
        using var semaphore = new SemaphoreSlim(maxParallelism);
        var tasks = batch.Select(async item => {
            await semaphore.WaitAsync(cancellationToken);
            try {
                await processor(item);
            }
            finally {
                semaphore.Release();
            }
        });
        
        await Task.WhenAll(tasks);
        
        // Update progress
        processedItems += batch.Count;
        progress?.Report((double)processedItems / totalItems);
    }
}
```

**Task 6.2: Progressive Enhancement**
- Implement two-phase tagging (fast basic, then enhanced)
- Create background processing for advanced features
- Add event-based progress notification
- Implement user-facing status updates

**Task 6.3: Tagging Reports and Analytics**
- Create detailed tagging reports
- Implement match confidence logging
- Add statistical analysis for match quality
- Create user-friendly report visualizations

**Task 6.4: Concurrent Operations Management**
- Implement thread-safe operation queuing
- Create resource management for concurrent operations
- Add priority-based scheduling
- Implement adaptive concurrency based on system load

### Phase 4: Refinement and Edge Cases (Weeks 14-16)

#### Milestone 7: Edge Case Handling (Weeks 14-15)

**Task 7.1: Various Artists and Compilations**
- Finalize compilation flag handling
- Implement special logic for soundtrack albums
- Add support for multiple album artists
- Create detection for hidden compilations

**Task 7.2: Character Encoding and International Support**
- Ensure proper handling of all character sets
- Add comprehensive tests for international metadata
- Implement filename sanitization and normalization
- Create RTL text visualization support in reports

**Task 7.3: Container-Specific Optimizations**
- Add Docker-aware file operations
- Implement permission handling for Linux containers
- Create path validation utilities
- Add container environment detection

**Task 7.4: Classical Music Special Handling**
- Implement composer and performer differentiation
- Add support for opus numbers and catalog numbers
- Create movement detection and organization
- Support for ensemble and conductor tagging

#### Milestone 8: Final Optimization and Documentation (Week 16)

**Task 8.1: Performance Profiling and Optimization**
- Conduct end-to-end performance testing
- Optimize memory usage for large libraries
- Fine-tune concurrency settings
- Implement memory usage monitoring

**Task 8.2: Comprehensive Documentation**
- Create technical documentation for developers
- Write user guide for configuration options
- Add troubleshooting section for common issues
- Create API documentation for extensibility

**Task 8.3: Final Testing and Release**
- Conduct full regression testing
- Prepare release notes
- Finalize versioning and package
- Create automated deployment process

## Key Implementation Details

### Track Matching Algorithm

A robust track matching system is critical. The algorithm will:

1. Try to match by track number in filename (e.g., "01 - Track Title.mp3")
2. If unsuccessful, try to match by track position in album
3. If still unsuccessful, use fuzzy string matching on track title
4. For problematic tracks, optionally use acoustic fingerprinting

### Handling Artist Credits

Artist crediting will handle several patterns:

```csharp
// Special handling for Various Artists
if (album.IsCompilation || album.ArtistName.Contains("Various Artists"))
{
    file.Tag.AlbumArtists = new[] { "Various Artists" };
    file.Tag.Performers = track.Artists.ToArray(); // Preserve track artists
    file.Tag.SetValue("TCMP", new string[] { "1" }); // Set compilation flag
}

// Handle featured artists
var (primaryArtists, featuredArtists) = ParseArtists(artistString);
if (featuredArtists.Length > 0)
{
    // Store as separate fields where possible
    // Fall back to combined representation where needed
}

// Classical music special handling
if (IsClassicalMusic(album, track))
{
    // Store composer in Composer field
    file.Tag.Composers = track.Composers?.ToArray() ?? new string[0];
    
    // Store performers appropriately
    if (track.Performers?.Any() == true)
        file.Tag.Performers = track.Performers.ToArray();
        
    // Add conductor if available
    if (!string.IsNullOrEmpty(track.Conductor))
        file.Tag.Conductor = track.Conductor;
}
```

### Unicode and International Support

All text handling will use proper Unicode support:

```csharp
// Ensure proper UTF-8 encoding when writing tags
public void EnsureProperEncoding(TagLib.File file)
{
    // For ID3v2, force UTF-8 encoding
    if (file.TagTypes.HasFlag(TagLib.TagTypes.Id3v2))
    {
        var id3v2 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2);
        id3v2.ForceDefaultEncoding = true;
        id3v2.DefaultEncoding = TagLib.StringType.UTF8;
    }
}

// Normalize Unicode to NFC form (composed form)
public string NormalizeToNFC(string input)
{
    if (string.IsNullOrEmpty(input))
        return input;
        
    return input.Normalize(NormalizationForm.FormC);
}

// Sanitize filenames
public string SanitizeFileName(string fileName)
{
    // Replace invalid filename characters
    var invalidChars = Path.GetInvalidFileNameChars();
    return string.Join("_", fileName.Split(invalidChars));
}
```

### Progressive Enhancement Strategy

The system will use a progressive enhancement approach:

```csharp
public async Task ProgressiveTaggingAsync(string albumPath, TaggingOptions options)
{
    // Phase 1: Fast basic tagging with Tidal metadata
    var basicResults = await PerformBasicTaggingAsync(albumPath, options);
    
    // Report completion of basic tagging
    _eventAggregator.PublishEvent(new TaggingProgressEvent { 
        Stage = "Basic", 
        CompletedFiles = basicResults.ProcessedFiles.Count,
        Message = "Basic tagging completed" 
    });
    
    if (options.UseEnhancedMetadata)
    {
        // Phase 2: Background task for enhanced metadata
        _ = Task.Run(async () => {
            await PerformEnhancedTaggingAsync(albumPath, options, basicResults);
            
            _eventAggregator.PublishEvent(new TaggingProgressEvent { 
                Stage = "Enhanced", 
                IsComplete = true,
                Message = "Enhanced tagging completed" 
            });
        });
    }
}
```

### Integration with Lidarr Plugin Lifecycle

The tagging system will properly integrate with the Lidarr plugin lifecycle:

```csharp
public class TaggingSystem : IDisposable
{
    private readonly ILifecycleService _lifecycleService;
    private readonly CancellationTokenSource _shutdownTokenSource = new CancellationTokenSource();
    private readonly ILogger _logger;
    
    public TaggingSystem(ILifecycleService lifecycleService, ILogger logger)
    {
        _lifecycleService = lifecycleService;
        _logger = logger;
        
        // Register for application events
        _lifecycleService.ApplicationStarted += OnApplicationStarted;
        _lifecycleService.ApplicationShutdown += OnApplicationShutdown;
    }
    
    private void OnApplicationStarted(object sender, EventArgs e)
    {
        _logger.Info("Tagging system initializing");
        // Initialize caches, background tasks, etc.
    }
    
    private void OnApplicationShutdown(object sender, EventArgs e)
    {
        _logger.Info("Tagging system shutting down");
        _shutdownTokenSource.Cancel();
        
        // Clean up resources, flush caches, etc.
    }
    
    public void Dispose()
    {
        _shutdownTokenSource.Dispose();
        _lifecycleService.ApplicationStarted -= OnApplicationStarted;
        _lifecycleService.ApplicationShutdown -= OnApplicationShutdown;
    }
}
```

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| MusicBrainz API rate limiting | Could slow or block tagging | Implement caching, respect rate limits, graceful fallback to Tidal-only |
| Chromaprint native dependency | Installation issues on some platforms | Provide clear documentation, package native libraries where possible |
| Incompatible audio files | Tagging failures | Robust error handling, skip problem files with clear logging |
| High resource usage during fingerprinting | Performance impact | Optional feature, selective application only where needed |
| Unreliable matching for non-mainstream content | Poor tagging quality | Use confidence thresholds, preserve original metadata |
| Docker compatibility issues | File permission errors | Container-aware operations, fallback strategies |
| Plugin lifecycle disruptions | Data loss or corruption | Implement proper shutdown handling and transaction safety |
| API key security issues | Unauthorized API access | Secure key storage, key rotation, minimal permission scopes |
| Non-Latin text corruption | Metadata loss or garbling | Unicode normalization, encoding detection, and proper UTF-8 usage |
| Memory leaks during batch operations | System instability | Implement proper resource disposal, memory monitoring |

## Success Criteria

The implementation will be considered successful when:

1. **Basic functionality**: Files are successfully tagged with Tidal metadata
2. **Enhanced metadata**: Enrichment with MusicBrainz data works reliably
3. **Edge cases**: Compilations, featured artists, and international text are handled properly
4. **Performance**: Large libraries can be processed efficiently
5. **Resilience**: System degrades gracefully when services are unavailable
6. **Configurability**: Users have control over tagging behavior
7. **Security**: API keys and credentials are handled securely
8. **Internationalization**: Non-Latin scripts are properly supported
9. **Lifecycle integration**: Proper behavior during Lidarr startup/shutdown

## Testing Strategy

### Unit Testing

- **Core components**: All utility classes and core logic
- **File format handling**: Each supported audio format
- **Metadata mapping**: Translation between different sources
- **Error handling**: Verify proper error detection and recovery

### Integration Testing

- **End-to-end workflow**: Complete tagging process
- **API interactions**: MusicBrainz and AcoustID communication
- **Plugin lifecycle**: Startup, shutdown, and update behaviors
- **UI integration**: Configuration and status reporting

### Specialized Testing

- **International character handling**: Test with various scripts and encodings
- **Edge cases**: Various artist compilations, collaborations, etc.
- **Security testing**: API key handling and secure storage
- **Multi-platform testing**: Windows, macOS, Linux, and Docker

### Performance Testing

- **Large library benchmarks**: Processing time for large collections
- **Memory usage profiling**: Resource consumption during batch operations
- **Concurrency testing**: Multiple simultaneous operations
- **Long-term stability**: Extended running with continuous operations

### Acceptance Testing

- **User experience validation**: End-user testing with real-world scenarios
- **Configuration testing**: Verify all user settings work as expected
- **Reporting validation**: Ensure tagging reports are accurate and useful
- **Error recovery testing**: System resilience under various failure conditions

## Compatibility Matrix

| Feature | Windows | macOS | Linux | Docker |
|---------|---------|-------|-------|--------|
| Basic Tagging | Yes | Yes | Yes | Yes |
| MusicBrainz | Yes | Yes | Yes | Yes[1] |
| Fingerprinting | Yes | Yes | Yes[2] | Yes[2] |
| Unicode Support | Yes | Yes | Yes | Yes |
| Classical Music | Yes | Yes | Yes | Yes |
| Multi-disc Albums | Yes | Yes | Yes | Yes |
| Artwork Embedding | Yes | Yes | Yes | Yes |

Notes:
1. May require additional rate limiting in containerized environments
2. Requires Chromaprint libraries to be available

## Future Enhancements

1. Support for additional tagging standards (e.g., Vorbis comments, ID3v2.4)
2. Machine learning-based track matching for difficult cases
3. Integration with additional metadata sources beyond MusicBrainz
4. Batch re-tagging of existing libraries
5. Custom tag mapping and scripting capabilities
6. Web-based tagging report visualization
7. User-defined tagging templates and profiles
8. Deeper integration with other Lidarr components
9. Advanced artwork handling (multiple images, higher resolution)
10. Audio quality analysis and reporting 