# This script first cleans up SQLite files and then runs the build-local.ps1 script
# This helps avoid SQLite conflicts that can cause build failures

param (
    [Parameter(ValueFromRemainingArguments=$true)]
    $RemainingArgs
)

Write-Host "=== SQLite Cleanup and Build Script ===" -ForegroundColor Cyan
Write-Host "This script will first clean up SQLite files to prevent conflicts, then run the normal build script." -ForegroundColor Cyan
Write-Host

# Run the SQLite cleanup script
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$cleanScriptPath = Join-Path $scriptPath "clean-sqlite.ps1"
Write-Host "Step 1: Running SQLite cleanup script..." -ForegroundColor Yellow
. $cleanScriptPath

# Run the build script with all parameters passed through
$buildScriptPath = Join-Path $scriptPath "build-local.ps1"
Write-Host
Write-Host "Step 2: Running build script..." -ForegroundColor Yellow

# Convert any passed parameters into a string to pass to the build script
$paramString = ""
if ($RemainingArgs.Count -gt 0) {
    $paramString = $RemainingArgs -join " "
}

# Execute the build script with parameters
Write-Host "Executing: $buildScriptPath $paramString" -ForegroundColor Gray
Invoke-Expression "& `"$buildScriptPath`" $paramString" 