using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace NzbDrone.Core.Download.Clients.Tidal.Queue
{
    /// <summary>
    /// Helper class for parsing Tidal API track data
    /// </summary>
    public static class TidalTrackParser
    {
        /// <summary>
        /// Extracts track IDs from Tidal API response
        /// </summary>
        /// <param name="tracks">Collection of track data from Tidal API</param>
        /// <param name="logger">Logger for diagnostic information</param>
        /// <returns>List of track IDs with estimated sizes</returns>
        public static List<(string trackId, long estimatedSize)> ExtractTrackInfo(
            IEnumerable<KeyValuePair<string, JToken>> tracks, 
            NLog.ILogger logger)
        {
            var result = new List<(string trackId, long estimatedSize)>();
            
            foreach (var track in tracks)
            {
                if (track.Value == null)
                {
                    logger?.Debug("Skipping null track value");
                    continue;
                }
                
                string trackId = track.Value["id"]?.ToString();
                if (string.IsNullOrEmpty(trackId))
                {
                    logger?.Debug($"Track ID missing in track data: {track.Value}");
                    continue;
                }
                
                // Estimate size based on duration if available
                long estimatedSize = EstimateTrackSize(track.Value);
                result.Add((trackId, estimatedSize));
            }
            
            return result;
        }
        
        /// <summary>
        /// Estimates track size based on duration and quality
        /// </summary>
        private static long EstimateTrackSize(JToken trackData)
        {
            // Default estimate: 10MB
            long defaultSize = 10 * 1024 * 1024;
            
            // Try to get duration
            int? durationSeconds = trackData["duration"]?.Value<int>();
            if (!durationSeconds.HasValue)
                return defaultSize;
                
            // Try to get quality
            string quality = trackData["audioQuality"]?.ToString() ?? "HIGH";
            
            // Estimate bitrate based on quality
            double bitrateMbps = quality switch
            {
                "LOW" => 0.096,     // 96kbps
                "HIGH" => 0.320,    // 320kbps
                "LOSSLESS" => 1.411, // 1411kbps for CD quality
                "HI_RES_LOSSLESS" => 3.0, // ~3Mbps for hi-res
                _ => 0.320          // Default to HIGH
            };
            
            // Calculate size: bitrate (Mbps) * duration (s) / 8 (bits to bytes)
            return (long)(bitrateMbps * 1024 * 1024 * durationSeconds.Value / 8);
        }
    }
}