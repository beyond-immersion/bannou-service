using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.Location.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.History;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Location;

// =============================================================================
// LocationService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by LocationService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (LocationService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in ILocationService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (LocationService.Helpers.cs):
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
/// Private and internal helper methods for LocationService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class LocationService
{
    // Move private/internal helper methods here from LocationService.cs
    #region Private Helpers

    private async Task<List<LocationModel>> LoadLocationsByIdsAsync(List<Guid> locationIds, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.location", "LocationService.LoadLocationsByIds");

        if (locationIds.Count == 0)
        {
            return new List<LocationModel>();
        }

        var keysList = locationIds.Select(BuildLocationKey).ToList();

        // Try cache first with bulk get
        var cacheStore = _locationCacheStore;
        var cachedResult = await cacheStore.GetBulkAsync(keysList, cancellationToken);

        // Find cache misses
        var missedKeys = keysList.Where(k => !cachedResult.ContainsKey(k)).ToList();

        // Fetch misses from persistent store
        Dictionary<string, LocationModel> fetchedFromStore = new();
        if (missedKeys.Count > 0)
        {
            var persistentStore = _locationStore;
            var bulkResult = await persistentStore.GetBulkAsync(missedKeys, cancellationToken);
            fetchedFromStore = new Dictionary<string, LocationModel>(bulkResult);

            // Populate cache for fetched items
            foreach (var kvp in fetchedFromStore)
            {
                await cacheStore.SaveAsync(kvp.Key, kvp.Value,
                    new StateOptions { Ttl = _configuration.CacheTtlSeconds }, cancellationToken);
            }
        }

        // Combine cached and fetched, preserving order from input list
        var results = new List<LocationModel>(locationIds.Count);
        foreach (var id in locationIds)
        {
            var key = BuildLocationKey(id);
            if (cachedResult.TryGetValue(key, out var cachedModel))
            {
                results.Add(cachedModel);
            }
            else if (fetchedFromStore.TryGetValue(key, out var fetchedModel))
            {
                results.Add(fetchedModel);
            }
        }
        return results;
    }

    private async Task AddToRealmIndexAsync(Guid realmId, Guid locationId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.location", "LocationService.AddToRealmIndex");

        var realmIndexKey = BuildRealmIndexKey(realmId);

        // Acquire distributed lock for index modification (per IMPLEMENTATION TENETS)
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.LocationLock,
            realmIndexKey,
            Guid.NewGuid().ToString(),
            _configuration.IndexLockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            throw new InvalidOperationException(
                $"Could not acquire distributed lock for realm index {realmIndexKey}");
        }

        var locationIds = await _guidListStore.GetAsync(realmIndexKey, cancellationToken) ?? new List<Guid>();
        if (!locationIds.Contains(locationId))
        {
            locationIds.Add(locationId);
            await _guidListStore.SaveAsync(realmIndexKey, locationIds, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromRealmIndexAsync(Guid realmId, Guid locationId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.location", "LocationService.RemoveFromRealmIndex");

        var realmIndexKey = BuildRealmIndexKey(realmId);

        // Acquire distributed lock for index modification (per IMPLEMENTATION TENETS)
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.LocationLock,
            realmIndexKey,
            Guid.NewGuid().ToString(),
            _configuration.IndexLockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            throw new InvalidOperationException(
                $"Could not acquire distributed lock for realm index {realmIndexKey}");
        }

        var locationIds = await _guidListStore.GetAsync(realmIndexKey, cancellationToken) ?? new List<Guid>();
        if (locationIds.Remove(locationId))
        {
            await _guidListStore.SaveAsync(realmIndexKey, locationIds, cancellationToken: cancellationToken);
        }
    }

    private async Task AddToParentIndexAsync(Guid realmId, Guid parentId, Guid locationId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.location", "LocationService.AddToParentIndex");

        var parentIndexKey = BuildParentIndexKey(realmId, parentId);

        // Acquire distributed lock for index modification (per IMPLEMENTATION TENETS)
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.LocationLock,
            parentIndexKey,
            Guid.NewGuid().ToString(),
            _configuration.IndexLockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            throw new InvalidOperationException(
                $"Could not acquire distributed lock for parent index {parentIndexKey}");
        }

        var childIds = await _guidListStore.GetAsync(parentIndexKey, cancellationToken) ?? new List<Guid>();
        if (!childIds.Contains(locationId))
        {
            childIds.Add(locationId);
            await _guidListStore.SaveAsync(parentIndexKey, childIds, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromParentIndexAsync(Guid realmId, Guid parentId, Guid locationId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.location", "LocationService.RemoveFromParentIndex");

        var parentIndexKey = BuildParentIndexKey(realmId, parentId);

        // Acquire distributed lock for index modification (per IMPLEMENTATION TENETS)
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.LocationLock,
            parentIndexKey,
            Guid.NewGuid().ToString(),
            _configuration.IndexLockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            throw new InvalidOperationException(
                $"Could not acquire distributed lock for parent index {parentIndexKey}");
        }

        var store = _guidListStore;
        var childIds = await store.GetAsync(parentIndexKey, cancellationToken) ?? new List<Guid>();
        if (childIds.Remove(locationId))
        {
            if (childIds.Count == 0)
            {
                // Clean up empty index key to prevent accumulation
                await store.DeleteAsync(parentIndexKey, cancellationToken);
            }
            else
            {
                await store.SaveAsync(parentIndexKey, childIds, cancellationToken: cancellationToken);
            }
        }
    }

    private async Task AddToRootLocationsAsync(Guid realmId, Guid locationId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.location", "LocationService.AddToRootLocations");

        var rootKey = BuildRootLocationsKey(realmId);

        // Acquire distributed lock for index modification (per IMPLEMENTATION TENETS)
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.LocationLock,
            rootKey,
            Guid.NewGuid().ToString(),
            _configuration.IndexLockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            throw new InvalidOperationException(
                $"Could not acquire distributed lock for root locations {rootKey}");
        }

        var rootIds = await _guidListStore.GetAsync(rootKey, cancellationToken) ?? new List<Guid>();
        if (!rootIds.Contains(locationId))
        {
            rootIds.Add(locationId);
            await _guidListStore.SaveAsync(rootKey, rootIds, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromRootLocationsAsync(Guid realmId, Guid locationId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.location", "LocationService.RemoveFromRootLocations");

        var rootKey = BuildRootLocationsKey(realmId);

        // Acquire distributed lock for index modification (per IMPLEMENTATION TENETS)
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.LocationLock,
            rootKey,
            Guid.NewGuid().ToString(),
            _configuration.IndexLockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            throw new InvalidOperationException(
                $"Could not acquire distributed lock for root locations {rootKey}");
        }

        var rootIds = await _guidListStore.GetAsync(rootKey, cancellationToken) ?? new List<Guid>();
        if (rootIds.Remove(locationId))
        {
            await _guidListStore.SaveAsync(rootKey, rootIds, cancellationToken: cancellationToken);
        }
    }

    private async Task CollectDescendantsAsync(Guid parentId, Guid realmId, List<LocationModel> descendants, int currentDepth, int maxDepth, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.location", "LocationService.CollectDescendants");

        if (currentDepth >= maxDepth)
        {
            return;
        }

        var parentIndexKey = BuildParentIndexKey(realmId, parentId);
        var childIds = await _guidListStore.GetAsync(parentIndexKey, cancellationToken) ?? new List<Guid>();

        foreach (var childId in childIds)
        {
            var childKey = BuildLocationKey(childId);
            var childModel = await _locationStore.GetAsync(childKey, cancellationToken);

            if (childModel != null)
            {
                descendants.Add(childModel);
                await CollectDescendantsAsync(childId, realmId, descendants, currentDepth + 1, maxDepth, cancellationToken);
            }
        }
    }

    private async Task<bool> IsDescendantOfAsync(Guid potentialDescendantId, Guid potentialAncestorId, Guid realmId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.location", "LocationService.IsDescendantOf");

        var descendants = new List<LocationModel>();
        await CollectDescendantsAsync(potentialAncestorId, realmId, descendants, 0, _configuration.MaxDescendantDepth, cancellationToken);
        return descendants.Any(d => d.LocationId == potentialDescendantId);
    }

    private async Task UpdateDescendantDepthsAsync(Guid parentId, Guid realmId, int depthChange, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.location", "LocationService.UpdateDescendantDepths");

        var descendants = new List<LocationModel>();
        await CollectDescendantsAsync(parentId, realmId, descendants, 0, _configuration.MaxDescendantDepth, cancellationToken);

        if (descendants.Count == 0)
        {
            return;
        }

        // Update depths in memory and prepare bulk operations
        var now = DateTimeOffset.UtcNow;
        var itemsToSave = new List<KeyValuePair<string, LocationModel>>();
        var cacheKeysToInvalidate = new List<string>();

        foreach (var descendant in descendants)
        {
            descendant.Depth += depthChange;
            descendant.UpdatedAt = now;
            var key = BuildLocationKey(descendant.LocationId);
            itemsToSave.Add(new KeyValuePair<string, LocationModel>(key, descendant));
            cacheKeysToInvalidate.Add(key);
        }

        // Bulk save all descendants to state store (single database call)
        var locationStore = _locationStore;
        await locationStore.SaveBulkAsync(itemsToSave, cancellationToken: cancellationToken);

        // Bulk invalidate cache for all updated descendants (single cache call)
        var cacheStore = _locationCacheStore;
        await cacheStore.DeleteBulkAsync(cacheKeysToInvalidate, cancellationToken);

        _logger.LogDebug("Updated depths for {Count} descendants of location {ParentId}", descendants.Count, parentId);
    }

    private LocationResponse MapToResponse(LocationModel model)
    {
        return new LocationResponse
        {
            LocationId = model.LocationId,
            RealmId = model.RealmId,
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            LocationType = model.LocationType,
            ParentLocationId = model.ParentLocationId,
            Depth = model.Depth,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            Bounds = model.Bounds,
            BoundsPrecision = model.BoundsPrecision,
            CoordinateMode = model.CoordinateMode,
            LocalOrigin = model.LocalOrigin,
            Metadata = model.Metadata,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }

    private async Task PublishLocationCreatedEventAsync(LocationModel model, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.location", "LocationService.PublishLocationCreatedEvent");

        var eventData = new LocationCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            LocationId = model.LocationId,
            RealmId = model.RealmId,
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            LocationType = model.LocationType,
            ParentLocationId = model.ParentLocationId,
            Depth = model.Depth,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            Bounds = model.Bounds,
            BoundsPrecision = model.BoundsPrecision,
            CoordinateMode = model.CoordinateMode,
            LocalOrigin = model.LocalOrigin,
            Metadata = model.Metadata,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };

        await _messageBus.PublishLocationCreatedAsync(eventData, cancellationToken);
    }

    private async Task PublishLocationUpdatedEventAsync(LocationModel model, IList<string> changedFields, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.location", "LocationService.PublishLocationUpdatedEvent");

        var eventData = new LocationUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            LocationId = model.LocationId,
            RealmId = model.RealmId,
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            LocationType = model.LocationType,
            ParentLocationId = model.ParentLocationId,
            Depth = model.Depth,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            Bounds = model.Bounds,
            BoundsPrecision = model.BoundsPrecision,
            CoordinateMode = model.CoordinateMode,
            LocalOrigin = model.LocalOrigin,
            Metadata = model.Metadata,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt,
            ChangedFields = changedFields.ToList()
        };

        await _messageBus.PublishLocationUpdatedAsync(eventData, cancellationToken);

        // Publish client event to sessions observing this location
        await _entitySessionRegistry.PublishToEntitySessionsAsync(
            "location", model.LocationId,
            new LocationUpdatedClientEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                LocationId = model.LocationId,
                RealmId = model.RealmId,
                Name = model.Name,
                Description = model.Description,
                LocationType = model.LocationType,
                IsDeprecated = model.IsDeprecated,
                ChangedFields = changedFields.ToList()
            }, cancellationToken);
    }

    private async Task PublishPresenceClientEventAsync(
        Guid locationId, Guid realmId, string entityType, Guid entityId,
        PresenceChangeType changeType, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.location", "LocationService.PublishPresenceClientEvent");

        await _entitySessionRegistry.PublishToEntitySessionsAsync(
            "location", locationId,
            new LocationPresenceChangedClientEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                LocationId = locationId,
                RealmId = realmId,
                EntityType = entityType,
                EntityId = entityId,
                ChangeType = changeType
            }, cancellationToken);
    }

    private async Task PublishLocationDeletedEventAsync(LocationModel model, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.location", "LocationService.PublishLocationDeletedEvent");

        var eventData = new LocationDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            LocationId = model.LocationId,
            RealmId = model.RealmId,
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            LocationType = model.LocationType,
            ParentLocationId = model.ParentLocationId,
            Depth = model.Depth,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            Bounds = model.Bounds,
            BoundsPrecision = model.BoundsPrecision,
            CoordinateMode = model.CoordinateMode,
            LocalOrigin = model.LocalOrigin,
            Metadata = model.Metadata
        };

        await _messageBus.PublishLocationDeletedAsync(eventData, cancellationToken);
    }

    private async Task PublishEntityArrivedEventAsync(
        string entityType, Guid entityId, Guid locationId, Guid realmId, string? reportedBy, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.location", "LocationService.PublishEntityArrivedEvent");

        var eventData = new LocationEntityArrivedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            EntityType = entityType,
            EntityId = entityId,
            LocationId = locationId,
            RealmId = realmId,
            ReportedBy = reportedBy
        };

        await _messageBus.PublishLocationEntityArrivedAsync(eventData, cancellationToken);

        // Publish client event to sessions observing this location
        await PublishPresenceClientEventAsync(locationId, realmId, entityType, entityId, PresenceChangeType.Arrived, cancellationToken);
    }

    private async Task PublishEntityDepartedEventAsync(
        string entityType, Guid entityId, Guid locationId, Guid realmId, string? reportedBy, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.location", "LocationService.PublishEntityDepartedEvent");

        var eventData = new LocationEntityDepartedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            EntityType = entityType,
            EntityId = entityId,
            LocationId = locationId,
            RealmId = realmId,
            ReportedBy = reportedBy
        };

        await _messageBus.PublishLocationEntityDepartedAsync(eventData, cancellationToken);

        // Publish client event to sessions observing this location
        await PublishPresenceClientEventAsync(locationId, realmId, entityType, entityId, PresenceChangeType.Departed, cancellationToken);
    }

    #endregion
    #region Cache Methods

    /// <summary>
    /// Get location with Redis cache read-through. Falls back to MySQL persistent store on cache miss.
    /// </summary>
    private async Task<LocationModel?> GetLocationWithCacheAsync(string locationKey, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.location", "LocationService.GetLocationWithCache");

        var cacheStore = _locationCacheStore;

        // Try cache first
        var cached = await cacheStore.GetAsync(locationKey, ct);
        if (cached is not null) return cached;

        // Fallback to persistent store
        var store = _locationStore;
        var model = await store.GetAsync(locationKey, ct);
        if (model is null) return null;

        // Populate cache
        await cacheStore.SaveAsync(locationKey, model,
            new StateOptions { Ttl = _configuration.CacheTtlSeconds }, ct);
        return model;
    }

    /// <summary>
    /// Populate location cache after a write operation.
    /// </summary>
    private async Task PopulateLocationCacheAsync(string locationKey, LocationModel model, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.location", "LocationService.PopulateLocationCache");

        var cacheStore = _locationCacheStore;
        await cacheStore.SaveAsync(locationKey, model,
            new StateOptions { Ttl = _configuration.CacheTtlSeconds }, ct);
    }

    /// <summary>
    /// Invalidate location cache after a write/delete operation.
    /// </summary>
    private async Task InvalidateLocationCacheAsync(string locationKey, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.location", "LocationService.InvalidateLocationCache");

        var cacheStore = _locationCacheStore;
        await cacheStore.DeleteAsync(locationKey, ct);
    }

    #endregion
}
