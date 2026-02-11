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

# Permission registration code generator
# Usage: ./generate-permissions.sh <service-name> <schema-file>
# Extracts x-permissions from OpenAPI schema and generates RegisterServicePermissionsAsync code

set -e

# Source common utilities
source "$(dirname "$0")/common.sh"

if [ $# -lt 2 ]; then
    log_error "Usage: $0 <service-name> <schema-file>"
    exit 1
fi

SERVICE_NAME="$1"
SCHEMA_FILE="$2"

SERVICE_PASCAL=$(to_pascal_case "$SERVICE_NAME")
PROJECT_DIR="../plugins/lib-${SERVICE_NAME}"
OUTPUT_DIR="${PROJECT_DIR}/Generated"
OUTPUT_FILE="${OUTPUT_DIR}/${SERVICE_PASCAL}PermissionRegistration.cs"

echo -e "${BLUE}🔐 Generating permission registration for: $SERVICE_NAME${NC}"

# Check if schema file exists
if [ ! -f "$SCHEMA_FILE" ]; then
    echo -e "${RED}❌ Schema file not found: $SCHEMA_FILE${NC}"
    exit 1
fi

# Check if project directory exists
if [ ! -d "$PROJECT_DIR" ]; then
    echo -e "${YELLOW}⚠️  Project directory not found: $PROJECT_DIR - skipping permission generation${NC}"
    exit 0
fi

# Ensure Generated directory exists
mkdir -p "$OUTPUT_DIR"

# Clean up old .Generated.cs file if it exists (legacy naming)
OLD_FILE="${OUTPUT_DIR}/${SERVICE_PASCAL}PermissionRegistration.Generated.cs"
if [ -f "$OLD_FILE" ]; then
    rm "$OLD_FILE"
    echo -e "  🧹 Cleaned up legacy file: $(basename "$OLD_FILE")"
fi

# Extract service version from schema info.version
# Use awk to find the version field within the info block (handles multiline descriptions)
SERVICE_VERSION=$(awk '
/^info:/ { in_info=1; next }
in_info && /^[^ ]/ { in_info=0 }
in_info && /^  version:/ { gsub(/.*version:[[:space:]]*/, ""); gsub(/["\047]/, ""); print; exit }
' "$SCHEMA_FILE")
if [ -z "$SERVICE_VERSION" ]; then
    SERVICE_VERSION="1.0.0"
fi

echo -e "  📋 Service version: $SERVICE_VERSION"

# Use Python to parse YAML and extract x-permissions (more reliable than bash YAML parsing)
# Fall back to a simpler approach if Python/PyYAML not available

PYTHON_SCRIPT=$(cat << 'PYEOF'
import sys
import json

try:
    import yaml
except ImportError:
    # PyYAML not available - use simple regex extraction
    print("PYYAML_NOT_AVAILABLE", file=sys.stderr)
    sys.exit(2)

schema_file = sys.argv[1]

with open(schema_file, 'r') as f:
    schema = yaml.safe_load(f)

endpoints = []

paths = schema.get('paths', {})
for path, methods in paths.items():
    if not isinstance(methods, dict):
        continue

    for method, operation in methods.items():
        if method.startswith('x-') or not isinstance(operation, dict):
            continue

        permissions = operation.get('x-permissions', [])
        if not permissions:
            continue

        endpoint_info = {
            'path': path,
            'method': method.upper(),
            'operationId': operation.get('operationId', ''),
            'permissions': []
        }

        for perm in permissions:
            if isinstance(perm, dict):
                endpoint_info['permissions'].append({
                    'role': perm.get('role', 'user'),
                    'states': perm.get('states', {}) or {}
                })

        if endpoint_info['permissions']:
            endpoints.append(endpoint_info)

# Output as JSON
print(json.dumps(endpoints))
PYEOF
)

# Try Python extraction first
PERMISSIONS_JSON=""
if command -v python3 &> /dev/null; then
    PERMISSIONS_JSON=$(echo "$PYTHON_SCRIPT" | python3 - "$SCHEMA_FILE" 2>/dev/null) || true
fi

# If Python extraction failed or returned empty, check if there are any x-permissions in the file
if [ -z "$PERMISSIONS_JSON" ] || [ "$PERMISSIONS_JSON" == "[]" ]; then
    # Check if x-permissions exists in the file at all
    if ! grep -q "x-permissions:" "$SCHEMA_FILE"; then
        echo -e "${YELLOW}⚠️  No x-permissions found in schema - generating empty registration${NC}"
        PERMISSIONS_JSON="[]"
    else
        echo -e "${YELLOW}⚠️  Could not parse x-permissions (Python/PyYAML required for complex parsing)${NC}"
        echo -e "  To enable permission extraction, install: pip install pyyaml"
        PERMISSIONS_JSON="[]"
    fi
fi

# Count endpoints with permissions
ENDPOINT_COUNT=$(echo "$PERMISSIONS_JSON" | python3 -c "import sys,json; print(len(json.load(sys.stdin)))" 2>/dev/null || echo "0")
echo -e "  📊 Found $ENDPOINT_COUNT endpoints with permissions"

# Generate C# code
cat > "$OUTPUT_FILE" << CSHARP_EOF
//----------------------
// <auto-generated>
//     Generated from OpenAPI schema: schemas/${SERVICE_NAME}-api.yaml
//
//     WARNING: DO NOT EDIT THIS FILE (FOUNDATION TENETS)
//     This file is auto-generated from x-permissions sections in the schema.
//     Any manual changes will be overwritten on next generation.
//
//     To modify permissions:
//     1. Edit x-permissions sections in schemas/${SERVICE_NAME}-api.yaml
//     2. Run: scripts/generate-all-services.sh
//
//     See: docs/reference/tenets/FOUNDATION.md
// </auto-generated>
//----------------------

#nullable enable

using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.${SERVICE_PASCAL};

/// <summary>
/// Generated permission registration for ${SERVICE_PASCAL} service.
/// Contains permission matrix extracted from x-permissions sections in OpenAPI schema.
/// </summary>
public static class ${SERVICE_PASCAL}PermissionRegistration
{
    /// <summary>
    /// Service ID for permission registration.
    /// </summary>
    public const string ServiceId = "${SERVICE_NAME}";

    /// <summary>
    /// Service version from OpenAPI schema.
    /// </summary>
    public const string ServiceVersion = "${SERVICE_VERSION}";

    /// <summary>
    /// Generates the ServiceRegistrationEvent containing all endpoint permissions.
    /// </summary>
    /// <param name="instanceId">The unique instance GUID for this bannou instance</param>
    /// <param name="appId">The effective app ID for this service instance</param>
    public static ServiceRegistrationEvent CreateRegistrationEvent(Guid instanceId, string appId)
    {
        return new ServiceRegistrationEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ServiceId = instanceId,
            ServiceName = ServiceId,
            Version = ServiceVersion,
            AppId = appId,
            Endpoints = GetEndpoints()
        };
    }

    /// <summary>
    /// Gets the list of endpoints with their permission requirements.
    /// </summary>
    public static ICollection<ServiceEndpoint> GetEndpoints()
    {
        var endpoints = new List<ServiceEndpoint>();
CSHARP_EOF

# Parse JSON and generate endpoint entries
if [ "$PERMISSIONS_JSON" != "[]" ] && [ -n "$PERMISSIONS_JSON" ]; then
    echo "$PERMISSIONS_JSON" | python3 -c "
import sys
import json

data = json.load(sys.stdin)

for endpoint in data:
    path = endpoint['path']
    method = endpoint['method']
    operation_id = endpoint.get('operationId', '')
    permissions = endpoint['permissions']

    print(f'''
        endpoints.Add(new ServiceEndpoint
        {{
            Path = \"{path}\",
            Method = ServiceEndpointMethod.{method},
            Description = \"{operation_id}\",
            Permissions = new List<PermissionRequirement>
            {{''')

    for perm in permissions:
        role = perm['role']
        states = perm['states']
        states_dict = ', '.join([f'{{\"{k}\", \"{v}\"}}' for k, v in states.items()]) if states else ''

        print(f'''                new PermissionRequirement
                {{
                    Role = \"{role}\",
                    RequiredStates = new Dictionary<string, string> {{ {states_dict} }}
                }},''')

    print('''            }
        });''')
" >> "$OUTPUT_FILE"
fi

# Complete the static class (data methods only, no event publishing)
cat >> "$OUTPUT_FILE" << 'CSHARP_FOOTER'

        return endpoints;
    }

    /// <summary>
    /// Builds the permission matrix for RegisterServicePermissionsAsync.
    /// Key structure: state -> role -> list of methods
    /// State key construction must match PermissionService.RecompileSessionPermissionsAsync:
    /// - Same service (s.Key == ServiceId): use just s.Value (e.g., "ringing")
    /// - Cross-service: use "{s.Key}:{s.Value}" (e.g., "game-session:in_game")
    /// </summary>
    public static Dictionary<string, IDictionary<string, ICollection<string>>> BuildPermissionMatrix()
    {
        var matrix = new Dictionary<string, IDictionary<string, ICollection<string>>>();

        foreach (var endpoint in GetEndpoints())
        {
            var methodKey = endpoint.Path;

            foreach (var permission in endpoint.Permissions)
            {
                // Determine state key - use "default" if no specific states required
                // For same-service states, use just the value to match lookup logic
                var stateKey = permission.RequiredStates.Count > 0
                    ? string.Join("|", permission.RequiredStates.Select(s =>
                        s.Key == ServiceId ? s.Value : $"{s.Key}:{s.Value}"))
                    : "default";

                if (!matrix.TryGetValue(stateKey, out var roleMap))
                {
                    roleMap = new Dictionary<string, ICollection<string>>();
                    matrix[stateKey] = roleMap;
                }

                if (!roleMap.TryGetValue(permission.Role, out var methods))
                {
                    methods = new List<string>();
                    roleMap[permission.Role] = methods;
                }

                if (!methods.Contains(methodKey))
                {
                    methods.Add(methodKey);
                }
            }
        }

        return matrix;
    }

}
CSHARP_FOOTER

# Generate partial class overlay for DI-based permission registration
# Skip for services with custom registration logic (orchestrator has SecureWebsocket conditional)
SKIP_PARTIAL_SERVICES="orchestrator"

if [[ ! " $SKIP_PARTIAL_SERVICES " =~ " $SERVICE_NAME " ]]; then
    cat >> "$OUTPUT_FILE" << CSHARP_PARTIAL

/// <summary>
/// Partial class overlay: registers ${SERVICE_PASCAL} service permissions via DI registry.
/// Generated from x-permissions sections in ${SERVICE_NAME}-api.yaml.
/// Push-based: this service pushes its permission matrix TO the IPermissionRegistry.
/// </summary>
public partial class ${SERVICE_PASCAL}Service
{
    /// <summary>
    /// Registers this service's permissions with the Permission service via DI registry.
    /// Called by PluginLoader during startup with the resolved IPermissionRegistry.
    /// </summary>
    async Task IBannouService.RegisterServicePermissionsAsync(
        string appId, IPermissionRegistry? registry)
    {
        if (registry != null)
        {
            await registry.RegisterServiceAsync(
                ${SERVICE_PASCAL}PermissionRegistration.ServiceId,
                ${SERVICE_PASCAL}PermissionRegistration.ServiceVersion,
                ${SERVICE_PASCAL}PermissionRegistration.BuildPermissionMatrix());
        }
    }
}
CSHARP_PARTIAL
    echo -e "  🔗 Generated partial class overlay for ${SERVICE_PASCAL}Service"
else
    echo -e "  ⚡ Skipped partial class overlay for ${SERVICE_NAME} (custom registration logic)"
fi

echo -e "${GREEN}✅ Generated: $OUTPUT_FILE${NC}"
