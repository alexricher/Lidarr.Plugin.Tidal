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

namespace NzbDrone.Plugin.Tidal
{
    public class TidalAPI
    {
        public static TidalAPI Instance { get; private set; }
        private static readonly object _initLock = new object();

        public static void Initialize(string configDir, IHttpClient httpClient, Logger logger)
        {
            if (logger != null)
            {
                logger.Debug($"Initializing TidalAPI with config path: {configDir ?? "null"}");
            }
            
            // Basic parameter validation before locking
            if (string.IsNullOrWhiteSpace(configDir))
            {
                logger?.Error("Cannot initialize TidalAPI: Config directory is null or empty");
                return; // Fail gracefully
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
                    if (!Directory.Exists(configDir))
                    {
                        logger?.Info($"Creating Tidal config directory: {configDir}");
                        try
                        {
                            Directory.CreateDirectory(configDir);
                        }
                        catch (Exception ex)
                        {
                            logger?.Error(ex, $"Failed to create config directory '{configDir}'");
                            return; // Fail gracefully instead of throwing
                        }
                    }
                    
                    // Initialize the country manager first
                    try
                    {
                        TidalCountryManager.EnsureInitialized(httpClient, logger);
                    }
                    catch (Exception ex)
                    {
                        logger?.Error(ex, "Failed to initialize TidalCountryManager");
                        // Continue anyway - country manager is helpful but not critical
                    }
                    
                    // Create the client
                    try
                    {
                        logger?.Debug("Creating TidalAPI instance");

                        // Create the client
                        var client = new TidalClient(configDir, httpClient);

                        // Create the API instance
                        Instance = new TidalAPI(configDir, httpClient, logger, client);
                        
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

        private TidalAPI(string configDir, IHttpClient httpClient, Logger logger, TidalClient client)
        {
            _client = client;
            _logger = logger;
        }

        public TidalClient Client => _client;

        private TidalClient _client;
        private readonly Logger _logger;

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
                _logger?.Error(ex, "Error building API URL");
                // Return a basic URL as fallback
                return $"https://api.tidal.com/v1/{method}";
            }
        }
    }
}
