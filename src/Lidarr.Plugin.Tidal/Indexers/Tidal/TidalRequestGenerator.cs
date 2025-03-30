using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Plugin.Tidal;
using System.Text;

namespace NzbDrone.Core.Indexers.Tidal
{
    public class TidalRequestGenerator : IIndexerRequestGenerator
    {
        private const int DEFAULT_PAGE_SIZE = 100;
        private const int DEFAULT_MAX_PAGES = 3;
        private const int DEFAULT_MAX_CONCURRENT_REQUESTS = 5;
        
        public TidalIndexerSettings Settings { get; set; }
        public Logger Logger { get; set; }
        
        private SemaphoreSlim _searchSemaphore;
        private RateLimiter _rateLimiter;
        
        public IHttpRequestInterceptor HttpCustomizer { get; private set; }

        // Inside the constructor or if there's no constructor, add one 
        public TidalRequestGenerator()
        {
            // No initialization needed here anymore
        }
        
        // Initialize rate limiters based on settings
        private void InitializeRateLimiters()
        {
            try
            {
                var maxConcurrentSearches = Settings?.MaxConcurrentSearches ?? DEFAULT_MAX_CONCURRENT_REQUESTS;
                var maxRequestsPerMinute = Settings?.MaxRequestsPerMinute ?? 50;
                
                Logger?.Debug($"🔧 Initializing rate limiters: MaxConcurrentSearches={maxConcurrentSearches}, MaxRequestsPerMinute={maxRequestsPerMinute}");
                
                _searchSemaphore = new SemaphoreSlim(maxConcurrentSearches, maxConcurrentSearches);
                _rateLimiter = new RateLimiter(maxRequestsPerMinute, TimeSpan.FromMinutes(1));
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, "Failed to initialize rate limiters");
                
                // Create defaults as fallback
                _searchSemaphore = new SemaphoreSlim(DEFAULT_MAX_CONCURRENT_REQUESTS, DEFAULT_MAX_CONCURRENT_REQUESTS);
                _rateLimiter = new RateLimiter(50, TimeSpan.FromMinutes(1));
            }
        }

        private class RateLimiter
        {
            private readonly Queue<DateTime> _requestTimestamps = new Queue<DateTime>();
            private readonly int _maxRequests;
            private readonly TimeSpan _interval;
            private readonly object _lock = new object();

            public RateLimiter(int maxRequests, TimeSpan interval)
            {
                _maxRequests = maxRequests;
                _interval = interval;
            }

            public async Task WaitForSlot()
            {
                int waitCount = 0;
                DateTime startWait = DateTime.UtcNow;
                
                while (true)
                {
                    lock (_lock)
                    {
                        // Remove timestamps outside the window
                        while (_requestTimestamps.Count > 0 && 
                               DateTime.UtcNow - _requestTimestamps.Peek() > _interval)
                        {
                            _requestTimestamps.Dequeue();
                        }

                        // If we have room, add timestamp and proceed
                        if (_requestTimestamps.Count < _maxRequests)
                        {
                            _requestTimestamps.Enqueue(DateTime.UtcNow);
                            return;
                        }
                    }
                    
                    waitCount++;
                    if (waitCount % 10 == 0) // Log every ~1 second of waiting
                    {
                        var waitTime = DateTime.UtcNow - startWait;
                        // This will be logged by the caller
                    }
                    
                    // Wait before checking again
                    await Task.Delay(100);
                }
            }
            
            public int CurrentRequestCount
            {
                get
                {
                    lock (_lock)
                    {
                        // Remove timestamps outside the window
                        while (_requestTimestamps.Count > 0 && 
                               DateTime.UtcNow - _requestTimestamps.Peek() > _interval)
                        {
                            _requestTimestamps.Dequeue();
                        }
                        
                        return _requestTimestamps.Count;
                    }
                }
            }
        }

        public virtual IndexerPageableRequestChain GetRecentRequests()
        {
            Logger.Debug("🔍 Getting recent requests for Tidal indexer");
            
            // this is a lazy implementation, just here so that lidarr has something to test against when saving settings 
            var pageableRequests = new IndexerPageableRequestChain();
            pageableRequests.Add(GetRequests("never gonna give you up"));

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(AlbumSearchCriteria searchCriteria)
        {
            var searchQuery = $"{searchCriteria.ArtistQuery} {searchCriteria.AlbumQuery}";
            Logger.Info($"🔍 Creating album search request: '{searchQuery}'");
            
            var chain = new IndexerPageableRequestChain();
            chain.AddTier(GetRequests(searchQuery));

            return chain;
        }

        public IndexerPageableRequestChain GetSearchRequests(ArtistSearchCriteria searchCriteria)
        {
            Logger.Info($"🔍 Creating artist search request: '{searchCriteria.ArtistQuery}'");
            
            var chain = new IndexerPageableRequestChain();
            chain.AddTier(GetRequests(searchCriteria.ArtistQuery));

            return chain;
        }

        private IEnumerable<IndexerRequest> GetRequests(string searchParameters)
        {
            if (string.IsNullOrWhiteSpace(searchParameters))
            {
                Logger?.Warn("Received empty search parameters");
                return new List<IndexerRequest>(); // Return empty list
            }
            
            // Initialize rate limiters if not already done
            if (_searchSemaphore == null || _rateLimiter == null)
            {
                InitializeRateLimiters();
            }
            
            Logger?.Info($"🎵 Beginning Tidal search for '{searchParameters}'");
            
            // Create a list to hold our requests
            var requests = new List<IndexerRequest>();
            
            try
            {
                // Acquire semaphore to limit concurrent searches - with timeout
                Logger?.Debug($"⏳ Waiting for search semaphore (current usage: {_searchSemaphore.CurrentCount}/{Settings?.MaxConcurrentSearches ?? DEFAULT_MAX_CONCURRENT_REQUESTS})");
                
                bool semaphoreAcquired = false;
                try
                {
                    semaphoreAcquired = _searchSemaphore.Wait(TimeSpan.FromSeconds(30));
                    if (!semaphoreAcquired)
                    {
                        Logger?.Error("Failed to acquire search semaphore after 30 seconds");
                        return requests; // Return empty list
                    }
                    
                    Logger?.Debug("✅ Search semaphore acquired");
                }
                catch (Exception ex)
                {
                    Logger?.Error(ex, "Error waiting for search semaphore");
                    return requests; // Return empty list
                }
                
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
                    
                    // Check if token is expired or needs refresh
                    bool needsRefresh = true;
                    
                    try
                    {
                        needsRefresh = DateTime.UtcNow > TidalAPI.Instance.Client.ActiveUser.ExpirationDate;
                    }
                    catch (Exception ex)
                    {
                        Logger?.Warn(ex, "Error checking token expiration - will refresh token");
                    }
                    
                    if (needsRefresh)
                    {
                        // ensure we always have an accurate expiration date
                        if (TidalAPI.Instance.Client.ActiveUser.ExpirationDate == DateTime.MinValue)
                        {
                            Logger?.Debug("🔄 Token expiration date not set, forcing refresh");
                            try
                            {
                                var refreshTask = TidalAPI.Instance.Client.ForceRefreshToken();
                                if (!refreshTask.Wait(TimeSpan.FromSeconds(30)))
                                {
                                    Logger?.Error("Token refresh timed out after 30 seconds");
                                    return requests;
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger?.Error(ex, "Error refreshing token");
                                return requests;
                            }
                        }
                        else
                        {
                            Logger?.Debug("🔄 Token expired, refreshing");
                            try
                            {
                                var loginTask = TidalAPI.Instance.Client.IsLoggedIn();
                                if (!loginTask.Wait(TimeSpan.FromSeconds(30)))
                                {
                                    Logger?.Error("Login check timed out after 30 seconds");
                                    return requests;
                                }
                                
                                if (!loginTask.Result)
                                {
                                    Logger?.Error("Login check failed - not logged in");
                                    return requests;
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger?.Error(ex, "Error calling IsLoggedIn (for token refresh)");
                                return requests;
                            }
                        }
                        
                        // Check token expiration again 
                        if (TidalAPI.Instance.Client.ActiveUser == null || 
                            string.IsNullOrEmpty(TidalAPI.Instance.Client.ActiveUser.AccessToken))
                        {
                            Logger?.Error("After refresh, access token is still missing - login failed");
                            return requests;
                        }
                        
                        Logger?.Debug($"✨ Token refreshed, new expiration: {TidalAPI.Instance.Client.ActiveUser.ExpirationDate}");
                    }

                    // Create requests for each page
                    var maxPages = Settings?.MaxPages ?? DEFAULT_MAX_PAGES;
                    var pageSize = Settings?.PageSize ?? DEFAULT_PAGE_SIZE;
                    
                    Logger?.Debug($"⚙️ Search parameters: MaxPages={maxPages}, PageSize={pageSize}");
                    
                    for (var page = 0; page < maxPages; page++)
                    {
                        // Wait for rate limiter before making request - with timeout
                        var startWait = DateTime.UtcNow;
                        
                        try
                        {
                            var waitTask = _rateLimiter.WaitForSlot();
                            if (!waitTask.Wait(TimeSpan.FromSeconds(30)))
                            {
                                Logger?.Warn($"Rate limiter wait timed out for page {page+1}");
                                continue; // Skip this page
                            }
                            
                            var waitTime = DateTime.UtcNow - startWait;
                            
                            if (waitTime.TotalMilliseconds > 500)
                            {
                                Logger?.Debug($"⏱️ Rate limiter delayed request for {waitTime.TotalSeconds:F1}s (current usage: {_rateLimiter.CurrentRequestCount}/{Settings?.MaxRequestsPerMinute ?? 50} requests/min)");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger?.Error(ex, $"Error waiting for rate limiter for page {page+1}");
                            continue; // Skip this page
                        }
                        
                        var data = new Dictionary<string, string>()
                        {
                            ["query"] = searchParameters,
                            ["limit"] = $"{pageSize}",
                            ["types"] = "albums,tracks",
                            ["offset"] = $"{page * pageSize}",
                        };

                        Logger?.Debug($"📦 Creating request for page {page+1} with offset {page * pageSize}");
                        
                        string url;
                        try
                        {
                            url = TidalAPI.Instance!.GetAPIUrl("search", data);
                        }
                        catch (Exception ex)
                        {
                            Logger?.Error(ex, "Error generating API URL");
                            continue; // Skip this page if URL generation fails
                        }
                        
                        if (string.IsNullOrEmpty(url))
                        {
                            Logger?.Error("Generated API URL is empty");
                            continue; // Skip this page
                        }
                        
                        IndexerRequest req;
                        try
                        {
                            req = new IndexerRequest(url, HttpAccept.Json);
                            req.HttpRequest.Method = System.Net.Http.HttpMethod.Get;
                        }
                        catch (Exception ex)
                        {
                            Logger?.Error(ex, "Error creating HTTP request");
                            continue; // Skip this page
                        }
                        
                        try
                        {
                            if (TidalAPI.Instance?.Client?.ActiveUser == null)
                            {
                                Logger?.Error("ActiveUser is null when adding Authorization header");
                                continue; // Skip this page
                            }
                            
                            var activeUser = TidalAPI.Instance.Client.ActiveUser;
                            if (activeUser.TokenType == null || activeUser.AccessToken == null)
                            {
                                Logger?.Error("Tidal token or token type is null");
                                continue; // Skip this request if token information is missing
                            }

                            var tokenType = activeUser.TokenType;
                            var accessToken = activeUser.AccessToken;
                            
                            try
                            {
                                if (req.HttpRequest.Headers.ContainsKey("Authorization"))
                                {
                                    req.HttpRequest.Headers.Remove("Authorization");
                                }
                                
                                var auth = $"{tokenType} {accessToken}";
                                req.HttpRequest.Headers.Add("Authorization", auth);
                            }
                            catch (Exception ex)
                            {
                                Logger?.Error(ex, "Error setting authorization header");
                                continue; // Skip this request if adding auth headers fails
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger?.Error(ex, "Error setting authorization header");
                            continue; // Skip this request if adding auth headers fails
                        }

                        requests.Add(req);
                    }
                    
                    Logger?.Info($"✅ Completed all search requests for '{searchParameters}' - {requests.Count} requests created");
                }
                finally
                {
                    // Make sure we release the semaphore
                    if (semaphoreAcquired)
                    {
                        try
                        {
                            _searchSemaphore.Release();
                            Logger?.Debug("🔓 Search semaphore released");
                        }
                        catch (Exception ex)
                        {
                            Logger?.Error(ex, "Error releasing search semaphore");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.Error(ex, $"❌ Error generating search requests for '{searchParameters}'");
                
                // Clean up semaphore if needed
                try
                {
                    if (_searchSemaphore != null && _searchSemaphore.CurrentCount < (Settings?.MaxConcurrentSearches ?? DEFAULT_MAX_CONCURRENT_REQUESTS))
                    {
                        _searchSemaphore.Release();
                        Logger?.Debug("🔓 Search semaphore released during exception handling");
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
            
            // Return all the requests we created
            return requests;
        }
    }
}
