using NzbDrone.Core.Download.Clients.Tidal;

namespace Lidarr.Plugin.Tidal.Services.Behavior
{
    /// <summary>
    /// Service for managing behavior profiles for Tidal downloads.
    /// </summary>
    public interface IBehaviorProfileService
    {
        /// <summary>
        /// Applies a predefined behavior profile to the provided settings object.
        /// Updates all relevant settings properties to match the selected profile.
        /// </summary>
        /// <param name="settings">The TidalSettings object to update</param>
        /// <param name="profile">The behavior profile to apply</param>
        void ApplyProfile(TidalSettings settings, BehaviorProfile profile);

        /// <summary>
        /// Checks if the provided settings match a predefined behavior profile.
        /// </summary>
        /// <param name="settings">The TidalSettings object to check</param>
        /// <param name="profile">The behavior profile to compare against</param>
        /// <returns>True if the settings match the profile, false otherwise</returns>
        bool MatchesProfile(TidalSettings settings, BehaviorProfile profile);
    }
} 