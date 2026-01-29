#!/usr/bin/env python3
"""
Generate typed service proxy classes for the Bannou TypeScript Client SDK.

This script reads the consolidated client OpenAPI schema and generates
TypeScript proxy classes that provide typed wrappers around BannouClient.invokeAsync.

Architecture:
- Reads from schemas/Generated/bannou-client-api.yaml (consolidated schema)
- Each service gets a proxy class (e.g., CharacterProxy, AuthProxy)
- Proxy methods call invokeAsync with proper request/response types
- Extension file adds proxy properties to BannouClient

Output:
- sdks/typescript/client/src/Generated/proxies/{Service}Proxy.ts - One per service
- sdks/typescript/client/src/Generated/proxies/index.ts - Barrel export
- sdks/typescript/client/src/Generated/BannouClientExtensions.ts - Proxy property accessors

Usage:
    python3 scripts/generate-client-proxies-ts.py

Prerequisites:
    Run scripts/generate-client-schema.py first to create the consolidated schema.
"""

import re
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
    """Convert kebab-case, snake_case, or camelCase to PascalCase."""
    name = name.replace('-', ' ').replace('_', ' ')
    if ' ' not in name:
        name = re.sub(r'([a-z])([A-Z])', r'\1 \2', name)
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

    def __init__(self, name: str):
        self.name = name  # e.g., 'auth'
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


def extract_service_name(path: str) -> str:
    """Extract service name from path (e.g., '/account/get' -> 'account')."""
    parts = path.strip('/').split('/')
    if parts:
        # Handle paths like /auth/oauth/{provider}/init -> auth
        return parts[0]
    return 'unknown'


def parse_consolidated_schema(schema_path: Path) -> Dict[str, ServiceInfo]:
    """Parse the consolidated schema and organize endpoints by service."""
    with open(schema_path) as f:
        schema = yaml.load(f)

    if schema is None or 'paths' not in schema:
        return {}

    services: Dict[str, ServiceInfo] = {}

    # Parse each path
    for path, methods in schema.get('paths', {}).items():
        if not isinstance(methods, dict):
            continue

        service_name = extract_service_name(path)

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

            # Create service if not exists
            if service_name not in services:
                services[service_name] = ServiceInfo(service_name)

            services[service_name].endpoints.append(endpoint)

    return services


def generate_proxy_class(service: ServiceInfo) -> str:
    """Generate TypeScript proxy class for a service."""
    lines = [
        "// <auto-generated>",
        "// This code was generated by generate-client-proxies-ts.py",
        "// Do not edit this file manually.",
        "// </auto-generated>",
        "",
        "import type { IBannouClient } from '../../IBannouClient.js';",
        "import type { ApiResponse } from '@beyondimmersion/bannou-core';",
        "import type { components } from '../types/bannou-client-api.js';",
        "",
        "// Type aliases for cleaner code",
        "type Schemas = components['schemas'];",
        "",
        "/**",
        f" * Typed proxy for {service.pascal_name} API endpoints.",
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
            lines.append(f"    request: Schemas['{endpoint.request_type}'],")
            lines.append("    channel: number = 0,")
            lines.append("    timeout?: number")
            lines.append(f"  ): Promise<ApiResponse<Schemas['{endpoint.response_type}']>> {{")
            lines.append(f"    return this.client.invokeAsync<Schemas['{endpoint.request_type}'], Schemas['{endpoint.response_type}']>(")
            lines.append(f"      '{endpoint.path}', request, channel, timeout")
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
            lines.append(f"  ): Promise<ApiResponse<Schemas['{endpoint.response_type}']>> {{")
            lines.append(f"    return this.client.invokeAsync<object, Schemas['{endpoint.response_type}']>(")
            lines.append(f"      '{endpoint.path}', {{}}, channel, timeout")
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
            lines.append(f"    request: Schemas['{endpoint.request_type}'],")
            lines.append("    channel: number = 0")
            lines.append("  ): Promise<void> {")
            lines.append(f"    return this.client.sendEventAsync<Schemas['{endpoint.request_type}']>(")
            lines.append(f"      '{endpoint.path}', request, channel")
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


def generate_extensions_file(services: List[ServiceInfo]) -> str:
    """Generate TypeScript module augmentation file that adds proxy properties to BannouClient."""
    lines = [
        "// <auto-generated>",
        "// This code was generated by generate-client-proxies-ts.py",
        "// Do not edit this file manually.",
        "// </auto-generated>",
        "",
        "/**",
        " * BannouClient Extensions",
        " *",
        " * This module augments BannouClient with lazy-initialized proxy properties",
        " * for type-safe API access. Import this module (or import from the main entry",
        " * point) to enable proxy access like `client.account.getAccountAsync(...)`.",
        " */",
        "",
        "import { BannouClient } from '../BannouClient.js';",
    ]

    # Import all proxy classes
    for service in services:
        lines.append(f"import {{ {service.proxy_class_name} }} from './proxies/{service.proxy_class_name}.js';")

    lines.append("")
    lines.append("// Symbol for private proxy cache storage")
    lines.append("const PROXY_CACHE = Symbol('proxyCache');")
    lines.append("")
    lines.append("// Interface for the proxy cache")
    lines.append("interface ProxyCache {")
    for service in services:
        lines.append(f"  {service.property_name}?: {service.proxy_class_name};")
    lines.append("}")
    lines.append("")
    lines.append("// Type for BannouClient with cache")
    lines.append("type BannouClientWithCache = BannouClient & { [PROXY_CACHE]?: ProxyCache };")
    lines.append("")

    # Generate Object.defineProperty calls for each proxy
    for service in services:
        lines.append(f"/**")
        lines.append(f" * Add lazy-initialized {service.property_name} proxy property to BannouClient.")
        lines.append(f" */")
        lines.append(f"Object.defineProperty(BannouClient.prototype, '{service.property_name}', {{")
        lines.append(f"  get(this: BannouClientWithCache): {service.proxy_class_name} {{")
        lines.append(f"    const cache = (this[PROXY_CACHE] ??= {{}});")
        lines.append(f"    return (cache.{service.property_name} ??= new {service.proxy_class_name}(this));")
        lines.append(f"  }},")
        lines.append(f"  configurable: true,")
        lines.append(f"  enumerable: true,")
        lines.append(f"}});")
        lines.append("")

    # Generate declaration merging for type safety
    lines.append("// Declaration merging to add proxy property types to BannouClient")
    lines.append("declare module '../BannouClient.js' {")
    lines.append("  interface BannouClient {")
    for service in services:
        lines.append(f"    /**")
        lines.append(f"     * Typed proxy for {service.pascal_name} API endpoints.")
        lines.append(f"     */")
        lines.append(f"    readonly {service.property_name}: {service.proxy_class_name};")
    lines.append("  }")
    lines.append("}")
    lines.append("")

    # Re-export BannouClient for convenience
    lines.append("// Re-export BannouClient with augmented types")
    lines.append("export { BannouClient };")
    lines.append("")

    return '\n'.join(lines)


def main():
    """Process consolidated schema and generate proxy classes."""
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_path = repo_root / 'schemas' / 'Generated' / 'bannou-client-api.yaml'
    output_dir = repo_root / 'sdks' / 'typescript' / 'client' / 'src' / 'Generated' / 'proxies'
    generated_dir = repo_root / 'sdks' / 'typescript' / 'client' / 'src' / 'Generated'

    if not schema_path.exists():
        print(f"ERROR: Consolidated client schema not found: {schema_path}")
        print("       Run scripts/generate-client-schema.py first.")
        sys.exit(1)

    # Create output directory
    output_dir.mkdir(parents=True, exist_ok=True)

    # Clean up old proxy files
    for old_file in output_dir.glob('*.ts'):
        old_file.unlink()

    print("Generating TypeScript client proxies from consolidated schema...")
    print(f"  Reading from: schemas/Generated/bannou-client-api.yaml")
    print(f"  Writing to: sdks/typescript/client/src/Generated/proxies/")
    print()

    # Parse consolidated schema
    services = parse_consolidated_schema(schema_path)

    if not services:
        print("No services found in consolidated schema")
        sys.exit(1)

    service_list: List[ServiceInfo] = []
    total_endpoints = 0

    for service_name in sorted(services.keys()):
        service = services[service_name]

        # Skip services with no usable endpoints
        usable_endpoints = [e for e in service.endpoints if e.request_type or e.response_type]
        if not usable_endpoints:
            continue

        # Generate proxy class
        proxy_code = generate_proxy_class(service)
        output_file = output_dir / f"{service.proxy_class_name}.ts"

        with open(output_file, 'w', newline='\n') as f:
            f.write(proxy_code)

        service_list.append(service)
        total_endpoints += len(usable_endpoints)
        print(f"  Generated {service.proxy_class_name}.ts ({len(usable_endpoints)} endpoints)")

    # Generate index file
    if service_list:
        index_code = generate_index_file(service_list)
        index_file = output_dir / "index.ts"

        with open(index_file, 'w', newline='\n') as f:
            f.write(index_code)

        print(f"\n  Generated index.ts ({len(service_list)} proxies)")

        # Generate extensions file (adds proxy properties to BannouClient)
        extensions_code = generate_extensions_file(service_list)
        extensions_file = generated_dir / "BannouClientExtensions.ts"

        with open(extensions_file, 'w', newline='\n') as f:
            f.write(extensions_code)

        print(f"  Generated BannouClientExtensions.ts ({len(service_list)} proxies)")

    print()
    print(f"Generated {len(service_list)} proxy classes with {total_endpoints} total endpoints")


if __name__ == '__main__':
    main()
