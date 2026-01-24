// =============================================================================
// Actor-Local Memory Store
// MVP implementation using lib-state for actor-local memory storage.
// Stores memories per-entity with keyword-based relevance matching.
// =============================================================================

using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.Bannou.Behavior.Cognition;

/// <summary>
/// Actor-local memory store using lib-state with keyword-based relevance matching.
/// </summary>
/// <remarks>
/// <para>
/// <b>MVP Status Acknowledged</b>: This implementation uses keyword matching for relevance
/// scoring. This is appropriate for structured game data (categories, entity IDs, metadata keys)
/// where terminology is consistent. See ACTOR_SYSTEM.md section 7.3 for migration criteria.
/// </para>
/// <para>
/// <b>When keyword matching works well</b>:
/// <list type="bullet">
/// <item>Game-defined perception categories ("threat", "social", "routine")</item>
/// <item>Entity-based relationships (entity IDs in metadata)</item>
/// <item>Structured events where NPCs write their own memories</item>
/// </list>
/// </para>
/// <para>
/// <b>Migration path</b>: The <see cref="IMemoryStore"/> interface is designed for swappable
/// implementations. An embedding-based store can be created without changes to the cognition
/// pipeline.
/// </para>
/// </remarks>
public sealed class ActorLocalMemoryStore : IMemoryStore
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly BeyondImmersion.BannouService.Behavior.BehaviorServiceConfiguration _configuration;
    private readonly ILogger<ActorLocalMemoryStore> _logger;

    // Store name and key prefixes now come from configuration

    /// <summary>
    /// Creates a new actor-local memory store.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for Redis/in-memory storage.</param>
    /// <param name="configuration">Behavior service configuration.</param>
    /// <param name="logger">Logger instance.</param>
    public ActorLocalMemoryStore(
        IStateStoreFactory stateStoreFactory,
        BeyondImmersion.BannouService.Behavior.BehaviorServiceConfiguration configuration,
        ILogger<ActorLocalMemoryStore> logger)
    {
        _stateStoreFactory = stateStoreFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Memory>> FindRelevantAsync(
        string entityId,
        IReadOnlyList<Perception> perceptions,
        int limit,
        CancellationToken ct)
    {

        if (perceptions.Count == 0 || limit <= 0)
        {
            return [];
        }

        _logger.LogDebug(
            "Finding relevant memories for entity {EntityId} with {PerceptionCount} perceptions",
            entityId, perceptions.Count);

        // Get all memories for this entity
        var allMemories = await GetAllAsync(entityId, _configuration.DefaultMemoryLimit, ct);

        if (allMemories.Count == 0)
        {
            return [];
        }

        // Extract keywords from perceptions for matching
        var perceptionKeywords = ExtractKeywords(perceptions);

        // Score each memory by keyword overlap and recency
        // Filter by minimum threshold to avoid weakly-related memories
        var scoredMemories = allMemories
            .Select(memory => new
            {
                Memory = memory,
                Score = ComputeRelevanceScore(memory, perceptionKeywords)
            })
            .Where(x => x.Score >= CognitionConstants.MemoryMinimumRelevanceThreshold)
            .OrderByDescending(x => x.Score)
            .Take(limit)
            .ToList();

        // Set the QueryRelevance property on returned memories
        foreach (var item in scoredMemories)
        {
            item.Memory.QueryRelevance = item.Score;
        }

        _logger.LogDebug(
            "Found {Count} relevant memories for entity {EntityId}",
            scoredMemories.Count, entityId);

        return scoredMemories.Select(x => x.Memory).ToList();
    }

    /// <inheritdoc/>
    public async Task StoreExperienceAsync(
        string entityId,
        Perception perception,
        float significance,
        IReadOnlyList<Memory> context,
        CancellationToken ct)
    {

        var memoryId = Guid.NewGuid().ToString();

        var memory = new Memory
        {
            Id = memoryId,
            EntityId = entityId,
            Content = perception.Content,
            Category = perception.Category,
            Significance = significance,
            Timestamp = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, object>(perception.Data),
            RelatedMemoryIds = context.Select(m => m.Id).ToList()
        };

        _logger.LogDebug(
            "Storing memory {MemoryId} for entity {EntityId} with significance {Significance}",
            memoryId, entityId, significance);

        // Store the memory data
        var memoryStore = _stateStoreFactory.GetStore<Memory>(StateStoreDefinitions.AgentMemories);
        var memoryKey = BuildMemoryKey(entityId, memoryId);
        await memoryStore.SaveAsync(memoryKey, memory, cancellationToken: ct);

        // Add to the entity's memory index
        await AddToMemoryIndexAsync(entityId, memoryId, ct);

        _logger.LogInformation(
            "Stored memory {MemoryId} for entity {EntityId}",
            memoryId, entityId);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Memory>> GetAllAsync(
        string entityId,
        int limit,
        CancellationToken ct)
    {

        if (limit <= 0)
        {
            return [];
        }

        // Get the memory index for this entity
        var indexStore = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.AgentMemories);
        var indexKey = BuildMemoryIndexKey(entityId);
        var memoryIds = await indexStore.GetAsync(indexKey, ct) ?? [];

        if (memoryIds.Count == 0)
        {
            return [];
        }

        // Take only up to limit IDs (most recent first - assuming IDs are appended)
        var idsToFetch = memoryIds.TakeLast(limit).ToList();

        // Bulk fetch the memories
        var memoryStore = _stateStoreFactory.GetStore<Memory>(StateStoreDefinitions.AgentMemories);
        var memoryKeys = idsToFetch.Select(id => BuildMemoryKey(entityId, id));
        var memoriesDict = await memoryStore.GetBulkAsync(memoryKeys, ct);

        // Return in order (most recent last)
        var memories = idsToFetch
            .Select(id => BuildMemoryKey(entityId, id))
            .Where(key => memoriesDict.ContainsKey(key))
            .Select(key => memoriesDict[key])
            .ToList();

        _logger.LogDebug(
            "Retrieved {Count} memories for entity {EntityId}",
            memories.Count, entityId);

        return memories;
    }

    /// <inheritdoc/>
    public async Task RemoveAsync(
        string entityId,
        string memoryId,
        CancellationToken ct)
    {

        _logger.LogDebug(
            "Removing memory {MemoryId} for entity {EntityId}",
            memoryId, entityId);

        // Delete the memory data
        var memoryStore = _stateStoreFactory.GetStore<Memory>(StateStoreDefinitions.AgentMemories);
        var memoryKey = BuildMemoryKey(entityId, memoryId);
        await memoryStore.DeleteAsync(memoryKey, ct);

        // Remove from the entity's memory index
        await RemoveFromMemoryIndexAsync(entityId, memoryId, ct);

        _logger.LogInformation(
            "Removed memory {MemoryId} for entity {EntityId}",
            memoryId, entityId);
    }

    /// <inheritdoc/>
    public async Task ClearAsync(string entityId, CancellationToken ct)
    {

        _logger.LogDebug("Clearing all memories for entity {EntityId}", entityId);

        // Get the memory index
        var indexStore = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.AgentMemories);
        var indexKey = BuildMemoryIndexKey(entityId);
        var memoryIds = await indexStore.GetAsync(indexKey, ct) ?? [];

        // Delete all memory data
        var memoryStore = _stateStoreFactory.GetStore<Memory>(StateStoreDefinitions.AgentMemories);
        foreach (var memoryId in memoryIds)
        {
            var memoryKey = BuildMemoryKey(entityId, memoryId);
            await memoryStore.DeleteAsync(memoryKey, ct);
        }

        // Clear the index
        await indexStore.DeleteAsync(indexKey, ct);

        _logger.LogInformation(
            "Cleared {Count} memories for entity {EntityId}",
            memoryIds.Count, entityId);
    }

    #region Helper Methods

    private string BuildMemoryKey(string entityId, string memoryId)
        => $"{_configuration.MemoryKeyPrefix}{entityId}:{memoryId}";

    private string BuildMemoryIndexKey(string entityId)
        => $"{_configuration.MemoryIndexKeyPrefix}{entityId}";

    /// <summary>
    /// Adds a memory ID to the entity's memory index with optimistic concurrency.
    /// Evicts oldest memories when index exceeds DefaultMemoryLimit.
    /// </summary>
    private async Task AddToMemoryIndexAsync(string entityId, string memoryId, CancellationToken ct)
    {
        var indexKey = BuildMemoryIndexKey(entityId);
        var store = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.AgentMemories);
        List<string> evictedIds = [];

        for (int retry = 0; retry < _configuration.MemoryStoreMaxRetries; retry++)
        {
            var (currentIndex, etag) = await store.GetWithETagAsync(indexKey, ct);
            var index = currentIndex ?? [];

            // Add the new memory ID (appending maintains chronological order)
            index.Add(memoryId);

            // Evict oldest entries if over capacity
            evictedIds = [];
            if (index.Count > _configuration.DefaultMemoryLimit)
            {
                var excessCount = index.Count - _configuration.DefaultMemoryLimit;
                evictedIds = index.GetRange(0, excessCount);
                index.RemoveRange(0, excessCount);
            }

            // Try to save with ETag for concurrency safety
            if (etag != null)
            {
                if (await store.TrySaveAsync(indexKey, index, etag, ct) != null)
                {
                    break; // Success - proceed to cleanup
                }
                // ETag mismatch - retry
            }
            else
            {
                // First entry - no ETag needed
                await store.SaveAsync(indexKey, index, cancellationToken: ct);
                break;
            }

            if (retry == _configuration.MemoryStoreMaxRetries - 1)
            {
                // Final attempt without concurrency check
                _logger.LogWarning(
                    "Memory index update for entity {EntityId} retries exhausted, forcing save",
                    entityId);

                var finalIndex = await store.GetAsync(indexKey, ct) ?? [];
                if (!finalIndex.Contains(memoryId))
                {
                    finalIndex.Add(memoryId);
                }
                evictedIds = [];
                if (finalIndex.Count > _configuration.DefaultMemoryLimit)
                {
                    var excessCount = finalIndex.Count - _configuration.DefaultMemoryLimit;
                    evictedIds = finalIndex.GetRange(0, excessCount);
                    finalIndex.RemoveRange(0, excessCount);
                }
                await store.SaveAsync(indexKey, finalIndex, cancellationToken: ct);
            }
        }

        // Clean up evicted memory records (best-effort)
        if (evictedIds.Count > 0)
        {
            var memoryStore = _stateStoreFactory.GetStore<Memory>(StateStoreDefinitions.AgentMemories);
            foreach (var evictedId in evictedIds)
            {
                try
                {
                    await memoryStore.DeleteAsync(BuildMemoryKey(entityId, evictedId), ct);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to clean up evicted memory {MemoryId} for entity {EntityId}",
                        evictedId, entityId);
                }
            }

            _logger.LogDebug("Evicted {Count} oldest memories for entity {EntityId}", evictedIds.Count, entityId);
        }
    }

    /// <summary>
    /// Removes a memory ID from the entity's memory index with optimistic concurrency.
    /// </summary>
    private async Task RemoveFromMemoryIndexAsync(string entityId, string memoryId, CancellationToken ct)
    {
        var indexKey = BuildMemoryIndexKey(entityId);
        var store = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.AgentMemories);

        for (int retry = 0; retry < _configuration.MemoryStoreMaxRetries; retry++)
        {
            var (currentIndex, etag) = await store.GetWithETagAsync(indexKey, ct);
            if (currentIndex == null || !currentIndex.Contains(memoryId))
            {
                return; // Nothing to remove
            }

            currentIndex.Remove(memoryId);

            if (etag != null)
            {
                if (await store.TrySaveAsync(indexKey, currentIndex, etag, ct) != null)
                {
                    return; // Success
                }
                // ETag mismatch - retry
            }
            else
            {
                await store.SaveAsync(indexKey, currentIndex, cancellationToken: ct);
                return;
            }
        }

        // Final attempt without concurrency check
        _logger.LogWarning(
            "Memory index removal for entity {EntityId} retries exhausted, forcing save",
            entityId);

        var finalIndex = await store.GetAsync(indexKey, ct);
        if (finalIndex != null && finalIndex.Remove(memoryId))
        {
            await store.SaveAsync(indexKey, finalIndex, cancellationToken: ct);
        }
    }

    /// <summary>
    /// Extracts keywords from perceptions for relevance matching.
    /// </summary>
    private static HashSet<string> ExtractKeywords(IReadOnlyList<Perception> perceptions)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var perception in perceptions)
        {
            // Add category as keyword
            if (!string.IsNullOrEmpty(perception.Category))
            {
                keywords.Add(perception.Category);
            }

            // Add source as keyword
            if (!string.IsNullOrEmpty(perception.Source))
            {
                keywords.Add(perception.Source);
            }

            // Extract words from content (simple tokenization)
            if (!string.IsNullOrEmpty(perception.Content))
            {
                var words = perception.Content
                    .Split([' ', ',', '.', '!', '?', ';', ':'], StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length > 3); // Skip short words

                foreach (var word in words)
                {
                    keywords.Add(word);
                }
            }

            // Add data keys as keywords (they often indicate important entities/concepts)
            foreach (var key in perception.Data.Keys)
            {
                keywords.Add(key);
            }
        }

        return keywords;
    }

    /// <summary>
    /// Computes relevance score for a memory based on keyword overlap and recency.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keyword-based scoring is appropriate for structured game data where terminology
    /// is consistent. For semantic similarity (e.g., player-generated content), consider
    /// an embedding-based <see cref="IMemoryStore"/> implementation.
    /// </para>
    /// <para>
    /// Score components (see <see cref="CognitionConstants"/> for weights):
    /// <list type="bullet">
    /// <item>Category match: If memory category matches any perception keyword</item>
    /// <item>Content overlap: Ratio of shared keywords between perception and memory content</item>
    /// <item>Metadata overlap: Ratio of shared keys between perception data and memory metadata</item>
    /// <item>Recency bonus: Memories less than 1 hour old get a recency boost</item>
    /// <item>Significance bonus: Higher significance memories score higher</item>
    /// </list>
    /// </para>
    /// </remarks>
    private static float ComputeRelevanceScore(Memory memory, HashSet<string> keywords)
    {
        var score = 0f;

        // Category match
        if (keywords.Contains(memory.Category))
        {
            score += CognitionConstants.MemoryCategoryMatchWeight;
        }

        // Content keyword overlap
        if (!string.IsNullOrEmpty(memory.Content))
        {
            var memoryWords = memory.Content
                .Split([' ', ',', '.', '!', '?', ';', ':'], StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 3)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var overlap = keywords.Count(k => memoryWords.Contains(k));
            if (memoryWords.Count > 0)
            {
                score += CognitionConstants.MemoryContentOverlapWeight *
                    (overlap / (float)Math.Max(keywords.Count, memoryWords.Count));
            }
        }

        // Metadata key overlap
        var metadataKeyOverlap = keywords.Count(k => memory.Metadata.ContainsKey(k));
        if (memory.Metadata.Count > 0)
        {
            score += CognitionConstants.MemoryMetadataOverlapWeight *
                (metadataKeyOverlap / (float)Math.Max(keywords.Count, memory.Metadata.Count));
        }

        // Recency bonus (memories from last hour get boost)
        var ageHours = (DateTimeOffset.UtcNow - memory.Timestamp).TotalHours;
        if (ageHours < 1)
        {
            score += CognitionConstants.MemoryRecencyBonusWeight * (1f - (float)ageHours);
        }

        // Significance bonus
        score += CognitionConstants.MemorySignificanceBonusWeight * memory.Significance;

        return score;
    }

    #endregion
}
