# Lidarr Tidal Plugin: Technical Design Document

## Table of Contents
1. [System Architecture](#1-system-architecture)
2. [Core Component Design](#2-core-component-design)
3. [Database Schema Design](#3-database-schema-design)
4. [API Design](#4-api-design)
5. [User Interface Design](#5-user-interface-design)
6. [Security Considerations](#6-security-considerations)
7. [Performance Optimization](#7-performance-optimization)
8. [Implementation Roadmap](#8-implementation-roadmap)
9. [Testing Strategy](#9-testing-strategy)
10. [Deployment Strategy](#10-deployment-strategy)

## 1. System Architecture

### 1.1 Current Architecture Overview
The Lidarr Tidal Plugin is structured around several key components:

- **Download Management**: Centered on the `DownloadTaskQueue` class which handles the core download functionality
- **API Integration**: Interfaces with Tidal's API for content discovery and retrieval
- **Processing Pipeline**: Handles audio format conversion and metadata embedding
- **Integration Layer**: Connects with Lidarr's core functionality

### 1.2 Proposed Architecture Enhancements
CopyInsert
┌─────────────────────────────────────────────────────────────┐
│                      Lidarr Core System                      │
└───────────────────────────┬─────────────────────────────────┘
                            │
┌───────────────────────────▼─────────────────────────────────┐
│                    Plugin Integration Layer                  │
└───────────────────────────┬─────────────────────────────────┘
                            │
┌───────────────────────────▼─────────────────────────────────┐
│                      Tidal Plugin Core                       │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │ API Service │  │  Download   │  │ Processing Pipeline │  │
│  │             │◄─┤   Manager   ├─►│                     │  │
│  └─────────────┘  └─────────────┘  └─────────────────────┘  │
│         ▲                 ▲                    ▲            │
└─────────┼─────────────────┼────────────────────┼────────────┘
          │                 │                    │
┌─────────▼─────┐  ┌────────▼────────┐  ┌───────▼────────────┐
│  Tidal API    │  │ Download Viewer │  │ Configuration UI   │
│  Integration  │  │                 │  │                    │
└───────────────┘  └─────────────────┘  └────────────────────┘
### 1.2 Enhanced Architecture Components

#### 1.2.1 API Resilience Layer
The API Resilience Layer will provide a robust buffer against Tidal API changes and limitations:

- **Circuit Breaker Pattern**: Prevents cascading failures by temporarily disabling operations after consecutive errors
- **Session Management**: Handles token refresh, expiration, and geographic optimization
- **Response Parsing**: Implements tolerant parsing to handle minor API changes
- **Degradation Strategies**: Defines fallback behaviors when API limits are reached

#### 1.2.2 Quality Selection System
The Adaptive Quality Selection system will intelligently manage content quality:

- **Quality Profiles**: User-configurable profiles defining preferred formats and fallbacks
- **Bandwidth Awareness**: Adjusts quality based on network conditions and time of day
- **Format Negotiation**: Handles format availability and conversion requirements
- **Preview System**: Shows quality options before download commitment

#### 1.2.3 Metadata Enhancement Bridge
The Metadata Bridge ensures seamless integration between Tidal and Lidarr metadata:

- **Field Mapping**: Translates between Tidal and Lidarr metadata schemas
- **Genre Normalization**: Converts Tidal-specific genres to Lidarr standards
- **Extended Tags**: Preserves Tidal-exclusive metadata in extended tag fields
- **Completeness Scoring**: Evaluates metadata quality and completeness

#### 1.2.4 Download Integrity System
The Download Integrity System ensures content reliability:

- **Verification Service**: Performs checksum validation on completed downloads
- **Recovery Manager**: Handles partial file recovery for interrupted downloads
- **Reporting Engine**: Generates detailed integrity reports for problematic files
- **Repair Service**: Attempts automatic fixes for common integrity issues
## 2. Core Component Design

### 2.1 Download Management System

#### 2.1.1 Current Implementation

The `DownloadTaskQueue` class is the central component of the download management system, located at `src/Lidarr.Plugin.Tidal/Download/Clients/Tidal/Queue/DownloadTaskQueue.cs`. This class orchestrates the entire download process with the following key functionalities:

**Queue Management:**
- Implements a thread-safe concurrent download queue using `ConcurrentQueue<IDownloadItem>`
- Maintains separate queues for pending, active, and completed downloads
- Uses semaphore-based concurrency control via `SemaphoreSlim` to limit parallel downloads
- Default concurrency limit is configurable through settings (typically 5 concurrent downloads)

**Rate Limiting:**
- Implements the token bucket algorithm for API rate limiting
- Uses a time-based token allocation system (e.g., 50 tokens per hour)
- Each download operation consumes tokens based on its complexity
- Implements backoff strategies when rate limits are approached

**Download Process:**
- Main processing loop in `ProcessQueueAsync()` that continuously processes queue items
- Individual item processing in `ProcessItemAsync(IDownloadItem item)` with the following steps:
  1. Authentication validation and token refresh if needed
  2. Content metadata retrieval from Tidal API
  3. Audio stream download with progress tracking
  4. Format conversion if necessary (e.g., M4A to FLAC)
  5. Metadata embedding (album art, artist info, etc.)
  6. File system operations for saving processed content

**Error Handling:**
- Implements retry logic with exponential backoff for transient errors
- Categorizes errors into recoverable and non-recoverable types
- Maintains error statistics for monitoring and reporting
- Implements circuit breaker pattern for API failures

**State Persistence:**
- Serializes queue state to disk at configurable intervals
- Implements recovery mechanism for interrupted downloads
- Uses JSON serialization for queue state with custom converters
- Maintains a status file for each download with detailed progress information

**Event System:**
- Raises events for download state changes:
  - `DownloadStarted`
  - `DownloadProgress`
  - `DownloadCompleted`
  - `DownloadFailed`
- Implements `IObservable<DownloadEvent>` pattern for subscribers

**Code Sample (Simplified):**
```csharp
// Core processing method for download items
private async Task<bool> ProcessItemAsync(IDownloadItem item)
{
    try
    {
        // Update item status
        item.Status = DownloadItemStatus.Processing;
        _eventAggregator.PublishEvent(new DownloadStartedEvent(item));
        
        // Validate authentication
        if (!await EnsureAuthenticationAsync())
        {
            _logger.Error("Authentication failed, cannot process item {0}", item.Id);
            item.Status = DownloadItemStatus.Failed;
            item.ErrorMessage = "Authentication failed";
            return false;
        }
        
        // Retrieve metadata if needed
        if (item.Metadata == null || !item.Metadata.IsComplete)
        {
            await RetrieveMetadataAsync(item);
        }
        
        // Download content
        var downloadResult = await DownloadContentAsync(item, 
            progress => _eventAggregator.PublishEvent(
                new DownloadProgressEvent(item, progress)));
        
        if (!downloadResult.Success)
        {
            _logger.Error("Download failed for item {0}: {1}", 
                item.Id, downloadResult.ErrorMessage);
            item.Status = DownloadItemStatus.Failed;
            item.ErrorMessage = downloadResult.ErrorMessage;
            return false;
        }
        
        // Process content (format conversion, metadata embedding)
        var processingResult = await ProcessContentAsync(
            item, downloadResult.FilePath);
        
        if (!processingResult.Success)
        {
            _logger.Error("Processing failed for item {0}: {1}", 
                item.Id, processingResult.ErrorMessage);
            item.Status = DownloadItemStatus.Failed;
            item.ErrorMessage = processingResult.ErrorMessage;
            return false;
        }
        
        // Complete download
        item.Status = DownloadItemStatus.Completed;
        item.CompletedDate = DateTime.UtcNow;
        _eventAggregator.PublishEvent(new DownloadCompletedEvent(item));
        return true;
    }
    catch (Exception ex)
    {
        _logger.Error(ex, "Unhandled exception processing item {0}", item.Id);
        item.Status = DownloadItemStatus.Failed;
        item.ErrorMessage = $"Unhandled exception: {ex.Message}";
        _eventAggregator.PublishEvent(new DownloadFailedEvent(item, ex));
        return false;
    }
}

Performance Considerations:

Implements adaptive throttling based on system load
Uses memory-efficient streaming for large files
Implements cancellation support throughout the pipeline
Monitors and logs performance metrics for optimization
Integration Points:

Interfaces with ITidalApiClient for content retrieval
Uses IAudioProcessor for format conversion and metadata handling
Integrates with Lidarr's event system via IEventAggregator
Utilizes IConfigurationService for settings management

3. Database Schema Design
3.1 Current Schema
Basic download queue persistence
Limited metadata storage
Simple configuration tables
3.2 Proposed Schema Enhancements
CopyInsert
┌───────────────────┐      ┌───────────────────┐
│  DownloadItems    │      │  DownloadHistory  │
├───────────────────┤      ├───────────────────┤
│ Id (PK)           │      │ Id (PK)           │
│ Title             │      │ DownloadItemId    │
│ Artist            │──┐   │ StartTime         │
│ Album             │  │   │ EndTime           │
│ Status            │  │   │ Status            │
│ Progress          │  │   │ ErrorDetails      │
│ QualityProfile    │  │   └───────────────────┘
└───────────────────┘  │
                       │   ┌───────────────────┐
                       └──►│  ArtistMetadata   │
                           ├───────────────────┤
                           │ Id (PK)           │
                           │ ArtistId          │
                           │ Name              │
                           │ Biography         │
                           │ ImageUrl          │
                           └───────────────────┘
4. API Design
4.1 Internal APIs
4.1.1 Download Management API
csharp
CopyInsert
public interface IDownloadManager
{
    Task<Guid> QueueDownloadAsync(DownloadRequest request);
    Task<bool> PauseDownloadAsync(Guid id);
    Task<bool> ResumeDownloadAsync(Guid id);
    Task<bool> CancelDownloadAsync(Guid id);
    Task<DownloadStatus> GetDownloadStatusAsync(Guid id);
    Task<IEnumerable<DownloadSummary>> GetActiveDownloadsAsync();
    Task<DownloadStatistics> GetDownloadStatisticsAsync();
}
4.1.2 Content Discovery API
csharp
CopyInsert
public interface IContentDiscoveryService
{
    Task<SearchResults> SearchAsync(SearchRequest request);
    Task<ArtistDetails> GetArtistDetailsAsync(string artistId);
    Task<AlbumDetails> GetAlbumDetailsAsync(string albumId);
    Task<TrackDetails> GetTrackDetailsAsync(string trackId);
    Task<IEnumerable<AlbumSummary>> GetNewReleasesAsync();
    Task<IEnumerable<PlaylistSummary>> GetFeaturedPlaylistsAsync();
}
4.2 External APIs
4.2.1 Plugin API for Third-Party Integration
csharp
CopyInsert
[ApiController]
[Route("api/v1/tidal")]
public class TidalPluginApiController : ControllerBase
{
    [HttpGet("status")]
    public ActionResult<PluginStatus> GetStatus();
    
    [HttpGet("downloads")]
    public ActionResult<IEnumerable<DownloadSummary>> GetDownloads();
    
    [HttpPost("downloads")]
    public ActionResult<Guid> QueueDownload(DownloadRequest request);
    
    [HttpGet("statistics")]
    public ActionResult<DownloadStatistics> GetStatistics();
}
5. User Interface Design
5.1 Download Viewer
5.1.1 Component Structure
CopyInsert
┌─────────────────────────────────────────────────────────────┐
│ Download Queue                                     [Refresh] │
├─────────────────────────────────────────────────────────────┤
│ ┌─────────┐ ┌───────────────────────────────────────────┐   │
│ │         │ │ Album Title                               │   │
│ │ Album   │ │ Artist Name                               │   │
│ │ Art     │ │ Quality: FLAC                             │   │
│ │         │ │ [▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓] 75%    │   │
│ └─────────┘ │ [Pause] [Cancel] [Priority ▼]             │   │
│             └───────────────────────────────────────────┘   │
│                                                             │
│ ┌─────────┐ ┌───────────────────────────────────────────┐   │
│ │         │ │ Another Album                             │   │
│ │ Album   │ │ Another Artist                            │   │
│ │ Art     │ │ Quality: MP3 320                          │   │
│ │         │ │ [▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓] 75%    │   │
│ └─────────┘ │ [Pause] [Cancel] [Priority ▼]             │   │
│             └───────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
5.1.2 Interaction Flow
User views download queue
User can pause/resume/cancel individual downloads
User can adjust priority of downloads
Real-time updates via SignalR
5.2 Configuration Interface
5.2.1 Component Structure
CopyInsert
┌─────────────────────────────────────────────────────────────┐
│ Tidal Plugin Configuration                         [Save]    │
├─────────────────────────────────────────────────────────────┤
│ Authentication                                              │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ Username: [                                           ] │ │
│ │ Password: [                                           ] │ │
│ │ [Test Connection]                                       │ │
│ └─────────────────────────────────────────────────────────┘ │
│                                                             │
│ Download Settings                                           │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ Max Concurrent Downloads: [   5   ] ▲▼                  │ │
│ │ Download Rate Limit: [   50   ] ▲▼ downloads per hour   │ │
│ │ Default Quality: [FLAC ▼]                               │ │
│ │ Download Location: [/music/tidal                      ] │ │
│ └─────────────────────────────────────────────────────────┘ │
│                                                             │
│ Processing Settings                                         │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ [x] Embed Artwork                                       │ │
│ │ [x] Extract Lyrics                                      │ │
│ │ [x] Convert to MP3 if FLAC unavailable                  │ │
│ │ [ ] Use AI-based prioritization                         │ │
│ └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
5.3 Beets Integration Module - Pragmatic Approach

#### 5.3.1 Architecture Overview

The Beets Integration Module will provide advanced metadata tagging capabilities by leveraging the Beets music library management system as a pre-processing step before files are handed off to Lidarr.

```
┌─────────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│                     │     │                 │     │                 │
│  Download Manager   │────►│  Beets Tagger   │────►│  Lidarr Import  │
│                     │     │                 │     │                 │
└─────────────────────┘     └─────────────────┘     └─────────────────┘
```

#### 5.3.2 External Beets Integration - Phase 1

The initial implementation will use Beets as an external dependency:

1. **Dependency Detection**:
   - Check for existing Beets installation
   - Validate Beets version compatibility
   - Verify required plugins are available

2. **Setup Assistance**:
   - Provide step-by-step installation guide
   - Create setup wizard with platform-specific instructions
   - Validate installation success

3. **Command-line Execution**:
   - Execute Beets via command-line interface
   - Parse command output for status and errors
   - Implement timeout and cancellation support

#### 5.3.3 Tagging Pipeline

The tagging pipeline will consist of the following components:

1. **Pre-processing Queue**:
   - Intercept completed downloads before Lidarr notification
   - Queue items for Beets processing
   - Track processing status

2. **Beets Command Generation**:
   - Generate appropriate Beets commands based on configuration
   - Create temporary configuration files as needed
   - Handle path and special character escaping

3. **Post-processing Handoff**:
   - Verify successful tagging
   - Update file paths if renamed by Beets
   - Notify Lidarr of processed files

#### 5.3.4 Configuration Interface

The Beets integration will provide a simple but effective configuration UI:

1. **Basic Settings**:
   - Beets executable path
   - Default import options
   - Enable/disable automatic processing

2. **Plugin Configuration**:
   - Enable/disable common plugins
   - Basic plugin settings

3. **Processing Options**:
   - Write tags to files
   - Copy/move files
   - Handle artwork

#### 5.3.5 Implementation Details

1. **Process Management**:
   - Execute Beets as separate process
   - Implement timeout handling
   - Capture stdout/stderr for logging

2. **Error Handling**:
   - Detect common Beets errors
   - Implement retry logic for transient issues
   - Provide clear error messages to users

3. **Fallback Mechanism**:
   - Skip Beets processing if errors occur
   - Continue normal Lidarr workflow
   - Log issues for troubleshooting

#### 5.3.6 Future Enhancements (Phase 2)

For future iterations, consider these enhancements:

1. **Automated Installation**:
   - Package manager integration for common platforms
   - Plugin installation automation
   - Configuration template system

2. **Enhanced UI**:
   - Metadata preview and comparison
   - Manual tag editing interface
   - Batch processing controls

3. **Advanced Configuration**:
   - Genre-specific profiles
   - Custom plugin support
   - Advanced matching options
6. Security Considerations
6.1 Authentication
Secure storage of Tidal credentials using encryption
Implementation of OAuth for third-party integrations
Session token management with secure refresh mechanisms
6.2 Data Protection
Encryption of sensitive configuration data
Secure storage of download history
Privacy-focused logging (no PII in logs)
6.3 Access Control
Role-based access for plugin features
API key management for external integrations
Audit logging for security-relevant operations
7. Performance Optimization

### 7.1 Download Optimization

The download system implements several optimization strategies to ensure efficient operation:

#### 7.1.1 Adaptive Concurrency
- **Dynamic scaling**: Adjusts concurrent downloads based on system load
- **Resource monitoring**: Tracks CPU, memory, and network utilization
- **Auto-throttling**: Reduces concurrency when system resources are constrained
- **Implementation**: Uses a feedback loop in `DownloadTaskQueue` to monitor performance metrics

#### 7.1.2 Intelligent Chunking
- **Variable chunk sizes**: Adapts chunk size based on file type and network conditions
- **Resumable downloads**: Supports partial downloads with byte range requests
- **Parallel chunk processing**: Downloads multiple chunks simultaneously for large files
- **Memory management**: Uses buffer pooling to reduce GC pressure

#### 7.1.3 Background Processing
- **Task isolation**: Runs downloads in background threads to minimize UI impact
- **Priority management**: Assigns priorities to different download types
- **Cancellation support**: Implements cooperative cancellation throughout the pipeline
- **Progress reporting**: Provides real-time progress updates without blocking

### 7.2 Memory Management

#### 7.2.1 Buffer Management
- **Pooled buffers**: Reuses memory buffers for file operations
- **Size optimization**: Adjusts buffer sizes based on file characteristics
- **Disposal patterns**: Ensures proper cleanup of unmanaged resources
- **Memory pressure detection**: Monitors for low memory conditions

#### 7.2.2 Stream Processing
- **Streaming API**: Uses streams instead of loading entire files into memory
- **Pipeline architecture**: Processes data in small chunks through transformation stages
- **Lazy loading**: Defers loading of metadata until needed
- **Incremental processing**: Handles large files in manageable segments

#### 7.2.3 Garbage Collection Optimization
- **Generation management**: Minimizes gen2 collections through object lifecycle management
- **Weak references**: Uses weak references for cacheable but recreatable objects
- **Disposal patterns**: Implements IDisposable throughout the codebase
- **Memory profiling**: Regular profiling to identify and fix memory leaks

### 7.3 Concurrency Model

The plugin implements a sophisticated concurrency model to ensure efficient operation while maintaining system stability:

#### 7.3.1 Thread Synchronization
- **Modern Async Patterns**: Uses AsyncLock and other modern synchronization primitives
- **Cancellation Propagation**: Implements comprehensive cancellation token support throughout all async operations
- **Deadlock Prevention**: Employs structured task scheduling to prevent deadlocks
- **Resource-Aware Threading**: Dynamically adjusts thread usage based on system capabilities

#### 7.3.2 Task Coordination
- **Task Prioritization**: Assigns priorities to different types of operations
- **Cooperative Scheduling**: Implements yield points in long-running operations
- **Progress Reporting**: Non-blocking progress updates via IProgress<T>
- **Graceful Shutdown**: Ensures clean task termination during system shutdown

### 7.4 Configuration Management

The configuration system is designed for flexibility, safety, and user experience:

#### 7.4.1 Configuration Architecture
- **Strongly-Typed Configuration**: All settings use strongly-typed objects with validation
- **Hierarchical Structure**: Organizes settings in logical groups for easier management
- **Default Profiles**: Provides pre-configured profiles for common use cases
- **Runtime Updates**: Supports changing configuration without restart

#### 7.4.2 Configuration Persistence
- **Versioned Storage**: Maintains configuration version for migration support
- **Validation Layer**: Validates all configuration changes before applying
- **Backup System**: Automatically backs up configuration before significant changes
- **Import/Export**: Supports exporting and importing configuration

### 7.5 State Management

The state management system ensures reliable operation across sessions:

#### 7.5.1 Download State Machine
- **Formal State Transitions**: Implements a formal state machine for download operations
- **Atomic Updates**: Ensures state transitions are atomic and consistent
- **Validation Rules**: Enforces validation rules for all state changes
- **Audit Trail**: Maintains history of state transitions for troubleshooting

#### 7.5.2 Persistence Strategy
- **Incremental Persistence**: Saves state incrementally to prevent data loss
- **Corruption Protection**: Uses transactional writes to prevent corruption
- **Recovery Mechanisms**: Implements multiple recovery strategies for interrupted operations
- **Consistency Checking**: Validates state consistency on startup and after errors

### 7.3 Storage Optimization
Intelligent caching of frequently accessed content
Compression of metadata
Cleanup of temporary files
8. Implementation Roadmap

### 8.1 Phase 1: Core Enhancements (Q1-Q2 2023)

#### 8.1.1 Refactor DownloadTaskQueue
- **Strategy pattern**: Implement pluggable download strategies
- **Dependency injection**: Remove hard dependencies
- **Interface-based design**: Create clear contracts for components
- **Unit testing**: Comprehensive test coverage for core functionality

#### 8.1.2 Enhanced Rate Limiting
- **Token bucket implementation**: Sophisticated rate limiting algorithm
- **Adaptive throttling**: Adjusts based on API response headers
- **Jitter implementation**: Prevents thundering herd problems
- **Backoff strategies**: Exponential backoff for error conditions

#### 8.1.3 Download Viewer UI
- **Real-time updates**: Live progress reporting
- **Filtering capabilities**: Sort and filter by various criteria
- **Action buttons**: Pause, resume, cancel operations
- **Detailed view**: Expandable entries with detailed information

### 8.2 Phase 2: Advanced Features (Q3-Q4 2023)

#### 8.2.1 Adaptive Quality Processing
- **Quality detection**: Automatically detect optimal quality settings
- **Format conversion**: Smart conversion based on user preferences
- **Metadata preservation**: Ensure metadata integrity during conversion
- **Quality verification**: Validate output quality matches expectations

#### 8.2.2 Enhanced Metadata Handling
- **Extended tag support**: Support for additional metadata fields
- **Artwork embedding**: High-quality artwork with format optimization
- **Lyrics integration**: Support for synchronized lyrics
- **Genre normalization**: Consistent genre tagging across libraries
9. Testing Strategy

### 9.1 Unit Testing
- Component-level tests for all core functionality
- Mocking of external dependencies
- Coverage targets of 80%+
- **Testing frameworks**: NUnit, Moq, FluentAssertions
- **CI integration**: Automated test runs on PR submission
- **Test data**: Synthetic test data sets for various scenarios

### 9.2 Integration Testing
- End-to-end tests for download pipeline
- API integration tests with mock Tidal API
- UI testing with Selenium
- **Test environments**: Development, Staging, Production-like
- **Integration points**: Verify all external service interactions

### 9.3 Performance Testing
- Load testing for concurrent downloads
- Memory profiling under heavy load
- Network throttling tests
- **Tools**: JMeter for load testing, dotMemory for profiling
- **Metrics**: Response time, throughput, memory usage
- **Benchmarks**: Establish baseline performance metrics
10. Deployment Strategy
10.1 Release Management
Semantic versioning
Release notes generation
Automated packaging
10.2 Distribution
NuGet package for Lidarr integration
Docker container for standalone testing
GitHub releases for distribution
10.3 Monitoring
Telemetry for performance monitoring
Error reporting with stack traces
Usage analytics for feature prioritization
