using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.Validation;
using NzbDrone.Plugin.Tidal;
using Lidarr.Plugin.Tidal.Services.Country;
using Lidarr.Plugin.Tidal.Services.FileSystem;

namespace NzbDrone.Core.Download.Clients.Tidal
{
    /// <summary>
    /// Tidal download client implementation.
    /// </summary>
    public class Tidal : DownloadClientBase<TidalSettings>
    {
        private readonly NzbDrone.Core.Download.Clients.Tidal.ITidalProxy _proxy;
        private readonly new Logger _logger;
        private readonly IHttpClient _httpClient;
        private readonly new IDiskProvider _diskProvider;
        private readonly IFileSystemService _fileSystemService;
        private readonly ICountryManagerService _countryManagerService;

        /// <summary>
        /// Initializes a new instance of the <see cref="Tidal"/> class.
        /// </summary>
        public Tidal(NzbDrone.Core.Download.Clients.Tidal.ITidalProxy proxy,
                      IConfigService configService,
                      IDiskProvider diskProvider,
                      IRemotePathMappingService remotePathMappingService,
                      IHttpClient httpClient,
                      IFileSystemService fileSystemService,
                      ICountryManagerService countryManagerService,
                      Logger logger)
            : base(configService, diskProvider, remotePathMappingService, logger)
        {
            _proxy = proxy;
            _logger = logger;
            _httpClient = httpClient;
            _diskProvider = diskProvider;
            _fileSystemService = fileSystemService;
            _countryManagerService = countryManagerService;
        }

        /// <summary>
        /// Gets the protocol used by this download client.
        /// </summary>
        public override string Protocol => "TidalDownloadProtocol";

        /// <summary>
        /// Gets the name of this download client.
        /// </summary>
        public override string Name => "Tidal";

        /// <summary>
        /// Gets the items in the download client's queue.
        /// </summary>
        public override IEnumerable<DownloadClientItem> GetItems()
        {
            var queue = _proxy.GetQueue(Settings);

            foreach (var item in queue)
            {
                item.DownloadClientInfo = DownloadClientItemClientInfo.FromDownloadClient(this, false);
            }

            return queue;
        }

        /// <summary>
        /// Removes an item from the download client's queue.
        /// </summary>
        public override void RemoveItem(DownloadClientItem item, bool deleteData)
        {
            if (deleteData)
                DeleteItemData(item);

            _proxy.RemoveFromQueue(item.DownloadId, Settings);
        }

        /// <summary>
        /// Downloads an album using this download client.
        /// </summary>
        public override Task<string> Download(RemoteAlbum remoteAlbum, IIndexer indexer)
        {
            return _proxy.Download(remoteAlbum, Settings);
        }

        /// <summary>
        /// Gets the status of this download client.
        /// </summary>
        public override DownloadClientInfo GetStatus()
        {
            return new DownloadClientInfo
            {
                IsLocalhost = true,
                OutputRootFolders = new() { new OsPath(Settings.DownloadPath) }
            };
        }

        /// <summary>
        /// Tests the connection to the download client.
        /// </summary>
        protected override void Test(List<ValidationFailure> failures)
        {
            try
            {
                // Verify CountryManagerService is available
                if (_countryManagerService == null)
                {
                    _logger.Error("CountryManagerService not available");
                    failures.Add(new ValidationFailure("CountryManagerService", "CountryManagerService is not available"));
                }
                else
                {
                    // Update country code from settings
                    _countryManagerService.UpdateCountryCode(Settings);
                    _logger.Info($"Updated country code to: {_countryManagerService.GetCountryCode()}");
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
                failures.Add(new ValidationFailure("Connection", "Error testing Tidal settings: " + ex.Message));
            }
        }

        /// <summary>
        /// Helper method to return a validation error.
        /// </summary>
        private ValidationResult ValidationError(string message)
        {
            return new ValidationResult(new List<ValidationFailure> { new ValidationFailure("", message) });
        }
    }
}
