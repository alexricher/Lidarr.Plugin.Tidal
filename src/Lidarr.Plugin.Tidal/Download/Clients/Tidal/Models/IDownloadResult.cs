namespace NzbDrone.Core.Download.Clients.Tidal
{
    public interface IDownloadResult
    {
        bool Success { get; }
        string Message { get; }
        string DownloadedPath { get; }
    }
}