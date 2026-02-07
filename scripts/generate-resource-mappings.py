#!/usr/bin/env python3
"""
Generate ResourceEventMappings.cs from x-resource-mapping schema extensions.

This script scans all event schemas (including generated lifecycle events) for
x-resource-mapping extensions and produces a static registry class that Puppetmaster
uses for watch subscriptions.

Architecture:
- Scans: {service}-events.yaml (manual events) and Generated/{service}-lifecycle-events.yaml
- Skips: *-client-events.yaml, common-events.yaml
- Output: bannou-service/Generated/ResourceEventMappings.cs

Usage:
    python3 scripts/generate-resource-mappings.py

The script must run AFTER generate-lifecycle-events.py (so Generated/ lifecycle files exist).
"""

import sys
import re
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple
from dataclasses import dataclass

# Use ruamel.yaml to preserve formatting and comments
try:
    from ruamel.yaml import YAML
    yaml = YAML()
    yaml.preserve_quotes = True
except ImportError:
    print("ERROR: ruamel.yaml is required. Install with: pip install ruamel.yaml")
    sys.exit(1)


@dataclass
class ResourceMapping:
    """A single resource event mapping entry."""
    resource_type: str
    source_type: str
    event_topic: str
    resource_id_field: str
    is_deletion: bool
    event_name: str  # For comments/debugging


def to_kebab_case(name: str) -> str:
    """Convert PascalCase to kebab-case."""
    s1 = re.sub('(.)([A-Z][a-z]+)', r'\1-\2', name)
    return re.sub('([a-z0-9])([A-Z])', r'\1-\2', s1).lower()


def infer_is_deletion(event_name: str) -> bool:
    """Infer is_deletion from event name if not explicitly set."""
    return event_name.endswith('DeletedEvent') or event_name.endswith('Deleted')


def extract_topic_from_event_name(event_name: str) -> str:
    """
    Extract topic from lifecycle event name.

    E.g., CharacterCreatedEvent -> character.created
          RealmDeletedEvent -> realm.deleted
    """
    # Remove 'Event' suffix
    name = event_name
    if name.endswith('Event'):
        name = name[:-5]

    # Find the action suffix (Created, Updated, Deleted)
    for action in ['Created', 'Updated', 'Deleted']:
        if name.endswith(action):
            entity = name[:-len(action)]
            return f"{to_kebab_case(entity)}.{action.lower()}"

    # Fallback: just kebab-case the whole thing
    return to_kebab_case(name)


def find_topic_in_publications(
    schema: Dict[str, Any],
    event_name: str
) -> Optional[str]:
    """
    Find the topic for an event from x-event-publications.

    Returns None if not found.
    """
    publications = schema.get('info', {}).get('x-event-publications', [])
    if not publications:
        publications = schema.get('x-event-publications', [])

    for pub in publications:
        if pub.get('event') == event_name:
            return pub.get('topic')

    return None


def extract_mappings_from_schema(
    schema_path: Path,
    schema: Dict[str, Any],
    is_lifecycle: bool
) -> List[ResourceMapping]:
    """
    Extract all x-resource-mapping entries from a schema.

    Args:
        schema_path: Path to the schema file (for error messages)
        schema: Parsed YAML schema
        is_lifecycle: True if this is a generated lifecycle events file

    Returns:
        List of ResourceMapping entries
    """
    mappings = []

    components = schema.get('components') or {}
    schemas = components.get('schemas') or {}

    for event_name, event_schema in schemas.items():
        if not isinstance(event_schema, dict):
            continue

        resource_mapping = event_schema.get('x-resource-mapping')
        if resource_mapping is None:
            continue

        # Extract mapping fields
        resource_type = resource_mapping.get('resource_type')
        resource_id_field = resource_mapping.get('resource_id_field')
        source_type = resource_mapping.get('source_type')
        is_deletion = resource_mapping.get('is_deletion')

        # Validate required fields
        if not resource_type or not resource_id_field or not source_type:
            print(f"  WARNING: {schema_path.name}/{event_name}: x-resource-mapping missing required fields")
            continue

        # Infer is_deletion if not set
        if is_deletion is None:
            is_deletion = infer_is_deletion(event_name)

        # Find the event topic
        if is_lifecycle:
            # Lifecycle events: infer from event name
            event_topic = extract_topic_from_event_name(event_name)
        else:
            # Manual events: look up in x-event-publications
            event_topic = find_topic_in_publications(schema, event_name)
            if event_topic is None:
                print(f"  WARNING: {schema_path.name}/{event_name}: No topic found in x-event-publications")
                continue

        mappings.append(ResourceMapping(
            resource_type=resource_type,
            source_type=source_type,
            event_topic=event_topic,
            resource_id_field=resource_id_field,
            is_deletion=is_deletion,
            event_name=event_name
        ))

    return mappings


def generate_csharp(mappings: List[ResourceMapping]) -> str:
    """Generate the C# source code for ResourceEventMappings.cs."""

    # Sort mappings for deterministic output
    mappings.sort(key=lambda m: (m.resource_type, m.source_type, m.event_topic))

    # Build the entries
    entries = []
    for m in mappings:
        is_deletion_str = "true" if m.is_deletion else "false"
        entries.append(
            f'        new("{m.resource_type}", "{m.source_type}", "{m.event_topic}", '
            f'"{m.resource_id_field}", {is_deletion_str})'
        )

    entries_str = ",\n".join(entries)

    return f'''// <auto-generated />
// Generated by generate-resource-mappings.py from x-resource-mapping schema extensions.
// DO NOT EDIT - This file is completely overwritten on regeneration.

using System.Collections.Generic;

namespace BeyondImmersion.BannouService;

/// <summary>
/// Auto-generated resource event mappings from x-resource-mapping schema extensions.
/// Used by Puppetmaster's watch system for resource change subscriptions.
/// </summary>
public static class ResourceEventMappings
{{
    /// <summary>
    /// All discovered resource event mappings.
    /// </summary>
    public static readonly IReadOnlyList<ResourceEventMappingEntry> All =
    [
{entries_str}
    ];
}}

/// <summary>
/// A single resource event mapping entry.
/// </summary>
/// <param name="ResourceType">Type of resource affected (e.g., "character", "realm").</param>
/// <param name="SourceType">Source type identifier for filtering (e.g., "character-personality").</param>
/// <param name="EventTopic">RabbitMQ event topic (e.g., "personality.updated").</param>
/// <param name="ResourceIdField">JSON field containing the resource ID.</param>
/// <param name="IsDeletion">True if this event indicates resource deletion.</param>
public sealed record ResourceEventMappingEntry(
    string ResourceType,
    string SourceType,
    string EventTopic,
    string ResourceIdField,
    bool IsDeletion);
'''


def main():
    """Scan all event schemas and generate ResourceEventMappings.cs."""
    # Find directories relative to script location
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'
    generated_schema_dir = schema_dir / 'Generated'
    output_dir = repo_root / 'bannou-service' / 'Generated'
    output_file = output_dir / 'ResourceEventMappings.cs'

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    # Ensure output directory exists
    output_dir.mkdir(parents=True, exist_ok=True)

    print("Generating ResourceEventMappings.cs from x-resource-mapping extensions...")
    print()

    all_mappings: List[ResourceMapping] = []

    # Process manual event schemas (non-lifecycle, non-client)
    print("Scanning manual event schemas:")
    for events_file in sorted(schema_dir.glob('*-events.yaml')):
        # Skip lifecycle, client, and common events
        if '-lifecycle-events' in events_file.name:
            continue
        if '-client-events' in events_file.name:
            continue
        if events_file.name == 'common-events.yaml':
            continue

        try:
            with open(events_file) as f:
                schema = yaml.load(f)

            if schema is None:
                continue

            mappings = extract_mappings_from_schema(events_file, schema, is_lifecycle=False)
            if mappings:
                print(f"  {events_file.name}: {len(mappings)} mapping(s)")
                all_mappings.extend(mappings)
        except Exception as e:
            print(f"  ERROR: {events_file.name}: {e}")

    # Process generated lifecycle event schemas
    if generated_schema_dir.exists():
        print()
        print("Scanning generated lifecycle event schemas:")
        for lifecycle_file in sorted(generated_schema_dir.glob('*-lifecycle-events.yaml')):
            try:
                with open(lifecycle_file) as f:
                    schema = yaml.load(f)

                if schema is None:
                    continue

                mappings = extract_mappings_from_schema(lifecycle_file, schema, is_lifecycle=True)
                if mappings:
                    print(f"  {lifecycle_file.name}: {len(mappings)} mapping(s)")
                    all_mappings.extend(mappings)
            except Exception as e:
                print(f"  ERROR: {lifecycle_file.name}: {e}")

    # Generate output
    print()
    if not all_mappings:
        print("No x-resource-mapping extensions found.")
        # Still generate an empty file for compilation
        csharp_code = generate_csharp([])
    else:
        print(f"Found {len(all_mappings)} total mapping(s)")
        csharp_code = generate_csharp(all_mappings)

    # Write output file
    with open(output_file, 'w') as f:
        f.write(csharp_code)

    print(f"Generated: {output_file}")
    print()

    # Summary
    if all_mappings:
        print("Mappings by resource type:")
        by_resource = {}
        for m in all_mappings:
            by_resource.setdefault(m.resource_type, []).append(m)
        for resource_type, mappings in sorted(by_resource.items()):
            print(f"  {resource_type}: {len(mappings)} event(s)")


if __name__ == '__main__':
    main()
