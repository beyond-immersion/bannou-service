#!/bin/bash

# Generate NSwag controller from OpenAPI schema
# Usage: ./generate-controller.sh <service-name> [schema-file]

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
OUTPUT_FILE="$OUTPUT_DIR/${SERVICE_PASCAL}Controller.cs"

echo -e "${YELLOW}🔧 Generating controller for service: $SERVICE_NAME${NC}"
echo -e "  📋 Schema: $SCHEMA_FILE"
echo -e "  📁 Output: $OUTPUT_FILE"

# Validate schema file exists
if [ ! -f "$SCHEMA_FILE" ]; then
    echo -e "${RED}❌ Schema file not found: $SCHEMA_FILE${NC}"
    exit 1
fi

# Ensure output directory exists
mkdir -p "$OUTPUT_DIR"

# Function to find NSwag executable
find_nswag_exe() {
    local possible_paths=(
        "$HOME/.nuget/packages/nswag.msbuild/14.2.0/tools/Net90/dotnet-nswag.exe"
        "$HOME/.nuget/packages/nswag.msbuild/14.1.0/tools/Net90/dotnet-nswag.exe"
        "$HOME/.nuget/packages/nswag.msbuild/14.0.7/tools/Net90/dotnet-nswag.exe"
        "$(find $HOME/.nuget/packages/nswag.msbuild -name "dotnet-nswag.exe" 2>/dev/null | head -1)"
        "$(which nswag 2>/dev/null)"
    )

    for path in "${possible_paths[@]}"; do
        if [ -n "$path" ] && [ -f "$path" ]; then
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

# Check for x-manual-implementation flag in schema
HAS_MANUAL_IMPLEMENTATION=false
if grep -q "x-manual-implementation:\s*true" "$SCHEMA_FILE"; then
    HAS_MANUAL_IMPLEMENTATION=true
    echo -e "${YELLOW}🔧 Schema contains x-manual-implementation: true${NC}"
fi

# Check for controller-only methods for selective generation
if grep -q "x-controller-only:\s*true" "$SCHEMA_FILE"; then
    echo -e "${YELLOW}🔧 Schema contains x-controller-only methods${NC}"
    HAS_MANUAL_IMPLEMENTATION=true
fi

# Create filtered schema if x-controller-only methods exist
FILTERED_SCHEMA_FILE="$SCHEMA_FILE"
if grep -q "x-controller-only:\s*true" "$SCHEMA_FILE"; then
    echo -e "${YELLOW}🔧 Creating filtered schema excluding x-controller-only methods...${NC}"
    FILTERED_SCHEMA_FILE="/tmp/${SERVICE_NAME}-filtered-schema.yaml"

    # Use Python to remove x-controller-only methods from schema
    python3 -c "
import yaml
import sys

with open('$SCHEMA_FILE', 'r') as f:
    schema = yaml.safe_load(f)

if 'paths' in schema:
    paths_to_remove = []
    for path, path_data in schema['paths'].items():
        methods_to_remove = []
        for method, method_data in path_data.items():
            if isinstance(method_data, dict) and method_data.get('x-controller-only') is True:
                methods_to_remove.append(method)

        # Remove controller-only methods
        for method in methods_to_remove:
            del path_data[method]

        # If path has no methods left, mark for removal
        if not any(isinstance(v, dict) for v in path_data.values()):
            paths_to_remove.append(path)

    # Remove empty paths
    for path in paths_to_remove:
        del schema['paths'][path]

# Write filtered schema
with open('$FILTERED_SCHEMA_FILE', 'w') as f:
    yaml.dump(schema, f, default_flow_style=False, sort_keys=False)

print('Filtered schema created successfully', file=sys.stderr)
"
fi

# Generate controller using NSwag
echo -e "${YELLOW}🔄 Running NSwag controller generation...${NC}"

"$NSWAG_EXE" openapi2cscontroller \
    "/input:$FILTERED_SCHEMA_FILE" \
    "/output:$OUTPUT_FILE" \
    "/namespace:BeyondImmersion.BannouService.$SERVICE_PASCAL" \
    "/ControllerBaseClass:Microsoft.AspNetCore.Mvc.ControllerBase" \
    "/ClassName:${SERVICE_PASCAL}" \
    "/UseCancellationToken:true" \
    "/UseActionResultType:true" \
    "/GenerateModelValidationAttributes:true" \
    "/GenerateDataAnnotations:true" \
    "/GenerateDtoTypes:false" \
    "/JsonLibrary:NewtonsoftJson" \
    "/GenerateNullableReferenceTypes:true" \
    "/NewLineBehavior:LF" \
    "/GenerateOptionalParameters:false" \
    "/TemplateDirectory:../templates/nswag"

# Check if generation succeeded
if [ $? -eq 0 ] && [ -f "$OUTPUT_FILE" ]; then
    FILE_SIZE=$(wc -l < "$OUTPUT_FILE" 2>/dev/null || echo "0")
    echo -e "${GREEN}✅ Generated controller ($FILE_SIZE lines)${NC}"

    # Post-process for partial class support if needed
    if [ "$HAS_MANUAL_IMPLEMENTATION" = true ]; then
        echo -e "${YELLOW}🔧 Post-processing for partial class support...${NC}"

        # Convert the class declaration to partial
        sed -i 's/public abstract class \([^:]*\)ControllerBase/public abstract partial class \1ControllerBase/' "$OUTPUT_FILE"

        # Create empty partial controller if it doesn't exist
        PARTIAL_CONTROLLER="../lib-${SERVICE_NAME}/${SERVICE_PASCAL}Controller.cs"
        if [ ! -f "$PARTIAL_CONTROLLER" ]; then
            echo -e "${YELLOW}📝 Creating empty partial controller: $PARTIAL_CONTROLLER${NC}"

            cat > "$PARTIAL_CONTROLLER" << EOF
using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.$SERVICE_PASCAL;

/// <summary>
/// Manual implementation for endpoints that require custom logic.
/// This partial class extends the generated ${SERVICE_PASCAL}ControllerBase.
/// </summary>
public partial class ${SERVICE_PASCAL}Controller : ${SERVICE_PASCAL}ControllerBase
{
    private readonly I${SERVICE_PASCAL}Service _${SERVICE_NAME}Service;

    public ${SERVICE_PASCAL}Controller(I${SERVICE_PASCAL}Service ${SERVICE_NAME}Service)
    {
        _${SERVICE_NAME}Service = ${SERVICE_NAME}Service;
    }

    // TODO: Implement abstract methods marked with x-manual-implementation: true
    // The generated controller base class contains abstract methods that require manual implementation
}
EOF
            echo -e "${GREEN}✅ Created partial controller template${NC}"
        else
            echo -e "${YELLOW}📝 Manual partial controller already exists${NC}"
        fi

        echo -e "${GREEN}✅ Partial controller post-processing complete${NC}"
    fi

    # Cleanup temporary filtered schema file
    if [ "$FILTERED_SCHEMA_FILE" != "$SCHEMA_FILE" ] && [ -f "$FILTERED_SCHEMA_FILE" ]; then
        rm -f "$FILTERED_SCHEMA_FILE"
    fi
    exit 0
else
    echo -e "${RED}❌ Failed to generate controller${NC}"
    # Cleanup temporary filtered schema file
    if [ "$FILTERED_SCHEMA_FILE" != "$SCHEMA_FILE" ] && [ -f "$FILTERED_SCHEMA_FILE" ]; then
        rm -f "$FILTERED_SCHEMA_FILE"
    fi
    exit 1
fi

echo -e "${GREEN}✅ controller generation completed${NC}"
