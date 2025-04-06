# Tagging Testing Strategy

This document outlines the comprehensive testing strategy for the Lidarr.Plugin.Tidal audio tagging implementation.

## Testing Approach

The testing approach combines multiple testing techniques to ensure thorough validation of the tagging functionality:

1. **Unit Testing**: Testing individual components in isolation
2. **Integration Testing**: Testing interaction between components
3. **End-to-End Testing**: Testing complete workflows
4. **Performance Testing**: Validating performance under various conditions
5. **Edge Case Testing**: Testing uncommon but important scenarios

## Test Categories

### 1. Unit Tests

Unit tests verify that individual components function correctly in isolation.

#### Core Components Tests

| Component | Test Focus | Examples |
|-----------|------------|----------|
| `TaggingService` | Core functionality | Verify service handles files correctly |
| `TrackMatcher` | Matching algorithms | Test matching by track number, title similarity |
| `TagProcessor` | Tag manipulation | Test reading, writing different tag types |
| `FormatHandler` | File format support | Verify format detection and handling |

#### Example Unit Tests

```csharp
[TestFixture]
public class TrackMatcherTests
{
    private ITrackMatcher _matcher;
    private Mock<ILogger<TrackMatcher>> _logger;
    
    [SetUp]
    public void Setup()
    {
        _logger = new Mock<ILogger<TrackMatcher>>();
        _matcher = new TrackMatcher(_logger.Object);
    }
    
    [Test]
    public void MatchTracksToFiles_WithTrackNumbersInFilenames_ShouldMatchCorrectly()
    {
        // Arrange
        var files = new List<LocalAudioFile>
        {
            new LocalAudioFile("01 - First Track.mp3"),
            new LocalAudioFile("02 - Second Track.mp3")
        };
        
        var tracks = new List<TrackMetadata>
        {
            new TrackMetadata { Title = "First Track", TrackNumber = 1 },
            new TrackMetadata { Title = "Second Track", TrackNumber = 2 }
        };
        
        // Act
        var matches = _matcher.MatchTracksToFiles(files, tracks);
        
        // Assert
        Assert.That(matches.Count, Is.EqualTo(2));
        Assert.That(matches[0].Track.Title, Is.EqualTo("First Track"));
        Assert.That(matches[1].Track.Title, Is.EqualTo("Second Track"));
    }
    
    [Test]
    public void MatchTracksToFiles_WithSimilarTitles_ShouldMatchByTitleSimilarity()
    {
        // Arrange
        var files = new List<LocalAudioFile>
        {
            new LocalAudioFile("First Song.mp3"),
            new LocalAudioFile("The 2nd Song.mp3")
        };
        
        var tracks = new List<TrackMetadata>
        {
            new TrackMetadata { Title = "First Song", TrackNumber = 1 },
            new TrackMetadata { Title = "Second Song", TrackNumber = 2 }
        };
        
        // Act
        var matches = _matcher.MatchTracksToFiles(files, tracks);
        
        // Assert
        Assert.That(matches.Count, Is.EqualTo(2));
        Assert.That(matches[0].Track.Title, Is.EqualTo("First Song"));
        Assert.That(matches[1].Track.Title, Is.EqualTo("Second Song"));
    }
}
```

### 2. Integration Tests

Integration tests verify that components interact correctly with each other and external systems.

#### Component Integration Tests

| Integration | Test Focus | Examples |
|-------------|------------|----------|
| TagLib# Integration | File reading/writing | Test tags are written correctly to real files |
| Tidal API Integration | Metadata retrieval | Test mapping of Tidal metadata to tagging models |
| MusicBrainz Integration | External metadata | Test enriching tags with MusicBrainz data |
| AcoustID Integration | Fingerprinting | Test audio fingerprinting and matching |

#### Example Integration Test

```csharp
[TestFixture]
public class TagLibIntegrationTests
{
    private string _testDirectory;
    
    [SetUp]
    public void Setup()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "TaggingTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
        
        // Copy test files to temp directory
        CopyTestFiles();
    }
    
    [TearDown]
    public void TearDown()
    {
        // Clean up temp directory
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, true);
    }
    
    [Test]
    public void TagFile_WithBasicMetadata_ShouldWriteAndReadCorrectly()
    {
        // Arrange
        var testFile = Path.Combine(_testDirectory, "test.mp3");
        var tagProcessor = new TagLibProcessor();
        var metadata = new TrackMetadata
        {
            Title = "Test Title",
            Artists = new[] { "Test Artist" },
            Album = "Test Album",
            Year = 2023,
            TrackNumber = 1
        };
        
        // Act
        var result = tagProcessor.ApplyTags(testFile, metadata);
        
        // Assert
        Assert.That(result.Success, Is.True);
        
        // Verify by reading back
        using var file = TagLib.File.Create(testFile);
        Assert.That(file.Tag.Title, Is.EqualTo("Test Title"));
        Assert.That(file.Tag.Performers, Has.Member("Test Artist"));
        Assert.That(file.Tag.Album, Is.EqualTo("Test Album"));
        Assert.That(file.Tag.Year, Is.EqualTo(2023));
        Assert.That(file.Tag.Track, Is.EqualTo(1));
    }
    
    private void CopyTestFiles()
    {
        // Copy test files from embedded resources to temp directory
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "Lidarr.Plugin.Tidal.Test.Test_Data.Audio_Files.test.mp3";
        
        using (var stream = assembly.GetManifestResourceStream(resourceName))
        using (var fileStream = File.Create(Path.Combine(_testDirectory, "test.mp3")))
        {
            stream.CopyTo(fileStream);
        }
    }
}
```

### 3. End-to-End Tests

End-to-end tests verify complete workflows from initial download to final library import.

#### Workflow Tests

| Workflow | Test Focus | Examples |
|----------|------------|----------|
| Full Import Pipeline | Import process | Test entire import and tagging process |
| Settings Changes | Configuration | Test tagging behavior with different settings |
| Error Recovery | Robustness | Test recovery from errors during tagging |

#### Example End-to-End Test

```csharp
[TestFixture]
public class TaggingPipelineTests
{
    private IServiceProvider _serviceProvider;
    private TidalImportHandler _importHandler;
    private Mock<ITidalDownloadClient> _downloadClient;
    private TidalSettings _settings;
    
    [SetUp]
    public void Setup()
    {
        // Setup services
        var services = new ServiceCollection();
        
        // Create test settings
        _settings = new TidalSettings
        {
            EnableTagging = true,
            WriteArtwork = true,
            EnableEnhancedMetadata = false
        };
        
        // Mock download client
        _downloadClient = new Mock<ITidalDownloadClient>();
        _downloadClient.Setup(x => x.GetDownloadedFiles(It.IsAny<string>()))
            .Returns(new List<string> { "test1.mp3", "test2.mp3" });
            
        // Register services
        services.AddSingleton(_settings);
        services.AddSingleton(_downloadClient.Object);
        services.AddSingleton<ITidalMetadataProvider, MockTidalMetadataProvider>();
        services.AddSingleton<ITaggingService, TaggingService>();
        services.AddSingleton<ITrackMatcher, TrackMatcher>();
        services.AddSingleton<ITagProcessor, MockTagProcessor>();
        services.AddLogging();
        
        _serviceProvider = services.BuildServiceProvider();
        _importHandler = _serviceProvider.GetRequiredService<TidalImportHandler>();
    }
    
    [Test]
    public async Task ProcessImport_WithValidAlbum_ShouldTagFilesAndImport()
    {
        // Arrange
        var importContext = new ImportContext
        {
            AlbumId = "test_album_id",
            DownloadId = "test_download_id"
        };
        
        // Act
        var result = await _importHandler.ProcessImportAsync(importContext);
        
        // Assert
        Assert.That(result.Successful, Is.True);
        Assert.That(result.ImportedFiles.Count, Is.EqualTo(2));
        
        // Verify tagging was performed
        var mockTagProcessor = (MockTagProcessor)_serviceProvider.GetRequiredService<ITagProcessor>();
        Assert.That(mockTagProcessor.TaggedFiles.Count, Is.EqualTo(2));
    }
}
```

### 4. Performance Tests

Performance tests evaluate the tagging system's speed, resource usage, and scalability.

#### Performance Metrics

| Metric | Test Focus | Target |
|--------|------------|--------|
| Tagging Speed | Time per file | < 500ms per file average |
| Parallel Performance | Scaling with threads | Near-linear scaling up to 8 threads |
| Memory Usage | RAM consumption | < 50MB base + 10MB per thread |
| Large Library | Scale test | 1000+ files without degradation |

#### Example Performance Test

```csharp
[TestFixture]
public class TaggingPerformanceTests
{
    private ITaggingService _taggingService;
    private AlbumMetadata _albumMetadata;
    private List<string> _testFiles;
    
    [SetUp]
    public void Setup()
    {
        // Setup services with all dependencies
        var services = new ServiceCollection();
        // ... register services ...
        var serviceProvider = services.BuildServiceProvider();
        
        _taggingService = serviceProvider.GetRequiredService<ITaggingService>();
        
        // Create test metadata
        _albumMetadata = CreateTestMetadata();
        
        // Get test files
        _testFiles = GenerateTestFiles(100);
    }
    
    [Test]
    public async Task TagFilesAsync_With100Files_ShouldCompleteWithinTimeLimit()
    {
        // Arrange
        var files = _testFiles.Select(f => new LocalAudioFile(f)).ToList();
        var options = new TaggingOptions 
        { 
            IncludeArtwork = true,
            ParallelLimit = 4
        };
        
        // Act
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        
        var result = await _taggingService.TagFilesAsync(files, _albumMetadata, options);
        
        stopwatch.Stop();
        var totalTime = stopwatch.ElapsedMilliseconds;
        var timePerFile = totalTime / files.Count;
        
        // Assert
        Assert.That(result.SuccessCount, Is.EqualTo(files.Count));
        Assert.That(timePerFile, Is.LessThan(500), "Average time per file exceeds 500ms");
        
        Console.WriteLine($"Total time: {totalTime}ms");
        Console.WriteLine($"Time per file: {timePerFile}ms");
    }
    
    [Test]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(4)]
    [TestCase(8)]
    public async Task TagFilesAsync_WithDifferentParallelLimits_ShouldScaleEfficiently(int parallelLimit)
    {
        // Arrange
        var files = _testFiles.Select(f => new LocalAudioFile(f)).ToList();
        var options = new TaggingOptions 
        { 
            IncludeArtwork = true,
            ParallelLimit = parallelLimit
        };
        
        // Act
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        
        var result = await _taggingService.TagFilesAsync(files, _albumMetadata, options);
        
        stopwatch.Stop();
        var totalTime = stopwatch.ElapsedMilliseconds;
        
        // Log for analysis (real test would use metrics for validation)
        Console.WriteLine($"Parallel limit: {parallelLimit}, Total time: {totalTime}ms");
        
        // Assert
        Assert.That(result.SuccessCount, Is.EqualTo(files.Count));
    }
    
    private AlbumMetadata CreateTestMetadata()
    {
        // Create test metadata
    }
    
    private List<string> GenerateTestFiles(int count)
    {
        // Generate test files
    }
}
```

### 5. Edge Case Testing

Edge case tests verify the system handles unusual but important scenarios.

#### Edge Cases to Test

| Category | Test Case | Expected Behavior |
|----------|-----------|-------------------|
| Non-Latin Script | Japanese/Korean artist names | Characters preserved correctly |
| Various Artists | Compilation albums | Various Artists tag handled properly |
| Missing Metadata | Files with incomplete tags | Best-effort tagging with warnings |
| Corrupt Files | Damaged audio files | Skip with error, continue with others |
| Mixed Formats | Album with MP3/FLAC mix | All formats tagged consistently |

#### Example Edge Case Test

```csharp
[TestFixture]
public class TaggingEdgeCaseTests
{
    private ITaggingService _taggingService;
    
    [SetUp]
    public void Setup()
    {
        // Setup services
    }
    
    [Test]
    public async Task TagFilesAsync_WithNonLatinText_ShouldPreserveCharacters()
    {
        // Arrange
        var testFile = PrepareTestFile("non_latin_test.mp3");
        var albumMetadata = new AlbumMetadata
        {
            Title = "테스트 앨범", // Korean
            Artist = "テスト アーティスト", // Japanese
            Tracks = new List<TrackMetadata>
            {
                new TrackMetadata
                {
                    Title = "测试歌曲", // Chinese
                    Artists = new[] { "テスト アーティスト" },
                    TrackNumber = 1
                }
            }
        };
        
        // Act
        var result = await _taggingService.TagFilesAsync(
            new[] { new LocalAudioFile(testFile) },
            albumMetadata,
            new TaggingOptions());
            
        // Assert
        Assert.That(result.SuccessCount, Is.EqualTo(1));
        
        // Verify tags written correctly
        using var file = TagLib.File.Create(testFile);
        Assert.That(file.Tag.Album, Is.EqualTo("테스트 앨범"));
        Assert.That(file.Tag.FirstPerformer, Is.EqualTo("テスト アーティスト"));
        Assert.That(file.Tag.Title, Is.EqualTo("测试歌曲"));
    }
    
    [Test]
    public async Task TagFilesAsync_WithVariousArtistsAlbum_ShouldHandleCompilationFlag()
    {
        // Arrange
        var testFiles = new[]
        {
            PrepareTestFile("track1.mp3"),
            PrepareTestFile("track2.mp3")
        };
        
        var albumMetadata = new AlbumMetadata
        {
            Title = "Compilation Album",
            Artist = "Various Artists",
            IsCompilation = true,
            Tracks = new List<TrackMetadata>
            {
                new TrackMetadata
                {
                    Title = "Track 1",
                    Artists = new[] { "Artist 1" },
                    TrackNumber = 1
                },
                new TrackMetadata
                {
                    Title = "Track 2",
                    Artists = new[] { "Artist 2" },
                    TrackNumber = 2
                }
            }
        };
        
        // Act
        var result = await _taggingService.TagFilesAsync(
            testFiles.Select(f => new LocalAudioFile(f)).ToList(),
            albumMetadata,
            new TaggingOptions());
            
        // Assert
        Assert.That(result.SuccessCount, Is.EqualTo(2));
        
        // Verify compilation flag set correctly
        using var file1 = TagLib.File.Create(testFiles[0]);
        Assert.That(file1.Tag.Album, Is.EqualTo("Compilation Album"));
        Assert.That(file1.Tag.AlbumArtists, Contains.Item("Various Artists"));
        Assert.That(file1.Tag.FirstPerformer, Is.EqualTo("Artist 1"));
        Assert.That(file1.Tag.Compilation, Is.True);
        
        using var file2 = TagLib.File.Create(testFiles[1]);
        Assert.That(file2.Tag.FirstPerformer, Is.EqualTo("Artist 2"));
        Assert.That(file2.Tag.Compilation, Is.True);
    }
    
    [Test]
    public async Task TagFilesAsync_WithCorruptFile_ShouldSkipAndContinue()
    {
        // Arrange
        var goodFile = PrepareTestFile("good.mp3");
        var corruptFile = PrepareCorruptFile("corrupt.mp3");
        
        var files = new List<LocalAudioFile>
        {
            new LocalAudioFile(goodFile),
            new LocalAudioFile(corruptFile)
        };
        
        var albumMetadata = CreateBasicAlbumMetadata();
        
        // Act
        var result = await _taggingService.TagFilesAsync(files, albumMetadata, new TaggingOptions());
        
        // Assert
        Assert.That(result.SuccessCount, Is.EqualTo(1));
        Assert.That(result.Errors.Count, Is.EqualTo(1));
        Assert.That(result.Errors[0].FilePath, Is.EqualTo(corruptFile));
    }
    
    private string PrepareTestFile(string filename)
    {
        // Prepare test file
    }
    
    private string PrepareCorruptFile(string filename)
    {
        // Prepare corrupt test file
    }
    
    private AlbumMetadata CreateBasicAlbumMetadata()
    {
        // Create basic metadata
    }
}
```

## Test Automation

### Test Resources

Store test resources in a structured manner:

```
test/Test_Data/
├── Audio_Files/                  # Audio files for testing
│   ├── mp3/                      # MP3 test files
│   ├── flac/                     # FLAC test files
│   └── edge_cases/               # Files for specific edge cases
├── Metadata/                     # Test metadata
│   ├── tidal/                    # Sample Tidal responses
│   ├── musicbrainz/              # Sample MusicBrainz responses
│   └── acoustid/                 # Sample AcoustID responses
└── Expected_Results/             # Expected test results
    ├── tag_dumps/                # Expected tag data
    └── performance/              # Performance baselines
```

### Test Categories

Use test categories to organize and run specific test subsets:

```csharp
[TestFixture]
[Category("UnitTests")]
public class TagProcessorTests
{
    // Unit tests
}

[TestFixture]
[Category("IntegrationTests")]
public class TagLibIntegrationTests
{
    // Integration tests
}

[TestFixture]
[Category("PerformanceTests")]
public class TaggingPerformanceTests
{
    // Performance tests
}

[TestFixture]
[Category("EdgeCaseTests")]
public class TaggingEdgeCaseTests
{
    // Edge case tests
}
```

### Continuous Integration

Configure CI to run different test categories:

```yaml
# Example GitHub Actions workflow for tagging tests
name: Tagging Tests

on:
  push:
    branches: [ main ]
    paths:
      - 'src/Lidarr.Plugin.Tidal/Tagging/**'
      - 'tests/Lidarr.Plugin.Tidal.Test/Tagging/**'
  pull_request:
    branches: [ main ]
    paths:
      - 'src/Lidarr.Plugin.Tidal/Tagging/**'
      - 'tests/Lidarr.Plugin.Tidal.Test/Tagging/**'

jobs:
  unit-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v2
      - name: Setup .NET
        uses: actions/setup-dotnet@v1
        with:
          dotnet-version: 6.0.x
      - name: Restore dependencies
        run: dotnet restore
      - name: Build
        run: dotnet build --no-restore
      - name: Run unit tests
        run: dotnet test --filter Category=UnitTests

  integration-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v2
      - name: Setup .NET
        uses: actions/setup-dotnet@v1
        with:
          dotnet-version: 6.0.x
      - name: Restore dependencies
        run: dotnet restore
      - name: Build
        run: dotnet build --no-restore
      - name: Run integration tests
        run: dotnet test --filter Category=IntegrationTests
        
  performance-tests:
    runs-on: ubuntu-latest
    if: github.event_name == 'push' && github.ref == 'refs/heads/main'
    steps:
      - uses: actions/checkout@v2
      - name: Setup .NET
        uses: actions/setup-dotnet@v1
        with:
          dotnet-version: 6.0.x
      - name: Restore dependencies
        run: dotnet restore
      - name: Build
        run: dotnet build --no-restore
      - name: Run performance tests
        run: dotnet test --filter Category=PerformanceTests
```

## Mocking

### Mocking External Dependencies

Use mocking for external dependencies:

```csharp
public class MockTidalMetadataProvider : ITidalMetadataProvider
{
    public Task<AlbumMetadata> GetAlbumMetadataAsync(string albumId)
    {
        // Return mock data based on albumId
        return Task.FromResult(new AlbumMetadata
        {
            Id = albumId,
            Title = "Mock Album",
            Artist = "Mock Artist",
            Year = 2023,
            Tracks = new List<TrackMetadata>
            {
                new TrackMetadata { Title = "Track 1", TrackNumber = 1 },
                new TrackMetadata { Title = "Track 2", TrackNumber = 2 }
            }
        });
    }
}

public class MockTagProcessor : ITagProcessor
{
    public List<string> TaggedFiles { get; } = new List<string>();
    
    public TaggingResult ApplyTags(string filePath, TrackMetadata metadata)
    {
        // Record that this file was tagged
        TaggedFiles.Add(filePath);
        
        // Return success
        return new TaggingResult { Success = true };
    }
}
```

### Environment-Specific Mocks

Create environment-specific mocks for different testing scenarios:

```csharp
public class ErrorProneTagProcessor : ITagProcessor
{
    public TaggingResult ApplyTags(string filePath, TrackMetadata metadata)
    {
        // Simulate random failures
        if (Path.GetFileName(filePath).Contains("fail") || new Random().Next(10) == 0)
        {
            return new TaggingResult
            {
                Success = false,
                ErrorMessage = "Simulated random failure"
            };
        }
        
        return new TaggingResult { Success = true };
    }
}

public class SlowTagProcessor : ITagProcessor
{
    public TaggingResult ApplyTags(string filePath, TrackMetadata metadata)
    {
        // Simulate slow processing
        Thread.Sleep(200 + new Random().Next(300));
        return new TaggingResult { Success = true };
    }
}
```

## Test Coverage

### Coverage Targets

Aim for the following test coverage targets:

| Component | Coverage Target |
|-----------|----------------|
| Core interfaces and models | 95%+ |
| TagLib integration | 90%+ |
| Matching algorithms | 90%+ |
| Import pipeline | 85%+ |
| Edge case handlers | 80%+ |
| Overall | 85%+ |

### Coverage Reporting

Configure code coverage reports in CI:

```yaml
- name: Run tests with coverage
  run: dotnet test --collect:"XPlat Code Coverage"
  
- name: Generate coverage report
  uses: danielpalme/ReportGenerator-GitHub-Action@4.8.12
  with:
    reports: '**/coverage.cobertura.xml'
    targetdir: 'coveragereport'
    reporttypes: 'HtmlInline;Cobertura'
    
- name: Upload coverage report
  uses: actions/upload-artifact@v2
  with:
    name: Coverage Report
    path: coveragereport
```

## Next Steps

1. Create the basic test project structure
2. Implement key unit tests for core components
3. Set up test data resources
4. Configure CI pipeline for automated testing
5. Implement integration tests with external dependencies mocked
6. Add performance and edge case tests
7. Measure and improve test coverage 