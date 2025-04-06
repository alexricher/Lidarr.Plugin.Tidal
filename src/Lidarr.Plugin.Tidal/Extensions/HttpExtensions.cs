using System;
using System.Collections.Generic;
using System.Net;
using NzbDrone.Common.Http;

namespace NzbDrone.Plugin.Tidal.Extensions
{
    /// <summary>
    /// Extension methods for HTTP related classes to standardize property access
    /// and work around API limitations.
    /// </summary>
    public static class HttpExtensions
    {
        /// <summary>
        /// Determines if an HTTP response is successful (2xx status code).
        /// </summary>
        /// <param name="response">The HTTP response.</param>
        /// <returns>True if the response is successful, false otherwise.</returns>
        public static bool IsSuccessful(this HttpResponse response)
        {
            if (response == null)
            {
                return false;
            }
            
            int statusCode = (int)response.StatusCode;
            return statusCode >= 200 && statusCode < 300;
        }
        
        /// <summary>
        /// Gets the value for a header, or a default value if the header is not present.
        /// </summary>
        /// <param name="headers">The HTTP headers.</param>
        /// <param name="headerName">The name of the header to get.</param>
        /// <param name="defaultValue">The default value to return if the header is not present.</param>
        /// <returns>The header value, or the default value if the header is not present.</returns>
        public static string GetValueOrDefault(this HttpHeader headers, string headerName, string defaultValue = null)
        {
            if (headers == null || string.IsNullOrWhiteSpace(headerName))
            {
                return defaultValue;
            }
            
            // Check if the header contains the specified name
            foreach (var header in headers)
            {
                if (string.Equals(header.Key, headerName, StringComparison.OrdinalIgnoreCase))
                {
                    return header.Value;
                }
            }
            
            return defaultValue;
        }
        
        /// <summary>
        /// Gets all values for a header, or an empty collection if the header is not present.
        /// </summary>
        /// <param name="headers">The HTTP headers.</param>
        /// <param name="headerName">The name of the header to get.</param>
        /// <returns>The header values, or an empty collection if the header is not present.</returns>
        public static IEnumerable<string> GetValues(this HttpHeader headers, string headerName)
        {
            if (headers == null || string.IsNullOrWhiteSpace(headerName))
            {
                yield break;
            }
            
            // Iterate through headers to find matching ones
            foreach (var header in headers)
            {
                if (string.Equals(header.Key, headerName, StringComparison.OrdinalIgnoreCase))
                {
                    yield return header.Value;
                }
            }
        }
        
        /// <summary>
        /// Gets a strongly-typed header value, or a default value if the header is not present or cannot be converted.
        /// </summary>
        /// <typeparam name="T">The type to convert the header value to.</typeparam>
        /// <param name="headers">The HTTP headers.</param>
        /// <param name="headerName">The name of the header to get.</param>
        /// <param name="defaultValue">The default value to return if the header is not present or cannot be converted.</param>
        /// <returns>The converted header value, or the default value if the header is not present or cannot be converted.</returns>
        public static T GetValueOrDefault<T>(this HttpHeader headers, string headerName, T defaultValue = default)
        {
            if (headers == null || string.IsNullOrWhiteSpace(headerName))
            {
                return defaultValue;
            }
            
            // Get the string value from the headers
            string stringValue = GetValueOrDefault(headers, headerName);
            
            if (string.IsNullOrEmpty(stringValue))
            {
                return defaultValue;
            }
            
            try
            {
                if (typeof(T) == typeof(int))
                {
                    if (int.TryParse(stringValue, out var intValue))
                    {
                        return (T)(object)intValue;
                    }
                }
                else if (typeof(T) == typeof(long))
                {
                    if (long.TryParse(stringValue, out var longValue))
                    {
                        return (T)(object)longValue;
                    }
                }
                else if (typeof(T) == typeof(bool))
                {
                    if (bool.TryParse(stringValue, out var boolValue))
                    {
                        return (T)(object)boolValue;
                    }
                }
                else if (typeof(T) == typeof(DateTime))
                {
                    if (DateTime.TryParse(stringValue, out var dateTimeValue))
                    {
                        return (T)(object)dateTimeValue;
                    }
                }
                else if (typeof(T) == typeof(TimeSpan))
                {
                    if (int.TryParse(stringValue, out var seconds))
                    {
                        return (T)(object)TimeSpan.FromSeconds(seconds);
                    }
                    
                    if (TimeSpan.TryParse(stringValue, out var timeSpanValue))
                    {
                        return (T)(object)timeSpanValue;
                    }
                }
                else
                {
                    // For other types, try to use the Convert class
                    return (T)Convert.ChangeType(stringValue, typeof(T));
                }
            }
            catch (Exception)
            {
                // Conversion failed, return default value
            }
            
            return defaultValue;
        }

        /// <summary>
        /// Gets a status code as an integer value from an HTTP response.
        /// </summary>
        /// <param name="response">The HTTP response.</param>
        /// <returns>The status code as an integer, or 0 if the response is null.</returns>
        public static int GetStatusCode(this HttpResponse response)
        {
            if (response == null)
            {
                return 0;
            }
            
            return (int)response.StatusCode;
        }

        /// <summary>
        /// Determines if an HTTP response status code represents a rate limit being hit.
        /// </summary>
        /// <param name="response">The HTTP response.</param>
        /// <returns>True if the response indicates a rate limit, false otherwise.</returns>
        public static bool IsRateLimited(this HttpResponse response)
        {
            if (response == null)
            {
                return false;
            }
            
            int statusCode = (int)response.StatusCode;
            return statusCode == 429; // Too Many Requests
        }

        /// <summary>
        /// Gets the retry-after value from a response header if present.
        /// </summary>
        /// <param name="response">The HTTP response.</param>
        /// <returns>The retry-after value in seconds, or null if not present.</returns>
        public static int? GetRetryAfter(this HttpResponse response)
        {
            if (response?.Headers == null)
            {
                return null;
            }
            
            string retryAfterValue = GetValueOrDefault(response.Headers, "Retry-After");
            if (!string.IsNullOrEmpty(retryAfterValue) && int.TryParse(retryAfterValue, out int seconds))
            {
                return seconds;
            }
            
            return null;
        }
    }
} 