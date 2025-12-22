#!/usr/bin/env python3
"""
Extract x-service-configuration from -api.yaml files and create separate -configuration.yaml files.
Then remove x-service-configuration from the -api.yaml files.

This is a one-time migration script.
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


def extract_service_name(api_file: Path) -> str:
    """Extract service name from API file path."""
    return api_file.stem.replace('-api', '')


def create_configuration_file(service_name: str, config_section: dict, output_path: Path):
    """Create a configuration yaml file."""
    # Convert service name to title
    title_parts = service_name.replace('-', ' ').title().split()
    service_title = ' '.join(title_parts)

    doc = {
        'openapi': '3.0.3',
        'info': {
            'title': f'{service_title} Service Configuration',
            'description': f'Configuration schema for {service_title} service.\nProperties are bound to environment variables with BANNOU_ prefix.',
            'version': '1.0.0'
        },
        'paths': {},  # Configuration doesn't have HTTP paths
        'x-service-configuration': config_section
    }

    with open(output_path, 'w') as f:
        yaml.dump(doc, f)

    return True


def create_minimal_configuration(service_name: str, output_path: Path):
    """Create a minimal configuration file for services without x-service-configuration."""
    service_upper = service_name.upper().replace('-', '_')
    title_parts = service_name.replace('-', ' ').title().split()
    service_title = ' '.join(title_parts)

    config_section = {
        'properties': {
            'Enabled': {
                'type': 'boolean',
                'description': f'Enable/disable {service_title} service',
                'default': True
            }
        }
    }

    return create_configuration_file(service_name, config_section, output_path)


def main():
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    print("Extracting service configurations...")
    print()

    # Get all API files
    api_files = sorted(schema_dir.glob('*-api.yaml'))

    extracted = []
    created_minimal = []
    errors = []

    for api_file in api_files:
        service_name = extract_service_name(api_file)
        config_file = schema_dir / f'{service_name}-configuration.yaml'

        try:
            with open(api_file) as f:
                schema = yaml.load(f)

            if schema and 'x-service-configuration' in schema:
                # Extract and save to separate file
                config_section = schema['x-service-configuration']
                create_configuration_file(service_name, config_section, config_file)
                extracted.append(service_name)
                print(f"  ✅ Extracted: {service_name}-configuration.yaml")

                # Remove from API file
                del schema['x-service-configuration']
                with open(api_file, 'w') as f:
                    yaml.dump(schema, f)
                print(f"     Removed x-service-configuration from {service_name}-api.yaml")
            else:
                # Create minimal configuration
                create_minimal_configuration(service_name, config_file)
                created_minimal.append(service_name)
                print(f"  📝 Created minimal: {service_name}-configuration.yaml")

        except Exception as e:
            errors.append(f"{api_file.name}: {e}")

    print()
    if errors:
        print("Errors:")
        for error in errors:
            print(f"  {error}")
        sys.exit(1)

    print(f"Summary:")
    print(f"  Extracted from API: {len(extracted)}")
    print(f"  Created minimal: {len(created_minimal)}")
    print(f"  Total: {len(extracted) + len(created_minimal)}")


if __name__ == '__main__':
    main()
