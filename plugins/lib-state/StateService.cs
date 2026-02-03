using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BeyondImmersion.BannouService.State;

/// <summary>
/// Implementation of the State service.
/// Provides HTTP API layer over native Redis/MySQL state management infrastructure.
/// </summary>
[BannouService("state", typeof(IStateService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.Infrastructure)]
public partial class StateService : IStateService
{
    private readonly ILogger<StateService> _logger;
    private readonly StateServiceConfiguration _configuration;
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;

    public StateService(
        ILogger<StateService> logger,
        StateServiceConfiguration configuration,
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, GetStateResponse?)> GetStateAsync(
        GetStateRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting state from store {StoreName} with key {Key}", body.StoreName, body.Key);

        try
        {
            if (!_stateStoreFactory.HasStore(body.StoreName))
            {
                _logger.LogWarning("State store {StoreName} not configured", body.StoreName);
                return (StatusCodes.NotFound, null);
            }

            var store = _stateStoreFactory.GetStore<object>(body.StoreName);
            var (value, etag) = await store.GetWithETagAsync(body.Key, cancellationToken);

            if (value == null)
            {
                _logger.LogDebug("Key {Key} not found in store {StoreName}", body.Key, body.StoreName);
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, new GetStateResponse
            {
                Value = value,
                Etag = etag,
                Metadata = new StateMetadata()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get state from store {StoreName} with key {Key}", body.StoreName, body.Key);
            await _messageBus.TryPublishErrorAsync(
                "state",
                "GetState",
                ex.GetType().Name,
                ex.Message,
                dependency: body.StoreName,
                endpoint: "post:/state/get",
                details: new { StoreName = body.StoreName, Key = body.Key },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, SaveStateResponse?)> SaveStateAsync(
        SaveStateRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Saving state to store {StoreName} with key {Key}", body.StoreName, body.Key);

        try
        {
            if (!_stateStoreFactory.HasStore(body.StoreName))
            {
                _logger.LogWarning("State store {StoreName} not configured", body.StoreName);
                return (StatusCodes.NotFound, null);
            }

            var store = _stateStoreFactory.GetStore<object>(body.StoreName);

            // If ETag is provided, use optimistic concurrency
            if (!string.IsNullOrEmpty(body.Options?.Etag))
            {
                var optimisticEtag = await store.TrySaveAsync(body.Key, body.Value, body.Options.Etag, cancellationToken);
                if (optimisticEtag == null)
                {
                    _logger.LogDebug("ETag mismatch for key {Key} in store {StoreName}", body.Key, body.StoreName);
                    return (StatusCodes.Conflict, null);
                }

                return (StatusCodes.OK, new SaveStateResponse { Etag = optimisticEtag });
            }

            // Standard save - pass options directly (generated StateOptions type)
            var newEtag = await store.SaveAsync(body.Key, body.Value, body.Options, cancellationToken);

            _logger.LogDebug("Saved state to store {StoreName} with key {Key}", body.StoreName, body.Key);
            return (StatusCodes.OK, new SaveStateResponse
            {
                Etag = newEtag
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save state to store {StoreName} with key {Key}", body.StoreName, body.Key);
            await _messageBus.TryPublishErrorAsync(
                "state",
                "SaveState",
                ex.GetType().Name,
                ex.Message,
                dependency: body.StoreName,
                endpoint: "post:/state/save",
                details: new { StoreName = body.StoreName, Key = body.Key },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, DeleteStateResponse?)> DeleteStateAsync(
        DeleteStateRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting state from store {StoreName} with key {Key}", body.StoreName, body.Key);

        try
        {
            if (!_stateStoreFactory.HasStore(body.StoreName))
            {
                _logger.LogWarning("State store {StoreName} not configured", body.StoreName);
                return (StatusCodes.NotFound, null);
            }

            var store = _stateStoreFactory.GetStore<object>(body.StoreName);
            var deleted = await store.DeleteAsync(body.Key, cancellationToken);

            _logger.LogDebug("Delete result for key {Key} in store {StoreName}: {Deleted}", body.Key, body.StoreName, deleted);
            return (StatusCodes.OK, new DeleteStateResponse { Deleted = deleted });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete state from store {StoreName} with key {Key}", body.StoreName, body.Key);
            await _messageBus.TryPublishErrorAsync(
                "state",
                "DeleteState",
                ex.GetType().Name,
                ex.Message,
                dependency: body.StoreName,
                endpoint: "post:/state/delete",
                details: new { StoreName = body.StoreName, Key = body.Key },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, QueryStateResponse?)> QueryStateAsync(
        QueryStateRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Querying state from store {StoreName}", body.StoreName);

        try
        {
            if (!_stateStoreFactory.HasStore(body.StoreName))
            {
                _logger.LogWarning("State store {StoreName} not configured", body.StoreName);
                return (StatusCodes.NotFound, null);
            }

            var backend = _stateStoreFactory.GetBackendType(body.StoreName);

            // Route to appropriate backend
            if (backend == StateBackend.MySql)
            {
                return await QueryMySqlAsync(body, cancellationToken);
            }
            else if (_stateStoreFactory.SupportsSearch(body.StoreName))
            {
                return await QueryRedisSearchAsync(body, cancellationToken);
            }
            else
            {
                _logger.LogWarning("Query not supported for Redis store {StoreName} without search enabled", body.StoreName);
                return (StatusCodes.BadRequest, null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query state from store {StoreName}", body.StoreName);
            await _messageBus.TryPublishErrorAsync(
                "state",
                "QueryState",
                ex.GetType().Name,
                ex.Message,
                dependency: body.StoreName,
                endpoint: "post:/state/query",
                details: new { StoreName = body.StoreName },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Execute query against MySQL backend using JSON path conditions.
    /// </summary>
    private async Task<(StatusCodes, QueryStateResponse?)> QueryMySqlAsync(
        QueryStateRequest body,
        CancellationToken cancellationToken)
    {
        var store = _stateStoreFactory.GetJsonQueryableStore<object>(body.StoreName);

        // Use conditions directly from request (schema-defined QueryCondition type)
        var conditions = (IReadOnlyList<QueryCondition>?)body.Conditions?.ToList() ?? Array.Empty<QueryCondition>();

        // Parse sort specification
        JsonSortSpec? sortSpec = null;
        if (body.Sort?.Count > 0)
        {
            var firstSort = body.Sort.First();
            sortSpec = new JsonSortSpec
            {
                Path = firstSort.Field ?? "$.id",
                Descending = firstSort.Order == SortFieldOrder.Desc
            };
        }

        // Calculate offset from page (Page and PageSize have defaults in schema)
        var page = body.Page;
        var pageSize = body.PageSize > 0 ? body.PageSize : 100;
        var offset = page * pageSize;

        // Execute query
        var result = await store.JsonQueryPagedAsync(
            conditions,
            offset,
            pageSize,
            sortSpec,
            cancellationToken);

        // Convert results to response format
        var results = result.Items.Select(item => item.Value).ToList();

        _logger.LogDebug("MySQL query on store {StoreName} returned {Count}/{Total} results",
            body.StoreName, results.Count, result.TotalCount);

        return (StatusCodes.OK, new QueryStateResponse
        {
            Results = results,
            TotalCount = (int)result.TotalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// Execute query against Redis backend using RedisSearch.
    /// </summary>
    private async Task<(StatusCodes, QueryStateResponse?)> QueryRedisSearchAsync(
        QueryStateRequest body,
        CancellationToken cancellationToken)
    {
        var store = _stateStoreFactory.GetSearchableStore<object>(body.StoreName);

        // Use explicit properties from request
        var indexName = body.IndexName ?? $"{body.StoreName}-idx";
        var searchQuery = body.Query ?? "*";

        // Parse sort specification
        string? sortBy = null;
        var sortDescending = false;
        if (body.Sort?.Count > 0)
        {
            var firstSort = body.Sort.First();
            sortBy = firstSort.Field;
            sortDescending = firstSort.Order == SortFieldOrder.Desc;
        }

        // Calculate offset from page (Page and PageSize have defaults in schema)
        var page = body.Page;
        var pageSize = body.PageSize > 0 ? body.PageSize : 100;
        var offset = page * pageSize;

        // Execute search
        var options = new SearchQueryOptions
        {
            Offset = offset,
            Limit = pageSize,
            SortBy = sortBy,
            SortDescending = sortDescending,
            WithScores = true
        };

        SearchPagedResult<object> result;
        try
        {
            result = await store.SearchAsync(indexName, searchQuery, options, cancellationToken);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("No such index") || ex.Message.Contains("Unknown index"))
        {
            // Index doesn't exist - treat as search not configured (same as SupportsSearch=false)
            _logger.LogWarning("Search index {IndexName} does not exist for store {StoreName}. " +
                "The store has search enabled but the index has not been created.",
                indexName, body.StoreName);
            return (StatusCodes.BadRequest, null);
        }

        // Convert results to response format
        var results = result.Items.Select(item => item.Value).ToList();

        _logger.LogDebug("Redis search on store {StoreName} index {IndexName} returned {Count}/{Total} results",
            body.StoreName, indexName, results.Count, result.TotalCount);

        return (StatusCodes.OK, new QueryStateResponse
        {
            Results = results,
            TotalCount = (int)result.TotalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, BulkGetStateResponse?)> BulkGetStateAsync(
        BulkGetStateRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Bulk getting {Count} keys from store {StoreName}", body.Keys.Count, body.StoreName);

        try
        {
            if (!_stateStoreFactory.HasStore(body.StoreName))
            {
                _logger.LogWarning("State store {StoreName} not configured", body.StoreName);
                return (StatusCodes.NotFound, null);
            }

            var store = _stateStoreFactory.GetStore<object>(body.StoreName);
            var results = await store.GetBulkAsync(body.Keys, cancellationToken);

            // Build response items for all requested keys
            var items = new List<BulkStateItem>();
            foreach (var key in body.Keys)
            {
                if (results.TryGetValue(key, out var value))
                {
                    items.Add(new BulkStateItem
                    {
                        Key = key,
                        Value = value,
                        Found = true
                    });
                }
                else
                {
                    items.Add(new BulkStateItem
                    {
                        Key = key,
                        Value = null,
                        Found = false
                    });
                }
            }

            _logger.LogDebug("Bulk get returned {Found}/{Total} keys from store {StoreName}",
                items.Count(i => i.Found), body.Keys.Count, body.StoreName);

            return (StatusCodes.OK, new BulkGetStateResponse { Items = items });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to bulk get from store {StoreName}", body.StoreName);
            await _messageBus.TryPublishErrorAsync(
                "state",
                "BulkGetState",
                ex.GetType().Name,
                ex.Message,
                dependency: body.StoreName,
                endpoint: "post:/state/bulk-get",
                details: new { StoreName = body.StoreName, KeyCount = body.Keys.Count },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, BulkSaveStateResponse?)> BulkSaveStateAsync(
        BulkSaveStateRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Bulk saving {Count} items to store {StoreName}", body.Items.Count, body.StoreName);

        try
        {
            if (!_stateStoreFactory.HasStore(body.StoreName))
            {
                _logger.LogWarning("State store {StoreName} not configured", body.StoreName);
                return (StatusCodes.NotFound, null);
            }

            var store = _stateStoreFactory.GetStore<object>(body.StoreName);
            var items = body.Items.Select(i => new KeyValuePair<string, object>(i.Key, i.Value));
            var etags = await store.SaveBulkAsync(items, body.Options, cancellationToken);

            var results = etags.Select(kv => new BulkSaveResult
            {
                Key = kv.Key,
                Etag = kv.Value
            }).ToList();

            _logger.LogDebug("Bulk save completed {Count} items to store {StoreName}",
                results.Count, body.StoreName);

            return (StatusCodes.OK, new BulkSaveStateResponse { Results = results });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to bulk save to store {StoreName}", body.StoreName);
            await _messageBus.TryPublishErrorAsync(
                "state",
                "BulkSaveState",
                ex.GetType().Name,
                ex.Message,
                dependency: body.StoreName,
                endpoint: "post:/state/bulk-save",
                details: new { StoreName = body.StoreName, ItemCount = body.Items.Count },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, BulkExistsStateResponse?)> BulkExistsStateAsync(
        BulkExistsStateRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Bulk checking existence of {Count} keys in store {StoreName}", body.Keys.Count, body.StoreName);

        try
        {
            if (!_stateStoreFactory.HasStore(body.StoreName))
            {
                _logger.LogWarning("State store {StoreName} not configured", body.StoreName);
                return (StatusCodes.NotFound, null);
            }

            var store = _stateStoreFactory.GetStore<object>(body.StoreName);
            var existingKeys = await store.ExistsBulkAsync(body.Keys, cancellationToken);

            _logger.LogDebug("Bulk exists check returned {Found}/{Total} keys from store {StoreName}",
                existingKeys.Count, body.Keys.Count, body.StoreName);

            return (StatusCodes.OK, new BulkExistsStateResponse { ExistingKeys = existingKeys.ToList() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to bulk check existence in store {StoreName}", body.StoreName);
            await _messageBus.TryPublishErrorAsync(
                "state",
                "BulkExistsState",
                ex.GetType().Name,
                ex.Message,
                dependency: body.StoreName,
                endpoint: "post:/state/bulk-exists",
                details: new { StoreName = body.StoreName, KeyCount = body.Keys.Count },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, BulkDeleteStateResponse?)> BulkDeleteStateAsync(
        BulkDeleteStateRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Bulk deleting {Count} keys from store {StoreName}", body.Keys.Count, body.StoreName);

        try
        {
            if (!_stateStoreFactory.HasStore(body.StoreName))
            {
                _logger.LogWarning("State store {StoreName} not configured", body.StoreName);
                return (StatusCodes.NotFound, null);
            }

            var store = _stateStoreFactory.GetStore<object>(body.StoreName);
            var deletedCount = await store.DeleteBulkAsync(body.Keys, cancellationToken);

            _logger.LogDebug("Bulk delete removed {Deleted}/{Total} keys from store {StoreName}",
                deletedCount, body.Keys.Count, body.StoreName);

            return (StatusCodes.OK, new BulkDeleteStateResponse { DeletedCount = deletedCount });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to bulk delete from store {StoreName}", body.StoreName);
            await _messageBus.TryPublishErrorAsync(
                "state",
                "BulkDeleteState",
                ex.GetType().Name,
                ex.Message,
                dependency: body.StoreName,
                endpoint: "post:/state/bulk-delete",
                details: new { StoreName = body.StoreName, KeyCount = body.Keys.Count },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ListStoresResponse?)> ListStoresAsync(
        ListStoresRequest? body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing state stores with filter {BackendFilter}", body?.BackendFilter);

        try
        {
            IEnumerable<string> storeNames;

            // Get store names, optionally filtered by backend
            if (body?.BackendFilter != null)
            {
                var backend = body.BackendFilter == ListStoresRequestBackendFilter.Redis
                    ? StateBackend.Redis
                    : StateBackend.MySql;
                storeNames = _stateStoreFactory.GetStoreNames(backend);
            }
            else
            {
                storeNames = _stateStoreFactory.GetStoreNames();
            }

            // Build store info list
            var includeStats = body?.IncludeStats ?? false;
            var stores = new List<StoreInfo>();

            foreach (var name in storeNames)
            {
                var backend = _stateStoreFactory.GetBackendType(name);
                long? keyCount = null;

                if (includeStats)
                {
                    keyCount = await _stateStoreFactory.GetKeyCountAsync(name, cancellationToken);
                }

                stores.Add(new StoreInfo
                {
                    Name = name,
                    Backend = backend == StateBackend.Redis
                        ? StoreInfoBackend.Redis
                        : StoreInfoBackend.Mysql,
                    KeyCount = keyCount.HasValue ? (int)keyCount.Value : null
                });
            }

            _logger.LogDebug("Listed {Count} state stores (includeStats={IncludeStats})", stores.Count, includeStats);
            return (StatusCodes.OK, new ListStoresResponse { Stores = stores });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list state stores");
            await _messageBus.TryPublishErrorAsync(
                "state",
                "ListStores",
                ex.GetType().Name,
                ex.Message,
                dependency: "state-factory",
                endpoint: "post:/state/list-stores",
                details: new { BackendFilter = body?.BackendFilter },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permission service on startup.
    /// Overrides the default IBannouService implementation to use generated permission data.
    /// </summary>
    public async Task RegisterServicePermissionsAsync(string appId)
    {
        _logger.LogInformation("Registering State service permissions...");
        await StatePermissionRegistration.RegisterViaEventAsync(_messageBus, appId, _logger);
    }

    #endregion
}
