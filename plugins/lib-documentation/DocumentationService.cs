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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Documentation;

/// <summary>
/// State-store implementation for Documentation service following schema-first architecture.
/// Uses IStateStoreFactory for persistence.
/// </summary>
[BannouService("documentation", typeof(IDocumentationService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.AppFeatures)]
public partial class DocumentationService : IDocumentationService
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<DocumentationService> _logger;
    private readonly DocumentationServiceConfiguration _configuration;
    private readonly ISearchIndexService _searchIndexService;
    private readonly IGitSyncService _gitSyncService;
    private readonly IContentTransformService _contentTransformService;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IAssetClient _assetClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITelemetryProvider _telemetryProvider;

    // Event topics following QUALITY TENETS: {entity}.{action} pattern
    private const string DOCUMENT_CREATED_TOPIC = "document.created";
    private const string DOCUMENT_UPDATED_TOPIC = "document.updated";
    private const string DOCUMENT_DELETED_TOPIC = "document.deleted";
    private const string BINDING_CREATED_TOPIC = "documentation.binding.created";
    private const string BINDING_REMOVED_TOPIC = "documentation.binding.removed";
    private const string SYNC_STARTED_TOPIC = "documentation.sync.started";
    private const string SYNC_COMPLETED_TOPIC = "documentation.sync.completed";
    private const string ARCHIVE_CREATED_TOPIC = "documentation.archive.created";

    // State store key prefixes per plan specification
    // NOTE: DOC_KEY_PREFIX is empty because the store already prefixes with "doc:" via KeyPrefix
    // config in StateServicePlugin. Final Redis keys become: doc:{namespaceId}:{documentId}
    private const string DOC_KEY_PREFIX = "";
    private const string SLUG_INDEX_PREFIX = "slug-idx:";
    private const string NAMESPACE_DOCS_PREFIX = "ns-docs:";
    private const string TRASH_KEY_PREFIX = "trash:";
    private const string BINDING_KEY_PREFIX = "repo-binding:";
    private const string BINDINGS_REGISTRY_KEY = "repo-bindings";
    private const string ARCHIVE_KEY_PREFIX = "archive:";
    private const string SYNC_LOCK_PREFIX = "repo-sync:";
    private const string ALL_NAMESPACES_KEY = "all-namespaces";

    // Static search result cache shared across scoped instances (performance optimization, not authoritative state)
    private static readonly SearchResultCache _searchCache = new();

    /// <summary>
    /// Creates a new instance of the DocumentationService.
    /// </summary>
    public DocumentationService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        ILogger<DocumentationService> logger,
        DocumentationServiceConfiguration configuration,
        IEventConsumer eventConsumer,
        ISearchIndexService searchIndexService,
        IGitSyncService gitSyncService,
        IContentTransformService contentTransformService,
        IDistributedLockProvider lockProvider,
        IAssetClient assetClient,
        IHttpClientFactory httpClientFactory,
        ITelemetryProvider telemetryProvider)
    {
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        _searchIndexService = searchIndexService;
        _gitSyncService = gitSyncService;
        _contentTransformService = contentTransformService;
        _lockProvider = lockProvider;
        _assetClient = assetClient;
        _httpClientFactory = httpClientFactory;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;

        // Register event handlers via partial class (minimal event subscriptions per schema)
        RegisterEventConsumers(eventConsumer);
    }

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
        var slugStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Documentation);
        var documentIdStr = await slugStore.GetAsync(slugKey, cancellationToken);
        if (string.IsNullOrEmpty(documentIdStr) || !Guid.TryParse(documentIdStr, out var documentId))
        {
            _logger.LogDebug("Document with slug {Slug} not found in namespace {Namespace}", slug, namespaceId);
            return (StatusCodes.NotFound, null);
        }

        // Fetch document content
        var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{documentId}";
        var docStore = _stateStoreFactory.GetStore<StoredDocument>(StateStoreDefinitions.Documentation);
        var storedDoc = await docStore.GetAsync(docKey, cancellationToken);
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
        <span>Category: {System.Web.HttpUtility.HtmlEncode(storedDoc.Category)}</span> |
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

    /// <inheritdoc />
    public async Task<(StatusCodes, QueryDocumentationResponse?)> QueryDocumentationAsync(QueryDocumentationRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("QueryDocumentation: namespace={Namespace}, query={Query}", body.Namespace, body.Query);
        // Input validation
        if (string.IsNullOrWhiteSpace(body.Namespace))
        {
            _logger.LogWarning("QueryDocumentation failed: Namespace is required");
            return (StatusCodes.BadRequest, null);
        }

        if (string.IsNullOrWhiteSpace(body.Query))
        {
            _logger.LogWarning("QueryDocumentation failed: Query is required");
            return (StatusCodes.BadRequest, null);
        }

        var namespaceId = body.Namespace;
        var maxResults = Math.Min(body.MaxResults, _configuration.MaxSearchResults);
        var minRelevance = Math.Max(body.MinRelevanceScore, _configuration.MinRelevanceScore);

        // Perform natural language query using search index
        // Pass category as string for filtering (or null if default enum value)
        var categoryFilter = body.Category == default ? null : body.Category.ToString();
        var searchResults = await _searchIndexService.QueryAsync(
            namespaceId,
            body.Query,
            categoryFilter,
            maxResults,
            minRelevance,
            cancellationToken);

        // Build response with document results
        var results = new List<DocumentResult>();
        var docStore = _stateStoreFactory.GetStore<StoredDocument>(StateStoreDefinitions.Documentation);
        foreach (var result in searchResults)
        {
            var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{result.DocumentId}";
            var doc = await docStore.GetAsync(docKey, cancellationToken);

            if (doc != null)
            {
                results.Add(new DocumentResult
                {
                    DocumentId = doc.DocumentId,
                    Slug = doc.Slug,
                    Title = doc.Title,
                    Category = ParseDocumentCategory(doc.Category),
                    Summary = doc.Summary,
                    VoiceSummary = doc.VoiceSummary,
                    RelevanceScore = (float)result.RelevanceScore
                });
            }
        }

        // Publish analytics event (non-blocking)
        var topResult = results.FirstOrDefault();
        _ = PublishQueryAnalyticsEventAsync(namespaceId, body.Query, body.SessionId, results.Count,
            topResult?.DocumentId, topResult?.RelevanceScore);

        _logger.LogInformation("Query in namespace {Namespace} returned {Count} results", namespaceId, results.Count);

        return (StatusCodes.OK, new QueryDocumentationResponse
        {
            Results = results,
            TotalResults = results.Count
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, GetDocumentResponse?)> GetDocumentAsync(GetDocumentRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("GetDocument: namespace={Namespace}, documentId={DocumentId}, slug={Slug}",
            body.Namespace, body.DocumentId, body.Slug);
        // Input validation
        if (string.IsNullOrWhiteSpace(body.Namespace))
        {
            _logger.LogWarning("GetDocument failed: Namespace is required");
            return (StatusCodes.BadRequest, null);
        }

        if ((!body.DocumentId.HasValue || body.DocumentId.Value == Guid.Empty) && string.IsNullOrWhiteSpace(body.Slug))
        {
            _logger.LogWarning("GetDocument failed: Either DocumentId or Slug is required");
            return (StatusCodes.BadRequest, null);
        }

        var namespaceId = body.Namespace;
        Guid documentId;
        var slugStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Documentation);
        var docStore = _stateStoreFactory.GetStore<StoredDocument>(StateStoreDefinitions.Documentation);

        // Resolve document ID from slug if provided
        if (body.DocumentId.HasValue && body.DocumentId.Value != Guid.Empty)
        {
            documentId = body.DocumentId.Value;
        }
        else
        {
            // Use slug (already validated as non-empty above)
            var slugKey = $"{SLUG_INDEX_PREFIX}{namespaceId}:{body.Slug}";
            var resolvedIdStr = await slugStore.GetAsync(slugKey, cancellationToken);
            if (string.IsNullOrEmpty(resolvedIdStr) || !Guid.TryParse(resolvedIdStr, out documentId))
            {
                _logger.LogDebug("Document with slug {Slug} not found in namespace {Namespace}", body.Slug, namespaceId);
                return (StatusCodes.NotFound, null);
            }
        }

        // Fetch document from state store
        var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{documentId}";
        var storedDoc = await docStore.GetAsync(docKey, cancellationToken);
        if (storedDoc == null)
        {
            _logger.LogDebug("Document {DocumentId} not found in namespace {Namespace}", documentId, namespaceId);
            return (StatusCodes.NotFound, null);
        }

        // Build response document
        var doc = new Document
        {
            DocumentId = storedDoc.DocumentId,
            Namespace = storedDoc.Namespace,
            Slug = storedDoc.Slug,
            Title = storedDoc.Title,
            Category = ParseDocumentCategory(storedDoc.Category),
            Summary = storedDoc.Summary,
            VoiceSummary = storedDoc.VoiceSummary,
            Tags = storedDoc.Tags,
            RelatedDocuments = storedDoc.RelatedDocuments,
            Metadata = storedDoc.Metadata ?? new object(),
            CreatedAt = storedDoc.CreatedAt,
            UpdatedAt = storedDoc.UpdatedAt
        };

        // Include content if requested
        if (body.IncludeContent)
        {
            // Content should never be null for a stored document - this is a data integrity issue
            var content = storedDoc.Content;
            if (string.IsNullOrEmpty(content))
            {
                _logger.LogError("Document {DocumentId} in namespace {Namespace} has null/empty Content - data integrity issue", documentId, namespaceId);
                await _messageBus.TryPublishErrorAsync(
                    serviceName: "documentation",
                    operation: "GetDocument",
                    errorType: "DataIntegrityError",
                    message: "Stored document has null/empty Content",
                    details: new { DocumentId = documentId, Namespace = namespaceId },
                    cancellationToken: cancellationToken);
                return (StatusCodes.InternalServerError, null);
            }
            // content is guaranteed non-null after the check above
            doc.Content = body.RenderHtml ? RenderMarkdownToHtml(content) ?? content : content;
        }

        var response = new GetDocumentResponse
        {
            Document = doc,
            ContentFormat = body.IncludeContent
                ? (body.RenderHtml ? ContentFormat.Html : ContentFormat.Markdown)
                : ContentFormat.None
        };

        // Include related documents if requested
        var includeRelated = body.IncludeRelated ?? RelatedDepth.None;
        if (includeRelated != RelatedDepth.None && storedDoc.RelatedDocuments.Count > 0)
        {
            response.RelatedDocuments = await GetRelatedDocumentSummariesAsync(
                namespaceId,
                storedDoc.RelatedDocuments,
                includeRelated,
                cancellationToken);
        }

        _logger.LogDebug("Retrieved document {DocumentId} from namespace {Namespace}", documentId, namespaceId);
        return (StatusCodes.OK, response);
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, SearchDocumentationResponse?)> SearchDocumentationAsync(SearchDocumentationRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("SearchDocumentation: namespace={Namespace}, term={Term}", body.Namespace, body.SearchTerm);
        // Input validation
        if (string.IsNullOrWhiteSpace(body.Namespace))
        {
            _logger.LogWarning("SearchDocumentation failed: Namespace is required");
            return (StatusCodes.BadRequest, null);
        }

        if (string.IsNullOrWhiteSpace(body.SearchTerm))
        {
            _logger.LogWarning("SearchDocumentation failed: SearchTerm is required");
            return (StatusCodes.BadRequest, null);
        }

        var namespaceId = body.Namespace;
        var maxResults = Math.Min(body.MaxResults, _configuration.MaxSearchResults);
        var categoryFilter = body.Category == default ? null : body.Category.ToString();

        // Check search cache
        var cacheKey = SearchResultCache.BuildKey(namespaceId, body.SearchTerm, categoryFilter, maxResults);
        if (_configuration.SearchCacheTtlSeconds > 0 &&
            _searchCache.TryGet(cacheKey, out var cachedResponse))
        {
            return (StatusCodes.OK, cachedResponse);
        }

        // Perform keyword search using search index
        var searchResults = await _searchIndexService.SearchAsync(
            namespaceId,
            body.SearchTerm,
            categoryFilter,
            maxResults,
            cancellationToken);

        // Build response with document results (track CreatedAt for recency sorting)
        var resultsWithMetadata = new List<(DocumentResult Result, DateTimeOffset CreatedAt)>();
        var docStore = _stateStoreFactory.GetStore<StoredDocument>(StateStoreDefinitions.Documentation);
        foreach (var result in searchResults)
        {
            var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{result.DocumentId}";
            var doc = await docStore.GetAsync(docKey, cancellationToken);

            if (doc != null)
            {
                resultsWithMetadata.Add((new DocumentResult
                {
                    DocumentId = doc.DocumentId,
                    Slug = doc.Slug,
                    Title = doc.Title,
                    Category = ParseDocumentCategory(doc.Category),
                    Summary = doc.Summary,
                    VoiceSummary = doc.VoiceSummary,
                    RelevanceScore = (float)result.RelevanceScore,
                    MatchHighlights = new List<string> { GenerateSearchSnippet(doc.Content, body.SearchTerm, _configuration.SearchSnippetLength) }
                }, doc.CreatedAt));
            }
        }

        // Filter out results below minimum relevance score
        if (_configuration.MinRelevanceScore > 0)
        {
            resultsWithMetadata = resultsWithMetadata
                .Where(r => r.Result.RelevanceScore >= _configuration.MinRelevanceScore)
                .ToList();
        }

        // Apply sorting based on request
        IEnumerable<(DocumentResult Result, DateTimeOffset CreatedAt)> sortedResults = body.SortBy switch
        {
            SearchSortBy.Relevance => resultsWithMetadata, // Already sorted by relevance from search engine
            SearchSortBy.Recency => resultsWithMetadata.OrderByDescending(r => r.CreatedAt),
            SearchSortBy.Alphabetical => resultsWithMetadata.OrderBy(r => r.Result.Title, StringComparer.OrdinalIgnoreCase),
            _ => resultsWithMetadata
        };

        var finalResults = sortedResults.Select(r => r.Result).ToList();

        // Publish analytics event (non-blocking)
        _ = PublishSearchAnalyticsEventAsync(namespaceId, body.SearchTerm, body.SessionId, finalResults.Count);

        _logger.LogInformation("Search in namespace {Namespace} for '{Term}' returned {Count} results (sorted by {SortBy})",
            namespaceId, body.SearchTerm, finalResults.Count, body.SortBy);

        var response = new SearchDocumentationResponse
        {
            Results = finalResults,
            TotalResults = finalResults.Count
        };

        // Cache results
        if (_configuration.SearchCacheTtlSeconds > 0)
        {
            _searchCache.Set(cacheKey, response, TimeSpan.FromSeconds(_configuration.SearchCacheTtlSeconds));
        }

        return (StatusCodes.OK, response);
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ListDocumentsResponse?)> ListDocumentsAsync(ListDocumentsRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("ListDocuments: namespace={Namespace}, category={Category}, sortBy={SortBy}, sortOrder={SortOrder}",
            body.Namespace, body.Category, body.SortBy, body.SortOrder);
        // Input validation
        if (string.IsNullOrWhiteSpace(body.Namespace))
        {
            _logger.LogWarning("ListDocuments failed: Namespace is required");
            return (StatusCodes.BadRequest, null);
        }

        var namespaceId = body.Namespace;
        var page = body.Page;
        var pageSize = body.PageSize;
        var requestedTags = body.Tags?.ToList() ?? new List<string>();
        var hasTags = requestedTags.Count > 0;

        // If filtering by tags or sorting, we need to fetch all documents first, then filter/sort/paginate
        // Otherwise, use the optimized path
        var categoryFilter = body.Category == default ? null : body.Category.ToString();
        var docStore = _stateStoreFactory.GetStore<StoredDocument>(StateStoreDefinitions.Documentation);

        // Fetch more documents if we need to filter/sort (configurable max to prevent memory issues)
        var fetchLimit = hasTags || body.SortBy != default ? _configuration.MaxFetchLimit : pageSize;
        var docIds = await _searchIndexService.ListDocumentIdsAsync(
            namespaceId,
            categoryFilter,
            0, // Always start from 0 when sorting/filtering
            fetchLimit,
            cancellationToken);

        // Fetch document data with metadata for sorting using bulk get (fixes N+1 query pattern)
        var documentsWithMetadata = new List<(DocumentSummary Summary, StoredDocument Doc)>();
        if (docIds.Count > 0)
        {
            var docKeys = docIds.Select(docId => $"{DOC_KEY_PREFIX}{namespaceId}:{docId}").ToList();
            var bulkResult = await docStore.GetBulkAsync(docKeys, cancellationToken);

            foreach (var (_, doc) in bulkResult)
            {
                if (doc != null)
                {
                    documentsWithMetadata.Add((new DocumentSummary
                    {
                        DocumentId = doc.DocumentId,
                        Slug = doc.Slug,
                        Title = doc.Title,
                        Category = ParseDocumentCategory(doc.Category),
                        Summary = doc.Summary,
                        VoiceSummary = doc.VoiceSummary,
                        Tags = doc.Tags
                    }, doc));
                }
            }
        }

        // Apply tags filter if specified
        if (hasTags)
        {
            documentsWithMetadata = body.TagsMatch switch
            {
                TagMatchMode.All => documentsWithMetadata
                    .Where(d => requestedTags.All(t => d.Doc.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)))
                    .ToList(),
                TagMatchMode.Any => documentsWithMetadata
                    .Where(d => requestedTags.Any(t => d.Doc.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)))
                    .ToList(),
                _ => documentsWithMetadata
            };
        }

        // Apply sorting
        IEnumerable<(DocumentSummary Summary, StoredDocument Doc)> sortedDocuments = body.SortBy switch
        {
            ListSortField.CreatedAt => body.SortOrder == SortOrder.Asc
                ? documentsWithMetadata.OrderBy(d => d.Doc.CreatedAt)
                : documentsWithMetadata.OrderByDescending(d => d.Doc.CreatedAt),
            ListSortField.UpdatedAt => body.SortOrder == SortOrder.Asc
                ? documentsWithMetadata.OrderBy(d => d.Doc.UpdatedAt)
                : documentsWithMetadata.OrderByDescending(d => d.Doc.UpdatedAt),
            ListSortField.Title => body.SortOrder == SortOrder.Asc
                ? documentsWithMetadata.OrderBy(d => d.Doc.Title, StringComparer.OrdinalIgnoreCase)
                : documentsWithMetadata.OrderByDescending(d => d.Doc.Title, StringComparer.OrdinalIgnoreCase),
            _ => documentsWithMetadata // Default: no additional sorting
        };

        // Get filtered count for pagination
        var filteredList = sortedDocuments.ToList();
        var totalCount = filteredList.Count;

        // Apply pagination
        var skip = (page - 1) * pageSize;
        var documents = filteredList.Skip(skip).Take(pageSize).Select(d => d.Summary).ToList();

        // Calculate total pages
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        _logger.LogDebug("ListDocuments returned {Count} of {Total} documents in namespace {Namespace} (filtered by tags: {HasTags})",
            documents.Count, totalCount, namespaceId, hasTags);

        return (StatusCodes.OK, new ListDocumentsResponse
        {
            Documents = documents,
            TotalCount = totalCount,
            TotalPages = totalPages
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, SuggestRelatedResponse?)> SuggestRelatedTopicsAsync(SuggestRelatedRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("SuggestRelatedTopics: namespace={Namespace}, source={SourceValue}", body.Namespace, body.SourceValue);
        var namespaceId = body.Namespace;
        var maxSuggestions = body.MaxSuggestions;

        if (string.IsNullOrEmpty(body.SourceValue))
        {
            return (StatusCodes.BadRequest, null);
        }

        // Get related document IDs from search index
        var relatedIds = await _searchIndexService.GetRelatedSuggestionsAsync(
            namespaceId,
            body.SourceValue,
            maxSuggestions,
            cancellationToken);

        // Fetch document summaries and build topic suggestions
        var suggestions = new List<TopicSuggestion>();
        var docStore = _stateStoreFactory.GetStore<StoredDocument>(StateStoreDefinitions.Documentation);
        foreach (var docId in relatedIds)
        {
            var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{docId}";
            var doc = await docStore.GetAsync(docKey, cancellationToken);

            if (doc != null)
            {
                suggestions.Add(new TopicSuggestion
                {
                    DocumentId = doc.DocumentId,
                    Slug = doc.Slug,
                    Title = doc.Title,
                    Category = ParseDocumentCategory(doc.Category),
                    RelevanceReason = DetermineRelevanceReason(body.SuggestionSource, body.SourceValue, doc)
                });
            }
        }

        _logger.LogDebug("SuggestRelatedTopics for '{Source}' returned {Count} suggestions",
            body.SourceValue, suggestions.Count);

        return (StatusCodes.OK, new SuggestRelatedResponse
        {
            Suggestions = suggestions,
            Namespace = namespaceId
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, CreateDocumentResponse?)> CreateDocumentAsync(CreateDocumentRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("CreateDocument: namespace={Namespace}, slug={Slug}", body.Namespace, body.Slug);
        // Input validation
        if (string.IsNullOrWhiteSpace(body.Namespace))
        {
            _logger.LogWarning("CreateDocument failed: Namespace is required");
            return (StatusCodes.BadRequest, null);
        }

        if (string.IsNullOrWhiteSpace(body.Slug))
        {
            _logger.LogWarning("CreateDocument failed: Slug is required");
            return (StatusCodes.BadRequest, null);
        }

        if (string.IsNullOrWhiteSpace(body.Title))
        {
            _logger.LogWarning("CreateDocument failed: Title is required");
            return (StatusCodes.BadRequest, null);
        }

        // Validate content size against configuration limit
        if (body.Content != null && System.Text.Encoding.UTF8.GetByteCount(body.Content) > _configuration.MaxContentSizeBytes)
        {
            _logger.LogWarning("CreateDocument failed: Content size exceeds maximum of {MaxBytes} bytes",
                _configuration.MaxContentSizeBytes);
            return (StatusCodes.BadRequest, null);
        }

        // Check if namespace is bound to a repository (403 for manual modifications)
        var binding = await GetBindingForNamespaceAsync(body.Namespace, cancellationToken);
        if (binding != null && binding.Status != Models.BindingStatusInternal.Disabled)
        {
            _logger.LogWarning("CreateDocument rejected: namespace {Namespace} is bound to repository", body.Namespace);
            return (StatusCodes.Forbidden, null);
        }

        var namespaceId = body.Namespace;
        var slug = body.Slug;
        var slugStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Documentation);
        var docStore = _stateStoreFactory.GetStore<StoredDocument>(StateStoreDefinitions.Documentation);

        // Check if slug already exists in this namespace
        var slugKey = $"{SLUG_INDEX_PREFIX}{namespaceId}:{slug}";
        var existingDocIdStr = await slugStore.GetAsync(slugKey, cancellationToken);
        if (!string.IsNullOrEmpty(existingDocIdStr))
        {
            _logger.LogWarning("Document with slug {Slug} already exists in namespace {Namespace}", slug, namespaceId);
            return (StatusCodes.Conflict, null);
        }

        // Generate new document ID and timestamps
        var documentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Create stored document model
        var storedDoc = new StoredDocument
        {
            DocumentId = documentId,
            Namespace = namespaceId,
            Slug = slug,
            Title = body.Title,
            Category = body.Category.ToString(),
            Content = body.Content,
            Summary = body.Summary,
            VoiceSummary = TruncateVoiceSummary(body.VoiceSummary),
            Tags = body.Tags?.ToList() ?? [],
            RelatedDocuments = body.RelatedDocuments?.ToList() ?? [],
            Metadata = body.Metadata,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Ensure search index exists BEFORE saving (Redis Search only auto-indexes new documents)
        await _searchIndexService.EnsureIndexExistsAsync(namespaceId, cancellationToken);

        // Save document to state store
        var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{documentId}";
        await docStore.SaveAsync(docKey, storedDoc, cancellationToken: cancellationToken);

        // Create slug index entry
        await slugStore.SaveAsync(slugKey, documentId.ToString(), cancellationToken: cancellationToken);

        // Add to namespace document list
        await AddDocumentToNamespaceIndexAsync(namespaceId, documentId, cancellationToken);

        // Index for search
        _searchIndexService.IndexDocument(
            namespaceId,
            documentId,
            storedDoc.Title,
            storedDoc.Slug,
            storedDoc.Content,
            storedDoc.Category,
            storedDoc.Tags);

        // Publish lifecycle event
        await PublishDocumentCreatedEventAsync(storedDoc, cancellationToken);

        _logger.LogInformation("Created document {DocumentId} with slug {Slug} in namespace {Namespace}",
            documentId, slug, namespaceId);

        return (StatusCodes.OK, new CreateDocumentResponse
        {
            DocumentId = documentId,
            Slug = slug,
            CreatedAt = now
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, UpdateDocumentResponse?)> UpdateDocumentAsync(UpdateDocumentRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("UpdateDocument: namespace={Namespace}, documentId={DocumentId}", body.Namespace, body.DocumentId);
        var namespaceId = body.Namespace;
        var documentId = body.DocumentId;

        if (documentId == Guid.Empty)
        {
            _logger.LogWarning("UpdateDocument requires documentId");
            return (StatusCodes.BadRequest, null);
        }

        // Check if namespace is bound to a repository (403 for manual modifications)
        var binding = await GetBindingForNamespaceAsync(namespaceId, cancellationToken);
        if (binding != null && binding.Status != Models.BindingStatusInternal.Disabled)
        {
            _logger.LogWarning("UpdateDocument rejected: namespace {Namespace} is bound to repository", namespaceId);
            return (StatusCodes.Forbidden, null);
        }

        var slugStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Documentation);
        var docStore = _stateStoreFactory.GetStore<StoredDocument>(StateStoreDefinitions.Documentation);

        // Fetch existing document
        var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{documentId}";
        var storedDoc = await docStore.GetAsync(docKey, cancellationToken);
        if (storedDoc == null)
        {
            _logger.LogDebug("Document {DocumentId} not found in namespace {Namespace}", documentId, namespaceId);
            return (StatusCodes.NotFound, null);
        }

        var changedFields = new List<string>();
        var oldSlug = storedDoc.Slug;
        var now = DateTimeOffset.UtcNow;

        // Apply updates for each provided field
        if (!string.IsNullOrEmpty(body.Slug) && body.Slug != storedDoc.Slug)
        {
            // Check new slug doesn't conflict
            var newSlugKey = $"{SLUG_INDEX_PREFIX}{namespaceId}:{body.Slug}";
            var conflictIdStr = await slugStore.GetAsync(newSlugKey, cancellationToken);
            if (!string.IsNullOrEmpty(conflictIdStr) && Guid.TryParse(conflictIdStr, out var conflictId) && conflictId != documentId)
            {
                _logger.LogWarning("Slug {Slug} already exists in namespace {Namespace}", body.Slug, namespaceId);
                return (StatusCodes.Conflict, null);
            }
            storedDoc.Slug = body.Slug;
            changedFields.Add("slug");
        }

        if (!string.IsNullOrEmpty(body.Title) && body.Title != storedDoc.Title)
        {
            storedDoc.Title = body.Title;
            changedFields.Add("title");
        }

        if (body.Category.HasValue && body.Category.Value.ToString() != storedDoc.Category)
        {
            storedDoc.Category = body.Category.Value.ToString();
            changedFields.Add("category");
        }

        if (!string.IsNullOrEmpty(body.Content) && body.Content != storedDoc.Content)
        {
            if (System.Text.Encoding.UTF8.GetByteCount(body.Content) > _configuration.MaxContentSizeBytes)
            {
                _logger.LogWarning("UpdateDocument failed: Content size exceeds maximum of {MaxBytes} bytes",
                    _configuration.MaxContentSizeBytes);
                return (StatusCodes.BadRequest, null);
            }
            storedDoc.Content = body.Content;
            changedFields.Add("content");
        }

        if (body.Summary != null && body.Summary != storedDoc.Summary)
        {
            storedDoc.Summary = body.Summary;
            changedFields.Add("summary");
        }

        if (body.VoiceSummary != null && body.VoiceSummary != storedDoc.VoiceSummary)
        {
            storedDoc.VoiceSummary = TruncateVoiceSummary(body.VoiceSummary);
            changedFields.Add("voiceSummary");
        }

        if (body.Tags != null)
        {
            storedDoc.Tags = body.Tags.ToList();
            changedFields.Add("tags");
        }

        if (body.RelatedDocuments != null)
        {
            storedDoc.RelatedDocuments = body.RelatedDocuments.ToList();
            changedFields.Add("relatedDocuments");
        }

        if (body.Metadata != null)
        {
            storedDoc.Metadata = body.Metadata;
            changedFields.Add("metadata");
        }

        // Update timestamp
        storedDoc.UpdatedAt = now;

        // Save updated document
        await docStore.SaveAsync(docKey, storedDoc, cancellationToken: cancellationToken);

        // Update slug index if changed
        if (changedFields.Contains("slug"))
        {
            // Remove old slug index
            var oldSlugKey = $"{SLUG_INDEX_PREFIX}{namespaceId}:{oldSlug}";
            await slugStore.DeleteAsync(oldSlugKey, cancellationToken);

            // Add new slug index
            var newSlugKey = $"{SLUG_INDEX_PREFIX}{namespaceId}:{storedDoc.Slug}";
            await slugStore.SaveAsync(newSlugKey, documentId.ToString(), cancellationToken: cancellationToken);
        }

        // Update search index
        _searchIndexService.IndexDocument(
            namespaceId,
            documentId,
            storedDoc.Title,
            storedDoc.Slug,
            storedDoc.Content,
            storedDoc.Category,
            storedDoc.Tags);

        // Publish lifecycle event
        await PublishDocumentUpdatedEventAsync(storedDoc, changedFields, cancellationToken);

        _logger.LogInformation("Updated document {DocumentId} in namespace {Namespace}, changed fields: {ChangedFields}",
            documentId, namespaceId, string.Join(", ", changedFields));

        return (StatusCodes.OK, new UpdateDocumentResponse
        {
            DocumentId = documentId,
            UpdatedAt = now
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, DeleteDocumentResponse?)> DeleteDocumentAsync(DeleteDocumentRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("DeleteDocument: namespace={Namespace}, documentId={DocumentId}, slug={Slug}",
            body.Namespace, body.DocumentId, body.Slug);
        // Input validation
        if (string.IsNullOrWhiteSpace(body.Namespace))
        {
            _logger.LogWarning("DeleteDocument failed: Namespace is required");
            return (StatusCodes.BadRequest, null);
        }

        if ((!body.DocumentId.HasValue || body.DocumentId.Value == Guid.Empty) && string.IsNullOrWhiteSpace(body.Slug))
        {
            _logger.LogWarning("DeleteDocument failed: Either DocumentId or Slug is required");
            return (StatusCodes.BadRequest, null);
        }

        // Check if namespace is bound to a repository (403 for manual modifications)
        var binding = await GetBindingForNamespaceAsync(body.Namespace, cancellationToken);
        if (binding != null && binding.Status != Models.BindingStatusInternal.Disabled)
        {
            _logger.LogWarning("DeleteDocument rejected: namespace {Namespace} is bound to repository", body.Namespace);
            return (StatusCodes.Forbidden, null);
        }

        var namespaceId = body.Namespace;
        Guid documentId;
        var slugStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Documentation);
        var docStore = _stateStoreFactory.GetStore<StoredDocument>(StateStoreDefinitions.Documentation);
        var trashStore = _stateStoreFactory.GetStore<TrashedDocument>(StateStoreDefinitions.Documentation);

        // Resolve document ID from slug if provided
        if (body.DocumentId.HasValue && body.DocumentId.Value != Guid.Empty)
        {
            documentId = body.DocumentId.Value;
        }
        else
        {
            // Use slug (already validated as non-empty above)
            var slugKey = $"{SLUG_INDEX_PREFIX}{namespaceId}:{body.Slug}";
            var resolvedIdStr = await slugStore.GetAsync(slugKey, cancellationToken);
            if (string.IsNullOrEmpty(resolvedIdStr) || !Guid.TryParse(resolvedIdStr, out documentId))
            {
                _logger.LogDebug("Document with slug {Slug} not found in namespace {Namespace}", body.Slug, namespaceId);
                return (StatusCodes.NotFound, null);
            }
        }

        // Fetch existing document
        var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{documentId}";
        var storedDoc = await docStore.GetAsync(docKey, cancellationToken);
        if (storedDoc == null)
        {
            _logger.LogDebug("Document {DocumentId} not found in namespace {Namespace}", documentId, namespaceId);
            return (StatusCodes.NotFound, null);
        }

        var now = DateTimeOffset.UtcNow;
        var trashcanTtl = TimeSpan.FromDays(_configuration.TrashcanTtlDays);
        var expiresAt = now.Add(trashcanTtl);

        // Create trashcan entry with TTL metadata
        var trashedDoc = new TrashedDocument
        {
            Document = storedDoc,
            DeletedAt = now,
            ExpiresAt = expiresAt
        };

        // Save to trashcan
        var trashKey = $"{TRASH_KEY_PREFIX}{namespaceId}:{documentId}";
        await trashStore.SaveAsync(trashKey, trashedDoc, cancellationToken: cancellationToken);

        // Add to trashcan index
        await AddDocumentToTrashcanIndexAsync(namespaceId, documentId, cancellationToken);

        // Remove from main storage
        await docStore.DeleteAsync(docKey, cancellationToken);

        // Remove slug index
        var slugKey2 = $"{SLUG_INDEX_PREFIX}{namespaceId}:{storedDoc.Slug}";
        await slugStore.DeleteAsync(slugKey2, cancellationToken);

        // Remove from namespace document list
        await RemoveDocumentFromNamespaceIndexAsync(namespaceId, documentId, cancellationToken);

        // Remove from search index
        _searchIndexService.RemoveDocument(namespaceId, documentId);

        // Publish lifecycle event
        await PublishDocumentDeletedEventAsync(storedDoc, "User requested deletion", cancellationToken);

        _logger.LogInformation("Soft-deleted document {DocumentId} from namespace {Namespace}, recoverable until {ExpiresAt}",
            documentId, namespaceId, expiresAt);

        return (StatusCodes.OK, new DeleteDocumentResponse
        {
            DocumentId = documentId,
            DeletedAt = now,
            RecoverableUntil = expiresAt
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, RecoverDocumentResponse?)> RecoverDocumentAsync(RecoverDocumentRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("RecoverDocument: namespace={Namespace}, documentId={DocumentId}", body.Namespace, body.DocumentId);
        var namespaceId = body.Namespace;
        var documentId = body.DocumentId;
        var slugStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Documentation);
        var docStore = _stateStoreFactory.GetStore<StoredDocument>(StateStoreDefinitions.Documentation);
        var trashStore = _stateStoreFactory.GetStore<TrashedDocument>(StateStoreDefinitions.Documentation);

        // Fetch from trashcan
        var trashKey = $"{TRASH_KEY_PREFIX}{namespaceId}:{documentId}";
        var trashedDoc = await trashStore.GetAsync(trashKey, cancellationToken);
        if (trashedDoc == null)
        {
            _logger.LogDebug("Document {DocumentId} not found in trashcan for namespace {Namespace}", documentId, namespaceId);
            return (StatusCodes.NotFound, null);
        }

        // Check if expired
        if (trashedDoc.ExpiresAt < DateTimeOffset.UtcNow)
        {
            _logger.LogDebug("Document {DocumentId} has expired and cannot be recovered", documentId);
            // Cleanup expired item
            await trashStore.DeleteAsync(trashKey, cancellationToken);
            return (StatusCodes.NotFound, null);
        }

        // Check if slug is still available
        var slugKey = $"{SLUG_INDEX_PREFIX}{namespaceId}:{trashedDoc.Document.Slug}";
        var existingSlugIdStr = await slugStore.GetAsync(slugKey, cancellationToken);
        if (!string.IsNullOrEmpty(existingSlugIdStr))
        {
            _logger.LogWarning("Cannot recover document {DocumentId}: slug {Slug} is already in use", documentId, trashedDoc.Document.Slug);
            return (StatusCodes.Conflict, null);
        }

        var now = DateTimeOffset.UtcNow;
        var storedDoc = trashedDoc.Document;
        storedDoc.UpdatedAt = now;

        // Ensure search index exists BEFORE saving (Redis Search only auto-indexes new documents)
        await _searchIndexService.EnsureIndexExistsAsync(namespaceId, cancellationToken);

        // Restore to main storage
        var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{documentId}";
        await docStore.SaveAsync(docKey, storedDoc, cancellationToken: cancellationToken);

        // Restore slug index
        await slugStore.SaveAsync(slugKey, documentId.ToString(), cancellationToken: cancellationToken);

        // Add back to namespace document list
        await AddDocumentToNamespaceIndexAsync(namespaceId, documentId, cancellationToken);

        // Re-index for search
        _searchIndexService.IndexDocument(
            namespaceId,
            documentId,
            storedDoc.Title,
            storedDoc.Slug,
            storedDoc.Content,
            storedDoc.Category,
            storedDoc.Tags);

        // Remove from trashcan
        await trashStore.DeleteAsync(trashKey, cancellationToken);

        // Remove from trashcan index
        await RemoveDocumentFromTrashcanIndexAsync(namespaceId, documentId, cancellationToken);

        _logger.LogInformation("Recovered document {DocumentId} from trashcan in namespace {Namespace}", documentId, namespaceId);

        return (StatusCodes.OK, new RecoverDocumentResponse
        {
            DocumentId = documentId,
            RecoveredAt = now
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, BulkUpdateResponse?)> BulkUpdateDocumentsAsync(BulkUpdateRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("BulkUpdateDocuments: namespace={Namespace}, count={Count}", body.Namespace, body.DocumentIds.Count);
        var namespaceId = body.Namespace;
        var succeeded = new List<Guid>();
        var failed = new List<BulkOperationFailure>();
        var now = DateTimeOffset.UtcNow;
        var docStore = _stateStoreFactory.GetStore<StoredDocument>(StateStoreDefinitions.Documentation);

        var batchCounter = 0;
        foreach (var documentId in body.DocumentIds)
        {
            try
            {
                var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{documentId}";
                var storedDoc = await docStore.GetAsync(docKey, cancellationToken);

                if (storedDoc == null)
                {
                    failed.Add(new BulkOperationFailure { DocumentId = documentId, Error = "Document not found" });
                    continue;
                }

                var changedFields = new List<string>();

                // Apply category update if specified
                if (body.Category != null)
                {
                    storedDoc.Category = body.Category.Value.ToString();
                    changedFields.Add("category");
                }

                // Add tags
                if (body.AddTags != null && body.AddTags.Count > 0)
                {
                    foreach (var tag in body.AddTags)
                    {
                        if (!storedDoc.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                        {
                            storedDoc.Tags.Add(tag);
                        }
                    }
                    changedFields.Add("tags");
                }

                // Remove tags
                if (body.RemoveTags != null && body.RemoveTags.Count > 0)
                {
                    storedDoc.Tags.RemoveAll(t => body.RemoveTags.Contains(t, StringComparer.OrdinalIgnoreCase));
                    if (!changedFields.Contains("tags")) changedFields.Add("tags");
                }

                if (changedFields.Count > 0)
                {
                    storedDoc.UpdatedAt = now;
                    await docStore.SaveAsync(docKey, storedDoc, cancellationToken: cancellationToken);

                    // Update search index
                    _searchIndexService.IndexDocument(
                        namespaceId,
                        documentId,
                        storedDoc.Title,
                        storedDoc.Slug,
                        storedDoc.Content,
                        storedDoc.Category,
                        storedDoc.Tags);

                    // Publish update event
                    await PublishDocumentUpdatedEventAsync(storedDoc, changedFields, cancellationToken);
                }

                succeeded.Add(documentId);
            }
            catch (Exception ex)
            {
                failed.Add(new BulkOperationFailure { DocumentId = documentId, Error = ex.Message });
            }

            batchCounter++;
            if (batchCounter >= _configuration.BulkOperationBatchSize)
            {
                batchCounter = 0;
                await Task.Yield();
            }
        }

        _logger.LogInformation("BulkUpdate in namespace {Namespace}: {Succeeded} succeeded, {Failed} failed",
            namespaceId, succeeded.Count, failed.Count);

        return (StatusCodes.OK, new BulkUpdateResponse
        {
            Succeeded = succeeded,
            Failed = failed
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, BulkDeleteResponse?)> BulkDeleteDocumentsAsync(BulkDeleteRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("BulkDeleteDocuments: namespace={Namespace}, count={Count}", body.Namespace, body.DocumentIds.Count);
        var namespaceId = body.Namespace;
        var succeeded = new List<Guid>();
        var failed = new List<BulkOperationFailure>();
        var now = DateTimeOffset.UtcNow;
        var trashcanTtl = TimeSpan.FromDays(_configuration.TrashcanTtlDays);
        var expiresAt = now.Add(trashcanTtl);
        var slugStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Documentation);
        var docStore = _stateStoreFactory.GetStore<StoredDocument>(StateStoreDefinitions.Documentation);
        var trashStore = _stateStoreFactory.GetStore<TrashedDocument>(StateStoreDefinitions.Documentation);

        var batchCounter = 0;
        foreach (var documentId in body.DocumentIds)
        {
            try
            {
                var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{documentId}";
                var storedDoc = await docStore.GetAsync(docKey, cancellationToken);

                if (storedDoc == null)
                {
                    failed.Add(new BulkOperationFailure { DocumentId = documentId, Error = "Document not found" });
                    continue;
                }

                // Create trashcan entry
                var trashedDoc = new TrashedDocument
                {
                    Document = storedDoc,
                    DeletedAt = now,
                    ExpiresAt = expiresAt
                };

                var trashKey = $"{TRASH_KEY_PREFIX}{namespaceId}:{documentId}";
                await trashStore.SaveAsync(trashKey, trashedDoc, cancellationToken: cancellationToken);

                // Add to trashcan index
                await AddDocumentToTrashcanIndexAsync(namespaceId, documentId, cancellationToken);

                // Remove from main storage
                await docStore.DeleteAsync(docKey, cancellationToken);

                // Remove slug index
                var slugKey = $"{SLUG_INDEX_PREFIX}{namespaceId}:{storedDoc.Slug}";
                await slugStore.DeleteAsync(slugKey, cancellationToken);

                // Remove from namespace document list
                await RemoveDocumentFromNamespaceIndexAsync(namespaceId, documentId, cancellationToken);

                // Remove from search index
                _searchIndexService.RemoveDocument(namespaceId, documentId);

                // Publish delete event
                await PublishDocumentDeletedEventAsync(storedDoc, "Bulk delete operation", cancellationToken);

                succeeded.Add(documentId);
            }
            catch (Exception ex)
            {
                failed.Add(new BulkOperationFailure { DocumentId = documentId, Error = ex.Message });
            }

            batchCounter++;
            if (batchCounter >= _configuration.BulkOperationBatchSize)
            {
                batchCounter = 0;
                await Task.Yield();
            }
        }

        _logger.LogInformation("BulkDelete in namespace {Namespace}: {Succeeded} succeeded, {Failed} failed",
            namespaceId, succeeded.Count, failed.Count);

        return (StatusCodes.OK, new BulkDeleteResponse
        {
            Succeeded = succeeded,
            Failed = failed
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ImportDocumentationResponse?)> ImportDocumentationAsync(ImportDocumentationRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("ImportDocumentation: namespace={Namespace}, count={Count}", body.Namespace, body.Documents.Count);
        var namespaceId = body.Namespace;
        var created = 0;
        var updated = 0;
        var skipped = 0;
        var failed = new List<ImportFailure>();
        var now = DateTimeOffset.UtcNow;

        // Check max import limit if configured
        if (_configuration.MaxImportDocuments > 0 && body.Documents.Count > _configuration.MaxImportDocuments)
        {
            _logger.LogWarning("Import request exceeds maximum allowed documents ({Max})", _configuration.MaxImportDocuments);
            return (StatusCodes.BadRequest, null);
        }

        var slugStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Documentation);
        var docStore = _stateStoreFactory.GetStore<StoredDocument>(StateStoreDefinitions.Documentation);

        foreach (var importDoc in body.Documents)
        {
            try
            {
                // Validate content size
                if (importDoc.Content != null &&
                    System.Text.Encoding.UTF8.GetByteCount(importDoc.Content) > _configuration.MaxContentSizeBytes)
                {
                    failed.Add(new ImportFailure
                    {
                        Slug = importDoc.Slug,
                        Error = $"Content exceeds maximum size of {_configuration.MaxContentSizeBytes} bytes"
                    });
                    continue;
                }

                // Check if slug exists
                var slugKey = $"{SLUG_INDEX_PREFIX}{namespaceId}:{importDoc.Slug}";
                var existingDocIdStr = await slugStore.GetAsync(slugKey, cancellationToken);

                if (!string.IsNullOrEmpty(existingDocIdStr) && Guid.TryParse(existingDocIdStr, out var existingDocId))
                {
                    // Handle conflict based on policy
                    switch (body.OnConflict)
                    {
                        case ConflictResolution.Skip:
                            skipped++;
                            continue;

                        case ConflictResolution.Fail:
                            failed.Add(new ImportFailure { Slug = importDoc.Slug, Error = "Document already exists" });
                            continue;

                        case ConflictResolution.Update:
                            // Update existing document
                            var existingDocKey = $"{DOC_KEY_PREFIX}{namespaceId}:{existingDocId}";
                            var existingDoc = await docStore.GetAsync(existingDocKey, cancellationToken);
                            if (existingDoc != null)
                            {
                                existingDoc.Title = importDoc.Title;
                                existingDoc.Category = importDoc.Category.ToString();
                                existingDoc.Content = importDoc.Content;
                                existingDoc.Summary = importDoc.Summary;
                                existingDoc.VoiceSummary = TruncateVoiceSummary(importDoc.VoiceSummary);
                                existingDoc.Tags = importDoc.Tags?.ToList() ?? [];
                                existingDoc.Metadata = importDoc.Metadata;
                                existingDoc.UpdatedAt = now;

                                await docStore.SaveAsync(existingDocKey, existingDoc, cancellationToken: cancellationToken);

                                _searchIndexService.IndexDocument(namespaceId, existingDocId, existingDoc.Title, existingDoc.Slug, existingDoc.Content, existingDoc.Category, existingDoc.Tags);
                                await PublishDocumentUpdatedEventAsync(existingDoc, new[] { "title", "category", "content", "summary", "tags" }, cancellationToken);
                                updated++;
                            }
                            continue;
                    }
                }

                // Create new document
                var documentId = Guid.NewGuid();
                var storedDoc = new StoredDocument
                {
                    DocumentId = documentId,
                    Namespace = namespaceId,
                    Slug = importDoc.Slug,
                    Title = importDoc.Title,
                    Category = importDoc.Category.ToString(),
                    Content = importDoc.Content,
                    Summary = importDoc.Summary,
                    VoiceSummary = TruncateVoiceSummary(importDoc.VoiceSummary),
                    Tags = importDoc.Tags?.ToList() ?? [],
                    RelatedDocuments = [],
                    Metadata = importDoc.Metadata,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                // Ensure search index exists BEFORE saving (Redis Search only auto-indexes new documents)
                await _searchIndexService.EnsureIndexExistsAsync(namespaceId, cancellationToken);

                var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{documentId}";
                await docStore.SaveAsync(docKey, storedDoc, cancellationToken: cancellationToken);
                await slugStore.SaveAsync(slugKey, documentId.ToString(), cancellationToken: cancellationToken);
                await AddDocumentToNamespaceIndexAsync(namespaceId, documentId, cancellationToken);

                _searchIndexService.IndexDocument(namespaceId, documentId, storedDoc.Title, storedDoc.Slug, storedDoc.Content, storedDoc.Category, storedDoc.Tags);
                await PublishDocumentCreatedEventAsync(storedDoc, cancellationToken);
                created++;
            }
            catch (Exception ex)
            {
                failed.Add(new ImportFailure { Slug = importDoc.Slug, Error = ex.Message });
            }
        }

        _logger.LogInformation("Import in namespace {Namespace}: {Created} created, {Updated} updated, {Skipped} skipped, {Failed} failed",
            namespaceId, created, updated, skipped, failed.Count);

        return (StatusCodes.OK, new ImportDocumentationResponse
        {
            Namespace = namespaceId,
            Created = created,
            Updated = updated,
            Skipped = skipped,
            Failed = failed
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ListTrashcanResponse?)> ListTrashcanAsync(ListTrashcanRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("ListTrashcan: namespace={Namespace}", body.Namespace);
        var namespaceId = body.Namespace;
        var page = body.Page;
        var pageSize = body.PageSize;
        var guidListStore = _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Documentation);
        var trashStore = _stateStoreFactory.GetStore<TrashedDocument>(StateStoreDefinitions.Documentation);

        // Get trashcan items from namespace trashcan list
        var trashListKey = $"ns-trash:{namespaceId}";
        var trashedDocIds = await guidListStore.GetAsync(trashListKey, cancellationToken) ?? [];

        var items = new List<TrashcanItem>();
        var now = DateTimeOffset.UtcNow;
        var expiredIds = new List<Guid>();
        var expiredKeysToDelete = new List<string>();

        // Fetch trashcan items using bulk get (fixes N+1 query pattern)
        if (trashedDocIds.Count > 0)
        {
            var trashKeys = trashedDocIds.Select(docId => $"{TRASH_KEY_PREFIX}{namespaceId}:{docId}").ToList();
            var bulkResult = await trashStore.GetBulkAsync(trashKeys, cancellationToken);

            // Build a lookup for quick access
            var keyToDocId = trashedDocIds.ToDictionary(
                docId => $"{TRASH_KEY_PREFIX}{namespaceId}:{docId}",
                docId => docId);

            foreach (var docId in trashedDocIds)
            {
                var trashKey = $"{TRASH_KEY_PREFIX}{namespaceId}:{docId}";

                if (!bulkResult.TryGetValue(trashKey, out var trashedDoc) || trashedDoc == null)
                {
                    expiredIds.Add(docId);
                    continue;
                }

                // Check if expired
                if (trashedDoc.ExpiresAt < now)
                {
                    expiredIds.Add(docId);
                    expiredKeysToDelete.Add(trashKey);
                    continue;
                }

                items.Add(new TrashcanItem
                {
                    DocumentId = trashedDoc.Document.DocumentId,
                    Slug = trashedDoc.Document.Slug,
                    Title = trashedDoc.Document.Title,
                    Category = ParseDocumentCategory(trashedDoc.Document.Category),
                    DeletedAt = trashedDoc.DeletedAt,
                    ExpiresAt = trashedDoc.ExpiresAt
                });
            }

            // Batch delete expired items
            if (expiredKeysToDelete.Count > 0)
            {
                await trashStore.DeleteBulkAsync(expiredKeysToDelete, cancellationToken);
            }
        }

        // Clean up expired entries from list
        if (expiredIds.Count > 0)
        {
            trashedDocIds.RemoveAll(id => expiredIds.Contains(id));
            await guidListStore.SaveAsync(trashListKey, trashedDocIds, cancellationToken: cancellationToken);
        }

        // Apply pagination
        var totalCount = items.Count;
        var skip = (page - 1) * pageSize;
        items = items.OrderByDescending(i => i.DeletedAt).Skip(skip).Take(pageSize).ToList();

        _logger.LogDebug("ListTrashcan returned {Count} of {Total} items in namespace {Namespace}",
            items.Count, totalCount, namespaceId);

        return (StatusCodes.OK, new ListTrashcanResponse
        {
            Namespace = namespaceId,
            Items = items,
            TotalCount = totalCount
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, PurgeTrashcanResponse?)> PurgeTrashcanAsync(PurgeTrashcanRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("PurgeTrashcan: namespace={Namespace}", body.Namespace);
        var namespaceId = body.Namespace;
        var purgedCount = 0;
        var guidListStore = _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Documentation);
        var trashStore = _stateStoreFactory.GetStore<TrashedDocument>(StateStoreDefinitions.Documentation);

        // Get trashcan list with ETag for optimistic concurrency
        var trashListKey = $"ns-trash:{namespaceId}";
        var (trashedDocIds, trashEtag) = await guidListStore.GetWithETagAsync(trashListKey, cancellationToken);
        trashedDocIds ??= [];

        // Determine which documents to purge
        IEnumerable<Guid> docsToPurge;
        if (body.DocumentIds != null && body.DocumentIds.Count > 0)
        {
            // Purge specific documents
            docsToPurge = body.DocumentIds.Where(id => trashedDocIds.Contains(id));
        }
        else
        {
            // Purge all
            docsToPurge = trashedDocIds.ToList();
        }

        // Permanently delete each document
        foreach (var docId in docsToPurge)
        {
            var trashKey = $"{TRASH_KEY_PREFIX}{namespaceId}:{docId}";
            await trashStore.DeleteAsync(trashKey, cancellationToken);
            trashedDocIds.Remove(docId);
            purgedCount++;
        }

        // Update trashcan list with optimistic concurrency
        if (purgedCount > 0)
        {
            if (trashedDocIds.Count > 0)
            {
                // GetWithETagAsync returns non-null etag when key exists (loaded above);
                // coalesce satisfies compiler's nullable analysis (will never execute)
                var saveResult = await guidListStore.TrySaveAsync(trashListKey, trashedDocIds, trashEtag ?? string.Empty, cancellationToken);
                if (saveResult == null)
                {
                    _logger.LogWarning("PurgeTrashcan: Concurrent modification on trashcan index for namespace {Namespace}", namespaceId);
                    return (StatusCodes.Conflict, null);
                }
            }
            else
            {
                await guidListStore.DeleteAsync(trashListKey, cancellationToken);
            }
        }

        _logger.LogInformation("Purged {Count} documents from trashcan in namespace {Namespace}", purgedCount, namespaceId);

        return (StatusCodes.OK, new PurgeTrashcanResponse
        {
            PurgedCount = purgedCount
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, NamespaceStatsResponse?)> GetNamespaceStatsAsync(GetNamespaceStatsRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("GetNamespaceStats: namespace={Namespace}", body.Namespace);
        var namespaceId = body.Namespace;
        var guidSetStore = _stateStoreFactory.GetStore<HashSet<Guid>>(StateStoreDefinitions.Documentation);
        var guidListStore = _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Documentation);
        var docStore = _stateStoreFactory.GetStore<StoredDocument>(StateStoreDefinitions.Documentation);

        // Get stats from search index
        var searchStats = await _searchIndexService.GetNamespaceStatsAsync(namespaceId, cancellationToken);

        // Get trashcan count (trashcan uses List<Guid> for ordered, duplicate-allowing semantics)
        var trashListKey = $"ns-trash:{namespaceId}";
        var trashedDocIds = await guidListStore.GetAsync(trashListKey, cancellationToken) ?? [];

        // Calculate total content size (approximate from document count)
        // In production, this could be tracked more precisely
        var estimatedContentSize = searchStats.TotalDocuments * 10000; // ~10KB average per document

        // Find last updated document
        var lastUpdated = DateTimeOffset.MinValue;
        var docListKey = $"{NAMESPACE_DOCS_PREFIX}{namespaceId}";
        var docIds = await guidSetStore.GetAsync(docListKey, cancellationToken) ?? [];

        if (docIds.Count > 0)
        {
            // Sample recent documents to find last updated (configurable sample size)
            foreach (var docId in docIds.Take(_configuration.StatsSampleSize))
            {
                var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{docId}";
                var doc = await docStore.GetAsync(docKey, cancellationToken);
                if (doc != null && doc.UpdatedAt > lastUpdated)
                {
                    lastUpdated = doc.UpdatedAt;
                }
            }
        }

        // Convert category counts from string keys to proper format
        var categoryCounts = searchStats.DocumentsByCategory.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value);

        _logger.LogDebug("GetNamespaceStats for {Namespace}: {DocumentCount} documents, {TrashcanCount} in trashcan",
            namespaceId, searchStats.TotalDocuments, trashedDocIds.Count);

        return (StatusCodes.OK, new NamespaceStatsResponse
        {
            Namespace = namespaceId,
            DocumentCount = searchStats.TotalDocuments,
            CategoryCounts = categoryCounts,
            TrashcanCount = trashedDocIds.Count,
            TotalContentSizeBytes = estimatedContentSize,
            LastUpdated = lastUpdated == DateTimeOffset.MinValue ? DateTimeOffset.UtcNow : lastUpdated
        });
    }

    #region Helper Methods

    /// <summary>
    /// Adds a document ID to the namespace document list for pagination support.
    /// Also maintains the global namespace registry for search index rebuild.
    /// </summary>
    private async Task AddDocumentToNamespaceIndexAsync(string namespaceId, Guid documentId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.AddDocumentToNamespaceIndexAsync");
        var guidSetStore = _stateStoreFactory.GetStore<HashSet<Guid>>(StateStoreDefinitions.Documentation);
        var indexKey = $"{NAMESPACE_DOCS_PREFIX}{namespaceId}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (docIds, etag) = await guidSetStore.GetWithETagAsync(indexKey, cancellationToken);
            docIds ??= [];

            if (docIds.Add(documentId))
            {
                // etag is null when key doesn't exist yet; passing empty string signals "create new" semantics
                var result = await guidSetStore.TrySaveAsync(indexKey, docIds, etag ?? string.Empty, cancellationToken);
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
        var stringSetStore = _stateStoreFactory.GetStore<HashSet<string>>(StateStoreDefinitions.Documentation);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (allNamespaces, nsEtag) = await stringSetStore.GetWithETagAsync(ALL_NAMESPACES_KEY, cancellationToken);
            allNamespaces ??= [];

            if (allNamespaces.Add(namespaceId))
            {
                // nsEtag is null when registry doesn't exist yet; passing empty string signals "create new" semantics
                var result = await stringSetStore.TrySaveAsync(ALL_NAMESPACES_KEY, allNamespaces, nsEtag ?? string.Empty, cancellationToken);
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
        var guidSetStore = _stateStoreFactory.GetStore<HashSet<Guid>>(StateStoreDefinitions.Documentation);
        var indexKey = $"{NAMESPACE_DOCS_PREFIX}{namespaceId}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (docIds, etag) = await guidSetStore.GetWithETagAsync(indexKey, cancellationToken);

            if (docIds != null && docIds.Remove(documentId))
            {
                // GetWithETagAsync returns non-null etag when key exists (checked above);
                // coalesce satisfies compiler's nullable analysis (will never execute)
                var result = await guidSetStore.TrySaveAsync(indexKey, docIds, etag ?? string.Empty, cancellationToken);
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
        var guidListStore = _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Documentation);
        var trashListKey = $"ns-trash:{namespaceId}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (trashedDocIds, etag) = await guidListStore.GetWithETagAsync(trashListKey, cancellationToken);
            trashedDocIds ??= [];

            if (!trashedDocIds.Contains(documentId))
            {
                trashedDocIds.Add(documentId);
                // etag is null when trashcan doesn't exist yet; passing empty string signals "create new" semantics
                var result = await guidListStore.TrySaveAsync(trashListKey, trashedDocIds, etag ?? string.Empty, cancellationToken);
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
        var guidListStore = _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Documentation);
        var trashListKey = $"ns-trash:{namespaceId}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (trashedDocIds, etag) = await guidListStore.GetWithETagAsync(trashListKey, cancellationToken);

            if (trashedDocIds != null && trashedDocIds.Remove(documentId))
            {
                if (trashedDocIds.Count > 0)
                {
                    // GetWithETagAsync returns non-null etag when key exists (checked above);
                    // coalesce satisfies compiler's nullable analysis (will never execute)
                    var result = await guidListStore.TrySaveAsync(trashListKey, trashedDocIds, etag ?? string.Empty, cancellationToken);
                    if (result != null)
                    {
                        return;
                    }

                    _logger.LogDebug("Concurrent modification on trashcan index {Namespace}, retrying (attempt {Attempt})", namespaceId, attempt + 1);
                    continue;
                }
                else
                {
                    await guidListStore.DeleteAsync(trashListKey, cancellationToken);
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
    private static DocumentCategory ParseDocumentCategory(string? category)
    {
        if (string.IsNullOrEmpty(category))
        {
            return DocumentCategory.Other;
        }

        return Enum.TryParse<DocumentCategory>(category, ignoreCase: true, out var result)
            ? result
            : DocumentCategory.Other;
    }

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
        var docStore = _stateStoreFactory.GetStore<StoredDocument>(StateStoreDefinitions.Documentation);

        foreach (var relatedId in relatedIds.Take(maxRelated))
        {
            var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{relatedId}";
            var doc = await docStore.GetAsync(docKey, cancellationToken);

            if (doc != null)
            {
                summaries.Add(new DocumentSummary
                {
                    DocumentId = doc.DocumentId,
                    Slug = doc.Slug,
                    Title = doc.Title,
                    Category = ParseDocumentCategory(doc.Category),
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

            await _messageBus.TryPublishAsync(DOCUMENT_CREATED_TOPIC, eventModel);
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

            await _messageBus.TryPublishAsync(DOCUMENT_UPDATED_TOPIC, eventModel);
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

            await _messageBus.TryPublishAsync(DOCUMENT_DELETED_TOPIC, eventModel);
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
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.PublishQueryAnalyticsEventAsync");
        try
        {
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

            await _messageBus.TryPublishAsync("documentation.queried", eventModel);
            _logger.LogDebug("Published DocumentationQueriedEvent for query '{Query}'", query);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish DocumentationQueriedEvent");
            // Non-critical - don't propagate
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
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.PublishSearchAnalyticsEventAsync");
        try
        {
            var eventModel = new DocumentationSearchedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Namespace = namespaceId,
                SearchTerm = searchTerm,
                SessionId = sessionId,
                ResultCount = resultCount
            };

            await _messageBus.TryPublishAsync("documentation.searched", eventModel);
            _logger.LogDebug("Published DocumentationSearchedEvent for term '{Term}'", searchTerm);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish DocumentationSearchedEvent");
            // Non-critical - don't propagate
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, BindRepositoryResponse?)> BindRepositoryAsync(BindRepositoryRequest body, CancellationToken cancellationToken = default)
    {

        if (string.IsNullOrWhiteSpace(body.Namespace))
        {
            _logger.LogWarning("BindRepository failed: namespace is required");
            return (StatusCodes.BadRequest, null);
        }

        if (string.IsNullOrWhiteSpace(body.RepositoryUrl))
        {
            _logger.LogWarning("BindRepository failed: repositoryUrl is required");
            return (StatusCodes.BadRequest, null);
        }

        _logger.LogInformation("Binding repository {Url} to namespace {Namespace}", body.RepositoryUrl, body.Namespace);
        // Check if namespace already has a binding
        var existingBinding = await GetBindingForNamespaceAsync(body.Namespace, cancellationToken);
        if (existingBinding != null)
        {
            _logger.LogWarning("Namespace {Namespace} already has a repository binding", body.Namespace);
            return (StatusCodes.Conflict, null);
        }

        // Create binding record
        var binding = new Models.RepositoryBinding
        {
            BindingId = Guid.NewGuid(),
            Namespace = body.Namespace,
            RepositoryUrl = body.RepositoryUrl,
            Branch = body.Branch ?? "main",
            Status = Models.BindingStatusInternal.Pending,
            SyncEnabled = true,
            SyncIntervalMinutes = body.SyncIntervalMinutes,
            FilePatterns = body.FilePatterns?.ToList() ?? ["**/*.md"],
            ExcludePatterns = body.ExcludePatterns?.ToList() ?? [".git/**", ".obsidian/**", "node_modules/**"],
            CategoryMapping = body.CategoryMapping?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value) ?? [],
            DefaultCategory = body.DefaultCategory.ToString(),
            ArchiveEnabled = body.ArchiveEnabled,
            ArchiveOnSync = body.ArchiveOnSync,
            CreatedAt = DateTimeOffset.UtcNow,
            Owner = body.Owner
        };

        // Save binding
        await SaveBindingAsync(binding, cancellationToken);

        // Publish binding created event
        await TryPublishBindingCreatedEventAsync(binding, cancellationToken);

        _logger.LogInformation("Created repository binding {BindingId} for namespace {Namespace}", binding.BindingId, body.Namespace);

        return (StatusCodes.OK, new BindRepositoryResponse
        {
            BindingId = binding.BindingId,
            Namespace = binding.Namespace,
            RepositoryUrl = binding.RepositoryUrl,
            Branch = binding.Branch,
            Status = MapBindingStatus(binding.Status),
            CreatedAt = binding.CreatedAt
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, UnbindRepositoryResponse?)> UnbindRepositoryAsync(UnbindRepositoryRequest body, CancellationToken cancellationToken = default)
    {

        _logger.LogInformation("Unbinding repository from namespace {Namespace}", body.Namespace);
        var binding = await GetBindingForNamespaceAsync(body.Namespace, cancellationToken);
        if (binding == null)
        {
            _logger.LogWarning("No repository binding found for namespace {Namespace}", body.Namespace);
            return (StatusCodes.NotFound, null);
        }

        var documentsDeleted = 0;

        // Optionally delete all documents
        if (body.DeleteDocuments)
        {
            documentsDeleted = await DeleteAllNamespaceDocumentsAsync(body.Namespace, cancellationToken);
        }

        // Delete binding
        var bindingKey = $"{BINDING_KEY_PREFIX}{body.Namespace}";
        var bindingStore = _stateStoreFactory.GetStore<Models.RepositoryBinding>(StateStoreDefinitions.Documentation);
        await bindingStore.DeleteAsync(bindingKey, cancellationToken);

        // Remove from bindings registry
        var registryStore = _stateStoreFactory.GetStore<HashSet<string>>(StateStoreDefinitions.Documentation);
        var registry = await registryStore.GetAsync(BINDINGS_REGISTRY_KEY, cancellationToken);
        if (registry != null && registry.Remove(body.Namespace))
        {
            await registryStore.SaveAsync(BINDINGS_REGISTRY_KEY, registry, cancellationToken: cancellationToken);
        }

        // Cleanup local repository
        var localPath = GetLocalRepositoryPath(binding.BindingId);
        await _gitSyncService.CleanupRepositoryAsync(localPath, cancellationToken);

        // Publish binding removed event
        await TryPublishBindingRemovedEventAsync(binding, documentsDeleted, cancellationToken);

        _logger.LogInformation("Removed repository binding from namespace {Namespace}, deleted {Count} documents", body.Namespace, documentsDeleted);

        return (StatusCodes.OK, new UnbindRepositoryResponse
        {
            Namespace = body.Namespace,
            DocumentsDeleted = documentsDeleted
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, SyncRepositoryResponse?)> SyncRepositoryAsync(SyncRepositoryRequest body, CancellationToken cancellationToken = default)
    {

        if (string.IsNullOrWhiteSpace(body.Namespace))
        {
            _logger.LogWarning("SyncRepository failed: namespace is required");
            return (StatusCodes.BadRequest, null);
        }

        _logger.LogInformation("Manual sync requested for namespace {Namespace}", body.Namespace);
        var binding = await GetBindingForNamespaceAsync(body.Namespace, cancellationToken);
        if (binding == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Execute sync
        var result = await ExecuteSyncAsync(binding, body.Force, SyncTrigger.Manual, cancellationToken);

        return (StatusCodes.OK, new SyncRepositoryResponse
        {
            SyncId = result.SyncId,
            Status = MapSyncStatus(result.Status),
            CommitHash = result.CommitHash,
            DocumentsCreated = result.DocumentsCreated,
            DocumentsUpdated = result.DocumentsUpdated,
            DocumentsDeleted = result.DocumentsDeleted,
            DocumentsFailed = 0,
            DurationMs = result.DurationMs,
            ErrorMessage = result.ErrorMessage
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, RepositoryStatusResponse?)> GetRepositoryStatusAsync(RepositoryStatusRequest body, CancellationToken cancellationToken = default)
    {
        var binding = await GetBindingForNamespaceAsync(body.Namespace, cancellationToken);
        if (binding == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var response = new RepositoryStatusResponse
        {
            Binding = MapToBindingInfo(binding)
        };

        // Add last sync info if available
        if (binding.LastSyncAt.HasValue)
        {
            response.LastSync = new SyncInfo
            {
                SyncId = Guid.Empty, // Not tracked per-sync currently
                Status = binding.Status == Models.BindingStatusInternal.Error ? SyncStatus.Failed : SyncStatus.Success,
                TriggeredBy = SyncTrigger.Scheduled,
                StartedAt = binding.LastSyncAt.Value,
                CompletedAt = binding.LastSyncAt.Value,
                CommitHash = binding.LastCommitHash,
                DocumentsProcessed = binding.DocumentCount
            };
        }

        return (StatusCodes.OK, response);
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ListRepositoryBindingsResponse?)> ListRepositoryBindingsAsync(ListRepositoryBindingsRequest body, CancellationToken cancellationToken = default)
    {
        var bindingStore = _stateStoreFactory.GetStore<Models.RepositoryBinding>(StateStoreDefinitions.Documentation);

        // Get all binding namespace IDs from registry
        var registryStore = _stateStoreFactory.GetStore<HashSet<string>>(StateStoreDefinitions.Documentation);
        var bindingNamespaces = await registryStore.GetAsync(BINDINGS_REGISTRY_KEY, cancellationToken) ?? [];

        var allBindings = new List<Models.RepositoryBinding>();
        foreach (var namespaceId in bindingNamespaces)
        {
            var bindingKey = $"{BINDING_KEY_PREFIX}{namespaceId}";
            var binding = await bindingStore.GetAsync(bindingKey, cancellationToken);
            if (binding != null)
            {
                allBindings.Add(binding);
            }
        }

        // Filter by status if specified (default enum value means no filter)
        var filteredBindings = allBindings.AsEnumerable();
        if (body.Status != default)
        {
            var targetStatus = MapToInternalBindingStatus(body.Status);
            filteredBindings = filteredBindings.Where(b => b.Status == targetStatus);
        }

        var totalCount = filteredBindings.Count();
        var paginatedBindings = filteredBindings
            .Skip(body.Offset)
            .Take(body.Limit)
            .Select(MapToBindingInfo)
            .ToList();

        return (StatusCodes.OK, new ListRepositoryBindingsResponse
        {
            Bindings = paginatedBindings,
            Total = totalCount
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, UpdateRepositoryBindingResponse?)> UpdateRepositoryBindingAsync(UpdateRepositoryBindingRequest body, CancellationToken cancellationToken = default)
    {
        var binding = await GetBindingForNamespaceAsync(body.Namespace, cancellationToken);
        if (binding == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Update binding properties
        binding.SyncEnabled = body.SyncEnabled;
        binding.SyncIntervalMinutes = body.SyncIntervalMinutes;

        if (body.FilePatterns != null)
        {
            binding.FilePatterns = body.FilePatterns.ToList();
        }

        if (body.ExcludePatterns != null)
        {
            binding.ExcludePatterns = body.ExcludePatterns.ToList();
        }

        if (body.CategoryMapping != null)
        {
            binding.CategoryMapping = body.CategoryMapping.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        if (body.DefaultCategory != null)
        {
            binding.DefaultCategory = body.DefaultCategory.Value.ToString();
        }

        binding.ArchiveEnabled = body.ArchiveEnabled;
        binding.ArchiveOnSync = body.ArchiveOnSync;

        await SaveBindingAsync(binding, cancellationToken);

        _logger.LogInformation("Updated repository binding for namespace {Namespace}", body.Namespace);

        return (StatusCodes.OK, new UpdateRepositoryBindingResponse
        {
            Binding = MapToBindingInfo(binding)
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, CreateArchiveResponse?)> CreateDocumentationArchiveAsync(CreateArchiveRequest body, CancellationToken cancellationToken = default)
    {

        if (string.IsNullOrWhiteSpace(body.Namespace))
        {
            _logger.LogWarning("CreateArchive failed: namespace is required");
            return (StatusCodes.BadRequest, null);
        }

        _logger.LogInformation("Creating archive for namespace {Namespace}", body.Namespace);
        // Get all documents in namespace
        var documents = await GetAllNamespaceDocumentsAsync(body.Namespace, cancellationToken);
        if (documents.Count == 0)
        {
            _logger.LogWarning("No documents found in namespace {Namespace} to archive", body.Namespace);
            return (StatusCodes.NotFound, null);
        }

        // Create archive bundle (JSON format for simplicity)
        var archiveId = Guid.NewGuid();
        var bundleData = await CreateArchiveBundleAsync(body.Namespace, documents, cancellationToken);

        // Store archive record
        var archive = new Models.DocumentationArchive
        {
            ArchiveId = archiveId,
            Namespace = body.Namespace,
            DocumentCount = documents.Count,
            SizeBytes = bundleData.Length,
            CreatedAt = DateTimeOffset.UtcNow,
            Owner = body.Owner,
            Description = body.Description,
            CommitHash = await GetCurrentCommitHashForNamespaceAsync(body.Namespace, cancellationToken)
        };

        // Upload the bundle to Asset Service
        try
        {
            var uploadResponse = await _assetClient.RequestBundleUploadAsync(new BundleUploadRequest
            {
                Owner = body.Owner,
                Filename = $"docs-{body.Namespace}-{archiveId:N}.bannou",
                Size = bundleData.Length
            }, cancellationToken);

            // Upload to pre-signed URL
            using var httpClient = _httpClientFactory.CreateClient();
            using var content = new ByteArrayContent(bundleData);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            var uploadResult = await httpClient.PutAsync(uploadResponse.UploadUrl.ToString(), content, cancellationToken);

            if (uploadResult.IsSuccessStatusCode)
            {
                archive.BundleAssetId = uploadResponse.UploadId;
                _logger.LogInformation("Archive bundle uploaded to Asset Service: {BundleId}", archive.BundleAssetId);
            }
            else
            {
                _logger.LogWarning("Failed to upload archive bundle to Asset Service: {StatusCode}", uploadResult.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Asset Service integration failed, archive stored without bundle upload");
        }

        // Save archive record to state store
        await SaveArchiveAsync(archive, cancellationToken);

        // Publish archive created event
        await TryPublishArchiveCreatedEventAsync(archive, cancellationToken);

        return (StatusCodes.OK, new CreateArchiveResponse
        {
            ArchiveId = archiveId,
            Namespace = body.Namespace,
            DocumentCount = documents.Count,
            SizeBytes = bundleData.Length,
            CreatedAt = archive.CreatedAt
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ListArchivesResponse?)> ListDocumentationArchivesAsync(ListArchivesRequest body, CancellationToken cancellationToken = default)
    {

        if (string.IsNullOrWhiteSpace(body.Namespace))
        {
            _logger.LogWarning("ListArchives failed: namespace is required");
            return (StatusCodes.BadRequest, null);
        }

        var archives = await GetArchivesForNamespaceAsync(body.Namespace, cancellationToken);
        var offset = body.Offset;
        var limit = body.Limit;

        var pagedArchives = archives
            .OrderByDescending(a => a.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .Select(a => new ArchiveInfo
            {
                ArchiveId = a.ArchiveId,
                Namespace = a.Namespace,
                CreatedAt = a.CreatedAt,
                Owner = a.Owner,
                DocumentCount = a.DocumentCount,
                SizeBytes = (int)Math.Min(a.SizeBytes, int.MaxValue),
                Description = a.Description,
                CommitHash = a.CommitHash
            })
            .ToList();

        return (StatusCodes.OK, new ListArchivesResponse
        {
            Archives = pagedArchives,
            Total = archives.Count
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, RestoreArchiveResponse?)> RestoreDocumentationArchiveAsync(RestoreArchiveRequest body, CancellationToken cancellationToken = default)
    {

        if (body.ArchiveId == Guid.Empty)
        {
            _logger.LogWarning("RestoreArchive failed: archiveId is required");
            return (StatusCodes.BadRequest, null);
        }

        // Get archive metadata
        var archive = await GetArchiveByIdAsync(body.ArchiveId, cancellationToken);
        if (archive == null)
        {
            _logger.LogWarning("Archive {ArchiveId} not found", body.ArchiveId);
            return (StatusCodes.NotFound, null);
        }

        _logger.LogInformation("Restoring archive {ArchiveId} for namespace {Namespace}", body.ArchiveId, archive.Namespace);

        // Check if namespace is bound to a repository
        var binding = await GetBindingForNamespaceAsync(archive.Namespace, cancellationToken);
        if (binding != null && binding.Status != Models.BindingStatusInternal.Disabled)
        {
            _logger.LogWarning("Cannot restore to bound namespace {Namespace}", archive.Namespace);
            return (StatusCodes.Forbidden, null);
        }

        int documentsRestored = 0;

        // Download and restore from bundle if we have one
        if (archive.BundleAssetId != Guid.Empty)
        {
            try
            {
                var bundleResponse = await _assetClient.GetBundleAsync(new GetBundleRequest
                {
                    BundleId = archive.BundleAssetId.ToString()
                }, cancellationToken);

                if (bundleResponse.DownloadUrl != null)
                {
                    using var httpClient = _httpClientFactory.CreateClient();
                    var bundleData = await httpClient.GetByteArrayAsync(bundleResponse.DownloadUrl.ToString(), cancellationToken);
                    documentsRestored = await RestoreFromBundleAsync(archive.Namespace, bundleData, cancellationToken);
                }
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                _logger.LogWarning("Archive bundle {BundleId} not found in Asset Service", archive.BundleAssetId);
                return (StatusCodes.NotFound, null);
            }
        }
        else
        {
            _logger.LogWarning("Cannot restore archive {ArchiveId} - no bundle data available", body.ArchiveId);
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, new RestoreArchiveResponse
        {
            Namespace = archive.Namespace,
            DocumentsRestored = documentsRestored
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, DeleteArchiveResponse?)> DeleteDocumentationArchiveAsync(DeleteArchiveRequest body, CancellationToken cancellationToken = default)
    {

        if (body.ArchiveId == Guid.Empty)
        {
            _logger.LogWarning("DeleteArchive failed: archiveId is required");
            return (StatusCodes.BadRequest, null);
        }

        // Get archive metadata
        var archive = await GetArchiveByIdAsync(body.ArchiveId, cancellationToken);
        if (archive == null)
        {
            _logger.LogWarning("Archive {ArchiveId} not found", body.ArchiveId);
            return (StatusCodes.NotFound, null);
        }

        _logger.LogInformation("Deleting archive {ArchiveId} for namespace {Namespace}", body.ArchiveId, archive.Namespace);

        // Delete archive record from state store
        await DeleteArchiveAsync(archive, cancellationToken);

        // Note: We don't delete the bundle from Asset Service as it may be used for other purposes
        // or the Asset Service may have its own retention policies

        return (StatusCodes.OK, new DeleteArchiveResponse());
    }

    #region Repository Binding Helpers

    /// <summary>
    /// Gets the binding for a namespace if it exists.
    /// </summary>
    internal async Task<Models.RepositoryBinding?> GetBindingForNamespaceAsync(string namespaceId, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.GetBindingForNamespaceAsync");
        var bindingKey = $"{BINDING_KEY_PREFIX}{namespaceId}";
        var bindingStore = _stateStoreFactory.GetStore<Models.RepositoryBinding>(StateStoreDefinitions.Documentation);
        return await bindingStore.GetAsync(bindingKey, cancellationToken);
    }

    /// <summary>
    /// Saves a binding to the state store and updates the registry.
    /// </summary>
    private async Task SaveBindingAsync(Models.RepositoryBinding binding, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.SaveBindingAsync");
        var bindingKey = $"{BINDING_KEY_PREFIX}{binding.Namespace}";
        var bindingStore = _stateStoreFactory.GetStore<Models.RepositoryBinding>(StateStoreDefinitions.Documentation);
        await bindingStore.SaveAsync(bindingKey, binding, cancellationToken: cancellationToken);

        // Update bindings registry
        var registryStore = _stateStoreFactory.GetStore<HashSet<string>>(StateStoreDefinitions.Documentation);
        var registry = await registryStore.GetAsync(BINDINGS_REGISTRY_KEY, cancellationToken) ?? [];
        if (registry.Add(binding.Namespace))
        {
            await registryStore.SaveAsync(BINDINGS_REGISTRY_KEY, registry, cancellationToken: cancellationToken);
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
        var docsStore = _stateStoreFactory.GetStore<HashSet<Guid>>(StateStoreDefinitions.Documentation);
        var docIds = await docsStore.GetAsync(docsKey, cancellationToken);

        if (docIds == null || docIds.Count == 0)
        {
            return 0;
        }

        var docStore = _stateStoreFactory.GetStore<StoredDocument>(StateStoreDefinitions.Documentation);
        var slugStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Documentation);
        var deletedCount = 0;

        foreach (var docId in docIds)
        {
            var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{docId}";
            var doc = await docStore.GetAsync(docKey, cancellationToken);

            if (doc != null)
            {
                // Delete slug index
                var slugKey = $"{SLUG_INDEX_PREFIX}{namespaceId}:{doc.Slug}";
                await slugStore.DeleteAsync(slugKey, cancellationToken);

                // Delete document
                await docStore.DeleteAsync(docKey, cancellationToken);
                deletedCount++;
            }
        }

        // Clear namespace document list
        await docsStore.DeleteAsync(docsKey, cancellationToken);

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
        binding.Status = Models.BindingStatusInternal.Syncing;
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
                binding.Status = Models.BindingStatusInternal.Error;
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
                binding.Status = Models.BindingStatusInternal.Synced;
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
                        // Update existing document
                        await UpdateDocumentFromTransformAsync(binding.Namespace, existingDocId.Value, transformed, cancellationToken);
                        documentsUpdated++;
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

            // Delete orphan documents (documents not in processed slugs)
            // Skip orphan deletion if we truncated the file list to avoid incorrectly deleting unprocessed documents
            var documentsDeleted = 0;
            if (!truncated)
            {
                documentsDeleted = await DeleteOrphanDocumentsAsync(binding.Namespace, processedSlugs, cancellationToken);
            }

            // Update binding state
            binding.Status = Models.BindingStatusInternal.Synced;
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

            _logger.LogInformation("Sync completed for namespace {Namespace}: {Created} created, {Updated} updated, {Deleted} deleted",
                binding.Namespace, documentsCreated, documentsUpdated, documentsDeleted);

            return successResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync failed for namespace {Namespace}", binding.Namespace);

            binding.Status = Models.BindingStatusInternal.Error;
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
        var slugStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Documentation);
        var docIdStr = await slugStore.GetAsync(slugKey, cancellationToken);

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
            UpdatedAt = now
        };

        // Store document
        var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{documentId}";
        var docStore = _stateStoreFactory.GetStore<StoredDocument>(StateStoreDefinitions.Documentation);
        await docStore.SaveAsync(docKey, storedDoc, cancellationToken: cancellationToken);

        // Store slug index
        var slugKey = $"{SLUG_INDEX_PREFIX}{namespaceId}:{transformed.Slug}";
        var slugStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Documentation);
        await slugStore.SaveAsync(slugKey, documentId.ToString(), cancellationToken: cancellationToken);

        // Add to namespace document list
        var docsKey = $"{NAMESPACE_DOCS_PREFIX}{namespaceId}";
        var docsStore = _stateStoreFactory.GetStore<HashSet<Guid>>(StateStoreDefinitions.Documentation);
        var docIds = await docsStore.GetAsync(docsKey, cancellationToken) ?? [];
        docIds.Add(documentId);
        await docsStore.SaveAsync(docsKey, docIds, cancellationToken: cancellationToken);

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
    /// Updates a document from a transformed file.
    /// </summary>
    private async Task UpdateDocumentFromTransformAsync(
        string namespaceId,
        Guid documentId,
        TransformedDocument transformed,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.UpdateDocumentFromTransformAsync");
        var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{documentId}";
        var docStore = _stateStoreFactory.GetStore<StoredDocument>(StateStoreDefinitions.Documentation);
        var existingDoc = await docStore.GetAsync(docKey, cancellationToken);

        if (existingDoc == null)
        {
            return;
        }

        existingDoc.Title = transformed.Title;
        existingDoc.Category = transformed.Category;
        existingDoc.Content = transformed.Content;
        existingDoc.Summary = transformed.Summary;
        existingDoc.VoiceSummary = transformed.VoiceSummary;
        existingDoc.Tags = transformed.Tags;
        existingDoc.Metadata = transformed.Metadata;
        existingDoc.UpdatedAt = DateTimeOffset.UtcNow;

        await docStore.SaveAsync(docKey, existingDoc, cancellationToken: cancellationToken);

        // Re-index for search
        _searchIndexService.IndexDocument(
            existingDoc.Namespace,
            existingDoc.DocumentId,
            existingDoc.Title,
            existingDoc.Slug,
            existingDoc.Content,
            existingDoc.Category,
            existingDoc.Tags);
    }

    /// <summary>
    /// Gets the count of documents in a namespace.
    /// </summary>
    private async Task<int> GetNamespaceDocumentCountAsync(string namespaceId, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.GetNamespaceDocumentCountAsync");
        var docsKey = $"{NAMESPACE_DOCS_PREFIX}{namespaceId}";
        var docsStore = _stateStoreFactory.GetStore<HashSet<Guid>>(StateStoreDefinitions.Documentation);
        var docIds = await docsStore.GetAsync(docsKey, cancellationToken);
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
        var docsStore = _stateStoreFactory.GetStore<HashSet<Guid>>(StateStoreDefinitions.Documentation);
        var docIds = await docsStore.GetAsync(docsKey, cancellationToken);

        if (docIds == null || docIds.Count == 0)
        {
            return 0;
        }

        var docStore = _stateStoreFactory.GetStore<StoredDocument>(StateStoreDefinitions.Documentation);
        var slugStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Documentation);
        var deletedCount = 0;
        var orphanIds = new List<Guid>();

        // Find orphan documents (slugs not in processed set)
        foreach (var docId in docIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{docId}";
            var doc = await docStore.GetAsync(docKey, cancellationToken);

            if (doc != null && !processedSlugs.Contains(doc.Slug))
            {
                // This document's slug was not processed, it's an orphan
                orphanIds.Add(docId);

                // Delete slug index
                var slugKey = $"{SLUG_INDEX_PREFIX}{namespaceId}:{doc.Slug}";
                await slugStore.DeleteAsync(slugKey, cancellationToken);

                // Delete search index
                _searchIndexService.RemoveDocument(namespaceId, docId);

                // Delete document
                await docStore.DeleteAsync(docKey, cancellationToken);
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
                var (currentIds, etag) = await docsStore.GetWithETagAsync(docsKey, cancellationToken);
                if (currentIds == null) break;

                foreach (var orphanId in orphanIds)
                {
                    currentIds.Remove(orphanId);
                }

                string? savedEtag;
                if (etag != null)
                {
                    savedEtag = await docsStore.TrySaveAsync(docsKey, currentIds, etag, cancellationToken: cancellationToken);
                }
                else
                {
                    savedEtag = await docsStore.SaveAsync(docsKey, currentIds, cancellationToken: cancellationToken);
                }

                if (savedEtag != null) break;

                _logger.LogDebug("Namespace doc list update for {Namespace} had ETag conflict, retrying ({Retry}/{MaxRetries})",
                    namespaceId, retry + 1, maxRetries);
            }
        }

        return deletedCount;
    }

    /// <summary>
    /// Maps internal binding status to API enum.
    /// </summary>
    private static BindingStatus MapBindingStatus(Models.BindingStatusInternal status) => status switch
    {
        Models.BindingStatusInternal.Pending => BindingStatus.Pending,
        Models.BindingStatusInternal.Syncing => BindingStatus.Syncing,
        Models.BindingStatusInternal.Synced => BindingStatus.Synced,
        Models.BindingStatusInternal.Error => BindingStatus.Error,
        Models.BindingStatusInternal.Disabled => BindingStatus.Disabled,
        _ => BindingStatus.Pending
    };

    /// <summary>
    /// Maps API binding status to internal enum.
    /// </summary>
    private static Models.BindingStatusInternal MapToInternalBindingStatus(BindingStatus status) => status switch
    {
        BindingStatus.Pending => Models.BindingStatusInternal.Pending,
        BindingStatus.Syncing => Models.BindingStatusInternal.Syncing,
        BindingStatus.Synced => Models.BindingStatusInternal.Synced,
        BindingStatus.Error => Models.BindingStatusInternal.Error,
        BindingStatus.Disabled => Models.BindingStatusInternal.Disabled,
        _ => Models.BindingStatusInternal.Pending
    };

    /// <summary>
    /// Maps internal sync status to API enum.
    /// </summary>
    private static SyncStatus MapSyncStatus(Models.SyncStatusInternal status) => status switch
    {
        Models.SyncStatusInternal.Success => SyncStatus.Success,
        Models.SyncStatusInternal.Partial => SyncStatus.Partial,
        Models.SyncStatusInternal.Failed => SyncStatus.Failed,
        _ => SyncStatus.Failed
    };

    /// <summary>
    /// Maps binding to API info model.
    /// </summary>
    private static RepositoryBindingInfo MapToBindingInfo(Models.RepositoryBinding binding) => new()
    {
        BindingId = binding.BindingId,
        Namespace = binding.Namespace,
        RepositoryUrl = binding.RepositoryUrl,
        Branch = binding.Branch,
        Status = MapBindingStatus(binding.Status),
        SyncEnabled = binding.SyncEnabled,
        SyncIntervalMinutes = binding.SyncIntervalMinutes,
        DocumentCount = binding.DocumentCount,
        CreatedAt = binding.CreatedAt,
        Owner = binding.Owner
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
            await _messageBus.TryPublishAsync(BINDING_CREATED_TOPIC, eventModel, cancellationToken: cancellationToken);
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
            await _messageBus.TryPublishAsync(BINDING_REMOVED_TOPIC, eventModel, cancellationToken: cancellationToken);
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
            await _messageBus.TryPublishAsync(SYNC_STARTED_TOPIC, eventModel, cancellationToken: cancellationToken);
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
            var status = result.Status switch
            {
                Models.SyncStatusInternal.Success => SyncStatus.Success,
                Models.SyncStatusInternal.Partial => SyncStatus.Partial,
                Models.SyncStatusInternal.Failed => SyncStatus.Failed,
                _ => SyncStatus.Failed
            };
            var eventModel = new DocumentationSyncCompletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Namespace = binding.Namespace,
                BindingId = binding.BindingId,
                SyncId = syncId,
                Status = status,
                CommitHash = result.CommitHash,
                DocumentsCreated = result.DocumentsCreated,
                DocumentsUpdated = result.DocumentsUpdated,
                DocumentsDeleted = result.DocumentsDeleted,
                DurationMs = result.DurationMs,
                ErrorMessage = result.ErrorMessage
            };
            await _messageBus.TryPublishAsync(SYNC_COMPLETED_TOPIC, eventModel, cancellationToken: cancellationToken);
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
        var docsStore = _stateStoreFactory.GetStore<HashSet<Guid>>(StateStoreDefinitions.Documentation);
        var docIds = await docsStore.GetAsync(docsKey, cancellationToken);

        if (docIds == null || docIds.Count == 0)
        {
            return [];
        }

        var docStore = _stateStoreFactory.GetStore<StoredDocument>(StateStoreDefinitions.Documentation);
        var documents = new List<StoredDocument>();

        foreach (var docId in docIds)
        {
            var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{docId}";
            var doc = await docStore.GetAsync(docKey, cancellationToken);
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
        var archiveStore = _stateStoreFactory.GetStore<Models.DocumentationArchive>(StateStoreDefinitions.Documentation);
        await archiveStore.SaveAsync(archiveKey, archive, cancellationToken: cancellationToken);

        // Also update the namespace archive list (with ETag protection)
        var listKey = $"{ARCHIVE_KEY_PREFIX}list:{archive.Namespace}";
        var listStore = _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Documentation);

        const int maxRetries = 3;
        for (int retry = 0; retry < maxRetries; retry++)
        {
            var (archiveIds, etag) = await listStore.GetWithETagAsync(listKey, cancellationToken);
            archiveIds ??= [];

            if (archiveIds.Contains(archive.ArchiveId)) break;

            archiveIds.Add(archive.ArchiveId);

            string? savedEtag;
            if (etag != null)
            {
                savedEtag = await listStore.TrySaveAsync(listKey, archiveIds, etag, cancellationToken: cancellationToken);
            }
            else
            {
                savedEtag = await listStore.SaveAsync(listKey, archiveIds, cancellationToken: cancellationToken);
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
        var listStore = _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Documentation);
        var archiveIds = await listStore.GetAsync(listKey, cancellationToken);

        if (archiveIds == null || archiveIds.Count == 0)
        {
            return [];
        }

        var archiveStore = _stateStoreFactory.GetStore<Models.DocumentationArchive>(StateStoreDefinitions.Documentation);
        var archives = new List<Models.DocumentationArchive>();

        foreach (var archiveId in archiveIds)
        {
            var archiveKey = $"{ARCHIVE_KEY_PREFIX}{archiveId}";
            var archive = await archiveStore.GetAsync(archiveKey, cancellationToken);
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
        var archiveStore = _stateStoreFactory.GetStore<Models.DocumentationArchive>(StateStoreDefinitions.Documentation);
        return await archiveStore.GetAsync(archiveKey, cancellationToken);
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
        var docStore = _stateStoreFactory.GetStore<StoredDocument>(StateStoreDefinitions.Documentation);
        var slugStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Documentation);
        var docsStore = _stateStoreFactory.GetStore<HashSet<Guid>>(StateStoreDefinitions.Documentation);
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
            await docStore.SaveAsync(docKey, storedDoc, cancellationToken: cancellationToken);

            var slugKey = $"{SLUG_INDEX_PREFIX}{namespaceId}:{storedDoc.Slug}";
            await slugStore.SaveAsync(slugKey, storedDoc.DocumentId.ToString(), cancellationToken: cancellationToken);

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

        await docsStore.SaveAsync(docsKey, docIds, cancellationToken: cancellationToken);

        return bundle.Documents.Count;
    }

    /// <summary>
    /// Deletes an archive record from state store.
    /// </summary>
    private async Task DeleteArchiveAsync(Models.DocumentationArchive archive, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.documentation", "DocumentationService.DeleteArchiveAsync");
        var archiveKey = $"{ARCHIVE_KEY_PREFIX}{archive.ArchiveId}";
        var archiveStore = _stateStoreFactory.GetStore<Models.DocumentationArchive>(StateStoreDefinitions.Documentation);
        await archiveStore.DeleteAsync(archiveKey, cancellationToken);

        // Remove from namespace archive list
        var listKey = $"{ARCHIVE_KEY_PREFIX}list:{archive.Namespace}";
        var listStore = _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Documentation);
        var archiveIds = await listStore.GetAsync(listKey, cancellationToken);
        if (archiveIds != null)
        {
            archiveIds.Remove(archive.ArchiveId);
            await listStore.SaveAsync(listKey, archiveIds, cancellationToken: cancellationToken);
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
            await _messageBus.TryPublishAsync(ARCHIVE_CREATED_TOPIC, eventModel, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish archive created event");
        }
    }

    #endregion

    #endregion

    #region Internal Types

    /// <summary>
    /// Bundle format for documentation archives.
    /// </summary>
    internal sealed class DocumentationBundle
    {
        public string Version { get; set; } = "1.0";
        public string Namespace { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public List<BundledDocument> Documents { get; set; } = [];
    }

    /// <summary>
    /// Document data within a bundle.
    /// </summary>
    internal sealed class BundledDocument
    {
        public Guid DocumentId { get; set; }
        public string Slug { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string? Summary { get; set; }
        public string? VoiceSummary { get; set; }
        public List<string>? Tags { get; set; }
        public object? Metadata { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    /// <summary>
    /// Internal model for document storage in lib-state store.
    /// </summary>
    internal sealed class StoredDocument
    {
        public Guid DocumentId { get; set; }
        public string Namespace { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string? Content { get; set; }
        public string? Summary { get; set; }
        public string? VoiceSummary { get; set; }
        public List<string> Tags { get; set; } = [];
        public List<Guid> RelatedDocuments { get; set; } = [];
        public object? Metadata { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    /// <summary>
    /// Internal model for trashcan storage with TTL metadata.
    /// </summary>
    internal sealed class TrashedDocument
    {
        public StoredDocument Document { get; set; } = new();
        public DateTimeOffset DeletedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }

    #endregion

    /// <summary>
    /// Thread-safe in-memory cache for search results.
    /// Static lifetime shared across scoped service instances (performance optimization, not authoritative state).
    /// Uses ConcurrentDictionary per IMPLEMENTATION TENETS (Multi-Instance Safety).
    /// </summary>
    private sealed class SearchResultCache
    {
        private readonly ConcurrentDictionary<string, (SearchDocumentationResponse Response, DateTimeOffset Expiry)> _cache = new();

        /// <summary>
        /// Builds a deterministic cache key from search parameters.
        /// </summary>
        public static string BuildKey(string namespaceId, string searchTerm, string? category, int maxResults)
        {
            return $"{namespaceId}:{searchTerm}:{category ?? "_"}:{maxResults}";
        }

        /// <summary>
        /// Attempts to retrieve a cached search response. Returns false if not found or expired.
        /// </summary>
        public bool TryGet(string key, out SearchDocumentationResponse? response)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.Expiry > DateTimeOffset.UtcNow)
                {
                    response = entry.Response;
                    return true;
                }

                // Expired - remove stale entry
                _cache.TryRemove(key, out _);
            }

            response = null;
            return false;
        }

        /// <summary>
        /// Stores a search response in the cache with the specified TTL.
        /// </summary>
        public void Set(string key, SearchDocumentationResponse response, TimeSpan ttl)
        {
            _cache[key] = (response, DateTimeOffset.UtcNow.Add(ttl));
        }
    }
}
