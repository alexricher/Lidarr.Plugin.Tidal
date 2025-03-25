# This script cleans up SQLite files that might cause conflicts during the build process
# Run this script before running build-local.ps1 if you encounter SQLite conflicts

Write-Host "===== COMPREHENSIVE SQLITE CLEANUP =====" -ForegroundColor Cyan
Write-Host "Cleaning up SQLite files to prevent build conflicts..." -ForegroundColor Yellow

# Get the user's NuGet packages directory
$nugetPackagesDir = "$env:USERPROFILE\.nuget\packages"
if (!(Test-Path $nugetPackagesDir)) {
    # Try alternative location for non-Windows
    $nugetPackagesDir = "$HOME/.nuget/packages"
}

# Also remove the whole SQLite package to force a clean restore
$sqlitePackageDir = Join-Path $nugetPackagesDir "system.data.sqlite.core.servarr"
if (Test-Path $sqlitePackageDir) {
    Write-Host "Found SQLite package at: $sqlitePackageDir" -ForegroundColor Yellow
    Write-Host "Renaming entire SQLite package directory to force a clean restore..." -ForegroundColor Yellow
    
    # Force close any open handles
    [System.GC]::Collect()
    [System.GC]::WaitForPendingFinalizers()
    
    # Try to rename the entire directory
    $timestamp = Get-Date -Format "yyyyMMddHHmmss"
    $backupDir = "$sqlitePackageDir.bak$timestamp"
    
    try {
        Rename-Item -Path $sqlitePackageDir -NewName $backupDir -Force -ErrorAction Stop
        Write-Host "Successfully renamed SQLite package directory to: $backupDir" -ForegroundColor Green
    } catch {
        Write-Host "Could not rename entire directory. Will try to rename individual files." -ForegroundColor Yellow
    }
}

# Find all SQLite packages
Write-Host "Searching for all SQLite-related packages..." -ForegroundColor Yellow
$sqlitePackageDirs = Get-ChildItem -Path $nugetPackagesDir -Filter "*sqlite*" -Directory -ErrorAction SilentlyContinue
foreach ($dir in $sqlitePackageDirs) {
    Write-Host "  Found SQLite-related package: $($dir.FullName)" -ForegroundColor Gray
    
    # Find all sqlite3.dll files
    $sqliteFiles = Get-ChildItem -Path $dir.FullName -Filter "sqlite3.dll" -Recurse -ErrorAction SilentlyContinue
    
    if ($sqliteFiles.Count -gt 0) {
        Write-Host "  Found $($sqliteFiles.Count) SQLite DLLs in this package" -ForegroundColor Yellow
        
        foreach ($file in $sqliteFiles) {
            # Check if this is a .NET Framework version
            $isNetFramework = $file.FullName -like "*net4*" -or $file.Directory.FullName -like "*net4*"
            
            if ($isNetFramework) {
                Write-Host "    Renaming .NET Framework SQLite DLL: $($file.FullName)" -ForegroundColor Yellow
                $timestamp = Get-Date -Format "yyyyMMddHHmmss"
                $newName = "sqlite3.dll.bak$timestamp"
                
                try {
                    Rename-Item -Path $file.FullName -NewName $newName -Force -ErrorAction Stop
                    Write-Host "      Successfully renamed to $newName" -ForegroundColor Green
                } catch {
                    Write-Host "      Failed to rename: $_" -ForegroundColor Red
                    
                    # Try deleting instead
                    try {
                        Remove-Item -Path $file.FullName -Force -ErrorAction Stop
                        Write-Host "      Deleted file instead" -ForegroundColor Green
                    } catch {
                        Write-Host "      Could not delete either. You may need to close all instances of Visual Studio/MSBuild and run as administrator." -ForegroundColor Red
                    }
                }
            } else {
                Write-Host "    Found non-framework SQLite DLL (keeping): $($file.FullName)" -ForegroundColor Gray
            }
        }
    }
}

# Clean all build and obj directories
Write-Host "Cleaning all build output directories..." -ForegroundColor Yellow
$buildDirs = @(
    ".\src\Lidarr.Plugin.Tidal\bin",
    ".\src\Lidarr.Plugin.Tidal\obj",
    ".\src\TidalSharp\bin",
    ".\src\TidalSharp\obj"
)

foreach ($dir in $buildDirs) {
    if (Test-Path $dir) {
        Write-Host "  Removing $dir..." -ForegroundColor Gray
        
        # Force close any open handles
        [System.GC]::Collect()
        [System.GC]::WaitForPendingFinalizers()
        
        try {
            Remove-Item -Path $dir -Recurse -Force -ErrorAction Stop
            Write-Host "    Successfully removed" -ForegroundColor Green
        } catch {
            Write-Host "    Failed to remove: $_" -ForegroundColor Red
        }
    }
}

# Create a temporary NuGet.Config to use a different global packages folder
Write-Host "Creating temporary NuGet.Config to use a clean packages folder..." -ForegroundColor Yellow
$nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <config>
    <add key="globalPackagesFolder" value="$([System.IO.Path]::GetFullPath(".\packages"))" />
  </config>
</configuration>
"@

$nugetConfigPath = ".\NuGet.Config"
Set-Content -Path $nugetConfigPath -Value $nugetConfig

# Create Directory.Build.targets
Write-Host "Creating a more comprehensive Directory.Build.targets file..." -ForegroundColor Yellow
$targetFile = @"
<Project>
  <!-- Exclude problematic SQLite files during build and publish -->
  <PropertyGroup>
    <!-- Exclude SQLite core package from publishing -->
    <SuppressSystemDataSQLite>true</SuppressSystemDataSQLite>
    <PublishReadyToRunExclude>System.Data.SQLite.Core.Servarr</PublishReadyToRunExclude>
  </PropertyGroup>
  
  <!-- Handle file conflicts pre-emptively -->
  <Target Name="RemoveSQLiteConflictsDuringBuild" BeforeTargets="Build;Restore;ResolvePackageAssets">
    <Message Text="Removing SQLite conflicts during build..." Importance="high" />
    <ItemGroup>
      <!-- Remove problematic references -->
      <Reference Remove="@(Reference)" Condition="$([System.String]::Copy('%(Reference.Identity)').Contains('System.Data.SQLite'))" />
      <ReferenceCopyLocalPaths Remove="@(ReferenceCopyLocalPaths)" 
                              Condition="$([System.String]::Copy('%(ReferenceCopyLocalPaths.DestinationSubPath)').Contains('System.Data.SQLite')) Or 
                                        $([System.String]::Copy('%(ReferenceCopyLocalPaths.DestinationSubPath)').Contains('sqlite3.dll'))" />
    </ItemGroup>
  </Target>
  
  <!-- Special publish target -->
  <Target Name="HandleSQLiteConflicts" BeforeTargets="ComputeFilesToPublish;_HandleFileConflictsForPublish">
    <Message Text="Handling SQLite conflicts during publish..." Importance="high" />
    <ItemGroup>
      <!-- Remove problematic publish files -->
      <ResolvedFileToPublish Remove="@(ResolvedFileToPublish)" 
                            Condition="$([System.String]::Copy('%(ResolvedFileToPublish.OriginalItemSpec)').Contains('system.data.sqlite')) Or
                                      $([System.String]::Copy('%(ResolvedFileToPublish.OriginalItemSpec)').Contains('sqlite3.dll'))" />
    </ItemGroup>
  </Target>
</Project>
"@

$targetFilePath = ".\src\Directory.Build.targets"
Set-Content -Path $targetFilePath -Value $targetFile

# Write a helper batch file that reconfigures dotnet before building
$batchFile = @"
@echo off
echo Setting up environment for clean build...
if exist "packages" rmdir /s /q packages
md packages
set NUGET_PACKAGES=%CD%\packages
dotnet restore src\Lidarr.Plugin.Tidal.sln --force
echo Environment setup complete. You can now run the build script.
"@

$batchFilePath = ".\clean-setup.bat"
Set-Content -Path $batchFilePath -Value $batchFile

Write-Host "===== CLEANUP COMPLETED =====" -ForegroundColor Green
Write-Host "1. Created temporary NuGet.Config to use a clean local packages folder" -ForegroundColor Cyan
Write-Host "2. Added more comprehensive Directory.Build.targets to handle SQLite conflicts" -ForegroundColor Cyan
Write-Host "3. Cleaned all build output directories" -ForegroundColor Cyan
Write-Host "4. Created clean-setup.bat to set up an isolated environment" -ForegroundColor Cyan
Write-Host
Write-Host "NEXT STEPS:" -ForegroundColor Magenta
Write-Host "1. Run clean-setup.bat to create an isolated build environment" -ForegroundColor Magenta
Write-Host "2. Then run your build-local.ps1 script" -ForegroundColor Magenta 