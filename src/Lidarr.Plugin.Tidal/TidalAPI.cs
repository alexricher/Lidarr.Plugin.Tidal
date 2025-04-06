using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using NLog;
using NzbDrone.Common.Http;
using TidalSharp;
using System.Threading.Tasks;
using NzbDrone.Core.Download.Clients.Tidal;
using Lidarr.Plugin.Tidal.Services.Country;

namespace NzbDrone.Plugin.Tidal
{
    /// <summary>
    /// Main API wrapper for Tidal integration. Provides singleton access to the Tidal client.
    /// </summary>
    public class TidalAPI
    {
        /// <summary>
        /// Gets the singleton instance of the TidalAPI.
        /// </summary>
        public static TidalAPI Instance { get; private set; }
        
        /// <summary>
        /// Lock object for thread-safe initialization.
        /// </summary>
        private static readonly object _initLock = new object();

        /// <summary>
        /// Static logger for class-level operations.
        /// </summary>
        private static readonly Logger _staticLogger = LogManager.GetCurrentClassLogger();
        
        /// <summary>
        /// Static HTTP client for shared use.
        /// </summary>
        private static IHttpClient _staticHttpClient;
        
        /// <summary>
        /// Static country manager service for shared use.
        /// </summary>
        private static ICountryManagerService _staticCountryManager;

        /// <summary>
        /// The underlying TidalSharp client.
        /// </summary>
        private TidalSharp.TidalClient _client;
        
        /// <summary>
        /// Instance logger for this specific API instance.
        /// </summary>
        private readonly Logger _instanceLogger;
        
        /// <summary>
        /// Configuration directory path for this API instance.
        /// </summary>
        private readonly string _configDir;
        
        /// <summary>
        /// HTTP client for this specific API instance.
        /// </summary>
        private readonly IHttpClient _instanceHttpClient;

        /// <summary>
        /// Initializes the TidalAPI with specified configuration directory and HTTP client.
        /// </summary>
        /// <param name="configPath">The path to directory for storing configuration files.</param>
        /// <param name="httpClient">The HTTP client to use for API requests.</param>
        /// <param name="logger">The logger to use for diagnostic messages.</param>
        /// <param name="countryManagerService">Service for country code management</param>
        public static void Initialize(string configPath, IHttpClient httpClient, Logger logger, ICountryManagerService countryManagerService = null)
        {
            try
            {
                // Store dependencies
                _staticHttpClient = httpClient;
                _staticCountryManager = countryManagerService;
                
                if (logger != null)
                {
                    logger.Debug($"Initializing TidalAPI with config path: {configPath ?? "null"}");
                }
                
                // Basic parameter validation before locking
                if (string.IsNullOrWhiteSpace(configPath))
                {
                    logger?.Error("Cannot initialize TidalAPI: Config directory is null or empty");
                    // Create a default temp path for config instead of failing
                    configPath = Path.Combine(Path.GetTempPath(), "Lidarr", "TidalConfig");
                    logger?.Warn($"Using temporary config directory: {configPath}");
                }
                
                if (httpClient == null)
                {
                    logger?.Error("Cannot initialize TidalAPI: HTTP client is null");
                    return; // Fail gracefully
                }
                
                lock (_initLock)
                {
                    try
                    {
                        if (Instance != null)
                        {
                            logger?.Debug("TidalAPI instance already exists, skipping initialization");
                            return;
                        }
                            
                        // Ensure the config directory exists
                        if (!Directory.Exists(configPath))
                        {
                            logger?.Info($"Creating Tidal config directory: {configPath}");
                            try
                            {
                                Directory.CreateDirectory(configPath);
                            }
                            catch (Exception ex)
                            {
                                logger?.Error(ex, $"Failed to create config directory '{configPath}'");
                                return; // Fail gracefully instead of throwing
                            }
                        }
                        
                        // Create the client
                        try
                        {
                            logger?.Debug("Creating TidalAPI instance");

                            // Create the client
                            var client = new TidalSharp.TidalClient(configPath, httpClient);

                            // Create the API instance
                            Instance = new TidalAPI(configPath, httpClient, logger, client);
                            
                            // Initialize the TidalProxy
                            Instance.InitializeTidalProxy(logger);
                            
                            // Intercept HTTP requests to inject country code if possible
                            if (countryManagerService != null && Instance != null && Instance.Client != null)
                            {
                                // Note: This part depends on the TidalSharp library supporting event hooks
                                // If OnBeforeApiRequest isn't available, this code will need to be modified
                                // based on what the TidalSharp library actually provides
                                logger?.Debug("Country manager service is available, but TidalSharp client may not support request interception");
                            }
                            
                            logger?.Info("TidalAPI initialized successfully");
                        }
                        catch (Exception ex)
                        {
                            logger?.Error(ex, "Error initializing TidalAPI");
                            // Don't throw - just log and return
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.Error(ex, "Unexpected error initializing TidalAPI");
                        // Don't throw - just log and return
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "Error initializing TidalAPI");
            }
        }

        /// <summary>
        /// Private constructor to enforce singleton pattern.
        /// </summary>
        /// <param name="configDir">Directory where Tidal configuration files are stored.</param>
        /// <param name="httpClient">The HTTP client to use for API requests.</param>
        /// <param name="logger">The logger to use for diagnostic messages.</param>
        /// <param name="client">The TidalSharp client instance.</param>
        private TidalAPI(string configDir, IHttpClient httpClient, Logger logger, TidalSharp.TidalClient client)
        {
            _client = client;
            _instanceLogger = logger;
            _configDir = configDir;
            _instanceHttpClient = httpClient;
        }

        /// <summary>
        /// Gets the underlying TidalSharp client.
        /// </summary>
        public TidalSharp.TidalClient Client => _client;
        
        /// <summary>
        /// Gets the TidalProxy used for download operations.
        /// </summary>
        public NzbDrone.Core.Download.Clients.Tidal.ITidalProxy TidalProxy { get; private set; }
        
        /// <summary>
        /// Initializes the TidalProxy for download operations.
        /// </summary>
        /// <param name="logger">The logger to use for diagnostic messages.</param>
        private void InitializeTidalProxy(Logger logger)
        {
            try
            {
                // Use the passed logger or fall back to the instance logger
                var loggerToUse = logger ?? _instanceLogger ?? LogManager.GetCurrentClassLogger();
                TidalProxy = new NzbDrone.Core.Download.Clients.Tidal.TidalProxy(loggerToUse);
                _instanceLogger?.Debug("TidalProxy initialized successfully");
            }
            catch (Exception ex)
            {
                _instanceLogger?.Error(ex, "Failed to initialize TidalProxy");
            }
        }

        /// <summary>
        /// Builds a Tidal API URL with the specified method and parameters.
        /// </summary>
        /// <param name="method">The API method to call.</param>
        /// <param name="parameters">Optional dictionary of query parameters.</param>
        /// <returns>A fully-formed Tidal API URL.</returns>
        public string GetAPIUrl(string method, Dictionary<string, string> parameters = null)
        {
            try
            {
                parameters ??= new();
                parameters["sessionId"] = _client.ActiveUser?.SessionID ?? "";
                parameters["countryCode"] = _client.ActiveUser?.CountryCode ?? "";
                
                if (!parameters.ContainsKey("limit"))
                    parameters["limit"] = "1000";

                StringBuilder stringBuilder = new("https://api.tidal.com/v1/");
                stringBuilder.Append(method);
                for (var i = 0; i < parameters.Count; i++)
                {
                    var start = i == 0 ? "?" : "&";
                    var key = WebUtility.UrlEncode(parameters.ElementAt(i).Key);
                    var value = WebUtility.UrlEncode(parameters.ElementAt(i).Value);
                    stringBuilder.Append(start + key + "=" + value);
                }
                return stringBuilder.ToString();
            }
            catch (Exception ex)
            {
                _instanceLogger?.Error(ex, "Error building API URL");
                // Return a basic URL as fallback
                return $"https://api.tidal.com/v1/{method}";
            }
        }
    }
}
