#!/usr/bin/env python3
"""
Generate compression callback registration code from x-compression-callback schema declarations.

This script reads x-compression-callback definitions from OpenAPI API schemas and generates
C# static classes with registration methods for compression callbacks.

Architecture:
- {service}-api.yaml: Contains x-compression-callback declaration
- plugins/lib-{service}/Generated/{Service}CompressionCallbacks.cs: Generated code

Generated Code:
- Static class: {Service}CompressionCallbacks
- Method: RegisterAsync(IResourceClient, CancellationToken) -> Task<bool>

Usage:
    python3 scripts/generate-compression-callbacks.py

The script processes all *-api.yaml files in the schemas/ directory.
"""

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


class CompressionCallbackInfo(NamedTuple):
    """Information about a compression callback declaration."""
    resource_type: str           # e.g., 'character'
    source_type: str             # e.g., 'character-personality'
    compress_endpoint: str       # e.g., '/character-personality/get-compress-data'
    compress_payload_template: str
    priority: int                # e.g., 10
    description: str             # Optional description
    decompress_endpoint: Optional[str]  # Optional
    decompress_payload_template: Optional[str]  # Optional


def to_pascal_case(name: str) -> str:
    """Convert kebab-case or snake_case to PascalCase."""
    return ''.join(
        word.capitalize()
        for word in name.replace('-', ' ').replace('_', ' ').split()
    )


def extract_service_name(api_file: Path) -> str:
    """Extract service name from API file path."""
    return api_file.stem.replace('-api', '')


def read_api_schema(api_file: Path) -> Optional[Dict[str, Any]]:
    """Read an API schema file and return its contents."""
    try:
        with open(api_file) as f:
            return yaml.load(f)
    except Exception as e:
        print(f"  Warning: Failed to read {api_file.name}: {e}")
        return None


def extract_compression_callback(schema: Dict[str, Any]) -> Optional[CompressionCallbackInfo]:
    """Extract x-compression-callback declaration from an API schema."""
    info = schema.get('info', {})
    callback = info.get('x-compression-callback')

    if not callback:
        return None

    return CompressionCallbackInfo(
        resource_type=callback.get('resourceType', ''),
        source_type=callback.get('sourceType', ''),
        compress_endpoint=callback.get('compressEndpoint', ''),
        compress_payload_template=callback.get('compressPayloadTemplate', ''),
        priority=callback.get('priority', 0),
        description=callback.get('description', ''),
        decompress_endpoint=callback.get('decompressEndpoint'),
        decompress_payload_template=callback.get('decompressPayloadTemplate'),
    )


def validate_endpoints(schema: Dict[str, Any], callback: CompressionCallbackInfo, service_name: str) -> List[str]:
    """Validate that specified endpoints exist in the schema."""
    errors = []
    paths = schema.get('paths', {})

    if callback.compress_endpoint and callback.compress_endpoint not in paths:
        errors.append(f"{service_name}: compressEndpoint '{callback.compress_endpoint}' not found in paths")

    if callback.decompress_endpoint and callback.decompress_endpoint not in paths:
        errors.append(f"{service_name}: decompressEndpoint '{callback.decompress_endpoint}' not found in paths")

    return errors


def generate_compression_callbacks_code(
    service_name: str,
    callback: CompressionCallbackInfo
) -> str:
    """Generate C# static class with compression callback registration."""
    service_pascal = to_pascal_case(service_name)

    # Escape templates for C# string literals
    compress_template_escaped = callback.compress_payload_template.replace('\\', '\\\\').replace('"', '\\"')

    # Build the request properties
    request_lines = [
        f'                    ResourceType = "{callback.resource_type}",',
        f'                    SourceType = "{callback.source_type}",',
        f'                    ServiceName = "{callback.source_type}",',
        f'                    CompressEndpoint = "{callback.compress_endpoint}",',
        f'                    CompressPayloadTemplate = "{compress_template_escaped}",',
        f'                    Priority = {callback.priority},',
    ]

    if callback.description:
        description_escaped = callback.description.replace('\\', '\\\\').replace('"', '\\"')
        request_lines.append(f'                    Description = "{description_escaped}",')

    if callback.decompress_endpoint:
        request_lines.append(f'                    DecompressEndpoint = "{callback.decompress_endpoint}",')

    if callback.decompress_payload_template:
        decompress_template_escaped = callback.decompress_payload_template.replace('\\', '\\\\').replace('"', '\\"')
        request_lines.append(f'                    DecompressPayloadTemplate = "{decompress_template_escaped}",')

    # Remove trailing comma from last line
    request_lines[-1] = request_lines[-1].rstrip(',')

    request_block = '\n'.join(request_lines)

    return f'''// <auto-generated>
// This code was generated by generate-compression-callbacks.py
// Do not edit this file manually.
// </auto-generated>

#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Resource;

namespace BeyondImmersion.BannouService.{service_pascal};

/// <summary>
/// Generated compression callback registration for {service_pascal}Service.
/// </summary>
/// <remarks>
/// Call <see cref="RegisterAsync"/> during service startup (OnRunningAsync)
/// to register compression callbacks with lib-resource.
/// </remarks>
public static class {service_pascal}CompressionCallbacks
{{
    /// <summary>
    /// Registers compression callbacks with lib-resource.
    /// Call this during service startup (OnRunningAsync).
    /// </summary>
    /// <param name="resourceClient">The resource service client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if registration succeeded, false on failure.</returns>
    public static async Task<bool> RegisterAsync(
        IResourceClient resourceClient,
        CancellationToken cancellationToken = default)
    {{
        try
        {{
            await resourceClient.DefineCompressCallbackAsync(
                new DefineCompressCallbackRequest
                {{
{request_block}
                }},
                cancellationToken);
            return true;
        }}
        catch (ApiException)
        {{
            return false;
        }}
    }}
}}
'''


def main():
    """Process all API schema files and generate compression callback code."""
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'
    plugins_dir = repo_root / 'plugins'

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    print("Generating compression callback registration code from x-compression-callback definitions...")
    print(f"  Reading from: schemas/*-api.yaml")
    print(f"  Writing to: plugins/lib-*/Generated/*CompressionCallbacks.cs")
    print()

    generated_files = []
    validation_errors = []
    errors = []

    for api_file in sorted(schema_dir.glob('*-api.yaml')):
        if api_file.name == 'common-api.yaml':
            continue

        service_name = extract_service_name(api_file)
        schema = read_api_schema(api_file)

        if schema is None:
            continue

        callback = extract_compression_callback(schema)

        if callback is None:
            continue

        # Validate endpoints exist
        endpoint_errors = validate_endpoints(schema, callback, service_name)
        if endpoint_errors:
            validation_errors.extend(endpoint_errors)
            continue

        # Generate code
        service_pascal = to_pascal_case(service_name)
        plugin_dir = plugins_dir / f'lib-{service_name}'
        generated_dir = plugin_dir / 'Generated'

        if not plugin_dir.exists():
            print(f"  Warning: Plugin directory not found for {service_name}, skipping")
            continue

        generated_dir.mkdir(exist_ok=True)

        output_file = generated_dir / f'{service_pascal}CompressionCallbacks.cs'

        try:
            code = generate_compression_callbacks_code(service_name, callback)
            output_file.write_text(code)
            generated_files.append((service_name, callback.resource_type, callback.priority))
            print(f"  {service_name}: Generated compression callback (resourceType={callback.resource_type}, priority={callback.priority})")
        except Exception as e:
            errors.append(f"{service_name}: {e}")

    print()

    if validation_errors:
        print("Validation errors (endpoints not found in schema):")
        for error in validation_errors:
            print(f"  - {error}")
        sys.exit(1)

    if errors:
        print("Errors:")
        for error in errors:
            print(f"  - {error}")
        sys.exit(1)

    if generated_files:
        print(f"Generated {len(generated_files)} compression callback file(s):")
        for service, resource_type, priority in sorted(generated_files, key=lambda x: (x[1], x[2])):
            print(f"  - lib-{service}: {resource_type} (priority={priority})")
    else:
        print("No x-compression-callback definitions found in any API schemas")

    print("\nGeneration complete.")


if __name__ == '__main__':
    main()
