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
Generate typed event publishing extension methods from x-event-publications schema declarations.

This script reads x-event-publications definitions from OpenAPI event schemas and generates
C# static extension classes on IMessageBus with typed publish methods for each event topic,
eliminating inline topic strings and ensuring correct topic-to-event-type pairing.

Architecture:
- {service}-events.yaml: Contains x-event-publications in the info block
- plugins/lib-{service}/Generated/{Service}EventPublisher.cs: Generated code

Generated Code:
- Static extension class: {Service}EventPublisher
- Extension methods on IMessageBus: One per published topic
- Each method delegates to TryPublishAsync with the correct topic constant from *PublishedTopics

Companion to generate-published-topics.py which generates the topic constants that these
extension methods reference.

Usage:
    python3 scripts/generate-event-publishers.py

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


class TopicParam(NamedTuple):
    """A typed parameter extracted from a parameterized topic string."""
    name: str               # e.g., 'poolType'
    schema_type: str        # e.g., 'string', 'uuid', 'MapKind'


class PublishedTopic(NamedTuple):
    """Information about a published event topic."""
    topic: str              # e.g., 'quest.accepted'
    event_type_name: str    # e.g., 'QuestAcceptedEvent'
    constant_name: str      # e.g., 'QuestAccepted'
    description: str        # From the publication declaration
    topic_params: tuple     # Tuple of TopicParam (empty for non-parameterized)


def to_pascal_case(name: str) -> str:
    """Convert kebab-case or snake_case to PascalCase."""
    return ''.join(
        word.capitalize()
        for word in name.replace('-', ' ').replace('_', ' ').split()
    )


# Mapping from schema topic-param type to (C# type name, C# typeof expression)
SCHEMA_TYPE_TO_CSHARP = {
    'string': ('string', 'string'),
    'uuid': ('Guid', 'Guid'),
}


def map_schema_type(schema_type: str) -> tuple:
    """Map a schema topic-param type to (C# type, C# typeof expression).

    Known types: 'string' -> string, 'uuid' -> Guid.
    Unknown types are assumed to be C# type names (e.g., enum names like MapKind).
    """
    if schema_type in SCHEMA_TYPE_TO_CSHARP:
        return SCHEMA_TYPE_TO_CSHARP[schema_type]
    # Assume it's a C# type name directly (e.g., MapKind enum)
    return (schema_type, schema_type)


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
    """
    if event_type_name.endswith('Event'):
        return event_type_name[:-5]
    return event_type_name


def extract_publications(schema: Dict[str, Any], service_name: str) -> List[PublishedTopic]:
    """Extract all x-event-publications from an events schema."""
    publications = schema.get('info', {}).get('x-event-publications', [])

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

        # Extract topic-params for parameterized topics
        raw_params = pub.get('topic-params', [])
        topic_params = tuple(
            TopicParam(name=p['name'], schema_type=p['type'])
            for p in raw_params
            if 'name' in p and 'type' in p
        )

        topics.append(PublishedTopic(
            topic=topic,
            event_type_name=event_type,
            constant_name=constant_name,
            description=description,
            topic_params=topic_params
        ))

    return topics


def generate_method_name(constant_name: str) -> str:
    """Generate the extension method name from the constant name.

    Examples:
        QuestAccepted -> PublishQuestAcceptedAsync
        AccountCreated -> PublishAccountCreatedAsync
    """
    return f'Publish{constant_name}Async'


def generate_parameterized_overload(
    topic: 'PublishedTopic',
    method_name: str,
    desc: str
) -> str:
    """Generate a parameterized overload for a topic with topic-params.

    The overload accepts typed parameters for each placeholder in the topic string
    and uses C# string interpolation to produce the concrete topic at publish time.
    """
    # Build parameter list: C# type + name for each topic-param
    param_declarations = []
    typeof_args = []
    for tp in topic.topic_params:
        csharp_type, typeof_expr = map_schema_type(tp.schema_type)
        param_declarations.append(f'        {csharp_type} {tp.name},')
        typeof_args.append(f'typeof({typeof_expr})')

    # Build the attribute: [ParameterizedTopic(typeof(string), typeof(Guid), ...)]
    attr_args = ', '.join(typeof_args)
    attribute = f'    [ParameterizedTopic({attr_args})]'

    # Build parameter block
    params_block = '\n'.join(param_declarations)

    # The topic string with {placeholders} becomes a C# interpolated string
    interpolated_topic = f'$"{topic.topic}"'

    return (
        f'    /// <summary>{desc} Parameterized overload for dynamic topic resolution.</summary>\n'
        f'{attribute}\n'
        f'    public static Task<bool> {method_name}(\n'
        f'        this IMessageBus messageBus,\n'
        f'        {topic.event_type_name} eventData,\n'
        f'{params_block}\n'
        f'        CancellationToken cancellationToken = default)\n'
        f'        => messageBus.TryPublishAsync({interpolated_topic}, eventData, cancellationToken);'
    )


def generate_event_publisher_code(
    service_name: str,
    topics: List[PublishedTopic]
) -> str:
    """Generate C# static extension class with typed publish methods."""
    service_pascal = to_pascal_case(service_name)

    # Check if any topic has parameters (need extra using)
    has_parameterized = any(topic.topic_params for topic in topics)

    methods = []
    for topic in topics:
        desc = topic.description.split('\n')[0].strip()
        if desc and not desc.endswith('.'):
            desc += '.'

        method_name = generate_method_name(topic.constant_name)

        # Base method (uses topic constant — template string for parameterized topics)
        methods.append(
            f'    /// <summary>{desc}</summary>\n'
            f'    public static Task<bool> {method_name}(\n'
            f'        this IMessageBus messageBus,\n'
            f'        {topic.event_type_name} eventData,\n'
            f'        CancellationToken cancellationToken = default)\n'
            f'        => messageBus.TryPublishAsync({service_pascal}PublishedTopics.{topic.constant_name}, eventData, cancellationToken);'
        )

        # Parameterized overload (uses string interpolation with typed params)
        if topic.topic_params:
            methods.append(generate_parameterized_overload(topic, method_name, desc))

    methods_block = "\n\n".join(methods)

    # Build using block — add Attributes using only when needed
    usings = [
        'using BeyondImmersion.BannouService.Events;',
        'using BeyondImmersion.BannouService.Services;',
    ]
    if has_parameterized:
        usings.insert(0, 'using BeyondImmersion.BannouService.Attributes;')

    usings_block = '\n'.join(usings)

    return f'''// <auto-generated>
// This code was generated by generate-event-publishers.py from {service_name}-events.yaml.
// Do not edit this file manually.
// </auto-generated>

#nullable enable

{usings_block}

namespace BeyondImmersion.BannouService.{service_pascal};

/// <summary>
/// Typed event publishing extension methods for {service_pascal}Service.
/// Ensures correct topic-to-event-type pairing for all published events.
/// </summary>
/// <remarks>
/// Generated from <c>x-event-publications</c> in <c>{service_name}-events.yaml</c>.
/// Use these methods instead of calling <c>IMessageBus.TryPublishAsync</c> with inline topic strings.
/// </remarks>
public static class {service_pascal}EventPublisher
{{
{methods_block}
}}
'''


def main():
    """Process all events schema files and generate event publisher extension classes."""
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'
    plugins_dir = repo_root / 'plugins'

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    print("Generating event publisher extension methods from x-event-publications definitions...")
    print(f"  Reading from: schemas/*-events.yaml")
    print(f"  Writing to: plugins/lib-*/Generated/*EventPublisher.cs")
    print()

    generated_files = []
    skipped_empty = []
    errors = []

    for events_file in sorted(schema_dir.glob('*-events.yaml')):
        if events_file.name == 'common-events.yaml':
            continue
        if events_file.name.endswith('-lifecycle-events.yaml'):
            continue
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

        service_pascal = to_pascal_case(service_name)
        plugin_dir = plugins_dir / f'lib-{service_name}'
        generated_dir = plugin_dir / 'Generated'

        if not plugin_dir.exists():
            print(f"  Warning: Plugin directory not found for {service_name}, skipping")
            continue

        generated_dir.mkdir(exist_ok=True)

        output_file = generated_dir / f'{service_pascal}EventPublisher.cs'

        try:
            code = generate_event_publisher_code(service_name, topics)
            output_file.write_text(code)
            param_count = sum(1 for t in topics if t.topic_params)
            method_count = len(topics) + param_count  # base + overloads
            generated_files.append((service_name, method_count))
            for topic in topics:
                method_name = generate_method_name(topic.constant_name)
                if topic.topic_params:
                    param_types = ', '.join(map_schema_type(tp.schema_type)[0] for tp in topic.topic_params)
                    print(f"  {service_name}: {method_name}({topic.event_type_name}) + overload({param_types})")
                else:
                    print(f"  {service_name}: {method_name}({topic.event_type_name})")
        except Exception as e:
            errors.append(f"{service_name}: {e}")

    print()

    if errors:
        print("Errors:")
        for error in errors:
            print(f"  - {error}")
        sys.exit(1)

    if generated_files:
        total_methods = sum(count for _, count in generated_files)
        print(f"Generated {len(generated_files)} event publisher file(s) ({total_methods} total methods):")
        for service, count in sorted(generated_files):
            print(f"  - lib-{service}: {count} method(s)")
    else:
        print("No x-event-publications definitions found in any events schemas")

    if skipped_empty:
        print(f"\nSkipped {len(skipped_empty)} service(s) with empty x-event-publications:")
        for service in sorted(skipped_empty):
            print(f"  - {service}")

    print("\nGeneration complete.")


if __name__ == '__main__':
    main()
