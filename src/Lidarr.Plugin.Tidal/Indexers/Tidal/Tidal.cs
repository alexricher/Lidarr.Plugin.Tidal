using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Plugin.Tidal;
using FluentValidation.Results;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Music;
using NzbDrone.Core.Download.Clients.Tidal;
using NzbDrone.Core.Indexers.Exceptions;

namespace NzbDrone.Core.Indexers.Tidal
{
    public class Tidal : HttpIndexerBase<TidalIndexerSettings>
    {
        public override string Name => "Tidal";
        public override string Protocol => nameof(TidalDownloadProtocol);
        public override bool SupportsRss => false;
        public override bool SupportsSearch => true;
        public override int PageSize => 100;
        public override TimeSpan RateLimit => TimeSpan.FromSeconds(2);

        private readonly ITidalProxy _tidalProxy;
        private readonly IAlbumService _albumService;
        private readonly ICacheManager _cacheManager;

        private static readonly ConcurrentQueue<SearchRequest> _searchQueue = new ConcurrentQueue<SearchRequest>();
        private static readonly SemaphoreSlim _queueSemaphore = new SemaphoreSlim(0);
        private static readonly CancellationTokenSource _queueCts = new CancellationTokenSource();
        private static bool _queueProcessorStarted = false;
        private static readonly object _queueLock = new object();
        private static readonly SemaphoreSlim _searchThrottleSemaphore = new SemaphoreSlim(4, 4); // Limit concurrent searches
        private static readonly TimeSpan _searchTimeout = TimeSpan.FromMinutes(3); // 3 minute timeout for searches

        private class SearchRequest
        {
            public string SearchTerm { get; set; }
            public TaskCompletionSource<IList<ReleaseInfo>> CompletionSource { get; set; }
            public CancellationTokenSource TokenSource { get; set; }
            public DateTime RequestTime { get; set; } = DateTime.UtcNow;
        }

        public Tidal(ITidalProxy tidalProxy,
            IHttpClient httpClient,
            IIndexerStatusService indexerStatusService,
            IConfigService configService,
            IParsingService parsingService,
            Logger logger,
            IAlbumService albumService,
            ICacheManager cacheManager)
            : base(httpClient, indexerStatusService, configService, parsingService, logger)
        {
            _tidalProxy = tidalProxy;
            _albumService = albumService;
            _cacheManager = cacheManager;
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            if (string.IsNullOrEmpty(Settings.ConfigPath))
            {
                _logger.Warn("Config path is not set");
                return new TidalRequestGenerator()
                {
                    Settings = Settings,
                    Logger = _logger
                };
            }

            try
            {
                // Log the ConfigPath to verify it's correct
                _logger.Debug($"Initializing TidalAPI with config path: {Settings.ConfigPath}");
                
                if (!Directory.Exists(Settings.ConfigPath))
                {
                    _logger.Warn($"Config directory doesn't exist: {Settings.ConfigPath}. Attempting to create it.");
                    try
                    {
                        Directory.CreateDirectory(Settings.ConfigPath);
                    }
                    catch (Exception dirEx)
                    {
                        _logger.Error(dirEx, $"Failed to create config directory: {Settings.ConfigPath}");
                        throw new DirectoryNotFoundException($"Config directory doesn't exist and couldn't be created: {Settings.ConfigPath}");
                    }
                }
                
                // Create a safe request generator regardless of whether initialization succeeds
                var requestGenerator = new TidalRequestGenerator()
                {
                    Settings = Settings,
                    Logger = _logger
                };

                // Initialize the API with proper null checks
                TidalAPI.Initialize(Settings.ConfigPath, _httpClient, _logger);
                
                if (TidalAPI.Instance == null)
                {
                    _logger.Error("TidalAPI.Instance is null after initialization");
                    return requestGenerator;
                }
                
                if (TidalAPI.Instance.Client == null)
                {
                    _logger.Error("TidalAPI.Instance.Client is null after initialization");
                    return requestGenerator;
                }
                
                // Ensure country manager is initialized
                if (TidalCountryManager.Instance == null)
                {
                    _logger.Debug("Initializing TidalCountryManager during request generator creation");
                    TidalCountryManager.Initialize(_httpClient, _logger);
                }
                
                // Set the country code based on settings - with additional null checks
                if (TidalCountryManager.Instance != null)
                {
                    try
                    {
                        // Create a TidalSettings instance to hold the country code
                        var downloadSettings = new NzbDrone.Core.Download.Clients.Tidal.TidalSettings
                        {
                            CountrySelection = (int)NzbDrone.Core.Download.Clients.Tidal.TidalCountry.USA, // Default
                            CustomCountryCode = "" // Default
                        };
                        
                        // Update the country code manager
                        TidalCountryManager.Instance.UpdateCountryCode(downloadSettings);
                        _logger.Debug($"Updated country code to {TidalCountryManager.Instance.GetCountryCode()} in Tidal indexer");
                    }
                    catch (Exception countryEx)
                    {
                        _logger.Error(countryEx, "Error updating country code in TidalCountryManager");
                    }
                }
                
                try
                {
                    // Check if we need to perform login
                    if (string.IsNullOrEmpty(Settings.RedirectUrl))
                    {
                        _logger.Warn("Redirect URL is empty - login will not be performed");
                    }
                    else if (TidalAPI.Instance.Client.ActiveUser == null || string.IsNullOrEmpty(TidalAPI.Instance.Client.ActiveUser.AccessToken))
                    {
                        _logger.Info("No active user found or access token is missing - attempting login");
                        // Attempt login with timeout protection
                        var loginTask = TidalAPI.Instance.Client.Login(Settings.RedirectUrl);
                        
                        if (!loginTask.Wait(TimeSpan.FromSeconds(30)))
                        {
                            _logger.Error("Login timed out after 30 seconds");
                            throw new TimeoutException("Login timed out after 30 seconds");
                        }
                        
                        // Check login result
                        if (!loginTask.Result)
                        {
                            _logger.Error("Login failed");
                            throw new Exception("Login failed - check your redirect URL");
                        }
                        
                        _logger.Info("Login successful - regenerating PKCE codes");
                        // The URL was submitted to the API so it likely cannot be reused
                        TidalAPI.Instance.Client.RegeneratePkceCodes();
                    }
                    else
                    {
                        _logger.Debug("Already logged in, skipping login process");
                    }
                }
                catch (Exception loginEx)
                {
                    _logger.Error(loginEx, "Error during Tidal login");
                    // Continue - we'll still return the request generator
                }
                
                return requestGenerator;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error initializing Tidal");
                
                // Try to provide more context about the error
                if (ex.InnerException != null)
                {
                    _logger.Error(ex.InnerException, "Inner exception details");
                }
                
                // Return a basic request generator that won't try to use TidalAPI.Instance
                // which might be in an inconsistent state
                return new TidalRequestGenerator()
                {
                    Settings = Settings,
                    Logger = _logger
                };
            }
        }

        public override IParseIndexerResponse GetParser()
        {
            return new TidalParser()
            {
                Settings = Settings
            };
        }

        protected override async Task<ValidationFailure> TestConnection()
        {
            try
            {
                // Initialize Tidal API
                TidalAPI.Initialize(Settings.ConfigPath, _httpClient, _logger);
                
                if (TidalAPI.Instance == null || TidalAPI.Instance.Client == null)
                {
                    return new ValidationFailure("Connection", "Failed to initialize Tidal API client");
                }
                
                // Attempt to login
                if (!string.IsNullOrEmpty(Settings.RedirectUrl))
                {
                    _logger.Debug("Testing connection with redirect URL");
                    
                    // Check URL format
                    var originalUrl = Settings.RedirectUrl;
                    if (!originalUrl.StartsWith("http://") && !originalUrl.StartsWith("https://"))
                    {
                        _logger.Warn("Redirect URL seems malformed (missing http:// or https://)");
                    }
                    
                    if (!originalUrl.Contains("code="))
                    {
                        _logger.Warn("Redirect URL is missing 'code=' parameter needed for authentication");
                        return new ValidationFailure("RedirectUrl", "The redirect URL appears to be invalid - it must be the full URL from your browser after login, containing a 'code=' parameter");
                    }
                    
                    // Check if we need to perform login
                    var loginTask = TidalAPI.Instance.Client.Login(Settings.RedirectUrl);
                    
                    if (!loginTask.Wait(TimeSpan.FromSeconds(30)))
                    {
                        _logger.Error("Login timed out after 30 seconds");
                        throw new TimeoutException("Login timed out after 30 seconds");
                    }
                    
                    // Check login result
                    var success = loginTask.Result;
                    if (!success)
                    {
                        return new ValidationFailure(string.Empty, "Failed to login to Tidal. Please check your redirect URL.");
                    }
                    
                    _logger.Info("Successfully logged in to Tidal");
                }
                else
                {
                    // Try to check if already logged in
                    bool isLoggedIn = false;
                    try
                    {
                        isLoggedIn = await TidalAPI.Instance.Client.IsLoggedIn();
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Error checking login status");
                    }
                    
                    if (!isLoggedIn)
                    {
                        return new ValidationFailure("RedirectUrl", 
                            "Not logged in to Tidal. To authenticate:\n" +
                            "1. Copy the login URL from the settings\n" +
                            "2. Open it in your browser and log in to Tidal\n" +
                            "3. After successful login, copy the entire URL from your browser's address bar\n" +
                            "4. Paste that complete URL into the 'Redirect URL' field here");
                    }
                }
                
                // Try a simple search to verify search functionality
                try
                {
                    var generator = GetRequestGenerator();
                    var firstRequest = generator.GetRecentRequests().GetAllTiers().FirstOrDefault()?.FirstOrDefault();

                    if (firstRequest == null)
                    {
                        return new ValidationFailure(string.Empty, "No test search query available. This may be an issue with the indexer.");
                    }

                    // Execute the request to verify connectivity
                    var response = await FetchPageResponse(firstRequest, CancellationToken.None);
                    
                    if (response == null || response.HttpResponse == null)
                    {
                        return new ValidationFailure(string.Empty, "Received null response from Tidal API.");
                    }
                    
                    if (response.HttpResponse.HasHttpError)
                    {
                        return new ValidationFailure(string.Empty, $"Tidal API returned error: {response.HttpResponse.StatusCode}");
                    }
                    
                    _logger.Info("Test search request completed successfully");
                }
                catch (Exception searchEx)
                {
                    _logger.Error(searchEx, "Error during test search");
                    return new ValidationFailure(string.Empty, $"Error during test search: {searchEx.Message}");
                }

                // All tests passed
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error connecting to Tidal: {0}", ex.Message);
                return new ValidationFailure(string.Empty, $"Error connecting to Tidal: {ex.Message}");
            }
        }

        private void EnsureQueueProcessorStarted()
        {
            lock (_queueLock)
            {
                if (!_queueProcessorStarted)
                {
                    _queueProcessorStarted = true;
                    Task.Run(() => ProcessSearchQueue(_queueCts.Token));
                    _logger.Debug("Search queue processor started");
                }
            }
        }

        private async Task ProcessSearchQueue(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                SearchRequest request = null;
                bool throttleSemaphoreAcquired = false;
                
                try
                {
                    await _queueSemaphore.WaitAsync(cancellationToken);
                    
                    if (!_searchQueue.TryDequeue(out request))
                    {
                        continue; // Nothing to process, wait for next item
                    }
                    
                    // Null check for request
                    if (request == null)
                    {
                        _logger.Warn("Dequeued search request is null, skipping");
                        continue;
                    }
                    
                    // Check if the request is expired (older than 3 minutes)
                    if ((DateTime.UtcNow - request.RequestTime).TotalMinutes > 3)
                    {
                        _logger.Warn($"Search request for '{request.SearchTerm}' expired, completing with empty result");
                        request.CompletionSource?.TrySetResult(new List<ReleaseInfo>());
                        continue;
                    }
                    
                    // Check if any of the critical objects for search are null
                    if (request.TokenSource?.Token == null || request.CompletionSource == null)
                    {
                        _logger.Error($"Invalid search request for '{request.SearchTerm}' (missing token or completion source)");
                        request.CompletionSource?.TrySetResult(new List<ReleaseInfo>());
                        continue;
                    }
                    
                    // Check if the request has already been canceled
                    if (request.TokenSource.IsCancellationRequested)
                    {
                        _logger.Debug($"Search request for '{request.SearchTerm}' was already canceled, skipping");
                        request.CompletionSource.TrySetCanceled();
                        continue;
                    }
                    
                    // Throttle the number of concurrent searches
                    try
                    {
                        throttleSemaphoreAcquired = await _searchThrottleSemaphore.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
                        
                        if (!throttleSemaphoreAcquired)
                        {
                            _logger.Warn($"Search throttle timeout exceeded for '{request.SearchTerm}', completing with empty result");
                            request.CompletionSource.TrySetResult(new List<ReleaseInfo>());
                            continue;
                        }
                        
                        // Apply rate limiting
                        await Task.Delay(RateLimit, cancellationToken);
                        
                        // Create fresh cancellation tokens
                        using var timeoutCts = new CancellationTokenSource(_searchTimeout);
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                            cancellationToken, 
                            timeoutCts.Token, 
                            request.TokenSource.Token);
                        
                        try
                        {
                            _logger.Debug($"Processing search for '{request.SearchTerm}'");
                            
                            // Extra check for cancellation before starting search
                            linkedCts.Token.ThrowIfCancellationRequested();
                            
                            var results = await FetchReleasesFromIndexer(request.SearchTerm, linkedCts.Token);
                            _logger.Debug($"Search for '{request.SearchTerm}' completed with {results.Count} results");
                            
                            // Use TrySetResult to avoid exceptions if the task has been cancelled
                            request.CompletionSource.TrySetResult(results);
                        }
                        catch (OperationCanceledException)
                        {
                            if (timeoutCts.IsCancellationRequested)
                            {
                                _logger.Warn($"Search for '{request.SearchTerm}' timed out after {_searchTimeout.TotalMinutes} minutes");
                                request.CompletionSource.TrySetResult(new List<ReleaseInfo>());
                            }
                            else if (request.TokenSource.IsCancellationRequested)
                            {
                                _logger.Debug($"Search for '{request.SearchTerm}' was cancelled by requester");
                                request.CompletionSource.TrySetCanceled();
                            }
                            else
                            {
                                _logger.Debug($"Search for '{request.SearchTerm}' was cancelled");
                                request.CompletionSource.TrySetCanceled();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, $"Error processing search request for '{request.SearchTerm}'");
                            
                            // Set an empty result instead of propagating the error to let the UI continue gracefully
                            request.CompletionSource.TrySetResult(new List<ReleaseInfo>());
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Error setting up search execution for '{request?.SearchTerm}'");
                        request?.CompletionSource?.TrySetResult(new List<ReleaseInfo>());
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Normal shutdown
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error in search queue processor");
                    
                    // If we have a specific request that failed, complete it with empty results
                    request?.CompletionSource?.TrySetResult(new List<ReleaseInfo>());
                    
                    // Wait a bit before continuing to prevent error spam
                    try
                    {
                        await Task.Delay(1000, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
                finally
                {
                    if (throttleSemaphoreAcquired)
                    {
                        try
                        {
                            _searchThrottleSemaphore.Release();
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug(ex, "Error releasing search throttle semaphore");
                        }
                    }
                }
            }
            
            _logger.Info("Search queue processor shutting down");
            
            // Complete any remaining requests
            while (_searchQueue.TryDequeue(out var remainingRequest))
            {
                _logger.Debug($"Completing abandoned search request for '{remainingRequest?.SearchTerm}'");
                remainingRequest?.CompletionSource?.TrySetCanceled();
            }
        }

        // Handle the actual search
        private async Task<IList<ReleaseInfo>> FetchReleasesFromIndexer(string searchTerm, CancellationToken cancellationToken)
        {
            _logger.Debug($"Performing search for: {searchTerm}");
            
            try
            {
                // Verify TidalAPI is initialized first
                if (TidalAPI.Instance == null)
                {
                    _logger.Error("TidalAPI is not initialized, attempting re-initialization");
                    
                    // Try to re-initialize with proper error handling
                    try
                    {
                        // Make sure we have a valid config path
                        if (string.IsNullOrWhiteSpace(Settings.ConfigPath))
                        {
                            _logger.Error("Cannot initialize TidalAPI: ConfigPath is empty");
                            return new List<ReleaseInfo>();
                        }
                        
                        // Create config directory if it doesn't exist
                        if (!Directory.Exists(Settings.ConfigPath))
                        {
                            _logger.Warn($"Config directory doesn't exist: {Settings.ConfigPath}. Creating it now.");
                            try
                            {
                                Directory.CreateDirectory(Settings.ConfigPath);
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, $"Failed to create config directory: {Settings.ConfigPath}");
                                return new List<ReleaseInfo>();
                            }
                        }
                        
                        TidalAPI.Initialize(Settings.ConfigPath, _httpClient, _logger);
                        
                        // Still null? Give up
                        if (TidalAPI.Instance == null)
                        {
                            _logger.Error("Unable to initialize TidalAPI, search cannot proceed");
                            return new List<ReleaseInfo>(); // Return empty list
                        }
                        
                        // Check if we need to login
                        if (TidalAPI.Instance.Client?.ActiveUser == null && !string.IsNullOrEmpty(Settings.RedirectUrl))
                        {
                            _logger.Info("No active user found, attempting login with redirect URL");
                            try
                            {
                                var loginTask = TidalAPI.Instance.Client.Login(Settings.RedirectUrl);
                                
                                // Wait for either the login task to complete or a timeout
                                var completedTask = await Task.WhenAny(loginTask, Task.Delay(30000, cancellationToken));
                                if (completedTask != loginTask)
                                {
                                    _logger.Error("Login timed out after 30 seconds");
                                    return new List<ReleaseInfo>();
                                }
                                
                                if (!loginTask.Result)
                                {
                                    _logger.Error("Login failed with redirect URL");
                                    return new List<ReleaseInfo>();
                                }
                                
                                _logger.Info("Successfully logged in with redirect URL");
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "Error during login attempt: {0}", ex.Message);
                                if (ex.InnerException != null)
                                {
                                    _logger.Error("Inner exception: {0}", ex.InnerException.Message);
                                }
                                return new List<ReleaseInfo>();
                            }
                        }
                    }
                    catch (Exception initEx)
                    {
                        _logger.Error(initEx, "Error during TidalAPI re-initialization");
                        return new List<ReleaseInfo>(); // Return empty list
                    }
                }
                
                // Double-check that Client is initialized
                if (TidalAPI.Instance == null || TidalAPI.Instance.Client == null)
                {
                    _logger.Error("TidalAPI or TidalAPI.Client is still null after initialization");
                    return new List<ReleaseInfo>();
                }
                
                // Check if we're actually logged in
                if (TidalAPI.Instance.Client.ActiveUser == null || 
                    string.IsNullOrEmpty(TidalAPI.Instance.Client.ActiveUser.AccessToken))
                {
                    _logger.Error("Not properly logged in to Tidal (missing ActiveUser or AccessToken)");
                    return new List<ReleaseInfo>();
                }
                
                // Check circuit breaker status before initiating search
                bool circuitBreakerOpen = _tidalProxy.IsCircuitBreakerOpen();
                if (circuitBreakerOpen)
                {
                    var pendingCount = _tidalProxy.GetPendingDownloadCount();
                    var timeRemaining = _tidalProxy.GetCircuitBreakerReopenTime();
                    
                    // For user experience, add a small delay when circuit breaker is open
                    // This helps avoid database contention when many searches are happening
                    await Task.Delay(500, cancellationToken);
                    
                    var message = $"Circuit breaker is currently open - download operations limited for {timeRemaining.TotalMinutes:F1} more minutes";
                    
                    if (pendingCount > 0)
                    {
                        message += $". {pendingCount} downloads are queued for processing once circuit breaker reopens.";
                    }
                    
                    _logger.Warn(message);
                    
                    // If there are too many pending downloads already queued (15+), return limited results
                    // to avoid overwhelming the system with more search results that will just get queued
                    if (pendingCount > 15)
                    {
                        _logger.Warn($"Large number of pending downloads ({pendingCount}). Limiting search results to reduce system load.");
                        
                        // Return a single informational "release" to let the user know what's happening
                        var limitedRelease = new ReleaseInfo
                        {
                            Title = $"[SYSTEM BUSY] {searchTerm} - Search limited due to high download queue ({pendingCount} pending)",
                            Size = 0,
                            PublishDate = DateTime.Now,
                            DownloadUrl = string.Empty, // Cannot be downloaded
                            InfoUrl = string.Empty,
                            Guid = $"circuit_breaker_limit_{Guid.NewGuid()}"
                        };
                        
                        return new List<ReleaseInfo> { limitedRelease };
                    }
                }
                
                // Create a basic album search criteria - just enough for the request generator to work
                var searchCriteria = new AlbumSearchCriteria 
                {
                    Artist = new NzbDrone.Core.Music.Artist { Name = searchTerm }
                };
                
                // Use the appropriate method to perform the search
                var generator = GetRequestGenerator();
                var parser = GetParser();
                
                var releases = new List<ReleaseInfo>();
                
                // Generate requests based on search criteria
                try
                {
                    var requestChain = generator.GetSearchRequests(searchCriteria);
                    foreach (var pageableRequest in requestChain.GetAllTiers())
                    {
                        foreach (var request in pageableRequest)
                        {
                            try
                            {
                                var pageReleases = await FetchPage(request, parser, cancellationToken);
                                
                                // Modify release titles to indicate download limitations if circuit breaker is open
                                if (circuitBreakerOpen)
                                {
                                    var timedReleases = new List<ReleaseInfo>();
                                    foreach (var release in pageReleases)
                                    {
                                        // Add warning to indicate downloads will be queued
                                        release.Title += " [THROTTLED: Downloads will be queued due to circuit breaker]";
                                        timedReleases.Add(release);
                                    }
                                    releases.AddRange(timedReleases);
                                }
                                else
                                {
                                    releases.AddRange(pageReleases);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, $"Error during search page processing for '{searchTerm}': {ex.Message}");
                                // Continue to next request
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Error generating search requests for '{searchTerm}': {ex.Message}");
                    // Return whatever releases we've collected so far
                }
                
                return releases;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error during search for '{searchTerm}': {ex.Message}");
                return new List<ReleaseInfo>(); // Return empty list on error
            }
        }

        // Override all fetch methods to use our queue system
        public override Task<IList<ReleaseInfo>> FetchRecent()
        {
            if (!SupportsRss)
            {
                return Task.FromResult<IList<ReleaseInfo>>(Array.Empty<ReleaseInfo>());
            }

            // Not implemented for Tidal
            return Task.FromResult<IList<ReleaseInfo>>(Array.Empty<ReleaseInfo>());
        }

        public override Task<IList<ReleaseInfo>> Fetch(AlbumSearchCriteria searchCriteria)
        {
            if (!SupportsSearch)
            {
                return Task.FromResult<IList<ReleaseInfo>>(Array.Empty<ReleaseInfo>());
            }

            try
            {
                EnsureQueueProcessorStarted();
                
                // Extract the search term from the criteria
                var searchTerm = string.Empty;
                if (searchCriteria.Artist != null && !string.IsNullOrEmpty(searchCriteria.Artist.Name))
                {
                    searchTerm = searchCriteria.Artist.Name;
                    
                    // Add album info if available
                    if (searchCriteria.Albums != null && searchCriteria.Albums.Count > 0 && !string.IsNullOrEmpty(searchCriteria.Albums[0].Title))
                    {
                        searchTerm += " " + searchCriteria.Albums[0].Title;
                    }
                }
                
                // Don't use 'using' here as the CTS needs to live as long as the request is in the queue
                var cts = new CancellationTokenSource(_searchTimeout);
                var tcs = new TaskCompletionSource<IList<ReleaseInfo>>();
                
                // Enqueue the search request
                var request = new SearchRequest
                {
                    SearchTerm = searchTerm,
                    CompletionSource = tcs,
                    TokenSource = cts,
                    RequestTime = DateTime.UtcNow
                };
                
                _searchQueue.Enqueue(request);
                _queueSemaphore.Release();
                
                return tcs.Task;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error initiating search for album {searchCriteria.AlbumQuery}: {ex.Message}");
                return Task.FromResult<IList<ReleaseInfo>>(Array.Empty<ReleaseInfo>());
            }
        }

        public override Task<IList<ReleaseInfo>> Fetch(ArtistSearchCriteria searchCriteria)
        {
            if (!SupportsSearch)
            {
                return Task.FromResult<IList<ReleaseInfo>>(Array.Empty<ReleaseInfo>());
            }

            try
            {
                EnsureQueueProcessorStarted();
                
                // Extract the search term from the criteria
                var searchTerm = string.Empty;
                if (searchCriteria.Artist != null && !string.IsNullOrEmpty(searchCriteria.Artist.Name))
                {
                    searchTerm = searchCriteria.Artist.Name;
                }
                
                // Don't use 'using' here as the CTS needs to live as long as the request is in the queue
                var cts = new CancellationTokenSource(_searchTimeout);
                var tcs = new TaskCompletionSource<IList<ReleaseInfo>>();
                
                // Enqueue the search request
                var request = new SearchRequest
                {
                    SearchTerm = searchTerm,
                    CompletionSource = tcs,
                    TokenSource = cts,
                    RequestTime = DateTime.UtcNow
                };
                
                _searchQueue.Enqueue(request);
                _queueSemaphore.Release();
                
                return tcs.Task;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error initiating search for artist {searchCriteria.ArtistQuery}: {ex.Message}");
                return Task.FromResult<IList<ReleaseInfo>>(Array.Empty<ReleaseInfo>());
            }
        }

        // Override the HttpIndexerBase FetchReleases since we have a custom implementation
        protected override Task<IList<ReleaseInfo>> FetchReleases(Func<IIndexerRequestGenerator, IndexerPageableRequestChain> pageableRequestChainSelector, bool isRecent = false)
        {
            // This is called by the base Fetch methods, but we override those methods
            // so this should never be called. Return empty list to be safe.
            return Task.FromResult<IList<ReleaseInfo>>(Array.Empty<ReleaseInfo>());
        }

        // Fetch a page of search results
        private async Task<IndexerResponse> FetchPageResponse(IndexerRequest request, CancellationToken cancellationToken)
        {
            _logger.Debug($"Fetching page: {request.Url}");
            
            try
            {
                // Configure the cancellation token in the request
                request.HttpRequest.RequestTimeout = TimeSpan.FromMinutes(2);
                
                // Use the base method to fetch the response
                var response = await FetchIndexerResponse(request);
                return response;
            }
            catch (HttpException httpEx) when (httpEx.Response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.Warn("Unauthorized response from Tidal API - trying to refresh token");
                
                // Try to refresh token and retry once
                try
                {
                    if (TidalAPI.Instance?.Client?.ActiveUser != null)
                    {
                        await TidalAPI.Instance.Client.ForceRefreshToken();
                        
                    }
                }
                catch (Exception retryEx)
                {
                    _logger.Error(retryEx, "Error refreshing token for retry: {0}", retryEx.Message);
                }
                
                // If we get here, the retry failed or wasn't attempted
                _logger.Error("Authentication failed - token refresh unsuccessful");
                throw httpEx; // Just rethrow the original exception
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error fetching page {request.Url}: {ex.Message}");
                throw; // Rethrow the original exception
            }
        }

        // Fetch a page and parse it directly
        private async Task<IList<ReleaseInfo>> FetchPage(IndexerRequest request, IParseIndexerResponse parser, CancellationToken cancellationToken)
        {
            try
            {
                var response = await FetchPageResponse(request, cancellationToken);
                return parser.ParseResponse(response);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error parsing page {request.Url}: {ex.Message}");
                
                // Return an empty list instead of throwing to make error handling more graceful
                return new List<ReleaseInfo>();
            }
        }

        public ValidationResult OnHealthCheck()
        {
            try
            {
                // Ensure TidalAPI is initialized
                if (TidalAPI.Instance == null || TidalAPI.Instance.Client == null)
                {
                    var failure = new ValidationFailure("Connection", "Tidal API is not initialized");
                    return new ValidationResult(new[] { failure });
                }
                
                // Check login state - use the IsLoggedIn property or method
                bool isLoggedIn = false;
                try {
                    isLoggedIn = TidalAPI.Instance.Client.IsLoggedIn().Result;
                } catch {
                    // Fall back to checking if we have a valid access token
                    isLoggedIn = !string.IsNullOrEmpty(TidalAPI.Instance.Client.ActiveUser?.AccessToken);
                }
                
                if (!isLoggedIn)
                {
                    var failure = new ValidationFailure("Auth", "Not logged in to Tidal");
                    return new ValidationResult(new[] { failure });
                }
                
                return new ValidationResult();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during Tidal health check");
                var failure = new ValidationFailure("Error", $"Error during health check: {ex.Message}");
                return new ValidationResult(new[] { failure });
            }
        }
        
        private string GetCountryCodeFromSettings()
        {
            // Delegate to TidalCountryManager if possible
            if (TidalAPI.Instance != null && TidalAPI.Instance?.Client?.ActiveUser != null)
            {
                var countryCode = TidalAPI.Instance.Client.ActiveUser.CountryCode;
                if (!string.IsNullOrEmpty(countryCode))
                {
                    return countryCode;
                }
            }
            
            // Fallback to US if we can't determine country code
            return "US";
        }

        // Override the base FetchIndexerResponse method
        protected override async Task<IndexerResponse> FetchIndexerResponse(IndexerRequest request)
        {            
            int retryCount = 0;
            const int MAX_RETRIES = 2;
            
            while (retryCount <= MAX_RETRIES)
            {
                try
                {
                    // Try to fetch the response
                    var result = await base.FetchIndexerResponse(request);
                    if (result != null)
                    {
                        // If we got a valid result, return it
                        return result;
                    }
                    
                    _logger.Warn("Null result returned from API, retrying...");
                }
                catch (HttpException ex)
                {
                    if (ex.Response.StatusCode == HttpStatusCode.Unauthorized || 
                        ex.Response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        _logger.Warn("Authentication error: {0}. Attempting to refresh token and retry. Attempt {1}/{2}", 
                                     ex.Message, retryCount + 1, MAX_RETRIES + 1);
                        
                        if (await TokenRefresh())
                        {
                            // If token refresh succeeded, retry with a new token
                            retryCount++;
                            continue;
                        }
                        else
                        {
                            _logger.Error("Token refresh failed, cannot continue");
                            throw new AuthenticationException("Failed to refresh Tidal API token");
                        }
                    }
                    
                    // For other HTTP exceptions, log and re-throw
                    _logger.Error(ex, "HTTP error: {0}", ex.Message);
                    throw;
                }
                catch (WebException webEx)
                {
                    _logger.Error(webEx, "Web error: {0}", webEx.Message);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unknown error: {0}", ex.Message);
                    throw;
                }
                
                retryCount++;
            }
            
            // If we exhausted all retries without success, return null (will be handled by caller)
            _logger.Warn("Maximum retries reached without success");
            return null;
        }

        // Helper method to refresh the Tidal API token
        private async Task<bool> TokenRefresh()
        {
            try
            {
                _logger.Debug("Attempting to refresh Tidal API token");

                if (TidalAPI.Instance?.Client?.ActiveUser == null)
                {
                    _logger.Error("Cannot refresh token: Tidal client or active user is null");
                    return false;
                }

                await TidalAPI.Instance.Client.ForceRefreshToken();

                if (!string.IsNullOrEmpty(TidalAPI.Instance.Client.ActiveUser.AccessToken))
                {
                    _logger.Debug("Token refreshed successfully");
                    return true;
                }

                _logger.Warn("Token refresh completed but access token is empty");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error refreshing Tidal API token: {0}", ex.Message);
                return false;
            }
        }

        // Helper method to sanitize URLs for logging to avoid exposing sensitive info
        private string SanitizeUrlForLogging(string url)
        {
            try
            {
                if (string.IsNullOrEmpty(url))
                    return "[empty]";
                
                // Very basic sanitization, just to show format but hide specifics
                if (url.StartsWith("https://"))
                {
                    // Extract general structure without specific values
                    var uri = new Uri(url);
                    return $"https://{uri.Host}/...?[contains {uri.Query.Split('&').Length} parameters]";
                }
                
                // If it's not a URL, just indicate its length
                return $"[non-https string, length: {url.Length}]";
            }
            catch
            {
                return "[invalid url format]";
            }
        }

        // Helper method to attempt to fix common redirect URL formatting issues
        private string TryFixRedirectUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;
                
            try
            {
                // Problem: URL is not HTTP/HTTPS
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                {
                    // If it begins with something that looks like a domain
                    if (url.Contains(".") && !url.Contains(" "))
                    {
                        _logger.Debug("Attempting to fix missing protocol in URL");
                        return "https://" + url;
                    }
                }
                
                // URL already starts with http(s)://, see if it needs other fixes
                try
                {
                    // Try to parse as URI to see if it's valid
                    var uri = new Uri(url);
                    
                    // Check if the code parameter exists
                    if (!uri.Query.Contains("code="))
                    {
                        _logger.Warn("Redirect URL is missing required 'code=' parameter");
                    }
                    
                    // URL seems parseable, return as is
                    return url;
                }
                catch
                {
                    // URI parsing failed, may need additional fixes
                    _logger.Debug("Redirect URL parsing failed, URL may be malformed");
                    
                    // Some other common issues - spaces in URL
                    if (url.Contains(" "))
                    {
                        _logger.Debug("Fixing spaces in URL");
                        return url.Replace(" ", "%20");
                    }
                }
                
                // Return original if no fixes applied
                return url;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Error attempting to fix redirect URL");
                return url; // Return original on error
            }
        }
        
        // When attempting login, try to fix the URL format and add diagnostic logging
        private async Task<bool> AttemptLoginWithUrlFixes()
        {
            if (string.IsNullOrEmpty(Settings.RedirectUrl))
            {
                _logger.Error("Cannot login: Redirect URL is empty");
                return false;
            }
            
            try
            {
                _logger.Debug("Attempting login with redirect URL");
                
                // Try original URL first
                var originalResult = await TidalAPI.Instance.Client.Login(Settings.RedirectUrl);
                
                if (originalResult)
                {
                    _logger.Info("Login successful with original URL");
                    return true;
                }
                
                // Try to fix URL format and retry
                var fixedUrl = TryFixRedirectUrl(Settings.RedirectUrl);
                if (fixedUrl != Settings.RedirectUrl)
                {
                    _logger.Debug("Attempting login with fixed redirect URL");
                    var fixedResult = await TidalAPI.Instance.Client.Login(fixedUrl);
                    
                    if (fixedResult)
                    {
                        _logger.Info("Login successful with fixed URL format");
                        return true;
                    }
                }
                
                _logger.Error("Login failed with both original and fixed URL formats");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during login attempt with URL fixes: {0}", ex.Message);
                return false;
            }
        }
    }
}
