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

# Generate/create service plugin project structure
# Usage: ./generate-project.sh <service-name> [schema-file]

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
  <Import Project="../ServiceLib.targets" />
  <!--
    ServiceLib.targets provides:
    - TargetFramework (net9.0)
    - ImplicitUsings (enable)
    - Nullable (enable)
    - RootNamespace base (BeyondImmersion.BannouService)
    - ProjectReference to bannou-service with Private="false"

    DO NOT redefine these properties - extend RootNamespace using \$(RootNamespace).YourExtension
  -->

  <PropertyGroup>
    <!-- Extends base RootNamespace from ServiceLib.targets -->
    <RootNamespace>\$(RootNamespace).$SERVICE_PASCAL</RootNamespace>
    <ServiceLib>$SERVICE_NAME</ServiceLib>
  </PropertyGroup>

  <ItemGroup>
    <!-- Infrastructure libs (lib-state, lib-messaging, lib-mesh) are accessed via DI interfaces -->
    <!-- from bannou-service (IStateStoreFactory, IMessageBus, I*Client, etc.) -->
    <!-- DO NOT add ProjectReferences to infrastructure libs - use injected interfaces instead -->
    <!-- Add plugin-to-plugin references here ONLY for non-infrastructure cross-plugin types -->
  </ItemGroup>

  <ItemGroup>
    <!-- Only packages NOT provided by bannou-service - NSwag, etc. come transitively -->
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Core" Version="2.3.0" />
    <PackageReference Include="System.ComponentModel.Annotations" Version="5.0.0" />
  </ItemGroup>

</Project>
EOF

    echo -e "${GREEN}✅ Created project file: $PROJECT_FILE${NC}"

    # Create AssemblyInfo.cs for assembly-level attributes
    ASSEMBLY_INFO_FILE="$SERVICE_DIR/AssemblyInfo.cs"
    if [ ! -f "$ASSEMBLY_INFO_FILE" ]; then
        echo -e "${YELLOW}📝 Creating AssemblyInfo.cs...${NC}"
        cat > "$ASSEMBLY_INFO_FILE" << 'ASSEMBLYEOF'
using Microsoft.AspNetCore.Mvc;
using System.Runtime.CompilerServices;

[assembly: ApiController]
ASSEMBLYEOF
        # Add InternalsVisibleTo for tests and Moq
        echo "[assembly: InternalsVisibleTo(\"lib-${SERVICE_NAME}.tests\")]" >> "$ASSEMBLY_INFO_FILE"
        echo "[assembly: InternalsVisibleTo(\"DynamicProxyGenAssembly2\")]" >> "$ASSEMBLY_INFO_FILE"
        echo -e "${GREEN}✅ Created AssemblyInfo.cs${NC}"
    fi

    # Add project to solution
    # Note: Path must be relative from repo root, not from scripts directory
    PROJECT_FILE_FROM_ROOT="plugins/lib-${SERVICE_NAME}/lib-${SERVICE_NAME}.csproj"
    echo -e "${YELLOW}🔗 Adding project to solution...${NC}"

    ORIGINAL_DIR="$(pwd)"
    cd "$(dirname "$0")/.."

    if dotnet sln add "$PROJECT_FILE_FROM_ROOT" 2>&1; then
        echo -e "${GREEN}✅ Added to solution${NC}"
    else
        echo -e "${YELLOW}⚠️  Project might already be in solution or add failed${NC}"
    fi

    cd "$ORIGINAL_DIR"

else
    echo -e "${YELLOW}📝 Project file already exists: $PROJECT_FILE${NC}"

    # Still try to add to solution in case it's not there
    # Note: Path must be relative from repo root, not from scripts directory
    PROJECT_FILE_FROM_ROOT="plugins/lib-${SERVICE_NAME}/lib-${SERVICE_NAME}.csproj"

    ORIGINAL_DIR="$(pwd)"
    cd "$(dirname "$0")/.."

    if dotnet sln add "$PROJECT_FILE_FROM_ROOT" 2>&1; then
        echo -e "${GREEN}✅ Added existing project to solution${NC}"
    fi

    cd "$ORIGINAL_DIR"
fi

echo -e "${GREEN}✅ Service plugin project setup complete${NC}"
exit 0
