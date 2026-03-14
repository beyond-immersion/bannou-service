using BeyondImmersion.Bannou.BehaviorCompiler.Templates;
using BeyondImmersion.BannouService.CharacterPersonality.Providers;
using BeyondImmersion.BannouService.Generated.ResourceTemplates;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Resource;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.CharacterPersonality;

/// <summary>
/// Plugin wrapper for Character Personality service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class CharacterPersonalityServicePlugin : StandardServicePlugin<ICharacterPersonalityService>
{
    public override string PluginName => "character-personality";
    public override string DisplayName => "Character Personality Service";

    /// <inheritdoc />
    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        var serviceProvider = ServiceProvider
            ?? throw new InvalidOperationException("ServiceProvider not available during OnRunningAsync");

        // Register resource template for ABML compile-time path validation.
        // This enables SemanticAnalyzer to validate expressions like ${candidate.personality.archetypeHint}.
        // IResourceTemplateRegistry is L0 infrastructure - must be available (fail-fast per TENETS).
        var templateRegistry = serviceProvider.GetRequiredService<IResourceTemplateRegistry>();
        templateRegistry.Register(new CharacterPersonalityTemplate());
        Logger?.LogDebug("Registered character-personality resource template with namespace 'personality'");

        // Register cleanup callbacks with lib-resource for character reference tracking.
        // IResourceClient is L1 infrastructure - must be available (fail-fast per TENETS).
        using var scope = serviceProvider.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();

        var success = await CharacterPersonalityService.RegisterResourceCleanupCallbacksAsync(resourceClient, CancellationToken.None);
        if (success)
        {
            Logger?.LogInformation("Registered character cleanup callbacks with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register some cleanup callbacks with lib-resource");
        }

        // Register compression callback (generated from x-compression-callback)
        if (await CharacterPersonalityCompressionCallbacks.RegisterAsync(resourceClient, CancellationToken.None))
        {
            Logger?.LogInformation("Registered character-personality compression callback with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register character-personality compression callback with lib-resource");
        }
    }
}
