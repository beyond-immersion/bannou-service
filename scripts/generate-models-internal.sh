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

# Generate internal data models file for service (if it doesn't exist)
# Usage: ./generate-models-internal.sh <service-name> [schema-file]

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
SERVICE_DIR="../plugins/lib-${SERVICE_NAME}"
MODELS_FILE="$SERVICE_DIR/${SERVICE_PASCAL}ServiceModels.cs"

echo -e "${YELLOW}📦 Generating internal models file for: $SERVICE_NAME${NC}"
echo -e "  📋 Schema: $SCHEMA_FILE"
echo -e "  📁 Output: $MODELS_FILE"

# Validate schema file exists
if [ ! -f "$SCHEMA_FILE" ]; then
    echo -e "${RED}❌ Schema file not found: $SCHEMA_FILE${NC}"
    exit 1
fi

# Check if models file already exists
if [ -f "$MODELS_FILE" ]; then
    echo -e "${YELLOW}📝 Internal models file already exists, skipping: $MODELS_FILE${NC}"
    exit 0
fi

# Ensure service directory exists
mkdir -p "$SERVICE_DIR"

echo -e "${YELLOW}🔄 Creating internal models template...${NC}"

# Create internal models file
cat > "$MODELS_FILE" << EOF
namespace BeyondImmersion.BannouService.$SERVICE_PASCAL;

/// <summary>
/// Internal data models for ${SERVICE_PASCAL}Service.
/// </summary>
/// <remarks>
/// <para>
/// This file contains internal data models, DTOs, and helper structures used
/// exclusively by this service. These are NOT exposed via the API and are NOT
/// generated from schemas.
/// </para>
/// <para>
/// <b>When to add models here:</b>
/// <list type="bullet">
///   <item>Storage models for state stores (different from API request/response types)</item>
///   <item>Cache entry structures</item>
///   <item>Internal DTOs for service-to-service communication not exposed in API</item>
///   <item>Helper records for intermediate processing</item>
/// </list>
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Type Safety:</b> Internal models MUST use proper C# types
/// (enums, Guids, DateTimeOffset) - never string representations. "JSON requires strings"
/// is FALSE - BannouJson handles serialization correctly.
/// </para>
/// </remarks>
public partial class ${SERVICE_PASCAL}Service
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

// ============================================================================
// INTERNAL DATA MODELS
// ============================================================================
// Add your internal data models below. Examples:
//
// /// <summary>
// /// Internal storage model for [entity].
// /// </summary>
// internal class ${SERVICE_PASCAL}StorageModel
// {
//     public Guid Id { get; set; }
//     public string Name { get; set; } = string.Empty;
//     public DateTimeOffset CreatedAt { get; set; }
// }
//
// /// <summary>
// /// Cache entry for [purpose].
// /// </summary>
// internal record ${SERVICE_PASCAL}CacheEntry(Guid Id, string Data, DateTimeOffset CachedAt);
// ============================================================================
EOF

# Check if generation succeeded
if [ -f "$MODELS_FILE" ]; then
    FILE_SIZE=$(wc -l < "$MODELS_FILE" 2>/dev/null || echo "0")
    echo -e "${GREEN}✅ Generated internal models template ($FILE_SIZE lines)${NC}"
    echo -e "${YELLOW}💡 Add internal storage models, cache entries, and DTOs to this file${NC}"
    exit 0
else
    echo -e "${RED}❌ Failed to generate internal models file${NC}"
    exit 1
fi
