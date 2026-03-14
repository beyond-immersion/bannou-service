using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Resource;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Obligation;

/// <summary>
/// Plugin wrapper for Obligation service enabling plugin-based discovery and lifecycle management.
/// </summary>
/// <remarks>
/// Registers the ObligationProviderFactory for Actor (L2) to discover via DI,
/// and sets up lib-resource cleanup and compression callbacks during startup.
/// </remarks>
public class ObligationServicePlugin : StandardServicePlugin<IObligationService>
{
    public override string PluginName => "obligation";
    public override string DisplayName => "Obligation Service";

    /// <inheritdoc />
    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        var serviceProvider = ServiceProvider
            ?? throw new InvalidOperationException("ServiceProvider not available during OnRunningAsync");

        // Register cleanup callbacks with lib-resource for character reference tracking.
        // IResourceClient is L1 infrastructure - must be available (fail-fast per TENETS).
        using var scope = serviceProvider.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();

        var success = await ObligationService.RegisterResourceCleanupCallbacksAsync(
            resourceClient, CancellationToken.None);
        if (success)
        {
            Logger?.LogInformation("Registered character cleanup callbacks with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register some cleanup callbacks with lib-resource");
        }

        // Register compression callback (generated from x-compression-callback)
        if (await ObligationCompressionCallbacks.RegisterAsync(resourceClient, CancellationToken.None))
        {
            Logger?.LogInformation("Registered obligation compression callback with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register obligation compression callback with lib-resource");
        }
    }
}
