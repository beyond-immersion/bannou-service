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
Generate Service Details Documentation from Deep Dive Documents.

This script scans docs/plugins/*.md deep dive documents as the primary source
of truth. For each deep dive, it extracts the ## Overview section and looks up
the matching *-api.yaml schema (if one exists) for version and endpoint counts.
Services without deep dives are excluded. Services without schemas are included
with no version or endpoint information.

Output: docs/GENERATED-SERVICE-DETAILS.md

Usage:
    python3 scripts/generate-service-details-docs.py
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


"""Layer value to output filename mapping."""
LAYER_FILES = {
    'Infrastructure': 'GENERATED-INFRASTRUCTURE-SERVICE-DETAILS.md',
    'AppFoundation': 'GENERATED-APP-FOUNDATION-SERVICE-DETAILS.md',
    'GameFoundation': 'GENERATED-GAME-FOUNDATION-SERVICE-DETAILS.md',
    'AppFeatures': 'GENERATED-APP-FEATURES-SERVICE-DETAILS.md',
    'GameFeatures': 'GENERATED-GAME-FEATURES-SERVICE-DETAILS.md',
}

"""Layer display names for per-layer document titles."""
LAYER_DISPLAY = {
    'Infrastructure': 'Infrastructure (L0)',
    'AppFoundation': 'App Foundation (L1)',
    'GameFoundation': 'Game Foundation (L2)',
    'AppFeatures': 'App Features (L3)',
    'GameFeatures': 'Game Features (L4)',
}


def to_title_case(name: str) -> str:
    """Convert kebab-case service name to Title Case."""
    parts = name.split('-')
    return ' '.join(p.capitalize() for p in parts)


def extract_layer(deep_dive_file: Path) -> str:
    """Extract the > **Layer**: value from a deep dive document header."""
    try:
        content = deep_dive_file.read_text()
    except Exception:
        return ''

    for line in content.split('\n')[:15]:  # Layer is always in the header
        if line.startswith('> **Layer**:'):
            return line.split(':', 1)[1].strip()
    return ''


def extract_deep_dive_overview(deep_dive_file: Path) -> str:
    """Extract the ## Overview section from a deep dive document."""
    try:
        content = deep_dive_file.read_text()
    except Exception:
        return ''

    lines = content.split('\n')
    in_overview = False
    overview_lines = []

    for line in lines:
        if line.strip() == '## Overview':
            in_overview = True
            continue
        if in_overview:
            # Stop at next heading or horizontal rule
            if line.startswith('## ') or line.strip() == '---':
                break
            overview_lines.append(line)

    # Strip leading/trailing blank lines
    while overview_lines and not overview_lines[0].strip():
        overview_lines.pop(0)
    while overview_lines and not overview_lines[-1].strip():
        overview_lines.pop()

    return '\n'.join(overview_lines)


def scan_deep_dives(plugins_dir: Path) -> dict:
    """Scan all deep dive documents and extract service keys and overviews."""
    services = {}

    if not plugins_dir.exists():
        return services

    for md_file in sorted(plugins_dir.glob('*.md')):
        service_key = md_file.stem.lower()  # ACCOUNT.md -> account
        overview = extract_deep_dive_overview(md_file)

        if not overview:
            print(f"Warning: No ## Overview section in {md_file.name}, skipping")
            continue

        layer = extract_layer(md_file)
        if not layer:
            print(f"Warning: No > **Layer**: line in {md_file.name}")

        services[service_key] = {
            'display_name': to_title_case(service_key),
            'overview': overview,
            'layer': layer,
        }

    return services


def scan_api_schemas(schemas_dir: Path) -> dict:
    """Scan API schema files and extract version and endpoint counts."""
    schemas = {}

    if not schemas_dir.exists():
        return schemas

    for yaml_file in sorted(schemas_dir.glob('*-api.yaml')):
        try:
            with open(yaml_file) as f:
                content = yaml.load(f)

            if content is None:
                continue

            service_key = yaml_file.name.replace('-api.yaml', '')

            info = content.get('info', {})
            version = info.get('version', '1.0.0')

            # Count endpoints
            endpoint_count = 0
            paths = content.get('paths', {})
            for path, methods in paths.items():
                if not isinstance(methods, dict):
                    continue
                for method, details in methods.items():
                    if method.startswith('x-') or not isinstance(details, dict):
                        continue
                    endpoint_count += 1

            schemas[service_key] = {
                'version': version,
                'endpoint_count': endpoint_count,
                'source_file': yaml_file.name,
            }

        except Exception as e:
            print(f"Warning: Failed to parse {yaml_file}: {e}")

    return schemas


def generate_markdown(services: dict, schemas: dict) -> str:
    """Generate markdown documentation from deep dives with optional schema data."""
    lines = [
        "# Generated Service Details Reference",
        "",
        "> **Source**: `docs/plugins/*.md`",
        "> **Do not edit manually** - regenerate with `make generate-docs`",
        "",
        "This document provides a compact reference of all Bannou services.",
        "",
    ]

    total_endpoints = 0
    for service_key in sorted(services.keys()):
        svc = services[service_key]
        deep_dive_file = service_key.upper() + '.md'

        lines.append(f"## {svc['display_name']} {{#{service_key}}}")
        lines.append("")

        # Build metadata line based on whether a schema exists
        schema = schemas.get(service_key)
        if schema:
            total_endpoints += schema['endpoint_count']
            lines.append(
                f"**Version**: {schema['version']} | "
                f"**Schema**: `schemas/{schema['source_file']}` | "
                f"**Endpoints**: {schema['endpoint_count']} | "
                f"**Deep Dive**: [docs/plugins/{deep_dive_file}](plugins/{deep_dive_file})"
            )
        else:
            lines.append(
                f"**Deep Dive**: [docs/plugins/{deep_dive_file}](plugins/{deep_dive_file})"
            )

        lines.append("")
        lines.append(svc['overview'])
        lines.append("")

    # Summary
    lines.extend([
        "## Summary",
        "",
        f"- **Total services**: {len(services)}",
        f"- **Total endpoints**: {total_endpoints}",
        "",
        "---",
        "",
        "*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for architectural context.*",
        "",
    ])

    return '\n'.join(lines)


def generate_layer_markdown(layer: str, services: dict, schemas: dict) -> str:
    """Generate markdown for a single layer's services."""
    display = LAYER_DISPLAY.get(layer, layer)
    lines = [
        f"# Generated {display} Service Details",
        "",
        "> **Source**: `docs/plugins/*.md`",
        "> **Do not edit manually** - regenerate with `make generate-docs`",
        "",
        f"Services in the **{display}** layer.",
        "",
    ]

    total_endpoints = 0
    service_count = 0
    for service_key in sorted(services.keys()):
        svc = services[service_key]
        if svc.get('layer') != layer:
            continue
        service_count += 1
        deep_dive_file = service_key.upper() + '.md'

        lines.append(f"## {svc['display_name']} {{#{service_key}}}")
        lines.append("")

        schema = schemas.get(service_key)
        if schema:
            total_endpoints += schema['endpoint_count']
            lines.append(
                f"**Version**: {schema['version']} | "
                f"**Schema**: `schemas/{schema['source_file']}` | "
                f"**Endpoints**: {schema['endpoint_count']} | "
                f"**Deep Dive**: [docs/plugins/{deep_dive_file}](plugins/{deep_dive_file})"
            )
        else:
            lines.append(
                f"**Deep Dive**: [docs/plugins/{deep_dive_file}](plugins/{deep_dive_file})"
            )

        lines.append("")
        lines.append(svc['overview'])
        lines.append("")

    lines.extend([
        "## Summary",
        "",
        f"- **Services in layer**: {service_count}",
        f"- **Endpoints in layer**: {total_endpoints}",
        "",
        "---",
        "",
        "*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for architectural context.*",
        "",
    ])

    return '\n'.join(lines)


def main():
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schemas_dir = repo_root / 'schemas'
    plugins_dir = repo_root / 'docs' / 'plugins'

    # Deep dives are the primary source of truth
    services = scan_deep_dives(plugins_dir)

    if not services:
        print("Warning: No deep dive documents found")

    # Schemas provide version and endpoint counts
    schemas = scan_api_schemas(schemas_dir)

    # Generate combined markdown (backward compatibility)
    markdown = generate_markdown(services, schemas)

    total_endpoints = sum(s['endpoint_count'] for s in schemas.values() if s.get('endpoint_count'))

    # Write combined output
    output_file = repo_root / 'docs' / 'GENERATED-SERVICE-DETAILS.md'
    with open(output_file, 'w') as f:
        f.write(markdown)

    print(f"Generated {output_file} with {len(services)} services and {total_endpoints} endpoints")

    # Generate per-layer files
    for layer, filename in LAYER_FILES.items():
        layer_services = {k: v for k, v in services.items() if v.get('layer') == layer}
        if not layer_services:
            continue

        layer_markdown = generate_layer_markdown(layer, services, schemas)
        layer_file = repo_root / 'docs' / filename
        with open(layer_file, 'w') as f:
            f.write(layer_markdown)

        layer_endpoints = sum(
            schemas[k]['endpoint_count']
            for k in layer_services
            if k in schemas and schemas[k].get('endpoint_count')
        )
        print(f"Generated {layer_file} with {len(layer_services)} services and {layer_endpoints} endpoints")


if __name__ == '__main__':
    main()
