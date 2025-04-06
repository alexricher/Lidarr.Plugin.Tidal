namespace NzbDrone.Core.Download.Clients.Tidal
{
    public class DownloadRequest
    {
        public string TrackId { get; set; }
        public string AlbumId { get; set; }
        public string ArtistId { get; set; }
        public string DownloadPath { get; set; }
    }
}