using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Documentation.Services;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Documentation;

/// <summary>
/// State-store implementation for Documentation service following schema-first architecture.
/// Uses IStateStoreFactory for persistence.
/// </summary>
[DaprService("documentation", typeof(IDocumentationService), lifetime: ServiceLifetime.Scoped)]
public partial class DocumentationService : IDocumentationService
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<DocumentationService> _logger;
    private readonly DocumentationServiceConfiguration _configuration;
    private readonly ISearchIndexService _searchIndexService;

    private const string STATE_STORE = "documentation-statestore";

    // Event topics following Tenet 16: {entity}.{action} pattern
    private const string DOCUMENT_CREATED_TOPIC = "document.created";
    private const string DOCUMENT_UPDATED_TOPIC = "document.updated";
    private const string DOCUMENT_DELETED_TOPIC = "document.deleted";

    // State store key prefixes per plan specification
    private const string DOC_KEY_PREFIX = "doc:";
    private const string SLUG_INDEX_PREFIX = "slug-idx:";
    private const string NAMESPACE_DOCS_PREFIX = "ns-docs:";
    private const string TRASH_KEY_PREFIX = "trash:";

    /// <summary>
    /// Creates a new instance of the DocumentationService.
    /// </summary>
    public DocumentationService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        ILogger<DocumentationService> logger,
        DocumentationServiceConfiguration configuration,
        IEventConsumer eventConsumer,
        ISearchIndexService searchIndexService)
    {
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _searchIndexService = searchIndexService ?? throw new ArgumentNullException(nameof(searchIndexService));

        // Register event handlers via partial class (minimal event subscriptions per schema)
        ArgumentNullException.ThrowIfNull(eventConsumer, nameof(eventConsumer));
        RegisterEventConsumers(eventConsumer);
    }

    /// <summary>
    /// Registers this service's API permissions with the Permissions service on startup.
    /// Overrides the default IDaprService implementation to use generated permission data.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {
        _logger.LogInformation("Registering Documentation service permissions...");
        await DocumentationPermissionRegistration.RegisterViaEventAsync(_messageBus, _logger);
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, object?)> ViewDocumentBySlugAsync(string slug, string? ns = "bannou", CancellationToken cancellationToken = default)
    {
        var namespaceId = ns ?? "bannou";
        _logger.LogDebug("ViewDocumentBySlug: slug={Slug}, namespace={Namespace}", slug, namespaceId);
        try
        {
            // Look up document ID from slug index
            var slugKey = $"{SLUG_INDEX_PREFIX}{namespaceId}:{slug}";
            var slugStore = _stateStoreFactory.GetStore<string>(STATE_STORE);
            var documentIdStr = await slugStore.GetAsync(slugKey, cancellationToken);
            if (string.IsNullOrEmpty(documentIdStr) || !Guid.TryParse(documentIdStr, out var documentId))
            {
                _logger.LogDebug("Document with slug {Slug} not found in namespace {Namespace}", slug, namespaceId);
                return (StatusCodes.NotFound, null);
            }

            // Fetch document content
            var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{documentId}";
            var docStore = _stateStoreFactory.GetStore<StoredDocument>(STATE_STORE);
            var storedDoc = await docStore.GetAsync(docKey, cancellationToken);
            if (storedDoc == null)
            {
                _logger.LogWarning("Document {DocumentId} found in slug index but not in store for namespace {Namespace}", documentId, namespaceId);
                return (StatusCodes.NotFound, null);
            }

            // Render markdown to HTML for browser display
            var htmlContent = RenderMarkdownToHtml(storedDoc.Content) ?? string.Empty;

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ViewDocumentBySlug");
            await _messageBus.TryPublishErrorAsync("documentation", "ViewDocumentBySlug", "unexpected_exception", ex.Message, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, QueryDocumentationResponse?)> QueryDocumentationAsync(QueryDocumentationRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("QueryDocumentation: namespace={Namespace}, query={Query}", body.Namespace, body.Query);
        try
        {
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
            var maxResults = body.MaxResults;
            var minRelevance = body.MinRelevanceScore;

            // Perform natural language query using search index
            // Pass category as string for filtering (or null if default enum value)
            var categoryFilter = body.Category == default ? null : body.Category.ToString();
            var searchResults = _searchIndexService.Query(
                namespaceId,
                body.Query,
                categoryFilter,
                maxResults,
                minRelevance);

            // Build response with document results
            var results = new List<DocumentResult>();
            var docStore = _stateStoreFactory.GetStore<StoredDocument>(STATE_STORE);
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
                        Summary = doc.Summary ?? string.Empty,
                        VoiceSummary = doc.VoiceSummary ?? string.Empty,
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
                TotalResults = results.Count,
                Query = body.Query,
                Namespace = namespaceId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in QueryDocumentation");
            await _messageBus.TryPublishErrorAsync("documentation", "QueryDocumentation", "unexpected_exception", ex.Message, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, GetDocumentResponse?)> GetDocumentAsync(GetDocumentRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("GetDocument: namespace={Namespace}, documentId={DocumentId}, slug={Slug}",
            body.Namespace, body.DocumentId, body.Slug);
        try
        {
            // Input validation
            if (string.IsNullOrWhiteSpace(body.Namespace))
            {
                _logger.LogWarning("GetDocument failed: Namespace is required");
                return (StatusCodes.BadRequest, null);
            }

            if (body.DocumentId == Guid.Empty && string.IsNullOrWhiteSpace(body.Slug))
            {
                _logger.LogWarning("GetDocument failed: Either DocumentId or Slug is required");
                return (StatusCodes.BadRequest, null);
            }

            var namespaceId = body.Namespace;
            Guid documentId;
            var slugStore = _stateStoreFactory.GetStore<string>(STATE_STORE);
            var docStore = _stateStoreFactory.GetStore<StoredDocument>(STATE_STORE);

            // Resolve document ID from slug if provided
            if (body.DocumentId != Guid.Empty)
            {
                documentId = body.DocumentId;
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
                Summary = storedDoc.Summary ?? string.Empty,
                VoiceSummary = storedDoc.VoiceSummary ?? string.Empty,
                Tags = storedDoc.Tags,
                RelatedDocuments = storedDoc.RelatedDocuments,
                Metadata = storedDoc.Metadata ?? new object(),
                CreatedAt = storedDoc.CreatedAt,
                UpdatedAt = storedDoc.UpdatedAt
            };

            // Include content if requested
            if (body.IncludeContent)
            {
                doc.Content = (body.RenderHtml ? RenderMarkdownToHtml(storedDoc.Content) : storedDoc.Content) ?? string.Empty;
            }

            var response = new GetDocumentResponse
            {
                Document = doc,
                ContentFormat = body.IncludeContent
                    ? (body.RenderHtml ? GetDocumentResponseContentFormat.Html : GetDocumentResponseContentFormat.Markdown)
                    : GetDocumentResponseContentFormat.None
            };

            // Include related documents if requested
            if (body.IncludeRelated != RelatedDepth.None && storedDoc.RelatedDocuments.Count > 0)
            {
                response.RelatedDocuments = await GetRelatedDocumentSummariesAsync(
                    namespaceId,
                    storedDoc.RelatedDocuments,
                    body.IncludeRelated,
                    cancellationToken);
            }

            _logger.LogDebug("Retrieved document {DocumentId} from namespace {Namespace}", documentId, namespaceId);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetDocument");
            await _messageBus.TryPublishErrorAsync("documentation", "GetDocument", "unexpected_exception", ex.Message, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, SearchDocumentationResponse?)> SearchDocumentationAsync(SearchDocumentationRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("SearchDocumentation: namespace={Namespace}, term={Term}", body.Namespace, body.SearchTerm);
        try
        {
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
            var maxResults = body.MaxResults;

            // Perform keyword search using search index
            var categoryFilter = body.Category == default ? null : body.Category.ToString();
            var searchResults = _searchIndexService.Search(
                namespaceId,
                body.SearchTerm,
                categoryFilter,
                maxResults);

            // Build response with document results
            var results = new List<DocumentResult>();
            var docStore = _stateStoreFactory.GetStore<StoredDocument>(STATE_STORE);
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
                        Summary = doc.Summary ?? string.Empty,
                        VoiceSummary = doc.VoiceSummary ?? string.Empty,
                        RelevanceScore = (float)result.RelevanceScore,
                        MatchHighlights = new List<string> { GenerateSearchSnippet(doc.Content, body.SearchTerm) }
                    });
                }
            }

            // Publish analytics event (non-blocking)
            _ = PublishSearchAnalyticsEventAsync(namespaceId, body.SearchTerm, body.SessionId, results.Count);

            _logger.LogInformation("Search in namespace {Namespace} for '{Term}' returned {Count} results",
                namespaceId, body.SearchTerm, results.Count);

            return (StatusCodes.OK, new SearchDocumentationResponse
            {
                Results = results,
                TotalResults = results.Count,
                SearchTerm = body.SearchTerm,
                Namespace = namespaceId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SearchDocumentation");
            await _messageBus.TryPublishErrorAsync("documentation", "SearchDocumentation", "unexpected_exception", ex.Message, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ListDocumentsResponse?)> ListDocumentsAsync(ListDocumentsRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("ListDocuments: namespace={Namespace}, category={Category}", body.Namespace, body.Category);
        try
        {
            // Input validation
            if (string.IsNullOrWhiteSpace(body.Namespace))
            {
                _logger.LogWarning("ListDocuments failed: Namespace is required");
                return (StatusCodes.BadRequest, null);
            }

            var namespaceId = body.Namespace;
            var page = body.Page;
            var pageSize = body.PageSize;

            // Convert page-based to skip/take for search index
            var skip = (page - 1) * pageSize;
            var take = pageSize;

            // Get document IDs from search index (respects category filter)
            var categoryFilter = body.Category == default ? null : body.Category.ToString();
            var docIds = _searchIndexService.ListDocumentIds(
                namespaceId,
                categoryFilter,
                skip,
                take);

            // Fetch document summaries
            var documents = new List<DocumentSummary>();
            var docStore = _stateStoreFactory.GetStore<StoredDocument>(STATE_STORE);
            foreach (var docId in docIds)
            {
                var docKey = $"{DOC_KEY_PREFIX}{namespaceId}:{docId}";
                var doc = await docStore.GetAsync(docKey, cancellationToken);

                if (doc != null)
                {
                    documents.Add(new DocumentSummary
                    {
                        DocumentId = doc.DocumentId,
                        Slug = doc.Slug,
                        Title = doc.Title,
                        Category = ParseDocumentCategory(doc.Category),
                        Summary = doc.Summary ?? string.Empty,
                        VoiceSummary = doc.VoiceSummary ?? string.Empty,
                        Tags = doc.Tags
                    });
                }
            }

            // Get total count from namespace stats
            var stats = _searchIndexService.GetNamespaceStats(namespaceId);
            var totalCount = body.Category != default
                ? stats.DocumentsByCategory.GetValueOrDefault(body.Category.ToString(), 0)
                : stats.TotalDocuments;

            // Calculate total pages
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            _logger.LogDebug("ListDocuments returned {Count} of {Total} documents in namespace {Namespace}",
                documents.Count, totalCount, namespaceId);

            return (StatusCodes.OK, new ListDocumentsResponse
            {
                Documents = documents,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = totalPages,
                Namespace = namespaceId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ListDocuments");
            await _messageBus.TryPublishErrorAsync("documentation", "ListDocuments", "unexpected_exception", ex.Message, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, SuggestRelatedResponse?)> SuggestRelatedTopicsAsync(SuggestRelatedRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("SuggestRelatedTopics: namespace={Namespace}, source={SourceValue}", body.Namespace, body.SourceValue);
        try
        {
            var namespaceId = body.Namespace;
            var maxSuggestions = body.MaxSuggestions;

            // Get related document IDs from search index
            var relatedIds = _searchIndexService.GetRelatedSuggestions(
                namespaceId,
                body.SourceValue,
                maxSuggestions);

            // Fetch document summaries and build topic suggestions
            var suggestions = new List<TopicSuggestion>();
            var docStore = _stateStoreFactory.GetStore<StoredDocument>(STATE_STORE);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SuggestRelatedTopics");
            await _messageBus.TryPublishErrorAsync("documentation", "SuggestRelatedTopics", "unexpected_exception", ex.Message, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, CreateDocumentResponse?)> CreateDocumentAsync(CreateDocumentRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("CreateDocument: namespace={Namespace}, slug={Slug}", body.Namespace, body.Slug);
        try
        {
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

            var namespaceId = body.Namespace;
            var slug = body.Slug;
            var slugStore = _stateStoreFactory.GetStore<string>(STATE_STORE);
            var docStore = _stateStoreFactory.GetStore<StoredDocument>(STATE_STORE);

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
                VoiceSummary = body.VoiceSummary,
                Tags = body.Tags?.ToList() ?? [],
                RelatedDocuments = body.RelatedDocuments?.ToList() ?? [],
                Metadata = body.Metadata,
                CreatedAt = now,
                UpdatedAt = now
            };

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CreateDocument");
            await _messageBus.TryPublishErrorAsync("documentation", "CreateDocument", "unexpected_exception", ex.Message, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, UpdateDocumentResponse?)> UpdateDocumentAsync(UpdateDocumentRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("UpdateDocument: namespace={Namespace}, documentId={DocumentId}", body.Namespace, body.DocumentId);
        try
        {
            var namespaceId = body.Namespace;
            var documentId = body.DocumentId;

            if (documentId == Guid.Empty)
            {
                _logger.LogWarning("UpdateDocument requires documentId");
                return (StatusCodes.BadRequest, null);
            }

            var slugStore = _stateStoreFactory.GetStore<string>(STATE_STORE);
            var docStore = _stateStoreFactory.GetStore<StoredDocument>(STATE_STORE);

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

            if (body.Category != default && body.Category.ToString() != storedDoc.Category)
            {
                storedDoc.Category = body.Category.ToString();
                changedFields.Add("category");
            }

            if (!string.IsNullOrEmpty(body.Content) && body.Content != storedDoc.Content)
            {
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
                storedDoc.VoiceSummary = body.VoiceSummary;
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in UpdateDocument");
            await _messageBus.TryPublishErrorAsync("documentation", "UpdateDocument", "unexpected_exception", ex.Message, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, DeleteDocumentResponse?)> DeleteDocumentAsync(DeleteDocumentRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("DeleteDocument: namespace={Namespace}, documentId={DocumentId}, slug={Slug}",
            body.Namespace, body.DocumentId, body.Slug);
        try
        {
            // Input validation
            if (string.IsNullOrWhiteSpace(body.Namespace))
            {
                _logger.LogWarning("DeleteDocument failed: Namespace is required");
                return (StatusCodes.BadRequest, null);
            }

            if (body.DocumentId == Guid.Empty && string.IsNullOrWhiteSpace(body.Slug))
            {
                _logger.LogWarning("DeleteDocument failed: Either DocumentId or Slug is required");
                return (StatusCodes.BadRequest, null);
            }

            var namespaceId = body.Namespace;
            Guid documentId;
            var slugStore = _stateStoreFactory.GetStore<string>(STATE_STORE);
            var docStore = _stateStoreFactory.GetStore<StoredDocument>(STATE_STORE);
            var trashStore = _stateStoreFactory.GetStore<TrashedDocument>(STATE_STORE);

            // Resolve document ID from slug if provided
            if (body.DocumentId != Guid.Empty)
            {
                documentId = body.DocumentId;
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in DeleteDocument");
            await _messageBus.TryPublishErrorAsync("documentation", "DeleteDocument", "unexpected_exception", ex.Message, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, RecoverDocumentResponse?)> RecoverDocumentAsync(RecoverDocumentRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("RecoverDocument: namespace={Namespace}, documentId={DocumentId}", body.Namespace, body.DocumentId);
        try
        {
            var namespaceId = body.Namespace;
            var documentId = body.DocumentId;
            var slugStore = _stateStoreFactory.GetStore<string>(STATE_STORE);
            var docStore = _stateStoreFactory.GetStore<StoredDocument>(STATE_STORE);
            var trashStore = _stateStoreFactory.GetStore<TrashedDocument>(STATE_STORE);

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in RecoverDocument");
            await _messageBus.TryPublishErrorAsync("documentation", "RecoverDocument", "unexpected_exception", ex.Message, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, BulkUpdateResponse?)> BulkUpdateDocumentsAsync(BulkUpdateRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("BulkUpdateDocuments: namespace={Namespace}, count={Count}", body.Namespace, body.DocumentIds.Count);
        try
        {
            var namespaceId = body.Namespace;
            var succeeded = new List<Guid>();
            var failed = new List<BulkOperationFailure>();
            var now = DateTimeOffset.UtcNow;
            var docStore = _stateStoreFactory.GetStore<StoredDocument>(STATE_STORE);

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

                    // Apply category update if specified (non-default value)
                    if (body.Category != default)
                    {
                        storedDoc.Category = body.Category.ToString();
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
            }

            _logger.LogInformation("BulkUpdate in namespace {Namespace}: {Succeeded} succeeded, {Failed} failed",
                namespaceId, succeeded.Count, failed.Count);

            return (StatusCodes.OK, new BulkUpdateResponse
            {
                Succeeded = succeeded,
                Failed = failed
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in BulkUpdateDocuments");
            await _messageBus.TryPublishErrorAsync("documentation", "BulkUpdateDocuments", "unexpected_exception", ex.Message, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, BulkDeleteResponse?)> BulkDeleteDocumentsAsync(BulkDeleteRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("BulkDeleteDocuments: namespace={Namespace}, count={Count}", body.Namespace, body.DocumentIds.Count);
        try
        {
            var namespaceId = body.Namespace;
            var succeeded = new List<Guid>();
            var failed = new List<BulkOperationFailure>();
            var now = DateTimeOffset.UtcNow;
            var trashcanTtl = TimeSpan.FromDays(_configuration.TrashcanTtlDays);
            var expiresAt = now.Add(trashcanTtl);
            var slugStore = _stateStoreFactory.GetStore<string>(STATE_STORE);
            var docStore = _stateStoreFactory.GetStore<StoredDocument>(STATE_STORE);
            var trashStore = _stateStoreFactory.GetStore<TrashedDocument>(STATE_STORE);

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
            }

            _logger.LogInformation("BulkDelete in namespace {Namespace}: {Succeeded} succeeded, {Failed} failed",
                namespaceId, succeeded.Count, failed.Count);

            return (StatusCodes.OK, new BulkDeleteResponse
            {
                Succeeded = succeeded,
                Failed = failed
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in BulkDeleteDocuments");
            await _messageBus.TryPublishErrorAsync("documentation", "BulkDeleteDocuments", "unexpected_exception", ex.Message, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ImportDocumentationResponse?)> ImportDocumentationAsync(ImportDocumentationRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("ImportDocumentation: namespace={Namespace}, count={Count}", body.Namespace, body.Documents.Count);
        try
        {
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

            var slugStore = _stateStoreFactory.GetStore<string>(STATE_STORE);
            var docStore = _stateStoreFactory.GetStore<StoredDocument>(STATE_STORE);

            foreach (var importDoc in body.Documents)
            {
                try
                {
                    // Check if slug exists
                    var slugKey = $"{SLUG_INDEX_PREFIX}{namespaceId}:{importDoc.Slug}";
                    var existingDocIdStr = await slugStore.GetAsync(slugKey, cancellationToken);

                    if (!string.IsNullOrEmpty(existingDocIdStr) && Guid.TryParse(existingDocIdStr, out var existingDocId))
                    {
                        // Handle conflict based on policy
                        switch (body.OnConflict)
                        {
                            case ImportDocumentationRequestOnConflict.Skip:
                                skipped++;
                                continue;

                            case ImportDocumentationRequestOnConflict.Fail:
                                failed.Add(new ImportFailure { Slug = importDoc.Slug, Error = "Document already exists" });
                                continue;

                            case ImportDocumentationRequestOnConflict.Update:
                                // Update existing document
                                var existingDocKey = $"{DOC_KEY_PREFIX}{namespaceId}:{existingDocId}";
                                var existingDoc = await docStore.GetAsync(existingDocKey, cancellationToken);
                                if (existingDoc != null)
                                {
                                    existingDoc.Title = importDoc.Title;
                                    existingDoc.Category = importDoc.Category.ToString();
                                    existingDoc.Content = importDoc.Content;
                                    existingDoc.Summary = importDoc.Summary;
                                    existingDoc.VoiceSummary = importDoc.VoiceSummary;
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
                        VoiceSummary = importDoc.VoiceSummary,
                        Tags = importDoc.Tags?.ToList() ?? [],
                        RelatedDocuments = [],
                        Metadata = importDoc.Metadata,
                        CreatedAt = now,
                        UpdatedAt = now
                    };

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ImportDocumentation");
            await _messageBus.TryPublishErrorAsync("documentation", "ImportDocumentation", "unexpected_exception", ex.Message, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ListTrashcanResponse?)> ListTrashcanAsync(ListTrashcanRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("ListTrashcan: namespace={Namespace}", body.Namespace);
        try
        {
            var namespaceId = body.Namespace;
            var page = body.Page;
            var pageSize = body.PageSize;
            var guidListStore = _stateStoreFactory.GetStore<List<Guid>>(STATE_STORE);
            var trashStore = _stateStoreFactory.GetStore<TrashedDocument>(STATE_STORE);

            // Get trashcan items from namespace trashcan list
            var trashListKey = $"ns-trash:{namespaceId}";
            var trashedDocIds = await guidListStore.GetAsync(trashListKey, cancellationToken) ?? [];

            var items = new List<TrashcanItem>();
            var now = DateTimeOffset.UtcNow;
            var expiredIds = new List<Guid>();

            // Fetch trashcan items
            foreach (var docId in trashedDocIds)
            {
                var trashKey = $"{TRASH_KEY_PREFIX}{namespaceId}:{docId}";
                var trashedDoc = await trashStore.GetAsync(trashKey, cancellationToken);

                if (trashedDoc == null)
                {
                    expiredIds.Add(docId);
                    continue;
                }

                // Check if expired
                if (trashedDoc.ExpiresAt < now)
                {
                    expiredIds.Add(docId);
                    await trashStore.DeleteAsync(trashKey, cancellationToken);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ListTrashcan");
            await _messageBus.TryPublishErrorAsync("documentation", "ListTrashcan", "unexpected_exception", ex.Message, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, PurgeTrashcanResponse?)> PurgeTrashcanAsync(PurgeTrashcanRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("PurgeTrashcan: namespace={Namespace}", body.Namespace);
        try
        {
            var namespaceId = body.Namespace;
            var purgedCount = 0;
            var guidListStore = _stateStoreFactory.GetStore<List<Guid>>(STATE_STORE);
            var trashStore = _stateStoreFactory.GetStore<TrashedDocument>(STATE_STORE);

            // Get trashcan list
            var trashListKey = $"ns-trash:{namespaceId}";
            var trashedDocIds = await guidListStore.GetAsync(trashListKey, cancellationToken) ?? [];

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

            // Update trashcan list
            if (purgedCount > 0)
            {
                if (trashedDocIds.Count > 0)
                {
                    await guidListStore.SaveAsync(trashListKey, trashedDocIds, cancellationToken: cancellationToken);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in PurgeTrashcan");
            await _messageBus.TryPublishErrorAsync("documentation", "PurgeTrashcan", "unexpected_exception", ex.Message, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, NamespaceStatsResponse?)> GetNamespaceStatsAsync(GetNamespaceStatsRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("GetNamespaceStats: namespace={Namespace}", body.Namespace);
        try
        {
            var namespaceId = body.Namespace;
            var guidListStore = _stateStoreFactory.GetStore<List<Guid>>(STATE_STORE);
            var docStore = _stateStoreFactory.GetStore<StoredDocument>(STATE_STORE);

            // Get stats from search index
            var searchStats = _searchIndexService.GetNamespaceStats(namespaceId);

            // Get trashcan count
            var trashListKey = $"ns-trash:{namespaceId}";
            var trashedDocIds = await guidListStore.GetAsync(trashListKey, cancellationToken) ?? [];

            // Calculate total content size (approximate from document count)
            // In production, this could be tracked more precisely
            var estimatedContentSize = searchStats.TotalDocuments * 10000; // ~10KB average per document

            // Find last updated document
            var lastUpdated = DateTimeOffset.MinValue;
            var docListKey = $"{NAMESPACE_DOCS_PREFIX}{namespaceId}";
            var docIds = await guidListStore.GetAsync(docListKey, cancellationToken) ?? [];

            if (docIds.Count > 0)
            {
                // Sample a few recent documents to find last updated
                foreach (var docId in docIds.Take(10))
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetNamespaceStats");
            await _messageBus.TryPublishErrorAsync("documentation", "GetNamespaceStats", "unexpected_exception", ex.Message, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #region Helper Methods

    /// <summary>
    /// Adds a document ID to the namespace document list for pagination support.
    /// </summary>
    private async Task AddDocumentToNamespaceIndexAsync(string namespaceId, Guid documentId, CancellationToken cancellationToken)
    {
        var guidListStore = _stateStoreFactory.GetStore<List<Guid>>(STATE_STORE);
        var indexKey = $"{NAMESPACE_DOCS_PREFIX}{namespaceId}";
        var docIds = await guidListStore.GetAsync(indexKey, cancellationToken) ?? [];

        if (!docIds.Contains(documentId))
        {
            docIds.Add(documentId);
            await guidListStore.SaveAsync(indexKey, docIds, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Removes a document ID from the namespace document list.
    /// </summary>
    private async Task RemoveDocumentFromNamespaceIndexAsync(string namespaceId, Guid documentId, CancellationToken cancellationToken)
    {
        var guidListStore = _stateStoreFactory.GetStore<List<Guid>>(STATE_STORE);
        var indexKey = $"{NAMESPACE_DOCS_PREFIX}{namespaceId}";
        var docIds = await guidListStore.GetAsync(indexKey, cancellationToken);

        if (docIds != null && docIds.Remove(documentId))
        {
            await guidListStore.SaveAsync(indexKey, docIds, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Adds a document ID to the namespace trashcan list.
    /// </summary>
    private async Task AddDocumentToTrashcanIndexAsync(string namespaceId, Guid documentId, CancellationToken cancellationToken)
    {
        var guidListStore = _stateStoreFactory.GetStore<List<Guid>>(STATE_STORE);
        var trashListKey = $"ns-trash:{namespaceId}";
        var trashedDocIds = await guidListStore.GetAsync(trashListKey, cancellationToken) ?? [];

        if (!trashedDocIds.Contains(documentId))
        {
            trashedDocIds.Add(documentId);
            await guidListStore.SaveAsync(trashListKey, trashedDocIds, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Removes a document ID from the namespace trashcan list.
    /// </summary>
    private async Task RemoveDocumentFromTrashcanIndexAsync(string namespaceId, Guid documentId, CancellationToken cancellationToken)
    {
        var guidListStore = _stateStoreFactory.GetStore<List<Guid>>(STATE_STORE);
        var trashListKey = $"ns-trash:{namespaceId}";
        var trashedDocIds = await guidListStore.GetAsync(trashListKey, cancellationToken);

        if (trashedDocIds != null && trashedDocIds.Remove(documentId))
        {
            if (trashedDocIds.Count > 0)
            {
                await guidListStore.SaveAsync(trashListKey, trashedDocIds, cancellationToken: cancellationToken);
            }
            else
            {
                await guidListStore.DeleteAsync(trashListKey, cancellationToken);
            }
        }
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
    /// Renders markdown content to HTML (placeholder for future implementation).
    /// </summary>
    private static string? RenderMarkdownToHtml(string? markdown)
    {
        // TODO: Implement proper markdown rendering (e.g., with Markdig)
        // For now, return as-is
        return markdown;
    }

    /// <summary>
    /// Determines the relevance reason for a suggested document based on the source type.
    /// </summary>
    private static string DetermineRelevanceReason(SuggestionSource source, string sourceValue, StoredDocument doc)
    {
        return source switch
        {
            SuggestionSource.Document_id => $"Related to document {sourceValue}",
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
        var summaries = new List<DocumentSummary>();
        var maxRelated = depth == RelatedDepth.Extended ? 10 : 5;
        var docStore = _stateStoreFactory.GetStore<StoredDocument>(STATE_STORE);

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
                    Summary = doc.Summary ?? string.Empty,
                    VoiceSummary = doc.VoiceSummary ?? string.Empty,
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

            await _messageBus.PublishAsync(DOCUMENT_CREATED_TOPIC, eventModel);
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

            await _messageBus.PublishAsync(DOCUMENT_UPDATED_TOPIC, eventModel);
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

            await _messageBus.PublishAsync(DOCUMENT_DELETED_TOPIC, eventModel);
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

            await _messageBus.PublishAsync("documentation.queried", eventModel);
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

            await _messageBus.PublishAsync("documentation.searched", eventModel);
            _logger.LogDebug("Published DocumentationSearchedEvent for term '{Term}'", searchTerm);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish DocumentationSearchedEvent");
            // Non-critical - don't propagate
        }
    }

    #endregion

    #region Internal Types

    /// <summary>
    /// Internal model for document storage in Dapr state store.
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
}
