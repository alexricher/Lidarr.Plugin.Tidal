using System;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Download.Clients.Tidal;
using TidalSharp;

namespace NzbDrone.Plugin.Tidal
{
    /// <summary>
    /// Manages country code functionality for Tidal API requests without modifying the external TidalSharp library
    /// </summary>
    public class TidalCountryManager
    {
        private readonly Logger _logger;
        private readonly IHttpClient _httpClient;
        private static TidalCountryManager _instance;
        
        // Cache of current country code to avoid frequent lookups
        private string _currentCountryCode = "US"; // Default to US

        public static TidalCountryManager Instance => _instance;

        public static void Initialize(IHttpClient httpClient, Logger logger)
        {
            try
            {
                if (_instance == null)
                {
                    if (httpClient == null)
                    {
                        logger?.Error("Cannot initialize TidalCountryManager with null httpClient");
                        throw new ArgumentNullException(nameof(httpClient), "HTTP client cannot be null");
                    }
                    
                    _instance = new TidalCountryManager(httpClient, logger);
                    logger?.Debug("TidalCountryManager initialized successfully");
                }
                else
                {
                    logger?.Debug("TidalCountryManager already initialized, skipping initialization");
                }
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "Error initializing TidalCountryManager");
                // Don't rethrow - we want to fail gracefully
            }
        }

        // Create a safe way to ensure the manager is initialized
        public static void EnsureInitialized(IHttpClient httpClient, Logger logger)
        {
            if (_instance == null)
            {
                Initialize(httpClient, logger);
            }
        }

        private TidalCountryManager(IHttpClient httpClient, Logger logger)
        {
            _logger = logger;
            _httpClient = httpClient;
        }

        /// <summary>
        /// Updates the current country code based on settings
        /// </summary>
        public void UpdateCountryCode(TidalSettings settings)
        {
            try
            {
                if (settings == null)
                {
                    _logger?.Warn("Cannot update country code with null settings");
                    return;
                }

                string newCountryCode = settings.CountryCode;
                
                if (string.IsNullOrWhiteSpace(newCountryCode))
                {
                    _logger?.Warn("Country code from settings is null or empty, using default (US)");
                    newCountryCode = "US"; // Ensure we always have a valid country code
                }
                
                _currentCountryCode = newCountryCode;
                _logger?.Debug($"Updated Tidal country code to {_currentCountryCode}");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error updating country code");
            }
        }

        /// <summary>
        /// Gets the current country code
        /// </summary>
        public string GetCountryCode()
        {
            return string.IsNullOrEmpty(_currentCountryCode) ? "US" : _currentCountryCode;
        }

        /// <summary>
        /// Intercepts API requests to inject country code parameter - use this to modify requests
        /// prior to sending to maintain country code without modifying TidalSharp
        /// </summary>
        public void AddCountryCodeToRequest(HttpRequest request)
        {
            try
            {
                if (request == null)
                {
                    _logger?.Warn("Cannot add country code to null request");
                    return;
                }

                // Only add if we have a valid country code
                if (!string.IsNullOrEmpty(_currentCountryCode))
                {
                    if (request.Url.Query.Contains("countryCode="))
                    {
                        // If there's already a countryCode parameter, update it
                        string oldQuery = request.Url.Query;
                        string newQuery = System.Text.RegularExpressions.Regex.Replace(
                            oldQuery, 
                            @"countryCode=[^&]+", 
                            $"countryCode={_currentCountryCode}"
                        );
                        
                        // Update the URL with the new query
                        var builder = new UriBuilder(request.Url.ToString());
                        builder.Query = newQuery.TrimStart('?');
                        request.Url = new HttpUri(builder.Uri.ToString());
                    }
                    else
                    {
                        // Add country code as query parameter if not present
                        string separator = request.Url.Query.Length > 0 ? "&" : "?";
                        string newUrl = $"{request.Url}{separator}countryCode={_currentCountryCode}";
                        request.Url = new HttpUri(newUrl);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error adding country code to request");
                // Don't throw - we want to continue even if this fails
            }
        }
    }
} 
