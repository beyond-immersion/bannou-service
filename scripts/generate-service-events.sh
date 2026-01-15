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
        "/excludedTypeNames:ApiException,ApiException\<TResult\>,BaseServiceEvent" \
        "/additionalNamespaceUsages:BeyondImmersion.Bannou.Core" \
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
