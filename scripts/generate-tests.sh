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

# Generate unit test project for a service
# Usage: ./generate-tests.sh <service-name> <schema-file>

set -e  # Exit on any error

# Source common utilities
source "$(dirname "$0")/common.sh"

# Validate arguments
if [ $# -lt 2 ]; then
    log_error "Usage: $0 <service-name> <schema-file>"
    echo "Example: $0 account ../schemas/account-api.yaml"
    exit 1
fi

SERVICE_NAME="$1"
SCHEMA_FILE="$2"

SERVICE_PASCAL=$(to_pascal_case "$SERVICE_NAME")
TEST_PROJECT_DIR="../plugins/lib-${SERVICE_NAME}.tests"
TEST_PROJECT_FILE="$TEST_PROJECT_DIR/lib-${SERVICE_NAME}.tests.csproj"

echo -e "${YELLOW}🧪 Generating unit test project for: $SERVICE_NAME${NC}"
echo -e "  📋 Schema: $SCHEMA_FILE"
echo -e "  📁 Directory: $TEST_PROJECT_DIR"
echo -e "  📄 Project: $TEST_PROJECT_FILE"

# Validate schema file exists
if [ ! -f "$SCHEMA_FILE" ]; then
    echo -e "${RED}❌ Schema file not found: $SCHEMA_FILE${NC}"
    exit 1
fi

# Create test project directory if it doesn't exist
if [ ! -d "$TEST_PROJECT_DIR" ]; then
    echo -e "${YELLOW}📁 Creating test project directory...${NC}"
    mkdir -p "$TEST_PROJECT_DIR"
    echo -e "${GREEN}✅ Created directory: $TEST_PROJECT_DIR${NC}"
else
    echo -e "${YELLOW}📁 Test project directory already exists${NC}"
fi

# Create project file if it doesn't exist
if [ ! -f "$TEST_PROJECT_FILE" ]; then
    echo -e "${YELLOW}📝 Creating unit test project file...${NC}"

    cat > "$TEST_PROJECT_FILE" << EOF
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <RootNamespace>BeyondImmersion.BannouService.$SERVICE_PASCAL.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <!-- Test infrastructure packages only - other packages come from lib → bannou-service -->
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector" Version="6.0.4">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Moq" Version="4.20.72" />
    <!-- Logging.Abstractions, Mvc.Core come from lib-$SERVICE_NAME → bannou-service -->
  </ItemGroup>

  <ItemGroup>
    <!-- bannou-service comes transitively via lib-$SERVICE_NAME -->
    <ProjectReference Include="../lib-$SERVICE_NAME/lib-$SERVICE_NAME.csproj" />
    <!-- Shared test utilities (ServiceConstructorValidator, etc.) -->
    <ProjectReference Include="../../test-utilities/test-utilities.csproj" />
  </ItemGroup>

</Project>
EOF

    echo -e "${GREEN}✅ Created test project file: $TEST_PROJECT_FILE${NC}"

    # Create AssemblyInfo.cs for assembly-level test attributes
    ASSEMBLY_INFO_FILE="$TEST_PROJECT_DIR/AssemblyInfo.cs"
    if [ ! -f "$ASSEMBLY_INFO_FILE" ]; then
        echo -e "${YELLOW}📝 Creating AssemblyInfo.cs...${NC}"
        cat > "$ASSEMBLY_INFO_FILE" << 'ASSEMBLYEOF'
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
ASSEMBLYEOF
        echo -e "${GREEN}✅ Created AssemblyInfo.cs${NC}"
    fi

    # Add project to solution
    # Note: Path must be relative from repo root, not from scripts directory
    TEST_PROJECT_FILE_FROM_ROOT="plugins/lib-${SERVICE_NAME}.tests/lib-${SERVICE_NAME}.tests.csproj"
    echo -e "${YELLOW}🔗 Adding test project to solution...${NC}"

    ORIGINAL_DIR="$(pwd)"
    cd "$(dirname "$0")/.."

    if dotnet sln add "$TEST_PROJECT_FILE_FROM_ROOT" 2>&1; then
        echo -e "${GREEN}✅ Added test project to solution${NC}"
    else
        echo -e "${YELLOW}⚠️  Test project might already be in solution or add failed${NC}"
    fi

    cd "$ORIGINAL_DIR"

else
    echo -e "${YELLOW}📝 Test project file already exists: $TEST_PROJECT_FILE${NC}"

    # Still try to add to solution in case it's not there
    # Note: Path must be relative from repo root, not from scripts directory
    TEST_PROJECT_FILE_FROM_ROOT="plugins/lib-${SERVICE_NAME}.tests/lib-${SERVICE_NAME}.tests.csproj"

    ORIGINAL_DIR="$(pwd)"
    cd "$(dirname "$0")/.."

    if dotnet sln add "$TEST_PROJECT_FILE_FROM_ROOT" 2>&1; then
        echo -e "${GREEN}✅ Added existing test project to solution${NC}"
    fi

    cd "$ORIGINAL_DIR"
fi

# Create GlobalUsings.cs if it doesn't exist
GLOBAL_USINGS_FILE="$TEST_PROJECT_DIR/GlobalUsings.cs"
if [ ! -f "$GLOBAL_USINGS_FILE" ]; then
    echo -e "${YELLOW}📝 Creating GlobalUsings.cs...${NC}"

    # Ensure the target directory exists and has proper permissions
    if [ ! -d "$TEST_PROJECT_DIR" ]; then
        echo -e "${RED}❌ Test project directory does not exist: $TEST_PROJECT_DIR${NC}"
        exit 1
    fi

    # Create the file using printf to avoid heredoc redirection issues
    printf '%s\n' \
        'global using Xunit;' \
        'global using Microsoft.Extensions.Logging;' \
        'global using Microsoft.Extensions.DependencyInjection;' \
        'global using Moq;' \
        'global using System;' \
        'global using System.Threading.Tasks;' \
        > "$GLOBAL_USINGS_FILE"

    if [ $? -eq 0 ]; then
        echo -e "${GREEN}✅ Created GlobalUsings.cs${NC}"
    else
        echo -e "${RED}❌ Failed to create GlobalUsings.cs${NC}"
        exit 1
    fi
fi

# Per QUALITY TENETS: GlobalSuppressions.cs files with blanket CA1822 suppression are forbidden.
# xUnit does NOT require instance methods - the previous justification was incorrect.
# If CA1822 fires on a test method, either make it static or fix the underlying issue.
# See: docs/reference/tenets/QUALITY.md (Warning Suppression)
#
# NOTE: This script no longer creates GlobalSuppressions.cs files.
# Any existing files should be deleted as part of QUALITY TENETS compliance cleanup.

# Create basic service tests if they don't exist
SERVICE_TESTS_FILE="$TEST_PROJECT_DIR/${SERVICE_PASCAL}ServiceTests.cs"
if [ ! -f "$SERVICE_TESTS_FILE" ]; then
    echo -e "${YELLOW}📝 Creating service test class...${NC}"
    cat > "$SERVICE_TESTS_FILE" << EOF
using BeyondImmersion.BannouService.$SERVICE_PASCAL;
using BeyondImmersion.BannouService.TestUtilities;

namespace BeyondImmersion.BannouService.$SERVICE_PASCAL.Tests;

/// <summary>
/// Plugin-specific unit tests for ${SERVICE_PASCAL}Service.
///
/// NOTE: Constructor validation, configuration instantiation, key builder patterns,
/// hierarchy compliance, and other structural checks are handled centrally by
/// structural-tests/ (auto-discovered via [BannouService] attribute).
/// Only add plugin-specific business logic tests here.
///
/// See: docs/reference/tenets/TESTING-PATTERNS.md
/// </summary>
public class ${SERVICE_PASCAL}ServiceTests
{
    // TODO: Add service-specific tests based on schema operations
    // Schema file: $SCHEMA_FILE
    //
    // Guidelines:
    // - Use the Capture Pattern for event/state verification (see TESTING-PATTERNS.md)
    // - Verify side effects (saves, events, indices), not just response structure
    // - Keep Arrange < 50% of test code; extract helpers if needed
    // - Use EnumMappingValidator for any SDK boundary enum mappings
}
EOF
    echo -e "${GREEN}✅ Created service test class${NC}"
fi

echo -e "${GREEN}✅ Unit test project generation complete${NC}"
exit 0
