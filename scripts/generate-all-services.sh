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
cd "$(dirname "$0")/../bannou-service"

# Function to find NSwag executable
find_nswag_exe() {
    # Try multiple possible locations for NSwag executable (prioritize 14.2.0 to avoid CS1737 bug)
    local possible_paths=(
        "$HOME/.nuget/packages/nswag.msbuild/14.2.0/tools/Net90/dotnet-nswag.exe"
        "$HOME/.nuget/packages/nswag.msbuild/14.1.0/tools/Net90/dotnet-nswag.exe"
        "$HOME/.nuget/packages/nswag.msbuild/14.0.7/tools/Net90/dotnet-nswag.exe"
        "$HOME/.nuget/packages/nswag.msbuild/14.5.0/tools/Net90/dotnet-nswag.exe"
        "$HOME/.nuget/packages/nswag.msbuild/14.4.0/tools/Net90/dotnet-nswag.exe"
        "$HOME/.nuget/packages/nswag.msbuild/14.3.0/tools/Net90/dotnet-nswag.exe"
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
    local controller_file="$service_plugin_dir/Generated/${service_pascal}Controller.cs"

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

    # Extract full method signatures from I{Service}Controller interface and convert to service interface methods
    # Parse I{Service}Controller interface methods to extract complete signatures including parameters
    # EXCLUDE methods marked with x-controller-only: true in schema and extract parameter defaults
    python3 -c "
import re
import sys
import yaml

# Get service name from command line args
service_pascal = '$service_pascal'

# Load schema file to check for x-controller-only flags and parameter defaults
controller_only_methods = set()
method_parameter_defaults = {}
try:
    with open('$schema_file', 'r') as schema_f:
        schema = yaml.safe_load(schema_f)
        if 'paths' in schema:
            for path, path_data in schema['paths'].items():
                for method, method_data in path_data.items():
                    if isinstance(method_data, dict):
                        operation_id = method_data.get('operationId', '')
                        if operation_id:
                            # Convert operationId to method name (camelCase to PascalCase)
                            method_name = operation_id[0].upper() + operation_id[1:] if operation_id else ''

                            # Check for controller-only methods
                            if method_data.get('x-controller-only') is True:
                                controller_only_methods.add(method_name)
                                print(f'    # Excluding controller-only method: {method_name}', file=sys.stderr)

                            # Extract parameter defaults
                            if 'parameters' in method_data:
                                param_defaults = {}
                                for param in method_data['parameters']:
                                    param_name = param.get('name', '')
                                    if 'schema' in param and 'default' in param['schema']:
                                        default_value = param['schema']['default']
                                        # Convert Python boolean values to C# equivalents
                                        if isinstance(default_value, bool):
                                            default_value = 'true' if default_value else 'false'
                                        elif isinstance(default_value, str):
                                            # Ensure string defaults are quoted
                                            default_value = f'\"{default_value}\"'
                                        param_defaults[param_name] = default_value
                                if param_defaults:
                                    method_parameter_defaults[method_name] = param_defaults
except Exception as e:
    print(f'    # Warning: Could not parse schema for x-controller-only flags and defaults: {e}', file=sys.stderr)

with open('$controller_file', 'r') as f:
    content = f.read()

# Extract I{Service}Controller interface and find all its methods
interface_pattern = fr'public interface I{service_pascal}Controller\\s*\\{{([^}}]*)}}'
interface_match = re.search(interface_pattern, content, re.MULTILINE | re.DOTALL)

if interface_match:
    interface_body = interface_match.group(1)

    # Find all method declarations in the interface (handle multiline signatures and nested generics)
    # Use a different approach: match everything between ActionResult< and the first >;
    method_pattern = r'System\.Threading\.Tasks\.Task<Microsoft\.AspNetCore\.Mvc\.ActionResult<(.+?)>>\s+(\w+)Async\(([^;]*)\);|System\.Threading\.Tasks\.Task<Microsoft\.AspNetCore\.Mvc\.IActionResult>\s+(\w+)Async\(([^;]*)\);'

    matches = re.findall(method_pattern, interface_body, re.MULTILINE | re.DOTALL)

    for match in matches:
        # Handle different match groups - ActionResult vs IActionResult
        if match[0]:  # ActionResult pattern matched
            return_type, method_name, params = match[0], match[1], match[2]
        else:  # IActionResult pattern matched
            return_type, method_name, params = 'object', match[3], match[4]
        # Skip methods marked as controller-only in schema
        if method_name in controller_only_methods:
            print(f'    # Skipping controller-only method: {method_name}', file=sys.stderr)
            continue

        # Handle IActionResult methods (return_type will be empty)
        if not return_type:
            return_type = 'object'  # Generic return type for IActionResult

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

        # Apply default values from schema
        if method_name in method_parameter_defaults:
            param_defaults = method_parameter_defaults[method_name]
            # Split parameters and apply defaults where applicable
            param_parts = []
            for param in clean_params.split(','):
                param = param.strip()
                if param:
                    # Extract parameter name (handle complex types)
                    param_match = re.search(r'(\w+)\s*(?:=|$)', param)
                    if param_match:
                        param_name = param_match.group(1)
                        if param_name in param_defaults:
                            default_val = param_defaults[param_name]
                            # Remove existing default and apply schema default
                            base_param = re.sub(r'\s*=\s*[^,]*', '', param)
                            param = f'{base_param} = {default_val}'
                        elif param_name != 'cancellationToken' and '=' not in param:
                            # Add null default for optional parameters without defaults
                            if param.endswith('?') or 'string?' in param or 'bool?' in param or 'Provider?' in param:
                                param = f'{param} = null'
                    param_parts.append(param)
            clean_params = ', '.join(param_parts)

        print(f'''        /// <summary>
        /// {method_name} operation
        /// </summary>
        Task<(StatusCodes, {return_type}?)> {method_name}Async({clean_params});
''')
else:
    print('    # Warning: Could not find I${service_pascal}Controller interface in generated controller file', file=sys.stderr)
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

# Function to generate models only for Client SDK
generate_models_only() {
    local schema_file="$1"
    local service_name="$2"
    local service_plugin_dir="$3"
    local service_pascal=$(to_pascal_case "$service_name")
    local models_name="${service_pascal}Models.cs"
    local output_path="$service_plugin_dir/Generated/$models_name"

    echo "    🔄 Generating models-only file $models_name for Client SDK..."

    if [ ! -f "$schema_file" ]; then
        echo -e "${RED}    ⚠️  Schema file not found: $schema_file${NC}"
        return 1
    fi

    mkdir -p "$(dirname "$output_path")"

    # Generate models only using NSwag (no controller logic)
    "$NSWAG_EXE" openapi2csclient \
        /input:"$schema_file" \
        /output:"$output_path" \
        /namespace:"BeyondImmersion.BannouService.$service_pascal" \
        /generateClientClasses:false \
        /generateClientInterfaces:false \
        /generateDtoTypes:true \
        /excludedTypeNames:ApiException,ApiException\<TResult\> \
        /jsonLibrary:NewtonsoftJson \
        /generateNullableReferenceTypes:true \
        /newLineBehavior:LF \
        /templateDirectory:"../templates/nswag"

    # Check if models generation succeeded
    if [ $? -eq 0 ]; then
        if [ -f "$output_path" ]; then
            local file_size=$(wc -l < "$output_path" 2>/dev/null || echo "0")
            echo -e "${GREEN}    ✅ Generated models-only file $models_name ($file_size lines)${NC}"
        else
            echo -e "${YELLOW}    ⚠️  No models output file created (no change detected)${NC}"
        fi
        return 0
    else
        echo -e "${RED}    ❌ Failed to generate models-only file $models_name${NC}"
        return 1
    fi
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
            /namespace:"BeyondImmersion.BannouService.$service_pascal" \
            /clientBaseClass:"BeyondImmersion.BannouService.ServiceClients.DaprServiceClientBase" \
            /className:"${service_pascal}Client" \
            /generateClientClasses:true \
            /generateClientInterfaces:true \
            /generateDtoTypes:false \
            /excludedTypeNames:ApiException,ApiException\<TResult\> \
            /injectHttpClient:false \
            /disposeHttpClient:true \
            /jsonLibrary:NewtonsoftJson \
            /generateNullableReferenceTypes:true \
            /newLineBehavior:LF \
            /generateOptionalParameters:true \
            /useHttpClientCreationMethod:true \
            /additionalNamespaceUsages:"BeyondImmersion.BannouService,BeyondImmersion.BannouService.ServiceClients,BeyondImmersion.BannouService.$service_pascal" \
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

# Function to post-process generated controller for partial class support
post_process_partial_controller() {
    local controller_file="$1"
    local service_plugin_dir="$2"
    local service_pascal="$3"

    if [ ! -f "$controller_file" ]; then
        echo "      ❌ Controller file not found for post-processing: $controller_file"
        return 1
    fi

    echo "      🔧 Converting generated controller to partial class..."

    # Convert the class declaration to partial
    sed -i 's/public abstract class \([^:]*\)ControllerBase/public abstract partial class \1ControllerBase/' "$controller_file"

    echo "      📝 Creating empty partial controller in parent directory..."
    create_empty_partial_controller "$service_plugin_dir" "$service_pascal"

    echo "      ✅ Partial controller post-processing complete"
    return 0
}

# Function to create empty partial controller implementation template
create_empty_partial_controller() {
    local service_plugin_dir="$1"
    local service_pascal="$2"
    local partial_controller_file="$service_plugin_dir/${service_pascal}Controller.cs"

    # Don't overwrite existing manual implementation
    if [ -f "$partial_controller_file" ]; then
        echo "      📝 Manual partial controller already exists: $partial_controller_file"
        return 0
    fi

    echo "      📝 Creating empty partial controller: $partial_controller_file"

    # Create the empty partial controller implementation
    cat > "$partial_controller_file" << EOF
using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.$service_pascal;

/// <summary>
/// Manual implementation for endpoints that require custom logic.
/// This partial class extends the generated ${service_pascal}ControllerBase.
/// </summary>
public partial class ${service_pascal}Controller : ${service_pascal}ControllerBase
{
    private readonly I${service_pascal}Service _${service_pascal,,}Service;

    public ${service_pascal}Controller(I${service_pascal}Service ${service_pascal,,}Service)
    {
        _${service_pascal,,}Service = ${service_pascal,,}Service;
    }

    // TODO: Implement abstract methods marked with x-manual-implementation: true
    // The generated controller base class contains abstract methods that require manual implementation
}
EOF

    echo "      ✅ Created empty partial controller template"
    return 0
}

# Function to generate controller from schema (consolidated service architecture)
generate_controller() {
    local schema_file="$1"
    local service_name="$2"
    local service_pascal=$(to_pascal_case "$service_name")
    local controller_name="${service_pascal}Controller.cs"

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

    # Check for x-manual-implementation flag in schema
    local has_manual_implementation=false
    if grep -q "x-manual-implementation:\s*true" "$schema_file"; then
        has_manual_implementation=true
        echo "      🔧 Schema contains x-manual-implementation: true - generating partial controller"
    fi

    # Check for controller-only and service-only flags for selective generation
    local controller_only_methods=()
    local service_only_methods=()

    # Extract methods marked with x-controller-only: true
    if grep -q "x-controller-only:\s*true" "$schema_file"; then
        echo "      🔧 Schema contains x-controller-only methods - selective generation enabled"
        # Automatically enable manual implementation for controller-only methods
        has_manual_implementation=true
        echo "      🔧 Enabling manual implementation due to x-controller-only methods"
    fi

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
            "/ControllerBaseClass:Microsoft.AspNetCore.Mvc.ControllerBase" \
            "/ClassName:${service_pascal}" \
            "/UseCancellationToken:true" \
            "/UseActionResultType:true" \
            "/GenerateModelValidationAttributes:true" \
            "/GenerateDataAnnotations:true" \
            "/GenerateDtoTypes:false" \
            "/JsonLibrary:NewtonsoftJson" \
            "/GenerateNullableReferenceTypes:true" \
            "/NewLineBehavior:LF" \
            "/GenerateOptionalParameters:false" \
            "/TemplateDirectory:../templates/nswag"

        if [ $? -eq 0 ]; then
            nswag_success=true
        fi
    fi

    # Post-process controller for partial class support if manual implementation is needed
    if [ "$nswag_success" = true ] && [ "$has_manual_implementation" = true ]; then
        echo "      🔧 Post-processing controller for partial class support..."
        post_process_partial_controller "$output_path" "$service_plugin_dir" "$service_pascal"
    fi

    # Generate service client from schema (CRITICAL for service-to-service communication)
    generate_client "$schema_file" "$service_name" "$service_plugin_dir"

    # Generate separate model file for Client SDK
    generate_models_only "$schema_file" "$service_name" "$service_plugin_dir"

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
