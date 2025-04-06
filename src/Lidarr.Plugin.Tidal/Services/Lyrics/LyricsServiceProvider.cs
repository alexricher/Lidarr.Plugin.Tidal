using Lidarr.Plugin.Tidal.Services.FileSystem;

namespace Lidarr.Plugin.Tidal.Services.Lyrics
{
    /// <summary>
    /// Provider for lyrics service implementations.
    /// </summary>
    public static class LyricsServiceProvider
    {
        private static ILyricsService _instance;

        /// <summary>
        /// Gets the current instance of the lyrics service, creating it if it doesn't exist.
        /// </summary>
        public static ILyricsService Current
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new LyricsService(new FileSystemService());
                }
                return _instance;
            }
        }

        /// <summary>
        /// Registers a custom lyrics service implementation.
        /// </summary>
        /// <param name="service">The service implementation to register</param>
        public static void Register(ILyricsService service)
        {
            _instance = service;
        }

        /// <summary>
        /// Resets the current instance to a new default lyrics service.
        /// </summary>
        public static void Reset()
        {
            _instance = new LyricsService(new FileSystemService());
        }
    }
} 