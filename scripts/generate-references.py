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
Generate resource reference tracking code from x-references schema declarations.

This script reads x-references definitions from OpenAPI API schemas and generates
C# partial classes with helper methods for publishing reference events and
registering cleanup callbacks.

Architecture:
- {service}-api.yaml: Contains x-references declarations (consumer services L3/L4)
- {service}-api.yaml: Contains x-resource-lifecycle declarations (foundational services L2)
- plugins/lib-{service}/Generated/{Service}ReferenceTracking.cs: Generated code

Generated Code:
- Helper methods: Register{Target}ReferenceAsync(), Unregister{Target}ReferenceAsync()
- Cleanup callback registration method: RegisterResourceCleanupCallbacksAsync()

Usage:
    python3 scripts/generate-references.py

The script processes all *-api.yaml files in the schemas/ directory.
"""

import re
import sys
from pathlib import Path
from typing import Any, Dict, List, Optional, NamedTuple

# Use ruamel.yaml to preserve formatting and comments
try:
    from ruamel.yaml import YAML
    yaml = YAML()
    yaml.preserve_quotes = True
except ImportError:
    print("ERROR: ruamel.yaml is required. Install with: pip install ruamel.yaml")
    sys.exit(1)


class ReferenceInfo(NamedTuple):
    """Information about a resource reference declaration."""
    target: str           # e.g., 'character'
    source_type: str      # e.g., 'actor'
    field: str            # e.g., 'characterId'
    on_delete: str        # e.g., 'cascade'
    cleanup_endpoint: str # e.g., '/actor/cleanup-by-character'
    payload_template: str # e.g., '{"characterId": "{{resourceId}}"}'


class ResourceLifecycleInfo(NamedTuple):
    """Information about a resource lifecycle declaration."""
    resource_type: str        # e.g., 'character'
    grace_period_seconds: int # e.g., 604800
    cleanup_policy: str       # e.g., 'BEST_EFFORT'


def to_pascal_case(name: str) -> str:
    """Convert kebab-case or snake_case to PascalCase."""
    return ''.join(
        word.capitalize()
        for word in name.replace('-', ' ').replace('_', ' ').split()
    )


def to_camel_case(name: str) -> str:
    """Convert kebab-case or snake_case to camelCase."""
    pascal = to_pascal_case(name)
    return pascal[0].lower() + pascal[1:] if pascal else ''


def extract_service_name(api_file: Path) -> str:
    """Extract service name from API file path (e.g., 'actor' from 'actor-api.yaml')."""
    return api_file.stem.replace('-api', '')


def read_api_schema(api_file: Path) -> Optional[Dict[str, Any]]:
    """Read an API schema file and return its contents."""
    try:
        with open(api_file) as f:
            return yaml.load(f)
    except Exception as e:
        print(f"  Warning: Failed to read {api_file.name}: {e}")
        return None


def extract_references(schema: Dict[str, Any]) -> List[ReferenceInfo]:
    """Extract x-references declarations from an API schema."""
    info = schema.get('info', {})
    x_refs = info.get('x-references', [])

    if not x_refs:
        return []

    references = []
    for ref in x_refs:
        cleanup = ref.get('cleanup', {})
        references.append(ReferenceInfo(
            target=ref.get('target', ''),
            source_type=ref.get('sourceType', ''),
            field=ref.get('field', ''),
            on_delete=ref.get('onDelete', 'cascade'),
            cleanup_endpoint=cleanup.get('endpoint', ''),
            payload_template=cleanup.get('payloadTemplate', ''),
        ))

    return references


def extract_resource_lifecycle(schema: Dict[str, Any]) -> Optional[ResourceLifecycleInfo]:
    """Extract x-resource-lifecycle declaration from an API schema."""
    info = schema.get('info', {})
    lifecycle = info.get('x-resource-lifecycle')

    if not lifecycle:
        return None

    return ResourceLifecycleInfo(
        resource_type=lifecycle.get('resourceType', ''),
        grace_period_seconds=lifecycle.get('gracePeriodSeconds', 604800),
        cleanup_policy=lifecycle.get('cleanupPolicy', 'BEST_EFFORT'),
    )


def generate_reference_tracking_code(
    service_name: str,
    references: List[ReferenceInfo]
) -> str:
    """Generate C# partial class with reference tracking helper methods."""
    service_pascal = to_pascal_case(service_name)

    lines = [
        "// <auto-generated>",
        "// This code was generated by generate-references.py",
        "// Do not edit this file manually.",
        "// </auto-generated>",
        "",
        "#nullable enable",
        "",
        "using BeyondImmersion.Bannou.Core;",
        "using BeyondImmersion.BannouService;",
        "using BeyondImmersion.BannouService.Events;",
        "using BeyondImmersion.BannouService.Messaging;",
        "using BeyondImmersion.BannouService.Resource;",
        "",
        f"namespace BeyondImmersion.BannouService.{service_pascal};",
        "",
        "/// <summary>",
        f"/// Generated partial class providing resource reference tracking helpers for {service_pascal}Service.",
        "/// </summary>",
        "/// <remarks>",
        "/// <para>",
        "/// Call Register*ReferenceAsync() after creating an entity that references a foundational resource.",
        "/// Call Unregister*ReferenceAsync() before deleting an entity that references a foundational resource.",
        "/// Call RegisterResourceCleanupCallbacksAsync() during service startup to register cleanup callbacks.",
        "/// </para>",
        "/// </remarks>",
        f"public partial class {service_pascal}Service",
        "{",
    ]

    # Generate helper methods for each reference
    for ref in references:
        target_pascal = to_pascal_case(ref.target)
        source_camel = to_camel_case(ref.source_type)
        target_camel = to_camel_case(ref.target)

        # Register method
        lines.append("")
        lines.append("    /// <summary>")
        lines.append(f"    /// Registers a reference from a {ref.source_type} entity to a {ref.target} resource.")
        lines.append("    /// Call this after creating an entity that references the resource.")
        lines.append("    /// </summary>")
        lines.append(f"    /// <param name=\"{source_camel}Id\">The ID of the {ref.source_type} entity holding the reference.</param>")
        lines.append(f"    /// <param name=\"{target_camel}Id\">The ID of the {ref.target} resource being referenced.</param>")
        lines.append("    /// <param name=\"cancellationToken\">Cancellation token.</param>")
        lines.append("    /// <returns>True if the event was published successfully.</returns>")
        lines.append(f"    protected async Task<bool> Register{target_pascal}ReferenceAsync(")
        lines.append(f"        string {source_camel}Id,")
        lines.append(f"        Guid {target_camel}Id,")
        lines.append("        CancellationToken cancellationToken = default)")
        lines.append("    {")
        lines.append("        return await _messageBus.TryPublishAsync(")
        lines.append("            \"resource.reference.registered\",")
        lines.append("            new ResourceReferenceRegisteredEvent")
        lines.append("            {")
        lines.append(f"                ResourceType = \"{ref.target}\",")
        lines.append(f"                ResourceId = {target_camel}Id,")
        lines.append(f"                SourceType = \"{ref.source_type}\",")
        lines.append(f"                SourceId = {source_camel}Id,")
        lines.append("                Timestamp = DateTimeOffset.UtcNow")
        lines.append("            },")
        lines.append("            cancellationToken);")
        lines.append("    }")

        # Unregister method
        lines.append("")
        lines.append("    /// <summary>")
        lines.append(f"    /// Unregisters a reference from a {ref.source_type} entity to a {ref.target} resource.")
        lines.append("    /// Call this before deleting an entity that references the resource.")
        lines.append("    /// </summary>")
        lines.append(f"    /// <param name=\"{source_camel}Id\">The ID of the {ref.source_type} entity releasing the reference.</param>")
        lines.append(f"    /// <param name=\"{target_camel}Id\">The ID of the {ref.target} resource being dereferenced.</param>")
        lines.append("    /// <param name=\"cancellationToken\">Cancellation token.</param>")
        lines.append("    /// <returns>True if the event was published successfully.</returns>")
        lines.append(f"    protected async Task<bool> Unregister{target_pascal}ReferenceAsync(")
        lines.append(f"        string {source_camel}Id,")
        lines.append(f"        Guid {target_camel}Id,")
        lines.append("        CancellationToken cancellationToken = default)")
        lines.append("    {")
        lines.append("        return await _messageBus.TryPublishAsync(")
        lines.append("            \"resource.reference.unregistered\",")
        lines.append("            new ResourceReferenceUnregisteredEvent")
        lines.append("            {")
        lines.append(f"                ResourceType = \"{ref.target}\",")
        lines.append(f"                ResourceId = {target_camel}Id,")
        lines.append(f"                SourceType = \"{ref.source_type}\",")
        lines.append(f"                SourceId = {source_camel}Id,")
        lines.append("                Timestamp = DateTimeOffset.UtcNow")
        lines.append("            },")
        lines.append("            cancellationToken);")
        lines.append("    }")

    # Check if any references have cleanup endpoints
    has_cleanup = any(ref.cleanup_endpoint for ref in references)

    if has_cleanup:
        # Generate cleanup callback registration method
        lines.append("")
        lines.append("    // ═══════════════════════════════════════════════════════════════════════════")
        lines.append("    // Cleanup Callback Registration")
        lines.append("    // ═══════════════════════════════════════════════════════════════════════════")
        lines.append("")
        lines.append("    /// <summary>")
        lines.append("    /// Registers cleanup callbacks with lib-resource for all resource references.")
        lines.append("    /// Call this during service startup or when the IResourceClient becomes available.")
        lines.append("    /// </summary>")
        lines.append("    /// <param name=\"resourceClient\">The resource service client.</param>")
        lines.append("    /// <param name=\"cancellationToken\">Cancellation token.</param>")
        lines.append("    /// <returns>True if all callbacks were registered successfully.</returns>")
        lines.append("    public static async Task<bool> RegisterResourceCleanupCallbacksAsync(")
        lines.append("        IResourceClient resourceClient,")
        lines.append("        CancellationToken cancellationToken = default)")
        lines.append("    {")
        lines.append("        var allSucceeded = true;")

        for ref in references:
            if not ref.cleanup_endpoint:
                continue

            # Escape the payload template for C# string literal
            escaped_template = ref.payload_template.replace('\\', '\\\\').replace('"', '\\"')

            lines.append("")
            lines.append(f"        // Register cleanup callback for {ref.target} references")
            lines.append("        try")
            lines.append("        {")
            lines.append("            await resourceClient.DefineCleanupCallbackAsync(")
            lines.append("                new DefineCleanupRequest")
            lines.append("                {")
            lines.append(f"                    ResourceType = \"{ref.target}\",")
            lines.append(f"                    SourceType = \"{ref.source_type}\",")
            lines.append(f"                    OnDeleteAction = OnDeleteAction.{ref.on_delete.upper()},")
            lines.append(f"                    ServiceName = \"{service_name}\",")
            lines.append(f"                    CallbackEndpoint = \"{ref.cleanup_endpoint}\",")
            lines.append(f"                    PayloadTemplate = \"{escaped_template}\",")
            lines.append(f"                    Description = \"Cleanup {ref.source_type} entities referencing deleted {ref.target}\"")
            lines.append("                },")
            lines.append("                cancellationToken);")
            lines.append("        }")
            lines.append("        catch (ApiException)")
            lines.append("        {")
            lines.append("            allSucceeded = false;")
            lines.append("        }")

        lines.append("")
        lines.append("        return allSucceeded;")
        lines.append("    }")

    lines.append("}")
    lines.append("")

    return '\n'.join(lines)


def main():
    """Process all API schema files and generate reference tracking code."""
    # Find paths
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'
    plugins_dir = repo_root / 'plugins'

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    print("Generating resource reference tracking code from x-references definitions...")
    print(f"  Reading from: schemas/*-api.yaml")
    print(f"  Writing to: plugins/lib-*/Generated/*ReferenceTracking.cs")
    print()

    generated_files = []
    lifecycle_configs = []
    errors = []

    # Process all API schema files
    for api_file in sorted(schema_dir.glob('*-api.yaml')):
        # Skip common-api.yaml (shared types, not a service)
        if api_file.name == 'common-api.yaml':
            continue

        service_name = extract_service_name(api_file)
        schema = read_api_schema(api_file)

        if schema is None:
            continue

        # Check for x-references
        references = extract_references(schema)

        if references:
            # Generate reference tracking code
            service_pascal = to_pascal_case(service_name)
            plugin_dir = plugins_dir / f'lib-{service_name}'
            generated_dir = plugin_dir / 'Generated'

            if not plugin_dir.exists():
                print(f"  Warning: Plugin directory not found for {service_name}, skipping")
                continue

            generated_dir.mkdir(exist_ok=True)

            output_file = generated_dir / f'{service_pascal}ReferenceTracking.cs'

            try:
                code = generate_reference_tracking_code(service_name, references)
                output_file.write_text(code)
                generated_files.append((service_name, [r.target for r in references]))
                print(f"  {service_name}: Generated reference tracking for {', '.join(r.target for r in references)}")
            except Exception as e:
                errors.append(f"{service_name}: {e}")

        # Check for x-resource-lifecycle (just log for now, no code generation needed)
        lifecycle = extract_resource_lifecycle(schema)

        if lifecycle:
            lifecycle_configs.append((service_name, lifecycle))
            print(f"  {service_name}: Found resource lifecycle config (gracePeriod={lifecycle.grace_period_seconds}s, policy={lifecycle.cleanup_policy})")

    # Report results
    print()

    if errors:
        print("Errors:")
        for error in errors:
            print(f"  {error}")
        sys.exit(1)

    if generated_files:
        print(f"Generated {len(generated_files)} reference tracking file(s):")
        for service, targets in generated_files:
            print(f"  - lib-{service}: tracks {', '.join(targets)}")
    else:
        print("No x-references definitions found in any API schemas")

    if lifecycle_configs:
        print(f"\nFound {len(lifecycle_configs)} resource lifecycle configuration(s):")
        for service, config in lifecycle_configs:
            print(f"  - {service}: {config.resource_type} (grace={config.grace_period_seconds}s, policy={config.cleanup_policy})")

    print("\nGeneration complete.")


if __name__ == '__main__':
    main()
