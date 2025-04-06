using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using NLog;
using NzbDrone.Core.Download.Clients.Tidal.Interfaces;

namespace NzbDrone.Core.Download.Clients.Tidal.Queue
{
    /// <summary>
    /// Manages persistence of download queue items to disk
    /// </summary>
    public class QueuePersistenceManager
    {
        private readonly Logger _logger;
        private readonly string _queueFilePath;
        private readonly object _fileLock = new object();
        private readonly int _maxRetries = 3;
        private readonly int _retryDelayMs = 100;

        /// <summary>
        /// Initializes a new instance of the QueuePersistenceManager class
        /// </summary>
        /// <param name="basePath">Base path for queue file storage</param>
        /// <param name="logger">Logger for diagnostic messages</param>
        public QueuePersistenceManager(string basePath, Logger logger)
        {
            _logger = logger;

            // Ensure the directory exists
            string queueDirectory = Path.Combine(basePath, "queue");
            try
            {
                if (!Directory.Exists(queueDirectory))
                {
                    Directory.CreateDirectory(queueDirectory);
                }
            }
            catch (Exception ex)
            {
                logger?.Error(ex, $"Error creating queue directory: {queueDirectory}");
                throw; // Rethrow as this is critical - we can't proceed without a valid directory
            }

            _queueFilePath = Path.Combine(queueDirectory, "tidal_queue.json");
            _logger?.Debug($"Queue persistence file path: {_queueFilePath}");

            // Verify write access to the directory
            try
            {
                string testFilePath = Path.Combine(queueDirectory, ".write_test");
                File.WriteAllText(testFilePath, "Test");
                File.Delete(testFilePath);
                _logger?.Debug("Successfully verified write access to queue directory");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, $"Failed to verify write access to queue directory: {queueDirectory}");
                throw; // Rethrow as this is critical - we need write access
            }
        }

        /// <summary>
        /// Saves the current queue items to disk
        /// </summary>
        /// <param name="items">The download items to save</param>
        public void SaveQueue(IEnumerable<IDownloadItem> items)
        {
            lock (_fileLock) // Ensure thread safety when writing to the file
            {
                try
                {
                    _logger?.Debug("Saving queue to disk...");

                    // Convert download items to serializable records
                    var records = new List<QueueItemRecord>();
                    foreach (var item in items)
                    {
                        try
                        {
                            records.Add(new QueueItemRecord
                            {
                                ID = item.ID,
                                Title = item.Title,
                                Artist = item.Artist,
                                Album = item.Album,
                                Bitrate = item.Bitrate.ToString(),
                                TotalSize = item.TotalSize,
                                DownloadFolder = item.DownloadFolder,
                                Explicit = item.Explicit,
                                RemoteAlbumJson = item.RemoteAlbumJson,
                                QueuedTime = item.QueuedTime,
                                Priority = (int)item.Priority
                            });
                        }
                        catch (Exception itemEx)
                        {
                            _logger?.Warn(itemEx, $"Error converting item to record: {itemEx.Message}. Skipping item {item.ID}");
                            // Continue with other items
                        }
                    }

                    if (records.Count == 0)
                    {
                        _logger?.Warn("No valid records to save. Aborting save operation.");
                        return;
                    }

                    // Serialize to JSON
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true
                    };
                    string json = JsonSerializer.Serialize(records, options);

                    // Create backup of existing file if it exists
                    try
                    {
                        string backupPath = _queueFilePath + ".bak";
                        if (File.Exists(_queueFilePath))
                        {
                            if (File.Exists(backupPath))
                                File.Delete(backupPath);
                            File.Copy(_queueFilePath, backupPath);
                            _logger?.Debug($"Created backup of existing queue file at {backupPath}");
                        }
                    }
                    catch (Exception backupEx)
                    {
                        _logger?.Warn(backupEx, $"Failed to create backup of queue file: {backupEx.Message}");
                        // Continue - we can still try to save without a backup
                    }

                    // Write to a temporary file first
                    string tempFilePath = _queueFilePath + ".tmp";
                    
                    // Use retry pattern for file operations with exponential backoff
                    bool success = RetryWithBackoff(() => 
                    {
                        // Write to temp file first, then move to final location for atomicity
                        File.WriteAllText(tempFilePath, json);
                        
                        // Verify the temp file was written correctly
                        if (!File.Exists(tempFilePath) || new FileInfo(tempFilePath).Length == 0)
                        {
                            throw new IOException("Temp file not written correctly");
                        }
                        
                        // If the target file exists, delete it
                        if (File.Exists(_queueFilePath))
                        {
                            File.Delete(_queueFilePath);
                        }
                        
                        // Move temp file to target location
                        File.Move(tempFilePath, _queueFilePath);
                        
                        return true;
                    }, _maxRetries, _retryDelayMs);

                    if (success)
                    {
                        _logger?.Debug($"Queue saved to disk: {records.Count} items");
                    }
                    else
                    {
                        _logger?.Error("Failed to save queue file after retries");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "Error saving queue to disk");
                    throw; // Rethrow so caller can handle
                }
                finally
                {
                    // Clean up temp file if it exists
                    try
                    {
                        string tempFilePath = _queueFilePath + ".tmp";
                        if (File.Exists(tempFilePath))
                        {
                            File.Delete(tempFilePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Debug(ex, "Error cleaning up temp file");
                    }
                }
            }
        }

        /// <summary>
        /// Loads queue items from disk
        /// </summary>
        /// <returns>List of queue item records</returns>
        public List<QueueItemRecord> LoadQueue()
        {
            lock (_fileLock) // Ensure thread safety when reading from the file
            {
                try
                {
                    if (!File.Exists(_queueFilePath))
                    {
                        _logger?.Debug("Queue file does not exist, no items to restore");
                        return new List<QueueItemRecord>();
                    }

                    _logger?.Debug($"Loading queue from disk: {_queueFilePath}");

                    // Try to read the file with retries
                    string json = null;
                    bool success = RetryWithBackoff(() => 
                    {
                        json = File.ReadAllText(_queueFilePath);
                        return true;
                    }, _maxRetries, _retryDelayMs);

                    if (!success || string.IsNullOrWhiteSpace(json))
                    {
                        // Try to load from backup if primary file read fails
                        string backupPath = _queueFilePath + ".bak";
                        if (File.Exists(backupPath))
                        {
                            _logger?.Warn("Primary queue file read failed, attempting to load from backup");
                            json = File.ReadAllText(backupPath);
                        }
                        else
                        {
                            _logger?.Warn("No backup queue file found after primary file read failed");
                            return new List<QueueItemRecord>();
                        }
                    }

                    // Deserialize the JSON
                    var options = new JsonSerializerOptions();
                    var records = JsonSerializer.Deserialize<List<QueueItemRecord>>(json, options);

                    _logger?.Debug($"Loaded {records.Count} items from queue file");
                    return records;
                }
                catch (Exception ex)
                {
                    _logger?.Error(ex, "Error loading queue from disk");

                    // Try to load from backup if primary file read fails
                    try
                    {
                        string backupPath = _queueFilePath + ".bak";
                        if (File.Exists(backupPath))
                        {
                            _logger?.Warn("Primary queue file read failed, attempting to load from backup");
                            string json = File.ReadAllText(backupPath);
                            var options = new JsonSerializerOptions();
                            var records = JsonSerializer.Deserialize<List<QueueItemRecord>>(json, options);
                            _logger?.Info($"Successfully loaded {records.Count} items from backup queue file");
                            return records;
                        }
                    }
                    catch (Exception backupEx)
                    {
                        _logger?.Error(backupEx, "Error loading from backup queue file");
                    }

                    return new List<QueueItemRecord>();
                }
            }
        }

        /// <summary>
        /// Helper method to retry operations with exponential backoff
        /// </summary>
        /// <param name="action">The action to retry</param>
        /// <param name="maxRetries">Maximum number of retries</param>
        /// <param name="initialDelayMs">Initial delay between retries in milliseconds</param>
        /// <returns>True if the action succeeded, false otherwise</returns>
        private bool RetryWithBackoff(Func<bool> action, int maxRetries, int initialDelayMs)
        {
            int retryCount = 0;
            int delayMs = initialDelayMs;

            while (retryCount < maxRetries)
            {
                try
                {
                    return action();
                }
                catch (Exception ex)
                {
                    retryCount++;
                    if (retryCount >= maxRetries)
                    {
                        _logger?.Error(ex, $"Operation failed after {maxRetries} attempts: {ex.Message}");
                        return false;
                    }

                    _logger?.Warn(ex, $"Operation failed (attempt {retryCount}/{maxRetries}), retrying in {delayMs}ms: {ex.Message}");
                    Thread.Sleep(delayMs);
                    
                    // Exponential backoff with jitter
                    delayMs = (int)(delayMs * 2 * (0.8 + 0.4 * new Random().NextDouble()));
                }
            }

            return false;
        }
    }
}
