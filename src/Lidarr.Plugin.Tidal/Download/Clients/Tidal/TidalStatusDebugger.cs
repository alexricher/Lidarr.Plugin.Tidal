using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NLog;

namespace NzbDrone.Core.Download.Clients.Tidal
{
    public class TidalStatusDebugger
    {
        private readonly TidalStatusHelper _statusHelper;
        private readonly Logger _logger;

        public TidalStatusDebugger(TidalStatusHelper statusHelper, Logger logger)
        {
            _statusHelper = statusHelper;
            _logger = logger;
        }

        /// <summary>
        /// Generate a detailed debug report about the status files directory
        /// </summary>
        /// <returns>A debug report string</returns>
        public string GenerateDebugReport()
        {
            if (_statusHelper == null || !_statusHelper.IsEnabled)
            {
                return "Status helper is not initialized or directory is not configured.";
            }

            var report = new StringBuilder();
            report.AppendLine("=== TIDAL STATUS FILES DEBUG REPORT ===");
            report.AppendLine($"Date/Time: {DateTime.Now}");
            report.AppendLine($"Status Directory: {_statusHelper.StatusFilesPath}");

            try
            {
                // Get basic directory info
                var dirInfo = new DirectoryInfo(_statusHelper.StatusFilesPath);
                report.AppendLine($"Directory Exists: {dirInfo.Exists}");
                
                if (dirInfo.Exists)
                {
                    report.AppendLine($"Created: {dirInfo.CreationTime}");
                    report.AppendLine($"Last Modified: {dirInfo.LastWriteTime}");
                    report.AppendLine($"Attributes: {dirInfo.Attributes}");

                    // Check write permissions by testing
                    bool canWrite = false;
                    string testFile = Path.Combine(_statusHelper.StatusFilesPath, $"write_test_{Guid.NewGuid()}.tmp");
                    try
                    {
                        File.WriteAllText(testFile, "test");
                        canWrite = File.Exists(testFile);
                        if (canWrite)
                        {
                            File.Delete(testFile);
                        }
                    }
                    catch (Exception ex)
                    {
                        report.AppendLine($"Write Test Error: {ex.Message}");
                    }
                    report.AppendLine($"Can Write: {canWrite}");

                    // List files
                    var files = dirInfo.GetFiles();
                    report.AppendLine($"\nFiles Found: {files.Length}");
                    if (files.Length > 0)
                    {
                        report.AppendLine("\nFILE INVENTORY:");
                        report.AppendLine("---------------");
                        report.AppendLine(string.Format("{0,-30} {1,10} {2,20} {3,20}", "Filename", "Size (B)", "Created", "Last Modified"));
                        report.AppendLine(string.Format("{0,-30} {1,10} {2,20} {3,20}", "--------", "--------", "-------", "-------------"));
                        
                        foreach (var file in files.OrderBy(f => f.Name))
                        {
                            report.AppendLine(string.Format("{0,-30} {1,10} {2,20} {3,20}", 
                                file.Name, 
                                file.Length, 
                                file.CreationTime.ToString("yyyy-MM-dd HH:mm:ss"),
                                file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")));
                        }
                    }

                    // Check for JSON files and report their validity
                    var jsonFiles = files.Where(f => f.Extension.ToLower() == ".json").ToList();
                    if (jsonFiles.Any())
                    {
                        report.AppendLine("\nJSON FILES VALIDATION:");
                        report.AppendLine("---------------------");
                        
                        foreach (var file in jsonFiles)
                        {
                            try
                            {
                                // Attempt to read the file and parse it as JSON
                                var content = File.ReadAllText(file.FullName);
                                var isValid = IsValidJson(content);
                                
                                report.AppendLine($"{file.Name}: {(isValid ? "Valid JSON" : "Invalid JSON")}");
                                
                                // If it's a status file, try to show some details
                                if (isValid && file.Name.Contains("status"))
                                {
                                    // Sample a few key fields if available
                                    var keyFields = ExtractJsonKeyFields(content);
                                    if (keyFields.Count > 0)
                                    {
                                        report.AppendLine("  Key fields:");
                                        foreach (var kvp in keyFields)
                                        {
                                            report.AppendLine($"    {kvp.Key}: {kvp.Value}");
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                report.AppendLine($"{file.Name}: Error reading file - {ex.Message}");
                            }
                        }
                    }
                }
                else
                {
                    report.AppendLine("WARNING: Status directory does not exist!");
                    
                    // Check if parent directory exists and has write permissions
                    try
                    {
                        var parentDir = Directory.GetParent(_statusHelper.StatusFilesPath);
                        if (parentDir != null && parentDir.Exists)
                        {
                            report.AppendLine($"Parent directory exists: {parentDir.FullName}");
                            
                            // Check if we can create the status directory
                            bool canCreateDir = false;
                            try
                            {
                                Directory.CreateDirectory(_statusHelper.StatusFilesPath);
                                canCreateDir = Directory.Exists(_statusHelper.StatusFilesPath);
                                report.AppendLine($"Created directory during debug: {canCreateDir}");
                            }
                            catch (Exception ex)
                            {
                                report.AppendLine($"Failed to create directory: {ex.Message}");
                            }
                        }
                        else
                        {
                            report.AppendLine("Parent directory does not exist!");
                        }
                    }
                    catch (Exception ex)
                    {
                        report.AppendLine($"Error checking parent directory: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                report.AppendLine($"Error generating debug report: {ex.Message}");
                report.AppendLine($"Stack trace: {ex.StackTrace}");
            }

            report.AppendLine("\n=== END OF REPORT ===");
            
            return report.ToString();
        }
        
        /// <summary>
        /// Attempt to validate JSON content
        /// </summary>
        private bool IsValidJson(string content)
        {
            try
            {
                System.Text.Json.JsonDocument.Parse(content);
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Extract some key fields from JSON for summary
        /// </summary>
        private Dictionary<string, string> ExtractJsonKeyFields(string jsonContent)
        {
            var result = new Dictionary<string, string>();
            
            try
            {
                using (var doc = System.Text.Json.JsonDocument.Parse(jsonContent))
                {
                    var root = doc.RootElement;
                    
                    // Try to extract some common status fields
                    TryAddJsonProperty(result, root, "pluginVersion", "Plugin Version");
                    TryAddJsonProperty(result, root, "lastUpdated", "Last Updated");
                    TryAddJsonProperty(result, root, "totalPendingDownloads", "Pending Downloads");
                    TryAddJsonProperty(result, root, "totalCompletedDownloads", "Completed Downloads");
                    TryAddJsonProperty(result, root, "totalFailedDownloads", "Failed Downloads");
                    TryAddJsonProperty(result, root, "isHighVolumeMode", "High Volume Mode");
                }
            }
            catch
            {
                // Ignore extraction errors and just return what we've got
            }
            
            return result;
        }
        
        private void TryAddJsonProperty(Dictionary<string, string> dict, System.Text.Json.JsonElement element, string propertyName, string friendlyName)
        {
            try
            {
                if (element.TryGetProperty(propertyName, out var property))
                {
                    dict[friendlyName] = property.ToString();
                }
            }
            catch
            {
                // Ignore
            }
        }
        
        /// <summary>
        /// Create a debug report file in the status directory
        /// </summary>
        public string CreateDebugReport()
        {
            try
            {
                if (_statusHelper == null || !_statusHelper.IsEnabled)
                {
                    _logger.Warn("Cannot create debug report - StatusHelper is not initialized or disabled");
                    return null;
                }
                
                var report = GenerateDebugReport();
                var fileName = $"tidal_status_debug_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                var filePath = Path.Combine(_statusHelper.StatusFilesPath, fileName);
                
                _logger.Info($"Creating Tidal status debug report at: {filePath}");
                File.WriteAllText(filePath, report);
                
                return filePath;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to create Tidal status debug report");
                return null;
            }
        }
    }
} 