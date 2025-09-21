#!/bin/bash

# Generate service plugin wrapper (if it doesn't exist)
# Usage: ./generate-plugin.sh <service-name> [schema-file]

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
/// Bridges existing IDaprService implementation with the new Plugin system.
/// </summary>
public class ${SERVICE_PASCAL}ServicePlugin : BaseBannouPlugin
{
    public override string PluginName => "$SERVICE_NAME";
    public override string DisplayName => "$SERVICE_PASCAL Service";

    private I${SERVICE_PASCAL}Service? _service;
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Validate that this plugin should be loaded based on environment configuration.
    /// </summary>
    protected override bool OnValidatePlugin()
    {
        var enabled = Environment.GetEnvironmentVariable("${SERVICE_NAME^^}_SERVICE_ENABLED")?.ToLower();
        Logger?.LogDebug("$SERVICE_PASCAL service enabled check: {EnabledValue}", enabled);
        return enabled == "true";
    }

    /// <summary>
    /// Configure services for dependency injection - mimics existing [DaprService] registration.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        if (!OnValidatePlugin())
        {
            Logger?.LogInformation("$SERVICE_PASCAL service disabled, skipping service registration");
            return;
        }

        Logger?.LogInformation("Configuring $SERVICE_PASCAL service dependencies");

        // Register the service implementation (existing pattern from [DaprService] attribute)
        services.AddScoped<I${SERVICE_PASCAL}Service, ${SERVICE_PASCAL}Service>();
        services.AddScoped<${SERVICE_PASCAL}Service>();

        // Register generated configuration class
        services.AddScoped<${SERVICE_PASCAL}ServiceConfiguration>();

        // Add any service-specific dependencies
        // The generated clients should already be registered by AddAllBannouServiceClients()

        Logger?.LogInformation("$SERVICE_PASCAL service dependencies configured");
    }

    /// <summary>
    /// Configure application pipeline - handles controller registration.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        if (!OnValidatePlugin())
        {
            Logger?.LogInformation("$SERVICE_PASCAL service disabled, skipping application configuration");
            return;
        }

        Logger?.LogInformation("Configuring $SERVICE_PASCAL service application pipeline");

        // The generated ${SERVICE_PASCAL}Controller should already be discovered via standard ASP.NET Core controller discovery
        // since we're not excluding the assembly like we did with IDaprController approach

        // Store service provider for lifecycle management
        _serviceProvider = app.Services;

        Logger?.LogInformation("$SERVICE_PASCAL service application pipeline configured");
    }

    /// <summary>
    /// Start the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        if (!OnValidatePlugin()) return true;

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

            // Call existing IDaprService.OnStartAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnStartAsync for $SERVICE_PASCAL service");
                await daprService.OnStartAsync(CancellationToken.None);
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
    /// Running phase - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (!OnValidatePlugin() || _service == null) return;

        Logger?.LogDebug("$SERVICE_PASCAL service running");

        try
        {
            // Call existing IDaprService.OnRunningAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnRunningAsync for $SERVICE_PASCAL service");
                await daprService.OnRunningAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during $SERVICE_PASCAL service running phase");
        }
    }

    /// <summary>
    /// Shutdown the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnShutdownAsync()
    {
        if (!OnValidatePlugin() || _service == null) return;

        Logger?.LogInformation("Shutting down $SERVICE_PASCAL service");

        try
        {
            // Call existing IDaprService.OnShutdownAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnShutdownAsync for $SERVICE_PASCAL service");
                await daprService.OnShutdownAsync();
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
    echo -e "${YELLOW}🔧 Enable via environment variable: ${SERVICE_NAME^^}_SERVICE_ENABLED=true${NC}"
    exit 0
else
    echo -e "${RED}❌ Failed to generate service plugin${NC}"
    exit 1
fi
