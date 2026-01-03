#!/usr/bin/env python3
"""
Embed JSON Schema strings into OpenAPI schemas for meta endpoint generation.

This script reads API schemas and adds x-*-schema-json extension fields
that are used by Controller.liquid template to generate companion endpoints.

Architecture:
- Input: schemas/{service}-api.yaml
- Output: schemas/{service}-api.yaml (in-place modification)
- Extension fields added to each operation:
  - x-request-schema-json: Bundled JSON Schema for request body
  - x-response-schema-json: Bundled JSON Schema for 200/201 response
  - x-endpoint-info-json: Endpoint metadata (summary, description, tags, deprecated)

IDEMPOTENT: This script can be run multiple times safely. Each run regenerates
and overwrites the extension fields to reflect the current schema state.

Usage:
    python3 scripts/embed-meta-schemas.py [--service SERVICE_NAME]

    Without --service: processes all *-api.yaml files
    With --service: processes only {service}-api.yaml
"""

import sys
import json
import argparse
from pathlib import Path
from typing import Any, Dict, Optional, Set

try:
    from ruamel.yaml import YAML
    yaml = YAML()
    yaml.preserve_quotes = True
    yaml.width = 120
    yaml.default_flow_style = False
except ImportError:
    print("ERROR: ruamel.yaml is required. Install with: pip install ruamel.yaml")
    sys.exit(1)


def resolve_ref(ref: str, schema: Dict[str, Any]) -> Dict[str, Any]:
    """Resolve a $ref to its definition in the schema."""
    if not ref.startswith('#/'):
        # External refs not supported - return empty object
        return {'type': 'object'}

    parts = ref[2:].split('/')
    current = schema
    for part in parts:
        if isinstance(current, dict) and part in current:
            current = current[part]
        else:
            return {'type': 'object'}
    return current


def bundle_schema(
    schema_def: Any,
    root_schema: Dict[str, Any],
    defs: Dict[str, Any],
    visited: Set[str]
) -> Any:
    """
    Recursively bundle a schema, resolving $refs and collecting definitions.
    Converts #/components/schemas/* refs to #/$defs/* format for JSON Schema.
    """
    if not isinstance(schema_def, dict):
        return schema_def

    result = {}

    for key, value in schema_def.items():
        if key == '$ref':
            ref_path = str(value)
            if ref_path in visited:
                # Circular reference - keep as $ref but convert path
                result['$ref'] = ref_path.replace('#/components/schemas/', '#/$defs/')
                continue

            visited.add(ref_path)

            if ref_path.startswith('#/components/schemas/'):
                type_name = ref_path.split('/')[-1]
                resolved = resolve_ref(ref_path, root_schema)

                # Add to $defs if not already there
                if type_name not in defs and isinstance(resolved, dict):
                    # Mark as being processed to handle circular refs
                    defs[type_name] = {}  # Placeholder
                    defs[type_name] = bundle_schema(dict(resolved), root_schema, defs, visited.copy())

                # Replace with $defs reference
                result['$ref'] = f'#/$defs/{type_name}'
            else:
                # Other $ref - try to resolve inline
                resolved = resolve_ref(ref_path, root_schema)
                if isinstance(resolved, dict):
                    result.update(bundle_schema(dict(resolved), root_schema, defs, visited.copy()))
        elif isinstance(value, dict):
            result[key] = bundle_schema(dict(value), root_schema, defs, visited.copy())
        elif isinstance(value, list):
            result[key] = [
                bundle_schema(dict(item) if isinstance(item, dict) else item, root_schema, defs, visited.copy())
                for item in value
            ]
        else:
            result[key] = value

    return result


def create_json_schema(schema_def: Optional[Dict[str, Any]], root_schema: Dict[str, Any]) -> str:
    """Create a self-contained JSON Schema string from an OpenAPI schema definition."""
    if schema_def is None:
        return json.dumps({}, indent=2)

    defs: Dict[str, Any] = {}
    bundled = bundle_schema(dict(schema_def), root_schema, defs, set())

    result = {
        '$schema': 'http://json-schema.org/draft-07/schema#',
        **bundled
    }

    if defs:
        result['$defs'] = defs

    return json.dumps(result, indent=4)


def create_info_json(operation: Dict[str, Any]) -> str:
    """Create endpoint info JSON from operation metadata."""
    info = {
        'summary': str(operation.get('summary', '')),
        'description': str(operation.get('description', '')),
        'tags': list(operation.get('tags', [])),
        'deprecated': bool(operation.get('deprecated', False)),
        'operationId': str(operation.get('operationId', ''))
    }
    return json.dumps(info, indent=4)


def get_request_schema(operation: Dict[str, Any]) -> Optional[Dict[str, Any]]:
    """Extract request body schema from operation."""
    request_body = operation.get('requestBody', {})
    if not isinstance(request_body, dict):
        return None
    content = request_body.get('content', {})
    if not isinstance(content, dict):
        return None
    json_content = content.get('application/json', {})
    if not isinstance(json_content, dict):
        return None
    schema = json_content.get('schema')
    return dict(schema) if isinstance(schema, dict) else None


def get_response_schema(operation: Dict[str, Any]) -> Optional[Dict[str, Any]]:
    """Extract 200/201 response schema from operation."""
    responses = operation.get('responses', {})
    if not isinstance(responses, dict):
        return None

    # Try 200 first, then 201
    success_response = responses.get('200') or responses.get('201') or responses.get(200) or responses.get(201)
    if not isinstance(success_response, dict):
        return None

    content = success_response.get('content', {})
    if not isinstance(content, dict):
        return None

    json_content = content.get('application/json', {})
    if not isinstance(json_content, dict):
        return None

    schema = json_content.get('schema')
    return dict(schema) if isinstance(schema, dict) else None


def process_schema(schema_path: Path) -> bool:
    """
    Process a single API schema file, adding x-*-json extension fields.

    IDEMPOTENT: Always overwrites extension fields to ensure they reflect
    the current schema state. Safe to run multiple times.

    Returns True if any changes were made.
    """
    with open(schema_path) as f:
        schema = yaml.load(f)

    if schema is None or 'paths' not in schema:
        return False

    root_schema = dict(schema)
    modified = False

    for path, methods in schema['paths'].items():
        if not isinstance(methods, dict):
            continue

        for method, operation in methods.items():
            # Skip non-method keys (like 'parameters', 'servers', etc.)
            if method in ('parameters', 'servers', 'summary', 'description', '$ref'):
                continue

            if not isinstance(operation, dict):
                continue

            # Create request schema JSON
            request_schema = get_request_schema(operation)
            request_json = create_json_schema(request_schema, root_schema)

            # Create response schema JSON
            response_schema = get_response_schema(operation)
            response_json = create_json_schema(response_schema, root_schema)

            # Create info JSON
            info_json = create_info_json(operation)

            # IDEMPOTENT: Always overwrite extension fields to ensure they reflect
            # the current schema state. Safe to run multiple times.
            operation['x-request-schema-json'] = request_json
            operation['x-response-schema-json'] = response_json
            operation['x-endpoint-info-json'] = info_json

            modified = True

    if modified:
        with open(schema_path, 'w') as f:
            yaml.dump(schema, f)

    return modified


def main():
    parser = argparse.ArgumentParser(
        description='Embed meta schemas into API schemas for companion endpoint generation'
    )
    parser.add_argument(
        '--service',
        help='Process only this service (e.g., "accounts")'
    )
    args = parser.parse_args()

    # Find schemas directory
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    print("Embedding meta schemas into API schemas...")

    if args.service:
        schema_files = [schema_dir / f'{args.service}-api.yaml']
    else:
        schema_files = sorted(schema_dir.glob('*-api.yaml'))

    processed = 0
    for schema_path in schema_files:
        if not schema_path.exists():
            print(f"  WARNING: {schema_path.name} not found")
            continue

        if process_schema(schema_path):
            print(f"  Embedded: {schema_path.name}")
            processed += 1

    print(f"\nProcessed {processed} schema file(s)")


if __name__ == '__main__':
    main()
