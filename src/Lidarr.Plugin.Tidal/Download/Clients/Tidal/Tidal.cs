using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.Validation;
using NzbDrone.Plugin.Tidal;

namespace NzbDrone.Core.Download.Clients.Tidal
{
    public class Tidal : DownloadClientBase<TidalSettings>
    {
        private readonly ITidalProxy _proxy;
        private readonly new Logger _logger;

        public Tidal(ITidalProxy proxy,
                      IConfigService configService,
                      IDiskProvider diskProvider,
                      IRemotePathMappingService remotePathMappingService,
                      Logger logger)
            : base(configService, diskProvider, remotePathMappingService, logger)
        {
            _proxy = proxy;
            _logger = logger;
        }

        public override string Protocol => nameof(TidalDownloadProtocol);

        public override string Name => "Tidal";

        public override IEnumerable<DownloadClientItem> GetItems()
        {
            var queue = _proxy.GetQueue(Settings);

            foreach (var item in queue)
            {
                item.DownloadClientInfo = DownloadClientItemClientInfo.FromDownloadClient(this, false);
            }

            return queue;
        }

        public override void RemoveItem(DownloadClientItem item, bool deleteData)
        {
            if (deleteData)
                DeleteItemData(item);

            _proxy.RemoveFromQueue(item.DownloadId, Settings);
        }

        public override Task<string> Download(RemoteAlbum remoteAlbum, IIndexer indexer)
        {
            return _proxy.Download(remoteAlbum, Settings);
        }

        public override DownloadClientInfo GetStatus()
        {
            return new DownloadClientInfo
            {
                IsLocalhost = true,
                OutputRootFolders = new() { new OsPath(Settings.DownloadPath) }
            };
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            try
            {
                // Initialize TidalCountryManager if needed
                if (TidalCountryManager.Instance == null)
                {
                    _logger.Info("Initializing TidalCountryManager during test");
                    TidalCountryManager.Initialize(null, _logger);
                }
                else
                {
                    // Update the country code in case it changed
                    TidalCountryManager.Instance.UpdateCountryCode(Settings);
                    _logger.Info($"Updated TidalCountryManager with country code: {Settings.CountryCode}");
                }

                // Test status file path if the feature is enabled
                if (Settings.GenerateStatusFiles && !string.IsNullOrWhiteSpace(Settings.StatusFilesPath))
                {
                    _logger.Info($"Testing status file path: {Settings.StatusFilesPath}");
                    if (!Settings.TestStatusPath(_logger))
                    {
                        failures.Add(new ValidationFailure("StatusFilesPath", 
                            "Unable to write to the specified status files path. Please ensure the directory exists and has the correct permissions."));
                    }
                }

                // Test queue persistence path if the feature is enabled
                if (Settings.EnableQueuePersistence && !string.IsNullOrWhiteSpace(Settings.ActualQueuePersistencePath))
                {
                    _logger.Info($"Testing queue persistence path: {Settings.ActualQueuePersistencePath}");
                    if (!Settings.TestQueuePersistencePath(_logger))
                    {
                        failures.Add(new ValidationFailure("QueuePersistencePath", 
                            "Unable to write to the specified queue persistence path. Please ensure the directory exists and has the correct permissions."));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during Tidal download client test");
                failures.Add(new ValidationFailure("Connection", $"Error testing Tidal settings: {ex.Message}"));
            }
        }
    }
}
