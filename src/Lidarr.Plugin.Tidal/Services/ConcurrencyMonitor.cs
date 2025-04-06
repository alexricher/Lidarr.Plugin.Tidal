using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Download.Clients.Tidal;
using Lidarr.Plugin.Tidal.Services.Logging;

namespace Lidarr.Plugin.Tidal.Services
{
    /// <summary>
    /// Monitors overall system concurrency and applies throttling when needed
    /// to prevent file corruption and other issues during high load.
    /// </summary>
    public static class ConcurrencyMonitor
    {
        // Singleton static instance pattern
        private static readonly Lazy<ConcurrencyStats> _stats = new Lazy<ConcurrencyStats>(() => new ConcurrencyStats());
        private static Logger _logger;
        
        // Configurable values
        private static int _maxConcurrentFileOperations = 2;
        private static double _loadThresholdForThrottling = 0.8; // 80%
        
        // Threshold timings
        private static readonly TimeSpan _throttlingRecoveryTime = TimeSpan.FromSeconds(15);
        private static DateTime _lastThrottling = DateTime.MinValue;
        
        /// <summary>
        /// Initialize the concurrency monitor with settings and logger
        /// </summary>
        public static void Initialize(TidalSettings settings, Logger logger)
        {
            _logger = logger;
            
            // Set the max operations from settings
            _maxConcurrentFileOperations = settings.MaxConcurrentDownloads;
            
            // Log initialization
            _logger?.Debug("[TIDAL] ConcurrencyMonitor initialized with max operations = {0}", _maxConcurrentFileOperations);
        }
        
        /// <summary>
        /// Record the start of a file operation
        /// </summary>
        public static void RecordOperationStart(string operationType)
        {
            _stats.Value.RecordOperationStart(operationType);
            LogCurrentStats();
        }
        
        /// <summary>
        /// Record the end of a file operation
        /// </summary>
        public static void RecordOperationEnd(string operationType)
        {
            _stats.Value.RecordOperationEnd(operationType);
        }
        
        /// <summary>
        /// Check if the system is currently under high load
        /// </summary>
        public static bool IsHighLoad()
        {
            // Calculate load factor
            double loadFactor = _stats.Value.GetLoadFactor(_maxConcurrentFileOperations);
            
            // High load if over threshold
            return loadFactor >= _loadThresholdForThrottling;
        }
        
        /// <summary>
        /// Whether operations should be throttled based on current load
        /// </summary>
        public static bool ShouldThrottle()
        {
            // If we recently throttled, keep throttling for a bit to recover
            if (DateTime.UtcNow - _lastThrottling < _throttlingRecoveryTime)
            {
                return true;
            }
            
            bool highLoad = IsHighLoad();
            if (highLoad)
            {
                _lastThrottling = DateTime.UtcNow;
                LogCurrentStats();
            }
            
            return highLoad;
        }
        
        /// <summary>
        /// Apply throttling delay based on current load
        /// </summary>
        public static async Task ApplyThrottlingDelay(CancellationToken cancellationToken)
        {
            if (ShouldThrottle())
            {
                // Calculate delay based on load
                double loadFactor = _stats.Value.GetLoadFactor(_maxConcurrentFileOperations);
                int delayMs = (int)(1000 * Math.Min(5, Math.Max(1, loadFactor * 2)));
                
                _logger?.Debug("[TIDAL] Applying throttling delay of {0}ms due to high load (load factor: {1:F2})", 
                    delayMs, loadFactor);
                
                await Task.Delay(delayMs, cancellationToken);
            }
        }
        
        /// <summary>
        /// Log current concurrency statistics
        /// </summary>
        private static void LogCurrentStats()
        {
            var stats = _stats.Value;
            double loadFactor = stats.GetLoadFactor(_maxConcurrentFileOperations);
            
            _logger?.Debug("[TIDAL] Current concurrency stats: Active operations: {0}, Load factor: {1:F2}", 
                stats.GetActiveOperationsCount(),
                loadFactor);
        }
        
        /// <summary>
        /// Thread-safe class to track concurrency statistics
        /// </summary>
        private class ConcurrencyStats
        {
            // Track active operations by type
            private readonly ConcurrentDictionary<string, int> _activeOperations = new ConcurrentDictionary<string, int>();
            
            // Overall total
            private int _totalActiveOperations = 0;
            
            /// <summary>
            /// Record start of an operation
            /// </summary>
            public void RecordOperationStart(string operationType)
            {
                _activeOperations.AddOrUpdate(
                    operationType,
                    1,
                    (key, oldValue) => oldValue + 1);
                
                Interlocked.Increment(ref _totalActiveOperations);
            }
            
            /// <summary>
            /// Record end of an operation
            /// </summary>
            public void RecordOperationEnd(string operationType)
            {
                _activeOperations.AddOrUpdate(
                    operationType,
                    0,
                    (key, oldValue) => Math.Max(0, oldValue - 1));
                
                Interlocked.Decrement(ref _totalActiveOperations);
            }
            
            /// <summary>
            /// Get active operation count
            /// </summary>
            public int GetActiveOperationsCount() => _totalActiveOperations;
            
            /// <summary>
            /// Get load factor based on max operations
            /// </summary>
            public double GetLoadFactor(int maxOperations)
            {
                if (maxOperations <= 0) return 0;
                return (double)_totalActiveOperations / maxOperations;
            }
        }
    }
} 