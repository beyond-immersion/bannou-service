using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Resource;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Escrow;

/// <summary>
/// Plugin wrapper for Escrow service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class EscrowServicePlugin : StandardServicePlugin<IEscrowService>
{
    public override string PluginName => "escrow";
    public override string DisplayName => "Escrow Service";

    /// <inheritdoc />
    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();
        await RegisterCleanupCallbacksAsync();
    }

    /// <summary>
    /// Registers resource cleanup callbacks with lib-resource for character deletion cleanup per FOUNDATION TENETS.
    /// Uses the schema-generated <see cref="EscrowService.RegisterResourceCleanupCallbacksAsync"/> method.
    /// </summary>
    private async Task RegisterCleanupCallbacksAsync()
    {
        var serviceProvider = ServiceProvider;
        if (serviceProvider == null) return;

        using var scope = serviceProvider.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();

        if (await EscrowService.RegisterResourceCleanupCallbacksAsync(resourceClient, CancellationToken.None))
        {
            Logger?.LogInformation("Registered escrow cleanup callbacks with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register escrow cleanup callbacks with lib-resource");
        }
    }
}
