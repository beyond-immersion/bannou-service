#!/bin/bash

# Generate service-specific event models from {service}-events.yaml files
# These models are placed in bannou-service/Generated/Events/ so all services can reference them
# Excludes: *-lifecycle-events.yaml, *-client-events.yaml, common-events.yaml

set -e

# Change to scripts directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Source common utilities
source "./common.sh"

log_info "📡 Generating service-specific event models"

# Find NSwag executable and ensure DOTNET_ROOT is set
require_nswag
ensure_dotnet_root

# Target directory for generated models
TARGET_DIR="../bannou-service/Generated/Events"
mkdir -p "$TARGET_DIR"

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

    # Extract $refs to API schema types (T26: Schema Reference Hierarchy)
    API_REFS=$(extract_api_refs "$EVENTS_SCHEMA" "$SERVICE_NAME" "$SERVICE_PASCAL")

    # Extract $refs to common-api.yaml types (shared types like EntityType)
    COMMON_REFS=$(extract_common_api_refs "$EVENTS_SCHEMA")

    # Build exclusion list: base exclusions + any API-referenced types + common types
    EXCLUSIONS="ApiException,ApiException\<TResult\>,BaseServiceEvent"
    if [ -n "$API_REFS" ]; then
        EXCLUSIONS="${EXCLUSIONS},${API_REFS}"
        echo -e "  ${BLUE}Excluding API types: ${API_REFS}${NC}"
    fi
    if [ -n "$COMMON_REFS" ]; then
        EXCLUSIONS="${EXCLUSIONS},${COMMON_REFS}"
        echo -e "  ${BLUE}Excluding common types: ${COMMON_REFS}${NC}"
    fi

    # Build namespace usages: base + service namespace if we have API refs
    # BeyondImmersion.BannouService is always included for common types
    NAMESPACE_USAGES="BeyondImmersion.Bannou.Core,BeyondImmersion.BannouService"
    if [ -n "$API_REFS" ]; then
        NAMESPACE_USAGES="${NAMESPACE_USAGES},BeyondImmersion.BannouService.${SERVICE_PASCAL}"
    fi

    # Generate models using NSwag
    "$NSWAG_EXE" openapi2csclient \
        "/input:$EVENTS_SCHEMA" \
        "/output:$OUTPUT_FILE" \
        "/namespace:BeyondImmersion.BannouService.Events" \
        "/generateClientClasses:false" \
        "/generateClientInterfaces:false" \
        "/generateDtoTypes:true" \
        "/excludedTypeNames:${EXCLUSIONS}" \
        "/additionalNamespaceUsages:${NAMESPACE_USAGES}" \
        "/jsonLibrary:SystemTextJson" \
        "/generateNullableReferenceTypes:true" \
        "/newLineBehavior:LF" \
        "/templateDirectory:../templates/nswag" 2>&1

    if [ $? -eq 0 ]; then
        # Post-process: Add [JsonRequired] after each [Required] attribute
        sed -i 's/\(\[System\.ComponentModel\.DataAnnotations\.Required[^]]*\]\)/\1\n    [System.Text.Json.Serialization.JsonRequired]/g' "$OUTPUT_FILE"
        # Post-process: Fix EventName shadowing - add 'override' keyword
        sed -i 's/public string EventName { get; set; }/public override string EventName { get; set; }/g' "$OUTPUT_FILE"
        # Post-process: Wrap enums with CS1591 pragma suppressions (enum members cannot have XML docs)
        postprocess_enum_suppressions "$OUTPUT_FILE"
        # Post-process: Add XML docs to AdditionalProperties
        postprocess_additional_properties_docs "$OUTPUT_FILE"
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
