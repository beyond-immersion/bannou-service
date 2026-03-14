using BeyondImmersion.Bannou.BehaviorCompiler.Templates;
using BeyondImmersion.BannouService.CharacterHistory.Providers;
using BeyondImmersion.BannouService.Generated.ResourceTemplates;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Resource;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.CharacterHistory;

/// <summary>
/// Plugin wrapper for Character History service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class CharacterHistoryServicePlugin : StandardServicePlugin<ICharacterHistoryService>
{
    public override string PluginName => "character-history";
    public override string DisplayName => "Character History Service";

    /// <inheritdoc />
    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        var serviceProvider = ServiceProvider
            ?? throw new InvalidOperationException("ServiceProvider not available during OnRunningAsync");

        // Register resource template for ABML compile-time path validation.
        // This enables SemanticAnalyzer to validate expressions like ${candidate.history.hasBackstory}.
        // IResourceTemplateRegistry is L0 infrastructure - must be available (fail-fast per TENETS).
        var templateRegistry = serviceProvider.GetRequiredService<IResourceTemplateRegistry>();
        templateRegistry.Register(new CharacterHistoryTemplate());
        Logger?.LogDebug("Registered character-history resource template with namespace 'history'");

        // Register cleanup callbacks with lib-resource for character reference tracking.
        // IResourceClient is L1 infrastructure - must be available (fail-fast per TENETS).
        using var scope = serviceProvider.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();

        var success = await CharacterHistoryService.RegisterResourceCleanupCallbacksAsync(resourceClient, CancellationToken.None);
        if (success)
        {
            Logger?.LogInformation("Registered character cleanup callbacks with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register some cleanup callbacks with lib-resource");
        }

        // Register compression callback (generated from x-compression-callback)
        if (await CharacterHistoryCompressionCallbacks.RegisterAsync(resourceClient, CancellationToken.None))
        {
            Logger?.LogInformation("Registered character-history compression callback with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register character-history compression callback with lib-resource");
        }
    }
}
