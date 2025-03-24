using System;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.Clients.Tidal;
using NzbDrone.Core.Parser;
using NzbDrone.Plugin.Tidal;
using FluentValidation.Results;

namespace NzbDrone.Core.Indexers.Tidal
{
    public class Tidal : HttpIndexerBase<TidalIndexerSettings>
    {
        public override string Name => "Tidal";
        public override string Protocol => nameof(TidalDownloadProtocol);
        public override bool SupportsRss => false;
        public override bool SupportsSearch => true;
        public override int PageSize => 100;
        public override TimeSpan RateLimit => TimeSpan.FromSeconds(2);

        private readonly ITidalProxy _tidalProxy;

        public Tidal(ITidalProxy tidalProxy,
            IHttpClient httpClient,
            IIndexerStatusService indexerStatusService,
            IConfigService configService,
            IParsingService parsingService,
            Logger logger)
            : base(httpClient, indexerStatusService, configService, parsingService, logger)
        {
            _tidalProxy = tidalProxy;
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            if (string.IsNullOrEmpty(Settings.ConfigPath))
            {
                _logger.Warn("Config path is not set");
                return new TidalRequestGenerator()
                {
                    Settings = Settings,
                    Logger = _logger
                };
            }

            try
            {
                TidalAPI.Initialize(Settings.ConfigPath, _httpClient, _logger);
                
                var loginTask = TidalAPI.Instance.Client.Login(Settings.RedirectUrl);
                loginTask.Wait();

                // the url was submitted to the api so it likely cannot be reused
                TidalAPI.Instance.Client.RegeneratePkceCodes();

                var success = loginTask.Result;
                if (!success)
                {
                    _logger.Warn("Tidal login failed");
                    return new TidalRequestGenerator()
                    {
                        Settings = Settings,
                        Logger = _logger
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Tidal login failed:\n{ex}");
                return new TidalRequestGenerator()
                {
                    Settings = Settings,
                    Logger = _logger
                };
            }

            return new TidalRequestGenerator()
            {
                Settings = Settings,
                Logger = _logger
            };
        }

        public override IParseIndexerResponse GetParser()
        {
            return new TidalParser()
            {
                Settings = Settings
            };
        }

        protected override async Task<ValidationFailure> TestConnection()
        {
            if (string.IsNullOrEmpty(Settings.ConfigPath))
            {
                return new ValidationFailure("ConfigPath", "Config path is required");
            }

            try
            {
                TidalAPI.Initialize(Settings.ConfigPath, _httpClient, _logger);
                
                var loginTask = TidalAPI.Instance.Client.Login(Settings.RedirectUrl);
                await loginTask;

                // the url was submitted to the api so it likely cannot be reused
                TidalAPI.Instance.Client.RegeneratePkceCodes();

                var success = loginTask.Result;
                if (!success)
                {
                    return new ValidationFailure(string.Empty, "Failed to login to Tidal. Please check your redirect URL.");
                }

                // If login is successful, continue with the base test
                return await base.TestConnection();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error testing Tidal connection");
                return new ValidationFailure(string.Empty, $"Error connecting to Tidal: {ex.Message}");
            }
        }
    }
}
