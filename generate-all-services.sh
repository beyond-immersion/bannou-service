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

# Helper function to convert hyphenated names to PascalCase  
to_pascal_case() {
    local input="$1"
    # Split by hyphens and capitalize each part
    echo "$input" | sed 's/-/ /g' | awk '{for(i=1;i<=NF;i++) $i=toupper(substr($i,1,1)) tolower(substr($i,2))} 1' | sed 's/ //g'
}

# Set working directory to bannou-service
cd "$(dirname "$0")/bannou-service"

# Function to find NSwag executable
find_nswag_exe() {
    # Try multiple possible locations for NSwag executable
    local possible_paths=(
        "$HOME/.nuget/packages/nswag.msbuild/14.2.0/tools/Net90/dotnet-nswag.exe"
        "$HOME/.nuget/packages/nswag.msbuild/14.1.0/tools/Net90/dotnet-nswag.exe" 
        "$HOME/.nuget/packages/nswag.msbuild/14.0.7/tools/Net90/dotnet-nswag.exe"
        "$(find $HOME/.nuget/packages/nswag.msbuild -name "dotnet-nswag.exe" 2>/dev/null | head -1)"
        # Try global tool installation
        "$(which nswag 2>/dev/null)"
        "$(command -v nswag 2>/dev/null)"
    )
    
    for path in "${possible_paths[@]}"; do
        if [ -n "$path" ] && [ -f "$path" ]; then
            echo "$path"
            return 0
        fi
    done
    
    return 1
}

# NSwag executable path
NSWAG_EXE=$(find_nswag_exe)

# Verify NSwag executable exists, if not try alternative approach
if [ -z "$NSWAG_EXE" ]; then
    echo -e "${YELLOW}⚠️  NSwag executable not found in standard locations, trying dotnet build approach...${NC}"
    echo "  📦 Restoring NuGet packages first..."
    
    # Try restoring packages first
    if dotnet restore --verbosity quiet; then
        echo -e "${GREEN}    ✅ NuGet packages restored${NC}"
        
        # Try finding NSwag again after restore
        NSWAG_EXE=$(find_nswag_exe)
        
        if [ -z "$NSWAG_EXE" ]; then
            echo -e "${RED}❌ NSwag executable still not found after package restore${NC}"
            echo "Available NSwag packages:"
            find $HOME/.nuget/packages -name "*nswag*" -type d 2>/dev/null | head -5
            exit 1
        else
            echo -e "${GREEN}    ✅ Found NSwag at: $NSWAG_EXE${NC}"
        fi
    else
        echo -e "${RED}❌ Failed to restore NuGet packages${NC}"
        exit 1
    fi
else
    echo -e "${GREEN}✅ Found NSwag at: $NSWAG_EXE${NC}"
fi

echo -e "${YELLOW}📋 Generating NSwag controllers from schemas...${NC}"

# Function to create service plugin project if it doesn't exist
create_service_plugin() {
    local service_name="$1"
    local service_plugin_dir="../lib-${service_name}"
    local project_file="$service_plugin_dir/lib-${service_name}.csproj"

    if [ ! -d "$service_plugin_dir" ]; then
        echo "  📁 Creating service plugin directory: $service_plugin_dir"
        mkdir -p "$service_plugin_dir"
    fi

    if [ ! -f "$project_file" ]; then
        echo "  📝 Creating service plugin project: $project_file"

        # Generate consolidated service plugin project file
        cat > "$project_file" << EOF
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>BeyondImmersion.BannouService.$(to_pascal_case "$service_name")</RootNamespace>
    <ServiceLib>${service_name}</ServiceLib>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../bannou-service/bannou-service.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Core" Version="2.2.5" />
    <PackageReference Include="System.ComponentModel.Annotations" Version="5.0.0" />
  </ItemGroup>

  <Import Project="../ServiceLib.targets" />

</Project>
EOF
        echo -e "${GREEN}    ✅ Created service plugin project${NC}"
        
        # Automatically add new project to solution
        echo "  🔗 Adding project to solution..."
        if dotnet sln add "$project_file" --verbosity quiet 2>/dev/null; then
            echo -e "${GREEN}    ✅ Added to solution${NC}"
        else
            echo -e "${YELLOW}    ⚠️  Project might already be in solution${NC}"
        fi
    fi
}

# Function to generate service interface from schema
generate_service_interface() {
    local schema_file="$1"
    local service_name="$2"
    local service_plugin_dir="$3"
    local service_pascal=$(to_pascal_case "$service_name")
    local interface_name="I${service_pascal}Service.cs"
    local output_path="$service_plugin_dir/Generated/$interface_name"
    local controller_file="$service_plugin_dir/Generated/${service_pascal}Controller.Generated.cs"

    echo "    🔄 Generating service interface $interface_name..."

    if [ ! -f "$schema_file" ]; then
        echo -e "${RED}    ⚠️  Schema file not found: $schema_file${NC}"
        return 1
    fi

    # Check if controller exists to extract interface from
    if [ ! -f "$controller_file" ]; then
        echo -e "${RED}    ⚠️  Controller file not found: $controller_file${NC}"
        return 1
    fi

    mkdir -p "$(dirname "$output_path")"

    # Create service interface by extracting method signatures from generated controller
    cat > "$output_path" << EOF
using BeyondImmersion.BannouService;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.$service_pascal;

/// <summary>
/// Service interface for $service_pascal API - generated from controller
/// </summary>
public interface I${service_pascal}Service
{
EOF

    # Extract full method signatures from controller and convert to service interface methods
    # Parse controller methods to extract complete signatures including parameters
    python3 -c "
import re
import sys

with open('$controller_file', 'r') as f:
    content = f.read()

# Find all abstract methods with multiline handling
method_pattern = r'public abstract System\.Threading\.Tasks\.Task<Microsoft\.AspNetCore\.Mvc\.ActionResult<([^>]+)>>\s+(\w+)\(([^;]+)\);'

matches = re.findall(method_pattern, content, re.MULTILINE | re.DOTALL)

for return_type, method_name, params in matches:
    # Convert controller parameters to service parameters
    # Remove ASP.NET Core specific attributes and system types
    clean_params = re.sub(r'\[Microsoft\.AspNetCore\.Mvc\.[^\]]+\]\s*', '', params)
    clean_params = re.sub(r'\[Microsoft\.AspNetCore\.Mvc\.ModelBinding\.[^\]]+\]\s*', '', clean_params)
    clean_params = re.sub(r'System\.Threading\.', '', clean_params)
    clean_params = re.sub(r'System\.', '', clean_params)

    # Clean up extra whitespace and newlines
    clean_params = re.sub(r'\s+', ' ', clean_params).strip()

    # Handle specific type conversions - fix any remaining issues
    clean_params = clean_params.replace('Provider?', 'Provider?')
    clean_params = clean_params.replace('Provider2', 'Provider2')

    print(f'''        /// <summary>
        /// {method_name} operation
        /// </summary>
        Task<(StatusCodes, {return_type}?)> {method_name}Async({clean_params});
''')
" >> "$output_path"

    # Close the interface
    cat >> "$output_path" << EOF
}
EOF

    local file_size=$(wc -l < "$output_path" 2>/dev/null || echo "0")
    echo -e "${GREEN}    ✅ Generated service interface ($file_size lines)${NC}"
    return 0
}

# Function to generate service implementation from schema
generate_service_implementation() {
    local schema_file="$1"
    local service_name="$2"
    local service_plugin_dir="$3"
    local service_pascal=$(to_pascal_case "$service_name")
    local service_name_pascal="${service_pascal}Service.cs"
    local output_path="$service_plugin_dir/$service_name_pascal"

    echo "    🔄 Generating service implementation $service_name_pascal..."

    if [ ! -f "$schema_file" ]; then
        echo -e "${RED}    ⚠️  Schema file not found: $schema_file${NC}"
        return 1
    fi

    # Check if service implementation already exists - protect existing logic
    if [ -f "$output_path" ]; then
        echo -e "${YELLOW}    📝 Service implementation already exists, preserving existing logic${NC}"
        return 0
    fi

    mkdir -p "$(dirname "$output_path")"

    # Create service implementation with actual method stubs (including NotImplementedException)
    cat > "$output_path" << EOF
using BeyondImmersion.BannouService;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.$service_pascal;

/// <summary>
/// Generated service implementation for $service_pascal API
/// </summary>
public class ${service_pascal}Service : I${service_pascal}Service
{
    private readonly ILogger<${service_pascal}Service> _logger;
    private readonly ${service_pascal}ServiceConfiguration _configuration;

    public ${service_pascal}Service(
        ILogger<${service_pascal}Service> logger,
        ${service_pascal}ServiceConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }
EOF

    # Generate NotImplementedException method stubs from interface
    local interface_file="$service_plugin_dir/Generated/I${service_pascal}Service.cs"
    if [ -f "$interface_file" ]; then
        # Extract method signatures from interface and generate stub implementations
        python3 -c "
import re
import sys

with open('$interface_file', 'r') as f:
    content = f.read()

# Find all method declarations in the interface
method_pattern = r'Task<\(StatusCodes, ([^)]+)\)>\s+(\w+)\(([^;]*)\);'

matches = re.findall(method_pattern, content, re.MULTILINE | re.DOTALL)

for return_type, method_name, params in matches:
    # Clean up parameters for implementation
    clean_params = re.sub(r'\s+', ' ', params).strip()
    
    print(f'''
    /// <summary>
    /// {method_name} implementation - TODO: Add business logic
    /// </summary>
    public async Task<(StatusCodes, {return_type})> {method_name}({clean_params})
    {{
        _logger.LogWarning(\"Method {method_name} called but not implemented\");
        await Task.Delay(1); // Avoid async warning
        throw new NotImplementedException(\"Method {method_name} is not implemented\");
    }}''')
" >> "$output_path"
    else
        echo "    // Interface file not found, no method stubs generated" >> "$output_path"
    fi

    # Close the class
    cat >> "$output_path" << EOF
}
EOF

    local file_size=$(wc -l < "$output_path" 2>/dev/null || echo "0")
    echo -e "${GREEN}    ✅ Generated service implementation template ($file_size lines)${NC}"
    return 0
}

# Function to generate service configuration from schema
generate_service_configuration() {
    local schema_file="$1"
    local service_name="$2"
    local service_plugin_dir="$3"
    local service_pascal=$(to_pascal_case "$service_name")
    local config_name="${service_pascal}ServiceConfiguration.cs"
    local output_path="$service_plugin_dir/Generated/$config_name"

    echo "    🔄 Generating service configuration $config_name..."

    if [ ! -f "$schema_file" ]; then
        echo -e "${RED}    ⚠️  Schema file not found: $schema_file${NC}"
        return 1
    fi

    # Always regenerate service configuration from schema
    # Configuration should be schema-driven, not preserved from manual changes

    mkdir -p "$(dirname "$output_path")"

    # Create configuration class template
    cat > "$output_path" << EOF
using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.$service_pascal;

/// <summary>
/// Generated configuration for $service_pascal service
/// </summary>
[ServiceConfiguration(typeof(${service_pascal}Service), envPrefix: "$(echo "$service_name" | tr '[:lower:]' '[:upper:]' | tr '-' '_')_")]
public class ${service_pascal}ServiceConfiguration : IServiceConfiguration
{
    /// <summary>
    /// Force specific service ID (optional)
    /// </summary>
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Disable this service (optional)
    /// </summary>
    public bool? Service_Disabled { get; set; }

    // TODO: Add service-specific configuration properties from schema
    // Example properties:
    // [Required]
    // public string ConnectionString { get; set; } = string.Empty;
    //
    // public int MaxRetries { get; set; } = 3;
    //
    // public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
EOF

    local file_size=$(wc -l < "$output_path" 2>/dev/null || echo "0")
    echo -e "${GREEN}    ✅ Generated service configuration template ($file_size lines)${NC}"
    return 0
}

# Function to generate service client from schema
generate_client() {
    local schema_file="$1"
    local service_name="$2"
    local service_plugin_dir="$3"
    local service_pascal=$(to_pascal_case "$service_name")
    local client_name="${service_pascal}Client.cs"
    local output_path="$service_plugin_dir/Generated/$client_name"

    echo "    🔄 Generating service client $client_name..."

    if [ ! -f "$schema_file" ]; then
        echo -e "${RED}    ⚠️  Schema file not found: $schema_file${NC}"
        return 1
    fi

    mkdir -p "$(dirname "$output_path")"

    # Generate service client using appropriate method
    if [ "$USE_DOTNET_BUILD" = "true" ]; then
        echo "    🔧 Skipping direct client generation (using dotnet build approach)"
        echo -e "${YELLOW}    ⚠️  Client generation handled by dotnet build${NC}"
        return 0
    else
        # Generate service client using direct NSwag command (DaprServiceClientBase pattern)
        "$NSWAG_EXE" openapi2csclient \
            /input:"$schema_file" \
            /output:"$output_path" \
            /namespace:"BeyondImmersion.BannouService.$service_pascal.Client" \
            /clientBaseClass:"BeyondImmersion.BannouService.ServiceClients.DaprServiceClientBase" \
            /className:"${service_pascal}Client" \
            /generateClientClasses:true \
            /generateClientInterfaces:true \
            /injectHttpClient:true \
            /disposeHttpClient:false \
            /jsonLibrary:NewtonsoftJson \
            /generateNullableReferenceTypes:true \
            /newLineBehavior:LF \
            /generateOptionalParameters:true \
            /useHttpClientCreationMethod:false \
            /additionalNamespaceUsages:"BeyondImmersion.BannouService.ServiceClients" \
            /templateDirectory:"../templates/nswag"

        # Check if NSwag client generation succeeded
        if [ $? -eq 0 ]; then
            if [ -f "$output_path" ]; then
                local file_size=$(wc -l < "$output_path" 2>/dev/null || echo "0")
                echo -e "${GREEN}    ✅ Generated service client $client_name ($file_size lines)${NC}"
            else
                echo -e "${YELLOW}    ⚠️  No client output file created (no change detected)${NC}"
            fi
            return 0
        else
            echo -e "${RED}    ❌ Failed to generate service client $client_name${NC}"
            return 1
        fi
    fi
}

# Function to generate controller from schema (consolidated service architecture)
generate_controller() {
    local schema_file="$1"
    local service_name="$2"
    local service_pascal=$(to_pascal_case "$service_name")
    local controller_name="${service_pascal}Controller.Generated.cs"

    # Consolidated architecture: generate in service plugin directory
    local service_plugin_dir="../lib-${service_name}"
    local output_path="$service_plugin_dir/Generated/$controller_name"

    echo "  🔄 Generating $controller_name from $schema_file..."
    echo "      📁 Output: $output_path"

    if [ ! -f "$schema_file" ]; then
        echo -e "${RED}    ⚠️  Schema file not found: $schema_file${NC}"
        return 1
    fi

    # Ensure service plugin exists (create if needed)
    create_service_plugin "$service_name"

    # Create Generated directory if it doesn't exist
    mkdir -p "$(dirname "$output_path")"

    # Generate controller using appropriate method
    local nswag_success=false
    
    if [ "$USE_DOTNET_BUILD" = "true" ]; then
        # Use dotnet build approach - trigger NSwag via MSBuild
        echo "      🔧 Using dotnet build approach for NSwag generation..."
        if dotnet build -p:GenerateNewServices=true --verbosity quiet; then
            nswag_success=true
            echo "      ✅ Generated via dotnet build"
        else
            echo "      ❌ Failed via dotnet build"
        fi
    else
        # Use direct NSwag command (pure shell pattern)
        "$NSWAG_EXE" openapi2cscontroller \
            "/input:$schema_file" \
            "/output:$output_path" \
            "/namespace:BeyondImmersion.BannouService.$service_pascal" \
            "/ControllerStyle:Abstract" \
            "/ControllerBaseClass:Microsoft.AspNetCore.Mvc.ControllerBase" \
            "/ClassName:${service_pascal}ControllerBase" \
            "/UseCancellationToken:true" \
            "/UseActionResultType:true" \
            "/GenerateModelValidationAttributes:true" \
            "/GenerateDataAnnotations:true" \
            "/JsonLibrary:NewtonsoftJson" \
            "/GenerateNullableReferenceTypes:true" \
            "/NewLineBehavior:LF" \
            "/GenerateOptionalParameters:true" \
            "/TemplateDirectory:../templates/nswag"
        
        if [ $? -eq 0 ]; then
            nswag_success=true
        fi
    fi

    # Generate service client from schema (CRITICAL for service-to-service communication)
    generate_client "$schema_file" "$service_name" "$service_plugin_dir"

    # Generate service interface from schema
    generate_service_interface "$schema_file" "$service_name" "$service_plugin_dir"

    # Generate service implementation from schema
    generate_service_implementation "$schema_file" "$service_name" "$service_plugin_dir"

    # Generate configuration class from schema
    generate_service_configuration "$schema_file" "$service_name" "$service_plugin_dir"

    # Check if NSwag generation succeeded
    if [ "$nswag_success" = "true" ]; then
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

# Alternative approach: use dotnet build if NSwag executable fails
USE_DOTNET_BUILD=false

# Check if we should fall back to dotnet build approach
if [ -z "$NSWAG_EXE" ] || [ ! -x "$NSWAG_EXE" ]; then
    echo -e "${YELLOW}⚠️  Falling back to dotnet build approach for NSwag generation${NC}"
    USE_DOTNET_BUILD=true
fi

# Generate controllers for available schemas (support single service argument)
generated_count=0
failed_count=0

# Dynamically discover all available schemas
all_schemas=()
while IFS= read -r -d '' schema_file; do
    if [[ "$schema_file" == *"-api.yaml" ]]; then
        # Extract service name from filename (remove path and -api.yaml suffix)
        service_name=$(basename "$schema_file" "-api.yaml")
        all_schemas+=("$schema_file:$service_name")
    fi
done < <(find ../schemas -name "*-api.yaml" -print0 2>/dev/null)

# Check if specific service was requested
if [ "$1" ]; then
    requested_service="$1"
    schemas=()
    found=false

    for schema_entry in "${all_schemas[@]}"; do
        IFS=':' read -r schema_file service_name <<< "$schema_entry"
        if [ "$service_name" = "$requested_service" ]; then
            schemas=("$schema_entry")
            found=true
            echo "🎯 Generating only for requested service: $requested_service"
            break
        fi
    done

    if [ "$found" = false ]; then
        # Dynamically build available services list
        available_services=""
        for schema_entry in "${all_schemas[@]}"; do
            IFS=':' read -r schema_file service_name <<< "$schema_entry"
            available_services+="$service_name, "
        done
        available_services=${available_services%, }  # Remove trailing comma and space
        echo -e "${RED}❌ Service '$requested_service' not found. Available services: $available_services${NC}"
        exit 1
    fi
else
    # Generate all schemas if no specific service requested
    schemas=("${all_schemas[@]}")
fi

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

# Generate unit test projects using dotnet new custom template
echo -e "${YELLOW}📋 Generating unit test projects...${NC}"

generate_unit_test_projects() {
    local test_generated=0
    local test_failed=0

    for schema_entry in "${schemas[@]}"; do
        IFS=':' read -r schema_file service_name <<< "$schema_entry"
        local service_name_pascal=$(to_pascal_case "$service_name")
        local test_project_dir="../lib-${service_name}.tests"

        # Skip if test project already exists
        if [ -d "$test_project_dir" ]; then
            echo "    📝 Unit test project already exists: $test_project_dir"
            continue
        fi

        echo "    🧪 Creating unit test project for $service_name..."

        # Create test project directory
        mkdir -p "$test_project_dir"

        # Use dotnet new with our custom template (fallback to manual creation if template fails)
        if dotnet new bannou-test -n "lib-${service_name}.tests" -o "$test_project_dir" --ServiceName "$service_name" --force >/dev/null 2>&1; then

            # Fix the generated files (template has some quirks)
            if [ -f "$test_project_dir/lib-${service_name}.tests.Tests.csproj" ]; then
                mv "$test_project_dir/lib-${service_name}.tests.Tests.csproj" "$test_project_dir/lib-${service_name}.tests.csproj"
            fi

            # Create proper test class file
            cat > "$test_project_dir/${service_name_pascal}ServiceTests.cs" << EOF
using BeyondImmersion.BannouService.${service_name_pascal};
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.${service_name_pascal}.Tests;

/// <summary>
/// Unit tests for ${service_name_pascal}Service
/// This test project can reference other service clients for integration testing.
/// </summary>
public class ${service_name_pascal}ServiceTests
{
    private readonly Mock<ILogger<${service_name_pascal}Service>> _mockLogger;
    private readonly Mock<${service_name_pascal}ServiceConfiguration> _mockConfiguration;

    public ${service_name_pascal}ServiceTests()
    {
        _mockLogger = new Mock<ILogger<${service_name_pascal}Service>>();
        _mockConfiguration = new Mock<${service_name_pascal}ServiceConfiguration>();
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        var exception = Record.Exception(() => new ${service_name_pascal}Service(
            _mockConfiguration.Object,
            _mockLogger.Object));

        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ${service_name_pascal}Service(
            null!,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ${service_name_pascal}Service(
            _mockConfiguration.Object,
            null!));
    }

    // TODO: Add service-specific unit tests here
    // For service-to-service communication tests, add references to other service client projects
    // Example: Add reference to lib-accounts project to test AuthService → AccountsClient integration
}
EOF

            echo -e "${GREEN}        ✅ Generated unit test project for $service_name${NC}"
            
            # Automatically add test project to solution
            if dotnet sln add "$test_project_dir/lib-$service_name.tests.csproj" --verbosity quiet 2>/dev/null; then
                echo -e "${GREEN}        ✅ Added test project to solution${NC}"
            fi
            ((test_generated++))
        else
            echo -e "${RED}        ❌ Failed to generate unit test project for $service_name${NC}"
            ((test_failed++))
        fi
    done

    echo "    📊 Unit test generation: $test_generated created, $test_failed failed"
    if [ $test_failed -eq 0 ]; then
        echo -e "${GREEN}    ✅ All unit test projects generated successfully${NC}"
        ((generated_count++))
        return 0
    else
        echo -e "${RED}    ❌ Some unit test projects failed to generate${NC}"
        ((failed_count++))
        return 1
    fi
}

generate_unit_test_projects

# Fix line endings for all generated files
echo -e "${YELLOW}📋 Fixing line endings for EditorConfig compliance...${NC}"

if [ -f "../fix-endings.sh" ]; then
    chmod +x "../fix-endings.sh"
    if "../fix-endings.sh"; then
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
