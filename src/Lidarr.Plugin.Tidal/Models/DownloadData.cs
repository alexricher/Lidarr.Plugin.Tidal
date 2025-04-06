using System;

namespace Lidarr.Plugin.Tidal.Models
{
    /// <summary>
    /// Represents download data for a Tidal item.
    /// Renamed to avoid conflict with TidalSharp.DownloadData.
    /// </summary>
    /// <typeparam name="T">The type of data being downloaded.</typeparam>
    public class TidalDownloadData<T>
    {
        /// <summary>
        /// Gets or sets the unique identifier of the download item.
        /// </summary>
        public string Id { get; set; }
        
        /// <summary>
        /// Gets or sets the title of the download item.
        /// </summary>
        public string Title { get; set; }
        
        /// <summary>
        /// Gets or sets the artist name for the download item.
        /// </summary>
        public string Artist { get; set; }
        
        /// <summary>
        /// Gets or sets the album name for the download item.
        /// </summary>
        public string Album { get; set; }
        
        /// <summary>
        /// Gets or sets the actual data content being downloaded.
        /// </summary>
        public T Data { get; set; }
        
        /// <summary>
        /// Gets or sets the file extension for the downloaded item.
        /// </summary>
        public string FileExtension { get; set; }
    }
}
