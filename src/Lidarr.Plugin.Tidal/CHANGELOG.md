# Changelog

## 10.0.2.2 (2024-03-30)
- Improved track download coordination to better respect `MaxConcurrentTrackDownloads` and `MaxDownloadsPerHour` settings
- Enhanced throttling mechanism using token bucket algorithm for smoother rate limiting
- Added proper semaphore control to enforce concurrent download limits
- Fixed potential race conditions in the download queue
- Implemented more natural delays between track downloads using `UserBehaviorSimulator`

## 10.0.2.1 (2024-03-24)
- Fixed an issue where the plugin would fail to download tracks when the album had a large number of tracks
- Added better error handling for failed track downloads
- Improved logging for download progress
- Fixed a bug where the plugin would not respect the settings for download delay 