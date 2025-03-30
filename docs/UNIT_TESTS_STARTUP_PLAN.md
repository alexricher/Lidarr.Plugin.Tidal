# Revised Lidarr.Plugin.Tidal Unit Testing Strategy

## 1. Initial Focus Areas (First 2 Weeks)

- **Download Queue Management**: Start with basic functionality tests
- **Tidal API Integration**: Focus on authentication and core API calls
- **Error Handling**: Test basic error scenarios

## 2. Testing Framework Setup

- Continue using NUnit with NSubstitute for mocking
- Add FluentAssertions for more readable assertions
- Set up a basic CI workflow with GitHub Actions

## 3. First Test Implementation Targets

### Priority 1: Core API and Authentication
- Authentication with valid/invalid credentials
- Session management and token refresh
- Basic track and album info retrieval

### Priority 2: Download Queue Fundamentals
- Queue item addition and removal
- Basic processing flow
- Status tracking and updates

### Priority 3: Error Handling
- API errors and retry logic
- Network failures
- Invalid track/album IDs

Start with simple, focused tests before moving to more complex scenarios.

## 4. Test Project Structure

### 4.1 Folder Organization

```
Lidarr.Plugin.Tidal.Tests/
├── Download/
│   ├── Clients/
│   │   └── Tidal/
│   │       ├── Queue/
│   │       │   ├── DownloadTaskQueueTests.cs
│   │       │   ├── NaturalDownloadSchedulerTests.cs
│   │       │   └── UserBehaviorSimulatorTests.cs
│   │       ├── API/
│   │       │   ├── TidalApiClientTests.cs
│   │       │   └── TidalAuthenticationTests.cs
│   │       └── TidalDownloaderTests.cs
│   └── Persistence/
│       └── QueuePersistenceTests.cs
├── TestHelpers/
│   ├── TestFactory.cs
│   ├── MockHttpMessageHandler.cs
│   └── TestDataHelper.cs
└── TestData/
    ├── ApiResponses/
    │   ├── album_response.json
    │   ├── track_response.json
    │   └── error_response.json
    └── AudioSamples/
        └── test_audio.flac
```

### 4.2 Test Categories

All tests should be categorized using NUnit's Category attribute:

- `[Category("Unit")]`: For isolated unit tests that don't require external resources
- `[Category("Integration")]`: For tests that interact with external systems or resources
- `[Category("Performance")]`: For tests that measure performance metrics
- `[Category("Regression")]`: For tests that verify fixed bugs don't reoccur

## 5. Simplified CI Integration

```yaml
name: Unit Tests

on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main, develop ]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v3
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: 6.0.x
    - name: Restore dependencies
      run: dotnet restore
    - name: Build
      run: dotnet build --no-restore
    - name: Test
      run: dotnet test --no-build --verbosity normal
```

## 12. Implementation Plan

### 12.1 Week 1 Revised Plan

#### Day 1-2: Setup and Basic Tests
- Set up test project structure
- Implement basic test helpers and mocks
- Create first authentication tests

#### Day 3-4: API Client Core Tests
- Implement track/album info retrieval tests
- Test API error handling
- Test session management

#### Day 5: Review and Plan
- Review test coverage
- Identify gaps
- Plan week 2 priorities

### 12.2 Week 2 Revised Plan

#### Day 1-2: Queue Basics
- Test queue initialization
- Test adding/removing items
- Test basic queue processing

#### Day 3-4: Download Processing
- Test download workflow
- Test status tracking
- Test simple rate limiting

#### Day 5: Integration Points
- Test how components work together
- Review and refine test coverage

## 7. Additional Test Cases

### Simplified Rate Limiting Tests
```csharp
[Test]
public void TokenBucket_WhenRateLimitReached_PreventsImmediateDownloads()
{
    // Arrange
    var settings = new TidalSettings { MaxDownloadsPerHour = 5 };
    var tokenBucket = new TokenBucketRateLimiter(settings);
    
    // Act - Consume all tokens
    for (int i = 0; i < 5; i++)
    {
        tokenBucket.ConsumeToken();
    }
    
    // Assert
    Assert.That(tokenBucket.CanConsume(), Is.False, 
        "Should not allow more downloads after rate limit reached");
}
```

### Focused Error Handling Tests
```csharp
[Test]
public async Task ProcessQueueItem_WhenNetworkFailure_RetriesWithBackoff()
{
    // Arrange
    var mockClient = Substitute.For<ITidalApiClient>();
    var retryPolicy = new RetryPolicy(maxRetries: 3, initialDelayMs: 100);
    var queue = new DownloadTaskQueue(retryPolicy);
    var item = CreateTestItem("Network Error Track");
    
    int attemptCount = 0;
    mockClient.DownloadTrackAsync(Arg.Any<string>(), Arg.Any<string>())
        .Returns(_ => {
            attemptCount++;
            if (attemptCount < 3) {
                throw new NetworkException("Connection failed");
            }
            return Task.FromResult(new byte[100]);
        });
    
    // Act
    await queue.ProcessItemAsync(item, mockClient);
    
    // Assert
    Assert.That(attemptCount, Is.EqualTo(3), "Should have attempted 3 times");
    Assert.That(item.Status, Is.EqualTo(DownloadItemStatus.Completed));
}
```

### Natural Behavior Simulation Tests
```csharp
[Test]
public async Task ProcessQueue_WithNaturalBehaviorEnabled_AddsDelaysBetweenDownloads()
{
    // Arrange
    var settings = new TidalSettings { 
        EnableNaturalBehavior = true,
        TrackToTrackDelayMin = 3,
        TrackToTrackDelayMax = 5
    };
    var queue = CreateTestQueue(settings);
    var mockClient = Substitute.For<ITidalApiClient>();
    mockClient.DownloadTrackAsync(Arg.Any<string>(), Arg.Any<string>())
        .Returns(Task.FromResult(new byte[100]));
    
    // Add multiple items
    for (int i = 0; i < 3; i++)
    {
        await queue.QueueBackgroundWorkItemAsync(CreateTestItem($"Track {i}"));
    }
    
    // Act
    var stopwatch = new Stopwatch();
    stopwatch.Start();
    await queue.ProcessQueueAsync(mockClient, CancellationToken.None);
    stopwatch.Stop();
    
    // Assert
    // With 3 tracks and 3-5 second delays, should take at least 6 seconds
    Assert.That(stopwatch.ElapsedMilliseconds, Is.GreaterThan(6000));
}
```

## 8. Testing Specific Components

### 8.1 TidalDownloadViewer Tests

The TidalDownloadViewer component is planned for implementation in version 10.0.3.x. When developing this component, we'll need tests for:

```csharp
[Test]
public void GetDownloadStatistics_ReturnsCorrectCounts()
{
    // Arrange
    var viewer = new TidalDownloadViewer();
    var queue = CreateTestQueue();
    
    // Add some items with different statuses
    queue.AddItem(CreateTestItem("Completed Track", status: DownloadItemStatus.Completed));
    queue.AddItem(CreateTestItem("Failed Track", status: DownloadItemStatus.Failed));
    queue.AddItem(CreateTestItem("Queued Track", status: DownloadItemStatus.Queued));
    
    // Act
    var stats = viewer.GetDownloadStatistics(queue);
    
    // Assert
    Assert.That(stats.CompletedCount, Is.EqualTo(1));
    Assert.That(stats.FailedCount, Is.EqualTo(1));
    Assert.That(stats.QueuedCount, Is.EqualTo(1));
}
```

### 8.2 UserBehaviorSimulator Tests

The UserBehaviorSimulator is a critical component for natural behavior implementation:

```csharp
[Test]
public void CalculateDelay_WithinConfiguredBounds()
{
    // Arrange
    var settings = new TidalSettings {
        TrackToTrackDelayMin = 5,
        TrackToTrackDelayMax = 10
    };
    var simulator = new UserBehaviorSimulator(settings);
    
    // Act
    var delay = simulator.CalculateTrackToTrackDelay();
    
    // Assert
    Assert.That(delay.TotalSeconds, Is.GreaterThanOrEqualTo(5));
    Assert.That(delay.TotalSeconds, Is.LessThanOrEqualTo(10));
}

[Test]
public void SimulateListeningPattern_RespectsTimeOfDay()
{
    // Arrange
    var settings = new TidalSettings {
        EnableTimeOfDayAdaptation = true,
        ActiveHoursStart = 8,  // 8 AM
        ActiveHoursEnd = 22    // 10 PM
    };
    var simulator = new UserBehaviorSimulator(settings);
    
    // Act - simulate time outside active hours
    var currentTime = new DateTime(2023, 1, 1, 3, 0, 0); // 3 AM
    var shouldDownload = simulator.ShouldDownloadAtTime(currentTime);
    
    // Assert
    Assert.That(shouldDownload, Is.False, "Should not download outside active hours");
}
```

## 9. Test Data Management

### 9.1 Mock API Responses

Create a folder structure for storing mock API responses:

```
/src/Lidarr.Plugin.Tidal.Tests/TestData/
  /ApiResponses/
    album_details.json
    artist_search.json
    track_details.json
  /AudioSamples/
    test_track.flac (small sample file)
```

### 9.2 Test Helper Methods

```csharp
public static class TestDataHelper
{
    public static string GetMockApiResponse(string filename)
    {
        return File.ReadAllText(Path.Combine("TestData", "ApiResponses", filename));
    }
    
    public static byte[] GetAudioSample(string filename)
    {
        return File.ReadAllBytes(Path.Combine("TestData", "AudioSamples", filename));
    }
    
    public static TidalSettings CreateTestSettings(bool naturalBehavior = false)
    {
        return new TidalSettings
        {
            Username = "test_user",
            Password = "test_password",
            Quality = AudioQuality.High,
            MaxConcurrentTrackDownloads = 2,
            MaxDownloadsPerHour = 10,
            EnableNaturalBehavior = naturalBehavior,
            TrackToTrackDelayMin = naturalBehavior ? 3 : 0,
            TrackToTrackDelayMax = naturalBehavior ? 10 : 0
        };
    }
}
```

## 10. Integration with Lidarr Tests

Since our plugin integrates with Lidarr, we should test this integration:

```csharp
[Test]
public void OnGrab_SendsCorrectPayloadToLidarr()
{
    // Arrange
    var mockLidarrClient = Substitute.For<ILidarrClient>();
    var plugin = new TidalPlugin(mockLidarrClient);
    var release = new ReleaseInfo
    {
        Title = "Test Album",
        Artist = "Test Artist",
        Quality = new QualityModel { Quality = Quality.FLAC }
    };
    
    // Act
    plugin.OnGrab(release);
    
    // Assert
    mockLidarrClient.Received().NotifyGrab(Arg.Is<GrabNotification>(n => 
        n.Title == "Test Album" && 
        n.Artist == "Test Artist"));
}
```

## 11. Monitoring and Reporting

Set up test coverage reporting to track progress:

```yaml
# Add to CI workflow
- name: Generate coverage report
  run: dotnet test --collect:"XPlat Code Coverage"
  
- name: Upload coverage to Codecov
  uses: codecov/codecov-action@v3
```

## 13. Metadata Handling Tests

The plugin needs to properly handle track metadata when downloading from Tidal:

```csharp
[Test]
public async Task ProcessDownload_SetsCorrectMetadata()
{
    // Arrange
    var mockClient = Substitute.For<ITidalApiClient>();
    var mockMetadataWriter = Substitute.For<IMetadataWriter>();
    var queue = CreateTestQueue(metadataWriter: mockMetadataWriter);
    
    var trackInfo = new TidalTrackInfo
    {
        Id = "12345",
        Title = "Test Track",
        Artist = "Test Artist",
        Album = "Test Album",
        Year = 2023,
        TrackNumber = 1
    };
    
    mockClient.GetTrackInfoAsync("12345")
        .Returns(Task.FromResult(trackInfo));
    
    mockClient.DownloadTrackAsync("12345", Arg.Any<string>())
        .Returns(Task.FromResult(new byte[1000]));
    
    var item = CreateTestItem("Test Track", id: "12345");
    
    // Act
    await queue.QueueBackgroundWorkItemAsync(item);
    await queue.ProcessNextQueueItemAsync(mockClient);
    
    // Assert
    mockMetadataWriter.Received().WriteMetadata(
        Arg.Any<string>(),
        Arg.Is<TrackMetadata>(m => 
            m.Title == "Test Track" && 
            m.Artist == "Test Artist" && 
            m.Album == "Test Album" && 
            m.Year == 2023 && 
            m.TrackNumber == 1)
    );
}
```

## 14. Test Coverage Monitoring

### 14.1 Coverage Tools

We'll use the following tools to monitor test coverage:

- **Coverlet**: For collecting coverage data during test execution
- **ReportGenerator**: For generating HTML reports from coverage data

### 14.2 Realistic Coverage Targets

| Component | Initial Target | Final Target |
|-----------|----------------|--------------|
| Download Queue | 60% | 80% |
| API Client | 70% | 85% |
| Downloader | 60% | 80% |
| Persistence | 50% | 70% |

### 14.3 Simplified Coverage Reporting in CI

```yaml
- name: Test with coverage
  run: dotnet test --no-build --verbosity normal /p:CollectCoverage=true /p:CoverletOutputFormat=opencover

- name: Generate coverage report
  uses: danielpalme/ReportGenerator-GitHub-Action@4.8.12
  with:
    reports: '**/coverage.opencover.xml'
    targetdir: 'coveragereport'

- name: Upload coverage report
  uses: actions/upload-artifact@v2
  with:
    name: CoverageReport
    path: coveragereport
```

### 14.4 Local Coverage Workflow

To check coverage locally:

```bash
# Run tests with coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=html

# Open the generated HTML report
open ./Lidarr.Plugin.Tidal.Tests/coverage.html
```

## 15. Persistence Tests

The queue should maintain state between application restarts:

```csharp
[Test]
public void SaveQueue_PersistsQueueState()
{
    // Arrange
    var mockStorage = Substitute.For<IQueueStorage>();
    var queue = CreateTestQueue(storage: mockStorage);
    
    var item1 = CreateTestItem("Track 1");
    var item2 = CreateTestItem("Track 2");
    
    queue.AddItem(item1);
    queue.AddItem(item2);
    
    // Act
    queue.SaveQueue();
    
    // Assert
    mockStorage.Received().SaveQueue(Arg.Is<List<DownloadQueueItem>>(items => 
        items.Count == 2 && 
        items[0].Title == "Track 1" && 
        items[1].Title == "Track 2"));
}

[Test]
public void LoadQueue_RestoresQueueState()
{
    // Arrange
    var mockStorage = Substitute.For<IQueueStorage>();
    var savedItems = new List<DownloadQueueItem>
    {
        CreateTestItem("Saved Track 1"),
        CreateTestItem("Saved Track 2")
    };
    
    mockStorage.LoadQueue().Returns(savedItems);
    
    // Act
    var queue = CreateTestQueue(storage: mockStorage);
    queue.LoadQueue();
    
    // Assert
    var items = queue.GetQueueListing();
    Assert.That(items.Count, Is.EqualTo(2));
    Assert.That(items[0].Title, Is.EqualTo("Saved Track 1"));
    Assert.That(items[1].Title, Is.EqualTo("Saved Track 2"));
}
```

## 16. Concurrency Tests

The queue should handle concurrent operations safely:

```csharp
[Test]
public async Task ProcessQueue_WithMultipleThreads_ProcessesItemsSafely()
{
    // Arrange
    var settings = new TidalSettings { MaxConcurrentTrackDownloads = 3 };
    var queue = CreateTestQueue(settings);
    var mockClient = Substitute.For<ITidalApiClient>();
    
    // Simulate download taking some time
    mockClient.DownloadTrackAsync(Arg.Any<string>(), Arg.Any<string>())
        .Returns(callInfo => 
        {
            Thread.Sleep(100); // Simulate network delay
            return Task.FromResult(new byte[100]);
        });
    
    // Add 10 items
    for (int i = 0; i < 10; i++)
    {
        await queue.QueueBackgroundWorkItemAsync(CreateTestItem($"Track {i}", id: i.ToString()));
    }
    
    // Act
    var cts = new CancellationTokenSource();
    var task = queue.ProcessQueueAsync(mockClient, cts.Token);
    
    // Wait a bit to let processing start
    await Task.Delay(200);
    
    // Add more items while processing
    for (int i = 10; i < 15; i++)
    {
        await queue.QueueBackgroundWorkItemAsync(CreateTestItem($"Track {i}", id: i.ToString()));
    }
    
    // Let it process for a bit more
    await Task.Delay(500);
    cts.Cancel();
    
    try { await task; } catch (OperationCanceledException) { }
    
    // Assert
    var items = queue.GetQueueListing();
    var completedCount = items.Count(i => i.Status == DownloadItemStatus.Completed);
    var pendingCount = items.Count(i => i.Status == DownloadItemStatus.Queued);
    
    Assert.That(completedCount, Is.GreaterThan(0), "Some items should have completed");
    Assert.That(completedCount + pendingCount, Is.EqualTo(15), "All items should be accounted for");
}
```

## 17. Plugin Lifecycle Tests

Test the plugin's initialization and shutdown processes:

```csharp
[Test]
public void OnApplicationStartup_InitializesComponents()
{
    // Arrange
    var mockLogger = Substitute.For<ILogger>();
    var mockQueueFactory = Substitute.For<IDownloadQueueFactory>();
    var mockQueue = Substitute.For<IDownloadTaskQueue>();
    
    mockQueueFactory.CreateQueue().Returns(mockQueue);
    
    var plugin = new TidalPlugin(mockLogger, mockQueueFactory);
    
    // Act
    plugin.OnApplicationStartup();
    
    // Assert
    mockQueueFactory.Received().CreateQueue();
    mockQueue.Received().LoadQueue();
    mockQueue.Received().StartProcessing();
}

[Test]
public void OnApplicationShutdown_SavesStateAndStopsProcessing()
{
    // Arrange
    var mockLogger = Substitute.For<ILogger>();
    var mockQueue = Substitute.For<IDownloadTaskQueue>();
    
    var plugin = new TidalPlugin(mockLogger);
    plugin.SetQueue(mockQueue); // Method to set the queue for testing
    
    // Act
    plugin.OnApplicationShutdown();
    
    // Assert
    mockQueue.Received().SaveQueue();
    mockQueue.Received().StopProcessing();
}
```

## 18. Quality Settings Tests

Test that the plugin respects quality settings:

```csharp
[Test]
public async Task DownloadTrack_UsesConfiguredQuality()
{
    // Arrange
    var settings = new TidalSettings { Quality = AudioQuality.Master };
    var mockClient = Substitute.For<ITidalApiClient>();
    var downloader = new TidalDownloader(mockClient, settings);
    
    // Act
    await downloader.DownloadTrackAsync("12345", "output.flac");
    
    // Assert
    await mockClient.Received().DownloadTrackAsync(
        "12345", 
        Arg.Any<string>(), 
        Arg.Is<AudioQuality>(q => q == AudioQuality.Master)
    );
}
```

## 19. Retry Logic Tests

Test the retry mechanism for failed downloads:

```csharp
[Test]
public async Task ProcessQueueItem_WithTemporaryFailure_RetriesDownload()
{
    // Arrange
    var settings = new TidalSettings { MaxRetryCount = 3 };
    var queue = CreateTestQueue(settings);
    var mockClient = Substitute.For<ITidalApiClient>();
    
    // First call fails, second succeeds
    var callCount = 0;
    mockClient.DownloadTrackAsync(Arg.Any<string>(), Arg.Any<string>())
        .Returns(callInfo => 
        {
            callCount++;
            if (callCount == 1)
            {
                return Task.FromException<byte[]>(new TidalApiException("Temporary error"));
            }
            return Task.FromResult(new byte[100]);
        });
    
    var item = CreateTestItem("Retry Track");
    
    // Act
    await queue.QueueBackgroundWorkItemAsync(item);
    await queue.ProcessNextQueueItemAsync(mockClient);
    
    // Assert
    Assert.That(callCount, Is.EqualTo(2), "Should have retried once");
    var items = queue.GetQueueListing();
    Assert.That(items[0].Status, Is.EqualTo(DownloadItemStatus.Completed));
    Assert.That(items[0].RetryCount, Is.EqualTo(1));
}

[Test]
public async Task ProcessQueueItem_ExceedingMaxRetries_MarksAsFailed()
{
    // Arrange
    var settings = new TidalSettings { MaxRetryCount = 2 };
    var queue = CreateTestQueue(settings);
    var mockClient = Substitute.For<ITidalApiClient>();
    
    // All calls fail
    mockClient.DownloadTrackAsync(Arg.Any<string>(), Arg.Any<string>())
        .Returns(Task.FromException<byte[]>(new TidalApiException("Persistent error")));
    
    var item = CreateTestItem("Failed Track");
    
    // Act
    await queue.QueueBackgroundWorkItemAsync(item);
    await queue.ProcessNextQueueItemAsync(mockClient);
    
    // Assert
    var items = queue.GetQueueListing();
    Assert.That(items[0].Status, Is.EqualTo(DownloadItemStatus.Failed));
    Assert.That(items[0].RetryCount, Is.EqualTo(2));
    Assert.That(items[0].ErrorMessage, Contains.Substring("Persistent error"));
}
```

## 20. Notification Tests

Test that the plugin properly notifies Lidarr about download status:

```csharp
[Test]
public async Task ProcessQueueItem_WhenCompleted_NotifiesLidarr()
{
    // Arrange
    var mockLidarrClient = Substitute.For<ILidarrClient>();
    var mockClient = Substitute.For<ITidalApiClient>();
    
    mockClient.DownloadTrackAsync(Arg.Any<string>(), Arg.Any<string>())
        .Returns(Task.FromResult(new byte[100]));
    
    var queue = CreateTestQueue(lidarrClient: mockLidarrClient);
    var item = CreateTestItem("Notification Track", artist: "Test Artist", album: "Test Album");
    
    // Act
    await queue.QueueBackgroundWorkItemAsync(item);
    await queue.ProcessNextQueueItemAsync(mockClient);
    
    // Assert
    mockLidarrClient.Received().NotifyDownloadComplete(Arg.Is<DownloadCompletedNotification>(n => 
        n.Title == "Notification Track" && 
        n.Artist == "Test Artist" && 
        n.Album == "Test Album"));
}
```

## 21. Mocking Strategy

For effective testing, we'll use the following mocking approach:

### 21.1 Core Dependencies to Mock

- `ITidalApiClient`: For all Tidal API interactions
- `IQueueStorage`: For persistence operations
- `IMetadataWriter`: For metadata handling
- `ILidarrClient`: For Lidarr notifications
- `ILogger`: For logging operations

### 21.2 Simplified Mock Factory

```csharp
public static class MockFactory
{
    public static ITidalApiClient CreateMockTidalClient(
        bool authSuccess = true,
        bool downloadSuccess = true,
        int downloadDelayMs = 0)
    {
        var mock = Substitute.For<ITidalApiClient>();
        
        // Authentication behavior
        if (authSuccess) {
            mock.AuthenticateAsync(Arg.Any<string>(), Arg.Any<string>())
                .Returns(Task.FromResult(new TidalSession { 
                    SessionId = "test-session", 
                    ExpiresIn = 3600 
                }));
        } else {
            mock.AuthenticateAsync(Arg.Any<string>(), Arg.Any<string>())
                .Returns(Task.FromException<TidalSession>(
                    new TidalApiException("Authentication failed")));
        }
        
        // Download behavior
        if (downloadSuccess) {
            mock.DownloadTrackAsync(Arg.Any<string>(), Arg.Any<string>())
                .Returns(async _ => {
                    if (downloadDelayMs > 0) {
                        await Task.Delay(downloadDelayMs);
                    }
                    return new byte[1000];
                });
        } else {
            mock.DownloadTrackAsync(Arg.Any<string>(), Arg.Any<string>())
                .Returns(Task.FromException<byte[]>(
                    new TidalApiException("Download failed")));
        }
        
        return mock;
    }
    
    public static IDownloadItem CreateMockDownloadItem(
        string id = "track-1",
        string title = "Test Track",
        DownloadItemStatus status = DownloadItemStatus.Queued)
    {
        var item = Substitute.For<IDownloadItem>();
        item.ID.Returns(id);
        item.Title.Returns(title);
        item.Status.Returns(status);
        return item;
    }
}
```

## 22. Test Data Generation

Create realistic test data generators:

```csharp
public static class TestDataGenerator
{
    public static List<TidalTrackInfo> GenerateAlbumTracks(string albumId, int trackCount)
    {
        var tracks = new List<TidalTrackInfo>();
        
        for (int i = 1; i <= trackCount; i++)
        {
            tracks.Add(new TidalTrackInfo
            {
                Id = $"{albumId}-{i}",
                Title = $"Track {i}",
                Artist = "Test Artist",
                Album = "Test Album",
                Year = 2023,
                TrackNumber = i,
                AlbumId = albumId
            });
        }
        
        return tracks;
    }
    
    public static TidalAlbumInfo GenerateAlbum(string albumId, int trackCount)
    {
        return new TidalAlbumInfo
        {
            Id = albumId,
            Title = "Test Album",
            Artist = "Test Artist",
            Year = 2023,
            TrackCount = trackCount,
            Tracks = GenerateAlbumTracks(albumId, trackCount)
        };
    }
}
```

## 23. Test Execution Strategy

To ensure efficient test execution:

1. **Categorize tests** using NUnit categories:
   ```csharp
   public static class TestCategories
   {
       public const string Unit = "Unit";
       public const string Integration = "Integration";
       public const string Performance = "Performance";
   }
   
   [TestFixture]
   public class ApiClientTests
   {
       [Test]
       [Category(TestCategories.Unit)]
       public void GetTrackInfo_WithValidId_ReturnsTrackDetails() { /* ... */ }
       
       [Test]
       [Category(TestCategories.Integration)]
       public void AuthenticateWithApi_WithValidCredentials_Succeeds() { /* ... */ }
   }
   ```

2. **Run specific test categories**:
   ```bash
   # Run only unit tests
   dotnet test --filter Category=Unit
   
   # Run only integration tests
   dotnet test --filter Category=Integration
   ```

3. **Use test fixtures efficiently**:
   ```csharp
   [TestFixture]
   public class DownloadTaskQueueTests
   {
       private IDownloadTaskQueue _queue;
       private ITidalApiClient _mockClient;
       
       [SetUp]
       public void Setup()
       {
           _mockClient = MockFactory.CreateMockTidalClient();
           _queue = new DownloadTaskQueue(TestDataHelper.CreateDefaultSettings());
       }
       
       [TearDown]
       public void Cleanup()
       {
           _queue.Dispose();
       }
       
       // Tests using _queue and _mockClient
   }
   ```

## 24. Continuous Improvement Plan

To maintain and improve test coverage over time:

1. **Weekly test review** to identify gaps in coverage
2. **Test-driven development** for new features
3. **Regression tests** for any bugs fixed
4. **Quarterly test refactoring** to improve maintainability

## 25. Documentation

Document testing practices for the team:

1. **Test naming convention**: `MethodName_Scenario_ExpectedResult`
2. **Test structure**: Arrange-Act-Assert pattern
3. **Mock usage guidelines**: When to use mocks vs real implementations
4. **Test data management**: How to create and maintain test data

## 26. Final Implementation Checklist

Before considering the testing implementation complete:

- [ ] Basic test project structure set up
- [ ] Core component tests implemented
- [ ] CI integration configured
- [ ] Test coverage reporting working
- [ ] Documentation updated
- [ ] Team trained on testing approach

## 8. CI/CD Integration

### 8.1 GitHub Actions Workflow

Create a `.github/workflows/tests.yml` file:

```yaml
name: Unit Tests

on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main, develop ]

jobs:
  test:
    runs-on: ubuntu-latest
    
    steps:
    - uses: actions/checkout@v3
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: 6.0.x
        
    - name: Restore dependencies
      run: dotnet restore
      
    - name: Build
      run: dotnet build --no-restore
      
    - name: Test
      run: dotnet test --no-build --verbosity normal --collect:"XPlat Code Coverage"
      
    - name: Upload coverage reports
      uses: codecov/codecov-action@v3
```

### 8.2 Test Coverage Goals

- Phase 1: Achieve 50% code coverage for core components
- Phase 2: Increase to 70% coverage for all components
- Phase 3: Maintain 80%+ coverage for critical components

## 24. Test Data Management Best Practices

### 24.1 API Response Data

- Keep API response JSON files small and focused
- Include only the fields needed for testing
- Use consistent naming conventions

Example minimal track response:
```json
{
  "id": "12345",
  "title": "Test Track",
  "artist": {
    "id": "67890",
    "name": "Test Artist"
  },
  "album": {
    "id": "54321",
    "title": "Test Album"
  },
  "duration": 180,
  "trackNumber": 1
}
```

### 24.2 Audio Test Data

- Don't store large audio files in the repository
- Use tiny (1-2 second) audio samples for testing
- Consider generating synthetic audio data programmatically

### 24.3 Test Data Factory

```csharp
public static class TestDataFactory
{
    public static List<IDownloadItem> CreateTestQueue(int count = 5)
    {
        var result = new List<IDownloadItem>();
        for (int i = 0; i < count; i++)
        {
            result.Add(MockFactory.CreateMockDownloadItem(
                id: $"track-{i}",
                title: $"Test Track {i}"
            ));
        }
        return result;
    }
    
    public static TidalSettings CreateTestSettings(
        bool naturalBehavior = false,
        int maxDownloadsPerHour = 10)
    {
        return new TidalSettings
        {
            MaxDownloadsPerHour = maxDownloadsPerHour,
            EnableNaturalBehavior = naturalBehavior,
            TrackToTrackDelayMin = naturalBehavior ? 3 : 0,
            TrackToTrackDelayMax = naturalBehavior ? 10 : 0
        };
    }
}
```

## 25. Asynchronous Testing Best Practices

Since the plugin uses async/await extensively, follow these practices for testing asynchronous code:

### 25.1 Use Async Test Methods

```csharp
[Test]
public async Task DownloadTrack_WithValidId_CompletesSuccessfully()
{
    // Arrange
    var client = new TidalApiClient(_httpClient, _session);
    
    // Act
    var result = await client.DownloadTrackAsync("12345", "path/to/save");
    
    // Assert
    Assert.That(result, Is.Not.Null);
}
```

### 25.2 Test Timeouts

Add timeouts to prevent tests from hanging indefinitely:

```csharp
[Test, Timeout(5000)]
public async Task RefreshToken_WhenSessionExpired_GetsNewToken()
{
    // Test implementation
}
```

### 25.3 Testing Cancellation

Test that operations respect cancellation tokens:

```csharp
[Test]
public async Task DownloadTrack_WhenCancelled_StopsDownload()
{
    // Arrange
    var client = new TidalApiClient(_httpClient, _session);
    var cts = new CancellationTokenSource();
    
    // Act
    var downloadTask = client.DownloadTrackAsync("12345", "path/to/save", cts.Token);
    cts.Cancel();
    
    // Assert
    await Assert.ThrowsAsync<OperationCanceledException>(
        async () => await downloadTask);
}
```

### 25.4 Avoid Thread.Sleep

Instead of Thread.Sleep, use Task.Delay with async/await:

```csharp
// Bad
[Test]
public void ProcessQueue_AddsDelay()
{
    queue.StartProcessing();
    Thread.Sleep(1000); // Unreliable
    Assert.That(queue.IsProcessing);
}

// Good
[Test]
public async Task ProcessQueue_AddsDelay()
{
    queue.StartProcessing();
    await Task.Delay(100);
    Assert.That(queue.IsProcessing);
}
```

## 26. Parameterized Tests

Use parameterized tests to reduce code duplication and test multiple scenarios:

### 26.1 Basic Parameterized Tests

```csharp
[Test]
[TestCase("FLAC", AudioQuality.HiFi)]
[TestCase("MP3_320", AudioQuality.High)]
[TestCase("MP3_128", AudioQuality.Low)]
public void MapQualityString_ReturnsCorrectEnum(string qualityString, AudioQuality expected)
{
    // Arrange
    var mapper = new QualityMapper();
    
    // Act
    var result = mapper.MapQualityString(qualityString);
    
    // Assert
    Assert.That(result, Is.EqualTo(expected));
}
```

### 26.2 Complex Test Cases

```csharp
public static IEnumerable<TestCaseData> ErrorResponseTestCases()
{
    yield return new TestCaseData(
        401, 
        "Unauthorized", 
        typeof(AuthenticationException)
    ).SetName("Unauthorized_ThrowsAuthenticationException");
    
    yield return new TestCaseData(
        404, 
        "Not Found", 
        typeof(ResourceNotFoundException)
    ).SetName("NotFound_ThrowsResourceNotFoundException");
    
    yield return new TestCaseData(
        429, 
        "Too Many Requests", 
        typeof(RateLimitExceededException)
    ).SetName("RateLimit_ThrowsRateLimitExceededException");
}

[Test]
[TestCaseSource(nameof(ErrorResponseTestCases))]
public void HandleErrorResponse_ThrowsCorrectException(
    int statusCode, string message, Type expectedExceptionType)
{
    // Arrange
    var response = new HttpResponseMessage((HttpStatusCode)statusCode)
    {
        Content = new StringContent($"{{\"message\":\"{message}\"}}")
    };
    
    // Act & Assert
    Assert.That(() => _client.HandleErrorResponse(response), 
        Throws.Exception.TypeOf(expectedExceptionType));
}
```

## 28. Implementation Progress

### 28.1 Completed
- ✅ Created test project structure
- ✅ Added test helper classes
- ✅ Created sample test data
- ✅ Implemented first authentication tests
- ✅ Implemented basic queue tests

### 28.2 Next Steps
- Implement API client tests
- Implement downloader tests
- Implement persistence tests
- Set up CI workflow
- Add code coverage reporting

### 28.3 Running the Tests

To run the implemented tests:

```bash
# Navigate to the test project
cd src/Lidarr.Plugin.Tidal.Tests

# Run all tests
dotnet test

# Run with coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=html
```

## 29. Bitrate Calculation Tests

The plugin should correctly calculate the bitrate based on the quality setting:

```csharp
[Test]
[TestCase(AudioQuality.Low, 96)]
[TestCase(AudioQuality.Medium, 320)]
[TestCase(AudioQuality.High, 1411)]
[TestCase(AudioQuality.Master, 2304)]
public void GetBitrate_ForQualitySetting_ReturnsCorrectValue(AudioQuality quality, int expectedBitrate)
{
    // Arrange
    var settings = TestDataHelper.CreateDefaultSettings();
    settings.Quality = quality;
    
    // Act
    var result = new BitrateCalculator().GetBitrateForQuality(settings);
    
    // Assert
    Assert.That(result, Is.EqualTo(expectedBitrate));
}
```
