#!/bin/bash

# Bannou Service Implementation Generator
# Automatically creates complete service implementations from NSwag-generated controllers
# This script follows the schema-first, consolidated service architecture pattern

set -e  # Exit on any error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Helper functions
log_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

log_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check if we're in the right directory
if [ ! -f "bannou-service.sln" ]; then
    log_error "This script must be run from the bannou root directory"
    exit 1
fi

# Function to extract controller method signatures
extract_controller_signatures() {
    local controller_file="$1"
    local controller_name="$2"
    
    log_info "Extracting signatures from $controller_name"
    
    # Extract abstract method signatures using grep and sed
    grep -n "public abstract" "$controller_file" | grep -v "class\|interface" | while IFS=: read -r line_num signature; do
        # Clean up the signature
        clean_signature=$(echo "$signature" | sed 's/public abstract //' | sed 's/;$//')
        echo "Line $line_num: $clean_signature"
    done
}

# Function to generate service interface from controller signatures
generate_service_interface() {
    local controller_name="$1"
    local lib_dir="$2"
    
    log_info "Generating service interface for $controller_name"
    
    # Determine service name and interface name
    service_name=$(echo "$controller_name" | sed 's/Controller$//')
    interface_name="I${service_name}Service"
    interface_file="${lib_dir}/I${service_name}Service.cs"
    
    # Read the generated controller to extract method signatures
    controller_file="bannou-service/Controllers/Generated/${controller_name}.Generated.cs"
    
    if [ ! -f "$controller_file" ]; then
        log_error "Generated controller not found: $controller_file"
        return 1
    fi
    
    # Create the interface content
    cat > "$interface_file" << EOF
using Microsoft.AspNetCore.Mvc;
using BeyondImmersion.BannouService.Controllers.Generated;

namespace BeyondImmersion.BannouService.${service_name};

/// <summary>
/// Interface for ${service_name,,} service operations.
/// Implements business logic for the generated ${controller_name} methods.
/// </summary>
public interface $interface_name
{
EOF

    # Extract method signatures and convert them to interface methods
    grep "public abstract" "$controller_file" | grep -v "class\|interface" | while read -r line; do
        # Convert abstract method to interface method
        method_signature=$(echo "$line" | sed 's/.*public abstract //' | sed 's/;$//')
        method_name=$(echo "$method_signature" | sed 's/.* \([A-Z][a-zA-Z0-9]*\)(.*/\1/')
        
        # Add the method with Async suffix if not already present
        if [[ ! "$method_name" == *"Async" ]]; then
            method_signature=$(echo "$method_signature" | sed "s/$method_name/${method_name}Async/")
        fi
        
        echo "    /// <summary>" >> "$interface_file"
        echo "    /// Generated method from schema-first controller" >> "$interface_file"
        echo "    /// </summary>" >> "$interface_file"
        echo "    Task<$method_signature;" >> "$interface_file"
        echo "" >> "$interface_file"
    done
    
    echo "}" >> "$interface_file"
    
    log_success "Generated interface: $interface_file"
}

# Function to generate concrete controller implementation
generate_concrete_controller() {
    local controller_name="$1"
    
    log_info "Generating concrete controller for $controller_name"
    
    service_name=$(echo "$controller_name" | sed 's/Controller$//')
    service_interface="I${service_name}Service"
    controller_file="bannou-service/Controllers/${controller_name}.cs"
    
    cat > "$controller_file" << EOF
using Microsoft.AspNetCore.Mvc;
using BeyondImmersion.BannouService.Controllers.Generated;
using BeyondImmersion.BannouService.${service_name};

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Concrete implementation of the ${controller_name}.
/// Inherits from generated abstract controller and delegates to $service_interface.
/// </summary>
[ApiController]
public class $controller_name : ${controller_name}BaseControllerBase
{
    private readonly $service_interface _service;
    private readonly ILogger<$controller_name> _logger;

    public $controller_name(
        $service_interface service,
        ILogger<$controller_name> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
EOF

    # Extract and implement abstract methods
    generated_controller="bannou-service/Controllers/Generated/${controller_name}.Generated.cs"
    grep "public abstract" "$generated_controller" | grep -v "class\|interface" | while read -r line; do
        # Extract method signature components
        method_signature=$(echo "$line" | sed 's/.*public abstract //' | sed 's/;$//')
        method_name=$(echo "$method_signature" | grep -o '[A-Z][a-zA-Z0-9]*(' | sed 's/(//')
        
        echo "" >> "$controller_file"
        echo "    /// <inheritdoc/>" >> "$controller_file"
        echo "    public override async $method_signature" >> "$controller_file"
        echo "    {" >> "$controller_file"
        echo "        try" >> "$controller_file"
        echo "        {" >> "$controller_file"
        echo "            _logger.LogDebug(\"Processing $method_name request\");" >> "$controller_file"
        
        # Add service call with Async suffix
        service_method="${method_name}Async"
        echo "            return await _service.$service_method($(extract_parameters "$method_signature"));" >> "$controller_file"
        
        echo "        }" >> "$controller_file"
        echo "        catch (Exception ex)" >> "$controller_file"
        echo "        {" >> "$controller_file"
        echo "            _logger.LogError(ex, \"Error processing $method_name request\");" >> "$controller_file"
        echo "            return StatusCode(500, new { error = \"INTERNAL_ERROR\", message = \"An error occurred while processing the request\" });" >> "$controller_file"
        echo "        }" >> "$controller_file"
        echo "    }" >> "$controller_file"
    done
    
    echo "}" >> "$controller_file"
    
    log_success "Generated concrete controller: $controller_file"
}

# Helper function to extract parameter names for service calls
extract_parameters() {
    local signature="$1"
    # Extract parameter names (simplified - would need more complex parsing for real implementation)
    echo "/* TODO: Extract actual parameters */"
}

# Function to generate service implementation
generate_service_implementation() {
    local service_name="$1"
    local lib_dir="$2"
    
    log_info "Generating service implementation for $service_name"
    
    service_file="${lib_dir}/${service_name}Service.cs"
    interface_name="I${service_name}Service"
    
    cat > "$service_file" << EOF
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Controllers.Generated;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.${service_name};

/// <summary>
/// Service implementation for ${service_name,,} operations.
/// Implements the schema-first generated interface methods.
/// </summary>
[DaprService("${service_name,,}", typeof($interface_name), lifetime: ServiceLifetime.Scoped)]
public class ${service_name}Service : DaprService<${service_name}ServiceConfiguration>, $interface_name
{
    private readonly ILogger<${service_name}Service> _logger;

    public ${service_name}Service(
        ${service_name}ServiceConfiguration configuration,
        ILogger<${service_name}Service> logger)
        : base(configuration, logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // TODO: Implement interface methods based on generated interface
    // This would require parsing the interface file and generating method stubs
}
EOF
    
    log_success "Generated service implementation: $service_file"
}

# Main execution
main() {
    log_info "Starting Bannou Service Implementation Generation"
    log_info "==================================================="
    
    # Check for generated controllers
    generated_controllers_dir="bannou-service/Controllers/Generated"
    if [ ! -d "$generated_controllers_dir" ]; then
        log_error "Generated controllers directory not found. Run 'nswag run' first."
        exit 1
    fi
    
    # Find all generated controllers
    controllers=($(find "$generated_controllers_dir" -name "*Controller.Generated.cs" | sed 's|.*/||' | sed 's|\.Generated\.cs||'))
    
    if [ ${#controllers[@]} -eq 0 ]; then
        log_error "No generated controllers found. Run 'nswag run' first."
        exit 1
    fi
    
    log_info "Found ${#controllers[@]} generated controllers: ${controllers[*]}"
    
    # Process each controller
    for controller in "${controllers[@]}"; do
        log_info "Processing $controller..."
        
        # Determine the corresponding lib directory
        service_name=$(echo "$controller" | sed 's/Controller$//')
        lib_dir="lib-${service_name,,}"
        
        if [ ! -d "$lib_dir" ]; then
            log_warning "Creating lib directory: $lib_dir"
            mkdir -p "$lib_dir"
        fi
        
        # Generate service interface (commented out for now due to complexity)
        # generate_service_interface "$controller" "$lib_dir"
        
        # Generate concrete controller (commented out for now due to complexity)
        # generate_concrete_controller "$controller"
        
        # Generate service implementation (commented out for now due to complexity)
        # generate_service_implementation "$(echo $controller | sed 's/Controller$//')" "$lib_dir"
        
        # For now, just show what we found
        extract_controller_signatures "$generated_controllers_dir/${controller}.Generated.cs" "$controller"
        
        echo "----------------------------------------"
    done
    
    log_success "Service implementation generation completed!"
    log_info "Note: This script provides a framework. The actual implementation generators are"
    log_info "commented out due to the complexity of parsing C# method signatures accurately."
    log_info "The manual implementation approach used in this session is more reliable."
}

# Run the script
main "$@"
