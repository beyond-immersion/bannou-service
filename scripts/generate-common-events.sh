#!/bin/bash

# Generate common event models that all services can access
# These models are placed in bannou-service/Generated/ so all services can reference them

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

echo -e "${BLUE}🌟 Generating common event models${NC}"

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

# Find NSwag executable
NSWAG_EXE=$(find_nswag_exe)
if [ -z "$NSWAG_EXE" ]; then
    echo -e "${RED}❌ NSwag executable not found${NC}"
    echo -e "${YELLOW}💡 Please install NSwag: dotnet tool install -g NSwag.ConsoleCore${NC}"
    exit 1
fi

echo -e "${BLUE}🔧 Using NSwag: $NSWAG_EXE${NC}"

# Set DOTNET_ROOT if not already set (copied from working scripts)
if [ -z "$DOTNET_ROOT" ]; then
    if [ -d "/usr/share/dotnet" ]; then
        export DOTNET_ROOT="/usr/share/dotnet"
    elif command -v dotnet >/dev/null 2>&1; then
        # Get dotnet installation path
        DOTNET_PATH=$(dirname "$(readlink -f "$(which dotnet)")")
        export DOTNET_ROOT="$DOTNET_PATH"
    fi
fi

# Check if common-events.yaml exists
COMMON_EVENTS_SCHEMA="../schemas/common-events.yaml"
if [ ! -f "$COMMON_EVENTS_SCHEMA" ]; then
    echo -e "${RED}❌ Schema file not found: $COMMON_EVENTS_SCHEMA${NC}"
    exit 1
fi

# Target directory for generated models
TARGET_DIR="../bannou-service/Generated"
mkdir -p "$TARGET_DIR"

# Generate common event models using NSwag
echo -e "${YELLOW}📄 Generating CommonEvents models...${NC}"

# Use NSwag to generate models from common-events.yaml (exact same pattern as working scripts)
"$NSWAG_EXE" openapi2csclient \
    "/input:$COMMON_EVENTS_SCHEMA" \
    "/output:$TARGET_DIR/CommonEventsModels.cs" \
    "/namespace:BeyondImmersion.BannouService.Events" \
    "/generateClientClasses:false" \
    "/generateClientInterfaces:false" \
    "/generateDtoTypes:true" \
    "/excludedTypeNames:ApiException,ApiException\<TResult\>" \
    "/jsonLibrary:SystemTextJson" \
    "/generateNullableReferenceTypes:true" \
    "/newLineBehavior:LF" \
    "/templateDirectory:../templates/nswag"

if [ $? -eq 0 ]; then
    echo -e "${GREEN}✅ Common event models generated successfully${NC}"
    echo -e "   📁 Output: $TARGET_DIR/CommonEventsModels.cs"
    echo -e "   📦 Namespace: BeyondImmersion.BannouService.Events"
else
    echo -e "${RED}❌ Failed to generate common event models${NC}"
    exit 1
fi

echo -e "${GREEN}🎉 Common events generation complete!${NC}"
echo ""
echo -e "${BLUE}Available event types:${NC}"
echo -e "  • ServiceRegistrationEvent"
echo -e "  • ServiceEndpoint"
echo -e "  • PermissionRequirement"
echo -e "  • ServiceHeartbeatEvent"
echo -e "  • ServiceMappingEvent"
echo ""
echo -e "${YELLOW}💡 All services can now use: using BeyondImmersion.BannouService.Events;${NC}"
