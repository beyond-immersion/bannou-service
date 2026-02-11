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
Generate Service Details Documentation from API Schema Files.

This script scans *-api.yaml schema files and generates a compact reference
document with service descriptions and endpoint counts. If a deep dive
document exists (docs/plugins/{SERVICE}.md), its ## Overview section is
used in place of the schema's one-line description.

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


def to_title_case(name: str) -> str:
    """Convert kebab-case service name to Title Case."""
    parts = name.split('-')
    return ' '.join(p.capitalize() for p in parts)


def extract_service_name(filename: str) -> str:
    """Extract service name from API schema filename."""
    # auth-api.yaml -> auth
    name = filename.replace('-api.yaml', '')
    return name


def scan_api_schemas(schemas_dir: Path) -> dict:
    """Scan all API schema files and extract service info."""
    services = {}

    if not schemas_dir.exists():
        return services

    # Find all *-api.yaml files
    for yaml_file in sorted(schemas_dir.glob('*-api.yaml')):
        try:
            with open(yaml_file) as f:
                content = yaml.load(f)

            if content is None:
                continue

            service_key = extract_service_name(yaml_file.name)
            service_display = to_title_case(service_key)

            # Extract service info
            info = content.get('info', {})
            title = info.get('title', f'{service_display} Service')
            description = info.get('description', '')
            version = info.get('version', '1.0.0')

            # Clean up description - take first paragraph
            if description:
                # Split on double newline and take first part
                first_para = description.split('\n\n')[0]
                # Remove markdown formatting
                first_para = first_para.replace('**', '').replace('`', '')
                # Truncate if too long
                if len(first_para) > 200:
                    first_para = first_para[:197] + '...'
                description = first_para.strip()

            # Extract endpoints
            endpoints = []
            paths = content.get('paths', {})
            for path, methods in paths.items():
                if not isinstance(methods, dict):
                    continue
                for method, details in methods.items():
                    if method.startswith('x-') or not isinstance(details, dict):
                        continue

                    summary = details.get('summary', '')
                    operation_id = details.get('operationId', '')
                    tags = details.get('tags', [])
                    deprecated = details.get('deprecated', False)

                    # Get permission info
                    permissions = details.get('x-permissions', [])
                    roles = []
                    for perm in permissions:
                        if isinstance(perm, dict):
                            role = perm.get('role', '')
                            if role:
                                roles.append(role)

                    endpoints.append({
                        'method': method.upper(),
                        'path': path,
                        'summary': summary,
                        'operation_id': operation_id,
                        'tags': tags,
                        'deprecated': deprecated,
                        'roles': roles,
                    })

            services[service_key] = {
                'display_name': service_display,
                'title': title,
                'description': description,
                'version': version,
                'endpoints': endpoints,
                'source_file': yaml_file.name,
            }

        except Exception as e:
            print(f"Warning: Failed to parse {yaml_file}: {e}")

    return services


def extract_deep_dive_overview(docs_dir: Path, service_key: str) -> str:
    """Extract the ## Overview section from a deep dive document if it exists."""
    deep_dive_file = docs_dir / 'plugins' / (service_key.upper() + '.md')
    if not deep_dive_file.exists():
        return ''

    try:
        content = deep_dive_file.read_text()
    except Exception:
        return ''

    # Find the ## Overview section
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


def generate_markdown(services: dict, docs_dir: Path) -> str:
    """Generate markdown documentation from services."""
    lines = [
        "# Generated Service Details Reference",
        "",
        "> **Source**: `schemas/*-api.yaml`",
        "> **Do not edit manually** - regenerate with `make generate-docs`",
        "",
        "This document provides a compact reference of all Bannou services.",
        "",
        "## Service Overview",
        "",
        "| Service | Version | Endpoints | Description |",
        "|---------|---------|-----------|-------------|",
    ]

    # Overview table
    for service_key in sorted(services.keys()):
        svc = services[service_key]
        endpoint_count = len(svc['endpoints'])
        desc_short = svc['description'][:60] + '...' if len(svc['description']) > 60 else svc['description']
        lines.append(f"| [{svc['display_name']}](#{service_key}) | {svc['version']} | {endpoint_count} | {desc_short} |")

    lines.append("")
    lines.append("---")
    lines.append("")

    # Detailed sections for each service
    total_endpoints = 0
    for service_key in sorted(services.keys()):
        svc = services[service_key]
        total_endpoints += len(svc['endpoints'])

        lines.append(f"## {svc['display_name']} {{#{service_key}}}")
        lines.append("")
        deep_dive_file = service_key.upper() + '.md'
        lines.append(f"**Version**: {svc['version']} | **Schema**: `schemas/{svc['source_file']}` | **Deep Dive**: [docs/plugins/{deep_dive_file}](plugins/{deep_dive_file})")
        lines.append("")

        # Use deep dive overview if available, otherwise fall back to schema description
        overview = extract_deep_dive_overview(docs_dir, service_key)
        if overview:
            lines.append(overview)
        elif svc['description']:
            lines.append(svc['description'])

        lines.append("")
        lines.append("---")
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


def main():
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schemas_dir = repo_root / 'schemas'
    docs_dir = repo_root / 'docs'

    # Scan API schemas
    services = scan_api_schemas(schemas_dir)

    total_endpoints = sum(len(svc['endpoints']) for svc in services.values())

    if not services:
        print("Warning: No API schemas found")

    # Generate markdown
    markdown = generate_markdown(services, docs_dir)

    # Write output
    output_file = repo_root / 'docs' / 'GENERATED-SERVICE-DETAILS.md'
    with open(output_file, 'w') as f:
        f.write(markdown)

    print(f"Generated {output_file} with {len(services)} services and {total_endpoints} endpoints")


if __name__ == '__main__':
    main()
