#!/usr/bin/env python3
"""
Generate typed service proxy classes for the Bannou TypeScript Client SDK.

This script reads OpenAPI schemas from schemas/*-api.yaml and generates
TypeScript proxy classes that provide typed wrappers around BannouClient.invokeAsync.

Architecture:
- Each service gets a proxy class (e.g., CharacterProxy, AuthProxy)
- Proxy methods call invokeAsync with proper request/response types
- Extension file adds proxy properties to BannouClient

Output:
- sdks/typescript/client/Generated/proxies/{Service}Proxy.ts - One per service
- sdks/typescript/client/Generated/proxies/index.ts - Barrel export

Usage:
    python3 scripts/generate-client-proxies-ts.py

The script processes all *-api.yaml files in the schemas/ directory.
"""

import sys
from pathlib import Path
from typing import List, Optional

try:
    from ruamel.yaml import YAML
    yaml = YAML()
    yaml.preserve_quotes = True
except ImportError:
    print("ERROR: ruamel.yaml is required. Install with: pip install ruamel.yaml")
    sys.exit(1)


def to_pascal_case(name: str) -> str:
    """Convert kebab-case, snake_case, or camelCase to PascalCase."""
    import re

    # First, handle kebab-case and snake_case by replacing delimiters with spaces
    name = name.replace('-', ' ').replace('_', ' ')

    # If no spaces (was camelCase or PascalCase), split on case boundaries
    if ' ' not in name:
        name = re.sub(r'([a-z])([A-Z])', r'\1 \2', name)

    # Split on spaces and capitalize each part, then join
    parts = name.split()
    return ''.join(p[0].upper() + p[1:] if p else '' for p in parts)


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
    """Convert operationId to method name (e.g., 'createCharacter' -> 'createCharacterAsync')."""
    return to_camel_case(operation_id) + 'Async'


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
        """Get TypeScript method name for this endpoint."""
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
    def property_name(self) -> str:
        """Get property name (e.g., 'gameSession')."""
        return to_camel_case(self.name)


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
    """Generate TypeScript proxy class for a service."""
    lines = [
        "// <auto-generated>",
        "// This code was generated by generate-client-proxies-ts.py",
        "// Do not edit this file manually.",
        "// </auto-generated>",
        "",
        "import type { IBannouClient } from '../IBannouClient.js';",
        "import type { ApiResponse } from '@beyondimmersion/bannou-core';",
        "",
        "// Import generated types (placeholder - configure openapi-typescript for actual types)",
        f"// import type {{ ... }} from '../types/{service.name}-api.js';",
        "",
        "/**",
        f" * Typed proxy for {service.title} API endpoints.",
        " */",
        f"export class {service.proxy_class_name} {{",
        "  private readonly client: IBannouClient;",
        "",
        "  /**",
        f"   * Creates a new {service.proxy_class_name} instance.",
        "   * @param client - The underlying Bannou client.",
        "   */",
        f"  constructor(client: IBannouClient) {{",
        "    this.client = client;",
        "  }",
    ]

    # Generate methods for each endpoint
    for endpoint in service.endpoints:
        # Skip endpoints without proper request/response types
        if not endpoint.request_type and not endpoint.response_type:
            continue

        lines.append("")

        # Generate JSDoc
        lines.append("  /**")
        if endpoint.summary:
            lines.append(f"   * {endpoint.summary}")
        else:
            lines.append(f"   * Invokes {endpoint.method} {endpoint.path}")

        # Generate method based on endpoint type
        if endpoint.request_type and endpoint.response_type:
            # Full request/response method
            lines.append("   * @param request - The request payload.")
            lines.append("   * @param channel - Message channel for ordering (default 0).")
            lines.append("   * @param timeout - Request timeout in milliseconds.")
            lines.append(f"   * @returns ApiResponse containing the response on success.")
            lines.append("   */")
            lines.append(f"  async {endpoint.method_name}(")
            lines.append(f"    request: {endpoint.request_type},")
            lines.append("    channel: number = 0,")
            lines.append("    timeout?: number")
            lines.append(f"  ): Promise<ApiResponse<{endpoint.response_type}>> {{")
            lines.append(f"    return this.client.invokeAsync<{endpoint.request_type}, {endpoint.response_type}>(")
            lines.append(f"      '{endpoint.method}', '{endpoint.path}', request, channel, timeout")
            lines.append("    );")
            lines.append("  }")
        elif endpoint.response_type:
            # No request body, but has response
            lines.append("   * @param channel - Message channel for ordering (default 0).")
            lines.append("   * @param timeout - Request timeout in milliseconds.")
            lines.append(f"   * @returns ApiResponse containing the response on success.")
            lines.append("   */")
            lines.append(f"  async {endpoint.method_name}(")
            lines.append("    channel: number = 0,")
            lines.append("    timeout?: number")
            lines.append(f"  ): Promise<ApiResponse<{endpoint.response_type}>> {{")
            lines.append(f"    return this.client.invokeAsync<object, {endpoint.response_type}>(")
            lines.append(f"      '{endpoint.method}', '{endpoint.path}', {{}}, channel, timeout")
            lines.append("    );")
            lines.append("  }")
        elif endpoint.request_type:
            # Has request body, but no response type (fire-and-forget style)
            lines.append("   * @param request - The request payload.")
            lines.append("   * @param channel - Message channel for ordering (default 0).")
            lines.append("   * @returns Promise that completes when the event is sent.")
            lines.append("   */")
            event_method = endpoint.method_name.replace('Async', 'EventAsync')
            lines.append(f"  async {event_method}(")
            lines.append(f"    request: {endpoint.request_type},")
            lines.append("    channel: number = 0")
            lines.append("  ): Promise<void> {")
            lines.append(f"    return this.client.sendEventAsync<{endpoint.request_type}>(")
            lines.append(f"      '{endpoint.method}', '{endpoint.path}', request, channel")
            lines.append("    );")
            lines.append("  }")

    lines.append("}")
    lines.append("")

    return '\n'.join(lines)


def generate_index_file(services: List[ServiceInfo]) -> str:
    """Generate barrel export file for all proxies."""
    lines = [
        "// <auto-generated>",
        "// This code was generated by generate-client-proxies-ts.py",
        "// Do not edit this file manually.",
        "// </auto-generated>",
        "",
    ]

    for service in services:
        lines.append(f"export {{ {service.proxy_class_name} }} from './{service.proxy_class_name}.js';")

    lines.append("")
    return '\n'.join(lines)


def main():
    """Process all API schemas and generate proxy classes."""
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'
    output_dir = repo_root / 'sdks' / 'typescript' / 'client' / 'Generated' / 'proxies'

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    # Create output directory
    output_dir.mkdir(parents=True, exist_ok=True)

    # Clean up old proxy files
    for old_file in output_dir.glob('*.ts'):
        old_file.unlink()
        print(f"  Cleaned up: {old_file.name}")

    print("Generating TypeScript client proxies from OpenAPI schemas...")
    print(f"  Reading from: schemas/*-api.yaml")
    print(f"  Writing to: sdks/typescript/client/Generated/proxies/")
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
            output_file = output_dir / f"{service.proxy_class_name}.ts"

            with open(output_file, 'w', newline='\n') as f:
                f.write(proxy_code)

            services.append(service)
            print(f"  Generated {service.proxy_class_name}.ts ({len(service.endpoints)} endpoints)")

        except Exception as e:
            errors.append(f"{schema_path.name}: {e}")

    # Generate index file
    if services:
        index_code = generate_index_file(services)
        index_file = output_dir / "index.ts"

        with open(index_file, 'w', newline='\n') as f:
            f.write(index_code)

        print(f"\n  Generated index.ts ({len(services)} proxies)")

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
