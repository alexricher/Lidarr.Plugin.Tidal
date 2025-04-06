using System;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Common.Instrumentation;

namespace Lidarr.Plugin.Tidal.Services.Country
{
    /// <summary>
    /// Service for managing country code functionality for Tidal API requests.
    /// Provides country code validation, updating, and request modification capabilities.
    /// Replaces the singleton TidalCountryManager with dependency injection pattern.
    /// </summary>
    public class CountryManagerService : ICountryManagerService
    {
        private readonly Logger _logger;
        private readonly IHttpClient _httpClient;
        
        /// <summary>
        /// Cache of current country code to avoid frequent lookups
        /// </summary>
        private string _currentCountryCode = "US"; // Default to US

        /// <summary>
        /// Initializes a new instance of the CountryManagerService.
        /// </summary>
        /// <param name="httpClient">The HTTP client to use for API requests.</param>
        /// <param name="logger">The logger to use for diagnostic messages.</param>
        public CountryManagerService(IHttpClient httpClient, Logger logger)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient), "HTTP client cannot be null");
        }

        /// <summary>
        /// Updates the current country code based on settings.
        /// Sets the country code to be used in subsequent API requests.
        /// </summary>
        /// <param name="settings">The settings containing the country code information.</param>
        public void UpdateCountryCode(dynamic settings)
        {
            if (settings == null)
            {
                _logger?.Warn("Cannot update country code - settings is null");
                // Set a default country code
                _currentCountryCode = "US";
                return;
            }

            string newCountryCode = settings.CountryCode;
            if (string.IsNullOrEmpty(newCountryCode))
            {
                _logger?.Debug("Country code in settings is null or empty, defaulting to US");
                newCountryCode = "US";
            }

            if (_currentCountryCode != newCountryCode)
            {
                _logger?.Info($"Updating country code from {_currentCountryCode} to {newCountryCode}");
                _currentCountryCode = newCountryCode;
            }
        }

        /// <summary>
        /// Gets the current country code.
        /// Returns the cached country code or US as fallback.
        /// </summary>
        /// <returns>The current country code, or "US" if not set.</returns>
        public string GetCountryCode()
        {
            return string.IsNullOrEmpty(_currentCountryCode) ? "US" : _currentCountryCode;
        }

        /// <summary>
        /// Intercepts API requests to inject country code parameter.
        /// Modifies requests prior to sending to maintain country code without modifying TidalSharp.
        /// </summary>
        /// <param name="request">The HTTP request to modify.</param>
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
                        string newQuery = Regex.Replace(
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