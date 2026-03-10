#!/usr/bin/env python3
# ⛔⛔⛔ AGENT MODIFICATION PROHIBITED ⛔⛔⛔
# This script is part of Bannou's code generation pipeline.
# DO NOT MODIFY without EXPLICIT user instructions to change code generation.
#
# Changes to generation scripts silently break builds across ALL 76+ services.
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
Resolve complex cross-file $refs in lifecycle event schemas before NSwag processing.

Lifecycle events live in schemas/Generated/ and reference types from API schemas
via ../service-api.yaml paths. When those API types contain nested cross-file $refs
(e.g., AuthMethodInfo -> common-api.yaml#AuthProvider), NSwag fails to resolve the
transitive chain. This script inlines such types and their full dependency trees.

Architecture:
- Input: schemas/Generated/{service}-lifecycle-events.yaml
- Output: schemas/Generated/{service}-lifecycle-events-resolved.yaml

The script:
1. Loads the lifecycle event schema from Generated/
2. Finds cross-file $refs to API types
3. Checks if those types have ANY nested $refs (local or cross-file)
4. Recursively inlines complex types and all their dependencies (following cross-file chains)
5. Converts refs to local refs for all inlined types
6. Outputs resolved schema alongside the original in Generated/

Usage:
    python3 scripts/resolve-lifecycle-refs.py <service-name>

Called by generate-models.sh before NSwag lifecycle event processing.
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


# Cache loaded schemas to avoid re-reading files
_schema_cache: Dict[str, Optional[Dict[str, Any]]] = {}


def load_yaml(path: Path) -> Optional[Dict[str, Any]]:
    """Load a YAML file, returning None if it doesn't exist. Results are cached."""
    key = str(path.resolve())
    if key in _schema_cache:
        return _schema_cache[key]
    if not path.exists():
        _schema_cache[key] = None
        return None
    with open(path) as f:
        data = yaml.load(f)
    _schema_cache[key] = data
    return data


def save_yaml(path: Path, data: Dict[str, Any]) -> None:
    """Save data to a YAML file."""
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, 'w') as f:
        yaml.dump(data, f)


def find_all_refs(obj: Any) -> Set[Tuple[Optional[str], str]]:
    """
    Recursively find ALL $refs in a schema object.

    Returns set of tuples: (file_path_or_None, type_name)
    - file_path is None for local refs (#/components/schemas/TypeName)
    - file_path is the filename for cross-file refs
    """
    refs = set()
    _find_all_refs_inner(obj, refs)
    return refs


def _find_all_refs_inner(obj: Any, refs: Set[Tuple[Optional[str], str]]) -> None:
    if isinstance(obj, dict):
        if '$ref' in obj:
            ref = obj['$ref']
            # Cross-file ref?
            match = re.match(
                r"['\"]?(?:\.\.?/)?([a-zA-Z][^#'\"]+)#/components/schemas/([^'\"]+)['\"]?",
                ref
            )
            if match:
                refs.add((match.group(1), match.group(2)))
            else:
                # Local ref?
                match = re.match(r"['\"]?(?:\./?)?#/components/schemas/([^'\"]+)['\"]?", ref)
                if match:
                    refs.add((None, match.group(1)))
        for value in obj.values():
            _find_all_refs_inner(value, refs)
    elif isinstance(obj, list):
        for item in obj:
            _find_all_refs_inner(item, refs)


def find_cross_file_refs(obj: Any) -> Set[Tuple[str, str]]:
    """Find only cross-file $refs. Returns set of (file_path, type_name)."""
    all_refs = find_all_refs(obj)
    return {(f, t) for f, t in all_refs if f is not None}


def has_any_nested_refs(type_def: Dict[str, Any]) -> bool:
    """Check if a type definition contains ANY nested $refs (local or cross-file)."""
    refs = find_all_refs(type_def)
    return len(refs) > 0


def collect_type_with_all_deps(
    type_name: str,
    source_file: str,
    schema_dir: Path,
    collected: Dict[str, Dict[str, Any]] = None
) -> Dict[str, Dict[str, Any]]:
    """
    Get a type definition and ALL its dependencies, following both local and cross-file refs.

    Args:
        type_name: Name of the type to collect
        source_file: Filename where this type is defined (e.g., 'account-api.yaml')
        schema_dir: Base schemas/ directory for resolving file paths
        collected: Already collected types (for recursion)

    Returns:
        Dict mapping type names to their definitions (all inlined)
    """
    if collected is None:
        collected = {}

    if type_name in collected:
        return collected

    # Load the source file
    source_path = schema_dir / source_file
    source_schema = load_yaml(source_path)
    if source_schema is None:
        print(f"  Warning: Could not load {source_file}")
        return collected

    source_schemas = source_schema.get('components', {}).get('schemas', {})

    if type_name not in source_schemas:
        print(f"  Warning: Type {type_name} not found in {source_file}")
        return collected

    type_def = deepcopy(source_schemas[type_name])
    collected[type_name] = type_def

    # Find ALL refs in this type (local and cross-file)
    all_refs = find_all_refs(type_def)

    for ref_file, ref_type in sorted(all_refs):
        if ref_type in collected:
            continue

        if ref_file is None:
            # Local ref — same source file
            collect_type_with_all_deps(ref_type, source_file, schema_dir, collected)
        else:
            # Cross-file ref — different source file
            collect_type_with_all_deps(ref_type, ref_file, schema_dir, collected)

    return collected


def convert_refs_to_local(obj: Any, inlined_types: Set[str]) -> Any:
    """Convert cross-file refs to local refs for types that have been inlined."""
    if isinstance(obj, dict):
        if '$ref' in obj:
            ref = obj['$ref']
            # Check cross-file ref
            match = re.match(
                r"['\"]?(?:\.\.?/)?([a-zA-Z][^#'\"]+)#/components/schemas/([^'\"]+)['\"]?",
                ref
            )
            if match and match.group(2) in inlined_types:
                obj['$ref'] = f"#/components/schemas/{match.group(2)}"
        for key, value in obj.items():
            obj[key] = convert_refs_to_local(value, inlined_types)
    elif isinstance(obj, list):
        for i, item in enumerate(obj):
            obj[i] = convert_refs_to_local(item, inlined_types)

    return obj


def resolve_lifecycle_schema(lifecycle_path: Path, schema_dir: Path) -> Optional[Path]:
    """
    Resolve complex cross-file refs in a lifecycle event schema.

    Lifecycle events are in Generated/ with ../ prefixed refs to sibling schemas.
    NSwag can't handle transitive cross-file ref chains (e.g., lifecycle -> api -> common).
    This function inlines any type whose dependency tree includes nested refs.
    """
    events_schema = load_yaml(lifecycle_path)
    if events_schema is None:
        return None

    # Find all cross-file refs in the lifecycle event schema
    cross_refs = find_cross_file_refs(events_schema)
    if not cross_refs:
        return None

    # For each referenced type, check if it has nested refs that need inlining
    types_to_inline: Dict[str, Dict[str, Any]] = {}

    for file_path, type_name in sorted(cross_refs):
        # Strip ../ prefix for loading (lifecycle refs have ../ because they're in Generated/)
        clean_file = file_path.lstrip('./')
        if clean_file.startswith('/'):
            clean_file = clean_file[1:]

        source_path = schema_dir / clean_file
        source_schema = load_yaml(source_path)
        if source_schema is None:
            print(f"  Warning: Could not load {file_path}")
            continue

        source_schemas = source_schema.get('components', {}).get('schemas', {})

        if type_name not in source_schemas:
            continue

        type_def = source_schemas[type_name]

        # Check if this type has ANY nested refs (local or cross-file)
        if has_any_nested_refs(type_def):
            print(f"  Found complex type: {type_name} from {clean_file} (has nested refs)")
            # Collect this type and ALL its dependencies (following cross-file chains)
            deps = collect_type_with_all_deps(type_name, clean_file, schema_dir)
            types_to_inline.update(deps)

    if not types_to_inline:
        return None

    print(f"  Inlining {len(types_to_inline)} types: {', '.join(sorted(types_to_inline.keys()))}")

    # Create resolved schema
    resolved = deepcopy(events_schema)

    # Ensure components/schemas exists
    if 'components' not in resolved:
        resolved['components'] = {}
    if 'schemas' not in resolved['components']:
        resolved['components']['schemas'] = {}

    # Add inlined types (convert their internal refs to local)
    for t_name, t_def in sorted(types_to_inline.items()):
        t_def = convert_refs_to_local(t_def, set(types_to_inline.keys()))
        resolved['components']['schemas'][t_name] = t_def

    # Convert all cross-file refs in the main schema to local refs for inlined types
    resolved = convert_refs_to_local(resolved, set(types_to_inline.keys()))

    # No path fixing needed — lifecycle events are already in Generated/
    # and remaining cross-file refs (like ../common-events.yaml) already have correct ../ prefix

    # Embed the list of inlined types for the shell script to read
    resolved['x-inlined-types'] = sorted(types_to_inline.keys())

    # Write resolved schema (same directory as input, with -resolved suffix)
    output_name = lifecycle_path.stem.replace(
        '-lifecycle-events', '-lifecycle-events-resolved'
    ) + '.yaml'
    output_path = lifecycle_path.parent / output_name
    save_yaml(output_path, resolved)

    return output_path


def main():
    """Process a single service's lifecycle event schema and resolve complex refs."""
    if len(sys.argv) < 2:
        print("Usage: resolve-lifecycle-refs.py <service-name>")
        print("  Resolves complex cross-file refs in lifecycle event schemas.")
        sys.exit(1)

    service_name = sys.argv[1]

    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'
    generated_dir = schema_dir / 'Generated'

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    lifecycle_path = generated_dir / f"{service_name}-lifecycle-events.yaml"
    if not lifecycle_path.exists():
        # No lifecycle events for this service, nothing to do
        return

    # Clean up old resolved file for this service
    old_resolved = generated_dir / f"{service_name}-lifecycle-events-resolved.yaml"
    if old_resolved.exists():
        old_resolved.unlink()

    print(f"Resolving cross-file refs in {lifecycle_path.name}...")

    output_path = resolve_lifecycle_schema(lifecycle_path, schema_dir)
    if output_path:
        print(f"  -> {output_path.name}")
    else:
        print(f"  No complex refs, using original")


if __name__ == '__main__':
    main()
