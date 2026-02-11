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

# Generate common API models that all services can access
# These models are placed in bannou-service/Generated/ so all services can reference them
# Contains shared types like EntityType that are not owned by any specific plugin

set -e

# Change to scripts directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Source common utilities
source "./common.sh"

log_info "Generating common API models"

# Find NSwag executable and ensure DOTNET_ROOT is set
require_nswag
ensure_dotnet_root

# Check if common-api.yaml exists
COMMON_API_SCHEMA="../schemas/common-api.yaml"
if [ ! -f "$COMMON_API_SCHEMA" ]; then
    echo -e "${RED}Schema file not found: $COMMON_API_SCHEMA${NC}"
    exit 1
fi

# Target directory for generated models
TARGET_DIR="../bannou-service/Generated"
mkdir -p "$TARGET_DIR"

# Generate common API models using NSwag
echo -e "${YELLOW}Generating CommonApi models...${NC}"

# Use NSwag to generate models from common-api.yaml
"$NSWAG_EXE" openapi2csclient \
    "/input:$COMMON_API_SCHEMA" \
    "/output:$TARGET_DIR/CommonApiModels.cs" \
    "/namespace:BeyondImmersion.BannouService" \
    "/generateClientClasses:false" \
    "/generateClientInterfaces:false" \
    "/generateDtoTypes:true" \
    "/excludedTypeNames:ApiException,ApiException\<TResult\>" \
    "/jsonLibrary:SystemTextJson" \
    "/generateNullableReferenceTypes:true" \
    "/newLineBehavior:LF" \
    "/templateDirectory:../templates/nswag"

if [ $? -eq 0 ]; then
    # Post-process: Wrap enums with CS1591 pragma suppressions (enum members cannot have XML docs)
    postprocess_enum_suppressions "$TARGET_DIR/CommonApiModels.cs"
    # Post-process: Add XML docs to AdditionalProperties
    postprocess_additional_properties_docs "$TARGET_DIR/CommonApiModels.cs"
    echo -e "${GREEN}Common API models generated successfully${NC}"
    echo -e "   Output: $TARGET_DIR/CommonApiModels.cs"
    echo -e "   Namespace: BeyondImmersion.BannouService"
else
    echo -e "${RED}Failed to generate common API models${NC}"
    exit 1
fi

echo -e "${GREEN}Common API generation complete!${NC}"
echo ""
echo -e "${BLUE}Available types:${NC}"
echo -e "  EntityType (system, account, character, actor, guild, organization, ...)"
echo ""
echo -e "${YELLOW}All services can now use: using BeyondImmersion.BannouService;${NC}"
