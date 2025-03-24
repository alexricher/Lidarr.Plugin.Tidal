#!/bin/bash

# Exit on error
set -e

# Default values
CONFIGURATION="Release"
FRAMEWORK="net6.0"
RUNTIME="linux-x64"
RID="linux-x64"
CREATE_ZIP=false
CLEAN_PREVIOUS_BUILDS=true

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --configuration=*)
            CONFIGURATION="${1#*=}"
            shift
            ;;
        --framework=*)
            FRAMEWORK="${1#*=}"
            shift
            ;;
        --runtime=*)
            RUNTIME="${1#*=}"
            shift
            ;;
        --rid=*)
            RID="${1#*=}"
            shift
            ;;
        --create-zip=*)
            CREATE_ZIP="${1#*=}"
            shift
            ;;
        --clean=*)
            CLEAN_PREVIOUS_BUILDS="${1#*=}"
            shift
            ;;
        *)
            echo "Unknown parameter: $1"
            exit 1
            ;;
    esac
done

# Set up directories with absolute paths
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SLN_DIR="$(dirname "$SCRIPT_DIR")"
SRC_DIR="$SLN_DIR/src"
WORKFLOWS_DIR="$SLN_DIR/.github/workflows"
OUTPUT_DIR="$SLN_DIR/_output"
ARTIFACTS_DIR="$SLN_DIR/_artifacts"
PLUGINS_DIR="$SLN_DIR/_plugins"
TEMP_DIR="$SLN_DIR/_temp"
EXT_LIDARR_DIR="$SLN_DIR/ext/Lidarr"

echo "Script directory: $SCRIPT_DIR"
echo "Solution directory: $SLN_DIR"

# Read configuration from GitHub workflow file
WORKFLOW_FILE="$WORKFLOWS_DIR/build.yml"
if [ -f "$WORKFLOW_FILE" ]; then
    # Extract version components
    MAJOR_VERSION=$(grep -oP 'MAJOR_VERSION:\s*\K\d+' "$WORKFLOW_FILE" || echo "10")
    MINOR_VERSION=$(grep -oP 'MINOR_VERSION:\s*\K\d+' "$WORKFLOW_FILE" || echo "0")
    PATCH_VERSION=$(grep -oP 'PATCH_VERSION:\s*\K\d+' "$WORKFLOW_FILE" || echo "2")
    echo "Found version $MAJOR_VERSION.$MINOR_VERSION.$PATCH_VERSION in workflow file"
else
    echo "Warning: GitHub workflow file not found. Using default values."
    MAJOR_VERSION=10
    MINOR_VERSION=0
    PATCH_VERSION=2
fi

# Get build number from file or initialize to 1
BUILD_NUMBER_FILE="$SLN_DIR/.build_number"
if [ -f "$BUILD_NUMBER_FILE" ]; then
    BUILD_NUMBER=$(cat "$BUILD_NUMBER_FILE")
else
    BUILD_NUMBER=1
fi

# Clean previous builds if requested
if [ "$CLEAN_PREVIOUS_BUILDS" = true ]; then
    echo "Cleaning previous builds..."
    rm -rf "$OUTPUT_DIR" "$ARTIFACTS_DIR" "$PLUGINS_DIR" "$TEMP_DIR"
    
    # Also clean any output in ext/Lidarr/_plugins if it exists
    if [ -d "$EXT_LIDARR_DIR/_plugins" ]; then
        echo "Cleaning ext/Lidarr/_plugins directory..."
        rm -rf "$EXT_LIDARR_DIR/_plugins"
    fi
fi

# Create output directories
mkdir -p "$OUTPUT_DIR" "$ARTIFACTS_DIR" "$PLUGINS_DIR/Lidarr.Plugin.Tidal" "$TEMP_DIR"

# Update version info
VERSION="$MAJOR_VERSION.$MINOR_VERSION.$PATCH_VERSION.$BUILD_NUMBER"
echo "Building version $VERSION for $FRAMEWORK ($RUNTIME)"

# Build Lidarr solution first to ensure dependencies
if [ -d "$EXT_LIDARR_DIR" ] && [ -f "$EXT_LIDARR_DIR/src/Lidarr.sln" ]; then
    echo "Building Lidarr solution..."
    LIDARR_OUTPUT="$OUTPUT_DIR/lidarr"
    mkdir -p "$LIDARR_OUTPUT"
    
    dotnet restore "$EXT_LIDARR_DIR/src/Lidarr.sln"
    dotnet build "$EXT_LIDARR_DIR/src/Lidarr.sln" -c Release --no-restore -o "$LIDARR_OUTPUT"
fi

# Build plugin
echo "Building plugin with ILRepack support..."
PLUGIN_OUTPUT="$PLUGINS_DIR/Lidarr.Plugin.Tidal"

# Need MSBuild to support ILRepack
dotnet msbuild "$SRC_DIR/Lidarr.Plugin.Tidal/Lidarr.Plugin.Tidal.csproj" \
    /p:Configuration=Release \
    /p:TargetFramework="$FRAMEWORK" \
    /p:Version="$VERSION" \
    /p:OutputPath="$PLUGIN_OUTPUT" \
    /p:TreatWarningsAsErrors=false \
    /p:RunAnalyzers=false \
    /t:Clean,Build,ILRepacker \
    /v:n

# Check if plugin was built successfully
PLUGIN_DLL="$PLUGIN_OUTPUT/Lidarr.Plugin.Tidal.dll"
if [ -f "$PLUGIN_DLL" ]; then
    DLL_SIZE=$(du -k "$PLUGIN_DLL" | cut -f1)
    echo "Main plugin DLL built successfully: $DLL_SIZE KB"
    
    if [ $DLL_SIZE -gt 1000 ]; then
        echo "DLL size indicates dependencies were successfully merged"
    else
        echo "Warning: DLL size is smaller than expected. Dependencies may not have been merged correctly."
    fi
    
    # List files in plugin output directory
    echo "Files in plugin output directory:"
    find "$PLUGIN_OUTPUT" -type f -exec du -h {} \; | sort -h
else
    echo "Error: Plugin DLL not found at $PLUGIN_DLL"
    exit 1
fi

# Create zip file if requested
if [ "$CREATE_ZIP" = true ]; then
    echo "Creating zip file..."
    ZIP_NAME="Lidarr.Plugin.Tidal.$FRAMEWORK.zip"
    ZIP_PATH="$ARTIFACTS_DIR/$ZIP_NAME"
    TEMP_PACKAGE_DIR="$TEMP_DIR/package"
    
    mkdir -p "$TEMP_PACKAGE_DIR"
    
    # Copy only the essential files to the package directory
    cp "$PLUGIN_OUTPUT/Lidarr.Plugin.Tidal.dll" "$TEMP_PACKAGE_DIR/"
    cp "$PLUGIN_OUTPUT/Lidarr.Plugin.Tidal.pdb" "$TEMP_PACKAGE_DIR/" 2>/dev/null || echo "PDB file not found, skipping"
    cp "$PLUGIN_OUTPUT/Lidarr.Plugin.Tidal.deps.json" "$TEMP_PACKAGE_DIR/" 2>/dev/null || echo "deps.json file not found, skipping"
    
    # List files in package directory
    echo "Files in temp package directory:"
    find "$TEMP_PACKAGE_DIR" -type f -exec du -h {} \; | sort -h
    
    # Create the zip file
    cd "$TEMP_PACKAGE_DIR"
    zip -r "$ZIP_PATH" .
    
    # Verify the zip file
    if [ -f "$ZIP_PATH" ]; then
        ZIP_SIZE=$(du -h "$ZIP_PATH" | cut -f1)
        echo "Created zip file: $ZIP_PATH ($ZIP_SIZE)"
        
        # Verify zip contents
        echo "Verifying zip contents..."
        mkdir -p "$TEMP_DIR/verify"
        unzip -q -d "$TEMP_DIR/verify" "$ZIP_PATH"
        
        echo "Files in zip archive:"
        find "$TEMP_DIR/verify" -type f -exec du -h {} \; | sort -h
        
        # Check for required files
        MISSING_FILES=false
        for FILE in "Lidarr.Plugin.Tidal.dll" "Lidarr.Plugin.Tidal.pdb" "Lidarr.Plugin.Tidal.deps.json"; do
            if [ -f "$TEMP_DIR/verify/$FILE" ]; then
                FILE_SIZE=$(du -h "$TEMP_DIR/verify/$FILE" | cut -f1)
                echo "Found $FILE ($FILE_SIZE)"
            else
                echo "MISSING $FILE"
                MISSING_FILES=true
            fi
        done
        
        if [ "$MISSING_FILES" = true ]; then
            echo "WARNING: Some required files are missing from the zip! The plugin may not work correctly."
        else
            # Check DLL size
            DLL_VERIFY_SIZE=$(du -k "$TEMP_DIR/verify/Lidarr.Plugin.Tidal.dll" | cut -f1)
            if [ $DLL_VERIFY_SIZE -lt 100 ]; then
                echo "WARNING: DLL size ($DLL_VERIFY_SIZE KB) is smaller than expected! This may indicate a problem."
            else
                echo "All required files are present in the zip and have reasonable sizes."
            fi
        fi
        
        # Clean up verify directory
        rm -rf "$TEMP_DIR/verify"
    else
        echo "ERROR: Failed to create zip file!"
    fi
    
    # Clean up temp package directory
    rm -rf "$TEMP_PACKAGE_DIR"
fi

echo "Build completed successfully!"

# Increment build number and save
echo $((BUILD_NUMBER + 1)) > "$BUILD_NUMBER_FILE"

# Clean up any files in ext/Lidarr/_plugins if they exist
if [ -d "$EXT_LIDARR_DIR/_plugins" ]; then
    echo "Cleaning up ext/Lidarr/_plugins directory..."
    rm -rf "$EXT_LIDARR_DIR/_plugins"
fi






