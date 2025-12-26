using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Relationship;

/// <summary>
/// Service interface for Relationship API
/// </summary>
[Obsolete]
public partial interface IRelationshipService : IDaprService
{
    /// <summary>
    /// CreateRelationship operation
    /// </summary>
    Task<(StatusCodes, RelationshipResponse?)> CreateRelationshipAsync(CreateRelationshipRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetRelationship operation
    /// </summary>
    Task<(StatusCodes, RelationshipResponse?)> GetRelationshipAsync(GetRelationshipRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// ListRelationshipsByEntity operation
    /// </summary>
    Task<(StatusCodes, RelationshipListResponse?)> ListRelationshipsByEntityAsync(ListRelationshipsByEntityRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetRelationshipsBetween operation
    /// </summary>
    Task<(StatusCodes, RelationshipListResponse?)> GetRelationshipsBetweenAsync(GetRelationshipsBetweenRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// ListRelationshipsByType operation
    /// </summary>
    Task<(StatusCodes, RelationshipListResponse?)> ListRelationshipsByTypeAsync(ListRelationshipsByTypeRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// UpdateRelationship operation
    /// </summary>
    Task<(StatusCodes, RelationshipResponse?)> UpdateRelationshipAsync(UpdateRelationshipRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// EndRelationship operation
    /// </summary>
    Task<(StatusCodes, object?)> EndRelationshipAsync(EndRelationshipRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
