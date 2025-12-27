#!/bin/bash

# Generate common event models that all services can access
# These models are placed in bannou-service/Generated/ so all services can reference them

set -e

# Change to scripts directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Source common utilities
source "./common.sh"

log_info "🌟 Generating common event models"

# Find NSwag executable and ensure DOTNET_ROOT is set
require_nswag
ensure_dotnet_root

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
