using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using TidalSharp;

namespace NzbDrone.Core.Download.Clients.Tidal
{
    internal static class MetadataUtilities
    {
        public static string GetFilledTemplate(string template, string ext, JObject tidalPage, JObject tidalAlbum)
        {
            var rawReleaseDate = tidalAlbum["releaseDate"]?.ToString() ?? tidalAlbum["streamStartDate"]?.ToString();
            var releaseDate = !string.IsNullOrEmpty(rawReleaseDate) ? DateTime.Parse(rawReleaseDate, CultureInfo.InvariantCulture) : DateTime.MinValue;

            return GetFilledTemplate_Internal(template,
                GetTitleFromPage(tidalPage),
                GetTitleFromPage(tidalAlbum),
                tidalAlbum["artist"]!["name"]!.ToString(),
                tidalPage["artist"]!["name"]!.ToString(),
                tidalAlbum["artists"]!.Select(a => a["name"]!.ToString()).ToArray(),
                tidalPage!["artists"]!.Select(a => a["name"]!.ToString()).ToArray(),
                $"{(int)tidalPage["trackNumber"]!:00}",
                tidalAlbum["numberOfTracks"]!.ToString(),
                $"{(int)tidalPage["volumeNumber"]!:00}",
                tidalAlbum["numberOfVolumes"]!.ToString(),
                releaseDate.Year.ToString(CultureInfo.InvariantCulture),
                ext);
        }
        
        // Helper method to extract the complete title from a JObject page
        public static string GetTitleFromPage(JObject page)
        {
            if (page == null)
                return "Unknown";
                
            // Extract title from the JSON object
            string title = page["title"]?.ToString();
            if (string.IsNullOrEmpty(title))
                return "Unknown";
                
            // Check if there's a version info to append
            string version = page["version"]?.ToString();
            if (!string.IsNullOrEmpty(version))
            {
                return $"{title} ({version})";
            }
            
            return title;
        }

        private static string GetFilledTemplate_Internal(string template, string title, string album, string albumArtist, string artist, string[] albumArtists, string[] artists, string track, string trackCount, string volume, string volumeCount, string year, string ext)
        {
            StringBuilder t = new(template);
            ReplaceC("%title%", title);
            ReplaceC("%album%", album);
            ReplaceC("%albumartist%", albumArtist);
            ReplaceC("%artist%", artist);
            ReplaceC("%albumartists%", string.Join("; ", albumArtists));
            ReplaceC("%artists%", string.Join("; ", artists));
            ReplaceC("%track%", track);
            ReplaceC("%trackcount%", trackCount);
            ReplaceC("%volume%", volume);
            ReplaceC("%volumecount%", volumeCount);
            ReplaceC("%ext%", ext);
            ReplaceC("%year%", year);

            return t.ToString();

            void ReplaceC(string o, string r)
            {
                t.Replace(o, CleanPath(r));
            }
        }

        public static string CleanPath(string str)
        {
            var invalid = Path.GetInvalidFileNameChars();
            for (var i = 0; i < invalid.Length; i++)
            {
                var c = invalid[i];
                str = str.Replace(c, '_');
            }
            return str;
        }
    }
}
