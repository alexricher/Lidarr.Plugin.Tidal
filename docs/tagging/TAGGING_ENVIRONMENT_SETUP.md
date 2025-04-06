# Development Environment Setup for Audio Tagging

This document outlines the setup required for developing the audio tagging functionality for Lidarr.Plugin.Tidal.

## Required NuGet Packages

Add the following NuGet packages to the project:

```xml
<!-- Audio file tagging -->
<PackageReference Include="TagLibSharp" Version="2.3.0" />

<!-- For Phase 2: MusicBrainz integration -->
<PackageReference Include="MetaBrainz.MusicBrainz" Version="5.0.0" />
<PackageReference Include="MetaBrainz.Common" Version="5.0.0" />

<!-- For Phase 3: AcoustID integration (when needed) -->
<PackageReference Include="AcoustID.NET" Version="1.3.0" />
```

## Test Dataset

Create a test dataset with the following audio files to verify different aspects of the tagging system:

### Basic Files
- Standard MP3 files with embedded metadata
- FLAC files with Vorbis comments
- M4A/AAC files with iTunes-style tags
- Opus and Ogg Vorbis files (less common formats)

### Edge Cases
- Various Artists compilation album
- Album with featured artists (feat. mentions)
- Classical music with composers, conductors, and performers
- Files with non-Latin characters (Japanese, Korean, Arabic, Cyrillic)
- Multi-disc album with different disc numbering schemes
- Album with bonus tracks not in Tidal's metadata
- Files with inconsistent naming conventions

### Directory Structure for Test Files

```
test_audio/
├── basic/
│   ├── mp3_standard/          # Basic MP3 files
│   ├── flac_standard/         # Basic FLAC files
│   └── m4a_standard/          # Basic M4A files
├── edge_cases/
│   ├── various_artists/       # Compilation albums
│   ├── featured_artists/      # Tracks with featuring artists
│   ├── classical/             # Classical music
│   ├── international/         # Files with non-Latin characters
│   ├── multi_disc/            # Multi-disc albums
│   └── special_formats/       # Opus, Ogg Vorbis, etc.
└── metadata_issues/
    ├── mismatched_counts/     # Different track counts between files and metadata
    ├── missing_numbers/       # Files without track numbers
    └── incomplete_metadata/   # Files with partial metadata
```

## API Testing Tools

### MusicBrainz API Testing

1. Set up Postman collection for MusicBrainz API:
   - Collection available at: [MusicBrainz Postman Collection](https://github.com/metabrainz/musicbrainz-server/blob/master/postman/MusicBrainz.postman_collection.json)
   - Configure environment variables for your testing

2. Create a dedicated MusicBrainz application:
   - Register at [MusicBrainz](https://musicbrainz.org/account/register)
   - Create application at [MusicBrainz Applications](https://musicbrainz.org/account/applications)
   - Set a descriptive user agent: `Lidarr.Plugin.Tidal/1.0.0 (development)`

### AcoustID API Testing

1. Get an API key from [AcoustID](https://acoustid.org/api-key)
2. Test API interaction with sample fingerprints
3. Set up request templates for common operations

## Development Settings

### Rate Limiting Development Configuration

Create a `TaggingDevSettings.json` file with the following structure:

```json
{
  "MusicBrainz": {
    "RateLimitRequestsPerSecond": 1,
    "UseMockResponses": true,
    "MockResponsesPath": "./test_data/mb_responses"
  },
  "AcoustID": {
    "ApiKey": "YOUR_ACOUSTID_API_KEY",
    "UseMockResponses": true,
    "MockResponsesPath": "./test_data/acoustid_responses"
  },
  "Logging": {
    "DetailLevel": "Debug",
    "LogMatchingDecisions": true,
    "LogMetadataChanges": true
  }
}
```

### Mock Responses

Prepare a set of mock API responses for testing:

1. Create directories for mock responses:
   ```
   test_data/
   ├── mb_responses/          # MusicBrainz mock responses
   │   ├── search_releases/   # Release search results
   │   └── get_release/       # Release details
   └── acoustid_responses/    # AcoustID mock responses
       └── lookup/            # Fingerprint lookup results
   ```

2. Capture real API responses for common scenarios
3. Modify them as needed for testing edge cases

## Debugging Tools

1. **TagViewer**: A simple utility to view and dump tag contents
   - Create a small console app that can dump all tags from files
   - Use for debugging complex tag issues

2. **MetadataDiffer**: Compare metadata between sources
   - Tool to show differences between Tidal and MusicBrainz metadata
   - Helps in understanding metadata merging decisions

## Continuous Integration Setup

1. Set up a build pipeline that:
   - Installs required dependencies
   - Builds the project
   - Runs unit tests
   - Runs integration tests with mock responses

2. Configure separate test runs for:
   - Basic tagging (Phase 1)
   - Enhanced metadata (Phase 2)
   - Fingerprinting (Phase 3)

## Performance Testing Environment

1. Create a large sample library (1000+ tracks)
2. Set up performance monitoring tools:
   - Memory profiling
   - Execution time tracking
   - Concurrency testing

3. Establish baseline performance metrics

## Next Steps

1. Clone the repository
2. Install required NuGet packages
3. Configure development settings
4. Prepare test datasets
5. Start implementation with the first milestone: Core Infrastructure 