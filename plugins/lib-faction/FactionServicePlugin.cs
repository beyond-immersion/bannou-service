using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Faction.Providers;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Faction;

/// <summary>
/// Plugin wrapper for Faction service enabling plugin-based discovery and lifecycle management.
/// Registers DI listener and provider implementations.
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPLEMENTATION TENETS - DI Listener:</b> Registers ISeedEvolutionListener and
/// ICollectionUnlockListener for local-only fan-out of seed and collection notifications.
/// Reactions write to distributed state (MySQL/Redis), ensuring other nodes see updates.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Variable Provider Factory:</b> Registers IVariableProviderFactory
/// for Actor (L2) to discover faction data via <c>${faction.*}</c> ABML expressions.
/// </para>
/// </remarks>
public class FactionServicePlugin : StandardServicePlugin<IFactionService>
{
    /// <inheritdoc />
    public override string PluginName => "faction";

    /// <inheritdoc />
    public override string DisplayName => "Faction Service";

    /// <summary>
    /// Configures services for the Faction plugin including DI listener
    /// and variable provider factory registrations.
    /// </summary>
    /// <param name="services">The service collection to register services into.</param>
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
    }

    /// <summary>
    /// Running phase - registers the faction seed type with the Seed service.
    /// ISeedClient is L2 (hard dependency per SERVICE HIERARCHY).
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        var serviceProvider = ServiceProvider
            ?? throw new InvalidOperationException("ServiceProvider not available during OnRunningAsync");

        // Register compression callback (generated from x-compression-callback)
        using var scope = serviceProvider.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();
        if (await FactionCompressionCallbacks.RegisterAsync(resourceClient, CancellationToken.None))
        {
            Logger?.LogInformation("Registered faction compression callback with lib-resource");
        }

        // Register resource cleanup callbacks (generated from x-references)
        var cleanupSuccess = await FactionService.RegisterResourceCleanupCallbacksAsync(resourceClient, CancellationToken.None);
        if (cleanupSuccess)
        {
            Logger?.LogInformation("Registered faction cleanup callbacks with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register some faction cleanup callbacks with lib-resource");
        }

        var seedClient = serviceProvider.GetRequiredService<ISeedClient>();
        var configuration = serviceProvider.GetRequiredService<FactionServiceConfiguration>();

        try
        {
            await seedClient.RegisterSeedTypeAsync(new RegisterSeedTypeRequest
            {
                SeedTypeCode = configuration.SeedTypeCode,
                GameServiceId = null,
                DisplayName = "Faction Spirit",
                Description = "Faction growth seed that unlocks governance capabilities through member activities",
                MaxPerOwner = 1,
                AllowedOwnerTypes = new List<EntityType> { EntityType.Faction },
                GrowthPhases = new List<GrowthPhaseDefinition>
                {
                    new() { PhaseCode = "nascent", DisplayName = "Nascent", MinTotalGrowth = 0 },
                    new() { PhaseCode = "established", DisplayName = "Established", MinTotalGrowth = 50 },
                    new() { PhaseCode = "influential", DisplayName = "Influential", MinTotalGrowth = 200 },
                    new() { PhaseCode = "dominant", DisplayName = "Dominant", MinTotalGrowth = 1000 },
                    new() { PhaseCode = "sovereign", DisplayName = "Sovereign", MinTotalGrowth = 5000 }
                },
                BondCardinality = 0,
                BondPermanent = false
            }, CancellationToken.None);

            Logger?.LogInformation(
                "Registered faction seed type with code {SeedTypeCode}", configuration.SeedTypeCode);
        }
        catch (ApiException ex) when (ex.StatusCode == 409)
        {
            // Already registered from a previous startup - expected
            Logger?.LogDebug(
                "Faction seed type {SeedTypeCode} already registered", configuration.SeedTypeCode);
        }
    }
}
