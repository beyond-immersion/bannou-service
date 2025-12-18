#!/bin/bash

# Master service generation orchestrator using modular scripts
# Usage: ./generate-all-services.sh [service-name] [components...]
# If no service-name provided, processes all services
# Components: project, models, controller, client, interface, config, implementation, all

set -e  # Exit on any error

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Change to scripts directory to ensure all relative paths work correctly
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

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
