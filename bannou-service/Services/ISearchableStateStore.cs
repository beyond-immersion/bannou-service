#nullable enable

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Search result from full-text search operations.
/// </summary>
/// <param name="Key">The key of the matching document.</param>
/// <param name="Value">The deserialized value.</param>
/// <param name="Score">The relevance score (higher = more relevant).</param>
public record SearchResult<TValue>(string Key, TValue Value, double Score) where TValue : class;

/// <summary>
/// Paged search result for full-text search operations.
/// </summary>
/// <typeparam name="TValue">The type of values in the result.</typeparam>
public record SearchPagedResult<TValue>(
    IReadOnlyList<SearchResult<TValue>> Items,
    long TotalCount,
    int Offset,
    int Limit) where TValue : class
{
    /// <summary>
    /// Whether there are more results available.
    /// </summary>
    public bool HasMore => Offset + Items.Count < TotalCount;
}

/// <summary>
/// Schema field definition for creating search indexes.
/// </summary>
public record SearchSchemaField
{
    /// <summary>
    /// The JSON path to the field (e.g., "$.title" or "$.metadata.tags[*]").
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// The alias for this field in queries (defaults to path without $. prefix).
    /// </summary>
    public string? Alias { get; init; }

    /// <summary>
    /// The field type for indexing.
    /// </summary>
    public required SearchFieldType Type { get; init; }

    /// <summary>
    /// Weight for TEXT fields (higher = more important in scoring).
    /// </summary>
    public double Weight { get; init; } = 1.0;

    /// <summary>
    /// Whether this field can be used for sorting.
    /// </summary>
    public bool Sortable { get; init; }

    /// <summary>
    /// Whether to disable stemming for TEXT fields.
    /// </summary>
    public bool NoStem { get; init; }
}

/// <summary>
/// Field types for search index schema.
/// </summary>
public enum SearchFieldType
{
    /// <summary>
    /// Full-text searchable field with stemming and tokenization.
    /// </summary>
    Text,

    /// <summary>
    /// Exact-match tag field (comma-separated values).
    /// </summary>
    Tag,

    /// <summary>
    /// Numeric field for range queries.
    /// </summary>
    Numeric,

    /// <summary>
    /// Geographic coordinate field for geo queries.
    /// </summary>
    Geo,

    /// <summary>
    /// Vector field for similarity search.
    /// </summary>
    Vector
}

/// <summary>
/// Options for creating a search index.
/// </summary>
public record SearchIndexOptions
{
    /// <summary>
    /// Key prefix filter - only index keys matching this prefix.
    /// </summary>
    public string? Prefix { get; init; }

    /// <summary>
    /// Default language for text analysis (default: "english").
    /// </summary>
    public string Language { get; init; } = "english";

    /// <summary>
    /// Default score for documents (default: 1.0).
    /// </summary>
    public double DefaultScore { get; init; } = 1.0;

    /// <summary>
    /// Whether to index documents as they're created (default: true).
    /// </summary>
    public bool AutoIndex { get; init; } = true;
}

/// <summary>
/// Options for search queries.
/// </summary>
public record SearchQueryOptions
{
    /// <summary>
    /// Number of results to skip (for pagination).
    /// </summary>
    public int Offset { get; init; } = 0;

    /// <summary>
    /// Maximum number of results to return.
    /// </summary>
    public int Limit { get; init; } = 10;

    /// <summary>
    /// Field to sort by (null for relevance sorting).
    /// </summary>
    public string? SortBy { get; init; }

    /// <summary>
    /// Whether to sort descending.
    /// </summary>
    public bool SortDescending { get; init; }

    /// <summary>
    /// Fields to return (null for all fields).
    /// </summary>
    public IReadOnlyList<string>? ReturnFields { get; init; }

    /// <summary>
    /// Whether to include scores in results.
    /// </summary>
    public bool WithScores { get; init; } = true;

    /// <summary>
    /// Language for query analysis (null for index default).
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// Enable fuzzy matching for typo tolerance.
    /// </summary>
    public bool Fuzzy { get; init; }
}

/// <summary>
/// Information about a search index.
/// </summary>
public record SearchIndexInfo
{
    /// <summary>
    /// Name of the index.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Number of documents in the index.
    /// </summary>
    public long DocumentCount { get; init; }

    /// <summary>
    /// Number of terms in the index.
    /// </summary>
    public long TermCount { get; init; }

    /// <summary>
    /// Total memory used by the index in bytes.
    /// </summary>
    public long MemoryUsageBytes { get; init; }

    /// <summary>
    /// Whether the index is currently being built.
    /// </summary>
    public bool IsIndexing { get; init; }

    /// <summary>
    /// Percentage of indexing complete (0-100).
    /// </summary>
    public double IndexingProgress { get; init; }

    /// <summary>
    /// Schema fields in this index.
    /// </summary>
    public IReadOnlyList<string> Fields { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Searchable state store - extends ICacheableStateStore with full-text search capabilities.
/// Implemented by Redis stores using RedisSearch (FT.* commands).
/// All searchable stores are Redis-based and therefore support all cacheable operations
/// (sets, sorted sets, counters, hashes) in addition to full-text search.
/// </summary>
/// <typeparam name="TValue">Value type stored.</typeparam>
public interface ISearchableStateStore<TValue> : ICacheableStateStore<TValue>
    where TValue : class
{
    /// <summary>
    /// Creates or updates a search index for this store.
    /// </summary>
    /// <param name="indexName">Name of the index (must be unique).</param>
    /// <param name="schema">Field definitions for the index.</param>
    /// <param name="options">Index creation options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if index was created, false if it already existed and was updated.</returns>
    Task<bool> CreateIndexAsync(
        string indexName,
        IReadOnlyList<SearchSchemaField> schema,
        SearchIndexOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops a search index.
    /// </summary>
    /// <param name="indexName">Name of the index to drop.</param>
    /// <param name="deleteDocuments">Whether to also delete the indexed documents.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if index was dropped, false if it didn't exist.</returns>
    Task<bool> DropIndexAsync(
        string indexName,
        bool deleteDocuments = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a full-text search query.
    /// </summary>
    /// <param name="indexName">Name of the index to search.</param>
    /// <param name="query">Search query string (RedisSearch query syntax).</param>
    /// <param name="options">Query options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paged search results with scores.</returns>
    Task<SearchPagedResult<TValue>> SearchAsync(
        string indexName,
        string query,
        SearchQueryOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets search suggestions/autocomplete results.
    /// </summary>
    /// <param name="indexName">Name of the index.</param>
    /// <param name="prefix">Prefix to complete.</param>
    /// <param name="maxResults">Maximum suggestions to return.</param>
    /// <param name="fuzzy">Enable fuzzy matching.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of suggested completions with scores.</returns>
    Task<IReadOnlyList<(string Suggestion, double Score)>> SuggestAsync(
        string indexName,
        string prefix,
        int maxResults = 5,
        bool fuzzy = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about a search index.
    /// </summary>
    /// <param name="indexName">Name of the index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Index information or null if not found.</returns>
    Task<SearchIndexInfo?> GetIndexInfoAsync(
        string indexName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all search indexes for this store.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of index names.</returns>
    Task<IReadOnlyList<string>> ListIndexesAsync(
        CancellationToken cancellationToken = default);
}
