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
    private readonly IServiceProvider _serviceProvider;
    private readonly IStateStoreFactory _stateStoreFactory;

    /// <summary>
    /// Lazily resolved IMessageBus. State loads before Messaging in L0 infrastructure order,
    /// so we cannot inject IMessageBus directly in the constructor. Instead, we resolve it
    /// on first use when all plugins are guaranteed to be loaded.
    /// </summary>
    private IMessageBus MessageBus => _serviceProvider.GetRequiredService<IMessageBus>();

    public StateService(
        ILogger<StateService> logger,
        StateServiceConfiguration configuration,
        IServiceProvider serviceProvider,
        IStateStoreFactory stateStoreFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _stateStoreFactory = stateStoreFactory;
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, GetStateResponse?)> GetStateAsync(
        GetStateRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting state from store {StoreName} with key {Key}", body.StoreName, body.Key);

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
            Etag = etag
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, SaveStateResponse?)> SaveStateAsync(
        SaveStateRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Saving state to store {StoreName} with key {Key}", body.StoreName, body.Key);

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

    /// <inheritdoc />
    public async Task<(StatusCodes, DeleteStateResponse?)> DeleteStateAsync(
        DeleteStateRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting state from store {StoreName} with key {Key}", body.StoreName, body.Key);

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

    /// <inheritdoc />
    public async Task<(StatusCodes, QueryStateResponse?)> QueryStateAsync(
        QueryStateRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Querying state from store {StoreName}", body.StoreName);

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

    /// <inheritdoc />
    public async Task<(StatusCodes, BulkSaveStateResponse?)> BulkSaveStateAsync(
        BulkSaveStateRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Bulk saving {Count} items to store {StoreName}", body.Items.Count, body.StoreName);

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

    /// <inheritdoc />
    public async Task<(StatusCodes, BulkExistsStateResponse?)> BulkExistsStateAsync(
        BulkExistsStateRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Bulk checking existence of {Count} keys in store {StoreName}", body.Keys.Count, body.StoreName);

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

    /// <inheritdoc />
    public async Task<(StatusCodes, BulkDeleteStateResponse?)> BulkDeleteStateAsync(
        BulkDeleteStateRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Bulk deleting {Count} keys from store {StoreName}", body.Keys.Count, body.StoreName);

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

    /// <inheritdoc />
    public async Task<(StatusCodes, ListStoresResponse?)> ListStoresAsync(
        ListStoresRequest? body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing state stores with filter {BackendFilter}", body?.BackendFilter);

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

    #region Permission Registration

    #endregion
}
