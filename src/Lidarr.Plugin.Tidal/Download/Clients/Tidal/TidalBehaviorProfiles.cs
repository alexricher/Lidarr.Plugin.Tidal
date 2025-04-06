using System.Collections.Generic;
using System;

namespace NzbDrone.Core.Download.Clients.Tidal
{
    /// <summary>
    /// Provides predefined behavior profiles for the Tidal download client.
    /// Contains constants and methods to apply different download patterns and behaviors.
    /// Used to simulate natural listening patterns and avoid triggering anti-abuse measures.
    /// </summary>
    public static class TidalBehaviorProfiles
    {
        /// <summary>
        /// Balanced profile with moderate download rates and natural behavior.
        /// Good for general use with moderate-sized libraries.
        /// </summary>
        public static class Balanced
        {
            /// <summary>Duration of a download session in minutes before taking a break</summary>
            public const int SessionDurationMinutes = 120;
            
            /// <summary>Duration of breaks between download sessions in minutes</summary>
            public const int BreakDurationMinutes = 60;
            
            /// <summary>Minimum delay between track downloads in seconds</summary>
            public const float TrackToTrackDelayMin = 1.0f;
            
            /// <summary>Maximum delay between track downloads in seconds</summary>
            public const float TrackToTrackDelayMax = 10.0f;
            
            /// <summary>Minimum delay between album downloads in seconds</summary>
            public const float AlbumToAlbumDelayMin = 30.0f;
            
            /// <summary>Maximum delay between album downloads in seconds</summary>
            public const float AlbumToAlbumDelayMax = 180.0f;
            
            /// <summary>Whether to simulate natural listening patterns</summary>
            public const bool SimulateListeningPatterns = true;
            
            /// <summary>Whether to complete full albums before moving to the next</summary>
            public const bool CompleteAlbums = true;
            
            /// <summary>Whether to group downloads by artist</summary>
            public const bool PreferArtistGrouping = true;
            
            /// <summary>Whether to download tracks in sequential order</summary>
            public const bool SequentialTrackOrder = true;
            
            // Additional settings for comparison
            /// <summary>Maximum number of concurrent track downloads</summary>
            public const int MaxConcurrentDownloads = 3;
            
            /// <summary>Maximum number of downloads per hour</summary>
            public const int MaxDownloadsPerHour = 30;
            
            /// <summary>Whether to simulate delays between downloads</summary>
            public const bool SimulateDelays = true;
            
            /// <summary>Minimum delay between track downloads in milliseconds</summary>
            public const int MinDelayBetweenTracksMs = 2000;
            
            /// <summary>Maximum delay between track downloads in milliseconds</summary>
            public const int MaxDelayBetweenTracksMs = 5000;
            
            /// <summary>Minimum delay between album downloads in milliseconds</summary>
            public const int MinDelayBetweenAlbumsMs = 10000;
            
            /// <summary>Maximum delay between album downloads in milliseconds</summary>
            public const int MaxDelayBetweenAlbumsMs = 30000;
            
            /// <summary>Whether to randomize album download order</summary>
            public const bool RandomizeAlbumOrder = false;
            
            /// <summary>Whether to simulate track skips</summary>
            public const bool SimulateSkips = false;
            
            /// <summary>Probability of skipping a track (0.0-1.0)</summary>
            public const float SkipProbability = 0.05f;
        }

        /// <summary>
        /// Casual listener profile with lower download rates and longer breaks.
        /// Recommended for smaller libraries or when minimal impact is desired.
        /// </summary>
        public static class CasualListener
        {
            /// <summary>Duration of a download session in minutes before taking a break</summary>
            public const int SessionDurationMinutes = 60;
            
            /// <summary>Duration of breaks between download sessions in minutes</summary>
            public const int BreakDurationMinutes = 120;
            
            /// <summary>Minimum delay between track downloads in seconds</summary>
            public const float TrackToTrackDelayMin = 5.0f;
            
            /// <summary>Maximum delay between track downloads in seconds</summary>
            public const float TrackToTrackDelayMax = 30.0f;
            
            /// <summary>Minimum delay between album downloads in seconds</summary>
            public const float AlbumToAlbumDelayMin = 60.0f;
            
            /// <summary>Maximum delay between album downloads in seconds</summary>
            public const float AlbumToAlbumDelayMax = 300.0f;
            
            /// <summary>Whether to simulate natural listening patterns</summary>
            public const bool SimulateListeningPatterns = true;
            
            /// <summary>Whether to complete full albums before moving to the next</summary>
            public const bool CompleteAlbums = false;
            
            /// <summary>Whether to group downloads by artist</summary>
            public const bool PreferArtistGrouping = false;
            
            /// <summary>Whether to download tracks in sequential order</summary>
            public const bool SequentialTrackOrder = false;
            
            // Additional settings for comparison
            /// <summary>Maximum number of concurrent track downloads</summary>
            public const int MaxConcurrentDownloads = 2;
            
            /// <summary>Maximum number of downloads per hour</summary>
            public const int MaxDownloadsPerHour = 15;
            
            /// <summary>Whether to simulate delays between downloads</summary>
            public const bool SimulateDelays = true;
            
            /// <summary>Minimum delay between track downloads in milliseconds</summary>
            public const int MinDelayBetweenTracksMs = 5000;
            
            /// <summary>Maximum delay between track downloads in milliseconds</summary>
            public const int MaxDelayBetweenTracksMs = 15000;
            
            /// <summary>Minimum delay between album downloads in milliseconds</summary>
            public const int MinDelayBetweenAlbumsMs = 20000;
            
            /// <summary>Maximum delay between album downloads in milliseconds</summary>
            public const int MaxDelayBetweenAlbumsMs = 60000;
            
            /// <summary>Whether to randomize album download order</summary>
            public const bool RandomizeAlbumOrder = true;
            
            /// <summary>Whether to simulate track skips</summary>
            public const bool SimulateSkips = true;
            
            /// <summary>Probability of skipping a track (0.0-1.0)</summary>
            public const float SkipProbability = 0.15f;
        }

        /// <summary>
        /// Music enthusiast profile with higher download rates.
        /// Optimized for larger libraries when faster downloads are needed.
        /// </summary>
        public static class MusicEnthusiast
        {
            /// <summary>Duration of a download session in minutes before taking a break</summary>
            public const int SessionDurationMinutes = 180;
            
            /// <summary>Duration of breaks between download sessions in minutes</summary>
            public const int BreakDurationMinutes = 30;
            
            /// <summary>Minimum delay between track downloads in seconds</summary>
            public const float TrackToTrackDelayMin = 0.5f;
            
            /// <summary>Maximum delay between track downloads in seconds</summary>
            public const float TrackToTrackDelayMax = 5.0f;
            
            /// <summary>Minimum delay between album downloads in seconds</summary>
            public const float AlbumToAlbumDelayMin = 15.0f;
            
            /// <summary>Maximum delay between album downloads in seconds</summary>
            public const float AlbumToAlbumDelayMax = 90.0f;
            
            /// <summary>Whether to simulate natural listening patterns</summary>
            public const bool SimulateListeningPatterns = true;
            
            /// <summary>Whether to complete full albums before moving to the next</summary>
            public const bool CompleteAlbums = true;
            
            /// <summary>Whether to group downloads by artist</summary>
            public const bool PreferArtistGrouping = true;
            
            /// <summary>Whether to download tracks in sequential order</summary>
            public const bool SequentialTrackOrder = true;
            
            // Additional settings for comparison
            /// <summary>Maximum number of concurrent track downloads</summary>
            public const int MaxConcurrentDownloads = 5;
            
            /// <summary>Maximum number of downloads per hour</summary>
            public const int MaxDownloadsPerHour = 60;
            
            /// <summary>Whether to simulate delays between downloads</summary>
            public const bool SimulateDelays = true;
            
            /// <summary>Minimum delay between track downloads in milliseconds</summary>
            public const int MinDelayBetweenTracksMs = 1000;
            
            /// <summary>Maximum delay between track downloads in milliseconds</summary>
            public const int MaxDelayBetweenTracksMs = 3000;
            
            /// <summary>Minimum delay between album downloads in milliseconds</summary>
            public const int MinDelayBetweenAlbumsMs = 5000;
            
            /// <summary>Maximum delay between album downloads in milliseconds</summary>
            public const int MaxDelayBetweenAlbumsMs = 15000;
            
            /// <summary>Whether to randomize album download order</summary>
            public const bool RandomizeAlbumOrder = false;
            
            /// <summary>Whether to simulate track skips</summary>
            public const bool SimulateSkips = false;
            
            /// <summary>Probability of skipping a track (0.0-1.0)</summary>
            public const float SkipProbability = 0.02f;
        }

        /// <summary>
        /// Applies a predefined behavior profile to the provided settings object.
        /// Updates all relevant settings properties to match the selected profile.
        /// </summary>
        /// <param name="settings">The TidalSettings object to update</param>
        /// <param name="profile">The behavior profile to apply</param>
        public static void ApplyProfile(TidalSettings settings, BehaviorProfile profile)
        {
            // Check for null settings
            if (settings == null)
            {
                return;
            }
            
            switch (profile)
            {
                case BehaviorProfile.Balanced:
                    settings.SessionDurationMinutes = Balanced.SessionDurationMinutes;
                    settings.BreakDurationMinutes = Balanced.BreakDurationMinutes;
                    settings.TrackToTrackDelayMin = Balanced.TrackToTrackDelayMin;
                    settings.TrackToTrackDelayMax = Balanced.TrackToTrackDelayMax;
                    settings.AlbumToAlbumDelayMin = Balanced.AlbumToAlbumDelayMin;
                    settings.AlbumToAlbumDelayMax = Balanced.AlbumToAlbumDelayMax;
                    settings.SimulateListeningPatterns = Balanced.SimulateListeningPatterns;
                    settings.CompleteAlbums = Balanced.CompleteAlbums;
                    settings.PreferArtistGrouping = Balanced.PreferArtistGrouping;
                    settings.SequentialTrackOrder = Balanced.SequentialTrackOrder;
                    
                    // Set additional properties
                    settings.MaxConcurrentTrackDownloads = Balanced.MaxConcurrentDownloads;
                    settings.MaxDownloadsPerHour = Balanced.MaxDownloadsPerHour;
                    settings.SimulateDelays = Balanced.SimulateDelays;
                    settings.MinDelayBetweenTracksMs = Balanced.MinDelayBetweenTracksMs;
                    settings.MaxDelayBetweenTracksMs = Balanced.MaxDelayBetweenTracksMs;
                    settings.MinDelayBetweenAlbumsMs = Balanced.MinDelayBetweenAlbumsMs;
                    settings.MaxDelayBetweenAlbumsMs = Balanced.MaxDelayBetweenAlbumsMs;
                    settings.RandomizeAlbumOrder = Balanced.RandomizeAlbumOrder;
                    settings.SimulateSkips = Balanced.SimulateSkips;
                    settings.SkipProbability = Balanced.SkipProbability;
                    break;
                    
                case BehaviorProfile.CasualListener:
                    settings.SessionDurationMinutes = CasualListener.SessionDurationMinutes;
                    settings.BreakDurationMinutes = CasualListener.BreakDurationMinutes;
                    settings.TrackToTrackDelayMin = CasualListener.TrackToTrackDelayMin;
                    settings.TrackToTrackDelayMax = CasualListener.TrackToTrackDelayMax;
                    settings.AlbumToAlbumDelayMin = CasualListener.AlbumToAlbumDelayMin;
                    settings.AlbumToAlbumDelayMax = CasualListener.AlbumToAlbumDelayMax;
                    settings.SimulateListeningPatterns = CasualListener.SimulateListeningPatterns;
                    settings.CompleteAlbums = CasualListener.CompleteAlbums;
                    settings.PreferArtistGrouping = CasualListener.PreferArtistGrouping;
                    settings.SequentialTrackOrder = CasualListener.SequentialTrackOrder;
                    
                    // Set additional properties
                    settings.MaxConcurrentTrackDownloads = CasualListener.MaxConcurrentDownloads;
                    settings.MaxDownloadsPerHour = CasualListener.MaxDownloadsPerHour;
                    settings.SimulateDelays = CasualListener.SimulateDelays;
                    settings.MinDelayBetweenTracksMs = CasualListener.MinDelayBetweenTracksMs;
                    settings.MaxDelayBetweenTracksMs = CasualListener.MaxDelayBetweenTracksMs;
                    settings.MinDelayBetweenAlbumsMs = CasualListener.MinDelayBetweenAlbumsMs;
                    settings.MaxDelayBetweenAlbumsMs = CasualListener.MaxDelayBetweenAlbumsMs;
                    settings.RandomizeAlbumOrder = CasualListener.RandomizeAlbumOrder;
                    settings.SimulateSkips = CasualListener.SimulateSkips;
                    settings.SkipProbability = CasualListener.SkipProbability;
                    break;
                    
                case BehaviorProfile.MusicEnthusiast:
                    settings.SessionDurationMinutes = MusicEnthusiast.SessionDurationMinutes;
                    settings.BreakDurationMinutes = MusicEnthusiast.BreakDurationMinutes;
                    settings.TrackToTrackDelayMin = MusicEnthusiast.TrackToTrackDelayMin;
                    settings.TrackToTrackDelayMax = MusicEnthusiast.TrackToTrackDelayMax;
                    settings.AlbumToAlbumDelayMin = MusicEnthusiast.AlbumToAlbumDelayMin;
                    settings.AlbumToAlbumDelayMax = MusicEnthusiast.AlbumToAlbumDelayMax;
                    settings.SimulateListeningPatterns = MusicEnthusiast.SimulateListeningPatterns;
                    settings.CompleteAlbums = MusicEnthusiast.CompleteAlbums;
                    settings.PreferArtistGrouping = MusicEnthusiast.PreferArtistGrouping;
                    settings.SequentialTrackOrder = MusicEnthusiast.SequentialTrackOrder;
                    
                    // Set additional properties
                    settings.MaxConcurrentTrackDownloads = MusicEnthusiast.MaxConcurrentDownloads;
                    settings.MaxDownloadsPerHour = MusicEnthusiast.MaxDownloadsPerHour;
                    settings.SimulateDelays = MusicEnthusiast.SimulateDelays;
                    settings.MinDelayBetweenTracksMs = MusicEnthusiast.MinDelayBetweenTracksMs;
                    settings.MaxDelayBetweenTracksMs = MusicEnthusiast.MaxDelayBetweenTracksMs;
                    settings.MinDelayBetweenAlbumsMs = MusicEnthusiast.MinDelayBetweenAlbumsMs;
                    settings.MaxDelayBetweenAlbumsMs = MusicEnthusiast.MaxDelayBetweenAlbumsMs;
                    settings.RandomizeAlbumOrder = MusicEnthusiast.RandomizeAlbumOrder;
                    settings.SimulateSkips = MusicEnthusiast.SimulateSkips;
                    settings.SkipProbability = MusicEnthusiast.SkipProbability;
                    break;
            }
        }

        /// <summary>
        /// Checks if the provided settings match a specific behavior profile.
        /// Compares all behavior-related settings against the profile's default values.
        /// </summary>
        /// <param name="settings">The TidalSettings object to check</param>
        /// <param name="profile">The behavior profile to compare against</param>
        /// <returns>True if settings match the profile, false otherwise</returns>
        public static bool MatchesProfile(TidalSettings settings, BehaviorProfile profile)
        {
            // Check for null settings
            if (settings == null)
            {
                return false;
            }
            
            var tempSettings = (TidalSettings)((ICloneable)settings).Clone();
            ApplyProfile(tempSettings, profile);
            
            return 
                // Basic behavior profile settings
                settings.SessionDurationMinutes == tempSettings.SessionDurationMinutes &&
                settings.BreakDurationMinutes == tempSettings.BreakDurationMinutes &&
                
                // Album/Artist Organization settings
                settings.CompleteAlbums == tempSettings.CompleteAlbums &&
                settings.PreferArtistGrouping == tempSettings.PreferArtistGrouping &&
                settings.SequentialTrackOrder == tempSettings.SequentialTrackOrder &&
                settings.RandomizeAlbumOrder == tempSettings.RandomizeAlbumOrder &&
                
                // Listening Pattern settings
                settings.SimulateListeningPatterns == tempSettings.SimulateListeningPatterns &&
                settings.TrackToTrackDelayMin == tempSettings.TrackToTrackDelayMin &&
                settings.TrackToTrackDelayMax == tempSettings.TrackToTrackDelayMax &&
                settings.AlbumToAlbumDelayMin == tempSettings.AlbumToAlbumDelayMin &&
                settings.AlbumToAlbumDelayMax == tempSettings.AlbumToAlbumDelayMax &&
                
                // Delay settings
                settings.SimulateDelays == tempSettings.SimulateDelays &&
                settings.MinDelayBetweenTracksMs == tempSettings.MinDelayBetweenTracksMs &&
                settings.MaxDelayBetweenTracksMs == tempSettings.MaxDelayBetweenTracksMs &&
                settings.MinDelayBetweenAlbumsMs == tempSettings.MinDelayBetweenAlbumsMs &&
                settings.MaxDelayBetweenAlbumsMs == tempSettings.MaxDelayBetweenAlbumsMs &&
                
                // Performance settings
                settings.MaxConcurrentTrackDownloads == tempSettings.MaxConcurrentTrackDownloads &&
                settings.MaxDownloadsPerHour == tempSettings.MaxDownloadsPerHour &&
                
                // Skip simulation settings
                settings.SimulateSkips == tempSettings.SimulateSkips &&
                settings.SkipProbability == tempSettings.SkipProbability;
        }
    }
}