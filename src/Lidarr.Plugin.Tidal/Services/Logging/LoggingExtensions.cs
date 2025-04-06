using System;
using NLog;
using ILogger = NLog.ILogger;

namespace Lidarr.Plugin.Tidal.Services.Logging
{
    /// <summary>
    /// Extensions for adding emojis to log messages.
    /// </summary>
    public static class LoggingExtensions
    {
        /// <summary>
        /// Logs an informational message with an emoji.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="emoji">The emoji to prepend to the message.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="args">The arguments to format the message with.</param>
        public static void InfoWithEmoji(this Logger logger, string emoji, string message, params object[] args)
        {
            logger.Info($"{emoji} {message}", args);
        }

        /// <summary>
        /// Logs an informational message with an emoji.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="emoji">The emoji to prepend to the message.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="args">The arguments to format the message with.</param>
        public static void InfoWithEmoji(this ILogger logger, string emoji, string message, params object[] args)
        {
            logger.Info($"{emoji} {message}", args);
        }

        /// <summary>
        /// Logs a debug message with an emoji.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="emoji">The emoji to prepend to the message.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="args">The arguments to format the message with.</param>
        public static void DebugWithEmoji(this Logger logger, string emoji, string message, params object[] args)
        {
            logger.Debug($"{emoji} {message}", args);
        }

        /// <summary>
        /// Logs a debug message with an emoji.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="emoji">The emoji to prepend to the message.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="args">The arguments to format the message with.</param>
        public static void DebugWithEmoji(this ILogger logger, string emoji, string message, params object[] args)
        {
            logger.Debug($"{emoji} {message}", args);
        }

        /// <summary>
        /// Logs a warning message with an emoji.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="emoji">The emoji to prepend to the message.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="args">The arguments to format the message with.</param>
        public static void WarnWithEmoji(this Logger logger, string emoji, string message, params object[] args)
        {
            logger.Warn($"{emoji} {message}", args);
        }

        /// <summary>
        /// Logs a warning message with an emoji.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="emoji">The emoji to prepend to the message.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="args">The arguments to format the message with.</param>
        public static void WarnWithEmoji(this ILogger logger, string emoji, string message, params object[] args)
        {
            logger.Warn($"{emoji} {message}", args);
        }

        /// <summary>
        /// Logs an error message with an emoji.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="emoji">The emoji to prepend to the message.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="args">The arguments to format the message with.</param>
        public static void ErrorWithEmoji(this Logger logger, string emoji, string message, params object[] args)
        {
            logger.Error($"{emoji} {message}", args);
        }

        /// <summary>
        /// Logs an error message with an emoji.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="emoji">The emoji to prepend to the message.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="args">The arguments to format the message with.</param>
        public static void ErrorWithEmoji(this ILogger logger, string emoji, string message, params object[] args)
        {
            logger.Error($"{emoji} {message}", args);
        }

        /// <summary>
        /// Logs an error message with an emoji and exception.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="emoji">The emoji to prepend to the message.</param>
        /// <param name="ex">The exception to log.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="args">The arguments to format the message with.</param>
        public static void ErrorWithEmoji(this Logger logger, string emoji, Exception ex, string message, params object[] args)
        {
            logger.Error(ex, $"{emoji} {message}", args);
        }

        /// <summary>
        /// Logs an error message with an emoji and exception.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="emoji">The emoji to prepend to the message.</param>
        /// <param name="ex">The exception to log.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="args">The arguments to format the message with.</param>
        public static void ErrorWithEmoji(this ILogger logger, string emoji, Exception ex, string message, params object[] args)
        {
            logger.Error(ex, $"{emoji} {message}", args);
        }

        /// <summary>
        /// Logs a trace message with an emoji.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="emoji">The emoji to prepend to the message.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="args">The arguments to format the message with.</param>
        public static void TraceWithEmoji(this Logger logger, string emoji, string message, params object[] args)
        {
            logger.Trace($"{emoji} {message}", args);
        }

        /// <summary>
        /// Logs a trace message with an emoji.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="emoji">The emoji to prepend to the message.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="args">The arguments to format the message with.</param>
        public static void TraceWithEmoji(this ILogger logger, string emoji, string message, params object[] args)
        {
            logger.Trace($"{emoji} {message}", args);
        }

        /// <summary>
        /// Logs a fatal message with an emoji.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="emoji">The emoji to prepend to the message.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="args">The arguments to format the message with.</param>
        public static void FatalWithEmoji(this Logger logger, string emoji, string message, params object[] args)
        {
            logger.Fatal($"{emoji} {message}", args);
        }

        /// <summary>
        /// Logs a fatal message with an emoji.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="emoji">The emoji to prepend to the message.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="args">The arguments to format the message with.</param>
        public static void FatalWithEmoji(this ILogger logger, string emoji, string message, params object[] args)
        {
            logger.Fatal($"{emoji} {message}", args);
        }

        /// <summary>
        /// Logs a fatal message with an emoji and exception.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="emoji">The emoji to prepend to the message.</param>
        /// <param name="ex">The exception to log.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="args">The arguments to format the message with.</param>
        public static void FatalWithEmoji(this Logger logger, string emoji, Exception ex, string message, params object[] args)
        {
            logger.Fatal(ex, $"{emoji} {message}", args);
        }

        /// <summary>
        /// Logs a fatal message with an emoji and exception.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="emoji">The emoji to prepend to the message.</param>
        /// <param name="ex">The exception to log.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="args">The arguments to format the message with.</param>
        public static void FatalWithEmoji(this ILogger logger, string emoji, Exception ex, string message, params object[] args)
        {
            logger.Fatal(ex, $"{emoji} {message}", args);
        }

        /// <summary>
        /// Logs a rate limit message with standard format
        /// </summary>
        public static void LogRateLimit(this Logger logger, int currentRate, int maxRate, TimeSpan timeUntilReset)
        {
            string formattedTime = FormatTimeSpan(timeUntilReset);
            logger.InfoWithEmoji(LogEmojis.Wait, 
                $"Rate limit active: {currentRate}/{maxRate} per hour. Reset in {formattedTime}");
        }

        /// <summary>
        /// Logs a rate limit message with standard format
        /// </summary>
        public static void LogRateLimit(this ILogger logger, int currentRate, int maxRate, TimeSpan timeUntilReset)
        {
            string formattedTime = FormatTimeSpan(timeUntilReset);
            logger.InfoWithEmoji(LogEmojis.Wait, 
                $"Rate limit active: {currentRate}/{maxRate} per hour. Reset in {formattedTime}");
        }

        /// <summary>
        /// Logs a queue status update
        /// </summary>
        public static void LogQueueStatus(this Logger logger, int pendingCount, int completedCount, int failedCount, bool isActive)
        {
            string statusEmoji = isActive ? LogEmojis.Resume : LogEmojis.Pause;
            string statusText = isActive ? "Active" : "Paused";
            
            logger.InfoWithEmoji(LogEmojis.Queue, 
                $"Queue status: {statusEmoji} {statusText} - {pendingCount} pending, {completedCount} completed, {failedCount} failed");
        }

        /// <summary>
        /// Logs a queue status update
        /// </summary>
        public static void LogQueueStatus(this ILogger logger, int pendingCount, int completedCount, int failedCount, bool isActive)
        {
            string statusEmoji = isActive ? LogEmojis.Resume : LogEmojis.Pause;
            string statusText = isActive ? "Active" : "Paused";
            
            logger.InfoWithEmoji(LogEmojis.Queue, 
                $"Queue status: {statusEmoji} {statusText} - {pendingCount} pending, {completedCount} completed, {failedCount} failed");
        }

        /// <summary>
        /// Logs a download progress update
        /// </summary>
        public static void LogDownloadProgress(this Logger logger, string title, string artist, double progress, TimeSpan? estimatedTimeRemaining)
        {
            string timeRemaining = estimatedTimeRemaining.HasValue 
                ? $", {FormatTimeSpan(estimatedTimeRemaining.Value)} remaining" 
                : "";
            
            logger.InfoWithEmoji(LogEmojis.Download, 
                $"Downloading {artist} - {title}: {progress:F1}%{timeRemaining}");
        }

        /// <summary>
        /// Logs a download progress update
        /// </summary>
        public static void LogDownloadProgress(this ILogger logger, string title, string artist, double progress, TimeSpan? estimatedTimeRemaining)
        {
            string timeRemaining = estimatedTimeRemaining.HasValue 
                ? $", {FormatTimeSpan(estimatedTimeRemaining.Value)} remaining" 
                : "";
            
            logger.InfoWithEmoji(LogEmojis.Download, 
                $"Downloading {artist} - {title}: {progress:F1}%{timeRemaining}");
        }

        /// <summary>
        /// Formats a TimeSpan into a human-readable string
        /// </summary>
        public static string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalHours >= 1)
            {
                return $"{(int)timeSpan.TotalHours}h {timeSpan.Minutes}m";
            }
            else if (timeSpan.TotalMinutes >= 1)
            {
                return $"{timeSpan.Minutes}m {timeSpan.Seconds}s";
            }
            else
            {
                return $"{timeSpan.Seconds}s";
            }
        }
    }
}


