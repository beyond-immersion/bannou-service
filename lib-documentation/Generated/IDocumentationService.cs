using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Documentation;

/// <summary>
/// Service interface for Documentation API
/// </summary>
[Obsolete]
public partial interface IDocumentationService : IDaprService
{
    /// <summary>
    /// ViewDocumentBySlug operation
    /// </summary>
    Task<(StatusCodes, object?)> ViewDocumentBySlugAsync(string slug, string? ns = "bannou", CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// QueryDocumentation operation
    /// </summary>
    Task<(StatusCodes, QueryDocumentationResponse?)> QueryDocumentationAsync(QueryDocumentationRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetDocument operation
    /// </summary>
    Task<(StatusCodes, GetDocumentResponse?)> GetDocumentAsync(GetDocumentRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// SearchDocumentation operation
    /// </summary>
    Task<(StatusCodes, SearchDocumentationResponse?)> SearchDocumentationAsync(SearchDocumentationRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// ListDocuments operation
    /// </summary>
    Task<(StatusCodes, ListDocumentsResponse?)> ListDocumentsAsync(ListDocumentsRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// SuggestRelatedTopics operation
    /// </summary>
    Task<(StatusCodes, SuggestRelatedResponse?)> SuggestRelatedTopicsAsync(SuggestRelatedRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// CreateDocument operation
    /// </summary>
    Task<(StatusCodes, CreateDocumentResponse?)> CreateDocumentAsync(CreateDocumentRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// UpdateDocument operation
    /// </summary>
    Task<(StatusCodes, UpdateDocumentResponse?)> UpdateDocumentAsync(UpdateDocumentRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// DeleteDocument operation
    /// </summary>
    Task<(StatusCodes, DeleteDocumentResponse?)> DeleteDocumentAsync(DeleteDocumentRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// RecoverDocument operation
    /// </summary>
    Task<(StatusCodes, RecoverDocumentResponse?)> RecoverDocumentAsync(RecoverDocumentRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// BulkUpdateDocuments operation
    /// </summary>
    Task<(StatusCodes, BulkUpdateResponse?)> BulkUpdateDocumentsAsync(BulkUpdateRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// BulkDeleteDocuments operation
    /// </summary>
    Task<(StatusCodes, BulkDeleteResponse?)> BulkDeleteDocumentsAsync(BulkDeleteRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// ImportDocumentation operation
    /// </summary>
    Task<(StatusCodes, ImportDocumentationResponse?)> ImportDocumentationAsync(ImportDocumentationRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// ListTrashcan operation
    /// </summary>
    Task<(StatusCodes, ListTrashcanResponse?)> ListTrashcanAsync(ListTrashcanRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// PurgeTrashcan operation
    /// </summary>
    Task<(StatusCodes, PurgeTrashcanResponse?)> PurgeTrashcanAsync(PurgeTrashcanRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetNamespaceStats operation
    /// </summary>
    Task<(StatusCodes, NamespaceStatsResponse?)> GetNamespaceStatsAsync(GetNamespaceStatsRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
