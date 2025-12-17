#!/usr/bin/env python3
"""
Generate lifecycle events (Created, Updated, Deleted) from x-lifecycle definitions.

This script processes OpenAPI schema files and expands x-lifecycle shorthand
into full event schema definitions, ensuring consistent event patterns across
all Bannou services.

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
from typing import Any

# Use ruamel.yaml to preserve formatting and comments
try:
    from ruamel.yaml import YAML
    yaml = YAML()
    yaml.preserve_quotes = True
    yaml.width = 120
except ImportError:
    print("ERROR: ruamel.yaml is required. Install with: pip install ruamel.yaml")
    sys.exit(1)


def to_kebab_case(name: str) -> str:
    """Convert PascalCase to kebab-case for topic names."""
    # Insert hyphen before uppercase letters and convert to lowercase
    s1 = re.sub('(.)([A-Z][a-z]+)', r'\1-\2', name)
    return re.sub('([a-z0-9])([A-Z])', r'\1-\2', s1).lower()


def process_schema_file(path: Path) -> bool:
    """
    Process a single schema file, expanding x-lifecycle definitions.

    Returns True if the file was modified.
    """
    with open(path) as f:
        content = f.read()

    # Quick check before parsing
    if 'x-lifecycle:' not in content:
        return False

    with open(path) as f:
        schema = yaml.load(f)

    if schema is None or 'x-lifecycle' not in schema:
        return False

    lifecycle_defs = schema.pop('x-lifecycle')

    # Ensure components.schemas exists
    if 'components' not in schema:
        schema['components'] = {}
    if 'schemas' not in schema['components']:
        schema['components']['schemas'] = {}

    # Track generated events for reporting
    generated = []

    # Generate events for each entity
    for entity_name, config in lifecycle_defs.items():
        events = generate_events(entity_name, config)
        schema['components']['schemas'].update(events)
        generated.append(entity_name)

    # Write back
    with open(path, 'w') as f:
        yaml.dump(schema, f)

    if generated:
        print(f"  {path.name}: Generated events for {', '.join(generated)}")

    return True


def generate_events(entity: str, config: dict) -> dict:
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
        'properties': dict(all_props)  # Copy to avoid mutation
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


def main():
    """Process all schema files in the schemas/ directory."""
    # Find schemas directory relative to script location
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    print("Generating lifecycle events from x-lifecycle definitions...")

    modified = []
    errors = []

    for schema_file in sorted(schema_dir.glob('*-api.yaml')):
        try:
            if process_schema_file(schema_file):
                modified.append(schema_file.name)
        except Exception as e:
            errors.append(f"{schema_file.name}: {e}")

    # Report results
    if errors:
        print("\nErrors:")
        for error in errors:
            print(f"  {error}")
        sys.exit(1)

    if modified:
        print(f"\nProcessed {len(modified)} file(s)")
    else:
        print("No x-lifecycle definitions found")


if __name__ == '__main__':
    main()
