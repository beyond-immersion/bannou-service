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
Generate Configuration Documentation from Schema Files.

This script scans *-configuration.yaml schema files and generates comprehensive
markdown documentation for all configuration options used in Bannou services.

Output: docs/GENERATED-CONFIGURATION.md

Usage:
    python3 scripts/generate-config-docs.py
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
    """Extract service name from configuration schema filename."""
    # account-configuration.yaml -> account
    name = filename.replace('-configuration.yaml', '')
    return name


def get_type_display(prop_type: str) -> str:
    """Get display-friendly type name."""
    type_map = {
        'string': 'string',
        'integer': 'int',
        'number': 'double',
        'boolean': 'bool',
        'array': 'string[]'
    }
    return type_map.get(prop_type, prop_type)


def scan_config_schemas(schemas_dir: Path) -> dict:
    """Scan all configuration schema files and extract property definitions."""
    config_by_service = defaultdict(list)

    if not schemas_dir.exists():
        return config_by_service

    # Find all *-configuration.yaml files
    for yaml_file in sorted(schemas_dir.glob('*-configuration.yaml')):
        try:
            with open(yaml_file) as f:
                content = yaml.load(f)

            if content is None:
                continue

            service = extract_service_name(yaml_file.name)
            service_display = to_title_case(service)

            # Extract properties from x-service-configuration
            config_section = content.get('x-service-configuration', {})

            # Handle both direct properties and nested 'properties' key
            properties = config_section.get('properties', config_section)

            for prop_name, prop_info in properties.items():
                if not isinstance(prop_info, dict):
                    continue

                prop_type = prop_info.get('type', 'string')
                prop_default = prop_info.get('default')
                prop_description = prop_info.get('description', '')
                prop_env = prop_info.get('env', f'{service.upper()}_{prop_name.upper()}')

                # Determine if required (no default = required)
                is_required = prop_default is None and prop_type != 'boolean'

                # Format default value for display
                if prop_default is None:
                    default_display = '**REQUIRED**' if is_required else '-'
                elif isinstance(prop_default, str):
                    if 'guest' in prop_default.lower() or 'secret' in prop_default.lower():
                        default_display = f'`{prop_default}` (insecure)'
                    else:
                        default_display = f'`{prop_default}`'
                elif isinstance(prop_default, bool):
                    default_display = f'`{str(prop_default).lower()}`'
                elif isinstance(prop_default, list):
                    default_display = f'`{prop_default}`'
                else:
                    default_display = f'`{prop_default}`'

                config_by_service[service_display].append({
                    'property': prop_name,
                    'env_var': prop_env,
                    'type': get_type_display(prop_type),
                    'default': default_display,
                    'required': is_required,
                    'description': prop_description[:60] + '...' if len(prop_description) > 60 else prop_description,
                    'source_file': yaml_file.name,
                })

        except Exception as e:
            print(f"Warning: Failed to parse {yaml_file}: {e}")

    return config_by_service


def generate_markdown(config_by_service: dict) -> str:
    """Generate markdown documentation from configuration."""
    lines = [
        "# Generated Configuration Reference",
        "",
        "> **Source**: `schemas/*-configuration.yaml`",
        "> **Do not edit manually** - regenerate with `make generate-docs`",
        "",
        "This document lists all configuration options defined in Bannou's configuration schemas.",
        "",
        "## Configuration by Service",
        "",
    ]

    total_required = 0
    total_optional = 0

    # Sort services alphabetically
    for service in sorted(config_by_service.keys()):
        props = config_by_service[service]
        if not props:
            continue

        lines.append(f"### {service}")
        lines.append("")
        lines.append("| Environment Variable | Type | Default | Description |")
        lines.append("|---------------------|------|---------|-------------|")

        # Sort properties by env var name
        for prop in sorted(props, key=lambda x: x['env_var']):
            if prop['required']:
                total_required += 1
            else:
                total_optional += 1

            lines.append(
                f"| `{prop['env_var']}` | {prop['type']} | {prop['default']} | {prop['description']} |"
            )

        lines.append("")

    # Add summary section
    lines.extend([
        "## Configuration Summary",
        "",
        f"- **Total properties**: {total_required + total_optional}",
        f"- **Required (no default)**: {total_required}",
        f"- **Optional (has default)**: {total_optional}",
        "",
    ])

    # Add naming convention section
    lines.extend([
        "## Environment Variable Naming Convention",
        "",
        "Per FOUNDATION TENETS, all configuration environment variables follow `{SERVICE}_{PROPERTY}` pattern:",
        "",
        "```bash",
        "# Service prefix in UPPER_CASE",
        "AUTH_JWT_SECRET=your-secret",
        "CONNECT_MAX_CONNECTIONS=1000",
        "BEHAVIOR_CACHE_TTL_SECONDS=300",
        "```",
        "",
        "The BANNOU_ prefix is also supported for backwards compatibility:",
        "",
        "```bash",
        "# Also valid (BANNOU_ prefix)",
        "BANNOU_AUTH_JWT_SECRET=your-secret",
        "```",
        "",
    ])

    # Add fail-fast section
    lines.extend([
        "## Required Configuration (Fail-Fast)",
        "",
        "Per IMPLEMENTATION TENETS, configuration marked as **REQUIRED** will cause the service to",
        "throw an exception at startup if not configured. This prevents running with",
        "insecure defaults or missing critical configuration.",
        "",
        "```csharp",
        "// Example of fail-fast pattern",
        "var secret = config.JwtSecret",
        "    ?? throw new InvalidOperationException(\"AUTH_JWT_SECRET required\");",
        "```",
        "",
    ])

    # Add .env file section
    lines.extend([
        "## .env File Configuration",
        "",
        "Create a `.env` file in the repository root with your configuration:",
        "",
        "```bash",
        "# Required configuration (no defaults)",
        "AUTH_JWT_SECRET=your-production-secret",
        "CONNECT_RABBITMQ_CONNECTION_STRING=amqp://user:pass@host:5672",
        "",
        "# Optional configuration (has defaults)",
        "AUTH_JWT_EXPIRATION_MINUTES=60",
        "CONNECT_MAX_CONNECTIONS=1000",
        "```",
        "",
        "See `.env.example` for a complete template.",
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

    # Scan configuration schemas
    config_by_service = scan_config_schemas(schemas_dir)

    total_props = sum(len(props) for props in config_by_service.values())

    if total_props == 0:
        print("Warning: No configuration properties found in schemas")

    # Generate markdown
    markdown = generate_markdown(config_by_service)

    # Write output
    output_file = repo_root / 'docs' / 'GENERATED-CONFIGURATION.md'
    with open(output_file, 'w') as f:
        f.write(markdown)

    print(f"Generated {output_file} with {total_props} properties across {len(config_by_service)} services")


if __name__ == '__main__':
    main()
