# Test script for Tidal status files functionality
# This script helps verify that the status files functionality is working correctly

param (
    [switch]$KeepTestFiles = $false,
    [switch]$CleanOnly = $false,
    [string]$CustomTestDir = "",
    [string]$CustomPluginPath = ""
)

# Configuration - use params if provided, otherwise use defaults
$baseDir = $PSScriptRoot
if ([string]::IsNullOrEmpty($CustomTestDir)) {
    $testDir = Join-Path $baseDir "..\test-status-files"
} else {
    $testDir = $CustomTestDir
}
$statusFile = Join-Path $testDir "status_test.json"
$sampleJson = Join-Path $testDir "sample_status.json"
$debugReportPath = Join-Path $testDir "status_debug_report.txt"

Write-Host "Testing Tidal status files functionality" -ForegroundColor Cyan
Write-Host "-------------------------------------" -ForegroundColor Cyan
Write-Host "Using test directory: $testDir" -ForegroundColor Cyan

# If CleanOnly is specified, just clean up test files and exit
if ($CleanOnly) {
    if (Test-Path $testDir) {
        Write-Host "Cleaning up test directory: $testDir" -ForegroundColor Yellow
        Remove-Item -Path $testDir -Recurse -Force
        Write-Host "Test directory removed successfully" -ForegroundColor Green
    } else {
        Write-Host "Test directory does not exist: $testDir" -ForegroundColor Yellow
    }
    exit 0
}

# Create test directory if it doesn't exist
if (!(Test-Path $testDir)) {
    Write-Host "Creating test directory: $testDir" -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $testDir -Force | Out-Null
}
else {
    Write-Host "Test directory already exists: $testDir" -ForegroundColor Green
}

# Create a sample JSON file in the directory
Write-Host "Creating sample status files..." -ForegroundColor Yellow

# Sample download status JSON content
$sampleStatus = @{
    pluginVersion = "1.0.0"
    lastUpdated = (Get-Date).ToString("o")
    totalPendingDownloads = 10
    totalCompletedDownloads = 25
    totalFailedDownloads = 2
    downloadRate = 12
    sessionsCompleted = 3
    isHighVolumeMode = $false
    artistStats = @{
        "Test Artist" = @{
            artistName = "Test Artist"
            pendingTracks = 5
            completedTracks = 10
            failedTracks = 1
            albums = @("Album 1", "Album 2")
        }
    }
    recentDownloads = @(
        @{
            title = "Track 1"
            artist = "Test Artist"
            album = "Album 1"
            status = "Completed"
            timestamp = (Get-Date).AddMinutes(-5).ToString("o")
        },
        @{
            title = "Track 2"
            artist = "Test Artist"
            album = "Album 1"
            status = "Completed"
            timestamp = (Get-Date).AddMinutes(-10).ToString("o")
        }
    )
}

# Convert to JSON and save to file
$sampleStatusJson = $sampleStatus | ConvertTo-Json -Depth 10
$sampleStatusJson | Out-File -FilePath $sampleJson -Force -Encoding utf8

Write-Host "Sample status file created: $sampleJson" -ForegroundColor Green
Write-Host "Content:" -ForegroundColor Green
Get-Content $sampleJson | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }

# Create a basic test JSON file
$testData = @{
    test = $true
    timestamp = (Get-Date).ToString("o")
    message = "This is a test status file to verify that the directory is writable"
}

$testDataJson = $testData | ConvertTo-Json -Depth 5
$testDataJson | Out-File -FilePath $statusFile -Force -Encoding utf8

Write-Host "Test status file created: $statusFile" -ForegroundColor Green

# Verify files were created
Write-Host "`nVerifying files..." -ForegroundColor Yellow
$allFiles = Get-ChildItem $testDir -File
Write-Host "Found $($allFiles.Count) files in test directory:" -ForegroundColor Green
$allFiles | ForEach-Object { Write-Host "  $($_.Name) - $([math]::Round($_.Length/1KB, 2)) KB" -ForegroundColor Gray }

# Create a manual debug report
Write-Host "`nCreating a debug report..." -ForegroundColor Yellow

# Create the file inventory
$fileInventory = ""
$allFiles | ForEach-Object { 
    $fileInventory += "$($_.Name) - $([math]::Round($_.Length/1KB, 2)) KB - Modified: $($_.LastWriteTime)`n" 
}

# Create the JSON validation section
$jsonValidation = ""
$jsonFiles = $allFiles | Where-Object { $_.Extension -eq '.json' }
foreach ($file in $jsonFiles) {
    $content = Get-Content $file.FullName -Raw
    $valid = $true
    try {
        $null = ConvertFrom-Json $content -ErrorAction Stop
    } catch {
        $valid = $false
    }
    $jsonValidation += "$($file.Name) - $(if ($valid) { "Valid JSON" } else { "Invalid JSON" })`n"
}

$debugReport = @"
=== TIDAL STATUS FILES DEBUG REPORT ===
Generated at: $(Get-Date)
Status Directory: $testDir
Directory Exists: $(Test-Path $testDir)
Directory Creation Time: $((Get-Item $testDir).CreationTime)
Directory Last Modified: $((Get-Item $testDir).LastWriteTime)

Files Found: $($allFiles.Count)

FILE INVENTORY:
---------------
$fileInventory

JSON Validation:
---------------
$jsonValidation

=== END OF REPORT ===
"@

$debugReport | Out-File -FilePath $debugReportPath -Force -Encoding utf8
Write-Host "Debug report created: $debugReportPath" -ForegroundColor Green

# Check for the Lidarr plugins directory
Write-Host "`nChecking for Lidarr plugins directory..." -ForegroundColor Yellow

# Common Lidarr installation paths to check
$possibleLidarrPluginPaths = @(
    # Windows paths
    "$env:ProgramData\Lidarr\plugins",
    "$env:LOCALAPPDATA\Lidarr\plugins",
    "$env:APPDATA\Lidarr\plugins",
    # Linux paths (accessed via WSL or PowerShell Core)
    "/config/plugins", # Typical Docker volume mount point
    "/data/lidarr/plugins",
    "$env:HOME/.config/Lidarr/plugins",
    # macOS paths (for PowerShell Core)
    "$env:HOME/Library/Application Support/Lidarr/plugins"
)

# Add custom path if specified
if (![string]::IsNullOrEmpty($CustomPluginPath)) {
    $possibleLidarrPluginPaths = @($CustomPluginPath) + $possibleLidarrPluginPaths
}

$foundPluginPath = $null
foreach ($path in $possibleLidarrPluginPaths) {
    if (Test-Path $path) {
        $foundPluginPath = $path
        Write-Host "Found Lidarr plugins directory: $path" -ForegroundColor Green
        break
    }
}

if ($foundPluginPath) {
    # See if our plugin is installed
    $tidalPluginPath = Join-Path $foundPluginPath "alexricher\Lidarr.Plugin.Tidal"
    if (Test-Path $tidalPluginPath) {
        Write-Host "Tidal plugin is installed at: $tidalPluginPath" -ForegroundColor Green
        
        # Check if it has a status directory configured
        $tidalSettingsPath = Join-Path $tidalPluginPath "settings.json"
        if (Test-Path $tidalSettingsPath) {
            try {
                $tidalSettings = Get-Content $tidalSettingsPath -Raw | ConvertFrom-Json
                if ($tidalSettings.StatusFilesPath) {
                    Write-Host "Tidal plugin has status path configured: $($tidalSettings.StatusFilesPath)" -ForegroundColor Green
                    
                    # Check if directory exists
                    if (Test-Path $tidalSettings.StatusFilesPath) {
                        Write-Host "Status directory exists!" -ForegroundColor Green
                        Write-Host "Contents:" -ForegroundColor Green
                        Get-ChildItem $tidalSettings.StatusFilesPath | ForEach-Object { 
                            Write-Host "  $($_.Name) - $(if ($_.PSIsContainer) { "Directory" } else { "$([math]::Round($_.Length/1KB, 2)) KB" })" -ForegroundColor Gray 
                        }
                    } else {
                        Write-Host "Status directory does not exist: $($tidalSettings.StatusFilesPath)" -ForegroundColor Red
                        
                        # Offer to create it
                        $createDir = Read-Host "Would you like to create the status directory? (y/n)"
                        if ($createDir -eq "y") {
                            try {
                                New-Item -ItemType Directory -Path $tidalSettings.StatusFilesPath -Force | Out-Null
                                Write-Host "Created status directory: $($tidalSettings.StatusFilesPath)" -ForegroundColor Green
                                
                                # Copy sample files
                                Copy-Item -Path $statusFile -Destination (Join-Path $tidalSettings.StatusFilesPath "status_test.json") -Force
                                Copy-Item -Path $sampleJson -Destination (Join-Path $tidalSettings.StatusFilesPath "sample_status.json") -Force
                                Copy-Item -Path $debugReportPath -Destination (Join-Path $tidalSettings.StatusFilesPath "status_debug_report.txt") -Force
                                
                                Write-Host "Copied sample files to the status directory" -ForegroundColor Green
                            } catch {
                                Write-Host "Failed to create directory: $_" -ForegroundColor Red
                            }
                        }
                    }
                } else {
                    Write-Host "Tidal plugin does not have a status path configured" -ForegroundColor Yellow
                    Write-Host "You should set this in the Lidarr UI under Settings > Download Clients > Tidal" -ForegroundColor Yellow
                }
            } catch {
                Write-Host "Error reading Tidal settings: $_" -ForegroundColor Red
            }
        } else {
            Write-Host "Tidal plugin settings file not found: $tidalSettingsPath" -ForegroundColor Yellow
        }
    } else {
        Write-Host "Tidal plugin is not installed at: $tidalPluginPath" -ForegroundColor Yellow
    }
} else {
    Write-Host "Could not find Lidarr plugins directory" -ForegroundColor Yellow
    Write-Host "Please manually configure the status path in Lidarr" -ForegroundColor Yellow
}

# Clean up test files if not keeping them
if (!$KeepTestFiles) {
    Write-Host "`nCleaning up test files..." -ForegroundColor Yellow
    
    # Delete specific test files
    $testFilesToDelete = @(
        $statusFile,
        $sampleJson,
        $debugReportPath
    )
    
    foreach ($file in $testFilesToDelete) {
        if (Test-Path $file) {
            Remove-Item -Path $file -Force
            Write-Host "  Deleted: $file" -ForegroundColor Gray
        }
    }
    
    # Delete any other temporary files
    $tempFiles = Get-ChildItem -Path $testDir -Filter "write_test_*.tmp" -File
    foreach ($file in $tempFiles) {
        Remove-Item -Path $file.FullName -Force
        Write-Host "  Deleted: $($file.FullName)" -ForegroundColor Gray
    }
    
    # Check if the directory is now empty
    $remainingFiles = Get-ChildItem -Path $testDir -File
    if ($remainingFiles.Count -eq 0) {
        Write-Host "All test files cleaned up successfully" -ForegroundColor Green
        
        # Ask if the user wants to delete the test directory
        $removeDir = Read-Host "Would you like to delete the empty test directory? (y/n)"
        if ($removeDir -eq "y") {
            Remove-Item -Path $testDir -Force
            Write-Host "Deleted test directory: $testDir" -ForegroundColor Green
        }
    } else {
        Write-Host "Some files remain in the test directory:" -ForegroundColor Yellow
        $remainingFiles | ForEach-Object { Write-Host "  $($_.Name)" -ForegroundColor Gray }
    }
} else {
    Write-Host "`nKeeping test files in: $testDir" -ForegroundColor Yellow
}

Write-Host "`nStatus files test completed!" -ForegroundColor Green
Write-Host "To use this directory as your status files path in Lidarr, set it to:" -ForegroundColor Green
Write-Host $testDir -ForegroundColor Cyan
Write-Host "To clean up test files later, run: ./test-status-files.ps1 -CleanOnly" -ForegroundColor Green 