#!/bin/bash

# ⛔⛔⛔ AGENT MODIFICATION PROHIBITED ⛔⛔⛔
# This script is part of Bannou's code generation pipeline.
# DO NOT MODIFY without EXPLICIT user instructions to change code generation.
#
# Changes to generation scripts silently break builds across ALL 48 services.
# An agent once changed namespace strings across 4 scripts in a single commit,
# breaking every service. If you believe a change is needed:
#   1. STOP and explain what you think is wrong
#   2. Show the EXACT diff you propose
#   3. Wait for EXPLICIT approval before touching ANY generation script
#
# This applies to: namespace strings, output paths, exclusion logic,
# NSwag parameters, post-processing steps, and file naming conventions.
# ⛔⛔⛔ AGENT MODIFICATION PROHIBITED ⛔⛔⛔

# Generate service configuration class from configuration schema
# Usage: ./generate-config.sh <service-name> [schema-file]

set -e  # Exit on any error

# Source common utilities
source "$(dirname "$0")/common.sh"

# Validate arguments
if [ $# -lt 1 ]; then
    log_error "Usage: $0 <service-name> [schema-file]"
    echo "Example: $0 account"
    echo "Example: $0 account ../schemas/account-configuration.yaml"
    exit 1
fi

SERVICE_NAME="$1"
SCHEMA_FILE="${2:-../schemas/${SERVICE_NAME}-configuration.yaml}"

SERVICE_PASCAL=$(to_pascal_case "$SERVICE_NAME")
OUTPUT_DIR="../plugins/lib-${SERVICE_NAME}/Generated"
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
    # If name is ALL_CAPS (with or without underscores), preserve it as-is
    # This matches NSwag behavior which keeps JSON_PATCH, GZIP, BROTLI, etc.
    if name.isupper() or (name.replace('_', '').isupper() and '_' in name):
        return name
    # If name has hyphens or underscores, split and capitalize each part
    # Otherwise, just ensure first letter is uppercase (preserve existing casing)
    if '-' in name or '_' in name:
        return ''.join(word.capitalize() for word in name.replace('-', '_').split('_'))
    else:
        return name[0].upper() + name[1:] if name else name

def escape_xml_description(desc):
    '''Escape a description for use in XML doc comments.
    Handles multi-line descriptions by converting newlines to proper XML comment continuation.'''
    if not desc:
        return desc
    # Replace newlines with XML comment line continuation
    lines = desc.strip().split('\\n')
    if len(lines) == 1:
        return lines[0]
    # For multi-line descriptions, join with proper XML comment formatting
    return '\\n    /// '.join(line.strip() for line in lines if line.strip())

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

    # Check for existing API enums to avoid duplicates
    # Look for enums in the corresponding API schema and generated models
    api_schema_path = f'../schemas/{service_name}-api.yaml'
    existing_api_enums = set()
    api_schema = None
    try:
        with open(api_schema_path, 'r') as api_f:
            api_schema = yaml.safe_load(api_f)
            # Extract enum names from components/schemas
            if api_schema and 'components' in api_schema and 'schemas' in api_schema['components']:
                for schema_name, schema_def in api_schema['components']['schemas'].items():
                    if schema_def and schema_def.get('type') == 'string' and 'enum' in schema_def:
                        existing_api_enums.add(schema_name)
    except FileNotFoundError:
        pass  # No API schema, no conflicts possible

    # Track enum types to generate
    enum_types = []

    # Track SDK namespaces needed for x-sdk-type references
    sdk_namespaces = set()

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
            prop_enum = prop_info.get('enum', None)
            prop_ref = prop_info.get('\$ref', None)

            # OpenAPI 3.0 range validation keywords (numeric)
            prop_minimum = prop_info.get('minimum', None)
            prop_maximum = prop_info.get('maximum', None)
            prop_exclusive_min = prop_info.get('exclusiveMinimum', False)
            prop_exclusive_max = prop_info.get('exclusiveMaximum', False)

            # OpenAPI 3.0 string length validation keywords
            prop_min_length = prop_info.get('minLength', None)
            prop_max_length = prop_info.get('maxLength', None)

            # OpenAPI 3.0 pattern validation keyword
            prop_pattern = prop_info.get('pattern', None)

            # OpenAPI 3.0 multipleOf validation keyword
            prop_multiple_of = prop_info.get('multipleOf', None)

            # Check if this is a $ref to an external enum type
            if prop_ref:
                # Extract type name from $ref path (e.g., 'contract-api.yaml#/components/schemas/EnforcementMode' -> 'EnforcementMode')
                ref_type_name = prop_ref.split('/')[-1]
                csharp_type = ref_type_name
                # Mark as enum for default value handling
                prop_enum = True  # Signal that this is an enum type
                print(f'# NOTE: Using \$ref enum {ref_type_name} from {prop_ref}', file=sys.stderr)

                # Check if the referenced type has x-sdk-type (use SDK type directly)
                if api_schema and 'components' in api_schema and 'schemas' in api_schema['components']:
                    ref_schema = api_schema['components']['schemas'].get(ref_type_name)
                    if ref_schema and 'x-sdk-type' in ref_schema:
                        sdk_type = ref_schema['x-sdk-type']
                        # Extract namespace (everything before the last dot)
                        last_dot = sdk_type.rfind('.')
                        if last_dot > 0:
                            sdk_namespace = sdk_type[:last_dot]
                            sdk_namespaces.add(sdk_namespace)
                            print(f'# NOTE: Adding SDK namespace {sdk_namespace} for x-sdk-type {sdk_type}', file=sys.stderr)
            # Check if this is an inline enum property
            elif prop_enum and prop_type == 'string':
                # Generate enum type name from property name
                enum_type_name = to_pascal_case(prop_name)
                # Check if this enum already exists in API schema (avoid duplicates)
                if enum_type_name in existing_api_enums:
                    # API schema already defines this enum - use it directly (same namespace)
                    csharp_type = enum_type_name
                    print(f'# NOTE: Reusing API enum {enum_type_name} (already in same namespace)', file=sys.stderr)
                else:
                    # Store enum for generation
                    enum_types.append({
                        'name': enum_type_name,
                        'values': prop_enum,
                        'description': prop_description
                    })
                    csharp_type = enum_type_name
            else:
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
            # For enums: no suffix if default provided, ? if no default
            # For other types: add ? suffix if no default provided
            is_enum = prop_enum is not None
            if csharp_type == 'string':
                nullable_suffix = '?' if prop_nullable else ''
            elif is_enum:
                nullable_suffix = '' if prop_default is not None else '?'
            else:
                nullable_suffix = '?' if prop_default is None else ''
            default_value = ''

            if prop_default is not None:
                if is_enum:
                    # Enum default: convert value to PascalCase enum member name
                    # e.g., 'event_only' -> 'EventOnly'
                    enum_value = to_pascal_case(str(prop_default))
                    default_value = f' = {csharp_type}.{enum_value};'
                elif csharp_type == 'string':
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
                'is_required': is_required,
                'minimum': prop_minimum,
                'maximum': prop_maximum,
                'exclusive_min': prop_exclusive_min,
                'exclusive_max': prop_exclusive_max,
                'min_length': prop_min_length,
                'max_length': prop_max_length,
                'pattern': prop_pattern,
                'multiple_of': prop_multiple_of
            })

    # Generate the configuration class
    print(f'''//----------------------
// <auto-generated>
//     Generated from OpenAPI schema: schemas/{service_name}-configuration.yaml
//
//     WARNING: DO NOT EDIT THIS FILE (FOUNDATION TENETS)
//     This file is auto-generated from configuration schema.
//     Any manual changes will be overwritten on next generation.
//
//     To add/modify configuration:
//     1. Edit the source schema (schemas/{service_name}-configuration.yaml)
//     2. Run: scripts/generate-all-services.sh
//
//     IMPLEMENTATION TENETS - Configuration-First:
//     - Access configuration via dependency injection, never Environment.GetEnvironmentVariable.
//     - ALL properties below MUST be referenced somewhere in the plugin (no dead config).
//     - Any hardcoded tunable (limit, timeout, threshold, capacity) in service code means
//       a configuration property is MISSING - add it to the configuration schema.
//     - If a property is unused, remove it from the configuration schema.
//
//     Example: public MyService({service_pascal}ServiceConfiguration config) {{ _config = config; }}
//
//     See: docs/reference/tenets/IMPLEMENTATION.md (Configuration-First, Internal Model Type Safety)
// </auto-generated>
//----------------------

using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;''')

    # Add SDK namespace usings for x-sdk-type references
    for ns in sorted(sdk_namespaces):
        print(f'using {ns};')

    print(f'''
#nullable enable

namespace BeyondImmersion.BannouService.{service_pascal};
''')

    # Generate enum types if any
    for enum_type in enum_types:
        # Suppress CS1591 for enum members (they don't need individual docs)
        escaped_desc = escape_xml_description(enum_type['description'])
        print(f'''
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
/// <summary>
/// {escaped_desc}
/// </summary>
public enum {enum_type['name']}
{{''')
        for value in enum_type['values']:
            # Convert value to PascalCase for C# enum member
            member_name = to_pascal_case(str(value))
            print(f'    {member_name},')
        print('''}
#pragma warning restore CS1591
''')

    print(f'''/// <summary>
/// Configuration class for {service_pascal} service.
/// Properties are automatically bound from environment variables.
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPLEMENTATION TENETS - Configuration-First:</b> Access configuration via dependency injection.
/// Never use <c>Environment.GetEnvironmentVariable()</c> directly in service code.
/// ALL properties in this class MUST be referenced somewhere in the plugin.
/// If a property is unused, remove it from the configuration schema.
/// </para>
/// <para>
/// Environment variable names follow the pattern: {{SERVICE}}_{{PROPERTY}} (e.g., AUTH_JWT_SECRET).
/// </para>
/// </remarks>
[ServiceConfiguration(typeof({service_pascal}Service))]
public class {service_pascal}ServiceConfiguration : IServiceConfiguration
{{
    /// <inheritdoc />
    public Guid? ForceServiceId {{ get; set; }}
''')

    for prop in config_properties:
        required_attr = '    [Required(AllowEmptyStrings = false)]\n' if prop.get('is_required', False) else ''

        # Generate ConfigRange attribute if minimum or maximum is specified
        range_attr = ''
        has_min = prop.get('minimum') is not None
        has_max = prop.get('maximum') is not None
        has_exclusive_min = prop.get('exclusive_min', False)
        has_exclusive_max = prop.get('exclusive_max', False)

        if has_min or has_max:
            range_parts = []
            if has_min:
                range_parts.append('Minimum = ' + str(prop['minimum']))
            if has_max:
                range_parts.append('Maximum = ' + str(prop['maximum']))
            if has_exclusive_min:
                range_parts.append('ExclusiveMinimum = true')
            if has_exclusive_max:
                range_parts.append('ExclusiveMaximum = true')
            range_attr = '    [ConfigRange(' + ', '.join(range_parts) + ')]\\n'

        # Generate ConfigStringLength attribute if minLength or maxLength is specified
        string_length_attr = ''
        has_min_length = prop.get('min_length') is not None
        has_max_length = prop.get('max_length') is not None

        if has_min_length or has_max_length:
            length_parts = []
            if has_min_length:
                length_parts.append('MinLength = ' + str(prop['min_length']))
            if has_max_length:
                length_parts.append('MaxLength = ' + str(prop['max_length']))
            string_length_attr = '    [ConfigStringLength(' + ', '.join(length_parts) + ')]\\n'

        # Generate ConfigPattern attribute if pattern is specified
        pattern_attr = ''
        prop_pattern = prop.get('pattern')
        if prop_pattern:
            # Escape the pattern for C# verbatim string (@\\\"...\\\")
            # Double any quotes in the pattern
            escaped_pattern = prop_pattern.replace('\\\"', '\\\"\\\"')
            pattern_attr = '    [ConfigPattern(@\\\"' + escaped_pattern + '\\\")]\\n'

        # Generate ConfigMultipleOf attribute if multipleOf is specified
        multiple_of_attr = ''
        prop_multiple_of = prop.get('multiple_of')
        if prop_multiple_of is not None:
            multiple_of_attr = '    [ConfigMultipleOf(' + str(prop_multiple_of) + ')]\\n'

        escaped_prop_desc = escape_xml_description(prop['description'])
        print(f'''    /// <summary>
    /// {escaped_prop_desc}
    /// Environment variable: {prop['env_var']}
    /// </summary>
{required_attr}{range_attr}{string_length_attr}{pattern_attr}{multiple_of_attr}    public {prop['type']} {prop['name']} {{ get; set; }}{prop['default']}
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
