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
Generate Deprecation Entities Documentation from Event Schema Files.

This script scans all *-events.yaml files for x-lifecycle entities with
deprecation: true and generates a reference document listing every
deprecatable entity, its service, instanceEntity declaration, and model fields.

Output: docs/GENERATED-DEPRECATION-ENTITIES.md

Usage:
    python3 scripts/generate-deprecation-docs.py
"""

import sys
from pathlib import Path

# Use ruamel.yaml to parse YAML files
try:
    from ruamel.yaml import YAML
    yaml = YAML()
    yaml.preserve_quotes = True
except ImportError:
    print("ERROR: ruamel.yaml is required. Install with: pip install ruamel.yaml")
    sys.exit(1)


def to_title_case(name: str) -> str:
    """Convert kebab-case service name to Title Case."""
    parts = name.split('-')
    return ' '.join(p.capitalize() for p in parts)


def extract_service_name(filename: str) -> str:
    """Extract service name from events filename (e.g., 'item-events.yaml' -> 'item')."""
    name = filename.replace('.yaml', '')
    if name.endswith('-events'):
        return name[:-len('-events')]
    return name


def extract_deprecatable_entities(events_file: Path) -> list:
    """
    Extract all deprecatable entities from an events schema file.

    Returns list of dicts with keys:
        service, entity, instance_entity, topic_prefix, model_fields
    """
    with open(events_file) as f:
        content = f.read()

    if 'x-lifecycle:' not in content:
        return []

    with open(events_file) as f:
        schema = yaml.load(f)

    if schema is None or 'x-lifecycle' not in schema:
        return []

    lifecycle = schema['x-lifecycle']
    service_name = extract_service_name(events_file.name)
    topic_prefix = None
    if isinstance(lifecycle.get('topic_prefix'), str):
        topic_prefix = lifecycle['topic_prefix']

    # Collect all entity names in this file's x-lifecycle (for instanceEntity validation)
    all_entity_names = set()
    for key, value in lifecycle.items():
        if key == 'topic_prefix':
            continue
        if isinstance(value, dict):
            all_entity_names.add(key)

    results = []
    for entity_name, config in lifecycle.items():
        if entity_name == 'topic_prefix':
            continue
        if not isinstance(config, dict):
            continue
        if not config.get('deprecation', False):
            continue

        instance_entity = config.get('instanceEntity', None)
        model = config.get('model', {})
        model_fields = list(model.keys()) if isinstance(model, dict) else []

        # Validate instanceEntity reference
        instance_entity_valid = None
        if instance_entity:
            instance_entity_valid = instance_entity in all_entity_names

        results.append({
            'service': service_name,
            'entity': entity_name,
            'instance_entity': instance_entity,
            'instance_entity_valid': instance_entity_valid,
            'topic_prefix': topic_prefix,
            'model_fields': model_fields,
        })

    return results


def generate_markdown(all_entities: list) -> str:
    """Generate markdown documentation for GENERATED-DEPRECATION-ENTITIES.md."""
    lines = [
        "# Generated Deprecation Entities Reference",
        "",
        "> **Source**: `schemas/*-events.yaml` (x-lifecycle blocks with `deprecation: true`)",
        "> **Do not edit manually** - regenerate with `make generate-docs`",
        "",
        "This document lists every entity with `deprecation: true` in its x-lifecycle definition.",
        "Use this to identify which entities need `instanceEntity` declarations, which services",
        "need deprecation marker interfaces, and the current state of Category B compliance.",
        "",
    ]

    # Summary counts
    total = len(all_entities)
    with_instance = sum(1 for e in all_entities if e['instance_entity'])
    without_instance = total - with_instance
    invalid_refs = sum(1 for e in all_entities if e['instance_entity_valid'] is False)

    lines.extend([
        "## Summary",
        "",
        f"- **Total deprecatable entities**: {total}",
        f"- **With `instanceEntity` declared**: {with_instance}",
        f"- **Missing `instanceEntity`**: {without_instance}",
    ])

    if invalid_refs > 0:
        lines.append(f"- **Invalid `instanceEntity` references**: {invalid_refs}")

    lines.extend([
        "",
        "## All Deprecatable Entities",
        "",
        "| Service | Entity | `instanceEntity` | Status |",
        "|---------|--------|-------------------|--------|",
    ])

    for entity in sorted(all_entities, key=lambda e: (e['service'], e['entity'])):
        service = to_title_case(entity['service'])
        name = entity['entity']
        ie = entity['instance_entity']

        if ie is None:
            ie_display = "—"
            status = "Missing"
        elif entity['instance_entity_valid'] is False:
            ie_display = f"`{ie}`"
            status = "Invalid (not an x-lifecycle entity in same file)"
        else:
            ie_display = f"`{ie}`"
            status = "OK"

        lines.append(f"| {service} | `{name}` | {ie_display} | {status} |")

    # Detail section per entity
    lines.extend([
        "",
        "## Entity Details",
        "",
    ])

    for entity in sorted(all_entities, key=lambda e: (e['service'], e['entity'])):
        service = to_title_case(entity['service'])
        name = entity['entity']
        ie = entity['instance_entity'] or "*(not declared)*"
        prefix = entity['topic_prefix'] or "*(none — Pattern A)*"
        fields = entity['model_fields']

        lines.extend([
            f"### {service}: {name}",
            "",
            f"- **Service**: `{entity['service']}`",
            f"- **Schema**: `schemas/{entity['service']}-events.yaml`",
            f"- **Topic prefix**: `{prefix}`",
            f"- **Instance entity**: `{ie}`",
            f"- **Model fields**: {', '.join(f'`{f}`' for f in fields) if fields else '*(none)*'}",
            "",
        ])

    lines.extend([
        "---",
        "",
        "*This file is auto-generated from x-lifecycle definitions. See [TENETS.md](reference/TENETS.md) for deprecation lifecycle rules.*",
        "",
    ])

    return '\n'.join(lines)


def main():
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    all_entities = []

    for events_file in sorted(schema_dir.glob('*-events.yaml')):
        # Skip generated and client events
        if '-lifecycle-events' in events_file.name or '-client-events' in events_file.name:
            continue

        try:
            entities = extract_deprecatable_entities(events_file)
            all_entities.extend(entities)
        except Exception as e:
            print(f"  WARNING: Failed to process {events_file.name}: {e}")

    # Generate documentation
    markdown = generate_markdown(all_entities)
    md_output = repo_root / 'docs' / 'GENERATED-DEPRECATION-ENTITIES.md'
    with open(md_output, 'w') as f:
        f.write(markdown)
    print(f"Generated {md_output}")
    print(f"Processed {len(all_entities)} deprecatable entities")


if __name__ == '__main__':
    main()
