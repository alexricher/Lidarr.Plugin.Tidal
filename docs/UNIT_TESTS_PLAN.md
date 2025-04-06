# Unit Test Plan

## Table of Contents
- [Core Testing Areas](#core-testing-areas)
- [Test-Driven Development Approach](#test-driven-development-approach)
- [Integration Testing](#integration-testing)
- [Continuous Integration Strategy](#continuous-integration-strategy)
- [Test Data Management](#test-data-management)
- [Sample Test Implementations](#sample-test-implementations)

## Core Testing Areas

### 1. Download Pipeline
**Test Suite:** `DownloadTaskQueueTests`

**Key Test Cases:**
- Test queue initialization with various settings
- Test rate limiting functionality
- Test concurrent download management
- Test queue persistence and restoration
- Test error handling and recovery
- Test cancellation scenarios

**Mocking Strategy:**
- Mock `ITidalApiClient` for API responses
- Mock file system operations
- Simulate network conditions

### 2. Audio Processing
**Test Suite:** `AudioProcessingTests`

**Key Test Cases:**
- Test format conversion accuracy
- Test metadata preservation during processing
- Test artwork embedding
- Test lyrics extraction and formatting
- Test quality verification

**Mocking Strategy:**
- Mock audio file operations
- Use sample audio files for conversion testing
- Create synthetic metadata for embedding tests

### 3. API Integration
**Test Suite:** `TidalApiIntegrationTests`

**Key Test Cases:**
- Test authentication flows
- Test error handling for API responses
- Test rate limit detection and handling
- Test country code management
- Test session management

**Mocking Strategy:**
- Create mock Tidal API server
- Simulate various API response scenarios
- Test with different authentication states

### 4. User Interface
**Test Suite:** `TidalDownloadViewerTests`

**Key Test Cases:**
- Test UI rendering with various data sets
- Test real-time updates
- Test filtering and sorting
- Test user interactions (pause/resume/cancel)
- Test responsive design

**Mocking Strategy:**
- Mock backend services
- Create synthetic UI test data
- Simulate user interactions

### 5. Configuration Management
**Test Suite:** `TidalSettingsTests`

**Key Test Cases:**
- Test settings validation
- Test settings persistence
- Test runtime settings changes
- Test default values
- Test boundary conditions

**Mocking Strategy:**
- Mock configuration storage
- Create test configuration scenarios

### 6. API Resilience
**Test Suite:** `ApiResilienceTests`

**Key Test Cases:**
- Test circuit breaker functionality under various failure scenarios
- Test session recovery after token expiration
- Test graceful degradation when rate limits are reached
- Test response parsing tolerance with malformed API responses
- Test geographic optimization for API endpoints

**Mocking Strategy:**
- Simulate various API failure conditions
- Create mock responses with deliberate malformations
- Simulate rate limiting scenarios
- Mock geographic routing responses

### 7. Quality Selection
**Test Suite:** `QualitySelectionTests`

**Key Test Cases:**
- Test fallback logic when preferred quality is unavailable
- Test bandwidth-aware quality switching
- Test quality profile configuration
- Test quality comparison calculations
- Test format conversion decisions

**Mocking Strategy:**
- Create mock quality availability scenarios
- Simulate bandwidth conditions
- Use sample quality profiles
- Mock conversion services

### 8. Metadata Enhancement
**Test Suite:** `MetadataEnhancementTests`

**Key Test Cases:**
- Test supplementing missing metadata fields
- Test genre normalization across different music styles
- Test preservation of Tidal-exclusive metadata
- Test metadata completeness scoring
- Test handling of unusual or edge-case metadata

**Mocking Strategy:**
- Create sample metadata sets with various completeness levels
- Use genre mapping test fixtures
- Create mock Lidarr metadata expectations

## Test-Driven Development Approach
For Each Feature:
Write Failing Tests First
Define expected behavior through tests
Ensure tests fail appropriately before implementation
Implement Minimum Viable Code
Write just enough code to pass the tests
Focus on functionality over optimization initially
Refactor
Improve code quality while maintaining test passing status
Enhance performance, readability, and maintainability

## Integration Testing
Ensure new feature works with existing functionality
Verify no regressions in other areas

## Continuous Integration Strategy
Implement GitHub Actions workflow for automated testing
Set up test coverage reporting
Establish minimum coverage thresholds (aim for 80%+)
Create nightly integration test runs
Implement pre-merge test requirements

## Test Data Management

### Test Data Sources
- **Synthetic Data**: Generated test data for predictable scenarios
- **Sample Files**: Small audio files for processing tests
- **Mock API Responses**: JSON fixtures simulating Tidal API responses
- **Database Fixtures**: Pre-populated test databases

### Data Management Strategy
- Store test data in version control
- Use data factories to generate test cases
- Implement cleanup routines for test data
- Isolate test environments to prevent cross-contamination

### Test Data Versioning
- Version test data alongside code
- Document data format changes
- Provide migration scripts for test data

## Sample Test Implementations for TidalDownloadViewer

```csharp
[TestFixture]
public class TidalDownloadViewerTests
{
    private TidalDownloadViewer _viewer;
    private Mock<IDownloadTaskQueue> _mockQueue;
    private Mock<ITidalStatusHelper> _mockStatusHelper;
    
    [SetUp]
    public void Setup()
    {
        _mockQueue = new Mock<IDownloadTaskQueue>();
        _mockStatusHelper = new Mock<ITidalStatusHelper>();
        _viewer = new TidalDownloadViewer(_mockQueue.Object, _mockStatusHelper.Object);
    }
    
    [Test]
    public void GetActiveDownloads_ReturnsCorrectItems()
    {
        // Arrange
        var testItems = new List<IDownloadItem>
        {
            CreateTestDownloadItem("Item1", DownloadItemStatus.Downloading, 50),
            CreateTestDownloadItem("Item2", DownloadItemStatus.Queued, 0),
            CreateTestDownloadItem("Item3", DownloadItemStatus.Completed, 100)
        };
        
        _mockQueue.Setup(q => q.GetQueueListing()).Returns(testItems.ToArray());
        
        // Act
        var result = _viewer.GetActiveDownloads();
        
        // Assert
        Assert.AreEqual(1, result.Count());
        Assert.AreEqual("Item1", result.First().Title);
    }
    
    [Test]
    public void PauseDownload_CallsQueueWithCorrectId()
    {
        // Arrange
        var itemId = "test-id-123";
        _mockQueue.Setup(q => q.PauseDownload(itemId)).Returns(true);
        
        // Act
        var result = _viewer.PauseDownload(itemId);
        
        // Assert
        Assert.IsTrue(result);
        _mockQueue.Verify(q => q.PauseDownload(itemId), Times.Once);
    }
    
    private IDownloadItem CreateTestDownloadItem(string title, DownloadItemStatus status, float progress)
    {
        var mockItem = new Mock<IDownloadItem>();
        mockItem.Setup(i => i.Title).Returns(title);
        mockItem.Setup(i => i.Status).Returns(status);
        mockItem.Setup(i => i.Progress).Returns(progress);
        return mockItem.Object;
    }
}
```

## Prioritized Test Coverage Strategy

### 1. Download Queue Processing (Critical)
**Test Suite:** `DownloadTaskQueueTests`

**Key Test Cases:**
- Test queue initialization with various configurations
- Test item addition with null/empty/valid keys
- Test concurrent queue operations
- Test circuit breaker activation and recovery
- Test error handling during download processing

**Example Test:**
```csharp
[Test]
public void AddToQueue_WithNullKey_ThrowsArgumentNullException()
{
    // Arrange
    var queue = new DownloadTaskQueue(_mockLogger.Object, _mockConfig.Object);
    var item = CreateDownloadItem(null, "Test Title");
    
    // Act & Assert
    Assert.Throws<ArgumentNullException>(() => queue.AddToQueue(item));
}
```

### 2. API Integration (High Priority)
**Test Suite:** `TidalApiClientTests`

**Key Test Cases:**
- Test authentication flows
- Test API response parsing
- Test rate limiting handling
- Test error recovery mechanisms
- Test token refresh logic

**Mocking Strategy:**
- Use WireMock.NET to simulate Tidal API responses
- Create fixtures for common API responses
- Test with various network conditions

### 3. Download Processing Pipeline (High Priority)
**Test Suite:** `DownloadProcessorTests`

**Key Test Cases:**
- Test metadata extraction
- Test file format handling
- Test progress tracking
- Test cancellation handling
- Test error recovery during downloads

### 4. Error Handling & Resilience (Medium Priority)
**Test Suite:** `ErrorHandlingTests`

**Key Test Cases:**
- Test circuit breaker patterns
- Test retry mechanisms
- Test error logging
- Test graceful degradation
- Test recovery from common failure scenarios

### 5. Configuration Management (Medium Priority)
**Test Suite:** `ConfigurationTests`

**Key Test Cases:**
- Test configuration validation
- Test runtime configuration updates
- Test configuration persistence
- Test migration between versions
- Test default configurations

### 6. State Management (Medium Priority)
**Test Suite:** `StateManagementTests`

**Key Test Cases:**
- Test state transitions
- Test state persistence and recovery
- Test interrupted operations
- Test consistency validation
- Test state machine edge cases

## Implementation Plan

### Phase 1: Core Infrastructure (2 weeks)
- Set up testing framework with NUnit and Moq
- Implement test data factories
- Create base test fixtures
- Establish CI pipeline for tests
- Focus on DownloadTaskQueue tests to fix current issues

### Phase 2: Critical Path Coverage (3 weeks)
- Implement API client tests
- Add download processor tests
- Create integration tests for key workflows
- Target 50% coverage of core components

### Phase 3: Edge Cases & Resilience (2 weeks)
- Add tests for error conditions
- Implement performance tests
- Create stress tests for concurrency
- Add negative test cases
- Target 70% coverage

### Phase 4: UI & Integration (2 weeks)
- Implement UI component tests
- Add end-to-end tests for key user flows
- Create configuration tests
- Target 80%+ coverage

## Test Automation Strategy

### CI Integration:
- Run unit tests on every PR
- Run integration tests nightly
- Generate coverage reports automatically

### Coverage Monitoring:
- Set up coverage gates in CI (reject PRs below threshold)
- Create dashboard for coverage trends
- Implement code coverage annotations in PRs

### Test Data Management:
- Create data generators for common test scenarios
- Implement cleanup routines
- Version test fixtures alongside code

## Immediate Actions to Fix Current Issues

### Add comprehensive null checking in DownloadTaskQueue:
```csharp
public void AddToQueue(IDownloadItem item)
{
    if (item == null) throw new ArgumentNullException(nameof(item));
    if (string.IsNullOrEmpty(item.Id)) throw new ArgumentNullException("item.Id");
    
    // Existing implementation
}
```

### Create targeted tests for the "Value cannot be null. (Parameter 'key')" error:
```csharp
[Test]
public void ProcessQueueItem_WithNullKey_HandlesGracefully()
{
    // Arrange
    var queue = new DownloadTaskQueue(_mockLogger.Object, _mockConfig.Object);
    var problematicItem = CreateProblemItem(); // Create item that would cause the issue
    
    // Act & Assert
    Assert.DoesNotThrow(() => queue.ProcessQueueItem(problematicItem));
}
```
