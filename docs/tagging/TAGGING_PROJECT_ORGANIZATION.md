# Project Organization for Audio Tagging

This document outlines the recommended organization for the audio tagging functionality implementation in Lidarr.Plugin.Tidal.

## Module Structure

The tagging implementation should follow a modular structure that aligns with the tiered approach in the roadmap:

```
src/Lidarr.Plugin.Tidal/Tagging/
├── Core/                        # Core tagging infrastructure
│   ├── Interfaces/              # ITaggingService and related interfaces
│   ├── Models/                  # TaggingOptions, TaggingResult, etc.
│   ├── Services/                # Core implementation of services
│   └── Utilities/               # Shared utilities for tagging
├── Basic/                       # Tier 1: Basic tagging
│   ├── TagLib/                  # TagLib# integration
│   ├── Mapping/                 # Tidal to TagLib mapping
│   ├── Formats/                 # Format-specific handlers
│   └── Matching/                # Track matching algorithms
├── Enhanced/                    # Tier 2: Enhanced metadata
│   ├── MusicBrainz/             # MusicBrainz client and integration
│   ├── Caching/                 # Metadata caching
│   ├── Merging/                 # Metadata merging strategies
│   └── Artists/                 # Special artist handling
├── Advanced/                    # Tier 3: Advanced features
│   ├── Fingerprinting/          # AcoustID integration
│   ├── Reporting/               # Tagging reports and analytics
│   └── Optimization/            # Performance optimization
├── Config/                      # Configuration handling
│   ├── TaggingSettings.cs       # User configuration model
│   └── SettingsValidator.cs     # Validation for settings
└── Integration/                 # Integration with Lidarr
    ├── LifecycleHandler.cs      # Plugin lifecycle integration
    ├── DownloadClientHook.cs    # Integration with download client
    └── UIComponents/            # UI components for settings
```

## Namespace Structure

Use a consistent namespace structure that mirrors the directory organization:

```csharp
namespace Lidarr.Plugin.Tidal.Tagging.Core.Interfaces
namespace Lidarr.Plugin.Tidal.Tagging.Basic.TagLib
namespace Lidarr.Plugin.Tidal.Tagging.Enhanced.MusicBrainz
namespace Lidarr.Plugin.Tidal.Tagging.Advanced.Fingerprinting
```

## Naming Conventions

Follow these naming conventions for consistency:

| Type | Convention | Example |
|------|------------|---------|
| Interfaces | Prefix with `I` | `ITaggingService` |
| Abstract classes | Prefix with `Base` | `BaseTaggingService` |
| Implementation classes | Clear, descriptive names | `TagLibTaggingService` |
| Factory classes | Suffix with `Factory` | `TaggingServiceFactory` |
| Extension methods | Place in `Extensions` classes | `TaggingExtensions` |
| Utilities | Suffix with `Util` or `Utility` | `TrackMatchingUtil` |

## Dependency Injection

Use dependency injection throughout the system:

```csharp
// Register services
public static IServiceCollection AddTaggingServices(this IServiceCollection services)
{
    // Core services
    services.AddSingleton<ITaggingService, TagLibTaggingService>();
    services.AddSingleton<ITrackMatcher, TrackMatcher>();
    
    // Conditional registrations
    if (settings.UseEnhancedMetadata)
    {
        services.AddSingleton<IMusicBrainzClient, MusicBrainzClient>();
        services.AddSingleton<IMetadataEnhancer, MetadataEnhancer>();
    }
    
    // Advanced services (when enabled)
    if (settings.UseFingerprinting)
    {
        services.AddSingleton<IAcoustIdClient, AcoustIdClient>();
    }
    
    return services;
}
```

## Common Patterns

### Strategy Pattern for Format Handling

Use the strategy pattern for format-specific handling:

```csharp
public interface IFormatHandler
{
    bool CanHandle(string filePath);
    Task<bool> ApplyTagsAsync(string filePath, TrackMetadata track, AlbumMetadata album);
}

public class FormatHandlerFactory
{
    private readonly IEnumerable<IFormatHandler> _handlers;
    
    public FormatHandlerFactory(IEnumerable<IFormatHandler> handlers)
    {
        _handlers = handlers;
    }
    
    public IFormatHandler GetHandler(string filePath)
    {
        return _handlers.FirstOrDefault(h => h.CanHandle(filePath))
            ?? throw new NotSupportedException($"No handler found for {filePath}");
    }
}
```

### Factory Pattern for Service Creation

Use factory pattern for creating appropriate services:

```csharp
public interface ITaggingServiceFactory
{
    ITaggingService CreateService(TaggingOptions options);
}

public class TaggingServiceFactory : ITaggingServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    
    public TaggingServiceFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    
    public ITaggingService CreateService(TaggingOptions options)
    {
        if (options.UseFingerprinting)
            return _serviceProvider.GetRequiredService<FingerprintingTaggingService>();
            
        if (options.UseEnhancedMetadata)
            return _serviceProvider.GetRequiredService<EnhancedTaggingService>();
            
        return _serviceProvider.GetRequiredService<BasicTaggingService>();
    }
}
```

## Logging Strategy

Implement a consistent logging strategy:

```csharp
public static class TaggingLoggerExtensions
{
    private static readonly EventId TaggingEventId = new EventId(9000, "Tagging");
    
    public static void LogTaggingInfo(this ILogger logger, string message, params object[] args)
    {
        logger.LogInformation(TaggingEventId, $"🏷️ {message}", args);
    }
    
    public static void LogTaggingWarning(this ILogger logger, string message, params object[] args)
    {
        logger.LogWarning(TaggingEventId, $"⚠️ {message}", args);
    }
    
    public static void LogTaggingError(this ILogger logger, Exception ex, string message, params object[] args)
    {
        logger.LogError(TaggingEventId, ex, $"❌ {message}", args);
    }
    
    public static void LogTrackMatched(this ILogger logger, string filePath, string trackTitle, double confidence)
    {
        logger.LogInformation(TaggingEventId, $"🔍 Matched \"{Path.GetFileName(filePath)}\" to track \"{trackTitle}\" (confidence: {confidence:P})");
    }
    
    public static void LogMetadataEnhanced(this ILogger logger, string albumTitle, string source)
    {
        logger.LogInformation(TaggingEventId, $"✨ Enhanced metadata for \"{albumTitle}\" using {source}");
    }
}
```

## Test Organization

Organize tests to mirror the production code structure:

```
tests/Lidarr.Plugin.Tidal.Test/Tagging/
├── Core/                        # Core tagging tests
│   ├── Interfaces/              # Interface tests
│   └── Utilities/               # Utility tests
├── Basic/                       # Tier 1 tests
│   ├── TagLib/                  # TagLib# integration tests
│   ├── Mapping/                 # Mapping tests
│   └── Matching/                # Track matching tests
├── Enhanced/                    # Tier 2 tests
│   ├── MusicBrainz/             # MusicBrainz tests
│   └── Merging/                 # Metadata merging tests
├── Advanced/                    # Tier 3 tests
│   └── Fingerprinting/          # Fingerprinting tests
├── Integration/                 # Integration tests
└── Test_Data/                   # Test data and mocks
    ├── Audio_Files/             # Sample audio files
    ├── Mock_Responses/          # API mock responses
    └── Expected_Results/        # Expected test results
```

## Shared Test Utilities

Create shared test utilities:

```csharp
public static class TaggingTestUtilities
{
    public static TaggingOptions CreateDefaultOptions() => new TaggingOptions
    {
        AlbumId = "test_album_id",
        EmbedArtwork = true,
        UseEnhancedMetadata = false,
        UseFingerprinting = false
    };
    
    public static Mock<ILogger<T>> CreateMockLogger<T>() => new Mock<ILogger<T>>();
    
    public static string GetTestFilePath(string category, string filename) => 
        Path.Combine("Test_Data", "Audio_Files", category, filename);
        
    public static string GetMockResponsePath(string apiName, string endpoint, string filename) =>
        Path.Combine("Test_Data", "Mock_Responses", apiName, endpoint, filename);
        
    // Add more helpers as needed
}
```

## Documentation Templates

### Component Documentation Template

```csharp
/// <summary>
/// Provides functionality for [description of component purpose].
/// </summary>
/// <remarks>
/// <para>This component is responsible for [detailed description].</para>
/// <para>Usage example:</para>
/// <code>
/// var component = new Component();
/// var result = await component.DoSomethingAsync();
/// </code>
/// <para>Edge cases handled:</para>
/// <list type="bullet">
///   <item>Various Artists albums: [handling description]</item>
///   <item>International characters: [handling description]</item>
/// </list>
/// </remarks>
public class Component
{
    // Implementation
}
```

### Test Documentation Template

```csharp
/// <summary>
/// Tests for <see cref="ComponentName"/>.
/// </summary>
[TestFixture]
public class ComponentNameTests
{
    /// <summary>
    /// Tests that [description of test scenario].
    /// </summary>
    /// <remarks>
    /// This test verifies [detailed description of what's being tested].
    /// </remarks>
    [Test]
    public void MethodName_Scenario_ExpectedBehavior()
    {
        // Arrange
        
        // Act
        
        // Assert
    }
}
```

## Next Steps

1. Set up the directory structure
2. Create the core interfaces following the naming conventions
3. Implement the dependency injection setup
4. Create test project structure
5. Start developing the first milestone components 