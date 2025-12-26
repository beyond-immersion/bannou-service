#!/bin/bash

# Generate service plugin wrapper (if it doesn't exist)
# Usage: ./generate-plugin.sh <service-name> [schema-file]

set -e  # Exit on any error

# Source common utilities
source "$(dirname "$0")/common.sh"

# Validate arguments
if [ $# -lt 1 ]; then
    log_error "Usage: $0 <service-name> [schema-file]"
    echo "Example: $0 accounts"
    echo "Example: $0 accounts ../schemas/accounts-api.yaml"
    exit 1
fi

SERVICE_NAME="$1"
SCHEMA_FILE="${2:-../schemas/${SERVICE_NAME}-api.yaml}"

SERVICE_PASCAL=$(to_pascal_case "$SERVICE_NAME")
SERVICE_DIR="../lib-${SERVICE_NAME}"
PLUGIN_FILE="$SERVICE_DIR/${SERVICE_PASCAL}ServicePlugin.cs"

echo -e "${YELLOW}🔌 Generating service plugin wrapper for: $SERVICE_NAME${NC}"
echo -e "  📋 Schema: $SCHEMA_FILE"
echo -e "  📁 Output: $PLUGIN_FILE"

# Validate schema file exists
if [ ! -f "$SCHEMA_FILE" ]; then
    echo -e "${RED}❌ Schema file not found: $SCHEMA_FILE${NC}"
    exit 1
fi

# Check if plugin already exists
if [ -f "$PLUGIN_FILE" ]; then
    echo -e "${YELLOW}📝 Service plugin already exists, skipping: $PLUGIN_FILE${NC}"
    exit 0
fi

# Ensure service directory exists
mkdir -p "$SERVICE_DIR"

echo -e "${YELLOW}🔄 Creating service plugin wrapper...${NC}"

# Create service plugin wrapper
cat > "$PLUGIN_FILE" << EOF
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.$SERVICE_PASCAL;

/// <summary>
/// Plugin wrapper for $SERVICE_PASCAL service enabling plugin-based discovery and lifecycle management.
/// Bridges existing IBannouService implementation with the new Plugin system.
/// </summary>
public class ${SERVICE_PASCAL}ServicePlugin : BaseBannouPlugin
{
    public override string PluginName => "$SERVICE_NAME";
    public override string DisplayName => "$SERVICE_PASCAL Service";

    private I${SERVICE_PASCAL}Service? _service;
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Configure services for dependency injection - mimics existing [BannouService] registration.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring service dependencies");

        // Service registration is now handled centrally by PluginLoader based on [BannouService] attributes
        // No need to register I${SERVICE_PASCAL}Service and ${SERVICE_PASCAL}Service here

        // Configuration registration is now handled centrally by PluginLoader based on [ServiceConfiguration] attributes
        // No need to register ${SERVICE_PASCAL}ServiceConfiguration here

        // Add any service-specific dependencies
        // The generated clients should already be registered by AddAllBannouServiceClients()

        Logger?.LogDebug("Service dependencies configured");
    }

    /// <summary>
    /// Configure application pipeline - handles controller registration.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        Logger?.LogInformation("Configuring $SERVICE_PASCAL service application pipeline");

        // The generated ${SERVICE_PASCAL}Controller should already be discovered via standard ASP.NET Core controller discovery

        // Store service provider for lifecycle management
        _serviceProvider = app.Services;

        Logger?.LogInformation("$SERVICE_PASCAL service application pipeline configured");
    }

    /// <summary>
    /// Start the service - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        Logger?.LogInformation("Starting $SERVICE_PASCAL service");

        try
        {
            // Get service instance from DI container with proper scope handling
            // Note: CreateScope() is required for Scoped services to avoid "Cannot resolve scoped service from root provider" error
            using var scope = _serviceProvider?.CreateScope();
            _service = scope?.ServiceProvider.GetService<I${SERVICE_PASCAL}Service>();

            if (_service == null)
            {
                Logger?.LogError("Failed to resolve I${SERVICE_PASCAL}Service from DI container");
                return false;
            }

            // Call existing IBannouService.OnStartAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnStartAsync for $SERVICE_PASCAL service");
                await bannouService.OnStartAsync(CancellationToken.None);
            }

            Logger?.LogInformation("$SERVICE_PASCAL service started successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to start $SERVICE_PASCAL service");
            return false;
        }
    }

    /// <summary>
    /// Running phase - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (_service == null) return;

        Logger?.LogDebug("$SERVICE_PASCAL service running");

        try
        {
            // Call existing IBannouService.OnRunningAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnRunningAsync for $SERVICE_PASCAL service");
                await bannouService.OnRunningAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during $SERVICE_PASCAL service running phase");
        }
    }

    /// <summary>
    /// Shutdown the service - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task OnShutdownAsync()
    {
        if (_service == null) return;

        Logger?.LogInformation("Shutting down $SERVICE_PASCAL service");

        try
        {
            // Call existing IBannouService.OnShutdownAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnShutdownAsync for $SERVICE_PASCAL service");
                await bannouService.OnShutdownAsync();
            }

            Logger?.LogInformation("$SERVICE_PASCAL service shutdown complete");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during $SERVICE_PASCAL service shutdown");
        }
    }
}
EOF

# Check if generation succeeded
if [ -f "$PLUGIN_FILE" ]; then
    FILE_SIZE=$(wc -l < "$PLUGIN_FILE" 2>/dev/null || echo "0")
    echo -e "${GREEN}✅ Generated service plugin wrapper ($FILE_SIZE lines)${NC}"
    echo -e "${YELLOW}💡 Plugin bridges existing service implementation with new Plugin system${NC}"
    exit 0
else
    echo -e "${RED}❌ Failed to generate service plugin${NC}"
    exit 1
fi
