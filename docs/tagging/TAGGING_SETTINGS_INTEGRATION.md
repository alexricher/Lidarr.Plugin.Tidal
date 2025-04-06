# Tagging Settings Integration Guide

This document outlines how to integrate the tagging system settings with the existing Lidarr.Plugin.Tidal settings framework.

## Existing Settings Structure

The Tidal plugin currently uses a field-based settings system where settings are defined with attributes. New tagging settings should follow this pattern for consistency.

## Adding Tagging Settings

### 1. Extend TidalSettings Class

Add tagging-related properties to the existing `TidalSettings` class:

```csharp
public class TidalSettings : IProviderConfig, ICloneable
{
    // Existing settings...
    
    // Tagging Settings Section
    
    [FieldDefinition(100, Label = "Enable Tagging", HelpText = "Enable automatic tagging of downloaded files.", Type = FieldType.Checkbox, Section = "Audio Tagging")]
    public bool EnableTagging { get; set; } = true;
    
    [FieldDefinition(101, Label = "Write Artwork", HelpText = "Embed album artwork in audio files.", Type = FieldType.Checkbox, Section = "Audio Tagging")]
    public bool WriteArtwork { get; set; } = true;

    [FieldDefinition(102, Label = "Prefer Tidal Metadata", HelpText = "Prefer Tidal metadata over other sources when available.", Type = FieldType.Checkbox, Section = "Audio Tagging")]
    public bool PreferTidalMetadata { get; set; } = true;
    
    [FieldDefinition(103, Label = "Enhanced Metadata", HelpText = "Enable enhanced metadata from external sources like MusicBrainz.", Type = FieldType.Checkbox, Section = "Audio Tagging")]
    public bool EnableEnhancedMetadata { get; set; } = false;
    
    [FieldDefinition(104, Label = "Acoustic Fingerprinting", HelpText = "Enable acoustic fingerprinting for more accurate track matching (requires AcoustID).", Type = FieldType.Checkbox, Section = "Audio Tagging", Advanced = true)]
    public bool EnableAcousticFingerprinting { get; set; } = false;
    
    [FieldDefinition(105, Label = "MusicBrainz Base URL", HelpText = "URL for MusicBrainz API access.", Type = FieldType.Textbox, Section = "Audio Tagging", Advanced = true)]
    public string MusicBrainzBaseUrl { get; set; } = "https://musicbrainz.org/ws/2/";
    
    [FieldDefinition(106, Label = "MusicBrainz App Name", HelpText = "Application name used for MusicBrainz API requests.", Type = FieldType.Textbox, Section = "Audio Tagging", Advanced = true)]
    public string MusicBrainzAppName { get; set; } = "Lidarr Tidal Plugin";
    
    [FieldDefinition(107, Label = "AcoustID API Key", HelpText = "API key for AcoustID fingerprinting service.", Type = FieldType.Textbox, Privacy = PrivacyLevel.ApiKey, Section = "Audio Tagging", Advanced = true)]
    public string AcoustIdApiKey { get; set; } = "";
    
    [FieldDefinition(108, Label = "Parallel Tagging Limit", HelpText = "Maximum number of files to tag simultaneously. Higher values may increase performance but use more resources.", Type = FieldType.Number, Section = "Audio Tagging", Advanced = true)]
    public int ParallelTaggingLimit { get; set; } = 4;
    
    [FieldDefinition(109, Label = "API Rate Limit", HelpText = "Maximum requests per minute to external metadata services to avoid rate limiting.", Type = FieldType.Number, Section = "Audio Tagging", Advanced = true)]
    public int ApiRateLimitPerMinute { get; set; } = 50;
    
    [FieldDefinition(110, Label = "Tag Before Import", HelpText = "Apply tags before importing files into Lidarr library.", Type = FieldType.Checkbox, Section = "Audio Tagging")]
    public bool TagBeforeImport { get; set; } = true;
    
    [FieldDefinition(111, Label = "Create Tagging Report", HelpText = "Generate a report of tagging operations for troubleshooting.", Type = FieldType.Checkbox, Section = "Audio Tagging", Advanced = true)]
    public bool CreateTaggingReport { get; set; } = false;
    
    [FieldDefinition(112, Label = "Tagging Report Path", HelpText = "Path where tagging reports will be saved.", Type = FieldType.Path, Section = "Audio Tagging", Advanced = true)]
    public string TaggingReportPath { get; set; } = "";
}
```

### 2. Update SettingsSection Enum

Add tagging to the existing settings sections:

```csharp
public enum SettingsSection
{
    BasicSettings,
    NaturalBehavior,
    RateLimiting,
    QueueManagement,
    AdvancedSettings,
    LegacySettings,
    Diagnostics,
    FileValidation,
    AlbumArtistOrganization,
    HighVolumeHandling,
    StatusFiles,
    CircuitBreaker,
    PerformanceTuning,
    LyricProviders,
    AudioTagging // New section
}
```

### 3. Update SettingsSectionHelper

Add the tagging section to the section header helper:

```csharp
public static string GetSectionHeader(SettingsSection section)
{
    return section switch
    {
        // Existing cases...
        SettingsSection.AudioTagging => "Audio Tagging",
        _ => section.ToString()
    };
}
```

### 4. Add Validation Rules

Add validation for tagging settings in `TidalSettingsValidator`:

```csharp
public TidalSettingsValidator()
{
    // Existing validation rules...
    
    // Tagging validation rules
    RuleFor(c => c.ParallelTaggingLimit)
        .GreaterThanOrEqualTo(1)
        .LessThanOrEqualTo(16)
        .WithMessage("Parallel tagging limit must be between 1 and 16");
        
    RuleFor(c => c.ApiRateLimitPerMinute)
        .GreaterThanOrEqualTo(10)
        .LessThanOrEqualTo(300)
        .WithMessage("API rate limit must be between 10 and 300 requests per minute");
        
    RuleFor(c => c.TaggingReportPath)
        .Must(BeValidPath)
        .When(c => c.CreateTaggingReport)
        .WithMessage("Invalid tagging report path");
}

private bool BeValidPath(string path)
{
    if (string.IsNullOrWhiteSpace(path))
        return true; // Empty path is fine, will use default
        
    try
    {
        return Path.IsPathRooted(path) && Directory.Exists(Path.GetDirectoryName(path));
    }
    catch
    {
        return false;
    }
}
```

## Accessing Settings in Tagging Components

### 1. Inject Settings into Tagging Services

```csharp
public class TaggingService : ITaggingService
{
    private readonly TidalSettings _settings;
    private readonly ITrackMatcher _trackMatcher;
    private readonly ILogger<TaggingService> _logger;
    
    public TaggingService(
        TidalSettings settings,
        ITrackMatcher trackMatcher,
        ILogger<TaggingService> logger)
    {
        _settings = settings;
        _trackMatcher = trackMatcher;
        _logger = logger;
    }
    
    public async Task<TaggingResult> TagFilesAsync(/* params */)
    {
        // Check if tagging is enabled
        if (!_settings.EnableTagging)
        {
            _logger.LogInformation("Tagging is disabled in settings, skipping");
            return new TaggingResult(files, new List<TaggingError>());
        }
        
        // Configure options based on settings
        var options = new TaggingOptions
        {
            IncludeArtwork = _settings.WriteArtwork,
            UseEnhancedMetadata = _settings.EnableEnhancedMetadata,
            UseFingerprinting = _settings.EnableAcousticFingerprinting,
            ParallelLimit = _settings.ParallelTaggingLimit
        };
        
        // Implementation with settings...
    }
}
```

### 2. Create Extension Methods for Settings Integration

```csharp
public static class TaggingSettingsExtensions
{
    public static IServiceCollection AddTaggingServices(this IServiceCollection services, TidalSettings settings)
    {
        // Always register core tagging components
        services.AddSingleton<ITaggingService, TaggingService>();
        services.AddSingleton<ITrackMatcher, TrackMatcher>();
        services.AddSingleton<ITagProcessor, BasicTagProcessor>();
        
        // Register enhanced metadata services conditionally
        if (settings.EnableEnhancedMetadata)
        {
            services.AddSingleton<IMusicBrainzClient>(provider => 
                new MusicBrainzClient(
                    settings.MusicBrainzBaseUrl,
                    settings.MusicBrainzAppName,
                    provider.GetRequiredService<ILogger<MusicBrainzClient>>()));
                    
            services.AddSingleton<ITagEnricher, MusicBrainzEnricher>();
            services.AddSingleton<IMetadataMerger, MetadataMerger>();
        }
        
        // Register fingerprinting services conditionally
        if (settings.EnableAcousticFingerprinting)
        {
            services.AddSingleton<IAcoustIdClient>(provider => 
                new AcoustIdClient(
                    settings.AcoustIdApiKey,
                    provider.GetRequiredService<ILogger<AcoustIdClient>>()));
                    
            services.AddSingleton<IAudioFingerprinter, AcoustIdFingerprinter>();
        }
        
        return services;
    }
    
    public static TaggingOptions ToTaggingOptions(this TidalSettings settings, string albumArtworkUrl = null)
    {
        return new TaggingOptions
        {
            IncludeArtwork = settings.WriteArtwork,
            AlbumArtworkUrl = albumArtworkUrl,
            UseEnhancedMetadata = settings.EnableEnhancedMetadata,
            UseFingerprinting = settings.EnableAcousticFingerprinting,
            PreferTidalMetadata = settings.PreferTidalMetadata,
            ParallelLimit = settings.ParallelTaggingLimit
        };
    }
}
```

## UI Integration for Settings

### 1. Grouping Settings in the UI

To ensure the tagging settings are properly grouped in the UI, make sure all settings use the same section name:

```csharp
[FieldDefinition(100, Label = "Enable Tagging", HelpText = "...", Section = "Audio Tagging")]
```

### 2. Advanced Settings

For settings that should only be shown to advanced users, add the `Advanced = true` property:

```csharp
[FieldDefinition(107, Label = "AcoustID API Key", Type = FieldType.Textbox, Section = "Audio Tagging", Advanced = true)]
```

### 3. Settings UI Order

The field definition number determines the order of the settings in the UI. Make sure to use consistent numbering with gaps for future additions:

```
100-109: Basic tagging settings
110-119: Import-related settings
120-129: Advanced metadata settings
130-139: Performance settings
```

## Registration in Plugin Startup

Add tagging services to the plugin registration:

```csharp
public void Register(IServiceCollection container, IServiceProvider serviceProvider)
{
    // Existing registrations
    container.AddSingleton<ITidalApiClient, TidalApiClient>();
    container.AddSingleton<ITidalDownloadClient, TidalDownloadClient>();
    
    // Get settings
    var settings = container.BuildServiceProvider().GetRequiredService<TidalSettings>();
    
    // Add tagging services
    container.AddTaggingServices(settings);
}
```

## Dynamic Settings Updates

If settings can change while the application is running, implement a mechanism to update services:

```csharp
public class TaggingSettingsMonitor : IDisposable
{
    private readonly ISettingsMonitor<TidalSettings> _settingsMonitor;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TaggingSettingsMonitor> _logger;
    private IDisposable _subscription;
    
    public TaggingSettingsMonitor(
        ISettingsMonitor<TidalSettings> settingsMonitor,
        IServiceProvider serviceProvider,
        ILogger<TaggingSettingsMonitor> logger)
    {
        _settingsMonitor = settingsMonitor;
        _serviceProvider = serviceProvider;
        _logger = logger;
        
        // Subscribe to settings changes
        _subscription = _settingsMonitor.SettingsChanged.Subscribe(OnSettingsChanged);
    }
    
    private void OnSettingsChanged(TidalSettings settings)
    {
        _logger.LogInformation("Tagging settings changed, updating services");
        
        // Update services based on settings
        // This would depend on how your DI container handles runtime updates
        
        // Example: reload clients with new API settings
        if (_serviceProvider.GetService<IMusicBrainzClient>() is MusicBrainzClient mbClient)
        {
            mbClient.UpdateSettings(settings.MusicBrainzBaseUrl, settings.MusicBrainzAppName);
        }
    }
    
    public void Dispose()
    {
        _subscription?.Dispose();
    }
}
```

## Settings Migration

If needed, add migration logic for older settings:

```csharp
public void OnSettingsLoad(TidalSettings settings)
{
    // Convert legacy settings if present
    if (settings.LegacyTaggingSetting != null && settings.EnableTagging == null)
    {
        settings.EnableTagging = settings.LegacyTaggingSetting.Value;
    }
    
    // Set defaults for new settings if not already set
    if (settings.ParallelTaggingLimit <= 0)
    {
        settings.ParallelTaggingLimit = 4;
    }
}
```

## Next Steps

1. Add the tagging settings to the `TidalSettings` class
2. Update the settings validator with validation rules for tagging settings
3. Implement service registrations based on settings
4. Create extension methods for easy settings access in tagging components
5. Test the UI to ensure settings display correctly 