using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Seed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Gardener;

/// <summary>
/// Plugin wrapper for Gardener service enabling plugin-based discovery and lifecycle management.
/// Registers background workers and DI listener implementations.
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPLEMENTATION TENETS - DI Listener:</b> Registers GardenerService as an
/// ISeedEvolutionListener for local-only fan-out of seed growth/phase notifications.
/// Reactions write to distributed state (Redis), ensuring other nodes see updates.
/// </para>
/// </remarks>
public class GardenerServicePlugin : StandardServicePlugin<IGardenerService>
{
    /// <inheritdoc />
    public override string PluginName => "gardener";

    /// <inheritdoc />
    public override string DisplayName => "Gardener Service";

    /// <summary>
    /// Configures services for the Gardener plugin including background workers
    /// and DI listener registrations.
    /// </summary>
    /// <param name="services">The service collection to register services into.</param>
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Register ISeedEvolutionListener as Singleton for seed growth/phase notifications.
        // Must be a separate class (not GardenerService) because ISeedEvolutionListener
        // is consumed by BackgroundService workers (Singleton context), while GardenerService
        // is Scoped. Follows SeedCollectionUnlockListener pattern.
        // per IMPLEMENTATION TENETS - DI Listener pattern
        services.AddSingleton<ISeedEvolutionListener, GardenerSeedEvolutionListener>();

        // Register background workers as hosted services
        services.AddHostedService<GardenerVoidOrchestratorWorker>();
        services.AddHostedService<GardenerScenarioLifecycleWorker>();
    }

    /// <summary>
    /// Running phase - registers the guardian seed type with the Seed service.
    /// ISeedClient is L2 (hard dependency per SERVICE HIERARCHY).
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        var serviceProvider = ServiceProvider
            ?? throw new InvalidOperationException("ServiceProvider not available during OnRunningAsync");

        var seedClient = serviceProvider.GetRequiredService<ISeedClient>();
        var configuration = serviceProvider.GetRequiredService<GardenerServiceConfiguration>();

        try
        {
            await seedClient.RegisterSeedTypeAsync(new RegisterSeedTypeRequest
            {
                SeedTypeCode = configuration.SeedTypeCode,
                GameServiceId = Guid.Empty, // Gardener is cross-game; resolved at runtime
                DisplayName = "Guardian Spirit",
                Description = "Player guardian spirit that grows through void exploration and scenario completion",
                MaxPerOwner = 1,
                AllowedOwnerTypes = new List<string> { "account" },
                GrowthPhases = new List<GrowthPhaseDefinition>
                {
                    new() { PhaseCode = "nascent", DisplayName = "Nascent", MinTotalGrowth = 0 },
                    new() { PhaseCode = "awakening", DisplayName = "Awakening", MinTotalGrowth = 100 },
                    new() { PhaseCode = "attuned", DisplayName = "Attuned", MinTotalGrowth = 500 },
                    new() { PhaseCode = "resonant", DisplayName = "Resonant", MinTotalGrowth = 2000 },
                    new() { PhaseCode = "transcendent", DisplayName = "Transcendent", MinTotalGrowth = 10000 }
                },
                BondCardinality = 1,
                BondPermanent = true
            }, CancellationToken.None);

            Logger?.LogInformation(
                "Registered guardian seed type with code {SeedTypeCode}", configuration.SeedTypeCode);
        }
        catch (ApiException ex) when (ex.StatusCode == 409)
        {
            // Already registered from a previous startup - expected
            Logger?.LogDebug(
                "Guardian seed type {SeedTypeCode} already registered", configuration.SeedTypeCode);
        }
    }
}
