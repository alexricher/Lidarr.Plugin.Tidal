using System;

namespace NzbDrone.Plugin.Tidal.Constants
{
    /// <summary>
    /// Constants used throughout the Tidal plugin
    /// </summary>
    public static class TidalConstants
    {
        /// <summary>
        /// Default Tidal web API key
        /// </summary>
        public const string DefaultApiKey = "yU5qiQJ8dual5kkF";

        /// <summary>
        /// Default page size for Tidal API requests
        /// </summary>
        public const int DefaultPageSize = 100;

        /// <summary>
        /// Default maximum number of pages to retrieve
        /// </summary>
        public const int DefaultMaxPages = 3;

        /// <summary>
        /// Default maximum number of concurrent searches
        /// </summary>
        public const int DefaultMaxConcurrentSearches = 5;

        /// <summary>
        /// Default maximum requests per minute
        /// </summary>
        public const int DefaultMaxRequestsPerMinute = 30;

        #region Operation Timeouts

        /// <summary>
        /// Standard operation timeout for most operations
        /// </summary>
        public static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Timeout for initialization operations
        /// </summary>
        public static readonly TimeSpan InitTimeout = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Timeout for queue-related operations
        /// </summary>
        public static readonly TimeSpan QueueOperationTimeout = TimeSpan.FromSeconds(45);

        /// <summary>
        /// Default operation timeout for general operations
        /// </summary>
        public static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Timeout for semaphore acquisition
        /// </summary>
        public static readonly TimeSpan SemaphoreAcquisitionTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Search timeout for search operations
        /// </summary>
        public static readonly TimeSpan SearchTimeout = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Timeout for individual search queue items
        /// </summary>
        public static readonly TimeSpan SearchQueueItemTimeout = TimeSpan.FromSeconds(30);

        #endregion

        #region Database Timeouts

        /// <summary>
        /// Database operation timeout for high-priority operations
        /// </summary>
        public static readonly int HighPriorityDbTimeoutMs = 20000;

        /// <summary>
        /// Database operation timeout for low-priority operations
        /// </summary>
        public static readonly int LowPriorityDbTimeoutMs = 30000;

        /// <summary>
        /// Database operation timeout for queue item removal
        /// </summary>
        public static readonly int RemoveQueueItemTimeoutMs = 15000;

        #endregion

        #region Circuit Breaker Settings

        /// <summary>
        /// Duration to keep the circuit breaker open after a trip
        /// </summary>
        public static readonly TimeSpan CircuitBreakDuration = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Interval between status update log messages
        /// </summary>
        public static readonly TimeSpan StatusUpdateInterval = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Default circuit breaker failure threshold
        /// </summary>
        public const int DefaultCircuitBreakerFailureThreshold = 5;

        /// <summary>
        /// Default circuit breaker break duration
        /// </summary>
        public static readonly TimeSpan DefaultCircuitBreakerBreakDuration = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Default circuit breaker failure time window
        /// </summary>
        public static readonly TimeSpan DefaultCircuitBreakerFailureTimeWindow = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Default circuit breaker status update interval
        /// </summary>
        public static readonly TimeSpan DefaultCircuitBreakerStatusUpdateInterval = TimeSpan.FromMinutes(1);

        #endregion

        #region Health Monitoring

        /// <summary>
        /// Interval between health check operations
        /// </summary>
        public static readonly TimeSpan HealthCheckInterval = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Threshold to consider an operation stalled
        /// </summary>
        public static readonly TimeSpan StalledOperationThreshold = TimeSpan.FromMinutes(15);

        #endregion

        #region Concurrency Control

        /// <summary>
        /// Maximum number of concurrent operations
        /// </summary>
        public static readonly int MaxConcurrentOperations = 3;

        #endregion

        #region Rate Limiting

        /// <summary>
        /// Default time to wait between API requests
        /// </summary>
        public static readonly TimeSpan DefaultApiDelay = TimeSpan.FromMilliseconds(200);

        /// <summary>
        /// Default backoff time for rate limited requests
        /// </summary>
        public static readonly TimeSpan DefaultRateLimitBackoff = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Reset window for rate limit incidents
        /// </summary>
        public static readonly TimeSpan RateLimitResetWindow = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Default maximum rate limit incidents before circuit breaker trips
        /// </summary>
        public const int MaxRateLimitIncidents = 3;
        
        /// <summary>
        /// Default downloads per hour if not specified
        /// </summary>
        public const int DefaultDownloadsPerHour = 10;

        /// <summary>
        /// Maximum allowed downloads per hour - can be overridden by settings but values above
        /// this limit may increase the risk of rate limiting or detection by Tidal's systems
        /// </summary>
        public const int MaxDownloadsPerHour = 200;

        /// <summary>
        /// Maximum allowed requests per hour for indexer operations
        /// </summary>
        public const int MaxIndexerRequestsPerHour = 500;

        #endregion

        #region Throttling

        /// <summary>
        /// Time window for throttling repeated searches
        /// </summary>
        public static readonly TimeSpan SearchThrottleWindow = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Time window for cleaning up old search records
        /// </summary>
        public static readonly TimeSpan SearchRecordCleanupWindow = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Maximum number of search records to keep before aggressive cleanup
        /// </summary>
        public const int MaxSearchRecordsBeforeCleanup = 200;

        /// <summary>
        /// Short cleanup window for aggressive cleanup when too many search records
        /// </summary>
        public static readonly TimeSpan AggressiveSearchCleanupWindow = TimeSpan.FromMinutes(5);

        #endregion

        #region Queue Persistence

        /// <summary>
        /// Default interval for saving queue state to disk
        /// </summary>
        public static readonly TimeSpan QueuePersistenceInterval = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Default interval for saving queue state after changes
        /// </summary>
        public static readonly TimeSpan QueueChangesPersistenceDelay = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Default maximum retry count for failed downloads
        /// </summary>
        public const int DefaultDownloadRetryCount = 3;

        /// <summary>
        /// Default delay between retry attempts
        /// </summary>
        public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Default exponential backoff factor for retries
        /// </summary>
        public const double DefaultRetryBackoffFactor = 2.0;

        #endregion
    }
}
