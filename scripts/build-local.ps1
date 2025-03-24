param (
    [switch]$CreateZip = $true,
    [string]$OutputPath = "X:\lidarr-hotio-test\plugins\alexricher\Lidarr.Plugin.Tidal",
    [switch]$CopyToLocalPath = $true,
    [switch]$CleanPreviousBuilds = $true,
    [switch]$UpdateLidarrRepo = $false,
    [string]$LidarrRepoVersion = "latest",
    [string]$Framework = "net6.0"
)

# Set default values as fallbacks if workflow file can't be read
$PluginName = "Lidarr.Plugin.Tidal"
$BaseVersion = "10.0.1" # Fallback version if not found in workflow file
$MinimumLidarrVersion = "2.2.4.4129"
$DotnetVersion = "8.0.404"
$Framework = $Framework  # Use the parameter value or default
$SolutionPath = ""

# Dictionary to store original file contents
$originalFileContents = @{}

# Function to backup file contents
function BackupFileContents {
    param (
        [string]$filePath
    )
    
    if (Test-Path $filePath) {
        $originalFileContents[$filePath] = Get-Content $filePath -Raw
        Write-Host "  Backed up content of $filePath" -ForegroundColor Gray
    }
}

# Function to restore file contents
function RestoreFileContents {
    param (
        [string]$filePath
    )
    
    if ($originalFileContents.ContainsKey($filePath) -and (Test-Path $filePath)) {
        $originalFileContents[$filePath] | Set-Content $filePath -NoNewline
        Write-Host "  Restored content of $filePath" -ForegroundColor Gray
    }
}

# Files to track for restoration
$filesToRestore = @(
    "./src/Directory.Build.props",
    "./ext/Lidarr/src/Directory.Build.props"
)

# Restore files function
function RestoreFiles {
    Write-Host "Restoring build files to original state..." -ForegroundColor Yellow
    foreach ($file in $filesToRestore) {
        if (Test-Path $file) {
            # First try Git if the file is tracked
            $isTracked = git ls-files --error-unmatch $file 2>$null
            if ($LASTEXITCODE -eq 0) {
                # File is tracked by Git, restore it
                git checkout -- $file
                Write-Host "  Restored $file from Git" -ForegroundColor Gray
            } else {
                # File is not tracked by Git, restore from backup
                RestoreFileContents $file
            }
        }
    }
}

# Read configuration from GitHub workflow file
$workflowFile = ".github/workflows/build.yml"
if (Test-Path $workflowFile) {
    Write-Host "Reading configuration from GitHub workflow file..." -ForegroundColor Yellow
    
    $workflowContent = Get-Content $workflowFile -Raw
    
    # Extract plugin name
    if ($workflowContent -match "PLUGIN_NAME:\s*([^\s]+)") {
        $PluginName = $matches[1]
    }
    
    # Extract base version from env variables in workflow file
    if ($workflowContent -match "MAJOR_VERSION:\s*(\d+)[\s\r\n]+\s*MINOR_VERSION:\s*(\d+)[\s\r\n]+\s*PATCH_VERSION:\s*(\d+)") {
        $BaseVersion = "$($matches[1]).$($matches[2]).$($matches[3])"
        Write-Host "  Found version $BaseVersion in workflow file" -ForegroundColor Gray
    }
    
    # Extract minimum Lidarr version
    if ($workflowContent -match "MINIMUM_LIDARR_VERSION:\s*([^\s]+)") {
        $MinimumLidarrVersion = $matches[1]
    }
    
    # Extract dotnet version
    if ($workflowContent -match "DOTNET_VERSION:\s*([^\s]+)") {
        $DotnetVersion = $matches[1]
    }
    
    # Extract framework
    if ($workflowContent -match "FRAMEWORK:\s*([^\s]+)") {
        $workflowFramework = $matches[1].Trim("'").Trim('"')
        # Only override if not explicitly provided as parameter
        if (-not $PSBoundParameters.ContainsKey('Framework')) {
            $Framework = $workflowFramework
        }
    }
    
    # Extract solution path
    if ($workflowContent -match "SOLUTION_PATH:\s*([^\s]+)") {
        $SolutionPath = $matches[1]
    }
} else {
    Write-Host "Warning: GitHub workflow file not found. Using default values." -ForegroundColor Red
}

# Create a local build number file if it doesn't exist
$buildNumberFile = ".\.build_number"
if (Test-Path $buildNumberFile) {
    $BuildNumber = [int](Get-Content $buildNumberFile)
} else {
    $BuildNumber = 1
}

# Use the build number for version
$PluginVersion = "$BaseVersion.$BuildNumber"

Write-Host "Building $PluginName version $PluginVersion" -ForegroundColor Cyan
Write-Host "Using .NET version $DotnetVersion and framework $Framework" -ForegroundColor Cyan

try {
    # Update Lidarr repository
    if ($UpdateLidarrRepo) {
        Write-Host "Updating Lidarr repository..." -ForegroundColor Yellow
        
        # Save current directory
        $currentDir = Get-Location
        
        # Navigate to Lidarr directory
        Set-Location "ext\Lidarr"
        
        # Stash any changes
        git stash
        
        # Checkout the plugins branch instead of develop
        git checkout plugins
        
        # Pull latest changes
        git pull origin plugins
        
        # Return to original directory
        Set-Location $currentDir
        
        Write-Host "Updated Lidarr repository to latest plugins branch version" -ForegroundColor Green
    }

    # Clean previous builds if requested
    if ($CleanPreviousBuilds) {
        Write-Host "Cleaning previous builds..." -ForegroundColor Yellow
        
        # Clean temp directories first
        if (Test-Path "./_temp") { Remove-Item -Recurse -Force "./_temp" -ErrorAction SilentlyContinue }
        if (Test-Path "./_temp_package") { Remove-Item -Recurse -Force "./_temp_package" -ErrorAction SilentlyContinue }
        
        # Force garbage collection to release any handles to zip files
        [System.GC]::Collect()
        [System.GC]::WaitForPendingFinalizers()
        
        # Now try to remove plugins and artifacts
        if (Test-Path "./_plugins") { Remove-Item -Recurse -Force "./_plugins" -ErrorAction SilentlyContinue }
        
        if (Test-Path "./_artifacts") { 
            Remove-Item -Recurse -Force "./_artifacts" -ErrorAction SilentlyContinue
            
            # If it still can't be deleted, create a fresh directory instead
            if (Test-Path "./_artifacts") {
                Write-Host "Warning: Could not remove artifacts directory completely. Will use a new name instead." -ForegroundColor Yellow
                $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
                $artifactsDir = "./_artifacts_$timestamp"
                New-Item -ItemType Directory -Force -Path $artifactsDir | Out-Null
                $script:OutputPath = $artifactsDir
            } else {
                # Re-create the directory
                New-Item -ItemType Directory -Force -Path "./_artifacts" | Out-Null
            }
        } else {
            # Create the directory if it doesn't exist
            New-Item -ItemType Directory -Force -Path "./_artifacts" | Out-Null
        }
    }

    # Backup original file contents
    Write-Host "Backing up original file contents..." -ForegroundColor Yellow
    foreach ($file in $filesToRestore) {
        BackupFileContents $file
    }

    # Update version info in Directory.Build.props
    Write-Host "Updating version info..." -ForegroundColor Yellow
    $srcProps = "./src/Directory.Build.props"
    $lidarrProps = "./ext/Lidarr/src/Directory.Build.props"

    # Generate branch name similar to GitHub Actions
    $branch = git rev-parse --abbrev-ref HEAD
    $branch = $branch -replace "/", "-"

    # Update version in src/Directory.Build.props
    if (Test-Path $srcProps) {
        (Get-Content $srcProps) -replace '<AssemblyVersion>[0-9.*]+</AssemblyVersion>', "<AssemblyVersion>$PluginVersion</AssemblyVersion>" |
            Set-Content $srcProps
        (Get-Content $srcProps) -replace '<AssemblyConfiguration>[\$()A-Za-z-]+</AssemblyConfiguration>', "<AssemblyConfiguration>$branch</AssemblyConfiguration>" |
            Set-Content $srcProps
    }

    # Update version in ext/Lidarr/src/Directory.Build.props
    if (Test-Path $lidarrProps) {
        (Get-Content $lidarrProps) -replace '<AssemblyVersion>[0-9.*]+</AssemblyVersion>', "<AssemblyVersion>$MinimumLidarrVersion</AssemblyVersion>" |
            Set-Content $lidarrProps
    }

    # Create global.json with the correct .NET version
    Write-Host "Creating global.json with .NET version $DotnetVersion..." -ForegroundColor Yellow
    @"
{
  "sdk": {
    "version": "$DotnetVersion"
  }
}
"@ | Set-Content "./global.json"

    # Build the solution
    Write-Host "Building solution..." -ForegroundColor Yellow

    # First build the Lidarr solution to ensure dependencies are available
    if (Test-Path "ext\Lidarr\src\Lidarr.sln") {
        Write-Host "Building Lidarr solution..." -ForegroundColor Yellow
        
        # Create a specific output directory for Lidarr
        $lidarrOutputPath = Join-Path (Get-Location).Path "_output\lidarr"
        if (-not (Test-Path $lidarrOutputPath)) {
            New-Item -ItemType Directory -Path $lidarrOutputPath -Force | Out-Null
        }
        
        dotnet restore "ext\Lidarr\src\Lidarr.sln"
        dotnet build "ext\Lidarr\src\Lidarr.sln" -c Release --no-restore -o "$lidarrOutputPath"
    }

    # Then build the plugin
    if (Test-Path "src\Lidarr.Plugin.Tidal\Lidarr.Plugin.Tidal.csproj") {
        Write-Host "Building plugin..." -ForegroundColor Yellow
        
        # Clean output directories to ensure no old files interfere
        Write-Host "Cleaning output directories..." -ForegroundColor Yellow
        $outputDir = "./_plugins/$PluginName"
        if (Test-Path $outputDir) { Remove-Item -Recurse -Force $outputDir }
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
        
        # Also clean any previous build output that might interfere
        $buildOutputDir = "./src/$PluginName/bin"
        if (Test-Path $buildOutputDir) { 
            Write-Host "Cleaning previous build output at $buildOutputDir..." -ForegroundColor Yellow
            Remove-Item -Recurse -Force $buildOutputDir 
        }
        
        Write-Host "Restoring NuGet packages..." -ForegroundColor Yellow
        dotnet restore "src\Lidarr.Plugin.Tidal\Lidarr.Plugin.Tidal.csproj"
        
        # Build with MSBuild to ensure ILRepack target is executed
        Write-Host "Building plugin with MSBuild to trigger ILRepack..." -ForegroundColor Yellow
        
        # Get absolute path to the output directory
        $absOutputPath = Join-Path (Get-Location).Path "_plugins\$PluginName"
        Write-Host "Output path: $absOutputPath" -ForegroundColor Yellow
        
        $buildResult = dotnet msbuild "src\Lidarr.Plugin.Tidal\Lidarr.Plugin.Tidal.csproj" `
            /p:Configuration=Release `
            /p:TargetFramework=$Framework `
            /p:Version=$PluginVersion `
            /p:OutputPath="$absOutputPath" `
            /p:TreatWarningsAsErrors=false `
            /p:RunAnalyzers=false `
            /t:Clean,Build,ILRepacker `
            /v:n
        
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Build failed with exit code $LASTEXITCODE" -ForegroundColor Red
            Write-Host $buildResult -ForegroundColor Red
            exit $LASTEXITCODE
        }
        
        # Verify build output
        $mainDllPath = "$outputDir/$PluginName.dll"
        if (Test-Path $mainDllPath) {
            $dllSize = (Get-Item $mainDllPath).Length/1KB
            Write-Host "Main plugin DLL built successfully: $([Math]::Round($dllSize, 2)) KB" -ForegroundColor Green
            
            if ($dllSize -lt 1000) {
                Write-Host "Warning: Plugin DLL size ($([Math]::Round($dllSize, 2)) KB) is smaller than expected (< 1MB). Dependencies may not have been merged correctly." -ForegroundColor Yellow
            } else {
                Write-Host "DLL size indicates dependencies were successfully merged" -ForegroundColor Green
            }
        } else {
            Write-Host "Error: Plugin DLL was not created at $mainDllPath" -ForegroundColor Red
            exit 1
        }
        
        # List all files in the output directory
        Write-Host "Files in plugin output directory:" -ForegroundColor Yellow
        Get-ChildItem $outputDir | ForEach-Object {
            Write-Host "  $($_.Name) ($([Math]::Round($_.Length/1KB, 2)) KB)" -ForegroundColor Gray
        }
    } else {
        Write-Host "Plugin project file not found!" -ForegroundColor Red
        exit 1
    }

    # Increment build number for next time
    $BuildNumber++ | Out-File $buildNumberFile
    Write-Host "Build number incremented to $BuildNumber for next build" -ForegroundColor Green

    # Create zip file if requested
    if ($CreateZip) {
        Write-Host "Creating zip file..." -ForegroundColor Yellow
        $rootPath = (Get-Location).Path
        $pluginPath = Join-Path $rootPath "_plugins\$PluginName"
        $zipPath = Join-Path $rootPath "_artifacts\$PluginName.$Framework.zip"
        
        # Create artifacts directory if it doesn't exist
        if (-not (Test-Path "./_artifacts")) {
            New-Item -ItemType Directory -Path "./_artifacts" -Force | Out-Null
        }
        
        if (Test-Path $pluginPath) {
            # Create a temporary directory
            $tempDir = "./_temp_package"
            if (Test-Path $tempDir) { Remove-Item -Recurse -Force $tempDir }
            New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
            
            # Copy the main DLL with verification
            $mainDllPath = "$pluginPath/$PluginName.dll"
            $tempDllPath = "$tempDir/$PluginName.dll"
            
            if (Test-Path $mainDllPath) {
                # Get source file info before copy
                $sourceDll = Get-Item $mainDllPath
                $sourceDllSize = $sourceDll.Length
                
                Write-Host "Source DLL: $mainDllPath" -ForegroundColor Yellow
                Write-Host "  Size: $([Math]::Round($sourceDllSize/1KB, 2)) KB" -ForegroundColor Yellow
                
                # Use basic Copy-Item instead of System.IO.File::Copy
                Copy-Item -Path $mainDllPath -Destination $tempDllPath -Force
                
                # Verify the copied file
                if (Test-Path $tempDllPath) {
                    $destDll = Get-Item $tempDllPath
                    $destDllSize = $destDll.Length
                    
                    Write-Host "Copied DLL: $tempDllPath" -ForegroundColor Yellow
                    Write-Host "  Size: $([Math]::Round($destDllSize/1KB, 2)) KB" -ForegroundColor Yellow
                    
                    # Verify size matches
                    if ($sourceDllSize -ne $destDllSize) {
                        Write-Host "ERROR: File size mismatch after copy! Source: $sourceDllSize bytes, Destination: $destDllSize bytes" -ForegroundColor Red
                    }
                } else {
                    Write-Host "ERROR: Failed to copy DLL to temp directory!" -ForegroundColor Red
                }
            } else {
                Write-Host "ERROR: Plugin DLL not found at $mainDllPath" -ForegroundColor Red
            }
            
            # Copy PDB file
            $pdbPath = "$pluginPath/$PluginName.pdb"
            $tempPdbPath = "$tempDir/$PluginName.pdb"
            
            if (Test-Path $pdbPath) {
                $sourcePdbSize = (Get-Item $pdbPath).Length
                Write-Host "Copying PDB file ($([Math]::Round($sourcePdbSize/1KB, 2)) KB)" -ForegroundColor Yellow
                Copy-Item -Path $pdbPath -Destination $tempDir -Force
            }
            
            # Copy JSON file (deps.json)
            $depsJsonPath = "$pluginPath/$PluginName.deps.json"
            
            if (Test-Path $depsJsonPath) {
                $sourceJsonSize = (Get-Item $depsJsonPath).Length
                Write-Host "Copying deps.json file ($([Math]::Round($sourceJsonSize/1KB, 2)) KB)" -ForegroundColor Yellow
                Copy-Item -Path $depsJsonPath -Destination $tempDir -Force
            } else {
                Write-Host "ERROR: deps.json file not found at $depsJsonPath. This file is required for the plugin to work." -ForegroundColor Red
            }
            
            # List all files in temp directory before zip
            Write-Host "Files in temp directory before zip:" -ForegroundColor Yellow
            Get-ChildItem $tempDir | ForEach-Object {
                Write-Host "  $($_.Name) ($([Math]::Round($_.Length/1KB, 2)) KB)" -ForegroundColor Gray
            }
            
            # Create the zip file
            Write-Host "Creating zip archive..." -ForegroundColor Yellow
            
            # Make sure the destination path exists
            $zipDir = Split-Path -Parent $zipPath
            if (-not (Test-Path $zipDir)) {
                New-Item -ItemType Directory -Path $zipDir -Force | Out-Null
            }
            
            # If zip already exists, try to remove it first
            if (Test-Path $zipPath) {
                try {
                    Remove-Item -Path $zipPath -Force -ErrorAction Stop
                } catch {
                    Write-Host "Warning: Could not remove existing zip file. Using a different filename." -ForegroundColor Yellow
                    $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
                    $zipFileName = [System.IO.Path]::GetFileNameWithoutExtension($zipPath) + "_$timestamp" + [System.IO.Path]::GetExtension($zipPath)
                    $zipPath = Join-Path (Split-Path -Parent $zipPath) $zipFileName
                }
            }
            
            # Create the zip file
            Compress-Archive -Path "$tempDir/*" -DestinationPath $zipPath -Force
            
            # Force closure of any handles after zip creation
            [System.GC]::Collect()
            [System.GC]::WaitForPendingFinalizers()
            
            # Verify the zip file
            if (Test-Path $zipPath) {
                $zipInfo = Get-Item $zipPath
                Write-Host "Created zip file: $zipPath ($([Math]::Round($zipInfo.Length/1KB, 2)) KB)" -ForegroundColor Green
                
                # Add a verification step to check the contents of the zip
                try {
                    # Create a temporary directory to extract the zip for verification
                    $verifyDir = "./_verify_zip"
                    if (Test-Path $verifyDir) { 
                        # Force GC before trying to delete
                        [System.GC]::Collect()
                        [System.GC]::WaitForPendingFinalizers()
                        Remove-Item -Recurse -Force $verifyDir -ErrorAction SilentlyContinue 
                        
                        # If directory couldn't be removed, use a timestamped directory
                        if (Test-Path $verifyDir) {
                            $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
                            $verifyDir = "./_verify_zip_$timestamp"
                        }
                    }
                    
                    New-Item -ItemType Directory -Path $verifyDir -Force | Out-Null
                    
                    # Extract and verify the zip
                    Write-Host "Verifying zip contents..." -ForegroundColor Yellow
                    Expand-Archive -Path $zipPath -DestinationPath $verifyDir -Force
                    
                    $verifyFiles = Get-ChildItem $verifyDir -File
                    Write-Host "Files in zip archive:" -ForegroundColor Yellow
                    foreach ($file in $verifyFiles) {
                        Write-Host "  $($file.Name) ($([Math]::Round($file.Length/1KB, 2)) KB)" -ForegroundColor Gray
                    }
                    
                    # Check for required files
                    $requiredFiles = @(
                        @{Name = "$PluginName.dll"; Description = "Main plugin DLL"},
                        @{Name = "$PluginName.pdb"; Description = "Debug symbols"},
                        @{Name = "$PluginName.deps.json"; Description = "Dependencies JSON"}
                    )
                    
                    $missingFiles = $false
                    foreach ($reqFile in $requiredFiles) {
                        $filePath = "$verifyDir/$($reqFile.Name)"
                        if (Test-Path $filePath) {
                            $fileSize = (Get-Item $filePath).Length/1KB
                            Write-Host "Found $($reqFile.Description): $($reqFile.Name) ($([Math]::Round($fileSize, 2)) KB)" -ForegroundColor Green
                        } else {
                            Write-Host "MISSING $($reqFile.Description): $($reqFile.Name)" -ForegroundColor Red
                            $missingFiles = $true
                        }
                    }
                    
                    if ($missingFiles) {
                        Write-Host "WARNING: Some required files are missing from the zip! The plugin may not work correctly." -ForegroundColor Red
                    } else {
                        # Check sizes
                        $dllVerifyPath = "$verifyDir/$PluginName.dll"
                        $dllVerifySize = (Get-Item $dllVerifyPath).Length/1KB
                        if ($dllVerifySize -lt 100) {
                            Write-Host "WARNING: DLL size ($([Math]::Round($dllVerifySize, 2)) KB) is smaller than expected! This may indicate a problem." -ForegroundColor Red
                        } else {
                            Write-Host "All required files are present in the zip and have reasonable sizes." -ForegroundColor Green
                        }
                    }
                }
                catch {
                    Write-Host "ERROR during verification: $_" -ForegroundColor Red
                }
                finally {
                    # Clean up verification directory
                    if (Test-Path $verifyDir) { 
                        # Force GC before trying to delete
                        [System.GC]::Collect()
                        [System.GC]::WaitForPendingFinalizers()
                        Remove-Item -Recurse -Force $verifyDir -ErrorAction SilentlyContinue 
                    }
                }
            } else {
                Write-Host "ERROR: Failed to create zip file!" -ForegroundColor Red
            }
            
            # Clean up temp directory
            # Force GC before trying to delete
            [System.GC]::Collect()
            [System.GC]::WaitForPendingFinalizers()
            Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue
        } else {
            Write-Host "Plugin files not found at $pluginPath" -ForegroundColor Red
        }
    }

    # Copy to local path if requested
    if ($CopyToLocalPath -and $OutputPath) {
        # Ensure $OutputPath is a valid path, not just $true
        if ($OutputPath -eq $true -or $OutputPath -eq "True" -or $OutputPath -eq "true") {
            Write-Host "Warning: OutputPath should not be a boolean value. Skipping copy to local path." -ForegroundColor Yellow
        } else {
            Write-Host "Copying plugin to $OutputPath..." -ForegroundColor Yellow
            $rootPath = (Get-Location).Path
            $pluginPath = Join-Path $rootPath "_plugins\$PluginName"
            
            if (Test-Path $pluginPath) {
                if (-not (Test-Path $OutputPath)) {
                    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
                }
                
                # Copy only the plugin DLL, PDB, and JSON files
                Copy-Item -Path "$pluginPath/$PluginName.dll" -Destination $OutputPath -Force
                Copy-Item -Path "$pluginPath/$PluginName.pdb" -Destination $OutputPath -Force
                if (Test-Path "$pluginPath/$PluginName.deps.json") {
                    Copy-Item -Path "$pluginPath/$PluginName.deps.json" -Destination $OutputPath -Force
                }
                
                Write-Host "Copied plugin files to $OutputPath" -ForegroundColor Green
            } else {
                Write-Host "Plugin files not found at $pluginPath" -ForegroundColor Red
            }
        }
    }

    Write-Host "Build completed successfully!" -ForegroundColor Green
}
finally {
    # Always restore files, even if build fails
    RestoreFiles
    
    # Cleanup any files in ext/Lidarr/_plugins that shouldn't be there
    $extPluginsPath = "ext/Lidarr/_plugins"
    if (Test-Path $extPluginsPath) {
        Write-Host "Cleaning up any files generated in ext/Lidarr/_plugins..." -ForegroundColor Yellow
        try {
            Remove-Item -Recurse -Force $extPluginsPath -ErrorAction SilentlyContinue
        } catch {
            Write-Host "Warning: Could not remove $extPluginsPath. You may need to manually clean it up." -ForegroundColor Yellow
        }
    }
    
    # Also restore global.json if it was created during the build
    if (Test-Path "./global.json") {
        if (git ls-files --error-unmatch "./global.json" 2>$null) {
            # File is tracked by Git, restore it
            git checkout -- "./global.json"
        } else {
            # File is not tracked by Git, remove it
            Remove-Item -Path "./global.json" -Force
        }
    }
}






