#!/bin/bash

# Generate service configuration class from configuration schema
# Usage: ./generate-config.sh <service-name> [schema-file]

set -e  # Exit on any error

# Source common utilities
source "$(dirname "$0")/common.sh"

# Validate arguments
if [ $# -lt 1 ]; then
    log_error "Usage: $0 <service-name> [schema-file]"
    echo "Example: $0 accounts"
    echo "Example: $0 accounts ../schemas/accounts-configuration.yaml"
    exit 1
fi

SERVICE_NAME="$1"
SCHEMA_FILE="${2:-../schemas/${SERVICE_NAME}-configuration.yaml}"

SERVICE_PASCAL=$(to_pascal_case "$SERVICE_NAME")
OUTPUT_DIR="../lib-${SERVICE_NAME}/Generated"
OUTPUT_FILE="$OUTPUT_DIR/${SERVICE_PASCAL}ServiceConfiguration.cs"

echo -e "${YELLOW}🔧 Generating configuration for service: $SERVICE_NAME${NC}"
echo -e "  📋 Schema: $SCHEMA_FILE"
echo -e "  📁 Output: $OUTPUT_FILE"

# Validate schema file exists
if [ ! -f "$SCHEMA_FILE" ]; then
    echo -e "${RED}❌ Schema file not found: $SCHEMA_FILE${NC}"
    exit 1
fi

# Ensure output directory exists
mkdir -p "$OUTPUT_DIR"

echo -e "${YELLOW}🔄 Extracting configuration from schema...${NC}"

# Generate configuration class from schema
python3 -c "
import yaml
import sys
import re

def to_pascal_case(name):
    return ''.join(word.capitalize() for word in name.replace('-', '_').split('_'))

def to_property_name(name):
    # Convert environment variable style to property name
    # e.g., 'MAX_CONNECTIONS' -> 'MaxConnections', 'JwtSecret' -> 'JwtSecret'
    # Handle both camelCase/PascalCase and UPPER_CASE formats
    if '_' in name.upper():
        # Handle UPPER_CASE style: 'JWT_SECRET' -> 'JwtSecret'
        return to_pascal_case(name.lower())
    else:
        # Handle camelCase/PascalCase: 'JwtSecret' -> 'JwtSecret'
        return name[0].upper() + name[1:] if name else name

try:
    with open('$SCHEMA_FILE', 'r') as f:
        schema = yaml.safe_load(f)

    service_pascal = '$SERVICE_PASCAL'
    service_name = '$SERVICE_NAME'

    # Extract configuration from x-service-configuration section
    config_properties = []
    if 'x-service-configuration' in schema:
        config_section = schema['x-service-configuration']

        # Handle both direct properties and properties under 'properties' key
        properties_dict = config_section.get('properties', config_section)

        for prop_name, prop_info in properties_dict.items():
            prop_type = prop_info.get('type', 'string')
            prop_default = prop_info.get('default', None)
            prop_description = prop_info.get('description', f'{prop_name} configuration property')
            prop_env_var = prop_info.get('env', prop_name.upper())
            prop_nullable = prop_info.get('nullable', False)

            # Convert type to C# type
            csharp_type = {
                'string': 'string',
                'integer': 'int',
                'number': 'double',
                'boolean': 'bool',
                'array': 'string[]'
            }.get(prop_type, 'string')

            # Handle nullable types and defaults for properties
            # For strings: add ? suffix if explicitly nullable in schema
            # For other types: add ? suffix if no default provided
            if csharp_type == 'string':
                nullable_suffix = '?' if prop_nullable else ''
            else:
                nullable_suffix = '?' if prop_default is None else ''
            default_value = ''

            if prop_default is not None:
                if csharp_type == 'string':
                    default_value = f' = \"{prop_default}\";'
                elif csharp_type == 'bool':
                    default_value = f' = {str(prop_default).lower()};'
                elif csharp_type == 'string[]':
                    # Convert Python list to C# array literal with double quotes
                    if isinstance(prop_default, list):
                        array_items = ', '.join(f'\"{item}\"' for item in prop_default)
                        default_value = f' = [{array_items}];'
                    else:
                        default_value = f' = {prop_default};'
                else:
                    default_value = f' = {prop_default};'
            elif csharp_type == 'string' and not prop_nullable:
                # Required string with no default - mark as required for validation
                # Using string.Empty satisfies nullable reference types, but [Required]
                # ensures startup validation fails if not configured
                default_value = ' = string.Empty;'
                prop_info['is_required'] = True

            property_name = to_property_name(prop_name)
            is_required = prop_info.get('is_required', False)

            config_properties.append({
                'name': property_name,
                'type': csharp_type + nullable_suffix,
                'default': default_value,
                'description': prop_description,
                'env_var': prop_env_var,
                'is_required': is_required
            })

    # Generate the configuration class
    print(f'''//----------------------
// <auto-generated>
//     Generated from OpenAPI schema: schemas/{service_name}-configuration.yaml
//
//     WARNING: DO NOT EDIT THIS FILE (TENETS T1/T2)
//     This file is auto-generated from configuration schema.
//     Any manual changes will be overwritten on next generation.
//
//     To add/modify configuration:
//     1. Edit the source schema (schemas/{service_name}-configuration.yaml)
//     2. Run: scripts/generate-all-services.sh
//
//     USAGE (IMPLEMENTATION TENETS - Configuration-First):
//     Access configuration via dependency injection, never Environment.GetEnvironmentVariable.
//     Example: public MyService({service_pascal}ServiceConfiguration config) {{ _config = config; }}
//
//     See: docs/reference/tenets/FOUNDATION.md
//     See: docs/reference/tenets/IMPLEMENTATION.md
// </auto-generated>
//----------------------

using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

#nullable enable

namespace BeyondImmersion.BannouService.{service_pascal};

/// <summary>
/// Configuration class for {service_pascal} service.
/// Properties are automatically bound from environment variables.
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPLEMENTATION TENETS - Configuration-First:</b> Access configuration via dependency injection.
/// Never use <c>Environment.GetEnvironmentVariable()</c> directly in service code.
/// </para>
/// <para>
/// Environment variable names follow the pattern: {{SERVICE}}_{{PROPERTY}} (e.g., AUTH_JWT_SECRET).
/// </para>
/// </remarks>
[ServiceConfiguration(typeof({service_pascal}Service))]
public class {service_pascal}ServiceConfiguration : IServiceConfiguration
{{
    /// <inheritdoc />
    public string? ForceServiceId {{ get; set; }}
''')

    if config_properties:
        for prop in config_properties:
            required_attr = '    [Required(AllowEmptyStrings = false)]\n' if prop.get('is_required', False) else ''
            print(f'''    /// <summary>
    /// {prop['description']}
    /// Environment variable: {prop['env_var']}
    /// </summary>
{required_attr}    public {prop['type']} {prop['name']} {{ get; set; }}{prop['default']}
''')
    else:
        print(f'''    /// <summary>
    /// Default configuration property - can be removed if not needed.
    /// Environment variable: {service_name.upper()}_ENABLED
    /// </summary>
    public bool Enabled {{ get; set; }} = true;
''')

    print('}')

except FileNotFoundError:
    print(f'Schema file not found: $SCHEMA_FILE', file=sys.stderr)
    sys.exit(1)
except Exception as e:
    print(f'Error parsing schema: {e}', file=sys.stderr)
    sys.exit(1)
" > "$OUTPUT_FILE"

# Check if generation succeeded
if [ $? -eq 0 ] && [ -f "$OUTPUT_FILE" ]; then
    FILE_SIZE=$(wc -l < "$OUTPUT_FILE" 2>/dev/null || echo "0")
    echo -e "${GREEN}✅ Generated service configuration ($FILE_SIZE lines)${NC}"

    if ! grep -q "x-service-configuration" "$SCHEMA_FILE"; then
        echo -e "${YELLOW}💡 Add 'x-service-configuration:' section to schema for custom configuration properties${NC}"
    fi

    exit 0
else
    echo -e "${RED}❌ Failed to generate service configuration${NC}"
    exit 1
fi
