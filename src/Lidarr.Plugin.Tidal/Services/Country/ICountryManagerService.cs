using NzbDrone.Common.Http;

namespace Lidarr.Plugin.Tidal.Services.Country
{
    /// <summary>
    /// Interface for managing country code functionality for Tidal API requests.
    /// Provides methods for country code management and request modification.
    /// </summary>
    public interface ICountryManagerService
    {
        /// <summary>
        /// Updates the current country code based on settings.
        /// Used to set the country code for subsequent API requests.
        /// </summary>
        /// <param name="settings">The settings containing the country code information.</param>
        void UpdateCountryCode(dynamic settings);

        /// <summary>
        /// Gets the current country code.
        /// </summary>
        /// <returns>The current country code, or "US" if not set.</returns>
        string GetCountryCode();

        /// <summary>
        /// Intercepts API requests to inject country code parameter.
        /// Used to modify requests prior to sending to maintain country code.
        /// </summary>
        /// <param name="request">The HTTP request to modify.</param>
        void AddCountryCodeToRequest(HttpRequest request);
    }
} 