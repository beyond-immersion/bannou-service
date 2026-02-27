using BeyondImmersion.BannouService.Events;
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
        // Enables dependency inversion: Actor (L2) consumes providers without knowing about Quest (L2)
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
            // Resolve scoped service within a scope — do NOT store the reference beyond scope lifetime.
            // IQuestService is Scoped; storing it in a field causes use-after-dispose.
            var serviceProvider = _serviceProvider ?? throw new InvalidOperationException("ServiceProvider not available during OnStartAsync");
            using var scope = serviceProvider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IQuestService>();

            // Call existing IBannouService.OnStartAsync if the service implements it
            if (service is IBannouService bannouService)
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
        var serviceProvider = _serviceProvider ?? throw new InvalidOperationException("ServiceProvider not available during OnRunningAsync");

        Logger?.LogDebug("Quest service running");

        try
        {
            // Resolve scoped service within a scope for the running phase lifecycle call
            using var runningScope = serviceProvider.CreateScope();
            var service = runningScope.ServiceProvider.GetRequiredService<IQuestService>();

            // Call existing IBannouService.OnRunningAsync if the service implements it
            if (service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnRunningAsync for Quest service");
                await bannouService.OnRunningAsync(CancellationToken.None);
            }

            // Register compression callback with lib-resource (generated from x-compression-callback)
            await RegisterCompressionCallbackAsync();

            // Register event templates for emit_event: ABML action
            RegisterEventTemplates();
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during Quest service running phase");
        }
    }

    /// <summary>
    /// Registers event templates for emit_event: ABML action (generated from x-event-template).
    /// </summary>
    private void RegisterEventTemplates()
    {
        if (_serviceProvider == null) return;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var eventTemplateRegistry = scope.ServiceProvider.GetService<IEventTemplateRegistry>();
            if (eventTemplateRegistry != null)
            {
                QuestEventTemplates.RegisterAll(eventTemplateRegistry);
                Logger?.LogInformation("Registered quest event templates");
            }
            else
            {
                Logger?.LogDebug("IEventTemplateRegistry not available - event templates not registered");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Failed to register event templates");
        }
    }

    /// <summary>
    /// Registers the quest compression callback with lib-resource for character archival.
    /// Uses the schema-generated <see cref="QuestCompressionCallbacks"/> class.
    /// </summary>
    private async Task RegisterCompressionCallbackAsync()
    {
        if (_serviceProvider == null) return;

        // IResourceClient is L1 infrastructure - must be available (fail-fast per TENETS).
        using var scope = _serviceProvider.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();

        if (await QuestCompressionCallbacks.RegisterAsync(resourceClient, CancellationToken.None))
        {
            Logger?.LogInformation("Registered quest compression callback with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register quest compression callback with lib-resource");
        }
    }

    /// <summary>
    /// Shutdown the service - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task OnShutdownAsync()
    {
        var serviceProvider = _serviceProvider ?? throw new InvalidOperationException("ServiceProvider not available during OnShutdownAsync");

        Logger?.LogInformation("Shutting down Quest service");

        try
        {
            // Resolve scoped service within a scope for the shutdown phase lifecycle call
            using var shutdownScope = serviceProvider.CreateScope();
            var service = shutdownScope.ServiceProvider.GetRequiredService<IQuestService>();

            // Call existing IBannouService.OnShutdownAsync if the service implements it
            if (service is IBannouService bannouService)
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
