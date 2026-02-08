using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Relationship;

/// <summary>
/// Implementation of the Relationship service.
/// Manages entity-to-entity relationships with composite uniqueness validation,
/// bidirectional support, and soft-delete capability. Also manages the hierarchical
/// relationship type taxonomy (definitions, hierarchy, deprecation, merge, seed).
/// </summary>
[BannouService("relationship", typeof(IRelationshipService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFoundation)]
public partial class RelationshipService : IRelationshipService
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<RelationshipService> _logger;
    private readonly RelationshipServiceConfiguration _configuration;

    // Relationship instance key prefixes (relationship-statestore)
    private const string RELATIONSHIP_KEY_PREFIX = "rel:";
    private const string ENTITY_INDEX_PREFIX = "entity-idx:";
    private const string TYPE_INDEX_PREFIX = "type-idx:";
    private const string COMPOSITE_KEY_PREFIX = "composite:";

    // Relationship type key prefixes (relationship-type-statestore)
    private const string RT_TYPE_KEY_PREFIX = "type:";
    private const string RT_CODE_INDEX_PREFIX = "code-index:";
    private const string RT_PARENT_INDEX_PREFIX = "parent-index:";
    private const string RT_ALL_TYPES_KEY = "all-types";

    /// <summary>
    /// Initializes a new instance of the RelationshipService.
    /// </summary>
    /// <param name="stateStoreFactory">Factory for getting state stores.</param>
    /// <param name="messageBus">Message bus for event publishing.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="configuration">Service configuration.</param>
    /// <param name="eventConsumer">Event consumer for registering event handlers.</param>
    public RelationshipService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        ILogger<RelationshipService> logger,
        RelationshipServiceConfiguration configuration,
        IEventConsumer eventConsumer)
    {
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;

        // Register event handlers via partial class (RelationshipServiceEvents.cs)
        ((IBannouService)this).RegisterEventConsumers(eventConsumer);
    }

    // ========================================================================
    // RELATIONSHIP INSTANCE OPERATIONS
    // ========================================================================

    #region Relationship Read Operations

    /// <summary>
    /// Retrieves a relationship by its unique identifier.
    /// </summary>
    /// <param name="body">Request containing the relationship ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code and relationship response if found.</returns>
    public async Task<(StatusCodes, RelationshipResponse?)> GetRelationshipAsync(
        GetRelationshipRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting relationship by ID: {RelationshipId}", body.RelationshipId);

            var relationshipKey = BuildRelationshipKey(body.RelationshipId);
            var model = await _stateStoreFactory.GetStore<RelationshipModel>(StateStoreDefinitions.Relationship)
                .GetAsync(relationshipKey, cancellationToken);

            if (model == null)
            {
                _logger.LogDebug("Relationship not found: {RelationshipId}", body.RelationshipId);
                return (StatusCodes.NotFound, null);
            }

            var response = MapToResponse(model);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting relationship: {RelationshipId}", body.RelationshipId);
            await EmitErrorAsync("GetRelationship", "post:/relationship/get", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Lists all relationships for a specific entity, with optional filtering.
    /// </summary>
    /// <param name="body">Request containing entity ID, type, and filter options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code and paginated list of relationships.</returns>
    public async Task<(StatusCodes, RelationshipListResponse?)> ListRelationshipsByEntityAsync(
        ListRelationshipsByEntityRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Listing relationships for entity: {EntityId} ({EntityType})",
                body.EntityId, body.EntityType);

            // Get all relationship IDs for this entity from the entity index
            var entityIndexKey = BuildEntityIndexKey(body.EntityType, body.EntityId);
            var relationshipIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Relationship)
                .GetAsync(entityIndexKey, cancellationToken) ?? new List<Guid>();

            if (relationshipIds.Count == 0)
            {
                return (StatusCodes.OK, CreateEmptyListResponse(body.Page, body.PageSize));
            }

            // Bulk load all relationships
            var keys = relationshipIds.Select(BuildRelationshipKey).ToList();
            var bulkResults = await _stateStoreFactory.GetStore<RelationshipModel>(StateStoreDefinitions.Relationship)
                .GetBulkAsync(keys, cancellationToken);

            var relationships = new List<RelationshipModel>();
            foreach (var (key, model) in bulkResults)
            {
                if (model == null)
                {
                    _logger.LogError("Relationship {Key} in index but failed to load - data inconsistency detected", key);
                    await EmitDataInconsistencyErrorAsync("ListRelationshipsByEntity", key, body.EntityId, body.EntityType);
                    continue;
                }
                relationships.Add(model);
            }

            // Apply filters
            var filtered = relationships.AsEnumerable();

            // Filter out ended relationships by default
            if (body.IncludeEnded != true)
            {
                filtered = filtered.Where(r => !r.EndedAt.HasValue);
            }

            // Filter by relationship type
            if (body.RelationshipTypeId.HasValue)
            {
                var typeId = body.RelationshipTypeId.Value;
                filtered = filtered.Where(r => r.RelationshipTypeId == typeId);
            }

            // Filter by other entity type
            if (body.OtherEntityType.HasValue)
            {
                var otherType = body.OtherEntityType.Value;
                var entityId = body.EntityId;
                filtered = filtered.Where(r =>
                    (r.Entity1Id == entityId && r.Entity2Type == otherType) ||
                    (r.Entity2Id == entityId && r.Entity1Type == otherType));
            }

            // Apply pagination
            var page = body.Page;
            var pageSize = body.PageSize;
            var totalCount = filtered.Count();
            var pagedResults = filtered
                .OrderByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var responses = pagedResults.Select(MapToResponse).ToList();

            return (StatusCodes.OK, new RelationshipListResponse
            {
                Relationships = responses,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                HasNextPage = (page * pageSize) < totalCount,
                HasPreviousPage = page > 1
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing relationships for entity: {EntityId}", body.EntityId);
            await EmitErrorAsync("ListRelationshipsByEntity", "post:/relationship/list-by-entity", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets all relationships between two specific entities.
    /// </summary>
    /// <param name="body">Request containing both entity IDs and types.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code and list of relationships between the entities.</returns>
    public async Task<(StatusCodes, RelationshipListResponse?)> GetRelationshipsBetweenAsync(
        GetRelationshipsBetweenRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting relationships between {Entity1Id} and {Entity2Id}",
                body.Entity1Id, body.Entity2Id);

            // Get relationships from entity1's index
            var entity1IndexKey = BuildEntityIndexKey(body.Entity1Type, body.Entity1Id);
            var entity1RelationshipIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Relationship)
                .GetAsync(entity1IndexKey, cancellationToken) ?? new List<Guid>();

            if (entity1RelationshipIds.Count == 0)
            {
                return (StatusCodes.OK, CreateEmptyListResponse(1, 20));
            }

            // Bulk load all relationships from entity1
            var keys = entity1RelationshipIds.Select(BuildRelationshipKey).ToList();
            var bulkResults = await _stateStoreFactory.GetStore<RelationshipModel>(StateStoreDefinitions.Relationship)
                .GetBulkAsync(keys, cancellationToken);

            var relationships = new List<RelationshipModel>();
            var entity2Id = body.Entity2Id;

            foreach (var (key, model) in bulkResults)
            {
                if (model == null)
                {
                    _logger.LogError("Relationship {Key} in index but failed to load - data inconsistency detected", key);
                    await EmitDataInconsistencyErrorAsync("GetRelationshipsBetween", key, body.Entity1Id, body.Entity1Type);
                    continue;
                }

                // Filter to only include relationships with entity2
                if (model.Entity1Id == entity2Id || model.Entity2Id == entity2Id)
                {
                    relationships.Add(model);
                }
            }

            // Apply filters
            var filtered = relationships.AsEnumerable();

            // Filter out ended relationships by default
            if (body.IncludeEnded != true)
            {
                filtered = filtered.Where(r => !r.EndedAt.HasValue);
            }

            // Filter by relationship type
            if (body.RelationshipTypeId.HasValue)
            {
                var typeId = body.RelationshipTypeId.Value;
                filtered = filtered.Where(r => r.RelationshipTypeId == typeId);
            }

            var results = filtered.OrderByDescending(r => r.CreatedAt).ToList();
            var responses = results.Select(MapToResponse).ToList();

            return (StatusCodes.OK, new RelationshipListResponse
            {
                Relationships = responses,
                TotalCount = responses.Count,
                Page = 1,
                PageSize = responses.Count,
                HasNextPage = false,
                HasPreviousPage = false
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting relationships between entities");
            await EmitErrorAsync("GetRelationshipsBetween", "post:/relationship/get-between", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Lists all relationships of a specific type with optional filtering.
    /// </summary>
    /// <param name="body">Request containing relationship type ID and filter options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code and paginated list of relationships.</returns>
    public async Task<(StatusCodes, RelationshipListResponse?)> ListRelationshipsByTypeAsync(
        ListRelationshipsByTypeRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Listing relationships by type: {RelationshipTypeId}", body.RelationshipTypeId);

            // Get all relationship IDs for this type from the type index
            var typeIndexKey = BuildTypeIndexKey(body.RelationshipTypeId);
            var relationshipIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Relationship)
                .GetAsync(typeIndexKey, cancellationToken) ?? new List<Guid>();

            if (relationshipIds.Count == 0)
            {
                return (StatusCodes.OK, CreateEmptyListResponse(body.Page, body.PageSize));
            }

            // Bulk load all relationships
            var keys = relationshipIds.Select(BuildRelationshipKey).ToList();
            var bulkResults = await _stateStoreFactory.GetStore<RelationshipModel>(StateStoreDefinitions.Relationship)
                .GetBulkAsync(keys, cancellationToken);

            var relationships = new List<RelationshipModel>();
            foreach (var (key, model) in bulkResults)
            {
                if (model == null)
                {
                    _logger.LogError("Relationship {Key} in index but failed to load - data inconsistency detected", key);
                    await EmitDataInconsistencyErrorAsync("ListRelationshipsByType", key, body.RelationshipTypeId);
                    continue;
                }
                relationships.Add(model);
            }

            // Apply filters
            var filtered = relationships.AsEnumerable();

            // Filter out ended relationships by default
            if (body.IncludeEnded != true)
            {
                filtered = filtered.Where(r => !r.EndedAt.HasValue);
            }

            // Filter by entity1 type
            if (body.Entity1Type.HasValue)
            {
                var entity1Type = body.Entity1Type.Value;
                filtered = filtered.Where(r => r.Entity1Type == entity1Type);
            }

            // Filter by entity2 type
            if (body.Entity2Type.HasValue)
            {
                var entity2Type = body.Entity2Type.Value;
                filtered = filtered.Where(r => r.Entity2Type == entity2Type);
            }

            // Apply pagination
            var page = body.Page;
            var pageSize = body.PageSize;
            var totalCount = filtered.Count();
            var pagedResults = filtered
                .OrderByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var responses = pagedResults.Select(MapToResponse).ToList();

            return (StatusCodes.OK, new RelationshipListResponse
            {
                Relationships = responses,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                HasNextPage = (page * pageSize) < totalCount,
                HasPreviousPage = page > 1
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing relationships by type: {TypeId}", body.RelationshipTypeId);
            await EmitErrorAsync("ListRelationshipsByType", "post:/relationship/list-by-type", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Relationship Write Operations

    /// <summary>
    /// Creates a new relationship between two entities.
    /// Validates composite uniqueness (entity1 + entity2 + type).
    /// </summary>
    /// <param name="body">Request containing relationship details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code and created relationship response.</returns>
    public async Task<(StatusCodes, RelationshipResponse?)> CreateRelationshipAsync(
        CreateRelationshipRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Creating relationship between {Entity1Id} ({Entity1Type}) and {Entity2Id} ({Entity2Type}) with type {TypeId}",
                body.Entity1Id, body.Entity1Type, body.Entity2Id, body.Entity2Type, body.RelationshipTypeId);

            // Validate that entities are different
            if (body.Entity1Id == body.Entity2Id && body.Entity1Type == body.Entity2Type)
            {
                _logger.LogDebug("Cannot create relationship between an entity and itself");
                return (StatusCodes.BadRequest, null);
            }

            // Check composite uniqueness - normalize entity order for consistent key
            var compositeKey = BuildCompositeKey(
                body.Entity1Id, body.Entity1Type,
                body.Entity2Id, body.Entity2Type,
                body.RelationshipTypeId);

            var existingId = await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Relationship)
                .GetAsync(compositeKey, cancellationToken);

            if (!string.IsNullOrEmpty(existingId))
            {
                _logger.LogDebug("Relationship already exists with composite key: {CompositeKey}", compositeKey);
                return (StatusCodes.Conflict, null);
            }

            var relationshipId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            var model = new RelationshipModel
            {
                RelationshipId = relationshipId,
                Entity1Id = body.Entity1Id,
                Entity1Type = body.Entity1Type,
                Entity2Id = body.Entity2Id,
                Entity2Type = body.Entity2Type,
                RelationshipTypeId = body.RelationshipTypeId,
                StartedAt = body.StartedAt,
                Metadata = body.Metadata,
                CreatedAt = now,
                UpdatedAt = now
            };

            // Save the relationship
            var relationshipKey = BuildRelationshipKey(relationshipId);
            await _stateStoreFactory.GetStore<RelationshipModel>(StateStoreDefinitions.Relationship)
                .SaveAsync(relationshipKey, model, cancellationToken: cancellationToken);

            // Update composite uniqueness index
            await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Relationship)
                .SaveAsync(compositeKey, relationshipId.ToString(), cancellationToken: cancellationToken);

            // Update entity indices (both entities)
            await AddToEntityIndexAsync(body.Entity1Type, body.Entity1Id, relationshipId, cancellationToken);
            await AddToEntityIndexAsync(body.Entity2Type, body.Entity2Id, relationshipId, cancellationToken);

            // Update type index
            await AddToTypeIndexAsync(body.RelationshipTypeId, relationshipId, cancellationToken);

            // Publish relationship created event
            await PublishRelationshipCreatedEventAsync(model, cancellationToken);

            _logger.LogInformation("Created relationship: {RelationshipId}", relationshipId);
            return (StatusCodes.OK, MapToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating relationship");
            await EmitErrorAsync("CreateRelationship", "post:/relationship/create", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Updates a relationship's metadata. Entity IDs and types cannot be changed.
    /// </summary>
    /// <param name="body">Request containing relationship ID and new metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code and updated relationship response.</returns>
    public async Task<(StatusCodes, RelationshipResponse?)> UpdateRelationshipAsync(
        UpdateRelationshipRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Updating relationship: {RelationshipId}", body.RelationshipId);

            var relationshipKey = BuildRelationshipKey(body.RelationshipId);
            var model = await _stateStoreFactory.GetStore<RelationshipModel>(StateStoreDefinitions.Relationship)
                .GetAsync(relationshipKey, cancellationToken);

            if (model == null)
            {
                _logger.LogDebug("Relationship not found: {RelationshipId}", body.RelationshipId);
                return (StatusCodes.NotFound, null);
            }

            // Check if relationship has ended
            if (model.EndedAt.HasValue)
            {
                _logger.LogDebug("Cannot update ended relationship: {RelationshipId}", body.RelationshipId);
                return (StatusCodes.Conflict, null);
            }

            // Track changed fields
            var changedFields = new List<string>();
            var needsSave = false;

            // Handle relationship type migration (used for type merge operations)
            if (body.RelationshipTypeId.HasValue && body.RelationshipTypeId.Value != model.RelationshipTypeId)
            {
                // Update type indexes: remove from old, add to new
                await RemoveFromTypeIndexAsync(model.RelationshipTypeId, model.RelationshipId, cancellationToken);
                await AddToTypeIndexAsync(body.RelationshipTypeId.Value, model.RelationshipId, cancellationToken);

                changedFields.Add("relationshipTypeId");
                model.RelationshipTypeId = body.RelationshipTypeId.Value;
                needsSave = true;
            }

            // Handle metadata updates
            if (body.Metadata != null)
            {
                changedFields.Add("metadata");
                model.Metadata = body.Metadata;
                needsSave = true;
            }

            if (needsSave)
            {
                model.UpdatedAt = DateTimeOffset.UtcNow;
                await _stateStoreFactory.GetStore<RelationshipModel>(StateStoreDefinitions.Relationship)
                    .SaveAsync(relationshipKey, model, cancellationToken: cancellationToken);

                // Publish relationship updated event
                await PublishRelationshipUpdatedEventAsync(model, changedFields, cancellationToken);

                _logger.LogInformation("Updated relationship: {RelationshipId}, ChangedFields: {ChangedFields}",
                    body.RelationshipId, string.Join(", ", changedFields));
            }
            else
            {
                _logger.LogDebug("No changes to update for relationship: {RelationshipId}", body.RelationshipId);
            }

            return (StatusCodes.OK, MapToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating relationship: {RelationshipId}", body.RelationshipId);
            await EmitErrorAsync("UpdateRelationship", "post:/relationship/update", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Ends a relationship by setting the endedAt timestamp.
    /// The composite uniqueness key is cleared to allow new relationships
    /// between the same entities with the same type.
    /// </summary>
    /// <param name="body">Request containing relationship ID and optional end timestamp.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code indicating success.</returns>
    public async Task<StatusCodes> EndRelationshipAsync(
        EndRelationshipRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Ending relationship: {RelationshipId}", body.RelationshipId);

            var relationshipKey = BuildRelationshipKey(body.RelationshipId);
            var model = await _stateStoreFactory.GetStore<RelationshipModel>(StateStoreDefinitions.Relationship)
                .GetAsync(relationshipKey, cancellationToken);

            if (model == null)
            {
                _logger.LogDebug("Relationship not found: {RelationshipId}", body.RelationshipId);
                return StatusCodes.NotFound;
            }

            // Check if already ended
            if (model.EndedAt.HasValue)
            {
                _logger.LogDebug("Relationship already ended: {RelationshipId}", body.RelationshipId);
                return StatusCodes.Conflict;
            }

            // Set ended timestamp (use current time if not specified or default)
            model.EndedAt = body.EndedAt == default ? DateTimeOffset.UtcNow : body.EndedAt;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            await _stateStoreFactory.GetStore<RelationshipModel>(StateStoreDefinitions.Relationship)
                .SaveAsync(relationshipKey, model, cancellationToken: cancellationToken);

            // Clear composite uniqueness key to allow new relationships
            var compositeKey = BuildCompositeKey(
                model.Entity1Id, model.Entity1Type,
                model.Entity2Id, model.Entity2Type,
                model.RelationshipTypeId);
            await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Relationship)
                .DeleteAsync(compositeKey, cancellationToken);

            // Publish relationship deleted/ended event
            await PublishRelationshipDeletedEventAsync(model, "Relationship ended", cancellationToken);

            _logger.LogInformation("Ended relationship: {RelationshipId}", body.RelationshipId);
            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ending relationship: {RelationshipId}", body.RelationshipId);
            await EmitErrorAsync("EndRelationship", "post:/relationship/end", ex);
            return StatusCodes.InternalServerError;
        }
    }

    #endregion

    // ========================================================================
    // RELATIONSHIP TYPE OPERATIONS
    // ========================================================================

    #region Relationship Type Read Operations

    /// <summary>
    /// Retrieves a relationship type by its unique identifier.
    /// </summary>
    public async Task<(StatusCodes, RelationshipTypeResponse?)> GetRelationshipTypeAsync(
        GetRelationshipTypeRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting relationship type by ID: {TypeId}", body.RelationshipTypeId);

            var typeKey = BuildRtTypeKey(body.RelationshipTypeId);
            var model = await _stateStoreFactory.GetStore<RelationshipTypeModel>(StateStoreDefinitions.RelationshipType)
                .GetAsync(typeKey, cancellationToken);

            if (model == null)
            {
                _logger.LogDebug("Relationship type not found: {TypeId}", body.RelationshipTypeId);
                return (StatusCodes.NotFound, null);
            }

            var response = MapToTypeResponse(model);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting relationship type: {TypeId}", body.RelationshipTypeId);
            await EmitErrorAsync("GetRelationshipType", "post:/relationship-type/get", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Retrieves a relationship type by its code (case-insensitive).
    /// </summary>
    public async Task<(StatusCodes, RelationshipTypeResponse?)> GetRelationshipTypeByCodeAsync(
        GetRelationshipTypeByCodeRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting relationship type by code: {Code}", body.Code);

            var codeIndexKey = BuildRtCodeIndexKey(body.Code.ToUpperInvariant());
            var typeIdStr = await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.RelationshipType)
                .GetAsync(codeIndexKey, cancellationToken);

            if (string.IsNullOrEmpty(typeIdStr) || !Guid.TryParse(typeIdStr, out var typeId))
            {
                _logger.LogDebug("Relationship type not found for code: {Code}", body.Code);
                return (StatusCodes.NotFound, null);
            }

            var typeKey = BuildRtTypeKey(typeId);
            var model = await _stateStoreFactory.GetStore<RelationshipTypeModel>(StateStoreDefinitions.RelationshipType)
                .GetAsync(typeKey, cancellationToken);

            if (model == null)
            {
                _logger.LogDebug("Relationship type not found: {TypeId}", typeId);
                return (StatusCodes.NotFound, null);
            }

            var response = MapToTypeResponse(model);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting relationship type by code: {Code}", body.Code);
            await EmitErrorAsync("GetRelationshipTypeByCode", "post:/relationship-type/get-by-code", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Lists all relationship types with optional filtering by category, roots-only, and deprecated inclusion.
    /// </summary>
    public async Task<(StatusCodes, RelationshipTypeListResponse?)> ListRelationshipTypesAsync(
        ListRelationshipTypesRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Listing relationship types");

            var allTypeIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.RelationshipType)
                .GetAsync(RT_ALL_TYPES_KEY, cancellationToken) ?? new List<Guid>();

            if (allTypeIds.Count == 0)
            {
                return (StatusCodes.OK, new RelationshipTypeListResponse
                {
                    Types = new List<RelationshipTypeResponse>(),
                    TotalCount = 0
                });
            }

            // Bulk load all types
            var keys = allTypeIds.Select(BuildRtTypeKey).ToList();
            var bulkResults = await _stateStoreFactory.GetStore<RelationshipTypeModel>(StateStoreDefinitions.RelationshipType)
                .GetBulkAsync(keys, cancellationToken);

            var types = new List<RelationshipTypeModel>();
            foreach (var (_, model) in bulkResults)
            {
                if (model != null) types.Add(model);
            }

            // Apply filters
            var filtered = types.AsEnumerable();

            // Filter out deprecated types by default
            if (body.IncludeDeprecated != true)
            {
                filtered = filtered.Where(t => !t.IsDeprecated);
            }

            if (!string.IsNullOrEmpty(body.Category))
            {
                filtered = filtered.Where(t =>
                    string.Equals(t.Category, body.Category, StringComparison.OrdinalIgnoreCase));
            }

            if (body.RootsOnly == true)
            {
                filtered = filtered.Where(t => !t.ParentTypeId.HasValue);
            }

            var typesList = filtered.ToList();
            var responses = typesList.Select(MapToTypeResponse).ToList();

            return (StatusCodes.OK, new RelationshipTypeListResponse
            {
                Types = responses,
                TotalCount = responses.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing relationship types");
            await EmitErrorAsync("ListRelationshipTypes", "post:/relationship-type/list", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets child relationship types for a given parent, optionally including recursive descendants.
    /// </summary>
    public async Task<(StatusCodes, RelationshipTypeListResponse?)> GetChildRelationshipTypesAsync(
        GetChildRelationshipTypesRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting child types for parent: {ParentId}", body.ParentTypeId);

            // Verify parent exists
            var parentKey = BuildRtTypeKey(body.ParentTypeId);
            var parent = await _stateStoreFactory.GetStore<RelationshipTypeModel>(StateStoreDefinitions.RelationshipType)
                .GetAsync(parentKey, cancellationToken);

            if (parent == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var childIds = await GetChildTypeIdsAsync(body.ParentTypeId, body.Recursive == true, cancellationToken);

            if (childIds.Count == 0)
            {
                return (StatusCodes.OK, new RelationshipTypeListResponse
                {
                    Types = new List<RelationshipTypeResponse>(),
                    TotalCount = 0
                });
            }

            // Bulk load children
            var keys = childIds.Select(BuildRtTypeKey).ToList();
            var bulkResults = await _stateStoreFactory.GetStore<RelationshipTypeModel>(StateStoreDefinitions.RelationshipType)
                .GetBulkAsync(keys, cancellationToken);

            var responses = new List<RelationshipTypeResponse>();
            foreach (var (_, model) in bulkResults)
            {
                if (model != null)
                {
                    responses.Add(MapToTypeResponse(model));
                }
            }

            return (StatusCodes.OK, new RelationshipTypeListResponse
            {
                Types = responses,
                TotalCount = responses.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting child types: {ParentId}", body.ParentTypeId);
            await EmitErrorAsync("GetChildRelationshipTypes", "post:/relationship-type/get-children", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Checks if a relationship type matches or descends from a given ancestor type.
    /// </summary>
    public async Task<(StatusCodes, MatchesHierarchyResponse?)> MatchesHierarchyAsync(
        MatchesHierarchyRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Checking hierarchy match: {TypeId} -> {AncestorId}",
                body.TypeId, body.AncestorTypeId);

            // If they're the same, it's a match with depth 0
            if (body.TypeId == body.AncestorTypeId)
            {
                return (StatusCodes.OK, new MatchesHierarchyResponse
                {
                    Matches = true,
                    Depth = 0
                });
            }

            // Get the type and traverse up the hierarchy
            var typeKey = BuildRtTypeKey(body.TypeId);
            var store = _stateStoreFactory.GetStore<RelationshipTypeModel>(StateStoreDefinitions.RelationshipType);
            var currentType = await store.GetAsync(typeKey, cancellationToken);

            if (currentType == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Verify ancestor exists
            var ancestorKey = BuildRtTypeKey(body.AncestorTypeId);
            var ancestor = await store.GetAsync(ancestorKey, cancellationToken);

            if (ancestor == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var depth = 0;
            var currentParentId = currentType.ParentTypeId;

            while (currentParentId.HasValue && depth < _configuration.MaxHierarchyDepth)
            {
                depth++;
                if (currentParentId.Value == body.AncestorTypeId)
                {
                    return (StatusCodes.OK, new MatchesHierarchyResponse
                    {
                        Matches = true,
                        Depth = depth
                    });
                }

                var parentKey = BuildRtTypeKey(currentParentId.Value);
                var parentType = await store.GetAsync(parentKey, cancellationToken);

                currentParentId = parentType?.ParentTypeId;
            }

            return (StatusCodes.OK, new MatchesHierarchyResponse
            {
                Matches = false,
                Depth = -1
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking hierarchy match");
            await EmitErrorAsync("MatchesHierarchy", "post:/relationship-type/matches-hierarchy", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets the full ancestry chain from a type up to the root.
    /// </summary>
    public async Task<(StatusCodes, RelationshipTypeListResponse?)> GetAncestorsAsync(
        GetAncestorsRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting ancestors for type: {TypeId}", body.TypeId);

            var typeKey = BuildRtTypeKey(body.TypeId);
            var store = _stateStoreFactory.GetStore<RelationshipTypeModel>(StateStoreDefinitions.RelationshipType);
            var currentType = await store.GetAsync(typeKey, cancellationToken);

            if (currentType == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var ancestors = new List<RelationshipTypeResponse>();
            var currentParentId = currentType.ParentTypeId;
            var iterations = 0;

            while (currentParentId.HasValue && iterations < _configuration.MaxHierarchyDepth)
            {
                iterations++;
                var parentKey = BuildRtTypeKey(currentParentId.Value);
                var parentType = await store.GetAsync(parentKey, cancellationToken);

                if (parentType == null) break;

                ancestors.Add(MapToTypeResponse(parentType));
                currentParentId = parentType.ParentTypeId;
            }

            return (StatusCodes.OK, new RelationshipTypeListResponse
            {
                Types = ancestors,
                TotalCount = ancestors.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting ancestors: {TypeId}", body.TypeId);
            await EmitErrorAsync("GetAncestors", "post:/relationship-type/get-ancestors", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Relationship Type Write Operations

    /// <summary>
    /// Creates a new relationship type with optional parent, inverse, and hierarchy positioning.
    /// </summary>
    public async Task<(StatusCodes, RelationshipTypeResponse?)> CreateRelationshipTypeAsync(
        CreateRelationshipTypeRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Creating relationship type: {Code}", body.Code);

            var code = body.Code.ToUpperInvariant();

            // Check if code already exists
            var codeIndexKey = BuildRtCodeIndexKey(code);
            var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.RelationshipType);
            var existingIdStr = await stringStore.GetAsync(codeIndexKey, cancellationToken);

            if (!string.IsNullOrEmpty(existingIdStr))
            {
                _logger.LogDebug("Relationship type with code already exists: {Code}", code);
                return (StatusCodes.Conflict, null);
            }

            // Validate parent if specified
            string? parentTypeCode = null;
            var depth = 0;
            var modelStore = _stateStoreFactory.GetStore<RelationshipTypeModel>(StateStoreDefinitions.RelationshipType);
            if (body.ParentTypeId.HasValue)
            {
                var parentKey = BuildRtTypeKey(body.ParentTypeId.Value);
                var parent = await modelStore.GetAsync(parentKey, cancellationToken);

                if (parent == null)
                {
                    _logger.LogDebug("Parent type not found: {ParentId}", body.ParentTypeId);
                    return (StatusCodes.BadRequest, null);
                }
                parentTypeCode = parent.Code;
                depth = parent.Depth + 1;
            }

            // Resolve inverse type if specified by code
            Guid? inverseTypeId = null;
            if (!string.IsNullOrEmpty(body.InverseTypeCode))
            {
                var inverseIndexKey = BuildRtCodeIndexKey(body.InverseTypeCode.ToUpperInvariant());
                var resolvedInverseIdStr = await stringStore.GetAsync(inverseIndexKey, cancellationToken);
                if (!string.IsNullOrEmpty(resolvedInverseIdStr) && Guid.TryParse(resolvedInverseIdStr, out var resolvedInverseId))
                {
                    inverseTypeId = resolvedInverseId;
                }
            }

            var typeId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            var model = new RelationshipTypeModel
            {
                RelationshipTypeId = typeId,
                Code = code,
                Name = body.Name,
                Description = body.Description,
                Category = body.Category,
                ParentTypeId = body.ParentTypeId,
                ParentTypeCode = parentTypeCode,
                InverseTypeId = inverseTypeId,
                InverseTypeCode = body.InverseTypeCode,
                IsBidirectional = body.IsBidirectional,
                Depth = depth,
                Metadata = body.Metadata,
                CreatedAt = now,
                UpdatedAt = now
            };

            // Save the model
            var typeKey = BuildRtTypeKey(typeId);
            await modelStore.SaveAsync(typeKey, model, cancellationToken: cancellationToken);

            // Update code index (store typeId as string since state store requires reference type)
            await stringStore.SaveAsync(codeIndexKey, typeId.ToString(), cancellationToken: cancellationToken);

            // Update parent's children index
            if (body.ParentTypeId.HasValue)
            {
                await AddToRtParentIndexAsync(body.ParentTypeId.Value, typeId, cancellationToken);
            }

            // Update all types list
            await AddToRtAllTypesListAsync(typeId, cancellationToken);

            await PublishRelationshipTypeCreatedEventAsync(model, cancellationToken);

            var response = MapToTypeResponse(model);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating relationship type: {Code}", body.Code);
            await EmitErrorAsync("CreateRelationshipType", "post:/relationship-type/create", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Updates a relationship type's fields. Code is immutable. Parent reassignment validates no cycle.
    /// </summary>
    public async Task<(StatusCodes, RelationshipTypeResponse?)> UpdateRelationshipTypeAsync(
        UpdateRelationshipTypeRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Updating relationship type: {TypeId}", body.RelationshipTypeId);

            var typeKey = BuildRtTypeKey(body.RelationshipTypeId);
            var modelStore = _stateStoreFactory.GetStore<RelationshipTypeModel>(StateStoreDefinitions.RelationshipType);
            var existing = await modelStore.GetAsync(typeKey, cancellationToken);

            if (existing == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var changedFields = new List<string>();

            if (!string.IsNullOrEmpty(body.Name) && body.Name != existing.Name)
            {
                existing.Name = body.Name;
                changedFields.Add("name");
            }

            if (body.Description != null && body.Description != existing.Description)
            {
                existing.Description = body.Description;
                changedFields.Add("description");
            }

            if (body.Category != null && body.Category != existing.Category)
            {
                existing.Category = body.Category;
                changedFields.Add("category");
            }

            if (body.IsBidirectional.HasValue && body.IsBidirectional.Value != existing.IsBidirectional)
            {
                existing.IsBidirectional = body.IsBidirectional.Value;
                changedFields.Add("isBidirectional");
            }

            if (body.Metadata != null)
            {
                existing.Metadata = body.Metadata;
                changedFields.Add("metadata");
            }

            // Handle parent type change
            if (body.ParentTypeId.HasValue)
            {
                var newParentId = body.ParentTypeId.Value;
                if (newParentId != existing.ParentTypeId)
                {
                    // Validate new parent exists
                    var parentKey = BuildRtTypeKey(newParentId);
                    var parent = await modelStore.GetAsync(parentKey, cancellationToken);

                    if (parent == null)
                    {
                        return (StatusCodes.BadRequest, null);
                    }

                    // Check for circular hierarchy - prevent making an ancestor into a descendant
                    if (await WouldCreateCycleAsync(existing.RelationshipTypeId, newParentId, cancellationToken))
                    {
                        _logger.LogDebug("Cannot set parent {NewParentId} for type {TypeId}: would create circular hierarchy",
                            newParentId, existing.RelationshipTypeId);
                        return (StatusCodes.BadRequest, null);
                    }

                    // Remove from old parent's index
                    if (existing.ParentTypeId.HasValue)
                    {
                        await RemoveFromRtParentIndexAsync(existing.ParentTypeId.Value, existing.RelationshipTypeId, cancellationToken);
                    }

                    // Add to new parent's index
                    await AddToRtParentIndexAsync(newParentId, existing.RelationshipTypeId, cancellationToken);

                    existing.ParentTypeId = newParentId;
                    existing.ParentTypeCode = parent.Code;
                    existing.Depth = parent.Depth + 1;
                    changedFields.Add("parentTypeId");
                }
            }

            // Handle inverse type code change
            if (body.InverseTypeCode != null && body.InverseTypeCode != existing.InverseTypeCode)
            {
                if (string.IsNullOrEmpty(body.InverseTypeCode))
                {
                    existing.InverseTypeId = null;
                    existing.InverseTypeCode = null;
                }
                else
                {
                    var inverseIndexKey = BuildRtCodeIndexKey(body.InverseTypeCode.ToUpperInvariant());
                    var inverseIdStr = await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.RelationshipType)
                        .GetAsync(inverseIndexKey, cancellationToken);
                    existing.InverseTypeId = Guid.TryParse(inverseIdStr, out var inverseId) ? inverseId : null;
                    existing.InverseTypeCode = body.InverseTypeCode;
                }
                changedFields.Add("inverseTypeCode");
            }

            if (changedFields.Count > 0)
            {
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                await modelStore.SaveAsync(typeKey, existing, cancellationToken: cancellationToken);
                await PublishRelationshipTypeUpdatedEventAsync(existing, changedFields, cancellationToken);
            }

            var response = MapToTypeResponse(existing);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating relationship type: {TypeId}", body.RelationshipTypeId);
            await EmitErrorAsync("UpdateRelationshipType", "post:/relationship-type/update", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Deletes a relationship type. Requires deprecation, zero relationship references, and no children.
    /// </summary>
    public async Task<StatusCodes> DeleteRelationshipTypeAsync(
        DeleteRelationshipTypeRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Deleting relationship type: {TypeId}", body.RelationshipTypeId);

            var typeKey = BuildRtTypeKey(body.RelationshipTypeId);
            var modelStore = _stateStoreFactory.GetStore<RelationshipTypeModel>(StateStoreDefinitions.RelationshipType);
            var existing = await modelStore.GetAsync(typeKey, cancellationToken);

            if (existing == null)
            {
                return StatusCodes.NotFound;
            }

            // Check deprecation requirement (per API schema: "Only deprecated types...can be deleted")
            if (!existing.IsDeprecated)
            {
                _logger.LogDebug("Cannot delete non-deprecated type: {TypeId}", body.RelationshipTypeId);
                return StatusCodes.Conflict;
            }

            // Check for relationship references internally (includes ended relationships)
            var (listStatus, listResponse) = await this.ListRelationshipsByTypeAsync(
                new ListRelationshipsByTypeRequest
                {
                    RelationshipTypeId = body.RelationshipTypeId,
                    IncludeEnded = true,
                    Page = 1,
                    PageSize = 1
                },
                cancellationToken);

            if (listStatus == StatusCodes.OK && listResponse?.Relationships != null && listResponse.Relationships.Count > 0)
            {
                _logger.LogDebug("Cannot delete type with existing relationships: {TypeId}", body.RelationshipTypeId);
                return StatusCodes.Conflict;
            }

            // Check if type has children
            var childIds = await GetChildTypeIdsAsync(body.RelationshipTypeId, false, cancellationToken);
            if (childIds.Count > 0)
            {
                _logger.LogDebug("Cannot delete type with children: {TypeId}", body.RelationshipTypeId);
                return StatusCodes.Conflict;
            }

            // Delete the type
            await modelStore.DeleteAsync(typeKey, cancellationToken);

            // Remove from code index
            var codeIndexKey = BuildRtCodeIndexKey(existing.Code);
            await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.RelationshipType)
                .DeleteAsync(codeIndexKey, cancellationToken);

            // Remove from parent's children index
            if (existing.ParentTypeId.HasValue)
            {
                await RemoveFromRtParentIndexAsync(existing.ParentTypeId.Value, existing.RelationshipTypeId, cancellationToken);
            }

            // Remove from all types list
            await RemoveFromRtAllTypesListAsync(existing.RelationshipTypeId, cancellationToken);

            await PublishRelationshipTypeDeletedEventAsync(existing, cancellationToken);

            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting relationship type: {TypeId}", body.RelationshipTypeId);
            await EmitErrorAsync("DeleteRelationshipType", "post:/relationship-type/delete", ex);
            return StatusCodes.InternalServerError;
        }
    }

    /// <summary>
    /// Bulk seeds relationship types with dependency-ordered creation.
    /// </summary>
    public async Task<(StatusCodes, SeedRelationshipTypesResponse?)> SeedRelationshipTypesAsync(
        SeedRelationshipTypesRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Seeding {Count} relationship types", body.Types.Count);

            var created = 0;
            var updated = 0;
            var skipped = 0;
            var errors = new List<string>();

            // First pass: create types without parent references
            var codeToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.RelationshipType);

            // Load existing code-to-id mappings
            foreach (var seedType in body.Types)
            {
                var code = seedType.Code.ToUpperInvariant();
                var codeIndexKey = BuildRtCodeIndexKey(code);
                var existingId = await stringStore.GetAsync(codeIndexKey, cancellationToken);

                if (!string.IsNullOrEmpty(existingId))
                {
                    codeToId[code] = existingId;
                }
            }

            // Process types in order (parent types first for depth calculation)
            var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = body.Types.ToList();
            var maxIterations = pending.Count * 2; // Prevent infinite loops
            var iteration = 0;

            while (pending.Count > 0 && iteration < maxIterations)
            {
                iteration++;
                var toProcess = pending.Where(t =>
                    string.IsNullOrEmpty(t.ParentTypeCode) ||
                    processed.Contains(t.ParentTypeCode.ToUpperInvariant())).ToList();

                if (toProcess.Count == 0)
                {
                    // Remaining items have unresolvable parents
                    foreach (var unresolved in pending)
                    {
                        errors.Add($"Unresolved parent type '{unresolved.ParentTypeCode}' for '{unresolved.Code}'");
                    }
                    break;
                }

                foreach (var seedType in toProcess)
                {
                    pending.Remove(seedType);
                    var code = seedType.Code.ToUpperInvariant();

                    try
                    {
                        if (codeToId.TryGetValue(code, out var existingId))
                        {
                            // Type exists
                            if (body.UpdateExisting == true)
                            {
                                // Update existing type
                                var updateRequest = new UpdateRelationshipTypeRequest
                                {
                                    RelationshipTypeId = Guid.Parse(existingId),
                                    Name = seedType.Name,
                                    Description = seedType.Description,
                                    Category = seedType.Category,
                                    InverseTypeCode = seedType.InverseTypeCode,
                                    IsBidirectional = seedType.IsBidirectional
                                };

                                if (!string.IsNullOrEmpty(seedType.ParentTypeCode) &&
                                    codeToId.TryGetValue(seedType.ParentTypeCode.ToUpperInvariant(), out var parentId))
                                {
                                    updateRequest.ParentTypeId = Guid.Parse(parentId);
                                }

                                var (status, _) = await UpdateRelationshipTypeAsync(updateRequest, cancellationToken);
                                if (status == StatusCodes.OK)
                                {
                                    updated++;
                                }
                                else
                                {
                                    errors.Add($"Failed to update '{code}': {status}");
                                }
                            }
                            else
                            {
                                skipped++;
                            }
                        }
                        else
                        {
                            // Create new type
                            var createRequest = new CreateRelationshipTypeRequest
                            {
                                Code = code,
                                Name = seedType.Name,
                                Description = seedType.Description,
                                Category = seedType.Category,
                                InverseTypeCode = seedType.InverseTypeCode,
                                IsBidirectional = seedType.IsBidirectional,
                                Metadata = seedType.Metadata
                            };

                            if (!string.IsNullOrEmpty(seedType.ParentTypeCode) &&
                                codeToId.TryGetValue(seedType.ParentTypeCode.ToUpperInvariant(), out var parentId))
                            {
                                createRequest.ParentTypeId = Guid.Parse(parentId);
                            }

                            var (status, response) = await CreateRelationshipTypeAsync(createRequest, cancellationToken);
                            if (status == StatusCodes.OK && response != null)
                            {
                                codeToId[code] = response.RelationshipTypeId.ToString();
                                created++;
                            }
                            else
                            {
                                errors.Add($"Failed to create '{code}': {status}");
                            }
                        }

                        processed.Add(code);
                    }
                    catch (Exception typeEx)
                    {
                        errors.Add($"Error processing '{code}': {typeEx.Message}");
                    }
                }
            }

            return (StatusCodes.OK, new SeedRelationshipTypesResponse
            {
                Created = created,
                Updated = updated,
                Skipped = skipped,
                Errors = errors
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding relationship types");
            await EmitErrorAsync("SeedRelationshipTypes", "post:/relationship-type/seed", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Relationship Type Deprecation Operations

    /// <summary>
    /// Marks a relationship type as deprecated.
    /// </summary>
    public async Task<(StatusCodes, RelationshipTypeResponse?)> DeprecateRelationshipTypeAsync(
        DeprecateRelationshipTypeRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Deprecating relationship type: {TypeId}", body.RelationshipTypeId);

            var typeKey = BuildRtTypeKey(body.RelationshipTypeId);
            var store = _stateStoreFactory.GetStore<RelationshipTypeModel>(StateStoreDefinitions.RelationshipType);
            var model = await store.GetAsync(typeKey, cancellationToken);

            if (model == null)
            {
                _logger.LogDebug("Relationship type not found for deprecation: {TypeId}", body.RelationshipTypeId);
                return (StatusCodes.NotFound, null);
            }

            if (model.IsDeprecated)
            {
                _logger.LogDebug("Relationship type already deprecated: {TypeId}", body.RelationshipTypeId);
                return (StatusCodes.Conflict, null);
            }

            model.IsDeprecated = true;
            model.DeprecatedAt = DateTimeOffset.UtcNow;
            model.DeprecationReason = body.Reason;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            await store.SaveAsync(typeKey, model, cancellationToken: cancellationToken);

            // Publish updated event with deprecation fields
            await PublishRelationshipTypeUpdatedEventAsync(model, new[] { "isDeprecated", "deprecatedAt", "deprecationReason" }, cancellationToken);

            _logger.LogInformation("Deprecated relationship type: {TypeId}", body.RelationshipTypeId);
            return (StatusCodes.OK, MapToTypeResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deprecating relationship type: {TypeId}", body.RelationshipTypeId);
            await EmitErrorAsync("DeprecateRelationshipType", "post:/relationship-type/deprecate", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Removes deprecation from a relationship type.
    /// </summary>
    public async Task<(StatusCodes, RelationshipTypeResponse?)> UndeprecateRelationshipTypeAsync(
        UndeprecateRelationshipTypeRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Undeprecating relationship type: {TypeId}", body.RelationshipTypeId);

            var typeKey = BuildRtTypeKey(body.RelationshipTypeId);
            var store = _stateStoreFactory.GetStore<RelationshipTypeModel>(StateStoreDefinitions.RelationshipType);
            var model = await store.GetAsync(typeKey, cancellationToken);

            if (model == null)
            {
                _logger.LogDebug("Relationship type not found for undeprecation: {TypeId}", body.RelationshipTypeId);
                return (StatusCodes.NotFound, null);
            }

            if (!model.IsDeprecated)
            {
                _logger.LogDebug("Relationship type not deprecated: {TypeId}", body.RelationshipTypeId);
                return (StatusCodes.Conflict, null);
            }

            model.IsDeprecated = false;
            model.DeprecatedAt = null;
            model.DeprecationReason = null;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            await store.SaveAsync(typeKey, model, cancellationToken: cancellationToken);

            // Publish updated event with deprecation fields cleared
            await PublishRelationshipTypeUpdatedEventAsync(model, new[] { "isDeprecated", "deprecatedAt", "deprecationReason" }, cancellationToken);

            _logger.LogInformation("Undeprecated relationship type: {TypeId}", body.RelationshipTypeId);
            return (StatusCodes.OK, MapToTypeResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error undeprecating relationship type: {TypeId}", body.RelationshipTypeId);
            await EmitErrorAsync("UndeprecateRelationshipType", "post:/relationship-type/undeprecate", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Merges a deprecated relationship type into a target type, migrating all relationships.
    /// Calls internal relationship list/update methods directly (approach A per issue #331).
    /// </summary>
    public async Task<(StatusCodes, MergeRelationshipTypeResponse?)> MergeRelationshipTypeAsync(
        MergeRelationshipTypeRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Merging relationship type {SourceId} into {TargetId}",
                body.SourceTypeId, body.TargetTypeId);

            // Verify source exists and is deprecated
            var sourceKey = BuildRtTypeKey(body.SourceTypeId);
            var store = _stateStoreFactory.GetStore<RelationshipTypeModel>(StateStoreDefinitions.RelationshipType);
            var sourceModel = await store.GetAsync(sourceKey, cancellationToken);

            if (sourceModel == null)
            {
                _logger.LogDebug("Source relationship type not found: {TypeId}", body.SourceTypeId);
                return (StatusCodes.NotFound, null);
            }

            if (!sourceModel.IsDeprecated)
            {
                _logger.LogDebug("Source relationship type must be deprecated before merging: {TypeId}", body.SourceTypeId);
                return (StatusCodes.BadRequest, null);
            }

            // Verify target exists
            var targetKey = BuildRtTypeKey(body.TargetTypeId);
            var targetModel = await store.GetAsync(targetKey, cancellationToken);

            if (targetModel == null)
            {
                _logger.LogDebug("Target relationship type not found: {TypeId}", body.TargetTypeId);
                return (StatusCodes.NotFound, null);
            }

            // Migrate all relationships from source type to target type using internal methods
            var migratedCount = 0;
            var failedCount = 0;
            var migrationErrors = new List<MigrationError>();
            var maxErrorsToTrack = _configuration.MaxMigrationErrorsToTrack;
            var page = 1;
            var pageSize = _configuration.SeedPageSize;
            var hasMorePages = true;

            while (hasMorePages)
            {
                try
                {
                    // Call internal ListRelationshipsByType directly (no HTTP round-trip)
                    var (listStatus, relationshipsResponse) = await this.ListRelationshipsByTypeAsync(
                        new ListRelationshipsByTypeRequest
                        {
                            RelationshipTypeId = body.SourceTypeId,
                            Page = page,
                            PageSize = pageSize
                        },
                        cancellationToken);

                    if (listStatus != StatusCodes.OK ||
                        relationshipsResponse?.Relationships == null ||
                        relationshipsResponse.Relationships.Count == 0)
                    {
                        hasMorePages = false;
                        continue;
                    }

                    // Migrate each relationship to the target type
                    foreach (var relationship in relationshipsResponse.Relationships)
                    {
                        try
                        {
                            // Call internal UpdateRelationship directly (no HTTP round-trip)
                            await this.UpdateRelationshipAsync(
                                new UpdateRelationshipRequest
                                {
                                    RelationshipId = relationship.RelationshipId,
                                    RelationshipTypeId = body.TargetTypeId
                                },
                                cancellationToken);
                            migratedCount++;
                        }
                        catch (Exception relEx)
                        {
                            _logger.LogError(relEx, "Failed to migrate relationship {RelationshipId} from type {SourceId} to {TargetId}",
                                relationship.RelationshipId, body.SourceTypeId, body.TargetTypeId);
                            failedCount++;

                            // Track error details (limited to avoid unbounded memory usage)
                            if (migrationErrors.Count < maxErrorsToTrack)
                            {
                                migrationErrors.Add(new MigrationError
                                {
                                    RelationshipId = relationship.RelationshipId,
                                    Error = relEx.Message
                                });
                            }
                        }
                    }

                    hasMorePages = relationshipsResponse.HasNextPage;
                    page++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching relationships for type migration at page {Page}", page);
                    hasMorePages = false;
                }
            }

            if (failedCount > 0)
            {
                _logger.LogError("Relationship type merge completed with {FailedCount} failed relationship migrations", failedCount);

                // Publish error event for monitoring/alerting
                await _messageBus.TryPublishErrorAsync(
                    "relationship",
                    "MergeRelationshipType",
                    "PartialMigrationFailure",
                    $"Failed to migrate {failedCount} relationships from type {body.SourceTypeId} to {body.TargetTypeId}",
                    dependency: null,
                    endpoint: "post:/relationship-type/merge",
                    details: new { SourceTypeId = body.SourceTypeId, TargetTypeId = body.TargetTypeId, FailedCount = failedCount, MigratedCount = migratedCount },
                    stack: null,
                    cancellationToken: cancellationToken);
            }

            _logger.LogInformation("Merged relationship type {SourceId} into {TargetId}, migrated {MigratedCount} relationships (failed: {FailedCount})",
                body.SourceTypeId, body.TargetTypeId, migratedCount, failedCount);

            // Handle delete-after-merge if requested and all migrations succeeded
            var sourceDeleted = false;
            if (body.DeleteAfterMerge)
            {
                if (failedCount > 0)
                {
                    _logger.LogWarning("Skipping delete-after-merge for relationship type {SourceId}: {FailedCount} migrations failed",
                        body.SourceTypeId, failedCount);
                }
                else
                {
                    var deleteResult = await DeleteRelationshipTypeAsync(
                        new DeleteRelationshipTypeRequest { RelationshipTypeId = body.SourceTypeId },
                        cancellationToken);

                    if (deleteResult == StatusCodes.OK)
                    {
                        sourceDeleted = true;
                    }
                    else
                    {
                        _logger.LogWarning("Delete-after-merge failed for relationship type {SourceId} with status {Status}",
                            body.SourceTypeId, deleteResult);
                    }
                }
            }

            return (StatusCodes.OK, new MergeRelationshipTypeResponse
            {
                SourceTypeId = body.SourceTypeId,
                TargetTypeId = body.TargetTypeId,
                RelationshipsMigrated = migratedCount,
                RelationshipsFailed = failedCount,
                MigrationErrors = migrationErrors,
                SourceDeleted = sourceDeleted
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error merging relationship type {SourceId} into {TargetId}",
                body.SourceTypeId, body.TargetTypeId);
            await EmitErrorAsync("MergeRelationshipType", "post:/relationship-type/merge", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    // ========================================================================
    // HELPER METHODS
    // ========================================================================

    #region Relationship Instance Helpers

    private static string BuildRelationshipKey(Guid relationshipId) =>
        $"{RELATIONSHIP_KEY_PREFIX}{relationshipId}";

    private static string BuildEntityIndexKey(EntityType entityType, Guid entityId) =>
        $"{ENTITY_INDEX_PREFIX}{entityType}:{entityId}";

    private static string BuildTypeIndexKey(Guid relationshipTypeId) =>
        $"{TYPE_INDEX_PREFIX}{relationshipTypeId}";

    /// <summary>
    /// Builds the composite uniqueness key for a relationship.
    /// Normalizes entity order to ensure consistent key regardless of entity order in request.
    /// </summary>
    private static string BuildCompositeKey(
        Guid entity1Id, EntityType entity1Type,
        Guid entity2Id, EntityType entity2Type,
        Guid relationshipTypeId)
    {
        // Normalize entity order for consistent composite key
        var key1 = $"{entity1Type}:{entity1Id}";
        var key2 = $"{entity2Type}:{entity2Id}";

        // Sort to ensure consistent ordering
        if (string.Compare(key1, key2, StringComparison.Ordinal) > 0)
        {
            (key1, key2) = (key2, key1);
        }

        return $"{COMPOSITE_KEY_PREFIX}{key1}:{key2}:{relationshipTypeId}";
    }

    private async Task AddToEntityIndexAsync(
        EntityType entityType, Guid entityId, Guid relationshipId,
        CancellationToken cancellationToken)
    {
        var indexKey = BuildEntityIndexKey(entityType, entityId);
        var store = _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Relationship);
        var relationshipIds = await store.GetAsync(indexKey, cancellationToken) ?? new List<Guid>();

        if (!relationshipIds.Contains(relationshipId))
        {
            relationshipIds.Add(relationshipId);
            await store.SaveAsync(indexKey, relationshipIds, cancellationToken: cancellationToken);
        }
    }

    private async Task AddToTypeIndexAsync(
        Guid relationshipTypeId, Guid relationshipId,
        CancellationToken cancellationToken)
    {
        var indexKey = BuildTypeIndexKey(relationshipTypeId);
        var store = _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Relationship);
        var relationshipIds = await store.GetAsync(indexKey, cancellationToken) ?? new List<Guid>();

        if (!relationshipIds.Contains(relationshipId))
        {
            relationshipIds.Add(relationshipId);
            await store.SaveAsync(indexKey, relationshipIds, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromTypeIndexAsync(
        Guid relationshipTypeId, Guid relationshipId,
        CancellationToken cancellationToken)
    {
        var indexKey = BuildTypeIndexKey(relationshipTypeId);
        var store = _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Relationship);
        var relationshipIds = await store.GetAsync(indexKey, cancellationToken) ?? new List<Guid>();

        if (relationshipIds.Remove(relationshipId))
        {
            await store.SaveAsync(indexKey, relationshipIds, cancellationToken: cancellationToken);
        }
    }

    private static RelationshipListResponse CreateEmptyListResponse(int page, int pageSize)
    {
        return new RelationshipListResponse
        {
            Relationships = new List<RelationshipResponse>(),
            TotalCount = 0,
            Page = page,
            PageSize = pageSize,
            HasNextPage = false,
            HasPreviousPage = false
        };
    }

    private static RelationshipResponse MapToResponse(RelationshipModel model)
    {
        return new RelationshipResponse
        {
            RelationshipId = model.RelationshipId,
            Entity1Id = model.Entity1Id,
            Entity1Type = model.Entity1Type,
            Entity2Id = model.Entity2Id,
            Entity2Type = model.Entity2Type,
            RelationshipTypeId = model.RelationshipTypeId,
            StartedAt = model.StartedAt,
            EndedAt = model.EndedAt,
            Metadata = model.Metadata,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }

    #endregion

    #region Relationship Type Helpers

    private static string BuildRtTypeKey(Guid typeId) => $"{RT_TYPE_KEY_PREFIX}{typeId}";
    private static string BuildRtCodeIndexKey(string code) => $"{RT_CODE_INDEX_PREFIX}{code}";
    private static string BuildRtParentIndexKey(Guid parentId) => $"{RT_PARENT_INDEX_PREFIX}{parentId}";

    private async Task<List<Guid>> GetChildTypeIdsAsync(Guid parentId, bool recursive, CancellationToken cancellationToken, int currentDepth = 0)
    {
        var parentIndexKey = BuildRtParentIndexKey(parentId);
        var directChildren = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.RelationshipType)
            .GetAsync(parentIndexKey, cancellationToken) ?? new List<Guid>();

        if (!recursive || directChildren.Count == 0 || currentDepth >= _configuration.MaxHierarchyDepth)
        {
            return directChildren;
        }

        var allChildren = new List<Guid>(directChildren);
        foreach (var childId in directChildren)
        {
            var grandchildren = await GetChildTypeIdsAsync(childId, true, cancellationToken, currentDepth + 1);
            allChildren.AddRange(grandchildren);
        }

        return allChildren;
    }

    private async Task AddToRtParentIndexAsync(Guid parentId, Guid childId, CancellationToken cancellationToken)
    {
        var parentIndexKey = BuildRtParentIndexKey(parentId);
        var store = _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.RelationshipType);
        var children = await store.GetAsync(parentIndexKey, cancellationToken) ?? new List<Guid>();

        if (!children.Contains(childId))
        {
            children.Add(childId);
            await store.SaveAsync(parentIndexKey, children, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromRtParentIndexAsync(Guid parentId, Guid childId, CancellationToken cancellationToken)
    {
        var parentIndexKey = BuildRtParentIndexKey(parentId);
        var store = _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.RelationshipType);
        var children = await store.GetAsync(parentIndexKey, cancellationToken) ?? new List<Guid>();

        if (children.Remove(childId))
        {
            await store.SaveAsync(parentIndexKey, children, cancellationToken: cancellationToken);
        }
    }

    private async Task AddToRtAllTypesListAsync(Guid typeId, CancellationToken cancellationToken)
    {
        var store = _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.RelationshipType);
        var allTypes = await store.GetAsync(RT_ALL_TYPES_KEY, cancellationToken) ?? new List<Guid>();

        if (!allTypes.Contains(typeId))
        {
            allTypes.Add(typeId);
            await store.SaveAsync(RT_ALL_TYPES_KEY, allTypes, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromRtAllTypesListAsync(Guid typeId, CancellationToken cancellationToken)
    {
        var store = _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.RelationshipType);
        var allTypes = await store.GetAsync(RT_ALL_TYPES_KEY, cancellationToken) ?? new List<Guid>();

        if (allTypes.Remove(typeId))
        {
            await store.SaveAsync(RT_ALL_TYPES_KEY, allTypes, cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Checks if setting proposedParentId as the parent of typeId would create a circular hierarchy.
    /// A cycle exists if typeId is an ancestor of proposedParentId (i.e., walking up from proposedParentId
    /// eventually reaches typeId).
    /// </summary>
    private async Task<bool> WouldCreateCycleAsync(Guid typeId, Guid proposedParentId, CancellationToken cancellationToken)
    {
        // Self-reference is an obvious cycle
        if (typeId == proposedParentId)
        {
            return true;
        }

        // Walk up the ancestor chain from proposedParentId
        var store = _stateStoreFactory.GetStore<RelationshipTypeModel>(StateStoreDefinitions.RelationshipType);
        var currentParentId = proposedParentId;
        var iterations = 0;

        while (iterations < _configuration.MaxHierarchyDepth)
        {
            iterations++;
            var parentKey = BuildRtTypeKey(currentParentId);
            var parentType = await store.GetAsync(parentKey, cancellationToken);

            if (parentType == null || !parentType.ParentTypeId.HasValue)
            {
                return false;
            }

            if (parentType.ParentTypeId.Value == typeId)
            {
                return true;
            }

            currentParentId = parentType.ParentTypeId.Value;
        }

        // Exceeded max depth - treat as potential cycle for safety
        return true;
    }

    private static RelationshipTypeResponse MapToTypeResponse(RelationshipTypeModel model)
    {
        return new RelationshipTypeResponse
        {
            RelationshipTypeId = model.RelationshipTypeId,
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            Category = model.Category,
            ParentTypeId = model.ParentTypeId,
            ParentTypeCode = model.ParentTypeCode,
            InverseTypeId = model.InverseTypeId,
            InverseTypeCode = model.InverseTypeCode,
            IsBidirectional = model.IsBidirectional,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            Depth = model.Depth,
            Metadata = model.Metadata,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }

    #endregion

    // ========================================================================
    // EVENT PUBLISHING
    // ========================================================================

    #region Relationship Instance Events

    private async Task PublishRelationshipCreatedEventAsync(RelationshipModel model, CancellationToken cancellationToken)
    {
        var eventModel = new RelationshipCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RelationshipId = model.RelationshipId,
            Entity1Id = model.Entity1Id,
            Entity1Type = model.Entity1Type,
            Entity2Id = model.Entity2Id,
            Entity2Type = model.Entity2Type,
            RelationshipTypeId = model.RelationshipTypeId,
            StartedAt = model.StartedAt,
            Metadata = model.Metadata,
            CreatedAt = model.CreatedAt
        };

        await _messageBus.TryPublishAsync("relationship.created", eventModel);
        _logger.LogDebug("Published relationship.created event for {RelationshipId}", model.RelationshipId);
    }

    private async Task PublishRelationshipUpdatedEventAsync(
        RelationshipModel model,
        IEnumerable<string> changedFields,
        CancellationToken cancellationToken)
    {
        var eventModel = new RelationshipUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RelationshipId = model.RelationshipId,
            Entity1Id = model.Entity1Id,
            Entity1Type = model.Entity1Type,
            Entity2Id = model.Entity2Id,
            Entity2Type = model.Entity2Type,
            RelationshipTypeId = model.RelationshipTypeId,
            StartedAt = model.StartedAt,
            EndedAt = model.EndedAt,
            Metadata = model.Metadata,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt ?? DateTimeOffset.UtcNow,
            ChangedFields = changedFields.ToList()
        };

        await _messageBus.TryPublishAsync("relationship.updated", eventModel);
        _logger.LogDebug("Published relationship.updated event for {RelationshipId}", model.RelationshipId);
    }

    private async Task PublishRelationshipDeletedEventAsync(RelationshipModel model, string? deletedReason, CancellationToken cancellationToken)
    {
        var eventModel = new RelationshipDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RelationshipId = model.RelationshipId,
            Entity1Id = model.Entity1Id,
            Entity1Type = model.Entity1Type,
            Entity2Id = model.Entity2Id,
            Entity2Type = model.Entity2Type,
            RelationshipTypeId = model.RelationshipTypeId,
            StartedAt = model.StartedAt,
            EndedAt = model.EndedAt ?? DateTimeOffset.UtcNow,
            Metadata = model.Metadata,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt ?? DateTimeOffset.UtcNow,
            DeletedReason = deletedReason
        };

        await _messageBus.TryPublishAsync("relationship.deleted", eventModel);
        _logger.LogDebug("Published relationship.deleted event for {RelationshipId}", model.RelationshipId);
    }

    #endregion

    #region Relationship Type Events

    private async Task PublishRelationshipTypeCreatedEventAsync(RelationshipTypeModel model, CancellationToken cancellationToken)
    {
        try
        {
            var eventModel = new RelationshipTypeCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                RelationshipTypeId = model.RelationshipTypeId,
                Code = model.Code,
                Name = model.Name,
                Category = model.Category,
                ParentTypeId = model.ParentTypeId
            };

            await _messageBus.TryPublishAsync("relationship-type.created", eventModel);
            _logger.LogDebug("Published relationship-type.created event for {TypeId}", model.RelationshipTypeId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish relationship-type.created event for {TypeId}", model.RelationshipTypeId);
        }
    }

    private async Task PublishRelationshipTypeUpdatedEventAsync(RelationshipTypeModel model, IEnumerable<string> changedFields, CancellationToken cancellationToken)
    {
        try
        {
            var eventModel = new RelationshipTypeUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                RelationshipTypeId = model.RelationshipTypeId,
                Code = model.Code,
                Name = model.Name,
                Description = model.Description,
                Category = model.Category,
                ParentTypeId = model.ParentTypeId,
                InverseTypeId = model.InverseTypeId,
                IsBidirectional = model.IsBidirectional,
                Depth = model.Depth,
                IsDeprecated = model.IsDeprecated,
                DeprecatedAt = model.DeprecatedAt,
                DeprecationReason = model.DeprecationReason,
                CreatedAt = model.CreatedAt,
                UpdatedAt = model.UpdatedAt,
                ChangedFields = changedFields.ToList()
            };

            await _messageBus.TryPublishAsync("relationship-type.updated", eventModel);
            _logger.LogDebug("Published relationship-type.updated event for {TypeId} with changed fields: {ChangedFields}",
                model.RelationshipTypeId, string.Join(", ", changedFields));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish relationship-type.updated event for {TypeId}", model.RelationshipTypeId);
        }
    }

    private async Task PublishRelationshipTypeDeletedEventAsync(RelationshipTypeModel model, CancellationToken cancellationToken)
    {
        try
        {
            var eventModel = new RelationshipTypeDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                RelationshipTypeId = model.RelationshipTypeId,
                Code = model.Code
            };

            await _messageBus.TryPublishAsync("relationship-type.deleted", eventModel);
            _logger.LogDebug("Published relationship-type.deleted event for {TypeId}", model.RelationshipTypeId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish relationship-type.deleted event for {TypeId}", model.RelationshipTypeId);
        }
    }

    #endregion

    // ========================================================================
    // ERROR HANDLING & PERMISSIONS
    // ========================================================================

    #region Error Handling

    private async Task EmitErrorAsync(string operation, string endpoint, Exception ex)
    {
        await _messageBus.TryPublishErrorAsync(
            "relationship",
            operation,
            "unexpected_exception",
            ex.Message,
            dependency: null,
            endpoint: endpoint,
            details: null,
            stack: ex.StackTrace);
    }

    private async Task EmitDataInconsistencyErrorAsync(string operation, string orphanedKey, Guid indexEntityId, EntityType indexEntityType)
    {
        await _messageBus.TryPublishErrorAsync(
            "relationship",
            operation,
            "data_inconsistency",
            $"Relationship key {orphanedKey} exists in entity index but relationship record not found",
            dependency: "state-store",
            endpoint: null,
            details: $"entityId={indexEntityId}, entityType={indexEntityType}",
            stack: null);
    }

    private async Task EmitDataInconsistencyErrorAsync(string operation, string orphanedKey, Guid relationshipTypeId)
    {
        await _messageBus.TryPublishErrorAsync(
            "relationship",
            operation,
            "data_inconsistency",
            $"Relationship key {orphanedKey} exists in type index but relationship record not found",
            dependency: "state-store",
            endpoint: null,
            details: $"relationshipTypeId={relationshipTypeId}",
            stack: null);
    }

    #endregion

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permission service on startup.
    /// Uses generated permission data from x-permissions sections in the OpenAPI schema.
    /// </summary>
    public async Task RegisterServicePermissionsAsync(string appId)
    {
        _logger.LogDebug("Registering Relationship service permissions...");
        await RelationshipPermissionRegistration.RegisterViaEventAsync(_messageBus, appId, _logger);
    }

    #endregion
}

/// <summary>
/// Internal model for storing relationships in state store.
/// </summary>
internal class RelationshipModel
{
    /// <summary>Unique identifier for the relationship.</summary>
    public Guid RelationshipId { get; set; }
    /// <summary>ID of the first entity in the relationship.</summary>
    public Guid Entity1Id { get; set; }
    /// <summary>Type of the first entity.</summary>
    public EntityType Entity1Type { get; set; }
    /// <summary>ID of the second entity in the relationship.</summary>
    public Guid Entity2Id { get; set; }
    /// <summary>Type of the second entity.</summary>
    public EntityType Entity2Type { get; set; }
    /// <summary>ID of the relationship type definition.</summary>
    public Guid RelationshipTypeId { get; set; }
    /// <summary>In-game timestamp when the relationship started.</summary>
    public DateTimeOffset StartedAt { get; set; }
    /// <summary>In-game timestamp when the relationship ended (null if active).</summary>
    public DateTimeOffset? EndedAt { get; set; }
    /// <summary>Type-specific metadata for the relationship.</summary>
    public object? Metadata { get; set; }
    /// <summary>Timestamp when the record was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Timestamp when the record was last updated.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Internal model for storing relationship types in state store.
/// </summary>
internal class RelationshipTypeModel
{
    /// <summary>Unique identifier for the relationship type.</summary>
    public Guid RelationshipTypeId { get; set; }
    /// <summary>Machine-readable code (uppercase normalized).</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>Human-readable display name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Detailed description of the relationship type.</summary>
    public string? Description { get; set; }
    /// <summary>Category grouping (e.g., FAMILY, SOCIAL).</summary>
    public string? Category { get; set; }
    /// <summary>ID of the parent type in the hierarchy.</summary>
    public Guid? ParentTypeId { get; set; }
    /// <summary>Code of the parent type.</summary>
    public string? ParentTypeCode { get; set; }
    /// <summary>ID of the inverse relationship type.</summary>
    public Guid? InverseTypeId { get; set; }
    /// <summary>Code of the inverse relationship type.</summary>
    public string? InverseTypeCode { get; set; }
    /// <summary>Whether the relationship applies equally in both directions.</summary>
    public bool IsBidirectional { get; set; }
    /// <summary>Hierarchy depth level from root.</summary>
    public int Depth { get; set; }
    /// <summary>Additional custom metadata.</summary>
    public object? Metadata { get; set; }
    /// <summary>Whether this type is deprecated.</summary>
    public bool IsDeprecated { get; set; }
    /// <summary>Timestamp when deprecated.</summary>
    public DateTimeOffset? DeprecatedAt { get; set; }
    /// <summary>Reason for deprecation.</summary>
    public string? DeprecationReason { get; set; }
    /// <summary>Timestamp when the record was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Timestamp when the record was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
