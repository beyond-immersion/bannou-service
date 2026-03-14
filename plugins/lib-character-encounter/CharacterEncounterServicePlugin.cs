using BeyondImmersion.BannouService.CharacterEncounter.Caching;
using BeyondImmersion.BannouService.CharacterEncounter.Providers;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Resource;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.CharacterEncounter;

/// <summary>
/// Plugin wrapper for Character Encounter service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class CharacterEncounterServicePlugin : StandardServicePlugin<ICharacterEncounterService>
{
    public override string PluginName => "character-encounter";
    public override string DisplayName => "Character Encounter Service";

    /// <summary>
    /// Configure services for dependency injection.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Register the memory decay scheduler background service
        // This only activates when MemoryDecayMode is set to Scheduled
        services.AddHostedService<MemoryDecaySchedulerService>();
    }

    /// <summary>
    /// Running phase - registers cleanup and compression callbacks with lib-resource,
    /// and registers event templates for emit_event ABML action.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        // Register cleanup callbacks with lib-resource for character reference tracking.
        // IResourceClient is L1 infrastructure - must be available (fail-fast per TENETS).
        using var resourceScope = ServiceProvider!.CreateScope();
        var resourceClient = resourceScope.ServiceProvider.GetRequiredService<IResourceClient>();

        var success = await CharacterEncounterService.RegisterResourceCleanupCallbacksAsync(resourceClient, CancellationToken.None);
        if (success)
        {
            Logger?.LogInformation("Registered character cleanup callbacks with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register some cleanup callbacks with lib-resource");
        }

        // Register compression callback (generated from x-compression-callback)
        if (await CharacterEncounterCompressionCallbacks.RegisterAsync(resourceClient, CancellationToken.None))
        {
            Logger?.LogInformation("Registered character-encounter compression callback with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register character-encounter compression callback with lib-resource");
        }

        // Register event templates for emit_event: ABML action (generated from x-event-template)
        try
        {
            using var scope = ServiceProvider!.CreateScope();
            var eventTemplateRegistry = scope.ServiceProvider.GetService<IEventTemplateRegistry>();
            if (eventTemplateRegistry != null)
            {
                CharacterEncounterEventTemplates.RegisterAll(eventTemplateRegistry);
                Logger?.LogInformation("Registered character-encounter event templates");
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
}
