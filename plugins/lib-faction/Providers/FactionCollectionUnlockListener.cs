// =============================================================================
// Faction Collection Unlock Listener
// Converts member activity collection unlocks into faction seed growth.
// Part of the Collection->Seed growth pipeline for faction entities.
// =============================================================================

using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Faction.Providers;

/// <summary>
/// Listens for collection entry unlock notifications and records growth
/// to faction seeds when member activities contribute to faction development.
/// </summary>
/// <remarks>
/// <para>
/// This listener bridges member activities (tracked via Collection) to faction seed growth.
/// When a character who is a faction member unlocks a collection entry with matching tags,
/// this listener finds the character's factions and records growth to their seeds.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - DI Listener:</b> ICollectionUnlockListener is a local-only fan-out
/// mechanism. Reactions write to distributed state (Redis/MySQL) via ISeedClient, ensuring
/// other nodes see updates.
/// </para>
/// <para>
/// Registered as Singleton via DI. Collection discovers it via
/// <c>IEnumerable&lt;ICollectionUnlockListener&gt;</c> and calls it during GrantEntryAsync.
/// </para>
/// </remarks>
public class FactionCollectionUnlockListener : ICollectionUnlockListener
{
    /// <summary>Durable store for per-entity membership list aggregates (MySQL).</summary>
    private readonly IStateStore<MembershipListModel> _memberListStore;

    /// <summary>Durable store for faction entity records (MySQL).</summary>
    private readonly IStateStore<FactionModel> _factionStore;
    private readonly ISeedClient _seedClient;
    private readonly FactionServiceConfiguration _configuration;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<FactionCollectionUnlockListener> _logger;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="FactionCollectionUnlockListener"/> class.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for membership lookups.</param>
    /// <param name="seedClient">Seed client for recording growth.</param>
    /// <param name="configuration">Faction configuration for seed type code.</param>
    /// <param name="messageBus">Message bus for error publishing.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    public FactionCollectionUnlockListener(
        IStateStoreFactory stateStoreFactory,
        ISeedClient seedClient,
        FactionServiceConfiguration configuration,
        IMessageBus messageBus,
        ILogger<FactionCollectionUnlockListener> logger,
        ITelemetryProvider telemetryProvider)
    {
        _memberListStore = stateStoreFactory.GetStore<MembershipListModel>(StateStoreDefinitions.FactionMembership);
        _factionStore = stateStoreFactory.GetStore<FactionModel>(StateStoreDefinitions.Faction);
        _seedClient = seedClient;
        _configuration = configuration;
        _messageBus = messageBus;
        _logger = logger;
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    public async Task OnEntryUnlockedAsync(CollectionUnlockNotification notification, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.faction", "FactionCollectionUnlockListener.OnEntryUnlockedAsync");
        // Only process character-owned collection unlocks (faction growth comes from member activity)
        if (notification.OwnerType != EntityType.Character)
        {
            return;
        }

        if (notification.Tags == null || notification.Tags.Count == 0)
        {
            return;
        }

        // Check if any tag starts with "faction:" prefix indicating faction-relevant activity
        var factionTags = notification.Tags
            .Where(t => t.StartsWith("faction:", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (factionTags.Count == 0)
        {
            return;
        }

        // Look up the character's faction memberships
        var membershipList = await _memberListStore.GetAsync(
            FactionService.CharacterMembershipsKey(notification.OwnerId), ct);

        if (membershipList == null || membershipList.Memberships.Count == 0)
        {
            _logger.LogDebug(
                "Character {CharacterId} has no faction memberships, skipping faction growth from collection unlock",
                notification.OwnerId);
            return;
        }

        // For each faction the character belongs to, record growth from the collection unlock
        foreach (var membership in membershipList.Memberships)
        {
            var faction = await _factionStore.GetAsync(FactionService.FactionKey(membership.FactionId), ct);
            if (faction == null || faction.SeedId == null)
            {
                continue;
            }

            // Extract domain from faction tags (e.g., "faction:culture" -> domain "culture")
            var growthEntries = new Dictionary<string, float>();
            foreach (var tag in factionTags)
            {
                var domain = tag.Length > "faction:".Length
                    ? tag["faction:".Length..]
                    : "general";
                // Growth amount per collection unlock, from configuration
                growthEntries[domain] = growthEntries.GetValueOrDefault(domain, 0f) + _configuration.CollectionGrowthAmount;
            }

            try
            {
                await _seedClient.RecordGrowthBatchAsync(new RecordGrowthBatchRequest
                {
                    SeedId = faction.SeedId.Value,
                    Entries = growthEntries.Select(kvp => new GrowthEntry
                    {
                        Domain = kvp.Key,
                        Amount = kvp.Value,
                    }).ToList(),
                    Source = "collection",
                }, ct);

                _logger.LogDebug(
                    "Recorded faction growth for faction {FactionId} seed {SeedId} from character {CharacterId} collection unlock: {DomainCount} domains",
                    faction.FactionId, faction.SeedId, notification.OwnerId, growthEntries.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to record faction growth for faction {FactionId} seed {SeedId}",
                    faction.FactionId, faction.SeedId);

                await _messageBus.TryPublishErrorAsync(
                    "faction", "FactionCollectionUnlockListener", ex.GetType().Name,
                    ex.Message, dependency: "seed", endpoint: "listener:collection-unlock",
                    details: null, stack: ex.StackTrace, cancellationToken: ct);
            }
        }
    }
}
