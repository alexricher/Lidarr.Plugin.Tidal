using FluentValidation;
using NLog;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.Common.Instrumentation;
using System;
using System.IO;
using System.Text.Json;

namespace NzbDrone.Core.Download.Clients.Tidal
{
    public class TidalSettingsValidator : AbstractValidator<TidalSettings>
    {
        public TidalSettingsValidator()
        {
            RuleFor(x => x.DownloadPath).IsValidPath();
            
            // Natural behavior validation rules
            RuleFor(x => x.SessionDurationMinutes)
                .GreaterThan(0)
                .When(x => x.EnableNaturalBehavior)
                .WithMessage("Session duration must be greater than 0 minutes");
                
            RuleFor(x => x.BreakDurationMinutes)
                .GreaterThan(0)
                .When(x => x.EnableNaturalBehavior)
                .WithMessage("Break duration must be greater than 0 minutes");
                
            RuleFor(x => x.DownloadDelayMin)
                .GreaterThan(0)
                .When(x => x.EnableNaturalBehavior || x.DownloadDelay)
                .WithMessage("Minimum delay must be greater than 0 seconds");
                
            RuleFor(x => x.DownloadDelayMax)
                .GreaterThanOrEqualTo(x => x.DownloadDelayMin)
                .When(x => x.EnableNaturalBehavior || x.DownloadDelay)
                .WithMessage("Maximum delay must be greater than or equal to minimum delay");
                
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
        }
    }

    public enum BehaviorProfile
    {
        Balanced = 0,
        CasualListener = 1,
        MusicEnthusiast = 2,
        Custom = 3,
        Automatic = 4
    }

    public class TidalSettings : IProviderConfig, ICloneable
    {
        private static readonly TidalSettingsValidator Validator = new TidalSettingsValidator();

        // Basic Settings
        [FieldDefinition(0, Label = "Download Path", Type = FieldType.Textbox, Section = "Basic Settings")]
        public string DownloadPath { get; set; } = "";

        [FieldDefinition(1, Label = "Extract FLAC From M4A", HelpText = "Extracts FLAC data from the Tidal-provided M4A files.", HelpTextWarning = "This requires FFMPEG and FFProbe to be available to Lidarr.", Type = FieldType.Checkbox, Section = "Basic Settings")]
        public bool ExtractFlac { get; set; } = false;

        [FieldDefinition(2, Label = "Status Files Path", HelpText = "Path where download status files will be stored. Must be writable by Lidarr.", Type = FieldType.Textbox, Section = "Basic Settings")]
        public string StatusFilesPath { get; set; } = "";

        [FieldDefinition(3, Label = "Re-encode AAC into MP3", HelpText = "Re-encodes AAC data from the Tidal-provided M4A files into MP3s.", HelpTextWarning = "This requires FFMPEG and FFProbe to be available to Lidarr.", Type = FieldType.Checkbox, Section = "Basic Settings")]
        public bool ReEncodeAAC { get; set; } = false;

        [FieldDefinition(4, Label = "Save Synced Lyrics", HelpText = "Saves synced lyrics to a separate .lrc file if available. Requires .lrc to be allowed under Import Extra Files.", Type = FieldType.Checkbox, Section = "Basic Settings")]
        public bool SaveSyncedLyrics { get; set; } = false;

        [FieldDefinition(5, Label = "Use LRCLIB as Backup Lyric Provider", HelpText = "If Tidal does not have plain or synced lyrics for a track, the plugin will attempt to get them from LRCLIB.", Type = FieldType.Checkbox, Section = "Basic Settings")]
        public bool UseLRCLIB { get; set; } = false;

        // Natural Behavior Settings
        [FieldDefinition(6, Label = "Enable Natural Behavior", HelpText = "Simulates human-like download patterns to help avoid detection systems.", Type = FieldType.Checkbox, Section = "Natural Behavior")]
        public bool EnableNaturalBehavior { get; set; } = false;

        [FieldDefinition(7, Label = "Behavior Profile", HelpText = "Select a predefined behavior profile or choose custom to configure all settings manually. WARNING: Changing from Custom to a predefined profile will immediately overwrite all your custom settings!", Type = FieldType.Select, SelectOptions = typeof(BehaviorProfile), Section = "Natural Behavior")]
        public int BehaviorProfileType { get; set; } = (int)BehaviorProfile.Balanced;

        [FieldDefinition(8, Label = "Session Duration", HelpText = "How long to continuously download before taking a break (in minutes).", Type = FieldType.Number, Section = "Natural Behavior")]
        public int SessionDurationMinutes { get; set; } = TidalBehaviorProfiles.Balanced.SessionDurationMinutes;

        [FieldDefinition(9, Label = "Break Duration", HelpText = "How long to pause between download sessions (in minutes).", Type = FieldType.Number, Section = "Natural Behavior")]
        public int BreakDurationMinutes { get; set; } = TidalBehaviorProfiles.Balanced.BreakDurationMinutes;

        // Album/Artist Organization
        [FieldDefinition(10, Label = "Complete Albums", HelpText = "Complete all tracks from the same album before moving to another album.", Type = FieldType.Checkbox, Section = "Album/Artist Organization")]
        public bool CompleteAlbums { get; set; } = TidalBehaviorProfiles.Balanced.CompleteAlbums;

        [FieldDefinition(11, Label = "Preserve Artist Context", HelpText = "After completing an album, prefer the next album from the same artist.", Type = FieldType.Checkbox, Section = "Album/Artist Organization")]
        public bool PreferArtistGrouping { get; set; } = TidalBehaviorProfiles.Balanced.PreferArtistGrouping;

        [FieldDefinition(12, Label = "Sequential Track Order", HelpText = "Download tracks within an album in sequential order rather than randomly.", Type = FieldType.Checkbox, Section = "Album/Artist Organization")]
        public bool SequentialTrackOrder { get; set; } = TidalBehaviorProfiles.Balanced.SequentialTrackOrder;

        // Listening Pattern Simulation
        [FieldDefinition(13, Label = "Simulate Listening Patterns", HelpText = "Add realistic delays between tracks and albums to simulate actual listening behavior.", Type = FieldType.Checkbox, Section = "Listening Pattern Simulation")]
        public bool SimulateListeningPatterns { get; set; } = TidalBehaviorProfiles.Balanced.SimulateListeningPatterns;

        [FieldDefinition(15, Label = "Track-to-Track Delay Min", HelpText = "Minimum delay between tracks in the same album (seconds).", Type = FieldType.Number, Section = "Listening Pattern Simulation")]
        public float TrackToTrackDelayMin { get; set; } = TidalBehaviorProfiles.Balanced.TrackToTrackDelayMin;

        [FieldDefinition(16, Label = "Track-to-Track Delay Max", HelpText = "Maximum delay between tracks in the same album (seconds).", Type = FieldType.Number, Section = "Listening Pattern Simulation")]
        public float TrackToTrackDelayMax { get; set; } = TidalBehaviorProfiles.Balanced.TrackToTrackDelayMax;

        [FieldDefinition(17, Label = "Album-to-Album Delay Min", HelpText = "Minimum delay between different albums (seconds).", Type = FieldType.Number, Section = "Listening Pattern Simulation")]
        public float AlbumToAlbumDelayMin { get; set; } = TidalBehaviorProfiles.Balanced.AlbumToAlbumDelayMin;

        [FieldDefinition(18, Label = "Album-to-Album Delay Max", HelpText = "Maximum delay between different albums (seconds).", Type = FieldType.Number, Section = "Listening Pattern Simulation")]
        public float AlbumToAlbumDelayMax { get; set; } = TidalBehaviorProfiles.Balanced.AlbumToAlbumDelayMax;

        // Time-of-day adaptation
        [FieldDefinition(21, Label = "Time-of-Day Adaptation", HelpText = "Adjust download activity based on time of day to appear more natural.", Type = FieldType.Checkbox, Section = "Time-of-Day Adaptation")]
        public bool TimeOfDayAdaptation { get; set; } = false;

        [FieldDefinition(22, Label = "Active Hours Start", HelpText = "Hour when active hours begin (24-hour format).", Type = FieldType.Number, Section = "Time-of-Day Adaptation")]
        public int ActiveHoursStart { get; set; } = 8;

        [FieldDefinition(23, Label = "Active Hours End", HelpText = "Hour when active hours end (24-hour format).", Type = FieldType.Number, Section = "Time-of-Day Adaptation")]
        public int ActiveHoursEnd { get; set; } = 22;

        // Traffic Pattern Obfuscation
        [FieldDefinition(24, Label = "Rotate User-Agent", HelpText = "Occasionally change the user-agent between sessions to appear more natural.", Type = FieldType.Checkbox, Advanced = true)]
        public bool RotateUserAgent { get; set; } = false;

        [FieldDefinition(25, Label = "Vary Connection Parameters", HelpText = "Vary connection parameters between sessions to avoid fingerprinting.", Type = FieldType.Checkbox, Advanced = true)]
        public bool VaryConnectionParameters { get; set; } = false;

        // High Volume Download Settings
        [FieldDefinition(26, Label = "Enable High Volume Handling", HelpText = "Enables special handling for very large download queues to avoid rate limiting.", Type = FieldType.Checkbox, Section = "High Volume Handling")]
        public bool EnableHighVolumeHandling { get; set; } = true;

        [FieldDefinition(27, Label = "High Volume Threshold", HelpText = "Number of items in queue to trigger high volume mode.", Type = FieldType.Number, Section = "High Volume Handling")]
        public int HighVolumeThreshold { get; set; } = 1000;

        [FieldDefinition(28, Label = "High Volume Session Minutes", HelpText = "Session duration for high volume mode (minutes).", Type = FieldType.Number, Section = "High Volume Handling")]
        public int HighVolumeSessionMinutes { get; set; } = 60;

        [FieldDefinition(29, Label = "High Volume Break Minutes", HelpText = "Break duration for high volume mode (minutes).", Type = FieldType.Number, Section = "High Volume Handling")]
        public int HighVolumeBreakMinutes { get; set; } = 30;

        // Legacy Settings
        [FieldDefinition(30, Label = "Download Delay", HelpText = "Legacy option: When downloading many tracks, Tidal may rate-limit you. This will add a delay between track downloads to help prevent this.", Type = FieldType.Checkbox, Section = "Legacy Settings", Advanced = true)]
        public bool DownloadDelay { get; set; } = false;

        [FieldDefinition(31, Label = "Download Delay Minimum", HelpText = "Minimum download delay, in seconds.", Type = FieldType.Number, Section = "Legacy Settings", Advanced = true)]
        public float DownloadDelayMin { get; set; } = 3.0f;

        [FieldDefinition(32, Label = "Download Delay Maximum", HelpText = "Maximum download delay, in seconds.", Type = FieldType.Number, Section = "Legacy Settings", Advanced = true)]
        public float DownloadDelayMax { get; set; } = 5.0f;

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }

        // Add this method to detect if settings have been customized
        public bool IsCustomizedProfile()
        {
            // If already set to custom, return true
            if (BehaviorProfileType == (int)BehaviorProfile.Custom)
                return true;
            
            // Check if current settings match the selected profile
            return !TidalBehaviorProfiles.MatchesProfile(this, (BehaviorProfile)BehaviorProfileType);
        }

        // Implement Clone method from ICloneable
        public object Clone()
        {
            return MemberwiseClone();
        }

        // Add required behavior-related properties referenced in the code
        public bool RandomizeAlbumOrder { get; set; } = TidalBehaviorProfiles.Balanced.RandomizeAlbumOrder;
        public bool SimulateSkips { get; set; } = TidalBehaviorProfiles.Balanced.SimulateSkips;
        public float SkipProbability { get; set; } = TidalBehaviorProfiles.Balanced.SkipProbability;

        // Add these missing properties
        public int MaxConcurrentDownloads { get; set; } = TidalBehaviorProfiles.Balanced.MaxConcurrentDownloads;
        public int MaxDownloadsPerHour { get; set; } = TidalBehaviorProfiles.Balanced.MaxDownloadsPerHour;
        public bool SimulateDelays { get; set; } = TidalBehaviorProfiles.Balanced.SimulateDelays;
        public int MinDelayBetweenTracksMs { get; set; } = TidalBehaviorProfiles.Balanced.MinDelayBetweenTracksMs;
        public int MaxDelayBetweenTracksMs { get; set; } = TidalBehaviorProfiles.Balanced.MaxDelayBetweenTracksMs;
        public int MinDelayBetweenAlbumsMs { get; set; } = TidalBehaviorProfiles.Balanced.MinDelayBetweenAlbumsMs;
        public int MaxDelayBetweenAlbumsMs { get; set; } = TidalBehaviorProfiles.Balanced.MaxDelayBetweenAlbumsMs;

        // Helper for customization check
        public bool HasBeenCustomized()
        {
            // Use a temporary settings object with the default Balanced profile
            var defaultSettings = (TidalSettings)this.Clone();
            TidalBehaviorProfiles.ApplyProfile(defaultSettings, BehaviorProfile.Balanced);
            
            // Compare current settings with default balanced profile settings
            return 
                // Basic behavior profile settings
                SessionDurationMinutes != defaultSettings.SessionDurationMinutes ||
                BreakDurationMinutes != defaultSettings.BreakDurationMinutes ||
                
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
                SimulateDelays != defaultSettings.SimulateDelays ||
                MinDelayBetweenTracksMs != defaultSettings.MinDelayBetweenTracksMs ||
                MaxDelayBetweenTracksMs != defaultSettings.MaxDelayBetweenTracksMs ||
                MinDelayBetweenAlbumsMs != defaultSettings.MinDelayBetweenAlbumsMs ||
                MaxDelayBetweenAlbumsMs != defaultSettings.MaxDelayBetweenAlbumsMs ||
                
                // Performance settings
                MaxConcurrentDownloads != defaultSettings.MaxConcurrentDownloads ||
                MaxDownloadsPerHour != defaultSettings.MaxDownloadsPerHour ||
                
                // Skip simulation settings
                SimulateSkips != defaultSettings.SimulateSkips ||
                SkipProbability != defaultSettings.SkipProbability;
        }

        public void ValidateStatusFilePath(Logger logger)
        {
            // Use a mutex for this operation to ensure thread safety with a timeout
            bool mutexAcquired = false;
            var mutexName = "Global\\TidalPluginStatusPathValidation";
            using (var mutex = new System.Threading.Mutex(false, mutexName))
            {
                try
                {
                    // Try to acquire the mutex with a 5-second timeout
                    mutexAcquired = mutex.WaitOne(5000);
                    
                    if (!mutexAcquired)
                    {
                        logger.Warn("Another thread is already validating the status path. Skipping validation to avoid conflicts.");
                        return;
                    }
                    
                    // Proceed with validation with the lock acquired
                    ValidateStatusFilePathInternal(logger);
                }
                catch (System.Threading.AbandonedMutexException)
                {
                    // Handle the case where the mutex was abandoned (thread terminated without releasing)
                    logger.Warn("Mutex was abandoned. Taking ownership and proceeding with validation.");
                    ValidateStatusFilePathInternal(logger);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error acquiring mutex for status path validation");
                    // Still try to validate, but log the error
                    ValidateStatusFilePathInternal(logger);
                }
                finally
                {
                    // Release the mutex if we acquired it
                    if (mutexAcquired)
                    {
                        mutex.ReleaseMutex();
                    }
                }
            }
        }

        // Internal validation logic, called within the mutex lock
        private void ValidateStatusFilePathInternal(Logger logger)
        {
            // Temporary file path variables for later cleanup
            string testJsonFile = null;
            string writeTestFile = null;

            try
            {
                // Skip validation if path is invalid
                if (StatusFilesPath == null)
                {
                    logger.Warn("Status files path is null. Cannot validate or create directory.");
                    return;
                }

                logger.Debug($"Validating status files path: {StatusFilesPath}");

                if (string.IsNullOrWhiteSpace(StatusFilesPath))
                {
                    logger.Warn("Status files path is not configured. Status files will not be created.");
                    return;
                }

                // Validate that the path is valid for the file system
                try
                {
                    var fullPath = Path.GetFullPath(StatusFilesPath);
                    if (fullPath != StatusFilesPath)
                    {
                        logger.Debug($"Normalized status path from '{StatusFilesPath}' to '{fullPath}'");
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Status files path is invalid: {StatusFilesPath}");
                    throw new ValidationException($"Status files path is invalid: {ex.Message}");
                }

                // Check if the path exists
                if (!Directory.Exists(StatusFilesPath))
                {
                    logger.Info($"Status directory does not exist, creating: {StatusFilesPath}");
                    
                    try
                    {
                        Directory.CreateDirectory(StatusFilesPath);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"Failed to create status directory: {StatusFilesPath}");
                        throw new ValidationException($"Failed to create status directory: {ex.Message}");
                    }

                    // Test JSON file creation to ensure write permissions
                    testJsonFile = Path.Combine(StatusFilesPath, "status_test.json");
                    var testData = new {
                        timestamp = DateTime.Now.ToString("o"),
                        test = true,
                        message = "This is a test file to verify that JSON can be written to the status files directory"
                    };
                    
                    try
                    {
                        var json = System.Text.Json.JsonSerializer.Serialize(testData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(testJsonFile, json);
                        logger.Debug($"Created test JSON file: {testJsonFile}");
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"Failed to write test JSON file: {testJsonFile}");
                        throw new ValidationException($"Cannot write to status directory: {ex.Message}");
                    }
                }
                else
                {
                    // Check if directory has write permissions by creating a temporary file
                    Guid uniqueId = Guid.NewGuid();
                    writeTestFile = Path.Combine(StatusFilesPath, $"write_test_{uniqueId}.tmp");

                    try
                    {
                        File.WriteAllText(writeTestFile, "Write permission test");
                        logger.Debug($"Directory exists and has write permissions: {StatusFilesPath}");
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"Status directory exists but is not writable: {StatusFilesPath}");
                        throw new ValidationException($"Cannot write to status directory: {ex.Message}");
                    }
                }
            }
            catch (ValidationException)
            {
                // Pass through validation exceptions
                throw;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Failed to validate status files path: {StatusFilesPath}");
                throw new ValidationException($"Failed to validate status files path: {ex.Message}");
            }
            finally
            {
                // Clean up temporary test files
                try
                {
                    if (testJsonFile != null && File.Exists(testJsonFile))
                    {
                        File.Delete(testJsonFile);
                        logger.Debug($"Deleted temporary test JSON file: {testJsonFile}");
                    }

                    if (writeTestFile != null && File.Exists(writeTestFile))
                    {
                        File.Delete(writeTestFile);
                        logger.Debug($"Deleted temporary write test file: {writeTestFile}");
                    }
                }
                catch (Exception ex)
                {
                    logger.Debug($"Failed to delete temporary test files (non-critical): {ex.Message}");
                }
            }
        }
    }
}