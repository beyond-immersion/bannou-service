#!/bin/bash

# Unified service generation script for NSwag controllers and Roslyn source generators
# This script ensures all code generation happens in one place with proper error handling

# Note: Not using "set -e" to allow individual failures while continuing with other schemas

echo "🔧 Starting unified service generation..."

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Set working directory to bannou-service
cd "$(dirname "$0")/bannou-service"

# NSwag executable path
NSWAG_EXE="$HOME/.nuget/packages/nswag.msbuild/14.2.0/tools/Net90/dotnet-nswag.exe"

# Verify NSwag executable exists
if [ ! -f "$NSWAG_EXE" ]; then
    echo -e "${RED}❌ NSwag executable not found at: $NSWAG_EXE${NC}"
    echo "Please ensure NSwag.MSBuild package is restored."
    exit 1
fi

echo -e "${YELLOW}📋 Generating NSwag controllers from schemas...${NC}"

# Function to generate controller from schema
generate_controller() {
    local schema_file="$1"
    local service_name="$2"
    local controller_name="${service_name^}Controller.Generated.cs"  # Capitalize first letter
    local output_path="Controllers/Generated/$controller_name"
    
    echo "  🔄 Generating $controller_name from $schema_file..."
    
    if [ ! -f "$schema_file" ]; then
        echo -e "${RED}    ⚠️  Schema file not found: $schema_file${NC}"
        return 1
    fi
    
    # Generate controller using direct NSwag command
    "$NSWAG_EXE" openapi2cscontroller \
        "/input:$schema_file" \
        "/output:$output_path" \
        "/namespace:BeyondImmersion.BannouService.Controllers.Generated" \
        "/ControllerStyle:Abstract" \
        "/ControllerBaseClass:Microsoft.AspNetCore.Mvc.ControllerBase" \
        "/UseCancellationToken:true" \
        "/UseActionResultType:true" \
        "/GenerateModelValidationAttributes:true" \
        "/GenerateDataAnnotations:true" \
        "/JsonLibrary:NewtonsoftJson" \
        "/GenerateNullableReferenceTypes:true" \
        "/NewLineBehavior:LF"
        
    # Check if NSwag command succeeded (exit code 0)
    if [ $? -eq 0 ]; then
        if [ -f "$output_path" ]; then
            local file_size=$(wc -l < "$output_path" 2>/dev/null || echo "0")
            echo -e "${GREEN}    ✅ Generated $controller_name ($file_size lines)${NC}"
        else
            echo -e "${YELLOW}    ⚠️  No output file created (no change detected)${NC}"
        fi
        return 0
    else
        echo -e "${RED}    ❌ Failed to generate $controller_name${NC}"
        return 1
    fi
}

# Generate controllers for all available schemas
generated_count=0
failed_count=0

# Core service schemas
schemas=(
    "../schemas/accounts-api.yaml:accounts"
    "../schemas/auth-api.yaml:auth" 
    "../schemas/website-api.yaml:website"
    "../schemas/behaviour-api.yaml:behaviour"
    "../schemas/connect-api.yaml:connect"
)

for schema_entry in "${schemas[@]}"; do
    IFS=':' read -r schema_file service_name <<< "$schema_entry"
    
    if generate_controller "$schema_file" "$service_name"; then
        ((generated_count++))
    else
        ((failed_count++))
    fi
done

# Generate event models if event schemas exist
echo -e "${YELLOW}📋 Checking for event schemas...${NC}"
event_schemas=($(find ../schemas -name "*-events.yaml" 2>/dev/null || true))

if [ ${#event_schemas[@]} -gt 0 ]; then
    echo "  🔄 Found ${#event_schemas[@]} event schema(s), generating event models..."
    
    # Use Roslyn source generators for event models
    echo -e "${YELLOW}📋 Generating Roslyn source generators...${NC}"
    
    if dotnet build -p:GenerateEventModels=true --verbosity quiet; then
        echo -e "${GREEN}    ✅ Event models generated successfully${NC}"
        ((generated_count++))
    else
        echo -e "${RED}    ❌ Failed to generate event models${NC}"
        ((failed_count++))
    fi
else
    echo "  📝 No event schemas found, skipping event model generation"
fi

# Generate unit test projects using Roslyn (if enabled)
echo -e "${YELLOW}📋 Generating unit test projects...${NC}"

if dotnet build -p:GenerateUnitTests=true --verbosity quiet; then
    echo -e "${GREEN}    ✅ Unit test projects generated successfully${NC}"
    ((generated_count++))
else
    echo -e "${RED}    ❌ Failed to generate unit test projects${NC}"
    ((failed_count++))
fi

# Fix line endings for all generated files
echo -e "${YELLOW}📋 Fixing line endings for EditorConfig compliance...${NC}"

if [ -f "../fix-generated-line-endings.sh" ]; then
    chmod +x "../fix-generated-line-endings.sh"
    if "../fix-generated-line-endings.sh"; then
        echo -e "${GREEN}    ✅ Line endings fixed${NC}"
    else
        echo -e "${RED}    ⚠️  Line ending fixes had issues${NC}"
    fi
else
    echo "    📝 No line ending fix script found, skipping"
fi

# Summary
echo
echo "📊 Generation Summary:"
echo "  ✅ Successfully generated: $generated_count items"
if [ $failed_count -gt 0 ]; then
    echo -e "  ❌ Failed to generate: $failed_count items"
fi

if [ $failed_count -eq 0 ]; then
    echo -e "${GREEN}🎉 All service generation completed successfully!${NC}"
    exit 0
else
    echo -e "${YELLOW}⚠️  Service generation completed with some failures${NC}"
    exit 1
fi