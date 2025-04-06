# Queue Persistence System

## Overview

The Queue Persistence System is a critical component of the Tidal plugin for Lidarr that enables download tasks to persist across application restarts or crashes. Without this system, any in-progress downloads or queued items would be lost whenever the application is restarted, leading to potentially hours of lost work.

This documentation covers the technical implementation details, design decisions, and usage of the Queue Persistence System.

## Key Components

### 1. QueuePersistenceManager

The `QueuePersistenceManager` class is the core component responsible for saving and loading queue data to and from disk:

- **Initialization**: Sets up the persistence directory, creates necessary folders, and validates file access
- **Thread Safety**: Implements locking mechanisms to prevent concurrent file access
- **JSON Serialization**: Converts download items to/from JSON format for storage
- **Backup System**: Creates backups of queue files before overwriting to prevent data loss
- **Error Handling**: Implements robust error handling with detailed logging

### 2. QueueItemRecord

The `QueueItemRecord` class represents a serializable version of download items:

- Contains all necessary metadata to recreate a download item upon restart
- Properties include ID, Title, Artist, Album, Bitrate, TotalSize, etc.
- Designed to be JSON-serializable using System.Text.Json

### 3. DownloadTaskQueue Integration

The `DownloadTaskQueue` class has been enhanced with queue persistence functionality:

- **Periodic Saving**: Implemented a timer to save the queue every 2 minutes
- **Initialization Logic**: Restores queue from disk during startup
- **Shutdown Logic**: Ensures queue is saved before application closes
- **Path Fallback**: Implements fallback paths when primary path isn't available

## Implementation Details

### File Structure

The queue is stored in a JSON file at the following location:
```
{QueuePersistencePath}/queue/tidal_queue.json
```

Where `QueuePersistencePath` is determined in the following order:
1. Path specified in settings
2. Status files path (if specified)
3. Download path (if specified)
4. Application data folder

### Serialization Format

The queue is serialized as a JSON array of download item records. A simplified example:

```json
[
  {
    "ID": "123456789",
    "Title": "Song Title",
    "Artist": "Artist Name",
    "Album": "Album Name",
    "Bitrate": "HI_RES",
    "TotalSize": 58901234,
    "DownloadFolder": "E:\\Music\\Artist Name\\Album Name",
    "Explicit": false,
    "RemoteAlbumJson": "...",
    "QueuedTime": "2023-05-01T12:34:56.7890123Z"
  },
  // Additional queue items...
]
```

### Thread Safety

Queue persistence operations implement thread safety through several mechanisms:

1. File-level locking using the `_fileLock` object in `QueuePersistenceManager`
2. Queue-level locking using the `_persistenceLock` object in `DownloadTaskQueue`
3. Atomic file operations to prevent partial writes causing corruption

### Backup and Recovery

To prevent data loss, the system implements a multi-layered backup approach:

1. Before writing a new queue file, the existing file is backed up with a `.bak` extension
2. If loading the primary queue file fails, the system attempts to load from the backup
3. Validation checks ensure files are properly written with verification after save operations

## Usage

### Configuration

The Queue Persistence System can be configured through the Tidal settings:

- **Enable Queue Persistence**: Toggles the entire persistence system on/off
- **Queue Persistence Path**: Specifies a custom path for storing queue files
- **Auto-Save Interval**: Fixed at 2 minutes (configurable only in code)

### Diagnostics

If queue persistence issues occur, the system provides detailed logging:

- File permissions issues are reported with descriptive error messages
- File verification is performed after writes with size and content validation
- Operation timing is logged for performance monitoring
- Recovery attempts from backup files are logged with results

## Performance Considerations

The persistence system is designed with performance in mind:

- JSON serialization is only performed when needed
- File writes are batched to reduce I/O operations
- Memory usage is optimized by converting only necessary properties
- Periodic saves occur at reasonable intervals to balance data safety with performance

## Error Handling

The Queue Persistence System implements comprehensive error handling:

- **File Access Errors**: Caught and logged with detailed messages and fallback paths
- **Serialization Errors**: Handle malformed JSON with recovery from backup files
- **Disk Space Issues**: Detected and reported if file writes fail
- **Permissions Problems**: Validated during initialization with clear error messages

## Design Decisions

1. **Why JSON?**: JSON was chosen for its human-readability, cross-platform compatibility, and built-in .NET support through System.Text.Json.

2. **Periodic Save Interval**: The 2-minute interval balances data safety with performance. Too frequent saves could impact I/O, while longer intervals risk more lost data.

3. **Backup Before Write**: Creating a backup before writing new data ensures that even if the write operation fails, previous data can be recovered.

4. **Thread-Safe Design**: The locking mechanisms ensure that concurrent operations don't corrupt the queue file, even when multiple threads attempt to save or restore simultaneously.

## Future Improvements

Potential future enhancements for the queue persistence system:

1. User-configurable auto-save interval
2. Compression for large queue files
3. Encryption for sensitive data
4. Multiple backup files with rotation
5. Recovery UI for manual intervention if needed 