#!/usr/bin/env python3
# ⛔⛔⛔ AGENT MODIFICATION PROHIBITED ⛔⛔⛔
# This script is part of Bannou's code generation pipeline.
# DO NOT MODIFY without EXPLICIT user instructions to change code generation.
#
# Changes to generation scripts silently break builds across ALL 76+ services.
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
Generate published event topic constants from x-event-publications schema declarations.

This script reads x-event-publications definitions from OpenAPI event schemas and generates
C# static classes with const string fields for every published topic, eliminating inline
magic strings in service code.

Architecture:
- {service}-events.yaml: Contains x-event-publications in the info block
- plugins/lib-{service}/Generated/{Service}PublishedTopics.cs: Generated code

Generated Code:
- Static class: {Service}PublishedTopics
- Const string fields: One per published topic (e.g., QuestAccepted = "quest.accepted")

Naming Convention:
- Constant names are derived from the event model name with the "Event" suffix stripped
- Example: QuestAcceptedEvent -> QuestAccepted, EscrowDepositReceivedEvent -> EscrowDepositReceived

Usage:
    python3 scripts/generate-published-topics.py

The script processes all *-events.yaml files in the schemas/ directory.
"""

import sys
from pathlib import Path
from typing import Any, Dict, List, NamedTuple, Optional

try:
    from ruamel.yaml import YAML
    yaml = YAML()
    yaml.preserve_quotes = True
except ImportError:
    print("ERROR: ruamel.yaml is required. Install with: pip install ruamel.yaml")
    sys.exit(1)


class PublishedTopic(NamedTuple):
    """Information about a published event topic."""
    topic: str              # e.g., 'quest.accepted'
    event_type_name: str    # e.g., 'QuestAcceptedEvent'
    constant_name: str      # e.g., 'QuestAccepted'
    description: str        # From the publication declaration


def to_pascal_case(name: str) -> str:
    """Convert kebab-case or snake_case to PascalCase."""
    return ''.join(
        word.capitalize()
        for word in name.replace('-', ' ').replace('_', ' ').split()
    )


def extract_service_name(events_file: Path) -> str:
    """Extract service name from events file path."""
    return events_file.stem.replace('-events', '')


def read_events_schema(events_file: Path) -> Optional[Dict[str, Any]]:
    """Read an events schema file and return its contents."""
    try:
        with open(events_file) as f:
            return yaml.load(f)
    except Exception as e:
        print(f"  Warning: Failed to read {events_file.name}: {e}")
        return None


def derive_constant_name(event_type_name: str) -> str:
    """Derive the constant name from the event type name.

    Strips the 'Event' suffix to produce the constant name.
    Examples:
        QuestAcceptedEvent -> QuestAccepted
        EscrowDepositReceivedEvent -> EscrowDepositReceived
        CollectionEntryUnlockedEvent -> CollectionEntryUnlocked
        LocationEntityArrivedEvent -> LocationEntityArrived
    """
    if event_type_name.endswith('Event'):
        return event_type_name[:-5]
    return event_type_name


def extract_publications(schema: Dict[str, Any], service_name: str) -> List[PublishedTopic]:
    """Extract all x-event-publications from an events schema."""
    # x-event-publications lives inside the info block
    publications = schema.get('info', {}).get('x-event-publications', [])

    # Fallback: check top level (some schemas may use this)
    if not publications:
        publications = schema.get('x-event-publications', [])

    if not publications:
        return []

    topics = []
    for pub in publications:
        topic = pub.get('topic')
        event_type = pub.get('event')
        description = pub.get('description', '')

        if not topic or not event_type:
            print(f"  Warning: {service_name}: x-event-publications entry missing 'topic' or 'event'")
            continue

        constant_name = derive_constant_name(event_type)

        topics.append(PublishedTopic(
            topic=topic,
            event_type_name=event_type,
            constant_name=constant_name,
            description=description
        ))

    return topics


def generate_published_topics_code(
    service_name: str,
    topics: List[PublishedTopic]
) -> str:
    """Generate C# static class with published topic constants."""
    service_pascal = to_pascal_case(service_name)

    # Generate const fields for each topic
    const_fields = []
    for topic in topics:
        # Truncate description to first sentence for XML doc
        desc = topic.description.split('\n')[0].strip()
        if desc and not desc.endswith('.'):
            desc += '.'

        const_fields.append(
            f'    /// <summary>{desc}</summary>\n'
            f'    public const string {topic.constant_name} = "{topic.topic}";'
        )

    fields_block = "\n\n".join(const_fields)

    return f'''// <auto-generated>
// This code was generated by generate-published-topics.py from {service_name}-events.yaml.
// Do not edit this file manually.
// </auto-generated>

#nullable enable

namespace BeyondImmersion.BannouService.{service_pascal};

/// <summary>
/// Published event topic constants for {service_pascal}Service.
/// Use these constants instead of inline topic strings when calling
/// <c>IMessageBus.TryPublishAsync</c>.
/// </summary>
/// <remarks>
/// Generated from <c>x-event-publications</c> in <c>{service_name}-events.yaml</c>.
/// </remarks>
public static class {service_pascal}PublishedTopics
{{
{fields_block}
}}
'''


def main():
    """Process all events schema files and generate published topic constants."""
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'
    plugins_dir = repo_root / 'plugins'

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    print("Generating published topic constants from x-event-publications definitions...")
    print(f"  Reading from: schemas/*-events.yaml")
    print(f"  Writing to: plugins/lib-*/Generated/*PublishedTopics.cs")
    print()

    generated_files = []
    skipped_empty = []
    errors = []

    for events_file in sorted(schema_dir.glob('*-events.yaml')):
        # Skip common events and generated lifecycle events
        if events_file.name == 'common-events.yaml':
            continue
        if events_file.name.endswith('-lifecycle-events.yaml'):
            continue
        # Skip client events (server-to-client push, not pub/sub)
        if events_file.name.endswith('-client-events.yaml'):
            continue

        service_name = extract_service_name(events_file)
        schema = read_events_schema(events_file)

        if schema is None:
            continue

        topics = extract_publications(schema, service_name)

        if not topics:
            skipped_empty.append(service_name)
            continue

        # Generate code
        service_pascal = to_pascal_case(service_name)
        plugin_dir = plugins_dir / f'lib-{service_name}'
        generated_dir = plugin_dir / 'Generated'

        if not plugin_dir.exists():
            print(f"  Warning: Plugin directory not found for {service_name}, skipping")
            continue

        generated_dir.mkdir(exist_ok=True)

        output_file = generated_dir / f'{service_pascal}PublishedTopics.cs'

        try:
            code = generate_published_topics_code(service_name, topics)
            output_file.write_text(code)
            generated_files.append((service_name, len(topics)))
            for topic in topics:
                print(f"  {service_name}: {topic.constant_name} = \"{topic.topic}\"")
        except Exception as e:
            errors.append(f"{service_name}: {e}")

    print()

    if errors:
        print("Errors:")
        for error in errors:
            print(f"  - {error}")
        sys.exit(1)

    if generated_files:
        total_topics = sum(count for _, count in generated_files)
        print(f"Generated {len(generated_files)} published topics file(s) ({total_topics} total topics):")
        for service, count in sorted(generated_files):
            print(f"  - lib-{service}: {count} topic(s)")
    else:
        print("No x-event-publications definitions found in any events schemas")

    if skipped_empty:
        print(f"\nSkipped {len(skipped_empty)} service(s) with empty x-event-publications:")
        for service in sorted(skipped_empty):
            print(f"  - {service}")

    print("\nGeneration complete.")


if __name__ == '__main__':
    main()
