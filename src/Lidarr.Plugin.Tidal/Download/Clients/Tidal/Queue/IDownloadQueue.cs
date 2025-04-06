using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients.Tidal.Interfaces;

namespace NzbDrone.Core.Download.Clients.Tidal.Queue
{
    public interface IDownloadQueue
    {
        IDownloadItem GetItemById(string id);
        List<DownloadClientItem> GetItems();
        void AddItem(IDownloadItem item);
        void RemoveItem(string id);
    }
}