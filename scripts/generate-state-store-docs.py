#!/usr/bin/env python3
"""
Generate State Store Documentation from Component Files.

This script scans state store component YAML files and generates a comprehensive
markdown table documenting all state stores used in Bannou.

Output: docs/GENERATED-STATE-STORES.md

Usage:
    python3 scripts/generate-state-store-docs.py
"""

import sys
from pathlib import Path

# Use ruamel.yaml to parse YAML files
try:
    from ruamel.yaml import YAML
    yaml = YAML()
    yaml.preserve_quotes = True
except ImportError:
    print("ERROR: ruamel.yaml is required. Install with: pip install ruamel.yaml")
    sys.exit(1)


def get_backend_type(spec_type: str) -> str:
    """Convert component type to human-readable backend name."""
    type_map = {
        'state.redis': 'Redis',
        'state.mysql': 'MySQL',
        'state.postgresql': 'PostgreSQL',
        'state.mongodb': 'MongoDB',
        'state.cosmosdb': 'CosmosDB',
    }
    return type_map.get(spec_type, spec_type)


def get_service_from_name(component_name: str) -> str:
    """Extract service name from component name."""
    # Remove common prefixes/suffixes
    name = component_name

    # Handle mysql-* prefix
    if name.startswith('mysql-'):
        name = name[6:]

    # Remove -statestore suffix
    if name.endswith('-statestore'):
        name = name[:-11]
    elif name.endswith('-store'):
        name = name[:-6]

    return name.capitalize() if name else component_name


def get_purpose(component_name: str, spec_type: str) -> str:
    """Determine purpose based on component name and type."""
    backend = get_backend_type(spec_type)

    if 'mysql' in component_name.lower() or backend == 'MySQL':
        return 'Persistent queryable data'
    elif 'auth' in component_name.lower():
        return 'Session/token state (ephemeral)'
    elif 'connect' in component_name.lower():
        return 'WebSocket session state'
    elif 'permissions' in component_name.lower():
        return 'Permission cache and session capabilities'
    elif 'game-session' in component_name.lower():
        return 'Active game session state'
    else:
        return 'Service-specific state'


def scan_component_files(components_dir: Path) -> list:
    """Scan a components directory for state store definitions."""
    stores = []

    if not components_dir.exists():
        return stores

    for yaml_file in sorted(components_dir.glob('*.yaml')):
        # Skip non-statestore files
        if 'statestore' not in yaml_file.name and 'store' not in yaml_file.name:
            continue

        try:
            with open(yaml_file) as f:
                content = yaml.load(f)

            if content is None:
                continue

            # Check if it's a state store component
            kind = content.get('kind', '')
            if kind != 'Component':
                continue

            spec = content.get('spec', {})
            spec_type = spec.get('type', '')

            if not spec_type.startswith('state.'):
                continue

            metadata = content.get('metadata', {})
            name = metadata.get('name', yaml_file.stem)

            stores.append({
                'name': name,
                'backend': get_backend_type(spec_type),
                'service': get_service_from_name(name),
                'purpose': get_purpose(name, spec_type),
                'file': yaml_file.name,
            })
        except Exception as e:
            print(f"Warning: Failed to parse {yaml_file}: {e}")

    return stores


def generate_markdown(stores: list) -> str:
    """Generate markdown documentation from store list."""
    # Deduplicate by name (same stores may appear in multiple component dirs)
    seen = set()
    unique_stores = []
    for store in stores:
        if store['name'] not in seen:
            seen.add(store['name'])
            unique_stores.append(store)

    # Sort by name
    unique_stores.sort(key=lambda x: x['name'])

    lines = [
        "# Generated State Store Reference",
        "",
        "> **Source**: `provisioning/state-stores/*.yaml`",
        "> **Do not edit manually** - regenerate with `make generate-docs`",
        "",
        "This document lists all state store components used in Bannou.",
        "",
        "## State Store Components",
        "",
        "| Component Name | Backend | Service | Purpose |",
        "|----------------|---------|---------|---------|",
    ]

    for store in unique_stores:
        lines.append(
            f"| `{store['name']}` | {store['backend']} | {store['service']} | {store['purpose']} |"
        )

    lines.extend([
        "",
        "## Naming Conventions",
        "",
        "| Pattern | Backend | Description |",
        "|---------|---------|-------------|",
        "| `{service}-statestore` | Redis | Service-specific ephemeral state |",
        "| `mysql-{service}-statestore` | MySQL | Persistent queryable data |",
        "",
        "## Deployment Flexibility",
        "",
        "The state store abstraction means multiple logical state stores can share physical",
        "Redis/MySQL instances in simple deployments, while production deployments can map",
        "to dedicated infrastructure without code changes.",
        "",
        "### Example: Development (Shared Infrastructure)",
        "",
        "```yaml",
        "# All Redis state stores point to same instance",
        "auth-statestore:     bannou-redis:6379",
        "connect-statestore:  bannou-redis:6379",
        "permissions-statestore: bannou-redis:6379",
        "```",
        "",
        "### Example: Production (Dedicated Infrastructure)",
        "",
        "```yaml",
        "# Each service can have its own infrastructure",
        "auth-statestore:     auth-redis-cluster.prod:6379",
        "connect-statestore:  connect-redis-cluster.prod:6379",
        "permissions-statestore: permissions-redis.prod:6379",
        "```",
        "",
        "---",
        "",
        "*This file is auto-generated. See [TENETS.md](../TENETS.md) for architectural context.*",
        "",
    ])

    return '\n'.join(lines)


def main():
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent

    # Scan state store directories
    components_dirs = [
        repo_root / 'provisioning' / 'state-stores',
    ]

    all_stores = []
    for components_dir in components_dirs:
        stores = scan_component_files(components_dir)
        all_stores.extend(stores)

    if not all_stores:
        print("Warning: No state store components found")

    # Generate markdown
    markdown = generate_markdown(all_stores)

    # Write output
    output_file = repo_root / 'docs' / 'GENERATED-STATE-STORES.md'
    with open(output_file, 'w') as f:
        f.write(markdown)

    print(f"Generated {output_file} with {len(set(s['name'] for s in all_stores))} state stores")


if __name__ == '__main__':
    main()
