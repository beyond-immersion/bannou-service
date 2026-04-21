#!/bin/bash

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

# Generate NSwag service client from OpenAPI schema
# Usage: ./generate-client.sh <service-name> [schema-file]

set -e  # Exit on any error

# Source common utilities
source "$(dirname "$0")/common.sh"

# Validate arguments
if [ $# -lt 1 ]; then
    log_error "Usage: $0 <service-name> [schema-file]"
    echo "Example: $0 account"
    echo "Example: $0 account ../schemas/account-api.yaml"
    exit 1
fi

SERVICE_NAME="$1"
SCHEMA_FILE="${2:-../schemas/${SERVICE_NAME}-api.yaml}"

SERVICE_PASCAL=$(to_pascal_case "$SERVICE_NAME")
OUTPUT_DIR="../bannou-service/Generated/Clients"
OUTPUT_FILE="$OUTPUT_DIR/${SERVICE_PASCAL}Client.cs"

echo -e "${YELLOW}🔧 Generating service client for: $SERVICE_NAME${NC}"
echo -e "  📋 Schema: $SCHEMA_FILE"
echo -e "  📁 Output: $OUTPUT_FILE"

# Validate schema file exists
if [ ! -f "$SCHEMA_FILE" ]; then
    echo -e "${RED}❌ Schema file not found: $SCHEMA_FILE${NC}"
    exit 1
fi

# Ensure output directory exists
mkdir -p "$OUTPUT_DIR"

# Create filtered schema for client generation. Filters applied (all independent):
#   1. Drop operations with x-controller-only: true (no client method generated —
#      endpoint exists only as a manual controller override, no service-interface pair)
#   2. Drop operations with x-manual-implementation: true (same reason — no generated
#      interface method to dispatch to)
#   3. For requestBody with multiple content types, keep only application/json so the
#      NSwag-generated client body type matches the service-interface body type (avoids
#      string-body client vs. typed-body interface mismatch for yaml/text endpoints)
#   4. Remove parameters marked x-from-authorization (token is supplied via
#      WithAuthorization(...) header, not as a public client method parameter); also
#      annotate the operation with x-direct-dispatch-auth so the client template can
#      emit specialized direct-dispatch code that extracts the token from
#      _authorizationHeader and passes it to the service method's typed token parameter
#
# Filtered schema is always produced (even when no filter rule fires) so the downstream
# NSwag invocation has a single code path.
FILTERED_SCHEMA_FILE="../schemas/Generated/${SERVICE_NAME}-client-filtered-schema.yaml"
echo -e "${YELLOW}🔧 Creating filtered client schema...${NC}"

# Output goes to schemas/Generated/ so cross-file $refs need ../ prefix adjustment
python3 -c "
import yaml
import sys
import re

with open('$SCHEMA_FILE', 'r') as f:
    schema = yaml.safe_load(f)

HTTP_METHODS = {'get', 'post', 'put', 'patch', 'delete', 'head', 'options', 'trace'}

if 'paths' in schema:
    # Iterate over a snapshot so we can mutate the dict
    for path in list(schema['paths'].keys()):
        path_data = schema['paths'][path]
        if not isinstance(path_data, dict):
            continue

        # Rule 1+2: drop operations (HTTP methods) with x-controller-only or x-manual-implementation
        methods_to_drop = []
        for method in list(path_data.keys()):
            if method.lower() not in HTTP_METHODS:
                continue
            method_data = path_data[method]
            if not isinstance(method_data, dict):
                continue
            if method_data.get('x-controller-only'):
                print(f'Dropping x-controller-only operation: {method.upper()} {path}', file=sys.stderr)
                methods_to_drop.append(method)
            elif method_data.get('x-manual-implementation'):
                print(f'Dropping x-manual-implementation operation: {method.upper()} {path}', file=sys.stderr)
                methods_to_drop.append(method)

        for method in methods_to_drop:
            del path_data[method]

        # If a path has no remaining HTTP method operations, drop the path entry
        remaining_methods = [m for m in path_data.keys() if m.lower() in HTTP_METHODS]
        if not remaining_methods:
            del schema['paths'][path]
            continue

        # Per-operation rules 3 and 4
        for method in list(path_data.keys()):
            if method.lower() not in HTTP_METHODS:
                continue
            method_data = path_data[method]
            if not isinstance(method_data, dict):
                continue

            # Rule 3: collapse requestBody to application/json when multiple content types exist.
            # Service-interface methods always accept the typed schema body, so the client
            # body type must match — otherwise the direct-dispatch static lambda fails to
            # compile against the service-method signature (e.g., yaml → string vs typed).
            request_body = method_data.get('requestBody')
            if isinstance(request_body, dict):
                content = request_body.get('content')
                if isinstance(content, dict) and 'application/json' in content and len(content) > 1:
                    dropped = [ct for ct in content.keys() if ct != 'application/json']
                    for ct in dropped:
                        del content[ct]
                    print(f'Collapsed multi-content requestBody to application/json for {method.upper()} {path} (dropped: {dropped})', file=sys.stderr)

            # Rule 4: strip x-from-authorization parameters; annotate operation so the
            # client template can emit specialized direct-dispatch with token extraction.
            parameters = method_data.get('parameters')
            if isinstance(parameters, list):
                filtered_params = []
                auth_scheme = None
                for param in parameters:
                    auth_value = param.get('x-from-authorization') if isinstance(param, dict) else None
                    if auth_value:
                        name = param.get('name', 'unknown') if isinstance(param, dict) else 'unknown'
                        print(f'Removing x-from-authorization parameter \"{name}\" from {method.upper()} {path} for client generation', file=sys.stderr)
                        if auth_scheme is None:
                            auth_scheme = auth_value
                    else:
                        filtered_params.append(param)

                if auth_scheme is not None:
                    method_data['x-direct-dispatch-auth'] = auth_scheme
                method_data['parameters'] = filtered_params

# Fix relative paths: output is in schemas/Generated/ so sibling refs need ../ prefix.
# Handles three shapes a schema may use for a sibling-file ref:
#   'common-api.yaml#/x'      → '../common-api.yaml#/x'
#   './common-api.yaml#/x'    → '../common-api.yaml#/x'   (dot-prefix is legal OpenAPI)
#   '../something.yaml#/x'    → left alone (already parent-relative, don't double-up)
# Same-file refs ('#/components/schemas/X') are skipped by the 'not startswith #' guard.
def fix_refs(obj):
    if isinstance(obj, dict):
        for key, val in obj.items():
            if key == '\$ref' and isinstance(val, str) and '#' in val and not val.startswith('#'):
                # Strip leading './' (may appear multiple times in odd inputs) so the
                # downstream regex sees the bare filename.
                normalized = val
                while normalized.startswith('./'):
                    normalized = normalized[2:]
                # Only rewrite when the result is a sibling yaml ref — i.e., filename
                # starts with a letter and contains '.yaml#'. Parent-relative ('../…')
                # and absolute ('/…') refs keep their original text.
                if re.match(r'^[a-zA-Z][a-zA-Z0-9_-]*\\.yaml#', normalized):
                    obj[key] = '../' + normalized
            else:
                fix_refs(val)
    elif isinstance(obj, list):
        for item in obj:
            fix_refs(item)

fix_refs(schema)

# Write filtered schema for client generation
with open('$FILTERED_SCHEMA_FILE', 'w') as f:
    yaml.dump(schema, f, default_flow_style=False, sort_keys=False)

print('Client filtered schema created successfully', file=sys.stderr)
"

# Find NSwag executable and ensure DOTNET_ROOT is set
require_nswag
ensure_dotnet_root

# Generate service client using NSwag (IMeshInvocationClient pattern)
echo -e "${YELLOW}🔄 Running NSwag client generation...${NC}"

"$NSWAG_EXE" openapi2csclient \
    "/input:$FILTERED_SCHEMA_FILE" \
    "/output:$OUTPUT_FILE" \
    "/namespace:BeyondImmersion.BannouService.$SERVICE_PASCAL" \
    "/className:${SERVICE_PASCAL}Client" \
    "/generateClientClasses:true" \
    "/generateClientInterfaces:true" \
    "/generateDtoTypes:false" \
    "/excludedTypeNames:ApiException,ApiException\<TResult\>" \
    "/exceptionClass:BeyondImmersion.Bannou.Core.ApiException" \
    "/generateExceptionClasses:false" \
    "/injectHttpClient:false" \
    "/disposeHttpClient:true" \
    "/jsonLibrary:SystemTextJson" \
    "/generateNullableReferenceTypes:true" \
    "/newLineBehavior:LF" \
    "/generateOptionalParameters:true" \
    "/useHttpClientCreationMethod:true" \
    "/additionalNamespaceUsages:BeyondImmersion.BannouService,BeyondImmersion.BannouService.ServiceClients,BeyondImmersion.BannouService.$SERVICE_PASCAL" \
    "/templateDirectory:../templates/nswag"

# Check if generation succeeded
if [ $? -eq 0 ] && [ -f "$OUTPUT_FILE" ]; then
    FILE_SIZE=$(wc -l < "$OUTPUT_FILE" 2>/dev/null || echo "0")
    echo -e "${GREEN}✅ Generated service client ($FILE_SIZE lines)${NC}"

    # Cleanup temporary filtered schema file
    if [ "$FILTERED_SCHEMA_FILE" != "$SCHEMA_FILE" ] && [ -f "$FILTERED_SCHEMA_FILE" ]; then
        rm -f "$FILTERED_SCHEMA_FILE"
    fi
    exit 0
else
    echo -e "${RED}❌ Failed to generate service client${NC}"
    # Cleanup temporary filtered schema file
    if [ "$FILTERED_SCHEMA_FILE" != "$SCHEMA_FILE" ] && [ -f "$FILTERED_SCHEMA_FILE" ]; then
        rm -f "$FILTERED_SCHEMA_FILE"
    fi
    exit 1
fi
