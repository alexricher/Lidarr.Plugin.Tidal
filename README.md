# 🎧 Tidal for Lidarr

<div align="center">

![Tidal + Lidarr](https://img.shields.io/badge/Tidal-Lidarr-blue?style=for-the-badge)
![License](https://img.shields.io/github/license/alexricher/Lidarr.Plugin.Tidal?style=for-the-badge)
![Version](https://img.shields.io/badge/version-10.0.1-green?style=for-the-badge)
![Build](https://github.com/alexricher/Lidarr.Plugin.Tidal/actions/workflows/build.yml/badge.svg)
![Release](https://github.com/alexricher/Lidarr.Plugin.Tidal/actions/workflows/release.yml/badge.svg)

</div>

A powerful plugin that integrates Tidal music streaming service with Lidarr, enabling seamless searching, downloading, and management of high-quality music from Tidal directly within your Lidarr setup.

## 🎵 Features

- **Complete Tidal Integration**: Search and download music directly from Tidal within Lidarr
- **Multiple Audio Quality Options**:
  - 🔉 AAC 96kbps (Low quality)
  - 🔊 AAC 320kbps (High quality)
  - 🎼 FLAC Lossless
  - ✨ FLAC 24bit Lossless (Hi-Res)
- **Secure Authentication**: 🔐 OAuth-based authentication with Tidal
- **Advanced Download Management**:
  - 🤖 Natural download behavior simulation
  - ⏱️ Customizable download scheduling
  - ⚡ Parallel download support
  - 🛡️ Intelligent rate limiting to avoid detection
  - 🎲 Randomized download patterns mimicking human behavior
  - 📊 Session-based downloading with natural pauses
- **Audio Processing**:
  - 🧪 FLAC extraction from M4A containers
  - 🔄 Optional AAC to MP3 conversion
  - 📝 Metadata preservation
  - 🖼️ Album artwork embedding
- **Lyrics Support**:
  - 🎤 Download and save synchronized lyrics (.lrc files)
  - 🌐 Integration with external lyrics services (LRCLIB)
- **Smart Organization**:
  - 📁 Automatic folder structure creation
  - 🏷️ Proper metadata tagging
  - 👨‍🎤 Artist and album-based organization
- **Container-Friendly**:
  - 🐳 Works seamlessly in Docker containers
  - 🔄 Multiple fallback paths for permissions issues
  - 💾 Automatic temporary storage when primary storage is unavailable
  - ⚠️ Robust error handling for containerized environments
- **Fault-Tolerant Architecture**: 
  - 🛠️ Graceful handling of null settings
  - 🔄 Automatic retry mechanisms
  - 📜 Detailed logging for troubleshooting
  - 🧩 Component isolation to prevent cascading failures
- **Human Behavior Simulation**:
  - 🎭 Simulates real user listening patterns
  - 🔀 Genre and year-based track selection
  - ⏯️ Natural playback speed variation
  - 🔁 Track repeat behavior like real users
  - ⏭️ Simulates occasional track skipping
  - ⏰ Time-of-day aware download patterns

### Recent Updates

- **📱 Enhanced Docker Support**: Improved fallback mechanisms for containerized environments with multi-level path fallbacks
- **🛡️ Robust Error Handling**: Better handling of permissions, null references, and edge cases with component isolation
- **🔍 Advanced Diagnostics**: Detailed logging for easier troubleshooting with download status tracking
- **⚙️ Lazy Initialization**: Components now initialize only when needed, with graceful degradation
- **🔧 Path Management**: Automatic handling of unavailable or permission-restricted paths
- **⏱️ Rate Limiting Protection**: Automatic throttling detection and backoff strategy to prevent account limitations

## 📋 Requirements

- Lidarr running on the `plugins` branch
- FFMPEG (optional, for audio conversion features)
- Valid Tidal subscription

## 🚀 Installation

This plugin requires your Lidarr setup to be using the `plugins` branch. 

### Docker Setup

```yml
lidarr:
  image: ghcr.io/hotio/lidarr:pr-plugins
  container_name: lidarr
  environment:
    - PUID:100
    - PGID:1001
    - TZ:Etc/UTC
  volumes:
    - /path/to/config/:/config
    - /path/to/downloads/:/downloads
    - /path/to/music:/music
  ports:
    - 8686:8686
  restart: unless-stopped
```

### With FFMPEG Support

To enable audio conversion features, include FFMPEG in your container:

```yml
lidarr:
  build:
    context: /path/to/directory/containing/dockerfile
    dockerfile: Dockerfile
  container_name: lidarr
  environment:
    - PUID:1000
    - PGID:1001
    - TZ:Etc/UTC
  volumes:
    - /path/to/config/:/config
    - /path/to/downloads/:/data/downloads
    - /path/to/tidal/config/:/data/tidal-config
    - /path/to/music:/data/music
  ports:
    - 8686:8686
  restart: unless-stopped
```

```Dockerfile
FROM ghcr.io/hotio/lidarr:pr-plugins

RUN apk add --no-cache ffmpeg
```

### Plugin Installation Steps

1. In Lidarr, go to `System -> Plugins`, paste `https://github.com/alexricher/Lidarr.Plugin.Tidal` into the GitHub URL box, and press Install.
2. Go into the Indexer settings and press Add. In the modal, choose `Tidal` (under Other at the bottom).
3. Enter a path to use to store user data, press Test, it will error, press Cancel.
4. Refresh the page, then re-open the Add screen and choose Tidal again.
5. There should now be a `Tidal URL` setting with a URL in it. Open that URL in a new tab.
6. In the new tab, log in to Tidal, then press `Yes, continue`. It will then bring you to a page labeled "Oops." Copy the new URL for that tab (something like `https://tidal.com/android/login/auth?code=[VERY LONG CODE]`).
   - ⚠️ Do NOT share this URL with people as it grants people access to your account.
   - ⚠️ Redirect URLs are NOT reusable. If you need to sign in again, make sure to use a new Tidal URL from the settings, they are regenerated semi-often.
7. Enter a path to use to store user data and paste the copied Tidal URL into the `Redirect Url` option. Then press Save.
8. Go into the Download Client settings and press Add. In the modal, choose `Tidal` (under Other at the bottom).
9. Put the path you want to download tracks to and fill out the other settings to your choosing.
   - If you want `.lrc` files to be saved, go into the Media Management settings and enable Import Extra Files and add `lrc` to the list.
   - Make sure to only enable the FFMPEG settings if you are sure that FFMPEG is available to Lidarr, it may cause issues otherwise.
10. Go into the Profile settings and find the Delay Profiles. On each (by default there is only one), click the wrench on the right and toggle Tidal on.
11. Optional: To prevent Lidarr from downloading all track files into the base artist folder rather than into their own separate album folder, go into the Media Management settings and enable Rename Tracks. You can change the formats to your liking, but it helps to let each album have their own folder.

## ⚙️ Configuration Options

### Indexer Settings

| Setting | Description |
|---------|-------------|
| Tidal URL | The URL used to authenticate with Tidal |
| Redirect URL | The URL obtained after authentication |
| Config Path | Directory to store authentication data (with fallbacks to temp paths if unavailable) |
| Early Download Limit | Time before release date Lidarr will download from this indexer |

### Download Client Settings

#### Basic Settings

| Setting | Description |
|---------|-------------|
| Download Path | Directory where music will be downloaded (with fallbacks if permissions issues) |
| Status Files Path | Directory to store download status information (auto-creates with proper fallbacks) |
| Extract FLAC | Extract FLAC audio from M4A containers using FFMPEG |
| Re-encode AAC | Convert AAC audio to MP3 format using FFMPEG |
| Save Synced Lyrics | Save synchronized lyrics as .lrc files (requires Import Extra Files in Lidarr) |
| Use LRCLIB | Use external lyrics service as fallback when Tidal doesn't provide lyrics |

#### Natural Behavior Settings

| Setting | Description |
|---------|-------------|
| Enable Natural Behavior | Simulate human-like download patterns to help avoid detection systems |
| Behavior Profile | Choose between Balanced, Casual Listener, Music Enthusiast, or Custom |
| Session Duration | How long to continuously download before taking a break (in minutes) |
| Break Duration | How long to pause between download sessions (in minutes) |
| Complete Albums | Complete all tracks from the same album before moving to another album |
| Preserve Artist Context | Prefer next album from same artist after completion |
| Max Consecutive Artist Albums | Maximum number of consecutive albums to download from the same artist |
| Sequential Track Order | Download tracks within an album in sequential order rather than randomly |
| Randomize Album Order | Randomize album order instead of following queue order exactly |

#### Advanced Behavior Settings

| Setting | Description |
|---------|-------------|
| Enable Genre-Based Selection | Group downloads by music genre to simulate natural listening patterns |
| Genre Selection Probability | Probability (%) of using genre-based selection when available |
| Max Consecutive Genre Tracks | Maximum number of consecutive tracks from the same genre before switching |
| Enable Year-Based Selection | Group downloads by release year to simulate natural listening patterns |
| Year Selection Probability | Probability (%) of using year-based selection when available |
| Max Consecutive Year Tracks | Maximum number of consecutive tracks from the same year before switching |
| Simulate Listening Patterns | Applies sophisticated timing between tracks and albums to mimic actual listening |
| Track-to-Track Delay Min/Max | Minimum/maximum delay between tracks in the same album (seconds) |
| Album-to-Album Delay Min/Max | Minimum/maximum delay between different albums (seconds) |

#### Listening Simulation

| Setting | Description |
|---------|-------------|
| Simulate Listening Duration | Simulate actual time spent listening to tracks before downloading the next one |
| Maximum Listening Delay | Maximum seconds to simulate listening to a track (caps actual track duration) |
| Vary Listening Percentage | Occasionally listen to only a portion of tracks, even when not skipping |
| Minimum Listen Percentage | Minimum percentage of a track to listen to when varying listening time (1-100%) |
| Simulate Occasional Skips | Occasionally skip tracks as a real user might do when not interested |
| Skip Probability | Probability (%) of skipping a track |
| Maximum Skip Percentage | Maximum percentage of a track to listen to before skipping (1-100%) |
| Simulate Playback Speed | Simulate users adjusting playback speed while listening |
| Playback Speed Probability | Probability (%) of using non-standard playback speed for a track |

#### Pattern Variation

| Setting | Description |
|---------|-------------|
| Enable Repeat Behavior | Simulate users repeating tracks, albums, or artists they enjoy |
| Repeat Mode | Choose between None, RepeatTrack, RepeatAlbum, or RepeatArtist |
| Repeat Track Probability | Probability (%) of repeating a track when repeat is enabled |
| Maximum Repeat Count | Maximum number of times to repeat a track, album, or artist |
| Time-of-Day Adaptation | Adjust download activity based on time of day to appear more natural |
| Active Hours Start/End | Hours when active hours begin/end (24-hour format) |
| Rotate User-Agent | Occasionally change the user-agent between sessions to appear more natural |
| Vary Connection Parameters | Vary connection parameters between sessions to avoid fingerprinting |

#### Volume and Rate Handling

| Setting | Description |
|---------|-------------|
| Enable High Volume Handling | Special handling for very large download queues to avoid rate limiting |
| High Volume Threshold | Number of items in queue to trigger high volume mode |
| High Volume Session/Break Minutes | Session/break duration for high volume mode (minutes) |
| Enable Throttling Detection | Detects when Tidal is throttling downloads and implements a backoff strategy |
| Initial/Maximum Backoff Time | Initial/maximum waiting time after detecting throttling (minutes) |
| Max Retry Attempts | Maximum number of retry attempts for a throttled download before giving up |
| Download Delay | Add random delays between downloads to simulate human behavior |
| Download Delay Min/Max | Minimum/maximum delay between downloads (seconds) |
| Parallel Downloads | Number of tracks to download in parallel (higher values may increase detection risk) |

### Behavior Profiles

The plugin includes several predefined behavior profiles to simulate different types of users:

1. **Balanced** - Default profile with moderate download speeds and natural pauses
   - Medium session duration (120 minutes)
   - Medium breaks (60 minutes)
   - Moderate delays between tracks and albums
   - Some natural variations in listening patterns

2. **Casual Listener** - Slower downloads with longer pauses, mimicking occasional listening
   - Shorter sessions (60 minutes)
   - Longer breaks (120 minutes)
   - Higher probability of skipping tracks
   - More variation in listening duration
   - Fewer parallel downloads

3. **Music Enthusiast** - Faster downloads with shorter pauses, simulating active music collection
   - Longer sessions (180 minutes)
   - Shorter breaks (30 minutes)
   - Lower track skipping probability
   - Higher album completion rate
   - More genre-focused selection patterns

4. **Custom** - Fully customizable settings for advanced users
   - All behavior parameters manually configurable
   - Fine-grained control over every aspect of download behavior
   - Complete flexibility for specialized use cases

Each profile adjusts various parameters to create a realistic download pattern that helps avoid detection by Tidal's systems.

## 🎯 Recommended Configurations

To help you get the most out of the plugin while minimizing detection risk, here are some real-world configurations optimized for different scenarios:

### 🔒 Maximum Stealth Configuration

This configuration prioritizes extreme caution and stealth over download speed. Ideal for users with a history of account issues or those wanting maximum protection:

```
✅ Enable Natural Behavior: Yes
✅ Behavior Profile: Casual Listener
✅ Session Duration: 45 minutes (shorter than default)
✅ Break Duration: 180 minutes (longer than default)
✅ Complete Albums: Yes
✅ Preserve Artist Context: Yes
✅ Max Consecutive Artist Albums: 2 (lower than default)
✅ Randomize Album Order: Yes
✅ Enable Genre-Based Selection: Yes (80% probability)
✅ Simulate Listening Patterns: Yes
✅ Simulate Occasional Skips: Yes (30% probability)
✅ Enable Repeat Behavior: Yes (RepeatTrack, 15% probability)
✅ Time-of-Day Adaptation: Yes (Active hours: 9-23)
✅ Rotate User-Agent: Yes
✅ Vary Connection Parameters: Yes
✅ Enable High Volume Handling: Yes
✅ Enable Throttling Detection: Yes (with 3x default backoff)
✅ Download Delay: Yes (30-120 seconds)
✅ Parallel Downloads: 1 (serial downloads only)
```

This configuration limits downloads to realistic patterns, adds significant delays, and includes all anti-detection measures. Downloads will be slower but extremely natural-looking.

### 🏎️ Efficient Large Library Configuration

For users needing to download large libraries while maintaining reasonable protection:

```
✅ Enable Natural Behavior: Yes
✅ Behavior Profile: Music Enthusiast
✅ Session Duration: 240 minutes (longer than default)
✅ Break Duration: 60 minutes
✅ Complete Albums: Yes
✅ Preserve Artist Context: Yes
✅ Max Consecutive Artist Albums: 5 (higher than default)
✅ Randomize Album Order: No (maximize efficiency)
✅ Enable Genre-Based Selection: Yes (40% probability)
✅ Simulate Listening Patterns: Yes (with shorter track-to-track delays)
✅ Track-to-Track Delay Min/Max: 3-15 seconds (shorter than default)
✅ Album-to-Album Delay Min/Max: 20-120 seconds (shorter than default)
✅ Simulate Listening Duration: No (improves speed)
✅ Time-of-Day Adaptation: Yes (Active hours: 0-24, no restrictions)
✅ Enable High Volume Handling: Yes (with higher threshold)
✅ Enable Throttling Detection: Yes
✅ Download Delay: Yes (5-30 seconds, shorter than default)
✅ Parallel Downloads: 2-3 (balance of speed and safety)
```

This configuration maintains a reasonable level of protection while optimizing for throughput, suitable for downloading larger music collections over time.

### 🎭 Weekend-Only Collector

Ideal for users who want to simulate weekend-only listening habits:

```
✅ Enable Natural Behavior: Yes
✅ Behavior Profile: Custom
✅ Session Duration: 360 minutes (long weekend sessions)
✅ Break Duration: 120 minutes
✅ Complete Albums: Yes
✅ Preserve Artist Context: Yes
✅ Time-of-Day Adaptation: Yes (Active hours: 10-2)
✅ Scheduled Downloads: Configure in Lidarr to only run on Friday-Sunday
✅ Simulate Listening Patterns: Yes
✅ Track-to-Track Delay Min/Max: 10-180 seconds (highly variable)
✅ Simulate Occasional Skips: Yes (20% probability)
✅ Enable High Volume Handling: Yes
✅ Download Delay: Yes (variable 15-90 seconds)
✅ Parallel Downloads: 2 (reasonable for weekend sessions)
```

This configuration creates a realistic weekend music enthusiast pattern, with longer sessions during weekend hours and proper listening behavior.

### 📱 Mobile User Simulation

Simulates a user who primarily accesses Tidal via mobile devices with varying network conditions:

```
✅ Enable Natural Behavior: Yes
✅ Behavior Profile: Custom
✅ Session Duration: Variable (30-120 minutes)
✅ Break Duration: Variable (15-60 minutes)
✅ Complete Albums: Not always (70% probability)
✅ Preserve Artist Context: Sometimes (50% probability)
✅ Simulate Listening Patterns: Yes
✅ Simulate Occasional Skips: Yes (40% probability - higher than desktop)
✅ Vary Connection Parameters: Yes (aggressive variation)
✅ Rotate User-Agent: Yes (using mobile user agents)
✅ Download Delay: Yes (highly variable 10-240 seconds)
✅ Parallel Downloads: 1 (typical for mobile)
```

This configuration mimics typical mobile usage patterns with frequent network changes, more skips, and variable session lengths.

### 🎹 Genre Specialist Configuration

For users who focus on specific genres, creating a more believable listening profile:

```
✅ Enable Natural Behavior: Yes
✅ Behavior Profile: Custom
✅ Session Duration: 180 minutes
✅ Break Duration: 90 minutes
✅ Complete Albums: Yes
✅ Preserve Artist Context: Yes
✅ Enable Genre-Based Selection: Yes (90% probability)
✅ Max Consecutive Genre Tracks: 30 (much higher than default)
✅ Genre Selection Probability: 90% (focus heavily on genre)
✅ Simulate Listening Patterns: Yes
✅ Enable Repeat Behavior: Yes (RepeatArtist mode)
✅ Download Delay: Yes (10-60 seconds)
```

Configure Lidarr to focus on your preferred genres, and this configuration will create a highly believable pattern of a genre enthusiast.

## 📊 Download Status Tracking

The plugin includes basic download status tracking functionality that saves detailed information about your downloads. This data can be used to:

- Monitor download progress and completion
- Track statistics about your music collection
- Identify potential issues or bottlenecks
- Analyze download patterns and performance

### Status File Generation

To enable status file generation:

1. In Lidarr, go to `Settings -> Download Clients`
2. Edit your Tidal download client
3. Set a valid `Status Files Path` (e.g., `/config/tidal-status` in Docker or a local path)
4. Save your settings

The plugin will generate detailed status JSON files in this location that can be used for monitoring and analysis.

### Coming Soon: TidalDownloadViewer

A web-based interface for visualizing and managing your Tidal downloads is currently in development. This tool will provide:

- 📈 Real-time download statistics and analytics
- 📊 Artist and album-based insights
- ⏱️ Session monitoring and management
- 🚨 Error detection and diagnostics
- 🔄 Automatic updates and notifications
- 📱 Mobile-friendly interface

Stay tuned for the release of TidalDownloadViewer in an upcoming version!

## 🏠 Container-Friendly Architecture

Our plugin implements a sophisticated error-tolerant architecture optimized for containerized environments:

- 📁 **Hierarchical Path Resolution**: When accessing storage locations, the system tries paths in this order:
  1. User-configured paths in settings
  2. Home directory path (~/.lidarr/TidalPlugin)
  3. AppData directory (ApplicationData/Lidarr/TidalPlugin)
  4. System temporary directory (fallback of last resort)
  
- 🛡️ **Lazy Component Initialization**: Components are initialized only when needed:
  - Download status manager initializes on first status operation
  - TidalProxy starts with default settings, then updates when real settings available
  - Download task queue buffers tasks until proper initialization

- ⚙️ **Fail-Safe Configuration**: Protections against configuration issues:
  - Null settings handled gracefully throughout the codebase
  - Container permission issues detected and worked around
  - Test writes performed before committing to paths
  - Status files persist across container restarts

- 📊 **Status Preservation**: Download status information is maintained across sessions:
  - Status files written periodically to the most accessible location
  - Statistics tracked even during error conditions
  - Multiple fallback mechanisms for status storage

- 🔄 **Recovery Mechanisms**: The system includes various recovery strategies:
  - Automatic retry for throttled downloads
  - Exponential backoff when rate limiting detected
  - Automatic session rotation to avoid detection
  - Safe re-initialization after errors

## 🛠️ Development

### Building the Plugin

This repository includes a PowerShell script to help with building and managing the plugin.

To build the plugin locally:

```powershell
.\build.ps1
```

The build script supports several parameters:

```powershell
.\build.ps1 -BaseVersion "10.1.0" -Verbose                # Build with custom version
.\build.ps1 -CopyToLocalPath:$false -Verbose              # Build without copying to local path
.\build.ps1 -CreateZip:$false -Verbose                    # Build without creating zip file
.\build.ps1 -CleanPreviousBuilds:$false -Verbose          # Build without cleaning previous builds
```

### Creating Releases

This repository uses GitHub Actions to automate the release process. There are two workflows:

1. **build.yml**: Runs on every push to the main branch and pull requests to build and test the plugin
2. **release.yml**: Runs when a tag starting with 'v' is pushed to create a release

To create a new release:

1. Use the `create_release.ps1` script to create and push a new tag:

```powershell
.\create_release.ps1 -Version "10.0.2"
```

2. The GitHub Actions workflow will automatically:
   - Build the plugin with the specified version
   - Create a release with the plugin zip file
   - Generate release notes based on commits since the last tag

You can also use the `lidarr_plugin_manager.ps1` script for more advanced release management:

```powershell
.\scripts\lidarr_plugin_manager.ps1 -CreateGitHubRelease -PluginVersion "10.0.2"
```

### Architecture

The plugin's architecture consists of several key components working together:

1. **Tidal Indexer**: Implements Lidarr's indexer interface to search for music on Tidal
   - Handles OAuth authentication with Tidal
   - Provides search results with quality information
   - Integrates with Lidarr's release management system

2. **Tidal Download Client**: Implements Lidarr's download client interface 
   - Manages the download queue with the TidalProxy
   - Initializes the status manager with proper error handling
   - Uses lazy initialization to avoid startup errors
   - Implements natural behavior simulation via UserBehaviorSimulator

3. **Download Status Manager**: Tracks and persists download progress
   - Manages status files with multi-level path fallbacks
   - Provides detailed statistics on download activity
   - Includes robust error handling for file access issues
   - Automatically recovers from permission problems

4. **TidalProxy**: Handles core API interactions with Tidal
   - Manages authentication and requests
   - Initializes with default settings to avoid null references
   - Delegates downloads to the DownloadTaskQueue

5. **Download Task Queue**: Manages download operations
   - Implements the natural download order logic
   - Handles throttling detection and backoff
   - Processes downloads according to behavior profiles
   - Manages parallel download operations

6. **User Behavior Simulator**: Sophisticated behavior modeling
   - Implements realistic timing for download operations
   - Models human-like music consumption patterns
   - Adapts behavior based on time and context
   - Provides anti-detection measures through varied patterns

These components communicate through well-defined interfaces with proper error handling between boundaries, ensuring that failures in one component don't cascade to others.

## 🐛 Known Issues & Workarounds

- **Search results size estimates**: Search results provide an estimated file size instead of an actual one
- **Token storage location**: User access tokens are stored in a separate folder even though Lidarr has a system to store it available to plugins
- **Hi-Res FLAC identification**: Some Hi-Res FLAC files may not be properly identified
- **Docker permission issues**: If experiencing permission problems in Docker, try:
  - Setting proper PUID/PGID values matching your host user
  - Ensuring volume mounts have appropriate permissions
  - Specifying a writable path in the Status Files Path setting
  - Letting the plugin use fallback mechanisms by not setting paths at all
- **Rate limiting**: If Tidal begins to rate-limit your account:
  - Enable throttling detection to automatically handle this
  - Increase download delays between tracks
  - Reduce parallel download count
  - Enable natural behavior simulation with longer breaks
- **API Error: Sequence contains more than one matching element**: If you encounter this error when saving settings:
  - Restart Lidarr completely
  - If problem persists, try removing the plugin and reinstalling it
  - This indicates a duplicate class registration issue

## 📜 Versioning

This plugin uses a date-based versioning approach:

1. The base version is defined in `src/Directory.Build.props`:
   ```xml
   <PropertyGroup>
     <FileVersion>10.0.1</FileVersion>
     <AssemblyVersion>10.0.1</AssemblyVersion>
     <Version>10.0.1</Version>
   </PropertyGroup>
   ```

2. During the build process (both local and CI), a date-based build number is appended to this base version:
   - The build number format is `yyMMdd` (year, month, day)
   - Example: `230516` for May 16, 2023

3. The final version format is: `{BaseVersion}.{BuildNumber}`
   - Example: `10.0.1.230516` (build on May 16, 2023)

This approach ensures that:
- Each build has a unique, chronologically sortable version number
- The version number is predictable and human-readable
- The build process doesn't require modifying source files

## 🔮 Future Improvements

Here are some planned improvements for future versions:

1. **Reliability Enhancements**:
   - Further improvements to error handling in containerized environments
   - Transaction-based status files to prevent corruption
   - More granular control over fallback mechanisms
   - Advanced telemetry for debugging installation issues

2. **Performance Optimizations**:
   - Improved memory usage during large downloads
   - Optimized parallel download management
   - Enhanced caching mechanisms for better performance
   - Smarter throttling detection with machine learning

3. **User Experience**:
   - Web UI component for monitoring download progress
   - Detailed statistics and reporting dashboard
   - Visual feedback on download behavior patterns
   - Interactive configuration wizard for optimal settings

4. **Security Enhancements**:
   - Improved token storage and management
   - More sophisticated traffic pattern analysis
   - Additional obfuscation techniques
   - Enhanced protection against API rate limiting

5. **Integration Improvements**:
   - Better integration with Lidarr's metadata system
   - Enhanced album artwork handling
   - Improved lyrics synchronization
   - Support for collaborative playlists

## 👥 Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Submit a pull request

## 📝 Licensing

This project is licensed under the GNU General Public License v3.0.

The following libraries have been merged into the final plugin assembly due to Lidarr's plugin system requirements:

- [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) - MIT license. See [LICENSE](https://github.com/JamesNK/Newtonsoft.Json/blob/master/LICENSE.md).
- [TagLibSharp](https://github.com/mono/taglib-sharp) - LGPL-2.1 license. See [COPYING](https://github.com/mono/taglib-sharp/blob/main/COPYING).
- [TidalSharp](https://github.com/TrevTV/TidalSharp) - GPL-3.0 license. See [LICENSE](https://github.com/TrevTV/TidalSharp/blob/main/LICENSE).
