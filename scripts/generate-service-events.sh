#!/bin/bash

# Generate service-specific event models from {service}-events.yaml files
# These models are placed in bannou-service/Generated/Events/ so all services can reference them
# Excludes: *-lifecycle-events.yaml, *-client-events.yaml, common-events.yaml

set -e

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Change to scripts directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo -e "${BLUE}📡 Generating service-specific event models${NC}"

# Function to find NSwag executable
find_nswag_exe() {
    # On Linux/macOS, prefer the global dotnet tool over Windows executables
    if [[ "$OSTYPE" == "linux-gnu"* ]] || [[ "$OSTYPE" == "darwin"* ]]; then
        local nswag_global=$(which nswag 2>/dev/null)
        if [ -n "$nswag_global" ]; then
            echo "$nswag_global"
            return 0
        fi
    fi

    # Try common NSwag installation paths
    local nswag_paths=(
        "$HOME/.nuget/packages/nswag.msbuild/14.2.0/tools/Net90/dotnet-nswag.exe"
        "$HOME/.nuget/packages/nswag.msbuild/14.1.0/tools/Net90/dotnet-nswag.exe"
        "$HOME/.nuget/packages/nswag.msbuild/14.0.7/tools/Net90/dotnet-nswag.exe"
        "$(find $HOME/.nuget/packages/nswag.msbuild -name "dotnet-nswag.exe" 2>/dev/null | head -1)"
        "$(which nswag 2>/dev/null)"
    )

    for nswag_path in "${nswag_paths[@]}"; do
        if [ -f "$nswag_path" ] && [ -x "$nswag_path" ]; then
            echo "$nswag_path"
            return 0
        fi
    done

    return 1
}

# Helper function to convert to PascalCase
to_pascal_case() {
    local input="$1"
    echo "$input" | sed 's/-/ /g' | awk '{for(i=1;i<=NF;i++) $i=toupper(substr($i,1,1)) tolower(substr($i,2))} 1' | sed 's/ //g'
}

# Find NSwag executable
NSWAG_EXE=$(find_nswag_exe)
if [ -z "$NSWAG_EXE" ]; then
    echo -e "${RED}NSwag executable not found${NC}"
    echo -e "${YELLOW}Please install NSwag: dotnet tool install -g NSwag.ConsoleCore${NC}"
    exit 1
fi

echo -e "${BLUE}Using NSwag: $NSWAG_EXE${NC}"

# Set DOTNET_ROOT if not already set
if [ -z "$DOTNET_ROOT" ]; then
    if [ -d "/usr/share/dotnet" ]; then
        export DOTNET_ROOT="/usr/share/dotnet"
    elif command -v dotnet >/dev/null 2>&1; then
        DOTNET_PATH=$(dirname "$(readlink -f "$(which dotnet)")")
        export DOTNET_ROOT="$DOTNET_PATH"
    fi
fi

# Target directory for generated models
TARGET_DIR="../bannou-service/Generated/Events"
mkdir -p "$TARGET_DIR"

# NOTE: No exclusions needed - service-events.yaml files contain ONLY canonical definitions
# for events the service PUBLISHES. All $refs have been removed to prevent NSwag duplication.
# If you find duplicate types being generated, fix the source schema by removing $refs,
# not by adding exclusions here.

# Track generated files
GENERATED_COUNT=0
FAILED_COUNT=0

# Process each service-specific events yaml file
for EVENTS_SCHEMA in ../schemas/*-events.yaml; do
    # Skip lifecycle events (auto-generated from x-lifecycle)
    if [[ "$EVENTS_SCHEMA" == *"-lifecycle-events.yaml" ]]; then
        continue
    fi

    # Skip client events (server-to-client WebSocket push)
    if [[ "$EVENTS_SCHEMA" == *"-client-events.yaml" ]]; then
        continue
    fi

    # Skip common events (processed by generate-common-events.sh)
    if [[ "$EVENTS_SCHEMA" == *"common-events.yaml" ]]; then
        continue
    fi

    # Extract service name from filename
    FILENAME=$(basename "$EVENTS_SCHEMA")
    SERVICE_NAME="${FILENAME%-events.yaml}"
    SERVICE_PASCAL=$(to_pascal_case "$SERVICE_NAME")

    OUTPUT_FILE="${TARGET_DIR}/${SERVICE_PASCAL}EventsModels.cs"

    echo -e "${YELLOW}Generating ${SERVICE_PASCAL} events from ${FILENAME}...${NC}"

    # Generate models using NSwag
    "$NSWAG_EXE" openapi2csclient \
        "/input:$EVENTS_SCHEMA" \
        "/output:$OUTPUT_FILE" \
        "/namespace:BeyondImmersion.BannouService.Events" \
        "/generateClientClasses:false" \
        "/generateClientInterfaces:false" \
        "/generateDtoTypes:true" \
        "/excludedTypeNames:ApiException,ApiException\<TResult\>" \
        "/jsonLibrary:SystemTextJson" \
        "/generateNullableReferenceTypes:true" \
        "/newLineBehavior:LF" \
        "/templateDirectory:../templates/nswag" 2>&1

    if [ $? -eq 0 ]; then
        echo -e "${GREEN}  Generated: $OUTPUT_FILE${NC}"
        GENERATED_COUNT=$((GENERATED_COUNT + 1))
    else
        echo -e "${RED}  Failed to generate: $OUTPUT_FILE${NC}"
        FAILED_COUNT=$((FAILED_COUNT + 1))
    fi
done

echo ""
echo -e "${BLUE}========================================${NC}"
if [ $FAILED_COUNT -eq 0 ]; then
    echo -e "${GREEN}Service events generation complete!${NC}"
    echo -e "  Generated: ${GENERATED_COUNT} files"
else
    echo -e "${YELLOW}Service events generation completed with errors${NC}"
    echo -e "  Generated: ${GENERATED_COUNT} files"
    echo -e "  ${RED}Failed: ${FAILED_COUNT} files${NC}"
fi
echo -e "${BLUE}========================================${NC}"
echo ""
echo -e "${YELLOW}All services can now use: using BeyondImmersion.BannouService.Events;${NC}"
