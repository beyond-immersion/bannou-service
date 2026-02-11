#!/bin/bash

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

# Generate meta controller from OpenAPI meta schema
# Usage: ./generate-meta-controller.sh <service-name> [schema-file]
#
# This generates the meta/introspection endpoints as a partial class.
# Uses schemas/Generated/{service}-api-meta.yaml which contains x-*-json extensions.
# Output: plugins/lib-{service}/Generated/{Service}Controller.Meta.cs

set -e  # Exit on any error

# Source common utilities
source "$(dirname "$0")/common.sh"

# Validate arguments
if [ $# -lt 1 ]; then
    log_error "Usage: $0 <service-name> [schema-file]"
    echo "Example: $0 account"
    exit 1
fi

SERVICE_NAME="$1"
# Use the meta schema from Generated directory
META_SCHEMA_FILE="../schemas/Generated/${SERVICE_NAME}-api-meta.yaml"

SERVICE_PASCAL=$(to_pascal_case "$SERVICE_NAME")
OUTPUT_DIR="../plugins/lib-${SERVICE_NAME}/Generated"
OUTPUT_FILE="$OUTPUT_DIR/${SERVICE_PASCAL}Controller.Meta.cs"

echo -e "${YELLOW}🔧 Generating meta controller for service: $SERVICE_NAME${NC}"
echo -e "  📋 Meta Schema: $META_SCHEMA_FILE"
echo -e "  📁 Output: $OUTPUT_FILE"

# Validate meta schema file exists
if [ ! -f "$META_SCHEMA_FILE" ]; then
    echo -e "${RED}❌ Meta schema file not found: $META_SCHEMA_FILE${NC}"
    echo -e "${YELLOW}   Run: python3 scripts/embed-meta-schemas.py --service $SERVICE_NAME${NC}"
    exit 1
fi

# Ensure output directory exists
mkdir -p "$OUTPUT_DIR"

# Generate meta controller using Python
echo -e "${YELLOW}🔄 Generating meta controller...${NC}"

python3 - "$SERVICE_NAME" "$META_SCHEMA_FILE" "$OUTPUT_FILE" << 'PYTHON_SCRIPT'
import sys
import yaml
import re
from pathlib import Path

def to_pascal_case(name: str) -> str:
    """Convert kebab-case to PascalCase."""
    return ''.join(word.capitalize() for word in name.split('-'))

def escape_string_literal(s: str) -> str:
    """Escape a string for use in C# raw string literal."""
    return s

def generate_operation_name(path: str, method: str) -> str:
    """Generate operation name from path and method."""
    # Remove leading slash and convert to PascalCase
    parts = path.lstrip('/').split('/')
    name_parts = []
    for part in parts:
        if part.startswith('{') and part.endswith('}'):
            # Parameter - use "By" + parameter name
            param = part[1:-1]
            name_parts.append('By' + to_pascal_case(param))
        else:
            name_parts.append(to_pascal_case(part))
    return ''.join(name_parts)

def main():
    if len(sys.argv) != 4:
        print("Usage: script.py <service-name> <schema-file> <output-file>", file=sys.stderr)
        sys.exit(1)

    service_name = sys.argv[1]
    schema_file = sys.argv[2]
    output_file = sys.argv[3]

    service_pascal = to_pascal_case(service_name)

    # Load schema
    with open(schema_file, 'r') as f:
        schema = yaml.safe_load(f)

    if not schema or 'paths' not in schema:
        print(f"Error: Invalid schema file: {schema_file}", file=sys.stderr)
        sys.exit(1)

    # Generate meta endpoints
    regions = []

    for path, methods in schema['paths'].items():
        if not isinstance(methods, dict):
            continue

        for method, operation in methods.items():
            if method in ('parameters', 'servers', 'summary', 'description', '$ref'):
                continue

            if not isinstance(operation, dict):
                continue

            # Check for meta schema extensions
            request_schema = operation.get('x-request-schema-json')
            if not request_schema:
                continue

            response_schema = operation.get('x-response-schema-json', '{}')
            info_json = operation.get('x-endpoint-info-json', '{}')

            # Get operation name
            op_name = operation.get('operationId')
            if not op_name:
                op_name = generate_operation_name(path, method)

            # Convert operationId to method name (first letter uppercase)
            method_name = op_name[0].upper() + op_name[1:] if op_name else 'Unknown'

            http_method = method.upper()

            region = f'''
    #region Meta Endpoints for {method_name}

    private static readonly string _{method_name}_RequestSchema = """
{request_schema}
""";

    private static readonly string _{method_name}_ResponseSchema = """
{response_schema}
""";

    private static readonly string _{method_name}_Info = """
{info_json}
""";

    /// <summary>Returns endpoint information for {method_name}</summary>
    [Microsoft.AspNetCore.Mvc.HttpGet, Microsoft.AspNetCore.Mvc.Route("{path}/meta/info")]
    public Microsoft.AspNetCore.Mvc.ActionResult<BeyondImmersion.BannouService.Meta.MetaResponse> {method_name}_MetaInfo()
        => Ok(BeyondImmersion.BannouService.Meta.MetaResponseBuilder.BuildInfoResponse(
            "{service_pascal}",
            "{http_method}",
            "{path}",
            _{method_name}_Info));

    /// <summary>Returns request schema for {method_name}</summary>
    [Microsoft.AspNetCore.Mvc.HttpGet, Microsoft.AspNetCore.Mvc.Route("{path}/meta/request-schema")]
    public Microsoft.AspNetCore.Mvc.ActionResult<BeyondImmersion.BannouService.Meta.MetaResponse> {method_name}_MetaRequestSchema()
        => Ok(BeyondImmersion.BannouService.Meta.MetaResponseBuilder.BuildSchemaResponse(
            "{service_pascal}",
            "{http_method}",
            "{path}",
            "request-schema",
            _{method_name}_RequestSchema));

    /// <summary>Returns response schema for {method_name}</summary>
    [Microsoft.AspNetCore.Mvc.HttpGet, Microsoft.AspNetCore.Mvc.Route("{path}/meta/response-schema")]
    public Microsoft.AspNetCore.Mvc.ActionResult<BeyondImmersion.BannouService.Meta.MetaResponse> {method_name}_MetaResponseSchema()
        => Ok(BeyondImmersion.BannouService.Meta.MetaResponseBuilder.BuildSchemaResponse(
            "{service_pascal}",
            "{http_method}",
            "{path}",
            "response-schema",
            _{method_name}_ResponseSchema));

    /// <summary>Returns full schema for {method_name}</summary>
    [Microsoft.AspNetCore.Mvc.HttpGet, Microsoft.AspNetCore.Mvc.Route("{path}/meta/schema")]
    public Microsoft.AspNetCore.Mvc.ActionResult<BeyondImmersion.BannouService.Meta.MetaResponse> {method_name}_MetaFullSchema()
        => Ok(BeyondImmersion.BannouService.Meta.MetaResponseBuilder.BuildFullSchemaResponse(
            "{service_pascal}",
            "{http_method}",
            "{path}",
            _{method_name}_Info,
            _{method_name}_RequestSchema,
            _{method_name}_ResponseSchema));

    #endregion'''

            regions.append(region)

    # Generate the full file
    # Note: No [GeneratedCode] attribute - partial classes share attributes with main class
    output = f'''//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace BeyondImmersion.BannouService.{service_pascal};

/// <summary>
/// Meta/introspection endpoints for runtime schema access.
/// Generated from schemas/Generated/{service_name}-api-meta.yaml
/// </summary>
public partial class {service_pascal}Controller
{{{chr(10).join(regions)}
}}
'''

    # Write output
    Path(output_file).parent.mkdir(parents=True, exist_ok=True)
    with open(output_file, 'w', newline='\n') as f:
        f.write(output)

    print(f"Generated {len(regions)} meta endpoint regions", file=sys.stderr)

if __name__ == '__main__':
    main()
PYTHON_SCRIPT

# Check if generation succeeded
if [ $? -eq 0 ] && [ -f "$OUTPUT_FILE" ]; then
    FILE_SIZE=$(wc -l < "$OUTPUT_FILE" 2>/dev/null || echo "0")
    echo -e "${GREEN}✅ Generated meta controller ($FILE_SIZE lines)${NC}"
    exit 0
else
    echo -e "${RED}❌ Failed to generate meta controller${NC}"
    exit 1
fi
