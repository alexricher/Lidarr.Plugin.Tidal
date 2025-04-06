using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Download.Clients.Tidal
{
    public class DownloadResult : IDownloadResult
    {
        public bool Success { get; set; } = true;
        public string Message { get; set; } = string.Empty;
        public string DownloadedPath { get; set; } = string.Empty;
        
        public string Id { get; set; }
        public string Title { get; set; }
        public string Status { get; set; }
        
        public DownloadResult()
        {
            Id = Guid.NewGuid().ToString();
            Status = "Queued";
        }
        
        public DownloadResult(string id, string title, string status)
        {
            Id = id;
            Title = title;
            Status = status;
        }
    }
} 