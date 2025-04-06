using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Download;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Parser.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;

namespace NzbDrone.Core.Indexers.Tidal
{
    public static class TidalReleaseControllerHook
    {
        private static Logger Logger => LogManager.GetCurrentClassLogger();
        
        /// <summary>
        /// Register middleware for intercepting "release not found in cache" errors
        /// </summary>
        public static void RegisterMiddleware(IServiceCollection services)
        {
            try
            {
                Logger.Info("Registering Tidal cache recovery middleware");
                services.AddScoped<TidalCacheRecoveryMiddleware>();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error registering Tidal cache recovery middleware");
            }
        }
        
        public static void UseMiddleware(IApplicationBuilder app)
        {
            try
            {
                Logger.Info("Using Tidal cache recovery middleware");
                app.UseMiddleware<TidalCacheRecoveryMiddleware>();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error using Tidal cache recovery middleware");
            }
        }
    }
    
    public class TidalCacheRecoveryMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly Logger _logger;
        
        public TidalCacheRecoveryMiddleware(RequestDelegate next)
        {
            _next = next;
            _logger = LogManager.GetCurrentClassLogger();
        }
        
        public async System.Threading.Tasks.Task InvokeAsync(HttpContext context)
        {
            // Create a hook to intercept response
            var originalBodyStream = context.Response.Body;
            
            try
            {
                // Continue down the pipeline
                await _next(context);
                
                // If we get a 404 on the release endpoint, try to recover from our cache
                if (context.Response.StatusCode == 404 && 
                    context.Request.Path.Value.Contains("/api/v1/release") &&
                    context.Request.Method == "POST")
                {
                    _logger.Debug("Intercepted 404 on release endpoint - will try extended cache");
                    
                    try
                    {
                        // Get the releaseCache from the DI container
                        var releaseCache = context.RequestServices.GetService<ITidalReleaseCache>();
                        var downloadService = context.RequestServices.GetService<IDownloadService>();
                        
                        if (releaseCache != null && downloadService != null)
                        {
                            // Try to parse the request to get indexerId and guid
                            // This is a simplified example - in a real implementation,
                            // we would need to read the request body to get the release information
                            
                            _logger.Info("Tidal cache recovery middleware activated - attempting to recover release");
                            
                            // Redirect to our custom endpoint
                            context.Response.Redirect("/api/v1/tidal/release");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error in Tidal cache recovery middleware");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in Tidal cache recovery middleware");
                throw;
            }
        }
    }
} 