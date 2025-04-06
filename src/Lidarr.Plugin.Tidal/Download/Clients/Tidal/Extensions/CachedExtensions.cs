using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Common.Cache;

namespace NzbDrone.Core.Download.Clients.Tidal.Extensions
{
    /// <summary>
    /// Extension methods for the ICached interface to provide async functionality.
    /// </summary>
    public static class CachedExtensions
    {
        // Thread-safe dictionary to hold locks for specific cache keys
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _cacheLocks = 
            new ConcurrentDictionary<string, SemaphoreSlim>();

        /// <summary>
        /// Gets a value from the cache asynchronously, creating it if it doesn't exist using the provided function.
        /// This method ensures that only one thread can create a value for a given key at a time.
        /// </summary>
        /// <typeparam name="T">The type of value stored in the cache.</typeparam>
        /// <param name="cache">The cache instance.</param>
        /// <param name="key">The cache key.</param>
        /// <param name="createFunc">The async function to create the value if it doesn't exist.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <param name="lifeTime">Optional lifetime for the cache entry.</param>
        /// <returns>The cached value.</returns>
        public static async Task<T> GetOrAddAsync<T>(
            this ICached<T> cache, 
            string key, 
            Func<Task<T>> createFunc, 
            CancellationToken cancellationToken = default,
            TimeSpan? lifeTime = null)
        {
            // First try to get the value without any locking
            var result = cache.Find(key);
            if (result != null && !EqualityComparer<T>.Default.Equals(result, default(T)))
            {
                return result;
            }

            // Get or create a lock for this specific key
            var keyLock = _cacheLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

            try
            {
                // Wait for exclusive access
                await keyLock.WaitAsync(cancellationToken).ConfigureAwait(false);

                // Check again in case another thread created the value while we were waiting
                result = cache.Find(key);
                if (result != null && !EqualityComparer<T>.Default.Equals(result, default(T)))
                {
                    return result;
                }

                // Create the value
                result = await createFunc().ConfigureAwait(false);

                // Cache it
                cache.Set(key, result, lifeTime);

                return result;
            }
            finally
            {
                // Release the lock
                keyLock.Release();

                // Clean up the lock if we're the last user
                if (keyLock.CurrentCount == 1)
                {
                    // Try to remove the lock from the dictionary 
                    // (it's okay if this fails, it just means another thread is using it)
                    _cacheLocks.TryRemove(key, out _);
                }
            }
        }
    }
} 