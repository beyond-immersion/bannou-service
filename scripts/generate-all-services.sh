#!/bin/bash

# Master service generation orchestrator using modular scripts
# Usage: ./generate-all-services.sh [service-name] [components...]
# If no service-name provided, processes all services
# Components: project, models, controller, client, interface, config, implementation, all

set -e  # Exit on any error

# Change to scripts directory to ensure all relative paths work correctly
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Source common utilities
source "./common.sh"

# Parse arguments
REQUESTED_SERVICE=""
COMPONENTS=()

if [ $# -gt 0 ]; then
    REQUESTED_SERVICE="$1"
    shift
    COMPONENTS=("${@:-all}")
else
    COMPONENTS=("all")
fi

echo -e "${BLUE}🚀 Starting unified service generation${NC}"
if [ -n "$REQUESTED_SERVICE" ]; then
    echo -e "  🎯 Service: $REQUESTED_SERVICE"
else
    echo -e "  📋 Processing all services"
fi
echo -e "  🎯 Components: ${COMPONENTS[*]}"
echo ""

# Generate lifecycle events from x-lifecycle definitions in schemas
echo -e "${BLUE}🔄 Generating lifecycle events from x-lifecycle definitions...${NC}"
if python3 "$SCRIPT_DIR/generate-lifecycle-events.py"; then
    echo -e "${GREEN}✅ Lifecycle events generated successfully${NC}"
else
    echo -e "${RED}❌ Failed to generate lifecycle events${NC}"
    exit 1
fi
echo ""

# Generate common events first (shared across all services)
echo -e "${BLUE}🌟 Generating common events first...${NC}"
if ./generate-common-events.sh; then
    echo -e "${GREEN}✅ Common events generated successfully${NC}"
else
    echo -e "${RED}❌ Failed to generate common events${NC}"
    exit 1
fi
echo ""

# Generate service-specific events ({service}-events.yaml files)
echo -e "${BLUE}📡 Generating service-specific events...${NC}"
if ./generate-service-events.sh; then
    echo -e "${GREEN}✅ Service-specific events generated successfully${NC}"
else
    echo -e "${RED}❌ Failed to generate service-specific events${NC}"
    exit 1
fi
echo ""

# Generate client events (server-to-client push events via WebSocket)
echo -e "${BLUE}🌟 Generating client events...${NC}"
if ./generate-client-events.sh; then
    echo -e "${GREEN}✅ Client events generated successfully${NC}"
else
    echo -e "${RED}❌ Failed to generate client events${NC}"
    exit 1
fi

# Generate client event whitelist (for Connect service validation)
echo -e "${BLUE}🔐 Generating client event whitelist...${NC}"
if ./generate-client-event-whitelist.sh; then
    echo -e "${GREEN}✅ Client event whitelist generated successfully${NC}"
else
    echo -e "${RED}❌ Failed to generate client event whitelist${NC}"
    exit 1
fi
echo ""

# Generate event subscription registry (for NativeEventConsumerBackend deserialization)
echo -e "${BLUE}📋 Generating event subscription registry...${NC}"
if ./generate-event-subscription-registry.sh; then
    echo -e "${GREEN}✅ Event subscription registry generated successfully${NC}"
else
    echo -e "${RED}❌ Failed to generate event subscription registry${NC}"
    exit 1
fi
echo ""

# Embed meta schemas into API schemas for companion endpoint generation
echo -e "${BLUE}📋 Embedding meta schemas into API schemas...${NC}"
if python3 "$SCRIPT_DIR/embed-meta-schemas.py"; then
    echo -e "${GREEN}✅ Meta schemas embedded successfully${NC}"
else
    echo -e "${RED}❌ Failed to embed meta schemas${NC}"
    exit 1
fi
echo ""

# Find all schema files
SCHEMA_FILES=(../schemas/*-api.yaml)
if [ ! -e "${SCHEMA_FILES[0]}" ]; then
    echo -e "${RED}❌ No API schema files found in ../schemas/${NC}"
    exit 1
fi

# Track results
declare -a RESULTS=()
TOTAL_SERVICES=0
SUCCESSFUL_SERVICES=0

# Process each schema file
for schema_file in "${SCHEMA_FILES[@]}"; do
    # Extract service name from schema filename (e.g. "accounts-api.yaml" -> "accounts")
    service_name=$(basename "$schema_file" | sed 's/-api\.yaml$//')

    # Skip if specific service requested and this isn't it
    if [ -n "$REQUESTED_SERVICE" ] && [ "$service_name" != "$REQUESTED_SERVICE" ]; then
        continue
    fi

    TOTAL_SERVICES=$((TOTAL_SERVICES + 1))

    echo -e "${YELLOW}🔧 Processing service: $service_name${NC}"
    echo -e "  📋 Schema: $schema_file"

    # Call the modular service generation script
    if ./generate-service.sh "$service_name" "${COMPONENTS[@]}"; then
        echo -e "${GREEN}✅ Successfully processed $service_name${NC}"
        RESULTS+=("$service_name: ✅ SUCCESS")
        SUCCESSFUL_SERVICES=$((SUCCESSFUL_SERVICES + 1))
    else
        echo -e "${RED}❌ Failed to process $service_name${NC}"
        RESULTS+=("$service_name: ❌ FAILED")

        # If specific service requested, exit on failure
        if [ -n "$REQUESTED_SERVICE" ]; then
            echo -e "${RED}⚠️  Requested service $REQUESTED_SERVICE failed - stopping${NC}"
            exit 1
        fi
    fi

    echo ""
done

# Post-process generated files to fix NSwag AdditionalProperties lazy initialization
# This pattern causes invalid JSON when serialized: empty {} appears as ,{} which breaks parsing
# See: https://github.com/RicoSuter/NSwag/issues/3420
echo -e "${BLUE}🔧 Post-processing: Fixing AdditionalProperties lazy initialization...${NC}"

# Find all generated .cs files and fix the problematic pattern
GENERATED_FILES=($(find .. -path "*/Generated/*.cs" -o -name "*.Generated.cs" 2>/dev/null))
FIXED_COUNT=0

for file in "${GENERATED_FILES[@]}"; do
    if grep -q "return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>())" "$file" 2>/dev/null; then
        # Fix the getter to return null instead of creating empty dictionary
        # Change: get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
        # To:     get => _additionalProperties;
        sed -i 's/get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }/get => _additionalProperties;/g' "$file"

        # Also make the property nullable to match the backing field
        # Change: public System.Collections.Generic.IDictionary<string, object> AdditionalProperties
        # To:     public System.Collections.Generic.IDictionary<string, object>? AdditionalProperties
        sed -i 's/public System.Collections.Generic.IDictionary<string, object> AdditionalProperties/public System.Collections.Generic.IDictionary<string, object>? AdditionalProperties/g' "$file"

        FIXED_COUNT=$((FIXED_COUNT + 1))
    fi
done

if [ $FIXED_COUNT -gt 0 ]; then
    echo -e "${GREEN}✅ Fixed AdditionalProperties in $FIXED_COUNT files${NC}"
else
    echo -e "${YELLOW}ℹ️  No AdditionalProperties patterns found to fix${NC}"
fi
echo ""

# Post-process generated files to remove NSwag pragma warning blocks
# Per QUALITY TENETS: Warning suppressions are forbidden except for specific documented exceptions.
# We remove all pragmas and rely on .editorconfig for the minimal acceptable exceptions.
# See: docs/reference/tenets/QUALITY.md (Warning Suppression)
echo -e "${BLUE}🔧 Post-processing: Removing NSwag pragma warning blocks (QUALITY TENETS compliance)...${NC}"

PRAGMA_REMOVED_COUNT=0

for file in "${GENERATED_FILES[@]}"; do
    if grep -q "^#pragma warning disable 108" "$file" 2>/dev/null; then
        # Remove all 15 pragma warning disable lines that NSwag generates
        # These are lines 9-23 in NSwag output, all starting with #pragma warning disable
        sed -i '/^#pragma warning disable 108/d' "$file"
        sed -i '/^#pragma warning disable 114/d' "$file"
        sed -i '/^#pragma warning disable 472/d' "$file"
        sed -i '/^#pragma warning disable 612/d' "$file"
        sed -i '/^#pragma warning disable 649/d' "$file"
        sed -i '/^#pragma warning disable 1573/d' "$file"
        sed -i '/^#pragma warning disable 1591/d' "$file"
        sed -i '/^#pragma warning disable 8073/d' "$file"
        sed -i '/^#pragma warning disable 3016/d' "$file"
        sed -i '/^#pragma warning disable 8600/d' "$file"
        sed -i '/^#pragma warning disable 8602/d' "$file"
        sed -i '/^#pragma warning disable 8603/d' "$file"
        sed -i '/^#pragma warning disable 8604/d' "$file"
        sed -i '/^#pragma warning disable 8625/d' "$file"
        sed -i '/^#pragma warning disable 8765/d' "$file"

        PRAGMA_REMOVED_COUNT=$((PRAGMA_REMOVED_COUNT + 1))
    fi
done

if [ $PRAGMA_REMOVED_COUNT -gt 0 ]; then
    echo -e "${GREEN}✅ Removed pragma blocks from $PRAGMA_REMOVED_COUNT files${NC}"
else
    echo -e "${YELLOW}ℹ️  No pragma blocks found to remove${NC}"
fi
echo ""

# Post-process generated files to remove null checks on enum parameters (CS0472)
# NSwag generates null checks for all parameters, but enums are value types that can never be null.
# These checks cause CS0472: "The result of the expression is always 'false'"
# Per QUALITY TENETS: Fix via post-processing rather than suppression.
echo -e "${BLUE}🔧 Post-processing: Removing null checks on enum parameters (CS0472)...${NC}"

ENUM_NULL_CHECK_FIXED=0

# Known enum types from schemas that NSwag generates null checks for
# Pattern: if (enumVar == null)\n    throw new System.ArgumentNullException("enumVar");
ENUM_TYPES=("connection" "upgrade" "provider")

for file in "${GENERATED_FILES[@]}"; do
    for enum_var in "${ENUM_TYPES[@]}"; do
        # Check if this file has the pattern
        if grep -q "if (${enum_var} == null)" "$file" 2>/dev/null; then
            # Remove the two-line null check block using sed with pattern matching
            # Match: if (enumVar == null)\n followed by throw line
            sed -i "/${enum_var} == null/,/ArgumentNullException/d" "$file"
            ENUM_NULL_CHECK_FIXED=$((ENUM_NULL_CHECK_FIXED + 1))
        fi
    done
done

if [ $ENUM_NULL_CHECK_FIXED -gt 0 ]; then
    echo -e "${GREEN}✅ Removed enum null checks in $ENUM_NULL_CHECK_FIXED locations${NC}"
else
    echo -e "${YELLOW}ℹ️  No enum null checks found to remove${NC}"
fi
echo ""

# Post-process generated files to replace hardcoded "bannou" app-id defaults with AppConstants.DEFAULT_APP_NAME
# Per CLAUDE.md: We shouldn't have hardcoded "bannou" values - use the constant for consistency.
# This replaces specific property defaults that represent the app-id, NOT product names like "bannou-dlx".
echo -e "${BLUE}🔧 Post-processing: Replacing hardcoded app-id defaults with AppConstants.DEFAULT_APP_NAME...${NC}"

APPID_REPLACED_COUNT=0

# Properties that represent the default app-id and should use the constant
APPID_PROPERTIES=("DefaultAppId" "DefaultExchange" "DeploymentMode" "ControlPlaneAppId" "Exchange")

for file in "${GENERATED_FILES[@]}"; do
    for prop in "${APPID_PROPERTIES[@]}"; do
        # Pattern: public string PropName { get; set; } = "bannou";
        # Replace with: public string PropName { get; set; } = AppConstants.DEFAULT_APP_NAME;
        if grep -q "public string ${prop} { get; set; } = \"bannou\";" "$file" 2>/dev/null; then
            sed -i "s/public string ${prop} { get; set; } = \"bannou\";/public string ${prop} { get; set; } = AppConstants.DEFAULT_APP_NAME;/" "$file"
            APPID_REPLACED_COUNT=$((APPID_REPLACED_COUNT + 1))
        fi
    done
done

if [ $APPID_REPLACED_COUNT -gt 0 ]; then
    echo -e "${GREEN}✅ Replaced app-id defaults in $APPID_REPLACED_COUNT locations${NC}"
else
    echo -e "${YELLOW}ℹ️  No app-id defaults found to replace${NC}"
fi
echo ""

# Print summary
echo -e "${BLUE}📊 Generation Summary:${NC}"
echo "=========================="

if [ ${#RESULTS[@]} -eq 0 ]; then
    if [ -n "$REQUESTED_SERVICE" ]; then
        echo -e "${RED}❌ Service '$REQUESTED_SERVICE' not found${NC}"
        echo "Available services:"
        for schema_file in "${SCHEMA_FILES[@]}"; do
            service_name=$(basename "$schema_file" | sed 's/-api\.yaml$//')
            echo "  - $service_name"
        done
        exit 1
    else
        echo -e "${RED}❌ No services found to process${NC}"
        exit 1
    fi
fi

for result in "${RESULTS[@]}"; do
    if [[ "$result" == *"SUCCESS"* ]]; then
        echo -e "${GREEN}$result${NC}"
    else
        echo -e "${RED}$result${NC}"
    fi
done

echo ""
echo -e "${BLUE}📈 Statistics:${NC}"
echo -e "  Total services: $TOTAL_SERVICES"
echo -e "  Successful: $SUCCESSFUL_SERVICES"
echo -e "  Failed: $((TOTAL_SERVICES - SUCCESSFUL_SERVICES))"

if [ $SUCCESSFUL_SERVICES -eq $TOTAL_SERVICES ]; then
    echo ""
    echo -e "${GREEN}🎉 All services generated successfully!${NC}"
    exit 0
else
    echo ""
    echo -e "${RED}⚠️  Some services failed to generate${NC}"
    exit 1
fi
