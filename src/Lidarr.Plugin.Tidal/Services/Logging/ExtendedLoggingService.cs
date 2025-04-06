using System;
using System.IO;
using NLog;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using TidalSharp.Data;
using System.Collections.Generic;

namespace Lidarr.Plugin.Tidal.Services.Logging
{
    /// <summary>
    /// Provides extended diagnostic logging capabilities for the Tidal plugin
    /// without requiring modifications to the TidalSharp library
    /// </summary>
    public static class ExtendedLoggingService
    {
        private static readonly ConcurrentDictionary<string, FileFormatInfo> _fileFormatCache = new ConcurrentDictionary<string, FileFormatInfo>();
        
        /// <summary>
        /// Logs detailed information about a file format mismatch 
        /// </summary>
        /// <param name="filePath">Path to the file being analyzed</param>
        /// <param name="logger">Logger to use</param>
        public static void LogFileFormatDiagnostics(string filePath, Logger logger)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    logger.Debug($"[TIDAL] {LogEmojis.Debug} Cannot analyze non-existent file: {filePath}");
                    return;
                }
                
                var fileInfo = new FileInfo(filePath);
                string extension = Path.GetExtension(filePath).ToLowerInvariant();
                
                logger.Debug($"[TIDAL] {LogEmojis.Debug} File diagnostics for: {Path.GetFileName(filePath)}, Size: {FormatFileSize(fileInfo.Length)}");
                
                // Get the first 64 bytes for signature analysis
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] signatureBytes = new byte[64];
                    int bytesRead = fs.Read(signatureBytes, 0, Math.Min(signatureBytes.Length, (int)fs.Length));
                    
                    // Check for FLAC signature
                    bool hasFlacSignature = bytesRead >= 4 && 
                        signatureBytes[0] == 0x66 && signatureBytes[1] == 0x4C && 
                        signatureBytes[2] == 0x61 && signatureBytes[3] == 0x43;
                    
                    // Check for M4A/MP4 atoms
                    bool hasFtypAtom = false;
                    bool hasMoovAtom = false;
                    bool hasMdatAtom = false;
                    
                    for (int i = 0; i < bytesRead - 4; i++)
                    {
                        // Check for 'ftyp'
                        if (signatureBytes[i] == 0x66 && signatureBytes[i+1] == 0x74 && 
                            signatureBytes[i+2] == 0x79 && signatureBytes[i+3] == 0x70)
                        {
                            hasFtypAtom = true;
                        }
                        // Check for 'moov'
                        if (signatureBytes[i] == 0x6D && signatureBytes[i+1] == 0x6F && 
                            signatureBytes[i+2] == 0x6F && signatureBytes[i+3] == 0x76)
                        {
                            hasMoovAtom = true;
                        }
                        // Check for 'mdat'
                        if (signatureBytes[i] == 0x6D && signatureBytes[i+1] == 0x64 && 
                            signatureBytes[i+2] == 0x61 && signatureBytes[i+3] == 0x74)
                        {
                            hasMdatAtom = true;
                        }
                    }
                    
                    // Log signature findings
                    logger.Debug($"[TIDAL] {LogEmojis.Debug} File signature analysis: FLAC={hasFlacSignature}, ftyp={hasFtypAtom}, moov={hasMoovAtom}, mdat={hasMdatAtom}");
                    
                    // Detect format mismatch
                    if (extension == ".flac" && !hasFlacSignature)
                    {
                        logger.Warn($"[TIDAL] {LogEmojis.Warning} Format mismatch: File has .flac extension but no FLAC signature");
                    }
                    else if (extension == ".m4a" && hasFlacSignature)
                    {
                        logger.Warn($"[TIDAL] {LogEmojis.Warning} Format mismatch: File has .m4a extension but contains FLAC data");
                    }
                    else if (extension == ".m4a" && !hasFtypAtom && !hasMoovAtom && !hasMdatAtom)
                    {
                        logger.Warn($"[TIDAL] {LogEmojis.Warning} Format mismatch: File has .m4a extension but missing M4A container markers");
                    }
                    
                    // Cache the format info for future reference
                    var formatInfo = new FileFormatInfo
                    {
                        FilePath = filePath,
                        FileSize = fileInfo.Length,
                        Extension = extension,
                        HasFlacSignature = hasFlacSignature,
                        HasFtypAtom = hasFtypAtom,
                        HasMoovAtom = hasMoovAtom,
                        HasMdatAtom = hasMdatAtom,
                        AnalysisTime = DateTime.UtcNow
                    };
                    
                    _fileFormatCache[filePath] = formatInfo;
                }
            }
            catch (Exception ex)
            {
                logger.Debug($"[TIDAL] {LogEmojis.Debug} Error during file format diagnostics: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Logs information about a track download and validation
        /// </summary>
        /// <param name="trackId">Tidal track ID</param>
        /// <param name="quality">Requested audio quality</param>
        /// <param name="fileExtension">File extension determined by TidalSharp</param>
        /// <param name="logger">Logger to use</param>
        public static void LogTrackDownloadDiagnostics(string trackId, AudioQuality quality, string fileExtension, Logger logger)
        {
            logger.Debug($"[TIDAL] {LogEmojis.Debug} Track download diagnostics: ID={trackId}, Quality={quality}, Extension={fileExtension}");
        }
        
        /// <summary>
        /// Gets information about a previously analyzed file
        /// </summary>
        /// <param name="filePath">Path to the file</param>
        /// <returns>The file format information or null if not analyzed</returns>
        public static FileFormatInfo GetFileFormatInfo(string filePath)
        {
            if (_fileFormatCache.TryGetValue(filePath, out var info))
            {
                return info;
            }
            
            return null;
        }
        
        /// <summary>
        /// Gets statistics about file format mismatches
        /// </summary>
        /// <returns>Statistics about format mismatches</returns>
        public static FormatMismatchStats GetFormatMismatchStats()
        {
            var stats = new FormatMismatchStats();
            
            foreach (var info in _fileFormatCache.Values)
            {
                if (info.Extension == ".flac" && !info.HasFlacSignature)
                {
                    stats.FlacExtensionWithoutFlacSignature++;
                }
                else if (info.Extension == ".m4a" && info.HasFlacSignature)
                {
                    stats.M4aExtensionWithFlacSignature++;
                }
                else if (info.Extension == ".m4a" && !info.HasFtypAtom && !info.HasMoovAtom && !info.HasMdatAtom)
                {
                    stats.M4aExtensionWithoutM4aMarkers++;
                }
            }
            
            return stats;
        }
        
        /// <summary>
        /// Formats a file size in bytes to a human-readable string
        /// </summary>
        private static string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int suffixIndex = 0;
            double size = bytes;
            
            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }
            
            return $"{size:0.##} {suffixes[suffixIndex]}";
        }
    }
    
    /// <summary>
    /// Contains information about a file's format
    /// </summary>
    public class FileFormatInfo
    {
        /// <summary>
        /// Path to the file
        /// </summary>
        public string FilePath { get; set; }
        
        /// <summary>
        /// Size of the file in bytes
        /// </summary>
        public long FileSize { get; set; }
        
        /// <summary>
        /// File extension (with the dot)
        /// </summary>
        public string Extension { get; set; }
        
        /// <summary>
        /// Whether the file has a FLAC signature
        /// </summary>
        public bool HasFlacSignature { get; set; }
        
        /// <summary>
        /// Whether the file has an 'ftyp' atom (M4A/MP4)
        /// </summary>
        public bool HasFtypAtom { get; set; }
        
        /// <summary>
        /// Whether the file has a 'moov' atom (M4A/MP4)
        /// </summary>
        public bool HasMoovAtom { get; set; }
        
        /// <summary>
        /// Whether the file has an 'mdat' atom (M4A/MP4)
        /// </summary>
        public bool HasMdatAtom { get; set; }
        
        /// <summary>
        /// When the analysis was performed
        /// </summary>
        public DateTime AnalysisTime { get; set; }
    }
    
    /// <summary>
    /// Contains statistics about format mismatches
    /// </summary>
    public class FormatMismatchStats
    {
        /// <summary>
        /// Count of files with .flac extension but no FLAC signature
        /// </summary>
        public int FlacExtensionWithoutFlacSignature { get; set; }
        
        /// <summary>
        /// Count of files with .m4a extension but with FLAC content
        /// </summary>
        public int M4aExtensionWithFlacSignature { get; set; }
        
        /// <summary>
        /// Count of files with .m4a extension but no M4A container markers
        /// </summary>
        public int M4aExtensionWithoutM4aMarkers { get; set; }
    }
} 