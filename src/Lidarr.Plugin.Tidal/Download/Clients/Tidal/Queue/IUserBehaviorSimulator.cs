using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Download.Clients.Tidal
{
    public interface IUserBehaviorSimulator
    {
        void AdaptToQueueVolume(TidalSettings settings, int queueCount);
        Task ApplyNaturalBehaviorDelay(TidalSettings settings, DownloadContext context = null, CancellationToken cancellationToken = default);
        IEnumerable<DownloadQueueItem> ReorderQueue(TidalSettings settings, IEnumerable<DownloadQueueItem> items);
        bool ShouldSkipTrack(TidalSettings settings);
        Dictionary<string, string> GetConnectionParameters(TidalSettings settings);
        void LogSessionStats();
        SessionStatistics GetSessionStats();
        bool IsHighVolumeMode { get; }

    }

}