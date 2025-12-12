#!/bin/bash

# Generate client event models that services can publish to WebSocket clients
# Common client events go to bannou-service/Generated/ (shared across all services)
# Service-specific client events go to lib-{service}/Generated/

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

echo -e "${BLUE}🌟 Generating client event models${NC}"

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

# Set DOTNET_ROOT if not already set
if [ -z "$DOTNET_ROOT" ]; then
    if [ -d "/usr/share/dotnet" ]; then
        export DOTNET_ROOT="/usr/share/dotnet"
    elif command -v dotnet >/dev/null 2>&1; then
        DOTNET_PATH=$(dirname "$(readlink -f "$(which dotnet)")")
        export DOTNET_ROOT="$DOTNET_PATH"
    fi
fi

# Track generated events for summary
declare -a GENERATED_EVENTS=()

# ============================================
# Generate common client events (shared base)
# ============================================
echo -e "${YELLOW}📄 Generating common client events...${NC}"

COMMON_CLIENT_EVENTS_SCHEMA="../schemas/common-client-events.yaml"
if [ -f "$COMMON_CLIENT_EVENTS_SCHEMA" ]; then
    TARGET_DIR="../bannou-service/Generated"
    mkdir -p "$TARGET_DIR"

    "$NSWAG_EXE" openapi2csclient \
        "/input:$COMMON_CLIENT_EVENTS_SCHEMA" \
        "/output:$TARGET_DIR/CommonClientEventsModels.cs" \
        "/namespace:BeyondImmersion.BannouService.ClientEvents" \
        "/generateClientClasses:false" \
        "/generateClientInterfaces:false" \
        "/generateDtoTypes:true" \
        "/excludedTypeNames:ApiException,ApiException\<TResult\>" \
        "/jsonLibrary:SystemTextJson" \
        "/generateNullableReferenceTypes:true" \
        "/newLineBehavior:LF" \
        "/templateDirectory:../templates/nswag"

    if [ $? -eq 0 ]; then
        echo -e "${GREEN}✅ Common client events generated${NC}"
        echo -e "   📁 Output: $TARGET_DIR/CommonClientEventsModels.cs"
        GENERATED_EVENTS+=("BaseClientEvent")
        GENERATED_EVENTS+=("CapabilityManifestEvent")
        GENERATED_EVENTS+=("DisconnectNotificationEvent")
        GENERATED_EVENTS+=("SystemErrorEvent")
        GENERATED_EVENTS+=("SystemNotificationEvent")
    else
        echo -e "${RED}❌ Failed to generate common client events${NC}"
        exit 1
    fi
else
    echo -e "${YELLOW}⚠️  No common-client-events.yaml found, skipping${NC}"
fi

# ============================================
# Generate service-specific client events
# ============================================
echo ""
echo -e "${YELLOW}📄 Generating service-specific client events...${NC}"

# Find all service client event schemas
CLIENT_EVENT_SCHEMAS=(../schemas/*-client-events.yaml)

for schema_file in "${CLIENT_EVENT_SCHEMAS[@]}"; do
    # Skip if no files match (glob returns literal pattern)
    [ -e "$schema_file" ] || continue

    # Skip common-client-events.yaml (already processed)
    if [[ "$schema_file" == *"common-client-events.yaml" ]]; then
        continue
    fi

    # Extract service name (e.g., "game-session-client-events.yaml" -> "game-session")
    filename=$(basename "$schema_file")
    service_name="${filename%-client-events.yaml}"

    # Convert to lib directory format (e.g., "game-session" -> "lib-game-session")
    lib_dir="../lib-${service_name}"

    # Check if the lib directory exists
    if [ ! -d "$lib_dir" ]; then
        echo -e "${YELLOW}⚠️  Skipping $service_name: $lib_dir not found${NC}"
        continue
    fi

    TARGET_DIR="$lib_dir/Generated"
    mkdir -p "$TARGET_DIR"

    # Convert service name to PascalCase for namespace
    # e.g., "game-session" -> "GameSession"
    pascal_case=$(echo "$service_name" | sed -E 's/(^|-)([a-z])/\U\2/g')

    echo -e "${BLUE}  🔧 Processing $service_name...${NC}"

    "$NSWAG_EXE" openapi2csclient \
        "/input:$schema_file" \
        "/output:$TARGET_DIR/${pascal_case}ClientEventsModels.cs" \
        "/namespace:BeyondImmersion.Bannou.${pascal_case}.ClientEvents" \
        "/generateClientClasses:false" \
        "/generateClientInterfaces:false" \
        "/generateDtoTypes:true" \
        "/excludedTypeNames:ApiException,ApiException\<TResult\>,BaseClientEvent" \
        "/jsonLibrary:SystemTextJson" \
        "/generateNullableReferenceTypes:true" \
        "/newLineBehavior:LF" \
        "/templateDirectory:../templates/nswag"

    if [ $? -eq 0 ]; then
        # Add using statement for BaseClientEvent from common client events
        # This is needed because the generated classes inherit from BaseClientEvent
        output_file="$TARGET_DIR/${pascal_case}ClientEventsModels.cs"
        sed -i 's/namespace BeyondImmersion\.Bannou\.'${pascal_case}'\.ClientEvents;/using BeyondImmersion.BannouService.ClientEvents;\n\nnamespace BeyondImmersion.Bannou.'${pascal_case}'.ClientEvents;/' "$output_file"

        echo -e "${GREEN}  ✅ $pascal_case client events generated${NC}"
        echo -e "     📁 Output: $TARGET_DIR/${pascal_case}ClientEventsModels.cs"
        GENERATED_EVENTS+=("$pascal_case client events")
    else
        echo -e "${RED}  ❌ Failed to generate $pascal_case client events${NC}"
        exit 1
    fi
done

# ============================================
# Summary
# ============================================
echo ""
echo -e "${GREEN}🎉 Client events generation complete!${NC}"
echo ""
echo -e "${BLUE}Generated event types:${NC}"
for event in "${GENERATED_EVENTS[@]}"; do
    echo -e "  • $event"
done
echo ""
echo -e "${YELLOW}💡 Common events: using BeyondImmersion.BannouService.ClientEvents;${NC}"
echo -e "${YELLOW}💡 Service events: using BeyondImmersion.Bannou.{Service}.ClientEvents;${NC}"
