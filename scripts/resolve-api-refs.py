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
Resolve complex cross-file $refs in API schemas before NSwag processing.

NSwag cannot handle cross-file $refs in allOf that contain nested $refs.
This script inlines such types into the API schema, creating a resolved version
that NSwag can process correctly.

Architecture:
- Input: schemas/{service}-api.yaml (source of truth, never modified)
- Output: schemas/Generated/{service}-api-resolved.yaml (NSwag processes this)

The script:
1. Scans API schemas for cross-file $refs in allOf constructs
2. Identifies types that need inlining (types referenced via allOf from other files)
3. Inlines those types and their dependencies into the API schema
4. Converts cross-file refs to local refs for inlined types
5. Outputs resolved schema to Generated/ directory

Usage:
    python3 scripts/resolve-api-refs.py [service-name]

    If service-name provided, only processes that service.
    Otherwise processes all API schemas.

Called by generate-models.sh before NSwag processing.
"""

import sys
import re
from pathlib import Path
from typing import Any, Dict, Set, Optional, List
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


def find_allof_cross_file_refs(obj: Any, refs: Set[tuple] = None) -> Set[tuple]:
    """
    Recursively find cross-file $refs that appear in allOf constructs.

    These are the problematic refs that NSwag can't resolve properly.
    Returns set of tuples: (file_path, type_name)
    """
    if refs is None:
        refs = set()

    if isinstance(obj, dict):
        # Check if this is an allOf with cross-file refs
        if 'allOf' in obj and isinstance(obj['allOf'], list):
            for item in obj['allOf']:
                if isinstance(item, dict) and '$ref' in item:
                    ref = item['$ref']
                    # Match cross-file refs
                    match = re.match(r"['\"]?(?:\.\.?/)?([a-zA-Z][^#'\"]+)#/components/schemas/([^'\"]+)['\"]?", ref)
                    if match:
                        refs.add((match.group(1), match.group(2)))

        # Recurse into all values
        for value in obj.values():
            find_allof_cross_file_refs(value, refs)
    elif isinstance(obj, list):
        for item in obj:
            find_allof_cross_file_refs(item, refs)

    return refs


def find_local_refs(obj: Any, refs: Set[str] = None) -> Set[str]:
    """
    Recursively find all local $ref type names in a schema object.
    Returns set of type names referenced via #/components/schemas/TypeName
    """
    if refs is None:
        refs = set()

    if isinstance(obj, dict):
        if '$ref' in obj:
            ref = obj['$ref']
            match = re.match(r"['\"]?(?:\./?)?#/components/schemas/([^'\"]+)['\"]?", ref)
            if match:
                refs.add(match.group(1))
        for value in obj.values():
            find_local_refs(value, refs)
    elif isinstance(obj, list):
        for item in obj:
            find_local_refs(item, refs)

    return refs


def get_type_with_dependencies(
    type_name: str,
    source_schemas: Dict[str, Any],
    collected: Dict[str, Any] = None
) -> Dict[str, Any]:
    """
    Get a type definition and all its local dependencies from a schema.
    """
    if collected is None:
        collected = {}

    if type_name in collected:
        return collected

    if type_name not in source_schemas:
        print(f"  Warning: Type {type_name} not found in source schema")
        return collected

    type_def = deepcopy(source_schemas[type_name])
    collected[type_name] = type_def

    # Find and collect all local refs in this type
    local_refs = find_local_refs(type_def)
    for ref_name in sorted(local_refs):
        if ref_name not in collected:
            get_type_with_dependencies(ref_name, source_schemas, collected)

    return collected


def convert_cross_refs_to_local(obj: Any, inlined_types: Set[str]) -> Any:
    """
    Convert cross-file refs to local refs for types that have been inlined.
    """
    if isinstance(obj, dict):
        if '$ref' in obj:
            ref = obj['$ref']
            # Check if this is a cross-file ref to an inlined type
            match = re.match(r"['\"]?(?:\.\.?/)?([a-zA-Z][^#'\"]+)#/components/schemas/([^'\"]+)['\"]?", ref)
            if match and match.group(2) in inlined_types:
                obj['$ref'] = f"#/components/schemas/{match.group(2)}"
        for key, value in obj.items():
            obj[key] = convert_cross_refs_to_local(value, inlined_types)
    elif isinstance(obj, list):
        for i, item in enumerate(obj):
            obj[i] = convert_cross_refs_to_local(item, inlined_types)

    return obj


def resolve_api_schema(api_path: Path, schema_dir: Path) -> Optional[Path]:
    """
    Resolve cross-file allOf refs in an API schema.

    Returns path to resolved schema, or None if no resolution needed.
    """
    api_schema = load_yaml(api_path)
    if api_schema is None:
        return None

    # Find all cross-file refs in allOf constructs
    allof_refs = find_allof_cross_file_refs(api_schema)
    if not allof_refs:
        return None  # No cross-file allOf refs, no resolution needed

    print(f"  Found {len(allof_refs)} cross-file allOf ref(s)")

    # Group refs by source file
    refs_by_file: Dict[str, Set[str]] = {}
    for file_path, type_name in allof_refs:
        if file_path not in refs_by_file:
            refs_by_file[file_path] = set()
        refs_by_file[file_path].add(type_name)

    # Collect types to inline
    types_to_inline: Dict[str, Dict[str, Any]] = {}

    for file_path, type_names in refs_by_file.items():
        # Resolve the source file path
        source_path = schema_dir / file_path
        if not source_path.exists():
            source_path = schema_dir / file_path.lstrip('./')

        source_schema = load_yaml(source_path)
        if source_schema is None:
            print(f"  Warning: Could not load {file_path}")
            continue

        source_schemas = source_schema.get('components', {}).get('schemas', {})

        for type_name in type_names:
            if type_name not in source_schemas:
                print(f"  Warning: Type {type_name} not found in {file_path}")
                continue

            print(f"  Inlining: {type_name} from {file_path}")
            # Get this type and all its dependencies
            deps = get_type_with_dependencies(type_name, source_schemas)
            types_to_inline.update(deps)

    if not types_to_inline:
        return None

    print(f"  Total types to inline: {len(types_to_inline)} ({', '.join(sorted(types_to_inline.keys()))})")

    # Create resolved schema
    resolved = deepcopy(api_schema)

    # Ensure components/schemas exists
    if 'components' not in resolved:
        resolved['components'] = {}
    if 'schemas' not in resolved['components']:
        resolved['components']['schemas'] = {}

    # Add inlined types to the schema
    for type_name, type_def in sorted(types_to_inline.items()):
        resolved['components']['schemas'][type_name] = type_def

    # Convert cross-file refs to local refs for inlined types
    resolved = convert_cross_refs_to_local(resolved, set(types_to_inline.keys()))

    # Embed the list of inlined types for the shell script to read
    resolved['x-inlined-types'] = sorted(types_to_inline.keys())

    # Write resolved schema to Generated directory
    generated_dir = schema_dir / 'Generated'
    output_path = generated_dir / f"{api_path.stem}-resolved.yaml"
    save_yaml(output_path, resolved)

    return output_path


def main():
    """Process API schemas and resolve cross-file allOf refs."""
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'
    generated_dir = schema_dir / 'Generated'

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    # Create Generated directory if needed
    generated_dir.mkdir(exist_ok=True)

    # Check for specific service argument
    specific_service = None
    if len(sys.argv) > 1:
        specific_service = sys.argv[1]

    # Clean up old resolved API files
    if specific_service:
        old_file = generated_dir / f"{specific_service}-api-resolved.yaml"
        if old_file.exists():
            old_file.unlink()
            print(f"Cleaned up: {old_file.name}")
    else:
        for old_file in generated_dir.glob('*-api-resolved.yaml'):
            old_file.unlink()
            print(f"Cleaned up: {old_file.name}")

    print("Resolving cross-file allOf refs in API schemas...")
    print()

    resolved_files = []

    # Process API schemas
    if specific_service:
        api_files = [schema_dir / f"{specific_service}-api.yaml"]
    else:
        api_files = sorted(schema_dir.glob('*-api.yaml'))

    for api_file in api_files:
        # Skip common-api.yaml and Generated files
        if api_file.name == 'common-api.yaml' or 'Generated' in str(api_file):
            continue

        if not api_file.exists():
            if specific_service:
                print(f"ERROR: Schema not found: {api_file}")
                sys.exit(1)
            continue

        print(f"Processing {api_file.name}...")

        output_path = resolve_api_schema(api_file, schema_dir)
        if output_path:
            resolved_files.append(output_path)
            print(f"  -> {output_path.name}")
        else:
            print(f"  No cross-file allOf refs, using original")
        print()

    # Summary
    print("=" * 60)
    if resolved_files:
        print(f"Resolved {len(resolved_files)} API schema(s):")
        for f in resolved_files:
            print(f"  - {f.name}")
        print()
        print("generate-models.sh should use resolved files from Generated/")
    else:
        print("No API schemas required resolution")


if __name__ == '__main__':
    main()
