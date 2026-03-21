using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Documentation.Services;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Markdig;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Documentation;

// =============================================================================
// DocumentationService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by DocumentationService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (DocumentationService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IDocumentationService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (DocumentationService.Helpers.cs):
//     Contains all private/internal helper methods, core logic extracted
//     from endpoints, event publishing helpers, query builders, mapping
//     functions, and any other non-public methods. Every async method in
//     this file MUST call ITelemetryProvider.StartActivity to ensure
//     sub-operations are properly instrumented.
//
// Structural tests enforce both rules:
//   - Services_PrimaryFile_DoesNotCallStartActivity
//   - Services_HelperFiles_HaveStartActivityWhenAsync
//
// WHAT GOES HERE:
//   - Private async helper methods (with StartActivity spans)
//   - Private sync helper methods (query builders, mappers, validators)
//   - Internal static key builders (already in primary file by convention,
//     but may be moved here if the primary file is large)
//   - Event publishing helper methods
//   - Any extracted "core" logic (e.g., CreateAccountCoreAsync)
//
// WHAT STAYS IN THE PRIMARY FILE:
//   - Public interface method implementations (/// <inheritdoc/> methods)
//   - Constructor and field declarations
//   - Constants and key prefix definitions
//
// See: docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md (T30)
// See: docs/reference/HELPERS-AND-COMMON-PATTERNS.md
// =============================================================================

/// <summary>
/// Private and internal helper methods for DocumentationService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class DocumentationService
{
    // Helper methods moved from DocumentationService.cs for structural test compliance
    /// <summary>
    /// View documentation page in browser - returns fully rendered HTML string.
    /// This method is not part of the IDocumentationService interface (x-manual-implementation).
    /// Returns HTML content as string for browser rendering.
    /// </summary>
    /// <param name="slug">Document slug within namespace.</param>
    /// <param name="ns">Documentation namespace (defaults to "bannou").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tuple of status code and HTML string content.</returns>
    public async Task<(StatusCodes, string?)> ViewDocumentBySlugAsync(string slug, string? ns = "bannou", CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.ViewDocumentBySlugAsync");
        var namespaceId = ns ?? "bannou";
        _logger.LogDebug("ViewDocumentBySlug: slug={Slug}, namespace={Namespace}", slug, namespaceId);
        // Look up document ID from slug index
        var slugKey = $"{SLUG_INDEX_PREFIX}{namespaceId}:{slug}";
        var documentIdStr = await _stringStore.GetAsync(slugKey, cancellationToken);
        if (string.IsNullOrEmpty(documentIdStr) || !Guid.TryParse(documentIdStr, out var documentId))
        {
            _logger.LogDebug("Document with slug {Slug} not found in namespace {Namespace}", slug, namespaceId);
            return (StatusCodes.NotFound, null);
        }

        // Fetch document content
        var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{documentId}";
        var storedDoc = await _docStore.GetAsync(docKey, cancellationToken);
        if (storedDoc == null)
        {
            _logger.LogWarning("Document {DocumentId} found in slug index but not in store for namespace {Namespace}", documentId, namespaceId);
            return (StatusCodes.NotFound, null);
        }

        // Content should never be null for a stored document - this is a data integrity issue
        if (string.IsNullOrEmpty(storedDoc.Content))
        {
            _logger.LogError("Document {DocumentId} in namespace {Namespace} has null/empty Content - data integrity issue", documentId, namespaceId);
            await _messageBus.TryPublishErrorAsync(
                serviceName: "documentation",
                operation: "BrowseDocument",
                errorType: "DataIntegrityError",
                message: "Stored document has null/empty Content",
                details: new { DocumentId = documentId, Namespace = namespaceId, Slug = slug },
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }

        // Render markdown to HTML for browser display
        var htmlContent = RenderMarkdownToHtml(storedDoc.Content);

        // Build simple HTML page (in production, use proper templating)
        var html = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{System.Web.HttpUtility.HtmlEncode(storedDoc.Title)}</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 800px; margin: 0 auto; padding: 2rem; }}
        h1 {{ color: #333; }}
        .meta {{ color: #666; font-size: 0.9rem; margin-bottom: 1rem; }}
        .tags {{ margin-top: 1rem; }}
        .tag {{ background: #e0e0e0; padding: 0.2rem 0.5rem; border-radius: 3px; margin-right: 0.5rem; font-size: 0.8rem; }}
    </style>
</head>
<body>
    <h1>{System.Web.HttpUtility.HtmlEncode(storedDoc.Title)}</h1>
    <div class=""meta"">
        <span>Category: {System.Web.HttpUtility.HtmlEncode(storedDoc.Category.ToString())}</span> |
        <span>Updated: {storedDoc.UpdatedAt:yyyy-MM-dd HH:mm}</span>
    </div>
    {(string.IsNullOrEmpty(storedDoc.Summary) ? "" : $"<p><em>{System.Web.HttpUtility.HtmlEncode(storedDoc.Summary)}</em></p>")}
    <article>
        {htmlContent}
    </article>
    {(storedDoc.Tags.Count > 0 ? $"<div class=\"tags\">Tags: {string.Join("", storedDoc.Tags.Select(t => $"<span class=\"tag\">{System.Web.HttpUtility.HtmlEncode(t)}</span>"))}</div>" : "")}
</body>
</html>";

        _logger.LogDebug("Rendered document {Slug} in namespace {Namespace} as HTML", slug, namespaceId);
        return (StatusCodes.OK, html);
    }
    #region Helper Methods

    /// <summary>
    /// Updates the namespace last-updated timestamp when a document is created, updated, or recovered.
    /// Uses a dedicated key per namespace for O(1) reads instead of sampling document records.
    /// Stores as ISO 8601 string since DateTimeOffset is a value type incompatible with IStateStoreFactory.
    /// </summary>
    private async Task UpdateNamespaceLastUpdatedAsync(string namespaceId, DateTimeOffset updatedAt, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.UpdateNamespaceLastUpdatedAsync");
        var key = $"{NAMESPACE_LAST_UPDATED_PREFIX}{namespaceId}";
        await _stringStore.SaveAsync(key, updatedAt.ToString("O"), cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Adds a document ID to the namespace document list for pagination support.
    /// Also maintains the global namespace registry for search index rebuild.
    /// </summary>
    private async Task AddDocumentToNamespaceIndexAsync(string namespaceId, Guid documentId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.AddDocumentToNamespaceIndexAsync");
        var indexKey = $"{NAMESPACE_DOCS_PREFIX}{namespaceId}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (docIds, etag) = await _guidSetStore.GetWithETagAsync(indexKey, cancellationToken);
            docIds ??= [];

            if (docIds.Add(documentId))
            {
                // etag is null when key doesn't exist yet; passing empty string signals "create new" semantics
                var result = await _guidSetStore.TrySaveAsync(indexKey, docIds, etag ?? string.Empty, cancellationToken: cancellationToken);
                if (result != null)
                {
                    break;
                }

                _logger.LogDebug("Concurrent modification on namespace index {Namespace}, retrying (attempt {Attempt})", namespaceId, attempt + 1);
                continue;
            }

            break;
        }

        // Track namespace in global registry for search index rebuild on startup
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (allNamespaces, nsEtag) = await _stringSetStore.GetWithETagAsync(ALL_NAMESPACES_KEY, cancellationToken);
            allNamespaces ??= [];

            if (allNamespaces.Add(namespaceId))
            {
                // nsEtag is null when registry doesn't exist yet; passing empty string signals "create new" semantics
                var result = await _stringSetStore.TrySaveAsync(ALL_NAMESPACES_KEY, allNamespaces, nsEtag ?? string.Empty, cancellationToken: cancellationToken);
                if (result != null)
                {
                    break;
                }

                _logger.LogDebug("Concurrent modification on namespace registry, retrying (attempt {Attempt})", attempt + 1);
                continue;
            }

            break;
        }
    }

    /// <summary>
    /// Removes a document ID from the namespace document list.
    /// </summary>
    private async Task RemoveDocumentFromNamespaceIndexAsync(string namespaceId, Guid documentId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.RemoveDocumentFromNamespaceIndexAsync");
        var indexKey = $"{NAMESPACE_DOCS_PREFIX}{namespaceId}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (docIds, etag) = await _guidSetStore.GetWithETagAsync(indexKey, cancellationToken);

            if (docIds != null && docIds.Remove(documentId))
            {
                // GetWithETagAsync returns non-null etag when key exists (checked above);
                // coalesce satisfies compiler's nullable analysis (will never execute)
                var result = await _guidSetStore.TrySaveAsync(indexKey, docIds, etag ?? string.Empty, cancellationToken: cancellationToken);
                if (result != null)
                {
                    return;
                }

                _logger.LogDebug("Concurrent modification on namespace index {Namespace}, retrying (attempt {Attempt})", namespaceId, attempt + 1);
                continue;
            }

            return;
        }

        _logger.LogWarning("Failed to remove document {DocumentId} from namespace index {Namespace} after retries", documentId, namespaceId);
    }

    /// <summary>
    /// Adds a document ID to the namespace trashcan list.
    /// </summary>
    private async Task AddDocumentToTrashcanIndexAsync(string namespaceId, Guid documentId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.AddDocumentToTrashcanIndexAsync");
        var trashListKey = $"ns-trash:{namespaceId}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (trashedDocIds, etag) = await _guidListStore.GetWithETagAsync(trashListKey, cancellationToken);
            trashedDocIds ??= [];

            if (!trashedDocIds.Contains(documentId))
            {
                trashedDocIds.Add(documentId);
                // etag is null when trashcan doesn't exist yet; passing empty string signals "create new" semantics
                var result = await _guidListStore.TrySaveAsync(trashListKey, trashedDocIds, etag ?? string.Empty, cancellationToken: cancellationToken);
                if (result != null)
                {
                    return;
                }

                _logger.LogDebug("Concurrent modification on trashcan index {Namespace}, retrying (attempt {Attempt})", namespaceId, attempt + 1);
                continue;
            }

            return;
        }

        _logger.LogWarning("Failed to add document {DocumentId} to trashcan index {Namespace} after retries", documentId, namespaceId);
    }

    /// <summary>
    /// Removes a document ID from the namespace trashcan list.
    /// </summary>
    private async Task RemoveDocumentFromTrashcanIndexAsync(string namespaceId, Guid documentId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.RemoveDocumentFromTrashcanIndexAsync");
        var trashListKey = $"ns-trash:{namespaceId}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (trashedDocIds, etag) = await _guidListStore.GetWithETagAsync(trashListKey, cancellationToken);

            if (trashedDocIds != null && trashedDocIds.Remove(documentId))
            {
                if (trashedDocIds.Count > 0)
                {
                    // GetWithETagAsync returns non-null etag when key exists (checked above);
                    // coalesce satisfies compiler's nullable analysis (will never execute)
                    var result = await _guidListStore.TrySaveAsync(trashListKey, trashedDocIds, etag ?? string.Empty, cancellationToken: cancellationToken);
                    if (result != null)
                    {
                        return;
                    }

                    _logger.LogDebug("Concurrent modification on trashcan index {Namespace}, retrying (attempt {Attempt})", namespaceId, attempt + 1);
                    continue;
                }
                else
                {
                    await _guidListStore.DeleteAsync(trashListKey, cancellationToken);
                    return;
                }
            }

            return;
        }

        _logger.LogWarning("Failed to remove document {DocumentId} from trashcan index {Namespace} after retries", documentId, namespaceId);
    }

    /// <summary>
    /// Truncates a voice summary to the configured maximum length.
    /// </summary>
    private string? TruncateVoiceSummary(string? voiceSummary)
    {
        if (voiceSummary == null || voiceSummary.Length <= _configuration.VoiceSummaryMaxLength)
            return voiceSummary;

        return voiceSummary[.._configuration.VoiceSummaryMaxLength];
    }

    /// <summary>
    /// Parses a string category value to DocumentCategory enum.
    /// </summary>

    /// <summary>
    /// Markdig pipeline configured for safe HTML rendering with common extensions.
    /// </summary>
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    /// <summary>
    /// Renders markdown content to HTML using Markdig.
    /// </summary>
    private static string? RenderMarkdownToHtml(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return string.Empty;
        }

        return Markdown.ToHtml(markdown, MarkdownPipeline);
    }

    /// <summary>
    /// Determines the relevance reason for a suggested document based on the source type.
    /// </summary>
    private static string DetermineRelevanceReason(SuggestionSource source, string sourceValue, StoredDocument doc)
    {
        return source switch
        {
            SuggestionSource.DocumentId => $"Related to document {sourceValue}",
            SuggestionSource.Slug => $"Similar to '{sourceValue}'",
            SuggestionSource.Topic => $"Covers topic '{sourceValue}'",
            SuggestionSource.Category => $"In category '{doc.Category}'",
            _ => "Related content"
        };
    }

    /// <summary>
    /// Computes a SHA256 hex digest of the given content for incremental sync comparison.
    /// Returns null for null/empty content.
    /// </summary>
    private static string? ComputeContentHash(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return null;
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// Generates a snippet around the search term match in content.
    /// </summary>
    private static string GenerateSearchSnippet(string? content, string searchTerm, int snippetLength = 200)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        var index = content.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            // No match found, return beginning of content
            return content.Length <= snippetLength
                ? content
                : content[..snippetLength] + "...";
        }

        // Calculate snippet boundaries
        var start = Math.Max(0, index - snippetLength / 4);
        var end = Math.Min(content.Length, index + searchTerm.Length + snippetLength * 3 / 4);

        var snippet = content[start..end].Trim();

        // Add ellipsis if truncated
        if (start > 0) snippet = "..." + snippet;
        if (end < content.Length) snippet += "...";

        return snippet;
    }

    /// <summary>
    /// Gets document summaries for related documents.
    /// </summary>
    private async Task<ICollection<DocumentSummary>> GetRelatedDocumentSummariesAsync(
        string namespaceId,
        ICollection<Guid> relatedIds,
        RelatedDepth depth,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.GetRelatedDocumentSummariesAsync");
        var summaries = new List<DocumentSummary>();
        var maxRelated = depth == RelatedDepth.Extended
            ? _configuration.MaxRelatedDocumentsExtended
            : _configuration.MaxRelatedDocuments;

        foreach (var relatedId in relatedIds.Take(maxRelated))
        {
            var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{relatedId}";
            var doc = await _docStore.GetAsync(docKey, cancellationToken);

            if (doc != null)
            {
                summaries.Add(new DocumentSummary
                {
                    DocumentId = doc.DocumentId,
                    Slug = doc.Slug,
                    Title = doc.Title,
                    Category = doc.Category,
                    Summary = doc.Summary,
                    VoiceSummary = doc.VoiceSummary,
                    Tags = doc.Tags
                });
            }
        }

        return summaries;
    }

    #endregion
    #region Event Publishing

    /// <summary>
    /// Publishes DocumentCreatedEvent to RabbitMQ via IMessageBus.
    /// </summary>
    private async Task PublishDocumentCreatedEventAsync(StoredDocument doc, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.PublishDocumentCreatedEventAsync");
        try
        {
            var eventModel = new DocumentCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                DocumentId = doc.DocumentId,
                Namespace = doc.Namespace,
                Slug = doc.Slug,
                Title = doc.Title,
                Category = doc.Category,
                Tags = doc.Tags,
                CreatedAt = doc.CreatedAt,
                UpdatedAt = doc.UpdatedAt
            };

            await _messageBus.PublishDocumentCreatedAsync(eventModel);
            _logger.LogDebug("Published DocumentCreatedEvent for document {DocumentId}", doc.DocumentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish DocumentCreatedEvent for document {DocumentId}", doc.DocumentId);
            // Don't throw - event publishing failure shouldn't break document creation
        }
    }

    /// <summary>
    /// Publishes DocumentUpdatedEvent to RabbitMQ via IMessageBus.
    /// </summary>
    private async Task PublishDocumentUpdatedEventAsync(StoredDocument doc, IEnumerable<string> changedFields, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.PublishDocumentUpdatedEventAsync");
        try
        {
            var eventModel = new DocumentUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                DocumentId = doc.DocumentId,
                Namespace = doc.Namespace,
                Slug = doc.Slug,
                Title = doc.Title,
                Category = doc.Category,
                Tags = doc.Tags,
                CreatedAt = doc.CreatedAt,
                UpdatedAt = doc.UpdatedAt,
                ChangedFields = changedFields.ToList()
            };

            await _messageBus.PublishDocumentUpdatedAsync(eventModel);
            _logger.LogDebug("Published DocumentUpdatedEvent for document {DocumentId}", doc.DocumentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish DocumentUpdatedEvent for document {DocumentId}", doc.DocumentId);
            // Don't throw - event publishing failure shouldn't break document update
        }
    }

    /// <summary>
    /// Publishes DocumentDeletedEvent to RabbitMQ via IMessageBus.
    /// </summary>
    private async Task PublishDocumentDeletedEventAsync(StoredDocument doc, string? reason, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.PublishDocumentDeletedEventAsync");
        try
        {
            var eventModel = new DocumentDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                DocumentId = doc.DocumentId,
                Namespace = doc.Namespace,
                Slug = doc.Slug,
                Title = doc.Title,
                Category = doc.Category,
                Tags = doc.Tags,
                CreatedAt = doc.CreatedAt,
                UpdatedAt = doc.UpdatedAt,
                DeletedReason = reason
            };

            await _messageBus.PublishDocumentDeletedAsync(eventModel);
            _logger.LogDebug("Published DocumentDeletedEvent for document {DocumentId}", doc.DocumentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish DocumentDeletedEvent for document {DocumentId}", doc.DocumentId);
            // Don't throw - event publishing failure shouldn't break document deletion
        }
    }

    /// <summary>
    /// Publishes DocumentationQueriedEvent for analytics (fire and forget).
    /// </summary>
    private async Task PublishQueryAnalyticsEventAsync(
        string namespaceId,
        string query,
        Guid? sessionId,
        int resultCount,
        Guid? topResultId,
        double? relevanceScore)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.PublishQueryAnalyticsEventAsync");
            var eventModel = new DocumentationQueriedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Namespace = namespaceId,
                Query = query,
                SessionId = sessionId,
                ResultCount = resultCount,
                TopResultId = topResultId,
                RelevanceScore = relevanceScore
            };

            await _messageBus.PublishDocumentationQueriedAsync(eventModel);
            _logger.LogDebug("Published DocumentationQueriedEvent for query '{Query}'", query);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish DocumentationQueriedEvent");
        }
    }

    /// <summary>
    /// Publishes DocumentationSearchedEvent for analytics (fire and forget).
    /// </summary>
    private async Task PublishSearchAnalyticsEventAsync(
        string namespaceId,
        string searchTerm,
        Guid? sessionId,
        int resultCount)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.PublishSearchAnalyticsEventAsync");
            var eventModel = new DocumentationSearchedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Namespace = namespaceId,
                SearchTerm = searchTerm,
                SessionId = sessionId,
                ResultCount = resultCount
            };

            await _messageBus.PublishDocumentationSearchedAsync(eventModel);
            _logger.LogDebug("Published DocumentationSearchedEvent for term '{Term}'", searchTerm);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish DocumentationSearchedEvent");
        }
    }
    #endregion

    #region Repository Binding Helpers

    /// <summary>
    /// Gets the binding for a namespace if it exists.
    /// </summary>
    internal async Task<Models.RepositoryBinding?> GetBindingForNamespaceAsync(string namespaceId, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.GetBindingForNamespaceAsync");
        var bindingKey = $"{BINDING_KEY_PREFIX}{namespaceId}";
        return await _bindingStore.GetAsync(bindingKey, cancellationToken);
    }

    /// <summary>
    /// Saves a binding to the state store and updates the registry.
    /// </summary>
    private async Task SaveBindingAsync(Models.RepositoryBinding binding, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.SaveBindingAsync");
        var bindingKey = $"{BINDING_KEY_PREFIX}{binding.Namespace}";
        await _bindingStore.SaveAsync(bindingKey, binding, cancellationToken: cancellationToken);

        // Update bindings registry
        var registry = await _stringSetStore.GetAsync(BINDINGS_REGISTRY_KEY, cancellationToken) ?? [];
        if (registry.Add(binding.Namespace))
        {
            await _stringSetStore.SaveAsync(BINDINGS_REGISTRY_KEY, registry, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Gets the local file system path for a repository.
    /// </summary>
    private string GetLocalRepositoryPath(Guid bindingId)
    {
        return Path.Combine(_configuration.GitStoragePath, bindingId.ToString());
    }

    /// <summary>
    /// Deletes all documents in a namespace.
    /// </summary>
    private async Task<int> DeleteAllNamespaceDocumentsAsync(string namespaceId, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.DeleteAllNamespaceDocumentsAsync");
        var docsKey = $"{NAMESPACE_DOCS_PREFIX}{namespaceId}";
        var docIds = await _guidSetStore.GetAsync(docsKey, cancellationToken);

        if (docIds == null || docIds.Count == 0)
        {
            return 0;
        }

        var deletedCount = 0;

        foreach (var docId in docIds)
        {
            try
            {
                var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{docId}";
                var doc = await _docStore.GetAsync(docKey, cancellationToken);

                if (doc != null)
                {
                    // Delete slug index
                    var slugKey = $"{SLUG_INDEX_PREFIX}{namespaceId}:{doc.Slug}";
                    await _stringStore.DeleteAsync(slugKey, cancellationToken);

                    // Remove from search index
                    _searchIndexService.RemoveDocument(namespaceId, docId);

                    // Delete document
                    await _docStore.DeleteAsync(docKey, cancellationToken);

                    // Publish deletion event per FOUNDATION TENETS (event-driven architecture)
                    await PublishDocumentDeletedEventAsync(doc, "Namespace documents deleted during repository unbind", cancellationToken);

                    deletedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete document {DocumentId} during namespace cleanup for {Namespace}", docId, namespaceId);
            }
        }

        // Clear namespace document list
        await _guidSetStore.DeleteAsync(docsKey, cancellationToken);

        return deletedCount;
    }

    /// <summary>
    /// Executes a sync operation for a binding.
    /// </summary>
    private async Task<Models.SyncResult> ExecuteSyncAsync(
        Models.RepositoryBinding binding,
        bool force,
        SyncTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.ExecuteSyncAsync");
        var syncId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        var lockResourceId = $"{SYNC_LOCK_PREFIX}{binding.Namespace}";
        var lockOwner = Guid.NewGuid().ToString();

        // Acquire distributed lock to prevent concurrent syncs (IMPLEMENTATION TENETS: Multi-Instance Safety)
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.Documentation,
            lockResourceId,
            lockOwner,
            _configuration.SyncLockTtlSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogWarning("Could not acquire sync lock for namespace {Namespace}, sync already in progress", binding.Namespace);
            return Models.SyncResult.Failed(syncId, "Sync already in progress", startedAt);
        }

        _logger.LogInformation("Starting sync {SyncId} for namespace {Namespace}", syncId, binding.Namespace);

        // Update status to syncing
        binding.Status = BindingStatus.Syncing;
        await SaveBindingAsync(binding, cancellationToken);

        // Publish sync started event
        await TryPublishSyncStartedEventAsync(binding, syncId, trigger, cancellationToken);

        try
        {
            // Get local repository path
            var localPath = GetLocalRepositoryPath(binding.BindingId);

            // Clone or pull repository
            var gitResult = await _gitSyncService.SyncRepositoryAsync(
                binding.RepositoryUrl,
                binding.Branch,
                localPath,
                cancellationToken);

            if (!gitResult.Success)
            {
                var failedResult = Models.SyncResult.Failed(syncId, gitResult.ErrorMessage ?? "Git sync failed", startedAt);
                binding.Status = BindingStatus.Error;
                binding.LastSyncError = gitResult.ErrorMessage;
                await SaveBindingAsync(binding, cancellationToken);
                await TryPublishSyncCompletedEventAsync(binding, syncId, failedResult, cancellationToken);
                return failedResult;
            }

            // Check if we need to sync (commit hash unchanged)
            if (!force && binding.LastCommitHash == gitResult.CommitHash)
            {
                _logger.LogDebug("Repository unchanged, skipping sync for namespace {Namespace}", binding.Namespace);
                var noChangeResult = Models.SyncResult.Success(syncId, gitResult.CommitHash, 0, 0, 0, startedAt);
                binding.Status = BindingStatus.Synced;
                binding.LastSyncAt = DateTimeOffset.UtcNow;
                await SaveBindingAsync(binding, cancellationToken);
                await TryPublishSyncCompletedEventAsync(binding, syncId, noChangeResult, cancellationToken);
                return noChangeResult;
            }

            // Get matching files
            var allMatchingFiles = await _gitSyncService.GetMatchingFilesAsync(
                localPath,
                binding.FilePatterns,
                binding.ExcludePatterns,
                cancellationToken);

            // Apply max documents per sync limit
            var truncated = _configuration.MaxDocumentsPerSync > 0 && allMatchingFiles.Count > _configuration.MaxDocumentsPerSync;
            if (truncated)
            {
                _logger.LogWarning(
                    "Repository sync for namespace {Namespace} has {Total} files, limiting to {Max}",
                    binding.Namespace, allMatchingFiles.Count, _configuration.MaxDocumentsPerSync);
            }
            var matchingFiles = truncated
                ? allMatchingFiles.Take(_configuration.MaxDocumentsPerSync).ToList()
                : allMatchingFiles;

            var documentsCreated = 0;
            var documentsUpdated = 0;
            var documentsSkipped = 0;
            var processedSlugs = new HashSet<string>();

            // Process each matching file
            foreach (var filePath in matchingFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var content = await _gitSyncService.ReadFileContentAsync(localPath, filePath, cancellationToken);
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        continue;
                    }

                    var transformed = _contentTransformService.TransformFile(
                        filePath,
                        content,
                        binding.CategoryMapping,
                        binding.DefaultCategory);

                    if (transformed.IsDraft)
                    {
                        _logger.LogDebug("Skipping draft document: {FilePath}", filePath);
                        continue;
                    }

                    // Track this slug as processed
                    processedSlugs.Add(transformed.Slug);

                    // Check if document exists
                    var existingDocId = await GetDocumentIdBySlugAsync(binding.Namespace, transformed.Slug, cancellationToken);

                    if (existingDocId.HasValue)
                    {
                        // Update existing document (skips write if content unchanged)
                        var wasUpdated = await UpdateDocumentFromTransformAsync(binding.Namespace, existingDocId.Value, transformed, cancellationToken);
                        if (wasUpdated)
                        {
                            documentsUpdated++;
                        }
                        else
                        {
                            documentsSkipped++;
                        }
                    }
                    else
                    {
                        // Create new document
                        await CreateDocumentFromTransformAsync(binding.Namespace, transformed, cancellationToken);
                        documentsCreated++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process file {FilePath}", filePath);
                }
            }

            // Track namespace last-updated timestamp once for the sync batch
            if (documentsCreated > 0 || documentsUpdated > 0)
            {
                await UpdateNamespaceLastUpdatedAsync(binding.Namespace, DateTimeOffset.UtcNow, cancellationToken);
            }

            // Delete orphan documents (documents not in processed slugs)
            // Skip orphan deletion if we truncated the file list to avoid incorrectly deleting unprocessed documents
            var documentsDeleted = 0;
            if (!truncated)
            {
                documentsDeleted = await DeleteOrphanDocumentsAsync(binding.Namespace, processedSlugs, cancellationToken);
            }

            // Update binding state
            binding.Status = BindingStatus.Synced;
            binding.LastSyncAt = DateTimeOffset.UtcNow;
            binding.LastCommitHash = gitResult.CommitHash;
            binding.LastSyncError = null;
            binding.DocumentCount = await GetNamespaceDocumentCountAsync(binding.Namespace, cancellationToken);
            binding.NextSyncAt = DateTimeOffset.UtcNow.AddMinutes(binding.SyncIntervalMinutes);
            await SaveBindingAsync(binding, cancellationToken);

            var successResult = Models.SyncResult.Success(
                syncId,
                gitResult.CommitHash,
                documentsCreated,
                documentsUpdated,
                documentsDeleted,
                startedAt);

            await TryPublishSyncCompletedEventAsync(binding, syncId, successResult, cancellationToken);

            _logger.LogInformation("Sync completed for namespace {Namespace}: {Created} created, {Updated} updated, {Deleted} deleted, {Skipped} unchanged",
                binding.Namespace, documentsCreated, documentsUpdated, documentsDeleted, documentsSkipped);

            return successResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync failed for namespace {Namespace}", binding.Namespace);

            binding.Status = BindingStatus.Error;
            binding.LastSyncError = ex.Message;
            await SaveBindingAsync(binding, cancellationToken);

            var failedResult = Models.SyncResult.Failed(syncId, ex.Message, startedAt);
            await TryPublishSyncCompletedEventAsync(binding, syncId, failedResult, cancellationToken);
            return failedResult;
        }
    }

    /// <summary>
    /// Gets a document ID by slug.
    /// </summary>
    private async Task<Guid?> GetDocumentIdBySlugAsync(string namespaceId, string slug, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.GetDocumentIdBySlugAsync");
        var slugKey = $"{SLUG_INDEX_PREFIX}{namespaceId}:{slug}";
        var docIdStr = await _stringStore.GetAsync(slugKey, cancellationToken);

        if (string.IsNullOrEmpty(docIdStr) || !Guid.TryParse(docIdStr, out var docId))
        {
            return null;
        }

        return docId;
    }

    /// <summary>
    /// Creates a document from a transformed file.
    /// </summary>
    private async Task CreateDocumentFromTransformAsync(
        string namespaceId,
        TransformedDocument transformed,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.CreateDocumentFromTransformAsync");
        var documentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var storedDoc = new StoredDocument
        {
            DocumentId = documentId,
            Namespace = namespaceId,
            Slug = transformed.Slug,
            Title = transformed.Title,
            Category = transformed.Category,
            Content = transformed.Content,
            Summary = transformed.Summary,
            VoiceSummary = transformed.VoiceSummary,
            Tags = transformed.Tags,
            RelatedDocuments = [],
            Metadata = transformed.Metadata,
            CreatedAt = now,
            UpdatedAt = now,
            ContentHash = ComputeContentHash(transformed.Content)
        };

        // Store document
        var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{documentId}";
        await _docStore.SaveAsync(docKey, storedDoc, cancellationToken: cancellationToken);

        // Store slug index
        var slugKey = $"{SLUG_INDEX_PREFIX}{namespaceId}:{transformed.Slug}";
        await _stringStore.SaveAsync(slugKey, documentId.ToString(), cancellationToken: cancellationToken);

        // Add to namespace document list
        var docsKey = $"{NAMESPACE_DOCS_PREFIX}{namespaceId}";
        var docIds = await _guidSetStore.GetAsync(docsKey, cancellationToken) ?? [];
        docIds.Add(documentId);
        await _guidSetStore.SaveAsync(docsKey, docIds, cancellationToken: cancellationToken);

        // Index for search
        _searchIndexService.IndexDocument(
            namespaceId,
            storedDoc.DocumentId,
            storedDoc.Title,
            storedDoc.Slug,
            storedDoc.Content,
            storedDoc.Category,
            storedDoc.Tags);
    }

    /// <summary>
    /// Updates a document from a transformed file. Returns true if the document was
    /// actually modified, false if content was unchanged and the update was skipped.
    /// </summary>
    private async Task<bool> UpdateDocumentFromTransformAsync(
        string namespaceId,
        Guid documentId,
        TransformedDocument transformed,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.UpdateDocumentFromTransformAsync");
        var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{documentId}";
        var existingDoc = await _docStore.GetAsync(docKey, cancellationToken);

        if (existingDoc == null)
        {
            return false;
        }

        // Incremental sync: skip write if content is unchanged
        var newHash = ComputeContentHash(transformed.Content);
        if (existingDoc.ContentHash != null
            && existingDoc.ContentHash == newHash
            && existingDoc.Title == transformed.Title
            && existingDoc.Category == transformed.Category
            && existingDoc.Summary == transformed.Summary
            && existingDoc.VoiceSummary == transformed.VoiceSummary
            && TagsEqual(existingDoc.Tags, transformed.Tags))
        {
            return false;
        }

        existingDoc.Title = transformed.Title;
        existingDoc.Category = transformed.Category;
        existingDoc.Content = transformed.Content;
        existingDoc.Summary = transformed.Summary;
        existingDoc.VoiceSummary = transformed.VoiceSummary;
        existingDoc.Tags = transformed.Tags;
        existingDoc.Metadata = transformed.Metadata;
        existingDoc.UpdatedAt = DateTimeOffset.UtcNow;
        existingDoc.ContentHash = newHash;

        await _docStore.SaveAsync(docKey, existingDoc, cancellationToken: cancellationToken);

        // Re-index for search
        _searchIndexService.IndexDocument(
            existingDoc.Namespace,
            existingDoc.DocumentId,
            existingDoc.Title,
            existingDoc.Slug,
            existingDoc.Content,
            existingDoc.Category,
            existingDoc.Tags);

        return true;
    }

    /// <summary>
    /// Compares two tag lists for equality (order-insensitive).
    /// </summary>
    private static bool TagsEqual(List<string>? a, List<string>? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (a.Count != b.Count) return false;
        return a.OrderBy(t => t, StringComparer.Ordinal).SequenceEqual(b.OrderBy(t => t, StringComparer.Ordinal));
    }

    /// <summary>
    /// Gets the count of documents in a namespace.
    /// </summary>
    private async Task<int> GetNamespaceDocumentCountAsync(string namespaceId, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.GetNamespaceDocumentCountAsync");
        var docsKey = $"{NAMESPACE_DOCS_PREFIX}{namespaceId}";
        var docIds = await _guidSetStore.GetAsync(docsKey, cancellationToken);
        return docIds?.Count ?? 0;
    }

    /// <summary>
    /// Deletes documents in a namespace that are not in the processed slugs set.
    /// Used during sync to remove documents for files that have been deleted from the repository.
    /// </summary>
    private async Task<int> DeleteOrphanDocumentsAsync(
        string namespaceId,
        HashSet<string> processedSlugs,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.DeleteOrphanDocumentsAsync");
        var docsKey = $"{NAMESPACE_DOCS_PREFIX}{namespaceId}";
        var docIds = await _guidSetStore.GetAsync(docsKey, cancellationToken);

        if (docIds == null || docIds.Count == 0)
        {
            return 0;
        }

        var deletedCount = 0;
        var orphanIds = new List<Guid>();

        // Find orphan documents (slugs not in processed set)
        foreach (var docId in docIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{docId}";
            var doc = await _docStore.GetAsync(docKey, cancellationToken);

            if (doc != null && !processedSlugs.Contains(doc.Slug))
            {
                // This document's slug was not processed, it's an orphan
                orphanIds.Add(docId);

                // Delete slug index
                var slugKey = $"{SLUG_INDEX_PREFIX}{namespaceId}:{doc.Slug}";
                await _stringStore.DeleteAsync(slugKey, cancellationToken);

                // Delete search index
                _searchIndexService.RemoveDocument(namespaceId, docId);

                // Delete document
                await _docStore.DeleteAsync(docKey, cancellationToken);
                deletedCount++;

                _logger.LogInformation("Deleted orphan document {DocumentId} with slug {Slug} from namespace {Namespace}",
                    docId, doc.Slug, namespaceId);
            }
        }

        // Update namespace document list to remove orphan IDs (with ETag protection)
        if (orphanIds.Count > 0)
        {
            const int maxRetries = 3;
            for (int retry = 0; retry < maxRetries; retry++)
            {
                var (currentIds, etag) = await _guidSetStore.GetWithETagAsync(docsKey, cancellationToken);
                if (currentIds == null) break;

                foreach (var orphanId in orphanIds)
                {
                    currentIds.Remove(orphanId);
                }

                string? savedEtag;
                if (etag != null)
                {
                    savedEtag = await _guidSetStore.TrySaveAsync(docsKey, currentIds, etag, cancellationToken: cancellationToken);
                }
                else
                {
                    savedEtag = await _guidSetStore.SaveAsync(docsKey, currentIds, cancellationToken: cancellationToken);
                }

                if (savedEtag != null) break;

                _logger.LogDebug("Namespace doc list update for {Namespace} had ETag conflict, retrying ({Retry}/{MaxRetries})",
                    namespaceId, retry + 1, maxRetries);
            }
        }

        return deletedCount;
    }

    /// <summary>
    /// Maps binding to API info model.
    /// </summary>
    private static RepositoryBindingInfo MapToBindingInfo(Models.RepositoryBinding binding) => new()
    {
        BindingId = binding.BindingId,
        Namespace = binding.Namespace,
        RepositoryUrl = binding.RepositoryUrl,
        Branch = binding.Branch,
        Status = binding.Status,
        SyncEnabled = binding.SyncEnabled,
        SyncIntervalMinutes = binding.SyncIntervalMinutes,
        DocumentCount = binding.DocumentCount,
        CreatedAt = binding.CreatedAt,
        OwnerType = binding.OwnerType,
        OwnerId = binding.OwnerId
    };

    /// <summary>
    /// Publishes a binding created event.
    /// </summary>
    private async Task TryPublishBindingCreatedEventAsync(Models.RepositoryBinding binding, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.TryPublishBindingCreatedEventAsync");
        try
        {
            var eventModel = new DocumentationBindingCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Namespace = binding.Namespace,
                BindingId = binding.BindingId,
                RepositoryUrl = binding.RepositoryUrl,
                Branch = binding.Branch
            };
            await _messageBus.PublishDocumentationBindingCreatedAsync(eventModel, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish binding created event");
        }
    }

    /// <summary>
    /// Publishes a binding removed event.
    /// </summary>
    private async Task TryPublishBindingRemovedEventAsync(Models.RepositoryBinding binding, int documentsDeleted, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.TryPublishBindingRemovedEventAsync");
        try
        {
            var eventModel = new DocumentationBindingRemovedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Namespace = binding.Namespace,
                BindingId = binding.BindingId,
                DocumentsDeleted = documentsDeleted
            };
            await _messageBus.PublishDocumentationBindingRemovedAsync(eventModel, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish binding removed event");
        }
    }

    /// <summary>
    /// Publishes a sync started event.
    /// </summary>
    private async Task TryPublishSyncStartedEventAsync(Models.RepositoryBinding binding, Guid syncId, SyncTrigger trigger, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.TryPublishSyncStartedEventAsync");
        try
        {
            var triggeredBy = trigger switch
            {
                SyncTrigger.Manual => SyncTrigger.Manual,
                SyncTrigger.Scheduled => SyncTrigger.Scheduled,
                _ => SyncTrigger.Manual
            };
            var eventModel = new DocumentationSyncStartedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Namespace = binding.Namespace,
                BindingId = binding.BindingId,
                SyncId = syncId,
                TriggeredBy = triggeredBy
            };
            await _messageBus.PublishDocumentationSyncStartedAsync(eventModel, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish sync started event");
        }
    }

    /// <summary>
    /// Publishes a sync completed event.
    /// </summary>
    private async Task TryPublishSyncCompletedEventAsync(Models.RepositoryBinding binding, Guid syncId, Models.SyncResult result, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.TryPublishSyncCompletedEventAsync");
        try
        {
            var eventModel = new DocumentationSyncCompletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Namespace = binding.Namespace,
                BindingId = binding.BindingId,
                SyncId = syncId,
                Status = result.Status,
                CommitHash = result.CommitHash,
                DocumentsCreated = result.DocumentsCreated,
                DocumentsUpdated = result.DocumentsUpdated,
                DocumentsDeleted = result.DocumentsDeleted,
                DurationMs = result.DurationMs,
                ErrorMessage = result.ErrorMessage
            };
            await _messageBus.PublishDocumentationSyncCompletedAsync(eventModel, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish sync completed event");
        }
    }

    #endregion

    #region Archive Helpers

    /// <summary>
    /// Gets all documents in a namespace.
    /// </summary>
    private async Task<List<StoredDocument>> GetAllNamespaceDocumentsAsync(string namespaceId, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.GetAllNamespaceDocumentsAsync");
        var docsKey = $"{NAMESPACE_DOCS_PREFIX}{namespaceId}";
        var docIds = await _guidSetStore.GetAsync(docsKey, cancellationToken);

        if (docIds == null || docIds.Count == 0)
        {
            return [];
        }

        var documents = new List<StoredDocument>();

        foreach (var docId in docIds)
        {
            var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{docId}";
            var doc = await _docStore.GetAsync(docKey, cancellationToken);
            if (doc != null)
            {
                documents.Add(doc);
            }
        }

        return documents;
    }

    /// <summary>
    /// Creates a compressed archive bundle from documents.
    /// </summary>
    private async Task<byte[]> CreateArchiveBundleAsync(string namespaceId, List<StoredDocument> documents, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.CreateArchiveBundleAsync");
        // Filter out documents with null/empty Content - this is a data integrity issue
        var validDocuments = new List<StoredDocument>();
        foreach (var doc in documents)
        {
            if (string.IsNullOrEmpty(doc.Content))
            {
                _logger.LogError("Document {DocumentId} in namespace {Namespace} has null/empty Content - excluding from bundle", doc.DocumentId, namespaceId);
                await _messageBus.TryPublishErrorAsync(
                    serviceName: "documentation",
                    operation: "CreateArchiveBundle",
                    errorType: "DataIntegrityError",
                    message: "Stored document has null/empty Content - excluded from bundle",
                    details: new { DocumentId = doc.DocumentId, Namespace = namespaceId, Slug = doc.Slug },
                    cancellationToken: cancellationToken);
                continue;
            }
            validDocuments.Add(doc);
        }

        // Create a JSON bundle with valid documents
        var bundle = new DocumentationBundle
        {
            Version = "1.0",
            Namespace = namespaceId,
            CreatedAt = DateTimeOffset.UtcNow,
            // validDocuments only contains docs with non-null Content (filtered above)
            // The null-coalesce satisfies the compiler but will never execute
            Documents = validDocuments.Select(d => new BundledDocument
            {
                DocumentId = d.DocumentId,
                Slug = d.Slug,
                Title = d.Title,
                Content = d.Content ?? string.Empty,
                Category = d.Category,
                Summary = d.Summary,
                VoiceSummary = d.VoiceSummary,
                Tags = d.Tags,
                Metadata = d.Metadata,
                CreatedAt = d.CreatedAt,
                UpdatedAt = d.UpdatedAt
            }).ToList()
        };

        // Serialize to JSON and compress with GZip
        var json = BannouJson.Serialize(bundle);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            var jsonBytes = Encoding.UTF8.GetBytes(json);
            await gzip.WriteAsync(jsonBytes, cancellationToken);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Gets the current commit hash for a bound namespace.
    /// </summary>
    private async Task<string?> GetCurrentCommitHashForNamespaceAsync(string namespaceId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.GetCurrentCommitHashForNamespaceAsync");
        var binding = await GetBindingForNamespaceAsync(namespaceId, cancellationToken);
        return binding?.LastCommitHash;
    }

    /// <summary>
    /// Saves an archive record to state store.
    /// </summary>
    private async Task SaveArchiveAsync(Models.DocumentationArchive archive, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.SaveArchiveAsync");
        var archiveKey = $"{ARCHIVE_KEY_PREFIX}{archive.ArchiveId}";
        await _archiveStore.SaveAsync(archiveKey, archive, cancellationToken: cancellationToken);

        // Also update the namespace archive list (with ETag protection)
        var listKey = $"{ARCHIVE_KEY_PREFIX}list:{archive.Namespace}";

        const int maxRetries = 3;
        for (int retry = 0; retry < maxRetries; retry++)
        {
            var (archiveIds, etag) = await _guidListStore.GetWithETagAsync(listKey, cancellationToken);
            archiveIds ??= [];

            if (archiveIds.Contains(archive.ArchiveId)) break;

            archiveIds.Add(archive.ArchiveId);

            string? savedEtag;
            if (etag != null)
            {
                savedEtag = await _guidListStore.TrySaveAsync(listKey, archiveIds, etag, cancellationToken: cancellationToken);
            }
            else
            {
                savedEtag = await _guidListStore.SaveAsync(listKey, archiveIds, cancellationToken: cancellationToken);
            }

            if (savedEtag != null) break;

            _logger.LogDebug("Archive list update for namespace {Namespace} had ETag conflict, retrying ({Retry}/{MaxRetries})",
                archive.Namespace, retry + 1, maxRetries);
        }
    }

    /// <summary>
    /// Gets all archives for a namespace.
    /// </summary>
    private async Task<List<Models.DocumentationArchive>> GetArchivesForNamespaceAsync(string namespaceId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.GetArchivesForNamespaceAsync");
        var listKey = $"{ARCHIVE_KEY_PREFIX}list:{namespaceId}";
        var archiveIds = await _guidListStore.GetAsync(listKey, cancellationToken);

        if (archiveIds == null || archiveIds.Count == 0)
        {
            return [];
        }

        var archives = new List<Models.DocumentationArchive>();

        foreach (var archiveId in archiveIds)
        {
            var archiveKey = $"{ARCHIVE_KEY_PREFIX}{archiveId}";
            var archive = await _archiveStore.GetAsync(archiveKey, cancellationToken);
            if (archive != null)
            {
                archives.Add(archive);
            }
        }

        return archives;
    }

    /// <summary>
    /// Gets an archive by ID.
    /// </summary>
    private async Task<Models.DocumentationArchive?> GetArchiveByIdAsync(Guid archiveId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.GetArchiveByIdAsync");
        var archiveKey = $"{ARCHIVE_KEY_PREFIX}{archiveId}";
        return await _archiveStore.GetAsync(archiveKey, cancellationToken);
    }

    /// <summary>
    /// Restores documents from a bundle.
    /// </summary>
    private async Task<int> RestoreFromBundleAsync(string namespaceId, byte[] bundleData, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.RestoreFromBundleAsync");
        // Decompress and deserialize the bundle
        using var input = new MemoryStream(bundleData);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        var json = await reader.ReadToEndAsync(cancellationToken);

        var bundle = BannouJson.Deserialize<DocumentationBundle>(json);
        if (bundle?.Documents == null || bundle.Documents.Count == 0)
        {
            _logger.LogWarning("Archive bundle contains no documents");
            return 0;
        }

        // Delete existing documents in namespace
        await DeleteAllNamespaceDocumentsAsync(namespaceId, cancellationToken);

        // Import all documents from bundle
        var docsKey = $"{NAMESPACE_DOCS_PREFIX}{namespaceId}";
        var docIds = new HashSet<Guid>();

        foreach (var bundledDoc in bundle.Documents)
        {
            var storedDoc = new StoredDocument
            {
                DocumentId = bundledDoc.DocumentId,
                Namespace = namespaceId,
                Slug = bundledDoc.Slug,
                Title = bundledDoc.Title,
                Content = bundledDoc.Content,
                Category = bundledDoc.Category,
                Summary = bundledDoc.Summary,
                VoiceSummary = bundledDoc.VoiceSummary,
                Tags = bundledDoc.Tags ?? [],
                Metadata = bundledDoc.Metadata,
                CreatedAt = bundledDoc.CreatedAt,
                UpdatedAt = DateTimeOffset.UtcNow // Mark as updated on restore
            };

            var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{storedDoc.DocumentId}";
            await _docStore.SaveAsync(docKey, storedDoc, cancellationToken: cancellationToken);

            var slugKey = $"{SLUG_INDEX_PREFIX}{namespaceId}:{storedDoc.Slug}";
            await _stringStore.SaveAsync(slugKey, storedDoc.DocumentId.ToString(), cancellationToken: cancellationToken);

            docIds.Add(storedDoc.DocumentId);

            // Index for search
            _searchIndexService.IndexDocument(
                namespaceId,
                storedDoc.DocumentId,
                storedDoc.Title,
                storedDoc.Slug,
                storedDoc.Content,
                storedDoc.Category,
                storedDoc.Tags);
        }

        await _guidSetStore.SaveAsync(docsKey, docIds, cancellationToken: cancellationToken);

        // Track namespace last-updated timestamp once for the restore batch
        if (bundle.Documents.Count > 0)
        {
            await UpdateNamespaceLastUpdatedAsync(namespaceId, DateTimeOffset.UtcNow, cancellationToken);
        }

        return bundle.Documents.Count;
    }

    /// <summary>
    /// Deletes an archive record from state store.
    /// </summary>
    private async Task DeleteArchiveAsync(Models.DocumentationArchive archive, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.DeleteArchiveAsync");
        var archiveKey = $"{ARCHIVE_KEY_PREFIX}{archive.ArchiveId}";
        await _archiveStore.DeleteAsync(archiveKey, cancellationToken);

        // Remove from namespace archive list
        var listKey = $"{ARCHIVE_KEY_PREFIX}list:{archive.Namespace}";
        var archiveIds = await _guidListStore.GetAsync(listKey, cancellationToken);
        if (archiveIds != null)
        {
            archiveIds.Remove(archive.ArchiveId);
            await _guidListStore.SaveAsync(listKey, archiveIds, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Publishes an archive created event.
    /// </summary>
    private async Task TryPublishArchiveCreatedEventAsync(Models.DocumentationArchive archive, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.TryPublishArchiveCreatedEventAsync");
        try
        {
            var eventModel = new DocumentationArchiveCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Namespace = archive.Namespace,
                ArchiveId = archive.ArchiveId,
                BundleAssetId = archive.BundleAssetId,
                DocumentCount = archive.DocumentCount,
                SizeBytes = (int)Math.Min(archive.SizeBytes, int.MaxValue)
            };
            await _messageBus.PublishDocumentationArchiveCreatedAsync(eventModel, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish archive created event");
        }
    }

    /// <summary>
    /// Publishes an archive deleted event.
    /// </summary>
    private async Task TryPublishArchiveDeletedEventAsync(Models.DocumentationArchive archive, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.TryPublishArchiveDeletedEventAsync");
        try
        {
            var eventModel = new DocumentationArchiveDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Namespace = archive.Namespace,
                ArchiveId = archive.ArchiveId
            };
            await _messageBus.PublishDocumentationArchiveDeletedAsync(eventModel, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish archive deleted event");
        }
    }

    #endregion
}
