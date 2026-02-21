#!/bin/bash

# ⛔⛔⛔ AGENT MODIFICATION PROHIBITED ⛔⛔⛔
# This script is part of Bannou's code generation pipeline.
# DO NOT MODIFY without EXPLICIT user instructions to change code generation.
#
# Changes to generation scripts silently break builds across ALL 48 services.
# An agent once changed namespace strings across 4 scripts in a single commit,
# breaking every service. If you believe a change is needed:
#   1. STOP and explain what you think is wrong
#   2. Show the EXACT diff you propose
#   3. Wait for EXPLICIT approval before touching ANY generation script
#
# This applies to: namespace strings, output paths, exclusion logic,
# NSwag parameters, post-processing steps, and file naming conventions.
# ⛔⛔⛔ AGENT MODIFICATION PROHIBITED ⛔⛔⛔

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
    "/excludedTypeNames:ApiException,ApiException\<TResult\>,BaseServiceEvent" \
    "/additionalNamespaceUsages:BeyondImmersion.Bannou.Core" \
    "/jsonLibrary:SystemTextJson" \
    "/generateNullableReferenceTypes:true" \
    "/newLineBehavior:LF" \
    "/templateDirectory:../templates/nswag"

if [ $? -eq 0 ]; then
    # Post-process: Add [JsonRequired] after each [Required] attribute
    sed -i 's/\(\[System\.ComponentModel\.DataAnnotations\.Required[^]]*\]\)/\1\n    [System.Text.Json.Serialization.JsonRequired]/g' "$TARGET_DIR/CommonEventsModels.cs"
    # Post-process: Fix EventName shadowing - add 'override' keyword
    sed -i 's/public string EventName { get; set; }/public override string EventName { get; set; }/g' "$TARGET_DIR/CommonEventsModels.cs"
    # Post-process: Wrap enums with CS1591 pragma suppressions (enum members cannot have XML docs)
    postprocess_enum_suppressions "$TARGET_DIR/CommonEventsModels.cs"
    # Post-process: Add XML docs to AdditionalProperties
    postprocess_additional_properties_docs "$TARGET_DIR/CommonEventsModels.cs"
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
echo ""
echo -e "${YELLOW}💡 All services can now use: using BeyondImmersion.BannouService.Events;${NC}"
