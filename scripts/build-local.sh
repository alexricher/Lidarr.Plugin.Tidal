#!/bin/bash

# Exit on error
set -e

# Default values
CONFIGURATION="Release"
FRAMEWORK="net6.0"
RUNTIME="linux-x64"
RID="linux-x64"

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
        *)
            echo "Unknown parameter: $1"
            exit 1
            ;;
    esac
done

# Set up directories
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SLN_DIR="$(dirname "$SCRIPT_DIR")"
SRC_DIR="$SLN_DIR/src"
WORKFLOWS_DIR="$SLN_DIR/.github/workflows"
OUTPUT_DIR="$SLN_DIR/_output"
ARTIFACTS_DIR="$SLN_DIR/_artifacts"

# Read configuration from GitHub workflow file
WORKFLOW_FILE="$WORKFLOWS_DIR/build.yml"
if [ -f "$WORKFLOW_FILE" ]; then
    # Extract version components
    MAJOR_VERSION=$(grep -oP 'MAJOR_VERSION:\s*\K\d+' "$WORKFLOW_FILE" || echo "10")
    MINOR_VERSION=$(grep -oP 'MINOR_VERSION:\s*\K\d+' "$WORKFLOW_FILE" || echo "0")
    PATCH_VERSION=$(grep -oP 'PATCH_VERSION:\s*\K\d+' "$WORKFLOW_FILE" || echo "2")
else
    echo "Warning: GitHub workflow file not found. Using default values."
    MAJOR_VERSION=10
    MINOR_VERSION=0
    PATCH_VERSION=2
fi

# Get build number from file or initialize to 1
BUILD_NUMBER_FILE="$WORKFLOWS_DIR/.build_number"
if [ -f "$BUILD_NUMBER_FILE" ]; then
    BUILD_NUMBER=$(cat "$BUILD_NUMBER_FILE")
else
    BUILD_NUMBER=1
fi

# Clean previous builds
rm -rf "$OUTPUT_DIR" "$ARTIFACTS_DIR"

# Create output directories
mkdir -p "$OUTPUT_DIR" "$ARTIFACTS_DIR"

# Update version info
VERSION="$MAJOR_VERSION.$MINOR_VERSION.$PATCH_VERSION.$BUILD_NUMBER"
echo "Building version $VERSION"

# Build plugin
dotnet publish "$SRC_DIR/Lidarr.Plugin.Tidal/Lidarr.Plugin.Tidal.csproj" \
    -c "$CONFIGURATION" \
    -f "$FRAMEWORK" \
    -r "$RUNTIME" \
    --self-contained false \
    -p:Version="$VERSION" \
    -o "$OUTPUT_DIR"

# Create plugin package
PLUGIN_DIR="$OUTPUT_DIR/Lidarr.Plugin.Tidal"
mkdir -p "$PLUGIN_DIR"
mv "$OUTPUT_DIR"/* "$PLUGIN_DIR" 2>/dev/null || true

# Create zip file
ZIP_FILE="$ARTIFACTS_DIR/Lidarr.Plugin.Tidal.$VERSION.zip"
cd "$OUTPUT_DIR"
zip -r "$ZIP_FILE" "Lidarr.Plugin.Tidal"

echo "Build completed successfully!"
echo "Plugin package created at: $ZIP_FILE"

# Increment build number and save
echo $((BUILD_NUMBER + 1)) > "$BUILD_NUMBER_FILE"






