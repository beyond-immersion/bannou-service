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
Generate Metadata Properties Documentation from Schema Files.

This script scans all schema YAML files for properties with
`additionalProperties: true` and generates a tracking document for T29
(No Metadata Bag Contracts) compliance. Each metadata bag property is
categorized as compliant or non-compliant based on whether its description
contains the required compliance marker text.

Output: docs/GENERATED-METADATA-PROPERTIES.md

Usage:
    python3 scripts/generate-metadata-docs.py
"""

import sys
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


# Compliance marker text that should appear in descriptions of legitimate metadata bags
COMPLIANCE_MARKERS = [
    "no bannou plugin reads specific keys",
    "client-only",
    "opaque to all bannou plugins",
]

# Layer ordering for display
LAYER_ORDER = [
    ('Infrastructure', 'Infrastructure (L0)'),
    ('AppFoundation', 'App Foundation (L1)'),
    ('GameFoundation', 'Game Foundation (L2)'),
    ('AppFeatures', 'App Features (L3)'),
    ('GameFeatures', 'Game Features (L4)'),
]


def to_title_case(name: str) -> str:
    """Convert kebab-case service name to Title Case."""
    parts = name.split('-')
    return ' '.join(p.capitalize() for p in parts)


def extract_service_name(filename: str) -> str:
    """Extract service name from any schema filename.

    Examples:
        contract-api.yaml -> contract
        contract-events.yaml -> contract
        contract-configuration.yaml -> contract
        contract-client-events.yaml -> contract
        common-events.yaml -> common
        common-api.yaml -> common
    """
    name = filename.replace('.yaml', '')
    for suffix in ['-api', '-events', '-configuration', '-client-events']:
        if name.endswith(suffix):
            return name[:-len(suffix)]
    return name


def extract_schema_type(filename: str) -> str:
    """Extract the schema type (api, events, configuration, client-events) from filename."""
    name = filename.replace('.yaml', '')
    for suffix in ['api', 'events', 'configuration', 'client-events']:
        if name.endswith(suffix):
            return suffix
    return 'other'


def is_compliant(description: str) -> bool:
    """Check if a property description contains a compliance marker."""
    if not description:
        return False
    lower = description.lower()
    return any(marker in lower for marker in COMPLIANCE_MARKERS)


def scan_schema_for_metadata(yaml_file: Path) -> list:
    """Scan a single schema file for additionalProperties: true properties.

    Returns a list of dicts with property information.
    """
    results = []

    try:
        with open(yaml_file) as f:
            content = yaml.load(f)

        if content is None:
            return results

        schemas = content.get('components', {}).get('schemas', {})
        if not schemas:
            return results

        for schema_name, schema_def in schemas.items():
            if not isinstance(schema_def, dict):
                continue

            properties = schema_def.get('properties', {})
            if not isinstance(properties, dict):
                continue

            for prop_name, prop_info in properties.items():
                if not isinstance(prop_info, dict):
                    continue

                if prop_info.get('additionalProperties') is True:
                    description = prop_info.get('description', '')
                    results.append({
                        'schema_file': yaml_file.name,
                        'schema_type': schema_name,
                        'property': prop_name,
                        'description': description,
                        'compliant': is_compliant(description),
                    })

    except Exception as e:
        print(f"Warning: Failed to parse {yaml_file}: {e}")

    return results


def resolve_service_layers(schemas_dir: Path) -> dict:
    """Build a mapping of service name -> layer from *-api.yaml files."""
    layers = {}

    for yaml_file in sorted(schemas_dir.glob('*-api.yaml')):
        try:
            with open(yaml_file) as f:
                content = yaml.load(f)

            if content is None:
                continue

            service = extract_service_name(yaml_file.name)
            layer = content.get('x-service-layer', '')
            if layer:
                layers[service] = layer

        except Exception as e:
            print(f"Warning: Failed to read layer from {yaml_file}: {e}")

    return layers


def generate_markdown(properties_by_service: dict, service_layers: dict) -> str:
    """Generate markdown documentation from metadata property data."""
    lines = [
        "# Generated Metadata Properties Reference",
        "",
        "> **Source**: `schemas/*.yaml`",
        "> **Do not edit manually** - regenerate with `make generate-docs`",
        "",
        "This document tracks all `additionalProperties: true` properties across Bannou schemas",
        "for FOUNDATION TENETS (T29: No Metadata Bag Contracts) compliance.",
        "",
        "## What Metadata Bags Are For",
        "",
        "Per T29, `additionalProperties: true` serves exactly **two** legitimate purposes:",
        "",
        "1. **Client-side display data** - Game clients store rendering hints, UI preferences,",
        "   or display-only information that no Bannou service consumes.",
        "2. **Game-specific implementation data** - Data the game engine (not Bannou services)",
        "   interprets at runtime. Opaque to all Bannou plugins.",
        "",
        "In both cases, the metadata is **opaque to all Bannou plugins**. No plugin reads",
        "specific keys by convention. No plugin's correctness depends on its structure.",
        "",
        "### Compliance Marker",
        "",
        "Compliant properties include one of these phrases in their description:",
        "- \"No Bannou plugin reads specific keys\"",
        "- \"Client-only\"",
        "- \"Opaque to all Bannou plugins\"",
        "",
    ]

    # Compute totals
    total = 0
    compliant_count = 0
    non_compliant_count = 0
    all_non_compliant = []

    for service, props in properties_by_service.items():
        for p in props:
            total += 1
            if p['compliant']:
                compliant_count += 1
            else:
                non_compliant_count += 1
                all_non_compliant.append({**p, 'service': service})

    pct = f"{(compliant_count / total * 100):.0f}" if total > 0 else "0"

    lines.extend([
        "## Compliance Summary",
        "",
        f"| Metric | Count |",
        f"|--------|-------|",
        f"| Total metadata bag properties | {total} |",
        f"| Compliant (has marker) | {compliant_count} |",
        f"| Non-compliant (missing marker) | {non_compliant_count} |",
        f"| Compliance rate | {pct}% |",
        "",
    ])

    # Group services by layer
    layer_services = defaultdict(list)
    for service in properties_by_service:
        layer = service_layers.get(service, '')
        layer_services[layer].append(service)

    # Per-layer sections
    lines.extend([
        "## Properties by Service",
        "",
    ])

    for layer_key, layer_display in LAYER_ORDER:
        services_in_layer = sorted(layer_services.get(layer_key, []))
        if not services_in_layer:
            continue

        lines.append(f"### {layer_display}")
        lines.append("")

        for service in services_in_layer:
            props = properties_by_service[service]
            lines.append(f"#### {to_title_case(service)}")
            lines.append("")
            lines.append("| Schema Type | Property | Schema File | Compliant | Description |")
            lines.append("|-------------|----------|-------------|-----------|-------------|")

            for p in sorted(props, key=lambda x: (x['schema_file'], x['schema_type'], x['property'])):
                desc = p['description']
                # Truncate long descriptions for table readability
                if len(desc) > 80:
                    desc = desc[:77] + "..."
                # Escape pipe characters in descriptions
                desc = desc.replace('|', '\\|').replace('\n', ' ')
                mark = "Y" if p['compliant'] else "**N**"
                lines.append(
                    f"| `{p['schema_type']}` | `{p['property']}` | "
                    f"`{p['schema_file']}` | {mark} | {desc} |"
                )

            lines.append("")

    # Services with no layer (common schemas, etc.)
    unlayered = sorted(layer_services.get('', []))
    if unlayered:
        lines.append("### Common / Shared Schemas")
        lines.append("")

        for service in unlayered:
            props = properties_by_service[service]
            lines.append(f"#### {to_title_case(service)}")
            lines.append("")
            lines.append("| Schema Type | Property | Schema File | Compliant | Description |")
            lines.append("|-------------|----------|-------------|-----------|-------------|")

            for p in sorted(props, key=lambda x: (x['schema_file'], x['schema_type'], x['property'])):
                desc = p['description']
                if len(desc) > 80:
                    desc = desc[:77] + "..."
                desc = desc.replace('|', '\\|').replace('\n', ' ')
                mark = "Y" if p['compliant'] else "**N**"
                lines.append(
                    f"| `{p['schema_type']}` | `{p['property']}` | "
                    f"`{p['schema_file']}` | {mark} | {desc} |"
                )

            lines.append("")

    # Structural exceptions section (investigated and determined not T29-applicable)
    lines.extend([
        "## Structural Exceptions",
        "",
        "These properties use `additionalProperties: true` for structural reasons that are",
        "fundamentally different from metadata bags. They are **not subject to T29** because",
        "they are not data contracts between services -- they are the infrastructure primitives",
        "that other services build on, or they represent genuinely polymorphic payloads whose",
        "shape is defined by authored content rather than service convention.",
        "",
        "### Infrastructure Primitives",
        "",
        "These exist because the service's entire purpose is storing/forwarding arbitrary data:",
        "",
        "| Service | Property | Why Exempt |",
        "|---------|----------|-----------|",
        "| **State** (L0) | `SaveStateRequest.value`, `GetStateResponse.value`, `BulkSaveItem.value`, `BulkStateItem.value` | Key-value store. The entire purpose is to persist any JSON value. Not a metadata bag -- it IS the storage primitive. |",
        "| **Messaging** (L0) | `PublishEventRequest.payload` | Generic pub/sub bus. Must accept any event shape for forwarding. Wrapped opaquely in `GenericMessageEnvelope`. |",
        "| **Connect** (L1) | `InternalProxyRequest.body` | HTTP proxy forwarding body. Must accept any shape because it forwards arbitrary service request payloads. |",
        "",
        "### Polymorphic Response Data",
        "",
        "These vary by a typed discriminator field, not by cross-service convention:",
        "",
        "| Service | Property | Why Exempt |",
        "|---------|----------|-----------|",
        "| **Connect** (L1) | `GetEndpointMetaResponse.data` | Structure varies by `metaType` discriminator. Each meta type has a known shape. |",
        "| **Common** | `ServiceErrorEvent.details` | Diagnostic context that varies per error type. No service reads other services' error details by key. |",
        "",
        "### Behavior-Authored Content",
        "",
        "These are free-form because the keys are defined by ABML behavior documents (authored",
        "YAML content), not by any service schema. Different NPCs have different keys:",
        "",
        "| Service | Property | Why Exempt |",
        "|---------|----------|-----------|",
        "| **Behavior** (L4) | `GoapPlanRequest.worldState`, `ValidateGoapPlanRequest.worldState`, `CharacterContext.worldState` | GOAP world state keys (e.g., `hunger`, `gold`, `in_combat`) are defined per-NPC by behavior documents. The planner iterates them generically. |",
        "| **Common** | `GoalState.goalParameters`, `MemoryUpdate.memoryValue` | Actor internal state populated by behavior execution. Keys are behavior-authored. |",
        "",
    ])

    # Non-compliant properties section
    if all_non_compliant:
        lines.extend([
            "## Non-Compliant Properties",
            "",
            "These properties are missing the compliance marker text in their description.",
            "Each needs investigation: is it a legitimate metadata bag missing the marker,",
            "or is it being misused as a cross-service data contract?",
            "",
            "| Service | Schema Type | Property | Schema File | Description |",
            "|---------|-------------|----------|-------------|-------------|",
        ])

        for p in sorted(all_non_compliant, key=lambda x: (x['service'], x['schema_file'], x['property'])):
            desc = p['description']
            if len(desc) > 80:
                desc = desc[:77] + "..."
            desc = desc.replace('|', '\\|').replace('\n', ' ')
            lines.append(
                f"| {to_title_case(p['service'])} | `{p['schema_type']}` | "
                f"`{p['property']}` | `{p['schema_file']}` | {desc} |"
            )

        lines.append("")

    lines.extend([
        "---",
        "",
        "*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for T29 (No Metadata Bag Contracts).*",
        "",
    ])

    return '\n'.join(lines)


def main():
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schemas_dir = repo_root / 'schemas'

    # Build service layer mapping from *-api.yaml files
    service_layers = resolve_service_layers(schemas_dir)

    # Scan all schema files for additionalProperties: true
    properties_by_service = defaultdict(list)
    total_found = 0

    for yaml_file in sorted(schemas_dir.glob('*.yaml')):
        # Skip generated files
        if 'Generated' in str(yaml_file):
            continue
        # Skip non-service schema files (state-stores, variable-providers, etc.)
        # but include common-* files
        service = extract_service_name(yaml_file.name)

        results = scan_schema_for_metadata(yaml_file)
        if results:
            properties_by_service[service].extend(results)
            total_found += len(results)

    if total_found == 0:
        print("Warning: No additionalProperties: true properties found in schemas")

    # Generate markdown
    markdown = generate_markdown(properties_by_service, service_layers)

    # Write output
    output_file = repo_root / 'docs' / 'GENERATED-METADATA-PROPERTIES.md'
    with open(output_file, 'w') as f:
        f.write(markdown)

    services_count = len(properties_by_service)
    print(f"Generated {output_file} with {total_found} metadata properties across {services_count} services")


if __name__ == '__main__':
    main()
