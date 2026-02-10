#!/bin/bash

# Generate service plugin wrapper (if it doesn't exist)
# Usage: ./generate-plugin.sh <service-name> [schema-file]

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
PLUGIN_FILE="$SERVICE_DIR/${SERVICE_PASCAL}ServicePlugin.cs"

echo -e "${YELLOW}🔌 Generating service plugin wrapper for: $SERVICE_NAME${NC}"
echo -e "  📋 Schema: $SCHEMA_FILE"
echo -e "  📁 Output: $PLUGIN_FILE"

# Validate schema file exists
if [ ! -f "$SCHEMA_FILE" ]; then
    echo -e "${RED}❌ Schema file not found: $SCHEMA_FILE${NC}"
    exit 1
fi

# Check if plugin already exists
if [ -f "$PLUGIN_FILE" ]; then
    echo -e "${YELLOW}📝 Service plugin already exists, skipping: $PLUGIN_FILE${NC}"
    exit 0
fi

# Ensure service directory exists
mkdir -p "$SERVICE_DIR"

echo -e "${YELLOW}🔄 Creating service plugin wrapper...${NC}"

# Create service plugin wrapper using StandardServicePlugin<T> base class
# which handles all lifecycle boilerplate (ConfigureApplication, OnStartAsync,
# OnRunningAsync, OnShutdownAsync). Override ConfigureServices only if the
# service needs custom DI registrations (background services, caches, etc.).
cat > "$PLUGIN_FILE" << EOF
using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.$SERVICE_PASCAL;

/// <summary>
/// Plugin wrapper for $SERVICE_PASCAL service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class ${SERVICE_PASCAL}ServicePlugin : StandardServicePlugin<I${SERVICE_PASCAL}Service>
{
    public override string PluginName => "$SERVICE_NAME";
    public override string DisplayName => "$SERVICE_PASCAL Service";
}
EOF

# Check if generation succeeded
if [ -f "$PLUGIN_FILE" ]; then
    FILE_SIZE=$(wc -l < "$PLUGIN_FILE" 2>/dev/null || echo "0")
    echo -e "${GREEN}✅ Generated service plugin wrapper ($FILE_SIZE lines)${NC}"
    echo -e "${YELLOW}💡 Plugin bridges existing service implementation with new Plugin system${NC}"
    exit 0
else
    echo -e "${RED}❌ Failed to generate service plugin${NC}"
    exit 1
fi
