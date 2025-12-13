#!/bin/bash

# Generate/create service plugin project structure
# Usage: ./generate-project.sh <service-name> [schema-file]

set -e  # Exit on any error

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Validate arguments
if [ $# -lt 1 ]; then
    echo -e "${RED}Usage: $0 <service-name> [schema-file]${NC}"
    echo "Example: $0 accounts"
    echo "Example: $0 accounts ../schemas/accounts-api.yaml"
    exit 1
fi

SERVICE_NAME="$1"
SCHEMA_FILE="${2:-../schemas/${SERVICE_NAME}-api.yaml}"

# Helper function to convert hyphenated names to PascalCase
to_pascal_case() {
    local input="$1"
    echo "$input" | sed 's/-/ /g' | awk '{for(i=1;i<=NF;i++) $i=toupper(substr($i,1,1)) tolower(substr($i,2))} 1' | sed 's/ //g'
}

SERVICE_PASCAL=$(to_pascal_case "$SERVICE_NAME")
SERVICE_DIR="../lib-${SERVICE_NAME}"
PROJECT_FILE="$SERVICE_DIR/lib-${SERVICE_NAME}.csproj"

echo -e "${YELLOW}🔧 Creating service plugin project: $SERVICE_NAME${NC}"
echo -e "  📋 Schema: $SCHEMA_FILE"
echo -e "  📁 Directory: $SERVICE_DIR"
echo -e "  📄 Project: $PROJECT_FILE"

# Validate schema file exists
if [ ! -f "$SCHEMA_FILE" ]; then
    echo -e "${RED}❌ Schema file not found: $SCHEMA_FILE${NC}"
    exit 1
fi

# Create service plugin directory if it doesn't exist
if [ ! -d "$SERVICE_DIR" ]; then
    echo -e "${YELLOW}📁 Creating service plugin directory...${NC}"
    mkdir -p "$SERVICE_DIR"
    echo -e "${GREEN}✅ Created directory: $SERVICE_DIR${NC}"
else
    echo -e "${YELLOW}📁 Service plugin directory already exists${NC}"
fi

# Create Generated subdirectory
mkdir -p "$SERVICE_DIR/Generated"

# Create project file if it doesn't exist
if [ ! -f "$PROJECT_FILE" ]; then
    echo -e "${YELLOW}📝 Creating service plugin project file...${NC}"

    cat > "$PROJECT_FILE" << EOF
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>BeyondImmersion.BannouService.$SERVICE_PASCAL</RootNamespace>
    <ServiceLib>$SERVICE_NAME</ServiceLib>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="lib-${SERVICE_NAME}.tests" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../bannou-service/bannou-service.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Core" Version="2.3.0" />
    <PackageReference Include="System.ComponentModel.Annotations" Version="5.0.0" />
  </ItemGroup>

  <Import Project="../ServiceLib.targets" />

</Project>
EOF

    echo -e "${GREEN}✅ Created project file: $PROJECT_FILE${NC}"

    # Add project to solution
    echo -e "${YELLOW}🔗 Adding project to solution...${NC}"
    cd "$(dirname "$0")/.."

    if dotnet sln add "$PROJECT_FILE" --verbosity quiet 2>/dev/null; then
        echo -e "${GREEN}✅ Added to solution${NC}"
    else
        echo -e "${YELLOW}⚠️  Project might already be in solution${NC}"
    fi

else
    echo -e "${YELLOW}📝 Project file already exists: $PROJECT_FILE${NC}"
fi

echo -e "${GREEN}✅ Service plugin project setup complete${NC}"
exit 0
