using System;

namespace NzbDrone.Core.Download.Clients.Tidal.Models
{
    // Rename to avoid conflict with TidalSharp.DownloadData<T>
    public class TidalDownloadData<T>
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public T Data { get; set; }
        public string FileExtension { get; set; }
    }
}
