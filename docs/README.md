# Lidarr Tidal Plugin Documentation

Welcome to the documentation for the Lidarr Tidal Plugin. This documentation provides comprehensive information about the plugin's architecture, development, and usage.

## Documentation Tools

### DocFX Documentation System

The project uses [DocFX](https://dotnet.github.io/docfx/) for generating documentation. To set up and use DocFX, refer to [Documentation Setup](DOCUMENTATION_SETUP.md).

Key benefits of using DocFX:
- Automatically generates API documentation from XML comments
- Supports Markdown for conceptual documentation
- Creates a searchable, static HTML site
- Integrates with GitHub Actions for continuous documentation updates

To generate documentation locally:
1. Install DocFX: `dotnet tool install -g docfx`
2. Run the documentation script: `.\scripts\GenerateDocumentation.ps1`

## XML Documentation

The following classes have complete XML documentation:

- [FFMPEG.cs](../src/Lidarr.Plugin.Tidal/Download/Clients/Tidal/FFMPEG.cs) - Documents the FFMPEG integration for audio conversion
- [TidalCountryManager.cs](../src/Lidarr.Plugin.Tidal/TidalCountryManager.cs) - Documents country detection and management
- [DownloadItem.cs](../src/Lidarr.Plugin.Tidal/Download/Clients/Tidal/DownloadItem.cs) - Documents the download item implementation
- [DownloadTaskQueue.cs](../src/Lidarr.Plugin.Tidal/Download/Clients/Tidal/Queue/DownloadTaskQueue.cs) - Documents the thread-safe queue implementation

## Implementation Guides

- [DownloadTaskQueue_FixedImplementation.cs](DownloadTaskQueue_FixedImplementation.cs) - Reference implementation of `DownloadTaskQueue` with thread safety improvements
- [FIXING_LINTING_ERRORS.md](FIXING_LINTING_ERRORS.md) - Guide for resolving common linting errors in the codebase

## Architecture & Design

- [ARCHITECTURE.md](ARCHITECTURE.md) - Overview of the plugin architecture and components

## Development Plans

- [ROADMAP.md](ROADMAP.md) - Future development plans and features

## Documentation Standards

- [DOCUMENTATION_STANDARDS.md](DOCUMENTATION_STANDARDS.md) - Standards for maintaining consistent documentation

## Benefits of Documentation

Comprehensive documentation provides several benefits:

1. **Onboarding Efficiency**: Helps new developers understand the codebase quickly
2. **Maintenance Support**: Makes it easier to maintain and update code
3. **Quality Assurance**: Encourages high-quality code by requiring proper documentation
4. **Knowledge Preservation**: Preserves knowledge about the system's design and implementation

## Maintaining Documentation

To keep documentation current:

1. Update XML comments when modifying code
2. Run documentation generation regularly to check for missing documentation
3. Review documentation for accuracy with each release
4. Use the documentation GitHub Action workflow to automate documentation generation 