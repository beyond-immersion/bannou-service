using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-location.tests")]

namespace BeyondImmersion.BannouService.Location;

/// <summary>
/// Implementation of the Location service.
/// Manages location definitions - places within realms with hierarchical organization.
/// Locations are partitioned by realm for scalability.
/// </summary>
[DaprService("location", typeof(ILocationService), lifetime: ServiceLifetime.Scoped)]
public partial class LocationService : ILocationService
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<LocationService> _logger;
    private readonly LocationServiceConfiguration _configuration;
    private readonly IRealmClient _realmClient;

    private const string STATE_STORE = "location-statestore";
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
        IEventConsumer eventConsumer)
    {
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _realmClient = realmClient ?? throw new ArgumentNullException(nameof(realmClient));

        // Register event handlers via partial class (LocationServiceEvents.cs)
        ArgumentNullException.ThrowIfNull(eventConsumer, nameof(eventConsumer));
        ((IDaprService)this).RegisterEventConsumers(eventConsumer);
    }

    #region Key Building Helpers

    private static string BuildLocationKey(string locationId) => $"{LOCATION_KEY_PREFIX}{locationId}";
    private static string BuildCodeIndexKey(string realmId, string code) => $"{CODE_INDEX_PREFIX}{realmId}:{code.ToUpperInvariant()}";
    private static string BuildRealmIndexKey(string realmId) => $"{REALM_INDEX_PREFIX}{realmId}";
    private static string BuildParentIndexKey(string realmId, string parentId) => $"{PARENT_INDEX_PREFIX}{realmId}:{parentId}";
    private static string BuildRootLocationsKey(string realmId) => $"{ROOT_LOCATIONS_PREFIX}{realmId}";

    #endregion

    #region Read Operations

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationResponse?)> GetLocationAsync(GetLocationRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting location by ID: {LocationId}", body.LocationId);

            var locationKey = BuildLocationKey(body.LocationId.ToString());
            var model = await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).GetAsync(locationKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, MapToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting location {LocationId}", body.LocationId);
            await _messageBus.TryPublishErrorAsync(
                "location", "GetLocation", "unexpected_exception", ex.Message,
                dependency: "dapr-state", endpoint: "post:/location/get",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationResponse?)> GetLocationByCodeAsync(GetLocationByCodeRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting location by code: {Code} in realm {RealmId}", body.Code, body.RealmId);

            var code = body.Code.ToUpperInvariant();
            var codeIndexKey = BuildCodeIndexKey(body.RealmId.ToString(), code);
            var locationId = await _stateStoreFactory.GetStore<string>(STATE_STORE).GetAsync(codeIndexKey, cancellationToken);

            if (string.IsNullOrEmpty(locationId))
            {
                return (StatusCodes.NotFound, null);
            }

            var locationKey = BuildLocationKey(locationId);
            var model = await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).GetAsync(locationKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, MapToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting location by code {Code} in realm {RealmId}", body.Code, body.RealmId);
            await _messageBus.TryPublishErrorAsync(
                "location", "GetLocationByCode", "unexpected_exception", ex.Message,
                dependency: "dapr-state", endpoint: "post:/location/get-by-code",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationListResponse?)> ListLocationsAsync(ListLocationsRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Listing locations with filters - RealmId: {RealmId}, LocationType: {LocationType}, IncludeDeprecated: {IncludeDeprecated}",
                body.RealmId, body.LocationType, body.IncludeDeprecated);

            // RealmId is required - locations are partitioned by realm
            if (!body.RealmId.HasValue)
            {
                _logger.LogWarning("ListLocationsAsync called without required RealmId");
                return (StatusCodes.BadRequest, null);
            }

            var realmIndexKey = BuildRealmIndexKey(body.RealmId.Value.ToString());
            var locationIds = await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).GetAsync(realmIndexKey, cancellationToken) ?? new List<string>();
            var allLocations = await LoadLocationsByIdsAsync(locationIds, cancellationToken);

            // Apply filters
            var filtered = allLocations.AsEnumerable();

            if (!body.IncludeDeprecated)
            {
                filtered = filtered.Where(l => !l.IsDeprecated);
            }

            if (body.LocationType.HasValue)
            {
                filtered = filtered.Where(l => l.LocationType == body.LocationType.Value.ToString());
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing locations");
            await _messageBus.TryPublishErrorAsync(
                "location", "ListLocations", "unexpected_exception", ex.Message,
                dependency: "dapr-state", endpoint: "post:/location/list",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationListResponse?)> ListLocationsByRealmAsync(ListLocationsByRealmRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Listing locations by realm: {RealmId}", body.RealmId);

            var realmIndexKey = BuildRealmIndexKey(body.RealmId.ToString());
            var locationIds = await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).GetAsync(realmIndexKey, cancellationToken) ?? new List<string>();

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
                filtered = filtered.Where(l => l.LocationType == body.LocationType.Value.ToString());
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing locations for realm {RealmId}", body.RealmId);
            await _messageBus.TryPublishErrorAsync(
                "location", "ListLocationsByRealm", "unexpected_exception", ex.Message,
                dependency: "dapr-state", endpoint: "post:/location/list-by-realm",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationListResponse?)> ListLocationsByParentAsync(ListLocationsByParentRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Listing locations by parent: {ParentLocationId}", body.ParentLocationId);

            // First get the parent location to determine the realm
            var parentKey = BuildLocationKey(body.ParentLocationId.ToString());
            var parentModel = await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).GetAsync(parentKey, cancellationToken);

            if (parentModel == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var parentIndexKey = BuildParentIndexKey(parentModel.RealmId, body.ParentLocationId.ToString());
            var childIds = await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).GetAsync(parentIndexKey, cancellationToken) ?? new List<string>();

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
                filtered = filtered.Where(l => l.LocationType == body.LocationType.Value.ToString());
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing locations by parent {ParentLocationId}", body.ParentLocationId);
            await _messageBus.TryPublishErrorAsync(
                "location", "ListLocationsByParent", "unexpected_exception", ex.Message,
                dependency: "dapr-state", endpoint: "post:/location/list-by-parent",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationListResponse?)> ListRootLocationsAsync(ListRootLocationsRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Listing root locations for realm: {RealmId}", body.RealmId);

            var rootKey = BuildRootLocationsKey(body.RealmId.ToString());
            var rootIds = await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).GetAsync(rootKey, cancellationToken) ?? new List<string>();

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
                filtered = filtered.Where(l => l.LocationType == body.LocationType.Value.ToString());
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing root locations for realm {RealmId}", body.RealmId);
            await _messageBus.TryPublishErrorAsync(
                "location", "ListRootLocations", "unexpected_exception", ex.Message,
                dependency: "dapr-state", endpoint: "post:/location/list-root",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationListResponse?)> GetLocationAncestorsAsync(GetLocationAncestorsRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting ancestors for location: {LocationId}", body.LocationId);

            var locationKey = BuildLocationKey(body.LocationId.ToString());
            var model = await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).GetAsync(locationKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var ancestors = new List<LocationModel>();
            var currentParentId = model.ParentLocationId;
            var maxDepth = 10; // Safety limit
            var depth = 0;

            while (!string.IsNullOrEmpty(currentParentId) && depth < maxDepth)
            {
                var parentKey = BuildLocationKey(currentParentId);
                var parentModel = await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).GetAsync(parentKey, cancellationToken);

                if (parentModel == null)
                {
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting ancestors for location {LocationId}", body.LocationId);
            await _messageBus.TryPublishErrorAsync(
                "location", "GetLocationAncestors", "unexpected_exception", ex.Message,
                dependency: "dapr-state", endpoint: "post:/location/get-ancestors",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationListResponse?)> GetLocationDescendantsAsync(GetLocationDescendantsRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting descendants for location: {LocationId}, maxDepth: {MaxDepth}", body.LocationId, body.MaxDepth);

            var locationKey = BuildLocationKey(body.LocationId.ToString());
            var model = await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).GetAsync(locationKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var maxDepth = body.MaxDepth ?? 10;
            var descendants = new List<LocationModel>();
            await CollectDescendantsAsync(body.LocationId.ToString(), model.RealmId, descendants, 0, maxDepth, cancellationToken);

            // Apply filters
            var filtered = descendants.AsEnumerable();

            if (!body.IncludeDeprecated)
            {
                filtered = filtered.Where(l => !l.IsDeprecated);
            }

            if (body.LocationType.HasValue)
            {
                filtered = filtered.Where(l => l.LocationType == body.LocationType.Value.ToString());
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting descendants for location {LocationId}", body.LocationId);
            await _messageBus.TryPublishErrorAsync(
                "location", "GetLocationDescendants", "unexpected_exception", ex.Message,
                dependency: "dapr-state", endpoint: "post:/location/get-descendants",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationExistsResponse?)> LocationExistsAsync(LocationExistsRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Checking if location exists: {LocationId}", body.LocationId);

            var locationKey = BuildLocationKey(body.LocationId.ToString());
            var model = await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).GetAsync(locationKey, cancellationToken);

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
                LocationId = Guid.Parse(model.LocationId),
                RealmId = Guid.Parse(model.RealmId)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if location exists {LocationId}", body.LocationId);
            await _messageBus.TryPublishErrorAsync(
                "location", "LocationExists", "unexpected_exception", ex.Message,
                dependency: "dapr-state", endpoint: "post:/location/exists",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Write Operations

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationResponse?)> CreateLocationAsync(CreateLocationRequest body, CancellationToken cancellationToken = default)
    {
        try
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
            var codeIndexKey = BuildCodeIndexKey(body.RealmId.ToString(), code);
            var existingId = await _stateStoreFactory.GetStore<string>(STATE_STORE).GetAsync(codeIndexKey, cancellationToken);

            if (!string.IsNullOrEmpty(existingId))
            {
                _logger.LogWarning("Location with code {Code} already exists in realm {RealmId}", body.Code, body.RealmId);
                return (StatusCodes.Conflict, null);
            }

            // If parent specified, validate it exists in same realm and get depth
            var depth = 0;
            if (body.ParentLocationId.HasValue)
            {
                var parentKey = BuildLocationKey(body.ParentLocationId.Value.ToString());
                var parentModel = await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).GetAsync(parentKey, cancellationToken);

                if (parentModel == null)
                {
                    _logger.LogWarning("Parent location {ParentLocationId} does not exist", body.ParentLocationId);
                    return (StatusCodes.BadRequest, null);
                }

                if (parentModel.RealmId != body.RealmId.ToString())
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
                LocationId = locationId.ToString(),
                RealmId = body.RealmId.ToString(),
                Code = code,
                Name = body.Name,
                Description = body.Description,
                LocationType = body.LocationType.ToString(),
                ParentLocationId = body.ParentLocationId?.ToString(),
                Depth = depth,
                IsDeprecated = false,
                DeprecatedAt = null,
                DeprecationReason = null,
                Metadata = body.Metadata,
                CreatedAt = now,
                UpdatedAt = now
            };

            // Save the model
            var locationKey = BuildLocationKey(locationId.ToString());
            await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).SaveAsync(locationKey, model, cancellationToken: cancellationToken);

            // Update code index
            await _stateStoreFactory.GetStore<string>(STATE_STORE).SaveAsync(codeIndexKey, locationId.ToString(), cancellationToken: cancellationToken);

            // Update realm index
            await AddToRealmIndexAsync(body.RealmId.ToString(), locationId.ToString(), cancellationToken);

            // Update parent or root index
            if (body.ParentLocationId.HasValue)
            {
                await AddToParentIndexAsync(body.RealmId.ToString(), body.ParentLocationId.Value.ToString(), locationId.ToString(), cancellationToken);
            }
            else
            {
                await AddToRootLocationsAsync(body.RealmId.ToString(), locationId.ToString(), cancellationToken);
            }

            // Publish event
            await PublishLocationCreatedEventAsync(model, cancellationToken);

            _logger.LogInformation("Created location {LocationId} with code {Code} in realm {RealmId}", locationId, body.Code, body.RealmId);
            return (StatusCodes.Created, MapToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating location {Code}", body.Code);
            await _messageBus.TryPublishErrorAsync(
                "location", "CreateLocation", "unexpected_exception", ex.Message,
                dependency: "dapr-state", endpoint: "post:/location/create",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationResponse?)> UpdateLocationAsync(UpdateLocationRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Updating location: {LocationId}", body.LocationId);

            var locationKey = BuildLocationKey(body.LocationId.ToString());
            var model = await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).GetAsync(locationKey, cancellationToken);

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

            if (body.LocationType.HasValue && body.LocationType.Value.ToString() != model.LocationType)
            {
                model.LocationType = body.LocationType.Value.ToString();
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
                await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).SaveAsync(locationKey, model, cancellationToken: cancellationToken);

                // Publish event
                await PublishLocationUpdatedEventAsync(model, changedFields, cancellationToken);
            }

            _logger.LogDebug("Updated location {LocationId}, changed fields: {ChangedFields}", body.LocationId, changedFields);
            return (StatusCodes.OK, MapToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating location {LocationId}", body.LocationId);
            await _messageBus.TryPublishErrorAsync(
                "location", "UpdateLocation", "unexpected_exception", ex.Message,
                dependency: "dapr-state", endpoint: "post:/location/update",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationResponse?)> SetLocationParentAsync(SetLocationParentRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Setting parent for location: {LocationId} to {ParentLocationId}", body.LocationId, body.ParentLocationId);

            var locationKey = BuildLocationKey(body.LocationId.ToString());
            var model = await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).GetAsync(locationKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Get new parent
            var newParentKey = BuildLocationKey(body.ParentLocationId.ToString());
            var newParentModel = await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).GetAsync(newParentKey, cancellationToken);

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
            if (await IsDescendantOfAsync(body.ParentLocationId.ToString(), body.LocationId.ToString(), model.RealmId, cancellationToken))
            {
                _logger.LogWarning("Cannot set parent - would create circular reference");
                return (StatusCodes.BadRequest, null);
            }

            var oldParentId = model.ParentLocationId;
            var oldDepth = model.Depth;
            var newDepth = newParentModel.Depth + 1;

            // Update parent and depth
            model.ParentLocationId = body.ParentLocationId.ToString();
            model.Depth = newDepth;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).SaveAsync(locationKey, model, cancellationToken: cancellationToken);

            // Update indexes
            if (string.IsNullOrEmpty(oldParentId))
            {
                await RemoveFromRootLocationsAsync(model.RealmId, body.LocationId.ToString(), cancellationToken);
            }
            else
            {
                await RemoveFromParentIndexAsync(model.RealmId, oldParentId, body.LocationId.ToString(), cancellationToken);
            }

            await AddToParentIndexAsync(model.RealmId, body.ParentLocationId.ToString(), body.LocationId.ToString(), cancellationToken);

            // Update descendant depths if depth changed
            if (newDepth != oldDepth)
            {
                await UpdateDescendantDepthsAsync(body.LocationId.ToString(), model.RealmId, newDepth - oldDepth, cancellationToken);
            }

            // Publish event with changed fields
            var changedFields = new List<string> { "parentLocationId", "depth" };
            await PublishLocationUpdatedEventAsync(model, changedFields, cancellationToken);

            _logger.LogDebug("Set parent of location {LocationId} to {ParentLocationId}", body.LocationId, body.ParentLocationId);
            return (StatusCodes.OK, MapToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting parent for location {LocationId}", body.LocationId);
            await _messageBus.TryPublishErrorAsync(
                "location", "SetLocationParent", "unexpected_exception", ex.Message,
                dependency: "dapr-state", endpoint: "post:/location/set-parent",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationResponse?)> RemoveLocationParentAsync(RemoveLocationParentRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Removing parent from location: {LocationId}", body.LocationId);

            var locationKey = BuildLocationKey(body.LocationId.ToString());
            var model = await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).GetAsync(locationKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (string.IsNullOrEmpty(model.ParentLocationId))
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

            await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).SaveAsync(locationKey, model, cancellationToken: cancellationToken);

            // Update indexes
            await RemoveFromParentIndexAsync(model.RealmId, oldParentId, body.LocationId.ToString(), cancellationToken);
            await AddToRootLocationsAsync(model.RealmId, body.LocationId.ToString(), cancellationToken);

            // Update descendant depths
            if (oldDepth != 0)
            {
                await UpdateDescendantDepthsAsync(body.LocationId.ToString(), model.RealmId, -oldDepth, cancellationToken);
            }

            // Publish event with changed fields
            var changedFields = new List<string> { "parentLocationId", "depth" };
            await PublishLocationUpdatedEventAsync(model, changedFields, cancellationToken);

            _logger.LogDebug("Removed parent from location {LocationId}", body.LocationId);
            return (StatusCodes.OK, MapToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing parent from location {LocationId}", body.LocationId);
            await _messageBus.TryPublishErrorAsync(
                "location", "RemoveLocationParent", "unexpected_exception", ex.Message,
                dependency: "dapr-state", endpoint: "post:/location/remove-parent",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, object?)> DeleteLocationAsync(DeleteLocationRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Deleting location: {LocationId}", body.LocationId);

            var locationKey = BuildLocationKey(body.LocationId.ToString());
            var model = await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).GetAsync(locationKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Check for children
            var parentIndexKey = BuildParentIndexKey(model.RealmId, body.LocationId.ToString());
            var childIds = await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).GetAsync(parentIndexKey, cancellationToken) ?? new List<string>();

            if (childIds.Count > 0)
            {
                _logger.LogWarning("Cannot delete location {LocationId} - has {ChildCount} children", body.LocationId, childIds.Count);
                return (StatusCodes.Conflict, null);
            }

            // Delete the location
            await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).DeleteAsync(locationKey, cancellationToken);

            // Clean up code index
            var codeIndexKey = BuildCodeIndexKey(model.RealmId, model.Code);
            await _stateStoreFactory.GetStore<string>(STATE_STORE).DeleteAsync(codeIndexKey, cancellationToken);

            // Remove from realm index
            await RemoveFromRealmIndexAsync(model.RealmId, body.LocationId.ToString(), cancellationToken);

            // Remove from parent or root index
            if (string.IsNullOrEmpty(model.ParentLocationId))
            {
                await RemoveFromRootLocationsAsync(model.RealmId, body.LocationId.ToString(), cancellationToken);
            }
            else
            {
                await RemoveFromParentIndexAsync(model.RealmId, model.ParentLocationId, body.LocationId.ToString(), cancellationToken);
            }

            // Publish event
            await PublishLocationDeletedEventAsync(model, cancellationToken);

            _logger.LogInformation("Deleted location {LocationId}", body.LocationId);
            return (StatusCodes.NoContent, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting location {LocationId}", body.LocationId);
            await _messageBus.TryPublishErrorAsync(
                "location", "DeleteLocation", "unexpected_exception", ex.Message,
                dependency: "dapr-state", endpoint: "post:/location/delete",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationResponse?)> DeprecateLocationAsync(DeprecateLocationRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Deprecating location: {LocationId}", body.LocationId);

            var locationKey = BuildLocationKey(body.LocationId.ToString());
            var model = await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).GetAsync(locationKey, cancellationToken);

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

            await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).SaveAsync(locationKey, model, cancellationToken: cancellationToken);

            // Publish event with changed fields
            var changedFields = new List<string> { "isDeprecated", "deprecatedAt", "deprecationReason" };
            await PublishLocationUpdatedEventAsync(model, changedFields, cancellationToken);

            _logger.LogDebug("Deprecated location {LocationId}", body.LocationId);
            return (StatusCodes.OK, MapToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deprecating location {LocationId}", body.LocationId);
            await _messageBus.TryPublishErrorAsync(
                "location", "DeprecateLocation", "unexpected_exception", ex.Message,
                dependency: "dapr-state", endpoint: "post:/location/deprecate",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, LocationResponse?)> UndeprecateLocationAsync(UndeprecateLocationRequest body, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Undeprecating location: {LocationId}", body.LocationId);

            var locationKey = BuildLocationKey(body.LocationId.ToString());
            var model = await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).GetAsync(locationKey, cancellationToken);

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

            await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).SaveAsync(locationKey, model, cancellationToken: cancellationToken);

            // Publish event with changed fields
            var changedFields = new List<string> { "isDeprecated", "deprecatedAt", "deprecationReason" };
            await PublishLocationUpdatedEventAsync(model, changedFields, cancellationToken);

            _logger.LogDebug("Undeprecated location {LocationId}", body.LocationId);
            return (StatusCodes.OK, MapToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error undeprecating location {LocationId}", body.LocationId);
            await _messageBus.TryPublishErrorAsync(
                "location", "UndeprecateLocation", "unexpected_exception", ex.Message,
                dependency: "dapr-state", endpoint: "post:/location/undeprecate",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
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

        try
        {
            // Build a map of realm codes to realm IDs
            var realmCodeToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var seedLocation in body.Locations)
            {
                if (!realmCodeToId.ContainsKey(seedLocation.RealmCode))
                {
                    // Look up realm by code
                    var realmResponse = await _realmClient.GetRealmByCodeAsync(new GetRealmByCodeRequest { Code = seedLocation.RealmCode }, cancellationToken);
                    if (realmResponse != null)
                    {
                        realmCodeToId[seedLocation.RealmCode] = realmResponse.RealmId.ToString();
                    }
                }
            }

            // First pass: Create all locations without parent relationships
            var codeToLocationId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var seedLocation in body.Locations)
            {
                var code = seedLocation.Code.ToUpperInvariant();

                if (!realmCodeToId.TryGetValue(seedLocation.RealmCode, out var realmId))
                {
                    errors.Add($"Realm '{seedLocation.RealmCode}' not found for location '{code}'");
                    continue;
                }

                var codeIndexKey = BuildCodeIndexKey(realmId, code);
                var existingId = await _stateStoreFactory.GetStore<string>(STATE_STORE).GetAsync(codeIndexKey, cancellationToken);

                var compositeKey = $"{seedLocation.RealmCode}:{code}";

                if (!string.IsNullOrEmpty(existingId))
                {
                    codeToLocationId[compositeKey] = existingId;

                    if (body.UpdateExisting)
                    {
                        var locationKey = BuildLocationKey(existingId);
                        var existingModel = await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).GetAsync(locationKey, cancellationToken);

                        if (existingModel != null)
                        {
                            existingModel.Name = seedLocation.Name;
                            if (seedLocation.Description != null) existingModel.Description = seedLocation.Description;
                            existingModel.LocationType = seedLocation.LocationType.ToString();
                            if (seedLocation.Metadata != null) existingModel.Metadata = seedLocation.Metadata;
                            existingModel.UpdatedAt = DateTimeOffset.UtcNow;

                            await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).SaveAsync(locationKey, existingModel, cancellationToken: cancellationToken);
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
                        RealmId = Guid.Parse(realmId),
                        LocationType = seedLocation.LocationType,
                        ParentLocationId = null, // Set later in second pass
                        Metadata = seedLocation.Metadata
                    };

                    var (status, response) = await CreateLocationAsync(createRequest, cancellationToken);

                    if (status == StatusCodes.Created && response != null)
                    {
                        codeToLocationId[compositeKey] = response.LocationId.ToString();
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
                    LocationId = Guid.Parse(locationId),
                    ParentLocationId = Guid.Parse(parentLocationId)
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding locations");
            await _messageBus.TryPublishErrorAsync(
                "location", "SeedLocations", "unexpected_exception", ex.Message,
                dependency: "dapr-state", endpoint: "post:/location/seed",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            errors.Add($"Unexpected error: {ex.Message}");
            return (StatusCodes.OK, new SeedLocationsResponse
            {
                Created = created,
                Updated = updated,
                Skipped = skipped,
                Errors = errors
            });
        }
    }

    #endregion

    #region Private Helpers

    private async Task<List<LocationModel>> LoadLocationsByIdsAsync(List<string> locationIds, CancellationToken cancellationToken)
    {
        var results = new List<LocationModel>();
        foreach (var id in locationIds)
        {
            var key = BuildLocationKey(id);
            var model = await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).GetAsync(key, cancellationToken);
            if (model != null)
            {
                results.Add(model);
            }
        }
        return results;
    }

    private async Task AddToRealmIndexAsync(string realmId, string locationId, CancellationToken cancellationToken)
    {
        var realmIndexKey = BuildRealmIndexKey(realmId);
        var locationIds = await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).GetAsync(realmIndexKey, cancellationToken) ?? new List<string>();
        if (!locationIds.Contains(locationId))
        {
            locationIds.Add(locationId);
            await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).SaveAsync(realmIndexKey, locationIds, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromRealmIndexAsync(string realmId, string locationId, CancellationToken cancellationToken)
    {
        var realmIndexKey = BuildRealmIndexKey(realmId);
        var locationIds = await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).GetAsync(realmIndexKey, cancellationToken) ?? new List<string>();
        if (locationIds.Remove(locationId))
        {
            await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).SaveAsync(realmIndexKey, locationIds, cancellationToken: cancellationToken);
        }
    }

    private async Task AddToParentIndexAsync(string realmId, string parentId, string locationId, CancellationToken cancellationToken)
    {
        var parentIndexKey = BuildParentIndexKey(realmId, parentId);
        var childIds = await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).GetAsync(parentIndexKey, cancellationToken) ?? new List<string>();
        if (!childIds.Contains(locationId))
        {
            childIds.Add(locationId);
            await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).SaveAsync(parentIndexKey, childIds, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromParentIndexAsync(string realmId, string parentId, string locationId, CancellationToken cancellationToken)
    {
        var parentIndexKey = BuildParentIndexKey(realmId, parentId);
        var childIds = await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).GetAsync(parentIndexKey, cancellationToken) ?? new List<string>();
        if (childIds.Remove(locationId))
        {
            await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).SaveAsync(parentIndexKey, childIds, cancellationToken: cancellationToken);
        }
    }

    private async Task AddToRootLocationsAsync(string realmId, string locationId, CancellationToken cancellationToken)
    {
        var rootKey = BuildRootLocationsKey(realmId);
        var rootIds = await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).GetAsync(rootKey, cancellationToken) ?? new List<string>();
        if (!rootIds.Contains(locationId))
        {
            rootIds.Add(locationId);
            await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).SaveAsync(rootKey, rootIds, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromRootLocationsAsync(string realmId, string locationId, CancellationToken cancellationToken)
    {
        var rootKey = BuildRootLocationsKey(realmId);
        var rootIds = await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).GetAsync(rootKey, cancellationToken) ?? new List<string>();
        if (rootIds.Remove(locationId))
        {
            await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).SaveAsync(rootKey, rootIds, cancellationToken: cancellationToken);
        }
    }

    private async Task CollectDescendantsAsync(string parentId, string realmId, List<LocationModel> descendants, int currentDepth, int maxDepth, CancellationToken cancellationToken)
    {
        if (currentDepth >= maxDepth)
        {
            return;
        }

        var parentIndexKey = BuildParentIndexKey(realmId, parentId);
        var childIds = await _stateStoreFactory.GetStore<List<string>>(STATE_STORE).GetAsync(parentIndexKey, cancellationToken) ?? new List<string>();

        foreach (var childId in childIds)
        {
            var childKey = BuildLocationKey(childId);
            var childModel = await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).GetAsync(childKey, cancellationToken);

            if (childModel != null)
            {
                descendants.Add(childModel);
                await CollectDescendantsAsync(childId, realmId, descendants, currentDepth + 1, maxDepth, cancellationToken);
            }
        }
    }

    private async Task<bool> IsDescendantOfAsync(string potentialDescendantId, string potentialAncestorId, string realmId, CancellationToken cancellationToken)
    {
        var descendants = new List<LocationModel>();
        await CollectDescendantsAsync(potentialAncestorId, realmId, descendants, 0, 20, cancellationToken);
        return descendants.Any(d => d.LocationId == potentialDescendantId);
    }

    private async Task UpdateDescendantDepthsAsync(string parentId, string realmId, int depthChange, CancellationToken cancellationToken)
    {
        var descendants = new List<LocationModel>();
        await CollectDescendantsAsync(parentId, realmId, descendants, 0, 20, cancellationToken);

        foreach (var descendant in descendants)
        {
            descendant.Depth += depthChange;
            descendant.UpdatedAt = DateTimeOffset.UtcNow;
            var key = BuildLocationKey(descendant.LocationId);
            await _stateStoreFactory.GetStore<LocationModel>(STATE_STORE).SaveAsync(key, descendant, cancellationToken: cancellationToken);
        }
    }

    private LocationResponse MapToResponse(LocationModel model)
    {
        return new LocationResponse
        {
            LocationId = Guid.Parse(model.LocationId),
            RealmId = Guid.Parse(model.RealmId),
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            LocationType = Enum.Parse<LocationType>(model.LocationType),
            ParentLocationId = string.IsNullOrEmpty(model.ParentLocationId) ? null : Guid.Parse(model.ParentLocationId),
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
            LocationId = Guid.Parse(model.LocationId),
            RealmId = Guid.Parse(model.RealmId),
            Code = model.Code,
            Name = model.Name,
            Description = model.Description ?? string.Empty,
            LocationType = model.LocationType,
            ParentLocationId = string.IsNullOrEmpty(model.ParentLocationId) ? Guid.Empty : Guid.Parse(model.ParentLocationId),
            Depth = model.Depth,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt ?? default,
            DeprecationReason = model.DeprecationReason ?? string.Empty,
            Metadata = model.Metadata ?? new object(),
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };

        await _messageBus.PublishAsync("location.created", eventData, cancellationToken: cancellationToken);
    }

    private async Task PublishLocationUpdatedEventAsync(LocationModel model, IList<string> changedFields, CancellationToken cancellationToken)
    {
        var eventData = new LocationUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            LocationId = Guid.Parse(model.LocationId),
            RealmId = Guid.Parse(model.RealmId),
            Code = model.Code,
            Name = model.Name,
            Description = model.Description ?? string.Empty,
            LocationType = model.LocationType,
            ParentLocationId = string.IsNullOrEmpty(model.ParentLocationId) ? Guid.Empty : Guid.Parse(model.ParentLocationId),
            Depth = model.Depth,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt ?? default,
            DeprecationReason = model.DeprecationReason ?? string.Empty,
            Metadata = model.Metadata ?? new object(),
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt,
            ChangedFields = changedFields.ToList()
        };

        await _messageBus.PublishAsync("location.updated", eventData, cancellationToken: cancellationToken);
    }

    private async Task PublishLocationDeletedEventAsync(LocationModel model, CancellationToken cancellationToken)
    {
        var eventData = new LocationDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            LocationId = Guid.Parse(model.LocationId),
            RealmId = Guid.Parse(model.RealmId),
            Code = model.Code,
            Name = model.Name,
            Description = model.Description ?? string.Empty,
            LocationType = model.LocationType,
            ParentLocationId = string.IsNullOrEmpty(model.ParentLocationId) ? Guid.Empty : Guid.Parse(model.ParentLocationId),
            Depth = model.Depth,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt ?? default,
            DeprecationReason = model.DeprecationReason ?? string.Empty,
            Metadata = model.Metadata ?? new object()
        };

        await _messageBus.PublishAsync("location.deleted", eventData, cancellationToken: cancellationToken);
    }

    #endregion

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permissions service on startup.
    /// Uses generated permission data from x-permissions sections in the OpenAPI schema.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {
        _logger.LogInformation("Registering Location service permissions...");
        await LocationPermissionRegistration.RegisterViaEventAsync(_messageBus, _logger);
    }

    #endregion

    #region Internal Model

    internal class LocationModel
    {
        public string LocationId { get; set; } = string.Empty;
        public string RealmId { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string LocationType { get; set; } = "OTHER";
        public string? ParentLocationId { get; set; }
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
