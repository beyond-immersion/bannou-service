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
/// bidirectional support, and soft-delete capability.
/// </summary>
[BannouService("relationship", typeof(IRelationshipService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFoundation)]
public partial class RelationshipService : IRelationshipService
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<RelationshipService> _logger;
    private readonly RelationshipServiceConfiguration _configuration;

    private const string RELATIONSHIP_KEY_PREFIX = "rel:";
    private const string ENTITY_INDEX_PREFIX = "entity-idx:";
    private const string TYPE_INDEX_PREFIX = "type-idx:";
    private const string COMPOSITE_KEY_PREFIX = "composite:";

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

    #region Read Operations

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

    #region Write Operations

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

    #region Helper Methods

    /// <summary>
    /// Builds the state store key for a relationship record.
    /// </summary>
    private static string BuildRelationshipKey(Guid relationshipId) =>
        $"{RELATIONSHIP_KEY_PREFIX}{relationshipId}";

    /// <summary>
    /// Builds the state store key for an entity's relationship index.
    /// </summary>
    private static string BuildEntityIndexKey(EntityType entityType, Guid entityId) =>
        $"{ENTITY_INDEX_PREFIX}{entityType}:{entityId}";

    /// <summary>
    /// Builds the state store key for a relationship type's index.
    /// </summary>
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

    /// <summary>
    /// Adds a relationship ID to an entity's index.
    /// </summary>
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

    /// <summary>
    /// Adds a relationship ID to a type's index.
    /// </summary>
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

    /// <summary>
    /// Removes a relationship ID from a type's index.
    /// </summary>
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

    /// <summary>
    /// Creates an empty paginated list response.
    /// </summary>
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

    /// <summary>
    /// Maps an internal relationship model to the API response model.
    /// </summary>
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

    /// <summary>
    /// Publishes a relationship created event. TryPublishAsync handles buffering, retry, and error logging.
    /// </summary>
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

    /// <summary>
    /// Publishes a relationship updated event. TryPublishAsync handles buffering, retry, and error logging.
    /// </summary>
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

    /// <summary>
    /// Publishes a relationship deleted event. TryPublishAsync handles buffering, retry, and error logging.
    /// </summary>
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

    /// <summary>
    /// Emits an error event for monitoring and alerting.
    /// </summary>
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

    /// <summary>
    /// Emits a data inconsistency error event when an index contains an ID for a non-existent relationship.
    /// Used by list operations to signal orphaned index entries for monitoring and cleanup.
    /// </summary>
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

    /// <summary>
    /// Emits a data inconsistency error event when a type index contains an ID for a non-existent relationship.
    /// Used by ListRelationshipsByType to signal orphaned index entries for monitoring and cleanup.
    /// </summary>
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
    /// <summary>
    /// Unique identifier for the relationship.
    /// </summary>
    public Guid RelationshipId { get; set; }

    /// <summary>
    /// ID of the first entity in the relationship.
    /// </summary>
    public Guid Entity1Id { get; set; }

    /// <summary>
    /// Type of the first entity (CHARACTER, NPC, ITEM, etc.).
    /// </summary>
    public EntityType Entity1Type { get; set; }

    /// <summary>
    /// ID of the second entity in the relationship.
    /// </summary>
    public Guid Entity2Id { get; set; }

    /// <summary>
    /// Type of the second entity.
    /// </summary>
    public EntityType Entity2Type { get; set; }

    /// <summary>
    /// ID of the relationship type (from RelationshipType service).
    /// </summary>
    public Guid RelationshipTypeId { get; set; }

    /// <summary>
    /// In-game timestamp when the relationship started.
    /// </summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// In-game timestamp when the relationship ended (null if active).
    /// </summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>
    /// Type-specific metadata for the relationship.
    /// </summary>
    public object? Metadata { get; set; }

    /// <summary>
    /// Timestamp when the record was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Timestamp when the record was last updated.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
