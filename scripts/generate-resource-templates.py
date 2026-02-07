#!/usr/bin/env python3
"""
Generate IResourceTemplate implementations from x-archive-type schema extensions.

This script scans all API schemas for:
1. x-compression-callback declarations (to get sourceType and templateNamespace)
2. Archive response schemas with x-archive-type: true

It generates ResourceTemplateBase implementations with ValidPaths dictionaries
built from traversing the archive schema structure.

Architecture:
- Scans: {service}-api.yaml files
- Identifies: x-compression-callback at root + x-archive-type on response schemas
- Output: bannou-service/Generated/ResourceTemplates/{Name}Template.cs

Usage:
    python3 scripts/generate-resource-templates.py

The script must run AFTER generate-models (so model types exist for using directives).
"""

import sys
import re
from pathlib import Path
from typing import Any, Dict, List, Optional, Set, Tuple
from dataclasses import dataclass, field

try:
    from ruamel.yaml import YAML
    yaml = YAML()
    yaml.preserve_quotes = True
except ImportError:
    print("ERROR: ruamel.yaml is required. Install with: pip install ruamel.yaml")
    sys.exit(1)


@dataclass
class TemplateInfo:
    """Information needed to generate a resource template."""
    source_type: str
    namespace: str
    archive_type_name: str
    service_name: str  # For namespace imports
    valid_paths: Dict[str, str] = field(default_factory=dict)  # path -> C# type


def to_pascal_case(name: str) -> str:
    """Convert kebab-case to PascalCase."""
    return ''.join(word.capitalize() for word in name.split('-'))


def openapi_type_to_csharp(
    prop_schema: Dict[str, Any],
    type_name: Optional[str] = None,
    all_schemas: Optional[Dict[str, Any]] = None
) -> str:
    """
    Convert OpenAPI type definition to C# type string.

    Args:
        prop_schema: The property schema definition
        type_name: Optional explicit type name (from $ref)
        all_schemas: All schemas in the file (for $ref resolution)

    Returns:
        C# type string
    """
    if type_name:
        return type_name

    # Handle $ref
    if '$ref' in prop_schema:
        ref = prop_schema['$ref']
        # Extract type name from #/components/schemas/TypeName or ./file.yaml#/...
        if '#/components/schemas/' in ref:
            return ref.split('/')[-1]
        # Cross-file ref - just use the type name
        return ref.split('/')[-1]

    prop_type = prop_schema.get('type', 'object')
    prop_format = prop_schema.get('format')

    # Handle nullable wrapper
    if prop_schema.get('nullable'):
        base_type = openapi_type_to_csharp({k: v for k, v in prop_schema.items() if k != 'nullable'}, None, all_schemas)
        # Value types need ? suffix, reference types don't
        if base_type in ('int', 'long', 'float', 'double', 'bool', 'Guid', 'DateTimeOffset'):
            return f'{base_type}?'
        return base_type

    if prop_type == 'string':
        if prop_format == 'uuid':
            return 'Guid'
        elif prop_format == 'date-time':
            return 'DateTimeOffset'
        elif prop_format == 'date':
            return 'DateOnly'
        elif prop_format == 'time':
            return 'TimeOnly'
        elif prop_format == 'uri':
            return 'Uri'
        elif prop_format == 'byte':
            return 'byte[]'
        # Check for enum
        if 'enum' in prop_schema:
            return 'string'  # Will be actual enum name from $ref
        return 'string'

    elif prop_type == 'integer':
        if prop_format == 'int64':
            return 'long'
        return 'int'

    elif prop_type == 'number':
        if prop_format == 'double':
            return 'double'
        return 'float'

    elif prop_type == 'boolean':
        return 'bool'

    elif prop_type == 'array':
        items = prop_schema.get('items', {})
        item_type = openapi_type_to_csharp(items, None, all_schemas)
        return f'ICollection<{item_type}>'

    elif prop_type == 'object':
        # Check for additionalProperties (dictionary)
        if 'additionalProperties' in prop_schema:
            value_schema = prop_schema['additionalProperties']
            if isinstance(value_schema, bool):
                return 'IDictionary<string, object>'
            value_type = openapi_type_to_csharp(value_schema, None, all_schemas)
            return f'IDictionary<string, {value_type}>'
        return 'object'

    return 'object'


def resolve_ref(ref: str, current_schema: Dict[str, Any], all_schemas: Dict[str, Dict[str, Any]]) -> Optional[Dict[str, Any]]:
    """
    Resolve a $ref to its schema definition.

    Args:
        ref: The $ref string
        current_schema: The current file's full schema
        all_schemas: All loaded schemas by filename

    Returns:
        The resolved schema, or None if not found
    """
    if ref.startswith('#/'):
        # Local ref within same file
        path_parts = ref[2:].split('/')
        result = current_schema
        for part in path_parts:
            if isinstance(result, dict):
                result = result.get(part)
            else:
                return None
        return result

    elif ref.startswith('./'):
        # Cross-file ref
        parts = ref.split('#/')
        if len(parts) != 2:
            return None

        file_path = parts[0][2:]  # Remove ./
        json_path = parts[1]

        if file_path not in all_schemas:
            return None

        path_parts = json_path.split('/')
        result = all_schemas[file_path]
        for part in path_parts:
            if isinstance(result, dict):
                result = result.get(part)
            else:
                return None
        return result

    return None


def traverse_schema(
    schema: Dict[str, Any],
    current_path: str,
    paths: Dict[str, str],
    current_schema: Dict[str, Any],
    all_schemas: Dict[str, Dict[str, Any]],
    visited: Optional[Set[str]] = None,
    depth: int = 0
) -> None:
    """
    Recursively traverse a schema to build ValidPaths dictionary.

    Args:
        schema: Current schema node to traverse
        current_path: Current dot-separated path
        paths: Output dictionary of path -> C# type
        current_schema: Full schema of current file
        all_schemas: All loaded schemas
        visited: Set of visited $refs to prevent infinite recursion
        depth: Current recursion depth
    """
    if visited is None:
        visited = set()

    # Limit recursion depth to prevent infinite loops
    if depth > 10:
        return

    # Handle $ref
    if '$ref' in schema:
        ref = schema['$ref']
        if ref in visited:
            return
        visited = visited | {ref}

        resolved = resolve_ref(ref, current_schema, all_schemas)
        if resolved:
            traverse_schema(resolved, current_path, paths, current_schema, all_schemas, visited, depth)
        return

    # Handle allOf - merge all schemas
    if 'allOf' in schema:
        for sub_schema in schema['allOf']:
            traverse_schema(sub_schema, current_path, paths, current_schema, all_schemas, visited, depth)
        return

    # Handle properties
    properties = schema.get('properties', {})
    for prop_name, prop_schema in properties.items():
        prop_path = f"{current_path}.{prop_name}" if current_path else prop_name

        # Get the C# type for this property
        csharp_type = openapi_type_to_csharp(prop_schema, None, all_schemas)
        paths[prop_path] = csharp_type

        # Recursively traverse nested objects
        if '$ref' in prop_schema:
            ref = prop_schema['$ref']
            if ref not in visited:
                resolved = resolve_ref(ref, current_schema, all_schemas)
                if resolved:
                    # Only traverse if it's an object with properties
                    if resolved.get('type') == 'object' or 'properties' in resolved or 'allOf' in resolved:
                        traverse_schema(resolved, prop_path, paths, current_schema, all_schemas, visited | {ref}, depth + 1)

        elif prop_schema.get('type') == 'object' and 'properties' in prop_schema:
            traverse_schema(prop_schema, prop_path, paths, current_schema, all_schemas, visited, depth + 1)


def find_archive_schema(
    compress_endpoint: str,
    paths: Dict[str, Any],
    schemas: Dict[str, Any]
) -> Optional[Tuple[str, Dict[str, Any]]]:
    """
    Find the archive schema from a compression endpoint's 200 response.

    Args:
        compress_endpoint: The endpoint path (e.g., /character-personality/get-compress-data)
        paths: The paths section of the API schema
        schemas: The components/schemas section

    Returns:
        Tuple of (archive_type_name, archive_schema) or None
    """
    endpoint_def = paths.get(compress_endpoint)
    if not endpoint_def:
        return None

    post_def = endpoint_def.get('post')
    if not post_def:
        return None

    responses = post_def.get('responses', {})
    success_response = responses.get('200') or responses.get('201')
    if not success_response:
        return None

    content = success_response.get('content', {})
    json_content = content.get('application/json', {})
    response_schema = json_content.get('schema', {})

    # Get the $ref to the response type
    ref = response_schema.get('$ref')
    if not ref or '#/components/schemas/' not in ref:
        return None

    type_name = ref.split('/')[-1]
    schema = schemas.get(type_name)

    if not schema:
        return None

    # Check if it has x-archive-type: true
    if not schema.get('x-archive-type'):
        return None

    return (type_name, schema)


def extract_template_info(
    api_schema: Dict[str, Any],
    all_schemas: Dict[str, Dict[str, Any]],
    service_name: str
) -> Optional[TemplateInfo]:
    """
    Extract template information from an API schema with x-compression-callback.

    Args:
        api_schema: The full API schema
        all_schemas: All loaded schemas
        service_name: The service name (e.g., "character-personality")

    Returns:
        TemplateInfo or None if not a compression callback schema
    """
    info = api_schema.get('info', {})
    callback = info.get('x-compression-callback')

    if not callback:
        return None

    source_type = callback.get('sourceType')
    compress_endpoint = callback.get('compressEndpoint')
    namespace = callback.get('templateNamespace', source_type)

    if not source_type or not compress_endpoint:
        return None

    paths = api_schema.get('paths', {})
    schemas = api_schema.get('components', {}).get('schemas', {})

    # Find the archive schema
    result = find_archive_schema(compress_endpoint, paths, schemas)
    if not result:
        return None

    archive_type_name, archive_schema = result

    # Build ValidPaths by traversing the schema
    valid_paths: Dict[str, str] = {}

    # Add root type
    valid_paths[""] = archive_type_name

    # Traverse the schema
    traverse_schema(archive_schema, "", valid_paths, api_schema, all_schemas)

    return TemplateInfo(
        source_type=source_type,
        namespace=namespace,
        archive_type_name=archive_type_name,
        service_name=service_name,
        valid_paths=valid_paths
    )


def generate_template_class(info: TemplateInfo) -> str:
    """Generate C# source code for a resource template class."""

    class_name = f"{to_pascal_case(info.source_type)}Template"

    # Sort paths for deterministic output
    sorted_paths = sorted(info.valid_paths.items(), key=lambda x: (x[0].count('.'), x[0]))

    # Build path entries
    path_entries = []
    for path, csharp_type in sorted_paths:
        escaped_path = path.replace('"', '\\"')
        path_entries.append(f'        ["{escaped_path}"] = typeof({csharp_type}),')

    paths_str = '\n'.join(path_entries)

    # Determine required using statements based on types used
    usings = set([
        'System',
        'System.Collections.Generic',
        'BeyondImmersion.Bannou.BehaviorCompiler.Templates',
        'BeyondImmersion.BannouService.ResourceTemplates',
    ])

    # Add service-specific namespace for the archive type and related models
    service_pascal = to_pascal_case(info.service_name)
    usings.add(f'BeyondImmersion.BannouService.{service_pascal}')

    usings_str = '\n'.join(f'using {u};' for u in sorted(usings))

    return f'''// <auto-generated />
// Generated by generate-resource-templates.py from x-archive-type schema extensions.
// DO NOT EDIT - This file is completely overwritten on regeneration.

{usings_str}

namespace BeyondImmersion.BannouService.Generated.ResourceTemplates;

/// <summary>
/// Auto-generated resource template for {info.source_type} archive data.
/// Provides compile-time path validation for ABML expressions.
/// </summary>
public sealed class {class_name} : ResourceTemplateBase
{{
    /// <inheritdoc />
    public override string SourceType => "{info.source_type}";

    /// <inheritdoc />
    public override string Namespace => "{info.namespace}";

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, Type> ValidPaths {{ get; }} = new Dictionary<string, Type>
    {{
{paths_str}
    }};
}}
'''


def main():
    """Scan all API schemas and generate resource templates."""
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'
    output_dir = repo_root / 'bannou-service' / 'Generated' / 'ResourceTemplates'

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    # Ensure output directory exists
    output_dir.mkdir(parents=True, exist_ok=True)

    print("Generating resource templates from x-archive-type extensions...")
    print()

    # First pass: load all schemas for cross-file $ref resolution
    all_schemas: Dict[str, Dict[str, Any]] = {}
    for schema_file in schema_dir.glob('*.yaml'):
        try:
            with open(schema_file) as f:
                schema = yaml.load(f)
                if schema:
                    all_schemas[schema_file.name] = schema
        except Exception as e:
            print(f"  WARNING: Failed to load {schema_file.name}: {e}")

    # Second pass: find compression callbacks and generate templates
    templates: List[TemplateInfo] = []

    print("Scanning API schemas for x-compression-callback + x-archive-type:")
    for schema_file in sorted(schema_dir.glob('*-api.yaml')):
        schema_name = schema_file.name
        schema = all_schemas.get(schema_name)

        if not schema:
            continue

        # Extract service name from filename
        service_name = schema_name.replace('-api.yaml', '')

        info = extract_template_info(schema, all_schemas, service_name)
        if info:
            print(f"  {schema_name}: {info.archive_type_name} -> {info.namespace} ({len(info.valid_paths)} paths)")
            templates.append(info)

    print()

    if not templates:
        print("No templates to generate (no x-compression-callback with x-archive-type found)")
        return

    # Generate template files
    print(f"Generating {len(templates)} template file(s):")
    for info in templates:
        class_name = f"{to_pascal_case(info.source_type)}Template"
        output_file = output_dir / f"{class_name}.cs"

        code = generate_template_class(info)

        with open(output_file, 'w') as f:
            f.write(code)

        print(f"  {output_file.name}")

    print()
    print(f"Generated {len(templates)} template(s) to {output_dir}")
    print()

    # Summary by resource type
    print("Templates by resource type:")
    by_resource = {}
    for t in templates:
        # Infer resource type from compression callback
        by_resource.setdefault(t.source_type.split('-')[0], []).append(t)
    for resource_type, ts in sorted(by_resource.items()):
        names = ', '.join(t.namespace for t in ts)
        print(f"  {resource_type}: {names}")


if __name__ == '__main__':
    main()
