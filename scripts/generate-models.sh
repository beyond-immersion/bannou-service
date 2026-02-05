#!/bin/bash

# Generate models (DTOs) from OpenAPI schema
# Also generates lifecycle event models from {service}-lifecycle-events.yaml if present
# Usage: ./generate-models.sh <service-name> [schema-file]

set -e  # Exit on any error

# Source common utilities
source "$(dirname "$0")/common.sh"

# Validate arguments
if [ $# -lt 1 ]; then
    log_error "Usage: $0 <service-name> [schema-file]"
    echo "Example: $0 account"
    echo "Example: $0 account ../schemas/account-api.yaml"
    exit 1
fi

SERVICE_NAME="$1"
SCHEMA_FILE="${2:-../schemas/${SERVICE_NAME}-api.yaml}"

SERVICE_PASCAL=$(to_pascal_case "$SERVICE_NAME")
OUTPUT_DIR="../bannou-service/Generated/Models"
OUTPUT_FILE="$OUTPUT_DIR/${SERVICE_PASCAL}Models.cs"

echo -e "${YELLOW}🔧 Generating models for service: $SERVICE_NAME${NC}"
echo -e "  📋 Schema: $SCHEMA_FILE"
echo -e "  📁 Output: $OUTPUT_FILE"

# Validate schema file exists
if [ ! -f "$SCHEMA_FILE" ]; then
    echo -e "${RED}❌ Schema file not found: $SCHEMA_FILE${NC}"
    exit 1
fi

# Ensure output directory exists
mkdir -p "$OUTPUT_DIR"

# Find NSwag executable and ensure DOTNET_ROOT is set
require_nswag
ensure_dotnet_root

# Run resolver to handle cross-file allOf refs (creates resolved schema if needed)
SCRIPT_DIR="$(dirname "$0")"
echo -e "${YELLOW}🔄 Checking for cross-file allOf refs...${NC}"
python3 "$SCRIPT_DIR/resolve-api-refs.py" "$SERVICE_NAME" 2>&1 | while read line; do echo "  $line"; done

# Check if a resolved version exists (for schemas with cross-file allOf refs)
RESOLVED_SCHEMA="../schemas/Generated/${SERVICE_NAME}-api-resolved.yaml"
if [ -f "$RESOLVED_SCHEMA" ]; then
    echo -e "${BLUE}ℹ️  Using resolved schema (cross-file allOf refs inlined)${NC}"
    SCHEMA_TO_PROCESS="$RESOLVED_SCHEMA"
else
    SCHEMA_TO_PROCESS="$SCHEMA_FILE"
fi

# Base NSwag configuration
EXCLUDED_TYPES="ApiException,ApiException\<TResult\>"
ADDITIONAL_NAMESPACES="BeyondImmersion.BannouService,BeyondImmersion.BannouService.$SERVICE_PASCAL"

# NOTE: Do NOT exclude inlined types from resolved schemas
# The x-inlined-types field tracks types inlined from other service schemas
# (e.g., VoiceTier from voice-api.yaml) - these need to be GENERATED, not excluded.
# Only types from common-api.yaml should be excluded (handled below).

# Extract $refs to common-api.yaml types (shared types like EntityType)
# These are generated once in CommonApiModels.cs, so we exclude them
# Use the ORIGINAL schema file for ref extraction (not resolved)
COMMON_API_REFS=$(extract_common_api_refs "$SCHEMA_FILE")
if [ -n "$COMMON_API_REFS" ]; then
    echo -e "${BLUE}ℹ️  Found common-api.yaml refs - excluding shared types${NC}"
    echo -e "  📦 Common types: $COMMON_API_REFS"
    EXCLUDED_TYPES="$EXCLUDED_TYPES,$COMMON_API_REFS"
    # Common types are in BeyondImmersion.BannouService namespace (already included above)
fi

# Extract SDK type mappings from schema (x-sdk-type extensions)
# This allows schemas to reference types from external SDKs without generating duplicates
SDK_TYPES_OUTPUT=$(python3 "$SCRIPT_DIR/extract-sdk-types.py" "$SCHEMA_FILE" --format=shell 2>/dev/null || echo "")

if [ -n "$SDK_TYPES_OUTPUT" ]; then
    # Parse the shell output (EXCLUDED_TYPES=... and SDK_NAMESPACES=...)
    SDK_EXCLUDED=$(echo "$SDK_TYPES_OUTPUT" | grep "^EXCLUDED_TYPES=" | cut -d'=' -f2)
    SDK_NAMESPACES=$(echo "$SDK_TYPES_OUTPUT" | grep "^SDK_NAMESPACES=" | cut -d'=' -f2)

    if [ -n "$SDK_EXCLUDED" ]; then
        echo -e "${BLUE}ℹ️  Found x-sdk-type annotations - excluding SDK types from generation${NC}"
        echo -e "  📦 SDK types: $SDK_EXCLUDED"
        EXCLUDED_TYPES="$EXCLUDED_TYPES,$SDK_EXCLUDED"
    fi

    if [ -n "$SDK_NAMESPACES" ]; then
        echo -e "  📁 SDK namespaces: $SDK_NAMESPACES"
        ADDITIONAL_NAMESPACES="$ADDITIONAL_NAMESPACES,$SDK_NAMESPACES"
    fi
fi

# Generate models using NSwag (DTOs only)
echo -e "${YELLOW}🔄 Running NSwag model generation...${NC}"

"$NSWAG_EXE" openapi2csclient \
    "/input:$SCHEMA_TO_PROCESS" \
    "/output:$OUTPUT_FILE" \
    "/namespace:BeyondImmersion.BannouService.$SERVICE_PASCAL" \
    "/generateClientClasses:false" \
    "/generateClientInterfaces:false" \
    "/generateDtoTypes:true" \
    "/excludedTypeNames:$EXCLUDED_TYPES" \
    "/jsonLibrary:SystemTextJson" \
    "/generateNullableReferenceTypes:true" \
    "/newLineBehavior:LF" \
    "/templateDirectory:../templates/nswag" \
    "/additionalNamespaceUsages:$ADDITIONAL_NAMESPACES"

# Check if generation succeeded
if [ $? -eq 0 ] && [ -f "$OUTPUT_FILE" ]; then
    # Post-process: Add [JsonRequired] after each [Required] attribute
    # NSwag generates [System.ComponentModel.DataAnnotations.Required] which is ignored by System.Text.Json
    # We add [System.Text.Json.Serialization.JsonRequired] which IS enforced during deserialization
    echo -e "${YELLOW}🔄 Post-processing: Adding [JsonRequired] attributes...${NC}"
    sed -i 's/\(\[System\.ComponentModel\.DataAnnotations\.Required[^]]*\]\)/\1\n    [System.Text.Json.Serialization.JsonRequired]/g' "$OUTPUT_FILE"

    # Post-process: Wrap enums with CS1591 pragma suppressions (enum members cannot have XML docs)
    echo -e "${YELLOW}🔄 Post-processing: Adding enum CS1591 suppressions...${NC}"
    postprocess_enum_suppressions "$OUTPUT_FILE"

    # Post-process: Add XML docs to AdditionalProperties
    echo -e "${YELLOW}🔄 Post-processing: Adding AdditionalProperties XML docs...${NC}"
    postprocess_additional_properties_docs "$OUTPUT_FILE"

    # Post-process: Convert enum member names to proper PascalCase
    echo -e "${YELLOW}🔄 Post-processing: Converting enum names to PascalCase...${NC}"
    postprocess_enum_pascalcase "$OUTPUT_FILE"

    FILE_SIZE=$(wc -l < "$OUTPUT_FILE" 2>/dev/null || echo "0")
    echo -e "${GREEN}✅ Generated models ($FILE_SIZE lines)${NC}"
else
    echo -e "${RED}❌ Failed to generate models${NC}"
    exit 1
fi

# Check for lifecycle events file and generate lifecycle event models if present
# Lifecycle events are now generated to schemas/Generated/ by generate-lifecycle-events.py
# If complex cross-file refs exist, a resolved version is created by resolve-event-refs.py
LIFECYCLE_EVENTS_FILE="../schemas/Generated/${SERVICE_NAME}-lifecycle-events.yaml"
LIFECYCLE_EVENTS_RESOLVED="../schemas/Generated/${SERVICE_NAME}-lifecycle-events-resolved.yaml"
LIFECYCLE_OUTPUT_DIR="../bannou-service/Generated/Events"
LIFECYCLE_OUTPUT_FILE="$LIFECYCLE_OUTPUT_DIR/${SERVICE_PASCAL}LifecycleEvents.cs"

if [ -f "$LIFECYCLE_EVENTS_FILE" ]; then
    echo -e "${YELLOW}🔄 Found lifecycle events file, generating lifecycle event models...${NC}"

    # Check if a resolved version exists (for schemas with complex cross-file refs)
    if [ -f "$LIFECYCLE_EVENTS_RESOLVED" ]; then
        LIFECYCLE_SCHEMA_TO_PROCESS="$LIFECYCLE_EVENTS_RESOLVED"
        echo -e "  📋 Schema: $LIFECYCLE_EVENTS_FILE (using resolved version)"
    else
        LIFECYCLE_SCHEMA_TO_PROCESS="$LIFECYCLE_EVENTS_FILE"
        echo -e "  📋 Schema: $LIFECYCLE_EVENTS_FILE"
    fi
    echo -e "  📁 Output: $LIFECYCLE_OUTPUT_FILE"

    # Ensure lifecycle events output directory exists
    mkdir -p "$LIFECYCLE_OUTPUT_DIR"

    # Extract $refs to API schema types from ORIGINAL file (not resolved)
    # These types exist in the service's API namespace and should be excluded
    LIFECYCLE_API_REFS=$(extract_api_refs "$LIFECYCLE_EVENTS_FILE" "$SERVICE_NAME")

    # Extract $refs to common-api.yaml types (shared types like EntityType)
    LIFECYCLE_COMMON_REFS=$(extract_common_api_refs "$LIFECYCLE_EVENTS_FILE")

    # Build exclusion list: base exclusions + any API-referenced types + common types
    LIFECYCLE_EXCLUSIONS="ApiException,ApiException\<TResult\>,BaseServiceEvent"

    # If using resolved schema, extract inlined types (API types that were inlined for NSwag)
    if [ "$LIFECYCLE_SCHEMA_TO_PROCESS" = "$LIFECYCLE_EVENTS_RESOLVED" ]; then
        echo -e "  ${BLUE}Using resolved schema (complex refs inlined for NSwag)${NC}"
        LIFECYCLE_INLINED_TYPES=$(extract_inlined_types "$LIFECYCLE_EVENTS_RESOLVED")
        if [ -n "$LIFECYCLE_INLINED_TYPES" ]; then
            LIFECYCLE_EXCLUSIONS="${LIFECYCLE_EXCLUSIONS},${LIFECYCLE_INLINED_TYPES}"
            echo -e "  ${BLUE}Excluding inlined API types: ${LIFECYCLE_INLINED_TYPES}${NC}"
        fi
    fi

    # Always exclude API refs (simple types like enums that aren't inlined but are referenced)
    if [ -n "$LIFECYCLE_API_REFS" ]; then
        LIFECYCLE_EXCLUSIONS="${LIFECYCLE_EXCLUSIONS},${LIFECYCLE_API_REFS}"
        echo -e "  ${BLUE}Excluding API types: ${LIFECYCLE_API_REFS}${NC}"
    fi
    if [ -n "$LIFECYCLE_COMMON_REFS" ]; then
        LIFECYCLE_EXCLUSIONS="${LIFECYCLE_EXCLUSIONS},${LIFECYCLE_COMMON_REFS}"
        echo -e "  ${BLUE}Excluding common types: ${LIFECYCLE_COMMON_REFS}${NC}"
    fi

    # Build namespace usages: base + service namespace if we have API refs
    # BeyondImmersion.BannouService is always included for common types
    LIFECYCLE_NAMESPACE_USAGES="BeyondImmersion.Bannou.Core,BeyondImmersion.BannouService"
    if [ -n "$LIFECYCLE_API_REFS" ]; then
        LIFECYCLE_NAMESPACE_USAGES="${LIFECYCLE_NAMESPACE_USAGES},BeyondImmersion.BannouService.${SERVICE_PASCAL}"
    fi

    "$NSWAG_EXE" openapi2csclient \
        "/input:$LIFECYCLE_SCHEMA_TO_PROCESS" \
        "/output:$LIFECYCLE_OUTPUT_FILE" \
        "/namespace:BeyondImmersion.BannouService.Events" \
        "/generateClientClasses:false" \
        "/generateClientInterfaces:false" \
        "/generateDtoTypes:true" \
        "/excludedTypeNames:${LIFECYCLE_EXCLUSIONS}" \
        "/additionalNamespaceUsages:${LIFECYCLE_NAMESPACE_USAGES}" \
        "/jsonLibrary:SystemTextJson" \
        "/generateNullableReferenceTypes:true" \
        "/newLineBehavior:LF" \
        "/templateDirectory:../templates/nswag"

    if [ $? -eq 0 ] && [ -f "$LIFECYCLE_OUTPUT_FILE" ]; then
        # Post-process: Add [JsonRequired] after each [Required] attribute
        echo -e "${YELLOW}🔄 Post-processing lifecycle events: Adding [JsonRequired] attributes...${NC}"
        sed -i 's/\(\[System\.ComponentModel\.DataAnnotations\.Required[^]]*\]\)/\1\n    [System.Text.Json.Serialization.JsonRequired]/g' "$LIFECYCLE_OUTPUT_FILE"

        # Post-process: Fix EventName shadowing - add 'override' keyword
        # Base class has 'virtual string EventName', generated classes shadow it without override
        echo -e "${YELLOW}🔄 Post-processing lifecycle events: Fixing EventName override...${NC}"
        sed -i 's/public string EventName { get; set; }/public override string EventName { get; set; }/g' "$LIFECYCLE_OUTPUT_FILE"

        # Post-process: Wrap enums with CS1591 pragma suppressions (enum members cannot have XML docs)
        echo -e "${YELLOW}🔄 Post-processing lifecycle events: Adding enum CS1591 suppressions...${NC}"
        postprocess_enum_suppressions "$LIFECYCLE_OUTPUT_FILE"

        # Post-process: Add XML docs to AdditionalProperties
        echo -e "${YELLOW}🔄 Post-processing lifecycle events: Adding AdditionalProperties XML docs...${NC}"
        postprocess_additional_properties_docs "$LIFECYCLE_OUTPUT_FILE"

        # Post-process: Convert enum member names to proper PascalCase
        echo -e "${YELLOW}🔄 Post-processing lifecycle events: Converting enum names to PascalCase...${NC}"
        postprocess_enum_pascalcase "$LIFECYCLE_OUTPUT_FILE"

        LIFECYCLE_FILE_SIZE=$(wc -l < "$LIFECYCLE_OUTPUT_FILE" 2>/dev/null || echo "0")
        echo -e "${GREEN}✅ Generated lifecycle event models ($LIFECYCLE_FILE_SIZE lines)${NC}"
    else
        echo -e "${RED}❌ Failed to generate lifecycle event models${NC}"
        exit 1
    fi
else
    echo -e "${BLUE}ℹ️  No lifecycle events file found (${SERVICE_NAME}-lifecycle-events.yaml)${NC}"
fi

exit 0
