#!/bin/bash

# Generate NSwag service client from OpenAPI schema
# Usage: ./generate-client.sh <service-name> [schema-file]

set -e  # Exit on any error

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Validate arguments
if [ $# -lt 1 ]; then
    echo -e "${RED}Usage: $0 <service-name> [schema-file]${NC}"
    echo "Example: $0 accounts"
    echo "Example: $0 accounts ../schemas/accounts-api.yaml"
    exit 1
fi

SERVICE_NAME="$1"
SCHEMA_FILE="${2:-../schemas/${SERVICE_NAME}-api.yaml}"

# Helper function to convert hyphenated names to PascalCase
to_pascal_case() {
    local input="$1"
    echo "$input" | sed 's/-/ /g' | awk '{for(i=1;i<=NF;i++) $i=toupper(substr($i,1,1)) tolower(substr($i,2))} 1' | sed 's/ //g'
}

SERVICE_PASCAL=$(to_pascal_case "$SERVICE_NAME")
OUTPUT_DIR="../lib-${SERVICE_NAME}/Generated"
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

    # Windows or fallback: try MSBuild package paths first
    local possible_paths=(
        "$HOME/.nuget/packages/nswag.msbuild/14.2.0/tools/Net90/dotnet-nswag.exe"
        "$HOME/.nuget/packages/nswag.msbuild/14.1.0/tools/Net90/dotnet-nswag.exe"
        "$HOME/.nuget/packages/nswag.msbuild/14.0.7/tools/Net90/dotnet-nswag.exe"
        "$(find $HOME/.nuget/packages/nswag.msbuild -name "dotnet-nswag.exe" 2>/dev/null | head -1)"
        "$(which nswag 2>/dev/null)"
    )

    for path in "${possible_paths[@]}"; do
        if [ -n "$path" ] && [ -f "$path" ]; then
            # On Linux, skip .exe files as they won't execute
            if [[ "$OSTYPE" == "linux-gnu"* ]] && [[ "$path" == *.exe ]]; then
                continue
            fi
            echo "$path"
            return 0
        fi
    done

    return 1
}

# Find NSwag executable
NSWAG_EXE=$(find_nswag_exe)
if [ -z "$NSWAG_EXE" ]; then
    echo -e "${RED}❌ NSwag executable not found${NC}"
    exit 1
fi

echo -e "${GREEN}✅ Found NSwag at: $NSWAG_EXE${NC}"

# Ensure DOTNET_ROOT is set for NSwag global tool to work properly
if [ -z "$DOTNET_ROOT" ]; then
    # Try to find dotnet installation
    if [ -d "/usr/local/share/dotnet" ]; then
        export DOTNET_ROOT="/usr/local/share/dotnet"
    elif [ -d "/usr/share/dotnet" ]; then
        export DOTNET_ROOT="/usr/share/dotnet"
    elif command -v dotnet >/dev/null 2>&1; then
        # Get dotnet installation path
        DOTNET_PATH=$(dirname "$(readlink -f "$(which dotnet)")")
        export DOTNET_ROOT="$DOTNET_PATH"
    fi
fi

# Generate service client using NSwag (DaprServiceClientBase pattern)
echo -e "${YELLOW}🔄 Running NSwag client generation...${NC}"

"$NSWAG_EXE" openapi2csclient \
    "/input:$FILTERED_SCHEMA_FILE" \
    "/output:$OUTPUT_FILE" \
    "/namespace:BeyondImmersion.BannouService.$SERVICE_PASCAL" \
    "/clientBaseClass:BeyondImmersion.BannouService.ServiceClients.DaprServiceClientBase" \
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
