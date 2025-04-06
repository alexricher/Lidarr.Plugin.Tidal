using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace Lidarr.Plugin.Tidal.Services.FileSystem
{
    /// <summary>
    /// Implementation of file system operations used by the Tidal plugin.
    /// Handles path validation, directory creation, and Docker environment detection.
    /// </summary>
    public class FileSystemService : IFileSystemService
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Validates a file path to ensure it exists and is writable.
        /// If the path doesn't exist, attempts to create it with fallback to alternative paths if needed.
        /// </summary>
        /// <param name="path">The path to validate</param>
        /// <param name="logger">Logger for recording validation steps</param>
        /// <returns>Validated path, or alternative path if original was invalid</returns>
        public string ValidatePath(string path, Logger logger)
        {
            // Use a mutex for this operation to ensure thread safety with a timeout
            bool mutexAcquired = false;
            var mutexName = "Global\\TidalPluginPathValidation";
            using (var mutex = new Mutex(false, mutexName))
            {
                try
                {
                    // Try to acquire the mutex with a 5-second timeout
                    mutexAcquired = mutex.WaitOne(5000);

                    if (!mutexAcquired)
                    {
                        logger.Warn("Another thread is already validating the path. Skipping validation to avoid conflicts.");
                        return path;
                    }

                    // Proceed with validation with the lock acquired
                    return ValidatePathInternal(path, logger);
                }
                catch (AbandonedMutexException)
                {
                    // Handle the case where the mutex was abandoned (thread terminated without releasing)
                    logger.Warn("Mutex was abandoned. Taking ownership and proceeding with validation.");
                    return ValidatePathInternal(path, logger);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error acquiring mutex for path validation");
                    // Still try to validate, but log the error
                    return ValidatePathInternal(path, logger);
                }
                finally
                {
                    // Release the mutex if we acquired it
                    if (mutexAcquired)
                    {
                        mutex.ReleaseMutex();
                    }
                }
            }
        }

        /// <summary>
        /// Internal validation logic for paths, called within the mutex lock.
        /// </summary>
        /// <param name="path">The path to validate</param>
        /// <param name="logger">Logger for recording validation steps</param>
        /// <returns>Validated path, or alternative path if original was invalid</returns>
        private string ValidatePathInternal(string path, Logger logger)
        {
            // Temporary file path variables for later cleanup
            string testJsonFile = null;
            string writeTestFile = null;

            try
            {
                // If path is null or empty, use a default path in temp directory
                if (string.IsNullOrWhiteSpace(path))
                {
                    string defaultPath = Path.Combine(Path.GetTempPath(), "Lidarr", "TidalPlugin");
                    logger.Warn($"Path is not configured. Using default path: {defaultPath}");
                    path = defaultPath;
                }

                logger.Debug($"Validating path: {path}");

                // Validate that the path is valid for the file system
                try
                {
                    var fullPath = Path.GetFullPath(path);
                    if (fullPath != path)
                    {
                        logger.Debug($"Normalized path from '{path}' to '{fullPath}'");
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Path is invalid: {path}");
                    // Don't throw - continue and try to create a default path
                    string defaultPath = Path.Combine(Path.GetTempPath(), "Lidarr", "TidalPlugin");
                    logger.Warn($"Path is invalid. Using default path: {defaultPath}");
                    path = defaultPath;
                }

                // Check if we can create and write to the directory
                bool directoryExists = false;
                bool canWrite = false;
                string originalPath = path;
                
                // Try the specified path first
                try
                {
                    if (!Directory.Exists(path))
                    {
                        logger.Info($"Directory does not exist, creating: {path}");
                        Directory.CreateDirectory(path);
                    }
                    
                    directoryExists = Directory.Exists(path);
                    
                    // Test write permissions
                    if (directoryExists)
                    {
                        writeTestFile = Path.Combine(path, $"write_test_{Guid.NewGuid()}.tmp");
                        File.WriteAllText(writeTestFile, "Write permission test");
                        canWrite = true;
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, $"Cannot use specified path: {path}. Will try alternative locations.");
                }
                
                // If specified path doesn't work, try alternative locations commonly available in containers
                if (!directoryExists || !canWrite)
                {
                    // Check if we're in a Docker environment
                    bool isDocker = IsRunningInDocker(logger);
                    logger.Info($"{(isDocker ? "⚠️ Primary path failed and Docker environment detected" : "⚠️ Primary path failed")}. Trying common Docker writable paths.");
                    
                    // Try common writable locations in Docker containers
                    var alternativePaths = new List<string>
                    {
                        // Docker-specific paths first
                        "/config/tidal",                    // Common Lidarr Docker config location
                        "/config/plugins/tidal",            // Nested in config
                        "/config/cache/tidal",              // Cache dir in config
                        "/data/tidal",                      // Common data location
                        "/data/.tidal",                     // Hidden directory in data folder
                        "/downloads/tidal",                 // Common downloads location
                        "/downloads/.tidal-status",         // Hidden directory in downloads
                        
                        // Temp directories that should work in most environments
                        "/tmp/lidarr/tidal",                // Temp directory (works in most containers)
                        "/var/tmp/lidarr/tidal",            // Alternative temp location
                        
                        // Generic fallback paths
                        Path.Combine(Path.GetTempPath(), "Lidarr", "TidalPlugin") // Generic temp path
                    };
                    
                    foreach (var altPath in alternativePaths)
                    {
                        try
                        {
                            logger.Info($"📁 Trying alternative path: {altPath}");
                            
                            if (!Directory.Exists(altPath))
                            {
                                Directory.CreateDirectory(altPath);
                            }
                            
                            // Test write access
                            writeTestFile = Path.Combine(altPath, $"write_test_{Guid.NewGuid()}.tmp");
                            File.WriteAllText(writeTestFile, "Write permission test");
                            logger.Info($"📄 Test file created at: {writeTestFile}");
                            
                            // If we get here, the path works!
                            path = altPath;
                            logger.Info($"✅ Using alternative path: {path} (original path {originalPath} was not writable)");
                            directoryExists = true;
                            canWrite = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            logger.Debug(ex, $"Alternative path failed: {altPath}");
                            // Continue to the next path
                        }
                    }
                }
                
                // If we still can't find a writable location, try one last fallback to temp without throwing
                if (!directoryExists || !canWrite)
                {
                    try 
                    {
                        string lastResortPath = Path.Combine(Path.GetTempPath(), "TidalPlugin");
                        logger.Warn($"🔄 All paths failed, trying system temp as last resort: {lastResortPath}");
                        
                        if (!Directory.Exists(lastResortPath))
                        {
                            Directory.CreateDirectory(lastResortPath);
                        }
                        
                        // Simple write test
                        File.WriteAllText(Path.Combine(lastResortPath, "test.tmp"), "test");
                        
                        path = lastResortPath;
                        directoryExists = true;
                        canWrite = true;
                        logger.Info($"✓ Using system temp directory as last resort: {lastResortPath}");
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Even system temp directory is not writable!");
                        // At this point we'll return the current path but expect file operations to fail
                        // The plugin should have additional error handling for this case
                    }
                }

                // Test JSON file creation to ensure write permissions
                if (directoryExists && canWrite)
                {
                    testJsonFile = Path.Combine(path, "test.json");
                    var testData = new {
                        timestamp = DateTime.Now.ToString("o"),
                        test = true,
                        message = "This is a test file to verify that JSON can be written to the directory"
                    };

                    try
                    {
                        var json = JsonSerializer.Serialize(testData, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(testJsonFile, json);
                        logger.Debug($"Created test JSON file: {testJsonFile}");
                        logger.Info($"📄 Path validated: {path}");
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, $"Failed to write test JSON file: {testJsonFile}");
                        // We'll continue anyway and let the plugin handle file write failures
                    }
                }

                return path;
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Failed to validate path: {path}");
                return path; // Return current path and let caller handle fallbacks
            }
            finally
            {
                // Clean up temporary test files
                try
                {
                    if (testJsonFile != null && File.Exists(testJsonFile))
                    {
                        File.Delete(testJsonFile);
                        logger.Debug($"Deleted temporary test JSON file: {testJsonFile}");
                    }

                    if (writeTestFile != null && File.Exists(writeTestFile))
                    {
                        File.Delete(writeTestFile);
                        logger.Debug($"Deleted temporary write test file: {writeTestFile}");
                    }
                }
                catch (Exception ex)
                {
                    logger.Debug($"Failed to delete temporary test files (non-critical): {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Tests if a path is writable by creating a test file.
        /// </summary>
        /// <param name="path">The path to test</param>
        /// <param name="logger">Logger for recording test results</param>
        /// <returns>True if the path is writable; false otherwise</returns>
        public bool TestPathWritable(string path, Logger logger)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                logger.Warn("Path is empty or null");
                return false;
            }

            try
            {
                // Ensure directory exists
                if (!Directory.Exists(path))
                {
                    logger.Info($"Creating directory: {path}");
                    Directory.CreateDirectory(path);
                }

                if (!Directory.Exists(path))
                {
                    logger.Debug($"Directory creation failed: {path}");
                    return false;
                }

                // Try to create a test file
                var testFilePath = Path.Combine(path, $"test_{Guid.NewGuid()}.tmp");
                File.WriteAllText(testFilePath, "Test");

                // Verify we can read it back
                if (File.Exists(testFilePath))
                {
                    var content = File.ReadAllText(testFilePath);
                    if (content == "Test")
                    {
                        // Success
                        logger.Debug($"Path test write succeeded for {path}, cleaning up test file");
                        File.Delete(testFilePath);
                        return true;
                    }
                }

                logger.Debug($"Path test failed for {path} - file could not be read back");
                return false;
            }
            catch (Exception ex)
            {
                logger.Debug(ex, $"Error testing path {path}");
                return false;
            }
        }

        /// <summary>
        /// Cleans up any temporary files matching a specific pattern in the directory
        /// </summary>
        /// <param name="directoryPath">Directory to clean</param>
        /// <param name="pattern">File pattern to match (e.g., "*.tmp")</param>
        /// <param name="logger">Logger for output</param>
        /// <returns>Number of files cleaned up</returns>
        public int CleanupTempFiles(string directoryPath, string pattern, Logger logger)
        {
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
            {
                return 0;
            }
            
            try
            {
                string[] tempFiles = Directory.GetFiles(directoryPath, pattern);
                int count = 0;
                
                foreach (string file in tempFiles)
                {
                    try
                    {
                        File.Delete(file);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        logger?.Debug($"Failed to delete temporary file {file}: {ex.Message}");
                    }
                }
                
                return count;
            }
            catch (Exception ex)
            {
                logger?.Error($"Error cleaning up temporary files: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Ensures that a directory exists, creating it if necessary
        /// </summary>
        /// <param name="path">Path to the directory</param>
        /// <param name="retryCount">Number of retry attempts</param>
        /// <param name="logger">Logger for output</param>
        /// <returns>True if the directory exists or was successfully created, false otherwise</returns>
        public bool EnsureDirectoryExists(string path, int retryCount = 3, Logger logger = null)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            
            int attempts = 0;
            
            while (attempts < retryCount)
            {
                try
                {
                    logger?.Debug($"Checking if directory exists (attempt {attempts+1}/{retryCount}): {path}");
                    
                    if (!Directory.Exists(path))
                    {
                        Directory.CreateDirectory(path);
                        logger?.Debug($"Created directory: {path}");
                    }
                    
                    // Test file creation to verify write permissions
                    var testPath = Path.Combine(path, $"directory_test_{Guid.NewGuid()}.tmp");
                    File.WriteAllText(testPath, "Directory creation test");
                    
                    // Clean up the test file
                    try
                    {
                        if (File.Exists(testPath))
                        {
                            File.Delete(testPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.Debug($"Failed to delete test file (non-critical): {ex.Message}");
                    }
                    
                    return true;
                }
                catch (Exception ex)
                {
                    attempts++;
                    logger?.Warn($"Failed to create/verify directory (attempt {attempts}/{retryCount}): {ex.Message}");
                    
                    if (attempts < retryCount)
                    {
                        // Wait before retrying
                        Thread.Sleep(500);
                    }
                }
            }
            
            return false; // All attempts failed
        }

        /// <summary>
        /// Determines if the current environment is running in a Docker container.
        /// </summary>
        /// <param name="logger">Logger for recording detection steps</param>
        /// <returns>True if running in Docker; false otherwise</returns>
        public bool IsRunningInDocker(Logger logger)
        {
            try
            {
                // Check for .dockerenv file which is present in Docker containers
                if (File.Exists("/.dockerenv"))
                {
                    return true;
                }
                
                // Check for Docker-specific cgroup info
                if (File.Exists("/proc/1/cgroup"))
                {
                    string content = File.ReadAllText("/proc/1/cgroup");
                    if (content.Contains("docker") || content.Contains("kubepods"))
                    {
                        return true;
                    }
                }
                
                // Check if we're in a read-only filesystem (common for Docker)
                if (Directory.Exists("/app") && !CanWriteToPath("/app"))
                {
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Error checking for Docker environment, assuming not Docker");
                return false;
            }
        }

        /// <summary>
        /// Helper method to check if a path is writable.
        /// </summary>
        /// <param name="path">Path to check</param>
        /// <returns>True if writable; false otherwise</returns>
        private bool CanWriteToPath(string path)
        {
            try
            {
                string testFile = Path.Combine(path, $"docker_test_{Guid.NewGuid()}.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
} 