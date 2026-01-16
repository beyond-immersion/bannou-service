#!/usr/bin/env python3
"""
Generate typed service proxy classes for the Bannou Client SDK.

This script reads OpenAPI schemas from schemas/*-api.yaml and generates
proxy classes that provide typed wrappers around IBannouClient.InvokeAsync.

Architecture:
- Each service gets a proxy class (e.g., CharacterProxy, AuthProxy)
- Proxy methods call InvokeAsync with proper request/response types
- Extension file adds lazy-initialized proxy properties to BannouClient

Output:
- sdks/client/Generated/Proxies/{Service}Proxy.cs - One per service
- sdks/client/Generated/BannouClientExtensions.cs - Proxy property accessors

Usage:
    python3 scripts/generate-client-proxies.py

The script processes all *-api.yaml files in the schemas/ directory.
"""

import re
import sys
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

try:
    from ruamel.yaml import YAML
    yaml = YAML()
    yaml.preserve_quotes = True
except ImportError:
    print("ERROR: ruamel.yaml is required. Install with: pip install ruamel.yaml")
    sys.exit(1)


def to_pascal_case(name: str) -> str:
    """Convert kebab-case or snake_case to PascalCase."""
    # Handle kebab-case
    parts = name.replace('_', '-').split('-')
    return ''.join(p.capitalize() for p in parts)


def to_camel_case(name: str) -> str:
    """Convert to camelCase."""
    pascal = to_pascal_case(name)
    return pascal[0].lower() + pascal[1:] if pascal else ''


def extract_type_name(ref: str) -> str:
    """Extract type name from $ref string like '#/components/schemas/LoginRequest'."""
    if not ref or not ref.startswith('#/components/schemas/'):
        return ''
    return ref.split('/')[-1]


def operation_id_to_method_name(operation_id: str) -> str:
    """Convert operationId to method name (e.g., 'createCharacter' -> 'CreateAsync')."""
    # Handle common prefixes that should be kept
    prefixes = ['get', 'list', 'create', 'update', 'delete', 'validate', 'login', 'register', 'logout']

    # Find and capitalize the prefix
    for prefix in prefixes:
        if operation_id.lower().startswith(prefix):
            # Capitalize the prefix and the rest
            rest = operation_id[len(prefix):]
            return prefix.capitalize() + to_pascal_case(rest) + 'Async'

    # Default: just capitalize
    return to_pascal_case(operation_id) + 'Async'


class EndpointInfo:
    """Information about a single API endpoint."""

    def __init__(
        self,
        method: str,
        path: str,
        operation_id: str,
        request_type: Optional[str],
        response_type: Optional[str],
        summary: str,
        tag: str
    ):
        self.method = method.upper()
        self.path = path
        self.operation_id = operation_id
        self.request_type = request_type
        self.response_type = response_type
        self.summary = summary
        self.tag = tag

    @property
    def method_name(self) -> str:
        """Get C# method name for this endpoint."""
        return operation_id_to_method_name(self.operation_id)


class ServiceInfo:
    """Information about a service extracted from its OpenAPI schema."""

    def __init__(self, name: str, title: str, description: str):
        self.name = name  # e.g., 'auth'
        self.title = title
        self.description = description
        self.endpoints: List[EndpointInfo] = []

    @property
    def pascal_name(self) -> str:
        """Get PascalCase name (e.g., 'GameSession')."""
        return to_pascal_case(self.name)

    @property
    def proxy_class_name(self) -> str:
        """Get proxy class name (e.g., 'GameSessionProxy')."""
        return f"{self.pascal_name}Proxy"

    @property
    def field_name(self) -> str:
        """Get private field name (e.g., '_gameSession')."""
        return f"_{to_camel_case(self.name)}"

    @property
    def property_name(self) -> str:
        """Get public property name (e.g., 'GameSession')."""
        return self.pascal_name


def parse_schema(schema_path: Path) -> Optional[ServiceInfo]:
    """Parse an OpenAPI schema file and extract service information."""
    with open(schema_path) as f:
        try:
            schema = yaml.load(f)
        except Exception as e:
            print(f"  Warning: Failed to parse {schema_path.name}: {e}")
            return None

    if schema is None or 'paths' not in schema:
        return None

    # Extract service info
    service_name = schema_path.stem.replace('-api', '')
    info = schema.get('info', {})
    title = info.get('title', service_name)
    description = info.get('description', '')

    service = ServiceInfo(service_name, title, description)

    # Parse each path
    for path, methods in schema.get('paths', {}).items():
        if not isinstance(methods, dict):
            continue

        for method, details in methods.items():
            # Skip non-HTTP methods (parameters, servers, etc.)
            if method.lower() not in ('get', 'post', 'put', 'delete', 'patch'):
                continue

            if not isinstance(details, dict):
                continue

            operation_id = details.get('operationId')
            if not operation_id:
                continue

            # Extract request type
            request_type = None
            request_body = details.get('requestBody', {})
            if request_body:
                content = request_body.get('content', {})
                json_content = content.get('application/json', {})
                schema_ref = json_content.get('schema', {})
                if isinstance(schema_ref, dict) and '$ref' in schema_ref:
                    request_type = extract_type_name(schema_ref['$ref'])

            # Extract response type from 200 response
            response_type = None
            responses = details.get('responses', {})
            success_response = responses.get('200', {})
            if success_response:
                content = success_response.get('content', {})
                json_content = content.get('application/json', {})
                schema_ref = json_content.get('schema', {})
                if isinstance(schema_ref, dict) and '$ref' in schema_ref:
                    response_type = extract_type_name(schema_ref['$ref'])

            summary = details.get('summary', '')
            tags = details.get('tags', [])
            tag = tags[0] if tags else ''

            endpoint = EndpointInfo(
                method=method,
                path=path,
                operation_id=operation_id,
                request_type=request_type,
                response_type=response_type,
                summary=summary,
                tag=tag
            )
            service.endpoints.append(endpoint)

    return service if service.endpoints else None


def generate_proxy_class(service: ServiceInfo) -> str:
    """Generate C# proxy class for a service."""
    # Service-specific model namespace
    models_namespace = f"BeyondImmersion.BannouService.{service.pascal_name}"

    lines = [
        "// <auto-generated>",
        "// This code was generated by generate-client-proxies.py",
        "// Do not edit this file manually.",
        "// </auto-generated>",
        "",
        "#nullable enable",
        "",
        "using BeyondImmersion.Bannou.Core;",
        f"using {models_namespace};",
        "",
        "namespace BeyondImmersion.Bannou.Client.Proxies;",
        "",
        "/// <summary>",
        f"/// Typed proxy for {service.title} API endpoints.",
        "/// </summary>",
        f"public sealed class {service.proxy_class_name}",
        "{",
        "    private readonly IBannouClient _client;",
        "",
        "    /// <summary>",
        f"    /// Creates a new {service.proxy_class_name} instance.",
        "    /// </summary>",
        "    /// <param name=\"client\">The underlying Bannou client.</param>",
        f"    internal {service.proxy_class_name}(IBannouClient client)",
        "    {",
        "        _client = client ?? throw new ArgumentNullException(nameof(client));",
        "    }",
    ]

    # Generate methods for each endpoint
    for endpoint in service.endpoints:
        # Skip endpoints without proper request/response types (unusual GET endpoints, etc.)
        if not endpoint.request_type and not endpoint.response_type:
            continue

        lines.append("")

        # Generate XML docs based on method type
        lines.append("    /// <summary>")
        if endpoint.summary:
            lines.append(f"    /// {endpoint.summary}")
        else:
            lines.append(f"    /// Invokes {endpoint.method} {endpoint.path}")
        lines.append("    /// </summary>")

        # Generate method signature based on endpoint type
        if endpoint.request_type and endpoint.response_type:
            # Full request/response method
            lines.append("    /// <param name=\"request\">The request payload.</param>")
            lines.append("    /// <param name=\"channel\">Message channel for ordering (default 0).</param>")
            lines.append("    /// <param name=\"timeout\">Request timeout.</param>")
            lines.append("    /// <param name=\"cancellationToken\">Cancellation token.</param>")
            lines.append(f"    /// <returns>ApiResponse containing {endpoint.response_type} on success.</returns>")
            lines.append(f"    public Task<ApiResponse<{endpoint.response_type}>> {endpoint.method_name}(")
            lines.append(f"        {endpoint.request_type} request,")
            lines.append("        ushort channel = 0,")
            lines.append("        TimeSpan? timeout = null,")
            lines.append("        CancellationToken cancellationToken = default)")
            lines.append("    {")
            lines.append(f"        return _client.InvokeAsync<{endpoint.request_type}, {endpoint.response_type}>(")
            lines.append(f"            \"{endpoint.method}\", \"{endpoint.path}\", request, channel, timeout, cancellationToken);")
            lines.append("    }")
        elif endpoint.response_type:
            # No request body, but has response
            lines.append("    /// <param name=\"channel\">Message channel for ordering (default 0).</param>")
            lines.append("    /// <param name=\"timeout\">Request timeout.</param>")
            lines.append("    /// <param name=\"cancellationToken\">Cancellation token.</param>")
            lines.append(f"    /// <returns>ApiResponse containing {endpoint.response_type} on success.</returns>")
            lines.append(f"    public Task<ApiResponse<{endpoint.response_type}>> {endpoint.method_name}(")
            lines.append("        ushort channel = 0,")
            lines.append("        TimeSpan? timeout = null,")
            lines.append("        CancellationToken cancellationToken = default)")
            lines.append("    {")
            lines.append(f"        return _client.InvokeAsync<object, {endpoint.response_type}>(")
            lines.append(f"            \"{endpoint.method}\", \"{endpoint.path}\", new {{}}, channel, timeout, cancellationToken);")
            lines.append("    }")
        elif endpoint.request_type:
            # Has request body, but no response type (fire-and-forget style)
            lines.append("    /// <param name=\"request\">The request payload.</param>")
            lines.append("    /// <param name=\"channel\">Message channel for ordering (default 0).</param>")
            lines.append("    /// <param name=\"cancellationToken\">Cancellation token.</param>")
            lines.append("    /// <returns>Task that completes when the event is sent.</returns>")
            lines.append(f"    public Task {endpoint.method_name.replace('Async', 'EventAsync')}(")
            lines.append(f"        {endpoint.request_type} request,")
            lines.append("        ushort channel = 0,")
            lines.append("        CancellationToken cancellationToken = default)")
            lines.append("    {")
            lines.append(f"        return _client.SendEventAsync<{endpoint.request_type}>(")
            lines.append(f"            \"{endpoint.method}\", \"{endpoint.path}\", request, channel, cancellationToken);")
            lines.append("    }")

    lines.append("}")
    lines.append("")

    return '\n'.join(lines)


def generate_extensions_class(services: List[ServiceInfo]) -> str:
    """Generate C# partial class extension with proxy properties."""
    lines = [
        "// <auto-generated>",
        "// This code was generated by generate-client-proxies.py",
        "// Do not edit this file manually.",
        "// </auto-generated>",
        "",
        "#nullable enable",
        "",
        "using BeyondImmersion.Bannou.Client.Proxies;",
        "",
        "namespace BeyondImmersion.Bannou.Client;",
        "",
        "/// <summary>",
        "/// Partial class extension providing typed service proxy properties.",
        "/// </summary>",
        "public partial class BannouClient",
        "{",
    ]

    # Generate private fields
    for service in services:
        lines.append(f"    private {service.proxy_class_name}? {service.field_name};")

    lines.append("")

    # Generate public properties
    for service in services:
        lines.append("    /// <summary>")
        lines.append(f"    /// Gets the typed proxy for {service.title} operations.")
        lines.append("    /// </summary>")
        lines.append(f"    public {service.proxy_class_name} {service.property_name} =>")
        lines.append(f"        {service.field_name} ??= new {service.proxy_class_name}(this);")
        lines.append("")

    lines.append("}")
    lines.append("")

    return '\n'.join(lines)


def main():
    """Process all API schemas and generate proxy classes."""
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'
    output_dir = repo_root / 'sdks' / 'client' / 'Generated' / 'Proxies'

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    # Create output directory
    output_dir.mkdir(parents=True, exist_ok=True)

    # Clean up old proxy files
    for old_file in output_dir.glob('*.cs'):
        old_file.unlink()
        print(f"  Cleaned up: {old_file.name}")

    print("Generating client proxies from OpenAPI schemas...")
    print(f"  Reading from: schemas/*-api.yaml")
    print(f"  Writing to: sdks/client/Generated/Proxies/")
    print()

    services: List[ServiceInfo] = []
    errors = []

    for schema_path in sorted(schema_dir.glob('*-api.yaml')):
        try:
            service = parse_schema(schema_path)
            if service is None:
                continue

            # Generate proxy class
            proxy_code = generate_proxy_class(service)
            output_file = output_dir / f"{service.proxy_class_name}.cs"

            with open(output_file, 'w', newline='\n') as f:
                f.write(proxy_code)

            services.append(service)
            print(f"  Generated {service.proxy_class_name}.cs ({len(service.endpoints)} endpoints)")

        except Exception as e:
            errors.append(f"{schema_path.name}: {e}")

    # Generate extensions class
    if services:
        extensions_code = generate_extensions_class(services)
        extensions_file = output_dir.parent / "BannouClientProxyExtensions.cs"

        with open(extensions_file, 'w', newline='\n') as f:
            f.write(extensions_code)

        print(f"\n  Generated BannouClientProxyExtensions.cs ({len(services)} proxies)")

    # Report results
    print()
    if errors:
        print("Errors:")
        for error in errors:
            print(f"  {error}")
        sys.exit(1)

    if services:
        print(f"Generated {len(services)} proxy classes with {sum(len(s.endpoints) for s in services)} total endpoints")
    else:
        print("No services found to generate proxies for")


if __name__ == '__main__':
    main()
