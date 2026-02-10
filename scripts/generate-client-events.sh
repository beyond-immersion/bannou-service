#!/bin/bash

# Generate client event models that services can publish to WebSocket clients
# Common client events go to bannou-service/Generated/ (shared across all services)
# Service-specific client events go to lib-{service}/Generated/

set -e

# Change to scripts directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Source common utilities
source "./common.sh"

log_info "🌟 Generating client event models"

# Find NSwag executable and ensure DOTNET_ROOT is set
require_nswag
ensure_dotnet_root

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
        "/excludedTypeNames:ApiException,ApiException\<TResult\>,BaseClientEvent" \
        "/additionalNamespaceUsages:BeyondImmersion.Bannou.Core" \
        "/jsonLibrary:SystemTextJson" \
        "/generateNullableReferenceTypes:true" \
        "/newLineBehavior:LF" \
        "/templateDirectory:../templates/nswag"

    if [ $? -eq 0 ]; then
        # Post-process: Add [JsonRequired] after each [Required] attribute
        sed -i 's/\(\[System\.ComponentModel\.DataAnnotations\.Required[^]]*\]\)/\1\n    [System.Text.Json.Serialization.JsonRequired]/g' "$TARGET_DIR/CommonClientEventsModels.cs"
        # Post-process: Fix EventName shadowing - add 'override' keyword
        sed -i 's/public string EventName { get; set; }/public override string EventName { get; set; }/g' "$TARGET_DIR/CommonClientEventsModels.cs"
        # Post-process: Wrap enums with CS1591 pragma suppressions (enum members cannot have XML docs)
        postprocess_enum_suppressions "$TARGET_DIR/CommonClientEventsModels.cs"
        # Post-process: Add XML docs to AdditionalProperties
        postprocess_additional_properties_docs "$TARGET_DIR/CommonClientEventsModels.cs"
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
    lib_dir="../plugins/lib-${service_name}"

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

    # Extract $refs to API schema types (T26: Schema Reference Hierarchy)
    # Client events often reference enums/types from the service's API schema
    API_REFS=$(extract_api_refs "$schema_file" "$service_name")

    # Extract $refs to common-api.yaml types (shared types like EntityType)
    COMMON_REFS=$(extract_common_api_refs "$schema_file")

    # Build exclusion list: base exclusions + any API-referenced types + common types
    EXCLUSIONS="ApiException,ApiException\<TResult\>,BaseClientEvent"
    if [ -n "$API_REFS" ]; then
        EXCLUSIONS="${EXCLUSIONS},${API_REFS}"
        echo -e "    ${BLUE}Excluding API types: ${API_REFS}${NC}"
    fi
    if [ -n "$COMMON_REFS" ]; then
        EXCLUSIONS="${EXCLUSIONS},${COMMON_REFS}"
        echo -e "    ${BLUE}Excluding common types: ${COMMON_REFS}${NC}"
    fi

    # Build namespace usages: base + service API namespace if we have API refs
    NAMESPACE_USAGES="BeyondImmersion.Bannou.Core,BeyondImmersion.BannouService.ClientEvents"
    if [ -n "$API_REFS" ]; then
        NAMESPACE_USAGES="${NAMESPACE_USAGES},BeyondImmersion.BannouService.${pascal_case}"
    fi
    if [ -n "$COMMON_REFS" ]; then
        # Common types are in BeyondImmersion.BannouService.Common namespace
        NAMESPACE_USAGES="${NAMESPACE_USAGES},BeyondImmersion.BannouService.Common"
    fi

    "$NSWAG_EXE" openapi2csclient \
        "/input:$schema_file" \
        "/output:$TARGET_DIR/${pascal_case}ClientEventsModels.cs" \
        "/namespace:BeyondImmersion.Bannou.${pascal_case}.ClientEvents" \
        "/generateClientClasses:false" \
        "/generateClientInterfaces:false" \
        "/generateDtoTypes:true" \
        "/excludedTypeNames:${EXCLUSIONS}" \
        "/additionalNamespaceUsages:${NAMESPACE_USAGES}" \
        "/jsonLibrary:SystemTextJson" \
        "/generateNullableReferenceTypes:true" \
        "/newLineBehavior:LF" \
        "/templateDirectory:../templates/nswag"

    if [ $? -eq 0 ]; then
        output_file="$TARGET_DIR/${pascal_case}ClientEventsModels.cs"
        # Post-process: Add [JsonRequired] after each [Required] attribute
        sed -i 's/\(\[System\.ComponentModel\.DataAnnotations\.Required[^]]*\]\)/\1\n    [System.Text.Json.Serialization.JsonRequired]/g' "$output_file"
        # Post-process: Fix EventName shadowing - add 'override' keyword
        sed -i 's/public string EventName { get; set; }/public override string EventName { get; set; }/g' "$output_file"
        # Post-process: Wrap enums with CS1591 pragma suppressions (enum members cannot have XML docs)
        postprocess_enum_suppressions "$output_file"
        # Post-process: Add XML docs to AdditionalProperties
        postprocess_additional_properties_docs "$output_file"

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
