using System.Reflection;

namespace NzbDrone.Core.Plugins
{
    public class TidalPlugin : Plugin
    {
        public override string Name => "Tidal";
        public override string Owner => "alexricher";
        public override string GithubUrl => "https://github.com/alexricher/Lidarr.Plugin.Tidal";
    }
}
