#!/usr/bin/env python3
"""
Generate lifecycle events (Created, Updated, Deleted) from x-lifecycle definitions.

This script reads x-lifecycle definitions from OpenAPI API schemas and generates
separate lifecycle event schema files. The API schemas are NEVER modified.

Architecture:
- {service}-api.yaml: API definitions + x-lifecycle (SOURCE OF TRUTH, read-only)
- {service}-events.yaml: Custom service events (manually maintained, never touched)
- {service}-lifecycle-events.yaml: Auto-generated lifecycle events (completely overwritten)

Event Pattern:
- All events have: eventId (uuid), timestamp (datetime)
- All events have: full model data (sensitive fields omitted)
- UpdatedEvent adds: changedFields (string[])
- DeletedEvent adds: deletedReason (string?)

Topic Pattern:
- {entity}.created (e.g., account.created)
- {entity}.updated (e.g., account.updated)
- {entity}.deleted (e.g., account.deleted)

Usage:
    python3 scripts/generate-lifecycle-events.py

The script processes all *-api.yaml files in the schemas/ directory.
"""

import sys
import re
from pathlib import Path
from typing import Any, Dict, List, Tuple

# Use ruamel.yaml to preserve formatting and comments
try:
    from ruamel.yaml import YAML
    yaml = YAML()
    yaml.preserve_quotes = True
    yaml.width = 120
    yaml.default_flow_style = False
except ImportError:
    print("ERROR: ruamel.yaml is required. Install with: pip install ruamel.yaml")
    sys.exit(1)


def to_kebab_case(name: str) -> str:
    """Convert PascalCase to kebab-case for topic names."""
    s1 = re.sub('(.)([A-Z][a-z]+)', r'\1-\2', name)
    return re.sub('([a-z0-9])([A-Z])', r'\1-\2', s1).lower()


def extract_service_name(api_file: Path) -> str:
    """Extract service name from API file path (e.g., 'accounts' from 'accounts-api.yaml')."""
    return api_file.stem.replace('-api', '')


def read_lifecycle_definitions(api_file: Path) -> Tuple[Dict[str, Any], str]:
    """
    Read x-lifecycle definitions from an API schema file.

    Returns:
        Tuple of (lifecycle_defs dict, service title from info section)
        Returns (None, None) if no x-lifecycle found
    """
    with open(api_file) as f:
        content = f.read()

    # Quick check before parsing
    if 'x-lifecycle:' not in content:
        return None, None

    with open(api_file) as f:
        schema = yaml.load(f)

    if schema is None or 'x-lifecycle' not in schema:
        return None, None

    # Extract service title for the generated file
    title = schema.get('info', {}).get('title', 'Unknown Service')

    return schema['x-lifecycle'], title


def generate_events(entity: str, config: dict) -> Dict[str, Any]:
    """
    Generate Created, Updated, Deleted event schemas for an entity.

    Args:
        entity: Entity name in PascalCase (e.g., "Account")
        config: Configuration dict with 'model' and optional 'sensitive' keys

    Returns:
        Dict with three event schema definitions
    """
    model = config.get('model', {})
    sensitive = set(config.get('sensitive', []))

    # Find primary key (field marked with primary: true)
    primary_key = None
    for field, defn in model.items():
        if isinstance(defn, dict) and defn.get('primary'):
            primary_key = field
            break

    if not primary_key:
        raise ValueError(f"No primary key defined for {entity}. Mark one field with 'primary: true'")

    # Generate topic name (kebab-case)
    topic_base = to_kebab_case(entity)

    # Build base event properties (all events have these)
    base_props = {
        'eventId': {
            'type': 'string',
            'format': 'uuid',
            'description': 'Unique identifier for this event'
        },
        'timestamp': {
            'type': 'string',
            'format': 'date-time',
            'description': 'When this event occurred'
        },
    }

    # Build model properties (excluding sensitive fields)
    model_props = {}
    required_fields = ['eventId', 'timestamp']

    for field, defn in model.items():
        if field in sensitive:
            continue

        # Handle dict definitions vs $ref strings
        if isinstance(defn, dict):
            # Copy definition without our custom markers
            prop = {k: v for k, v in defn.items() if k not in ('required', 'primary')}
            if defn.get('required'):
                required_fields.append(field)
        else:
            # It's a $ref or simple type
            prop = defn

        model_props[field] = prop

    # Ensure primary key is required
    if primary_key not in required_fields:
        required_fields.append(primary_key)

    # Build all properties (base + model)
    all_props = {**base_props, **model_props}

    # CreatedEvent
    created = {
        'type': 'object',
        'description': f'Published to {topic_base}.created when a {entity.lower()} is created',
        'required': required_fields.copy(),
        'properties': dict(all_props)
    }

    # UpdatedEvent - adds changedFields
    updated_required = required_fields.copy()
    updated_required.append('changedFields')
    updated_props = dict(all_props)
    updated_props['changedFields'] = {
        'type': 'array',
        'items': {'type': 'string'},
        'description': 'List of field names that were modified'
    }
    updated = {
        'type': 'object',
        'description': f'Published to {topic_base}.updated when a {entity.lower()} is updated',
        'required': updated_required,
        'properties': updated_props
    }

    # DeletedEvent - adds deletedReason
    deleted_props = dict(all_props)
    deleted_props['deletedReason'] = {
        'type': 'string',
        'nullable': True,
        'description': 'Optional reason for deletion (e.g., "Merged into {targetId}")'
    }
    deleted = {
        'type': 'object',
        'description': f'Published to {topic_base}.deleted when a {entity.lower()} is deleted',
        'required': required_fields.copy(),
        'properties': deleted_props
    }

    return {
        f'{entity}CreatedEvent': created,
        f'{entity}UpdatedEvent': updated,
        f'{entity}DeletedEvent': deleted,
    }


def generate_lifecycle_events_file(
    service_name: str,
    service_title: str,
    lifecycle_defs: Dict[str, Any],
    output_path: Path
) -> List[str]:
    """
    Generate a complete lifecycle events schema file.

    Args:
        service_name: Service name (e.g., "accounts")
        service_title: Human-readable service title
        lifecycle_defs: x-lifecycle definitions from API schema
        output_path: Path to write the generated file

    Returns:
        List of entity names that had events generated
    """
    # Build all event schemas
    all_schemas = {}
    generated_entities = []

    for entity_name, config in lifecycle_defs.items():
        events = generate_events(entity_name, config)
        all_schemas.update(events)
        generated_entities.append(entity_name)

    # Build the complete OpenAPI document
    doc = {
        'openapi': '3.0.3',
        'info': {
            'title': f'{service_title} Lifecycle Events',
            'description': (
                f'Auto-generated lifecycle event schemas for {service_name} service.\n'
                f'DO NOT EDIT - This file is completely overwritten by generate-lifecycle-events.py.\n'
                f'Source of truth: {service_name}-api.yaml x-lifecycle section.'
            ),
            'version': '1.0.0'
        },
        'paths': {},  # Events don't have HTTP paths
        'components': {
            'schemas': all_schemas
        }
    }

    # Write the file (completely overwriting)
    with open(output_path, 'w') as f:
        yaml.dump(doc, f)

    return generated_entities


def main():
    """Process all API schema files and generate lifecycle event files."""
    # Find schemas directory relative to script location
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    print("Generating lifecycle events from x-lifecycle definitions...")
    print("  Reading from: *-api.yaml (source of truth)")
    print("  Writing to: *-lifecycle-events.yaml (completely overwritten)")
    print()

    generated_files = []
    errors = []

    for api_file in sorted(schema_dir.glob('*-api.yaml')):
        try:
            lifecycle_defs, service_title = read_lifecycle_definitions(api_file)

            if lifecycle_defs is None:
                continue

            service_name = extract_service_name(api_file)
            output_file = schema_dir / f'{service_name}-lifecycle-events.yaml'

            entities = generate_lifecycle_events_file(
                service_name,
                service_title,
                lifecycle_defs,
                output_file
            )

            generated_files.append((output_file.name, entities))
            print(f"  {output_file.name}: Generated events for {', '.join(entities)}")

        except Exception as e:
            errors.append(f"{api_file.name}: {e}")

    # Report results
    print()
    if errors:
        print("Errors:")
        for error in errors:
            print(f"  {error}")
        sys.exit(1)

    if generated_files:
        print(f"Generated {len(generated_files)} lifecycle event file(s)")
        print("Note: API schemas were NOT modified (read-only)")
    else:
        print("No x-lifecycle definitions found in any API schemas")


if __name__ == '__main__':
    main()
