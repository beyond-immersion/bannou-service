using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;

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
/// and records growth to the corresponding domains.
/// </para>
/// <para>
/// Registered as Singleton via DI. Collection discovers it via
/// <c>IEnumerable&lt;ICollectionUnlockListener&gt;</c> and calls it during GrantEntryAsync.
/// </para>
/// </remarks>
public class SeedCollectionUnlockListener : ICollectionUnlockListener
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<SeedCollectionUnlockListener> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeedCollectionUnlockListener"/> class.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for seed data access.</param>
    /// <param name="messageBus">Message bus for error event publishing.</param>
    /// <param name="logger">Logger instance.</param>
    public SeedCollectionUnlockListener(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        ILogger<SeedCollectionUnlockListener> logger)
    {
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task OnEntryUnlockedAsync(CollectionUnlockNotification notification, CancellationToken ct)
    {
        if (notification.Tags == null || notification.Tags.Count == 0)
        {
            _logger.LogDebug(
                "Collection entry {EntryCode} has no tags, skipping seed growth mapping",
                notification.EntryCode);
            return;
        }

        // Find all seeds owned by this entity
        var seedStore = _stateStoreFactory.GetQueryableStore<SeedModel>(StateStoreDefinitions.Seed);
        var ownerSeeds = await seedStore.QueryAsync(
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

        // Load seed type definitions to get collection growth mappings
        var typeStore = _stateStoreFactory.GetStore<SeedTypeDefinitionModel>(StateStoreDefinitions.SeedTypeDefinitions);

        foreach (var seed in ownerSeeds)
        {
            var seedType = await typeStore.GetAsync(
                $"type:{seed.GameServiceId}:{seed.SeedTypeCode}", ct);

            if (seedType?.CollectionGrowthMappings == null) continue;

            // Find mappings for this collection type
            var typeMapping = seedType.CollectionGrowthMappings
                .FirstOrDefault(m => m.CollectionType == notification.CollectionType);

            if (typeMapping == null) continue;

            // Match entry tags against domain mappings
            var growthEntries = new List<(string Domain, float Amount)>();

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

                    growthEntries.Add((domain, amount));
                }
            }

            if (growthEntries.Count == 0) continue;

            _logger.LogInformation(
                "Recording collection-driven growth for seed {SeedId} ({SeedType}): {DomainCount} domains from entry {EntryCode}",
                seed.SeedId, seed.SeedTypeCode, growthEntries.Count, notification.EntryCode);

            // Record growth via the seed's RecordGrowth API (reuse existing infrastructure)
            try
            {
                // In-process: directly use the seed service's growth state store
                var growthStore = _stateStoreFactory.GetStore<SeedGrowthModel>(StateStoreDefinitions.SeedGrowth);
                var growth = await growthStore.GetAsync($"growth:{seed.SeedId}", ct);
                growth ??= new SeedGrowthModel { SeedId = seed.SeedId, Domains = new() };

                foreach (var (domain, amount) in growthEntries)
                {
                    if (!growth.Domains.ContainsKey(domain))
                    {
                        growth.Domains[domain] = new DomainGrowthEntry();
                    }

                    growth.Domains[domain].Depth += amount;
                    growth.Domains[domain].LastActivityAt = DateTimeOffset.UtcNow;
                }

                var totalGrowth = growth.Domains.Values.Sum(d => d.Depth);
                await growthStore.SaveAsync($"growth:{seed.SeedId}", growth, cancellationToken: ct);

                // Publish per-domain growth events
                foreach (var (domain, amount) in growthEntries)
                {
                    await _messageBus.TryPublishAsync(
                        "seed.growth.updated",
                        new Events.SeedGrowthUpdatedEvent
                        {
                            EventId = Guid.NewGuid(),
                            Timestamp = DateTimeOffset.UtcNow,
                            SeedId = seed.SeedId,
                            SeedTypeCode = seed.SeedTypeCode,
                            Domain = domain,
                            PreviousDepth = growth.Domains[domain].Depth - amount,
                            NewDepth = growth.Domains[domain].Depth,
                            TotalGrowth = totalGrowth,
                            CrossPollinated = false
                        },
                        ct);
                }
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
