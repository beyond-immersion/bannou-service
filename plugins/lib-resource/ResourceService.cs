using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
namespace BeyondImmersion.BannouService.Resource;

/// <summary>
/// Implementation of the Resource lifecycle management service.
/// Tracks references from higher-layer services (L3/L4) to foundational resources (L2)
/// and coordinates cleanup callbacks when resources are deleted.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
/// Generated code (event handlers, permissions) is placed in companion partial classes.
/// </para>
/// <para>
/// <b>Key Design Principle:</b> lib-resource (L1) uses opaque string identifiers for
/// resourceType and sourceType. It does NOT enumerate or validate these against any
/// service registry - that would create implicit coupling to higher layers.
/// </para>
/// <para>
/// <b>RELATED FILES:</b>
/// <list type="bullet">
///   <item>Internal data models: ResourceServiceModels.cs (storage models, cache entries, internal DTOs)</item>
///   <item>Event handlers: ResourceServiceEvents.cs (event consumer registration and handlers)</item>
///   <item>Request/Response models: bannou-service/Generated/Models/ResourceModels.cs</item>
///   <item>Event models: bannou-service/Generated/Events/ResourceEventsModels.cs</item>
///   <item>Configuration: Generated/ResourceServiceConfiguration.cs</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("resource", typeof(IResourceService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.AppFoundation)]
public partial class ResourceService : IResourceService
{
    private readonly IMessageBus _messageBus;
    private readonly IServiceNavigator _navigator;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ILogger<ResourceService> _logger;
    private readonly ResourceServiceConfiguration _configuration;
    private readonly IEnumerable<ISeededResourceProvider> _seededProviders;
    private readonly ITelemetryProvider _telemetryProvider;

    // State stores - using constants from StateStoreDefinitions
    private readonly ICacheableStateStore<ResourceReferenceEntry> _refStore;
    private readonly IStateStore<CleanupCallbackDefinition> _cleanupStore;
    private readonly IStateStore<GracePeriodRecord> _graceStore;
    private readonly IStateStore<CompressCallbackDefinition> _compressStore;
    private readonly IStateStore<ResourceArchiveModel> _archiveStore;
    private readonly IStateStore<ResourceSnapshotModel> _snapshotStore;

    /// <summary>
    /// Cacheable store for cleanup callback index operations (set-based source type tracking).
    /// </summary>
    private readonly ICacheableStateStore<string> _cleanupCacheStore;

    /// <summary>
    /// Cacheable store for compression callback index operations (set-based source type tracking).
    /// </summary>
    private readonly ICacheableStateStore<string> _compressCacheStore;

    /// <summary>
    /// State store for migration callback definitions.
    /// </summary>
    private readonly IStateStore<MigrateCallbackDefinition> _migrateStore;

    /// <summary>
    /// Cacheable store for migration callback index operations (set-based source type tracking).
    /// </summary>
    private readonly ICacheableStateStore<string> _migrateIndexStore;

    /// <summary>
    /// State store for provisioning transaction records (MySQL, durable).
    /// </summary>
    private readonly IStateStore<ResourceTransactionModel> _transactionStore;

    /// <summary>
    /// State store for individual provisions within transactions (MySQL, durable).
    /// </summary>
    private readonly IStateStore<ResourceProvisionModel> _provisionStore;

    /// <summary>
    /// String store for provision index lists (transactionId → provisionId list).
    /// </summary>
    private readonly IStateStore<string> _provisionStringStore;

    /// <summary>
    /// Initializes a new instance of the ResourceService.
    /// </summary>
    public ResourceService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        IServiceNavigator navigator,
        IDistributedLockProvider lockProvider,
        ILogger<ResourceService> logger,
        ResourceServiceConfiguration configuration,
        IEventConsumer eventConsumer,
        IEnumerable<ISeededResourceProvider> seededProviders,
        ITelemetryProvider telemetryProvider)
    {
        _messageBus = messageBus;
        _navigator = navigator;
        _lockProvider = lockProvider;
        _logger = logger;
        _configuration = configuration;
        _seededProviders = seededProviders;
        _telemetryProvider = telemetryProvider;

        // Get state stores
        _refStore = stateStoreFactory.GetCacheableStore<ResourceReferenceEntry>(
            StateStoreDefinitions.ResourceRefcounts);
        _cleanupStore = stateStoreFactory.GetStore<CleanupCallbackDefinition>(
            StateStoreDefinitions.ResourceCleanup);
        _graceStore = stateStoreFactory.GetStore<GracePeriodRecord>(
            StateStoreDefinitions.ResourceGrace);
        _compressStore = stateStoreFactory.GetStore<CompressCallbackDefinition>(
            StateStoreDefinitions.ResourceCompress);
        _archiveStore = stateStoreFactory.GetStore<ResourceArchiveModel>(
            StateStoreDefinitions.ResourceArchives);
        _snapshotStore = stateStoreFactory.GetStore<ResourceSnapshotModel>(
            StateStoreDefinitions.ResourceSnapshots);
        _cleanupCacheStore = stateStoreFactory.GetCacheableStore<string>(
            StateStoreDefinitions.ResourceCleanup);
        _compressCacheStore = stateStoreFactory.GetCacheableStore<string>(
            StateStoreDefinitions.ResourceCompress);
        _migrateStore = stateStoreFactory.GetStore<MigrateCallbackDefinition>(
            StateStoreDefinitions.ResourceMigrate);
        _migrateIndexStore = stateStoreFactory.GetCacheableStore<string>(
            StateStoreDefinitions.ResourceMigrate);
        _transactionStore = stateStoreFactory.GetStore<ResourceTransactionModel>(
            StateStoreDefinitions.ResourceTransactions);
        _provisionStore = stateStoreFactory.GetStore<ResourceProvisionModel>(
            StateStoreDefinitions.ResourceProvisions);
        _provisionStringStore = stateStoreFactory.GetStore<string>(
            StateStoreDefinitions.ResourceProvisions);

        // Register event handlers via partial class (ResourceServiceEvents.cs)
        RegisterEventConsumers(eventConsumer);
    }

    // =========================================================================
    // Reference Management
    // =========================================================================

    /// <inheritdoc />
    public async Task<(StatusCodes, RegisterReferenceResponse?)> RegisterReferenceAsync(
        RegisterReferenceRequest body,
        CancellationToken cancellationToken = default)
    {
        var resourceKey = BuildResourceKey(body.ResourceType, body.ResourceId);
        var sourceEntry = new ResourceReferenceEntry
        {
            SourceType = body.SourceType,
            SourceId = body.SourceId,
            RegisteredAt = DateTimeOffset.UtcNow
        };

        // AddToSetAsync is atomic - returns true if item was newly added
        var added = await _refStore.AddToSetAsync(resourceKey, sourceEntry, cancellationToken: cancellationToken);

        if (added)
        {
            // Clear grace period since we now have references
            await _graceStore.DeleteAsync(
                BuildGraceKey(body.ResourceType, body.ResourceId),
                cancellationToken);

            _logger.LogDebug(
                "Registered reference: {SourceType}:{SourceId} -> {ResourceType}:{ResourceId}",
                body.SourceType, body.SourceId, body.ResourceType, body.ResourceId);
        }
        else
        {
            _logger.LogDebug(
                "Reference already registered: {SourceType}:{SourceId} -> {ResourceType}:{ResourceId}",
                body.SourceType, body.SourceId, body.ResourceType, body.ResourceId);
        }

        var refCount = await _refStore.SetCountAsync(resourceKey, cancellationToken);

        return (StatusCodes.OK, new RegisterReferenceResponse
        {
            ResourceType = body.ResourceType,
            ResourceId = body.ResourceId,
            NewRefCount = (int)refCount,
            AlreadyRegistered = !added
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, UnregisterReferenceResponse?)> UnregisterReferenceAsync(
        UnregisterReferenceRequest body,
        CancellationToken cancellationToken = default)
    {
        var resourceKey = BuildResourceKey(body.ResourceType, body.ResourceId);
        var sourceEntry = new ResourceReferenceEntry
        {
            SourceType = body.SourceType,
            SourceId = body.SourceId,
            RegisteredAt = default // Not used for removal matching
        };

        // RemoveFromSetAsync is atomic - returns true if item was removed
        var removed = await _refStore.RemoveFromSetAsync(resourceKey, sourceEntry, cancellationToken);
        var refCount = await _refStore.SetCountAsync(resourceKey, cancellationToken);

        DateTimeOffset? gracePeriodStartedAt = null;

        if (removed && refCount == 0)
        {
            // Record when refcount became zero
            var now = DateTimeOffset.UtcNow;
            gracePeriodStartedAt = now;

            var graceRecord = new GracePeriodRecord
            {
                ResourceType = body.ResourceType,
                ResourceId = body.ResourceId,
                ZeroTimestamp = now
            };

            await _graceStore.SaveAsync(
                BuildGraceKey(body.ResourceType, body.ResourceId),
                graceRecord,
                cancellationToken: cancellationToken);

            // Publish grace period started event
            var gracePeriodEndsAt = now.AddSeconds(_configuration.DefaultGracePeriodSeconds);
            await _messageBus.PublishResourceGracePeriodStartedAsync(
                new ResourceGracePeriodStartedEvent
                {
                    ResourceType = body.ResourceType,
                    ResourceId = body.ResourceId,
                    GracePeriodEndsAt = gracePeriodEndsAt,
                    Timestamp = now
                },
                cancellationToken);

            _logger.LogInformation(
                "Resource refcount reached zero, grace period started: {ResourceType}:{ResourceId}, ends at {GracePeriodEndsAt}",
                body.ResourceType, body.ResourceId, gracePeriodEndsAt);
        }

        if (removed)
        {
            _logger.LogDebug(
                "Unregistered reference: {SourceType}:{SourceId} -> {ResourceType}:{ResourceId}",
                body.SourceType, body.SourceId, body.ResourceType, body.ResourceId);
        }

        return (StatusCodes.OK, new UnregisterReferenceResponse
        {
            ResourceType = body.ResourceType,
            ResourceId = body.ResourceId,
            NewRefCount = (int)refCount,
            WasRegistered = removed,
            GracePeriodStartedAt = gracePeriodStartedAt
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, CheckReferencesResponse?)> CheckReferencesAsync(
        CheckReferencesRequest body,
        CancellationToken cancellationToken = default)
    {
        var resourceKey = BuildResourceKey(body.ResourceType, body.ResourceId);
        var refCount = await _refStore.SetCountAsync(resourceKey, cancellationToken);

        // Get sources for diagnostics
        var sourceEntries = await _refStore.GetSetAsync<ResourceReferenceEntry>(resourceKey, cancellationToken);
        var sources = sourceEntries.Select(e => new ResourceReference
        {
            SourceType = e.SourceType,
            SourceId = e.SourceId,
            RegisteredAt = e.RegisteredAt
        }).ToList();

        // Check grace period
        var graceRecord = await _graceStore.GetAsync(
            BuildGraceKey(body.ResourceType, body.ResourceId),
            cancellationToken);

        var isCleanupEligible = false;
        DateTimeOffset? gracePeriodEndsAt = null;
        DateTimeOffset? lastZeroTimestamp = null;

        if (refCount == 0 && graceRecord != null)
        {
            lastZeroTimestamp = graceRecord.ZeroTimestamp;
            gracePeriodEndsAt = graceRecord.ZeroTimestamp.AddSeconds(_configuration.DefaultGracePeriodSeconds);
            isCleanupEligible = DateTimeOffset.UtcNow >= gracePeriodEndsAt;

            // Clear gracePeriodEndsAt if already passed
            if (isCleanupEligible)
            {
                gracePeriodEndsAt = null;
            }
        }

        return (StatusCodes.OK, new CheckReferencesResponse
        {
            ResourceType = body.ResourceType,
            ResourceId = body.ResourceId,
            RefCount = (int)refCount,
            Sources = sources,
            IsCleanupEligible = isCleanupEligible,
            GracePeriodEndsAt = gracePeriodEndsAt,
            LastZeroTimestamp = lastZeroTimestamp
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ListReferencesResponse?)> ListReferencesAsync(
        ListReferencesRequest body,
        CancellationToken cancellationToken = default)
    {
        var resourceKey = BuildResourceKey(body.ResourceType, body.ResourceId);
        var sourceEntries = await _refStore.GetSetAsync<ResourceReferenceEntry>(resourceKey, cancellationToken);

        var references = sourceEntries
            .Where(e => string.IsNullOrEmpty(body.FilterSourceType) || e.SourceType == body.FilterSourceType)
            .Select(e => new ResourceReference
            {
                SourceType = e.SourceType,
                SourceId = e.SourceId,
                RegisteredAt = e.RegisteredAt
            })
            .Take(body.Limit)
            .ToList();

        var totalCount = string.IsNullOrEmpty(body.FilterSourceType)
            ? sourceEntries.Count
            : sourceEntries.Count(e => e.SourceType == body.FilterSourceType);

        return (StatusCodes.OK, new ListReferencesResponse
        {
            ResourceType = body.ResourceType,
            ResourceId = body.ResourceId,
            References = references,
            TotalCount = totalCount
        });
    }

    // =========================================================================
    // Cleanup Management
    // =========================================================================

    /// <inheritdoc />
    public async Task<(StatusCodes, DefineCleanupResponse?)> DefineCleanupCallbackAsync(
        DefineCleanupRequest body,
        CancellationToken cancellationToken = default)
    {
        var callbackKey = BuildCleanupKey(body.ResourceType, body.SourceType);

        // Check if already defined
        var existing = await _cleanupStore.GetAsync(callbackKey, cancellationToken);
        var previouslyDefined = existing != null;

        // ServiceName defaults to SourceType when not specified
        var serviceName = body.ServiceName ?? body.SourceType;

        var callback = new CleanupCallbackDefinition
        {
            ResourceType = body.ResourceType,
            SourceType = body.SourceType,
            OnDeleteAction = body.OnDeleteAction ?? OnDeleteAction.Cascade,
            ServiceName = serviceName,
            CallbackEndpoint = body.CallbackEndpoint,
            PayloadTemplate = body.PayloadTemplate,
            Description = body.Description,
            RegisteredAt = DateTimeOffset.UtcNow
        };

        await _cleanupStore.SaveAsync(callbackKey, callback, cancellationToken: cancellationToken);

        // Maintain the callback index for this resource type
        await MaintainCallbackIndexAsync(body.ResourceType, body.SourceType, cancellationToken);

        _logger.LogInformation(
            "Cleanup callback {Action} for {ResourceType}/{SourceType}: {ServiceName}{Endpoint}",
            previouslyDefined ? "updated" : "registered",
            body.ResourceType, body.SourceType, serviceName, body.CallbackEndpoint);

        return (StatusCodes.OK, new DefineCleanupResponse
        {
            ResourceType = body.ResourceType,
            SourceType = body.SourceType,
            Registered = true,
            PreviouslyDefined = previouslyDefined
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ExecuteCleanupResponse?)> ExecuteCleanupAsync(
        ExecuteCleanupRequest body,
        CancellationToken cancellationToken = default)
    {
        var resourceKey = BuildResourceKey(body.ResourceType, body.ResourceId);
        var stopwatch = Stopwatch.StartNew();

        // Get callbacks early to determine RESTRICT vs CASCADE/DETACH behavior
        var callbacks = await GetCleanupCallbacksAsync(body.ResourceType, cancellationToken);
        var restrictedSourceTypes = callbacks
            .Where(c => c.OnDeleteAction == OnDeleteAction.Restrict)
            .Select(c => c.SourceType)
            .ToHashSet();

        // Handle dry run - simulate full pre-check without executing callbacks
        if (body.DryRun == true)
        {
            var hasRestrict = callbacks.Any(c => c.OnDeleteAction == OnDeleteAction.Restrict);

            // Run the same pre-check that real execution does
            var (dryCheckStatus, dryCheckResult) = await CheckReferencesAsync(
                new CheckReferencesRequest
                {
                    ResourceType = body.ResourceType,
                    ResourceId = body.ResourceId
                },
                cancellationToken);

            string? dryAbortReason = null;
            var drySuccess = true;

            if (hasRestrict && dryCheckResult?.Sources?.Any(s => restrictedSourceTypes.Contains(s.SourceType)) == true)
            {
                dryAbortReason = "Would be blocked by RESTRICT policy (active references from restricted source types)";
                drySuccess = false;
            }
            else if (dryCheckResult?.RefCount > 0)
            {
                dryAbortReason = $"Resource still has {dryCheckResult.RefCount} active reference(s)";
                drySuccess = false;
            }
            else if (dryCheckResult?.IsCleanupEligible == false)
            {
                dryAbortReason = dryCheckResult.GracePeriodEndsAt.HasValue
                    ? $"Grace period has not passed (ends at {dryCheckResult.GracePeriodEndsAt.Value:O})"
                    : "Resource is not cleanup-eligible";
                drySuccess = false;
            }

            // Check for unhandled references (sources without registered callbacks)
            if (drySuccess && dryCheckResult?.Sources != null)
            {
                var handledSourceTypes = callbacks.Select(c => c.SourceType).ToHashSet();
                var unhandledRefs = dryCheckResult.Sources
                    .Where(s => !handledSourceTypes.Contains(s.SourceType))
                    .ToList();
                if (unhandledRefs.Count > 0)
                {
                    var unhandledTypes = string.Join(", ", unhandledRefs.Select(r => r.SourceType).Distinct());
                    dryAbortReason = $"Unhandled references from source types without cleanup callbacks: {unhandledTypes}";
                    drySuccess = false;
                }
            }

            stopwatch.Stop();
            return (StatusCodes.OK, new ExecuteCleanupResponse
            {
                ResourceType = body.ResourceType,
                ResourceId = body.ResourceId,
                Success = drySuccess,
                DryRun = true,
                AbortReason = dryAbortReason,
                CallbackResults = callbacks.Select(c => new CleanupCallbackResult
                {
                    SourceType = c.SourceType,
                    ServiceName = c.ServiceName,
                    Endpoint = c.CallbackEndpoint,
                    Success = true, // Hypothetical - not actually executed
                    DurationMs = 0
                }).ToList(),
                CleanupDurationMs = (int)stopwatch.ElapsedMilliseconds
            });
        }

        // First check without lock
        var (preCheckStatus, preCheckResult) = await CheckReferencesAsync(
            new CheckReferencesRequest
            {
                ResourceType = body.ResourceType,
                ResourceId = body.ResourceId
            },
            cancellationToken);

        if (preCheckResult == null)
        {
            return (StatusCodes.InternalServerError, null);
        }

        var currentSources = preCheckResult.Sources ?? new List<ResourceReference>();

        // Check for RESTRICT violations first - these always block
        var activeRestrictedRefs = currentSources
            .Where(s => restrictedSourceTypes.Contains(s.SourceType))
            .ToList();

        if (activeRestrictedRefs.Count > 0)
        {
            stopwatch.Stop();
            var blockers = string.Join(", ", activeRestrictedRefs
                .GroupBy(r => r.SourceType)
                .Select(g => $"{g.Key}:{g.Count()}"));

            _logger.LogInformation(
                "Cleanup blocked by RESTRICT policy: {ResourceType}:{ResourceId} has references: {Blockers}",
                body.ResourceType, body.ResourceId, blockers);

            return (StatusCodes.OK, new ExecuteCleanupResponse
            {
                ResourceType = body.ResourceType,
                ResourceId = body.ResourceId,
                Success = false,
                DryRun = false,
                AbortReason = $"Blocked by RESTRICT policy from: {blockers}",
                CallbackResults = new List<CleanupCallbackResult>(),
                CleanupDurationMs = (int)stopwatch.ElapsedMilliseconds
            });
        }

        // Check for non-RESTRICT active references (unexpected - should have been cleaned up)
        // These are from source types without registered callbacks, or callbacks not yet registered
        var unresolvedRefs = currentSources
            .Where(s => !restrictedSourceTypes.Contains(s.SourceType))
            .ToList();

        if (unresolvedRefs.Count > 0)
        {
            // Check if these refs have CASCADE/DETACH callbacks that will handle them
            var handledSourceTypes = callbacks
                .Where(c => c.OnDeleteAction != OnDeleteAction.Restrict)
                .Select(c => c.SourceType)
                .ToHashSet();

            var unhandledRefs = unresolvedRefs
                .Where(r => !handledSourceTypes.Contains(r.SourceType))
                .ToList();

            if (unhandledRefs.Count > 0)
            {
                stopwatch.Stop();
                return (StatusCodes.OK, new ExecuteCleanupResponse
                {
                    ResourceType = body.ResourceType,
                    ResourceId = body.ResourceId,
                    Success = false,
                    DryRun = false,
                    AbortReason = $"Resource has {unhandledRefs.Count} active reference(s) without registered cleanup callbacks",
                    CallbackResults = new List<CleanupCallbackResult>(),
                    CleanupDurationMs = (int)stopwatch.ElapsedMilliseconds
                });
            }
        }

        // Check grace period (can be overridden, 0 means skip grace period check)
        var gracePeriodSeconds = body.GracePeriodSeconds ?? _configuration.DefaultGracePeriodSeconds;

        if (!preCheckResult.IsCleanupEligible && gracePeriodSeconds > 0)
        {
            stopwatch.Stop();
            return (StatusCodes.OK, new ExecuteCleanupResponse
            {
                ResourceType = body.ResourceType,
                ResourceId = body.ResourceId,
                Success = false,
                DryRun = false,
                AbortReason = $"Grace period not yet passed (ends at {preCheckResult.GracePeriodEndsAt})",
                CallbackResults = new List<CleanupCallbackResult>(),
                CleanupDurationMs = (int)stopwatch.ElapsedMilliseconds
            });
        }

        // Acquire distributed lock
        var lockOwner = Guid.NewGuid().ToString();
        await using var lockResponse = await _lockProvider.LockAsync(
            storeName: StateStoreDefinitions.ResourceRefcounts,
            resourceId: $"cleanup:{body.ResourceType}:{body.ResourceId}",
            lockOwner: lockOwner,
            expiryInSeconds: _configuration.CleanupLockExpirySeconds,
            cancellationToken: cancellationToken);

        if (!lockResponse.Success)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "Failed to acquire cleanup lock for {ResourceType}:{ResourceId}",
                body.ResourceType, body.ResourceId);

            return (StatusCodes.OK, new ExecuteCleanupResponse
            {
                ResourceType = body.ResourceType,
                ResourceId = body.ResourceId,
                Success = false,
                DryRun = false,
                AbortReason = "Failed to acquire cleanup lock (another cleanup in progress?)",
                CallbackResults = new List<CleanupCallbackResult>(),
                CleanupDurationMs = (int)stopwatch.ElapsedMilliseconds
            });
        }

        // Re-validate under lock - check if any new RESTRICT references appeared
        var refCountUnderLock = await _refStore.SetCountAsync(resourceKey, cancellationToken);
        if (refCountUnderLock != preCheckResult.RefCount)
        {
            // Refcount changed - need to re-check RESTRICT violations
            var sourcesUnderLock = await _refStore.GetSetAsync<ResourceReferenceEntry>(resourceKey, cancellationToken);
            var newRestrictedRefs = sourcesUnderLock
                .Where(s => restrictedSourceTypes.Contains(s.SourceType))
                .ToList();

            if (newRestrictedRefs.Count > 0)
            {
                stopwatch.Stop();
                var blockers = string.Join(", ", newRestrictedRefs
                    .GroupBy(r => r.SourceType)
                    .Select(g => $"{g.Key}:{g.Count()}"));

                _logger.LogInformation(
                    "Cleanup blocked by RESTRICT policy (under lock): {ResourceType}:{ResourceId} has references: {Blockers}",
                    body.ResourceType, body.ResourceId, blockers);

                return (StatusCodes.OK, new ExecuteCleanupResponse
                {
                    ResourceType = body.ResourceType,
                    ResourceId = body.ResourceId,
                    Success = false,
                    DryRun = false,
                    AbortReason = $"Blocked by RESTRICT policy from: {blockers}",
                    CallbackResults = new List<CleanupCallbackResult>(),
                    CleanupDurationMs = (int)stopwatch.ElapsedMilliseconds
                });
            }
        }

        // Only execute CASCADE and DETACH callbacks (RESTRICT callbacks block, not execute)
        var executableCallbacks = callbacks
            .Where(c => c.OnDeleteAction != OnDeleteAction.Restrict)
            .ToList();

        var context = new Dictionary<string, object?>
        {
            ["resourceId"] = body.ResourceId.ToString(),
            ["resourceType"] = body.ResourceType
        };

        var callbackResults = new List<CleanupCallbackResult>();
        var cleanupPolicy = body.CleanupPolicy ?? _configuration.DefaultCleanupPolicy;

        if (executableCallbacks.Count > 0)
        {
            var apiDefinitions = executableCallbacks.Select(c => new PreboundApi
            {
                ServiceName = c.ServiceName,
                Endpoint = c.CallbackEndpoint,
                PayloadTemplate = c.PayloadTemplate,
                Description = c.Description
            }).ToList();

            // Apply configured timeout for cleanup callbacks
            // Note: Retry logic is handled by lib-mesh at the infrastructure level (MESH_MAX_RETRIES)
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_configuration.CleanupCallbackTimeoutSeconds));

            var results = await _navigator.ExecutePreboundApiBatchAsync(
                apiDefinitions,
                context,
                BatchExecutionMode.Parallel,
                timeoutCts.Token);

            for (var i = 0; i < results.Count; i++)
            {
                var result = results[i];
                var callback = executableCallbacks[i];

                var callbackResult = new CleanupCallbackResult
                {
                    SourceType = callback.SourceType,
                    ServiceName = callback.ServiceName,
                    Endpoint = callback.CallbackEndpoint,
                    Success = result.IsSuccess,
                    StatusCode = result.Result.StatusCode,
                    ErrorMessage = result.IsSuccess ? null : (result.SubstitutionError ?? result.Result.ErrorMessage),
                    DurationMs = (int)result.Result.Duration.TotalMilliseconds
                };

                callbackResults.Add(callbackResult);

                if (!result.IsSuccess)
                {
                    _logger.LogWarning(
                        "Cleanup callback failed: {ServiceName}{Endpoint} returned {StatusCode} for {ResourceType}:{ResourceId}",
                        callback.ServiceName, callback.CallbackEndpoint, result.Result.StatusCode,
                        body.ResourceType, body.ResourceId);

                    // Publish failure event for monitoring
                    await _messageBus.PublishResourceCleanupCallbackFailedAsync(
                        new ResourceCleanupCallbackFailedEvent
                        {
                            ResourceType = body.ResourceType,
                            ResourceId = body.ResourceId,
                            SourceType = callback.SourceType,
                            ServiceName = callback.ServiceName,
                            Endpoint = callback.CallbackEndpoint,
                            StatusCode = result.Result.StatusCode,
                            ErrorMessage = callbackResult.ErrorMessage,
                            Timestamp = DateTimeOffset.UtcNow
                        },
                        cancellationToken);
                }
            }
        }

        // Check if we should abort due to failures
        var failedCallbacks = callbackResults.Where(r => !r.Success).ToList();
        if (failedCallbacks.Count > 0 && cleanupPolicy == CleanupPolicy.AllRequired)
        {
            stopwatch.Stop();
            return (StatusCodes.OK, new ExecuteCleanupResponse
            {
                ResourceType = body.ResourceType,
                ResourceId = body.ResourceId,
                Success = false,
                DryRun = false,
                AbortReason = $"{failedCallbacks.Count} cleanup callback(s) failed with ALL_REQUIRED policy",
                CallbackResults = callbackResults,
                CleanupDurationMs = (int)stopwatch.ElapsedMilliseconds
            });
        }

        // Cleanup successful - remove grace period record
        await _graceStore.DeleteAsync(
            BuildGraceKey(body.ResourceType, body.ResourceId),
            cancellationToken);

        // Delete the reference set (should already be empty but clean up anyway)
        await _refStore.DeleteSetAsync(resourceKey, cancellationToken);

        stopwatch.Stop();
        _logger.LogInformation(
            "Cleanup completed for {ResourceType}:{ResourceId} with {CallbackCount} callback(s) in {DurationMs}ms",
            body.ResourceType, body.ResourceId, executableCallbacks.Count, stopwatch.ElapsedMilliseconds);

        return (StatusCodes.OK, new ExecuteCleanupResponse
        {
            ResourceType = body.ResourceType,
            ResourceId = body.ResourceId,
            Success = true,
            DryRun = false,
            CallbackResults = callbackResults,
            CleanupDurationMs = (int)stopwatch.ElapsedMilliseconds
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ListCleanupCallbacksResponse?)> ListCleanupCallbacksAsync(
        ListCleanupCallbacksRequest body,
        CancellationToken cancellationToken = default)
    {
        var callbacks = new List<CleanupCallbackSummary>();

        if (!string.IsNullOrEmpty(body.ResourceType))
        {
            // Get callbacks for specific resource type
            var resourceCallbacks = await GetCleanupCallbacksAsync(body.ResourceType, cancellationToken);

            // Optional filter by source type
            if (!string.IsNullOrEmpty(body.SourceType))
            {
                resourceCallbacks = resourceCallbacks
                    .Where(c => c.SourceType == body.SourceType)
                    .ToList();
            }

            callbacks.AddRange(resourceCallbacks.Select(MapToSummary));
        }
        else
        {
            // List all callbacks - use master resource type index
            var resourceTypes = await _cleanupCacheStore.GetSetAsync<string>(
                MasterResourceTypeIndexKey, cancellationToken);

            foreach (var resourceType in resourceTypes)
            {
                var resourceCallbacks = await GetCleanupCallbacksAsync(resourceType, cancellationToken);
                callbacks.AddRange(resourceCallbacks.Select(MapToSummary));
            }
        }

        _logger.LogDebug("Listed {Count} cleanup callbacks", callbacks.Count);

        return (StatusCodes.OK, new ListCleanupCallbacksResponse
        {
            Callbacks = callbacks,
            TotalCount = callbacks.Count
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, RemoveCleanupCallbackResponse?)> RemoveCleanupCallbackAsync(
        RemoveCleanupCallbackRequest body,
        CancellationToken cancellationToken = default)
    {
        var callbackKey = BuildCleanupKey(body.ResourceType, body.SourceType);
        var existing = await _cleanupStore.GetAsync(callbackKey, cancellationToken);

        if (existing == null)
        {
            _logger.LogDebug(
                "Cleanup callback not found for removal: {ResourceType}/{SourceType}",
                body.ResourceType, body.SourceType);

            return (StatusCodes.OK, new RemoveCleanupCallbackResponse
            {
                ResourceType = body.ResourceType,
                SourceType = body.SourceType,
                WasRegistered = false,
                RemovedAt = null
            });
        }

        // Delete callback
        await _cleanupStore.DeleteAsync(callbackKey, cancellationToken);

        // Remove from source type index
        var indexKey = $"{CLEANUP_INDEX_KEY_PREFIX}{body.ResourceType}";
        await _cleanupCacheStore.RemoveFromSetAsync(indexKey, body.SourceType, cancellationToken);

        // Check if resource type has any remaining callbacks
        var remainingSourceTypes = await _cleanupCacheStore.GetSetAsync<string>(indexKey, cancellationToken);
        if (remainingSourceTypes.Count == 0)
        {
            // Remove from master resource type index
            await _cleanupCacheStore.RemoveFromSetAsync(
                MasterResourceTypeIndexKey, body.ResourceType, cancellationToken);
        }

        _logger.LogInformation(
            "Removed cleanup callback: {ResourceType}/{SourceType}",
            body.ResourceType, body.SourceType);

        return (StatusCodes.OK, new RemoveCleanupCallbackResponse
        {
            ResourceType = body.ResourceType,
            SourceType = body.SourceType,
            WasRegistered = true,
            RemovedAt = DateTimeOffset.UtcNow
        });
    }

    // =========================================================================
    // Compression Management
    // =========================================================================

    /// <inheritdoc />
    public async Task<(StatusCodes, DefineCompressCallbackResponse?)> DefineCompressCallbackAsync(
        DefineCompressCallbackRequest body,
        CancellationToken cancellationToken = default)
    {
        var callbackKey = BuildCompressKey(body.ResourceType, body.SourceType);

        // Check if already defined
        var existing = await _compressStore.GetAsync(callbackKey, cancellationToken);
        var previouslyDefined = existing != null;

        // ServiceName defaults to SourceType when not specified
        var serviceName = body.ServiceName ?? body.SourceType;

        var callback = new CompressCallbackDefinition
        {
            ResourceType = body.ResourceType,
            SourceType = body.SourceType,
            ServiceName = serviceName,
            CompressEndpoint = body.CompressEndpoint,
            CompressPayloadTemplate = body.CompressPayloadTemplate,
            DecompressEndpoint = body.DecompressEndpoint,
            DecompressPayloadTemplate = body.DecompressPayloadTemplate,
            Priority = body.Priority,
            Description = body.Description,
            RegisteredAt = DateTimeOffset.UtcNow
        };

        await _compressStore.SaveAsync(callbackKey, callback, cancellationToken: cancellationToken);

        // Maintain the callback index for this resource type
        await MaintainCompressCallbackIndexAsync(body.ResourceType, body.SourceType, cancellationToken);

        _logger.LogInformation(
            "Compression callback {Action} for {ResourceType}/{SourceType}: {ServiceName}{Endpoint} (priority={Priority})",
            previouslyDefined ? "updated" : "registered",
            body.ResourceType, body.SourceType, serviceName, body.CompressEndpoint, body.Priority);

        return (StatusCodes.OK, new DefineCompressCallbackResponse
        {
            ResourceType = body.ResourceType,
            SourceType = body.SourceType,
            Registered = true,
            PreviouslyDefined = previouslyDefined
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ExecuteCompressResponse?)> ExecuteCompressAsync(
        ExecuteCompressRequest body,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // Get all compression callbacks for this resource type, sorted by priority
        var callbacks = await GetCompressCallbacksAsync(body.ResourceType, cancellationToken);

        if (callbacks.Count == 0)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "No compression callbacks registered for resource type {ResourceType}",
                body.ResourceType);

            return (StatusCodes.OK, new ExecuteCompressResponse
            {
                ResourceType = body.ResourceType,
                ResourceId = body.ResourceId,
                Success = false,
                DryRun = body.DryRun,
                AbortReason = "No compression callbacks registered for this resource type",
                CallbackResults = new List<CompressCallbackResult>(),
                SourceDataDeleted = false,
                CompressionDurationMs = (int)stopwatch.ElapsedMilliseconds
            });
        }

        // Handle dry run - return preview without executing
        if (body.DryRun)
        {
            stopwatch.Stop();
            return (StatusCodes.OK, new ExecuteCompressResponse
            {
                ResourceType = body.ResourceType,
                ResourceId = body.ResourceId,
                Success = true,
                DryRun = true,
                ArchiveId = null,
                CallbackResults = callbacks.Select(c => new CompressCallbackResult
                {
                    SourceType = c.SourceType,
                    ServiceName = c.ServiceName,
                    Endpoint = c.CompressEndpoint,
                    Success = true, // Hypothetical - not actually executed
                    DurationMs = 0
                }).ToList(),
                SourceDataDeleted = false,
                CompressionDurationMs = (int)stopwatch.ElapsedMilliseconds
            });
        }

        // Acquire distributed lock
        var lockOwner = Guid.NewGuid().ToString();
        await using var lockResponse = await _lockProvider.LockAsync(
            storeName: StateStoreDefinitions.ResourceCompress,
            resourceId: $"compress:{body.ResourceType}:{body.ResourceId}",
            lockOwner: lockOwner,
            expiryInSeconds: _configuration.CompressionLockExpirySeconds,
            cancellationToken: cancellationToken);

        if (!lockResponse.Success)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "Failed to acquire compression lock for {ResourceType}:{ResourceId}",
                body.ResourceType, body.ResourceId);

            return (StatusCodes.OK, new ExecuteCompressResponse
            {
                ResourceType = body.ResourceType,
                ResourceId = body.ResourceId,
                Success = false,
                DryRun = false,
                AbortReason = "Failed to acquire compression lock (another compression in progress?)",
                CallbackResults = new List<CompressCallbackResult>(),
                SourceDataDeleted = false,
                CompressionDurationMs = (int)stopwatch.ElapsedMilliseconds
            });
        }

        var compressionPolicy = body.CompressionPolicy ?? _configuration.DefaultCompressionPolicy;
        var callbackResults = new List<CompressCallbackResult>();
        var archiveEntries = new List<ArchiveEntryModel>();
        var context = new Dictionary<string, object?>
        {
            ["resourceId"] = body.ResourceId.ToString()
        };

        // Execute each callback in priority order
        foreach (var callback in callbacks)
        {
            var callbackStopwatch = Stopwatch.StartNew();

            try
            {
                var apiDefinition = new PreboundApi
                {
                    ServiceName = callback.ServiceName,
                    Endpoint = callback.CompressEndpoint,
                    PayloadTemplate = callback.CompressPayloadTemplate,
                    Description = callback.Description
                };

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(_configuration.CompressionCallbackTimeoutSeconds));

                var result = await _navigator.ExecutePreboundApiAsync(apiDefinition, context, timeoutCts.Token);

                callbackStopwatch.Stop();

                if (result.IsSuccess && result.Result.StatusCode >= 200 && result.Result.StatusCode < 300)
                {
                    // Get the response body and compress it
                    var responseJson = result.Result.ResponseBody ?? "{}";
                    var originalBytes = System.Text.Encoding.UTF8.GetBytes(responseJson);
                    var compressedData = CompressJsonData(responseJson);
                    var checksum = ComputeChecksum(originalBytes);

                    archiveEntries.Add(new ArchiveEntryModel
                    {
                        SourceType = callback.SourceType,
                        ServiceName = callback.ServiceName,
                        Data = compressedData,
                        CompressedAt = DateTimeOffset.UtcNow,
                        DataChecksum = checksum,
                        OriginalSizeBytes = originalBytes.Length
                    });

                    callbackResults.Add(new CompressCallbackResult
                    {
                        SourceType = callback.SourceType,
                        ServiceName = callback.ServiceName,
                        Endpoint = callback.CompressEndpoint,
                        Success = true,
                        StatusCode = result.Result.StatusCode,
                        DataSize = compressedData.Length,
                        DurationMs = (int)callbackStopwatch.ElapsedMilliseconds
                    });

                    _logger.LogDebug(
                        "Compression callback succeeded: {ServiceName}{Endpoint} for {ResourceType}:{ResourceId}, {OriginalBytes} bytes -> {CompressedBytes} bytes",
                        callback.ServiceName, callback.CompressEndpoint, body.ResourceType, body.ResourceId,
                        originalBytes.Length, compressedData.Length);
                }
                else
                {
                    // Callback failed
                    callbackResults.Add(new CompressCallbackResult
                    {
                        SourceType = callback.SourceType,
                        ServiceName = callback.ServiceName,
                        Endpoint = callback.CompressEndpoint,
                        Success = false,
                        StatusCode = result.Result.StatusCode,
                        ErrorMessage = result.SubstitutionError ?? result.Result.ErrorMessage,
                        DurationMs = (int)callbackStopwatch.ElapsedMilliseconds
                    });

                    _logger.LogWarning(
                        "Compression callback failed: {ServiceName}{Endpoint} returned {StatusCode} for {ResourceType}:{ResourceId}",
                        callback.ServiceName, callback.CompressEndpoint, result.Result.StatusCode,
                        body.ResourceType, body.ResourceId);

                    // Publish failure event
                    await _messageBus.PublishResourceCompressCallbackFailedAsync(
                        new ResourceCompressCallbackFailedEvent
                        {
                            EventId = Guid.NewGuid(),
                            Timestamp = DateTimeOffset.UtcNow,
                            ResourceType = body.ResourceType,
                            ResourceId = body.ResourceId,
                            SourceType = callback.SourceType,
                            ServiceName = callback.ServiceName,
                            Endpoint = callback.CompressEndpoint,
                            StatusCode = result.Result.StatusCode,
                            ErrorMessage = result.SubstitutionError ?? result.Result.ErrorMessage
                        },
                        cancellationToken);

                    // Abort if policy requires all callbacks
                    if (compressionPolicy == CompressionPolicy.AllRequired)
                    {
                        stopwatch.Stop();
                        return (StatusCodes.OK, new ExecuteCompressResponse
                        {
                            ResourceType = body.ResourceType,
                            ResourceId = body.ResourceId,
                            Success = false,
                            DryRun = false,
                            AbortReason = $"Compression callback failed for {callback.SourceType} with ALL_REQUIRED policy",
                            CallbackResults = callbackResults,
                            SourceDataDeleted = false,
                            CompressionDurationMs = (int)stopwatch.ElapsedMilliseconds
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                callbackStopwatch.Stop();

                _logger.LogError(ex,
                    "Exception in compression callback: {ServiceName}{Endpoint} for {ResourceType}:{ResourceId}",
                    callback.ServiceName, callback.CompressEndpoint, body.ResourceType, body.ResourceId);

                callbackResults.Add(new CompressCallbackResult
                {
                    SourceType = callback.SourceType,
                    ServiceName = callback.ServiceName,
                    Endpoint = callback.CompressEndpoint,
                    Success = false,
                    StatusCode = 0,
                    ErrorMessage = ex.Message,
                    DurationMs = (int)callbackStopwatch.ElapsedMilliseconds
                });

                // Publish failure event
                await _messageBus.PublishResourceCompressCallbackFailedAsync(
                    new ResourceCompressCallbackFailedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        ResourceType = body.ResourceType,
                        ResourceId = body.ResourceId,
                        SourceType = callback.SourceType,
                        ServiceName = callback.ServiceName,
                        Endpoint = callback.CompressEndpoint,
                        StatusCode = 0,
                        ErrorMessage = ex.Message
                    },
                    cancellationToken);

                if (compressionPolicy == CompressionPolicy.AllRequired)
                {
                    stopwatch.Stop();
                    return (StatusCodes.OK, new ExecuteCompressResponse
                    {
                        ResourceType = body.ResourceType,
                        ResourceId = body.ResourceId,
                        Success = false,
                        DryRun = false,
                        AbortReason = $"Compression callback exception for {callback.SourceType} with ALL_REQUIRED policy: {ex.Message}",
                        CallbackResults = callbackResults,
                        SourceDataDeleted = false,
                        CompressionDurationMs = (int)stopwatch.ElapsedMilliseconds
                    });
                }
            }
        }

        // Check if we have any entries (with BEST_EFFORT, we may have partial results)
        if (archiveEntries.Count == 0)
        {
            stopwatch.Stop();
            return (StatusCodes.OK, new ExecuteCompressResponse
            {
                ResourceType = body.ResourceType,
                ResourceId = body.ResourceId,
                Success = false,
                DryRun = false,
                AbortReason = "No successful compression callbacks - cannot create archive",
                CallbackResults = callbackResults,
                SourceDataDeleted = false,
                CompressionDurationMs = (int)stopwatch.ElapsedMilliseconds
            });
        }

        // Check for existing archive to determine version
        var archiveKey = BuildArchiveKey(body.ResourceType, body.ResourceId);
        var existingArchive = await _archiveStore.GetAsync(archiveKey, cancellationToken);
        var newVersion = (existingArchive?.Version ?? 0) + 1;

        // Create archive
        var archiveId = Guid.NewGuid();
        var archive = new ResourceArchiveModel
        {
            ArchiveId = archiveId,
            ResourceType = body.ResourceType,
            ResourceId = body.ResourceId,
            Version = newVersion,
            Entries = archiveEntries,
            CreatedAt = DateTimeOffset.UtcNow,
            SourceDataDeleted = false
        };

        await _archiveStore.SaveAsync(archiveKey, archive, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Created archive {ArchiveId} v{Version} for {ResourceType}:{ResourceId} with {EntryCount} entries",
            archiveId, newVersion, body.ResourceType, body.ResourceId, archiveEntries.Count);

        // Optionally delete source data via cleanup callbacks
        var sourceDataDeleted = false;
        if (body.DeleteSourceData)
        {
            var (cleanupStatus, cleanupResult) = await ExecuteCleanupAsync(
                new ExecuteCleanupRequest
                {
                    ResourceType = body.ResourceType,
                    ResourceId = body.ResourceId,
                    GracePeriodSeconds = 0, // Skip grace period since we're archiving
                    CleanupPolicy = body.DeleteSourceDataPolicy ?? _configuration.DefaultCleanupPolicy
                },
                cancellationToken);

            if (cleanupResult?.Success == true)
            {
                sourceDataDeleted = true;
                archive.SourceDataDeleted = true;
                await _archiveStore.SaveAsync(archiveKey, archive, cancellationToken: cancellationToken);

                _logger.LogInformation(
                    "Source data deleted for {ResourceType}:{ResourceId} after archival",
                    body.ResourceType, body.ResourceId);
            }
            else
            {
                _logger.LogWarning(
                    "Failed to delete source data for {ResourceType}:{ResourceId} after archival: {Reason}",
                    body.ResourceType, body.ResourceId, cleanupResult?.AbortReason ?? "Unknown");
            }
        }

        // Publish compressed event
        await _messageBus.PublishResourceCompressedAsync(
            new ResourceCompressedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ResourceType = body.ResourceType,
                ResourceId = body.ResourceId,
                ArchiveId = archiveId,
                SourceDataDeleted = sourceDataDeleted,
                EntriesCount = archiveEntries.Count
            },
            cancellationToken);

        stopwatch.Stop();
        return (StatusCodes.OK, new ExecuteCompressResponse
        {
            ResourceType = body.ResourceType,
            ResourceId = body.ResourceId,
            Success = true,
            DryRun = false,
            ArchiveId = archiveId,
            CallbackResults = callbackResults,
            SourceDataDeleted = sourceDataDeleted,
            CompressionDurationMs = (int)stopwatch.ElapsedMilliseconds
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ExecuteDecompressResponse?)> ExecuteDecompressAsync(
        ExecuteDecompressRequest body,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // Get the archive
        var archiveKey = BuildArchiveKey(body.ResourceType, body.ResourceId);
        var archive = await _archiveStore.GetAsync(archiveKey, cancellationToken);

        if (archive == null)
        {
            stopwatch.Stop();
            return (StatusCodes.OK, new ExecuteDecompressResponse
            {
                ResourceType = body.ResourceType,
                ResourceId = body.ResourceId,
                Success = false,
                AbortReason = "No archive found for this resource",
                CallbackResults = new List<DecompressCallbackResult>(),
                DecompressionDurationMs = (int)stopwatch.ElapsedMilliseconds
            });
        }

        // If specific archiveId requested, verify it matches
        if (body.ArchiveId.HasValue && archive.ArchiveId != body.ArchiveId.Value)
        {
            stopwatch.Stop();
            return (StatusCodes.OK, new ExecuteDecompressResponse
            {
                ResourceType = body.ResourceType,
                ResourceId = body.ResourceId,
                Success = false,
                AbortReason = $"Archive {body.ArchiveId} not found; current archive is {archive.ArchiveId}",
                CallbackResults = new List<DecompressCallbackResult>(),
                DecompressionDurationMs = (int)stopwatch.ElapsedMilliseconds
            });
        }

        // Get compression callbacks to find decompression endpoints
        var callbacks = await GetCompressCallbacksAsync(body.ResourceType, cancellationToken);
        var callbacksBySourceType = callbacks.ToDictionary(c => c.SourceType);

        var callbackResults = new List<DecompressCallbackResult>();
        var context = new Dictionary<string, object?>
        {
            ["resourceId"] = body.ResourceId.ToString()
        };

        // Execute decompression for each archive entry
        foreach (var entry in archive.Entries)
        {
            var callbackStopwatch = Stopwatch.StartNew();

            if (!callbacksBySourceType.TryGetValue(entry.SourceType, out var callback))
            {
                callbackStopwatch.Stop();
                _logger.LogWarning(
                    "No decompression callback registered for {SourceType} in archive {ArchiveId}",
                    entry.SourceType, archive.ArchiveId);

                callbackResults.Add(new DecompressCallbackResult
                {
                    SourceType = entry.SourceType,
                    ServiceName = entry.ServiceName,
                    Endpoint = "(not registered)",
                    Success = false,
                    ErrorMessage = "No decompression callback registered for this source type",
                    DurationMs = (int)callbackStopwatch.ElapsedMilliseconds
                });
                continue;
            }

            if (string.IsNullOrEmpty(callback.DecompressEndpoint))
            {
                callbackStopwatch.Stop();
                _logger.LogWarning(
                    "No decompression endpoint defined for {SourceType} in archive {ArchiveId}",
                    entry.SourceType, archive.ArchiveId);

                callbackResults.Add(new DecompressCallbackResult
                {
                    SourceType = entry.SourceType,
                    ServiceName = callback.ServiceName,
                    Endpoint = "(not defined)",
                    Success = false,
                    ErrorMessage = "Decompression endpoint not defined for this callback",
                    DurationMs = (int)callbackStopwatch.ElapsedMilliseconds
                });
                continue;
            }

            try
            {
                // Add the compressed data to the context for template substitution
                context["data"] = entry.Data;

                var apiDefinition = new PreboundApi
                {
                    ServiceName = callback.ServiceName,
                    Endpoint = callback.DecompressEndpoint,
                    PayloadTemplate = callback.DecompressPayloadTemplate ?? "{}",
                    Description = $"Restore {callback.SourceType} data"
                };

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(_configuration.CompressionCallbackTimeoutSeconds));

                var result = await _navigator.ExecutePreboundApiAsync(apiDefinition, context, timeoutCts.Token);

                callbackStopwatch.Stop();

                if (result.IsSuccess && result.Result.StatusCode >= 200 && result.Result.StatusCode < 300)
                {
                    callbackResults.Add(new DecompressCallbackResult
                    {
                        SourceType = entry.SourceType,
                        ServiceName = callback.ServiceName,
                        Endpoint = callback.DecompressEndpoint,
                        Success = true,
                        StatusCode = result.Result.StatusCode,
                        DurationMs = (int)callbackStopwatch.ElapsedMilliseconds
                    });

                    _logger.LogDebug(
                        "Decompression callback succeeded: {ServiceName}{Endpoint} for {ResourceType}:{ResourceId}",
                        callback.ServiceName, callback.DecompressEndpoint, body.ResourceType, body.ResourceId);
                }
                else
                {
                    callbackResults.Add(new DecompressCallbackResult
                    {
                        SourceType = entry.SourceType,
                        ServiceName = callback.ServiceName,
                        Endpoint = callback.DecompressEndpoint,
                        Success = false,
                        StatusCode = result.Result.StatusCode,
                        ErrorMessage = result.SubstitutionError ?? result.Result.ErrorMessage,
                        DurationMs = (int)callbackStopwatch.ElapsedMilliseconds
                    });

                    _logger.LogWarning(
                        "Decompression callback failed: {ServiceName}{Endpoint} returned {StatusCode} for {ResourceType}:{ResourceId}",
                        callback.ServiceName, callback.DecompressEndpoint, result.Result.StatusCode,
                        body.ResourceType, body.ResourceId);
                }
            }
            catch (Exception ex)
            {
                callbackStopwatch.Stop();

                _logger.LogError(ex,
                    "Exception in decompression callback: {ServiceName}{Endpoint} for {ResourceType}:{ResourceId}",
                    callback.ServiceName, callback.DecompressEndpoint, body.ResourceType, body.ResourceId);

                callbackResults.Add(new DecompressCallbackResult
                {
                    SourceType = entry.SourceType,
                    ServiceName = callback.ServiceName,
                    Endpoint = callback.DecompressEndpoint ?? "(exception)",
                    Success = false,
                    StatusCode = 0,
                    ErrorMessage = ex.Message,
                    DurationMs = (int)callbackStopwatch.ElapsedMilliseconds
                });
            }
        }

        // Publish decompressed event if any callbacks succeeded, with per-source-type results
        var succeeded = callbackResults.Where(r => r.Success).Select(r => r.SourceType).ToList();
        var failed = callbackResults.Where(r => !r.Success).Select(r => r.SourceType).ToList();
        if (succeeded.Count > 0)
        {
            await _messageBus.PublishResourceDecompressedAsync(
                new ResourceDecompressedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    ResourceType = body.ResourceType,
                    ResourceId = body.ResourceId,
                    ArchiveId = archive.ArchiveId,
                    EntriesCount = callbackResults.Count,
                    SucceededSourceTypes = succeeded,
                    FailedSourceTypes = failed
                },
                cancellationToken);
        }

        stopwatch.Stop();
        return (StatusCodes.OK, new ExecuteDecompressResponse
        {
            ResourceType = body.ResourceType,
            ResourceId = body.ResourceId,
            Success = callbackResults.All(r => r.Success),
            ArchiveId = archive.ArchiveId,
            CallbackResults = callbackResults,
            DecompressionDurationMs = (int)stopwatch.ElapsedMilliseconds
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ListCompressCallbacksResponse?)> ListCompressCallbacksAsync(
        ListCompressCallbacksRequest body,
        CancellationToken cancellationToken = default)
    {
        var callbacks = new List<CompressCallbackSummary>();

        if (!string.IsNullOrEmpty(body.ResourceType))
        {
            // Get callbacks for specific resource type
            var resourceCallbacks = await GetCompressCallbacksAsync(body.ResourceType, cancellationToken);

            // Optional filter by source type
            if (!string.IsNullOrEmpty(body.SourceType))
            {
                resourceCallbacks = resourceCallbacks
                    .Where(c => c.SourceType == body.SourceType)
                    .ToList();
            }

            callbacks.AddRange(resourceCallbacks.Select(MapToCompressSummary));
        }
        else
        {
            // List all callbacks - use master resource type index
            var resourceTypes = await _compressCacheStore.GetSetAsync<string>(
                MasterCompressResourceTypeIndexKey, cancellationToken);

            foreach (var resourceType in resourceTypes)
            {
                var resourceCallbacks = await GetCompressCallbacksAsync(resourceType, cancellationToken);
                callbacks.AddRange(resourceCallbacks.Select(MapToCompressSummary));
            }
        }

        _logger.LogDebug("Listed {Count} compression callbacks", callbacks.Count);

        return (StatusCodes.OK, new ListCompressCallbacksResponse
        {
            Callbacks = callbacks,
            TotalCount = callbacks.Count
        });
    }

    // =========================================================================
    // Migration Management
    // =========================================================================

    /// <inheritdoc />
    public async Task<(StatusCodes, DefineMigrateCallbackResponse?)> DefineMigrateCallbackAsync(
        DefineMigrateCallbackRequest body,
        CancellationToken cancellationToken = default)
    {
        var callbackKey = BuildMigrateKey(body.ResourceType, body.SourceType);

        // Check if already defined
        var existing = await _migrateStore.GetAsync(callbackKey, cancellationToken);
        var previouslyDefined = existing != null;

        // ServiceName defaults to SourceType when not specified
        var serviceName = body.ServiceName ?? body.SourceType;

        var callback = new MigrateCallbackDefinition
        {
            ResourceType = body.ResourceType,
            SourceType = body.SourceType,
            ServiceName = serviceName,
            MigrateEndpoint = body.MigrateEndpoint,
            MigratePayloadTemplate = body.MigratePayloadTemplate,
            Description = body.Description,
            RegisteredAt = DateTimeOffset.UtcNow
        };

        await _migrateStore.SaveAsync(callbackKey, callback, cancellationToken: cancellationToken);

        // Maintain the callback index for this resource type
        await MaintainMigrateCallbackIndexAsync(body.ResourceType, body.SourceType, cancellationToken);

        _logger.LogInformation(
            "Migration callback {Action} for {ResourceType}/{SourceType}: {ServiceName}{Endpoint}",
            previouslyDefined ? "updated" : "registered",
            body.ResourceType, body.SourceType, serviceName, body.MigrateEndpoint);

        return (StatusCodes.OK, new DefineMigrateCallbackResponse
        {
            ResourceType = body.ResourceType,
            SourceType = body.SourceType,
            Registered = true,
            PreviouslyDefined = previouslyDefined
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ExecuteMigrateResponse?)> ExecuteMigrateAsync(
        ExecuteMigrateRequest body,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // Get all migration callbacks for this resource type
        var callbacks = await GetMigrateCallbacksAsync(body.ResourceType, cancellationToken);

        if (callbacks.Count == 0)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "No migration callbacks registered for resource type {ResourceType}",
                body.ResourceType);

            return (StatusCodes.OK, new ExecuteMigrateResponse
            {
                ResourceType = body.ResourceType,
                SourceResourceId = body.SourceResourceId,
                TargetResourceId = body.TargetResourceId,
                Success = false,
                DryRun = body.DryRun == true,
                AbortReason = "No migration callbacks registered for this resource type",
                CallbackResults = new List<MigrateCallbackResult>(),
                MigrationDurationMs = (int)stopwatch.ElapsedMilliseconds
            });
        }

        // Handle dry run - return preview without executing
        if (body.DryRun == true)
        {
            stopwatch.Stop();
            return (StatusCodes.OK, new ExecuteMigrateResponse
            {
                ResourceType = body.ResourceType,
                SourceResourceId = body.SourceResourceId,
                TargetResourceId = body.TargetResourceId,
                Success = true,
                DryRun = true,
                CallbackResults = callbacks.Select(c => new MigrateCallbackResult
                {
                    SourceType = c.SourceType,
                    ServiceName = c.ServiceName,
                    Endpoint = c.MigrateEndpoint,
                    Success = true, // Hypothetical - not actually executed
                    DurationMs = 0
                }).ToList(),
                MigrationDurationMs = (int)stopwatch.ElapsedMilliseconds
            });
        }

        // Acquire distributed lock on the source resource
        var lockOwner = Guid.NewGuid().ToString();
        await using var lockResponse = await _lockProvider.LockAsync(
            storeName: StateStoreDefinitions.ResourceMigrate,
            resourceId: $"migrate:{body.ResourceType}:{body.SourceResourceId}",
            lockOwner: lockOwner,
            expiryInSeconds: _configuration.CleanupLockExpirySeconds,
            cancellationToken: cancellationToken);

        if (!lockResponse.Success)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "Failed to acquire migration lock for {ResourceType}:{SourceResourceId}",
                body.ResourceType, body.SourceResourceId);

            return (StatusCodes.OK, new ExecuteMigrateResponse
            {
                ResourceType = body.ResourceType,
                SourceResourceId = body.SourceResourceId,
                TargetResourceId = body.TargetResourceId,
                Success = false,
                DryRun = false,
                AbortReason = "Failed to acquire migration lock (another migration in progress?)",
                CallbackResults = new List<MigrateCallbackResult>(),
                MigrationDurationMs = (int)stopwatch.ElapsedMilliseconds
            });
        }

        // Build context for payload template substitution
        var context = new Dictionary<string, object?>
        {
            ["sourceResourceId"] = body.SourceResourceId.ToString(),
            ["targetResourceId"] = body.TargetResourceId.ToString()
        };

        var apiDefinitions = callbacks.Select(c => new PreboundApi
        {
            ServiceName = c.ServiceName,
            Endpoint = c.MigrateEndpoint,
            PayloadTemplate = c.MigratePayloadTemplate,
            Description = c.Description
        }).ToList();

        // Apply configured timeout for migration callbacks
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_configuration.CleanupCallbackTimeoutSeconds));

        var results = await _navigator.ExecutePreboundApiBatchAsync(
            apiDefinitions,
            context,
            BatchExecutionMode.Parallel,
            timeoutCts.Token);

        var callbackResults = new List<MigrateCallbackResult>();

        for (var i = 0; i < results.Count; i++)
        {
            var result = results[i];
            var callback = callbacks[i];

            var callbackResult = new MigrateCallbackResult
            {
                SourceType = callback.SourceType,
                ServiceName = callback.ServiceName,
                Endpoint = callback.MigrateEndpoint,
                Success = result.IsSuccess,
                StatusCode = result.Result.StatusCode,
                ErrorMessage = result.IsSuccess ? null : (result.SubstitutionError ?? result.Result.ErrorMessage),
                DurationMs = (int)result.Result.Duration.TotalMilliseconds
            };

            callbackResults.Add(callbackResult);

            if (!result.IsSuccess)
            {
                _logger.LogWarning(
                    "Migration callback failed: {ServiceName}{Endpoint} returned {StatusCode} for {ResourceType}:{SourceResourceId} -> {TargetResourceId}",
                    callback.ServiceName, callback.MigrateEndpoint, result.Result.StatusCode,
                    body.ResourceType, body.SourceResourceId, body.TargetResourceId);

                // Publish failure event for monitoring
                await _messageBus.PublishResourceMigrateCallbackFailedAsync(
                    new ResourceMigrateCallbackFailedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        ResourceType = body.ResourceType,
                        SourceResourceId = body.SourceResourceId,
                        TargetResourceId = body.TargetResourceId,
                        SourceType = callback.SourceType,
                        ServiceName = callback.ServiceName,
                        Endpoint = callback.MigrateEndpoint,
                        StatusCode = result.Result.StatusCode,
                        ErrorMessage = callbackResult.ErrorMessage
                    },
                    cancellationToken);
            }
        }

        var succeeded = callbackResults.Where(r => r.Success).Select(r => r.SourceType).ToList();
        var failed = callbackResults.Where(r => !r.Success).Select(r => r.SourceType).ToList();
        var allSucceeded = failed.Count == 0;

        if (allSucceeded)
        {
            // Publish migrated event on success
            await _messageBus.PublishResourceMigratedAsync(
                new ResourceMigratedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    ResourceType = body.ResourceType,
                    SourceResourceId = body.SourceResourceId,
                    TargetResourceId = body.TargetResourceId,
                    CallbackCount = callbacks.Count,
                    SucceededSourceTypes = succeeded,
                    FailedSourceTypes = failed
                },
                cancellationToken);
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "Migration {Status} for {ResourceType}:{SourceResourceId} -> {TargetResourceId} with {CallbackCount} callback(s) in {DurationMs}ms",
            allSucceeded ? "completed" : "partially failed",
            body.ResourceType, body.SourceResourceId, body.TargetResourceId,
            callbacks.Count, stopwatch.ElapsedMilliseconds);

        return (StatusCodes.OK, new ExecuteMigrateResponse
        {
            ResourceType = body.ResourceType,
            SourceResourceId = body.SourceResourceId,
            TargetResourceId = body.TargetResourceId,
            Success = allSucceeded,
            DryRun = false,
            AbortReason = allSucceeded ? null : $"{failed.Count} migration callback(s) failed",
            CallbackResults = callbackResults,
            MigrationDurationMs = (int)stopwatch.ElapsedMilliseconds
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ListMigrateCallbacksResponse?)> ListMigrateCallbacksAsync(
        ListMigrateCallbacksRequest body,
        CancellationToken cancellationToken = default)
    {
        var callbacks = new List<MigrateCallbackSummary>();

        if (!string.IsNullOrEmpty(body.ResourceType))
        {
            // Get callbacks for specific resource type
            var resourceCallbacks = await GetMigrateCallbacksAsync(body.ResourceType, cancellationToken);

            // Optional filter by source type
            if (!string.IsNullOrEmpty(body.SourceType))
            {
                resourceCallbacks = resourceCallbacks
                    .Where(c => c.SourceType == body.SourceType)
                    .ToList();
            }

            callbacks.AddRange(resourceCallbacks.Select(MapToMigrateSummary));
        }
        else
        {
            // List all callbacks - use master resource type index
            var resourceTypes = await _migrateIndexStore.GetSetAsync<string>(
                MasterMigrateResourceTypeIndexKey, cancellationToken);

            foreach (var resourceType in resourceTypes)
            {
                var resourceCallbacks = await GetMigrateCallbacksAsync(resourceType, cancellationToken);
                callbacks.AddRange(resourceCallbacks.Select(MapToMigrateSummary));
            }
        }

        _logger.LogDebug("Listed {Count} migration callbacks", callbacks.Count);

        return (StatusCodes.OK, new ListMigrateCallbacksResponse
        {
            Callbacks = callbacks,
            TotalCount = callbacks.Count
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, GetArchiveResponse?)> GetArchiveAsync(
        GetArchiveRequest body,
        CancellationToken cancellationToken = default)
    {
        var archiveKey = BuildArchiveKey(body.ResourceType, body.ResourceId);
        var archive = await _archiveStore.GetAsync(archiveKey, cancellationToken);

        if (archive == null)
        {
            return (StatusCodes.OK, new GetArchiveResponse
            {
                ResourceType = body.ResourceType,
                ResourceId = body.ResourceId,
                Found = false,
                Archive = null
            });
        }

        // If specific archiveId requested, verify it matches
        if (body.ArchiveId.HasValue && archive.ArchiveId != body.ArchiveId.Value)
        {
            return (StatusCodes.OK, new GetArchiveResponse
            {
                ResourceType = body.ResourceType,
                ResourceId = body.ResourceId,
                Found = false,
                Archive = null
            });
        }

        // Map internal model to API model
        return (StatusCodes.OK, new GetArchiveResponse
        {
            ResourceType = body.ResourceType,
            ResourceId = body.ResourceId,
            Found = true,
            Archive = new ResourceArchive
            {
                ArchiveId = archive.ArchiveId,
                ResourceType = archive.ResourceType,
                ResourceId = archive.ResourceId,
                Version = archive.Version,
                Entries = archive.Entries.Select(e => new ArchiveBundleEntry
                {
                    SourceType = e.SourceType,
                    ServiceName = e.ServiceName,
                    Data = e.Data,
                    CompressedAt = e.CompressedAt,
                    DataChecksum = e.DataChecksum,
                    OriginalSizeBytes = e.OriginalSizeBytes
                }).ToList(),
                CreatedAt = archive.CreatedAt,
                SourceDataDeleted = archive.SourceDataDeleted
            }
        });
    }

    /// <summary>
    /// Maps a CleanupCallbackDefinition to a CleanupCallbackSummary for API responses.
    /// </summary>
    private static CleanupCallbackSummary MapToSummary(CleanupCallbackDefinition callback)
        => new()
        {
            ResourceType = callback.ResourceType,
            SourceType = callback.SourceType,
            OnDeleteAction = callback.OnDeleteAction,
            ServiceName = callback.ServiceName,
            CallbackEndpoint = callback.CallbackEndpoint,
            RegisteredAt = callback.RegisteredAt,
            Description = callback.Description
        };

    /// <summary>
    /// Key for the master index of all resource types that have callbacks registered.
    /// </summary>
    private const string MasterResourceTypeIndexKey = "callback-resource-types";

    private const string RESOURCE_SOURCES_KEY_SUFFIX = ":sources";
    private const string RESOURCE_GRACE_KEY_SUFFIX = ":grace";
    private const string CLEANUP_KEY_PREFIX = "callback:";
    private const string CLEANUP_INDEX_KEY_PREFIX = "callback-index:";
    private const string COMPRESS_KEY_PREFIX = "compress-callback:";
    private const string COMPRESS_INDEX_KEY_PREFIX = "compress-callback-index:";
    private const string ARCHIVE_KEY_PREFIX = "archive:";
    private const string MIGRATE_KEY_PREFIX = "callback:";
    private const string MIGRATE_INDEX_KEY_PREFIX = "callback-index:";
    private const string MasterMigrateResourceTypeIndexKey = "callback-resource-types";
    private const string TRANSACTION_KEY_PREFIX = "tx:";
    private const string PROVISION_KEY_PREFIX = "prov:";
    private const string PROVISION_TX_INDEX_PREFIX = "prov-tx:";

    // =========================================================================
    // Internal Helpers
    // =========================================================================

    /// <summary>
    /// Builds the Redis key for a resource's reference set.
    /// </summary>
    internal static string BuildResourceKey(string resourceType, Guid resourceId)
        => $"{resourceType}:{resourceId}{RESOURCE_SOURCES_KEY_SUFFIX}";

    /// <summary>
    /// Builds the Redis key for a resource's grace period record.
    /// </summary>
    internal static string BuildGraceKey(string resourceType, Guid resourceId)
        => $"{resourceType}:{resourceId}{RESOURCE_GRACE_KEY_SUFFIX}";

    /// <summary>
    /// Builds the Redis key for a cleanup callback definition.
    /// </summary>
    internal static string BuildCleanupKey(string resourceType, string sourceType)
        => $"{CLEANUP_KEY_PREFIX}{resourceType}:{sourceType}";


    /// <summary>
    /// Maps a MigrateCallbackDefinition to a MigrateCallbackSummary for API responses.
    /// </summary>
    private static MigrateCallbackSummary MapToMigrateSummary(MigrateCallbackDefinition callback)
        => new()
        {
            ResourceType = callback.ResourceType,
            SourceType = callback.SourceType,
            ServiceName = callback.ServiceName,
            MigrateEndpoint = callback.MigrateEndpoint,
            RegisteredAt = callback.RegisteredAt,
            Description = callback.Description
        };

    /// <summary>
    /// Maps a CompressCallbackDefinition to a CompressCallbackSummary for API responses.
    /// </summary>
    private static CompressCallbackSummary MapToCompressSummary(CompressCallbackDefinition callback)
        => new()
        {
            ResourceType = callback.ResourceType,
            SourceType = callback.SourceType,
            ServiceName = callback.ServiceName,
            CompressEndpoint = callback.CompressEndpoint,
            DecompressEndpoint = callback.DecompressEndpoint,
            Priority = callback.Priority,
            RegisteredAt = callback.RegisteredAt,
            Description = callback.Description
        };

    /// <summary>
    /// Compresses JSON data using GZip and returns base64-encoded string.
    /// </summary>
    private static string CompressJsonData(string jsonData)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(jsonData);
        using var output = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(
            output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(bytes, 0, bytes.Length);
        }
        return Convert.ToBase64String(output.ToArray());
    }

    /// <summary>
    /// Computes SHA256 checksum for data integrity verification.
    /// </summary>
    private static string ComputeChecksum(byte[] data)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(data);
        return Convert.ToHexString(hash);
    }

    // =========================================================================
    // Snapshot Management (Living Entity Snapshots)
    // =========================================================================

    /// <inheritdoc />
    public async Task<(StatusCodes, ExecuteSnapshotResponse?)> ExecuteSnapshotAsync(
        ExecuteSnapshotRequest body,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // Get all compression callbacks for this resource type, sorted by priority
        var allCallbacks = await GetCompressCallbacksAsync(body.ResourceType, cancellationToken);

        // Apply server-side filtering if filterSourceTypes is specified
        var callbacks = body.FilterSourceTypes is { Count: > 0 }
            ? allCallbacks.Where(c => body.FilterSourceTypes.Contains(c.SourceType, StringComparer.OrdinalIgnoreCase)).ToList()
            : allCallbacks;

        if (callbacks.Count == 0)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "No compression callbacks registered for resource type {ResourceType} (snapshot)",
                body.ResourceType);

            return (StatusCodes.OK, new ExecuteSnapshotResponse
            {
                ResourceType = body.ResourceType,
                ResourceId = body.ResourceId,
                Success = false,
                DryRun = body.DryRun,
                AbortReason = "No compression callbacks registered for this resource type",
                CallbackResults = new List<CompressCallbackResult>(),
                SnapshotDurationMs = (int)stopwatch.ElapsedMilliseconds
            });
        }

        // Handle dry run - return preview without executing
        if (body.DryRun)
        {
            stopwatch.Stop();
            return (StatusCodes.OK, new ExecuteSnapshotResponse
            {
                ResourceType = body.ResourceType,
                ResourceId = body.ResourceId,
                Success = true,
                DryRun = true,
                SnapshotId = null,
                ExpiresAt = null,
                CallbackResults = callbacks.Select(c => new CompressCallbackResult
                {
                    SourceType = c.SourceType,
                    ServiceName = c.ServiceName,
                    Endpoint = c.CompressEndpoint,
                    Success = true, // Hypothetical - not actually executed
                    DurationMs = 0
                }).ToList(),
                SnapshotDurationMs = (int)stopwatch.ElapsedMilliseconds
            });
        }

        // Use configuration defaults when not specified in request
        var compressionPolicy = body.CompressionPolicy ?? _configuration.DefaultCompressionPolicy;
        var ttlSeconds = body.TtlSeconds ?? _configuration.SnapshotDefaultTtlSeconds;
        ttlSeconds = Math.Clamp(ttlSeconds, _configuration.SnapshotMinTtlSeconds, _configuration.SnapshotMaxTtlSeconds);

        var callbackResults = new List<CompressCallbackResult>();
        var snapshotEntries = new List<ArchiveEntryModel>();
        var context = new Dictionary<string, object?>
        {
            ["resourceId"] = body.ResourceId.ToString()
        };

        // Execute each callback in priority order (same logic as compression)
        foreach (var callback in callbacks)
        {
            var callbackStopwatch = Stopwatch.StartNew();

            try
            {
                var apiDefinition = new PreboundApi
                {
                    ServiceName = callback.ServiceName,
                    Endpoint = callback.CompressEndpoint,
                    PayloadTemplate = callback.CompressPayloadTemplate,
                    Description = callback.Description
                };

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(_configuration.CompressionCallbackTimeoutSeconds));

                var result = await _navigator.ExecutePreboundApiAsync(apiDefinition, context, timeoutCts.Token);

                callbackStopwatch.Stop();

                if (result.IsSuccess && result.Result.StatusCode >= 200 && result.Result.StatusCode < 300)
                {
                    // Get the response body and compress it
                    var responseJson = result.Result.ResponseBody ?? "{}";
                    var originalBytes = System.Text.Encoding.UTF8.GetBytes(responseJson);
                    var compressedData = CompressJsonData(responseJson);
                    var checksum = ComputeChecksum(originalBytes);

                    snapshotEntries.Add(new ArchiveEntryModel
                    {
                        SourceType = callback.SourceType,
                        ServiceName = callback.ServiceName,
                        Data = compressedData,
                        CompressedAt = DateTimeOffset.UtcNow,
                        DataChecksum = checksum,
                        OriginalSizeBytes = originalBytes.Length
                    });

                    callbackResults.Add(new CompressCallbackResult
                    {
                        SourceType = callback.SourceType,
                        ServiceName = callback.ServiceName,
                        Endpoint = callback.CompressEndpoint,
                        Success = true,
                        StatusCode = result.Result.StatusCode,
                        DataSize = compressedData.Length,
                        DurationMs = (int)callbackStopwatch.ElapsedMilliseconds
                    });

                    _logger.LogDebug(
                        "Snapshot callback succeeded: {ServiceName}{Endpoint} for {ResourceType}:{ResourceId}",
                        callback.ServiceName, callback.CompressEndpoint, body.ResourceType, body.ResourceId);
                }
                else
                {
                    // Callback failed
                    callbackResults.Add(new CompressCallbackResult
                    {
                        SourceType = callback.SourceType,
                        ServiceName = callback.ServiceName,
                        Endpoint = callback.CompressEndpoint,
                        Success = false,
                        StatusCode = result.Result.StatusCode,
                        ErrorMessage = result.SubstitutionError ?? result.Result.ErrorMessage,
                        DurationMs = (int)callbackStopwatch.ElapsedMilliseconds
                    });

                    _logger.LogWarning(
                        "Snapshot callback failed: {ServiceName}{Endpoint} returned {StatusCode} for {ResourceType}:{ResourceId}",
                        callback.ServiceName, callback.CompressEndpoint, result.Result.StatusCode,
                        body.ResourceType, body.ResourceId);

                    // Abort if policy requires all callbacks
                    if (compressionPolicy == CompressionPolicy.AllRequired)
                    {
                        stopwatch.Stop();
                        return (StatusCodes.OK, new ExecuteSnapshotResponse
                        {
                            ResourceType = body.ResourceType,
                            ResourceId = body.ResourceId,
                            Success = false,
                            DryRun = false,
                            AbortReason = $"Snapshot callback failed for {callback.SourceType} with ALL_REQUIRED policy",
                            CallbackResults = callbackResults,
                            SnapshotDurationMs = (int)stopwatch.ElapsedMilliseconds
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                callbackStopwatch.Stop();

                _logger.LogError(ex,
                    "Exception in snapshot callback: {ServiceName}{Endpoint} for {ResourceType}:{ResourceId}",
                    callback.ServiceName, callback.CompressEndpoint, body.ResourceType, body.ResourceId);

                callbackResults.Add(new CompressCallbackResult
                {
                    SourceType = callback.SourceType,
                    ServiceName = callback.ServiceName,
                    Endpoint = callback.CompressEndpoint,
                    Success = false,
                    StatusCode = 0,
                    ErrorMessage = ex.Message,
                    DurationMs = (int)callbackStopwatch.ElapsedMilliseconds
                });

                if (compressionPolicy == CompressionPolicy.AllRequired)
                {
                    stopwatch.Stop();
                    return (StatusCodes.OK, new ExecuteSnapshotResponse
                    {
                        ResourceType = body.ResourceType,
                        ResourceId = body.ResourceId,
                        Success = false,
                        DryRun = false,
                        AbortReason = $"Snapshot callback exception for {callback.SourceType} with ALL_REQUIRED policy: {ex.Message}",
                        CallbackResults = callbackResults,
                        SnapshotDurationMs = (int)stopwatch.ElapsedMilliseconds
                    });
                }
            }
        }

        // Check if we have any entries
        if (snapshotEntries.Count == 0)
        {
            stopwatch.Stop();
            return (StatusCodes.OK, new ExecuteSnapshotResponse
            {
                ResourceType = body.ResourceType,
                ResourceId = body.ResourceId,
                Success = false,
                DryRun = false,
                AbortReason = "No successful snapshot callbacks - cannot create snapshot",
                CallbackResults = callbackResults,
                SnapshotDurationMs = (int)stopwatch.ElapsedMilliseconds
            });
        }

        // Create snapshot with TTL
        var snapshotId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(ttlSeconds);
        var snapshotKey = $"snap:{snapshotId}";

        var snapshot = new ResourceSnapshotModel
        {
            SnapshotId = snapshotId,
            ResourceType = body.ResourceType,
            ResourceId = body.ResourceId,
            SnapshotType = body.SnapshotType ?? "default",
            Entries = snapshotEntries,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt
        };

        // Save with TTL (Redis auto-expires)
        await _snapshotStore.SaveAsync(snapshotKey, snapshot, new StateOptions { Ttl = ttlSeconds }, cancellationToken);

        _logger.LogInformation(
            "Created snapshot {SnapshotId} for {ResourceType}:{ResourceId} with {EntryCount} entries, expires at {ExpiresAt}",
            snapshotId, body.ResourceType, body.ResourceId, snapshotEntries.Count, expiresAt);

        // Publish snapshot created event
        await _messageBus.PublishResourceSnapshotCreatedAsync(
            new ResourceSnapshotCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ResourceType = body.ResourceType,
                ResourceId = body.ResourceId,
                SnapshotId = snapshotId,
                SnapshotType = body.SnapshotType ?? "default",
                ExpiresAt = expiresAt,
                EntriesCount = snapshotEntries.Count
            },
            cancellationToken);

        stopwatch.Stop();
        return (StatusCodes.OK, new ExecuteSnapshotResponse
        {
            ResourceType = body.ResourceType,
            ResourceId = body.ResourceId,
            Success = true,
            DryRun = false,
            SnapshotId = snapshotId,
            ExpiresAt = expiresAt,
            CallbackResults = callbackResults,
            SnapshotDurationMs = (int)stopwatch.ElapsedMilliseconds
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, GetSnapshotResponse?)> GetSnapshotAsync(
        GetSnapshotRequest body,
        CancellationToken cancellationToken = default)
    {
        var snapshotKey = $"snap:{body.SnapshotId}";
        var snapshot = await _snapshotStore.GetAsync(snapshotKey, cancellationToken);

        if (snapshot == null)
        {
            _logger.LogDebug(
                "Snapshot {SnapshotId} not found (may have expired)",
                body.SnapshotId);

            return (StatusCodes.OK, new GetSnapshotResponse
            {
                SnapshotId = body.SnapshotId,
                Found = false,
                Snapshot = null
            });
        }

        // Apply server-side filtering if filterSourceTypes is specified
        var entriesToReturn = body.FilterSourceTypes is { Count: > 0 }
            ? snapshot.Entries.Where(e => body.FilterSourceTypes.Contains(e.SourceType, StringComparer.OrdinalIgnoreCase))
            : snapshot.Entries;

        // Convert internal model to API model
        var apiSnapshot = new ResourceSnapshot
        {
            SnapshotId = snapshot.SnapshotId,
            ResourceType = snapshot.ResourceType,
            ResourceId = snapshot.ResourceId,
            SnapshotType = snapshot.SnapshotType,
            Entries = entriesToReturn.Select(e => new ArchiveBundleEntry
            {
                SourceType = e.SourceType,
                ServiceName = e.ServiceName,
                Data = e.Data,
                CompressedAt = e.CompressedAt,
                DataChecksum = e.DataChecksum,
                OriginalSizeBytes = e.OriginalSizeBytes
            }).ToList(),
            CreatedAt = snapshot.CreatedAt,
            ExpiresAt = snapshot.ExpiresAt
        };

        return (StatusCodes.OK, new GetSnapshotResponse
        {
            SnapshotId = body.SnapshotId,
            Found = true,
            Snapshot = apiSnapshot
        });
    }

    // =========================================================================
    // Seeded Resource Management
    // =========================================================================

    /// <inheritdoc />
    public async Task<(StatusCodes, ListSeededResourcesResponse?)> ListSeededResourcesAsync(
        ListSeededResourcesRequest body,
        CancellationToken cancellationToken = default)
    {
        var resources = new List<SeededResourceSummary>();

        // Query all registered providers
        foreach (var provider in _seededProviders)
        {
            // Filter by resource type if specified
            if (!string.IsNullOrEmpty(body.ResourceType) &&
                !string.Equals(provider.ResourceType, body.ResourceType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var identifiers = await provider.ListSeededAsync(cancellationToken);

                // Use provider-level ContentType to avoid loading each resource
                // SizeBytes is left null for efficiency - caller can get full resource if size is needed
                foreach (var identifier in identifiers)
                {
                    resources.Add(new SeededResourceSummary
                    {
                        ResourceType = provider.ResourceType,
                        Identifier = identifier,
                        ContentType = provider.ContentType,
                        SizeBytes = null
                    });
                }
            }
            catch (Exception ex)
            {
                // Log but continue - one failing provider shouldn't break the whole list
                _logger.LogWarning(
                    ex,
                    "Failed to list seeded resources from provider {ResourceType}",
                    provider.ResourceType);
            }
        }

        _logger.LogDebug(
            "Listed {Count} seeded resources (filter: {ResourceType})",
            resources.Count,
            body.ResourceType ?? "none");

        return (StatusCodes.OK, new ListSeededResourcesResponse
        {
            Resources = resources,
            TotalCount = resources.Count
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, GetSeededResourceResponse?)> GetSeededResourceAsync(
        GetSeededResourceRequest body,
        CancellationToken cancellationToken = default)
    {
        // Find providers for the requested resource type
        var matchingProviders = _seededProviders
            .Where(p => string.Equals(p.ResourceType, body.ResourceType, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingProviders.Count == 0)
        {
            _logger.LogDebug(
                "No providers registered for resource type {ResourceType}",
                body.ResourceType);

            return (StatusCodes.OK, new GetSeededResourceResponse
            {
                ResourceType = body.ResourceType,
                Identifier = body.Identifier,
                Found = false,
                Resource = null
            });
        }

        // Try each provider until we find the resource
        foreach (var provider in matchingProviders)
        {
            try
            {
                var resource = await provider.GetSeededAsync(body.Identifier, cancellationToken);
                if (resource != null)
                {
                    _logger.LogDebug(
                        "Found seeded resource {ResourceType}:{Identifier} ({Size} bytes)",
                        body.ResourceType,
                        body.Identifier,
                        resource.SizeBytes);

                    return (StatusCodes.OK, new GetSeededResourceResponse
                    {
                        ResourceType = body.ResourceType,
                        Identifier = body.Identifier,
                        Found = true,
                        Resource = new SeededResourceDetail
                        {
                            ResourceType = resource.ResourceType,
                            Identifier = resource.Identifier,
                            ContentType = resource.ContentType,
                            Content = Convert.ToBase64String(resource.Content),
                            Metadata = resource.Metadata.Count > 0
                                ? resource.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                                : null
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                // Log but continue to next provider
                _logger.LogWarning(
                    ex,
                    "Failed to get seeded resource {ResourceType}:{Identifier} from provider",
                    body.ResourceType,
                    body.Identifier);
            }
        }

        _logger.LogDebug(
            "Seeded resource {ResourceType}:{Identifier} not found in any provider",
            body.ResourceType,
            body.Identifier);

        return (StatusCodes.OK, new GetSeededResourceResponse
        {
            ResourceType = body.ResourceType,
            Identifier = body.Identifier,
            Found = false,
            Resource = null
        });
    }

    // =========================================================================
    // Transaction Management
    // =========================================================================

    /// <inheritdoc />
    public async Task<(StatusCodes, BeginTransactionResponse?)> BeginTransactionAsync(
        BeginTransactionRequest body, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var effectiveTtl = Math.Clamp(
            body.TtlSeconds ?? _configuration.TransactionDefaultTtlSeconds,
            _configuration.TransactionMinTtlSeconds,
            _configuration.TransactionMaxTtlSeconds);

        string? serializedValidation = null;
        if (body.CompletionValidation != null)
        {
            serializedValidation = BannouJson.Serialize(body.CompletionValidation);
        }

        var transactionId = Guid.NewGuid();
        var transaction = new ResourceTransactionModel
        {
            TransactionId = transactionId,
            OwnerService = body.OwnerService,
            ParentResourceType = body.ParentResourceType,
            ParentResourceId = body.ParentResourceId,
            Status = TransactionStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
            TtlSeconds = effectiveTtl,
            ExpectedProvisionCount = body.ExpectedProvisionCount,
            ValidationAttempts = 0,
            CompletionValidation = serializedValidation
        };

        await _transactionStore.SaveAsync(BuildTransactionKey(transactionId), transaction, cancellationToken: ct);

        await _messageBus.PublishResourceTransactionCreatedAsync(
            new ResourceTransactionCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                TransactionId = transactionId,
                OwnerService = body.OwnerService,
                ParentResourceType = body.ParentResourceType,
                ParentResourceId = body.ParentResourceId,
                TtlSeconds = effectiveTtl,
            }, ct);

        _logger.LogInformation(
            "Transaction {TransactionId} begun for {OwnerService}/{ParentResourceType}:{ParentResourceId} with TTL {TtlSeconds}s",
            transactionId, body.OwnerService, body.ParentResourceType, body.ParentResourceId, effectiveTtl);

        return (StatusCodes.OK, new BeginTransactionResponse
        {
            TransactionId = transactionId,
            Status = TransactionStatus.Active,
            TtlSeconds = effectiveTtl,
            ExpiresAt = now.AddSeconds(effectiveTtl)
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, RegisterProvisionResponse?)> RegisterProvisionAsync(
        RegisterProvisionRequest body, CancellationToken ct = default)
    {
        var transaction = await _transactionStore.GetAsync(BuildTransactionKey(body.TransactionId), ct);
        if (transaction == null)
            return (StatusCodes.NotFound, null);
        if (transaction.Status != TransactionStatus.Active)
            return (StatusCodes.BadRequest, null);

        // Determine sequence number: read current index to count existing provisions
        var indexKey = BuildProvisionTxIndexKey(body.TransactionId);
        var indexJson = await _provisionStringStore.GetAsync(indexKey, ct);
        var existingCount = 0;
        if (!string.IsNullOrEmpty(indexJson))
        {
            var existing = BannouJson.Deserialize<List<string>>(indexJson);
            existingCount = existing?.Count ?? 0;
        }

        var provisionId = Guid.NewGuid();
        var provision = new ResourceProvisionModel
        {
            ProvisionId = provisionId,
            TransactionId = body.TransactionId,
            SequenceNumber = existingCount,
            ResourceType = body.ResourceType,
            ResourceId = body.ResourceId,
            Status = ProvisionStatus.Pending,
            RegisteredAt = DateTimeOffset.UtcNow,
            CompensationAttempts = 0,
            Compensation = BannouJson.Serialize(body.Compensation),
            Verification = body.Verification != null ? BannouJson.Serialize(body.Verification) : null
        };

        // Save provision record first (idempotent by key)
        await _provisionStore.SaveAsync(BuildProvisionKey(provisionId), provision, cancellationToken: ct);

        // Append to index using ETag-protected helper (per IMPLEMENTATION TENETS — multi-instance safe)
        await _provisionStringStore.AddToStringListAsync(
            indexKey,
            provisionId.ToString(),
            _configuration.ProvisionIndexMaxRetries,
            _logger,
            ct);

        _logger.LogDebug(
            "Provision {ProvisionId} registered for transaction {TransactionId}: {ResourceType}:{ResourceId} (seq {Seq})",
            provisionId, body.TransactionId, body.ResourceType, body.ResourceId, provision.SequenceNumber);

        return (StatusCodes.OK, new RegisterProvisionResponse
        {
            ProvisionId = provisionId,
            SequenceNumber = provision.SequenceNumber,
            Status = ProvisionStatus.Pending
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ConfirmProvisionResponse?)> ConfirmProvisionAsync(
        ConfirmProvisionRequest body, CancellationToken ct = default)
    {
        var transaction = await _transactionStore.GetAsync(BuildTransactionKey(body.TransactionId), ct);
        if (transaction == null)
            return (StatusCodes.NotFound, null);
        if (transaction.Status != TransactionStatus.Active)
            return (StatusCodes.BadRequest, null);

        // Find provision by resourceId within this transaction
        var provision = await FindProvisionByResourceIdAsync(body.TransactionId, body.ResourceId, ct);
        if (provision == null)
            return (StatusCodes.NotFound, null);
        if (provision.Status != ProvisionStatus.Pending)
            return (StatusCodes.BadRequest, null);

        provision.Status = ProvisionStatus.Provisioned;
        provision.ProvisionedAt = DateTimeOffset.UtcNow;
        await _provisionStore.SaveAsync(BuildProvisionKey(provision.ProvisionId), provision, cancellationToken: ct);

        _logger.LogDebug(
            "Provision {ProvisionId} confirmed for transaction {TransactionId}: {ResourceType}:{ResourceId}",
            provision.ProvisionId, body.TransactionId, provision.ResourceType, body.ResourceId);

        return (StatusCodes.OK, new ConfirmProvisionResponse
        {
            ProvisionId = provision.ProvisionId,
            Status = ProvisionStatus.Provisioned
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, CommitTransactionResponse?)> CommitTransactionAsync(
        CommitTransactionRequest body, CancellationToken ct = default)
    {
        var (transaction, etag) = await _transactionStore.GetWithETagAsync(
            BuildTransactionKey(body.TransactionId), ct);
        if (transaction == null)
            return (StatusCodes.NotFound, null);
        if (transaction.Status != TransactionStatus.Active)
            return (StatusCodes.BadRequest, null);

        // Phase 1: Transition to Committing (crash-safe checkpoint per R2)
        transaction.Status = TransactionStatus.Committing;
        transaction.UpdatedAt = DateTimeOffset.UtcNow;
        var newEtag = await _transactionStore.TrySaveAsync(
            BuildTransactionKey(body.TransactionId), transaction, etag ?? string.Empty, cancellationToken: ct);
        if (newEtag == null)
            return (StatusCodes.Conflict, null);

        // Phase 2: Register references one by one, checkpointing each provision
        // Per-item error isolation (IMPLEMENTATION TENETS T7) — one failed registration must not block others
        var provisions = await GetOrderedProvisionsAsync(body.TransactionId, ct);
        var referencesRegistered = 0;
        var registrationFailed = false;

        foreach (var provision in provisions)
        {
            if (provision.Status != ProvisionStatus.Provisioned)
                continue;

            try
            {
                // Register as permanent reference via existing internal path
                var (refStatus, _) = await RegisterReferenceAsync(new RegisterReferenceRequest
                {
                    ResourceType = transaction.ParentResourceType,
                    ResourceId = transaction.ParentResourceId,
                    SourceType = provision.ResourceType,
                    SourceId = provision.ResourceId.ToString()
                }, ct);

                if (refStatus != StatusCodes.OK)
                {
                    _logger.LogWarning(
                        "Reference registration returned {Status} for provision {ProvisionId} ({ResourceType}:{ResourceId}), skipping checkpoint",
                        refStatus, provision.ProvisionId, provision.ResourceType, provision.ResourceId);
                    registrationFailed = true;
                    continue;
                }

                provision.Status = ProvisionStatus.ReferenceRegistered;
                await _provisionStore.SaveAsync(
                    BuildProvisionKey(provision.ProvisionId), provision, cancellationToken: ct);
                referencesRegistered++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to register reference for provision {ProvisionId} ({ResourceType}:{ResourceId}), continuing",
                    provision.ProvisionId, provision.ResourceType, provision.ResourceId);
                registrationFailed = true;
            }
        }

        if (registrationFailed)
        {
            // Some provisions failed — remain in Committing state for the recovery worker to resume
            _logger.LogWarning(
                "Transaction {TransactionId} commit partially failed: {Registered} registered, some remaining — worker will resume",
                body.TransactionId, referencesRegistered);
            transaction.UpdatedAt = DateTimeOffset.UtcNow;
            await _transactionStore.SaveAsync(
                BuildTransactionKey(body.TransactionId), transaction, cancellationToken: ct);
            return (StatusCodes.OK, new CommitTransactionResponse
            {
                TransactionId = body.TransactionId,
                Status = TransactionStatus.Committing,
                ReferencesRegistered = referencesRegistered
            });
        }

        // Phase 3: Transition to Committed
        transaction.Status = TransactionStatus.Committed;
        transaction.UpdatedAt = DateTimeOffset.UtcNow;
        await _transactionStore.SaveAsync(BuildTransactionKey(body.TransactionId), transaction, cancellationToken: ct);

        await _messageBus.PublishResourceTransactionCommittedAsync(
            new ResourceTransactionCommittedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                TransactionId = body.TransactionId,
                OwnerService = transaction.OwnerService,
                ParentResourceType = transaction.ParentResourceType,
                ParentResourceId = transaction.ParentResourceId,
                ProvisionCount = referencesRegistered,
            }, ct);

        _logger.LogInformation(
            "Transaction {TransactionId} committed: {RefsRegistered} references registered for {ParentResourceType}:{ParentResourceId}",
            body.TransactionId, referencesRegistered, transaction.ParentResourceType, transaction.ParentResourceId);

        return (StatusCodes.OK, new CommitTransactionResponse
        {
            TransactionId = body.TransactionId,
            Status = TransactionStatus.Committed,
            ReferencesRegistered = referencesRegistered
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, AbortTransactionResponse?)> AbortTransactionAsync(
        AbortTransactionRequest body, CancellationToken ct = default)
    {
        var (transaction, etag) = await _transactionStore.GetWithETagAsync(
            BuildTransactionKey(body.TransactionId), ct);
        if (transaction == null)
            return (StatusCodes.NotFound, null);
        if (transaction.Status != TransactionStatus.Active && transaction.Status != TransactionStatus.Aborting)
            return (StatusCodes.BadRequest, null);

        // Transition to Aborting (optimistic concurrency per R10)
        if (transaction.Status == TransactionStatus.Active)
        {
            transaction.Status = TransactionStatus.Aborting;
            transaction.AbortReason = body.Reason;
            transaction.UpdatedAt = DateTimeOffset.UtcNow;
            var newEtag = await _transactionStore.TrySaveAsync(
                BuildTransactionKey(body.TransactionId), transaction, etag ?? string.Empty, cancellationToken: ct);
            if (newEtag == null)
                return (StatusCodes.Conflict, null);
        }

        // Compensate provisions in reverse sequence order
        var provisions = await GetOrderedProvisionsAsync(body.TransactionId, ct);
        provisions.Reverse(); // Reverse for compensation order

        var compensatedCount = 0;
        var failedCount = 0;
        var pendingCount = 0;

        foreach (var provision in provisions)
        {
            if (provision.Status == ProvisionStatus.Compensated ||
                provision.Status == ProvisionStatus.ReferenceRegistered)
                continue;

            if (provision.Status == ProvisionStatus.Pending)
            {
                // Resource was never created — mark as compensated directly
                provision.Status = ProvisionStatus.Compensated;
                provision.CompensatedAt = DateTimeOffset.UtcNow;
                await _provisionStore.SaveAsync(BuildProvisionKey(provision.ProvisionId), provision, cancellationToken: ct);
                pendingCount++;
                continue;
            }

            // Status is Provisioned or CompensationFailed — execute compensation
            try
            {
                var compensationApi = BannouJson.Deserialize<PreboundApi>(provision.Compensation);
                if (compensationApi == null)
                {
                    _logger.LogWarning(
                        "Provision {ProvisionId} has invalid compensation definition, marking as failed",
                        provision.ProvisionId);
                    provision.Status = ProvisionStatus.CompensationFailed;
                    provision.CompensationAttempts++;
                    provision.LastCompensationError = "Invalid compensation PreboundApi definition";
                    failedCount++;
                    await _provisionStore.SaveAsync(BuildProvisionKey(provision.ProvisionId), provision, cancellationToken: ct);
                    continue;
                }

                var context = new Dictionary<string, object?>
                {
                    ["provisionResourceId"] = provision.ResourceId.ToString()
                };
                var result = await _navigator.ExecutePreboundApiAsync(compensationApi, context, ct);

                // Apply response transformation if configured
                var transformed = ResponseTransformer.Transform(
                    result.Result.StatusCode, result.Result.ResponseBody,
                    compensationApi.ResponseTransformation);

                // 404 = resource doesn't exist = compensation succeeded (per planning doc)
                if (transformed.IsSuccess || result.Result.StatusCode == 404)
                {
                    provision.Status = ProvisionStatus.Compensated;
                    provision.CompensatedAt = DateTimeOffset.UtcNow;
                    compensatedCount++;
                }
                else
                {
                    provision.Status = ProvisionStatus.CompensationFailed;
                    provision.CompensationAttempts++;
                    provision.LastCompensationError = result.Result.ErrorMessage
                        ?? $"Compensation returned status {result.Result.StatusCode}";
                    failedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Compensation failed for provision {ProvisionId} ({ResourceType}:{ResourceId})",
                    provision.ProvisionId, provision.ResourceType, provision.ResourceId);
                provision.Status = ProvisionStatus.CompensationFailed;
                provision.CompensationAttempts++;
                provision.LastCompensationError = ex.Message;
                failedCount++;
            }

            await _provisionStore.SaveAsync(BuildProvisionKey(provision.ProvisionId), provision, cancellationToken: ct);
        }

        // If all compensated, finalize to Aborted
        if (failedCount == 0)
        {
            transaction.Status = TransactionStatus.Aborted;
            transaction.UpdatedAt = DateTimeOffset.UtcNow;
            await _transactionStore.SaveAsync(BuildTransactionKey(body.TransactionId), transaction, cancellationToken: ct);

            await _messageBus.PublishResourceTransactionAbortedAsync(
                new ResourceTransactionAbortedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    TransactionId = body.TransactionId,
                    OwnerService = transaction.OwnerService,
                    ParentResourceType = transaction.ParentResourceType,
                    ParentResourceId = transaction.ParentResourceId,
                    CompensatedCount = compensatedCount + pendingCount,
                    FailedCount = 0,
                }, ct);
        }

        _logger.LogInformation(
            "Transaction {TransactionId} abort: {Compensated} compensated, {Failed} failed, {Pending} pending (never created)",
            body.TransactionId, compensatedCount, failedCount, pendingCount);

        return (StatusCodes.OK, new AbortTransactionResponse
        {
            TransactionId = body.TransactionId,
            Status = transaction.Status,
            CompensatedCount = compensatedCount,
            FailedCount = failedCount,
            PendingCount = pendingCount
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, TransactionStatusResponse?)> GetTransactionStatusAsync(
        GetTransactionStatusRequest body, CancellationToken ct = default)
    {
        var transaction = await _transactionStore.GetAsync(BuildTransactionKey(body.TransactionId), ct);
        if (transaction == null)
            return (StatusCodes.NotFound, null);

        var provisions = await GetOrderedProvisionsAsync(body.TransactionId, ct);

        return (StatusCodes.OK, new TransactionStatusResponse
        {
            TransactionId = transaction.TransactionId,
            OwnerService = transaction.OwnerService,
            ParentResourceType = transaction.ParentResourceType,
            ParentResourceId = transaction.ParentResourceId,
            Status = transaction.Status,
            CreatedAt = transaction.CreatedAt,
            UpdatedAt = transaction.UpdatedAt,
            TtlSeconds = transaction.TtlSeconds,
            ExpiresAt = transaction.CreatedAt.AddSeconds(transaction.TtlSeconds),
            ExpectedProvisionCount = transaction.ExpectedProvisionCount,
            ValidationAttempts = transaction.ValidationAttempts,
            Provisions = provisions.Select(p => new ProvisionDetail
            {
                ProvisionId = p.ProvisionId,
                SequenceNumber = p.SequenceNumber,
                ResourceType = p.ResourceType,
                ResourceId = p.ResourceId,
                Status = p.Status,
                RegisteredAt = p.RegisteredAt,
                ProvisionedAt = p.ProvisionedAt,
                CompensatedAt = p.CompensatedAt,
                CompensationAttempts = p.CompensationAttempts,
                LastCompensationError = p.LastCompensationError,
            }).ToList()
        });
    }

    // =========================================================================
    // Transaction Internal Helpers
    // =========================================================================

}
