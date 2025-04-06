using System.Reflection;
using Lidarr.Plugin.Tidal.Services;
using Lidarr.Plugin.Tidal.Services.Behavior;
using Lidarr.Plugin.Tidal.Services.Country;
using Lidarr.Plugin.Tidal.Services.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using NzbDrone.Core.Download.Clients.Tidal.Services;

namespace NzbDrone.Core.Plugins
{
    /// <summary>
    /// Main plugin class for the Tidal integration with Lidarr.
    /// This class provides metadata about the plugin to Lidarr's plugin system.
    /// </summary>
    public class TidalPlugin : Plugin
    {
        /// <summary>
        /// Gets the name of the plugin as displayed in Lidarr.
        /// </summary>
        public override string Name => "Tidal";
        
        /// <summary>
        /// Gets the GitHub username of the plugin owner.
        /// </summary>
        public override string Owner => "alexricher";
        
        /// <summary>
        /// Gets the GitHub repository URL where the plugin is maintained.
        /// </summary>
        public override string GithubUrl => "https://github.com/alexricher/Lidarr.Plugin.Tidal";

        /// <summary>
        /// Registers the plugin's services with the dependency injection container.
        /// Called by the Lidarr plugin system during initialization.
        /// </summary>
        /// <param name="services">The service collection to register services with</param>
        public void RegisterServices(IServiceCollection services)
        {
            // Register services for dependency injection
            services.AddSingleton<IFileSystemService, FileSystemService>();
            services.AddSingleton<IBehaviorProfileService, BehaviorProfileService>();
            services.AddSingleton<ICountryManagerService, CountryManagerService>();
            services.AddSingleton<IRateLimiter, UnifiedRateLimiter>();
            
            // Other registrations
            // ... existing code ...
        }
    }
}
