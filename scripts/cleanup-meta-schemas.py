#!/usr/bin/env python3
"""
ONE-TIME CLEANUP SCRIPT: Remove x-*-json extensions from schema files.

This script removes the embedded meta schema extensions (x-request-schema-json,
x-response-schema-json, x-endpoint-info-json) from the main API schema files.

After running this, the main schema files will be clean and the meta schemas
will be generated separately into schemas/Generated/{service}-api-meta.yaml.

Usage:
    python3 scripts/cleanup-meta-schemas.py

DELETE THIS SCRIPT AFTER USE - it's a one-time migration tool.
"""

import sys
from pathlib import Path

try:
    from ruamel.yaml import YAML
    yaml = YAML()
    yaml.preserve_quotes = True
    yaml.width = 120
    yaml.default_flow_style = False
except ImportError:
    print("ERROR: ruamel.yaml is required. Install with: pip install ruamel.yaml")
    sys.exit(1)


EXTENSIONS_TO_REMOVE = [
    'x-request-schema-json',
    'x-response-schema-json',
    'x-endpoint-info-json'
]


def cleanup_schema(schema_path: Path) -> tuple[bool, int]:
    """
    Remove x-*-json extension fields from a schema file.

    Returns (was_modified, count_removed).
    """
    with open(schema_path) as f:
        schema = yaml.load(f)

    if schema is None or 'paths' not in schema:
        return False, 0

    removed_count = 0

    for path, methods in schema['paths'].items():
        if not isinstance(methods, dict):
            continue

        for method, operation in methods.items():
            # Skip non-method keys
            if method in ('parameters', 'servers', 'summary', 'description', '$ref'):
                continue

            if not isinstance(operation, dict):
                continue

            # Remove each extension
            for ext in EXTENSIONS_TO_REMOVE:
                if ext in operation:
                    del operation[ext]
                    removed_count += 1

    if removed_count > 0:
        with open(schema_path, 'w') as f:
            yaml.dump(schema, f)
        return True, removed_count

    return False, 0


def main():
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    print("Cleaning meta schema extensions from API schemas...")
    print(f"Extensions to remove: {', '.join(EXTENSIONS_TO_REMOVE)}")
    print()

    schema_files = sorted(schema_dir.glob('*-api.yaml'))

    total_cleaned = 0
    total_extensions = 0

    for schema_path in schema_files:
        was_modified, count = cleanup_schema(schema_path)
        if was_modified:
            print(f"  Cleaned: {schema_path.name} ({count} extensions removed)")
            total_cleaned += 1
            total_extensions += count
        else:
            print(f"  Skipped: {schema_path.name} (no extensions found)")

    print()
    print(f"Summary: Cleaned {total_cleaned} file(s), removed {total_extensions} extension(s)")

    if total_cleaned > 0:
        print()
        print("SUCCESS: Schema files are now clean.")
        print("You can delete this script now: rm scripts/cleanup-meta-schemas.py")


if __name__ == '__main__':
    main()
