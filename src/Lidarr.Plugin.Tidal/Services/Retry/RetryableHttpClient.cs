using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Tidal.Services.Logging;
using NLog;
using NzbDrone.Common.Http;

namespace Lidarr.Plugin.Tidal.Services.Retry
{
    /// <summary>
    /// An HTTP client that automatically retries failed requests.
    /// </summary>
    public class RetryableHttpClient
    {
        private readonly IHttpClient _httpClient;
        private readonly RetryPolicy _retryPolicy;
        private readonly Logger _logger;

        /// <summary>
        /// Initializes a new instance of the RetryableHttpClient class.
        /// </summary>
        /// <param name="httpClient">The HTTP client to use.</param>
        /// <param name="logger">The logger to use.</param>
        /// <param name="retryPolicy">The retry policy to use.</param>
        public RetryableHttpClient(IHttpClient httpClient, Logger logger, RetryPolicy retryPolicy = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _retryPolicy = retryPolicy ?? RetryPolicyFactory.CreateDefault(_logger);
        }

        /// <summary>
        /// Executes an HTTP request with automatic retries.
        /// </summary>
        /// <param name="request">The HTTP request to execute.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>The HTTP response.</returns>
        public Task<HttpResponse> ExecuteAsync(HttpRequest request, CancellationToken cancellationToken = default)
        {
            return _retryPolicy.ExecuteAsync(async token =>
            {
                _logger.DebugWithEmoji(LogEmojis.Request, "Executing HTTP request: {0} {1}", request.Method, request.Url);
                var response = await _httpClient.ExecuteAsync(request).ConfigureAwait(false);
                _logger.DebugWithEmoji(LogEmojis.Response, "Received HTTP response: {0} {1}", (int)response.StatusCode, response.StatusCode);
                return response;
            }, $"HTTP {request.Method} {request.Url}", cancellationToken);
        }

        /// <summary>
        /// Executes an HTTP GET request with automatic retries.
        /// </summary>
        /// <param name="request">The HTTP request to execute.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>The HTTP response.</returns>
        public Task<HttpResponse> GetAsync(HttpRequest request, CancellationToken cancellationToken = default)
        {
            request.Method = HttpMethod.Get;
            return ExecuteAsync(request, cancellationToken);
        }

        /// <summary>
        /// Executes an HTTP POST request with automatic retries.
        /// </summary>
        /// <param name="request">The HTTP request to execute.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>The HTTP response.</returns>
        public Task<HttpResponse> PostAsync(HttpRequest request, CancellationToken cancellationToken = default)
        {
            request.Method = HttpMethod.Post;
            return ExecuteAsync(request, cancellationToken);
        }

        /// <summary>
        /// Executes an HTTP PUT request with automatic retries.
        /// </summary>
        /// <param name="request">The HTTP request to execute.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>The HTTP response.</returns>
        public Task<HttpResponse> PutAsync(HttpRequest request, CancellationToken cancellationToken = default)
        {
            request.Method = HttpMethod.Put;
            return ExecuteAsync(request, cancellationToken);
        }

        /// <summary>
        /// Executes an HTTP DELETE request with automatic retries.
        /// </summary>
        /// <param name="request">The HTTP request to execute.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>The HTTP response.</returns>
        public Task<HttpResponse> DeleteAsync(HttpRequest request, CancellationToken cancellationToken = default)
        {
            request.Method = HttpMethod.Delete;
            return ExecuteAsync(request, cancellationToken);
        }

        /// <summary>
        /// Executes an HTTP HEAD request with automatic retries.
        /// </summary>
        /// <param name="request">The HTTP request to execute.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>The HTTP response.</returns>
        public Task<HttpResponse> HeadAsync(HttpRequest request, CancellationToken cancellationToken = default)
        {
            request.Method = HttpMethod.Head;
            return ExecuteAsync(request, cancellationToken);
        }
    }
}
