# Lidarr Tidal Plugin Documentation Generator
# This script automates the generation of documentation for the Lidarr Tidal Plugin
# Usage: .\GenerateDocumentation.ps1

# Ensure the script stops on errors
$ErrorActionPreference = "Stop"

Write-Host "=== Lidarr Tidal Plugin Documentation Generator ===" -ForegroundColor Green

# Variables
$projectRoot = Join-Path $PSScriptRoot ".."
$srcFolder = Join-Path $projectRoot "src"
$docfxFolder = Join-Path $projectRoot "docfx_project"
$buildOutputFolder = Join-Path $docfxFolder "_site"
$docOutputFolder = Join-Path $projectRoot "docs"
$tempFolder = Join-Path $projectRoot "temp_docs"

# Check if DocFX is installed
try {
    $docfxVersion = docfx --version
    Write-Host "Found DocFX: $docfxVersion" -ForegroundColor Green
}
catch {
    Write-Host "DocFX not found. Installing DocFX..." -ForegroundColor Yellow
    dotnet tool install -g docfx
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to install DocFX. Please install it manually: 'dotnet tool install -g docfx'" -ForegroundColor Red
        exit 1
    }
}

# Check if docfx_project exists, if not create it
if (-not (Test-Path $docfxFolder)) {
    Write-Host "Creating DocFX project folder..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $docfxFolder -Force | Out-Null
    Set-Location $docfxFolder
    docfx init -q
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to initialize DocFX project." -ForegroundColor Red
        exit 1
    }
    
    # Update docfx.json to target our project
    Write-Host "Updating DocFX configuration..." -ForegroundColor Yellow
    $docfxJsonPath = Join-Path $docfxFolder "docfx.json"
    $docfxConfig = Get-Content -Path $docfxJsonPath -Raw | ConvertFrom-Json
    
    # Create new objects for configuration sections
    $newMetadata = $docfxConfig.metadata[0].PSObject.Copy()
    $newSrc = $newMetadata.src[0].PSObject.Copy()
    
    # Update metadata section properly
    $newSrc.files = @("src/**.csproj")
    $newSrc.src = ".."
    $newMetadata.src = @($newSrc)
    
    # Create new build metadata
    $docfxConfig.build | Add-Member -MemberType NoteProperty -Name "globalMetadata" -Value @{
        "_appTitle" = "Lidarr Tidal Plugin Documentation"
        "_appFooter" = "Lidarr Tidal Plugin Documentation"
        "_enableSearch" = "true"
        "_enableNewTab" = "true"
    } -Force
    
    # Update metadata
    $docfxConfig.metadata = @($newMetadata)
    
    # Save updated config
    $docfxConfig | ConvertTo-Json -Depth 10 | Set-Content -Path $docfxJsonPath
}

# Generate XML documentation files
Write-Host "Building projects to generate XML documentation..." -ForegroundColor Yellow
Set-Location $srcFolder
$csprojFiles = Get-ChildItem -Filter "*.csproj" -Recurse
foreach ($csproj in $csprojFiles) {
    Write-Host "Building $($csproj.Name)..." -ForegroundColor Yellow
    dotnet build $csproj.FullName /p:GenerateDocumentationFile=true
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to build $($csproj.Name)." -ForegroundColor Red
        exit 1
    }
}

# Generate API documentation
Write-Host "Generating API documentation..." -ForegroundColor Yellow
Set-Location $docfxFolder
docfx metadata
if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to generate API metadata." -ForegroundColor Red
    exit 1
}

# Copy existing Markdown documentation
Write-Host "Copying existing documentation..." -ForegroundColor Yellow
if (-not (Test-Path (Join-Path $docfxFolder "articles"))) {
    New-Item -ItemType Directory -Path (Join-Path $docfxFolder "articles") -Force | Out-Null
}

# Create articles TOC if it doesn't exist
$articlesTocPath = Join-Path $docfxFolder "articles/toc.yml"
if (-not (Test-Path $articlesTocPath)) {
    @"
- name: Introduction
  href: intro.md
- name: Architecture
  href: ../architecture/overview.md
- name: Developer Guide
  href: ../guides/dev-guide.md
- name: User Guide
  href: ../guides/user-guide.md
"@ | Set-Content $articlesTocPath
}

# Create intro.md if it doesn't exist
$introPath = Join-Path $docfxFolder "articles/intro.md"
if (-not (Test-Path $introPath)) {
    @"
# Introduction to Lidarr Tidal Plugin

The Lidarr Tidal Plugin extends Lidarr's functionality by adding support for the Tidal music streaming service.

## Features

- Search for albums and tracks on Tidal
- Download music from Tidal
- Manage your Tidal library through Lidarr

## Getting Started

See the [User Guide](../guides/user-guide.md) for installation and usage instructions.

## For Developers

If you're a developer looking to contribute, see the [Developer Guide](../guides/dev-guide.md).
"@ | Set-Content $introPath
}

# Copy existing docs
if (Test-Path $docOutputFolder) {
    # Create architecture folder
    $archFolder = Join-Path $docfxFolder "architecture"
    if (-not (Test-Path $archFolder)) {
        New-Item -ItemType Directory -Path $archFolder -Force | Out-Null
    }
    
    # Create guides folder
    $guidesFolder = Join-Path $docfxFolder "guides"
    if (-not (Test-Path $guidesFolder)) {
        New-Item -ItemType Directory -Path $guidesFolder -Force | Out-Null
    }
    
    # Copy architecture docs
    Copy-Item -Path (Join-Path $docOutputFolder "ARCHITECTURE.md") -Destination (Join-Path $archFolder "overview.md") -Force -ErrorAction SilentlyContinue
    
    # Create architecture TOC
    @"
- name: Overview
  href: overview.md
"@ | Set-Content (Join-Path $archFolder "toc.yml")
    
    # Copy guide docs
    Copy-Item -Path (Join-Path $docOutputFolder "FIXING_LINTING_ERRORS.md") -Destination (Join-Path $guidesFolder "linting-guide.md") -Force -ErrorAction SilentlyContinue
    Copy-Item -Path (Join-Path $docOutputFolder "DOCUMENTATION_SETUP.md") -Destination (Join-Path $guidesFolder "documentation-setup.md") -Force -ErrorAction SilentlyContinue
    Copy-Item -Path (Join-Path $docOutputFolder "DOCUMENTATION_STANDARDS.md") -Destination (Join-Path $guidesFolder "documentation-standards.md") -Force -ErrorAction SilentlyContinue
    
    # Create guides TOC
    @"
- name: Developer Guide
  href: dev-guide.md
- name: User Guide
  href: user-guide.md
- name: Documentation Setup
  href: documentation-setup.md
- name: Documentation Standards
  href: documentation-standards.md
- name: Fixing Linting Errors
  href: linting-guide.md
"@ | Set-Content (Join-Path $guidesFolder "toc.yml")
    
    # Create placeholder guides if they don't exist
    if (-not (Test-Path (Join-Path $guidesFolder "dev-guide.md"))) {
        @"
# Developer Guide

This guide provides information for developers who want to contribute to the Lidarr Tidal Plugin.

## Setting Up Development Environment

1. Clone the repository
2. Install dependencies
3. Build the project

## Architecture

See the [Architecture Overview](../architecture/overview.md) for information about the plugin's design.

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Submit a pull request

## Coding Standards

- Follow C# coding conventions
- Document public APIs with XML comments
- Write unit tests for new functionality
"@ | Set-Content (Join-Path $guidesFolder "dev-guide.md")
    }
    
    if (-not (Test-Path (Join-Path $guidesFolder "user-guide.md"))) {
        @"
# User Guide

This guide provides information for users of the Lidarr Tidal Plugin.

## Installation

1. Download the latest release
2. Extract the files to your Lidarr plugins directory
3. Restart Lidarr

## Configuration

1. Open Lidarr
2. Go to Settings > Download Clients
3. Click "+"
4. Select "Tidal"
5. Enter your Tidal credentials
6. Save

## Usage

1. Search for albums or artists
2. Select the quality profile
3. Add to library

## Troubleshooting

### Common Issues

- **Authentication failed**: Check your Tidal credentials
- **Download fails**: Check your internet connection and Tidal subscription
- **Plugin not loading**: Verify plugin compatibility with your Lidarr version
"@ | Set-Content (Join-Path $guidesFolder "user-guide.md")
    }
}

# Update main TOC
@"
- name: Articles
  href: articles/
- name: Architecture
  href: architecture/
- name: Guides
  href: guides/
- name: API Documentation
  href: api/
"@ | Set-Content (Join-Path $docfxFolder "toc.yml")

# Create index.md
@"
# Lidarr Tidal Plugin Documentation

Welcome to the documentation for the Lidarr Tidal Plugin, which extends Lidarr to support the Tidal music streaming service.

## Getting Started

- [Introduction](articles/intro.md)
- [User Guide](guides/user-guide.md)
- [Developer Guide](guides/dev-guide.md)

## API Reference

Full [API Documentation](api/index.md) is available for developers.

## Architecture

Learn about the [system architecture](architecture/overview.md) and design principles.

## Contributing

Contributions are welcome! See the [Developer Guide](guides/dev-guide.md) for information on getting started.
"@ | Set-Content (Join-Path $docfxFolder "index.md")

# Build documentation
Write-Host "Building documentation..." -ForegroundColor Yellow
docfx build
if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to build documentation." -ForegroundColor Red
    exit 1
}

# Generate XML documentation issues report
Write-Host "Generating documentation issues report..." -ForegroundColor Yellow
$tempReportFolder = Join-Path $tempFolder "reports"
New-Item -ItemType Directory -Path $tempReportFolder -Force | Out-Null

$missingDocsReport = Join-Path $tempReportFolder "missing_docs.md"
@"
# Missing Documentation Report

This report identifies classes, methods, and properties that are missing XML documentation.

"@ | Set-Content $missingDocsReport

Set-Location $srcFolder
$csFiles = Get-ChildItem -Filter "*.cs" -Recurse

foreach ($csFile in $csFiles) {
    $fileContent = Get-Content $csFile.FullName -Raw
    $fileName = $csFile.Name
    $missingDocs = @()
    
    # Check for public classes without documentation
    $classMatches = [regex]::Matches($fileContent, '(?<!\/\/\/\s*<summary>[\s\S]*?)public\s+(class|interface|enum|struct)\s+(\w+)')
    foreach ($match in $classMatches) {
        $missingDocs += "- Class/Interface: $($match.Groups[2].Value)"
    }
    
    # Check for public methods without documentation
    $methodMatches = [regex]::Matches($fileContent, '(?<!\/\/\/\s*<summary>[\s\S]*?)public\s+[\w<>\[\]]+\s+(\w+)\s*\(')
    foreach ($match in $methodMatches) {
        $missingDocs += "- Method: $($match.Groups[1].Value)"
    }
    
    # Check for public properties without documentation
    $propMatches = [regex]::Matches($fileContent, '(?<!\/\/\/\s*<summary>[\s\S]*?)public\s+[\w<>\[\]]+\s+(\w+)\s*\{')
    foreach ($match in $propMatches) {
        $missingDocs += "- Property: $($match.Groups[1].Value)"
    }
    
    if ($missingDocs.Count -gt 0) {
        Add-Content -Path $missingDocsReport -Value "## $fileName`n"
        foreach ($item in $missingDocs) {
            Add-Content -Path $missingDocsReport -Value "$item"
        }
        Add-Content -Path $missingDocsReport -Value "`n"
    }
}

Write-Host "Documentation issues report generated at: $missingDocsReport" -ForegroundColor Yellow

# Open documentation in browser
Write-Host "Documentation built successfully!" -ForegroundColor Green
Write-Host "Starting documentation server..." -ForegroundColor Yellow
Start-Process "http://localhost:8080"
docfx serve $buildOutputFolder

Write-Host "Documentation generation complete." -ForegroundColor Green 