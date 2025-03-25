# Tidal Plugin Status Files

The Tidal plugin for Lidarr includes a status file system that tracks download statistics and provides visibility into download operations. This document explains how the status file system works and how to configure it.

## Configuration

In the Lidarr UI, navigate to **Settings > Download Clients > Tidal**. Set the **Status Files Path** to a directory where you want status files to be stored.

Example: `/downloads/tidal_status`

## Status File Types

The plugin creates several status files:

- **status.json**: The main status file containing download statistics, artist information, and recent downloads
- **write_test_{guid}.tmp**: Temporary files used to verify write permissions (automatically cleaned up)
- **status_test.json**: Test file created during validation (automatically cleaned up)

## Automatic Directory Recreation

If the status directory is accidentally deleted while Lidarr is running, the plugin will automatically:
1. Detect the missing directory during the next status update
2. Log a warning message
3. Recreate the directory
4. Resume normal status file operations

This ensures continuous operation even if the status directory is deleted or becomes unavailable.

## Debugging Status Files

If you encounter issues with status files, you can use the included test script:

```powershell
# Run with default settings (creates test files)
.\scripts\test-status-files.ps1

# Keep test files for inspection
.\scripts\test-status-files.ps1 -KeepTestFiles

# Clean up existing test files
.\scripts\test-status-files.ps1 -CleanOnly
```

This script will:
1. Create a test directory with sample status files
2. Validate the directory permissions
3. Check for Lidarr plugin directories
4. Generate a debug report
5. Clean up temporary files (unless -KeepTestFiles is specified)

## The TidalStatusHelper Class

The `TidalStatusHelper` class provides a centralized way to manage status files:

- File creation with retry mechanisms
- JSON serialization/deserialization
- Error handling and logging
- Directory validation
- Automatic cleanup of temporary files
- Auto-recreation of deleted directories

## Status File Structure

The main status file contains:

```json
{
  "pluginVersion": "10.0.2.1",
  "lastUpdated": "2023-01-01T12:00:00Z",
  "totalPendingDownloads": 10,
  "totalCompletedDownloads": 25,
  "totalFailedDownloads": 2,
  "downloadRate": 12,
  "sessionsCompleted": 3,
  "isHighVolumeMode": false,
  "artistStats": {
    "Artist Name": {
      "artistName": "Artist Name",
      "pendingTracks": 5,
      "completedTracks": 10,
      "failedTracks": 1,
      "albums": ["Album 1", "Album 2"]
    }
  },
  "recentDownloads": [
    {
      "title": "Track Title",
      "artist": "Artist Name",
      "album": "Album Name",
      "status": "Completed",
      "timestamp": "2023-01-01T12:00:00Z"
    }
  ]
}
```

## Troubleshooting

If status files are not being created:

1. Verify the configured path exists and has write permissions
2. Check Lidarr logs for status file errors
3. Run the test script to generate a debug report
4. Ensure the container has access to the configured directory

## Security Considerations

Status files may contain information about your music library. Ensure the status files directory is properly secured with appropriate permissions. 