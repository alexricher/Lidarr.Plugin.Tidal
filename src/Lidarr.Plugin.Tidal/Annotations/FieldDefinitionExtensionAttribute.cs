using System;

namespace NzbDrone.Core.Annotations
{
    // This class extends the FieldDefinitionAttribute with the additional properties
    // needed by the Tidal plugin that aren't in the core Lidarr codebase
    public static class FieldDefinitionExtensions
    {
        // The HideWhen property key
        public const string HideWhenPropertyKey = "HideWhen";
        
        // The HideValue property key
        public const string HideValuePropertyKey = "HideValue";
    }
} 