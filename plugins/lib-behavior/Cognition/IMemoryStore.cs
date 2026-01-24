// =============================================================================
// Memory Store Interface
// Abstraction for memory storage in the cognition pipeline.
// Designed for swappable implementations (keyword-based MVP, embedding-based future).
// =============================================================================

namespace BeyondImmersion.Bannou.Behavior.Cognition;

/// <summary>
/// Memory storage interface for the cognition pipeline.
/// </summary>
/// <remarks>
/// <para>
/// This interface abstracts memory storage and retrieval, enabling different implementations:
/// <list type="bullet">
/// <item><see cref="ActorLocalMemoryStore"/>: MVP using keyword-based relevance (current)</item>
/// <item>EmbeddingMemoryStore: Future option using semantic similarity via LLM embeddings</item>
/// </list>
/// </para>
/// <para>
/// <b>Implementation Selection Criteria</b> (see ACTOR_SYSTEM.md section 7.3):
/// <list type="bullet">
/// <item>Keyword: Fast, free, transparent - good for structured game data</item>
/// <item>Embedding: Semantic similarity - needed for player content or thematic matching</item>
/// </list>
/// </para>
/// </remarks>
public interface IMemoryStore
{
    /// <summary>
    /// Query relevant memories for given perceptions (batched).
    /// </summary>
    /// <param name="entityId">The agent's entity ID.</param>
    /// <param name="perceptions">Perceptions to find relevant memories for.</param>
    /// <param name="limit">Maximum number of memories to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Relevant memories sorted by relevance.</returns>
    Task<IReadOnlyList<Memory>> FindRelevantAsync(
        string entityId,
        IReadOnlyList<Perception> perceptions,
        int limit,
        CancellationToken ct);

    /// <summary>
    /// Store a significant experience as a memory.
    /// </summary>
    /// <param name="entityId">The agent's entity ID.</param>
    /// <param name="perception">The perception to store.</param>
    /// <param name="significance">The significance score.</param>
    /// <param name="context">Related memories for context.</param>
    /// <param name="ct">Cancellation token.</param>
    Task StoreExperienceAsync(
        string entityId,
        Perception perception,
        float significance,
        IReadOnlyList<Memory> context,
        CancellationToken ct);

    /// <summary>
    /// Get all memories for an entity.
    /// </summary>
    /// <param name="entityId">The agent's entity ID.</param>
    /// <param name="limit">Maximum number of memories to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All memories for the entity.</returns>
    Task<IReadOnlyList<Memory>> GetAllAsync(
        string entityId,
        int limit,
        CancellationToken ct);

    /// <summary>
    /// Remove a specific memory.
    /// </summary>
    /// <param name="entityId">The agent's entity ID.</param>
    /// <param name="memoryId">The memory ID to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RemoveAsync(
        string entityId,
        string memoryId,
        CancellationToken ct);

    /// <summary>
    /// Clear all memories for an entity.
    /// </summary>
    /// <param name="entityId">The agent's entity ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ClearAsync(string entityId, CancellationToken ct);
}

/// <summary>
/// A stored memory from past experiences.
/// </summary>
public sealed class Memory
{
    /// <summary>
    /// Unique identifier for this memory.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// The entity ID this memory belongs to.
    /// </summary>
    public string EntityId { get; init; } = string.Empty;

    /// <summary>
    /// Content description of the memory.
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Category of the original perception.
    /// </summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// Significance score when stored.
    /// </summary>
    public float Significance { get; init; }

    /// <summary>
    /// When this memory was created.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Additional metadata about the memory.
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata { get; init; } =
        new Dictionary<string, object>();

    /// <summary>
    /// IDs of related memories that provided context.
    /// </summary>
    public IReadOnlyList<string> RelatedMemoryIds { get; init; } = [];

    /// <summary>
    /// Relevance score when returned from a query (transient, not stored).
    /// </summary>
    public float QueryRelevance { get; set; }
}

/// <summary>
/// A perception from the environment or other agents.
/// </summary>
public sealed class Perception
{
    /// <summary>
    /// Unique identifier for this perception.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Category of the perception: threat, novelty, social, routine.
    /// </summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// Content description of the perception.
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Urgency level (0-1). Higher values indicate more immediate attention needed.
    /// </summary>
    public float Urgency { get; init; }

    /// <summary>
    /// Source of the perception (e.g., entity ID, sensor type).
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// When this perception occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Additional data about the perception.
    /// </summary>
    public IReadOnlyDictionary<string, object> Data { get; init; } =
        new Dictionary<string, object>();

    /// <summary>
    /// Priority score calculated during attention filtering.
    /// </summary>
    public float Priority { get; set; }

    /// <summary>
    /// Creates a perception from a dictionary (for ABML integration).
    /// </summary>
    /// <param name="data">Dictionary containing perception data.</param>
    /// <returns>A new Perception instance.</returns>
    /// <exception cref="ArgumentException">Thrown when content is missing or empty.</exception>
    public static Perception FromDictionary(IReadOnlyDictionary<string, object?> data)
    {
        // Content is required - a perception without content is meaningless
        var contentValue = data.TryGetValue("content", out var content) ? content?.ToString() : null;
        if (string.IsNullOrEmpty(contentValue))
        {
            throw new ArgumentException("Perception requires non-empty 'content' field", nameof(data));
        }

        return new Perception
        {
            Id = data.TryGetValue("id", out var id) ? id?.ToString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString(),
            Category = data.TryGetValue("category", out var cat) ? cat?.ToString() ?? "routine" : "routine",
            Content = contentValue,
            Urgency = data.TryGetValue("urgency", out var urg) && urg is float urgFloat ? urgFloat : 0f,
            // Source can be empty for perceptions from unknown/implicit sources
            Source = data.TryGetValue("source", out var src) ? src?.ToString() ?? string.Empty : string.Empty,
            Timestamp = data.TryGetValue("timestamp", out var ts) && ts is DateTimeOffset dto ? dto : DateTimeOffset.UtcNow,
            // Where clause filters nulls; coalesce satisfies compiler nullable analysis (will never execute)
            Data = data.Where(kv => kv.Value != null)
                        .ToDictionary(kv => kv.Key, kv => kv.Value ?? throw new InvalidOperationException("Unexpected null after filter"))
        };
    }
}
