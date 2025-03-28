using System.Collections.Generic;

namespace NzbDrone.Core.Download.Clients.Tidal
{
    public static class TidalBehaviorProfiles
    {
        public static class Balanced
        {
            public const int SessionDurationMinutes = 120;
            public const int BreakDurationMinutes = 60;
            public const float TrackToTrackDelayMin = 1.0f;
            public const float TrackToTrackDelayMax = 10.0f;
            public const float AlbumToAlbumDelayMin = 30.0f;
            public const float AlbumToAlbumDelayMax = 180.0f;
            public const bool SimulateListeningPatterns = true;
            public const bool CompleteAlbums = true;
            public const bool PreferArtistGrouping = true;
            public const bool SequentialTrackOrder = true;
            
            // Additional settings for comparison
            public const int MaxConcurrentDownloads = 3;
            public const int MaxDownloadsPerHour = 30;
            public const bool SimulateDelays = true;
            public const int MinDelayBetweenTracksMs = 2000;
            public const int MaxDelayBetweenTracksMs = 5000;
            public const int MinDelayBetweenAlbumsMs = 10000;
            public const int MaxDelayBetweenAlbumsMs = 30000;
            public const bool RandomizeAlbumOrder = false;
            public const bool SimulateSkips = false;
            public const float SkipProbability = 0.05f;
        }

        public static class CasualListener
        {
            public const int SessionDurationMinutes = 60;
            public const int BreakDurationMinutes = 120;
            public const float TrackToTrackDelayMin = 5.0f;
            public const float TrackToTrackDelayMax = 30.0f;
            public const float AlbumToAlbumDelayMin = 60.0f;
            public const float AlbumToAlbumDelayMax = 300.0f;
            public const bool SimulateListeningPatterns = true;
            public const bool CompleteAlbums = false;
            public const bool PreferArtistGrouping = false;
            public const bool SequentialTrackOrder = false;
            
            // Additional settings for comparison
            public const int MaxConcurrentDownloads = 2;
            public const int MaxDownloadsPerHour = 15;
            public const bool SimulateDelays = true;
            public const int MinDelayBetweenTracksMs = 5000;
            public const int MaxDelayBetweenTracksMs = 15000;
            public const int MinDelayBetweenAlbumsMs = 20000;
            public const int MaxDelayBetweenAlbumsMs = 60000;
            public const bool RandomizeAlbumOrder = true;
            public const bool SimulateSkips = true;
            public const float SkipProbability = 0.15f;
        }

        public static class MusicEnthusiast
        {
            public const int SessionDurationMinutes = 180;
            public const int BreakDurationMinutes = 30;
            public const float TrackToTrackDelayMin = 0.5f;
            public const float TrackToTrackDelayMax = 5.0f;
            public const float AlbumToAlbumDelayMin = 15.0f;
            public const float AlbumToAlbumDelayMax = 90.0f;
            public const bool SimulateListeningPatterns = true;
            public const bool CompleteAlbums = true;
            public const bool PreferArtistGrouping = true;
            public const bool SequentialTrackOrder = true;
            
            // Additional settings for comparison
            public const int MaxConcurrentDownloads = 5;
            public const int MaxDownloadsPerHour = 60;
            public const bool SimulateDelays = true;
            public const int MinDelayBetweenTracksMs = 1000;
            public const int MaxDelayBetweenTracksMs = 3000;
            public const int MinDelayBetweenAlbumsMs = 5000;
            public const int MaxDelayBetweenAlbumsMs = 15000;
            public const bool RandomizeAlbumOrder = false;
            public const bool SimulateSkips = false;
            public const float SkipProbability = 0.02f;
        }

        // Apply profile settings to a TidalSettings object
        public static void ApplyProfile(TidalSettings settings, BehaviorProfile profile)
        {
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

        // Check if settings match a profile
        public static bool MatchesProfile(TidalSettings settings, BehaviorProfile profile)
        {
            var tempSettings = (TidalSettings)settings.Clone();
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