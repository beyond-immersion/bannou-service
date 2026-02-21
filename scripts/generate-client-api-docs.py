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
Generate Client API Documentation from OpenAPI schemas.

This script scans *-api.yaml schema files and generates a compact reference
document showing all typed proxy methods available to Client SDK users.

Output: docs/GENERATED-CLIENT-API.md

Usage:
    python3 scripts/generate-client-api-docs.py
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


def to_title_case(name: str) -> str:
    """Convert kebab-case service name to Title Case."""
    parts = name.split('-')
    return ' '.join(p.capitalize() for p in parts)


def extract_type_name(ref: str) -> str:
    """Extract type name from $ref string."""
    if not ref or not ref.startswith('#/components/schemas/'):
        return ''
    return ref.split('/')[-1]


def operation_id_to_method_name(operation_id: str) -> str:
    """Convert operationId to C# method name."""
    prefixes = ['get', 'list', 'create', 'update', 'delete', 'validate', 'login', 'register', 'logout']
    for prefix in prefixes:
        if operation_id.lower().startswith(prefix):
            rest = operation_id[len(prefix):]
            return prefix.capitalize() + to_pascal_case(rest) + 'Async'
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
        tag: str,
        access: str
    ):
        self.method = method.upper()
        self.path = path
        self.operation_id = operation_id
        self.request_type = request_type
        self.response_type = response_type
        self.summary = summary
        self.tag = tag
        self.access = access

    @property
    def method_name(self) -> str:
        """Get C# method name for this endpoint."""
        base_name = operation_id_to_method_name(self.operation_id)
        # Fire-and-forget endpoints get EventAsync suffix
        if self.request_type and not self.response_type:
            return base_name.replace('Async', 'EventAsync')
        return base_name

    @property
    def is_fire_and_forget(self) -> bool:
        """Check if this is a fire-and-forget endpoint."""
        return bool(self.request_type and not self.response_type)


class ServiceInfo:
    """Information about a service."""

    def __init__(self, name: str, title: str, description: str, version: str):
        self.name = name
        self.title = title
        self.description = description
        self.version = version
        self.endpoints: List[EndpointInfo] = []

    @property
    def pascal_name(self) -> str:
        return to_pascal_case(self.name)

    @property
    def proxy_name(self) -> str:
        return f"{self.pascal_name}Proxy"


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

    service_name = schema_path.stem.replace('-api', '')
    info = schema.get('info', {})
    title = info.get('title', to_title_case(service_name))
    description = info.get('description', '')
    version = info.get('version', '1.0.0')

    # Clean up description
    if description:
        first_para = description.split('\n\n')[0]
        first_para = first_para.replace('**', '').replace('`', '').replace('\n', ' ')
        if len(first_para) > 150:
            first_para = first_para[:147] + '...'
        description = first_para.strip()

    service = ServiceInfo(service_name, title, description, version)

    for path, methods in schema.get('paths', {}).items():
        if not isinstance(methods, dict):
            continue

        for method, details in methods.items():
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

            # Extract response type
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

            # Extract access level from x-permissions
            # x-permissions can be a list of objects like [{role: "user", states: {...}}]
            x_permissions = details.get('x-permissions', [])
            if not x_permissions:
                access = 'anonymous'
            else:
                # Extract roles from permission objects
                roles = []
                for perm in x_permissions:
                    if isinstance(perm, dict):
                        role = perm.get('role', '')
                        if role:
                            roles.append(role)
                    elif isinstance(perm, str):
                        roles.append(perm)

                if not roles or 'anonymous' in roles:
                    access = 'anonymous'
                elif 'admin' in roles:
                    access = 'admin'
                elif 'developer' in roles:
                    access = 'developer'
                elif 'user' in roles:
                    access = 'user'
                elif 'authenticated' in roles:
                    access = 'authenticated'
                else:
                    access = ', '.join(roles) if roles else 'anonymous'

            # Only include endpoints with at least one type
            if request_type or response_type:
                endpoint = EndpointInfo(
                    method=method,
                    path=path,
                    operation_id=operation_id,
                    request_type=request_type,
                    response_type=response_type,
                    summary=summary,
                    tag=tag,
                    access=access
                )
                service.endpoints.append(endpoint)

    return service if service.endpoints else None


def generate_markdown(services: List[ServiceInfo]) -> str:
    """Generate the documentation markdown."""
    lines = [
        "# Generated Client API Reference",
        "",
        "> **Source**: `schemas/*-api.yaml`",
        "> **Do not edit manually** - regenerate with `make generate-docs`",
        "",
        "This document lists all typed proxy methods available in the Bannou Client SDK.",
        "",
        "## Quick Reference",
        "",
        "| Service | Proxy Property | Methods | Description |",
        "|---------|---------------|---------|-------------|",
    ]

    # Service overview table
    for service in services:
        desc = service.description[:60] + '...' if len(service.description) > 60 else service.description
        lines.append(f"| [{service.title}](#{service.name}) | `client.{service.pascal_name}` | {len(service.endpoints)} | {desc} |")

    lines.extend([
        "",
        "---",
        "",
        "## Usage Pattern",
        "",
        "```csharp",
        "using BeyondImmersion.Bannou.Client;",
        "",
        "var client = new BannouClient();",
        "await client.ConnectWithTokenAsync(url, token);",
        "",
        "// Use typed proxy methods",
        "var response = await client.Auth.LoginAsync(new LoginRequest",
        "{",
        "    Email = \"user@example.com\",",
        "    Password = \"password\"",
        "});",
        "",
        "if (response.IsSuccess)",
        "{",
        "    var token = response.Result.Token;",
        "}",
        "```",
        "",
        "---",
        "",
    ])

    # Service details
    for service in services:
        lines.extend([
            f"## {service.title} {{#{service.name}}}",
            "",
            f"**Proxy**: `client.{service.pascal_name}` | **Version**: {service.version}",
            "",
        ])

        if service.description:
            lines.extend([service.description, ""])

        # Group endpoints by tag
        by_tag: dict = {}
        for ep in service.endpoints:
            tag = ep.tag or 'Other'
            if tag not in by_tag:
                by_tag[tag] = []
            by_tag[tag].append(ep)

        for tag, endpoints in sorted(by_tag.items()):
            lines.extend([
                f"### {tag}",
                "",
                "| Method | Request | Response | Summary |",
                "|--------|---------|----------|---------|",
            ])

            for ep in endpoints:
                req = f"`{ep.request_type}`" if ep.request_type else "-"
                resp = f"`{ep.response_type}`" if ep.response_type else "*(fire-and-forget)*"
                summary = ep.summary if ep.summary else "-"
                lines.append(f"| `{ep.method_name}` | {req} | {resp} | {summary} |")

            lines.append("")

        lines.append("---")
        lines.append("")

    # Summary
    total_methods = sum(len(s.endpoints) for s in services)
    lines.extend([
        "## Summary",
        "",
        f"- **Total services**: {len(services)}",
        f"- **Total methods**: {total_methods}",
        "",
        "---",
        "",
        "*This file is auto-generated from OpenAPI schemas. See [TENETS.md](reference/TENETS.md) for architectural context.*",
        "",
    ])

    return '\n'.join(lines)


def main():
    """Process all API schemas and generate documentation."""
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'
    output_file = repo_root / 'docs' / 'GENERATED-CLIENT-API.md'

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    print("Generating Client API documentation from OpenAPI schemas...")
    print(f"  Reading from: schemas/*-api.yaml")
    print(f"  Writing to: {output_file.relative_to(repo_root)}")
    print()

    services: List[ServiceInfo] = []

    for schema_path in sorted(schema_dir.glob('*-api.yaml')):
        try:
            service = parse_schema(schema_path)
            if service:
                services.append(service)
                print(f"  Parsed {schema_path.name}: {len(service.endpoints)} methods")
        except Exception as e:
            print(f"  Error parsing {schema_path.name}: {e}")

    # Generate and write documentation
    output_file.parent.mkdir(parents=True, exist_ok=True)
    markdown = generate_markdown(services)

    with open(output_file, 'w', newline='\n') as f:
        f.write(markdown)

    total_methods = sum(len(s.endpoints) for s in services)
    print(f"\nGenerated documentation for {len(services)} services with {total_methods} methods")


if __name__ == '__main__':
    main()
