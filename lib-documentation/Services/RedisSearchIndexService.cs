using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Documentation.Services;

/// <summary>
/// Redis Search-backed implementation of ISearchIndexService.
/// Uses Redis 8+ FT.SEARCH via NRedisStack for full-text search instead of in-memory indexing.
/// </summary>
public class RedisSearchIndexService : ISearchIndexService
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<RedisSearchIndexService> _logger;
    private readonly DocumentationServiceConfiguration _configuration;

    private const string STATE_STORE = "documentation-statestore";
    private const string INDEX_PREFIX = "doc-idx:";

    // Track which namespace indexes have been created
    private readonly HashSet<string> _initializedNamespaces = new();
    private readonly object _initLock = new();

    /// <summary>
    /// Schema fields for Redis Search index.
    /// Maps to StoredDocument properties.
    /// </summary>
    /// <remarks>
    /// NOTE: UpdatedAt is NOT indexed because DateTimeOffset serializes as ISO 8601 string,
    /// not a numeric value. Redis Search NUMERIC fields require actual numbers.
    /// If sorting by UpdatedAt is needed, StoredDocument.UpdatedAt must be changed to
    /// store Unix timestamp (long) instead of DateTimeOffset.
    /// </remarks>
    private static readonly IReadOnlyList<SearchSchemaField> DocumentSchema = new List<SearchSchemaField>
    {
        new() { Path = "$.Title", Alias = "title", Type = SearchFieldType.Text, Weight = 2.0, Sortable = true },
        new() { Path = "$.Slug", Alias = "slug", Type = SearchFieldType.Text, Weight = 1.5 },
        new() { Path = "$.Content", Alias = "content", Type = SearchFieldType.Text, Weight = 1.0 },
        new() { Path = "$.Summary", Alias = "summary", Type = SearchFieldType.Text, Weight = 1.2 },
        new() { Path = "$.Category", Alias = "category", Type = SearchFieldType.Tag, Sortable = true },
        new() { Path = "$.Tags[*]", Alias = "tags", Type = SearchFieldType.Tag },
        new() { Path = "$.Namespace", Alias = "namespace", Type = SearchFieldType.Tag }
    };

    /// <summary>
    /// Creates a new instance of the RedisSearchIndexService.
    /// </summary>
    public RedisSearchIndexService(
        IStateStoreFactory stateStoreFactory,
        ILogger<RedisSearchIndexService> logger,
        DocumentationServiceConfiguration configuration)
    {
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <inheritdoc />
    public async Task EnsureIndexExistsAsync(string namespaceId, CancellationToken cancellationToken = default)
    {
        lock (_initLock)
        {
            if (_initializedNamespaces.Contains(namespaceId))
            {
                return;
            }
        }

        var indexName = GetIndexName(namespaceId);

        try
        {
            if (!_stateStoreFactory.SupportsSearch(STATE_STORE))
            {
                _logger.LogWarning("Store '{Store}' does not support search - falling back to in-memory indexing", STATE_STORE);
                return;
            }

            var searchStore = _stateStoreFactory.GetSearchableStore<DocumentIndexData>(STATE_STORE);

            // Check if index exists
            var indexInfo = await searchStore.GetIndexInfoAsync(indexName, cancellationToken);
            if (indexInfo == null)
            {
                _logger.LogInformation("Creating Redis Search index '{Index}' for namespace '{Namespace}'",
                    indexName, namespaceId);

                await searchStore.CreateIndexAsync(
                    indexName,
                    DocumentSchema,
                    new SearchIndexOptions
                    {
                        Prefix = $"doc:{namespaceId}:",
                        Language = "english"
                    },
                    cancellationToken);

                _logger.LogInformation("Created Redis Search index '{Index}' with {FieldCount} fields",
                    indexName, DocumentSchema.Count);
            }
            else
            {
                _logger.LogDebug("Redis Search index '{Index}' already exists with {DocCount} documents",
                    indexName, indexInfo.DocumentCount);
            }

            lock (_initLock)
            {
                _initializedNamespaces.Add(namespaceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure Redis Search index '{Index}' exists", indexName);
            throw;
        }
    }

    private static string GetIndexName(string namespaceId) => $"{INDEX_PREFIX}{namespaceId}";

    /// <inheritdoc />
    public async Task<int> RebuildIndexAsync(string namespaceId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Rebuilding Redis Search index for namespace '{Namespace}'", namespaceId);

        try
        {
            var indexName = GetIndexName(namespaceId);

            if (!_stateStoreFactory.SupportsSearch(STATE_STORE))
            {
                _logger.LogWarning("Store '{Store}' does not support search", STATE_STORE);
                return 0;
            }

            var searchStore = _stateStoreFactory.GetSearchableStore<DocumentIndexData>(STATE_STORE);

            // Drop existing index if it exists
            await searchStore.DropIndexAsync(indexName, deleteDocuments: false, cancellationToken);

            // Remove from initialized set so next operation recreates it
            lock (_initLock)
            {
                _initializedNamespaces.Remove(namespaceId);
            }

            // Recreate the index
            await EnsureIndexExistsAsync(namespaceId, cancellationToken);

            // Get current document count from the new index
            var indexInfo = await searchStore.GetIndexInfoAsync(indexName, cancellationToken);
            var documentCount = (int)(indexInfo?.DocumentCount ?? 0);

            _logger.LogInformation("Rebuilt Redis Search index for namespace '{Namespace}': {DocumentCount} documents indexed",
                namespaceId, documentCount);

            return documentCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rebuild Redis Search index for namespace '{Namespace}'", namespaceId);
            throw;
        }
    }

    /// <inheritdoc />
    public void IndexDocument(string namespaceId, Guid documentId, string title, string slug, string? content, string category, IEnumerable<string>? tags)
    {
        // Redis Search automatically indexes documents stored via ISearchableStateStore
        // when they match the index prefix. No explicit indexing needed - just ensure index exists.
        Task.Run(async () =>
        {
            try
            {
                await EnsureIndexExistsAsync(namespaceId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to ensure index exists for namespace '{Namespace}'", namespaceId);
            }
        });

        _logger.LogDebug("Document {DocumentId} will be auto-indexed by Redis Search", documentId);
    }

    /// <inheritdoc />
    public void RemoveDocument(string namespaceId, Guid documentId)
    {
        // Redis Search automatically removes documents from index when they're deleted from the store.
        // No explicit removal needed.
        _logger.LogDebug("Document {DocumentId} will be auto-removed from Redis Search index", documentId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string namespaceId, string searchTerm, string? category = null, int maxResults = 20, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureIndexExistsAsync(namespaceId, cancellationToken);

            if (!_stateStoreFactory.SupportsSearch(STATE_STORE))
            {
                _logger.LogWarning("Search not supported, returning empty results");
                return Array.Empty<SearchResult>();
            }

            var searchStore = _stateStoreFactory.GetSearchableStore<DocumentIndexData>(STATE_STORE);
            var indexName = GetIndexName(namespaceId);

            // Build Redis Search query
            // Format: @namespace:{ns} @title|content|summary:searchTerm [@category:{category}]
            var queryParts = new List<string>
            {
                $"@namespace:{{{EscapeTagValue(namespaceId)}}}"
            };

            // Add search term query (searches title, content, summary with wildcard)
            var escapedTerm = EscapeSearchTerm(searchTerm);
            queryParts.Add($"({escapedTerm}* | @title:({escapedTerm}*) | @content:({escapedTerm}*) | @summary:({escapedTerm}*))");

            // Add category filter if specified
            if (!string.IsNullOrEmpty(category))
            {
                queryParts.Add($"@category:{{{EscapeTagValue(category)}}}");
            }

            var query = string.Join(" ", queryParts);

            var result = await searchStore.SearchAsync(
                indexName,
                query,
                new SearchQueryOptions
                {
                    Limit = maxResults,
                    WithScores = true,
                    SortBy = null // Sort by relevance
                },
                cancellationToken);

            // Convert to SearchResult format
            var results = result.Items.Select(item => new SearchResult(
                item.Value.DocumentId,
                NormalizeScore(item.Score),
                item.Value.Title,
                item.Value.Slug
            )).ToList();

            _logger.LogDebug("Redis Search for '{Term}' in namespace '{Namespace}' returned {Count} results",
                searchTerm, namespaceId, results.Count);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis Search failed for term '{Term}' in namespace '{Namespace}'", searchTerm, namespaceId);
            return Array.Empty<SearchResult>();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResult>> QueryAsync(string namespaceId, string query, string? category = null, int maxResults = 20, double minRelevanceScore = 0.3, CancellationToken cancellationToken = default)
    {
        var results = await SearchAsync(namespaceId, query, category, maxResults * 2, cancellationToken);
        return results.Where(r => r.RelevanceScore >= minRelevanceScore).Take(maxResults).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetRelatedSuggestionsAsync(string namespaceId, string sourceValue, int maxSuggestions = 5, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureIndexExistsAsync(namespaceId, cancellationToken);

            if (!_stateStoreFactory.SupportsSearch(STATE_STORE))
            {
                return Array.Empty<Guid>();
            }

            // Use search to find related documents
            var searchResults = await SearchAsync(namespaceId, sourceValue, null, maxSuggestions, cancellationToken);
            return searchResults.Select(r => r.DocumentId).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get related suggestions for '{Source}' in namespace '{Namespace}'",
                sourceValue, namespaceId);
            return Array.Empty<Guid>();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListDocumentIdsAsync(string namespaceId, string? category = null, int skip = 0, int take = 100, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureIndexExistsAsync(namespaceId, cancellationToken);

            if (!_stateStoreFactory.SupportsSearch(STATE_STORE))
            {
                return Array.Empty<Guid>();
            }

            var searchStore = _stateStoreFactory.GetSearchableStore<DocumentIndexData>(STATE_STORE);
            var indexName = GetIndexName(namespaceId);

            // Query for all documents in namespace, optionally filtered by category
            var queryParts = new List<string>
            {
                $"@namespace:{{{EscapeTagValue(namespaceId)}}}"
            };

            if (!string.IsNullOrEmpty(category))
            {
                queryParts.Add($"@category:{{{EscapeTagValue(category)}}}");
            }

            var query = string.Join(" ", queryParts);

            var result = await searchStore.SearchAsync(
                indexName,
                query,
                new SearchQueryOptions
                {
                    Offset = skip,
                    Limit = take,
                    SortBy = "title"
                },
                cancellationToken);

            return result.Items.Select(item => item.Value.DocumentId).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list document IDs for namespace '{Namespace}'", namespaceId);
            return Array.Empty<Guid>();
        }
    }

    /// <inheritdoc />
    public async Task<NamespaceStats> GetNamespaceStatsAsync(string namespaceId, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureIndexExistsAsync(namespaceId, cancellationToken);

            if (!_stateStoreFactory.SupportsSearch(STATE_STORE))
            {
                return new NamespaceStats(0, new Dictionary<string, int>(), 0);
            }

            var searchStore = _stateStoreFactory.GetSearchableStore<DocumentIndexData>(STATE_STORE);
            var indexName = GetIndexName(namespaceId);

            var indexInfo = await searchStore.GetIndexInfoAsync(indexName, cancellationToken);
            if (indexInfo == null)
            {
                return new NamespaceStats(0, new Dictionary<string, int>(), 0);
            }

            // TODO: Get category counts - would require aggregation queries
            // For now, return total count with empty category breakdown
            var categoryCounts = new Dictionary<string, int>();

            return new NamespaceStats(
                (int)indexInfo.DocumentCount,
                categoryCounts,
                0 // Tag count not easily available from index info
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get namespace stats for '{Namespace}'", namespaceId);
            return new NamespaceStats(0, new Dictionary<string, int>(), 0);
        }
    }

    /// <summary>
    /// Escapes special characters for Redis Search tag values.
    /// </summary>
    private static string EscapeTagValue(string value)
    {
        // Tag values in Redis Search need certain characters escaped
        return value
            .Replace("\\", "\\\\")
            .Replace("-", "\\-")
            .Replace(".", "\\.")
            .Replace("_", "\\_");
    }

    /// <summary>
    /// Escapes special characters for Redis Search query terms.
    /// </summary>
    private static string EscapeSearchTerm(string term)
    {
        // Escape special Redis Search query characters
        var escaped = term
            .Replace("\\", "\\\\")
            .Replace("@", "\\@")
            .Replace("{", "\\{")
            .Replace("}", "\\}")
            .Replace("[", "\\[")
            .Replace("]", "\\]")
            .Replace("(", "\\(")
            .Replace(")", "\\)")
            .Replace("-", "\\-")
            .Replace("|", "\\|")
            .Replace("~", "\\~")
            .Replace("*", "\\*")
            .Replace(":", "\\:");

        return escaped;
    }

    /// <summary>
    /// Normalizes Redis Search score to 0-1 range.
    /// </summary>
    private static double NormalizeScore(double score)
    {
        // Redis Search returns scores as positive numbers where higher is better
        // Normalize to 0-1 range using a sigmoid-like function
        return Math.Min(1.0, score / (score + 1.0));
    }

    /// <summary>
    /// Internal data structure for document indexing (matches StoredDocument structure).
    /// </summary>
    private sealed class DocumentIndexData
    {
        public Guid DocumentId { get; set; }
        public string Namespace { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? Content { get; set; }
        public string? Summary { get; set; }
        public string Category { get; set; } = string.Empty;
        public List<string>? Tags { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
