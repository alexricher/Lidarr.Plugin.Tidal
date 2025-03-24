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
                    break;
            }
        }

        // Check if settings match a profile
        public static bool MatchesProfile(TidalSettings settings, BehaviorProfile profile)
        {
            var tempSettings = (TidalSettings)settings.Clone();
            ApplyProfile(tempSettings, profile);
            
            return settings.SessionDurationMinutes == tempSettings.SessionDurationMinutes &&
                   settings.BreakDurationMinutes == tempSettings.BreakDurationMinutes &&
                   settings.TrackToTrackDelayMin == tempSettings.TrackToTrackDelayMin &&
                   settings.TrackToTrackDelayMax == tempSettings.TrackToTrackDelayMax &&
                   settings.AlbumToAlbumDelayMin == tempSettings.AlbumToAlbumDelayMin &&
                   settings.AlbumToAlbumDelayMax == tempSettings.AlbumToAlbumDelayMax &&
                   settings.SimulateListeningPatterns == tempSettings.SimulateListeningPatterns &&
                   settings.CompleteAlbums == tempSettings.CompleteAlbums &&
                   settings.PreferArtistGrouping == tempSettings.PreferArtistGrouping &&
                   settings.SequentialTrackOrder == tempSettings.SequentialTrackOrder;
        }
    }
}