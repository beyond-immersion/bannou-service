#!/bin/bash

# Generate NSwag service client from OpenAPI schema
# Usage: ./generate-client.sh <service-name> [schema-file]

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
OUTPUT_DIR="../bannou-service/Generated/Clients"
OUTPUT_FILE="$OUTPUT_DIR/${SERVICE_PASCAL}Client.cs"

echo -e "${YELLOW}🔧 Generating service client for: $SERVICE_NAME${NC}"
echo -e "  📋 Schema: $SCHEMA_FILE"
echo -e "  📁 Output: $OUTPUT_FILE"

# Validate schema file exists
if [ ! -f "$SCHEMA_FILE" ]; then
    echo -e "${RED}❌ Schema file not found: $SCHEMA_FILE${NC}"
    exit 1
fi

# Ensure output directory exists
mkdir -p "$OUTPUT_DIR"

# Create filtered schema for client generation (remove x-from-authorization parameters)
FILTERED_SCHEMA_FILE="$SCHEMA_FILE"
if grep -q "x-from-authorization" "$SCHEMA_FILE"; then
    echo -e "${YELLOW}🔧 Creating filtered schema for client generation (removing x-from-authorization parameters)...${NC}"
    FILTERED_SCHEMA_FILE="/tmp/${SERVICE_NAME}-client-filtered-schema.yaml"

    # Use Python to remove x-from-authorization parameters from client schema
    python3 -c "
import yaml
import sys

with open('$SCHEMA_FILE', 'r') as f:
    schema = yaml.safe_load(f)

if 'paths' in schema:
    for path, path_data in schema['paths'].items():
        for method, method_data in path_data.items():
            if isinstance(method_data, dict) and 'parameters' in method_data:
                # Filter out parameters marked with x-from-authorization
                original_params = method_data['parameters']
                filtered_params = []

                for param in original_params:
                    if param.get('x-from-authorization'):
                        print(f'Removing x-from-authorization parameter \"{param.get(\"name\", \"unknown\")}\" from {path} {method} for client generation', file=sys.stderr)
                    else:
                        filtered_params.append(param)

                method_data['parameters'] = filtered_params

# Write filtered schema for client generation
with open('$FILTERED_SCHEMA_FILE', 'w') as f:
    yaml.dump(schema, f, default_flow_style=False, sort_keys=False)

print('Client filtered schema created successfully', file=sys.stderr)
"
fi

# Find NSwag executable and ensure DOTNET_ROOT is set
require_nswag
ensure_dotnet_root

# Generate service client using NSwag (IMeshInvocationClient pattern)
echo -e "${YELLOW}🔄 Running NSwag client generation...${NC}"

"$NSWAG_EXE" openapi2csclient \
    "/input:$FILTERED_SCHEMA_FILE" \
    "/output:$OUTPUT_FILE" \
    "/namespace:BeyondImmersion.BannouService.$SERVICE_PASCAL" \
    "/className:${SERVICE_PASCAL}Client" \
    "/generateClientClasses:true" \
    "/generateClientInterfaces:true" \
    "/generateDtoTypes:false" \
    "/excludedTypeNames:ApiException,ApiException\<TResult\>" \
    "/injectHttpClient:false" \
    "/disposeHttpClient:true" \
    "/jsonLibrary:SystemTextJson" \
    "/generateNullableReferenceTypes:true" \
    "/newLineBehavior:LF" \
    "/generateOptionalParameters:true" \
    "/useHttpClientCreationMethod:true" \
    "/additionalNamespaceUsages:BeyondImmersion.BannouService,BeyondImmersion.BannouService.ServiceClients,BeyondImmersion.BannouService.$SERVICE_PASCAL" \
    "/templateDirectory:../templates/nswag"

# Check if generation succeeded
if [ $? -eq 0 ] && [ -f "$OUTPUT_FILE" ]; then
    FILE_SIZE=$(wc -l < "$OUTPUT_FILE" 2>/dev/null || echo "0")
    echo -e "${GREEN}✅ Generated service client ($FILE_SIZE lines)${NC}"

    # Cleanup temporary filtered schema file
    if [ "$FILTERED_SCHEMA_FILE" != "$SCHEMA_FILE" ] && [ -f "$FILTERED_SCHEMA_FILE" ]; then
        rm -f "$FILTERED_SCHEMA_FILE"
    fi
    exit 0
else
    echo -e "${RED}❌ Failed to generate service client${NC}"
    # Cleanup temporary filtered schema file
    if [ "$FILTERED_SCHEMA_FILE" != "$SCHEMA_FILE" ] && [ -f "$FILTERED_SCHEMA_FILE" ]; then
        rm -f "$FILTERED_SCHEMA_FILE"
    fi
    exit 1
fi
