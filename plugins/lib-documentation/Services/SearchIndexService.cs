using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace BeyondImmersion.BannouService.Documentation.Services;

/// <summary>
/// In-memory search index with thread-safe operations.
/// Uses ConcurrentDictionary per IMPLEMENTATION TENETS (Multi-Instance Safety).
/// Rebuilt on startup from lib-state store.
/// </summary>
public partial class SearchIndexService : ISearchIndexService
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<SearchIndexService> _logger;
    private readonly DocumentationServiceConfiguration _configuration;

    private const string STATE_STORE = "documentation-statestore";

    /// <summary>
    /// Thread-safe index storage per namespace.
    /// Key: namespace ID, Value: namespace index
    /// </summary>
    private readonly ConcurrentDictionary<string, NamespaceIndex> _indices = new();

    /// <summary>
    /// Creates a new instance of the SearchIndexService.
    /// </summary>
    /// <param name="stateStoreFactory">The state store factory for state operations.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="configuration">The service configuration.</param>
    public SearchIndexService(
        IStateStoreFactory stateStoreFactory,
        ILogger<SearchIndexService> logger,
        DocumentationServiceConfiguration configuration)
    {
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <inheritdoc />
    public async Task EnsureIndexExistsAsync(string namespaceId, CancellationToken cancellationToken = default)
    {
        // In-memory index is created on first use, no pre-creation needed
        _indices.GetOrAdd(namespaceId, _ => new NamespaceIndex());
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<int> RebuildIndexAsync(string namespaceId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Rebuilding search index for namespace {Namespace}", namespaceId);

        // Get or create the namespace index
        var nsIndex = _indices.GetOrAdd(namespaceId, _ => new NamespaceIndex());

        // Clear existing index for this namespace
        nsIndex.Clear();

        // Load document list from state store
        var docListKey = $"ns-docs:{namespaceId}";
        var listStore = _stateStoreFactory.GetStore<List<Guid>>(STATE_STORE);
        var documentIds = await listStore.GetAsync(docListKey, cancellationToken);

        if (documentIds == null || documentIds.Count == 0)
        {
            _logger.LogInformation("No documents found for namespace {Namespace}", namespaceId);
            return 0;
        }

        var indexedCount = 0;
        var docStore = _stateStoreFactory.GetStore<DocumentIndexData>(STATE_STORE);
        foreach (var docId in documentIds)
        {
            try
            {
                // Key without "doc:" prefix since store already prepends it via KeyPrefix config
                var docKey = $"{namespaceId}:{docId}";
                var doc = await docStore.GetAsync(docKey, cancellationToken);

                if (doc != null)
                {
                    nsIndex.AddDocument(docId, doc.Title, doc.Slug, doc.Content, doc.Category, doc.Tags);
                    indexedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to index document {DocumentId} in namespace {Namespace}", docId, namespaceId);
            }
        }

        _logger.LogInformation("Search index rebuilt for namespace {Namespace}: {DocumentCount} documents indexed", namespaceId, indexedCount);
        return indexedCount;
    }

    /// <inheritdoc />
    public void IndexDocument(string namespaceId, Guid documentId, string title, string slug, string? content, string category, IEnumerable<string>? tags)
    {
        var nsIndex = _indices.GetOrAdd(namespaceId, _ => new NamespaceIndex());
        nsIndex.AddDocument(documentId, title, slug, content, category, tags);

        _logger.LogDebug("Indexed document {DocumentId} in namespace {Namespace}", documentId, namespaceId);
    }

    /// <inheritdoc />
    public void RemoveDocument(string namespaceId, Guid documentId)
    {
        if (_indices.TryGetValue(namespaceId, out var nsIndex))
        {
            nsIndex.RemoveDocument(documentId);
            _logger.LogDebug("Removed document {DocumentId} from namespace {Namespace} index", documentId, namespaceId);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string namespaceId, string searchTerm, string? category = null, int maxResults = 20, CancellationToken cancellationToken = default)
    {
        if (!_indices.TryGetValue(namespaceId, out var nsIndex))
        {
            await Task.CompletedTask;
            return Array.Empty<SearchResult>();
        }

        await Task.CompletedTask;
        return nsIndex.Search(searchTerm, category, maxResults);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResult>> QueryAsync(string namespaceId, string query, string? category = null, int maxResults = 20, double minRelevanceScore = 0.3, CancellationToken cancellationToken = default)
    {
        if (!_indices.TryGetValue(namespaceId, out var nsIndex))
        {
            await Task.CompletedTask;
            return Array.Empty<SearchResult>();
        }

        // For now, query uses the same logic as search but with relevance filtering
        // Future: Add semantic/AI-based query processing when enabled
        var results = nsIndex.Search(query, category, maxResults * 2);
        await Task.CompletedTask;
        return results.Where(r => r.RelevanceScore >= minRelevanceScore).Take(maxResults).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetRelatedSuggestionsAsync(string namespaceId, string sourceValue, int maxSuggestions = 5, CancellationToken cancellationToken = default)
    {
        if (!_indices.TryGetValue(namespaceId, out var nsIndex))
        {
            await Task.CompletedTask;
            return Array.Empty<Guid>();
        }

        await Task.CompletedTask;
        return nsIndex.GetRelated(sourceValue, maxSuggestions);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListDocumentIdsAsync(string namespaceId, string? category = null, int skip = 0, int take = 100, CancellationToken cancellationToken = default)
    {
        if (!_indices.TryGetValue(namespaceId, out var nsIndex))
        {
            await Task.CompletedTask;
            return Array.Empty<Guid>();
        }

        await Task.CompletedTask;
        return nsIndex.ListDocuments(category, skip, take);
    }

    /// <inheritdoc />
    public async Task<NamespaceStats> GetNamespaceStatsAsync(string namespaceId, CancellationToken cancellationToken = default)
    {
        if (!_indices.TryGetValue(namespaceId, out var nsIndex))
        {
            await Task.CompletedTask;
            return new NamespaceStats(0, new Dictionary<string, int>(), 0);
        }

        await Task.CompletedTask;
        return nsIndex.GetStats();
    }

    /// <summary>
    /// Internal data structure for document indexing from state store.
    /// </summary>
    private sealed class DocumentIndexData
    {
        public string Title { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? Content { get; set; }
        public string Category { get; set; } = string.Empty;
        public List<string>? Tags { get; set; }
    }

    /// <summary>
    /// Thread-safe index for a single namespace.
    /// Uses ConcurrentDictionary for all mutable state.
    /// </summary>
    private sealed partial class NamespaceIndex
    {
        private readonly ConcurrentDictionary<Guid, IndexedDocument> _documents = new();
        private readonly ConcurrentDictionary<string, ConcurrentBag<Guid>> _invertedIndex = new();
        private readonly ConcurrentDictionary<string, ConcurrentBag<Guid>> _categoryIndex = new();
        private readonly ConcurrentDictionary<string, int> _tagCounts = new();
        private readonly object _statsLock = new();

        public void Clear()
        {
            _documents.Clear();
            _invertedIndex.Clear();
            _categoryIndex.Clear();
            _tagCounts.Clear();
        }

        public void AddDocument(Guid id, string title, string slug, string? content, string category, IEnumerable<string>? tags)
        {
            var doc = new IndexedDocument(id, title, slug, category, tags?.ToList() ?? new List<string>());
            _documents[id] = doc;

            // Build inverted index from title, slug, and content
            var terms = ExtractTerms(title, slug, content);
            foreach (var term in terms)
            {
                var bag = _invertedIndex.GetOrAdd(term.ToLowerInvariant(), _ => new ConcurrentBag<Guid>());
                if (!bag.Contains(id))
                {
                    bag.Add(id);
                }
            }

            // Index by category
            if (!string.IsNullOrEmpty(category))
            {
                var catBag = _categoryIndex.GetOrAdd(category.ToLowerInvariant(), _ => new ConcurrentBag<Guid>());
                if (!catBag.Contains(id))
                {
                    catBag.Add(id);
                }
            }

            // Track tags
            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    _tagCounts.AddOrUpdate(tag.ToLowerInvariant(), 1, (_, count) => count + 1);
                }
            }
        }

        public void RemoveDocument(Guid id)
        {
            if (_documents.TryRemove(id, out var doc))
            {
                // Note: ConcurrentBag doesn't support removal, so we accept some stale references
                // They'll be filtered out during search. For full cleanup, rebuild the index.

                // Decrement tag counts
                foreach (var tag in doc.Tags)
                {
                    _tagCounts.AddOrUpdate(tag.ToLowerInvariant(), 0, (_, count) => Math.Max(0, count - 1));
                }
            }
        }

        public IReadOnlyList<SearchResult> Search(string searchTerm, string? category, int maxResults)
        {
            var searchTerms = ExtractTerms(searchTerm, null, null);
            var matchScores = new ConcurrentDictionary<Guid, double>();

            // Search inverted index for each term
            foreach (var term in searchTerms)
            {
                var lowerTerm = term.ToLowerInvariant();

                // Exact match
                if (_invertedIndex.TryGetValue(lowerTerm, out var exactMatches))
                {
                    foreach (var docId in exactMatches)
                    {
                        matchScores.AddOrUpdate(docId, 1.0, (_, score) => score + 1.0);
                    }
                }

                // Prefix match for partial terms
                foreach (var kvp in _invertedIndex)
                {
                    if (kvp.Key.StartsWith(lowerTerm) && kvp.Key != lowerTerm)
                    {
                        foreach (var docId in kvp.Value)
                        {
                            matchScores.AddOrUpdate(docId, 0.5, (_, score) => score + 0.5);
                        }
                    }
                }
            }

            // Filter by category if specified
            var results = matchScores
                .Where(kvp => _documents.ContainsKey(kvp.Key))
                .Select(kvp => (DocId: kvp.Key, Score: kvp.Value, Doc: _documents[kvp.Key]))
                .Where(x => string.IsNullOrEmpty(category) ||
                            x.Doc.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Score)
                .Take(maxResults)
                .Select(x => new SearchResult(
                    x.DocId,
                    Math.Min(1.0, x.Score / searchTerms.Count), // Normalize score to 0-1
                    x.Doc.Title,
                    x.Doc.Slug))
                .ToList();

            return results;
        }

        public IReadOnlyList<Guid> GetRelated(string sourceValue, int maxSuggestions)
        {
            // Try to find the source document
            var sourceDoc = _documents.Values.FirstOrDefault(d =>
                d.Id.ToString() == sourceValue ||
                d.Slug.Equals(sourceValue, StringComparison.OrdinalIgnoreCase) ||
                d.Title.Equals(sourceValue, StringComparison.OrdinalIgnoreCase));

            if (sourceDoc == null)
            {
                // Fall back to search-based suggestions
                var searchResults = Search(sourceValue, null, maxSuggestions);
                return searchResults.Select(r => r.DocumentId).ToList();
            }

            // Find documents with matching tags or same category
            var related = _documents.Values
                .Where(d => d.Id != sourceDoc.Id)
                .Select(d => new
                {
                    Doc = d,
                    Score = CalculateRelationScore(sourceDoc, d)
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Take(maxSuggestions)
                .Select(x => x.Doc.Id)
                .ToList();

            return related;
        }

        public IReadOnlyList<Guid> ListDocuments(string? category, int skip, int take)
        {
            IEnumerable<IndexedDocument> docs = _documents.Values;

            if (!string.IsNullOrEmpty(category))
            {
                docs = docs.Where(d => d.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
            }

            return docs
                .OrderBy(d => d.Title)
                .Skip(skip)
                .Take(take)
                .Select(d => d.Id)
                .ToList();
        }

        public NamespaceStats GetStats()
        {
            var docsByCategory = _documents.Values
                .GroupBy(d => d.Category)
                .ToDictionary(g => g.Key, g => g.Count());

            var uniqueTags = _tagCounts.Count(kvp => kvp.Value > 0);

            return new NamespaceStats(_documents.Count, docsByCategory, uniqueTags);
        }

        private static double CalculateRelationScore(IndexedDocument source, IndexedDocument target)
        {
            double score = 0;

            // Same category
            if (source.Category.Equals(target.Category, StringComparison.OrdinalIgnoreCase))
            {
                score += 1.0;
            }

            // Shared tags
            var sharedTags = source.Tags.Intersect(target.Tags, StringComparer.OrdinalIgnoreCase).Count();
            score += sharedTags * 0.5;

            return score;
        }

        private static HashSet<string> ExtractTerms(string? title, string? slug, string? content)
        {
            var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var allText = $"{title ?? ""} {slug ?? ""} {content ?? ""}";

            // Extract words (alphanumeric sequences of 2+ characters)
            var matches = WordPattern().Matches(allText);
            foreach (Match match in matches)
            {
                var word = match.Value.ToLowerInvariant();
                if (word.Length >= 2 && !StopWords.Contains(word))
                {
                    terms.Add(word);
                }
            }

            return terms;
        }

        [GeneratedRegex(@"\b[\w]+\b")]
        private static partial Regex WordPattern();

        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for",
            "of", "with", "by", "from", "as", "is", "was", "are", "were", "been",
            "be", "have", "has", "had", "do", "does", "did", "will", "would",
            "could", "should", "may", "might", "must", "shall", "can", "this",
            "that", "these", "those", "it", "its", "what", "which", "who", "whom"
        };

        private sealed record IndexedDocument(Guid Id, string Title, string Slug, string Category, List<string> Tags);
    }
}
