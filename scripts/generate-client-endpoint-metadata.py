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
Generate ClientEndpointMetadata static registry for runtime type discovery.

This script reads OpenAPI schemas from schemas/*-api.yaml and generates
a static class that maps (method, path) tuples to their request/response types.

This enables runtime discovery of endpoint types without compile-time knowledge:
    var requestType = ClientEndpointMetadata.GetRequestType("POST", "/auth/login");
    var responseType = ClientEndpointMetadata.GetResponseType("POST", "/auth/login");

Output:
- sdks/client/Generated/ClientEndpointMetadata.cs

Usage:
    python3 scripts/generate-client-endpoint-metadata.py
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
    """Convert kebab-case or snake_case to PascalCase."""
    parts = name.replace('_', '-').split('-')
    return ''.join(p.capitalize() for p in parts)


def extract_type_name(ref: str) -> str:
    """Extract type name from $ref string like '#/components/schemas/LoginRequest'."""
    if not ref or not ref.startswith('#/components/schemas/'):
        return ''
    return ref.split('/')[-1]


class EndpointMetadata:
    """Metadata for a single endpoint."""

    def __init__(
        self,
        method: str,
        path: str,
        service: str,
        request_type: Optional[str],
        response_type: Optional[str],
        summary: str
    ):
        self.method = method.upper()
        self.path = path
        self.service = service
        self.request_type = request_type
        self.response_type = response_type
        self.summary = summary

    @property
    def key(self) -> str:
        """Get dictionary key for this endpoint."""
        return f"{self.method}:{self.path}"

    @property
    def qualified_request_type(self) -> Optional[str]:
        """Get fully qualified request type name."""
        if not self.request_type:
            return None
        return f"BeyondImmersion.BannouService.{self.service}.{self.request_type}"

    @property
    def qualified_response_type(self) -> Optional[str]:
        """Get fully qualified response type name."""
        if not self.response_type:
            return None
        return f"BeyondImmersion.BannouService.{self.service}.{self.response_type}"


def parse_schema(schema_path: Path) -> List[EndpointMetadata]:
    """Parse an OpenAPI schema file and extract endpoint metadata."""
    with open(schema_path) as f:
        try:
            schema = yaml.load(f)
        except Exception as e:
            print(f"  Warning: Failed to parse {schema_path.name}: {e}")
            return []

    if schema is None or 'paths' not in schema:
        return []

    service_name = schema_path.stem.replace('-api', '')
    pascal_name = to_pascal_case(service_name)
    endpoints = []

    for path, methods in schema.get('paths', {}).items():
        if not isinstance(methods, dict):
            continue

        for method, details in methods.items():
            if method.lower() not in ('get', 'post', 'put', 'delete', 'patch'):
                continue

            if not isinstance(details, dict):
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

            # Only include endpoints with at least one type
            if request_type or response_type:
                endpoints.append(EndpointMetadata(
                    method=method,
                    path=path,
                    service=pascal_name,
                    request_type=request_type,
                    response_type=response_type,
                    summary=summary
                ))

    return endpoints


def generate_metadata_class(endpoints: List[EndpointMetadata]) -> str:
    """Generate the ClientEndpointMetadata C# class."""

    # Sort for consistent output
    sorted_endpoints = sorted(endpoints, key=lambda e: e.key)

    lines = [
        "// <auto-generated>",
        "// This code was generated by generate-client-endpoint-metadata.py",
        "// Do not edit this file manually.",
        "// </auto-generated>",
        "",
        "#nullable enable",
        "",
        "namespace BeyondImmersion.Bannou.Client;",
        "",
        "/// <summary>",
        "/// Static registry mapping API endpoints to their request/response types.",
        "/// Enables runtime type discovery without compile-time knowledge.",
        "/// </summary>",
        "public static class ClientEndpointMetadata",
        "{",
        "    /// <summary>",
        "    /// Endpoint information including request and response types.",
        "    /// </summary>",
        "    public sealed class EndpointInfo",
        "    {",
        "        /// <summary>HTTP method (e.g., POST, GET).</summary>",
        "        public string Method { get; }",
        "",
        "        /// <summary>API path (e.g., /auth/login).</summary>",
        "        public string Path { get; }",
        "",
        "        /// <summary>Service name (e.g., Auth, Character).</summary>",
        "        public string Service { get; }",
        "",
        "        /// <summary>Request body type, or null if no request body.</summary>",
        "        public Type? RequestType { get; }",
        "",
        "        /// <summary>Response body type, or null if no response body.</summary>",
        "        public Type? ResponseType { get; }",
        "",
        "        /// <summary>Endpoint summary from OpenAPI schema.</summary>",
        "        public string Summary { get; }",
        "",
        "        internal EndpointInfo(string method, string path, string service, Type? requestType, Type? responseType, string summary)",
        "        {",
        "            Method = method;",
        "            Path = path;",
        "            Service = service;",
        "            RequestType = requestType;",
        "            ResponseType = responseType;",
        "            Summary = summary;",
        "        }",
        "    }",
        "",
        "    private static readonly Dictionary<string, EndpointInfo> Endpoints = new()",
        "    {",
    ]

    # Generate dictionary entries
    for ep in sorted_endpoints:
        # Use fully qualified type names to avoid ambiguity
        req_type = f"typeof({ep.qualified_request_type})" if ep.request_type else "null"
        resp_type = f"typeof({ep.qualified_response_type})" if ep.response_type else "null"
        # Escape quotes in summary
        summary = ep.summary.replace('"', '\\"') if ep.summary else ""

        lines.append(f'        {{ "{ep.key}", new EndpointInfo("{ep.method}", "{ep.path}", "{ep.service}", {req_type}, {resp_type}, "{summary}") }},')

    lines.extend([
        "    };",
        "",
        "    /// <summary>",
        "    /// Gets the request type for an endpoint.",
        "    /// </summary>",
        "    /// <param name=\"method\">HTTP method (e.g., \"POST\").</param>",
        "    /// <param name=\"path\">API path (e.g., \"/auth/login\").</param>",
        "    /// <returns>The request type, or null if not found or no request body.</returns>",
        "    public static Type? GetRequestType(string method, string path)",
        "    {",
        "        var key = $\"{method.ToUpperInvariant()}:{path}\";",
        "        return Endpoints.TryGetValue(key, out var info) ? info.RequestType : null;",
        "    }",
        "",
        "    /// <summary>",
        "    /// Gets the response type for an endpoint.",
        "    /// </summary>",
        "    /// <param name=\"method\">HTTP method (e.g., \"POST\").</param>",
        "    /// <param name=\"path\">API path (e.g., \"/auth/login\").</param>",
        "    /// <returns>The response type, or null if not found or no response body.</returns>",
        "    public static Type? GetResponseType(string method, string path)",
        "    {",
        "        var key = $\"{method.ToUpperInvariant()}:{path}\";",
        "        return Endpoints.TryGetValue(key, out var info) ? info.ResponseType : null;",
        "    }",
        "",
        "    /// <summary>",
        "    /// Gets complete endpoint information.",
        "    /// </summary>",
        "    /// <param name=\"method\">HTTP method (e.g., \"POST\").</param>",
        "    /// <param name=\"path\">API path (e.g., \"/auth/login\").</param>",
        "    /// <returns>The endpoint info, or null if not found.</returns>",
        "    public static EndpointInfo? GetEndpointInfo(string method, string path)",
        "    {",
        "        var key = $\"{method.ToUpperInvariant()}:{path}\";",
        "        return Endpoints.GetValueOrDefault(key);",
        "    }",
        "",
        "    /// <summary>",
        "    /// Checks if an endpoint is registered.",
        "    /// </summary>",
        "    /// <param name=\"method\">HTTP method (e.g., \"POST\").</param>",
        "    /// <param name=\"path\">API path (e.g., \"/auth/login\").</param>",
        "    /// <returns>True if the endpoint is registered.</returns>",
        "    public static bool IsRegistered(string method, string path)",
        "    {",
        "        var key = $\"{method.ToUpperInvariant()}:{path}\";",
        "        return Endpoints.ContainsKey(key);",
        "    }",
        "",
        "    /// <summary>",
        "    /// Gets all registered endpoints.",
        "    /// </summary>",
        "    public static IEnumerable<EndpointInfo> GetAllEndpoints() => Endpoints.Values;",
        "",
        "    /// <summary>",
        "    /// Gets all endpoints for a specific service.",
        "    /// </summary>",
        "    /// <param name=\"serviceName\">Service name (e.g., \"Auth\", \"Character\").</param>",
        "    public static IEnumerable<EndpointInfo> GetEndpointsByService(string serviceName)",
        "    {",
        "        return Endpoints.Values.Where(e => e.Service.Equals(serviceName, StringComparison.OrdinalIgnoreCase));",
        "    }",
        "",
        "    /// <summary>",
        "    /// Gets the total number of registered endpoints.",
        "    /// </summary>",
        "    public static int Count => Endpoints.Count;",
        "}",
        "",
    ])

    return '\n'.join(lines)


def main():
    """Process all API schemas and generate endpoint metadata."""
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'
    output_file = repo_root / 'sdks' / 'client' / 'Generated' / 'ClientEndpointMetadata.cs'

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    print("Generating client endpoint metadata from OpenAPI schemas...")
    print(f"  Reading from: schemas/*-api.yaml")
    print(f"  Writing to: {output_file.relative_to(repo_root)}")
    print()

    all_endpoints: List[EndpointMetadata] = []
    errors = []

    for schema_path in sorted(schema_dir.glob('*-api.yaml')):
        try:
            endpoints = parse_schema(schema_path)
            all_endpoints.extend(endpoints)
            if endpoints:
                print(f"  Parsed {schema_path.name}: {len(endpoints)} endpoints")
        except Exception as e:
            errors.append(f"{schema_path.name}: {e}")

    if errors:
        print("\nErrors:")
        for error in errors:
            print(f"  {error}")
        sys.exit(1)

    # Generate and write the metadata class
    output_file.parent.mkdir(parents=True, exist_ok=True)
    metadata_code = generate_metadata_class(all_endpoints)

    with open(output_file, 'w', newline='\n') as f:
        f.write(metadata_code)

    print(f"\nGenerated ClientEndpointMetadata.cs with {len(all_endpoints)} endpoints")


if __name__ == '__main__':
    main()
