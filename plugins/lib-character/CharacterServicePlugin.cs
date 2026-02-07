using BeyondImmersion.Bannou.BehaviorCompiler.Templates;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.LibCharacter.Templates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Character;

/// <summary>
/// Plugin wrapper for Character service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class CharacterServicePlugin : StandardServicePlugin<ICharacterService>
{
    public override string PluginName => "character";
    public override string DisplayName => "Character Service";

    /// <summary>
    /// Running phase - registers resource template and compression callback with lib-resource.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        if (ServiceProvider == null) return;

        // Register resource template for ABML compile-time path validation.
        // This enables SemanticAnalyzer to validate expressions like ${candidate.character.name}.
        try
        {
            var templateRegistry = ServiceProvider.GetService<IResourceTemplateRegistry>();
            if (templateRegistry != null)
            {
                templateRegistry.Register(new CharacterBaseTemplate());
                Logger?.LogDebug("Registered character resource template with namespace 'character'");
            }
            else
            {
                Logger?.LogDebug("IResourceTemplateRegistry not available - resource template not registered");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Failed to register character resource template");
        }

        // Register compression callback with lib-resource (generated from x-compression-callback)
        try
        {
            using var scope = ServiceProvider.CreateScope();
            var resourceClient = scope.ServiceProvider.GetService<IResourceClient>();
            if (resourceClient != null)
            {
                if (await CharacterCompressionCallbacks.RegisterAsync(resourceClient, CancellationToken.None))
                {
                    Logger?.LogInformation("Registered character compression callback with lib-resource");
                }
                else
                {
                    Logger?.LogWarning("Failed to register character compression callback with lib-resource");
                }
            }
            else
            {
                Logger?.LogDebug("IResourceClient not available - compression callback not registered");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Failed to register compression callback with lib-resource");
        }
    }
}
