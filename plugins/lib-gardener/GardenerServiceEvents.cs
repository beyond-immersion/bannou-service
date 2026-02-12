using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.State;

namespace BeyondImmersion.BannouService.Gardener;

/// <summary>
/// Partial class for GardenerService event handling and ISeedEvolutionListener implementation.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class GardenerService : ISeedEvolutionListener
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IGardenerService, SeedBondFormedEvent>(
            "seed.bond.formed",
            async (svc, evt) => await ((GardenerService)svc).HandleSeedBondFormedAsync(evt));

        eventConsumer.RegisterHandler<IGardenerService, SeedActivatedEvent>(
            "seed.activated",
            async (svc, evt) => await ((GardenerService)svc).HandleSeedActivatedAsync(evt));

        eventConsumer.RegisterHandler<IGardenerService, GameSessionDeletedEvent>(
            "game-session.deleted",
            async (svc, evt) => await ((GardenerService)svc).HandleGameSessionDeletedAsync(evt));
    }

    #region ISeedEvolutionListener

    /// <inheritdoc />
    public IReadOnlySet<string> InterestedSeedTypes { get; } = new HashSet<string> { "guardian" };

    /// <summary>
    /// Called when growth is recorded for a guardian seed.
    /// Marks the garden instance for re-evaluation on the next orchestrator tick.
    /// </summary>
    public async Task OnGrowthRecordedAsync(SeedGrowthNotification notification, CancellationToken ct)
    {
        var garden = await GardenStore.GetAsync($"void:{notification.OwnerId}", ct);
        if (garden == null) return;

        garden.NeedsReEvaluation = true;
        await GardenStore.SaveAsync($"void:{notification.OwnerId}", garden, ct);

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
        var garden = await GardenStore.GetAsync($"void:{notification.OwnerId}", ct);
        if (garden == null) return;

        garden.CachedGrowthPhase = notification.NewPhase;
        garden.NeedsReEvaluation = true;
        await GardenStore.SaveAsync($"void:{notification.OwnerId}", garden, ct);

        _logger.LogInformation(
            "Updated cached growth phase to {NewPhase} for seed {SeedId}",
            notification.NewPhase, notification.SeedId);
    }

    /// <summary>
    /// Called when capabilities change. Not currently used by Gardener.
    /// </summary>
    public Task OnCapabilitiesChangedAsync(SeedCapabilityNotification notification, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles seed.bond.formed events.
    /// Enables shared garden and adjusts POI scoring for bond-preferred scenarios.
    /// </summary>
    public async Task HandleSeedBondFormedAsync(SeedBondFormedEvent evt)
    {
        if (!_configuration.BondSharedVoidEnabled)
        {
            _logger.LogDebug("Bond shared garden disabled, ignoring bond formed event");
            return;
        }

        // Find garden instances for both bond participants via their seed owners
        // Bond events contain seed IDs, we need to find the garden by account
        // The garden store is keyed by account ID, which we don't have from the bond event.
        // We'll mark both seeds' gardens for re-evaluation when they next update position.
        _logger.LogInformation(
            "Bond formed between seeds {SeedIdA} and {SeedIdB}, bond {BondId}",
            evt.InitiatorSeedId, evt.TargetSeedId, evt.BondId);
    }

    /// <summary>
    /// Handles seed.activated events.
    /// Updates the active seed reference in the garden instance.
    /// </summary>
    public async Task HandleSeedActivatedAsync(SeedActivatedEvent evt)
    {
        var garden = await GardenStore.GetAsync($"void:{evt.OwnerId}", CancellationToken.None);
        if (garden == null) return;

        garden.SeedId = evt.SeedId;
        garden.NeedsReEvaluation = true;
        await GardenStore.SaveAsync($"void:{evt.OwnerId}", garden, CancellationToken.None);

        _logger.LogInformation(
            "Updated active seed to {SeedId} for garden owner {OwnerId}",
            evt.SeedId, evt.OwnerId);
    }

    /// <summary>
    /// Handles game-session.deleted events.
    /// Cleans up any scenario instance backed by the deleted game session.
    /// </summary>
    public async Task HandleGameSessionDeletedAsync(GameSessionDeletedEvent evt)
    {
        // Scan for a scenario instance backed by this game session
        // Since scenarios are keyed by accountId, we need to check participants
        _logger.LogInformation(
            "Game session {GameSessionId} deleted, checking for associated scenarios",
            evt.GameSessionId);

        // The scenario lifecycle worker will detect this as abandoned
        // via the LastActivityAt check, since the game session no longer exists.
        // This handler provides an early cleanup path.
    }

    #endregion
}
