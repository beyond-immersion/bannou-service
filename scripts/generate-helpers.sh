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

# Generate service helpers partial class file (if it doesn't exist)
# Usage: ./generate-helpers.sh <service-name> [schema-file]

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
SCHEMA_FILE="${2:-../schemas/${SERVICE_NAME}-api.yaml}"

SERVICE_PASCAL=$(to_pascal_case "$SERVICE_NAME")
SERVICE_DIR="../plugins/lib-${SERVICE_NAME}"
HELPERS_FILE="$SERVICE_DIR/${SERVICE_PASCAL}Service.Helpers.cs"

echo -e "${YELLOW}🔧 Generating service helpers file for: $SERVICE_NAME${NC}"
echo -e "  📁 Output: $HELPERS_FILE"

# Validate schema file exists
if [ ! -f "$SCHEMA_FILE" ]; then
    echo -e "${RED}❌ Schema file not found: $SCHEMA_FILE${NC}"
    exit 1
fi

# Check if helpers file already exists
if [ -f "$HELPERS_FILE" ]; then
    echo -e "${YELLOW}📝 Service helpers file already exists, skipping: $HELPERS_FILE${NC}"
    exit 0
fi

# Ensure service directory exists
mkdir -p "$SERVICE_DIR"

echo -e "${YELLOW}🔄 Creating service helpers template...${NC}"

# Create helpers file
cat > "$HELPERS_FILE" << EOF
namespace BeyondImmersion.BannouService.$SERVICE_PASCAL;

// =============================================================================
// ${SERVICE_PASCAL}Service — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by ${SERVICE_PASCAL}Service. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (${SERVICE_PASCAL}Service.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in I${SERVICE_PASCAL}Service). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (${SERVICE_PASCAL}Service.Helpers.cs):
//     Contains all private/internal helper methods, core logic extracted
//     from endpoints, event publishing helpers, query builders, mapping
//     functions, and any other non-public methods. Every async method in
//     this file MUST call ITelemetryProvider.StartActivity to ensure
//     sub-operations are properly instrumented.
//
// Structural tests enforce both rules:
//   - Services_PrimaryFile_DoesNotCallStartActivity
//   - Services_HelperFiles_HaveStartActivityWhenAsync
//
// WHAT GOES HERE:
//   - Private async helper methods (with StartActivity spans)
//   - Private sync helper methods (query builders, mappers, validators)
//   - Internal static key builders (already in primary file by convention,
//     but may be moved here if the primary file is large)
//   - Event publishing helper methods
//   - Any extracted "core" logic (e.g., CreateAccountCoreAsync)
//
// WHAT STAYS IN THE PRIMARY FILE:
//   - Public interface method implementations (/// <inheritdoc/> methods)
//   - Constructor and field declarations
//   - Constants and key prefix definitions
//
// See: docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md (T30)
// See: docs/reference/HELPERS-AND-COMMON-PATTERNS.md
// =============================================================================

/// <summary>
/// Private and internal helper methods for ${SERVICE_PASCAL}Service.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class ${SERVICE_PASCAL}Service
{
    // Move private/internal helper methods here from ${SERVICE_PASCAL}Service.cs
}
EOF

# Check if generation succeeded
if [ -f "$HELPERS_FILE" ]; then
    FILE_SIZE=$(wc -l < "$HELPERS_FILE" 2>/dev/null || echo "0")
    echo -e "${GREEN}✅ Generated service helpers template ($FILE_SIZE lines)${NC}"
    echo -e "${YELLOW}💡 Move private/internal methods from ${SERVICE_PASCAL}Service.cs to this file${NC}"
    exit 0
else
    echo -e "${RED}❌ Failed to generate service helpers file${NC}"
    exit 1
fi
