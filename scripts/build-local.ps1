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
        if (Test-Path "./_plugins") { Remove-Item -Recurse -Force "./_plugins" }
        if (Test-Path "./_artifacts") { Remove-Item -Recurse -Force "./_artifacts" }
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
        dotnet restore "ext\Lidarr\src\Lidarr.sln"
        dotnet build "ext\Lidarr\src\Lidarr.sln" -c Release --no-restore
    }

    # Then build the plugin
    if (Test-Path "src\Lidarr.Plugin.Tidal\Lidarr.Plugin.Tidal.csproj") {
        Write-Host "Building plugin..." -ForegroundColor Yellow
        dotnet restore "src\Lidarr.Plugin.Tidal\Lidarr.Plugin.Tidal.csproj"
        dotnet publish "src\Lidarr.Plugin.Tidal\Lidarr.Plugin.Tidal.csproj" -c Release -f $Framework -o "./_plugins/$PluginName"
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
        $pluginPath = "./_plugins/$PluginName"
        $zipPath = "./_artifacts/$PluginName.$Framework.zip"
        
        # Create artifacts directory if it doesn't exist
        if (-not (Test-Path "./_artifacts")) {
            New-Item -ItemType Directory -Path "./_artifacts" -Force | Out-Null
        }
        
        if (Test-Path $pluginPath) {
            # Create a temporary directory with just the required files
            $tempDir = "./_temp_package"
            if (Test-Path $tempDir) { Remove-Item -Recurse -Force $tempDir }
            New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
            
            # Copy only the plugin DLL, PDB, and JSON files
            Copy-Item -Path "$pluginPath/$PluginName.dll" -Destination $tempDir -Force
            Copy-Item -Path "$pluginPath/$PluginName.pdb" -Destination $tempDir -Force
            if (Test-Path "$pluginPath/$PluginName.json") {
                Copy-Item -Path "$pluginPath/$PluginName.json" -Destination $tempDir -Force
            }
            
            # Create the zip with just these files
            Compress-Archive -Path "$tempDir/*" -DestinationPath $zipPath -Force
            
            # Clean up temp directory
            Remove-Item -Recurse -Force $tempDir
            
            Write-Host "Created zip file: $zipPath" -ForegroundColor Green
        } else {
            Write-Host "Plugin files not found at $pluginPath" -ForegroundColor Red
        }
    }

    # Copy to local path if requested
    if ($CopyToLocalPath -and $OutputPath) {
        Write-Host "Copying plugin to $OutputPath..." -ForegroundColor Yellow
        $pluginPath = "./_plugins/$PluginName"
        
        if (Test-Path $pluginPath) {
            if (-not (Test-Path $OutputPath)) {
                New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
            }
            
            # Copy only the plugin DLL, PDB, and JSON files
            Copy-Item -Path "$pluginPath/$PluginName.dll" -Destination $OutputPath -Force
            Copy-Item -Path "$pluginPath/$PluginName.pdb" -Destination $OutputPath -Force
            if (Test-Path "$pluginPath/$PluginName.json") {
                Copy-Item -Path "$pluginPath/$PluginName.json" -Destination $OutputPath -Force
            }
            
            Write-Host "Copied plugin files to $OutputPath" -ForegroundColor Green
        } else {
            Write-Host "Plugin files not found at $pluginPath" -ForegroundColor Red
        }
    }

    Write-Host "Build completed successfully!" -ForegroundColor Green
}
finally {
    # Always restore files, even if build fails
    RestoreFiles
    
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






