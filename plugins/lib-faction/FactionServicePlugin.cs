using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Faction.Providers;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Providers;
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

        // Register ISeedEvolutionListener as Singleton for seed growth/phase/capability notifications.
        // Must be a separate class (not FactionService) because ISeedEvolutionListener
        // is consumed by BackgroundService workers (Singleton context), while FactionService
        // is Scoped. Follows GardenerSeedEvolutionListener pattern.
        // per IMPLEMENTATION TENETS - DI Listener pattern
        services.AddSingleton<ISeedEvolutionListener, FactionSeedEvolutionListener>();

        // Register ICollectionUnlockListener as Singleton for Collection->Seed growth pipeline.
        // Converts member activity collection unlocks into faction seed growth via tag matching.
        // per IMPLEMENTATION TENETS - DI Listener pattern
        services.AddSingleton<ICollectionUnlockListener, FactionCollectionUnlockListener>();

        // Register IVariableProviderFactory as Singleton for Actor ABML expression evaluation.
        // Provides ${faction.*} namespace with membership and faction data.
        // per IMPLEMENTATION TENETS - Variable Provider Factory pattern
        services.AddSingleton<IVariableProviderFactory, FactionProviderFactory>();
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
                AllowedOwnerTypes = new List<string> { "faction" },
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
