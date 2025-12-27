#!/bin/bash

# Generate NSwag controller from OpenAPI schema
# Usage: ./generate-controller.sh <service-name> [schema-file]

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

# Find NSwag executable and ensure DOTNET_ROOT is set
require_nswag
ensure_dotnet_root

# Check for controller-only methods (including legacy x-manual-implementation flag)
HAS_CONTROLLER_ONLY_METHODS=false
if grep -q "x-controller-only:\s*true\|x-manual-implementation:\s*true" "$SCHEMA_FILE"; then
    HAS_CONTROLLER_ONLY_METHODS=true
    echo -e "${YELLOW}🔧 Schema contains controller-only methods (requires partial controller implementation)${NC}"
fi

# Create filtered schema for content type and URI format fixes ONLY
# Keep ALL methods including x-controller-only - Liquid template handles the conditional logic
FILTERED_SCHEMA_FILE="$SCHEMA_FILE"
if grep -q "application/yaml" "$SCHEMA_FILE" || grep -q 'format: uri' "$SCHEMA_FILE"; then
    echo -e "${YELLOW}🔧 Creating filtered schema for content type and URI format fixes...${NC}"
    FILTERED_SCHEMA_FILE="/tmp/${SERVICE_NAME}-filtered-schema.yaml"

    # Use Python to fix content types and URI formats, but KEEP controller-only methods
    python3 -c "
import yaml
import sys

with open('$SCHEMA_FILE', 'r') as f:
    schema = yaml.safe_load(f)

if 'paths' in schema:
    for path, path_data in schema['paths'].items():
        for method, method_data in path_data.items():
            if isinstance(method_data, dict):
                # Fix dual content-type issue: prefer JSON over YAML for consistent controller generation
                if 'requestBody' in method_data and 'content' in method_data['requestBody']:
                    content = method_data['requestBody']['content']
                    # If both application/json and application/yaml exist, remove application/yaml
                    if 'application/json' in content and 'application/yaml' in content:
                        print(f'Removing application/yaml from {path} {method} to prioritize JSON', file=sys.stderr)
                        del content['application/yaml']

                # Fix URI format issue: remove format: uri to prevent NSwag from generating Uri types
                if 'parameters' in method_data:
                    for param in method_data['parameters']:
                        if 'schema' in param and param['schema'].get('type') == 'string' and param['schema'].get('format') == 'uri':
                            print(f'Removing format: uri from {path} {method} parameter {param.get(\"name\", \"unknown\")} to maintain string type', file=sys.stderr)
                            del param['schema']['format']

# Write filtered schema - KEEP ALL METHODS including x-controller-only
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
    "/JsonLibrary:SystemTextJson" \
    "/GenerateNullableReferenceTypes:true" \
    "/NewLineBehavior:LF" \
    "/GenerateOptionalParameters:false" \
    "/DateType:System.DateTime" \
    "/DateTimeType:System.DateTime" \
    "/TemplateDirectory:../templates/nswag"

# Check if generation succeeded
if [ $? -eq 0 ] && [ -f "$OUTPUT_FILE" ]; then
    FILE_SIZE=$(wc -l < "$OUTPUT_FILE" 2>/dev/null || echo "0")
    echo -e "${GREEN}✅ Generated controller ($FILE_SIZE lines)${NC}"

    # Create empty partial controller if it doesn't exist and we have controller-only methods
    if [ "$HAS_CONTROLLER_ONLY_METHODS" = true ]; then
        PARTIAL_CONTROLLER="../lib-${SERVICE_NAME}/${SERVICE_PASCAL}Controller.cs"
        if [ ! -f "$PARTIAL_CONTROLLER" ]; then
            echo -e "${YELLOW}📝 Creating partial controller for x-controller-only methods: $PARTIAL_CONTROLLER${NC}"

            # Generate the controller class with method overrides by parsing the generated base controller
            python3 -c "
import re
import sys

# Read the generated base controller to extract abstract method signatures
try:
    with open('$OUTPUT_FILE', 'r') as f:
        base_controller_content = f.read()
except:
    print('Error: Could not read generated base controller file', file=sys.stderr)
    sys.exit(1)

# Find abstract method signatures
abstract_methods = []
lines = base_controller_content.split('\n')
i = 0
while i < len(lines):
    line = lines[i].strip()
    if 'public abstract' in line and ('Task<' in line or 'Task ' in line):
        # This is an abstract method declaration, capture the full signature
        method_sig = line

        # If the method signature spans multiple lines, capture them
        while not method_sig.rstrip().endswith(');'):
            i += 1
            if i < len(lines):
                method_sig += ' ' + lines[i].strip()
            else:
                break

        # Remove the trailing semicolon for abstract methods
        method_sig = method_sig.rstrip().rstrip(';')

        abstract_methods.append(method_sig)
    i += 1

# Generate the controller class content
controller_content = '''using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.$SERVICE_PASCAL;

/// <summary>
/// Manual implementation for endpoints that require custom logic.
/// This class extends the generated ${SERVICE_PASCAL}ControllerBase.
/// </summary>
public class ${SERVICE_PASCAL}Controller : ${SERVICE_PASCAL}ControllerBase
{
    public ${SERVICE_PASCAL}Controller(I${SERVICE_PASCAL}Service ${SERVICE_NAME}Service) : base(${SERVICE_NAME}Service)
    {
    }
'''

# Generate override methods for each abstract method
for method_sig in abstract_methods:
    # Convert 'public abstract' to 'public override' and add method body
    override_sig = method_sig.replace('public abstract', 'public override')

    # Extract method name for TODO comment
    method_name_match = re.search(r'(\w+)\s*\(', override_sig)
    method_name = method_name_match.group(1) if method_name_match else 'Unknown'

    controller_content += f'''
    {override_sig}
    {{
        // TODO: Implement {method_name} - WebSocket connection handling
        throw new System.NotImplementedException(\"{method_name} requires manual implementation for WebSocket functionality\");
    }}
'''

controller_content += '''
}
'''

print(controller_content)
" > "$PARTIAL_CONTROLLER"
            echo -e "${GREEN}✅ Created partial controller template${NC}"
        else
            echo -e "${YELLOW}📝 Manual partial controller already exists${NC}"
        fi
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
