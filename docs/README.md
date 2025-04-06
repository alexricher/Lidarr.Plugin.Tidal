# Lidarr Tidal Plugin Documentation

This directory contains documentation for the Lidarr Tidal Plugin project.

## Documentation Categories

### Tagging Implementation

A comprehensive set of documentation for implementing audio tagging functionality is available in the [tagging](./tagging) directory:

- [Tagging Overview](./tagging/TAGGING_OVERVIEW.md) - Main reference guide for the tagging implementation
- [Project Organization](./tagging/TAGGING_PROJECT_ORGANIZATION.md) - Code structure and organization
- [Proof of Concept](./tagging/TAGGING_PROOF_OF_CONCEPT.md) - Initial implementation approach
- [Integration Guide](./tagging/TAGGING_INTEGRATION_GUIDE.md) - Integration with Lidarr and Tidal
- [Settings Integration](./tagging/TAGGING_SETTINGS_INTEGRATION.md) - UI settings implementation
- [Testing Strategy](./tagging/TAGGING_TESTING_STRATEGY.md) - Comprehensive testing approach
- [Milestone Plan](./tagging/TAGGING_MILESTONE_PLAN.md) - Implementation timeline and tasks

### Core Documentation

- [Architecture](./ARCHITECTURE.md) - Overview of the plugin architecture
- [Roadmap](./ROADMAP.md) - Overall project roadmap and planned features
- [Documentation Standards](./DOCUMENTATION_STANDARDS.md) - Standards for documenting code and features
- [Performance Optimization](./PERFORMANCE_OPTIMIZATION.md) - Performance optimization techniques

### Download System

- [Download Item Documentation](./DownloadItem_Documentation.md) - Details on the download item system
- [Download Task Queue Documentation](./DownloadTaskQueue_Documentation.md) - Download queue implementation
- [Queue Persistence](./Queue_Persistence.md) - Queue persistence implementation
- [Rate Limiting](./Rate_Limiting.md) - Rate limiting implementation

### User Interface

- [Usage Guide](./USAGE_GUIDE.md) - Guide for using the Tidal plugin
- [Tidal Download Viewer](./TidalDownloadViewer.md) - Documentation for the download viewer

### Testing

- [Unit Tests Plan](./UNIT_TESTS_PLAN.md) - Plan for implementing unit tests
- [TDD](./TDD.md) - Test-driven development approach
- [Unit Tests Startup Plan](./UNIT_TESTS_STARTUP_PLAN.md) - Plan for setting up unit tests

### Features

- [Natural Behavior](./NaturalBehavior.md) - Natural behavior simulation
- [Smart Pagination](./SMART_PAGINATION.md) - Smart pagination implementation
- [Status Files](./STATUS_FILES.md) - Status files implementation
- [Lyrics Research](./LYRICS_RESEARCH.md) - Research on lyrics integration

### Refactoring

- [Refactoring](./REFACTORING.md) - Refactoring guidelines
- [Refactoring Summary](./REFACTORING_SUMMARY.md) - Summary of refactoring efforts

## Contributing

When adding new documentation, please follow the [Documentation Standards](./DOCUMENTATION_STANDARDS.md) and update this README to include references to your new documents.

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