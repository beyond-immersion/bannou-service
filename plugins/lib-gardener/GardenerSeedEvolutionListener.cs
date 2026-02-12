using BeyondImmersion.BannouService.Providers;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Gardener;

/// <summary>
/// Listens to seed evolution notifications via DI for in-process delivery.
/// Replaces broadcast event subscriptions for seed.growth.updated and seed.phase.changed.
/// </summary>
/// <remarks>
/// <para>
/// <b>DISTRIBUTED DEPLOYMENT NOTE</b>: This listener is LOCAL-ONLY fan-out.
/// Only the node that processes the seed mutation API request will invoke this listener.
/// This is safe because all reactions write to distributed state (Redis/MySQL).
/// </para>
/// <para>
/// Gardener uses this listener to react to guardian seed growth and phase changes:
/// - Growth: Update void orchestration parameters based on seed development
/// - Phase changes: May unlock new scenario categories or deployment phases
/// - Capability changes: May affect scenario selection algorithm weights
/// </para>
/// </remarks>
public class GardenerSeedEvolutionListener : ISeedEvolutionListener
{
    private readonly GardenerServiceConfiguration _configuration;
    private readonly ILogger<GardenerSeedEvolutionListener> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="GardenerSeedEvolutionListener"/>.
    /// </summary>
    /// <param name="configuration">Gardener service configuration.</param>
    /// <param name="logger">Logger instance.</param>
    public GardenerSeedEvolutionListener(
        GardenerServiceConfiguration configuration,
        ILogger<GardenerSeedEvolutionListener> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Only interested in the seed type code this gardener instance manages (e.g., "guardian").
    /// </summary>
    public IReadOnlySet<string> InterestedSeedTypes { get; } = new HashSet<string>();

    /// <summary>
    /// Called after growth is recorded for a seed. Gardener uses this to track growth
    /// progression and adjust scenario selection if the player is currently in the void.
    /// </summary>
    public async Task OnGrowthRecordedAsync(SeedGrowthNotification notification, CancellationToken ct)
    {
        // Filter by seed type code at runtime since we initialize from config
        if (notification.SeedTypeCode != _configuration.SeedTypeCode)
        {
            return;
        }

        _logger.LogDebug(
            "Seed growth recorded: SeedId={SeedId}, TotalGrowth={TotalGrowth}, Domains={DomainCount}",
            notification.SeedId, notification.TotalGrowth, notification.DomainChanges.Count);

        // Growth data affects scenario selection weights on next orchestrator tick.
        // No immediate state change needed; the void orchestrator reads seed data on each tick.
        await Task.CompletedTask;
    }

    /// <summary>
    /// Called after a phase transition for a seed. Phase changes may unlock new scenario
    /// categories or affect deployment phase eligibility checks.
    /// </summary>
    public async Task OnPhaseChangedAsync(SeedPhaseNotification notification, CancellationToken ct)
    {
        if (notification.SeedTypeCode != _configuration.SeedTypeCode)
        {
            return;
        }

        _logger.LogInformation(
            "Seed phase changed: SeedId={SeedId}, {PreviousPhase} -> {NewPhase}, Progressed={Progressed}",
            notification.SeedId, notification.PreviousPhase, notification.NewPhase, notification.Progressed);

        // Phase changes may affect which scenarios are available.
        // The scenario selection algorithm checks seed phase on each evaluation.
        await Task.CompletedTask;
    }

    /// <summary>
    /// Called after capability manifest is recomputed. New capabilities may unlock
    /// additional scenario types or POI interactions.
    /// </summary>
    public async Task OnCapabilitiesChangedAsync(SeedCapabilityNotification notification, CancellationToken ct)
    {
        if (notification.SeedTypeCode != _configuration.SeedTypeCode)
        {
            return;
        }

        _logger.LogDebug(
            "Seed capabilities updated: SeedId={SeedId}, UnlockedCount={UnlockedCount}, Version={Version}",
            notification.SeedId, notification.UnlockedCount, notification.Version);

        // Capability data is read by the scenario selection algorithm on each evaluation.
        await Task.CompletedTask;
    }
}
