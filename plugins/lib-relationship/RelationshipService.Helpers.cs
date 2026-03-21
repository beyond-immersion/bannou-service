using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Relationship.Caching;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Relationship;

// =============================================================================
// RelationshipService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by RelationshipService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (RelationshipService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IRelationshipService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (RelationshipService.Helpers.cs):
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
/// Private and internal helper methods for RelationshipService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class RelationshipService
{
    /// <summary>
    /// Publishes a single summary event for a completed merge operation.
    /// </summary>
    private async Task PublishMergedEventAsync(
        Guid sourceTypeId, Guid targetTypeId,
        int migratedCount, int failedCount, bool sourceDeleted,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.relationship", "RelationshipService.PublishMergedEventAsync");
        var eventModel = new RelationshipTypeMergedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SourceTypeId = sourceTypeId,
            TargetTypeId = targetTypeId,
            MigratedCount = migratedCount,
            FailedCount = failedCount,
            SourceDeleted = sourceDeleted
        };

        await _messageBus.PublishRelationshipTypeMergedAsync(eventModel);
        _logger.LogDebug("Published relationship.type.merged event for source {SourceId} into target {TargetId}", sourceTypeId, targetTypeId);
    }
    // ========================================================================
    // HELPER METHODS
    // ========================================================================

    #region Relationship Instance Helpers

    internal static string BuildRelationshipKey(Guid relationshipId) =>
        $"{RELATIONSHIP_KEY_PREFIX}{relationshipId}";

    internal static string BuildEntityIndexKey(EntityType entityType, Guid entityId) =>
        $"{ENTITY_INDEX_PREFIX}{entityType}:{entityId}";

    internal static string BuildTypeIndexKey(Guid relationshipTypeId) =>
        $"{TYPE_INDEX_PREFIX}{relationshipTypeId}";

    /// <summary>
    /// Builds the composite uniqueness key for a relationship.
    /// Normalizes entity order to ensure consistent key regardless of entity order in request.
    /// </summary>
    internal static string BuildCompositeKey(
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
        using var activity = _telemetryProvider.StartActivity("bannou.relationship", "RelationshipService.AddToEntityIndexAsync");
        var indexKey = BuildEntityIndexKey(entityType, entityId);

        // Acquire distributed lock for entity index modification (per IMPLEMENTATION TENETS)
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.RelationshipLock,
            indexKey,
            Guid.NewGuid().ToString(),
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogError("Could not acquire lock for entity index {IndexKey}, index update failed", indexKey);
            throw new InvalidOperationException($"Could not acquire distributed lock for entity index '{indexKey}'");
        }

        var store = _relationshipIndexStore;
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
        using var activity = _telemetryProvider.StartActivity("bannou.relationship", "RelationshipService.AddToTypeIndexAsync");
        var indexKey = BuildTypeIndexKey(relationshipTypeId);

        // Acquire distributed lock for type index modification (per IMPLEMENTATION TENETS)
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.RelationshipLock,
            indexKey,
            Guid.NewGuid().ToString(),
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogError("Could not acquire lock for type index {IndexKey}, index update failed", indexKey);
            throw new InvalidOperationException($"Could not acquire distributed lock for type index '{indexKey}'");
        }

        var store = _relationshipIndexStore;
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
        using var activity = _telemetryProvider.StartActivity("bannou.relationship", "RelationshipService.RemoveFromTypeIndexAsync");
        var indexKey = BuildTypeIndexKey(relationshipTypeId);

        // Acquire distributed lock for type index modification (per IMPLEMENTATION TENETS)
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.RelationshipLock,
            indexKey,
            Guid.NewGuid().ToString(),
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogError("Could not acquire lock for type index {IndexKey}, index update failed", indexKey);
            throw new InvalidOperationException($"Could not acquire distributed lock for type index '{indexKey}'");
        }

        var store = _relationshipIndexStore;
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

    internal static string BuildRtTypeKey(Guid typeId) => $"{RT_TYPE_KEY_PREFIX}{typeId}";
    internal static string BuildRtCodeIndexKey(string code) => $"{RT_CODE_INDEX_PREFIX}{code}";
    internal static string BuildRtParentIndexKey(Guid parentId) => $"{RT_PARENT_INDEX_PREFIX}{parentId}";

    private async Task<List<Guid>> GetChildTypeIdsAsync(Guid parentId, bool recursive, CancellationToken cancellationToken, int currentDepth = 0)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.relationship", "RelationshipService.GetChildTypeIdsAsync");
        var parentIndexKey = BuildRtParentIndexKey(parentId);
        var directChildren = await _typeIndexStore
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
        using var activity = _telemetryProvider.StartActivity("bannou.relationship", "RelationshipService.AddToRtParentIndexAsync");
        var parentIndexKey = BuildRtParentIndexKey(parentId);

        // Acquire distributed lock for parent index modification (per IMPLEMENTATION TENETS)
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.RelationshipLock,
            parentIndexKey,
            Guid.NewGuid().ToString(),
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogError("Could not acquire lock for parent index {ParentIndexKey}, index update failed", parentIndexKey);
            throw new InvalidOperationException($"Could not acquire distributed lock for parent index '{parentIndexKey}'");
        }

        var store = _typeIndexStore;
        var children = await store.GetAsync(parentIndexKey, cancellationToken) ?? new List<Guid>();

        if (!children.Contains(childId))
        {
            children.Add(childId);
            await store.SaveAsync(parentIndexKey, children, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromRtParentIndexAsync(Guid parentId, Guid childId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.relationship", "RelationshipService.RemoveFromRtParentIndexAsync");
        var parentIndexKey = BuildRtParentIndexKey(parentId);

        // Acquire distributed lock for parent index modification (per IMPLEMENTATION TENETS)
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.RelationshipLock,
            parentIndexKey,
            Guid.NewGuid().ToString(),
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogError("Could not acquire lock for parent index {ParentIndexKey}, index update failed", parentIndexKey);
            throw new InvalidOperationException($"Could not acquire distributed lock for parent index '{parentIndexKey}'");
        }

        var store = _typeIndexStore;
        var children = await store.GetAsync(parentIndexKey, cancellationToken) ?? new List<Guid>();

        if (children.Remove(childId))
        {
            await store.SaveAsync(parentIndexKey, children, cancellationToken: cancellationToken);
        }
    }

    private async Task AddToRtAllTypesListAsync(Guid typeId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.relationship", "RelationshipService.AddToRtAllTypesListAsync");
        // Acquire distributed lock for all-types list modification (per IMPLEMENTATION TENETS)
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.RelationshipLock,
            RT_ALL_TYPES_KEY,
            Guid.NewGuid().ToString(),
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogError("Could not acquire lock for all-types list, index update failed");
            throw new InvalidOperationException("Could not acquire distributed lock for all-types list");
        }

        var store = _typeIndexStore;
        var allTypes = await store.GetAsync(RT_ALL_TYPES_KEY, cancellationToken) ?? new List<Guid>();

        if (!allTypes.Contains(typeId))
        {
            allTypes.Add(typeId);
            await store.SaveAsync(RT_ALL_TYPES_KEY, allTypes, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromRtAllTypesListAsync(Guid typeId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.relationship", "RelationshipService.RemoveFromRtAllTypesListAsync");
        // Acquire distributed lock for all-types list modification (per IMPLEMENTATION TENETS)
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.RelationshipLock,
            RT_ALL_TYPES_KEY,
            Guid.NewGuid().ToString(),
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogError("Could not acquire lock for all-types list, index update failed");
            throw new InvalidOperationException("Could not acquire distributed lock for all-types list");
        }

        var store = _typeIndexStore;
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
        using var activity = _telemetryProvider.StartActivity("bannou.relationship", "RelationshipService.WouldCreateCycleAsync");
        // Self-reference is an obvious cycle
        if (typeId == proposedParentId)
        {
            return true;
        }

        // Walk up the ancestor chain from proposedParentId
        var store = _typeModelStore;
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
        using var activity = _telemetryProvider.StartActivity("bannou.relationship", "RelationshipService.PublishRelationshipCreatedEventAsync");
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

        await _messageBus.PublishRelationshipCreatedAsync(eventModel);
        _logger.LogDebug("Published relationship.created event for {RelationshipId}", model.RelationshipId);
    }

    private async Task PublishRelationshipUpdatedEventAsync(
        RelationshipModel model,
        IEnumerable<string> changedFields,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.relationship", "RelationshipService.PublishRelationshipUpdatedEventAsync");
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

        await _messageBus.PublishRelationshipUpdatedAsync(eventModel);
        _logger.LogDebug("Published relationship.updated event for {RelationshipId}", model.RelationshipId);
    }

    private async Task PublishRelationshipDeletedEventAsync(RelationshipModel model, string? deletedReason, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.relationship", "RelationshipService.PublishRelationshipDeletedEventAsync");
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

        await _messageBus.PublishRelationshipDeletedAsync(eventModel);
        _logger.LogDebug("Published relationship.deleted event for {RelationshipId}", model.RelationshipId);
    }

    #endregion

    #region Relationship Type Events

    private async Task PublishRelationshipTypeCreatedEventAsync(RelationshipTypeModel model, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.relationship", "RelationshipService.PublishRelationshipTypeCreatedEventAsync");
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

        await _messageBus.PublishRelationshipTypeCreatedAsync(eventModel);
        _logger.LogDebug("Published relationship.type.created event for {TypeId}", model.RelationshipTypeId);
    }

    private async Task PublishRelationshipTypeUpdatedEventAsync(RelationshipTypeModel model, IEnumerable<string> changedFields, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.relationship", "RelationshipService.PublishRelationshipTypeUpdatedEventAsync");
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

        await _messageBus.PublishRelationshipTypeUpdatedAsync(eventModel);
        _logger.LogDebug("Published relationship.type.updated event for {TypeId} with changed fields: {ChangedFields}",
            model.RelationshipTypeId, string.Join(", ", changedFields));
    }

    private async Task PublishRelationshipTypeDeletedEventAsync(RelationshipTypeModel model, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.relationship", "RelationshipService.PublishRelationshipTypeDeletedEventAsync");
        var eventModel = new RelationshipTypeDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RelationshipTypeId = model.RelationshipTypeId,
            Code = model.Code
        };

        await _messageBus.PublishRelationshipTypeDeletedAsync(eventModel);
        _logger.LogDebug("Published relationship.type.deleted event for {TypeId}", model.RelationshipTypeId);
    }

    #endregion

    // ========================================================================
    // ERROR HANDLING & PERMISSIONS
    // ========================================================================

    #region Error Handling

    private async Task EmitDataInconsistencyErrorAsync(string operation, string orphanedKey, Guid indexEntityId, EntityType indexEntityType)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.relationship", "RelationshipService.EmitDataInconsistencyErrorAsync");
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
        using var activity = _telemetryProvider.StartActivity("bannou.relationship", "RelationshipService.EmitDataInconsistencyErrorAsync");
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
}
