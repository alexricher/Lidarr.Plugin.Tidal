using System;

namespace NzbDrone.Core.Download.Clients.Tidal.Utilities
{
    /// <summary>
    /// Constants used throughout the Tidal integration
    /// </summary>
    public static class TidalConstants
    {
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
    }
}