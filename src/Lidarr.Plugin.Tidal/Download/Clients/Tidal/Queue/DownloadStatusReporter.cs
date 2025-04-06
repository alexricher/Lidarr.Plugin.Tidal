using System;
using System.Text;
using NLog;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients.Tidal.Interfaces;
using Lidarr.Plugin.Tidal.Services.Logging;

namespace NzbDrone.Core.Download.Clients.Tidal.Queue
{
    /// <summary>
    /// Generates status reports for Tidal download items
    /// </summary>
    public class DownloadStatusReporter
    {
        private readonly Logger _logger;

        public DownloadStatusReporter(Logger logger)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
        }

        /// <summary>
        /// Generates a detailed status report for a download item
        /// </summary>
        /// <param name="item">The download item to generate a report for</param>
        /// <returns>A formatted string containing the download status information</returns>
        public string GenerateStatusReport(IDownloadItem item)
        {
            if (item == null)
            {
                return $"{LogEmojis.Warning} No download item available";
            }

            try
            {
                var sb = new StringBuilder();
                
                // Basic information with appropriate emojis
                sb.AppendLine($"{LogEmojis.Info} Download ID: {item.ID}");
                sb.AppendLine($"{LogEmojis.Track} Title: {item.Title}");
                sb.AppendLine($"{LogEmojis.Artist} Artist: {item.Artist}");
                sb.AppendLine($"{LogEmojis.Album} Album: {item.Album}");
                
                // Status with appropriate emoji
                string statusEmoji = GetStatusEmoji(item.Status);
                sb.AppendLine($"{statusEmoji} Status: {item.Status}");
                
                // Progress with visual indicator
                sb.AppendLine($"{LogEmojis.Process} Progress: {item.Progress:F1}% {GenerateProgressBar(item.Progress, 20)}");
                
                // Track information
                if (item.TotalTracks > 0)
                {
                    sb.AppendLine($"{LogEmojis.Music} Tracks: {item.CompletedTracks}/{item.TotalTracks} completed");
                }
                
                // Size information
                if (item.TotalSize > 0)
                {
                    var downloadedMB = item.DownloadedSize / 1024.0 / 1024.0;
                    var totalMB = item.TotalSize / 1024.0 / 1024.0;
                    sb.AppendLine($"{LogEmojis.Data} Size: {downloadedMB:F2} MB / {totalMB:F2} MB");
                    
                    // Add download rate if available and downloading
                    if (item.Status == Interfaces.DownloadItemStatus.Downloading && 
                        item.DownloadedSize > 0 && 
                        item.StartTime != DateTime.MinValue &&
                        DateTime.UtcNow > item.StartTime)
                    {
                        double elapsedSeconds = (DateTime.UtcNow - item.StartTime).TotalSeconds;
                        if (elapsedSeconds > 0)
                        {
                            double bytesPerSecond = item.DownloadedSize / elapsedSeconds;
                            double kbps = bytesPerSecond * 8 / 1024;
                            sb.AppendLine($"{LogEmojis.Performance} Speed: {kbps:F1} kbit/s");
                        }
                    }
                }
                
                // Time remaining - only for downloading items
                if (item.Status == Interfaces.DownloadItemStatus.Downloading && 
                    item.EstimatedTimeRemaining.HasValue && 
                    item.EstimatedTimeRemaining.Value.TotalSeconds > 0)
                {
                    sb.AppendLine($"{LogEmojis.Time} Estimated time remaining: {FormatTimeSpan(item.EstimatedTimeRemaining.Value)}");
                }
                
                // Error information for failed items
                if (item.Status == Interfaces.DownloadItemStatus.Failed && !string.IsNullOrEmpty(item.LastErrorMessage))
                {
                    sb.AppendLine($"{LogEmojis.Error} Error: {item.LastErrorMessage}");
                }
                
                // Failed tracks information
                if (item.FailedTracks != null && item.FailedTracks.Length > 0)
                {
                    sb.AppendLine($"{LogEmojis.Warning} Failed tracks: {item.FailedTracks.Length}");
                    
                    // Add up to 5 failed track numbers for detail, more gets truncated
                    if (item.FailedTracks.Length <= 5)
                    {
                        sb.AppendLine($"{LogEmojis.Track} Track numbers: {string.Join(", ", item.FailedTracks)}");
                    }
                    else
                    {
                        sb.AppendLine($"{LogEmojis.Track} Track numbers: {string.Join(", ", item.FailedTracks[..5])} and {item.FailedTracks.Length - 5} more");
                    }
                }
                
                // Timing information
                sb.AppendLine($"{LogEmojis.Schedule} Queued: {item.QueuedTime}");
                
                if (item.StartTime != DateTime.MinValue)
                {
                    sb.AppendLine($"{LogEmojis.Start} Started: {item.StartTime}");
                    
                    // Add elapsed time for ongoing downloads
                    if (item.Status == Interfaces.DownloadItemStatus.Downloading && !item.EndTime.HasValue)
                    {
                        TimeSpan elapsed = DateTime.UtcNow - item.StartTime;
                        sb.AppendLine($"{LogEmojis.Time} Elapsed: {FormatTimeSpan(elapsed)}");
                    }
                }
                
                if (item.EndTime.HasValue)
                {
                    string endEmoji = item.Status == Interfaces.DownloadItemStatus.Failed ? LogEmojis.Error : LogEmojis.Complete;
                    sb.AppendLine($"{endEmoji} Completed: {item.EndTime}");
                    
                    // Add download duration
                    if (item.StartTime != DateTime.MinValue)
                    {
                        TimeSpan duration = item.EndTime.Value - item.StartTime;
                        sb.AppendLine($"{LogEmojis.Time} Duration: {FormatTimeSpan(duration)}");
                    }
                }
                
                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"{LogEmojis.Error} Error generating status report");
                return $"{LogEmojis.Error} Error generating status report: {ex.Message}";
            }
        }
        
        /// <summary>
        /// Gets an appropriate emoji for the download status
        /// </summary>
        private string GetStatusEmoji(Interfaces.DownloadItemStatus status)
        {
            return status switch
            {
                Interfaces.DownloadItemStatus.Queued => LogEmojis.Queue,
                Interfaces.DownloadItemStatus.Downloading => LogEmojis.Download,
                Interfaces.DownloadItemStatus.Completed => LogEmojis.Success,
                Interfaces.DownloadItemStatus.Failed => LogEmojis.Error,
                Interfaces.DownloadItemStatus.Paused => LogEmojis.Pause,
                Interfaces.DownloadItemStatus.Cancelled => LogEmojis.Cancel,
                _ => LogEmojis.Info
            };
        }

        /// <summary>
        /// Generate a text-based progress bar
        /// </summary>
        private string GenerateProgressBar(double percentage, int length)
        {
            int filledLength = (int)Math.Round(length * percentage / 100);
            return $"[{new string('█', filledLength)}{new string('░', length - filledLength)}]";
        }
        
        /// <summary>
        /// Formats a TimeSpan into a human-readable string
        /// </summary>
        /// <param name="timeSpan">The TimeSpan to format</param>
        /// <returns>A formatted string representation of the TimeSpan</returns>
        private string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalHours >= 1)
            {
                return $"{(int)timeSpan.TotalHours}h {timeSpan.Minutes}m {timeSpan.Seconds}s";
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



