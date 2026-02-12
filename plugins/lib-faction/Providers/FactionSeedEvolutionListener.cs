// =============================================================================
// Faction Seed Evolution Listener
// Receives seed growth/phase/capability notifications for faction seeds.
// Updates faction's currentPhase and status based on seed evolution.
// =============================================================================

using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Faction.Providers;

/// <summary>
/// Listens for seed evolution notifications and updates faction state accordingly.
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPLEMENTATION TENETS - DI Listener:</b> ISeedEvolutionListener is a local-only fan-out
/// mechanism. Reactions write to distributed state (MySQL), ensuring other nodes see updates.
/// </para>
/// <para>
/// Registered as Singleton via DI. The Seed service discovers it via
/// <c>IEnumerable&lt;ISeedEvolutionListener&gt;</c> and dispatches notifications during growth/phase changes.
/// </para>
/// <para>
/// This is a separate class from FactionService (which is Scoped) because ISeedEvolutionListener
/// must be resolvable from singleton context (BackgroundService workers).
/// Follows the same pattern as GardenerSeedEvolutionListener.
/// </para>
/// </remarks>
public class FactionSeedEvolutionListener : ISeedEvolutionListener
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<FactionSeedEvolutionListener> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FactionSeedEvolutionListener"/> class.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for faction data access.</param>
    /// <param name="configuration">Faction configuration for seed type code.</param>
    /// <param name="messageBus">Message bus for event publishing.</param>
    /// <param name="logger">Logger instance.</param>
    public FactionSeedEvolutionListener(
        IStateStoreFactory stateStoreFactory,
        FactionServiceConfiguration configuration,
        IMessageBus messageBus,
        ILogger<FactionSeedEvolutionListener> logger)
    {
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _logger = logger;
        InterestedSeedTypes = new HashSet<string> { configuration.SeedTypeCode };
    }

    /// <inheritdoc />
    public IReadOnlySet<string> InterestedSeedTypes { get; }

    /// <summary>
    /// Called when growth is recorded for a faction seed.
    /// Publishes a faction updated event reflecting the growth change.
    /// </summary>
    public async Task OnGrowthRecordedAsync(SeedGrowthNotification notification, CancellationToken ct)
    {
        // Faction seeds are owned by factions (ownerType = "faction")
        if (!string.Equals(notification.OwnerType, "faction", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _logger.LogDebug(
            "Growth recorded for faction seed {SeedId}, faction {FactionId}, total growth {TotalGrowth}",
            notification.SeedId, notification.OwnerId, notification.TotalGrowth);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Called when a faction seed changes phase.
    /// Updates the faction's cached currentPhase and publishes an updated event.
    /// </summary>
    public async Task OnPhaseChangedAsync(SeedPhaseNotification notification, CancellationToken ct)
    {
        if (!string.Equals(notification.OwnerType, "faction", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var factionStore = _stateStoreFactory.GetStore<FactionModel>(
            StateStoreDefinitions.Faction);
        var factionKey = $"fac:{notification.OwnerId}";

        var faction = await factionStore.GetAsync(factionKey, ct);
        if (faction == null)
        {
            _logger.LogWarning(
                "Faction {FactionId} not found during phase change notification for seed {SeedId}",
                notification.OwnerId, notification.SeedId);
            return;
        }

        faction.CurrentPhase = notification.NewPhase;
        faction.UpdatedAt = DateTimeOffset.UtcNow;
        await factionStore.SaveAsync(factionKey, faction, cancellationToken: ct);

        _logger.LogInformation(
            "Updated faction {FactionId} phase from {OldPhase} to {NewPhase} for seed {SeedId}",
            notification.OwnerId, notification.PreviousPhase, notification.NewPhase, notification.SeedId);

        await _messageBus.TryPublishAsync(
            "faction.updated",
            new FactionUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                FactionId = faction.FactionId,
                GameServiceId = faction.GameServiceId,
                Name = faction.Name,
                Code = faction.Code,
                RealmId = faction.RealmId,
                IsRealmBaseline = faction.IsRealmBaseline,
                ParentFactionId = faction.ParentFactionId.GetValueOrDefault(),
                SeedId = faction.SeedId.GetValueOrDefault(),
                Status = faction.Status,
                CurrentPhase = notification.NewPhase,
                MemberCount = faction.MemberCount,
                CreatedAt = faction.CreatedAt,
                UpdatedAt = faction.UpdatedAt
            },
            ct);
    }

    /// <summary>
    /// Called when capabilities change for a faction seed.
    /// Capability changes may unlock governance abilities (norm.define, territory.claim, etc.).
    /// This notification is informational; capability checks happen at operation time via ISeedClient.
    /// </summary>
    public async Task OnCapabilitiesChangedAsync(SeedCapabilityNotification notification, CancellationToken ct)
    {
        if (!string.Equals(notification.OwnerType, "faction", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _logger.LogInformation(
            "Capabilities updated for faction {FactionId} seed {SeedId}: {UnlockedCount} unlocked (version {Version})",
            notification.OwnerId, notification.SeedId, notification.UnlockedCount, notification.Version);

        await Task.CompletedTask;
    }
}
