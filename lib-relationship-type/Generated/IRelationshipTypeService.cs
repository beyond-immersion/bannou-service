using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.RelationshipType;

/// <summary>
/// Service interface for RelationshipType API
/// </summary>
public partial interface IRelationshipTypeService : IBannouService
{
    /// <summary>
    /// GetRelationshipType operation
    /// </summary>
    Task<(StatusCodes, RelationshipTypeResponse?)> GetRelationshipTypeAsync(GetRelationshipTypeRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetRelationshipTypeByCode operation
    /// </summary>
    Task<(StatusCodes, RelationshipTypeResponse?)> GetRelationshipTypeByCodeAsync(GetRelationshipTypeByCodeRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// ListRelationshipTypes operation
    /// </summary>
    Task<(StatusCodes, RelationshipTypeListResponse?)> ListRelationshipTypesAsync(ListRelationshipTypesRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetChildRelationshipTypes operation
    /// </summary>
    Task<(StatusCodes, RelationshipTypeListResponse?)> GetChildRelationshipTypesAsync(GetChildRelationshipTypesRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// MatchesHierarchy operation
    /// </summary>
    Task<(StatusCodes, MatchesHierarchyResponse?)> MatchesHierarchyAsync(MatchesHierarchyRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetAncestors operation
    /// </summary>
    Task<(StatusCodes, RelationshipTypeListResponse?)> GetAncestorsAsync(GetAncestorsRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// CreateRelationshipType operation
    /// </summary>
    Task<(StatusCodes, RelationshipTypeResponse?)> CreateRelationshipTypeAsync(CreateRelationshipTypeRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// UpdateRelationshipType operation
    /// </summary>
    Task<(StatusCodes, RelationshipTypeResponse?)> UpdateRelationshipTypeAsync(UpdateRelationshipTypeRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// DeleteRelationshipType operation
    /// </summary>
    Task<StatusCodes> DeleteRelationshipTypeAsync(DeleteRelationshipTypeRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// DeprecateRelationshipType operation
    /// </summary>
    Task<(StatusCodes, RelationshipTypeResponse?)> DeprecateRelationshipTypeAsync(DeprecateRelationshipTypeRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// UndeprecateRelationshipType operation
    /// </summary>
    Task<(StatusCodes, RelationshipTypeResponse?)> UndeprecateRelationshipTypeAsync(UndeprecateRelationshipTypeRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// MergeRelationshipType operation
    /// </summary>
    Task<(StatusCodes, MergeRelationshipTypeResponse?)> MergeRelationshipTypeAsync(MergeRelationshipTypeRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// SeedRelationshipTypes operation
    /// </summary>
    Task<(StatusCodes, SeedRelationshipTypesResponse?)> SeedRelationshipTypesAsync(SeedRelationshipTypesRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
