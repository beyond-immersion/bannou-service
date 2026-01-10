#!/usr/bin/env python3
"""
Generate .env.example from Configuration Schema Files.

This script scans *-configuration.yaml schema files and generates a complete
.env.example file with all configuration options, using 'example' values where
available, falling back to 'default' values.

Output: .env.example

Usage:
    python3 scripts/generate-env-example.py

Schema Support:
    Properties can define both 'default' (used in code) and 'example' (used in .env.example):

    JwtSecret:
      type: string
      env: AUTH_JWT_SECRET
      default: ""                    # Actual code default
      example: "your-secret-key"     # Value for .env.example
      description: JWT signing secret
"""

import sys
from pathlib import Path
from collections import defaultdict

# Use ruamel.yaml to parse YAML files (preserves comments)
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


def get_env_prefix(service_name: str) -> str:
    """Get the environment variable prefix for a service."""
    # game-session -> GAME_SESSION
    return service_name.upper().replace('-', '_')


def format_value(value, prop_type: str, prop_name: str, is_secret: bool = False) -> str:
    """Format a value for .env file output."""
    if value is None:
        # Required field with no default/example - use placeholder
        if is_secret:
            return "your-secret-here"
        return ""

    if isinstance(value, bool):
        return str(value).lower()

    if isinstance(value, (int, float)):
        return str(value)

    if isinstance(value, list):
        return ','.join(str(v) for v in value)

    # String value
    return str(value)


def is_secret_property(prop_name: str, env_var: str) -> bool:
    """Determine if a property is likely a secret/credential."""
    secret_keywords = ['secret', 'password', 'key', 'token', 'credential', 'api_key']
    name_lower = prop_name.lower()
    env_lower = env_var.lower()

    for keyword in secret_keywords:
        if keyword in name_lower or keyword in env_lower:
            return True
    return False


def get_example_value(prop_info: dict, prop_name: str, env_var: str) -> str:
    """Get the example value for a property, with smart defaults."""
    prop_type = prop_info.get('type', 'string')
    is_secret = is_secret_property(prop_name, env_var)

    # Priority: example > default > smart placeholder
    if 'example' in prop_info:
        return format_value(prop_info['example'], prop_type, prop_name, is_secret)

    if 'default' in prop_info:
        return format_value(prop_info['default'], prop_type, prop_name, is_secret)

    # Generate smart placeholder for required fields
    if is_secret:
        if 'jwt' in prop_name.lower():
            return "your-jwt-secret-minimum-32-characters"
        if 'client_secret' in env_var.lower():
            return "your-client-secret"
        if 'api_key' in env_var.lower():
            return "your-api-key"
        return "your-secret-here"

    # URL patterns
    if 'url' in prop_name.lower() or 'uri' in prop_name.lower():
        if 'redirect' in prop_name.lower():
            return "https://your-domain.com/callback"
        if 'connect' in prop_name.lower():
            return "ws://localhost:5014/connect"
        return "https://your-domain.com"

    # Connection strings
    if 'connection' in prop_name.lower() and 'string' in prop_name.lower():
        if 'rabbit' in env_var.lower():
            return "amqp://guest:guest@localhost:5672"
        if 'redis' in env_var.lower():
            return "localhost:6379"
        return "your-connection-string"

    # Email patterns
    if 'email' in prop_name.lower():
        return "admin@example.com"

    # Domain patterns
    if 'domain' in prop_name.lower():
        return "example.com"

    # Client ID patterns
    if 'client_id' in env_var.lower():
        return "your-client-id"

    # Empty string for other required fields
    return ""


def scan_config_schemas(schemas_dir: Path) -> dict:
    """Scan all configuration schema files and extract property definitions."""
    config_by_service = {}

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
            env_prefix = get_env_prefix(service)

            # Extract properties from x-service-configuration
            config_section = content.get('x-service-configuration', {})

            # Handle both direct properties and nested 'properties' key
            properties = config_section.get('properties', config_section)

            service_props = []
            current_group = None

            for prop_name, prop_info in properties.items():
                if not isinstance(prop_info, dict):
                    continue

                env_var = prop_info.get('env', f'{env_prefix}_{prop_name.upper()}')
                description = prop_info.get('description', '')
                example_value = get_example_value(prop_info, prop_name, env_var)

                # Extract group from YAML comments if available
                # (ruamel.yaml preserves comments, but accessing them is complex)
                # For now, we'll use description patterns or property name patterns
                group = None
                if 'jwt' in prop_name.lower():
                    group = 'JWT Configuration'
                elif 'oauth' in prop_name.lower() or 'discord' in prop_name.lower() or 'google' in prop_name.lower() or 'twitch' in prop_name.lower() or 'steam' in prop_name.lower():
                    group = 'OAuth Configuration'
                elif 'mock' in prop_name.lower():
                    group = 'Mock Providers (Development)'
                elif 'statestore' in prop_name.lower() or 'state_store' in env_var.lower():
                    group = 'State Store Configuration'
                elif 'key_prefix' in env_var.lower() or 'keyprefix' in prop_name.lower():
                    group = 'Key Prefixes'
                elif 'lock' in prop_name.lower():
                    group = 'Distributed Lock Configuration'
                elif 'ttl' in prop_name.lower() or 'timeout' in prop_name.lower() or 'expir' in prop_name.lower():
                    group = 'Timing/TTL Configuration'

                service_props.append({
                    'env_var': env_var,
                    'value': example_value,
                    'description': description,
                    'group': group,
                    'has_default': 'default' in prop_info,
                    'has_example': 'example' in prop_info,
                })

            if service_props:
                config_by_service[service] = {
                    'display_name': service_display,
                    'env_prefix': env_prefix,
                    'properties': service_props,
                }

        except Exception as e:
            print(f"Warning: Failed to parse {yaml_file}: {e}")

    return config_by_service


def generate_env_example(config_by_service: dict) -> str:
    """Generate .env.example content from configuration."""
    lines = [
        "# Bannou Service Configuration Template",
        "# Copy this file to .env and update with your actual values",
        "#",
        "# This file is AUTO-GENERATED from schemas/*-configuration.yaml",
        "# Do not edit manually - regenerate with: python3 scripts/generate-env-example.py",
        "#",
        "# Environment variable naming follows FOUNDATION TENETS:",
        "#   - Service prefix: {SERVICE}_ (e.g., AUTH_, CONNECT_, ACCOUNT_)",
        "#   - Property names: UPPER_SNAKE_CASE with underscores between words",
        "#   - Example: AUTH_JWT_SECRET -> JwtSecret property in AuthServiceConfiguration",
        "#",
        "",
    ]

    # Add manual sections first (not from schemas)
    lines.extend([
        "# =============================================================================",
        "# Database Configuration (Third-party Infrastructure)",
        "# =============================================================================",
        "ACCOUNT_DB_USER=your_db_user",
        "ACCOUNT_DB_PASSWORD=your_db_password",
        "",
    ])

    # Define service order (important services first, then alphabetical)
    priority_services = ['auth', 'account', 'connect', 'game-session', 'orchestrator', 'actor', 'behavior', 'permission']

    # Get all service names and sort
    all_services = list(config_by_service.keys())
    ordered_services = []

    # Add priority services first (if they exist)
    for svc in priority_services:
        if svc in all_services:
            ordered_services.append(svc)
            all_services.remove(svc)

    # Add remaining services alphabetically
    ordered_services.extend(sorted(all_services))

    # Generate sections for each service
    for service in ordered_services:
        info = config_by_service[service]
        display_name = info['display_name']
        env_prefix = info['env_prefix']
        props = info['properties']

        lines.append("# =============================================================================")
        lines.append(f"# {display_name} Service Configuration ({env_prefix}_*)")
        lines.append("# =============================================================================")

        # Group properties
        grouped_props = defaultdict(list)
        ungrouped_props = []

        for prop in props:
            if prop['group']:
                grouped_props[prop['group']].append(prop)
            else:
                ungrouped_props.append(prop)

        # Output ungrouped properties first
        for prop in ungrouped_props:
            if prop['description'] and len(prop['description']) < 80:
                lines.append(f"# {prop['description']}")
            lines.append(f"{prop['env_var']}={prop['value']}")

        # Output grouped properties
        for group_name in sorted(grouped_props.keys()):
            if ungrouped_props or list(grouped_props.keys()).index(group_name) > 0:
                lines.append("")
            lines.append(f"# {group_name}")
            for prop in grouped_props[group_name]:
                lines.append(f"{prop['env_var']}={prop['value']}")

        lines.append("")

    # Add service enable/disable flags section
    lines.extend([
        "# =============================================================================",
        "# Service Enable/Disable Flags (Loading Control)",
        "# These control which services are loaded at startup",
        "# =============================================================================",
        "SERVICES_ENABLED=true",
        "AUTH_SERVICE_ENABLED=true",
        "ACCOUNT_SERVICE_ENABLED=true",
        "PERMISSION_SERVICE_ENABLED=true",
        "CONNECT_SERVICE_ENABLED=true",
        "ORCHESTRATOR_SERVICE_ENABLED=true",
        "BEHAVIOR_SERVICE_ENABLED=true",
        "ACTOR_SERVICE_ENABLED=true",
        "# Add {SERVICE}_SERVICE_DISABLED=true to explicitly disable a service",
        "",
    ])

    # Add local development port configuration
    lines.extend([
        "# =============================================================================",
        "# Local Development Configuration",
        "# =============================================================================",
        "SERVICE_DOMAIN=localhost",
        "HTTP_WEB_HOST_PORT=8080",
        "HTTPS_WEB_HOST_PORT=8443",
        "",
    ])

    return '\n'.join(lines)


def main():
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schemas_dir = repo_root / 'schemas'

    print("Scanning configuration schemas...")
    config_by_service = scan_config_schemas(schemas_dir)

    total_props = sum(len(info['properties']) for info in config_by_service.values())

    if total_props == 0:
        print("Warning: No configuration properties found in schemas")
        return

    print(f"Found {total_props} properties across {len(config_by_service)} services")

    # Generate .env.example content
    env_content = generate_env_example(config_by_service)

    # Write output
    output_file = repo_root / '.env.example'

    with open(output_file, 'w') as f:
        f.write(env_content)

    print(f"Generated {output_file}")
    print(f"  - {len(config_by_service)} services")
    print(f"  - {total_props} configuration properties")


if __name__ == '__main__':
    main()
