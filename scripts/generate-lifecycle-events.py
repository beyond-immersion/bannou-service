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
Generate lifecycle events (Created, Updated, Deleted) from x-lifecycle definitions.

This script reads x-lifecycle definitions from OpenAPI event schemas and generates
separate lifecycle event schema files. Event schemas are NEVER modified.

Architecture:
- {service}-api.yaml: API definitions (no x-lifecycle, no events)
- {service}-events.yaml: Event definitions + x-lifecycle (SOURCE OF TRUTH, read-only)
- schemas/Generated/{service}-lifecycle-events.yaml: Auto-generated lifecycle events (completely overwritten)

Event Pattern:
- All events have: eventId (uuid), timestamp (datetime)
- All events have: full model data (sensitive fields omitted)
- UpdatedEvent adds: changedFields (string[])
- DeletedEvent adds: deletedReason (string?)

Topic Pattern:
- Without topic_prefix: {entity}.{action} (Pattern A, e.g., account.created)
- With topic_prefix: depends on entity name vs prefix:
  - entity equals prefix -> {entity}.{action} (Pattern A, e.g., seed.created)
  - entity starts with prefix -> {prefix}.{rest}.{action} (Pattern C, e.g., transit.connection.created)
  - otherwise -> {prefix}.{entity}.{action} (Pattern C, e.g., worldstate.calendar-template.created)

Resource Mapping (optional):
- If resource_mapping is defined, x-resource-mapping is emitted on generated events
- Used by Puppetmaster watch system for resource change subscriptions

Usage:
    python3 scripts/generate-lifecycle-events.py

The script processes all *-events.yaml files in the schemas/ directory.
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


def fix_relative_paths_for_generated(obj: Any) -> Any:
    """
    Fix relative paths in refs when the output file is in Generated/ subdirectory.

    All cross-file refs need '../' prefix because the output is in schemas/Generated/.
    This function normalizes refs - whether they already have '../' or not - to ensure
    consistent '../filename.yaml#/...' format in the output.

    Schema authors should write sibling-relative refs (e.g., 'actor-api.yaml#/...').
    This function adds the '../' needed for the Generated/ output location.
    """
    if isinstance(obj, dict):
        if '$ref' in obj:
            ref = obj['$ref']
            # Match refs to sibling files - with or without existing ../ prefix
            # Pattern 1: '../filename.yaml#/...' (already has ../)
            # Pattern 2: 'filename.yaml#/...' (no ../)
            # Pattern 3: './filename.yaml#/...' (explicit same-dir)
            # But NOT local refs like '#/components/schemas/TypeName'
            match = re.match(r"['\"]?(?:\.\.?/)?([a-zA-Z][a-zA-Z0-9_-]*\.yaml)#(.+)['\"]?", ref)
            if match:
                # Normalize to ../ prefix for Generated/ output location
                obj['$ref'] = f"../{match.group(1)}#{match.group(2)}"
        for key, value in obj.items():
            obj[key] = fix_relative_paths_for_generated(value)
    elif isinstance(obj, list):
        for i, item in enumerate(obj):
            obj[i] = fix_relative_paths_for_generated(item)

    return obj


def extract_service_name(events_file: Path) -> str:
    """Extract service name from events file path (e.g., 'account' from 'account-events.yaml')."""
    return events_file.stem.replace('-events', '')


def read_lifecycle_definitions(events_file: Path) -> Tuple[Dict[str, Any], str]:
    """
    Read x-lifecycle definitions from an events schema file.

    Returns:
        Tuple of (lifecycle_defs dict, service title from info section)
        Returns (None, None) if no x-lifecycle found
    """
    with open(events_file) as f:
        content = f.read()

    # Quick check before parsing
    if 'x-lifecycle:' not in content:
        return None, None

    with open(events_file) as f:
        schema = yaml.load(f)

    if schema is None or 'x-lifecycle' not in schema:
        return None, None

    # Extract service title for the generated file
    title = schema.get('info', {}).get('title', 'Unknown Service')

    return schema['x-lifecycle'], title


def generate_events(entity: str, config: dict, topic_prefix: str = None) -> Dict[str, Any]:
    """
    Generate Created, Updated, Deleted event schemas for an entity.

    Events inherit from BaseServiceEvent and include:
    - eventName: enum with the topic in dot notation (e.g., "account.created")
    - eventId: UUID string (inherited from BaseServiceEvent)
    - timestamp: ISO 8601 datetime (inherited from BaseServiceEvent)

    If resource_mapping is provided, x-resource-mapping extension is added to each event.

    Args:
        entity: Entity name in PascalCase (e.g., "Account")
        config: Configuration dict with 'model', optional 'sensitive', and optional 'resource_mapping' keys
        topic_prefix: Optional service namespace prefix for Pattern C topics (e.g., "transit", "worldstate")

    Returns:
        Dict with three event schema definitions
    """
    model = config.get('model', {})
    sensitive = set(config.get('sensitive', []))
    resource_mapping = config.get('resource_mapping', None)

    # Find primary key (field marked with primary: true)
    primary_key = None
    for field, defn in model.items():
        if isinstance(defn, dict) and defn.get('primary'):
            primary_key = field
            break

    if not primary_key:
        raise ValueError(f"No primary key defined for {entity}. Mark one field with 'primary: true'")

    # Generate topic name (kebab-case for topic, used in eventName enum)
    entity_kebab = to_kebab_case(entity)
    if topic_prefix:
        if entity_kebab == topic_prefix:
            # Pattern A: entity IS the service (e.g., entity=Seed, prefix=seed)
            topic_base = entity_kebab
        elif entity_kebab.startswith(f'{topic_prefix}-'):
            # Pattern C: strip service prefix, replace hyphen with dot
            # e.g., prefix=transit, entity=TransitConnection -> transit.connection
            topic_base = f'{topic_prefix}.{entity_kebab[len(topic_prefix)+1:]}'
        else:
            # Pattern C: entity doesn't start with service name, prepend
            # e.g., prefix=worldstate, entity=CalendarTemplate -> worldstate.calendar-template
            topic_base = f'{topic_prefix}.{entity_kebab}'
    else:
        topic_base = entity_kebab

    # Build model properties (excluding sensitive fields)
    model_props = {}
    model_required_fields = []

    for field, defn in model.items():
        if field in sensitive:
            continue

        # Handle dict definitions vs $ref strings
        if isinstance(defn, dict):
            # Copy definition without our custom markers
            prop = {k: v for k, v in defn.items() if k not in ('required', 'primary')}
            if defn.get('required'):
                model_required_fields.append(field)
        else:
            # It's a $ref or simple type
            prop = defn

        model_props[field] = prop

    # Ensure primary key is required
    if primary_key not in model_required_fields:
        model_required_fields.append(primary_key)

    def build_event(event_type: str, extra_props: dict = None, extra_required: list = None, is_deletion: bool = False) -> dict:
        """Build an event schema that inherits from BaseServiceEvent."""
        topic = f'{topic_base}.{event_type}'

        # Properties specific to this event (not inherited)
        props = {
            'eventName': {
                'type': 'string',
                'default': topic,
                'description': f'Event type identifier: {topic}'
            },
            **model_props
        }

        if extra_props:
            props.update(extra_props)

        # Required fields: base class fields + eventName + model required + extras
        required = ['eventName', 'eventId', 'timestamp'] + model_required_fields.copy()
        if extra_required:
            required.extend(extra_required)

        event_schema = {
            'allOf': [
                {'$ref': '../common-events.yaml#/components/schemas/BaseServiceEvent'}
            ],
            'type': 'object',
            'additionalProperties': False,
            'description': f'Published to {topic} when a {entity.lower()} is {event_type}',
            'required': required,
            'properties': props
        }

        # Add x-resource-mapping if resource_mapping is configured
        if resource_mapping is not None:
            # Determine values with defaults
            resource_type = resource_mapping.get('resource_type', topic_base)
            resource_id_field = resource_mapping.get('resource_id_field', primary_key)
            source_type = resource_mapping.get('source_type', topic_base)

            event_schema['x-resource-mapping'] = {
                'resource_type': resource_type,
                'resource_id_field': resource_id_field,
                'source_type': source_type,
                'is_deletion': is_deletion
            }

        return event_schema

    # CreatedEvent
    created = build_event('created')

    # UpdatedEvent - adds changedFields
    updated = build_event('updated',
        extra_props={
            'changedFields': {
                'type': 'array',
                'items': {'type': 'string'},
                'description': 'List of field names that were modified'
            }
        },
        extra_required=['changedFields']
    )

    # DeletedEvent - adds deletedReason (optional)
    deleted = build_event('deleted',
        extra_props={
            'deletedReason': {
                'type': 'string',
                'nullable': True,
                'description': 'Optional reason for deletion (e.g., "Merged into {targetId}")'
            }
        },
        is_deletion=True
    )

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
        service_name: Service name (e.g., "account")
        service_title: Human-readable service title
        lifecycle_defs: x-lifecycle definitions from API schema
        output_path: Path to write the generated file

    Returns:
        List of entity names that had events generated
    """
    # Extract topic_prefix configuration (not an entity definition)
    topic_prefix = None
    if isinstance(lifecycle_defs.get('topic_prefix'), str):
        topic_prefix = lifecycle_defs['topic_prefix']

    # Build all event schemas
    all_schemas = {}
    generated_entities = []

    for entity_name, config in lifecycle_defs.items():
        # Skip configuration keys that aren't entity definitions
        if entity_name == 'topic_prefix':
            continue
        events = generate_events(entity_name, config, topic_prefix=topic_prefix)
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
                f'Source of truth: {service_name}-events.yaml x-lifecycle section.'
            ),
            'version': '1.0.0'
        },
        'paths': {},  # Events don't have HTTP paths
        'components': {
            'schemas': all_schemas
        }
    }

    # Fix relative paths for Generated/ output location
    doc = fix_relative_paths_for_generated(doc)

    # Write the file (completely overwriting)
    with open(output_path, 'w') as f:
        yaml.dump(doc, f)

    return generated_entities


def main():
    """Process all event schema files and generate lifecycle event files."""
    # Find schemas directory relative to script location
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'
    generated_dir = schema_dir / 'Generated'

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    # Create Generated directory if it doesn't exist
    generated_dir.mkdir(exist_ok=True)

    # Clean up old lifecycle event files
    for old_file in generated_dir.glob('*-lifecycle-events.yaml'):
        old_file.unlink()
        print(f"  Cleaned up: {old_file.name}")

    print("Generating lifecycle events from x-lifecycle definitions...")
    print("  Reading from: *-events.yaml (source of truth)")
    print("  Writing to: Generated/*-lifecycle-events.yaml (completely overwritten)")
    print()

    generated_files = []
    errors = []

    for events_file in sorted(schema_dir.glob('*-events.yaml')):
        # Skip lifecycle events files (already in Generated/) and client events
        if '-lifecycle-events' in events_file.name or '-client-events' in events_file.name:
            continue

        try:
            lifecycle_defs, service_title = read_lifecycle_definitions(events_file)

            if lifecycle_defs is None:
                continue

            service_name = extract_service_name(events_file)
            output_file = generated_dir / f'{service_name}-lifecycle-events.yaml'

            entities = generate_lifecycle_events_file(
                service_name,
                service_title,
                lifecycle_defs,
                output_file
            )

            generated_files.append((output_file.name, entities))
            print(f"  Generated/{output_file.name}: Generated events for {', '.join(entities)}")

        except Exception as e:
            errors.append(f"{events_file.name}: {e}")

    # Report results
    print()
    if errors:
        print("Errors:")
        for error in errors:
            print(f"  {error}")
        sys.exit(1)

    if generated_files:
        print(f"Generated {len(generated_files)} lifecycle event file(s) in schemas/Generated/")
        print("Note: Event schemas were NOT modified (read-only)")
    else:
        print("No x-lifecycle definitions found in any event schemas")


if __name__ == '__main__':
    main()
