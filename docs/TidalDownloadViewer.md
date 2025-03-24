# TidalDownloadViewer

TidalDownloadViewer is an upcoming web-based interface for monitoring and managing your Tidal downloads in Lidarr. While the full viewer interface is still in development, the plugin already includes the core functionality for generating and maintaining download status files.

## Current Status File Functionality

The plugin currently generates detailed JSON status files that track your download progress and history. These files can be used for monitoring and debugging purposes.

### Setting Up Status File Generation

1. In Lidarr, go to `Settings -> Download Clients`
2. Edit your Tidal download client
3. Set a valid `Status Files Path` in one of these locations:
   - Docker: `/config/tidal-status` (recommended for containerized setups)
   - Windows: `C:\ProgramData\Lidarr\TidalStatus` or `%APPDATA%\Lidarr\TidalStatus`
   - Linux/macOS: `~/.config/Lidarr/TidalStatus` or `/var/lib/Lidarr/TidalStatus`
4. Ensure the path has proper write permissions for the Lidarr process/container
5. Save your settings

### Status File Structure

The plugin generates a `status.json` file in your configured status path with the following information:

```json
{
  "pluginVersion": "10.0.2.1",
  "lastUpdated": "2024-01-20T12:34:56Z",
  "totalPendingDownloads": 5,
  "totalCompletedDownloads": 42,
  "totalFailedDownloads": 1,
  "downloadRate": 10,
  "sessionsCompleted": 3,
  "isHighVolumeMode": false,
  "artistStats": {
    "Artist Name": {
      "artistName": "Artist Name",
      "pendingTracks": 2,
      "completedTracks": 15,
      "failedTracks": 0,
      "albums": ["Album 1", "Album 2"]
    }
  },
  "recentDownloads": [
    {
      "title": "Track Title",
      "artist": "Artist Name",
      "album": "Album Name",
      "status": "Completed",
      "timestamp": "2024-01-20T12:30:00Z"
    }
  ]
}
```

### Troubleshooting Status Files

If you don't see the status file being generated:

1. **Check Permissions**:
   - Ensure the configured path exists
   - Verify the Lidarr process has write permissions
   - For Docker, check that the volume mount is correct

2. **Check Paths**:
   - The plugin uses a fallback system for paths:
     1. User-configured path in settings
     2. Home directory path (~/.lidarr/TidalPlugin)
     3. AppData directory
     4. System temporary directory
   - Check these locations if the file isn't in your configured path

3. **Enable Debug Logging**:
   - In Lidarr, go to `Settings -> General -> Logging`
   - Set "Log Level" to Debug or Trace
   - Check logs for any path-related errors

4. **Docker Specific**:
   - Ensure proper PUID/PGID settings
   - Verify volume mount permissions
   - Check container logs for permission errors

## Upcoming TidalDownloadViewer Features

The web-based viewer interface will provide:

- 📈 **Real-time Statistics**:
  - Download rates and completion metrics
  - Session statistics
  - Queue management
  - Error tracking

- 📊 **Artist Analytics**:
  - Per-artist download statistics
  - Album completion tracking
  - Genre-based analytics
  - Release year distribution

- ⏱️ **Session Monitoring**:
  - Active download sessions
  - Historical session data
  - Performance metrics
  - Rate limiting detection

- 🚨 **Error Detection**:
  - Failed download analysis
  - Error pattern recognition
  - Automatic issue reporting
  - Troubleshooting suggestions

- 🔄 **Auto-updates**:
  - Real-time data refresh
  - Push notifications
  - Status alerts
  - Progress tracking

- 📱 **Mobile Support**:
  - Responsive design
  - Touch-friendly interface
  - Mobile notifications
  - Compact view mode

### Planned Implementation

The viewer will be implemented as:

1. **Standalone Web App**:
   - React-based frontend
   - Real-time updates via WebSocket
   - Modern, responsive design
   - Dark/light theme support

2. **Integration Options**:
   - Direct Lidarr plugin integration
   - Standalone server mode
   - Docker container deployment
   - Reverse proxy support

3. **Security Features**:
   - Authentication support
   - HTTPS/SSL encryption
   - API key management
   - Access control

4. **Data Management**:
   - Historical data retention
   - Data export capabilities
   - Backup/restore functionality
   - Custom reporting

Stay tuned for the release of TidalDownloadViewer in an upcoming version! 