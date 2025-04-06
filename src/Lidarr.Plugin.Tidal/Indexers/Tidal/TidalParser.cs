using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Plugin.Tidal;
using TidalSharp.Data;
using TidalSharp.Exceptions;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Indexers.Tidal
{
    public class TidalParser : IParseIndexerResponse
    {
        public TidalIndexerSettings Settings { get; set; }
        public Logger Logger { get; set; }
        public IParsingService ParsingService { get; set; }
        public ITidalReleaseCache ReleaseCache { get; set; }

        public IList<ReleaseInfo> ParseResponse(IndexerResponse response)
        {
            var releaseInfos = new List<ReleaseInfo>();
            var content = new HttpResponse<TidalSearchResponse>(response.HttpResponse).Content;

            var jsonResponse = JObject.Parse(content).ToObject<TidalSearchResponse>();
            
            int albumCount = jsonResponse?.AlbumResults?.Items?.Length ?? 0;
            int trackCount = jsonResponse?.TrackResults?.Items?.Length ?? 0;
            
            Logger?.Debug($"📊 Processing Tidal search response - Found {albumCount} albums and {trackCount} tracks");
            
            // Process album results
            if (jsonResponse?.AlbumResults?.Items != null)
            {
                var albumReleases = jsonResponse.AlbumResults.Items.Select(ProcessAlbumResult).ToArray();
                foreach (var releases in albumReleases)
                {
                    releaseInfos.AddRange(releases);
                }
                
                // Process track results (only for albums not already processed)
                if (jsonResponse?.TrackResults?.Items != null && jsonResponse.TrackResults.Items.Length > 0)
                {
                    var processedAlbumIds = jsonResponse.AlbumResults.Items.Select(a => a.Id).ToHashSet();
                    
                    var tracksByAlbum = jsonResponse.TrackResults.Items
                        .Where(track => track?.Album != null && !processedAlbumIds.Contains(track.Album.Id))
                        .GroupBy(t => t.Album.Id)
                        .ToDictionary(g => g.Key, g => g.ToList());
                    
                    Logger?.Debug($"📀 Found {tracksByAlbum.Count} additional albums from track results that need processing");
                    
                    var trackTasks = tracksByAlbum.Select(async album => 
                    {
                        Logger?.Debug($"🔍 Fetching full album details for album ID {album.Key} (found through track results)");
                        return await ProcessTrackAlbumResultAsync(album.Value.First());
                    })
                    .ToArray();
                    
                    // Wait for all track processing to complete
                    Task.WhenAll(trackTasks).Wait();
                    
                    // Add results from track processing
                    int validTrackResults = 0;
                    foreach (var task in trackTasks)
                    {
                        if (task.Result != null)
                        {
                            releaseInfos.AddRange(task.Result);
                            validTrackResults++;
                        }
                    }
                    
                    Logger?.Debug($"🎵 Successfully processed {validTrackResults}/{tracksByAlbum.Count} additional albums from track results");
                }
            }

            var finalResults = releaseInfos
                .OrderByDescending(o => o.Size)
                .ToArray();
            
            Logger?.Info($"✅ Generated {finalResults.Length} total release results from Tidal search");
            
            // Cache each release with extended timeout
            if (ReleaseCache != null && ParsingService != null)
            {
                foreach (var release in finalResults)
                {
                    try
                    {
                        var parsedAlbumInfo = Parser.Parser.ParseAlbumTitle(release.Title);
                        if (parsedAlbumInfo != null)
                        {
                            // Map to RemoteAlbum and cache it
                            var remoteAlbum = new RemoteAlbum
                            {
                                Release = release,
                                ParsedAlbumInfo = parsedAlbumInfo
                            };
                            
                            // If parsing service is available, try to map artist/album info
                            try
                            {
                                // Use ParsingService to map remote album
                                var mappedRemoteAlbum = ParsingService.Map(parsedAlbumInfo);
                                if (mappedRemoteAlbum != null)
                                {
                                    // Copy over the mapping but keep our release
                                    remoteAlbum.Artist = mappedRemoteAlbum.Artist;
                                    remoteAlbum.Albums = mappedRemoteAlbum.Albums;
                                    Logger?.Debug($"Successfully mapped release: '{release.Title}' to {remoteAlbum.Artist?.Name}, Albums: {(remoteAlbum.Albums == null ? "None" : remoteAlbum.Albums.Count.ToString())}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger?.Debug($"Error mapping release '{release.Title}': {ex.Message}");
                            }

                            // Cache the release with extended timeout
                            string cacheKey = GetCacheKey(release);
                            ReleaseCache.Set(cacheKey, remoteAlbum);
                            Logger?.Debug($"Cached release with key: {cacheKey}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger?.Error(ex, $"Error caching release '{release.Title}'");
                    }
                }
            }
            
            return finalResults;
        }

        // Generate a consistent cache key for a release
        private string GetCacheKey(ReleaseInfo release)
        {
            return $"{release.IndexerId}_{release.Guid}";
        }

        private IEnumerable<ReleaseInfo> ProcessAlbumResult(TidalSearchResponse.Album result)
        {
            // determine available audio qualities
            List<TidalSharp.Data.AudioQuality> qualityList = new() { TidalSharp.Data.AudioQuality.LOW, TidalSharp.Data.AudioQuality.HIGH };

            if (result.MediaMetadata.Tags.Contains("HIRES_LOSSLESS"))
            {
                qualityList.Add(TidalSharp.Data.AudioQuality.LOSSLESS);
                qualityList.Add(TidalSharp.Data.AudioQuality.HI_RES_LOSSLESS);
                Logger?.Debug($"🎧 Album '{result.Title}' by {result.Artists.First().Name} (ID: {result.Id}) supports Hi-Res Lossless");
            }
            else if (result.MediaMetadata.Tags.Contains("LOSSLESS"))
            {
                qualityList.Add(TidalSharp.Data.AudioQuality.LOSSLESS);
                Logger?.Debug($"🎧 Album '{result.Title}' by {result.Artists.First().Name} (ID: {result.Id}) supports Lossless");
            }
            else
            {
                Logger?.Debug($"🎧 Album '{result.Title}' by {result.Artists.First().Name} (ID: {result.Id}) supports standard quality only");
            }

            var releases = qualityList.Select(q => ToReleaseInfo(result, q)).ToList();
            Logger?.Debug($"📦 Created {releases.Count} quality variants for album '{result.Title}' by {result.Artists.First().Name}");
            return releases;
        }

        private async Task<IEnumerable<ReleaseInfo>> ProcessTrackAlbumResultAsync(TidalSearchResponse.Track result)
        {
            try
            {
                Logger?.Debug($"🔄 Retrieving full album data for '{result.Album.Title}' (ID: {result.Album.Id}) from track result");
                var album = (await TidalAPI.Instance.Client.API.GetAlbum(result.Album.Id)).ToObject<TidalSearchResponse.Album>(); // track albums hold much less data so we get the full one
                return ProcessAlbumResult(album);
            }
            catch (ResourceNotFoundException ex)
            {
                Logger?.Warn($"❌ Album not found for track '{result.Title}' with album ID {result.Album.Id}: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Logger?.Error($"❌ Error processing album for track '{result.Title}': {ex.Message}");
                return null;
            }
        }

        private static ReleaseInfo ToReleaseInfo(TidalSearchResponse.Album x, TidalSharp.Data.AudioQuality bitrate)
        {
            var publishDate = DateTime.UtcNow;
            var year = 0;
            if (DateTime.TryParse(x.ReleaseDate, out var digitalReleaseDate))
            {
                publishDate = digitalReleaseDate;
                year = publishDate.Year;
            }
            else if (DateTime.TryParse(x.StreamStartDate, out var startStreamDate))
            {
                publishDate = startStreamDate;
                year = startStreamDate.Year;
            }

            var url = x.Url;

            // Format the title to include album and artist for better parsing
            var title = $"{x.Artists.First().Name} - {x.Title}";

            var result = new ReleaseInfo
            {
                Guid = $"Tidal-{x.Id}-{bitrate}",
                Artist = x.Artists.First().Name,
                Album = x.Title,
                Title = title,
                DownloadUrl = url,
                InfoUrl = url,
                PublishDate = publishDate,
                DownloadProtocol = nameof(TidalDownloadProtocol),
                IndexerId = 1,
                Indexer = "Tidal"
            };

            string format;
            switch (bitrate)
            {
                case TidalSharp.Data.AudioQuality.LOW:
                    result.Codec = "AAC";
                    result.Container = "96";
                    format = "AAC (M4A) 96kbps";
                    break;
                case TidalSharp.Data.AudioQuality.HIGH:
                    result.Codec = "AAC";
                    result.Container = "320";
                    format = "AAC (M4A) 320kbps";
                    break;
                case TidalSharp.Data.AudioQuality.LOSSLESS:
                    result.Codec = "FLAC";
                    result.Container = "Lossless";
                    format = "FLAC (M4A) Lossless";
                    break;
                case TidalSharp.Data.AudioQuality.HI_RES_LOSSLESS:
                    result.Codec = "FLAC";
                    result.Container = "24bit Lossless";
                    format = "FLAC (M4A) 24bit Lossless";
                    break;
                default:
                    throw new NotImplementedException();
            }

            // estimated sizing as tidal doesn't provide exact sizes in its api
            var bps = bitrate switch
            {
                TidalSharp.Data.AudioQuality.HI_RES_LOSSLESS => 1152000,
                TidalSharp.Data.AudioQuality.LOSSLESS => 176400,
                TidalSharp.Data.AudioQuality.HIGH => 40000,
                TidalSharp.Data.AudioQuality.LOW => 12000,
                _ => 40000
            };
            var size = x.Duration * bps;

            result.Size = size;

            if (year > 0)
            {
                result.Title += $" ({year})";
            }

            if (x.Explicit)
            {
                result.Title += " [Explicit]";
            }

            result.Title += $" [{format}] [WEB]";

            return result;
        }
    }
}
