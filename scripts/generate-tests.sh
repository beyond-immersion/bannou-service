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
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="NUnit" Version="4.0.1" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.5.0" />
    <PackageReference Include="NUnit.Analyzers" Version="3.9.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeInTargetFramework>false</IncludeInTargetFramework>
    </PackageReference>
    <PackageReference Include="coverlet.collector" Version="6.0.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeInTargetFramework>false</IncludeInTargetFramework>
    </PackageReference>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="9.0.0" />
    <PackageReference Include="Moq" Version="4.20.70" />
    <PackageReference Include="Dapr.Client" Version="1.14.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../lib-$SERVICE_NAME/lib-$SERVICE_NAME.csproj" />
    <ProjectReference Include="../bannou-service/bannou-service.csproj" />
  </ItemGroup>

</Project>
EOF

    echo -e "${GREEN}✅ Created test project file: $TEST_PROJECT_FILE${NC}"

    # Add project to solution
    echo -e "${YELLOW}🔗 Adding test project to solution...${NC}"
    cd "$(dirname "$0")/.."

    if dotnet sln add "$TEST_PROJECT_FILE" --verbosity quiet 2>/dev/null; then
        echo -e "${GREEN}✅ Added test project to solution${NC}"
    else
        echo -e "${YELLOW}⚠️  Test project might already be in solution${NC}"
    fi

else
    echo -e "${YELLOW}📝 Test project file already exists: $TEST_PROJECT_FILE${NC}"

    # Still try to add to solution in case it's not there
    cd "$(dirname "$0")/.."
    if dotnet sln add "$TEST_PROJECT_FILE" --verbosity quiet 2>/dev/null; then
        echo -e "${GREEN}✅ Added existing test project to solution${NC}"
    fi
fi

# Create GlobalUsings.cs if it doesn't exist
GLOBAL_USINGS_FILE="$TEST_PROJECT_DIR/GlobalUsings.cs"
if [ ! -f "$GLOBAL_USINGS_FILE" ]; then
    echo -e "${YELLOW}📝 Creating GlobalUsings.cs...${NC}"
    cat > "$GLOBAL_USINGS_FILE" << 'EOF'
global using NUnit.Framework;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.DependencyInjection;
global using Moq;
global using System;
global using System.Threading.Tasks;
EOF
    echo -e "${GREEN}✅ Created GlobalUsings.cs${NC}"
fi

# Create basic service tests if they don't exist
SERVICE_TESTS_FILE="$TEST_PROJECT_DIR/${SERVICE_PASCAL}ServiceTests.cs"
if [ ! -f "$SERVICE_TESTS_FILE" ]; then
    echo -e "${YELLOW}📝 Creating service test class...${NC}"
    cat > "$SERVICE_TESTS_FILE" << EOF
using BeyondImmersion.BannouService.$SERVICE_PASCAL;

namespace BeyondImmersion.BannouService.$SERVICE_PASCAL.Tests;

[TestFixture]
public class ${SERVICE_PASCAL}ServiceTests
{
    private Mock<ILogger<${SERVICE_PASCAL}Service>> _mockLogger = null!;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = new Mock<ILogger<${SERVICE_PASCAL}Service>>();
    }

    [Test]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        var service = new ${SERVICE_PASCAL}Service(_mockLogger.Object);

        // Assert
        Assert.That(service, Is.Not.Null);
    }

    [Test]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ${SERVICE_PASCAL}Service(null!));
    }

    // TODO: Add service-specific tests based on schema operations
    // Schema file: $SCHEMA_FILE
}

[TestFixture]
public class ${SERVICE_PASCAL}ConfigurationTests
{
    [Test]
    public void Configuration_WithValidSettings_ShouldInitializeCorrectly()
    {
        // Arrange
        var config = new ${SERVICE_PASCAL}ServiceConfiguration();

        // Act & Assert
        Assert.That(config, Is.Not.Null);
    }

    // TODO: Add configuration-specific tests
}
EOF
    echo -e "${GREEN}✅ Created service test class${NC}"
fi

echo -e "${GREEN}✅ Unit test project generation complete${NC}"
exit 0