using NLog;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.Download.Clients.Tidal;

namespace Lidarr.Plugin.Tidal.Services.Lyrics
{
    /// <summary>
    /// Interface for Tidal lyrics service to handle all lyrics-related operations.
    /// </summary>
    public interface ILyricsService
    {
        /// <summary>
        /// Fetches lyrics from Tidal and backup providers if configured.
        /// </summary>
        /// <param name="trackId">The Tidal track ID</param>
        /// <param name="trackTitle">The track title</param>
        /// <param name="artistName">The artist name</param>
        /// <param name="albumTitle">The album title</param>
        /// <param name="duration">Track duration in seconds</param>
        /// <param name="settings">Tidal settings</param>
        /// <param name="logger">Logger instance</param>
        /// <param name="cancellation">Cancellation token</param>
        /// <returns>A tuple containing plain lyrics text and synchronized lyrics text (or null if not available)</returns>
        Task<(string plainLyrics, string syncLyrics)> FetchLyrics(
            string trackId, 
            string trackTitle, 
            string artistName, 
            string albumTitle, 
            int duration, 
            TidalSettings settings, 
            Logger logger, 
            CancellationToken cancellation);

        /// <summary>
        /// Creates an LRC file for synchronized lyrics and returns the file path.
        /// </summary>
        /// <param name="baseAudioFilePath">The audio file path (without extension)</param>
        /// <param name="syncLyrics">The synchronized lyrics content</param>
        /// <param name="logger">Logger instance</param>
        /// <param name="cancellation">Cancellation token</param>
        /// <returns>Path to the created LRC file, or null if creation failed</returns>
        Task<string> CreateLrcFile(
            string baseAudioFilePath, 
            string syncLyrics, 
            Logger logger, 
            CancellationToken cancellation);

        /// <summary>
        /// Processes lyrics for a track - gets lyrics and creates LRC file if enabled.
        /// </summary>
        /// <param name="trackId">The Tidal track ID</param>
        /// <param name="trackFilePath">The track file path</param>
        /// <param name="trackTitle">The track title</param>
        /// <param name="artistName">The artist name</param>
        /// <param name="albumTitle">The album title</param>
        /// <param name="duration">Track duration in seconds</param>
        /// <param name="settings">Tidal settings</param>
        /// <param name="logger">Logger instance</param>
        /// <param name="cancellation">Cancellation token</param>
        /// <returns>A tuple containing plain lyrics text and path to LRC file (or null if not created)</returns>
        Task<(string plainLyrics, string lrcFilePath)> ProcessTrackLyrics(
            string trackId,
            string trackFilePath,
            string trackTitle,
            string artistName,
            string albumTitle,
            int duration,
            TidalSettings settings,
            Logger logger,
            CancellationToken cancellation);
    }
} 