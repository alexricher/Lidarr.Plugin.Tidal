using NLog;
using System;

namespace Lidarr.Plugin.Tidal.Services.FileSystem
{
    /// <summary>
    /// Interface for file system operations used by the Tidal plugin.
    /// Provides methods for validating and working with directories and files.
    /// </summary>
    public interface IFileSystemService
    {
        /// <summary>
        /// Validates a file path to ensure it exists and is writable.
        /// If the path doesn't exist, attempts to create it.
        /// </summary>
        /// <param name="path">The path to validate</param>
        /// <param name="logger">Logger for recording validation steps</param>
        /// <returns>Validated path, or alternative path if original was invalid</returns>
        string ValidatePath(string path, Logger logger);

        /// <summary>
        /// Tests if a path is writable by creating a test file.
        /// </summary>
        /// <param name="path">The path to test</param>
        /// <param name="logger">Logger for recording test results</param>
        /// <returns>True if the path is writable; false otherwise</returns>
        bool TestPathWritable(string path, Logger logger);

        /// <summary>
        /// Ensures a directory exists, creating it if necessary.
        /// </summary>
        /// <param name="path">The directory path to ensure exists</param>
        /// <param name="retryCount">Number of retry attempts if creation fails</param>
        /// <param name="logger">Logger for recording attempts and failures</param>
        /// <returns>True if the directory exists or was successfully created; otherwise, false</returns>
        bool EnsureDirectoryExists(string path, int retryCount = 3, Logger logger = null);

        /// <summary>
        /// Determines if the current environment is running in a Docker container.
        /// </summary>
        /// <param name="logger">Logger for recording detection steps</param>
        /// <returns>True if running in Docker; false otherwise</returns>
        bool IsRunningInDocker(Logger logger);
    }
} 