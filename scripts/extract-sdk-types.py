#!/usr/bin/env python3
"""
Extract SDK type mappings from OpenAPI schemas.

Scans a schema for `x-sdk-type` extensions and outputs:
1. Comma-separated list of type names to exclude from NSwag generation
2. Comma-separated list of SDK namespaces to import

Usage:
    python3 extract-sdk-types.py <schema-file> [--format=shell|json]

Example:
    python3 extract-sdk-types.py ../schemas/music-api.yaml --format=shell

Output (shell format):
    EXCLUDED_TYPES=PitchClass,Pitch,MidiJson
    SDK_NAMESPACES=BeyondImmersion.Bannou.MusicTheory.Pitch,BeyondImmersion.Bannou.MusicTheory.Output

The x-sdk-type extension marks schema types that exist in an external SDK.
Instead of generating duplicate C# classes, NSwag should exclude these types
and import the SDK namespaces so the generated code references the SDK types.

Schema example:
    components:
      schemas:
        PitchClass:
          x-sdk-type: BeyondImmersion.Bannou.MusicTheory.Pitch.PitchClass
          type: string
          enum: [C, Cs, D, Ds, E, F, Fs, G, Gs, A, As, B]
"""

import sys
import json
import argparse
from pathlib import Path

try:
    import yaml
except ImportError:
    print("Error: PyYAML is required. Install with: pip install pyyaml", file=sys.stderr)
    sys.exit(1)


def extract_sdk_types(schema_path: Path) -> tuple[list[str], set[str]]:
    """
    Extract SDK type mappings from an OpenAPI schema.

    Args:
        schema_path: Path to the OpenAPI YAML schema file

    Returns:
        Tuple of (excluded_type_names, sdk_namespaces)
    """
    with open(schema_path, 'r', encoding='utf-8') as f:
        schema = yaml.safe_load(f)

    excluded_types: list[str] = []
    sdk_namespaces: set[str] = set()

    # Check components/schemas for x-sdk-type extensions
    components = schema.get('components', {})
    schemas = components.get('schemas', {})

    for type_name, type_def in schemas.items():
        if not isinstance(type_def, dict):
            continue

        sdk_type = type_def.get('x-sdk-type')
        if sdk_type:
            # Add type name to exclusion list
            excluded_types.append(type_name)

            # Extract namespace from fully-qualified type name
            # e.g., "BeyondImmersion.Bannou.MusicTheory.Pitch.PitchClass" -> "BeyondImmersion.Bannou.MusicTheory.Pitch"
            if '.' in sdk_type:
                namespace = sdk_type.rsplit('.', 1)[0]
                sdk_namespaces.add(namespace)

    return excluded_types, sdk_namespaces


def main():
    parser = argparse.ArgumentParser(
        description='Extract SDK type mappings from OpenAPI schemas',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__
    )
    parser.add_argument('schema', type=Path, help='Path to OpenAPI schema file')
    parser.add_argument(
        '--format',
        choices=['shell', 'json'],
        default='shell',
        help='Output format (default: shell)'
    )

    args = parser.parse_args()

    if not args.schema.exists():
        print(f"Error: Schema file not found: {args.schema}", file=sys.stderr)
        sys.exit(1)

    excluded_types, sdk_namespaces = extract_sdk_types(args.schema)

    if args.format == 'json':
        output = {
            'excludedTypes': excluded_types,
            'sdkNamespaces': sorted(sdk_namespaces)
        }
        print(json.dumps(output, indent=2))
    else:
        # Shell format - easy to source in bash
        print(f"EXCLUDED_TYPES={','.join(excluded_types)}")
        print(f"SDK_NAMESPACES={','.join(sorted(sdk_namespaces))}")


if __name__ == '__main__':
    main()
