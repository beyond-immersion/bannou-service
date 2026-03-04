using BeyondImmersion.Bannou.BehaviorCompiler.Templates;
using BeyondImmersion.BannouService.Generated.ResourceTemplates;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Resource;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Realm;

/// <summary>
/// Plugin wrapper for Realm service enabling plugin-based discovery and lifecycle management.
/// Registers compression callback for providing realm context to location archives.
/// </summary>
public class RealmServicePlugin : StandardServicePlugin<IRealmService>
{
    public override string PluginName => "realm";
    public override string DisplayName => "Realm Service";

    /// <summary>
    /// Running phase - registers compression callback for location archives.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        await RegisterCompressionCallbackAsync(CancellationToken.None);
    }

    /// <summary>
    /// Registers the realm resource template and compression callback with lib-resource.
    /// Realm provides contextual data (realm name, code, description) for location archives.
    /// IResourceTemplateRegistry is L0 infrastructure and IResourceClient is L1 — both guaranteed available.
    /// </summary>
    private async Task RegisterCompressionCallbackAsync(CancellationToken cancellationToken)
    {
        var serviceProvider = ServiceProvider ?? throw new InvalidOperationException(
            "ServiceProvider not available during OnRunningAsync");

        // Register resource template for ABML compile-time path validation.
        var templateRegistry = serviceProvider.GetRequiredService<IResourceTemplateRegistry>();
        templateRegistry.Register(new RealmContextTemplate());
        Logger?.LogDebug("Registered realm location archive context template with namespace 'realm'");

        // Register compression callback with lib-resource.
        using var scope = serviceProvider.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();
        if (await RealmCompressionCallbacks.RegisterAsync(resourceClient, cancellationToken))
        {
            Logger?.LogInformation("Registered realm compression callback for location archives with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register realm compression callback with lib-resource");
        }
    }
}
