using System.Collections.Generic;
using System;
using System.IO;
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
        private bool _hasValidatedStatusPath;
        private TidalStatusHelper _statusHelper;

        public TidalClient(ITidalProxy proxy, 
                          IConfigService configService,
                          IDiskProvider diskProvider,
                          IRemotePathMappingService remotePathMappingService,
                          Logger logger)
            : base(configService, diskProvider, remotePathMappingService, logger)
        {
            _proxy = proxy;
            _hasValidatedStatusPath = false;
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
            
            // Validate the status file path once after settings are loaded
            if (!_hasValidatedStatusPath)
            {
                try
                {
                    settings.ValidateStatusFilePath(_logger);
                    // Initialize status helper after path is validated
                    _statusHelper = new TidalStatusHelper(settings.StatusFilesPath, _logger);
                    _hasValidatedStatusPath = true;
                    
                    // Clean up any temporary files that might have been left behind
                    _statusHelper.CleanupTempFiles();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error validating status file path during initialization");
                }
            }
            
            return ApplyBehaviorProfile(settings);
        }

        // This method should be called when settings are saved
        public void OnSettingsSaving(TidalSettings settings)
        {
            // Get the previous settings to check if the path changed
            var previousSettings = Settings;
            bool pathChanged = !string.Equals(previousSettings?.StatusFilesPath, settings?.StatusFilesPath, StringComparison.OrdinalIgnoreCase);
            
            // Check if behavior profile has changed
            bool profileChanged = previousSettings?.BehaviorProfileType != settings?.BehaviorProfileType;
            if (profileChanged && settings.BehaviorProfileType != (int)BehaviorProfile.Custom)
            {
                _logger.Debug($"Behavior profile changed from {(BehaviorProfile)previousSettings.BehaviorProfileType} to {(BehaviorProfile)settings.BehaviorProfileType}, applying new profile settings");
                // Make sure to apply the profile settings
                TidalBehaviorProfiles.ApplyProfile(settings, (BehaviorProfile)settings.BehaviorProfileType);
            }
            
            // Check if behavior profile is set to Automatic
            if (settings.BehaviorProfileType == (int)BehaviorProfile.Automatic)
            {
                // Detect if user has manually customized settings, if so suggest changing to custom profile
                if (settings.HasBeenCustomized())
                {
                    _logger.Debug("Settings have been customized, automatically changing profile to Custom");
                    settings.BehaviorProfileType = (int)BehaviorProfile.Custom;
                }
            }

            // If the path changed, we need to validate again
            if (pathChanged)
            {
                _logger.Info($"Status files path changed from '{previousSettings?.StatusFilesPath}' to '{settings?.StatusFilesPath}', will revalidate");
                _hasValidatedStatusPath = false;
            }

            // Validate status file path when settings are saved
            settings.ValidateStatusFilePath(_logger);

            // Create a new status helper with the updated path
            _statusHelper = new TidalStatusHelper(settings.StatusFilesPath, _logger);

            // Mark as validated so we don't validate again
            _hasValidatedStatusPath = true;
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            try
            {
                var settings = GetEffectiveSettings();
                // Test connection to Tidal
                _proxy.TestConnection(settings);  // Uncommented as method is now implemented
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to test Tidal");
                failures.Add(new ValidationFailure("Connection", "Unable to connect to Tidal: " + ex.Message));
            }
        }
        
        // This method is used to create a status file with the given name and content
        public void CreateOrUpdateStatusFile(string fileName, string content)
        {
            try
            {
                var settings = GetEffectiveSettings();
                if (_statusHelper == null || !_statusHelper.IsEnabled)
                {
                    _logger.Warn("Cannot create status file - StatusHelper is not initialized or disabled");
                    return;
                }
                
                // Use status helper to write file
                var success = _statusHelper.WriteJsonFile(fileName, content);
                if (!success)
                {
                    _logger.Warn($"Failed to write status file: {fileName}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to create/update status file {fileName}");
            }
        }
        
        // Create or update a status file from an object
        public bool CreateOrUpdateStatusFile<T>(string fileName, T data)
        {
            try
            {
                var settings = GetEffectiveSettings();
                if (_statusHelper == null || !_statusHelper.IsEnabled)
                {
                    _logger.Warn("Cannot create status file - StatusHelper is not initialized or disabled");
                    return false;
                }
                
                return _statusHelper.WriteJsonFile(fileName, data);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to create/update status file {fileName} from object");
                return false;
            }
        }
        
        // This method is used to check if a status file exists
        public bool StatusFileExists(string fileName)
        {
            try
            {
                var settings = GetEffectiveSettings();
                if (_statusHelper == null || !_statusHelper.IsEnabled)
                {
                    _logger.Warn("Cannot check status file - StatusHelper is not initialized or disabled");
                    return false;
                }
                
                return _statusHelper.FileExists(fileName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to check if status file {fileName} exists");
                return false;
            }
        }
        
        // This method is used to read a status file
        public string ReadStatusFile(string fileName)
        {
            try
            {
                var settings = GetEffectiveSettings();
                if (_statusHelper == null || !_statusHelper.IsEnabled)
                {
                    _logger.Warn("Cannot read status file - StatusHelper is not initialized or disabled");
                    return null;
                }
                
                var filePath = Path.Combine(settings.StatusFilesPath, fileName);
                if (!File.Exists(filePath))
                {
                    _logger.Debug($"Status file does not exist: {filePath}");
                    return null;
                }
                
                // Use standard file read since we need string content
                return File.ReadAllText(filePath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to read status file {fileName}");
                return null;
            }
        }
        
        // Read a status file and deserialize it to the specified type
        public T ReadStatusFile<T>(string fileName) where T : class
        {
            try
            {
                var settings = GetEffectiveSettings();
                if (_statusHelper == null || !_statusHelper.IsEnabled)
                {
                    _logger.Warn("Cannot read status file - StatusHelper is not initialized or disabled");
                    return null;
                }
                
                return _statusHelper.ReadJsonFile<T>(fileName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to read and deserialize status file {fileName}");
                return null;
            }
        }
        
        // List all status files matching a pattern
        public string[] ListStatusFiles(string pattern = "*.*")
        {
            try
            {
                var settings = GetEffectiveSettings();
                if (_statusHelper == null || !_statusHelper.IsEnabled)
                {
                    _logger.Warn("Cannot list status files - StatusHelper is not initialized or disabled");
                    return Array.Empty<string>();
                }
                
                return _statusHelper.ListFiles(pattern);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to list status files with pattern {pattern}");
                return Array.Empty<string>();
            }
        }
        
        // Delete a status file
        public bool DeleteStatusFile(string fileName)
        {
            try
            {
                var settings = GetEffectiveSettings();
                if (_statusHelper == null || !_statusHelper.IsEnabled)
                {
                    _logger.Warn("Cannot delete status file - StatusHelper is not initialized or disabled");
                    return false;
                }
                
                return _statusHelper.DeleteFile(fileName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to delete status file {fileName}");
                return false;
            }
        }
    }
}







