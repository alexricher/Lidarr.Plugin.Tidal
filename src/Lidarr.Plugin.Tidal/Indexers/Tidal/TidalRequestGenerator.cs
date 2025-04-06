using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Plugin.Tidal;
using System.Text;
using System.Net;
using System.Linq;
using Lidarr.Plugin.Tidal.Services.CircuitBreaker;
using Lidarr.Plugin.Tidal.Services.Logging;

namespace NzbDrone.Core.Indexers.Tidal
{
    public class TidalRequestGenerator : IIndexerRequestGenerator
    {
        private const int DEFAULT_PAGE_SIZE = 100;
        private const int DEFAULT_MAX_PAGES = 3;
        private const int DEFAULT_MAX_CONCURRENT_REQUESTS = 5;

        // Use the proper CircuitBreaker implementation
        private static readonly ICircuitBreaker _searchCircuitBreaker;

        // Static constructor to initialize the circuit breaker
        static TidalRequestGenerator()
        {
            // Create a circuit breaker with appropriate settings for search timeouts
            var settings = new CircuitBreakerSettings
            {
                Name = "TidalSearch",
                FailureThreshold = 5,
                BreakDuration = TimeSpan.FromMinutes(5),
                FailureTimeWindow = TimeSpan.FromMinutes(10),
                StatusUpdateInterval = TimeSpan.FromMinutes(1)
            };

            _searchCircuitBreaker = new CircuitBreaker(LogManager.GetLogger("TidalSearchCircuitBreaker"), settings);
        }

        public TidalIndexerSettings Settings { get; set; }
        public Logger Logger { get; set; }

        private readonly ITidalRateLimitService _rateLimitService;

        public IHttpRequestInterceptor HttpCustomizer { get; private set; }

        public TidalRequestGenerator(ITidalRateLimitService rateLimitService)
        {
            _rateLimitService = rateLimitService;
        }

        public virtual IndexerPageableRequestChain GetRecentRequests()
        {
            Logger.Debug("🔍 Getting recent requests for Tidal indexer");

            // this is a lazy implementation, just here so that lidarr has something to test against when saving settings
            var pageableRequests = new IndexerPageableRequestChain();
            pageableRequests.Add(GetRequestsAsync("never gonna give you up", CancellationToken.None).GetAwaiter().GetResult());

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(AlbumSearchCriteria searchCriteria)
        {
            var searchQuery = $"{searchCriteria.ArtistQuery} {searchCriteria.AlbumQuery}";
            Logger.Info($"🔍 Creating album search request: '{searchQuery}'");

            var chain = new IndexerPageableRequestChain();
            chain.AddTier(GetRequestsAsync(searchQuery, CancellationToken.None).GetAwaiter().GetResult());

            return chain;
        }

        public IndexerPageableRequestChain GetSearchRequests(ArtistSearchCriteria searchCriteria)
        {
            Logger.Info($"🔍 Creating artist search request: '{searchCriteria.ArtistQuery}'");

            var chain = new IndexerPageableRequestChain();
            chain.AddTier(GetRequestsAsync(searchCriteria.ArtistQuery, CancellationToken.None).GetAwaiter().GetResult());

            return chain;
        }

        private async Task<IEnumerable<IndexerRequest>> GetRequestsAsync(string searchParameters, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(searchParameters))
            {
                Logger?.Warn("Received empty search parameters");
                return new List<IndexerRequest>(); // Return empty list
            }

            // Check if circuit breaker is open before proceeding
            if (_searchCircuitBreaker.IsOpen())
            {
                var reopenTime = _searchCircuitBreaker.GetReopenTime();
                Logger?.Warn($"{LogEmojis.CircuitOpen} Circuit breaker for Tidal searches is OPEN. Searches temporarily disabled for {reopenTime.TotalMinutes:0.0} more minutes.");
                return new List<IndexerRequest>(); // Return empty list when circuit breaker is open
            }

            // Initialize rate limiters if not already done
            try
            {
                // Safely initialize rate limiters
                var maxConcurrentSearches = Settings?.MaxConcurrentSearches ?? DEFAULT_MAX_CONCURRENT_REQUESTS;
                var maxRequestsPerMinute = Settings?.MaxRequestsPerMinute ?? 50;

                Logger?.Debug($"🔧 Initializing rate limiters: MaxConcurrentSearches={maxConcurrentSearches}, MaxRequestsPerMinute={maxRequestsPerMinute}");

                // Ensure rate limit service is available
                if (_rateLimitService == null)
                {
                    Logger?.Error("Rate limit service is null - this is a dependency injection issue");
                    return new List<IndexerRequest>(); // Return empty list
                }

                _rateLimitService.Initialize(maxConcurrentSearches, maxRequestsPerMinute);
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "Failed to initialize rate limiters");
                // Continue regardless of initialization error - service will use default values
            }

            Logger?.Debug($"🎵 Beginning Tidal search for '{searchParameters}'");

            // Create a list to hold our requests
            var requests = new List<IndexerRequest>();

            // Only acquire the semaphore once for the entire operation
            bool semaphoreAcquired = false;
            try
            {
                // We'll use a timeout to avoid hanging forever
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(90), cancellationToken);

                // Log that we're waiting for the semaphore with detailed concurrency info
                int waitingCurrentCount = _rateLimitService.CurrentCount;
                int waitingActiveSearches = _rateLimitService.CurrentRequestCount;
                int waitingMaxConcurrentSearches = Settings?.MaxConcurrentSearches ?? DEFAULT_MAX_CONCURRENT_REQUESTS;
                Logger?.Info($"⏳ Waiting for rate limit slot to search: {searchParameters} (active searches: {waitingActiveSearches}/{waitingMaxConcurrentSearches}, available slots: {waitingCurrentCount})");
                var waitTask = _rateLimitService.WaitForSlot(cancellationToken);

                var completedTask = await Task.WhenAny(waitTask, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    // Timeout message
                    Logger?.Warn($"Timed out after 90 seconds waiting for rate limit semaphore. Search will be skipped: {searchParameters}");

                    // Record the timeout with circuit breaker
                    _searchCircuitBreaker.RecordFailure(new TimeoutException($"Search timeout for: {searchParameters}"));

                    return requests; // Return empty list on timeout
                }

                // If we get here, we acquired the semaphore
                semaphoreAcquired = true;

                // Record success with circuit breaker
                _searchCircuitBreaker.RecordSuccess();

                // Log detailed concurrency info after acquiring the semaphore
                int acquiredCurrentCount = _rateLimitService.CurrentCount;
                int acquiredActiveSearches = _rateLimitService.CurrentRequestCount;
                int acquiredMaxConcurrentSearches = Settings?.MaxConcurrentSearches ?? DEFAULT_MAX_CONCURRENT_REQUESTS;
                Logger?.Info($"✅ Search semaphore acquired for: {searchParameters} (active searches: {acquiredActiveSearches}/{acquiredMaxConcurrentSearches}, available slots: {acquiredCurrentCount})");

                try
                {
                    // First check if TidalAPI is properly initialized
                    if (TidalAPI.Instance == null)
                    {
                        Logger?.Error("TidalAPI.Instance is null - Tidal API is not initialized");
                        return requests;
                    }

                    if (TidalAPI.Instance.Client == null)
                    {
                        Logger?.Error("TidalAPI.Instance.Client is null - Tidal client is not initialized");
                        return requests;
                    }

                    if (TidalAPI.Instance.Client.ActiveUser == null)
                    {
                        Logger?.Error("TidalAPI.Instance.Client.ActiveUser is null - Not logged in to Tidal");
                        return requests;
                    }

                    // CRITICAL FIX: Don't wait for token refresh if we hold the semaphore
                    // Instead, run a quick check and skip refresh if it might block
                    bool needsRefresh = false;
                    try
                    {
                        // Only refresh if token is definitely expired (within 5 min of expiry)
                        if (TidalAPI.Instance.Client.ActiveUser.ExpirationDate != DateTime.MinValue)
                        {
                            needsRefresh = DateTime.UtcNow.AddMinutes(5) > TidalAPI.Instance.Client.ActiveUser.ExpirationDate;
                            if (needsRefresh)
                            {
                                // IMPROVED: Instead of just skipping, initiate a non-blocking background token refresh
                                Logger?.Info($"Token expires soon ({TidalAPI.Instance.Client.ActiveUser.ExpirationDate}), initiating background refresh");

                                // Start background refresh without awaiting it - this will overlap with the search
                                // but won't block the critical path
                                _ = Task.Run(async () => {
                                    try
                                    {
                                        // Release our semaphore during token refresh to avoid deadlocks
                                        if (semaphoreAcquired && _rateLimitService != null)
                                        {
                                            try
                                            {
                                                _rateLimitService.Release();
                                                Logger?.Debug("✅ Temporarily released search semaphore for token refresh");
                                                semaphoreAcquired = false;
                                            }
                                            catch (Exception ex)
                                            {
                                                Logger?.Error(ex, "Error releasing search semaphore for token refresh");
                                            }
                                        }

                                        // Attempt token refresh
                                        Logger?.Debug("🔑 Beginning background token refresh");
                                        await TidalAPI.Instance.Client.ForceRefreshToken();
                                        Logger?.Info("🔑 Successfully refreshed Tidal token in background");
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger?.Error(ex, "Background token refresh failed");
                                    }
                                });

                                Logger?.Debug("🔑 Continuing with search while token refreshes in background");
                                // Continue with existing token for current search - it might still work
                                // and the background task will refresh it for future searches
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger?.Warn(ex, "Error checking token expiration - continuing anyway");
                    }

                    // Create requests for each page
                    var maxPages = Settings?.MaxPages ?? DEFAULT_MAX_PAGES;
                    var pageSize = Settings?.PageSize ?? DEFAULT_PAGE_SIZE;

                    Logger?.Debug($"⚙️ Search parameters: MaxPages={maxPages}, PageSize={pageSize}");

                    for (var page = 0; page < maxPages; page++)
                    {
                        // CRITICAL FIX: Check cancellation between pages
                        if (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        var data = new Dictionary<string, string>()
                        {
                            ["query"] = searchParameters,
                            ["limit"] = $"{pageSize}",
                            ["types"] = "albums,tracks",
                            ["offset"] = $"{page * pageSize}"
                        };

                        // Use the correct property name PreferredCountryCode instead of CountryCode
                        var countryCode = Settings?.PreferredCountryCode;
                        if (!string.IsNullOrEmpty(countryCode))
                        {
                            Logger?.Debug($"🌍 Using country code: {countryCode}");
                            data["countryCode"] = countryCode;
                        }

                        // Build the URL with parameters - don't use ApiBaseUrl which doesn't exist
                        string urlBase = "https://api.tidal.com/v1";
                        var endpoint = $"{urlBase}/search";

                        // Create the query string manually without using Select
                        var queryParams = string.Join("&", data.Keys
                            .Select(key => $"{WebUtility.UrlEncode(key)}={WebUtility.UrlEncode(data[key])}"));
                        var url = $"{endpoint}?{queryParams}";

                        var httpRequest = new HttpRequest(url, HttpAccept.Json);

                        // Add authorization
                        if (TidalAPI.Instance?.Client?.ActiveUser != null)
                        {
                            string token = TidalAPI.Instance.Client.ActiveUser.AccessToken;
                            if (!string.IsNullOrEmpty(token))
                            {
                                httpRequest.Headers.Add("Authorization", $"Bearer {token}");
                            }
                            else
                            {
                                Logger?.Error("Access token is null or empty - cannot add authorization header");
                            }
                        }

                        // Add other required headers - don't use ApiKey which doesn't exist
                        string apiKey = "yU5qiQJ8dual5kkF"; // Default Tidal web API key
                        httpRequest.Headers.Add("X-Tidal-Token", apiKey);

                        // Use Customize instead of In for HttpCustomizer
                        if (HttpCustomizer != null)
                        {
                            HttpCustomizer.PreRequest(httpRequest);
                        }

                        Logger?.Debug($"📄 Created request for page {page+1}: {url}");
                        requests.Add(new IndexerRequest(httpRequest));
                    }

                    Logger?.Info($"✅ Completed all search requests for '{searchParameters}' - {requests.Count} requests created (PageSize={pageSize}, MaxPages={maxPages}, SearchTypes=albums,tracks)");
                }
                catch (Exception ex)
                {
                    _searchCircuitBreaker.RecordFailure(ex);
                    Logger?.Error(ex, $"Error creating search requests for '{searchParameters}': {ex.Message}");
                }
            }
            catch (OperationCanceledException)
            {
                Logger?.Debug($"Search for '{searchParameters}' was cancelled");
                throw; // Propagate cancellation
            }
            catch (Exception ex)
            {
                // Record any unexpected errors with circuit breaker
                _searchCircuitBreaker.RecordFailure(ex);
                Logger?.Error(ex, $"Unexpected error during search setup for '{searchParameters}': {ex.Message}");
            }
            finally
            {
                // Always release the semaphore if we acquired it
                if (semaphoreAcquired && _rateLimitService != null)
                {
                    try
                    {
                        _rateLimitService.Release();
                        // Log detailed concurrency info after releasing the semaphore
                        int releasedCurrentCount = _rateLimitService.CurrentCount;
                        int releasedActiveSearches = _rateLimitService.CurrentRequestCount;
                        int releasedMaxConcurrentSearches = Settings?.MaxConcurrentSearches ?? DEFAULT_MAX_CONCURRENT_REQUESTS;
                        Logger?.Info($"✅ Released search semaphore for: {searchParameters} (active searches: {releasedActiveSearches}/{releasedMaxConcurrentSearches}, available slots: {releasedCurrentCount})");
                    }
                    catch (Exception ex)
                    {
                        Logger?.Error(ex, "Error releasing search semaphore");
                    }
                }
            }

            return requests;
        }
    }
}
