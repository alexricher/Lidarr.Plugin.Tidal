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
using Lidarr.Plugin.Tidal.Services.Country;

namespace NzbDrone.Core.Indexers.Tidal
{
    public class Tidal : HttpIndexerBase<TidalIndexerSettings>
    {
        public override string Name => "Tidal";
        public override string Protocol => "TidalDownloadProtocol";
        public override bool SupportsRss => false;
        public override bool SupportsSearch => true;
        public override int PageSize => 100;
        public override TimeSpan RateLimit => TimeSpan.FromSeconds(2);

        private readonly NzbDrone.Core.Download.Clients.Tidal.ITidalProxy _tidalProxy;
        private readonly IAlbumService _albumService;
        private readonly ICacheManager _cacheManager;
        private readonly ITidalRateLimitService _rateLimitService;
        private readonly ITidalReleaseCache _releaseCache;
        private new readonly IHttpClient _httpClient;
        private new readonly Logger _logger;
        private readonly ICountryManagerService _countryManagerService;

        // Search queue management
        private static readonly ConcurrentQueue<SearchRequest> _searchQueue = new ConcurrentQueue<SearchRequest>();
        private static readonly SemaphoreSlim _queueSemaphore = new SemaphoreSlim(0);
        private static readonly SemaphoreSlim _queueLock = new SemaphoreSlim(1, 1); // Add lock for queue operations
        private static Task _searchProcessorTask;
        private static readonly CancellationTokenSource _processorCts = new CancellationTokenSource();
        private static bool _processorStarted = false;
        private static readonly object _processorLock = new object(); // Lock for starting/stopping processor
        private static readonly TimeSpan _searchTimeout = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan _searchQueueItemTimeout = TimeSpan.FromSeconds(30); // Shorter timeout per item

        private class SearchRequest
        {
            public string SearchTerm { get; set; }
            public TaskCompletionSource<IList<ReleaseInfo>> CompletionSource { get; set; }
            public CancellationTokenSource TokenSource { get; set; }
            public DateTime RequestTime { get; set; } = DateTime.UtcNow;
        }

        public Tidal(NzbDrone.Core.Download.Clients.Tidal.ITidalProxy tidalProxy,
            IHttpClient httpClient,
            IIndexerStatusService indexerStatusService,
            IConfigService configService,
            IParsingService parsingService,
            Logger logger,
            IAlbumService albumService,
            ICacheManager cacheManager,
            ITidalRateLimitService rateLimitService,
            ITidalReleaseCache releaseCache,
            ICountryManagerService countryManagerService)
            : base(httpClient, indexerStatusService, configService, parsingService, logger)
        {
            _tidalProxy = tidalProxy;
            _albumService = albumService;
            _cacheManager = cacheManager;
            _rateLimitService = rateLimitService;
            _releaseCache = releaseCache;
            _httpClient = httpClient;
            _logger = logger;
            _countryManagerService = countryManagerService;
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            try
            {
                // Log the ConfigPath to verify it's correct
                _logger.Debug($"Initializing TidalAPI with config path: {Settings?.ConfigPath ?? "null"}");
                
                if (Settings == null)
                {
                    _logger.Error("Settings is null during initialization");
                    return new TidalRequestGenerator(_rateLimitService)
                    {
                        Settings = new TidalIndexerSettings(), // Create default settings
                        Logger = _logger
                    };
                }
                
                if (!string.IsNullOrWhiteSpace(Settings.ConfigPath) && !Directory.Exists(Settings.ConfigPath))
                {
                    _logger.Warn($"Config directory doesn't exist: {Settings.ConfigPath}. Attempting to create it.");
                    try
                    {
                        Directory.CreateDirectory(Settings.ConfigPath);
                    }
                    catch (Exception dirEx)
                    {
                        _logger.Error(dirEx, $"Failed to create config directory: {Settings.ConfigPath}");
                        // Continue with initialization - TidalAPI.Initialize will use a temporary directory
                    }
                }
                
                // Create a safe request generator regardless of whether initialization succeeds
                var requestGenerator = new TidalRequestGenerator(_rateLimitService)
                {
                    Settings = Settings,
                    Logger = _logger
                };

                // Initialize the API with proper null checks
                TidalAPI.Initialize(Settings.ConfigPath, _httpClient, _logger, _countryManagerService);
                
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
                
                // Set the country code based on settings - with additional null checks
                if (_countryManagerService != null)
                {
                    try
                    {
                        // Create a TidalSettings instance to hold the country code
                        var downloadSettings = new NzbDrone.Core.Download.Clients.Tidal.TidalSettings
                        {
                            Country = (int)NzbDrone.Core.Download.Clients.Tidal.TidalCountry.Canada, // Default
                            CustomCountryCode = "" // Default
                        };
                        
                        // Update the country code manager
                        _countryManagerService.UpdateCountryCode(downloadSettings);
                        _logger.Debug($"Updated country code to {_countryManagerService.GetCountryCode()} in Tidal indexer");
                    }
                    catch (Exception countryEx)
                    {
                        _logger.Error(countryEx, "Error updating country code in CountryManagerService");
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
                return new TidalRequestGenerator(_rateLimitService)
                {
                    Settings = Settings,
                    Logger = _logger
                };
            }
        }

        public override IParseIndexerResponse GetParser()
        {
            return new TidalParser
            {
                Settings = Settings,
                Logger = _logger,
                ParsingService = _parsingService,
                ReleaseCache = _releaseCache
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
            lock (_processorLock)
            {
                if (_processorStarted && (_searchProcessorTask == null || _searchProcessorTask.IsCompleted))
                {
                    _logger.Warn("Search processor task completed unexpectedly. Restarting...");
                    _processorStarted = false;
                }

                if (!_processorStarted)
                {
                    // Clear any pending requests from previous runs
                    // CRITICAL FIX: This prevents stale search requests from blocking new searches
                    ClearSearchQueue();
                    
                    _searchProcessorTask = Task.Run(ProcessSearchQueue);
                    _processorStarted = true;
                    _logger.Debug("Search queue processor started");
                }
            }
        }

        // CRITICAL FIX: New method to clear search queue
        private void ClearSearchQueue()
        {
            try
            {
                _queueLock.Wait();
                
                _logger.Debug("Clearing search queue");
                // Drain the queue
                while (_searchQueue.TryDequeue(out var pendingRequest))
                {
                    // Complete any waiting tasks with empty results to avoid memory leaks
                    try
                    {
                        pendingRequest.CompletionSource?.TrySetResult(Array.Empty<ReleaseInfo>());
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Error completing abandoned search request");
                    }
                    
                    // Dispose any cancellation token sources
                    try
                    {
                        pendingRequest.TokenSource?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Error disposing token source for abandoned search request");
                    }
                }
                
                // Reset semaphore to 0
                while (_queueSemaphore.CurrentCount > 0)
                {
                    _queueSemaphore.Wait(0);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error clearing search queue");
            }
            finally
            {
                _queueLock.Release();
            }
        }

        // CRITICAL FIX: Modified search queue processor
        private async Task ProcessSearchQueue()
        {
            _logger.Debug("Search queue processor started");
            
            try
            {
                while (!_processorCts.IsCancellationRequested)
                {
                    SearchRequest request = null;
                    
                    try
                    {
                        // Wait for an item to be available in the queue
                        if (!await _queueSemaphore.WaitAsync(1000, _processorCts.Token))
                        {
                            continue;
                        }
                        
                        // Acquire lock before dequeuing
                        await _queueLock.WaitAsync(_processorCts.Token);
                        
                        try
                        {
                            // Try to get the next request from the queue
                            if (!_searchQueue.TryDequeue(out request))
                            {
                                _logger.Debug("Search queue semaphore was signaled but queue is empty");
                                _queueLock.Release();
                                continue;
                            }
                        }
                        finally
                        {
                            _queueLock.Release();
                        }
                        
                        // Check if the request is already timed out or canceled
                        if (request.TokenSource.IsCancellationRequested)
                        {
                            _logger.Debug($"Search request for '{request.SearchTerm}' was canceled before processing");
                            request.CompletionSource.TrySetCanceled();
                            continue;
                        }
                        
                        // Check if request is too old (more than timeout threshold)
                        if ((DateTime.UtcNow - request.RequestTime).TotalMilliseconds > _searchQueueItemTimeout.TotalMilliseconds)
                        {
                            _logger.Warn($"Search request for '{request.SearchTerm}' timed out in queue");
                            request.CompletionSource.TrySetResult(Array.Empty<ReleaseInfo>());
                            continue;
                        }
                        
                        _logger.Info($"🎵 Beginning Tidal search for '{request.SearchTerm}'");
                        
                        // Use a timeout for the actual search operation
                        using var searchTimeoutCts = new CancellationTokenSource(_searchQueueItemTimeout);
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                            request.TokenSource.Token, searchTimeoutCts.Token);
                        
                        try
                        {
                            // Perform the actual search
                            var results = await FetchReleasesFromIndexer(request.SearchTerm, linkedCts.Token);
                            
                            // CRITICAL FIX: Add a small delay after completing the search operation
                            // This ensures state is properly settled before processing another search
                            await Task.Delay(100, CancellationToken.None);
                            
                            // Complete the task with the results 
                            // Try multiple times with exponential backoff in case of task completion contention
                            bool taskCompleted = false;
                            int attempts = 0;
                            while (!taskCompleted && attempts < 3)
                            {
                                taskCompleted = request.CompletionSource.TrySetResult(results);
                                if (!taskCompleted)
                                {
                                    attempts++;
                                    _logger.Warn($"Failed to complete task for search '{request.SearchTerm}', attempt {attempts}/3");
                                    await Task.Delay(100 * attempts, CancellationToken.None);
                                }
                            }
                            
                            if (taskCompleted)
                            {
                                _logger.Info($"✅ Generated {results.Count} total release results from Tidal search");
                            }
                            else
                            {
                                _logger.Error($"Failed to complete search task for '{request.SearchTerm}' after 3 attempts");
                            }
                            
                            // CRITICAL FIX: Add a small delay after setting the result to allow 
                            // the task scheduler to process the completion callback
                            await Task.Delay(100, CancellationToken.None);
                        }
                        catch (OperationCanceledException) when (searchTimeoutCts.IsCancellationRequested)
                        {
                            _logger.Warn($"Search for '{request.SearchTerm}' timed out");
                            request.CompletionSource.TrySetResult(Array.Empty<ReleaseInfo>());
                        }
                        catch (OperationCanceledException) when (request.TokenSource.IsCancellationRequested)
                        {
                            _logger.Debug($"Search for '{request.SearchTerm}' was canceled");
                            request.CompletionSource.TrySetCanceled();
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, $"Error performing search for '{request.SearchTerm}'");
                            request.CompletionSource.TrySetException(ex);
                        }
                    }
                    catch (OperationCanceledException) when (_processorCts.IsCancellationRequested)
                    {
                        // Normal cancellation, just exit
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Unhandled exception in search queue processor");
                        
                        // Complete the current request if it failed
                        if (request?.CompletionSource != null && !request.CompletionSource.Task.IsCompleted)
                        {
                            request.CompletionSource.TrySetException(ex);
                        }
                        
                        // Delay before continuing to avoid tight loop on persistent errors
                        await Task.Delay(1000, _processorCts.Token);
                    }
                    finally
                    {
                        // Clean up the request resources
                        request?.TokenSource?.Dispose();
                    }
                }
            }
            catch (OperationCanceledException) when (_processorCts.IsCancellationRequested)
            {
                // Normal processor shutdown
                _logger.Debug("Search queue processor shutting down");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Fatal error in search queue processor");
            }
            finally
            {
                lock (_processorLock)
                {
                    _processorStarted = false;
                }
                
                _logger.Debug("Search queue processor stopped");
                
                // Clean up any remaining requests
                ClearSearchQueue();
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
        
        /// <summary>
        /// Gets country code from the settings, using the CountryManagerService
        /// </summary>
        /// <returns>Two-letter country code</returns>
        private string GetCountryCodeFromSettings()
        {
            try
            {
                if (_countryManagerService == null)
                {
                    _logger.Warn("CountryManagerService is not available, using default country code US");
                    return "US";
                }
                
                var settings = Settings;
                _countryManagerService.UpdateCountryCode(settings);
                
                return _countryManagerService.GetCountryCode();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error retrieving country code from settings");
                return "US"; // Default fallback
            }
        }

        /// <summary>
        /// Applies country code to HTTP requests
        /// </summary>
        /// <param name="request">The HTTP request to modify</param>
        /// <returns>The modified request with country code added</returns>
        protected HttpRequest ApplyCountryCode(HttpRequest request)
        {
            if (request == null)
            {
                return null;
            }

            // Apply the country code to the request if needed
            try
            {
                if (_countryManagerService != null)
                {
                    _countryManagerService.AddCountryCodeToRequest(request);
                    _logger.Debug($"Applied country code {_countryManagerService.GetCountryCode()} to request");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error applying country code to request");
            }

            return request;
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
