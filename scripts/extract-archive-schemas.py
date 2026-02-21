#!/usr/bin/env python3
"""
Extract archive schemas marked with x-archive-type: true from plugin API schemas.

This script reads plugin *-api.yaml schemas and creates a consolidated schema
containing only archive types for storyline-theory SDK code generation.

Features:
- Extracts types marked with x-archive-type: true
- Includes all transitive dependencies (referenced types)
- Resolves cross-file $refs (e.g., common-api.yaml ResourceArchiveBase)
- Outputs a minimal schema for NSwag generation

Output:
- schemas/Generated/storyline-archives.yaml

Usage:
    python3 scripts/extract-archive-schemas.py [--list]

Options:
    --list    Just list archive types found, don't generate schema
"""

import sys
import re
from pathlib import Path
from typing import Any, Dict, Set, Optional
from copy import deepcopy

try:
    from ruamel.yaml import YAML
    yaml = YAML()
    yaml.preserve_quotes = True
    yaml.width = 120
    yaml.default_flow_style = False
except ImportError:
    print("ERROR: ruamel.yaml is required. Install with: pip install ruamel.yaml")
    sys.exit(1)


# Plugins that may have archive types
ARCHIVE_PLUGINS = [
    'character',
    'character-personality',
    'character-history',
    'character-encounter',
    'realm-history',
]


def load_yaml(path: Path) -> Optional[Dict[str, Any]]:
    """Load a YAML file, returning None if it doesn't exist."""
    if not path.exists():
        return None
    with open(path) as f:
        return yaml.load(f)


def save_yaml(path: Path, data: Dict[str, Any]) -> None:
    """Save data to a YAML file."""
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, 'w') as f:
        yaml.dump(data, f)


def find_archive_types(schema: Dict[str, Any]) -> Set[str]:
    """Find all types marked with x-archive-type: true in a schema."""
    archive_types = set()
    components = schema.get('components', {})
    schemas = components.get('schemas', {})

    for type_name, type_def in schemas.items():
        if not isinstance(type_def, dict):
            continue
        if type_def.get('x-archive-type') is True:
            archive_types.add(type_name)

    return archive_types


def find_refs_in_type(type_def: Any) -> Set[tuple]:
    """
    Find all $ref references in a type definition.
    Returns set of tuples: (file_path or None, type_name)
    """
    refs = set()

    if isinstance(type_def, dict):
        if '$ref' in type_def:
            ref = type_def['$ref']
            # Match cross-file refs like 'common-api.yaml#/components/schemas/ResourceArchiveBase'
            cross_match = re.match(r"['\"]?(?:\.\.?/)?([a-zA-Z][^#'\"]+)#/components/schemas/([^'\"]+)['\"]?", ref)
            if cross_match:
                refs.add((cross_match.group(1), cross_match.group(2)))
            else:
                # Match local refs like '#/components/schemas/SomeType'
                local_match = re.match(r"['\"]?#/components/schemas/([^'\"]+)['\"]?", ref)
                if local_match:
                    refs.add((None, local_match.group(1)))

        for value in type_def.values():
            refs.update(find_refs_in_type(value))
    elif isinstance(type_def, list):
        for item in type_def:
            refs.update(find_refs_in_type(item))

    return refs


def collect_type_with_deps(
    type_name: str,
    schemas: Dict[str, Any],
    schema_dir: Path,
    collected: Dict[str, Any] = None,
    loaded_schemas: Dict[str, Dict[str, Any]] = None
) -> Dict[str, Any]:
    """
    Collect a type and all its dependencies recursively.
    Resolves cross-file refs as needed.
    """
    if collected is None:
        collected = {}
    if loaded_schemas is None:
        loaded_schemas = {}

    if type_name in collected:
        return collected

    if type_name not in schemas:
        return collected

    type_def = deepcopy(schemas[type_name])
    collected[type_name] = type_def

    # Find all refs in this type
    refs = find_refs_in_type(type_def)

    for file_path, ref_name in refs:
        if ref_name in collected:
            continue

        if file_path:
            # Cross-file ref - load the source schema
            if file_path not in loaded_schemas:
                source_path = schema_dir / file_path
                source_schema = load_yaml(source_path)
                if source_schema:
                    loaded_schemas[file_path] = source_schema.get('components', {}).get('schemas', {})
                else:
                    print(f"  Warning: Could not load {file_path}")
                    continue

            source_schemas = loaded_schemas.get(file_path, {})
            if ref_name in source_schemas:
                collect_type_with_deps(ref_name, source_schemas, schema_dir, collected, loaded_schemas)
        else:
            # Local ref
            collect_type_with_deps(ref_name, schemas, schema_dir, collected, loaded_schemas)

    return collected


def convert_refs_to_local(type_def: Any, collected_types: Set[str]) -> Any:
    """Convert cross-file refs to local refs for collected types."""
    if isinstance(type_def, dict):
        result = {}
        for key, value in type_def.items():
            if key == '$ref' and isinstance(value, str):
                # Check if this is a cross-file ref to a collected type
                cross_match = re.match(r"['\"]?(?:\.\.?/)?([a-zA-Z][^#'\"]+)#/components/schemas/([^'\"]+)['\"]?", value)
                if cross_match and cross_match.group(2) in collected_types:
                    result[key] = f"#/components/schemas/{cross_match.group(2)}"
                else:
                    result[key] = value
            else:
                result[key] = convert_refs_to_local(value, collected_types)
        return result
    elif isinstance(type_def, list):
        return [convert_refs_to_local(item, collected_types) for item in type_def]
    else:
        return type_def


def main():
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'
    generated_dir = schema_dir / 'Generated'

    list_only = '--list' in sys.argv

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    print("Extracting archive schemas for storyline-theory SDK...")
    print()

    # Collect all archive types from all plugins
    all_archive_types: Dict[str, str] = {}  # type_name -> source_plugin
    all_schemas: Dict[str, Dict[str, Any]] = {}  # Combined schemas from all sources
    loaded_schemas: Dict[str, Dict[str, Any]] = {}  # Cache of loaded schema files

    for plugin in ARCHIVE_PLUGINS:
        schema_path = schema_dir / f"{plugin}-api.yaml"

        # Check for resolved version first
        resolved_path = generated_dir / f"{plugin}-api-resolved.yaml"
        if resolved_path.exists():
            source_path = resolved_path
        else:
            source_path = schema_path

        if not source_path.exists():
            print(f"  Skipping {plugin}: schema not found")
            continue

        schema = load_yaml(source_path)
        if schema is None:
            continue

        schemas = schema.get('components', {}).get('schemas', {})
        archive_types = find_archive_types(schema)

        if archive_types:
            print(f"  {plugin}: {', '.join(sorted(archive_types))}")
            for type_name in archive_types:
                all_archive_types[type_name] = plugin
            all_schemas.update(schemas)

    if not all_archive_types:
        print("\nNo archive types found (no x-archive-type: true markers)")
        sys.exit(0)

    print(f"\nFound {len(all_archive_types)} archive type(s)")

    if list_only:
        sys.exit(0)

    # Collect all archive types with their dependencies
    collected_types: Dict[str, Any] = {}

    for type_name in all_archive_types:
        collect_type_with_deps(type_name, all_schemas, schema_dir, collected_types, loaded_schemas)

    print(f"Total types (with dependencies): {len(collected_types)}")

    # Convert cross-file refs to local refs
    collected_type_names = set(collected_types.keys())
    for type_name, type_def in collected_types.items():
        collected_types[type_name] = convert_refs_to_local(type_def, collected_type_names)

    # Create consolidated archive schema
    archive_schema = {
        'openapi': '3.0.0',
        'info': {
            'title': 'Storyline Archive Types',
            'version': '1.0.0',
            'description': 'Consolidated archive schemas for storyline-theory SDK. Auto-generated from plugin schemas.'
        },
        'paths': {},  # No endpoints, just types
        'components': {
            'schemas': dict(sorted(collected_types.items()))
        },
        'x-archive-types': sorted(all_archive_types.keys()),
        'x-source-plugins': sorted(set(all_archive_types.values()))
    }

    # Write output
    output_path = generated_dir / 'storyline-archives.yaml'
    save_yaml(output_path, archive_schema)

    print(f"\nGenerated: {output_path}")
    print(f"Archive types: {', '.join(sorted(all_archive_types.keys()))}")
    print(f"Source plugins: {', '.join(sorted(set(all_archive_types.values())))}")


if __name__ == '__main__':
    main()
