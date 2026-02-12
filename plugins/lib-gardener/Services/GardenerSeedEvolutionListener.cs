using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Gardener;

/// <summary>
/// Listens for seed evolution notifications (growth, phase changes, capabilities)
/// and updates garden instances accordingly.
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPLEMENTATION TENETS - DI Listener:</b> ISeedEvolutionListener is a local-only fan-out
/// mechanism. Reactions write to distributed state (Redis), ensuring other nodes see updates.
/// </para>
/// <para>
/// Registered as Singleton via DI. The Seed service discovers it via
/// <c>IEnumerable&lt;ISeedEvolutionListener&gt;</c> and dispatches notifications during growth/phase changes.
/// </para>
/// <para>
/// This is a separate class from GardenerService (which is Scoped) because ISeedEvolutionListener
/// must be resolvable from singleton context (BackgroundService workers).
/// Follows the same pattern as SeedCollectionUnlockListener.
/// </para>
/// </remarks>
public class GardenerSeedEvolutionListener : ISeedEvolutionListener
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<GardenerSeedEvolutionListener> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GardenerSeedEvolutionListener"/> class.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for garden data access.</param>
    /// <param name="configuration">Gardener configuration for seed type code.</param>
    /// <param name="logger">Logger instance.</param>
    public GardenerSeedEvolutionListener(
        IStateStoreFactory stateStoreFactory,
        GardenerServiceConfiguration configuration,
        ILogger<GardenerSeedEvolutionListener> logger)
    {
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        InterestedSeedTypes = new HashSet<string> { configuration.SeedTypeCode };
    }

    /// <inheritdoc />
    public IReadOnlySet<string> InterestedSeedTypes { get; }

    /// <summary>
    /// Called when growth is recorded for a guardian seed.
    /// Marks the garden instance for re-evaluation on the next orchestrator tick.
    /// </summary>
    public async Task OnGrowthRecordedAsync(SeedGrowthNotification notification, CancellationToken ct)
    {
        var gardenStore = _stateStoreFactory.GetStore<GardenInstanceModel>(
            StateStoreDefinitions.GardenerVoidInstances);
        var gardenKey = $"garden:{notification.OwnerId}";

        var garden = await gardenStore.GetAsync(gardenKey, ct);
        if (garden == null) return;

        garden.NeedsReEvaluation = true;
        await gardenStore.SaveAsync(gardenKey, garden, cancellationToken: ct);

        _logger.LogDebug(
            "Marked garden for re-evaluation after growth for seed {SeedId}, owner {OwnerId}",
            notification.SeedId, notification.OwnerId);
    }

    /// <summary>
    /// Called when a guardian seed changes phase.
    /// Updates cached growth phase and marks for re-evaluation.
    /// </summary>
    public async Task OnPhaseChangedAsync(SeedPhaseNotification notification, CancellationToken ct)
    {
        var gardenStore = _stateStoreFactory.GetStore<GardenInstanceModel>(
            StateStoreDefinitions.GardenerVoidInstances);
        var gardenKey = $"garden:{notification.OwnerId}";

        var garden = await gardenStore.GetAsync(gardenKey, ct);
        if (garden == null) return;

        garden.CachedGrowthPhase = notification.NewPhase;
        garden.NeedsReEvaluation = true;
        await gardenStore.SaveAsync(gardenKey, garden, cancellationToken: ct);

        _logger.LogInformation(
            "Updated cached growth phase to {NewPhase} for seed {SeedId}",
            notification.NewPhase, notification.SeedId);
    }

    /// <summary>
    /// Called when capabilities change. Not currently used by Gardener.
    /// </summary>
    public async Task OnCapabilitiesChangedAsync(SeedCapabilityNotification notification, CancellationToken ct)
    {
        await Task.CompletedTask;
    }
}
