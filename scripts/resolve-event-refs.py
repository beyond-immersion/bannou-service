#!/usr/bin/env python3
"""
Resolve complex cross-file $refs in event schemas before NSwag processing.

NSwag cannot handle cross-file $refs to types that contain nested local $refs.
This script inlines such types into the event schema, creating a resolved version
that NSwag can process correctly.

Architecture:
- Input: schemas/{service}-events.yaml (source of truth, never modified)
- Output: schemas/Generated/{service}-events-resolved.yaml (NSwag processes this)

The script:
1. Scans event schemas for cross-file $refs to API types
2. Identifies "complex" types (types with nested $refs that would fail in NSwag)
3. Inlines complex types and their dependencies into the event schema
4. Converts cross-file refs to local refs for inlined types
5. Outputs resolved schema to Generated/ directory

Usage:
    python3 scripts/resolve-event-refs.py

Called by generate-service-events.sh before NSwag processing.
"""

import sys
import re
from pathlib import Path
from typing import Any, Dict, Set, Optional, Tuple
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


def find_cross_file_refs(obj: Any, refs: Set[str] = None) -> Set[str]:
    """
    Recursively find all cross-file $ref paths in a schema object.

    Returns set of tuples: (file_path, type_name)
    """
    if refs is None:
        refs = set()

    if isinstance(obj, dict):
        if '$ref' in obj:
            ref = obj['$ref']
            # Match cross-file refs like '../actor-api.yaml#/components/schemas/TypeName'
            # or './actor-api.yaml#/components/schemas/TypeName'
            match = re.match(r"['\"]?\.\.?/?([^#'\"]+)#/components/schemas/([^'\"]+)['\"]?", ref)
            if match:
                refs.add((match.group(1), match.group(2)))
        for value in obj.values():
            find_cross_file_refs(value, refs)
    elif isinstance(obj, list):
        for item in obj:
            find_cross_file_refs(item, refs)

    return refs


def find_local_refs(obj: Any, refs: Set[str] = None, source_file: str = None) -> Set[str]:
    """
    Recursively find all local $ref type names in a schema object.

    Returns set of type names referenced via:
    - #/components/schemas/TypeName (standard local ref)
    - ./#/components/schemas/TypeName (self-qualified local ref)
    - {source_file}#/components/schemas/TypeName (self-qualified by filename)

    Args:
        obj: Schema object to search
        refs: Set to accumulate results (for recursion)
        source_file: If provided, also match refs to this file as "local"
    """
    if refs is None:
        refs = set()

    if isinstance(obj, dict):
        if '$ref' in obj:
            ref = obj['$ref']
            # Match local refs like '#/components/schemas/TypeName'
            # Also match './#/components/schemas/TypeName' (self-qualified)
            match = re.match(r"['\"]?(?:\./?)?#/components/schemas/([^'\"]+)['\"]?", ref)
            if match:
                refs.add(match.group(1))
            elif source_file:
                # Also match self-qualified refs like 'actor-api.yaml#/components/schemas/TypeName'
                pattern = rf"['\"]?{re.escape(source_file)}#/components/schemas/([^'\"]+)['\"]?"
                match = re.match(pattern, ref)
                if match:
                    refs.add(match.group(1))
        for value in obj.values():
            find_local_refs(value, refs, source_file)
    elif isinstance(obj, list):
        for item in obj:
            find_local_refs(item, refs, source_file)

    return refs


def has_nested_refs(type_def: Dict[str, Any]) -> bool:
    """Check if a type definition contains nested $refs."""
    local_refs = find_local_refs(type_def)
    return len(local_refs) > 0


def get_type_with_dependencies(
    type_name: str,
    source_schemas: Dict[str, Any],
    collected: Dict[str, Any] = None
) -> Dict[str, Any]:
    """
    Get a type definition and all its local dependencies from a schema.

    Args:
        type_name: Name of the type to get
        source_schemas: The components/schemas dict from the source file
        collected: Already collected types (for recursion)

    Returns:
        Dict mapping type names to their definitions
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
    for ref_name in local_refs:
        if ref_name not in collected:
            get_type_with_dependencies(ref_name, source_schemas, collected)

    return collected


def convert_refs_to_local(obj: Any, inlined_types: Set[str]) -> Any:
    """
    Convert cross-file refs to local refs for types that have been inlined.

    Also converts self-qualified refs (./#/...) to standard local refs (#/...).
    """
    if isinstance(obj, dict):
        if '$ref' in obj:
            ref = obj['$ref']
            # Check if this is a cross-file ref to an inlined type
            match = re.match(r"['\"]?\.\.?/?[^#]+#/components/schemas/([^'\"]+)['\"]?", ref)
            if match and match.group(1) in inlined_types:
                obj['$ref'] = f"#/components/schemas/{match.group(1)}"
            else:
                # Also fix self-qualified local refs (./#/... -> #/...)
                match = re.match(r"['\"]?\./?#/components/schemas/([^'\"]+)['\"]?", ref)
                if match:
                    obj['$ref'] = f"#/components/schemas/{match.group(1)}"
        for key, value in obj.items():
            obj[key] = convert_refs_to_local(value, inlined_types)
    elif isinstance(obj, list):
        for i, item in enumerate(obj):
            obj[i] = convert_refs_to_local(item, inlined_types)

    return obj


def fix_relative_paths_for_generated(obj: Any) -> Any:
    """
    Fix relative paths in refs when the output file is in Generated/ subdirectory.

    Refs like 'common-events.yaml#/...' need to become '../common-events.yaml#/...'
    because the resolved file is in schemas/Generated/, one level deeper.
    """
    if isinstance(obj, dict):
        if '$ref' in obj:
            ref = obj['$ref']
            # Match refs to sibling files (no ../ prefix, not a local #/ ref)
            # Examples: 'common-events.yaml#/...' or 'actor-api.yaml#/...'
            match = re.match(r"['\"]?([a-zA-Z][a-zA-Z0-9_-]*\.yaml)#(.+)['\"]?", ref)
            if match:
                # Add ../ prefix to go up from Generated/ to schemas/
                obj['$ref'] = f"../{match.group(1)}#{match.group(2)}"
        for key, value in obj.items():
            obj[key] = fix_relative_paths_for_generated(value)
    elif isinstance(obj, list):
        for i, item in enumerate(obj):
            obj[i] = fix_relative_paths_for_generated(item)

    return obj


def resolve_event_schema(events_path: Path, schema_dir: Path) -> Optional[Path]:
    """
    Resolve complex cross-file refs in an event schema.

    Args:
        events_path: Path to the event schema file
        schema_dir: Directory containing all schemas

    Returns:
        Path to the resolved schema file, or None if no resolution needed
    """
    events_schema = load_yaml(events_path)
    if events_schema is None:
        return None

    # Find all cross-file refs in the event schema
    cross_refs = find_cross_file_refs(events_schema)
    if not cross_refs:
        return None  # No cross-file refs, no resolution needed

    # Group refs by source file
    refs_by_file: Dict[str, Set[str]] = {}
    for file_path, type_name in cross_refs:
        if file_path not in refs_by_file:
            refs_by_file[file_path] = set()
        refs_by_file[file_path].add(type_name)

    # Check each referenced type for nested refs
    types_to_inline: Dict[str, Dict[str, Any]] = {}  # type_name -> definition

    for file_path, type_names in refs_by_file.items():
        # Resolve the source file path
        source_path = schema_dir / file_path
        if not source_path.exists():
            # Try without leading ../
            source_path = schema_dir / file_path.lstrip('../')

        source_schema = load_yaml(source_path)
        if source_schema is None:
            print(f"  Warning: Could not load {file_path}")
            continue

        source_schemas = source_schema.get('components', {}).get('schemas', {})

        for type_name in type_names:
            if type_name not in source_schemas:
                continue

            type_def = source_schemas[type_name]

            # Check if this type has nested refs (making it "complex")
            if has_nested_refs(type_def):
                print(f"  Found complex type: {type_name} (has nested refs)")
                # Get this type and all its dependencies
                deps = get_type_with_dependencies(type_name, source_schemas)
                types_to_inline.update(deps)

    if not types_to_inline:
        return None  # No complex types found, no resolution needed

    print(f"  Inlining {len(types_to_inline)} types: {', '.join(sorted(types_to_inline.keys()))}")

    # Create resolved schema
    resolved = deepcopy(events_schema)

    # Ensure components/schemas exists
    if 'components' not in resolved:
        resolved['components'] = {}
    if 'schemas' not in resolved['components']:
        resolved['components']['schemas'] = {}

    # Add inlined types to the schema
    for type_name, type_def in types_to_inline.items():
        # Convert any self-qualified refs in the inlined type to local refs
        type_def = convert_refs_to_local(type_def, set(types_to_inline.keys()))
        resolved['components']['schemas'][type_name] = type_def

    # Convert cross-file refs to local refs for inlined types
    resolved = convert_refs_to_local(resolved, set(types_to_inline.keys()))

    # Fix relative paths since output is in Generated/ subdirectory
    resolved = fix_relative_paths_for_generated(resolved)

    # Update description to note this is a resolved file
    if 'info' in resolved and 'description' in resolved['info']:
        original_desc = resolved['info']['description']
        resolved['info']['description'] = (
            f"AUTO-RESOLVED: Complex cross-file refs have been inlined for NSwag compatibility.\n"
            f"DO NOT EDIT - Regenerate with scripts/resolve-event-refs.py\n"
            f"Source: {events_path.name}\n\n"
            f"{original_desc}"
        )

    # Write resolved schema to Generated directory
    generated_dir = schema_dir / 'Generated'
    output_path = generated_dir / f"{events_path.stem}-resolved.yaml"
    save_yaml(output_path, resolved)

    return output_path


def main():
    """Process all event schemas and resolve complex cross-file refs."""
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'
    generated_dir = schema_dir / 'Generated'

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    # Create Generated directory if needed
    generated_dir.mkdir(exist_ok=True)

    # Clean up old resolved files
    for old_file in generated_dir.glob('*-events-resolved.yaml'):
        old_file.unlink()
        print(f"  Cleaned up: {old_file.name}")

    print("Resolving complex cross-file refs in event schemas...")
    print()

    resolved_files = []

    for events_file in sorted(schema_dir.glob('*-events.yaml')):
        # Skip lifecycle events, client events, and common events
        if any(x in events_file.name for x in ['-lifecycle-events', '-client-events', 'common-events']):
            continue

        print(f"Processing {events_file.name}...")

        output_path = resolve_event_schema(events_file, schema_dir)
        if output_path:
            resolved_files.append(output_path)
            print(f"  -> {output_path.name}")
        else:
            print(f"  No complex refs, using original")
        print()

    # Summary
    print("=" * 60)
    if resolved_files:
        print(f"Resolved {len(resolved_files)} event schema(s):")
        for f in resolved_files:
            print(f"  - {f.name}")
        print()
        print("generate-service-events.sh should use resolved files from Generated/")
    else:
        print("No event schemas required resolution")


if __name__ == '__main__':
    main()
