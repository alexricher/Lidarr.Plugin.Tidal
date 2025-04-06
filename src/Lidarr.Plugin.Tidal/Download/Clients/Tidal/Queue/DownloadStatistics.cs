using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using Lidarr.Plugin.Tidal.Services.Logging;

namespace NzbDrone.Core.Download.Clients.Tidal.Queue
{
    /// <summary>
    /// Tracks and reports download statistics for the download queue
    /// </summary>
    public class DownloadStatistics : IDisposable
    {
        private readonly Logger _logger;
        private readonly List<DateTime> _recentDownloads = new List<DateTime>();
        private DateTime _lastStatsLogTime = DateTime.MinValue;
        private readonly TimeSpan _statsLogInterval;
        private readonly string _serviceName;

        /// <summary>
        /// Total number of items that have been queued
        /// </summary>
        public int TotalItemsQueued { get; private set; }

        /// <summary>
        /// Total number of items that have been processed
        /// </summary>
        public int TotalItemsProcessed { get; private set; }

        /// <summary>
        /// Number of successful downloads
        /// </summary>
        public int SuccessfulDownloads { get; private set; }

        /// <summary>
        /// Number of failed downloads
        /// </summary>
        public int FailedDownloads { get; private set; }

        /// <summary>
        /// Current download rate per hour
        /// </summary>
        public int DownloadRatePerHour { get; private set; }

        /// <summary>
        /// Whether the current rate is limited
        /// </summary>
        public bool IsRateLimited { get; private set; }

        /// <summary>
        /// Initializes a new instance of the DownloadStatistics class
        /// </summary>
        /// <param name="statsLogInterval">Interval for logging statistics</param>
        /// <param name="serviceName">Name of the service for logging</param>
        /// <param name="logger">Logger for diagnostic messages</param>
        public DownloadStatistics(TimeSpan statsLogInterval, string serviceName, Logger logger)
        {
            _statsLogInterval = statsLogInterval;
            _serviceName = serviceName;
            _logger = logger;
        }

        /// <summary>
        /// Records a successful download
        /// </summary>
        public void RecordDownload()
        {
            _recentDownloads.Add(DateTime.UtcNow);
            SuccessfulDownloads++;
            TotalItemsProcessed++;
        }

        /// <summary>
        /// Records a failed download
        /// </summary>
        public void RecordFailure()
        {
            FailedDownloads++;
            TotalItemsProcessed++;
        }

        /// <summary>
        /// Records a new item being queued
        /// </summary>
        public void RecordItemQueued()
        {
            TotalItemsQueued++;
        }

        /// <summary>
        /// Updates the current download rate based on recent downloads
        /// </summary>
        /// <param name="maxDownloadsPerHour">Maximum downloads per hour from settings</param>
        public void UpdateDownloadRate(int maxDownloadsPerHour)
        {
            // Remove downloads older than 1 hour
            var cutoff = DateTime.UtcNow.AddHours(-1);
            _recentDownloads.RemoveAll(d => d < cutoff);

            // Calculate current download rate per hour
            DownloadRatePerHour = _recentDownloads.Count;

            // Check if we're currently rate limited
            bool wasRateLimited = IsRateLimited;
            IsRateLimited = (maxDownloadsPerHour > 0 && DownloadRatePerHour >= maxDownloadsPerHour);

            // Log when rate limit status changes
            if (wasRateLimited != IsRateLimited)
            {
                if (IsRateLimited)
                {
                    _logger?.WarnWithEmoji(LogEmojis.Warning, $"[{_serviceName}] Rate limit reached: {DownloadRatePerHour}/{maxDownloadsPerHour} tracks/hr");
                }
                else
                {
                    _logger?.InfoWithEmoji(LogEmojis.Success, $"[{_serviceName}] Rate limit no longer active: {DownloadRatePerHour}/{maxDownloadsPerHour} tracks/hr");
                }
            }
        }

        /// <summary>
        /// Logs current queue statistics
        /// </summary>
        /// <param name="queueCount">Current number of items in the queue</param>
        /// <param name="maxDownloadsPerHour">Maximum downloads per hour from settings</param>
        public void LogStatistics(int queueCount, int maxDownloadsPerHour)
        {
            if (_logger == null)
                return;

            // Only log if it's time to log again
            if ((DateTime.UtcNow - _lastStatsLogTime) <= _statsLogInterval)
                return;

            _lastStatsLogTime = DateTime.UtcNow;

            // Calculate success rate
            double successRate = TotalItemsProcessed > 0
                ? (SuccessfulDownloads / (double)TotalItemsProcessed) * 100
                : 0;

            // Calculate rate limit percentage
            int percentOfLimit = maxDownloadsPerHour > 0
                ? (int)((DownloadRatePerHour / (float)maxDownloadsPerHour) * 100)
                : 0;

            // Log queue statistics
            _logger.InfoWithEmoji(LogEmojis.Stats,
                $"[{_serviceName}] Queue stats: {queueCount} items waiting, " +
                $"{SuccessfulDownloads} completed, {FailedDownloads} failed, " +
                $"{successRate:F1}% success rate");

            // Log rate information
            string rateInfo = maxDownloadsPerHour > 0
                ? $"{DownloadRatePerHour}/{maxDownloadsPerHour} tracks/hr ({percentOfLimit}%)"
                : $"{DownloadRatePerHour} tracks/hr (unlimited)";

            _logger.InfoWithEmoji(LogEmojis.Download,
                $"[{_serviceName}] Current rate: {rateInfo}");

            // Log throttle status if rate limited
            if (IsRateLimited && maxDownloadsPerHour > 0)
            {
                var oldestDownload = _recentDownloads.OrderBy(d => d).FirstOrDefault();
                if (oldestDownload != default)
                {
                    var timeUntilUnderLimit = oldestDownload.AddHours(1) - DateTime.UtcNow;
                    string throttleStatus = $"Rate limited: {DownloadRatePerHour}/{maxDownloadsPerHour} tracks/hr, resume in " +
                        (timeUntilUnderLimit.TotalMinutes > 60
                            ? $"{(int)timeUntilUnderLimit.TotalHours}h {timeUntilUnderLimit.Minutes}m"
                            : $"{timeUntilUnderLimit.Minutes}m {timeUntilUnderLimit.Seconds}s");

                    _logger.InfoWithEmoji(LogEmojis.Wait, $"[{_serviceName}] {throttleStatus}");
                }
            }
        }

        /// <summary>
        /// Disposes resources used by the download statistics
        /// </summary>
        public void Dispose()
        {
            try
            {
                // Clear the recent downloads list
                _recentDownloads.Clear();

                _logger?.Debug("Download statistics disposed");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error disposing download statistics");
            }
        }
    }
}
