using System;

namespace Lidarr.Plugin.Tidal.Services.Logging
{
    /// <summary>
    /// Constants for common emojis used in logging.
    /// </summary>
    public static class LogEmojis
    {

        // Status emojis
        public const string Success = "✅";
        public const string Error = "❌";
        public const string Warning = "⚠️";
        public const string Info = "ℹ️";
        public const string Debug = "🔍";
        public const string Trace = "🔎";
        public const string Fatal = "💥";
        public const string Status = "📊";

        // Operation emojis
        public const string Start = "🚀";
        public const string Stop = "🛑";
        public const string Pause = "⏸️";
        public const string Resume = "▶️";
        public const string Retry = "🔄";
        public const string Skip = "⏭️";
        public const string Cancel = "🚫";
        public const string Complete = "🏁";
        public const string Next = "➡️";

        // Time-related emojis
        public const string Wait = "⏳";
        public const string Time = "⏱️";
        public const string Schedule = "📅";
        public const string Delay = "⏰";

        // Data-related emojis
        public const string Data = "📊";
        public const string Stats = "📈";
        public const string Database = "🗄️";
        public const string File = "📄";
        public const string Folder = "📁";
        public const string Search = "🔍";
        public const string Download = "📥";
        public const string Upload = "📤";
        public const string Save = "💾";
        public const string Delete = "🗑️";

        // Network-related emojis
        public const string Network = "🌐";
        public const string Api = "🔌";
        public const string Request = "📡";
        public const string Response = "📨";

        // Music-related emojis
        public const string Music = "🎵";
        public const string Album = "💿";
        public const string Artist = "🎤";
        public const string Track = "🎧";
        public const string Playlist = "🎼";

        // Circuit breaker emojis
        public const string CircuitOpen = "🔴";
        public const string CircuitClosed = "🟢";
        public const string CircuitHalfOpen = "🟡";

        // Queue-related emojis
        public const string Queue = "📋";
        public const string Add = "➕";
        public const string Remove = "➖";
        public const string Process = "⚙️";

        // Authentication-related emojis
        public const string Login = "🔑";
        public const string Logout = "🔒";
        public const string Auth = "🔐";

        // Miscellaneous emojis
        public const string Settings = "⚙️";
        public const string User = "👤";
        public const string System = "🖥️";
        public const string Plugin = "🔌";
        public const string Health = "❤️";
        public const string Performance = "⚡";

        // Original EmojiConstants compatibility
        public const string Rocket = Start; // Same as Start
        public const string Hourglass = Wait; // Same as Wait

        /// <summary>
        /// Helper method to convert emoji constants to string to avoid method group conversion issues.
        /// This explicit conversion method ensures emojis can be used safely in logging methods.
        /// </summary>
        /// <param name="emoji">The emoji string to convert</param>
        /// <returns>The emoji as a string value</returns>
        public static string GetString(string emoji)
        {
            // Explicitly convert to string to avoid method group conversion issues
            return emoji?.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Returns the emoji as a string literal, which is compatible with all string-based logging methods.
        /// This is the most direct approach to avoid method group conversion issues.
        /// </summary>
        /// <param name="emoji">The emoji to return as a string literal</param>
        /// <returns>The emoji as a string literal</returns>
        public static string AsString(string emoji)
        {
            return emoji ?? string.Empty;
        }
        
        /// <summary>
        /// Gets a strongly-typed emoji for Start operations.
        /// Use this property to avoid method group conversion issues.
        /// </summary>
        public static string StartEmoji => Start;
        
        /// <summary>
        /// Gets a strongly-typed emoji for Wait operations.
        /// Use this property to avoid method group conversion issues.
        /// </summary>
        public static string WaitEmoji => Wait;
        
        /// <summary>
        /// Gets a strongly-typed emoji for Download operations.
        /// Use this property to avoid method group conversion issues.
        /// </summary>
        public static string DownloadEmoji => Download;
        
        /// <summary>
        /// Gets a strongly-typed emoji for Success operations.
        /// Use this property to avoid method group conversion issues.
        /// </summary>
        public static string SuccessEmoji => Success;
        
        /// <summary>
        /// Gets a strongly-typed emoji for Cancel operations.
        /// Use this property to avoid method group conversion issues.
        /// </summary>
        public static string CancelEmoji => Cancel;
        
        /// <summary>
        /// Gets a strongly-typed emoji for Error operations.
        /// Use this property to avoid method group conversion issues.
        /// </summary>
        public static string ErrorEmoji => Error;
    }
}
