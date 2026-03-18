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
Unified schema $ref resolver for the Bannou code generation pipeline.

Replaces three separate scripts:
- resolve-api-refs.py (API schemas — was limited to allOf-only triggers)
- resolve-event-refs.py (service event schemas + lifecycle events)
- resolve-lifecycle-refs.py (lifecycle event schemas, per-service)

NSwag cannot handle cross-file $refs where the referenced type contains its own
nested $refs. For example, arbitration-api.yaml references EntityReference from
common-api.yaml, and EntityReference internally $refs EntityType. NSwag resolves
the inner $ref relative to the PROCESSING file (arbitration-api.yaml), where
EntityType doesn't exist. This breaks generation.

This script fixes the problem by inlining complex types (those with nested $refs)
and their full transitive dependency trees into the target schema, producing a
"resolved" version that NSwag can process as a single self-contained file.

Usage:
    python3 resolve-schema-refs.py                        # All schema types
    python3 resolve-schema-refs.py --api [service]        # API schemas only
    python3 resolve-schema-refs.py --events [service]     # Event schemas only
    python3 resolve-schema-refs.py --lifecycle [service]   # Lifecycle schemas only

Output (only when resolution is needed):
    schemas/Generated/{service}-api-resolved.yaml
    schemas/Generated/{service}-service-events-resolved.yaml
    schemas/Generated/{service}-service-lifecycle-events-resolved.yaml

Called by:
    generate-models.sh          (--api <service>)
    generate-service-events.sh  (--events)
    common.sh                   (--lifecycle <service>)
"""

import argparse
import re
import sys
from copy import deepcopy
from pathlib import Path
from typing import Any, Dict, Optional, Set, Tuple

try:
    from ruamel.yaml import YAML
    yaml = YAML()
    yaml.preserve_quotes = True
    yaml.width = 120
    yaml.default_flow_style = False
except ImportError:
    print("ERROR: ruamel.yaml is required. Install with: pip install ruamel.yaml")
    sys.exit(1)


# =============================================================================
# Schema Cache
# =============================================================================

_schema_cache: Dict[str, Optional[Dict[str, Any]]] = {}


def load_yaml(path: Path) -> Optional[Dict[str, Any]]:
    """Load a YAML file with caching. Returns None if file doesn't exist."""
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
    """Save data to a YAML file, creating parent directories as needed."""
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, 'w') as f:
        yaml.dump(data, f)


# =============================================================================
# $ref Detection
# =============================================================================

# Matches cross-file refs in any format:
#   'common-api.yaml#/components/schemas/EntityType'
#   '../common-api.yaml#/components/schemas/EntityType'
#   './actor-api.yaml#/components/schemas/ActorStatus'
CROSS_FILE_REF_PATTERN = re.compile(
    r"['\"]?(?:\.\.?/)?([a-zA-Z][^#'\"]+)#/components/schemas/([^'\"]+)['\"]?"
)

LOCAL_REF_PATTERN = re.compile(
    r"['\"]?(?:\./?)?#/components/schemas/([^'\"]+)['\"]?"
)


def find_all_refs(obj: Any) -> Set[Tuple[Optional[str], str]]:
    """
    Recursively find ALL $refs in a schema object.

    Returns set of tuples: (file_path_or_None, type_name)
    - file_path is None for local refs (#/components/schemas/TypeName)
    - file_path is the filename for cross-file refs
    """
    refs: Set[Tuple[Optional[str], str]] = set()
    _find_all_refs_inner(obj, refs)
    return refs


def _find_all_refs_inner(obj: Any, refs: Set[Tuple[Optional[str], str]]) -> None:
    if isinstance(obj, dict):
        if '$ref' in obj:
            ref = str(obj['$ref'])
            cross_match = CROSS_FILE_REF_PATTERN.match(ref)
            if cross_match:
                refs.add((cross_match.group(1), cross_match.group(2)))
            else:
                local_match = LOCAL_REF_PATTERN.match(ref)
                if local_match:
                    refs.add((None, local_match.group(1)))
        for value in obj.values():
            _find_all_refs_inner(value, refs)
    elif isinstance(obj, list):
        for item in obj:
            _find_all_refs_inner(item, refs)


def find_cross_file_refs(obj: Any) -> Set[Tuple[str, str]]:
    """Find only cross-file $refs. Returns set of (file_path, type_name)."""
    return {(f, t) for f, t in find_all_refs(obj) if f is not None}


def has_nested_refs(type_def: Dict[str, Any]) -> bool:
    """Check if a type definition contains ANY nested $refs (local or cross-file)."""
    return len(find_all_refs(type_def)) > 0


# =============================================================================
# Recursive Type Collection
# =============================================================================

def collect_type_with_all_deps(
    type_name: str,
    source_file: str,
    schema_dir: Path,
    collected: Optional[Dict[str, Dict[str, Any]]] = None
) -> Dict[str, Dict[str, Any]]:
    """
    Collect a type definition and ALL its dependencies, following both local
    and cross-file ref chains recursively through multiple files.

    Args:
        type_name: Name of the type to collect
        source_file: Filename where this type is defined (e.g., 'common-api.yaml')
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

    # Find ALL refs in this type (local and cross-file) and collect recursively
    all_refs = find_all_refs(type_def)

    for ref_file, ref_type in sorted(all_refs, key=lambda x: (x[0] or '', x[1])):
        if ref_type in collected:
            continue

        if ref_file is None:
            # Local ref — same source file
            collect_type_with_all_deps(ref_type, source_file, schema_dir, collected)
        else:
            # Cross-file ref — different source file
            collect_type_with_all_deps(ref_type, ref_file, schema_dir, collected)

    return collected


# =============================================================================
# Ref Rewriting
# =============================================================================

def convert_refs_to_local(obj: Any, inlined_types: Set[str]) -> Any:
    """
    Convert cross-file refs to local #/components/schemas/ refs for types
    that have been inlined into the target schema.

    Also normalizes self-qualified local refs (./#/... -> #/...).
    """
    if isinstance(obj, dict):
        if '$ref' in obj:
            ref = str(obj['$ref'])
            # Check cross-file ref to an inlined type
            match = CROSS_FILE_REF_PATTERN.match(ref)
            if match and match.group(2) in inlined_types:
                obj['$ref'] = f"#/components/schemas/{match.group(2)}"
            else:
                # Normalize self-qualified local refs (./#/... -> #/...)
                local_match = re.match(r"['\"]?\./?#/components/schemas/([^'\"]+)['\"]?", ref)
                if local_match:
                    obj['$ref'] = f"#/components/schemas/{local_match.group(1)}"
        for key, value in obj.items():
            obj[key] = convert_refs_to_local(value, inlined_types)
    elif isinstance(obj, list):
        for i, item in enumerate(obj):
            obj[i] = convert_refs_to_local(item, inlined_types)

    return obj


def fix_relative_paths_for_generated(obj: Any) -> Any:
    """
    Fix relative paths when the output file is in the Generated/ subdirectory.

    All remaining cross-file refs (ones NOT inlined) need '../' prefix because
    the output is in schemas/Generated/. Schema authors write sibling-relative
    refs (e.g., 'actor-api.yaml#/...'); this adds the '../' needed for the
    Generated/ output location.
    """
    if isinstance(obj, dict):
        if '$ref' in obj:
            ref = str(obj['$ref'])
            # Match refs to sibling files — with or without existing ../ prefix
            match = re.match(
                r"['\"]?(?:\.\.?/)?([a-zA-Z][a-zA-Z0-9_-]*\.yaml)#(.+)['\"]?",
                ref
            )
            if match:
                obj['$ref'] = f"../{match.group(1)}#{match.group(2)}"
        for key, value in obj.items():
            obj[key] = fix_relative_paths_for_generated(value)
    elif isinstance(obj, list):
        for i, item in enumerate(obj):
            obj[i] = fix_relative_paths_for_generated(item)

    return obj


# =============================================================================
# Core Resolution Logic
# =============================================================================

def resolve_schema(
    target_path: Path,
    schema_dir: Path,
    is_in_generated: bool
) -> Optional[Path]:
    """
    Resolve cross-file refs in a single schema file.

    Finds ALL cross-file $refs (not just allOf), checks if the referenced type
    has nested refs (which NSwag can't handle), and inlines complex types with
    their full transitive dependency trees.

    Args:
        target_path: Path to the schema file to resolve
        schema_dir: Base schemas/ directory for loading referenced files
        is_in_generated: True if target is already in Generated/ (affects path fixing)

    Returns:
        Path to the resolved schema file, or None if no resolution needed.
    """
    target_schema = load_yaml(target_path)
    if target_schema is None:
        return None

    # Find all cross-file refs in the target schema
    cross_refs = find_cross_file_refs(target_schema)
    if not cross_refs:
        return None  # No cross-file refs at all, nothing to resolve

    # For each referenced type, check if it has nested refs that need inlining
    types_to_inline: Dict[str, Dict[str, Any]] = {}

    for file_path, type_name in sorted(cross_refs):
        # Strip leading ../ or ./ for path resolution
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

        # Check if this type has ANY nested refs (making it "complex")
        if has_nested_refs(type_def):
            print(f"  Complex type: {type_name} from {clean_file} (has nested refs)")
            # Collect this type and ALL its dependencies (following cross-file chains)
            deps = collect_type_with_all_deps(type_name, clean_file, schema_dir)
            types_to_inline.update(deps)

    if not types_to_inline:
        return None  # All cross-file refs are to simple types, NSwag handles these

    print(f"  Inlining {len(types_to_inline)} types: {', '.join(sorted(types_to_inline.keys()))}")

    # Create resolved schema (deep copy to avoid mutating cached original)
    resolved = deepcopy(target_schema)

    # Ensure components/schemas exists
    if 'components' not in resolved:
        resolved['components'] = {}
    if 'schemas' not in resolved['components']:
        resolved['components']['schemas'] = {}

    # Add inlined types (convert their internal refs to local)
    inlined_names = set(types_to_inline.keys())
    for t_name, t_def in sorted(types_to_inline.items()):
        t_def = convert_refs_to_local(t_def, inlined_names)
        resolved['components']['schemas'][t_name] = t_def

    # Convert cross-file refs in the main schema to local refs for inlined types
    resolved = convert_refs_to_local(resolved, inlined_names)

    # Fix relative paths if the output goes to Generated/ and the input was NOT
    # already in Generated/ (lifecycle schemas are already there and have ../ prefixes)
    if not is_in_generated:
        resolved = fix_relative_paths_for_generated(resolved)

    # Embed the list of inlined types for downstream exclusion logic
    resolved['x-inlined-types'] = sorted(types_to_inline.keys())

    # Determine output path
    generated_dir = schema_dir / 'Generated'

    if is_in_generated:
        # Lifecycle schemas: output next to input with -resolved suffix
        stem = target_path.stem
        if '-service-lifecycle-events' in stem:
            output_name = stem.replace(
                '-service-lifecycle-events', '-service-lifecycle-events-resolved'
            ) + '.yaml'
        else:
            output_name = f"{stem}-resolved.yaml"
        output_path = target_path.parent / output_name
    else:
        # API and event schemas: output to Generated/
        output_path = generated_dir / f"{target_path.stem}-resolved.yaml"

    save_yaml(output_path, resolved)
    return output_path


# =============================================================================
# Batch Processing
# =============================================================================

def clean_old_resolved(generated_dir: Path, pattern: str) -> None:
    """Remove old resolved files matching a glob pattern."""
    if not generated_dir.exists():
        return
    for old_file in generated_dir.glob(pattern):
        old_file.unlink()
        print(f"  Cleaned up: {old_file.name}")


def process_api_schemas(
    schema_dir: Path,
    service_filter: Optional[str] = None
) -> list:
    """Process API schemas and resolve cross-file refs."""
    generated_dir = schema_dir / 'Generated'

    # Clean old resolved files
    if service_filter:
        old = generated_dir / f"{service_filter}-api-resolved.yaml"
        if old.exists():
            old.unlink()
            print(f"  Cleaned up: {old.name}")
    else:
        clean_old_resolved(generated_dir, '*-api-resolved.yaml')

    resolved = []

    if service_filter:
        api_files = [schema_dir / f"{service_filter}-api.yaml"]
    else:
        api_files = sorted(schema_dir.glob('*-api.yaml'))

    for api_file in api_files:
        if api_file.name == 'common-api.yaml' or 'Generated' in str(api_file):
            continue
        if not api_file.exists():
            if service_filter:
                print(f"ERROR: Schema not found: {api_file}")
                sys.exit(1)
            continue

        print(f"Processing {api_file.name}...")
        output = resolve_schema(api_file, schema_dir, is_in_generated=False)
        if output:
            resolved.append(output)
            print(f"  -> {output.name}")
        else:
            print(f"  No complex refs, using original")

    return resolved


def process_event_schemas(
    schema_dir: Path,
    service_filter: Optional[str] = None
) -> list:
    """Process service event schemas and resolve cross-file refs."""
    generated_dir = schema_dir / 'Generated'

    # Clean old resolved files
    if service_filter:
        old = generated_dir / f"{service_filter}-service-events-resolved.yaml"
        if old.exists():
            old.unlink()
            print(f"  Cleaned up: {old.name}")
    else:
        clean_old_resolved(generated_dir, '*-service-events-resolved.yaml')

    resolved = []

    if service_filter:
        event_files = [schema_dir / f"{service_filter}-service-events.yaml"]
    else:
        event_files = sorted(schema_dir.glob('*-service-events.yaml'))

    for events_file in event_files:
        # Skip lifecycle events, client events, and common events
        if any(x in events_file.name for x in [
            '-service-lifecycle-events', '-client-events', 'common-events'
        ]):
            continue
        if not events_file.exists():
            if service_filter:
                print(f"Warning: Events schema not found: {events_file}")
            continue

        print(f"Processing {events_file.name}...")
        output = resolve_schema(events_file, schema_dir, is_in_generated=False)
        if output:
            resolved.append(output)
            print(f"  -> {output.name}")
        else:
            print(f"  No complex refs, using original")

    return resolved


def process_lifecycle_schemas(
    schema_dir: Path,
    service_filter: Optional[str] = None
) -> list:
    """Process lifecycle event schemas and resolve cross-file refs."""
    generated_dir = schema_dir / 'Generated'

    # Clean old resolved files
    if service_filter:
        old = generated_dir / f"{service_filter}-service-lifecycle-events-resolved.yaml"
        if old.exists():
            old.unlink()
            print(f"  Cleaned up: {old.name}")
    else:
        clean_old_resolved(generated_dir, '*-service-lifecycle-events-resolved.yaml')

    resolved = []

    if service_filter:
        lifecycle_files = [generated_dir / f"{service_filter}-service-lifecycle-events.yaml"]
    else:
        lifecycle_files = sorted(generated_dir.glob('*-service-lifecycle-events.yaml'))

    for lifecycle_file in lifecycle_files:
        if not lifecycle_file.exists():
            if service_filter:
                # Not an error — service may not have lifecycle events
                pass
            continue
        if '-resolved' in lifecycle_file.name:
            continue

        print(f"Processing {lifecycle_file.name}...")
        output = resolve_schema(lifecycle_file, schema_dir, is_in_generated=True)
        if output:
            resolved.append(output)
            print(f"  -> {output.name}")
        else:
            print(f"  No complex refs, using original")

    return resolved


# =============================================================================
# Main
# =============================================================================

def main():
    parser = argparse.ArgumentParser(
        description='Resolve cross-file $refs in Bannou OpenAPI schemas for NSwag compatibility.',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__
    )
    parser.add_argument(
        '--api', nargs='?', const='__all__', default=None, metavar='SERVICE',
        help='Process API schemas (all if no service specified)'
    )
    parser.add_argument(
        '--events', nargs='?', const='__all__', default=None, metavar='SERVICE',
        help='Process service event schemas (all if no service specified)'
    )
    parser.add_argument(
        '--lifecycle', nargs='?', const='__all__', default=None, metavar='SERVICE',
        help='Process lifecycle event schemas (all if no service specified)'
    )

    args = parser.parse_args()

    # If no flags specified, process everything
    process_all = args.api is None and args.events is None and args.lifecycle is None

    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'
    generated_dir = schema_dir / 'Generated'

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    generated_dir.mkdir(exist_ok=True)

    all_resolved = []

    # Process API schemas
    if process_all or args.api is not None:
        service = None if (process_all or args.api == '__all__') else args.api
        print("=" * 60)
        print(" Resolving API schemas")
        print("=" * 60)
        resolved = process_api_schemas(schema_dir, service)
        all_resolved.extend(resolved)
        print()

    # Process event schemas
    if process_all or args.events is not None:
        service = None if (process_all or args.events == '__all__') else args.events
        print("=" * 60)
        print(" Resolving event schemas")
        print("=" * 60)
        resolved = process_event_schemas(schema_dir, service)
        all_resolved.extend(resolved)
        print()

    # Process lifecycle schemas
    if process_all or args.lifecycle is not None:
        service = None if (process_all or args.lifecycle == '__all__') else args.lifecycle
        print("=" * 60)
        print(" Resolving lifecycle event schemas")
        print("=" * 60)
        resolved = process_lifecycle_schemas(schema_dir, service)
        all_resolved.extend(resolved)
        print()

    # Summary
    print("=" * 60)
    if all_resolved:
        print(f"Resolved {len(all_resolved)} schema(s):")
        for f in all_resolved:
            print(f"  - {f.name}")
    else:
        print("No schemas required resolution")


if __name__ == '__main__':
    main()
