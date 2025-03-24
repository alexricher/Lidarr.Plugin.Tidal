# Changelog

## [10.0.2.2] - 2024
### Fixed
- Added filtering with download client settings so it's easier to understand.
- Compilation errors for core plugin compatibility
  - Changed Protocol property from enum to string using nameof(TidalDownloadProtocol)
  - Fixed Test method to properly match base class signature and implementation
  - Removed HideWhen and HideValue attributes that weren't defined in core Lidarr
  - Fixed constructor parameters in TidalClient to match base class
  - Fixed RemoveItem method to properly handle DownloadClientItem parameter
  - Added missing Clone method implementation to TidalSettings
  - Fixed DownloadClientInfo in GetStatus() by removing undefined Status property
  - Added missing properties to TidalSettings class for behavior simulation
  - Fixed SkipProbability conversion in UserBehaviorSimulator with proper float to int cast

## [10.0.2.1] - 2024
### Added
- Natural behavior simulation to avoid detection
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
- Improved error handling
  - Better error messages
  - Graceful network error handling
  - Failed download cleanup
- Basic download status tracking
  - Download progress monitoring
  - Download history
  - Status file generation
  - Groundwork for TidalDownloadViewer

### Changed
- Refactored codebase for better maintainability
- Improved configuration options
- Enhanced logging
- Fixed protocol registration issues
- Improved delay profile handling
- Various bug fixes and stability improvements
- Added try-catch blocks to FetchLyricsFromTidal and FetchLyricsFromLRCLIB methods in Downloader.cs to catch and handle API exceptions.
  - Modified the DoTrackDownload method in DownloadItem.cs to:
  - Wrap the lyrics fetching in a try-catch block
  - Wrap the metadata application and LRC file creation in a try-catch block
  - Log warnings using Lidarr's logger instead of failing the download

### Coming Soon
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
