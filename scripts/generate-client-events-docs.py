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
Generate Client Events Documentation from event schemas.

This script scans *-client-events.yaml schema files and generates a compact reference
document showing all subscribable events available to Client SDK users.

Output: docs/GENERATED-CLIENT-EVENTS.md

Usage:
    python3 scripts/generate-client-events-docs.py
"""

import sys
from pathlib import Path
from typing import Dict, List, Optional

try:
    from ruamel.yaml import YAML
    yaml = YAML()
    yaml.preserve_quotes = True
except ImportError:
    print("ERROR: ruamel.yaml is required. Install with: pip install ruamel.yaml")
    sys.exit(1)


def to_pascal_case(name: str) -> str:
    """Convert kebab-case or snake_case to PascalCase."""
    parts = name.replace('_', '-').split('-')
    return ''.join(p.capitalize() for p in parts)


def to_title_case(name: str) -> str:
    """Convert kebab-case service name to Title Case."""
    parts = name.split('-')
    return ' '.join(p.capitalize() for p in parts)


class EventInfo:
    """Information about a client event."""

    def __init__(
        self,
        type_name: str,
        event_name: str,
        description: str,
        service: str,
        properties: Dict[str, str]
    ):
        self.type_name = type_name
        self.event_name = event_name
        self.description = description
        self.service = service
        self.properties = properties  # property name -> description


class ServiceEvents:
    """Events for a specific service."""

    def __init__(self, name: str, title: str, description: str):
        self.name = name
        self.title = title
        self.description = description
        self.events: List[EventInfo] = []


def parse_event_schema(schema_path: Path) -> Optional[ServiceEvents]:
    """Parse a client events schema file."""
    with open(schema_path) as f:
        try:
            schema = yaml.load(f)
        except Exception as e:
            print(f"  Warning: Failed to parse {schema_path.name}: {e}")
            return None

    if schema is None:
        return None

    # Extract service info
    service_name = schema_path.stem.replace('-client-events', '')
    info = schema.get('info', {})
    title = info.get('title', to_title_case(service_name) + ' Events')
    description = info.get('description', '')

    # Clean up description
    if description:
        first_para = description.split('\n\n')[0]
        first_para = first_para.replace('**', '').replace('`', '').replace('\n', ' ')
        if len(first_para) > 200:
            first_para = first_para[:197] + '...'
        description = first_para.strip()

    service = ServiceEvents(service_name, title, description)

    # Parse event schemas
    components = schema.get('components', {})
    schemas = components.get('schemas', {})

    for schema_name, schema_def in schemas.items():
        if not isinstance(schema_def, dict):
            continue

        # Skip BaseClientEvent (base schema, not an actual event)
        if schema_name == 'BaseClientEvent':
            continue

        # Skip if not marked as client event
        if not schema_def.get('x-client-event', False):
            continue

        # Skip internal events (not exposed to clients)
        if schema_def.get('x-internal', False):
            continue

        # Extract event name from properties.eventName.default
        properties = schema_def.get('properties', {})
        event_name_prop = properties.get('eventName', {})
        event_name = event_name_prop.get('default', '')

        if not event_name:
            # Try to infer from schema name
            event_name = schema_name.replace('Event', '').lower()

        # Extract description
        event_desc = schema_def.get('description', '')
        if event_desc:
            # Take first line/sentence
            event_desc = event_desc.split('\n')[0].strip()
            if len(event_desc) > 100:
                event_desc = event_desc[:97] + '...'

        # Extract properties (excluding inherited ones)
        prop_details = {}
        for prop_name, prop_def in properties.items():
            if prop_name in ('eventName', 'eventId', 'timestamp'):
                continue  # Skip inherited properties
            if isinstance(prop_def, dict):
                prop_desc = prop_def.get('description', '')
                if prop_desc:
                    prop_desc = prop_desc.split('\n')[0][:50]
                prop_details[prop_name] = prop_desc

        event = EventInfo(
            type_name=schema_name,
            event_name=event_name,
            description=event_desc,
            service=service_name,
            properties=prop_details
        )
        service.events.append(event)

    return service if service.events else None


def generate_markdown(services: List[ServiceEvents]) -> str:
    """Generate the documentation markdown."""
    # Flatten all events for the summary table
    all_events = []
    for service in services:
        for event in service.events:
            all_events.append(event)

    all_events.sort(key=lambda e: e.event_name)

    lines = [
        "# Generated Client Events Reference",
        "",
        "> **Source**: `schemas/*-client-events.yaml`",
        "> **Do not edit manually** - regenerate with `make generate-docs`",
        "",
        "This document lists all typed events available for subscription in the Bannou Client SDK.",
        "",
        "## Quick Reference",
        "",
        "| Event Type | Event Name | Description |",
        "|------------|------------|-------------|",
    ]

    for event in all_events:
        desc = event.description[:60] + '...' if len(event.description) > 60 else event.description
        lines.append(f"| `{event.type_name}` | `{event.event_name}` | {desc} |")

    lines.extend([
        "",
        "---",
        "",
        "## Usage Pattern",
        "",
        "```csharp",
        "using BeyondImmersion.Bannou.Client;",
        "",
        "var client = new BannouClient();",
        "await client.ConnectWithTokenAsync(url, token);",
        "",
        "// Subscribe to typed events - returns a disposable handle",
        "using var subscription = client.OnEvent<ChatMessageReceivedEvent>(evt =>",
        "{",
        "    Console.WriteLine($\"[{evt.SenderId}]: {evt.Message}\");",
        "});",
        "",
        "// Or subscribe to multiple event types",
        "using var matchSub = client.OnEvent<MatchFoundEvent>(evt =>",
        "{",
        "    Console.WriteLine($\"Match found! Players: {evt.PlayerCount}\");",
        "});",
        "",
        "// Use ClientEventRegistry for runtime type discovery",
        "var eventName = ClientEventRegistry.GetEventName<ChatMessageReceivedEvent>();",
        "// Returns: \"game_session.chat_received\"",
        "```",
        "",
        "---",
        "",
    ])

    # Service details
    for service in services:
        lines.extend([
            f"## {service.title}",
            "",
        ])

        if service.description:
            lines.extend([service.description, ""])

        for event in sorted(service.events, key=lambda e: e.event_name):
            lines.extend([
                f"### `{event.type_name}`",
                "",
                f"**Event Name**: `{event.event_name}`",
                "",
            ])

            if event.description:
                lines.extend([event.description, ""])

            if event.properties:
                lines.extend([
                    "**Properties**:",
                    "",
                    "| Property | Description |",
                    "|----------|-------------|",
                ])
                for prop_name, prop_desc in sorted(event.properties.items()):
                    lines.append(f"| `{prop_name}` | {prop_desc} |")
                lines.append("")

        lines.append("---")
        lines.append("")

    # Summary
    lines.extend([
        "## Summary",
        "",
        f"- **Total event types**: {len(all_events)}",
        f"- **Services with events**: {len(services)}",
        "",
        "---",
        "",
        "*This file is auto-generated from event schemas. See [TENETS.md](reference/TENETS.md) for architectural context.*",
        "",
    ])

    return '\n'.join(lines)


def main():
    """Process all event schemas and generate documentation."""
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'
    output_file = repo_root / 'docs' / 'GENERATED-CLIENT-EVENTS.md'

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    print("Generating Client Events documentation from event schemas...")
    print(f"  Reading from: schemas/*-client-events.yaml")
    print(f"  Writing to: {output_file.relative_to(repo_root)}")
    print()

    services: List[ServiceEvents] = []

    for schema_path in sorted(schema_dir.glob('*-client-events.yaml')):
        try:
            service = parse_event_schema(schema_path)
            if service:
                services.append(service)
                print(f"  Parsed {schema_path.name}: {len(service.events)} events")
        except Exception as e:
            print(f"  Error parsing {schema_path.name}: {e}")

    # Generate and write documentation
    output_file.parent.mkdir(parents=True, exist_ok=True)
    markdown = generate_markdown(services)

    with open(output_file, 'w', newline='\n') as f:
        f.write(markdown)

    total_events = sum(len(s.events) for s in services)
    print(f"\nGenerated documentation for {total_events} events across {len(services)} services")


if __name__ == '__main__':
    main()
