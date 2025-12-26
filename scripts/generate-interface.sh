#!/bin/bash

# Generate service interface from schema
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

echo -e "${YELLOW}🔄 Extracting interface from schema...${NC}"

# Create base interface structure
cat > "$OUTPUT_FILE" << EOF
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.$SERVICE_PASCAL;

/// <summary>
/// Service interface for $SERVICE_PASCAL API
/// </summary>
public partial interface I${SERVICE_PASCAL}Service : IBannouService
{
EOF

# Generate interface methods directly from schema (not controller)
python3 -c "
import re
import sys
import yaml

service_pascal = '$SERVICE_PASCAL'
schema_file = '$SCHEMA_FILE'

def discover_generated_enums(service_pascal):
    # Models are now centralized in bannou-service/Generated/Models/
    models_file = f'../bannou-service/Generated/Models/{service_pascal}Models.cs'
    discovered_enums = {}

    try:
        with open(models_file, 'r') as f:
            content = f.read()

        enum_matches = re.findall(r'public enum (\w+)\s*\{([^}]+)\}', content, re.DOTALL)

        for enum_name, enum_body in enum_matches:
            pattern = r'EnumMember\(Value = @\"([^\"]+)\"\)'
            value_matches = re.findall(pattern, enum_body)
            if value_matches:
                value_set = frozenset(value_matches)
                # Prefer shorter enum names (likely base enums vs response-specific enums)
                if value_set not in discovered_enums or len(enum_name) < len(discovered_enums[value_set]):
                    discovered_enums[value_set] = enum_name

        # print(f'DEBUG: Discovered enums: {discovered_enums}', file=sys.stderr)
        return discovered_enums
    except Exception as e:
        print(f'DEBUG: Could not parse models file: {e}', file=sys.stderr)
        return {}

def convert_openapi_type_to_csharp(openapi_type, format_type=None, nullable=False, items=None, enum_values=None, discovered_enums=None):
    '''Convert OpenAPI type to C# type'''
    type_mapping = {
        'string': 'string',
        'integer': 'int',
        'number': 'double',
        'boolean': 'bool',
        'array': 'ICollection',
        'object': 'object'
    }

    # Check for enum types first by matching against discovered NSwag enums
    if openapi_type == 'string' and enum_values and discovered_enums:
        enum_set = frozenset(enum_values)
        if enum_set in discovered_enums:
            base_type = discovered_enums[enum_set]
            pass  # Successfully matched enum
        else:
            print(f'DEBUG: No enum match for: {enum_values}', file=sys.stderr)
            base_type = 'string'  # Fallback to string if no enum found
    elif openapi_type == 'string' and format_type == 'uuid':
        base_type = 'Guid'
    elif openapi_type == 'array' and items:
        # Handle array items with $ref (model references) or primitive types
        if '\$ref' in items:
            # Extract model name from reference
            ref_parts = items['\$ref'].split('/')
            item_type = ref_parts[-1] if ref_parts else 'object'
        else:
            item_type = convert_openapi_type_to_csharp(items.get('type', 'object'))
        base_type = f'ICollection<{item_type}>'
    else:
        base_type = type_mapping.get(openapi_type, 'object')

    # Handle nullable types
    if nullable:
        if base_type == 'string':
            base_type = 'string?'  # String needs explicit nullable marker in C# nullable reference types
        elif base_type not in ['object'] and not base_type.startswith('ICollection'):
            base_type += '?'

    return base_type

def convert_operation_id_to_method_name(operation_id):
    '''Convert camelCase operationId to PascalCase method name'''
    return operation_id[0].upper() + operation_id[1:] if operation_id else ''

try:
    with open(schema_file, 'r') as schema_f:
        schema = yaml.safe_load(schema_f)

    # Discover enum types from generated models
    discovered_enums = discover_generated_enums(service_pascal)

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

            # Skip methods marked as controller-only or manual-implementation
            if method_data.get('x-controller-only') is True or method_data.get('x-manual-implementation') is True:
                print(f'    # Excluding controller-only or manual-implementation method: {method_name}', file=sys.stderr)
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
                            if '\$ref' in content_schema:
                                # Extract model name from reference
                                ref_parts = content_schema['\$ref'].split('/')
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
                    is_required = param.get('required', False)  # Default to False for query params

                    # Handle $ref parameters (like enum references)
                    if '\$ref' in param_schema:
                        # Extract model name from reference
                        ref_parts = param_schema['\$ref'].split('/')
                        param_type = ref_parts[-1] if ref_parts else 'object'
                        # Make nullable if not required
                        if not is_required and param_type not in ['object', 'string']:
                            param_type += '?'
                    else:
                        param_type = convert_openapi_type_to_csharp(
                            param_schema.get('type', 'string'),
                            param_schema.get('format'),
                            not is_required,  # If not required, make nullable
                            param_schema.get('items'),
                            param_schema.get('enum'),  # Pass enum values
                            discovered_enums  # Pass discovered enums from models
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
                        param_str += ' = null' if '?' in param_type else ''

                    param_parts.append(param_str)

            # Handle request body parameter
            if 'requestBody' in method_data:
                request_body = method_data['requestBody']
                if 'content' in request_body and 'application/json' in request_body['content']:
                    content_schema = request_body['content']['application/json'].get('schema', {})
                    if '\$ref' in content_schema:
                        # Extract model name from reference
                        ref_parts = content_schema['\$ref'].split('/')
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
