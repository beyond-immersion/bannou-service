#!/usr/bin/env python3
"""
Generate Service Details Documentation from API Schema Files.

This script scans *-api.yaml schema files and generates a compact reference
document with service descriptions and API endpoints for each service.

Output: docs/GENERATED-SERVICE-DETAILS.md

Usage:
    python3 scripts/generate-service-details-docs.py
"""

import sys
from pathlib import Path
from collections import defaultdict

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


def get_method_badge(method: str) -> str:
    """Get a compact method indicator."""
    return method.upper()


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


def generate_markdown(services: dict) -> str:
    """Generate markdown documentation from services."""
    lines = [
        "# Generated Service Details Reference",
        "",
        "> **Source**: `schemas/*-api.yaml`",
        "> **Do not edit manually** - regenerate with `make generate-docs`",
        "",
        "This document provides a compact reference of all Bannou services and their API endpoints.",
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
        lines.append(f"**Version**: {svc['version']} | **Schema**: `schemas/{svc['source_file']}`")
        lines.append("")

        if svc['description']:
            lines.append(svc['description'])
            lines.append("")

        if svc['endpoints']:
            # Group endpoints by tag
            by_tag = defaultdict(list)
            for ep in svc['endpoints']:
                tag = ep['tags'][0] if ep['tags'] else 'Other'
                by_tag[tag].append(ep)

            for tag in sorted(by_tag.keys()):
                eps = by_tag[tag]
                lines.append(f"### {tag}")
                lines.append("")
                lines.append("| Method | Path | Summary | Access |")
                lines.append("|--------|------|---------|--------|")

                for ep in sorted(eps, key=lambda x: x['path']):
                    deprecated_marker = " ⚠️" if ep['deprecated'] else ""
                    roles_str = ', '.join(ep['roles']) if ep['roles'] else 'authenticated'
                    lines.append(f"| `{ep['method']}` | `{ep['path']}` | {ep['summary']}{deprecated_marker} | {roles_str} |")

                lines.append("")
        else:
            lines.append("*No endpoints defined*")
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

    # Scan API schemas
    services = scan_api_schemas(schemas_dir)

    total_endpoints = sum(len(svc['endpoints']) for svc in services.values())

    if not services:
        print("Warning: No API schemas found")

    # Generate markdown
    markdown = generate_markdown(services)

    # Write output
    output_file = repo_root / 'docs' / 'GENERATED-SERVICE-DETAILS.md'
    with open(output_file, 'w') as f:
        f.write(markdown)

    print(f"Generated {output_file} with {len(services)} services and {total_endpoints} endpoints")


if __name__ == '__main__':
    main()
