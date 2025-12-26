namespace BeyondImmersion.BannouService.Documentation.Services;

/// <summary>
/// Interface for the documentation search index service.
/// Provides in-memory full-text search capabilities with thread-safe operations.
/// </summary>
public interface ISearchIndexService
{
    /// <summary>
    /// Rebuilds the search index for a specific namespace from the state store.
    /// </summary>
    /// <param name="namespaceId">The namespace to rebuild the index for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of documents indexed.</returns>
    Task<int> RebuildIndexAsync(string namespaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds or updates a document in the search index.
    /// </summary>
    /// <param name="namespaceId">The document's namespace.</param>
    /// <param name="documentId">The document ID.</param>
    /// <param name="title">The document title.</param>
    /// <param name="slug">The document slug.</param>
    /// <param name="content">The document content for full-text indexing.</param>
    /// <param name="category">The document category.</param>
    /// <param name="tags">The document tags.</param>
    void IndexDocument(string namespaceId, Guid documentId, string title, string slug, string? content, string category, IEnumerable<string>? tags);

    /// <summary>
    /// Removes a document from the search index.
    /// </summary>
    /// <param name="namespaceId">The document's namespace.</param>
    /// <param name="documentId">The document ID.</param>
    void RemoveDocument(string namespaceId, Guid documentId);

    /// <summary>
    /// Searches documents by keyword within a namespace.
    /// </summary>
    /// <param name="namespaceId">The namespace to search in.</param>
    /// <param name="searchTerm">The search term.</param>
    /// <param name="category">Optional category filter.</param>
    /// <param name="maxResults">Maximum results to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of matching document IDs with relevance scores.</returns>
    Task<IReadOnlyList<SearchResult>> SearchAsync(string namespaceId, string searchTerm, string? category = null, int maxResults = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a natural language query (more sophisticated than keyword search).
    /// </summary>
    /// <param name="namespaceId">The namespace to search in.</param>
    /// <param name="query">The natural language query.</param>
    /// <param name="category">Optional category filter.</param>
    /// <param name="maxResults">Maximum results to return.</param>
    /// <param name="minRelevanceScore">Minimum relevance score threshold.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of matching document IDs with relevance scores.</returns>
    Task<IReadOnlyList<SearchResult>> QueryAsync(string namespaceId, string query, string? category = null, int maxResults = 20, double minRelevanceScore = 0.3, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets suggestions for related topics based on a document or search term.
    /// </summary>
    /// <param name="namespaceId">The namespace to search in.</param>
    /// <param name="sourceValue">The source document ID or search term.</param>
    /// <param name="maxSuggestions">Maximum suggestions to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of suggested document IDs.</returns>
    Task<IReadOnlyList<Guid>> GetRelatedSuggestionsAsync(string namespaceId, string sourceValue, int maxSuggestions = 5, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all document IDs in a namespace matching the given criteria.
    /// </summary>
    /// <param name="namespaceId">The namespace to list from.</param>
    /// <param name="category">Optional category filter.</param>
    /// <param name="skip">Number of results to skip.</param>
    /// <param name="take">Number of results to take.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of document IDs.</returns>
    Task<IReadOnlyList<Guid>> ListDocumentIdsAsync(string namespaceId, string? category = null, int skip = 0, int take = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets statistics for a namespace.
    /// </summary>
    /// <param name="namespaceId">The namespace to get stats for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Namespace statistics.</returns>
    Task<NamespaceStats> GetNamespaceStatsAsync(string namespaceId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a search result with document ID and relevance score.
/// </summary>
/// <param name="DocumentId">The matching document ID.</param>
/// <param name="RelevanceScore">The relevance score (0.0 to 1.0).</param>
/// <param name="Title">The document title.</param>
/// <param name="Slug">The document slug.</param>
public record SearchResult(Guid DocumentId, double RelevanceScore, string Title, string Slug);

/// <summary>
/// Statistics for a documentation namespace.
/// </summary>
/// <param name="TotalDocuments">Total number of documents.</param>
/// <param name="DocumentsByCategory">Document count per category.</param>
/// <param name="TotalTags">Total unique tags.</param>
public record NamespaceStats(int TotalDocuments, IReadOnlyDictionary<string, int> DocumentsByCategory, int TotalTags);
