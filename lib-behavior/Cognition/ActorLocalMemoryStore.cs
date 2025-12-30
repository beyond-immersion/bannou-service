// =============================================================================
// Actor-Local Memory Store
// MVP implementation using lib-state for actor-local memory storage.
// Stores memories per-entity with keyword-based relevance matching.
// =============================================================================

using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.Bannou.Behavior.Cognition;

/// <summary>
/// Actor-local memory store using lib-state.
/// Stores memories in agent's state store with index-based retrieval.
/// This MVP implementation uses keyword matching for relevance.
/// The interface allows future migration to a dedicated Memory service with embeddings.
/// </summary>
public sealed class ActorLocalMemoryStore : IMemoryStore
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<ActorLocalMemoryStore> _logger;

    // Store name - must be configured in state store factory
    private const string MEMORY_STATE_STORE = "agent-memories";

    // Key prefixes for memory storage
    private const string MEMORY_KEY_PREFIX = "memory:";
    private const string MEMORY_INDEX_KEY_PREFIX = "memory-index:";

    // Default limits
    private const int DEFAULT_MEMORY_LIMIT = 100;
    private const int MAX_RETRIES = 3;

    /// <summary>
    /// Creates a new actor-local memory store.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for Redis/in-memory storage.</param>
    /// <param name="logger">Logger instance.</param>
    public ActorLocalMemoryStore(
        IStateStoreFactory stateStoreFactory,
        ILogger<ActorLocalMemoryStore> logger)
    {
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Memory>> FindRelevantAsync(
        string entityId,
        IReadOnlyList<Perception> perceptions,
        int limit,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entityId, nameof(entityId));
        ArgumentNullException.ThrowIfNull(perceptions, nameof(perceptions));

        if (perceptions.Count == 0 || limit <= 0)
        {
            return [];
        }

        _logger.LogDebug(
            "Finding relevant memories for entity {EntityId} with {PerceptionCount} perceptions",
            entityId, perceptions.Count);

        // Get all memories for this entity
        var allMemories = await GetAllAsync(entityId, DEFAULT_MEMORY_LIMIT, ct);

        if (allMemories.Count == 0)
        {
            return [];
        }

        // Extract keywords from perceptions for matching
        var perceptionKeywords = ExtractKeywords(perceptions);

        // Score each memory by keyword overlap and recency
        var scoredMemories = allMemories
            .Select(memory => new
            {
                Memory = memory,
                Score = ComputeRelevanceScore(memory, perceptionKeywords)
            })
            .Where(x => x.Score > 0)
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
        ArgumentNullException.ThrowIfNull(entityId, nameof(entityId));
        ArgumentNullException.ThrowIfNull(perception, nameof(perception));

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
        var memoryStore = _stateStoreFactory.GetStore<Memory>(MEMORY_STATE_STORE);
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
        ArgumentNullException.ThrowIfNull(entityId, nameof(entityId));

        if (limit <= 0)
        {
            return [];
        }

        // Get the memory index for this entity
        var indexStore = _stateStoreFactory.GetStore<List<string>>(MEMORY_STATE_STORE);
        var indexKey = BuildMemoryIndexKey(entityId);
        var memoryIds = await indexStore.GetAsync(indexKey, ct) ?? [];

        if (memoryIds.Count == 0)
        {
            return [];
        }

        // Take only up to limit IDs (most recent first - assuming IDs are appended)
        var idsToFetch = memoryIds.TakeLast(limit).ToList();

        // Bulk fetch the memories
        var memoryStore = _stateStoreFactory.GetStore<Memory>(MEMORY_STATE_STORE);
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
        ArgumentNullException.ThrowIfNull(entityId, nameof(entityId));
        ArgumentNullException.ThrowIfNull(memoryId, nameof(memoryId));

        _logger.LogDebug(
            "Removing memory {MemoryId} for entity {EntityId}",
            memoryId, entityId);

        // Delete the memory data
        var memoryStore = _stateStoreFactory.GetStore<Memory>(MEMORY_STATE_STORE);
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
        ArgumentNullException.ThrowIfNull(entityId, nameof(entityId));

        _logger.LogDebug("Clearing all memories for entity {EntityId}", entityId);

        // Get the memory index
        var indexStore = _stateStoreFactory.GetStore<List<string>>(MEMORY_STATE_STORE);
        var indexKey = BuildMemoryIndexKey(entityId);
        var memoryIds = await indexStore.GetAsync(indexKey, ct) ?? [];

        // Delete all memory data
        var memoryStore = _stateStoreFactory.GetStore<Memory>(MEMORY_STATE_STORE);
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

    private static string BuildMemoryKey(string entityId, string memoryId)
        => $"{MEMORY_KEY_PREFIX}{entityId}:{memoryId}";

    private static string BuildMemoryIndexKey(string entityId)
        => $"{MEMORY_INDEX_KEY_PREFIX}{entityId}";

    /// <summary>
    /// Adds a memory ID to the entity's memory index with optimistic concurrency.
    /// </summary>
    private async Task AddToMemoryIndexAsync(string entityId, string memoryId, CancellationToken ct)
    {
        var indexKey = BuildMemoryIndexKey(entityId);
        var store = _stateStoreFactory.GetStore<List<string>>(MEMORY_STATE_STORE);

        for (int retry = 0; retry < MAX_RETRIES; retry++)
        {
            var (currentIndex, etag) = await store.GetWithETagAsync(indexKey, ct);
            var index = currentIndex ?? [];

            // Add the new memory ID (appending maintains chronological order)
            index.Add(memoryId);

            // Try to save with ETag for concurrency safety
            if (etag != null)
            {
                if (await store.TrySaveAsync(indexKey, index, etag, ct))
                {
                    return; // Success
                }
                // ETag mismatch - retry
            }
            else
            {
                // First entry - no ETag needed
                await store.SaveAsync(indexKey, index, cancellationToken: ct);
                return;
            }
        }

        // Final attempt without concurrency check
        _logger.LogWarning(
            "Memory index update for entity {EntityId} retries exhausted, forcing save",
            entityId);

        var finalIndex = await store.GetAsync(indexKey, ct) ?? [];
        if (!finalIndex.Contains(memoryId))
        {
            finalIndex.Add(memoryId);
            await store.SaveAsync(indexKey, finalIndex, cancellationToken: ct);
        }
    }

    /// <summary>
    /// Removes a memory ID from the entity's memory index with optimistic concurrency.
    /// </summary>
    private async Task RemoveFromMemoryIndexAsync(string entityId, string memoryId, CancellationToken ct)
    {
        var indexKey = BuildMemoryIndexKey(entityId);
        var store = _stateStoreFactory.GetStore<List<string>>(MEMORY_STATE_STORE);

        for (int retry = 0; retry < MAX_RETRIES; retry++)
        {
            var (currentIndex, etag) = await store.GetWithETagAsync(indexKey, ct);
            if (currentIndex == null || !currentIndex.Contains(memoryId))
            {
                return; // Nothing to remove
            }

            currentIndex.Remove(memoryId);

            if (etag != null)
            {
                if (await store.TrySaveAsync(indexKey, currentIndex, etag, ct))
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
    private static float ComputeRelevanceScore(Memory memory, HashSet<string> keywords)
    {
        var score = 0f;

        // Category match (high weight)
        if (keywords.Contains(memory.Category))
        {
            score += 0.3f;
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
                score += 0.4f * (overlap / (float)Math.Max(keywords.Count, memoryWords.Count));
            }
        }

        // Metadata key overlap
        var metadataKeyOverlap = keywords.Count(k => memory.Metadata.ContainsKey(k));
        if (memory.Metadata.Count > 0)
        {
            score += 0.2f * (metadataKeyOverlap / (float)Math.Max(keywords.Count, memory.Metadata.Count));
        }

        // Recency bonus (memories from last hour get boost)
        var ageHours = (DateTimeOffset.UtcNow - memory.Timestamp).TotalHours;
        if (ageHours < 1)
        {
            score += 0.1f * (1f - (float)ageHours);
        }

        // Significance bonus
        score += 0.1f * memory.Significance;

        return score;
    }

    #endregion
}
