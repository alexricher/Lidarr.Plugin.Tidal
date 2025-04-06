# Tagging Proof of Concept

This document outlines a simple proof of concept (PoC) implementation to validate the core functionality of the audio tagging system before full integration with Lidarr.Plugin.Tidal.

## Purpose

The proof of concept serves several purposes:

1. Verify that TagLib# works properly with all required audio formats
2. Test Unicode handling with non-Latin scripts
3. Validate the track matching algorithm concepts
4. Measure baseline performance for tagging operations
5. Identify potential issues early in the development process

## Implementation

Create a simple console application with the following features:

### 1. Basic TagLib# Integration

```csharp
using System;
using System.IO;
using TagLib;

namespace TaggingPoC
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: TaggingPoC <file_path>");
                return;
            }

            var filePath = args[0];
            
            try
            {
                // Read existing tags
                var file = TagLib.File.Create(filePath);
                
                Console.WriteLine($"File: {Path.GetFileName(filePath)}");
                Console.WriteLine($"Title: {file.Tag.Title}");
                Console.WriteLine($"Artists: {string.Join(", ", file.Tag.Performers)}");
                Console.WriteLine($"Album: {file.Tag.Album}");
                Console.WriteLine($"Year: {file.Tag.Year}");
                Console.WriteLine($"Track: {file.Tag.Track}");
                Console.WriteLine($"Genre: {string.Join(", ", file.Tag.Genres)}");
                Console.WriteLine($"Has Pictures: {file.Tag.Pictures?.Length > 0}");
                
                // Demonstrate writing tags
                if (args.Length > 1 && args[1] == "--write")
                {
                    file.Tag.Title = $"{file.Tag.Title} [Tagged]";
                    file.Save();
                    Console.WriteLine("Updated tags successfully.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}
```

### 2. Format Support Verification

Add a format tester to verify compatibility with different audio formats:

```csharp
public static void TestFormatSupport(string directoryPath)
{
    var supportedExtensions = new[] { ".mp3", ".flac", ".m4a", ".ogg", ".opus", ".aac", ".wma" };
    var formatResults = new Dictionary<string, bool>();
    
    foreach (var extension in supportedExtensions)
    {
        var files = Directory.GetFiles(directoryPath, $"*{extension}", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            Console.WriteLine($"No {extension} files found for testing");
            formatResults[extension] = false;
            continue;
        }
        
        var file = files[0];
        try
        {
            var tagFile = TagLib.File.Create(file);
            // Try basic operations
            var title = tagFile.Tag.Title;
            tagFile.Tag.Title = "Test Title";
            tagFile.Save();
            tagFile.Tag.Title = title; // Restore original
            tagFile.Save();
            
            formatResults[extension] = true;
            Console.WriteLine($"{extension}: ✅ Supported");
        }
        catch (Exception ex)
        {
            formatResults[extension] = false;
            Console.WriteLine($"{extension}: ❌ Not supported - {ex.Message}");
        }
    }
    
    Console.WriteLine("\nFormat Support Summary:");
    foreach (var format in formatResults)
    {
        Console.WriteLine($"{format.Key}: {(format.Value ? "Supported" : "Not Supported")}");
    }
}
```

### 3. Unicode Support Testing

Add a Unicode test function:

```csharp
public static void TestUnicodeSupport(string filePath)
{
    try
    {
        var file = TagLib.File.Create(filePath);
        
        // Test various scripts
        var testStrings = new Dictionary<string, string>
        {
            { "Japanese", "音楽タグ付け" },
            { "Korean", "음악 태그" },
            { "Arabic", "وسم الموسيقى" },
            { "Russian", "Музыкальные теги" },
            { "Greek", "Μουσικές ετικέτες" },
            { "Thai", "แท็กเพลง" }
        };
        
        // Original values to restore later
        var originalTitle = file.Tag.Title;
        
        foreach (var test in testStrings)
        {
            Console.WriteLine($"Testing {test.Key} text: {test.Value}");
            
            // Write unicode text
            file.Tag.Title = test.Value;
            file.Save();
            
            // Read it back
            var reloadedFile = TagLib.File.Create(filePath);
            var readBack = reloadedFile.Tag.Title;
            
            if (readBack == test.Value)
                Console.WriteLine($"  ✅ Success: {readBack}");
            else
                Console.WriteLine($"  ❌ Failed: Wrote '{test.Value}' but read '{readBack}'");
        }
        
        // Restore original
        file.Tag.Title = originalTitle;
        file.Save();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Unicode test failed: {ex.Message}");
    }
}
```

### 4. Track Matching Algorithm Test

Implement a simple track matching algorithm:

```csharp
public static void TestTrackMatching(string directoryPath, List<TrackMetadata> knownTracks)
{
    var audioFiles = Directory.GetFiles(directoryPath, "*.*")
        .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
        .ToList();
    
    Console.WriteLine($"Testing track matching with {audioFiles.Count} files and {knownTracks.Count} known tracks");
    
    foreach (var file in audioFiles)
    {
        var fileName = Path.GetFileNameWithoutExtension(file);
        Console.WriteLine($"\nMatching file: {fileName}");
        
        // Method 1: Track number matching
        var trackNumberMatch = System.Text.RegularExpressions.Regex.Match(fileName, @"^(\d+)");
        if (trackNumberMatch.Success && int.TryParse(trackNumberMatch.Groups[1].Value, out var trackNumber))
        {
            var track = knownTracks.FirstOrDefault(t => t.TrackNumber == trackNumber);
            if (track != null)
            {
                Console.WriteLine($"  ✅ Matched by track number: {track.Title}");
                continue;
            }
        }
        
        // Method 2: Fuzzy title matching
        var bestMatch = FindBestTitleMatch(fileName, knownTracks);
        if (bestMatch.Confidence > 0.7)
        {
            Console.WriteLine($"  ✅ Matched by title similarity: {bestMatch.Track.Title} (confidence: {bestMatch.Confidence:P})");
        }
        else
        {
            Console.WriteLine($"  ❌ No good match found. Best candidate: {bestMatch.Track.Title} (confidence: {bestMatch.Confidence:P})");
        }
    }
}

private static (TrackMetadata Track, double Confidence) FindBestTitleMatch(string fileName, List<TrackMetadata> tracks)
{
    TrackMetadata bestMatch = null;
    double bestScore = 0;
    
    // Normalize filename for matching
    var normalizedFileName = NormalizeString(fileName);
    
    foreach (var track in tracks)
    {
        var normalizedTitle = NormalizeString(track.Title);
        var similarity = CalculateSimilarity(normalizedFileName, normalizedTitle);
        
        if (similarity > bestScore)
        {
            bestScore = similarity;
            bestMatch = track;
        }
    }
    
    return (bestMatch ?? tracks.First(), bestScore);
}

private static string NormalizeString(string input)
{
    if (string.IsNullOrEmpty(input))
        return string.Empty;
        
    // Convert to lowercase
    input = input.ToLowerInvariant();
    
    // Remove common words and characters that don't help with matching
    input = System.Text.RegularExpressions.Regex.Replace(input, @"\b(the|a|an)\b", "");
    input = System.Text.RegularExpressions.Regex.Replace(input, @"[^\w\s]", "");
    
    // Remove extra whitespace
    input = System.Text.RegularExpressions.Regex.Replace(input, @"\s+", " ").Trim();
    
    return input;
}

private static double CalculateSimilarity(string s1, string s2)
{
    // Simple implementation of Levenshtein distance
    if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
        return 0;
        
    int[,] d = new int[s1.Length + 1, s2.Length + 1];
    
    for (int i = 0; i <= s1.Length; i++)
        d[i, 0] = i;
    
    for (int j = 0; j <= s2.Length; j++)
        d[0, j] = j;
    
    for (int j = 1; j <= s2.Length; j++)
    {
        for (int i = 1; i <= s1.Length; i++)
        {
            int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
            d[i, j] = Math.Min(
                Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                d[i - 1, j - 1] + cost);
        }
    }
    
    int maxLength = Math.Max(s1.Length, s2.Length);
    return 1.0 - ((double)d[s1.Length, s2.Length] / maxLength);
}
```

### 5. Performance Measurement

Add simple performance testing:

```csharp
public static void MeasurePerformance(string directoryPath)
{
    var audioFiles = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
        .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
        .ToList();
    
    Console.WriteLine($"Measuring performance with {audioFiles.Count} files");
    
    // Measure read performance
    var stopwatch = new System.Diagnostics.Stopwatch();
    stopwatch.Start();
    
    foreach (var file in audioFiles)
    {
        try
        {
            var tagFile = TagLib.File.Create(file);
            var title = tagFile.Tag.Title;
            var artists = tagFile.Tag.Performers;
            var album = tagFile.Tag.Album;
        }
        catch (Exception)
        {
            // Ignore errors for performance testing
        }
    }
    
    stopwatch.Stop();
    var readTime = stopwatch.ElapsedMilliseconds;
    var avgReadTime = (double)readTime / audioFiles.Count;
    
    Console.WriteLine($"Read performance:");
    Console.WriteLine($"  Total time: {readTime}ms");
    Console.WriteLine($"  Average per file: {avgReadTime:F2}ms");
    
    // Measure write performance
    stopwatch.Reset();
    stopwatch.Start();
    
    foreach (var file in audioFiles.Take(10)) // Only test write on first 10 files
    {
        try
        {
            var tagFile = TagLib.File.Create(file);
            var originalTitle = tagFile.Tag.Title;
            tagFile.Tag.Title = $"{originalTitle} [Test]";
            tagFile.Save();
            
            // Restore original
            tagFile.Tag.Title = originalTitle;
            tagFile.Save();
        }
        catch (Exception)
        {
            // Ignore errors for performance testing
        }
    }
    
    stopwatch.Stop();
    var writeTime = stopwatch.ElapsedMilliseconds;
    var avgWriteTime = (double)writeTime / Math.Min(audioFiles.Count, 10);
    
    Console.WriteLine($"Write performance:");
    Console.WriteLine($"  Total time (10 files): {writeTime}ms");
    Console.WriteLine($"  Average per file: {avgWriteTime:F2}ms");
}
```

## How to Run the PoC

1. Create a new console application project
2. Add the TagLib# NuGet package:
   ```
   dotnet add package TagLibSharp
   ```
3. Implement the code above
4. Run the application with commands:
   ```
   # Basic tag reading
   dotnet run -- "path/to/audio/file.mp3"
   
   # Format support test
   dotnet run -- format-test "path/to/test/directory"
   
   # Unicode test
   dotnet run -- unicode-test "path/to/test/file.mp3"
   
   # Track matching test
   dotnet run -- match-test "path/to/test/directory" "path/to/metadata.json"
   
   # Performance test
   dotnet run -- perf-test "path/to/large/library"
   ```

## Test Data Preparation

1. Create a metadata.json file with known track metadata:
   ```json
   {
     "album": {
       "title": "Test Album",
       "artist": "Test Artist",
       "year": 2023
     },
     "tracks": [
       {
         "title": "Track One",
         "artists": ["Test Artist"],
         "trackNumber": 1,
         "discNumber": 1
       },
       {
         "title": "Track Two",
         "artists": ["Test Artist"],
         "trackNumber": 2,
         "discNumber": 1
       }
       // Add more tracks as needed
     ]
   }
   ```

2. Create test files with various naming patterns:
   - `01 - Track One.mp3`
   - `02 - Track Two.mp3`
   - `Track One.flac`
   - `The Track Two.m4a`
   - Files with various naming conventions and formats

## Expected Outcomes

- Verification that TagLib# supports all required formats
- Confirmation that Unicode characters are handled correctly
- Validation of track matching algorithms
- Performance metrics for tagging operations
- Identified issues that need to be addressed in the full implementation

## Next Steps After PoC

1. Document lessons learned and issues identified
2. Refine the track matching algorithm based on results
3. Create a performance optimization plan if needed
4. Begin implementing the core interfaces for the full solution
5. Apply the validated approaches to the Lidarr.Plugin.Tidal implementation 