using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Location;

/// <summary>
/// Implementation of the Location service.
/// Manages location definitions - places within realms with hierarchical organization.
/// Locations are partitioned by realm for scalability.
/// </summary>
[BannouService("location", typeof(ILocationService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFoundation)]
public partial class LocationService : ILocationService
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<LocationService> _logger;
    private readonly LocationServiceConfiguration _configuration;
    private readonly IRealmClient _realmClient;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IResourceClient _resourceClient;

    private const string LOCATION_KEY_PREFIX = "location:";
    private const string CODE_INDEX_PREFIX = "code-index:";
    private const string REALM_INDEX_PREFIX = "realm-index:";
    private const string PARENT_INDEX_PREFIX = "parent-index:";
    private const string ROOT_LOCATIONS_PREFIX = "root-locations:";

    public LocationService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        ILogger<LocationService> logger,
        LocationServiceConfiguration configuration,
        IRealmClient realmClient,
        IEventConsumer eventConsumer,
        IDistributedLockProvider lockProvider,
        IResourceClient resourceClient)
    {
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        _realmClient = realmClient;
        _lockProvider = lockProvider;
        _resourceClient = resourceClient;

        // Register event handlers via partial class (LocationServiceEvents.cs)
        ((IBannouService)this).RegisterEventConsumers(eventConsumer);
    }

    #region Key Building Helpers

    private static string BuildLocationKey(Guid locationId) => $"{LOCATION_KEY_PREFIX}{locationId}";
    private static string BuildCodeIndexKey(Guid realmId, string code) => $"{CODE_INDEX_PREFIX}{realmId}:{code.ToUpperInvariant()}";
    private static string BuildRealmIndexKey(Guid realmId) => $"{REALM_INDEX_PREFIX}{realmId}";
    private static string BuildParentIndexKey(Guid realmId, Guid parentId) => $"{PARENT_INDEX_PREFIX}{realmId}:{parentId}";
    private static string BuildRootLocationsKey(Guid realmId) => $"{ROOT_LOCATIONS_PREFIX}{realmId}";

    #endregion

    #region Read Operations

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationResponse?)> GetLocationAsync(GetLocationRequest body, CancellationToken cancellationToken = default)
    {
            _logger.LogDebug("Getting location by ID: {LocationId}", body.LocationId);

            var locationKey = BuildLocationKey(body.LocationId);
            var model = await GetLocationWithCacheAsync(locationKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, MapToResponse(model));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationResponse?)> GetLocationByCodeAsync(GetLocationByCodeRequest body, CancellationToken cancellationToken = default)
    {
            _logger.LogDebug("Getting location by code: {Code} in realm {RealmId}", body.Code, body.RealmId);

            var code = body.Code.ToUpperInvariant();
            var codeIndexKey = BuildCodeIndexKey(body.RealmId, code);
            var locationId = await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Location).GetAsync(codeIndexKey, cancellationToken);

            if (string.IsNullOrEmpty(locationId))
            {
                return (StatusCodes.NotFound, null);
            }

            if (!Guid.TryParse(locationId, out var parsedLocationId))
            {
                _logger.LogWarning("Invalid location ID in code index for code {Code} in realm {RealmId}: {LocationId}", body.Code, body.RealmId, locationId);
                return (StatusCodes.NotFound, null);
            }

            var locationKey = BuildLocationKey(parsedLocationId);
            var model = await GetLocationWithCacheAsync(locationKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, MapToResponse(model));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationListResponse?)> ListLocationsAsync(ListLocationsRequest body, CancellationToken cancellationToken = default)
    {
            _logger.LogDebug("Listing locations with filters - RealmId: {RealmId}, LocationType: {LocationType}, IncludeDeprecated: {IncludeDeprecated}",
                body.RealmId, body.LocationType, body.IncludeDeprecated);

            var realmIndexKey = BuildRealmIndexKey(body.RealmId);
            var locationIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Location).GetAsync(realmIndexKey, cancellationToken) ?? new List<Guid>();
            var allLocations = await LoadLocationsByIdsAsync(locationIds, cancellationToken);

            // Apply filters
            var filtered = allLocations.AsEnumerable();

            if (!body.IncludeDeprecated)
            {
                filtered = filtered.Where(l => !l.IsDeprecated);
            }

            if (body.LocationType.HasValue)
            {
                filtered = filtered.Where(l => l.LocationType == body.LocationType.Value);
            }

            var filteredList = filtered.ToList();
            var totalCount = filteredList.Count;

            // Apply pagination
            var page = body.Page;
            var pageSize = body.PageSize;
            var pagedList = filteredList
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(MapToResponse)
                .ToList();

            return (StatusCodes.OK, new LocationListResponse
            {
                Locations = pagedList,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                HasNextPage = page * pageSize < totalCount,
                HasPreviousPage = page > 1
            });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationListResponse?)> ListLocationsByRealmAsync(ListLocationsByRealmRequest body, CancellationToken cancellationToken = default)
    {
            _logger.LogDebug("Listing locations by realm: {RealmId}", body.RealmId);

            var realmIndexKey = BuildRealmIndexKey(body.RealmId);
            var locationIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Location).GetAsync(realmIndexKey, cancellationToken) ?? new List<Guid>();

            if (locationIds.Count == 0)
            {
                return (StatusCodes.OK, new LocationListResponse
                {
                    Locations = new List<LocationResponse>(),
                    TotalCount = 0,
                    Page = body.Page,
                    PageSize = body.PageSize
                });
            }

            var locations = await LoadLocationsByIdsAsync(locationIds, cancellationToken);

            // Apply filters
            var filtered = locations.AsEnumerable();

            if (!body.IncludeDeprecated)
            {
                filtered = filtered.Where(l => !l.IsDeprecated);
            }

            if (body.LocationType.HasValue)
            {
                filtered = filtered.Where(l => l.LocationType == body.LocationType.Value);
            }

            var filteredList = filtered.ToList();
            var totalCount = filteredList.Count;

            // Apply pagination
            var page = body.Page;
            var pageSize = body.PageSize;
            var pagedList = filteredList
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(MapToResponse)
                .ToList();

            return (StatusCodes.OK, new LocationListResponse
            {
                Locations = pagedList,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                HasNextPage = page * pageSize < totalCount,
                HasPreviousPage = page > 1
            });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationListResponse?)> ListLocationsByParentAsync(ListLocationsByParentRequest body, CancellationToken cancellationToken = default)
    {
            _logger.LogDebug("Listing locations by parent: {ParentLocationId}", body.ParentLocationId);

            // First get the parent location to determine the realm
            var parentKey = BuildLocationKey(body.ParentLocationId);
            var parentModel = await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location).GetAsync(parentKey, cancellationToken);

            if (parentModel == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var parentIndexKey = BuildParentIndexKey(parentModel.RealmId, body.ParentLocationId);
            var childIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Location).GetAsync(parentIndexKey, cancellationToken) ?? new List<Guid>();

            if (childIds.Count == 0)
            {
                return (StatusCodes.OK, new LocationListResponse
                {
                    Locations = new List<LocationResponse>(),
                    TotalCount = 0,
                    Page = body.Page,
                    PageSize = body.PageSize
                });
            }

            var locations = await LoadLocationsByIdsAsync(childIds, cancellationToken);

            // Apply filters
            var filtered = locations.AsEnumerable();

            if (!body.IncludeDeprecated)
            {
                filtered = filtered.Where(l => !l.IsDeprecated);
            }

            if (body.LocationType.HasValue)
            {
                filtered = filtered.Where(l => l.LocationType == body.LocationType.Value);
            }

            var filteredList = filtered.ToList();
            var totalCount = filteredList.Count;

            // Apply pagination
            var page = body.Page;
            var pageSize = body.PageSize;
            var pagedList = filteredList
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(MapToResponse)
                .ToList();

            return (StatusCodes.OK, new LocationListResponse
            {
                Locations = pagedList,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                HasNextPage = page * pageSize < totalCount,
                HasPreviousPage = page > 1
            });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationListResponse?)> ListRootLocationsAsync(ListRootLocationsRequest body, CancellationToken cancellationToken = default)
    {
            _logger.LogDebug("Listing root locations for realm: {RealmId}", body.RealmId);

            var rootKey = BuildRootLocationsKey(body.RealmId);
            var rootIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Location).GetAsync(rootKey, cancellationToken) ?? new List<Guid>();

            if (rootIds.Count == 0)
            {
                return (StatusCodes.OK, new LocationListResponse
                {
                    Locations = new List<LocationResponse>(),
                    TotalCount = 0,
                    Page = body.Page,
                    PageSize = body.PageSize
                });
            }

            var locations = await LoadLocationsByIdsAsync(rootIds, cancellationToken);

            // Apply filters
            var filtered = locations.AsEnumerable();

            if (!body.IncludeDeprecated)
            {
                filtered = filtered.Where(l => !l.IsDeprecated);
            }

            if (body.LocationType.HasValue)
            {
                filtered = filtered.Where(l => l.LocationType == body.LocationType.Value);
            }

            var filteredList = filtered.ToList();
            var totalCount = filteredList.Count;

            // Apply pagination
            var page = body.Page;
            var pageSize = body.PageSize;
            var pagedList = filteredList
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(MapToResponse)
                .ToList();

            return (StatusCodes.OK, new LocationListResponse
            {
                Locations = pagedList,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                HasNextPage = page * pageSize < totalCount,
                HasPreviousPage = page > 1
            });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationListResponse?)> GetLocationAncestorsAsync(GetLocationAncestorsRequest body, CancellationToken cancellationToken = default)
    {
            _logger.LogDebug("Getting ancestors for location: {LocationId}", body.LocationId);

            var locationKey = BuildLocationKey(body.LocationId);
            var model = await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location).GetAsync(locationKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var ancestors = new List<LocationModel>();
            var currentParentId = model.ParentLocationId;
            var maxDepth = _configuration.MaxAncestorDepth;
            var depth = 0;

            while (currentParentId.HasValue && depth < maxDepth)
            {
                var parentKey = BuildLocationKey(currentParentId.Value);
                var parentModel = await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location).GetAsync(parentKey, cancellationToken);

                if (parentModel == null)
                {
                    _logger.LogWarning("Ancestor walk for location {LocationId} found missing parent {ParentId} at depth {Depth}",
                        body.LocationId, currentParentId.Value, depth);
                    break;
                }

                ancestors.Add(parentModel);
                currentParentId = parentModel.ParentLocationId;
                depth++;
            }

            return (StatusCodes.OK, new LocationListResponse
            {
                Locations = ancestors.Select(MapToResponse).ToList(),
                TotalCount = ancestors.Count,
                Page = 1,
                PageSize = ancestors.Count
            });
    }

    /// <summary>
    /// Validates a location against territory boundaries.
    /// Used by Contract service's clause type handler system for territory validation.
    /// </summary>
    /// <param name="body">Territory validation request with location and boundaries.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result indicating if location passes territory check.</returns>
    public async Task<(StatusCodes, ValidateTerritoryResponse?)> ValidateTerritoryAsync(
        ValidateTerritoryRequest body,
        CancellationToken cancellationToken = default)
    {
            _logger.LogDebug("Validating territory for location {LocationId} against {TerritoryCount} territories, mode: {Mode}",
                body.LocationId, body.TerritoryLocationIds?.Count ?? 0, body.TerritoryMode);

            // Get location to verify it exists
            var locationKey = BuildLocationKey(body.LocationId);
            var location = await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location)
                .GetAsync(locationKey, cancellationToken);

            if (location == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Build hierarchy set (location + all ancestors)
            var locationHierarchy = new HashSet<Guid> { body.LocationId };

            var currentParentId = location.ParentLocationId;
            var maxDepth = _configuration.MaxAncestorDepth;
            var depth = 0;

            while (currentParentId.HasValue && depth < maxDepth)
            {
                locationHierarchy.Add(currentParentId.Value);

                var parentKey = BuildLocationKey(currentParentId.Value);
                var parentModel = await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location)
                    .GetAsync(parentKey, cancellationToken);

                if (parentModel == null)
                {
                    break;
                }

                currentParentId = parentModel.ParentLocationId;
                depth++;
            }

            // Check for overlap with territory
            var territorySet = body.TerritoryLocationIds?.ToHashSet() ?? new HashSet<Guid>();
            Guid? matchedTerritory = null;

            foreach (var territoryId in territorySet)
            {
                if (locationHierarchy.Contains(territoryId))
                {
                    matchedTerritory = territoryId;
                    break;
                }
            }

            var hasOverlap = matchedTerritory.HasValue;
            var mode = body.TerritoryMode ?? TerritoryMode.Exclusive;

            // Evaluate based on mode
            if (mode == TerritoryMode.Exclusive && hasOverlap)
            {
                return (StatusCodes.OK, new ValidateTerritoryResponse
                {
                    IsValid = false,
                    ViolationReason = "Location overlaps with exclusive territory",
                    MatchedTerritoryId = matchedTerritory
                });
            }

            if (mode == TerritoryMode.Inclusive && !hasOverlap)
            {
                return (StatusCodes.OK, new ValidateTerritoryResponse
                {
                    IsValid = false,
                    ViolationReason = "Location is outside inclusive territory",
                    MatchedTerritoryId = null
                });
            }

            return (StatusCodes.OK, new ValidateTerritoryResponse
            {
                IsValid = true,
                ViolationReason = null,
                MatchedTerritoryId = hasOverlap ? matchedTerritory : null
            });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationListResponse?)> GetLocationDescendantsAsync(GetLocationDescendantsRequest body, CancellationToken cancellationToken = default)
    {
            _logger.LogDebug("Getting descendants for location: {LocationId}, maxDepth: {MaxDepth}", body.LocationId, body.MaxDepth);

            var locationKey = BuildLocationKey(body.LocationId);
            var model = await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location).GetAsync(locationKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var maxDepth = body.MaxDepth ?? _configuration.DefaultDescendantMaxDepth;
            var descendants = new List<LocationModel>();
            await CollectDescendantsAsync(body.LocationId, model.RealmId, descendants, 0, maxDepth, cancellationToken);

            // Apply filters
            var filtered = descendants.AsEnumerable();

            if (!body.IncludeDeprecated)
            {
                filtered = filtered.Where(l => !l.IsDeprecated);
            }

            if (body.LocationType.HasValue)
            {
                filtered = filtered.Where(l => l.LocationType == body.LocationType.Value);
            }

            var filteredList = filtered.ToList();
            var totalCount = filteredList.Count;

            // Apply pagination
            var page = body.Page;
            var pageSize = body.PageSize;
            var pagedList = filteredList
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(MapToResponse)
                .ToList();

            return (StatusCodes.OK, new LocationListResponse
            {
                Locations = pagedList,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                HasNextPage = page * pageSize < totalCount,
                HasPreviousPage = page > 1
            });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationExistsResponse?)> LocationExistsAsync(LocationExistsRequest body, CancellationToken cancellationToken = default)
    {
            _logger.LogDebug("Checking if location exists: {LocationId}", body.LocationId);

            var locationKey = BuildLocationKey(body.LocationId);
            var model = await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location).GetAsync(locationKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.OK, new LocationExistsResponse
                {
                    Exists = false,
                    IsActive = false,
                    LocationId = null,
                    RealmId = null
                });
            }

            return (StatusCodes.OK, new LocationExistsResponse
            {
                Exists = true,
                IsActive = !model.IsDeprecated,
                LocationId = model.LocationId,
                RealmId = model.RealmId
            });
    }

    #endregion

    #region Write Operations

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationResponse?)> CreateLocationAsync(CreateLocationRequest body, CancellationToken cancellationToken = default)
    {
            _logger.LogDebug("Creating location with code: {Code} in realm {RealmId}", body.Code, body.RealmId);

            // Validate realm exists and is active
            var realmExistsResponse = await _realmClient.RealmExistsAsync(new RealmExistsRequest { RealmId = body.RealmId }, cancellationToken);
            if (realmExistsResponse == null || !realmExistsResponse.Exists || !realmExistsResponse.IsActive)
            {
                _logger.LogWarning("Cannot create location - realm {RealmId} does not exist or is not active", body.RealmId);
                return (StatusCodes.BadRequest, null);
            }

            // Check for duplicate code in realm
            var code = body.Code.ToUpperInvariant();
            var codeIndexKey = BuildCodeIndexKey(body.RealmId, code);
            var existingId = await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Location).GetAsync(codeIndexKey, cancellationToken);

            if (!string.IsNullOrEmpty(existingId))
            {
                _logger.LogWarning("Location with code {Code} already exists in realm {RealmId}", body.Code, body.RealmId);
                return (StatusCodes.Conflict, null);
            }

            // If parent specified, validate it exists in same realm and get depth
            var depth = 0;
            if (body.ParentLocationId.HasValue)
            {
                var parentKey = BuildLocationKey(body.ParentLocationId.Value);
                var parentModel = await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location).GetAsync(parentKey, cancellationToken);

                if (parentModel == null)
                {
                    _logger.LogWarning("Parent location {ParentLocationId} does not exist", body.ParentLocationId);
                    return (StatusCodes.BadRequest, null);
                }

                if (parentModel.RealmId != body.RealmId)
                {
                    _logger.LogWarning("Parent location {ParentLocationId} is in a different realm", body.ParentLocationId);
                    return (StatusCodes.BadRequest, null);
                }

                depth = parentModel.Depth + 1;
            }

            var locationId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            var model = new LocationModel
            {
                LocationId = locationId,
                RealmId = body.RealmId,
                Code = code,
                Name = body.Name,
                Description = body.Description,
                LocationType = body.LocationType,
                ParentLocationId = body.ParentLocationId,
                Depth = depth,
                IsDeprecated = false,
                DeprecatedAt = null,
                DeprecationReason = null,
                Metadata = body.Metadata,
                CreatedAt = now,
                UpdatedAt = now
            };

            // Save the model
            var locationKey = BuildLocationKey(locationId);
            await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location).SaveAsync(locationKey, model, cancellationToken: cancellationToken);

            // Update code index (maps code string -> locationId string, state store reference type constraint)
            await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Location).SaveAsync(codeIndexKey, locationId.ToString(), cancellationToken: cancellationToken);

            // Update realm index
            await AddToRealmIndexAsync(body.RealmId, locationId, cancellationToken);

            // Update parent or root index
            if (body.ParentLocationId.HasValue)
            {
                await AddToParentIndexAsync(body.RealmId, body.ParentLocationId.Value, locationId, cancellationToken);
            }
            else
            {
                await AddToRootLocationsAsync(body.RealmId, locationId, cancellationToken);
            }

            // Populate cache
            await PopulateLocationCacheAsync(locationKey, model, cancellationToken);

            // Publish event
            await PublishLocationCreatedEventAsync(model, cancellationToken);

            _logger.LogDebug("Created location {LocationId} with code {Code} in realm {RealmId}", locationId, body.Code, body.RealmId);
            return (StatusCodes.OK, MapToResponse(model));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationResponse?)> UpdateLocationAsync(UpdateLocationRequest body, CancellationToken cancellationToken = default)
    {
            _logger.LogDebug("Updating location: {LocationId}", body.LocationId);

            var locationKey = BuildLocationKey(body.LocationId);
            var model = await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location).GetAsync(locationKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Track changed fields and apply updates
            var changedFields = new List<string>();

            if (!string.IsNullOrEmpty(body.Name) && body.Name != model.Name)
            {
                model.Name = body.Name;
                changedFields.Add("name");
            }

            if (body.Description != null && body.Description != model.Description)
            {
                model.Description = body.Description;
                changedFields.Add("description");
            }

            if (body.LocationType.HasValue && body.LocationType.Value != model.LocationType)
            {
                model.LocationType = body.LocationType.Value;
                changedFields.Add("locationType");
            }

            if (body.Metadata != null)
            {
                model.Metadata = body.Metadata;
                changedFields.Add("metadata");
            }

            if (changedFields.Count > 0)
            {
                model.UpdatedAt = DateTimeOffset.UtcNow;

                // Save updated model
                await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location).SaveAsync(locationKey, model, cancellationToken: cancellationToken);

                // Update cache
                await PopulateLocationCacheAsync(locationKey, model, cancellationToken);

                // Publish event
                await PublishLocationUpdatedEventAsync(model, changedFields, cancellationToken);
            }

            _logger.LogDebug("Updated location {LocationId}, changed fields: {ChangedFields}", body.LocationId, changedFields);
            return (StatusCodes.OK, MapToResponse(model));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationResponse?)> SetLocationParentAsync(SetLocationParentRequest body, CancellationToken cancellationToken = default)
    {
            _logger.LogDebug("Setting parent for location: {LocationId} to {ParentLocationId}", body.LocationId, body.ParentLocationId);

            var locationKey = BuildLocationKey(body.LocationId);
            var model = await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location).GetAsync(locationKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Skip if parent is already set to the requested value
            if (model.ParentLocationId == body.ParentLocationId)
            {
                return (StatusCodes.OK, MapToResponse(model));
            }

            // Get new parent
            var newParentKey = BuildLocationKey(body.ParentLocationId);
            var newParentModel = await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location).GetAsync(newParentKey, cancellationToken);

            if (newParentModel == null)
            {
                _logger.LogWarning("New parent location {ParentLocationId} does not exist", body.ParentLocationId);
                return (StatusCodes.BadRequest, null);
            }

            // Validate same realm
            if (newParentModel.RealmId != model.RealmId)
            {
                _logger.LogWarning("New parent location {ParentLocationId} is in a different realm", body.ParentLocationId);
                return (StatusCodes.BadRequest, null);
            }

            // Prevent circular reference
            if (await IsDescendantOfAsync(body.ParentLocationId, body.LocationId, model.RealmId, cancellationToken))
            {
                _logger.LogWarning("Cannot set parent - would create circular reference");
                return (StatusCodes.BadRequest, null);
            }

            var oldParentId = model.ParentLocationId;
            var oldDepth = model.Depth;
            var newDepth = newParentModel.Depth + 1;

            // Update parent and depth
            model.ParentLocationId = body.ParentLocationId;
            model.Depth = newDepth;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location).SaveAsync(locationKey, model, cancellationToken: cancellationToken);

            // Update indexes
            if (!oldParentId.HasValue)
            {
                await RemoveFromRootLocationsAsync(model.RealmId, body.LocationId, cancellationToken);
            }
            else
            {
                await RemoveFromParentIndexAsync(model.RealmId, oldParentId.Value, body.LocationId, cancellationToken);
            }

            await AddToParentIndexAsync(model.RealmId, body.ParentLocationId, body.LocationId, cancellationToken);

            // Update descendant depths if depth changed
            if (newDepth != oldDepth)
            {
                await UpdateDescendantDepthsAsync(body.LocationId, model.RealmId, newDepth - oldDepth, cancellationToken);
            }

            // Update cache
            await PopulateLocationCacheAsync(locationKey, model, cancellationToken);

            // Publish event with changed fields
            var changedFields = new List<string> { "parentLocationId", "depth" };
            await PublishLocationUpdatedEventAsync(model, changedFields, cancellationToken);

            _logger.LogDebug("Set parent of location {LocationId} to {ParentLocationId}", body.LocationId, body.ParentLocationId);
            return (StatusCodes.OK, MapToResponse(model));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationResponse?)> RemoveLocationParentAsync(RemoveLocationParentRequest body, CancellationToken cancellationToken = default)
    {
            _logger.LogDebug("Removing parent from location: {LocationId}", body.LocationId);

            var locationKey = BuildLocationKey(body.LocationId);
            var model = await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location).GetAsync(locationKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (!model.ParentLocationId.HasValue)
            {
                // Already a root location
                return (StatusCodes.OK, MapToResponse(model));
            }

            var oldParentId = model.ParentLocationId;
            var oldDepth = model.Depth;

            // Make it a root location
            model.ParentLocationId = null;
            model.Depth = 0;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location).SaveAsync(locationKey, model, cancellationToken: cancellationToken);

            // Update indexes
            await RemoveFromParentIndexAsync(model.RealmId, oldParentId.Value, body.LocationId, cancellationToken);
            await AddToRootLocationsAsync(model.RealmId, body.LocationId, cancellationToken);

            // Update descendant depths
            if (oldDepth != 0)
            {
                await UpdateDescendantDepthsAsync(body.LocationId, model.RealmId, -oldDepth, cancellationToken);
            }

            // Update cache
            await PopulateLocationCacheAsync(locationKey, model, cancellationToken);

            // Publish event with changed fields
            var changedFields = new List<string> { "parentLocationId", "depth" };
            await PublishLocationUpdatedEventAsync(model, changedFields, cancellationToken);

            _logger.LogDebug("Removed parent from location {LocationId}", body.LocationId);
            return (StatusCodes.OK, MapToResponse(model));
    }

    /// <inheritdoc />
    public async Task<StatusCodes> DeleteLocationAsync(DeleteLocationRequest body, CancellationToken cancellationToken = default)
    {
            _logger.LogDebug("Deleting location: {LocationId}", body.LocationId);

            var locationKey = BuildLocationKey(body.LocationId);
            var model = await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location).GetAsync(locationKey, cancellationToken);

            if (model == null)
            {
                return StatusCodes.NotFound;
            }

            // Check for children
            var parentIndexKey = BuildParentIndexKey(model.RealmId, body.LocationId);
            var childIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Location).GetAsync(parentIndexKey, cancellationToken) ?? new List<Guid>();

            if (childIds.Count > 0)
            {
                _logger.LogWarning("Cannot delete location {LocationId} - has {ChildCount} children", body.LocationId, childIds.Count);
                return StatusCodes.Conflict;
            }

            // Check for external references via lib-resource (L1 - allowed per SERVICE_HIERARCHY)
            // L3/L4 services like CharacterEncounter register their location references with lib-resource
            try
            {
                var resourceCheck = await _resourceClient.CheckReferencesAsync(
                    new CheckReferencesRequest
                    {
                        ResourceType = "location",
                        ResourceId = body.LocationId
                    }, cancellationToken);

                if (resourceCheck != null && resourceCheck.RefCount > 0)
                {
                    var sourceTypes = resourceCheck.Sources != null
                        ? string.Join(", ", resourceCheck.Sources.Select(s => s.SourceType))
                        : "unknown";
                    _logger.LogWarning(
                        "Cannot delete location {LocationId} - has {RefCount} external references from: {SourceTypes}",
                        body.LocationId, resourceCheck.RefCount, sourceTypes);

                    // Execute cleanup callbacks (CASCADE/DETACH) before proceeding
                    var cleanupResult = await _resourceClient.ExecuteCleanupAsync(
                        new ExecuteCleanupRequest
                        {
                            ResourceType = "location",
                            ResourceId = body.LocationId,
                            CleanupPolicy = CleanupPolicy.ALL_REQUIRED
                        }, cancellationToken);

                    if (!cleanupResult.Success)
                    {
                        _logger.LogWarning(
                            "Cleanup blocked for location {LocationId}: {Reason}",
                            body.LocationId, cleanupResult.AbortReason ?? "cleanup failed");
                        return StatusCodes.Conflict;
                    }
                }
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // No references registered - this is normal
                _logger.LogDebug("No lib-resource references found for location {LocationId}", body.LocationId);
            }
            catch (ApiException ex)
            {
                // lib-resource unavailable - fail closed to protect referential integrity
                _logger.LogError(ex,
                    "lib-resource unavailable when checking references for location {LocationId}, blocking deletion for safety",
                    body.LocationId);
                await _messageBus.TryPublishErrorAsync(
                    "location", "DeleteLocation", "resource_service_unavailable",
                    $"lib-resource unavailable when checking references for location {body.LocationId}",
                    dependency: "resource", endpoint: "post:/location/delete",
                    details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
                return StatusCodes.ServiceUnavailable;
            }

            // Delete the location
            await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location).DeleteAsync(locationKey, cancellationToken);

            // Clean up code index
            var codeIndexKey = BuildCodeIndexKey(model.RealmId, model.Code);
            await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Location).DeleteAsync(codeIndexKey, cancellationToken);

            // Remove from realm index
            await RemoveFromRealmIndexAsync(model.RealmId, body.LocationId, cancellationToken);

            // Remove from parent or root index
            if (!model.ParentLocationId.HasValue)
            {
                await RemoveFromRootLocationsAsync(model.RealmId, body.LocationId, cancellationToken);
            }
            else
            {
                await RemoveFromParentIndexAsync(model.RealmId, model.ParentLocationId.Value, body.LocationId, cancellationToken);
            }

            // Invalidate cache
            await InvalidateLocationCacheAsync(locationKey, cancellationToken);

            // Publish event
            await PublishLocationDeletedEventAsync(model, cancellationToken);

            _logger.LogDebug("Deleted location {LocationId}", body.LocationId);
            return StatusCodes.OK;
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationResponse?)> DeprecateLocationAsync(DeprecateLocationRequest body, CancellationToken cancellationToken = default)
    {
            _logger.LogDebug("Deprecating location: {LocationId}", body.LocationId);

            var locationKey = BuildLocationKey(body.LocationId);
            var model = await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location).GetAsync(locationKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (model.IsDeprecated)
            {
                return (StatusCodes.Conflict, null);
            }

            model.IsDeprecated = true;
            model.DeprecatedAt = DateTimeOffset.UtcNow;
            model.DeprecationReason = body.Reason;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location).SaveAsync(locationKey, model, cancellationToken: cancellationToken);

            // Update cache
            await PopulateLocationCacheAsync(locationKey, model, cancellationToken);

            // Publish event with changed fields
            var changedFields = new List<string> { "isDeprecated", "deprecatedAt", "deprecationReason" };
            await PublishLocationUpdatedEventAsync(model, changedFields, cancellationToken);

            _logger.LogDebug("Deprecated location {LocationId}", body.LocationId);
            return (StatusCodes.OK, MapToResponse(model));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationResponse?)> UndeprecateLocationAsync(UndeprecateLocationRequest body, CancellationToken cancellationToken = default)
    {
            _logger.LogDebug("Undeprecating location: {LocationId}", body.LocationId);

            var locationKey = BuildLocationKey(body.LocationId);
            var model = await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location).GetAsync(locationKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (!model.IsDeprecated)
            {
                return (StatusCodes.BadRequest, null);
            }

            model.IsDeprecated = false;
            model.DeprecatedAt = null;
            model.DeprecationReason = null;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location).SaveAsync(locationKey, model, cancellationToken: cancellationToken);

            // Update cache
            await PopulateLocationCacheAsync(locationKey, model, cancellationToken);

            // Publish event with changed fields
            var changedFields = new List<string> { "isDeprecated", "deprecatedAt", "deprecationReason" };
            await PublishLocationUpdatedEventAsync(model, changedFields, cancellationToken);

            _logger.LogDebug("Undeprecated location {LocationId}", body.LocationId);
            return (StatusCodes.OK, MapToResponse(model));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, SeedLocationsResponse?)> SeedLocationsAsync(SeedLocationsRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Seeding {Count} locations, updateExisting: {UpdateExisting}",
            body.Locations.Count, body.UpdateExisting);

        var created = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<string>();

            // Build a map of realm codes to realm IDs
            var realmCodeToId = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var failedRealmCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var seedLocation in body.Locations)
            {
                if (!realmCodeToId.ContainsKey(seedLocation.RealmCode) && !failedRealmCodes.Contains(seedLocation.RealmCode))
                {
                    // Look up realm by code
                    var realmResponse = await _realmClient.GetRealmByCodeAsync(new GetRealmByCodeRequest { Code = seedLocation.RealmCode }, cancellationToken);
                    if (realmResponse != null)
                    {
                        realmCodeToId[seedLocation.RealmCode] = realmResponse.RealmId;
                    }
                    else
                    {
                        failedRealmCodes.Add(seedLocation.RealmCode);
                        _logger.LogWarning("Realm code {RealmCode} not found during seed, skipping all locations in this realm", seedLocation.RealmCode);
                    }
                }
            }

            // First pass: Create all locations without parent relationships
            var codeToLocationId = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

            foreach (var seedLocation in body.Locations)
            {
                var code = seedLocation.Code.ToUpperInvariant();

                if (!realmCodeToId.TryGetValue(seedLocation.RealmCode, out var realmId))
                {
                    errors.Add($"Realm '{seedLocation.RealmCode}' not found for location '{code}'");
                    continue;
                }

                var codeIndexKey = BuildCodeIndexKey(realmId, code);
                var existingId = await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Location).GetAsync(codeIndexKey, cancellationToken);

                var compositeKey = $"{seedLocation.RealmCode}:{code}";

                if (!string.IsNullOrEmpty(existingId) && Guid.TryParse(existingId, out var existingLocationId))
                {
                    codeToLocationId[compositeKey] = existingLocationId;

                    if (body.UpdateExisting)
                    {
                        var locationKey = BuildLocationKey(existingLocationId);
                        var existingModel = await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location).GetAsync(locationKey, cancellationToken);

                        if (existingModel != null)
                        {
                            existingModel.Name = seedLocation.Name;
                            if (seedLocation.Description != null) existingModel.Description = seedLocation.Description;
                            existingModel.LocationType = seedLocation.LocationType;
                            if (seedLocation.Metadata != null) existingModel.Metadata = seedLocation.Metadata;
                            existingModel.UpdatedAt = DateTimeOffset.UtcNow;

                            await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location).SaveAsync(locationKey, existingModel, cancellationToken: cancellationToken);
                            updated++;
                            _logger.LogDebug("Updated existing location: {Code}", code);
                        }
                    }
                    else
                    {
                        skipped++;
                        _logger.LogDebug("Skipped existing location: {Code}", code);
                    }
                }
                else
                {
                    // Create new location (without parent for now)
                    var createRequest = new CreateLocationRequest
                    {
                        Code = code,
                        Name = seedLocation.Name,
                        Description = seedLocation.Description,
                        RealmId = realmId,
                        LocationType = seedLocation.LocationType,
                        ParentLocationId = null, // Set later in second pass
                        Metadata = seedLocation.Metadata
                    };

                    var (status, response) = await CreateLocationAsync(createRequest, cancellationToken);

                    if (status == StatusCodes.OK && response != null)
                    {
                        codeToLocationId[compositeKey] = response.LocationId;
                        created++;
                        _logger.LogDebug("Created new location: {Code}", code);
                    }
                    else
                    {
                        errors.Add($"Failed to create location '{code}': status {status}");
                    }
                }
            }

            // Second pass: Set parent relationships
            foreach (var seedLocation in body.Locations.Where(l => !string.IsNullOrEmpty(l.ParentLocationCode)))
            {
                var code = seedLocation.Code.ToUpperInvariant();
                var compositeKey = $"{seedLocation.RealmCode}:{code}";
                // ParentLocationCode is known non-null from the Where filter above
                var parentCode = seedLocation.ParentLocationCode ?? throw new InvalidOperationException("ParentLocationCode was null after filtering");
                var parentCompositeKey = $"{seedLocation.RealmCode}:{parentCode.ToUpperInvariant()}";

                if (!codeToLocationId.TryGetValue(compositeKey, out var locationId))
                {
                    continue;
                }

                if (!codeToLocationId.TryGetValue(parentCompositeKey, out var parentLocationId))
                {
                    errors.Add($"Parent location '{seedLocation.ParentLocationCode}' not found for '{code}'");
                    continue;
                }

                var setParentRequest = new SetLocationParentRequest
                {
                    LocationId = locationId,
                    ParentLocationId = parentLocationId
                };

                var (status, _) = await SetLocationParentAsync(setParentRequest, cancellationToken);

                if (status != StatusCodes.OK)
                {
                    errors.Add($"Failed to set parent for '{code}': status {status}");
                }
            }

            return (StatusCodes.OK, new SeedLocationsResponse
            {
                Created = created,
                Updated = updated,
                Skipped = skipped,
                Errors = errors
            });
    }

    /// <summary>
    /// Transfer a location from its current realm to a different realm.
    /// Updates all realm-scoped indexes. The location becomes a root in the target realm
    /// (parent cleared). Caller is responsible for tree ordering and re-parenting.
    /// </summary>
    public async Task<(StatusCodes, LocationResponse?)> TransferLocationToRealmAsync(
        TransferLocationToRealmRequest body,
        CancellationToken cancellationToken = default)
    {
            _logger.LogDebug("Transferring location {LocationId} to realm {TargetRealmId}",
                body.LocationId, body.TargetRealmId);

            var locationKey = BuildLocationKey(body.LocationId);
            var model = await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location)
                .GetAsync(locationKey, cancellationToken);

            if (model == null)
            {
                _logger.LogDebug("Location not found for transfer: {LocationId}", body.LocationId);
                return (StatusCodes.NotFound, null);
            }

            // No-op if already in target realm (idempotent)
            if (model.RealmId == body.TargetRealmId)
            {
                return (StatusCodes.OK, MapToResponse(model));
            }

            // Validate target realm exists and is active
            var realmExistsResponse = await _realmClient.RealmExistsAsync(
                new RealmExistsRequest { RealmId = body.TargetRealmId }, cancellationToken);

            if (realmExistsResponse == null || !realmExistsResponse.Exists || !realmExistsResponse.IsActive)
            {
                _logger.LogWarning("Cannot transfer location - target realm {TargetRealmId} does not exist or is not active",
                    body.TargetRealmId);
                return (StatusCodes.NotFound, null);
            }

            // Check code uniqueness in target realm
            var targetCodeIndexKey = BuildCodeIndexKey(body.TargetRealmId, model.Code);
            var existingId = await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Location)
                .GetAsync(targetCodeIndexKey, cancellationToken);

            if (!string.IsNullOrEmpty(existingId))
            {
                _logger.LogWarning(
                    "Cannot transfer location {LocationId} - target realm {TargetRealmId} already has location with code {Code}",
                    body.LocationId, body.TargetRealmId, model.Code);
                return (StatusCodes.Conflict, null);
            }

            var oldRealmId = model.RealmId;
            var oldParentId = model.ParentLocationId;

            // Remove from source realm indexes
            var sourceCodeIndexKey = BuildCodeIndexKey(oldRealmId, model.Code);
            await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Location)
                .DeleteAsync(sourceCodeIndexKey, cancellationToken);
            await RemoveFromRealmIndexAsync(oldRealmId, body.LocationId, cancellationToken);

            if (oldParentId.HasValue)
            {
                await RemoveFromParentIndexAsync(oldRealmId, oldParentId.Value, body.LocationId, cancellationToken);
            }
            else
            {
                await RemoveFromRootLocationsAsync(oldRealmId, body.LocationId, cancellationToken);
            }

            // Update model: new realm, become root, depth 0
            model.RealmId = body.TargetRealmId;
            model.ParentLocationId = null;
            model.Depth = 0;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            // Save updated model
            await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location)
                .SaveAsync(locationKey, model, cancellationToken: cancellationToken);

            // Add to target realm indexes
            await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Location)
                .SaveAsync(targetCodeIndexKey, body.LocationId.ToString(), cancellationToken: cancellationToken);
            await AddToRealmIndexAsync(body.TargetRealmId, body.LocationId, cancellationToken);
            await AddToRootLocationsAsync(body.TargetRealmId, body.LocationId, cancellationToken);

            // Invalidate cache
            await InvalidateLocationCacheAsync(locationKey, cancellationToken);

            // Publish update event
            var changedFields = new List<string> { "realmId", "parentLocationId", "depth" };
            await PublishLocationUpdatedEventAsync(model, changedFields, cancellationToken);

            _logger.LogInformation(
                "Transferred location {LocationId} ({Code}) from realm {OldRealmId} to realm {NewRealmId}",
                body.LocationId, model.Code, oldRealmId, body.TargetRealmId);

            return (StatusCodes.OK, MapToResponse(model));
    }

    #endregion

    #region Private Helpers

    private async Task<List<LocationModel>> LoadLocationsByIdsAsync(List<Guid> locationIds, CancellationToken cancellationToken)
    {
        if (locationIds.Count == 0)
        {
            return new List<LocationModel>();
        }

        var keysList = locationIds.Select(BuildLocationKey).ToList();

        // Try cache first with bulk get
        var cacheStore = _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.LocationCache);
        var cachedResult = await cacheStore.GetBulkAsync(keysList, cancellationToken);

        // Find cache misses
        var missedKeys = keysList.Where(k => !cachedResult.ContainsKey(k)).ToList();

        // Fetch misses from persistent store
        Dictionary<string, LocationModel> fetchedFromStore = new();
        if (missedKeys.Count > 0)
        {
            var persistentStore = _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location);
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
            _logger.LogWarning("Could not acquire lock for realm index {RealmIndexKey}", realmIndexKey);
            return;
        }

        var locationIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Location).GetAsync(realmIndexKey, cancellationToken) ?? new List<Guid>();
        if (!locationIds.Contains(locationId))
        {
            locationIds.Add(locationId);
            await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Location).SaveAsync(realmIndexKey, locationIds, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromRealmIndexAsync(Guid realmId, Guid locationId, CancellationToken cancellationToken)
    {
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
            _logger.LogWarning("Could not acquire lock for realm index {RealmIndexKey}", realmIndexKey);
            return;
        }

        var locationIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Location).GetAsync(realmIndexKey, cancellationToken) ?? new List<Guid>();
        if (locationIds.Remove(locationId))
        {
            await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Location).SaveAsync(realmIndexKey, locationIds, cancellationToken: cancellationToken);
        }
    }

    private async Task AddToParentIndexAsync(Guid realmId, Guid parentId, Guid locationId, CancellationToken cancellationToken)
    {
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
            _logger.LogWarning("Could not acquire lock for parent index {ParentIndexKey}", parentIndexKey);
            return;
        }

        var childIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Location).GetAsync(parentIndexKey, cancellationToken) ?? new List<Guid>();
        if (!childIds.Contains(locationId))
        {
            childIds.Add(locationId);
            await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Location).SaveAsync(parentIndexKey, childIds, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromParentIndexAsync(Guid realmId, Guid parentId, Guid locationId, CancellationToken cancellationToken)
    {
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
            _logger.LogWarning("Could not acquire lock for parent index {ParentIndexKey}", parentIndexKey);
            return;
        }

        var store = _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Location);
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
            _logger.LogWarning("Could not acquire lock for root locations {RootKey}", rootKey);
            return;
        }

        var rootIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Location).GetAsync(rootKey, cancellationToken) ?? new List<Guid>();
        if (!rootIds.Contains(locationId))
        {
            rootIds.Add(locationId);
            await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Location).SaveAsync(rootKey, rootIds, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromRootLocationsAsync(Guid realmId, Guid locationId, CancellationToken cancellationToken)
    {
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
            _logger.LogWarning("Could not acquire lock for root locations {RootKey}", rootKey);
            return;
        }

        var rootIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Location).GetAsync(rootKey, cancellationToken) ?? new List<Guid>();
        if (rootIds.Remove(locationId))
        {
            await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Location).SaveAsync(rootKey, rootIds, cancellationToken: cancellationToken);
        }
    }

    private async Task CollectDescendantsAsync(Guid parentId, Guid realmId, List<LocationModel> descendants, int currentDepth, int maxDepth, CancellationToken cancellationToken)
    {
        if (currentDepth >= maxDepth)
        {
            return;
        }

        var parentIndexKey = BuildParentIndexKey(realmId, parentId);
        var childIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Location).GetAsync(parentIndexKey, cancellationToken) ?? new List<Guid>();

        foreach (var childId in childIds)
        {
            var childKey = BuildLocationKey(childId);
            var childModel = await _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location).GetAsync(childKey, cancellationToken);

            if (childModel != null)
            {
                descendants.Add(childModel);
                await CollectDescendantsAsync(childId, realmId, descendants, currentDepth + 1, maxDepth, cancellationToken);
            }
        }
    }

    private async Task<bool> IsDescendantOfAsync(Guid potentialDescendantId, Guid potentialAncestorId, Guid realmId, CancellationToken cancellationToken)
    {
        var descendants = new List<LocationModel>();
        await CollectDescendantsAsync(potentialAncestorId, realmId, descendants, 0, _configuration.MaxDescendantDepth, cancellationToken);
        return descendants.Any(d => d.LocationId == potentialDescendantId);
    }

    private async Task UpdateDescendantDepthsAsync(Guid parentId, Guid realmId, int depthChange, CancellationToken cancellationToken)
    {
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
        var locationStore = _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location);
        await locationStore.SaveBulkAsync(itemsToSave, cancellationToken: cancellationToken);

        // Bulk invalidate cache for all updated descendants (single cache call)
        var cacheStore = _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.LocationCache);
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
            Metadata = model.Metadata,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }

    private async Task PublishLocationCreatedEventAsync(LocationModel model, CancellationToken cancellationToken)
    {
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
            Metadata = model.Metadata,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };

        await _messageBus.TryPublishAsync("location.created", eventData, cancellationToken: cancellationToken);
    }

    private async Task PublishLocationUpdatedEventAsync(LocationModel model, IList<string> changedFields, CancellationToken cancellationToken)
    {
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
            Metadata = model.Metadata,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt,
            ChangedFields = changedFields.ToList()
        };

        await _messageBus.TryPublishAsync("location.updated", eventData, cancellationToken: cancellationToken);
    }

    private async Task PublishLocationDeletedEventAsync(LocationModel model, CancellationToken cancellationToken)
    {
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
            Metadata = model.Metadata
        };

        await _messageBus.TryPublishAsync("location.deleted", eventData, cancellationToken: cancellationToken);
    }

    #endregion

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permission service on startup.
    /// Uses generated permission data from x-permissions sections in the OpenAPI schema.
    /// </summary>
    public async Task RegisterServicePermissionsAsync(string appId)
    {
        _logger.LogDebug("Registering Location service permissions");
        await LocationPermissionRegistration.RegisterViaEventAsync(_messageBus, appId, _logger);
    }

    #endregion

    #region Cache Methods

    /// <summary>
    /// Get location with Redis cache read-through. Falls back to MySQL persistent store on cache miss.
    /// </summary>
    private async Task<LocationModel?> GetLocationWithCacheAsync(string locationKey, CancellationToken ct)
    {
        var cacheStore = _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.LocationCache);

        // Try cache first
        var cached = await cacheStore.GetAsync(locationKey, ct);
        if (cached is not null) return cached;

        // Fallback to persistent store
        var store = _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.Location);
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
        var cacheStore = _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.LocationCache);
        await cacheStore.SaveAsync(locationKey, model,
            new StateOptions { Ttl = _configuration.CacheTtlSeconds }, ct);
    }

    /// <summary>
    /// Invalidate location cache after a write/delete operation.
    /// </summary>
    private async Task InvalidateLocationCacheAsync(string locationKey, CancellationToken ct)
    {
        var cacheStore = _stateStoreFactory.GetStore<LocationModel>(StateStoreDefinitions.LocationCache);
        await cacheStore.DeleteAsync(locationKey, ct);
    }

    #endregion

    #region Internal Model

    internal class LocationModel
    {
        public Guid LocationId { get; set; }
        public Guid RealmId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public LocationType LocationType { get; set; } = LocationType.OTHER;
        public Guid? ParentLocationId { get; set; }
        public int Depth { get; set; }
        public bool IsDeprecated { get; set; }
        public DateTimeOffset? DeprecatedAt { get; set; }
        public string? DeprecationReason { get; set; }
        public object? Metadata { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    #endregion
}
