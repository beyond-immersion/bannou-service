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
    <RootNamespace>BeyondImmersion.BannouService.${service_name^}</RootNamespace>
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
    fi
}

# Function to generate service interface from schema
generate_service_interface() {
    local schema_file="$1"
    local service_name="$2"
    local service_plugin_dir="$3"
    local interface_name="I${service_name^}Service.cs"
    local output_path="$service_plugin_dir/Generated/$interface_name"
    local controller_file="$service_plugin_dir/Generated/${service_name^}Controller.Generated.cs"
    
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

namespace BeyondImmersion.BannouService.${service_name^}
{
    /// <summary>
    /// Service interface for ${service_name^} API - generated from controller
    /// </summary>
    public interface I${service_name^}Service
    {
EOF

    # Extract abstract method signatures from controller and convert to service interface methods
    # Use a simpler approach with grep and sed to extract methods
    grep -A 1 "public abstract.*Task<.*ActionResult<.*>" "$controller_file" | \
    sed -n 's/.*public abstract.*Task<.*ActionResult<\([^>]*\)>>\s*\([A-Za-z]*\)(.*/\2:\1/p' | \
    while IFS=: read -r method_name return_type; do
        if [ ! -z "$method_name" ] && [ ! -z "$return_type" ]; then
            cat >> "$output_path" << EOF
        /// <summary>
        /// ${method_name} operation  
        /// </summary>
        Task<(StatusCodes, ${return_type}?)> ${method_name}Async(/* TODO: Add parameters from schema */);

EOF
        fi
    done
    
    # Close the interface
    cat >> "$output_path" << EOF
    }
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
    local service_name_pascal="${service_name^}Service.cs"
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
    
    # Create service implementation template that returns tuples
    cat > "$output_path" << EOF
using BeyondImmersion.BannouService;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.${service_name^}
{
    /// <summary>
    /// Generated service implementation for ${service_name^} API
    /// </summary>
    public class ${service_name^}Service : I${service_name^}Service
    {
        private readonly ILogger<${service_name^}Service> _logger;
        private readonly ${service_name^}ServiceConfiguration _configuration;

        public ${service_name^}Service(
            ILogger<${service_name^}Service> logger,
            ${service_name^}ServiceConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        // TODO: Implement service methods that return (StatusCodes, ResponseModel?) tuples
        // Example method signature:
        // public async Task<(StatusCodes, CreateResponseModel?)> CreateAsync(
        //     CreateRequestModel request, CancellationToken cancellationToken = default)
        // {
        //     try 
        //     {
        //         // Business logic implementation here
        //         _logger.LogDebug("Processing create request");
        //         
        //         // Return success with response model
        //         return (StatusCodes.OK, new CreateResponseModel { /* ... */ });
        //     }
        //     catch (Exception ex)
        //     {
        //         _logger.LogError(ex, "Error processing create request");
        //         return (StatusCodes.InternalServerError, null);
        //     }
        // }
    }
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
    local config_name="${service_name^}ServiceConfiguration.cs"
    local output_path="$service_plugin_dir/$config_name"
    
    echo "    🔄 Generating service configuration $config_name..."
    
    if [ ! -f "$schema_file" ]; then
        echo -e "${RED}    ⚠️  Schema file not found: $schema_file${NC}"
        return 1
    fi
    
    # Check if service configuration already exists - protect existing logic
    if [ -f "$output_path" ]; then
        echo -e "${YELLOW}    📝 Service configuration already exists, preserving existing logic${NC}"
        return 0
    fi
    
    mkdir -p "$(dirname "$output_path")"
    
    # Create configuration class template
    cat > "$output_path" << EOF
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.${service_name^}
{
    /// <summary>
    /// Generated configuration for ${service_name^} service
    /// </summary>
    [ServiceConfiguration(typeof(${service_name^}Service), envPrefix: "${service_name^^}_")]
    public class ${service_name^}ServiceConfiguration : IServiceConfiguration
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
}
EOF

    local file_size=$(wc -l < "$output_path" 2>/dev/null || echo "0")
    echo -e "${GREEN}    ✅ Generated service configuration template ($file_size lines)${NC}"
    return 0
}

# Function to generate controller from schema (consolidated service architecture)
generate_controller() {
    local schema_file="$1"
    local service_name="$2"
    local controller_name="${service_name^}Controller.Generated.cs"  # Capitalize first letter
    
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
    
    # Generate controller using direct NSwag command (pure shell pattern)
    "$NSWAG_EXE" openapi2cscontroller \
        "/input:$schema_file" \
        "/output:$output_path" \
        "/namespace:BeyondImmersion.BannouService.${service_name^}" \
        "/ControllerStyle:Abstract" \
        "/ControllerBaseClass:Microsoft.AspNetCore.Mvc.ControllerBase" \
        "/ClassName:${service_name^}ControllerBase" \
        "/UseCancellationToken:true" \
        "/UseActionResultType:true" \
        "/GenerateModelValidationAttributes:true" \
        "/GenerateDataAnnotations:true" \
        "/JsonLibrary:NewtonsoftJson" \
        "/GenerateNullableReferenceTypes:true" \
        "/NewLineBehavior:LF" \
        "/GenerateOptionalParameters:true"
        
    # Generate service interface from schema
    generate_service_interface "$schema_file" "$service_name" "$service_plugin_dir"
    
    # Generate service implementation from schema  
    generate_service_implementation "$schema_file" "$service_name" "$service_plugin_dir"
    
    # Generate configuration class from schema
    generate_service_configuration "$schema_file" "$service_name" "$service_plugin_dir"
        
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
    "../schemas/behavior-api.yaml:behavior"
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