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

# Master service generation orchestrator
# Usage: ./generate-service.sh <service-name> [components...]
# Components: project, models, controller, client, interface, config, implementation, all

set -e  # Exit on any error

# Source common utilities
source "$(dirname "$0")/common.sh"

# Validate arguments
if [ $# -lt 1 ]; then
    log_error "Usage: $0 <service-name> [components...]"
    echo ""
    echo "Available components:"
    echo "  project        - Create service plugin project structure"
    echo "  models         - Generate models/DTOs from schema"
    echo "  controller     - Generate controller from schema"
    echo "  client         - Generate service client for mesh calls"
    echo "  interface      - Generate service interface from controller"
    echo "  config         - Generate service configuration class"
    echo "  implementation - Generate service implementation template"
    echo "  models-internal - Generate internal data models file"
    echo "  plugin         - Generate plugin wrapper for service discovery"
    echo "  permissions    - Generate permission registration from x-permissions"
    echo "  tests          - Generate unit test project with solution integration"
    echo "  all            - Generate all components (default)"
    echo ""
    echo "Examples:"
    echo "  $0 account all                    # Generate everything"
    echo "  $0 account controller client      # Generate only controller and client"
    echo "  $0 account                        # Generate everything (default)"
    exit 1
fi

SERVICE_NAME="$1"
shift # Remove service name from arguments

# Default to 'all' if no components specified
COMPONENTS=("${@:-all}")

SERVICE_PASCAL=$(to_pascal_case "$SERVICE_NAME")
SCHEMA_FILE="../schemas/${SERVICE_NAME}-api.yaml"
SCRIPT_DIR="$(dirname "$0")"

echo -e "${BLUE}🚀 Starting service generation for: $SERVICE_NAME${NC}"
echo -e "  📋 Schema: $SCHEMA_FILE"
echo -e "  🎯 Components: ${COMPONENTS[*]}"
echo ""

# Validate schema file exists
if [ ! -f "$SCHEMA_FILE" ]; then
    echo -e "${RED}❌ Schema file not found: $SCHEMA_FILE${NC}"
    exit 1
fi

# Track success/failure of each component
declare -a RESULTS=()

# Function to run a component generation script
run_component() {
    local component="$1"
    local script_name="generate-${component}.sh"
    local script_path="$SCRIPT_DIR/$script_name"

    echo -e "${YELLOW}🔄 Generating $component...${NC}"

    if [ -f "$script_path" ]; then
        # Config component uses -configuration.yaml, everything else uses -api.yaml
        local component_schema="$SCHEMA_FILE"
        if [ "$component" = "config" ]; then
            local config_schema="../schemas/${SERVICE_NAME}-configuration.yaml"
            if [ -f "$config_schema" ]; then
                component_schema="$config_schema"
            fi
        fi

        if "$script_path" "$SERVICE_NAME" "$component_schema"; then
            echo -e "${GREEN}✅ $component generation completed${NC}"
            RESULTS+=("$component: ✅ SUCCESS")
            return 0
        else
            echo -e "${RED}❌ $component generation failed${NC}"
            RESULTS+=("$component: ❌ FAILED")
            return 1
        fi
    else
        echo -e "${RED}❌ Script not found: $script_path${NC}"
        RESULTS+=("$component: ❌ SCRIPT NOT FOUND")
        return 1
    fi
}

# Function to check if a component should be generated
should_generate() {
    local component="$1"

    # If 'all' is specified, generate everything
    if [[ " ${COMPONENTS[*]} " =~ " all " ]]; then
        return 0
    fi

    # Check if component is explicitly requested
    if [[ " ${COMPONENTS[*]} " =~ " $component " ]]; then
        return 0
    fi

    return 1
}

# Generation order matters - dependencies first
declare -a GENERATION_ORDER=(
    "project"
    "models"
    "controller"
    "meta-controller"
    "event-subscriptions"
    "client"
    "interface"
    "config"
    "implementation"
    "models-internal"
    "plugin"
    "permissions"
    "tests"
)

# Generate components in dependency order
FAILED_COUNT=0

for component in "${GENERATION_ORDER[@]}"; do
    if should_generate "$component"; then
        echo ""
        if ! run_component "$component"; then
            ((FAILED_COUNT++))

            # For critical components, consider stopping
            if [[ "$component" == "project" || "$component" == "controller" ]]; then
                echo -e "${RED}⚠️  Critical component $component failed - stopping generation${NC}"
                break
            fi
        fi
    else
        echo -e "${BLUE}⏭️  Skipping $component (not requested)${NC}"
    fi
done

# Print summary
echo ""
echo -e "${BLUE}📊 Generation Summary for $SERVICE_NAME:${NC}"
echo "=================================="

for result in "${RESULTS[@]}"; do
    if [[ "$result" == *"SUCCESS"* ]]; then
        echo -e "${GREEN}$result${NC}"
    else
        echo -e "${RED}$result${NC}"
    fi
done

echo ""

if [ $FAILED_COUNT -eq 0 ]; then
    echo -e "${GREEN}🎉 All requested components generated successfully!${NC}"
    exit 0
else
    echo -e "${RED}⚠️  $FAILED_COUNT component(s) failed to generate${NC}"
    exit 1
fi
