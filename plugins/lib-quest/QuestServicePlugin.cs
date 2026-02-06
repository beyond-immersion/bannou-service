using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Quest.Caching;
using BeyondImmersion.BannouService.Quest.Providers;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Quest;

/// <summary>
/// Plugin wrapper for Quest service enabling plugin-based discovery and lifecycle management.
/// Bridges existing IBannouService implementation with the new Plugin system.
/// </summary>
public class QuestServicePlugin : BaseBannouPlugin
{
    public override string PluginName => "quest";
    public override string DisplayName => "Quest Service";

    private IQuestService? _service;
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Configure services for dependency injection - mimics existing [BannouService] registration.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring service dependencies");

        // Service registration is now handled centrally by PluginLoader based on [BannouService] attributes
        // No need to register IQuestService and QuestService here

        // Configuration registration is now handled centrally by PluginLoader based on [ServiceConfiguration] attributes
        // No need to register QuestServiceConfiguration here

        // Register quest data cache (singleton for cross-request caching)
        services.AddSingleton<IQuestDataCache, QuestDataCache>();

        // Register variable provider factory for Actor to discover via DI
        // Enables dependency inversion: Actor (L2) consumes providers without knowing about Quest (L4)
        services.AddSingleton<IVariableProviderFactory, QuestProviderFactory>();

        Logger?.LogDebug("Service dependencies configured");
    }

    /// <summary>
    /// Configure application pipeline - handles controller registration.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        Logger?.LogInformation("Configuring Quest service application pipeline");

        // The generated QuestController should already be discovered via standard ASP.NET Core controller discovery

        // Store service provider for lifecycle management
        _serviceProvider = app.Services;

        Logger?.LogInformation("Quest service application pipeline configured");
    }

    /// <summary>
    /// Start the service - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        Logger?.LogInformation("Starting Quest service");

        try
        {
            // Get service instance from DI container with proper scope handling
            // Note: CreateScope() is required for Scoped services to avoid "Cannot resolve scoped service from root provider" error
            using var scope = _serviceProvider?.CreateScope();
            _service = scope?.ServiceProvider.GetService<IQuestService>();

            if (_service == null)
            {
                Logger?.LogError("Failed to resolve IQuestService from DI container");
                return false;
            }

            // Call existing IBannouService.OnStartAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnStartAsync for Quest service");
                await bannouService.OnStartAsync(CancellationToken.None);
            }

            Logger?.LogInformation("Quest service started successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to start Quest service");
            return false;
        }
    }

    /// <summary>
    /// Running phase - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (_service == null) return;

        Logger?.LogDebug("Quest service running");

        try
        {
            // Call existing IBannouService.OnRunningAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnRunningAsync for Quest service");
                await bannouService.OnRunningAsync(CancellationToken.None);
            }

            // Register compression callback with lib-resource
            await RegisterCompressionCallbackAsync();
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during Quest service running phase");
        }
    }

    /// <summary>
    /// Registers the quest compression callback with lib-resource for character archival.
    /// </summary>
    private async Task RegisterCompressionCallbackAsync()
    {
        if (_serviceProvider == null) return;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var resourceClient = scope.ServiceProvider.GetService<IResourceClient>();
            if (resourceClient != null)
            {
                await resourceClient.DefineCompressCallbackAsync(new DefineCompressCallbackRequest
                {
                    ResourceType = "character",
                    SourceType = "quest",
                    ServiceName = "quest",
                    CompressEndpoint = "/quest/get-compress-data",
                    CompressPayloadTemplate = "{\"characterId\": \"{{resourceId}}\"}",
                    Priority = 50,
                    Description = "Quest state (active quests, completed counts, category breakdown)"
                });
                Logger?.LogInformation("Registered quest compression callback with lib-resource");
            }
            else
            {
                Logger?.LogDebug("IResourceClient not available - compression callback not registered (lib-resource may not be enabled)");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Failed to register compression callback with lib-resource");
        }
    }

    /// <summary>
    /// Shutdown the service - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task OnShutdownAsync()
    {
        if (_service == null) return;

        Logger?.LogInformation("Shutting down Quest service");

        try
        {
            // Call existing IBannouService.OnShutdownAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnShutdownAsync for Quest service");
                await bannouService.OnShutdownAsync();
            }

            Logger?.LogInformation("Quest service shutdown complete");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during Quest service shutdown");
        }
    }
}
