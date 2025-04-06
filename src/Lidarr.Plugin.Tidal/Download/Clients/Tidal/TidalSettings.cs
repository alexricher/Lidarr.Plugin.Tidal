using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using FluentValidation;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.Plugin.Tidal;
using System.Threading; // For Mutex and AbandonedMutexException
using Lidarr.Plugin.Tidal.Indexers.Tidal; // For CacheStrategyType
using NzbDrone.Core.Download.Clients.Tidal.Utilities;
using NzbDrone.Core.Download;
using Lidarr.Plugin.Tidal.Download.Clients.Tidal.Utilities;
using Lidarr.Plugin.Tidal.Services.FileSystem;
using Lidarr.Plugin.Tidal.Services.Behavior;

// Suppresses warnings about obsolete properties we need to keep for backward compatibility
#pragma warning disable CS0618

namespace NzbDrone.Core.Download.Clients.Tidal
{
    /// <summary>
    /// Validator for TidalSettings, enforcing constraints on configuration values.
    /// Validates settings for authenticity, filesystem access, download behavior, and rate limiting.
    /// </summary>
    public class TidalSettingsValidator : AbstractValidator<TidalSettings>
    {
        /// <summary>
        /// Initializes a new instance of the TidalSettingsValidator class.
        /// Sets up validation rules for all Tidal plugin settings.
        /// </summary>
        public TidalSettingsValidator()
        {
            RuleFor(x => x.DownloadPath).IsValidPath();

            // Basic Settings Validation
            RuleFor(x => x.MaxConcurrentDownloads)
                .GreaterThan(0)
                .WithMessage("Maximum concurrent track downloads must be greater than 0")
                .LessThanOrEqualTo(4)
                .WithMessage("Maximum concurrent downloads cannot exceed 4. Higher values can cause file corruption during high volume operations.");

            RuleFor(x => x.LyricProviderUrl)
                .NotEmpty()
                .When(x => x.UseLRCLIB) // Keep original boolean name for condition
                .WithMessage("Backup Lyric Provider URL cannot be empty when 'Use Backup Lyric Provider' is enabled.");
            // Optional: Add URI validation if needed
            // RuleFor(x => x.LyricProviderUrl)
            //     .Must(uri => Uri.TryCreate(uri, UriKind.Absolute, out _))
            //     .When(x => x.UseLRCLIB && !string.IsNullOrWhiteSpace(x.LyricProviderUrl))
            //     .WithMessage("Backup Lyric Provider URL must be a valid URL.");


            // Natural behavior validation rules
            RuleFor(x => x.SessionDurationMinutes)
                .GreaterThan(0)
                .When(x => x.EnableNaturalBehavior)
                .WithMessage("Session duration must be greater than 0 minutes");

            RuleFor(x => x.BreakDurationMinutes)
                .GreaterThan(0)
                .When(x => x.EnableNaturalBehavior)
                .WithMessage("Break duration must be greater than 0 minutes");

            // Advanced behavior validation rules
            RuleFor(x => x.TrackToTrackDelayMin)
                .GreaterThan(0)
                .When(x => x.EnableNaturalBehavior && x.SimulateListeningPatterns)
                .WithMessage("Minimum track-to-track delay must be greater than 0 seconds");

            RuleFor(x => x.TrackToTrackDelayMax)
                .GreaterThanOrEqualTo(x => x.TrackToTrackDelayMin)
                .When(x => x.EnableNaturalBehavior && x.SimulateListeningPatterns)
                .WithMessage("Maximum track-to-track delay must be greater than or equal to minimum delay");

            RuleFor(x => x.AlbumToAlbumDelayMin)
                .GreaterThan(0)
                .When(x => x.EnableNaturalBehavior && x.SimulateListeningPatterns)
                .WithMessage("Minimum album-to-album delay must be greater than 0 seconds");

            RuleFor(x => x.AlbumToAlbumDelayMax)
                .GreaterThanOrEqualTo(x => x.AlbumToAlbumDelayMin)
                .When(x => x.EnableNaturalBehavior && x.SimulateListeningPatterns)
                .WithMessage("Maximum album-to-album delay must be greater than or equal to minimum delay");

            // High volume validation rules
            RuleFor(x => x.HighVolumeThreshold)
                .GreaterThan(0)
                .When(x => x.EnableHighVolumeHandling)
                .WithMessage("High volume threshold must be greater than 0 items");

            RuleFor(x => x.HighVolumeSessionMinutes)
                .GreaterThan(0)
                .When(x => x.EnableHighVolumeHandling)
                .WithMessage("High volume session duration must be greater than 0 minutes");

            RuleFor(x => x.HighVolumeBreakMinutes)
                .GreaterThan(0)
                .When(x => x.EnableHighVolumeHandling)
                .WithMessage("High volume break duration must be greater than 0 minutes");

            // Advanced settings validation
            RuleFor(x => x.DownloadTrackRetryCount)
                .GreaterThan(0)
                .WithMessage("Download track retry count must be greater than 0");

            RuleFor(x => x.MaxTrackFailures)
                .GreaterThan(0)
                .WithMessage("Max track failures must be greater than 0");

            RuleFor(x => x.DownloadItemTimeoutMinutes)
                .GreaterThan(0)
                .WithMessage("Download item timeout must be greater than 0 minutes");

            RuleFor(x => x.StallDetectionThresholdMinutes)
                .GreaterThan(0)
                .WithMessage("Stall detection threshold must be greater than 0 minutes");

            RuleFor(x => x.StatsLogIntervalMinutes)
                .GreaterThan(0)
                .WithMessage("Stats log interval must be greater than 0 minutes");

            // Add validation for QueueCapacity
            RuleFor(x => x.QueueCapacity)
                .GreaterThanOrEqualTo(50)
                .WithMessage("Queue capacity must be at least 50")
                .LessThanOrEqualTo(500)
                .WithMessage("Queue capacity cannot exceed 500");

            RuleFor(x => x.DownloadPath)
                .NotEmpty()
                .WithMessage("Download path cannot be empty")
                .Must(path => !path.Contains("?") && !path.Contains("*"))
                .WithMessage("Download path cannot contain wildcards")
                .When(x => !string.IsNullOrWhiteSpace(x.DownloadPath));

            RuleFor(x => x.MaxDownloadsPerHour)
                .Must(x => x == 0 || (x >= 1 && x <= TokenBucketRateLimiter.MAX_DOWNLOADS_PER_HOUR))
                .WithMessage($"Maximum downloads per hour must be 0 (unlimited) or between 1 and {TokenBucketRateLimiter.MAX_DOWNLOADS_PER_HOUR}");

            // Add validation for circuit breaker settings after MaxDownloadsPerHour validation
            RuleFor(x => x.CircuitBreakerFailureThreshold)
                .GreaterThan(0)
                .WithMessage("Circuit breaker failure threshold must be greater than 0");

            RuleFor(x => x.CircuitBreakerResetTimeMinutes)
                .GreaterThan(0)
                .WithMessage("Circuit breaker reset time must be greater than 0 minutes");

            RuleFor(x => x.CircuitBreakerHalfOpenMaxAttempts)
                .GreaterThan(0)
                .WithMessage("Circuit breaker half-open max attempts must be greater than 0");

            RuleFor(x => x.QueueOperationTimeoutSeconds)
                .GreaterThan(0)
                .WithMessage("Queue operation timeout must be greater than 0 seconds");

            RuleFor(x => x.MaxTrackFailures)
                .GreaterThan(0)
                .WithMessage("Max track failures must be greater than 0");

            // Add validation for item processing delay
            RuleFor(x => x.ItemProcessingDelayMs)
                .GreaterThanOrEqualTo(0)
                .WithMessage("Item processing delay must be greater than or equal to 0 milliseconds")
                .LessThanOrEqualTo(5000)
                .WithMessage("Item processing delay should not exceed 5000 milliseconds (5 seconds)");
        }
    }

    /// <summary>
    /// Predefined behavior profiles for Tidal downloads.
    /// Each profile represents a different download pattern and behavior.
    /// </summary>
    public enum BehaviorProfile
    {
        /// <summary>
        /// Balanced profile with moderate download rates and natural behavior.
        /// Good for general use with moderate-sized libraries.
        /// </summary>
        Balanced = 0,
        
        /// <summary>
        /// Simulates a casual listener with lower download rates and longer breaks.
        /// Recommended for smaller libraries or when minimal impact is desired.
        /// </summary>
        CasualListener = 1,
        
        /// <summary>
        /// Simulates a music enthusiast with higher download rates.
        /// Optimized for larger libraries when faster downloads are needed.
        /// </summary>
        MusicEnthusiast = 2,
        
        /// <summary>
        /// Custom profile with user-defined settings.
        /// Allows complete customization of all behavior parameters.
        /// </summary>
        Custom = 3,
        
        /// <summary>
        /// Automatically adjusts behavior based on download volume.
        /// Switches between different modes based on queue size.
        /// </summary>
        Automatic = 4
    }

    /// <summary>
    /// Supported lyric provider sources for track lyrics.
    /// Used when Tidal doesn't provide lyrics for a track.
    /// </summary>
    public enum LyricProviderSource
    {
        /// <summary>
        /// LRCLIB lyric provider (lrclib.net).
        /// Provides time-synchronized lyrics for many tracks.
        /// </summary>
        LRCLIB = 0
        // Future providers can be added here
    }

    /// <summary>
    /// Tidal country options for API region selection.
    /// Affects content availability and regional restrictions.
    /// </summary>
    public enum TidalCountry
    {
        /// <summary>United States - US</summary>
        USA = 0,
        /// <summary>United Kingdom - GB</summary>
        UK = 1,
        /// <summary>Canada - CA</summary>
        Canada = 2,
        /// <summary>Australia - AU</summary>
        Australia = 3,
        /// <summary>Germany - DE</summary>
        Germany = 4,
        /// <summary>France - FR</summary>
        France = 5,
        /// <summary>Japan - JP</summary>
        Japan = 6,
        /// <summary>Brazil - BR</summary>
        Brazil = 7,
        /// <summary>Mexico - MX</summary>
        Mexico = 8,
        /// <summary>Netherlands - NL</summary>
        Netherlands = 9,
        /// <summary>Spain - ES</summary>
        Spain = 10,
        /// <summary>Sweden - SE</summary>
        Sweden = 11,
        /// <summary>Custom country code - allows specifying a non-listed country</summary>
        Custom = 12
    }

    /// <summary>
    /// Audio quality options for Tidal downloads.
    /// </summary>
    public enum AudioQualityProfile
    {
        /// <summary>AAC 96kbps quality</summary>
        Low = 0,
        
        /// <summary>AAC 320kbps quality</summary>
        High = 1,
        
        /// <summary>FLAC 16-bit/44.1kHz lossless quality</summary>
        HiFi = 2,
        
        /// <summary>MQA (Master Quality Authenticated) quality - will be mapped to HI_RES_LOSSLESS in TidalSharp</summary>
        MQA = 3,
        
        /// <summary>FLAC 24-bit/96kHz high resolution lossless quality</summary>
        HiRes = 4
    }

    /// <summary>
    /// Authentication types supported by the Tidal plugin.
    /// </summary>
    public enum TidalAuthType
    {
        /// <summary>
        /// OAuth PKCE-based authentication flow.
        /// This is the recommended and more secure authentication method.
        /// </summary>
        OAuth = 0,
        
        /// <summary>
        /// Legacy authentication using username and password.
        /// This method is deprecated and may be removed in future versions.
        /// </summary>
        Legacy = 1
    }

    /// <summary>
    /// Configuration settings for the Tidal download client.
    /// Includes authentication, download behavior, rate limiting, and natural behavior simulation options.
    /// Settings are persisted across Lidarr restarts and can be configured through the UI.
    /// </summary>
    public class TidalSettings : IProviderConfig, ICloneable
    {
        private static readonly TidalSettingsValidator Validator = new TidalSettingsValidator();
        private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        private IFileSystemService _fileSystemService;
        private IBehaviorProfileService _behaviorProfileService;

        /// <summary>
        /// Initializes a new instance of the TidalSettings class.
        /// </summary>
        public TidalSettings()
        {
            // Default constructor required for deserialization
        }

        /// <summary>
        /// Initializes a new instance of the TidalSettings class with the specified services.
        /// </summary>
        /// <param name="fileSystemService">The file system service to use for file operations</param>
        /// <param name="behaviorProfileService">The behavior profile service to use for profile operations</param>
        public TidalSettings(IFileSystemService fileSystemService, IBehaviorProfileService behaviorProfileService)
        {
            _fileSystemService = fileSystemService;
            _behaviorProfileService = behaviorProfileService;
        }

        /// <summary>
        /// Gets or sets the authentication token for the Tidal API.
        /// This token is obtained after successful login and is required for all API operations.
        /// </summary>
        [FieldDefinition(0, Label = "Authentication", Type = FieldType.Select, SelectOptions = typeof(TidalAuthType), HelpText = "Choose authentication method")]
        public int AuthType { get; set; } = 0;

        /// <summary>
        /// Gets or sets the access token for Tidal API authentication.
        /// This is the primary credential for API access.
        /// </summary>
        [FieldDefinition(1, Label = "Access Token", Type = FieldType.Textbox, Privacy = PrivacyLevel.ApiKey, HelpText = "Enter your Tidal access token")]
        public string AccessToken { get; set; }

        /// <summary>
        /// Gets or sets the refresh token for Tidal API authentication.
        /// Used to obtain a new access token when the current one expires.
        /// </summary>
        [FieldDefinition(2, Label = "Refresh Token", Type = FieldType.Textbox, Privacy = PrivacyLevel.ApiKey, HelpText = "Enter your Tidal refresh token")]
        public string RefreshToken { get; set; }

        /// <summary>
        /// Gets or sets the country code for Tidal API region selection.
        /// Affects content availability based on regional restrictions.
        /// </summary>
        [FieldDefinition(3, Label = "Country", Type = FieldType.Select, SelectOptions = typeof(TidalCountry), HelpText = "Select country for Tidal API")]
        public int Country { get; set; } = 0;

        /// <summary>
        /// Gets or sets the custom country code when Country is set to Custom.
        /// Must be a valid ISO 3166-1 alpha-2 country code (e.g., "US", "GB").
        /// </summary>
        [FieldDefinition(4, Label = "Custom Country Code", Type = FieldType.Textbox, HelpText = "Custom country code (ISO 3166-1 alpha-2)")]
        public string CustomCountryCode { get; set; }

        /// <summary>
        /// Gets or sets the behavior profile for download patterns and rate limiting.
        /// Affects download speeds, concurrency, and natural behavior simulation.
        /// </summary>
        [FieldDefinition(9, Label = "Behavior Profile", Type = FieldType.Select, SelectOptions = typeof(BehaviorProfile), HelpText = "Set download behavior pattern", Section = "Natural Behavior")]
        public int BehaviorProfileType { get; set; } = 0; // Use 0 instead of (int)BehaviorProfile.Balanced

        // Authentication Settings
        [FieldDefinition(0, Label = "Tidal URL", HelpText = "Use this to sign into Tidal.", Section = "Authentication")]
        public string TidalUrl { get => TidalAPI.Instance?.Client?.GetPkceLoginUrl() ?? ""; set { } }

        [FieldDefinition(1, Label = "Redirect Url", Type = FieldType.Textbox, Section = "Authentication")]
        public string RedirectUrl { get; set; } = "";

        [FieldDefinition(2, Label = "Config Path", Type = FieldType.Textbox, HelpText = "This is the directory where you account's information is stored so that it can be reloaded later.", Section = "Authentication")]
        public string ConfigPath { get; set; } = "";

        // Combined property to get the actual country code
        public string CountryCode
        {
            get
            {
                if ((TidalCountry)Country == TidalCountry.Custom && !string.IsNullOrEmpty(CustomCountryCode))
                    return CustomCountryCode.ToUpper();

                return (TidalCountry)Country switch
                {
                    TidalCountry.USA => "US",
                    TidalCountry.UK => "GB",
                    TidalCountry.Canada => "CA",
                    TidalCountry.Australia => "AU",
                    TidalCountry.Germany => "DE",
                    TidalCountry.France => "FR",
                    TidalCountry.Japan => "JP",
                    TidalCountry.Brazil => "BR",
                    TidalCountry.Mexico => "MX",
                    TidalCountry.Netherlands => "NL",
                    TidalCountry.Spain => "ES",
                    TidalCountry.Sweden => "SE",
                    _ => "US" // Default to US
                };
            }
        }

        // Basic Settings section
        [FieldDefinition(5, Label = "Download Directory", Type = FieldType.Path, HelpText = "Directory where Tidal music will be downloaded to before being imported by Lidarr", Section = "Basic Settings")]
        public string DownloadPath { get; set; } = "";

        [FieldDefinition(6, Label = "Max Concurrent Track Downloads", HelpText = "Maximum number of tracks to download simultaneously. PERFORMANCE IMPACT: 1 = Most stable, slowest. 2-3 = Good balance. 4+ = Faster but may cause issues during large operations.", Section = "Performance Tuning")]
        public int MaxConcurrentDownloads { get; set; } = 2;

        // Backwards compatibility property - not shown in UI but used by code
        [Obsolete("Use MaxConcurrentDownloads instead")]
        public int MaxConcurrentTrackDownloads
        {
            get => MaxConcurrentDownloads;
            set => MaxConcurrentDownloads = value;
        }

        // Legacy property - not shown in UI but used by code
        [Obsolete("Use SimulateListeningPatterns instead")]
        public bool SimulateDelays
        {
            get => SimulateListeningPatterns; // Always enabled now through TrackToTrackDelay settings
            set => SimulateListeningPatterns = value;
        }

        [FieldDefinition(6, Label = "Serialize File Operations", HelpText = "Prevents file corruption by serializing file I/O operations during high load", Type = FieldType.Checkbox, Section = "Performance Tuning")]
        public bool SerializeFileOperations { get; set; } = true;

        [FieldDefinition(7, Label = "Preferred Quality", Type = FieldType.Select, SelectOptions = typeof(AudioQualityProfile), HelpText = "Select the preferred audio quality for downloads", Section = "Basic Settings")]
        public int PreferredQuality { get; set; } = (int)AudioQualityProfile.HiFi;

        [FieldDefinition(8, Label = "Prefer Highest Quality", Type = FieldType.Checkbox, HelpText = "Choose highest quality available", Section = "Basic Settings")]
        public bool PreferHighestQuality { get; set; } = true;

        [FieldDefinition(9, Label = "Prefer Explicit", Type = FieldType.Checkbox, HelpText = "Prefer explicit versions when available", Section = "Basic Settings")]
        public bool PreferExplicit { get; set; } = true;

        [FieldDefinition(10, Label = "Include Lyrics", Type = FieldType.Checkbox, HelpText = "Include lyrics in downloads when available", Section = "Basic Settings")]
        public bool IncludeLyrics { get; set; } = true;

        [FieldDefinition(11, Label = "Add Cover Art", Type = FieldType.Checkbox, HelpText = "Embed album covers in audio files", Section = "Basic Settings")]
        public bool AddCoverArt { get; set; } = true;

        // File Format Settings
        [FieldDefinition(12, Label = "Extract FLAC From M4A", HelpText = "Extracts FLAC data from the Tidal-provided M4A files.", HelpTextWarning = "This requires FFMPEG and FFProbe to be available to Lidarr.", Type = FieldType.Checkbox, Section = "File Format")]
        public bool ExtractFlac { get; set; } = false;

        [FieldDefinition(13, Label = "Re-encode AAC into MP3", HelpText = "Re-encodes AAC data from the Tidal-provided M4A files into MP3s.", HelpTextWarning = "This requires FFMPEG and FFProbe to be available to Lidarr.", Type = FieldType.Checkbox, Section = "File Format")]
        public bool ReEncodeAAC { get; set; } = false;

        [FieldDefinition(14, Label = "Save Synced Lyrics", HelpText = "Saves synced lyrics to a separate .lrc file if available. Requires .lrc to be allowed under Import Extra Files.", Type = FieldType.Checkbox, Section = "File Format")]
        public bool SaveSyncedLyrics { get; set; } = false;

        // Lyrics Settings
        [FieldDefinition(15, Label = "Use Backup Lyric Provider", HelpText = "If Tidal does not have plain or synced lyrics for a track, the plugin will attempt to get them from a backup provider.", Type = FieldType.Checkbox, Section = "Lyrics")]
        public bool UseLRCLIB { get; set; } = false; // Keep original name for compatibility

        [FieldDefinition(16, Label = "Backup Lyric Provider", HelpText = "Select the backup provider to use if Tidal lyrics are missing and 'Use Backup Lyric Provider' is enabled.", Type = FieldType.Select, SelectOptions = typeof(LyricProviderSource), Section = "Lyrics")]
        public int BackupLyricProvider { get; set; } = (int)LyricProviderSource.LRCLIB;

        // Hidden property - not shown in UI
        public string LyricProviderUrl { get; set; } = "lrclib.net";

        // Only shown in UI when needed
        [FieldDefinition(17, Label = "LRCLIB URL", HelpText = "URL for the LRCLIB instance to use.", Type = FieldType.Textbox, Section = "Lyrics")]
        public string LyricProviderUrlUI
        {
            get => LyricProviderUrl;
            set => LyricProviderUrl = value;
        }

        // Status Files Settings
        [FieldDefinition(18, Label = "Generate Status Files", HelpText = "Enables generation of JSON status files for monitoring downloads. Required for TidalDownloadViewer.", Type = FieldType.Checkbox, Section = "Status Files")]
        public bool GenerateStatusFiles { get; set; } = false;

        [FieldDefinition(19, Label = "Status Files Path", HelpText = "Path where download status files will be stored. Must be writable by Lidarr.", Type = FieldType.Path, Section = "Status Files")]
        public string StatusFilesPath { get; set; } = "";

        // Queue Persistence
        [FieldDefinition(20, Label = "Enable Queue Persistence", HelpText = "Saves the download queue when Lidarr shuts down and restores it when it starts up again.", Type = FieldType.Checkbox, Section = "Queue Persistence")]
        public bool EnableQueuePersistence { get; set; } = true;

        [FieldDefinition(21, Label = "Queue Persistence Path", HelpText = "Path where queue data will be saved. If empty, will use the Status Files Path if set, or Download Path as fallback.", Type = FieldType.Path, Section = "Queue Persistence")]
        public string QueuePersistencePath { get; set; } = "";

        /// <summary>
        /// Gets the effective queue persistence path to use based on available settings.
        /// Follows a fallback strategy if the primary path is not set:
        /// 1. Uses explicitly configured QueuePersistencePath if not empty
        /// 2. Falls back to StatusFilesPath if available
        /// 3. Finally uses DownloadPath as a last resort
        /// </summary>
        /// <remarks>
        /// This property ensures queue persistence always has a valid location,
        /// handling Docker environments and permission issues appropriately.
        /// </remarks>
        public string ActualQueuePersistencePath
        {
            get
            {
                if (!EnableQueuePersistence)
                    return "";

                if (!string.IsNullOrWhiteSpace(QueuePersistencePath))
                    return QueuePersistencePath;

                if (!string.IsNullOrWhiteSpace(StatusFilesPath))
                    return StatusFilesPath;

                return DownloadPath;
            }
        }

        // Add feature toggle for experimental features
        [FieldDefinition(22, Label = "Enable Experimental Features", HelpText = "Enable experimental features that may not be fully stable.", Type = FieldType.Checkbox, Section = "Advanced")]
        public bool EnableExperimentalFeatures { get; set; } = false;

        // Additional advanced download retry settings
        [FieldDefinition(23, Label = "Download Track Retry Count", HelpText = "Number of times to retry downloading a track if it fails.", Type = FieldType.Number, Section = "Advanced Settings", Advanced = true)]
        public int DownloadTrackRetryCount { get; set; } = 5;

        [FieldDefinition(24, Label = "Max Track Failures", HelpText = "Maximum number of failures allowed for a track before skipping it.", Type = FieldType.Number, Section = "Advanced Settings", Advanced = true)]
        public int MaxTrackFailures { get; set; } = 5;

        [FieldDefinition(25, Label = "Download Item Timeout (Minutes)", HelpText = "Maximum time allowed for downloading a single item before timing out.", Type = FieldType.Number, Section = "Advanced Settings", Advanced = true)]
        public int DownloadItemTimeoutMinutes { get; set; } = 45;

        [FieldDefinition(26, Label = "Stall Detection Threshold (Minutes)", HelpText = "How long to wait before considering the download queue stalled and automatically resetting it.", Type = FieldType.Number, Section = "Advanced Settings", Advanced = true)]
        public int StallDetectionThresholdMinutes { get; set; } = 20;

        [FieldDefinition(27, Label = "Stats Log Interval (Minutes)", HelpText = "How often to log queue statistics to the log file.", Type = FieldType.Number, Section = "Advanced Settings", Advanced = true)]
        public int StatsLogIntervalMinutes { get; set; } = 15;

        [FieldDefinition(28, Label = "Queue Capacity", HelpText = "Maximum number of albums that can be queued at once. Increase for larger libraries but may impact performance. Default: 100, Range: 50-500", Type = FieldType.Number, Section = "Queue Management")]
        public int QueueCapacity { get; set; } = 100;

        // Cache settings
        [FieldDefinition(29, Label = "Memory Limit (MB)", HelpText = "Maximum memory to use for cache. Set to 0 for no limit.", Type = FieldType.Number, Section = "Cache Settings", Advanced = true)]
        public int MemoryLimitMB { get; set; } = 100;

        // Natural behavior settings
        [FieldDefinition(30, Label = "Enable Natural Behavior", HelpText = "Simulates human-like download patterns to help avoid detection systems.", Type = FieldType.Checkbox, Section = "Natural Behavior")]
        public bool EnableNaturalBehavior { get; set; } = true;

        [FieldDefinition(31, Label = "Session Duration", HelpText = "How long to continuously download before taking a break (in minutes).", Type = FieldType.Number, Section = "Natural Behavior")]
        public int SessionDurationMinutes { get; set; } = 120;

        [FieldDefinition(32, Label = "Break Duration", HelpText = "How long to pause between download sessions (in minutes).", Type = FieldType.Number, Section = "Natural Behavior")]
        public int BreakDurationMinutes { get; set; } = 30;

        [FieldDefinition(33, Label = "Adapt to Time of Day", HelpText = "Only download during specific hours of the day.", Type = FieldType.Checkbox, Section = "Natural Behavior")]
        public bool TimeOfDayAdaptation { get; set; } = false;

        [FieldDefinition(34, Label = "Active Hours Start", HelpText = "Hour to begin downloads (0-23, 24-hour format).", Type = FieldType.Number, Section = "Natural Behavior")]
        public int ActiveHoursStart { get; set; } = 8;

        [FieldDefinition(35, Label = "Active Hours End", HelpText = "Hour to stop downloads (0-23, 24-hour format).", Type = FieldType.Number, Section = "Natural Behavior")]
        public int ActiveHoursEnd { get; set; } = 22;

        // Add Circuit Breaker section
        [FieldDefinition(36, Label = "Circuit Breaker Failure Threshold", 
            HelpText = "Number of consecutive failures before the circuit breaker opens", 
            Type = FieldType.Number, Section = "Circuit Breaker", Advanced = true)]
        public int CircuitBreakerFailureThreshold { get; set; } = 8;

        [FieldDefinition(37, Label = "Circuit Breaker Reset Time (Minutes)", 
            HelpText = "Time to wait before attempting to reset the circuit breaker", 
            Type = FieldType.Number, Section = "Circuit Breaker", Advanced = true)]
        public int CircuitBreakerResetTimeMinutes { get; set; } = 15;

        [FieldDefinition(38, Label = "Circuit Breaker Half-Open Max Attempts", 
            HelpText = "Maximum number of operations to allow in half-open state before fully reopening", 
            Type = FieldType.Number, Section = "Circuit Breaker", Advanced = true)]
        public int CircuitBreakerHalfOpenMaxAttempts { get; set; } = 3;

        // Queue operation timeout
        [FieldDefinition(39, Label = "Queue Operation Timeout (Seconds)", 
            HelpText = "Maximum time allowed for queue operations", 
            Type = FieldType.Number, Section = "Advanced Settings", Advanced = true)]
        public int QueueOperationTimeoutSeconds { get; set; } = 60;
        
        // Listening Pattern Simulation
        [FieldDefinition(40, Label = "Simulate Listening Patterns", HelpText = "Add realistic delays between tracks and albums to simulate actual listening behavior.", Type = FieldType.Checkbox, Section = "Listening Pattern Simulation")]
        public bool SimulateListeningPatterns { get; set; } = true;

        [FieldDefinition(41, Label = "Track-to-Track Delay Min", HelpText = "Minimum delay between tracks in the same album (seconds).", Type = FieldType.Number, Section = "Listening Pattern Simulation")]
        public float TrackToTrackDelayMin { get; set; } = 0.5f;

        [FieldDefinition(42, Label = "Track-to-Track Delay Max", HelpText = "Maximum delay between tracks in the same album (seconds).", Type = FieldType.Number, Section = "Listening Pattern Simulation")]
        public float TrackToTrackDelayMax { get; set; } = 3.0f;

        [FieldDefinition(43, Label = "Album-to-Album Delay Min", HelpText = "Minimum delay between different albums (seconds).", Type = FieldType.Number, Section = "Listening Pattern Simulation")]
        public float AlbumToAlbumDelayMin { get; set; } = 5.0f;

        [FieldDefinition(44, Label = "Album-to-Album Delay Max", HelpText = "Maximum delay between different albums (seconds).", Type = FieldType.Number, Section = "Listening Pattern Simulation")]
        public float AlbumToAlbumDelayMax { get; set; } = 15.0f;

        // Rate limiting
        [FieldDefinition(45, Label = "Max Downloads Per Hour", Type = FieldType.Number, HelpText = "Maximum downloads per hour to avoid rate limiting. IMPACT: 0 = unlimited (not recommended). 20-30 = balanced. 10-15 = most conservative.", Section = "Performance Tuning", Advanced = true)]
        public int MaxDownloadsPerHour { get; set; } = 30;

        // Album/Artist Organization 
        [FieldDefinition(46, Label = "Complete Albums", HelpText = "Complete all tracks from the same album before moving to another album.", Type = FieldType.Checkbox, Section = "Album/Artist Organization")]
        public bool CompleteAlbums { get; set; } = true;

        [FieldDefinition(47, Label = "Preserve Artist Context", HelpText = "After completing an album, prefer the next album from the same artist.", Type = FieldType.Checkbox, Section = "Album/Artist Organization")]
        public bool PreferArtistGrouping { get; set; } = true;

        [FieldDefinition(48, Label = "Sequential Track Order", HelpText = "Download tracks within an album in sequential order rather than randomly.", Type = FieldType.Checkbox, Section = "Album/Artist Organization")]
        public bool SequentialTrackOrder { get; set; } = true;
        
        [FieldDefinition(49, Label = "Randomize Album Order", HelpText = "Randomize the order in which albums are downloaded.", Type = FieldType.Checkbox, Section = "Album/Artist Organization")]
        public bool RandomizeAlbumOrder { get; set; } = false;
        
        // High Volume Download Settings
        [FieldDefinition(50, Label = "Enable High Volume Handling", HelpText = "Enables special handling for very large download queues to avoid rate limiting.", Type = FieldType.Checkbox, Section = "High Volume Handling")]
        public bool EnableHighVolumeHandling { get; set; } = true;

        [FieldDefinition(51, Label = "High Volume Threshold", HelpText = "Number of items in queue to trigger high volume mode.", Type = FieldType.Number, Section = "High Volume Handling")]
        public int HighVolumeThreshold { get; set; } = 500;

        [FieldDefinition(52, Label = "High Volume Session Minutes", HelpText = "Session duration for high volume mode (minutes).", Type = FieldType.Number, Section = "High Volume Handling")]
        public int HighVolumeSessionMinutes { get; set; } = 90;

        [FieldDefinition(53, Label = "High Volume Break Minutes", HelpText = "Break duration for high volume mode (minutes).", Type = FieldType.Number, Section = "High Volume Handling")]
        public int HighVolumeBreakMinutes { get; set; } = 45;
        
        // Backward compatibility properties for millisecond delays
        // These are not shown in UI but used by code
        public int MinDelayBetweenTracksMs
        {
            get => (int)(TrackToTrackDelayMin * 1000);
            set => TrackToTrackDelayMin = value / 1000.0f;
        }
        
        public int MaxDelayBetweenTracksMs
        {
            get => (int)(TrackToTrackDelayMax * 1000);
            set => TrackToTrackDelayMax = value / 1000.0f;
        }
        
        public int MinDelayBetweenAlbumsMs
        {
            get => (int)(AlbumToAlbumDelayMin * 1000);
            set => AlbumToAlbumDelayMin = value / 1000.0f;
        }
        
        public int MaxDelayBetweenAlbumsMs
        {
            get => (int)(AlbumToAlbumDelayMax * 1000);
            set => AlbumToAlbumDelayMax = value / 1000.0f;
        }
        
        // Advanced security settings - not displayed in UI but used by code
        public bool RotateUserAgent { get; set; } = false;
        public bool VaryConnectionParameters { get; set; } = false;

        // Legacy skip simulation properties consolidated into advanced section
        [FieldDefinition(80, Label = "Simulate Skips", HelpText = "Simulate skipping tracks to appear more natural.", Type = FieldType.Checkbox, Section = "Advanced Settings", Advanced = true)]
        public bool SimulateSkips { get; set; } = false;

        [FieldDefinition(81, Label = "Skip Probability", HelpText = "Probability of skipping a track (0-100%).", Type = FieldType.Number, Section = "Advanced Settings", Advanced = true)]
        public float SkipProbability { get; set; } = 10.0f;

        // Add File Validation Settings section
        /// <summary>
        /// Gets or sets a value indicating whether file validation is enabled for downloaded tracks.
        /// </summary>
        /// <remarks>
        /// When enabled, the system will validate downloaded audio files for corruption by
        /// checking file signatures, structure, and size. Corrupted files will be automatically
        /// requeued for download to ensure the music library contains only valid files.
        /// </remarks>
        [FieldDefinition(52, Label = "Enable File Validation", HelpText = "Validates downloaded files for corruption and automatically retries corrupted files", Type = FieldType.Checkbox, Section = "File Validation")]
        public bool EnableFileValidation { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum number of retry attempts for files that fail validation.
        /// </summary>
        /// <remarks>
        /// Limits the number of times the system will attempt to redownload a file that fails validation.
        /// After this number of attempts, the file will be skipped to prevent infinite retry loops.
        /// This helps handle situations where a track might be consistently corrupted at the source.
        /// </remarks>
        [FieldDefinition(53, Label = "File Validation Max Retries", HelpText = "Maximum number of times to retry a file if validation fails", Type = FieldType.Number, Section = "File Validation")]
        public int FileValidationMaxRetries { get; set; } = 3;

        /// <summary>
        /// Gets or sets the minimum valid file size in kilobytes for downloaded audio files.
        /// </summary>
        /// <remarks>
        /// Files smaller than this threshold will be considered corrupted or incomplete.
        /// This helps to quickly identify truncated downloads without performing detailed validation.
        /// The default value of 32KB is sufficient to detect most corrupted audio files while
        /// avoiding false positives for legitimate short audio clips.
        /// </remarks>
        [FieldDefinition(54, Label = "Minimum Valid File Size (KB)", HelpText = "Files smaller than this size will be considered corrupted", Type = FieldType.Number, Section = "File Validation")]
        public int FileValidationMinSize { get; set; } = 32;

        /// <summary>
        /// Gets or sets a value indicating whether automatic quality upgrades are enabled.
        /// </summary>
        /// <remarks>
        /// When enabled, the system will replace existing audio files with higher quality versions
        /// when available. This includes upgrading from lossy formats (M4A) to lossless formats (FLAC),
        /// or upgrading from standard resolution to high resolution audio within the same format.
        /// Files are analyzed to determine their quality level before replacement.
        /// </remarks>
        [FieldDefinition(55, Label = "Auto-Upgrade Quality", HelpText = "Automatically replace files with higher quality versions when available", Type = FieldType.Checkbox, Section = "File Validation")]
        public bool EnableQualityUpgrade { get; set; } = true;
        
        /// <summary>
        /// Gets or sets a value indicating whether backup copies of replaced files should be kept.
        /// </summary>
        /// <remarks>
        /// When enabled, files that are replaced due to quality upgrades will be saved with a .bak extension.
        /// This provides a fallback option if the new file has issues or if the user prefers the original version.
        /// Note that enabling this option will increase disk space usage over time as multiple versions of files are retained.
        /// </remarks>
        [FieldDefinition(56, Label = "Keep Backup Files", HelpText = "Keep backup copies of replaced files with .bak extension", Type = FieldType.Checkbox, Section = "File Validation")]
        public bool KeepBackupFiles { get; set; } = false;

        [FieldDefinition(56, Label = "Extended Logging", HelpText = "Enable extended logging with detailed diagnostic information for troubleshooting", Type = FieldType.Checkbox, Section = "Diagnostics", Advanced = true)]
        public bool ExtendedLogging { get; set; } = false;
        
        [FieldDefinition(57, Label = "Enable Diagnostics", HelpText = "Save raw files for diagnostic purposes when errors occur", Type = FieldType.Checkbox, Section = "Diagnostics", Advanced = true)]
        public bool EnableDiagnostics { get; set; } = false;

        [FieldDefinition(111, Label = "Process Queue With Natural Behavior", HelpText = "When enabled, the queue will be processed to simulate a human-like download pattern.", Type = FieldType.Checkbox, Section = "Natural Behavior")]
        public bool ProcessQueueWithNaturalBehavior { get; set; } = true;

        // Item Processing Delay
        [FieldDefinition(112, Label = "Item Processing Delay", Unit = "ms", HelpText = "Delay between processing each download item. IMPACT: Lower values (0-300ms) = faster downloads but may cause search timeouts. Higher values (500-1000ms) = slower downloads but more stable operation. Set to 0 to disable.", Type = FieldType.Number, Section = "Performance Tuning", Advanced = true)]
        public int ItemProcessingDelayMs { get; set; } = 500;

        // Legacy properties - not shown in UI but used by code in other classes
        [Obsolete("Use EnableNaturalBehavior and TrackToTrackDelay* settings instead")]
        public bool DownloadDelay { get; set; } = false;

        [Obsolete("Use TrackToTrackDelayMin instead")]
        public float DownloadDelayMin 
        { 
            get => TrackToTrackDelayMin;
            set => TrackToTrackDelayMin = value;
        }

        [Obsolete("Use TrackToTrackDelayMax instead")]
        public float DownloadDelayMax
        {
            get => TrackToTrackDelayMax;
            set => TrackToTrackDelayMax = value;
        }

        [Obsolete("Connection timeout is now managed automatically")]
        public int ConnectionTimeoutSeconds { get; set; } = 60;

        // Download Priority Settings
        [FieldDefinition(60, Label = "Enable Priority System", HelpText = "Enables the download priority system for queue management.", Type = FieldType.Checkbox, Section = "Download Priorities")]
        public bool EnablePrioritySystem { get; set; } = true;
        
        [FieldDefinition(61, Label = "Default New Release Priority", HelpText = "Priority to assign to new releases added to queue.", Type = FieldType.Select, SelectOptions = typeof(NzbDrone.Core.Download.Clients.Tidal.Interfaces.DownloadItemPriority), Section = "Download Priorities")]
        public int DefaultNewReleasePriority { get; set; } = (int)NzbDrone.Core.Download.Clients.Tidal.Interfaces.DownloadItemPriority.High;
        
        [FieldDefinition(62, Label = "Default Backlog Priority", HelpText = "Priority to assign to older releases added to queue.", Type = FieldType.Select, SelectOptions = typeof(NzbDrone.Core.Download.Clients.Tidal.Interfaces.DownloadItemPriority), Section = "Download Priorities")]  
        public int DefaultBacklogPriority { get; set; } = (int)NzbDrone.Core.Download.Clients.Tidal.Interfaces.DownloadItemPriority.Normal;
        
        [FieldDefinition(63, Label = "Default Missing Track Priority", HelpText = "Priority to assign when downloading missing tracks from an album.", Type = FieldType.Select, SelectOptions = typeof(NzbDrone.Core.Download.Clients.Tidal.Interfaces.DownloadItemPriority), Section = "Download Priorities")]
        public int DefaultMissingTrackPriority { get; set; } = (int)NzbDrone.Core.Download.Clients.Tidal.Interfaces.DownloadItemPriority.Normal;

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }

        // Update profile detection methods to use the service
        public bool IsCustomizedProfile()
        {
            // If already set to custom, return true (Custom is normally 3)
            if (BehaviorProfileType == 3) // Use numeric literal for Custom profile
                return true;

            // Check if current settings match the selected profile
            EnsureBehaviorProfileService();
            return !_behaviorProfileService.MatchesProfile(this, (BehaviorProfile)BehaviorProfileType);
        }

        public bool HasBeenCustomized()
        {
            // Use a temporary settings object with the default Balanced profile
            var defaultSettings = (TidalSettings)((ICloneable)this).Clone();
            
            // Apply the Balanced profile to the cloned settings
            EnsureBehaviorProfileService();
            _behaviorProfileService.ApplyProfile(defaultSettings, (BehaviorProfile)0); // Use 0 for Balanced profile

            // Compare current settings with default balanced profile settings
            return
                // Basic behavior profile settings
                SessionDurationMinutes != defaultSettings.SessionDurationMinutes ||
                BreakDurationMinutes != defaultSettings.BreakDurationMinutes ||
                ProcessQueueWithNaturalBehavior != defaultSettings.ProcessQueueWithNaturalBehavior ||

                // Album/Artist Organization settings
                CompleteAlbums != defaultSettings.CompleteAlbums ||
                PreferArtistGrouping != defaultSettings.PreferArtistGrouping ||
                SequentialTrackOrder != defaultSettings.SequentialTrackOrder ||
                RandomizeAlbumOrder != defaultSettings.RandomizeAlbumOrder ||

                // Listening Pattern settings
                SimulateListeningPatterns != defaultSettings.SimulateListeningPatterns ||
                TrackToTrackDelayMin != defaultSettings.TrackToTrackDelayMin ||
                TrackToTrackDelayMax != defaultSettings.TrackToTrackDelayMax ||
                AlbumToAlbumDelayMin != defaultSettings.AlbumToAlbumDelayMin ||
                AlbumToAlbumDelayMax != defaultSettings.AlbumToAlbumDelayMax ||

                // Delay settings
                MinDelayBetweenTracksMs != defaultSettings.MinDelayBetweenTracksMs ||
                MaxDelayBetweenTracksMs != defaultSettings.MaxDelayBetweenTracksMs ||
                MinDelayBetweenAlbumsMs != defaultSettings.MinDelayBetweenAlbumsMs ||
                MaxDelayBetweenAlbumsMs != defaultSettings.MaxDelayBetweenAlbumsMs ||

                // Performance settings (MaxConcurrentDownloads is now basic)
                MaxDownloadsPerHour != defaultSettings.MaxDownloadsPerHour ||

                // Skip simulation settings
                SimulateSkips != defaultSettings.SimulateSkips ||
                SkipProbability != defaultSettings.SkipProbability;
        }

        /// <summary>
        /// Gets the effective country code based on the Country setting.
        /// Returns the selected country code or the custom country code if Country is set to Custom.
        /// </summary>
        /// <returns>A two-letter ISO 3166-1 alpha-2 country code.</returns>
        public string GetEffectiveCountryCode()
        {
            if ((TidalCountry)Country == TidalCountry.Custom && !string.IsNullOrEmpty(CustomCountryCode))
                return CustomCountryCode.ToUpper();

            return (TidalCountry)Country switch
            {
                TidalCountry.USA => "US",
                TidalCountry.UK => "GB",
                TidalCountry.Canada => "CA",
                TidalCountry.Australia => "AU",
                TidalCountry.Germany => "DE",
                TidalCountry.France => "FR",
                TidalCountry.Japan => "JP",
                TidalCountry.Brazil => "BR",
                TidalCountry.Mexico => "MX",
                TidalCountry.Netherlands => "NL", 
                TidalCountry.Spain => "ES",
                TidalCountry.Sweden => "SE",
                TidalCountry.Custom => "US", // Fallback if custom but no code provided
                _ => "US" // Default fallback
            };
        }

        /// <summary>
        /// Implements the Clone method from ICloneable.
        /// Creates a shallow copy of the current settings object.
        /// </summary>
        /// <returns>A new instance with the same property values.</returns>
        public object Clone()
        {
            return MemberwiseClone();
        }

        /// <summary>
        /// Validates the status file path and returns a usable path for status file storage.
        /// Uses the FileSystemService to perform validation.
        /// </summary>
        /// <returns>
        /// A valid, writable directory path where status files can be stored.
        /// </returns>
        public string ValidateStatusFilePath()
        {
            var logger = LogManager.GetCurrentClassLogger();
            return ValidateStatusFilePath(logger);
        }

        /// <summary>
        /// Validates the status file path with the specified logger.
        /// Uses the FileSystemService to perform validation.
        /// </summary>
        /// <param name="logger">Logger instance for diagnostic output</param>
        /// <returns>
        /// A valid, writable directory path where status files can be stored.
        /// </returns>
        public string ValidateStatusFilePath(Logger logger)
        {
            EnsureFileSystemService();
            StatusFilesPath = _fileSystemService.ValidatePath(StatusFilesPath, logger);
            return StatusFilesPath;
        }

        /// <summary>
        /// Tests if the status path is writable by creating a test file.
        /// </summary>
        /// <param name="logger">Logger to use for logging any issues</param>
        /// <returns>True if the test write succeeded, false otherwise</returns>
        public bool TestStatusPath(Logger logger)
        {
            try
            {
                if (!GenerateStatusFiles || string.IsNullOrWhiteSpace(StatusFilesPath))
                {
                    logger.Debug("Status file generation is disabled or path is empty, skipping test write");
                    return true; // Return success if feature is disabled
                }

                EnsureFileSystemService();
                return _fileSystemService.TestPathWritable(StatusFilesPath, logger);
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error testing write access to status files path: {StatusFilesPath}");
                return false;
            }
        }

        /// <summary>
        /// Tests if the queue persistence path is writable by creating a test file.
        /// </summary>
        /// <param name="logger">Logger to use for logging any issues</param>
        /// <returns>True if the test write succeeded, false otherwise</returns>
        public bool TestQueuePersistencePath(Logger logger)
        {
            if (string.IsNullOrWhiteSpace(ActualQueuePersistencePath))
            {
                logger.Warn("Queue persistence path is empty or null");
                return false;
            }

            EnsureFileSystemService();
            string originalPath = ActualQueuePersistencePath;
            bool success = false;

            try
            {
                // First try the specified path
                success = _fileSystemService.TestPathWritable(ActualQueuePersistencePath, logger);
                
                if (!success)
                {
                    // If that fails, try validating and getting an alternative path
                    var validatedPath = _fileSystemService.ValidatePath(ActualQueuePersistencePath, logger);
                    
                    if (validatedPath != ActualQueuePersistencePath)
                    {
                        // The service found a better path
                        QueuePersistencePath = validatedPath;
                        logger.Info($"Using alternative queue persistence path: {QueuePersistencePath} (original path {originalPath} was not writable)");
                        success = true;
                    }
                }

                if (success)
                {
                    logger.Info($"Successfully verified write access to queue persistence path: {ActualQueuePersistencePath}");
                }
                else
                {
                    logger.Warn($"Failed to verify write access to queue persistence path, and no alternatives worked");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error testing write access to queue persistence path: {ActualQueuePersistencePath}");
                return false;
            }
        }

        /// <summary>
        /// Validates the download rate settings to ensure they're within allowable ranges.
        /// </summary>
        /// <param name="logger">Logger for recording validation steps</param>
        public void ValidateDownloadRateSettings(Logger logger)
        {
            // Verify that the downloads per hour is within the allowable range if not set to unlimited (0)
            if (MaxDownloadsPerHour > 0 && MaxDownloadsPerHour > TokenBucketRateLimiter.MAX_DOWNLOADS_PER_HOUR)
            {
                logger?.Warn($"MaxDownloadsPerHour ({MaxDownloadsPerHour}) exceeds the recommended maximum ({TokenBucketRateLimiter.MAX_DOWNLOADS_PER_HOUR}).");
            }
        }

        /// <summary>
        /// Ensures that the FileSystemService is initialized.
        /// Creates a new instance if it doesn't exist.
        /// </summary>
        private void EnsureFileSystemService()
        {
            if (_fileSystemService == null)
            {
                _fileSystemService = new FileSystemService();
            }
        }

        /// <summary>
        /// Ensures that the BehaviorProfileService is initialized.
        /// Creates a new instance if it doesn't exist.
        /// </summary>
        private void EnsureBehaviorProfileService()
        {
            if (_behaviorProfileService == null)
            {
                _behaviorProfileService = new BehaviorProfileService();
            }
        }
    }

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
        LyricProviders
    }

    public static class SettingsSectionHelper
    {
        public static string GetSectionHeader(SettingsSection section)
        {
            return section switch
            {
                SettingsSection.BasicSettings => "Basic Settings",
                SettingsSection.NaturalBehavior => "Natural Behavior",
                SettingsSection.RateLimiting => "Rate Limiting",
                SettingsSection.QueueManagement => "Queue Management",
                SettingsSection.AdvancedSettings => "Advanced Settings",
                SettingsSection.LegacySettings => "Legacy Settings",
                SettingsSection.Diagnostics => "Diagnostics",
                SettingsSection.FileValidation => "File Validation",
                SettingsSection.AlbumArtistOrganization => "Album/Artist Organization",
                SettingsSection.HighVolumeHandling => "High Volume Handling",
                SettingsSection.StatusFiles => "Status Files",
                SettingsSection.CircuitBreaker => "Circuit Breaker",
                SettingsSection.PerformanceTuning => "Performance Tuning",
                SettingsSection.LyricProviders => "Lyric Providers",
                _ => string.Empty
            };
        }
    }
}












