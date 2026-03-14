using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Resource;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Quest;

/// <summary>
/// Plugin wrapper for Quest service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class QuestServicePlugin : StandardServicePlugin<IQuestService>
{
    public override string PluginName => "quest";
    public override string DisplayName => "Quest Service";

    /// <inheritdoc />
    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        // Register compression callback with lib-resource (generated from x-compression-callback)
        await RegisterCompressionCallbackAsync();

        // Register resource cleanup callbacks with lib-resource (generated from x-references)
        await RegisterCleanupCallbacksAsync();

        // Register event templates for emit_event: ABML action
        RegisterEventTemplates();
    }

    /// <summary>
    /// Registers event templates for emit_event: ABML action (generated from x-event-template).
    /// </summary>
    private void RegisterEventTemplates()
    {
        var serviceProvider = ServiceProvider;
        if (serviceProvider == null) return;

        try
        {
            using var scope = serviceProvider.CreateScope();
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
    /// Registers resource cleanup callbacks with lib-resource for character deletion cleanup per FOUNDATION TENETS.
    /// Uses the schema-generated <see cref="QuestService.RegisterResourceCleanupCallbacksAsync"/> method.
    /// </summary>
    private async Task RegisterCleanupCallbacksAsync()
    {
        var serviceProvider = ServiceProvider;
        if (serviceProvider == null) return;

        // IResourceClient is L1 infrastructure - must be available (fail-fast per TENETS).
        using var scope = serviceProvider.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();

        if (await QuestService.RegisterResourceCleanupCallbacksAsync(resourceClient, CancellationToken.None))
        {
            Logger?.LogInformation("Registered quest cleanup callbacks with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register quest cleanup callbacks with lib-resource");
        }
    }

    /// <summary>
    /// Registers the quest compression callback with lib-resource for character archival.
    /// Uses the schema-generated <see cref="QuestCompressionCallbacks"/> class.
    /// </summary>
    private async Task RegisterCompressionCallbackAsync()
    {
        var serviceProvider = ServiceProvider;
        if (serviceProvider == null) return;

        // IResourceClient is L1 infrastructure - must be available (fail-fast per TENETS).
        using var scope = serviceProvider.CreateScope();
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
}
