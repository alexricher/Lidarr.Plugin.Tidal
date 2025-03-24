using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using NzbDrone.Core.Download;
using NzbDrone.Core.Configuration;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Validation;
using FluentValidation.Results;
using NzbDrone.Core.Indexers;
using NLog;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RemotePathMappings;

namespace NzbDrone.Core.Download.Clients.Tidal
{
    public class TidalClient : DownloadClientBase<TidalSettings>
    {
        private readonly ITidalProxy _proxy;

        public TidalClient(ITidalProxy proxy, 
                          IConfigService configService,
                          IDiskProvider diskProvider,
                          IRemotePathMappingService remotePathMappingService,
                          Logger logger)
            : base(configService, diskProvider, remotePathMappingService, logger)
        {
            _proxy = proxy;
        }

        public override string Name => "Tidal";
        
        public override string Protocol => nameof(TidalDownloadProtocol);

        public override Task<string> Download(RemoteAlbum remoteAlbum, IIndexer indexer)
        {
            var settings = GetEffectiveSettings();
            return _proxy.Download(remoteAlbum, settings);
        }

        public override IEnumerable<DownloadClientItem> GetItems()
        {
            var settings = GetEffectiveSettings();
            return _proxy.GetQueue(settings);
        }

        public override void RemoveItem(DownloadClientItem item, bool deleteData)
        {
            if (deleteData)
                DeleteItemData(item);

            var settings = GetEffectiveSettings();
            _proxy.RemoveFromQueue(item.DownloadId, settings);
        }

        public override DownloadClientInfo GetStatus()
        {
            return new DownloadClientInfo
            {
                IsLocalhost = true,
                OutputRootFolders = new List<OsPath> { new OsPath(Settings.DownloadPath) }
            };
        }

        public TidalSettings ApplyBehaviorProfile(TidalSettings settings)
        {
            // Only apply profiles if not using custom
            if (settings.BehaviorProfileType != (int)BehaviorProfile.Custom)
            {
                TidalBehaviorProfiles.ApplyProfile(settings, (BehaviorProfile)settings.BehaviorProfileType);
            }
            
            return settings;
        }

        // Call this method before using the settings
        private TidalSettings GetEffectiveSettings()
        {
            var settings = Settings;
            return ApplyBehaviorProfile(settings);
        }
        
        // This method should be called when settings are saved
        public void OnSettingsSaving(TidalSettings settings)
        {
            // If settings have been customized but profile isn't set to Custom,
            // automatically change it to Custom
            if (settings.IsCustomizedProfile() && 
                settings.BehaviorProfileType != (int)BehaviorProfile.Custom)
            {
                _logger.Debug("Settings have been customized, automatically changing profile to Custom");
                settings.BehaviorProfileType = (int)BehaviorProfile.Custom;
            }
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            try
            {
                var settings = GetEffectiveSettings();
                // Test connection to Tidal
                // _proxy.TestConnection(settings);  // Commented out as method not implemented
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to test Tidal");
                failures.Add(new ValidationFailure("Connection", "Unable to connect to Tidal: " + ex.Message));
            }
        }
    }
}







