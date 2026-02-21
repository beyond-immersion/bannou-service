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
Generate event template registration code from x-event-template schema declarations.

This script reads x-event-template definitions from OpenAPI event schemas and generates
C# static classes with EventTemplate fields and registration methods.

Architecture:
- {service}-events.yaml: Contains event schemas with x-event-template declarations
- plugins/lib-{service}/Generated/{Service}EventTemplates.cs: Generated code

Generated Code:
- Static class: {Service}EventTemplates
- Static fields: EventTemplate for each declared template
- Method: RegisterAll(IEventTemplateRegistry) for bulk registration

Usage:
    python3 scripts/generate-event-templates.py

The script processes all *-events.yaml files in the schemas/ directory.
"""

import json
import sys
from pathlib import Path
from typing import Any, Dict, List, Optional, NamedTuple

try:
    from ruamel.yaml import YAML
    yaml = YAML()
    yaml.preserve_quotes = True
except ImportError:
    print("ERROR: ruamel.yaml is required. Install with: pip install ruamel.yaml")
    sys.exit(1)


class EventTemplateInfo(NamedTuple):
    """Information about an event template declaration."""
    event_type_name: str         # e.g., 'EncounterRecordedEvent'
    template_name: str           # e.g., 'encounter_recorded'
    topic: str                   # e.g., 'encounter.recorded'
    description: str             # From event schema description
    payload_template: str        # Generated JSON template


class PropertyInfo(NamedTuple):
    """Information about a schema property for template generation."""
    name: str
    json_name: str              # camelCase version
    prop_type: str              # string, integer, number, boolean, array, object
    format_type: Optional[str]  # uuid, date-time, etc.
    is_nullable: bool
    is_required: bool


def to_pascal_case(name: str) -> str:
    """Convert kebab-case or snake_case to PascalCase."""
    return ''.join(
        word.capitalize()
        for word in name.replace('-', ' ').replace('_', ' ').split()
    )


def to_camel_case(name: str) -> str:
    """Convert PascalCase or snake_case to camelCase."""
    # Handle already camelCase
    if name and name[0].islower():
        return name
    # Handle PascalCase
    if name:
        return name[0].lower() + name[1:]
    return name


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


def extract_properties(schema: Dict[str, Any]) -> List[PropertyInfo]:
    """Extract property information from an event schema."""
    properties = schema.get('properties', {})
    required = set(schema.get('required', []))

    result = []
    for prop_name, prop_def in properties.items():
        # Handle $ref - skip complex refs for now, they'll be handled as objects
        if '$ref' in prop_def:
            prop_type = 'object'
            format_type = None
        else:
            prop_type = prop_def.get('type', 'object')
            format_type = prop_def.get('format')

        is_nullable = prop_def.get('nullable', False)
        is_required = prop_name in required

        result.append(PropertyInfo(
            name=prop_name,
            json_name=to_camel_case(prop_name),
            prop_type=prop_type,
            format_type=format_type,
            is_nullable=is_nullable,
            is_required=is_required
        ))

    return result


def generate_payload_template(properties: List[PropertyInfo]) -> str:
    """Generate JSON payload template from properties.

    Rules:
    - string (non-nullable), format:uuid, format:date-time -> "{{propName}}" (quoted)
    - string (nullable) -> {{propName}} (unquoted - TemplateSubstitutor handles null)
    - integer, number, boolean -> {{propName}} (unquoted)
    - array, object -> {{propName}} (unquoted, pre-serialized)
    """
    lines = []

    for i, prop in enumerate(properties):
        json_name = prop.json_name
        is_last = (i == len(properties) - 1)
        comma = "" if is_last else ","

        # Determine if value should be quoted
        if prop.prop_type == 'string' and not prop.is_nullable:
            # Non-nullable strings are quoted
            value = f'""{{{{{json_name}}}}}""{comma}'
        elif prop.prop_type in ('integer', 'number', 'boolean'):
            # Numerics and booleans are unquoted
            value = f'{{{{{json_name}}}}}{comma}'
        else:
            # Nullable strings, arrays, objects, refs are unquoted
            # TemplateSubstitutor handles proper JSON serialization
            value = f'{{{{{json_name}}}}}{comma}'

        lines.append(f'    ""{json_name}"": {value}')

    return "{\n" + "\n".join(lines) + "\n}"


def extract_event_templates(schema: Dict[str, Any], service_name: str) -> List[EventTemplateInfo]:
    """Extract all x-event-template declarations from an events schema."""
    templates = []
    components = schema.get('components', {})
    if components is None:
        return templates
    schemas = components.get('schemas', {})
    if schemas is None:
        return templates

    for event_name, event_def in schemas.items():
        template_decl = event_def.get('x-event-template')
        if not template_decl:
            continue

        template_name = template_decl.get('name')
        topic = template_decl.get('topic')

        if not template_name or not topic:
            print(f"  Warning: {service_name}/{event_name}: x-event-template missing required 'name' or 'topic'")
            continue

        # Extract properties for payload template generation
        properties = extract_properties(event_def)
        if not properties:
            print(f"  Warning: {service_name}/{event_name}: No properties found for payload template")
            continue

        payload_template = generate_payload_template(properties)
        description = event_def.get('description', f'Event template for {event_name}')
        # Truncate description to first sentence/line for XML doc
        description = description.split('\n')[0].split('.')[0].strip()

        templates.append(EventTemplateInfo(
            event_type_name=event_name,
            template_name=template_name,
            topic=topic,
            description=description,
            payload_template=payload_template
        ))

    return templates


def generate_event_templates_code(
    service_name: str,
    templates: List[EventTemplateInfo]
) -> str:
    """Generate C# static class with event template definitions."""
    service_pascal = to_pascal_case(service_name)

    # Generate static fields for each template
    template_fields = []
    register_calls = []

    for template in templates:
        field_name = to_pascal_case(template.template_name.replace('_', ' '))

        # Escape the payload template for C# verbatim string
        # In verbatim strings, double-quotes are escaped by doubling them
        # The payload already has doubled quotes from generate_payload_template
        payload_escaped = template.payload_template

        template_fields.append(f'''    /// <summary>{template.description}.</summary>
    public static readonly EventTemplate {field_name} = new(
        Name: "{template.template_name}",
        Topic: "{template.topic}",
        EventType: typeof({template.event_type_name}),
        PayloadTemplate: @"{payload_escaped}",
        Description: "{template.description}");''')

        register_calls.append(f'        registry.Register({field_name});')

    fields_block = "\n\n".join(template_fields)
    register_block = "\n".join(register_calls)

    return f'''// <auto-generated>
// This code was generated by generate-event-templates.py
// Do not edit this file manually.
// </auto-generated>

#nullable enable

using BeyondImmersion.BannouService.Events;

namespace BeyondImmersion.BannouService.{service_pascal};

/// <summary>
/// Generated event templates for {service_pascal}Service.
/// </summary>
/// <remarks>
/// Call <see cref="RegisterAll"/> during service startup (OnRunningAsync)
/// to register event templates with IEventTemplateRegistry.
/// </remarks>
public static class {service_pascal}EventTemplates
{{
{fields_block}

    /// <summary>
    /// Registers all event templates for this service.
    /// Call this during service startup (OnRunningAsync).
    /// </summary>
    /// <param name="registry">The event template registry.</param>
    public static void RegisterAll(IEventTemplateRegistry registry)
    {{
{register_block}
    }}
}}
'''


def main():
    """Process all events schema files and generate event template code."""
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'
    plugins_dir = repo_root / 'plugins'

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    print("Generating event template registration code from x-event-template definitions...")
    print(f"  Reading from: schemas/*-events.yaml")
    print(f"  Writing to: plugins/lib-*/Generated/*EventTemplates.cs")
    print()

    generated_files = []
    errors = []

    for events_file in sorted(schema_dir.glob('*-events.yaml')):
        # Skip common events and generated lifecycle events
        if events_file.name in ('common-events.yaml',):
            continue
        if events_file.name.endswith('-lifecycle-events.yaml'):
            continue

        service_name = extract_service_name(events_file)
        schema = read_events_schema(events_file)

        if schema is None:
            continue

        templates = extract_event_templates(schema, service_name)

        if not templates:
            continue

        # Generate code
        service_pascal = to_pascal_case(service_name)
        plugin_dir = plugins_dir / f'lib-{service_name}'
        generated_dir = plugin_dir / 'Generated'

        if not plugin_dir.exists():
            print(f"  Warning: Plugin directory not found for {service_name}, skipping")
            continue

        generated_dir.mkdir(exist_ok=True)

        output_file = generated_dir / f'{service_pascal}EventTemplates.cs'

        try:
            code = generate_event_templates_code(service_name, templates)
            output_file.write_text(code)
            generated_files.append((service_name, len(templates)))
            for template in templates:
                print(f"  {service_name}: {template.template_name} -> {template.topic}")
        except Exception as e:
            errors.append(f"{service_name}: {e}")

    print()

    if errors:
        print("Errors:")
        for error in errors:
            print(f"  - {error}")
        sys.exit(1)

    if generated_files:
        print(f"Generated {len(generated_files)} event template file(s):")
        for service, count in sorted(generated_files):
            print(f"  - lib-{service}: {count} template(s)")
    else:
        print("No x-event-template definitions found in any events schemas")

    print("\nGeneration complete.")


if __name__ == '__main__':
    main()
