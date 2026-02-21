#!/usr/bin/env python3
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

"""
Generate consolidated client-facing OpenAPI schema from individual service schemas.

This script reads all *-api.yaml schemas and merges them into a single schema
suitable for client code generation (Unreal Engine, Unity, etc.).

Features:
- Filters endpoints by x-permissions (includes: anonymous, user, authenticated, developer)
- Excludes admin-only endpoints
- Deduplicates components/schemas across services
- Adds x-bannou-protocol extension with header sizes

Output:
- schemas/Generated/bannou-client-api.yaml
- schemas/Generated/bannou-client-api.json

Usage:
    python3 scripts/generate-client-schema.py
"""

import json
import re
import sys
from pathlib import Path
from typing import Any, Optional, Union

try:
    from ruamel.yaml import YAML
    yaml = YAML()
    yaml.preserve_quotes = True
    yaml.default_flow_style = False
except ImportError:
    print("ERROR: ruamel.yaml is required. Install with: pip install ruamel.yaml")
    sys.exit(1)


# Client-accessible permission roles (excludes admin-only)
CLIENT_ROLES = {'anonymous', 'user', 'authenticated', 'developer'}

# Services to exclude entirely from client schema (internal/infrastructure)
EXCLUDED_SERVICES = {
    'mesh',       # Internal service mesh
    'messaging',  # Internal pub/sub
    'state',      # Internal state management
    'orchestrator',  # Admin-only deployment management
}


def is_client_accessible(endpoint_details: dict) -> bool:
    """Check if an endpoint is accessible to clients (non-admin)."""
    permissions = endpoint_details.get('x-permissions', [])

    if not permissions:
        # No permissions specified - assume not accessible
        return False

    for perm in permissions:
        role = perm.get('role', '')
        if role in CLIENT_ROLES:
            return True

    return False


def get_refs_from_schema(schema: dict, refs: set) -> None:
    """Recursively extract all $ref references from a schema."""
    if not isinstance(schema, dict):
        return

    if '$ref' in schema:
        ref = schema['$ref']
        if ref.startswith('#/components/schemas/'):
            refs.add(ref.split('/')[-1])
        elif '-api.yaml#/components/schemas/' in ref:
            # Handle refs to any *-api.yaml (common-api.yaml, escrow-api.yaml, etc.)
            refs.add(ref.split('/')[-1])

    for key, value in schema.items():
        if isinstance(value, dict):
            get_refs_from_schema(value, refs)
        elif isinstance(value, list):
            for item in value:
                if isinstance(item, dict):
                    get_refs_from_schema(item, refs)


# Regex to match external API schema refs like './escrow-api.yaml#' or 'common-api.yaml#'
EXTERNAL_API_REF_PATTERN = re.compile(r'\.?/?[a-z-]+-api\.yaml#')


def rewrite_external_refs(schema: Any) -> Any:
    """Recursively rewrite all *-api.yaml refs to local refs."""
    if isinstance(schema, dict):
        result = {}
        for key, value in schema.items():
            if key == '$ref' and isinstance(value, str) and EXTERNAL_API_REF_PATTERN.search(value):
                # Rewrite any external API ref to local ref
                # e.g., './escrow-api.yaml#/components/schemas/X' -> '#/components/schemas/X'
                # e.g., 'common-api.yaml#/components/schemas/X' -> '#/components/schemas/X'
                result[key] = EXTERNAL_API_REF_PATTERN.sub('#', value)
            else:
                result[key] = rewrite_external_refs(value)
        return result
    elif isinstance(schema, list):
        return [rewrite_external_refs(item) for item in schema]
    else:
        return schema


def collect_required_schemas(
    endpoint_details: dict,
    components_schemas: dict,
    required: set
) -> None:
    """Collect all schemas required by an endpoint (recursively)."""
    refs_to_process: set = set()

    # Get request body schema refs
    request_body = endpoint_details.get('requestBody', {})
    if request_body:
        content = request_body.get('content', {})
        for media_type, media_content in content.items():
            schema = media_content.get('schema', {})
            get_refs_from_schema(schema, refs_to_process)

    # Get response schema refs
    responses = endpoint_details.get('responses', {})
    for code, response in responses.items():
        content = response.get('content', {})
        for media_type, media_content in content.items():
            schema = media_content.get('schema', {})
            get_refs_from_schema(schema, refs_to_process)

    # Get parameter schema refs
    parameters = endpoint_details.get('parameters', [])
    for param in parameters:
        schema = param.get('schema', {})
        get_refs_from_schema(schema, refs_to_process)

    # Process refs recursively
    while refs_to_process:
        ref_name = refs_to_process.pop()
        if ref_name in required:
            continue

        required.add(ref_name)

        # Get the schema definition and find its refs
        if ref_name in components_schemas:
            schema_def = components_schemas[ref_name]
            get_refs_from_schema(schema_def, refs_to_process)


def parse_service_schema(schema_path: Path) -> Optional[dict]:
    """Parse a service schema file."""
    try:
        with open(schema_path) as f:
            return yaml.load(f)
    except Exception as e:
        print(f"  Warning: Failed to parse {schema_path.name}: {e}")
        return None


def merge_schemas(schema_dir: Path) -> dict:
    """Merge all service schemas into a consolidated client schema."""
    # Initialize consolidated schema
    consolidated = {
        'openapi': '3.0.3',
        'info': {
            'title': 'Bannou Client API',
            'description': '''
Consolidated client-facing API schema for Bannou services.

This schema contains all endpoints accessible to game clients (anonymous, user,
authenticated, developer roles). Admin-only endpoints are excluded.

Generated by generate-client-schema.py from individual service schemas.

## WebSocket Binary Protocol

All endpoints can be accessed via the WebSocket binary protocol:
- Request header: 31 bytes (flags, channel, sequence, serviceGuid, messageId)
- Response header: 16 bytes (flags, channel, sequence, messageId, responseCode)
- Payload: JSON (UTF-8 encoded)

See the x-bannou-protocol extension for protocol details.
'''.strip(),
            'version': '1.0.0',
            'contact': {
                'name': 'Bannou Development Team'
            }
        },
        'servers': [
            {
                'url': 'http://localhost:5012',
                'description': 'Local development server'
            }
        ],
        'x-bannou-protocol': {
            'requestHeaderSize': 31,
            'responseHeaderSize': 16,
            'headerLayout': {
                'request': [
                    {'offset': 0, 'size': 1, 'field': 'flags', 'type': 'uint8'},
                    {'offset': 1, 'size': 2, 'field': 'channel', 'type': 'uint16_be'},
                    {'offset': 3, 'size': 4, 'field': 'sequence', 'type': 'uint32_be'},
                    {'offset': 7, 'size': 16, 'field': 'serviceGuid', 'type': 'guid_rfc4122'},
                    {'offset': 23, 'size': 8, 'field': 'messageId', 'type': 'uint64_be'},
                ],
                'response': [
                    {'offset': 0, 'size': 1, 'field': 'flags', 'type': 'uint8'},
                    {'offset': 1, 'size': 2, 'field': 'channel', 'type': 'uint16_be'},
                    {'offset': 3, 'size': 4, 'field': 'sequence', 'type': 'uint32_be'},
                    {'offset': 7, 'size': 8, 'field': 'messageId', 'type': 'uint64_be'},
                    {'offset': 15, 'size': 1, 'field': 'responseCode', 'type': 'uint8'},
                ]
            },
            'messageFlags': {
                'None': 0x00,
                'Binary': 0x01,
                'Encrypted': 0x02,
                'Compressed': 0x04,
                'HighPriority': 0x08,
                'Event': 0x10,
                'Client': 0x20,
                'Response': 0x40,
                'Meta': 0x80,
            },
            'responseCodes': {
                'OK': 0,
                'RequestError': 10,
                'RequestTooLarge': 11,
                'TooManyRequests': 12,
                'InvalidRequestChannel': 13,
                'Unauthorized': 20,
                'ServiceNotFound': 30,
                'ClientNotFound': 31,
                'MessageNotFound': 32,
                'BroadcastNotAllowed': 40,
                'Service_BadRequest': 50,
                'Service_NotFound': 51,
                'Service_Unauthorized': 52,
                'Service_Conflict': 53,
                'Service_InternalServerError': 60,
                'ShortcutExpired': 70,
                'ShortcutTargetNotFound': 71,
                'ShortcutRevoked': 72,
            }
        },
        'paths': {},
        'components': {
            'schemas': {},
            'securitySchemes': {
                'BearerAuth': {
                    'type': 'http',
                    'scheme': 'bearer',
                    'bearerFormat': 'JWT',
                    'description': 'JWT access token obtained from /auth/login or OAuth flow'
                }
            }
        },
        'tags': []
    }

    all_schemas: dict[str, dict] = {}
    required_schemas: set = set()
    service_tags: list = []
    endpoint_count = 0

    # Load common-api.yaml first to get shared types like EntityType
    common_api_path = schema_dir / 'common-api.yaml'
    if common_api_path.exists():
        common_schema = parse_service_schema(common_api_path)
        if common_schema:
            common_components = common_schema.get('components', {})
            common_schemas = common_components.get('schemas', {})
            for schema_name, schema_def in common_schemas.items():
                all_schemas[schema_name] = schema_def
            print(f"  Loaded {len(common_schemas)} common types from common-api.yaml")

    # Process each service schema
    for schema_path in sorted(schema_dir.glob('*-api.yaml')):
        service_name = schema_path.stem.replace('-api', '')

        # Skip excluded services
        if service_name in EXCLUDED_SERVICES:
            print(f"  Skipping {service_name} (excluded)")
            continue

        schema = parse_service_schema(schema_path)
        if not schema:
            continue

        info = schema.get('info', {})
        paths = schema.get('paths', {})
        components = schema.get('components', {})
        schemas = components.get('schemas', {})

        # Collect all schemas from this service
        for schema_name, schema_def in schemas.items():
            if schema_name not in all_schemas:
                all_schemas[schema_name] = schema_def
            # If duplicate, prefer the first one encountered

        # Process paths
        service_endpoint_count = 0
        for path, methods in paths.items():
            if not isinstance(methods, dict):
                continue

            for method, details in methods.items():
                if method.lower() not in ('get', 'post', 'put', 'delete', 'patch'):
                    continue

                if not isinstance(details, dict):
                    continue

                # Check if client-accessible
                if not is_client_accessible(details):
                    continue

                # Add path to consolidated schema
                if path not in consolidated['paths']:
                    consolidated['paths'][path] = {}

                # Copy endpoint details
                consolidated['paths'][path][method] = details

                # Collect required schemas
                collect_required_schemas(details, all_schemas, required_schemas)

                service_endpoint_count += 1
                endpoint_count += 1

        if service_endpoint_count > 0:
            # Add service tag
            tag_name = info.get('title', service_name).replace(' Service API', '').replace(' API', '')
            description = info.get('description', '').split('\n')[0]  # First line only
            service_tags.append({
                'name': tag_name,
                'description': description
            })
            print(f"  {service_name}: {service_endpoint_count} endpoints")

    # Add only required schemas
    for schema_name in sorted(required_schemas):
        if schema_name in all_schemas:
            consolidated['components']['schemas'][schema_name] = all_schemas[schema_name]

    # Add tags
    consolidated['tags'] = sorted(service_tags, key=lambda t: t['name'])

    print(f"\n  Total: {endpoint_count} endpoints, {len(required_schemas)} schemas")

    return consolidated


def main():
    """Generate consolidated client schema."""
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'
    output_dir = schema_dir / 'Generated'

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    output_dir.mkdir(parents=True, exist_ok=True)

    print("Generating consolidated client-facing OpenAPI schema...")
    print(f"  Reading from: schemas/*-api.yaml")
    print(f"  Writing to: schemas/Generated/")
    print()

    # Merge schemas
    consolidated = merge_schemas(schema_dir)

    # Rewrite external *-api.yaml refs to local refs (since types are now inlined)
    consolidated = rewrite_external_refs(consolidated)

    # Write YAML output
    yaml_path = output_dir / 'bannou-client-api.yaml'
    with open(yaml_path, 'w', newline='\n') as f:
        yaml.dump(consolidated, f)
    print(f"\n  Generated {yaml_path.name}")

    # Write JSON output
    json_path = output_dir / 'bannou-client-api.json'
    with open(json_path, 'w', newline='\n') as f:
        json.dump(consolidated, f, indent=2)
    print(f"  Generated {json_path.name}")

    print("\nClient schema generation complete.")


if __name__ == '__main__':
    main()
