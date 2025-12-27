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
    echo "Example: $0 accounts"
    echo "Example: $0 accounts ../schemas/accounts-api.yaml"
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

# Generate models using NSwag (DTOs only)
echo -e "${YELLOW}🔄 Running NSwag model generation...${NC}"

"$NSWAG_EXE" openapi2csclient \
    "/input:$SCHEMA_FILE" \
    "/output:$OUTPUT_FILE" \
    "/namespace:BeyondImmersion.BannouService.$SERVICE_PASCAL" \
    "/generateClientClasses:false" \
    "/generateClientInterfaces:false" \
    "/generateDtoTypes:true" \
    "/excludedTypeNames:ApiException,ApiException\<TResult\>" \
    "/jsonLibrary:SystemTextJson" \
    "/generateNullableReferenceTypes:true" \
    "/newLineBehavior:LF" \
    "/templateDirectory:../templates/nswag" \
    "/additionalNamespaceUsages:BeyondImmersion.BannouService,BeyondImmersion.BannouService.$SERVICE_PASCAL"

# Check if generation succeeded
if [ $? -eq 0 ] && [ -f "$OUTPUT_FILE" ]; then
    FILE_SIZE=$(wc -l < "$OUTPUT_FILE" 2>/dev/null || echo "0")
    echo -e "${GREEN}✅ Generated models ($FILE_SIZE lines)${NC}"
else
    echo -e "${RED}❌ Failed to generate models${NC}"
    exit 1
fi

# Check for lifecycle events file and generate lifecycle event models if present
# Lifecycle events are now generated to schemas/Generated/ by generate-lifecycle-events.py
LIFECYCLE_EVENTS_FILE="../schemas/Generated/${SERVICE_NAME}-lifecycle-events.yaml"
LIFECYCLE_OUTPUT_DIR="../bannou-service/Generated/Events"
LIFECYCLE_OUTPUT_FILE="$LIFECYCLE_OUTPUT_DIR/${SERVICE_PASCAL}LifecycleEvents.cs"

if [ -f "$LIFECYCLE_EVENTS_FILE" ]; then
    echo -e "${YELLOW}🔄 Found lifecycle events file, generating lifecycle event models...${NC}"
    echo -e "  📋 Schema: $LIFECYCLE_EVENTS_FILE"
    echo -e "  📁 Output: $LIFECYCLE_OUTPUT_FILE"

    # Ensure lifecycle events output directory exists
    mkdir -p "$LIFECYCLE_OUTPUT_DIR"

    "$NSWAG_EXE" openapi2csclient \
        "/input:$LIFECYCLE_EVENTS_FILE" \
        "/output:$LIFECYCLE_OUTPUT_FILE" \
        "/namespace:BeyondImmersion.BannouService.Events" \
        "/generateClientClasses:false" \
        "/generateClientInterfaces:false" \
        "/generateDtoTypes:true" \
        "/excludedTypeNames:ApiException,ApiException\<TResult\>" \
        "/jsonLibrary:SystemTextJson" \
        "/generateNullableReferenceTypes:true" \
        "/newLineBehavior:LF" \
        "/templateDirectory:../templates/nswag"

    if [ $? -eq 0 ] && [ -f "$LIFECYCLE_OUTPUT_FILE" ]; then
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
