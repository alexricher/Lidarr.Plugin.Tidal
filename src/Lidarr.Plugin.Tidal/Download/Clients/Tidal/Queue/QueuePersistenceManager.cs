using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
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
            if (!Directory.Exists(queueDirectory))
            {
                Directory.CreateDirectory(queueDirectory);
            }

            _queueFilePath = Path.Combine(queueDirectory, "tidal_queue.json");
            _logger?.Debug($"Queue persistence file path: {_queueFilePath}");
        }

        /// <summary>
        /// Saves the current queue items to disk
        /// </summary>
        /// <param name="items">The download items to save</param>
        public void SaveQueue(IEnumerable<IDownloadItem> items)
        {
            try
            {
                _logger?.Debug("Saving queue to disk...");

                // Convert download items to serializable records
                var records = new List<QueueItemRecord>();
                foreach (var item in items)
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
                        QueuedTime = item.QueuedTime
                    });
                }

                // Serialize to JSON
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                string json = JsonSerializer.Serialize(records, options);

                // Write to file
                File.WriteAllText(_queueFilePath, json);
                _logger?.Debug($"Queue saved to disk: {records.Count} items");
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error saving queue to disk");
            }
        }

        /// <summary>
        /// Loads queue items from disk
        /// </summary>
        /// <returns>List of queue item records</returns>
        public List<QueueItemRecord> LoadQueue()
        {
            try
            {
                if (!File.Exists(_queueFilePath))
                {
                    _logger?.Debug("Queue file does not exist, no items to restore");
                    return new List<QueueItemRecord>();
                }

                _logger?.Debug("Loading queue from disk...");

                // Read JSON from file
                string json = File.ReadAllText(_queueFilePath);

                // Deserialize from JSON
                var records = JsonSerializer.Deserialize<List<QueueItemRecord>>(json);

                _logger?.Debug($"Queue loaded from disk: {records?.Count ?? 0} items");
                return records ?? new List<QueueItemRecord>();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error loading queue from disk");
                return new List<QueueItemRecord>();
            }
        }
    }
}
