# Tagging Integration Guide

This document outlines how the tagging system integrates with Lidarr and the Tidal plugin architecture.

## Integration Points

### 1. Lidarr Import Pipeline

The tagging system integrates with Lidarr's import pipeline to process audio files:

```
Tidal Download → Lidarr Import → Tagging System → Final Library
```

Key integration points within the Lidarr architecture:

1. **Download Client Integration**: Files downloaded from Tidal need to be temporarily stored before tagging
2. **Import Scanner**: Identifies newly downloaded files and passes them to the tagging pipeline
3. **Metadata Application**: Apply tags before final file import to the library
4. **Post-Processing**: Handle any clean-up or additional tasks after tagging

### 2. Plugin Architecture

The tagging system is implemented as a component within the Tidal plugin:

```
Lidarr.Plugin.Tidal
├── Api
│   └── TidalApiClient.cs
├── Download
│   └── TidalDownloadClient.cs
├── Metadata
│   └── TidalMetadataProvider.cs
└── Tagging [New]
    ├── ITaggingService.cs
    ├── TaggingService.cs
    └── ... (other tagging components)
```

### 3. Service Registration

Register the tagging services with Lidarr's dependency injection system:

```csharp
// Plugin startup
public void Register(IServiceCollection container, IServiceProvider serviceProvider)
{
    // Existing registrations
    container.AddSingleton<ITidalApiClient, TidalApiClient>();
    container.AddSingleton<ITidalDownloadClient, TidalDownloadClient>();
    
    // Tagging service registrations
    container.AddSingleton<ITaggingService, TaggingService>();
    container.AddSingleton<ITagProcessor, BasicTagProcessor>();
    
    // Register advanced services if enabled in settings
    var settings = serviceProvider.GetRequiredService<ITaggingSettings>();
    if (settings.EnableEnhancedMetadata)
    {
        container.AddSingleton<IMusicBrainzClient, MusicBrainzClient>();
        container.AddSingleton<ITagEnricher, MusicBrainzEnricher>();
    }
    
    if (settings.EnableAcousticFingerprinting)
    {
        container.AddSingleton<IAcoustIdClient, AcoustIdClient>();
        container.AddSingleton<IAudioFingerprinter, AcoustIdFingerprinter>();
    }
}
```

## Data Flow

### 1. Import Event Hooks

Hook into Lidarr's import events to trigger tagging:

```csharp
public class TidalImportHandler : IImportHandler
{
    private readonly ITaggingService _taggingService;
    private readonly ITidalMetadataProvider _metadataProvider;
    private readonly ILogger<TidalImportHandler> _logger;
    
    public TidalImportHandler(
        ITaggingService taggingService, 
        ITidalMetadataProvider metadataProvider,
        ILogger<TidalImportHandler> logger)
    {
        _taggingService = taggingService;
        _metadataProvider = metadataProvider;
        _logger = logger;
    }
    
    public async Task<ImportResult> ProcessImportAsync(ImportContext context)
    {
        try
        {
            // 1. Get album and track metadata from Tidal
            var albumId = context.GetAlbumId();
            var albumMetadata = await _metadataProvider.GetAlbumMetadataAsync(albumId);
            
            // 2. Apply tags to downloaded files
            var files = context.GetDownloadedFiles();
            var taggingOptions = new TaggingOptions
            {
                Source = MetadataSource.Tidal,
                AlbumArtworkUrl = albumMetadata.CoverUrl,
                IncludeArtwork = true
            };
            
            var result = await _taggingService.TagFilesAsync(files, albumMetadata, taggingOptions);
            
            // 3. Log results
            _logger.LogInformation("Tagged {SuccessCount} of {TotalCount} files for album {AlbumTitle}",
                result.SuccessCount, result.TotalCount, albumMetadata.Title);
                
            if (result.Errors.Any())
            {
                foreach (var error in result.Errors)
                {
                    _logger.LogWarning("Tagging error for file {FileName}: {ErrorMessage}",
                        error.FilePath, error.ErrorMessage);
                }
            }
            
            // 4. Continue with normal import process
            return ImportResult.Success(result.TaggedFiles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during tagging for import");
            return ImportResult.Failed(ex.Message);
        }
    }
}
```

### 2. Track Matching Strategy

```csharp
public class TrackMatcher : ITrackMatcher
{
    private readonly ILogger<TrackMatcher> _logger;
    
    public TrackMatcher(ILogger<TrackMatcher> logger)
    {
        _logger = logger;
    }
    
    public IReadOnlyList<TrackFileMatch> MatchTracksToFiles(
        IReadOnlyList<LocalAudioFile> files,
        IReadOnlyList<TrackMetadata> tracks)
    {
        var matches = new List<TrackFileMatch>();
        var remainingFiles = files.ToList();
        var remainingTracks = tracks.ToList();
        
        // First pass: Match by track number in filename
        MatchByTrackNumber(remainingFiles, remainingTracks, matches);
        
        // Second pass: Match by title similarity
        MatchByTitleSimilarity(remainingFiles, remainingTracks, matches);
        
        // Log matching results
        _logger.LogInformation("Matched {MatchCount} tracks to files. " +
            "Unmatched: {UnmatchedFiles} files, {UnmatchedTracks} tracks.",
            matches.Count, remainingFiles.Count, remainingTracks.Count);
            
        return matches;
    }
    
    private void MatchByTrackNumber(
        List<LocalAudioFile> files, 
        List<TrackMetadata> tracks,
        List<TrackFileMatch> matches)
    {
        // Implementation details omitted for brevity
        // See the PoC implementation for algorithm details
    }
    
    private void MatchByTitleSimilarity(
        List<LocalAudioFile> files, 
        List<TrackMetadata> tracks,
        List<TrackFileMatch> matches)
    {
        // Implementation details omitted for brevity
        // See the PoC implementation for algorithm details
    }
}
```

## Configuration Integration

### 1. Settings UI Integration

The tagging system configuration is integrated into Lidarr's settings UI:

```csharp
public class TaggingSettingsController : Controller
{
    private readonly ITaggingSettingsService _settingsService;
    
    public TaggingSettingsController(ITaggingSettingsService settingsService)
    {
        _settingsService = settingsService;
    }
    
    [HttpGet]
    public IActionResult GetSettings()
    {
        var settings = _settingsService.GetSettings();
        return Ok(settings);
    }
    
    [HttpPost]
    public IActionResult SaveSettings([FromBody] TaggingSettings settings)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
        
        _settingsService.SaveSettings(settings);
        return Ok();
    }
}
```

### 2. Settings Model

```csharp
public class TaggingSettings
{
    // Basic settings
    public bool EnableTagging { get; set; } = true;
    public bool WriteArtwork { get; set; } = true;
    public bool PreferTidalMetadata { get; set; } = true;
    
    // Enhanced settings
    public bool EnableEnhancedMetadata { get; set; } = false;
    public bool EnableAcousticFingerprinting { get; set; } = false;
    
    // API settings
    public string MusicBrainzBaseUrl { get; set; } = "https://musicbrainz.org/ws/2/";
    public string MusicBrainzAppName { get; set; } = "Lidarr Tidal Plugin";
    public string AcoustIdApiKey { get; set; } = "";
    
    // Performance settings
    public int ParallelTaggingLimit { get; set; } = 4;
    public int ApiRateLimitPerMinute { get; set; } = 50;
}
```

## Logging and Monitoring

### 1. Logging Strategy

Implement consistent logging for tagging operations:

```csharp
public class TaggingService : ITaggingService
{
    private readonly ILogger<TaggingService> _logger;
    
    public TaggingService(ILogger<TaggingService> logger)
    {
        _logger = logger;
    }
    
    public async Task<TaggingResult> TagFilesAsync(
        IReadOnlyList<LocalAudioFile> files,
        AlbumMetadata albumMetadata,
        TaggingOptions options)
    {
        _logger.LogInformation("Starting tagging for {FileCount} files in album {AlbumTitle}",
            files.Count, albumMetadata.Title);
            
        using (_logger.BeginScope("TaggingOperation_{AlbumId}", albumMetadata.Id))
        {
            // Implementation details...
            
            // Log any warnings or issues
            if (someCondition)
            {
                _logger.LogWarning("Potential issue detected: {Issue}", "description");
            }
            
            // Log completion
            _logger.LogInformation("Completed tagging for album {AlbumTitle}. " +
                "Success: {SuccessCount}, Failed: {FailedCount}",
                albumMetadata.Title, successCount, failedCount);
                
            return new TaggingResult(/* ... */);
        }
    }
}
```

### 2. Telemetry

Implement optional telemetry to track tagging performance:

```csharp
public class TaggingTelemetry : ITaggingTelemetry
{
    private readonly IMetricsCollector _metrics;
    
    public TaggingTelemetry(IMetricsCollector metrics)
    {
        _metrics = metrics;
    }
    
    public void RecordTaggingOperation(
        string albumId, 
        int fileCount, 
        TimeSpan duration, 
        bool success)
    {
        _metrics.IncrementCounter("tagging_operations_total");
        _metrics.IncrementCounter(
            success ? "tagging_operations_success" : "tagging_operations_failed");
            
        _metrics.RecordGauge("tagging_operation_file_count", fileCount);
        _metrics.RecordHistogram("tagging_operation_duration_ms", duration.TotalMilliseconds);
    }
    
    public void RecordTaggingError(string errorType)
    {
        _metrics.IncrementCounter("tagging_errors_total");
        _metrics.IncrementCounter($"tagging_errors_{errorType}");
    }
}
```

## Error Handling

### 1. Exception Handling Strategy

```csharp
public class TaggingService : ITaggingService
{
    // Existing fields and constructor...
    
    public async Task<TaggingResult> TagFilesAsync(/* params */)
    {
        var errors = new List<TaggingError>();
        var taggedFiles = new List<LocalAudioFile>();
        
        foreach (var file in files)
        {
            try
            {
                // Attempt to tag file
                await TagFileAsync(file, albumMetadata, trackMetadata);
                taggedFiles.Add(file);
            }
            catch (TagLibSharpException ex)
            {
                _logger.LogError(ex, "TagLib# error for file {FilePath}", file.Path);
                errors.Add(new TaggingError(file.Path, "TagLib error: " + ex.Message));
            }
            catch (MetadataException ex)
            {
                _logger.LogError(ex, "Metadata error for file {FilePath}", file.Path);
                errors.Add(new TaggingError(file.Path, "Metadata error: " + ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error tagging file {FilePath}", file.Path);
                errors.Add(new TaggingError(file.Path, "Unexpected error: " + ex.Message));
            }
        }
        
        return new TaggingResult(taggedFiles, errors);
    }
}
```

### 2. Fallback Strategy

```csharp
public class MetadataProvider : IMetadataProvider
{
    // Fields and constructor...
    
    public async Task<AlbumMetadata> GetAlbumMetadataAsync(string albumId)
    {
        try
        {
            // Primary source
            return await _tidalMetadataProvider.GetAlbumMetadataAsync(albumId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get metadata from Tidal, trying fallback");
            
            try
            {
                // Fallback source
                return await _musicBrainzMetadataProvider.GetAlbumMetadataAsync(albumId);
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "All metadata sources failed");
                throw new AggregateException("All metadata sources failed", ex, fallbackEx);
            }
        }
    }
}
```

## Testing Strategy

### 1. Integration Test Structure

```csharp
[TestFixture]
public class TaggingIntegrationTests
{
    private ITaggingService _taggingService;
    private ITidalMetadataProvider _metadataProvider;
    private ITrackMatcher _trackMatcher;
    private IFileSystem _fileSystem;
    
    [SetUp]
    public void Setup()
    {
        // Setup test dependencies
        _fileSystem = new RealFileSystem();
        _trackMatcher = new TrackMatcher(NullLogger<TrackMatcher>.Instance);
        _metadataProvider = Substitute.For<ITidalMetadataProvider>();
        _taggingService = new TaggingService(
            _trackMatcher,
            new TagProcessor(),
            NullLogger<TaggingService>.Instance);
    }
    
    [Test]
    public async Task TagFiles_WithValidMetadata_ShouldApplyTagsToAllFiles()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), "TaggingTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(testDir);
        
        try
        {
            // Copy test files to temp directory
            CopyTestFiles(testDir);
            
            // Setup test metadata
            var albumMetadata = CreateTestAlbumMetadata();
            
            // Act
            var files = Directory.GetFiles(testDir, "*.mp3")
                .Select(f => new LocalAudioFile(f))
                .ToList();
                
            var result = await _taggingService.TagFilesAsync(
                files, 
                albumMetadata,
                new TaggingOptions { IncludeArtwork = true });
                
            // Assert
            Assert.That(result.SuccessCount, Is.EqualTo(files.Count));
            Assert.That(result.Errors, Is.Empty);
            
            // Verify tags were written
            foreach (var file in files)
            {
                using (var tagFile = TagLib.File.Create(file.Path))
                {
                    Assert.That(tagFile.Tag.Album, Is.EqualTo(albumMetadata.Title));
                    // Additional tag assertions...
                }
            }
        }
        finally
        {
            // Cleanup
            Directory.Delete(testDir, true);
        }
    }
    
    private AlbumMetadata CreateTestAlbumMetadata()
    {
        // Create test metadata...
    }
    
    private void CopyTestFiles(string destDir)
    {
        // Copy test files from resources to test directory...
    }
}
```

### 2. Mock API Responses

Create helpers for mocking API responses:

```csharp
public static class ApiMockHelper
{
    public static ITidalApiClient CreateMockTidalClient()
    {
        var mock = Substitute.For<ITidalApiClient>();
        
        // Setup album response
        mock.GetAlbumAsync(Arg.Any<string>())
            .Returns(callInfo => 
            {
                var albumId = callInfo.Arg<string>();
                return Task.FromResult(GetMockAlbumResponse(albumId));
            });
            
        // Setup tracks response
        mock.GetAlbumTracksAsync(Arg.Any<string>())
            .Returns(callInfo => 
            {
                var albumId = callInfo.Arg<string>();
                return Task.FromResult(GetMockTracksResponse(albumId));
            });
            
        return mock;
    }
    
    private static TidalAlbum GetMockAlbumResponse(string albumId)
    {
        // Return mock album data from test resources
        var json = File.ReadAllText($"TestData/TidalResponses/album_{albumId}.json");
        return JsonSerializer.Deserialize<TidalAlbum>(json);
    }
    
    private static List<TidalTrack> GetMockTracksResponse(string albumId)
    {
        // Return mock tracks data from test resources
        var json = File.ReadAllText($"TestData/TidalResponses/tracks_{albumId}.json");
        return JsonSerializer.Deserialize<List<TidalTrack>>(json);
    }
}
```

## Deployment Considerations

### 1. Version Compatibility

Ensure the tagging system is compatible with different Lidarr versions:

```csharp
[MinimumVersion("1.0.0.2000")]
public class TidalPlugin : IPlugin
{
    // Plugin implementation
}
```

### 2. Gradual Rollout Strategy

Create a phased rollout plan:

1. **Alpha Phase**:
   - Limited user testing
   - Enable only basic tagging features
   - Collect extensive logs

2. **Beta Phase**:
   - Enable for all users who opt-in
   - Include enhanced metadata features
   - Monitor error rates

3. **General Release**:
   - Default configuration with safe settings
   - Optional advanced features

## Next Steps

1. Start implementation with the core interfaces
2. Develop the track matching algorithm with testing
3. Implement the basic tagging service
4. Add Lidarr integration points
5. Implement settings UI
6. Add advanced features
7. Create comprehensive test suite 