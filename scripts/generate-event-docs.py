#!/usr/bin/env python3
"""
Generate Events Documentation from Schema Files.

This script scans *-events.yaml schema files and generates comprehensive
markdown documentation for all events used in Bannou.

Output: docs/GENERATED-EVENTS.md

Usage:
    python3 scripts/generate-event-docs.py
"""

import sys
import re
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


def to_kebab_case(name: str) -> str:
    """Convert PascalCase to kebab-case."""
    s1 = re.sub('(.)([A-Z][a-z]+)', r'\1-\2', name)
    return re.sub('([a-z0-9])([A-Z])', r'\1-\2', s1).lower()


def extract_service_from_filename(filename: str) -> str:
    """Extract service name from event schema filename."""
    # Handle patterns like:
    # - account-events.yaml -> Account
    # - account-lifecycle-events.yaml -> Account
    # - common-events.yaml -> Common
    # - common-client-events.yaml -> Common (Client)

    name = filename.replace('.yaml', '')

    # Check for lifecycle events
    if '-lifecycle-events' in name:
        service = name.replace('-lifecycle-events', '')
    elif '-client-events' in name:
        service = name.replace('-client-events', '') + ' (Client)'
    elif '-events' in name:
        service = name.replace('-events', '')
    else:
        service = name

    # Capitalize and handle multi-word services
    parts = service.split('-')
    return ' '.join(p.capitalize() for p in parts)


def get_event_type(event_name: str) -> str:
    """Determine event type from name."""
    name_lower = event_name.lower()

    if 'created' in name_lower:
        return 'Lifecycle (Created)'
    elif 'updated' in name_lower:
        return 'Lifecycle (Updated)'
    elif 'deleted' in name_lower:
        return 'Lifecycle (Deleted)'
    elif 'connected' in name_lower or 'disconnected' in name_lower:
        return 'Session'
    elif 'registered' in name_lower:
        return 'Registration'
    elif 'heartbeat' in name_lower:
        return 'Health'
    elif 'error' in name_lower:
        return 'Error'
    elif 'expired' in name_lower or 'revoked' in name_lower:
        return 'Expiration'
    else:
        return 'Custom'


def infer_topic_from_event(event_name: str) -> str:
    """Infer the likely topic name from event name."""
    # Remove 'Event' suffix
    if event_name.endswith('Event'):
        name = event_name[:-5]
    else:
        name = event_name

    # Convert to kebab-case with dot separator for action
    # AccountCreatedEvent -> account.created
    # SessionInvalidatedEvent -> session.invalidated
    # ServiceRegistrationEvent -> service.registration (or permissions.service-registered)

    # Find where the action starts (Created, Updated, Deleted, etc.)
    action_patterns = [
        'Created', 'Updated', 'Deleted', 'Connected', 'Disconnected',
        'Invalidated', 'Registered', 'Expired', 'Revoked', 'Error', 'Heartbeat'
    ]

    for pattern in action_patterns:
        if pattern in name:
            idx = name.index(pattern)
            entity = name[:idx]
            action = name[idx:]
            return f"{to_kebab_case(entity)}.{to_kebab_case(action)}"

    # Fallback: just kebab-case the whole thing
    return to_kebab_case(name)


def extract_required_fields(schema: dict) -> list:
    """Extract required fields from schema."""
    return schema.get('required', [])


def extract_description(schema: dict) -> str:
    """Extract and clean description from schema."""
    desc = schema.get('description', '')
    if isinstance(desc, str):
        # Get first line/sentence
        lines = desc.strip().split('\n')
        first_line = lines[0].strip()
        # Truncate if too long
        if len(first_line) > 80:
            first_line = first_line[:77] + '...'
        return first_line
    return ''


def scan_event_schemas(schemas_dir: Path) -> dict:
    """Scan all event schema files and extract event definitions."""
    events_by_service = defaultdict(list)

    if not schemas_dir.exists():
        return events_by_service

    # Find all *-events.yaml files (includes lifecycle-events)
    for yaml_file in sorted(schemas_dir.glob('*-events.yaml')):
        try:
            with open(yaml_file) as f:
                content = yaml.load(f)

            if content is None:
                continue

            service = extract_service_from_filename(yaml_file.name)

            # Extract schemas from components/schemas
            schemas = content.get('components', {}).get('schemas', {})

            for event_name, event_schema in schemas.items():
                # Skip non-event schemas (helper types)
                if not event_name.endswith('Event') and 'Event' not in event_name:
                    # Check if it looks like an event (has eventId, timestamp)
                    props = event_schema.get('properties', {})
                    if 'eventId' not in props and 'event_id' not in props:
                        continue

                events_by_service[service].append({
                    'name': event_name,
                    'type': get_event_type(event_name),
                    'topic': infer_topic_from_event(event_name),
                    'required_fields': extract_required_fields(event_schema),
                    'description': extract_description(event_schema),
                    'source_file': yaml_file.name,
                })

        except Exception as e:
            print(f"Warning: Failed to parse {yaml_file}: {e}")

    return events_by_service


def generate_markdown(events_by_service: dict) -> str:
    """Generate markdown documentation from events."""
    lines = [
        "# Generated Events Reference",
        "",
        "> **Source**: `schemas/*-events.yaml`",
        "> **Do not edit manually** - regenerate with `make generate-docs`",
        "",
        "This document lists all events defined in Bannou's event schemas.",
        "",
        "## Events by Service",
        "",
    ]

    # Sort services alphabetically, but put Common first
    sorted_services = sorted(events_by_service.keys())
    if 'Common' in sorted_services:
        sorted_services.remove('Common')
        sorted_services.insert(0, 'Common')

    for service in sorted_services:
        events = events_by_service[service]
        if not events:
            continue

        lines.append(f"### {service}")
        lines.append("")
        lines.append("| Event | Type | Likely Topic | Description |")
        lines.append("|-------|------|--------------|-------------|")

        # Sort events by name
        for event in sorted(events, key=lambda x: x['name']):
            desc = event['description'][:50] + '...' if len(event['description']) > 50 else event['description']
            lines.append(
                f"| `{event['name']}` | {event['type']} | `{event['topic']}` | {desc} |"
            )

        lines.append("")

    # Add event type summary
    lines.extend([
        "## Event Types",
        "",
        "| Type | Description | Example |",
        "|------|-------------|---------|",
        "| Lifecycle (Created) | Entity creation events from `x-lifecycle` | `AccountCreatedEvent` |",
        "| Lifecycle (Updated) | Entity update events from `x-lifecycle` | `CharacterUpdatedEvent` |",
        "| Lifecycle (Deleted) | Entity deletion events from `x-lifecycle` | `RelationshipDeletedEvent` |",
        "| Session | WebSocket connection events | `SessionConnectedEvent` |",
        "| Registration | Service/capability registration | `ServiceRegistrationEvent` |",
        "| Health | Heartbeat and health status | `ServiceHeartbeatEvent` |",
        "| Error | Error reporting events | `ServiceErrorEvent` |",
        "| Expiration | Subscription/token expiration | `SubscriptionExpiredEvent` |",
        "| Custom | Service-specific events | Varies by service |",
        "",
    ])

    # Add topic naming convention
    lines.extend([
        "## Topic Naming Convention",
        "",
        "Events are published to topics following the pattern: `{entity}.{action}`",
        "",
        "Examples:",
        "- `account.created` - Account was created",
        "- `session.invalidated` - Session was invalidated",
        "- `character.updated` - Character was updated",
        "",
        "All events use the `bannou-pubsub` pub/sub component.",
        "",
    ])

    # Add lifecycle events section
    lines.extend([
        "## Lifecycle Events (x-lifecycle)",
        "",
        "Lifecycle events are auto-generated from `x-lifecycle` definitions in API schemas.",
        "They follow a consistent pattern:",
        "",
        "- **Created**: Full entity data on creation",
        "- **Updated**: Full entity data + `changedFields` array",
        "- **Deleted**: Entity ID + `deletedReason`",
        "",
        "See [TENETS.md](../TENETS.md#lifecycle-events-x-lifecycle) for usage details.",
        "",
        "---",
        "",
        "*This file is auto-generated. See [TENETS.md](../TENETS.md) for architectural context.*",
        "",
    ])

    return '\n'.join(lines)


def main():
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schemas_dir = repo_root / 'schemas'

    # Scan event schemas
    events_by_service = scan_event_schemas(schemas_dir)

    total_events = sum(len(events) for events in events_by_service.values())

    if total_events == 0:
        print("Warning: No events found in schemas")

    # Generate markdown
    markdown = generate_markdown(events_by_service)

    # Write output
    output_file = repo_root / 'docs' / 'GENERATED-EVENTS.md'
    with open(output_file, 'w') as f:
        f.write(markdown)

    print(f"Generated {output_file} with {total_events} events across {len(events_by_service)} services")


if __name__ == '__main__':
    main()
