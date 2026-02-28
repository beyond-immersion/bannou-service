using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Seed;

/// <summary>
/// Listens for collection entry unlock notifications and drives seed growth
/// by matching entry tags against seed type collection growth mappings.
/// </summary>
/// <remarks>
/// <para>
/// This is the Collection→Seed pipeline: when a collection entry is unlocked,
/// this listener finds the owner's seeds, checks their type definitions for
/// <c>collectionGrowthMappings</c>, matches entry tags against tag prefixes,
/// and records growth via <see cref="ISeedClient"/> to reuse the full growth
/// pipeline (distributed locks, phase transitions, cross-pollination,
/// capability cache invalidation, evolution listener dispatch).
/// </para>
/// <para>
/// Registered as Singleton via DI. Collection discovers it via
/// <c>IEnumerable&lt;ICollectionUnlockListener&gt;</c> and calls it during GrantEntryAsync.
/// </para>
/// <para>
/// <b>Distributed safety</b>: This is a DI Listener (local-only fan-out).
/// Growth recording writes to distributed state via <see cref="ISeedClient"/>,
/// so the result is visible on all nodes regardless of which node processed
/// the collection grant. See SERVICE-HIERARCHY.md § DI Provider vs Listener.
/// </para>
/// </remarks>
public class SeedCollectionUnlockListener : ICollectionUnlockListener
{
    private readonly IQueryableStateStore<SeedModel> _seedStore;
    private readonly IStateStore<SeedTypeDefinitionModel> _typeStore;
    private readonly ISeedClient _seedClient;
    private readonly IMessageBus _messageBus;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<SeedCollectionUnlockListener> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeedCollectionUnlockListener"/> class.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for seed data access.</param>
    /// <param name="seedClient">Generated seed client for growth recording via mesh.</param>
    /// <param name="messageBus">Message bus for error event publishing.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    /// <param name="logger">Logger instance.</param>
    public SeedCollectionUnlockListener(
        IStateStoreFactory stateStoreFactory,
        ISeedClient seedClient,
        IMessageBus messageBus,
        ITelemetryProvider telemetryProvider,
        ILogger<SeedCollectionUnlockListener> logger)
    {
        _seedStore = stateStoreFactory.GetQueryableStore<SeedModel>(StateStoreDefinitions.Seed);
        _typeStore = stateStoreFactory.GetStore<SeedTypeDefinitionModel>(StateStoreDefinitions.SeedTypeDefinitions);
        _seedClient = seedClient;
        _messageBus = messageBus;
        _telemetryProvider = telemetryProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task OnEntryUnlockedAsync(CollectionUnlockNotification notification, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.seed", "SeedCollectionUnlockListener.OnEntryUnlocked");

        if (notification.Tags == null || notification.Tags.Count == 0)
        {
            _logger.LogDebug(
                "Collection entry {EntryCode} has no tags, skipping seed growth mapping",
                notification.EntryCode);
            return;
        }

        // Find all active seeds owned by this entity
        var ownerSeeds = await _seedStore.QueryAsync(
            s => s.OwnerId == notification.OwnerId
                && s.OwnerType == notification.OwnerType
                && s.GameServiceId == notification.GameServiceId
                && s.Status == SeedStatus.Active,
            ct);

        if (ownerSeeds.Count == 0)
        {
            _logger.LogDebug(
                "No active seeds found for {OwnerType} {OwnerId}, skipping growth",
                notification.OwnerType, notification.OwnerId);
            return;
        }

        foreach (var seed in ownerSeeds)
        {
            var seedType = await _typeStore.GetAsync(
                SeedService.TypeKey(seed.GameServiceId, seed.SeedTypeCode), ct);

            if (seedType?.CollectionGrowthMappings == null) continue;

            // Find mappings for this collection type
            var typeMapping = seedType.CollectionGrowthMappings
                .FirstOrDefault(m => m.CollectionType == notification.CollectionType);

            if (typeMapping == null) continue;

            // Match entry tags against domain mappings
            var growthEntries = new List<GrowthEntry>();

            foreach (var domainMapping in typeMapping.DomainMappings)
            {
                // Check if any tag starts with the prefix
                var matchingTags = notification.Tags
                    .Where(t => t.StartsWith(domainMapping.TagPrefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matchingTags.Count == 0) continue;

                // Calculate growth amount: base + discovery bonus
                var amount = domainMapping.BaseAmount;
                if (domainMapping.DiscoveryBonusPerLevel.HasValue && notification.DiscoveryLevel > 0)
                {
                    amount += domainMapping.DiscoveryBonusPerLevel.Value * notification.DiscoveryLevel;
                }

                // Extract domain from tag (part after prefix, or prefix itself if tag == prefix)
                foreach (var tag in matchingTags)
                {
                    var domain = tag.Length > domainMapping.TagPrefix.Length
                        ? tag[domainMapping.TagPrefix.Length..]
                        : domainMapping.TagPrefix.TrimEnd(':');

                    growthEntries.Add(new GrowthEntry { Domain = domain, Amount = amount });
                }
            }

            if (growthEntries.Count == 0) continue;

            _logger.LogInformation(
                "Recording collection-driven growth for seed {SeedId} ({SeedType}): {DomainCount} domains from entry {EntryCode}",
                seed.SeedId, seed.SeedTypeCode, growthEntries.Count, notification.EntryCode);

            // Record growth via ISeedClient — reuses full pipeline: distributed locks,
            // phase transitions, cross-pollination, capability cache invalidation,
            // evolution listener dispatch, and TotalGrowth update
            try
            {
                await _seedClient.RecordGrowthBatchAsync(new RecordGrowthBatchRequest
                {
                    SeedId = seed.SeedId,
                    Entries = growthEntries,
                    Source = $"collection:{notification.CollectionType}:{notification.EntryCode}"
                }, ct);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex,
                    "Seed growth recording failed for seed {SeedId} from collection unlock (status {Status})",
                    seed.SeedId, ex.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to record collection-driven growth for seed {SeedId}",
                    seed.SeedId);

                await _messageBus.TryPublishErrorAsync(
                    "seed", "SeedCollectionUnlockListener", "growth_recording_failed",
                    ex.Message, dependency: null, endpoint: "listener:collection-unlock",
                    details: null, stack: ex.StackTrace, cancellationToken: ct);
            }
        }
    }
}
