using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Location;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Worldstate;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Realm;

/// <summary>
/// Implementation of the Realm service.
/// Manages realm definitions - top-level persistent worlds (e.g., REALM_1, REALM_2).
/// Each realm operates as an independent peer with distinct characteristics.
/// </summary>
[BannouService("realm", typeof(IRealmService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFoundation)]
public partial class RealmService : IRealmService, IDeprecateAndMergeEntity
{
    private readonly IStateStore<RealmModel> _realmStore;
    private readonly IStateStore<string> _codeIndexStore;
    private readonly IStateStore<List<Guid>> _realmListStore;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<RealmService> _logger;
    private readonly RealmServiceConfiguration _configuration;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly IResourceClient _resourceClient;
    private readonly ILocationClient _locationClient;
    private readonly IWorldstateClient _worldstateClient;

    private const string REALM_KEY_PREFIX = "realm:";
    private const string CODE_INDEX_PREFIX = "code-index:";
    private const string ALL_REALMS_KEY = "all-realms";

    public RealmService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        ILogger<RealmService> logger,
        RealmServiceConfiguration configuration,
        IEventConsumer eventConsumer,
        IDistributedLockProvider lockProvider,
        ITelemetryProvider telemetryProvider,
        IResourceClient resourceClient,
        ILocationClient locationClient,
        IWorldstateClient worldstateClient)
    {
        _realmStore = stateStoreFactory.GetStore<RealmModel>(StateStoreDefinitions.Realm);
        _codeIndexStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.Realm);
        _realmListStore = stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Realm);
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        _lockProvider = lockProvider;
        _telemetryProvider = telemetryProvider;
        _resourceClient = resourceClient;
        _locationClient = locationClient;
        _worldstateClient = worldstateClient;

        // Register event handlers via partial class (RealmServiceEvents.cs)
        ((IBannouService)this).RegisterEventConsumers(eventConsumer);
    }

    #region Key Building Helpers

    internal static string BuildRealmKey(Guid realmId) => $"{REALM_KEY_PREFIX}{realmId}";
    internal static string BuildCodeIndexKey(string code) => $"{CODE_INDEX_PREFIX}{code.ToUpperInvariant()}";

    #endregion

    #region Read Operations

    /// <summary>
    /// Get realm by ID.
    /// </summary>
    public async Task<(StatusCodes, RealmResponse?)> GetRealmAsync(
        GetRealmRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting realm by ID: {RealmId}", body.RealmId);

        var realmKey = BuildRealmKey(body.RealmId);
        var model = await _realmStore.GetAsync(realmKey, cancellationToken);

        if (model == null)
        {
            _logger.LogDebug("Realm not found: {RealmId}", body.RealmId);
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, MapToResponse(model));
    }

    /// <summary>
    /// Get realm by unique code (e.g., "REALM_1", "REALM_2").
    /// </summary>
    public async Task<(StatusCodes, RealmResponse?)> GetRealmByCodeAsync(
        GetRealmByCodeRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting realm by code: {Code}", body.Code);

        var codeIndexKey = BuildCodeIndexKey(body.Code);
        var realmIdStr = await _codeIndexStore.GetAsync(codeIndexKey, cancellationToken);

        if (string.IsNullOrEmpty(realmIdStr) || !Guid.TryParse(realmIdStr, out var realmId))
        {
            _logger.LogDebug("Realm not found by code: {Code}", body.Code);
            return (StatusCodes.NotFound, null);
        }

        var realmKey = BuildRealmKey(realmId);
        var model = await _realmStore.GetAsync(realmKey, cancellationToken);

        if (model == null)
        {
            _logger.LogWarning("Realm data inconsistency - code index exists but realm not found: {Code}", body.Code);
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, MapToResponse(model));
    }

    /// <summary>
    /// List all realms with optional filtering.
    /// </summary>
    public async Task<(StatusCodes, RealmListResponse?)> ListRealmsAsync(
        ListRealmsRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Listing realms with filters - Category: {Category}, IsActive: {IsActive}, IncludeDeprecated: {IncludeDeprecated}",
            body.Category, body.IsActive, body.IncludeDeprecated);

        var allRealmIds = await _realmListStore.GetAsync(ALL_REALMS_KEY, cancellationToken);

        if (allRealmIds == null || allRealmIds.Count == 0)
        {
            return (StatusCodes.OK, new RealmListResponse
            {
                Realms = new List<RealmResponse>(),
                TotalCount = 0,
                Page = body.Page,
                PageSize = body.PageSize
            });
        }

        var realmList = await LoadRealmsByIdsAsync(allRealmIds, cancellationToken);

        // Apply filters
        var filtered = realmList.AsEnumerable();

        // Filter out deprecated unless explicitly included
        if (!body.IncludeDeprecated)
        {
            filtered = filtered.Where(r => !r.IsDeprecated);
        }

        if (!string.IsNullOrEmpty(body.Category))
        {
            filtered = filtered.Where(r => string.Equals(r.Category, body.Category, StringComparison.OrdinalIgnoreCase));
        }

        if (body.IsActive.HasValue)
        {
            filtered = filtered.Where(r => r.IsActive == body.IsActive.Value);
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

        return (StatusCodes.OK, new RealmListResponse
        {
            Realms = pagedList,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            HasNextPage = page * pageSize < totalCount,
            HasPreviousPage = page > 1
        });
    }

    /// <summary>
    /// Fast validation endpoint to check if realm exists and is active.
    /// </summary>
    public async Task<(StatusCodes, RealmExistsResponse?)> RealmExistsAsync(
        RealmExistsRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Checking if realm exists: {RealmId}", body.RealmId);

        var realmKey = BuildRealmKey(body.RealmId);
        var model = await _realmStore.GetAsync(realmKey, cancellationToken);

        if (model == null)
        {
            return (StatusCodes.OK, new RealmExistsResponse
            {
                Exists = false,
                IsActive = false,
                RealmId = null
            });
        }

        return (StatusCodes.OK, new RealmExistsResponse
        {
            Exists = true,
            IsActive = model.IsActive && !model.IsDeprecated,
            RealmId = model.RealmId
        });
    }

    /// <summary>
    /// Batch validation endpoint to check if multiple realms exist and are active.
    /// Uses bulk state store operations for efficient validation.
    /// </summary>
    public async Task<(StatusCodes, RealmsExistBatchResponse?)> RealmsExistBatchAsync(
        RealmsExistBatchRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Checking existence of {Count} realms", body.RealmIds.Count);

        if (body.RealmIds.Count == 0)
        {
            return (StatusCodes.OK, new RealmsExistBatchResponse
            {
                Results = new List<RealmExistsResponse>(),
                AllExist = true,
                AllActive = true,
                InvalidRealmIds = new List<Guid>(),
                DeprecatedRealmIds = new List<Guid>()
            });
        }

        // Use bulk loading for efficiency (single state store call)
        var realmModels = await LoadRealmsByIdsAsync(body.RealmIds.ToList(), cancellationToken);
        var modelLookup = realmModels.ToDictionary(m => m.RealmId);

        var results = new List<RealmExistsResponse>();
        var invalidRealmIds = new List<Guid>();
        var deprecatedRealmIds = new List<Guid>();

        // Build results in same order as request
        foreach (var realmId in body.RealmIds)
        {
            if (modelLookup.TryGetValue(realmId, out var model))
            {
                var isActive = model.IsActive && !model.IsDeprecated;
                results.Add(new RealmExistsResponse
                {
                    Exists = true,
                    IsActive = isActive,
                    RealmId = model.RealmId
                });

                if (!isActive)
                {
                    deprecatedRealmIds.Add(realmId);
                }
            }
            else
            {
                results.Add(new RealmExistsResponse
                {
                    Exists = false,
                    IsActive = false,
                    RealmId = null
                });
                invalidRealmIds.Add(realmId);
            }
        }

        return (StatusCodes.OK, new RealmsExistBatchResponse
        {
            Results = results,
            AllExist = invalidRealmIds.Count == 0,
            AllActive = invalidRealmIds.Count == 0 && deprecatedRealmIds.Count == 0,
            InvalidRealmIds = invalidRealmIds,
            DeprecatedRealmIds = deprecatedRealmIds
        });
    }

    #endregion

    #region Write Operations

    /// <summary>
    /// Create a new realm.
    /// </summary>
    public async Task<(StatusCodes, RealmResponse?)> CreateRealmAsync(
        CreateRealmRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating realm with code: {Code}", body.Code);

        var code = body.Code.ToUpperInvariant();

        // Check if code already exists
        var codeIndexKey = BuildCodeIndexKey(code);
        var existingIdStr = await _codeIndexStore.GetAsync(codeIndexKey, cancellationToken);

        if (!string.IsNullOrEmpty(existingIdStr))
        {
            _logger.LogDebug("Realm with code already exists: {Code}", code);
            return (StatusCodes.Conflict, null);
        }

        var realmId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var model = new RealmModel
        {
            RealmId = realmId,
            Code = code,
            Name = body.Name,
            GameServiceId = body.GameServiceId,
            Description = body.Description,
            Category = body.Category,
            IsActive = body.IsActive,
            IsSystemType = body.IsSystemType,
            IsDeprecated = false,
            DeprecatedAt = null,
            DeprecationReason = null,
            Metadata = body.Metadata,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Save the model
        var realmKey = BuildRealmKey(realmId);
        await _realmStore.SaveAsync(realmKey, model, cancellationToken: cancellationToken);

        // Update code index (stored as string for state store compatibility)
        await _codeIndexStore.SaveAsync(codeIndexKey, realmId.ToString(), cancellationToken: cancellationToken);

        // Update all-realms list with ETag-based optimistic concurrency
        await AddToRealmListAsync(realmId, cancellationToken);

        // Publish realm created event
        await PublishRealmCreatedEventAsync(model, cancellationToken);

        // Optionally auto-initialize worldstate clock for the new realm
        if (_configuration.AutoInitializeWorldstateClock)
        {
            await TryInitializeWorldstateClockAsync(realmId, cancellationToken);
        }

        _logger.LogInformation("Created realm: {RealmId} with code {Code}", realmId, code);
        return (StatusCodes.OK, MapToResponse(model));
    }

    /// <summary>
    /// Update an existing realm.
    /// </summary>
    public async Task<(StatusCodes, RealmResponse?)> UpdateRealmAsync(
        UpdateRealmRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Updating realm: {RealmId}", body.RealmId);

        var realmKey = BuildRealmKey(body.RealmId);
        var changedFields = new List<string>();

        var (result, entity, errorStatus) = await _realmStore.UpdateWithRetryAsync(
            realmKey,
            async model =>
            {
                changedFields.Clear();

                if (body.Name != null && body.Name != model.Name)
                {
                    model.Name = body.Name;
                    changedFields.Add("name");
                }
                if (body.Description != null && body.Description != model.Description)
                {
                    model.Description = body.Description;
                    changedFields.Add("description");
                }
                if (body.Category != null && body.Category != model.Category)
                {
                    model.Category = body.Category;
                    changedFields.Add("category");
                }
                if (body.IsActive.HasValue && body.IsActive.Value != model.IsActive)
                {
                    model.IsActive = body.IsActive.Value;
                    changedFields.Add("isActive");
                }
                if (body.IsSystemType.HasValue && body.IsSystemType.Value != model.IsSystemType)
                {
                    model.IsSystemType = body.IsSystemType.Value;
                    changedFields.Add("isSystemType");
                }
                if (body.GameServiceId.HasValue && body.GameServiceId.Value != model.GameServiceId)
                {
                    model.GameServiceId = body.GameServiceId.Value;
                    changedFields.Add("gameServiceId");
                }
                if (body.Metadata != null)
                {
                    model.Metadata = body.Metadata;
                    changedFields.Add("metadata");
                }

                if (changedFields.Count == 0)
                {
                    return MutationResult.SkipWith(StatusCodes.OK);
                }

                model.UpdatedAt = DateTimeOffset.UtcNow;
                await Task.CompletedTask;
                return MutationResult.Mutated;
            },
            _configuration.OptimisticRetryAttempts,
            _logger,
            cancellationToken);

        switch (result)
        {
            case UpdateResult.NotFound:
                _logger.LogDebug("Realm not found for update: {RealmId}", body.RealmId);
                return (StatusCodes.NotFound, null);

            case UpdateResult.ValidationFailed when errorStatus == StatusCodes.OK:
                // No fields changed — return current state as success
                var unchanged = await _realmStore.GetAsync(realmKey, cancellationToken);
                _logger.LogInformation("Updated realm: {RealmId} (no changes)", body.RealmId);
                return (StatusCodes.OK, unchanged != null ? MapToResponse(unchanged) : null);

            case UpdateResult.Success:
                await PublishRealmUpdatedEventAsync(entity!, changedFields, cancellationToken);
                _logger.LogInformation("Updated realm: {RealmId}", body.RealmId);
                return (StatusCodes.OK, MapToResponse(entity!));

            case UpdateResult.Conflict:
                _logger.LogWarning("Failed to update realm {RealmId} after {Attempts} attempts due to concurrent modifications",
                    body.RealmId, _configuration.OptimisticRetryAttempts);
                return (StatusCodes.Conflict, null);

            default:
                return (StatusCodes.Conflict, null);
        }
    }

    /// <summary>
    /// Hard delete a realm. Only deprecated realms with zero references can be deleted.
    /// </summary>
    public async Task<StatusCodes> DeleteRealmAsync(
        DeleteRealmRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Deleting realm: {RealmId}", body.RealmId);

        var realmKey = BuildRealmKey(body.RealmId);
        var model = await _realmStore.GetAsync(realmKey, cancellationToken);

        if (model == null)
        {
            _logger.LogDebug("Realm not found for deletion: {RealmId}", body.RealmId);
            return StatusCodes.NotFound;
        }

        // Realm should be deprecated before deletion (Category A per FOUNDATION TENETS)
        if (!model.IsDeprecated)
        {
            _logger.LogDebug("Cannot delete realm {Code}: realm must be deprecated first", model.Code);
            return StatusCodes.BadRequest;
        }

        // Check for external references via lib-resource (L1 - allowed per SERVICE_HIERARCHY)
        // L3/L4 services like RealmHistory register their realm references with lib-resource
        try
        {
            var resourceCheck = await _resourceClient.CheckReferencesAsync(
                new BeyondImmersion.BannouService.Resource.CheckReferencesRequest
                {
                    ResourceType = "realm",
                    ResourceId = body.RealmId
                }, cancellationToken);

            if (resourceCheck != null && resourceCheck.RefCount > 0)
            {
                var sourceTypes = resourceCheck.Sources != null
                    ? string.Join(", ", resourceCheck.Sources.Select(s => s.SourceType))
                    : "unknown";
                _logger.LogWarning(
                    "Cannot delete realm {RealmId} - has {RefCount} external references from: {SourceTypes}",
                    body.RealmId, resourceCheck.RefCount, sourceTypes);

                // Execute cleanup callbacks (CASCADE/DETACH) before proceeding
                var cleanupResult = await _resourceClient.ExecuteCleanupAsync(
                    new ExecuteCleanupRequest
                    {
                        ResourceType = "realm",
                        ResourceId = body.RealmId,
                        CleanupPolicy = CleanupPolicy.AllRequired
                    }, cancellationToken);

                if (!cleanupResult.Success)
                {
                    _logger.LogWarning(
                        "Cleanup blocked for realm {RealmId}: {Reason}",
                        body.RealmId, cleanupResult.AbortReason ?? "cleanup failed");
                    return StatusCodes.Conflict;
                }
            }
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            // No references registered - this is normal
            _logger.LogDebug("No lib-resource references found for realm {RealmId}", body.RealmId);
        }
        catch (ApiException ex)
        {
            // lib-resource unavailable - fail closed to protect referential integrity
            _logger.LogError(ex,
                "lib-resource unavailable when checking references for realm {RealmId}, blocking deletion for safety",
                body.RealmId);
            await _messageBus.TryPublishErrorAsync(
                "realm", "DeleteRealm", "resource_service_unavailable",
                $"lib-resource unavailable when checking references for realm {body.RealmId}",
                dependency: "resource", endpoint: "post:/realm/delete",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return StatusCodes.ServiceUnavailable;
        }

        // Delete the model
        await _realmStore.DeleteAsync(realmKey, cancellationToken);

        // Delete code index
        var codeIndexKey = BuildCodeIndexKey(model.Code);
        await _codeIndexStore.DeleteAsync(codeIndexKey, cancellationToken);

        // Remove from all-realms list with ETag-based optimistic concurrency
        await RemoveFromRealmListAsync(body.RealmId, cancellationToken);

        // Publish realm deleted event
        await PublishRealmDeletedEventAsync(model, null, cancellationToken);

        _logger.LogInformation("Deleted realm: {RealmId} ({Code})", body.RealmId, model.Code);
        return StatusCodes.OK;
    }

    #endregion

    #region Deprecation Operations

    /// <summary>
    /// Deprecate a realm (soft-delete).
    /// </summary>
    public async Task<(StatusCodes, RealmResponse?)> DeprecateRealmAsync(
        DeprecateRealmRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Deprecating realm: {RealmId}", body.RealmId);

        var realmKey = BuildRealmKey(body.RealmId);

        var (result, entity, errorStatus) = await _realmStore.UpdateWithRetryAsync(
            realmKey,
            async model =>
            {
                // Idempotent per FOUNDATION TENETS — caller's intent (deprecate) is already satisfied
                if (model.IsDeprecated)
                {
                    return MutationResult.SkipWith(StatusCodes.OK);
                }

                model.IsDeprecated = true;
                model.DeprecatedAt = DateTimeOffset.UtcNow;
                model.DeprecationReason = body.Reason;
                model.UpdatedAt = DateTimeOffset.UtcNow;

                await Task.CompletedTask;
                return MutationResult.Mutated;
            },
            _configuration.OptimisticRetryAttempts,
            _logger,
            cancellationToken);

        switch (result)
        {
            case UpdateResult.NotFound:
                _logger.LogDebug("Realm not found for deprecation: {RealmId}", body.RealmId);
                return (StatusCodes.NotFound, null);

            case UpdateResult.ValidationFailed when errorStatus == StatusCodes.OK:
                // Already deprecated — idempotent success
                var existing = await _realmStore.GetAsync(realmKey, cancellationToken);
                _logger.LogDebug("Realm {RealmId} already deprecated, returning OK (idempotent)", body.RealmId);
                return (StatusCodes.OK, existing != null ? MapToResponse(existing) : null);

            case UpdateResult.Success:
                await PublishRealmUpdatedEventAsync(entity!, new[] { "isDeprecated", "deprecatedAt", "deprecationReason" }, cancellationToken);
                _logger.LogInformation("Deprecated realm: {RealmId}", body.RealmId);
                return (StatusCodes.OK, MapToResponse(entity!));

            case UpdateResult.Conflict:
                _logger.LogWarning("Failed to deprecate realm {RealmId} after {Attempts} attempts due to concurrent modifications",
                    body.RealmId, _configuration.OptimisticRetryAttempts);
                return (StatusCodes.Conflict, null);

            default:
                return (StatusCodes.Conflict, null);
        }
    }

    /// <summary>
    /// Restore a deprecated realm.
    /// </summary>
    public async Task<(StatusCodes, RealmResponse?)> UndeprecateRealmAsync(
        UndeprecateRealmRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Undeprecating realm: {RealmId}", body.RealmId);

        var realmKey = BuildRealmKey(body.RealmId);

        var (result, entity, errorStatus) = await _realmStore.UpdateWithRetryAsync(
            realmKey,
            async model =>
            {
                // Idempotent per FOUNDATION TENETS — caller's intent (undeprecate) is already satisfied
                if (!model.IsDeprecated)
                {
                    return MutationResult.SkipWith(StatusCodes.OK);
                }

                model.IsDeprecated = false;
                model.DeprecatedAt = null;
                model.DeprecationReason = null;
                model.UpdatedAt = DateTimeOffset.UtcNow;

                await Task.CompletedTask;
                return MutationResult.Mutated;
            },
            _configuration.OptimisticRetryAttempts,
            _logger,
            cancellationToken);

        switch (result)
        {
            case UpdateResult.NotFound:
                _logger.LogDebug("Realm not found for undeprecation: {RealmId}", body.RealmId);
                return (StatusCodes.NotFound, null);

            case UpdateResult.ValidationFailed when errorStatus == StatusCodes.OK:
                // Not deprecated — idempotent success
                var existing = await _realmStore.GetAsync(realmKey, cancellationToken);
                _logger.LogDebug("Realm {RealmId} not deprecated, returning OK (idempotent)", body.RealmId);
                return (StatusCodes.OK, existing != null ? MapToResponse(existing) : null);

            case UpdateResult.Success:
                await PublishRealmUpdatedEventAsync(entity!, new[] { "isDeprecated", "deprecatedAt", "deprecationReason" }, cancellationToken);
                _logger.LogInformation("Undeprecated realm: {RealmId}", body.RealmId);
                return (StatusCodes.OK, MapToResponse(entity!));

            case UpdateResult.Conflict:
                _logger.LogWarning("Failed to undeprecate realm {RealmId} after {Attempts} attempts due to concurrent modifications",
                    body.RealmId, _configuration.OptimisticRetryAttempts);
                return (StatusCodes.Conflict, null);

            default:
                return (StatusCodes.Conflict, null);
        }
    }

    #endregion

    #region Merge Operation

    /// <summary>
    /// Merge a deprecated source realm into a target realm by migrating all entities.
    /// Migration order: Species → Locations (root-first tree moves) → Characters.
    /// Follows continue-on-individual-failure policy per design decisions.
    /// </summary>
    public async Task<(StatusCodes, MergeRealmsResponse?)> MergeRealmsAsync(
        MergeRealmsRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting realm merge: source {SourceRealmId} into target {TargetRealmId}",
            body.SourceRealmId, body.TargetRealmId);

        // Validate source != target
        if (body.SourceRealmId == body.TargetRealmId)
        {
            _logger.LogWarning("Cannot merge realm into itself: {RealmId}", body.SourceRealmId);
            return (StatusCodes.BadRequest, null);
        }

        // Acquire distributed lock for the merge operation to prevent concurrent merges
        // involving the same realms. Lock key uses deterministic ordering to prevent deadlocks.
        var lockKey = string.Compare(
            body.SourceRealmId.ToString(), body.TargetRealmId.ToString(), StringComparison.Ordinal) < 0
            ? $"merge:{body.SourceRealmId}:{body.TargetRealmId}"
            : $"merge:{body.TargetRealmId}:{body.SourceRealmId}";

        await using var mergeLock = await _lockProvider.LockAsync(
            StateStoreDefinitions.RealmLock, lockKey, Guid.NewGuid().ToString(),
            _configuration.MergeLockTimeoutSeconds, cancellationToken);

        if (!mergeLock.Success)
        {
            _logger.LogWarning("Failed to acquire merge lock for source {SourceRealmId} and target {TargetRealmId}",
                body.SourceRealmId, body.TargetRealmId);
            return (StatusCodes.Conflict, null);
        }

        // Load source realm
        var sourceKey = BuildRealmKey(body.SourceRealmId);
        var sourceRealm = await _realmStore
            .GetAsync(sourceKey, cancellationToken);

        if (sourceRealm == null)
        {
            _logger.LogWarning("Source realm not found for merge: {SourceRealmId}", body.SourceRealmId);
            return (StatusCodes.NotFound, null);
        }

        // Source must be deprecated
        if (!sourceRealm.IsDeprecated)
        {
            _logger.LogWarning("Source realm {SourceRealmId} must be deprecated before merge", body.SourceRealmId);
            return (StatusCodes.BadRequest, null);
        }

        // Block merge FROM system realms (e.g., VOID)
        if (sourceRealm.IsSystemType)
        {
            _logger.LogWarning("Cannot merge from system realm {SourceRealmId} ({Code})",
                body.SourceRealmId, sourceRealm.Code);
            return (StatusCodes.BadRequest, null);
        }

        // Load target realm
        var targetKey = BuildRealmKey(body.TargetRealmId);
        var targetRealm = await _realmStore
            .GetAsync(targetKey, cancellationToken);

        if (targetRealm == null)
        {
            _logger.LogWarning("Target realm not found for merge: {TargetRealmId}", body.TargetRealmId);
            return (StatusCodes.NotFound, null);
        }

        // Warn when merging into a system realm (e.g., VOID) — entities will be orphaned from gameplay
        if (targetRealm.IsSystemType)
        {
            _logger.LogWarning(
                "Merging into system realm {TargetRealmId} ({Code}) - all migrated entities will be orphaned from gameplay",
                body.TargetRealmId, targetRealm.Code);
        }

        // Delegate migration to lib-resource — it calls registered migrate callbacks
        // (species, location, character each registered their own migrate-by-realm endpoints)
        ExecuteMigrateResponse migrateResult;
        try
        {
            migrateResult = await _resourceClient.ExecuteMigrateAsync(
                new ExecuteMigrateRequest
                {
                    ResourceType = "realm",
                    SourceResourceId = body.SourceRealmId,
                    TargetResourceId = body.TargetRealmId
                }, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Resource service unavailable during realm merge {Source} -> {Target}",
                body.SourceRealmId, body.TargetRealmId);
            return ((StatusCodes)ex.StatusCode, null);
        }

        var totalMigrated = migrateResult.CallbackResults.Where(r => r.Success).Count();
        var totalFailed = migrateResult.CallbackResults.Where(r => !r.Success).Count();

        // Publish realm.merged event
        await PublishRealmMergedEventAsync(
            sourceRealm, targetRealm,
            totalMigrated, totalFailed,
            cancellationToken);

        // Optional: delete source realm if requested and migration fully succeeded
        var sourceDeleted = false;
        if (body.DeleteAfterMerge && migrateResult.Success)
        {
            var deleteResult = await DeleteRealmAsync(
                new DeleteRealmRequest { RealmId = body.SourceRealmId }, cancellationToken);

            sourceDeleted = deleteResult == StatusCodes.OK;
            if (!sourceDeleted)
            {
                _logger.LogWarning(
                    "Post-merge deletion of source realm {SourceRealmId} returned {Status} (merge itself succeeded)",
                    body.SourceRealmId, deleteResult);
            }
        }
        else if (body.DeleteAfterMerge && !migrateResult.Success)
        {
            _logger.LogWarning(
                "Skipping post-merge deletion of source realm {SourceRealmId} due to migration failures: {Reason}",
                body.SourceRealmId, migrateResult.AbortReason);
        }

        _logger.LogInformation(
            "Realm merge complete: source {SourceRealmId} into target {TargetRealmId} - " +
            "callbacks succeeded: {Succeeded}, failed: {Failed}, deleted: {Deleted}",
            body.SourceRealmId, body.TargetRealmId,
            totalMigrated, totalFailed, sourceDeleted);

        return (StatusCodes.OK, new MergeRealmsResponse
        {
            TotalMigrated = totalMigrated,
            TotalFailed = totalFailed,
            SourceDeleted = sourceDeleted
        });
    }

    // Migration of species, locations, and characters is now handled by lib-resource
    // via registered migrate callbacks. Each service (species, location, character) owns
    // its own migration logic through /service/migrate-by-realm endpoints.

    #endregion

    #region Seed Operation

    /// <summary>
    /// Idempotent operation to seed realms from configuration.
    /// </summary>
    public async Task<(StatusCodes, SeedRealmsResponse?)> SeedRealmsAsync(
        SeedRealmsRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Seeding {Count} realms, updateExisting: {UpdateExisting}",
            body.Realms.Count, body.UpdateExisting);

        var created = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<string>();

        foreach (var seedRealm in body.Realms)
        {
            try
            {
                var code = seedRealm.Code.ToUpperInvariant();
                var codeIndexKey = BuildCodeIndexKey(code);
                var existingIdStr = await _codeIndexStore.GetAsync(codeIndexKey, cancellationToken);

                if (!string.IsNullOrEmpty(existingIdStr) && Guid.TryParse(existingIdStr, out var existingId))
                {
                    if (body.UpdateExisting == true)
                    {
                        // Update existing with ETag-based optimistic concurrency per IMPLEMENTATION TENETS
                        var realmKey = BuildRealmKey(existingId);
                        var seedUpdated = false;

                        for (var attempt = 0; attempt < _configuration.OptimisticRetryAttempts; attempt++)
                        {
                            // GetWithETagAsync returns non-null etag for existing records;
                            // coalesce satisfies compiler's nullable analysis (will never execute)
                            var (existingModel, etag) = await _realmStore.GetWithETagAsync(realmKey, cancellationToken);

                            if (existingModel == null)
                            {
                                break;
                            }

                            var changedFields = new List<string>();

                            if (existingModel.Name != seedRealm.Name)
                            {
                                existingModel.Name = seedRealm.Name;
                                changedFields.Add("name");
                            }
                            if (existingModel.GameServiceId != seedRealm.GameServiceId)
                            {
                                existingModel.GameServiceId = seedRealm.GameServiceId;
                                changedFields.Add("gameServiceId");
                            }
                            if (seedRealm.Description != null && existingModel.Description != seedRealm.Description)
                            {
                                existingModel.Description = seedRealm.Description;
                                changedFields.Add("description");
                            }
                            if (seedRealm.Category != null && existingModel.Category != seedRealm.Category)
                            {
                                existingModel.Category = seedRealm.Category;
                                changedFields.Add("category");
                            }
                            if (existingModel.IsActive != seedRealm.IsActive)
                            {
                                existingModel.IsActive = seedRealm.IsActive;
                                changedFields.Add("isActive");
                            }
                            if (existingModel.IsSystemType != seedRealm.IsSystemType)
                            {
                                existingModel.IsSystemType = seedRealm.IsSystemType;
                                changedFields.Add("isSystemType");
                            }
                            if (seedRealm.Metadata != null)
                            {
                                existingModel.Metadata = seedRealm.Metadata;
                                changedFields.Add("metadata");
                            }

                            if (changedFields.Count == 0)
                            {
                                seedUpdated = true;
                                break;
                            }

                            existingModel.UpdatedAt = DateTimeOffset.UtcNow;
                            var saved = await _realmStore.TrySaveAsync(realmKey, existingModel,
                                etag ?? string.Empty, cancellationToken: cancellationToken);

                            if (saved != null)
                            {
                                await PublishRealmUpdatedEventAsync(existingModel, changedFields, cancellationToken);
                                seedUpdated = true;
                                _logger.LogDebug("Updated existing realm: {Code} (changed: {ChangedFields})",
                                    code, string.Join(", ", changedFields));
                                break;
                            }

                            _logger.LogDebug("Seed update retry {Attempt} for realm {Code} due to ETag conflict",
                                attempt + 1, code);
                        }

                        if (seedUpdated)
                        {
                            updated++;
                        }
                        else
                        {
                            errors.Add($"Failed to update realm '{seedRealm.Code}' after {_configuration.OptimisticRetryAttempts} attempts (concurrent modification)");
                        }
                    }
                    else
                    {
                        skipped++;
                        _logger.LogDebug("Skipped existing realm: {Code}", code);
                    }
                }
                else
                {
                    // Create new
                    var createRequest = new CreateRealmRequest
                    {
                        Code = code,
                        Name = seedRealm.Name,
                        GameServiceId = seedRealm.GameServiceId,
                        Description = seedRealm.Description,
                        Category = seedRealm.Category,
                        IsActive = seedRealm.IsActive,
                        IsSystemType = seedRealm.IsSystemType,
                        Metadata = seedRealm.Metadata
                    };

                    var (status, _) = await CreateRealmAsync(createRequest, cancellationToken);

                    if (status == StatusCodes.OK)
                    {
                        created++;
                        _logger.LogDebug("Created new realm: {Code}", code);
                    }
                    else
                    {
                        errors.Add($"Failed to create realm {code}: {status}");
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Error processing realm {seedRealm.Code}: {ex.Message}");
                _logger.LogWarning(ex, "Error seeding realm: {Code}", seedRealm.Code);
            }
        }

        _logger.LogInformation("Seed complete - Created: {Created}, Updated: {Updated}, Skipped: {Skipped}, Errors: {Errors}",
            created, updated, skipped, errors.Count);

        return (StatusCodes.OK, new SeedRealmsResponse
        {
            Created = created,
            Updated = updated,
            Skipped = skipped,
            Errors = errors
        });
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Add a realm ID to the all-realms list index.
    /// Uses ETag-based optimistic concurrency per IMPLEMENTATION TENETS (Multi-Instance Safety).
    /// </summary>
    private async Task AddToRealmListAsync(Guid realmId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.realm", "RealmService.AddToRealmListAsync");
        for (var attempt = 0; attempt < _configuration.OptimisticRetryAttempts; attempt++)
        {
            var (realmIds, etag) = await _realmListStore.GetWithETagAsync(ALL_REALMS_KEY, cancellationToken);
            realmIds ??= new List<Guid>();

            if (realmIds.Contains(realmId))
            {
                return; // Already in list
            }

            realmIds.Add(realmId);
            // etag is null when list key doesn't exist yet; empty string signals
            // "create new" to TrySaveAsync (will never conflict on new entries)
            var result = await _realmListStore.TrySaveAsync(ALL_REALMS_KEY, realmIds, etag ?? string.Empty, cancellationToken: cancellationToken);
            if (result != null)
            {
                return;
            }

            _logger.LogDebug("Concurrent modification on realm list, retrying add (attempt {Attempt})", attempt + 1);
        }

        _logger.LogWarning("Failed to add realm {RealmId} to list after {Attempts} attempts", realmId, _configuration.OptimisticRetryAttempts);
    }

    /// <summary>
    /// Remove a realm ID from the all-realms list index.
    /// Uses ETag-based optimistic concurrency per IMPLEMENTATION TENETS (Multi-Instance Safety).
    /// </summary>
    private async Task RemoveFromRealmListAsync(Guid realmId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.realm", "RealmService.RemoveFromRealmListAsync");
        for (var attempt = 0; attempt < _configuration.OptimisticRetryAttempts; attempt++)
        {
            var (realmIds, etag) = await _realmListStore.GetWithETagAsync(ALL_REALMS_KEY, cancellationToken);
            if (realmIds == null || !realmIds.Remove(realmId))
            {
                return; // Not in list or already removed
            }

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var result = await _realmListStore.TrySaveAsync(ALL_REALMS_KEY, realmIds, etag ?? string.Empty, cancellationToken: cancellationToken);
            if (result != null)
            {
                return;
            }

            _logger.LogDebug("Concurrent modification on realm list, retrying remove (attempt {Attempt})", attempt + 1);
        }

        _logger.LogWarning("Failed to remove realm {RealmId} from list after {Attempts} attempts", realmId, _configuration.OptimisticRetryAttempts);
    }

    private async Task<List<RealmModel>> LoadRealmsByIdsAsync(List<Guid> realmIds, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.realm", "RealmService.LoadRealmsByIdsAsync");
        if (realmIds.Count == 0)
        {
            return new List<RealmModel>();
        }

        var keys = realmIds.Select(BuildRealmKey).ToList();
        var bulkResults = await _realmStore.GetBulkAsync(keys, cancellationToken);

        var realmList = new List<RealmModel>();
        foreach (var (_, model) in bulkResults)
        {
            if (model != null)
            {
                realmList.Add(model);
            }
        }

        return realmList;
    }

    private static RealmResponse MapToResponse(RealmModel model)
    {
        return new RealmResponse
        {
            RealmId = model.RealmId,
            Code = model.Code,
            Name = model.Name,
            GameServiceId = model.GameServiceId,
            Description = model.Description,
            Category = model.Category,
            IsActive = model.IsActive,
            IsSystemType = model.IsSystemType,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            Metadata = model.Metadata,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }

    /// <summary>
    /// Attempts to initialize a worldstate clock for a newly created realm.
    /// Logs a warning on failure but does not fail the realm creation -- the clock
    /// can be initialized manually later via the worldstate API.
    /// </summary>
    private async Task TryInitializeWorldstateClockAsync(Guid realmId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.realm", "RealmService.TryInitializeWorldstateClock");

        try
        {
            await _worldstateClient.InitializeRealmClockAsync(
                new InitializeRealmClockRequest
                {
                    RealmId = realmId,
                    CalendarTemplateCode = _configuration.DefaultCalendarTemplateCode
                },
                cancellationToken);

            _logger.LogInformation(
                "Auto-initialized worldstate clock for realm {RealmId} with calendar template {CalendarTemplateCode}",
                realmId, _configuration.DefaultCalendarTemplateCode);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex,
                "Failed to auto-initialize worldstate clock for realm {RealmId} (status {StatusCode}). Clock can be initialized manually via the worldstate API",
                realmId, ex.StatusCode);
        }
    }

    #endregion

    #region Event Publishing

    /// <summary>
    /// Publishes a realm created event.
    /// </summary>
    private async Task PublishRealmCreatedEventAsync(RealmModel model, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.realm", "RealmService.PublishRealmCreatedEventAsync");
        var eventModel = new RealmCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RealmId = model.RealmId,
            Code = model.Code,
            Name = model.Name,
            GameServiceId = model.GameServiceId,
            Description = model.Description,
            Category = model.Category,
            IsActive = model.IsActive,
            IsSystemType = model.IsSystemType,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            Metadata = model.Metadata,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };

        await _messageBus.PublishRealmCreatedAsync(eventModel, cancellationToken);
        _logger.LogDebug("Published realm.created event for {RealmId}", model.RealmId);
    }

    /// <summary>
    /// Publishes a realm updated event with current state and changed fields.
    /// </summary>
    private async Task PublishRealmUpdatedEventAsync(RealmModel model, IEnumerable<string> changedFields, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.realm", "RealmService.PublishRealmUpdatedEventAsync");
        var eventModel = new RealmUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RealmId = model.RealmId,
            Code = model.Code,
            Name = model.Name,
            GameServiceId = model.GameServiceId,
            Description = model.Description,
            Category = model.Category,
            IsActive = model.IsActive,
            IsSystemType = model.IsSystemType,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            Metadata = model.Metadata,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt,
            ChangedFields = changedFields.ToList()
        };

        await _messageBus.PublishRealmUpdatedAsync(eventModel, cancellationToken);
        _logger.LogDebug("Published realm.updated event for {RealmId} with changed fields: {ChangedFields}",
            model.RealmId, string.Join(", ", changedFields));
    }

    /// <summary>
    /// Publishes a realm merged event with migration statistics.
    /// </summary>
    private async Task PublishRealmMergedEventAsync(
        RealmModel sourceRealm,
        RealmModel targetRealm,
        int totalMigrated,
        int totalFailed,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.realm", "RealmService.PublishRealmMergedEventAsync");
        var eventModel = new RealmMergedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SourceRealmId = sourceRealm.RealmId,
            SourceRealmCode = sourceRealm.Code,
            TargetRealmId = targetRealm.RealmId,
            TargetRealmCode = targetRealm.Code,
            TotalMigrated = totalMigrated,
            TotalFailed = totalFailed
        };

        await _messageBus.PublishRealmMergedAsync(eventModel, cancellationToken);
        _logger.LogDebug("Published realm.merged event for source {SourceRealmId} into target {TargetRealmId}",
            sourceRealm.RealmId, targetRealm.RealmId);
    }

    /// <summary>
    /// Publishes a realm deleted event with final state before deletion.
    /// </summary>
    private async Task PublishRealmDeletedEventAsync(RealmModel model, string? deletedReason, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.realm", "RealmService.PublishRealmDeletedEventAsync");

        var eventModel = new RealmDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RealmId = model.RealmId,
            Code = model.Code,
            Name = model.Name,
            GameServiceId = model.GameServiceId,
            Description = model.Description,
            Category = model.Category,
            IsActive = model.IsActive,
            IsSystemType = model.IsSystemType,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            Metadata = model.Metadata,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt,
            DeletedReason = deletedReason
        };

        await _messageBus.PublishRealmDeletedAsync(eventModel, cancellationToken);
        _logger.LogDebug("Published realm.deleted event for {RealmId}", model.RealmId);
    }

    #endregion

    #region Permission Registration

    #endregion

    #region Compression

    /// <inheritdoc />
    public async Task<(StatusCodes, RealmLocationArchiveContext?)> GetLocationCompressContextAsync(
        GetLocationCompressContextRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting realm context for location archive: {LocationId}", body.LocationId);

        // Resolve the location to get its realmId
        LocationResponse locationResponse;
        try
        {
            locationResponse = await _locationClient.GetLocationAsync(
                new GetLocationRequest { LocationId = body.LocationId },
                cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("Location not found for compression context: {LocationId}", body.LocationId);
            return (StatusCodes.NotFound, null);
        }

        // Load the realm
        var realmKey = BuildRealmKey(locationResponse.RealmId);
        var model = await _realmStore.GetAsync(realmKey, cancellationToken);

        if (model == null)
        {
            _logger.LogDebug("Realm not found for location {LocationId}: {RealmId}", body.LocationId, locationResponse.RealmId);
            return (StatusCodes.NotFound, null);
        }

        var context = new RealmLocationArchiveContext
        {
            ResourceId = body.LocationId,
            ResourceType = "location",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            RealmId = model.RealmId,
            RealmName = model.Name,
            RealmCode = model.Code,
            RealmDescription = model.Description
        };

        _logger.LogDebug("Generated realm context for location archive: {LocationId}, realm {RealmCode}", body.LocationId, model.Code);
        return (StatusCodes.OK, context);
    }

    #endregion
}
