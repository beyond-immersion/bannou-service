#!/usr/bin/env python3
"""
Generate Unreal Engine C++ type headers from consolidated OpenAPI schema.

This script reads the consolidated client schema and generates:
- BannouTypes.h: All request/response structs with USTRUCT/UPROPERTY macros
- BannouEnums.h: All shared enums with UENUM macro
- BannouEndpoints.h: Endpoint constants and registry

Type Mappings:
| OpenAPI          | Unreal C++        |
|------------------|-------------------|
| string           | FString           |
| string (uuid)    | FGuid             |
| string (date-time)| FDateTime        |
| integer          | int32             |
| integer (int64)  | int64             |
| number           | float             |
| number (double)  | double            |
| boolean          | bool              |
| array            | TArray<T>         |
| nullable         | TOptional<T>      |

Usage:
    python3 scripts/generate-unreal-types.py
"""

import json
import re
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Optional

try:
    from ruamel.yaml import YAML
    yaml = YAML()
    yaml.preserve_quotes = True
except ImportError:
    print("ERROR: ruamel.yaml is required. Install with: pip install ruamel.yaml")
    sys.exit(1)


def to_pascal_case(name: str) -> str:
    """Convert kebab-case, snake_case, or camelCase to PascalCase."""
    name = name.replace('-', ' ').replace('_', ' ')
    if ' ' not in name:
        name = re.sub(r'([a-z])([A-Z])', r'\1 \2', name)
    parts = name.split()
    return ''.join(p[0].upper() + p[1:] if p else '' for p in parts)


def to_camel_case(name: str) -> str:
    """Convert to camelCase."""
    pascal = to_pascal_case(name)
    return pascal[0].lower() + pascal[1:] if pascal else ''


def get_cpp_type(prop_schema: dict, prop_name: str, is_required: bool) -> tuple[str, bool]:
    """
    Get C++ type from OpenAPI schema property.
    Returns (type_string, needs_optional_wrapper).
    """
    prop_type = prop_schema.get('type', 'object')
    prop_format = prop_schema.get('format')
    is_nullable = prop_schema.get('nullable', False)

    # Check for $ref
    if '$ref' in prop_schema:
        ref_type = prop_schema['$ref'].split('/')[-1]
        cpp_type = f'F{ref_type}'
        # Struct refs that are nullable need TOptional
        if is_nullable or not is_required:
            return f'TOptional<{cpp_type}>', False
        return cpp_type, False

    # Handle arrays
    if prop_type == 'array':
        items = prop_schema.get('items', {})
        item_type, _ = get_cpp_type(items, prop_name, True)
        cpp_type = f'TArray<{item_type}>'
        # Arrays are never wrapped in TOptional, they're just empty
        return cpp_type, False

    # Handle enums
    if 'enum' in prop_schema:
        # Generate enum name from property name
        enum_name = f'E{to_pascal_case(prop_name)}'
        if is_nullable or not is_required:
            return f'TOptional<{enum_name}>', False
        return enum_name, False

    # Handle basic types
    type_mapping = {
        ('string', None): 'FString',
        ('string', 'uuid'): 'FGuid',
        ('string', 'date-time'): 'FDateTime',
        ('string', 'uri'): 'FString',
        ('string', 'email'): 'FString',
        ('string', 'password'): 'FString',
        ('integer', None): 'int32',
        ('integer', 'int32'): 'int32',
        ('integer', 'int64'): 'int64',
        ('number', None): 'float',
        ('number', 'float'): 'float',
        ('number', 'double'): 'double',
        ('boolean', None): 'bool',
        ('object', None): 'TMap<FString, FString>',  # Generic object
    }

    cpp_type = type_mapping.get((prop_type, prop_format))
    if cpp_type is None:
        cpp_type = type_mapping.get((prop_type, None), 'FString')

    # Handle nullable/optional
    # Note: FString and TArray are never wrapped - they have natural "empty" states
    if (is_nullable or not is_required) and cpp_type not in ('FString', 'bool'):
        if not cpp_type.startswith('TArray') and not cpp_type.startswith('TMap'):
            return f'TOptional<{cpp_type}>', False

    return cpp_type, False


def generate_enum(enum_name: str, values: list, description: str = '') -> str:
    """Generate a UENUM definition."""
    lines = []

    if description:
        lines.append(f'/** {description} */')

    lines.append('UENUM(BlueprintType)')
    lines.append(f'enum class E{enum_name} : uint8')
    lines.append('{')

    for value in values:
        safe_value = to_pascal_case(str(value))
        lines.append(f'    {safe_value} UMETA(DisplayName = "{value}"),')

    lines.append('};')
    lines.append('')

    return '\n'.join(lines)


def generate_struct(type_name: str, schema: dict, all_schemas: dict) -> str:
    """Generate a USTRUCT definition from OpenAPI schema."""
    lines = []

    description = schema.get('description', '').split('\n')[0]  # First line only
    if description:
        lines.append('/**')
        lines.append(f' * {description}')
        lines.append(' */')

    lines.append('USTRUCT(BlueprintType)')
    lines.append(f'struct F{type_name}')
    lines.append('{')
    lines.append('    GENERATED_BODY()')
    lines.append('')

    properties = schema.get('properties', {})
    required = set(schema.get('required', []))

    for prop_name, prop_schema in properties.items():
        prop_desc = prop_schema.get('description', '').split('\n')[0]
        cpp_name = to_pascal_case(prop_name)
        is_required = prop_name in required

        cpp_type, _ = get_cpp_type(prop_schema, prop_name, is_required)

        # Add property comment
        if prop_desc:
            lines.append(f'    /** {prop_desc} */')

        # Add UPROPERTY macro
        lines.append('    UPROPERTY(EditAnywhere, BlueprintReadWrite, Category = "Bannou")')

        # Add default value for optional basic types
        default_value = ''
        if cpp_type == 'bool':
            default_val = prop_schema.get('default', False)
            default_value = f' = {"true" if default_val else "false"}'
        elif cpp_type == 'int32' or cpp_type == 'int64':
            default_val = prop_schema.get('default', 0)
            default_value = f' = {default_val}'
        elif cpp_type == 'float' or cpp_type == 'double':
            default_val = prop_schema.get('default', 0.0)
            default_value = f' = {default_val}f' if cpp_type == 'float' else f' = {default_val}'

        lines.append(f'    {cpp_type} {cpp_name}{default_value};')
        lines.append('')

    lines.append('};')
    lines.append('')

    return '\n'.join(lines)


def collect_inline_enums(schemas: dict) -> dict:
    """Collect all inline enum definitions from schemas."""
    enums = {}

    def process_schema(schema: dict, parent_name: str = ''):
        if not isinstance(schema, dict):
            return

        # Check for enum in this schema
        if 'enum' in schema:
            enum_values = schema['enum']
            description = schema.get('description', '')
            # Use parent property name or schema name for enum name
            enum_name = parent_name if parent_name else 'Unknown'
            if enum_name not in enums:
                enums[enum_name] = {'values': enum_values, 'description': description}

        # Process properties
        for prop_name, prop_schema in schema.get('properties', {}).items():
            if isinstance(prop_schema, dict):
                if 'enum' in prop_schema:
                    process_schema(prop_schema, prop_name)
                elif prop_schema.get('type') == 'array':
                    items = prop_schema.get('items', {})
                    if 'enum' in items:
                        process_schema(items, prop_name)

    for schema_name, schema in schemas.items():
        process_schema(schema, schema_name)

    return enums


def generate_types_header(schemas: dict) -> str:
    """Generate BannouTypes.h with all struct definitions."""
    timestamp = datetime.now(timezone.utc).strftime('%Y-%m-%d %H:%M:%S UTC')

    header = f'''// <auto-generated>
// BannouTypes.h - Request/Response types for Bannou client API
// Generated by generate-unreal-types.py on {timestamp}
// Do not edit this file manually.
// </auto-generated>

#pragma once

#include "CoreMinimal.h"
#include "BannouEnums.h"
#include "BannouTypes.generated.h"

/**
 * Bannou API Types
 *
 * This header contains all request and response structs for the Bannou client API.
 * All structs are compatible with Unreal Engine's reflection system for Blueprint access.
 */

'''

    # Generate forward declarations for all types
    header += '// Forward declarations\n'
    for type_name in sorted(schemas.keys()):
        header += f'struct F{type_name};\n'
    header += '\n'

    # Generate struct definitions
    for type_name in sorted(schemas.keys()):
        schema = schemas[type_name]
        header += generate_struct(type_name, schema, schemas)
        header += '\n'

    return header


def generate_enums_header(schemas: dict) -> str:
    """Generate BannouEnums.h with all enum definitions."""
    timestamp = datetime.now(timezone.utc).strftime('%Y-%m-%d %H:%M:%S UTC')

    # Collect all enums
    enums = collect_inline_enums(schemas)

    header = f'''// <auto-generated>
// BannouEnums.h - Enum types for Bannou client API
// Generated by generate-unreal-types.py on {timestamp}
// Do not edit this file manually.
// </auto-generated>

#pragma once

#include "CoreMinimal.h"
#include "BannouEnums.generated.h"

/**
 * Bannou API Enums
 *
 * This header contains all enum types used by the Bannou client API.
 * All enums are compatible with Unreal Engine's reflection system for Blueprint access.
 */

'''

    for enum_name, enum_data in sorted(enums.items()):
        header += generate_enum(
            to_pascal_case(enum_name),
            enum_data['values'],
            enum_data['description']
        )
        header += '\n'

    return header


def generate_endpoints_header(paths: dict, schemas: dict) -> str:
    """Generate BannouEndpoints.h with endpoint constants and registry."""
    timestamp = datetime.now(timezone.utc).strftime('%Y-%m-%d %H:%M:%S UTC')

    header = f'''// <auto-generated>
// BannouEndpoints.h - Endpoint constants and registry for Bannou client API
// Generated by generate-unreal-types.py on {timestamp}
// Do not edit this file manually.
// </auto-generated>

#pragma once

#include "CoreMinimal.h"
#include "BannouEndpoints.generated.h"

/**
 * Bannou API Endpoints
 *
 * This header contains endpoint constants and a registry for all available
 * Bannou client API endpoints.
 */

namespace Bannou
{{
    namespace Endpoints
    {{
'''

    # Collect endpoints by service
    endpoints_by_service: dict[str, list] = {}

    for path, methods in paths.items():
        if not isinstance(methods, dict):
            continue

        for method, details in methods.items():
            if method.lower() not in ('get', 'post', 'put', 'delete', 'patch'):
                continue

            if not isinstance(details, dict):
                continue

            # Extract service name from path
            parts = path.strip('/').split('/')
            service = parts[0] if parts else 'unknown'

            operation_id = details.get('operationId', '')
            if not operation_id:
                continue

            const_name = to_pascal_case(operation_id)

            if service not in endpoints_by_service:
                endpoints_by_service[service] = []

            # Get request/response types
            request_type = None
            response_type = None

            request_body = details.get('requestBody', {})
            if request_body:
                content = request_body.get('content', {})
                json_content = content.get('application/json', {})
                schema_ref = json_content.get('schema', {})
                if '$ref' in schema_ref:
                    request_type = 'F' + schema_ref['$ref'].split('/')[-1]

            responses = details.get('responses', {})
            success_response = responses.get('200', {})
            if success_response:
                content = success_response.get('content', {})
                json_content = content.get('application/json', {})
                schema_ref = json_content.get('schema', {})
                if '$ref' in schema_ref:
                    response_type = 'F' + schema_ref['$ref'].split('/')[-1]

            endpoints_by_service[service].append({
                'const_name': const_name,
                'method': method.upper(),
                'path': path,
                'request_type': request_type,
                'response_type': response_type,
                'summary': details.get('summary', ''),
            })

    # Generate endpoint constants organized by service
    for service, endpoints in sorted(endpoints_by_service.items()):
        header += f'        // {to_pascal_case(service)} Service\n'
        for ep in endpoints:
            header += f'        /** {ep["summary"]} */\n'
            header += f'        constexpr const TCHAR* {ep["const_name"]} = TEXT("{ep["method"]}:{ep["path"]}");\n\n'

    header += '''    } // namespace Endpoints

    /**
     * Information about a single endpoint.
     */
    USTRUCT(BlueprintType)
    struct FEndpointInfo
    {
        GENERATED_BODY()

        /** HTTP method (GET, POST, etc.) */
        UPROPERTY(BlueprintReadOnly, Category = "Bannou")
        FString Method;

        /** Endpoint path */
        UPROPERTY(BlueprintReadOnly, Category = "Bannou")
        FString Path;

        /** Service name */
        UPROPERTY(BlueprintReadOnly, Category = "Bannou")
        FString Service;

        /** Request type name (empty if no request body) */
        UPROPERTY(BlueprintReadOnly, Category = "Bannou")
        FString RequestType;

        /** Response type name (empty if no response body) */
        UPROPERTY(BlueprintReadOnly, Category = "Bannou")
        FString ResponseType;

        /** Human-readable summary */
        UPROPERTY(BlueprintReadOnly, Category = "Bannou")
        FString Summary;
    };

    /**
     * Get all registered endpoints.
     * Call this to build a local endpoint registry.
     */
    inline const TMap<FString, FEndpointInfo>& GetAllEndpoints()
    {
        static TMap<FString, FEndpointInfo> Registry;

        if (Registry.Num() == 0)
        {
'''

    # Generate registry initialization
    for service, endpoints in sorted(endpoints_by_service.items()):
        for ep in endpoints:
            header += f'''            Registry.Add(TEXT("{ep["const_name"]}"), FEndpointInfo{{
                TEXT("{ep["method"]}"),
                TEXT("{ep["path"]}"),
                TEXT("{service}"),
                TEXT("{ep["request_type"] or ""}"),
                TEXT("{ep["response_type"] or ""}"),
                TEXT("{ep["summary"]}")
            }});
'''

    header += '''        }

        return Registry;
    }

} // namespace Bannou
'''

    return header


def generate_events_header(events_dir: Path) -> str:
    """Generate BannouEvents.h with client event types."""
    timestamp = datetime.now(timezone.utc).strftime('%Y-%m-%d %H:%M:%S UTC')

    header = f'''// <auto-generated>
// BannouEvents.h - Client event types for Bannou WebSocket push events
// Generated by generate-unreal-types.py on {timestamp}
// Do not edit this file manually.
// </auto-generated>

#pragma once

#include "CoreMinimal.h"

/**
 * Bannou Client Events
 *
 * This header contains event name constants for server-to-client push events.
 * These events are received via WebSocket when subscribed.
 */

namespace Bannou
{{
    namespace Events
    {{
'''

    # Parse client event schemas to extract event names
    event_names = []

    for events_path in sorted(events_dir.glob('*-client-events.yaml')):
        try:
            with open(events_path) as f:
                schema = yaml.load(f)

            if not schema:
                continue

            schemas = schema.get('components', {}).get('schemas', {})

            for schema_name, schema_def in schemas.items():
                if not isinstance(schema_def, dict):
                    continue

                # Look for x-client-event marker or eventName property
                if schema_def.get('x-client-event'):
                    # Try to get the default event name
                    props = schema_def.get('properties', {})
                    if 'eventName' in props:
                        event_name_prop = props['eventName']
                        default_name = event_name_prop.get('default', '')
                        if default_name:
                            const_name = to_pascal_case(default_name.replace('.', '_'))
                            description = schema_def.get('description', '').split('\n')[0]
                            event_names.append({
                                'const_name': const_name,
                                'event_name': default_name,
                                'description': description
                            })

        except Exception as e:
            print(f"  Warning: Failed to parse {events_path.name}: {e}")

    # Add common events (also from common-client-events.yaml)
    common_events_path = events_dir / 'common-client-events.yaml'
    if common_events_path.exists():
        try:
            with open(common_events_path) as f:
                schema = yaml.load(f)

            if schema:
                schemas = schema.get('components', {}).get('schemas', {})

                for schema_name, schema_def in schemas.items():
                    if not isinstance(schema_def, dict):
                        continue

                    if schema_def.get('x-client-event') and not schema_def.get('x-internal'):
                        props = schema_def.get('properties', {})
                        if 'eventName' in props:
                            event_name_prop = props['eventName']
                            default_name = event_name_prop.get('default', '')
                            if default_name:
                                const_name = to_pascal_case(default_name.replace('.', '_'))
                                description = schema_def.get('description', '').split('\n')[0]
                                event_names.append({
                                    'const_name': const_name,
                                    'event_name': default_name,
                                    'description': description
                                })

        except Exception as e:
            print(f"  Warning: Failed to parse common-client-events.yaml: {e}")

    # Deduplicate and sort
    seen = set()
    unique_events = []
    for event in event_names:
        if event['event_name'] not in seen:
            seen.add(event['event_name'])
            unique_events.append(event)

    unique_events.sort(key=lambda e: e['event_name'])

    # Generate event constants
    for event in unique_events:
        header += f'        /** {event["description"]} */\n'
        header += f'        constexpr const TCHAR* {event["const_name"]} = TEXT("{event["event_name"]}");\n\n'

    header += '''    } // namespace Events
} // namespace Bannou
'''

    return header


def main():
    """Generate Unreal Engine type headers."""
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'
    generated_schema_dir = schema_dir / 'Generated'
    output_dir = repo_root / 'sdks' / 'unreal' / 'Generated'

    # Check for consolidated schema
    client_schema_path = generated_schema_dir / 'bannou-client-api.yaml'
    if not client_schema_path.exists():
        print(f"ERROR: Consolidated client schema not found: {client_schema_path}")
        print("       Run generate-client-schema.py first.")
        sys.exit(1)

    output_dir.mkdir(parents=True, exist_ok=True)

    print("Generating Unreal Engine type headers...")

    # Load consolidated schema
    with open(client_schema_path) as f:
        schema = yaml.load(f)

    schemas = schema.get('components', {}).get('schemas', {})
    paths = schema.get('paths', {})

    print(f"  Processing {len(schemas)} types and {len(paths)} endpoints...")

    # Generate enums header (must be first, types depend on it)
    enums_header = generate_enums_header(schemas)
    enums_path = output_dir / 'BannouEnums.h'
    with open(enums_path, 'w', newline='\n') as f:
        f.write(enums_header)
    print(f"  Generated {enums_path.name}")

    # Generate types header
    types_header = generate_types_header(schemas)
    types_path = output_dir / 'BannouTypes.h'
    with open(types_path, 'w', newline='\n') as f:
        f.write(types_header)
    print(f"  Generated {types_path.name}")

    # Generate endpoints header
    endpoints_header = generate_endpoints_header(paths, schemas)
    endpoints_path = output_dir / 'BannouEndpoints.h'
    with open(endpoints_path, 'w', newline='\n') as f:
        f.write(endpoints_header)
    print(f"  Generated {endpoints_path.name}")

    # Generate events header
    events_header = generate_events_header(schema_dir)
    events_path = output_dir / 'BannouEvents.h'
    with open(events_path, 'w', newline='\n') as f:
        f.write(events_header)
    print(f"  Generated {events_path.name}")

    print("\nType header generation complete.")


if __name__ == '__main__':
    main()
