using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Resource;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Storyline;

/// <summary>
/// Plugin wrapper for Storyline service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class StorylineServicePlugin : StandardServicePlugin<IStorylineService>
{
    public override string PluginName => "storyline";
    public override string DisplayName => "Storyline Service";

    /// <inheritdoc />
    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        // Register compression callback with lib-resource (generated from x-compression-callback)
        await RegisterCompressionCallbackAsync();

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
                StorylineEventTemplates.RegisterAll(eventTemplateRegistry);
                Logger?.LogInformation("Registered storyline event templates");
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
    /// Registers the storyline compression callback with lib-resource for character archival.
    /// Uses the schema-generated <see cref="StorylineCompressionCallbacks"/> class.
    /// </summary>
    private async Task RegisterCompressionCallbackAsync()
    {
        var serviceProvider = ServiceProvider;
        if (serviceProvider == null) return;

        // IResourceClient is L1 infrastructure - must be available (fail-fast per TENETS).
        using var scope = serviceProvider.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();

        if (await StorylineCompressionCallbacks.RegisterAsync(resourceClient, CancellationToken.None))
        {
            Logger?.LogInformation("Registered storyline compression callback with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register storyline compression callback with lib-resource");
        }
    }
}
