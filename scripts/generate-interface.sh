#!/bin/bash

# Generate service interface from generated controller
# Usage: ./generate-interface.sh <service-name> [schema-file]

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
OUTPUT_FILE="$OUTPUT_DIR/I${SERVICE_PASCAL}Service.cs"
echo -e "${YELLOW}🔧 Generating service interface for: $SERVICE_NAME${NC}"
echo -e "  📋 Schema: $SCHEMA_FILE"
echo -e "  📁 Output: $OUTPUT_FILE"

# Validate schema file exists
if [ ! -f "$SCHEMA_FILE" ]; then
    echo -e "${RED}❌ Schema file not found: $SCHEMA_FILE${NC}"
    exit 1
fi

# Ensure output directory exists
mkdir -p "$OUTPUT_DIR"

echo -e "${YELLOW}🔄 Extracting interface from controller...${NC}"

# Create base interface structure
cat > "$OUTPUT_FILE" << EOF
using BeyondImmersion.BannouService;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.$SERVICE_PASCAL;

/// <summary>
/// Service interface for $SERVICE_PASCAL API - generated from controller
/// </summary>
public interface I${SERVICE_PASCAL}Service
{
EOF

# Generate interface methods directly from schema (not controller)
python3 -c "
import re
import sys
import yaml

# Get service name from command line args
service_pascal = '$SERVICE_PASCAL'
schema_file = '$SCHEMA_FILE'

def convert_openapi_type_to_csharp(openapi_type, format_type=None, nullable=False, items=None):
    '''Convert OpenAPI type to C# type'''
    type_mapping = {
        'string': 'string',
        'integer': 'int',
        'number': 'double',
        'boolean': 'bool',
        'array': 'ICollection',
        'object': 'object'
    }

    if openapi_type == 'string' and format_type == 'uuid':
        base_type = 'Guid'
    elif openapi_type == 'array' and items:
        item_type = convert_openapi_type_to_csharp(items.get('type', 'object'))
        base_type = f'ICollection<{item_type}>'
    else:
        base_type = type_mapping.get(openapi_type, 'object')

    # Handle nullable types
    if nullable and base_type not in ['string', 'object'] and not base_type.startswith('ICollection'):
        base_type += '?'

    return base_type

def convert_operation_id_to_method_name(operation_id):
    '''Convert camelCase operationId to PascalCase method name'''
    return operation_id[0].upper() + operation_id[1:] if operation_id else ''

try:
    with open(schema_file, 'r') as schema_f:
        schema = yaml.safe_load(schema_f)

    if 'paths' not in schema:
        print('    # Warning: No paths found in schema', file=sys.stderr)
        sys.exit(0)

    # Process each path and method
    for path, path_data in schema['paths'].items():
        for http_method, method_data in path_data.items():
            if not isinstance(method_data, dict):
                continue

            operation_id = method_data.get('operationId', '')
            if not operation_id:
                continue

            method_name = convert_operation_id_to_method_name(operation_id)

            # Skip methods marked as controller-only
            if method_data.get('x-controller-only') is True:
                print(f'    # Excluding controller-only method: {method_name}', file=sys.stderr)
                continue

            # Determine return type from responses
            return_type = 'object'
            if 'responses' in method_data:
                success_responses = ['200', '201', '202']
                for status_code in success_responses:
                    if status_code in method_data['responses']:
                        response = method_data['responses'][status_code]
                        if 'content' in response and 'application/json' in response['content']:
                            content_schema = response['content']['application/json'].get('schema', {})
                            if '$ref' in content_schema:
                                # Extract model name from reference
                                ref_parts = content_schema['$ref'].split('/')
                                return_type = ref_parts[-1] if ref_parts else 'object'
                            elif 'type' in content_schema:
                                return_type = convert_openapi_type_to_csharp(
                                    content_schema['type'],
                                    content_schema.get('format'),
                                    content_schema.get('nullable', False),
                                    content_schema.get('items')
                                )
                        break

            # Build parameter list
            param_parts = []
            if 'parameters' in method_data:
                for param in method_data['parameters']:
                    param_name = param.get('name', '')
                    param_schema = param.get('schema', {})
                    param_type = convert_openapi_type_to_csharp(
                        param_schema.get('type', 'string'),
                        param_schema.get('format'),
                        not param.get('required', True),  # If not required, make nullable
                        param_schema.get('items')
                    )

                    # Add default value if specified
                    param_str = f'{param_type} {param_name}'
                    if 'default' in param_schema:
                        default_val = param_schema['default']
                        if param_schema.get('type') == 'string':
                            param_str += f' = \"{default_val}\"'
                        elif param_schema.get('type') == 'boolean':
                            param_str += f' = {str(default_val).lower()}'
                        else:
                            param_str += f' = {default_val}'
                    elif not param.get('required', True):
                        # Optional parameter without default
                        param_str += ' = null' if '?' in param_type or param_type == 'string' else ''

                    param_parts.append(param_str)

            # Handle request body parameter
            if 'requestBody' in method_data:
                request_body = method_data['requestBody']
                if 'content' in request_body and 'application/json' in request_body['content']:
                    content_schema = request_body['content']['application/json'].get('schema', {})
                    if '$ref' in content_schema:
                        # Extract model name from reference
                        ref_parts = content_schema['$ref'].split('/')
                        body_type = ref_parts[-1] if ref_parts else 'object'
                        param_parts.append(f'{body_type} body')

            # Always add CancellationToken
            param_parts.append('CancellationToken cancellationToken = default(CancellationToken)')

            # Generate method signature
            params_str = ', '.join(param_parts)

            print(f'''        /// <summary>
        /// {method_name} operation
        /// </summary>
        Task<(StatusCodes, {return_type}?)> {method_name}Async({params_str});
''')

except Exception as e:
    print(f'    # Error processing schema: {e}', file=sys.stderr)
    sys.exit(1)
" >> "$OUTPUT_FILE"

# Close the interface
cat >> "$OUTPUT_FILE" << EOF
}
EOF

# Check if generation succeeded
if [ -f "$OUTPUT_FILE" ]; then
    FILE_SIZE=$(wc -l < "$OUTPUT_FILE" 2>/dev/null || echo "0")
    echo -e "${GREEN}✅ Generated service interface ($FILE_SIZE lines)${NC}"
    exit 0
else
    echo -e "${RED}❌ Failed to generate service interface${NC}"
    exit 1
fi
