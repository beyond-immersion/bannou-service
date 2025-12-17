#!/bin/bash

# Generate unit test project for a service
# Usage: ./generate-tests.sh <service-name> <schema-file>

set -e  # Exit on any error

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Validate arguments
if [ $# -lt 2 ]; then
    echo -e "${RED}Usage: $0 <service-name> <schema-file>${NC}"
    echo "Example: $0 accounts ../schemas/accounts-api.yaml"
    exit 1
fi

SERVICE_NAME="$1"
SCHEMA_FILE="$2"

# Helper function to convert hyphenated names to PascalCase
to_pascal_case() {
    local input="$1"
    echo "$input" | sed 's/-/ /g' | awk '{for(i=1;i<=NF;i++) $i=toupper(substr($i,1,1)) tolower(substr($i,2))} 1' | sed 's/ //g'
}

SERVICE_PASCAL=$(to_pascal_case "$SERVICE_NAME")
TEST_PROJECT_DIR="../lib-${SERVICE_NAME}.tests"
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
    <!-- Dapr.Client, Logging.Abstractions, Mvc.Core come from lib-$SERVICE_NAME → bannou-service -->
  </ItemGroup>

  <ItemGroup>
    <!-- bannou-service comes transitively via lib-$SERVICE_NAME -->
    <ProjectReference Include="../lib-$SERVICE_NAME/lib-$SERVICE_NAME.csproj" />
  </ItemGroup>

</Project>
EOF

    echo -e "${GREEN}✅ Created test project file: $TEST_PROJECT_FILE${NC}"

    # Add project to solution
    # Note: Path must be relative from repo root, not from scripts directory
    TEST_PROJECT_FILE_FROM_ROOT="lib-${SERVICE_NAME}.tests/lib-${SERVICE_NAME}.tests.csproj"
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
    TEST_PROJECT_FILE_FROM_ROOT="lib-${SERVICE_NAME}.tests/lib-${SERVICE_NAME}.tests.csproj"

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

# Always create/overwrite GlobalSuppressions.cs to ensure latest suppressions are applied
GLOBAL_SUPPRESSIONS_FILE="$TEST_PROJECT_DIR/GlobalSuppressions.cs"
echo -e "${YELLOW}📝 Creating GlobalSuppressions.cs...${NC}"

cat > "$GLOBAL_SUPPRESSIONS_FILE" << 'EOF'
// This file is used to suppress code analysis warnings that are applied project-wide.
// Note: Compiler warnings (CS8620, CS8602, etc.) are suppressed via Directory.Build.props
// using the <NoWarn> property, which is the correct MSBuild approach for project-wide suppression.

using System.Diagnostics.CodeAnalysis;

// CA1822: Member does not access instance data and can be marked as static
// Test methods often don't access instance data but should remain instance methods for test framework compatibility
[assembly: SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Test methods should remain instance methods for test framework compatibility")]
EOF

if [ $? -eq 0 ]; then
    echo -e "${GREEN}✅ Created GlobalSuppressions.cs${NC}"
else
    echo -e "${RED}❌ Failed to create GlobalSuppressions.cs${NC}"
    exit 1
fi

# Create basic service tests if they don't exist
SERVICE_TESTS_FILE="$TEST_PROJECT_DIR/${SERVICE_PASCAL}ServiceTests.cs"
if [ ! -f "$SERVICE_TESTS_FILE" ]; then
    echo -e "${YELLOW}📝 Creating service test class...${NC}"
    cat > "$SERVICE_TESTS_FILE" << EOF
using BeyondImmersion.BannouService.$SERVICE_PASCAL;

namespace BeyondImmersion.BannouService.$SERVICE_PASCAL.Tests;

public class ${SERVICE_PASCAL}ServiceTests
{
    private Mock<ILogger<${SERVICE_PASCAL}Service>> _mockLogger = null!;
    private Mock<${SERVICE_PASCAL}ServiceConfiguration> _mockConfiguration = null!;

    public ${SERVICE_PASCAL}ServiceTests()
    {
        _mockLogger = new Mock<ILogger<${SERVICE_PASCAL}Service>>();
        _mockConfiguration = new Mock<${SERVICE_PASCAL}ServiceConfiguration>();
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        var service = new ${SERVICE_PASCAL}Service(_mockLogger.Object, _mockConfiguration.Object);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ${SERVICE_PASCAL}Service(null!, _mockConfiguration.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ${SERVICE_PASCAL}Service(_mockLogger.Object, null!));
    }

    // TODO: Add service-specific tests based on schema operations
    // Schema file: $SCHEMA_FILE
}

public class ${SERVICE_PASCAL}ConfigurationTests
{
    [Fact]
    public void Configuration_WithValidSettings_ShouldInitializeCorrectly()
    {
        // Arrange
        var config = new ${SERVICE_PASCAL}ServiceConfiguration();

        // Act & Assert
        Assert.NotNull(config);
    }

    // TODO: Add configuration-specific tests
}
EOF
    echo -e "${GREEN}✅ Created service test class${NC}"
fi

echo -e "${GREEN}✅ Unit test project generation complete${NC}"
exit 0
