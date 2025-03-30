# Changelog

All notable changes to this project will be documented in this file.

## [10.0.2.2] - 2024-04-01

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
