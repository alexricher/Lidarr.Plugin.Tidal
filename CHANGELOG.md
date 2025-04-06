# Changelog

All notable changes to this project will be documented in this file.

## [10.0.2.3] - 2024-04-06

### Added
- **Smart Pagination System**: Implemented sophisticated search pagination that adapts to different search contexts
  - Added search intent detection to classify searches as artist discovery, discography, or specific album searches
  - Added result metrics tracking to measure search effectiveness
  - Implemented diminishing returns detection to stop pagination when additional results become less valuable
  - Added search thoroughness levels for user control over search depth
- **Improved Settings Organization**:
  - Reorganized settings into logical sections for better user experience
  - Added clearer help text for all settings
  - Improved settings validation
  - Enhanced settings explanations with performance impact details and recommendations for optimal configuration
- **Performance Optimizations**:
  - Enhanced rate limiting with more efficient token refresh
  - Reduced API delay from 500ms to 200ms for faster searching
  - Added HashSet for processed album IDs to prevent duplicate processing
  - Implemented asynchronous track album processing for better performance
  - Optimized search state tracking across multiple requests
  - Added token check caching to reduce token bucket contention
  - Implemented fast paths for common rate limiting scenarios
  - Added result caching for expensive calculations
  - Improved token bucket efficiency with optimized refill algorithm
- **Unified Country Code Management**:
  - Standardized country code handling across both indexer and download client
  - Added dropdown with common country selections for easier configuration
  - Implemented ISO validation for custom country codes
  - Created centralized country code service for consistent regional content access
- **Queue Priority System**:
  - Added download priority feature with High, Normal, and Low priority levels
  - Implemented priority-based queue processing to handle important downloads first
  - Added automated priority assignment based on content type (new releases vs backlog)
  - Added ability to manually change priorities through API or UI
  - Created configurable default priorities in settings for different download scenarios
  - Added priority visualization in logging and status reporting
  - Implemented priority inheritance for grouped downloads
- **Enhanced Queue Persistence**:
  - Added automatic periodic saving of queue state every 2 minutes
  - Implemented proper shutdown persistence to preserve queue across restarts
  - Added robust file access validation with permission checking
  - Created backup system for queue JSON files with recovery options
  - Improved error handling with detailed diagnostics for queue persistence issues
  - Implemented multiple backup versions with rotation
  - Added atomic file operations for crash resilience
  - Implemented file integrity verification with JSON validation
  - Added smart auto-save interval adjustment based on processing delay settings
  - Created centralized error recovery with multiple fallback paths
  - Added path validation with test file writes to verify permissions
  - Implemented proper application startup validation of persistence paths
  - Added dynamic settings updates with path reinitialization
  - Created comprehensive logging for persistence operations
  - Added automatic backup creation before saving
  - Implemented thread-safe file operations with proper locking
  - Added retry logic with exponential backoff for transient errors
- **Configurable Item Processing Delay**: Added a setting to control the delay between download item processing, helping to reduce resource contention and prevent search timeouts
- **Improved Natural Behavior Controls**: Added ability to toggle natural behavior for queue processing independently of other natural behavior features
- **Thread Safety Enhancements**:
  - Replaced List with ConcurrentBag for thread-safe item collections
  - Replaced Dictionary with ConcurrentDictionary for better concurrency
  - Improved lock-free operations with GetOrAdd for semaphores
  - Added proper exception handling in concurrent operations
  - Reduced lock contention with more granular locking
- **Memory Management Optimization**:
  - Implemented BoundedCollection to prevent unbounded memory growth
  - Added memory usage tracking and reporting
  - Optimized collection operations to reduce memory pressure
  - Implemented thread-safe counters with Interlocked operations
  - Added optional memory usage diagnostics
- **Error Handling Refinements**:
  - Added specific exception handling for network, I/O, and permission errors
  - Implemented automatic recovery from file system errors
  - Enhanced retry logic with exponential backoff
  - Added last-chance exception handlers to prevent queue failures
  - Improved error diagnostic information

### Changed
- Improved search queue processing with multiple worker tasks to better utilize MaxConcurrentSearches setting
- Enhanced logging to provide more detailed information about active searches and available slots
- Improved search request generation with smarter rate limiting
- Enhanced country code handling with better defaults
- Reduced token refresh window from 2 to 5 minutes
- Modified caching mechanism to be more selective about what gets cached
- Made the pagination system more context-aware
- Updated settings UI with better organization and descriptions
- Improved queue persistence reliability with file locking and validation
- Made rate limiting more intelligent with adaptive backoff for 429 responses
- Refactored DownloadTaskQueue for better concurrency handling
- Improved QueuePersistenceManager with more robust file operations
- Enhanced TokenBucketRateLimiter with caching and fast paths
- Reduced contention in high-throughput scenarios
- Made file operations more resilient to crashes and interruptions
- Improved diagnostics for memory usage and performance bottlenecks
- Consolidated multiple rate limiter implementations into a single UnifiedRateLimiter with improved performance

### Fixed
- Fixed issue with parallel search requests not fully utilizing configured concurrency limits
- Fixed rate limiter settings to properly use indexer's MaxRequestsPerMinute for search operations
- Fixed potential memory leaks in search state tracking
- Improved error handling in the request generator
- Enhanced circuit breaker reliability
- Fixed potential concurrency issues in the parser
- Improved token bucket rate limiter to handle bursts better
- Fixed queue persistence issues that could result in empty queue files
- Added proper thread safety for file operations to prevent file corruption
- Implemented fallback paths for queue persistence when primary path is unavailable
- Fixed potential race conditions in queue processing
- Improved handling of file system errors during persistence
- Enhanced handling of network errors during downloads
- Made token bucket more resistant to bursts of requests
- Improved recovery from network failures with better circuit breaker patterns
- Fixed memory leak in statistics tracking for large download operations
- Fixed build error by updating method call from GetWaitTime() to GetEstimatedWaitTime() in UnifiedRateLimiter.cs
- Fixed build error by adding missing properties for Smart Pagination in TidalIndexerSettings class
- Fixed queue persistence auto-save functionality by implementing a 2-minute timer that was missing in the original code
- Fixed TaskCanceledException issue during concurrent searching and downloading operations

## [10.0.2.2] - 2024-04-04

### Added
- **Queue Persistence**: Added functionality to save and restore the download queue across Lidarr restarts
  - Added a configurable path for queue persistence files
  - Will fallback to status files path or download path if not specified
  - Can be disabled completely via settings
- **Country Code Management**: Improved handling of country codes for Tidal API
  - Added support for selecting country from predefined list
  - Added support for custom country codes
  - Implemented without modifying external TidalSharp library
- **Time-of-Day Adaptation**: Implemented complete time-of-day adaptation functionality
  - Queue processing respects active hours configuration
  - Download delays increase outside of active hours
  - Detailed logging of time-based behavior
- **Enhanced Status Logging**: Improved status logging for various system states
  - Added periodic status updates during paused states
  - Added human-readable time formatting for ETAs
  - Improved rate limit information with time remaining
  - Enhanced circuit breaker event logging
- **Circuit Breaker Improvements**:
  - Added detailed logging when circuit breaker triggers
  - Added countdown information for when processing will resume
  - Added a background task to periodically log circuit breaker status
- **Size Tracking**: Fixed and improved file size tracking throughout the download pipeline
- **Improved Path Handling**:
  - Added robust path validation for both status and queue persistence paths
  - Implemented test write functionality to verify permissions before committing
  - Added ability to reinitialize paths at runtime when settings change
  - Enhanced error recovery for path-related operations
  - Added detailed diagnostic logging for file access operations

### Fixed
- Fixed calculation of time remaining in rate-limited situations
- Fixed issues with throttling detection and reporting
- Improved error handling in download pipeline
- Enhanced file size estimation and verification
- Fixed potential file access issues with persistence paths
- Fixed null reference exceptions in country code management
- Improved handling of path changes during runtime
- Enhanced robustness of status manager initialization process
- Added retry mechanisms for directory creation operations
- Fixed cross-platform path handling issues

## [10.0.2.1] - 2024-03-25

### Fixed
- Fixed critical issue with TidalProxy initializing DownloadTaskQueue with empty settings, causing status file path to be ignored
- Fixed various issues with status file path validation and directory creation
- Added proper error handling for file access and permissions issues
- Added automatic cleanup of temporary test files after validation
- Added auto-recreation of status directories if deleted while Lidarr is running
- Fixed compilation errors for core plugin compatibility
  - Changed Protocol property from enum to string using nameof(TidalDownloadProtocol)
  - Fixed Test method to properly match base class signature and implementation
  - Removed HideWhen and HideValue attributes that weren't defined in core Lidarr
  - Fixed constructor parameters in TidalClient to match base class
  - Fixed RemoveItem method to properly handle DownloadClientItem parameter
  - Added missing Clone method implementation to TidalSettings
  - Fixed DownloadClientInfo in GetStatus() by removing undefined Status property
  - Added missing properties to TidalSettings class for behavior simulation
  - Fixed SkipProbability conversion in UserBehaviorSimulator with proper float to int cast

### Added
- Added TidalStatusHelper class for centralized status file management
- Added TidalStatusDebugger for generating diagnostic reports
- Created test script for validating status files configuration with cleanup options
- Added robust error handling and retry mechanisms for file operations
- Added detailed logging for status file operations
- Added filtering with download client settings for better understanding
- Added Natural behavior simulation to avoid detection
  - Session-based download patterns
  - Configurable delays between downloads
  - User agent rotation
  - Connection parameter variation
  - High volume handling
- Enhanced audio processing
  - FLAC extraction from M4A containers
  - AAC to MP3 conversion
  - Metadata preservation
  - Album artwork embedding
  - Lyrics support with .lrc files
- Smart rate limiting
  - Exponential backoff for rate limits
  - Request throttling detection
  - Session-based download limits
  - Configurable retry strategies

### Changed
- Improved initialization process to use the user's configured settings
- Enhanced status file validation with more detailed error reporting
- Added automatic backup of corrupted JSON files
- Enhanced test script with options to keep or clean up test files
- Refactored codebase for better maintainability
- Improved configuration options
- Enhanced logging
- Fixed protocol registration issues
- Improved delay profile handling
- Various bug fixes and stability improvements
- Added try-catch blocks to FetchLyricsFromTidal and FetchLyricsFromLRCLIB methods
- Modified the DoTrackDownload method to better handle errors in lyrics fetching

## [10.0.3.33] - 2024-03-19

Initial release of the Tidal plugin for Lidarr.

### Features
- Stream tracks from Tidal directly to Lidarr
- Support for multiple audio quality options
- Natural download scheduling to simulate human behavior
- Status file tracking for download statistics

## Coming Soon
- TidalDownloadViewer web interface
- Advanced audio processing features
- Enhanced metadata handling
- Improved token storage
- Better Lidarr integration

## [10.0.1.x] - Initial Release
- First iteration by the great TrevTV. Thanks to their great work, a plugin to download from Tidal directly from Lidarr is born.
- Basic Tidal integration with Lidarr
- Album and track downloading
- Quality selection
- Basic metadata handling

## Planned Features
### [10.0.3.x]
- TidalDownloadViewer
  - Web-based interface for monitoring downloads
  - Download statistics and analytics
  - Status file visualization
  - Download queue management
