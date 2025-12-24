using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-state.tests")]

namespace BeyondImmersion.BannouService.State;

/// <summary>
/// Implementation of the State service.
/// Provides HTTP API layer over native Redis/MySQL state management infrastructure.
/// </summary>
[DaprService("state", typeof(IStateService), lifetime: ServiceLifetime.Scoped)]
public partial class StateService : IStateService
{
    private readonly ILogger<StateService> _logger;
    private readonly StateServiceConfiguration _configuration;
    private readonly IErrorEventEmitter _errorEventEmitter;
    private readonly IStateStoreFactory _stateStoreFactory;

    public StateService(
        ILogger<StateService> logger,
        StateServiceConfiguration configuration,
        IErrorEventEmitter errorEventEmitter,
        IStateStoreFactory stateStoreFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _errorEventEmitter = errorEventEmitter ?? throw new ArgumentNullException(nameof(errorEventEmitter));
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
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
            await _errorEventEmitter.TryPublishAsync(
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

            // Map request options to service-layer StateOptions
            Services.StateOptions? options = null;
            if (body.Options != null)
            {
                options = new Services.StateOptions
                {
                    Ttl = body.Options.Ttl.HasValue ? TimeSpan.FromSeconds(body.Options.Ttl.Value) : null,
                    Etag = body.Options.Etag,
                    Consistency = body.Options.Consistency == StateOptionsConsistency.Eventual
                        ? Services.StateConsistency.Eventual
                        : Services.StateConsistency.Strong
                };
            }

            // If ETag is provided, use optimistic concurrency
            if (!string.IsNullOrEmpty(body.Options?.Etag))
            {
                var success = await store.TrySaveAsync(body.Key, body.Value, body.Options.Etag, cancellationToken);
                if (!success)
                {
                    _logger.LogDebug("ETag mismatch for key {Key} in store {StoreName}", body.Key, body.StoreName);
                    return (StatusCodes.Conflict, new SaveStateResponse { Success = false });
                }

                return (StatusCodes.OK, new SaveStateResponse { Success = true });
            }

            // Standard save
            var newEtag = await store.SaveAsync(body.Key, body.Value, options, cancellationToken);

            _logger.LogDebug("Saved state to store {StoreName} with key {Key}", body.StoreName, body.Key);
            return (StatusCodes.OK, new SaveStateResponse
            {
                Success = true,
                Etag = newEtag
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save state to store {StoreName} with key {Key}", body.StoreName, body.Key);
            await _errorEventEmitter.TryPublishAsync(
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
            await _errorEventEmitter.TryPublishAsync(
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

            // Check if store supports queries (MySQL only)
            var backend = _stateStoreFactory.GetBackendType(body.StoreName);
            if (backend != Services.StateBackend.MySql)
            {
                _logger.LogWarning("Query not supported for backend {Backend} on store {StoreName}", backend, body.StoreName);
                return (StatusCodes.BadRequest, null);
            }

            // QueryState requires LINQ expression building from JSON filter
            // This is a complex feature that requires runtime expression compilation
            // For now, return NotImplemented as the JSON filter → LINQ translation is non-trivial
            _logger.LogWarning("QueryState JSON filter parsing not yet implemented");
            return (StatusCodes.InternalServerError, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query state from store {StoreName}", body.StoreName);
            await _errorEventEmitter.TryPublishAsync(
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
            await _errorEventEmitter.TryPublishAsync(
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
                    ? Services.StateBackend.Redis
                    : Services.StateBackend.MySql;
                storeNames = _stateStoreFactory.GetStoreNames(backend);
            }
            else
            {
                storeNames = _stateStoreFactory.GetStoreNames();
            }

            // Build store info list
            var stores = storeNames.Select(name =>
            {
                var backend = _stateStoreFactory.GetBackendType(name);
                return new StoreInfo
                {
                    Name = name,
                    Backend = backend == Services.StateBackend.Redis
                        ? StoreInfoBackend.Redis
                        : StoreInfoBackend.Mysql,
                    KeyCount = null // Key counts require backend-specific queries, skip for now
                };
            }).ToList();

            _logger.LogDebug("Listed {Count} state stores", stores.Count);
            return (StatusCodes.OK, new ListStoresResponse { Stores = stores });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list state stores");
            await _errorEventEmitter.TryPublishAsync(
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
}
