#!/usr/bin/env python3
"""
Generate Unreal Engine SDK helpers from Bannou schemas.

This is the main orchestrator script that runs all generation steps:
1. generate-client-schema.py - Consolidate service schemas
2. generate-unreal-protocol.py - Generate protocol headers
3. generate-unreal-types.py - Generate type headers

Usage:
    python3 scripts/generate-unreal-sdk.py

Output:
    schemas/Generated/
        bannou-client-api.yaml      - Consolidated client schema
        bannou-client-api.json      - JSON version for tooling

    sdks/unreal/Generated/
        BannouProtocol.h            - Binary protocol constants & helpers
        BannouTypes.h               - All request/response structs
        BannouEnums.h               - All shared enums
        BannouEndpoints.h           - Endpoint constants and registry
        BannouEvents.h              - Client event types
"""

import subprocess
import sys
from pathlib import Path


def run_script(script_path: Path, description: str) -> bool:
    """Run a Python script and return success status."""
    print(f"\n{'='*60}")
    print(f" {description}")
    print('='*60)

    result = subprocess.run(
        [sys.executable, str(script_path)],
        cwd=script_path.parent.parent,  # Run from repo root
        capture_output=False
    )

    if result.returncode != 0:
        print(f"\nERROR: {description} failed with exit code {result.returncode}")
        return False

    return True


def verify_outputs(repo_root: Path) -> bool:
    """Verify that all expected output files were generated."""
    expected_files = [
        # Schema outputs
        repo_root / 'schemas' / 'Generated' / 'bannou-client-api.yaml',
        repo_root / 'schemas' / 'Generated' / 'bannou-client-api.json',

        # Unreal SDK outputs
        repo_root / 'sdks' / 'unreal' / 'Generated' / 'BannouProtocol.h',
        repo_root / 'sdks' / 'unreal' / 'Generated' / 'BannouTypes.h',
        repo_root / 'sdks' / 'unreal' / 'Generated' / 'BannouEnums.h',
        repo_root / 'sdks' / 'unreal' / 'Generated' / 'BannouEndpoints.h',
        repo_root / 'sdks' / 'unreal' / 'Generated' / 'BannouEvents.h',
    ]

    print(f"\n{'='*60}")
    print(" Verifying generated files")
    print('='*60)

    all_present = True
    for file_path in expected_files:
        if file_path.exists():
            size = file_path.stat().st_size
            print(f"  [OK] {file_path.relative_to(repo_root)} ({size:,} bytes)")
        else:
            print(f"  [MISSING] {file_path.relative_to(repo_root)}")
            all_present = False

    return all_present


def main():
    """Run all Unreal SDK generation steps."""
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent

    print("="*60)
    print(" BANNOU UNREAL SDK GENERATOR")
    print("="*60)
    print(f"\nRepository root: {repo_root}")

    # Step 1: Generate consolidated client schema
    schema_script = script_dir / 'generate-client-schema.py'
    if not schema_script.exists():
        print(f"ERROR: Schema script not found: {schema_script}")
        sys.exit(1)

    if not run_script(schema_script, "Step 1: Consolidating client-facing schema"):
        sys.exit(1)

    # Step 2: Generate protocol headers
    protocol_script = script_dir / 'generate-unreal-protocol.py'
    if not protocol_script.exists():
        print(f"ERROR: Protocol script not found: {protocol_script}")
        sys.exit(1)

    if not run_script(protocol_script, "Step 2: Generating protocol headers"):
        sys.exit(1)

    # Step 3: Generate type headers
    types_script = script_dir / 'generate-unreal-types.py'
    if not types_script.exists():
        print(f"ERROR: Types script not found: {types_script}")
        sys.exit(1)

    if not run_script(types_script, "Step 3: Generating type headers"):
        sys.exit(1)

    # Verify outputs
    if not verify_outputs(repo_root):
        print("\nERROR: Some expected files were not generated")
        sys.exit(1)

    print(f"\n{'='*60}")
    print(" GENERATION COMPLETE")
    print('='*60)
    print("\nGenerated files:")
    print("  - schemas/Generated/bannou-client-api.yaml")
    print("  - schemas/Generated/bannou-client-api.json")
    print("  - sdks/unreal/Generated/BannouProtocol.h")
    print("  - sdks/unreal/Generated/BannouTypes.h")
    print("  - sdks/unreal/Generated/BannouEnums.h")
    print("  - sdks/unreal/Generated/BannouEndpoints.h")
    print("  - sdks/unreal/Generated/BannouEvents.h")
    print("\nSee sdks/unreal/Docs/INTEGRATION_GUIDE.md for usage instructions.")


if __name__ == '__main__':
    main()
