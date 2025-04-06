using System;

namespace Lidarr.Plugin.Tidal.Services.FileSystem
{
    /// <summary>
    /// Provides a centralized access point to the file validation service instance.
    /// </summary>
    /// <remarks>
    /// This service provider follows the singleton pattern to ensure that only one instance
    /// of the file validation service exists throughout the application. It enables:
    /// 
    /// - Consistent access to the file validation service across the application
    /// - Ability to mock the service for testing purposes via the Register method
    /// - Simplified dependency management without requiring a full DI container
    /// - Centralized tracking of retry attempts and validation statistics
    /// 
    /// Components should access the file validation service through this provider
    /// rather than creating instances directly to ensure consistent validation behavior.
    /// </remarks>
    public static class FileValidationServiceProvider
    {
        /// <summary>
        /// The singleton instance of the file validation service.
        /// </summary>
        /// <remarks>
        /// This is initialized lazily when first accessed through the Current property.
        /// </remarks>
        private static IFileValidationService _instance;
        
        /// <summary>
        /// Gets the current instance of the file validation service, creating a default implementation if none exists.
        /// </summary>
        /// <remarks>
        /// This property lazily initializes the file validation service on first access.
        /// The same instance is returned for all subsequent calls until Reset is called
        /// or a different implementation is registered via Register.
        /// </remarks>
        /// <value>
        /// A singleton instance of <see cref="IFileValidationService"/> that can be used
        /// to validate audio files and track retry attempts.
        /// </value>
        public static IFileValidationService Current
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new FileValidationService();
                }
                return _instance;
            }
        }
        
        /// <summary>
        /// Registers a custom file validation service implementation to be used throughout the application.
        /// </summary>
        /// <param name="service">The custom implementation to use as the singleton instance</param>
        /// <exception cref="ArgumentNullException">Thrown if the service parameter is null</exception>
        /// <remarks>
        /// This method is particularly useful for testing scenarios where you need to mock
        /// the file validation service or inject a specialized implementation with
        /// different validation rules. It replaces the default implementation with the provided one.
        /// </remarks>
        /// <example>
        /// <code>
        /// // Register a mock implementation for testing
        /// var mockService = new MockFileValidationService();
        /// FileValidationServiceProvider.Register(mockService);
        /// 
        /// // Use the mock service through the provider
        /// var result = await FileValidationServiceProvider.Current.ValidateAudioFileAsync(...);
        /// </code>
        /// </example>
        public static void Register(IFileValidationService service)
        {
            _instance = service ?? throw new ArgumentNullException(nameof(service));
        }
        
        /// <summary>
        /// Resets the current instance to a new default file validation service.
        /// </summary>
        /// <remarks>
        /// This discards the existing service instance and creates a new one.
        /// Any tracked retry attempts or validation statistics in the previous
        /// instance will be lost. This is useful for clearing validation history
        /// or resetting to a clean state during application lifecycle events.
        /// </remarks>
        /// <example>
        /// <code>
        /// // Reset the validation service to clear all retry counters
        /// FileValidationServiceProvider.Reset();
        /// </code>
        /// </example>
        public static void Reset()
        {
            _instance = new FileValidationService();
        }
    }
} 