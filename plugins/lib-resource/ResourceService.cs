using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-resource.tests")]

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
/// </remarks>
[BannouService("resource", typeof(IResourceService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.AppFoundation)]
public partial class ResourceService : IResourceService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IServiceNavigator _navigator;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ILogger<ResourceService> _logger;
    private readonly ResourceServiceConfiguration _configuration;

    // State stores - using constants from StateStoreDefinitions
    private readonly ICacheableStateStore<ResourceReferenceEntry> _refStore;
    private readonly IStateStore<CleanupCallbackDefinition> _cleanupStore;
    private readonly IStateStore<GracePeriodRecord> _graceStore;
    private readonly IStateStore<CompressCallbackDefinition> _compressStore;
    private readonly IStateStore<ResourceArchiveModel> _archiveStore;

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
        IEventConsumer eventConsumer)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _navigator = navigator;
        _lockProvider = lockProvider;
        _logger = logger;
        _configuration = configuration;

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
            await _messageBus.TryPublishAsync(
                "resource.grace-period.started",
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
            OnDeleteAction = body.OnDeleteAction ?? OnDeleteAction.CASCADE,
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
            .Where(c => c.OnDeleteAction == OnDeleteAction.RESTRICT)
            .Select(c => c.SourceType)
            .ToHashSet();

        // Handle dry run - return preview without executing
        if (body.DryRun == true)
        {
            stopwatch.Stop();
            var hasRestrict = callbacks.Any(c => c.OnDeleteAction == OnDeleteAction.RESTRICT);

            return (StatusCodes.OK, new ExecuteCleanupResponse
            {
                ResourceType = body.ResourceType,
                ResourceId = body.ResourceId,
                Success = !hasRestrict,
                DryRun = true,
                AbortReason = hasRestrict ? "Would be blocked by RESTRICT policy" : null,
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
                .Where(c => c.OnDeleteAction != OnDeleteAction.RESTRICT)
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
            .Where(c => c.OnDeleteAction != OnDeleteAction.RESTRICT)
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
            var apiDefinitions = executableCallbacks.Select(c => new PreboundApiDefinition
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
                    await _messageBus.TryPublishAsync(
                        "resource.cleanup.callback-failed",
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
        if (failedCallbacks.Count > 0 && cleanupPolicy == CleanupPolicy.ALL_REQUIRED)
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
        var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.ResourceCleanup);

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
            var resourceTypes = await cacheStore.GetSetAsync<string>(
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
        var indexKey = $"callback-index:{body.ResourceType}";
        var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.ResourceCleanup);
        await cacheStore.RemoveFromSetAsync(indexKey, body.SourceType, cancellationToken);

        // Check if resource type has any remaining callbacks
        var remainingSourceTypes = await cacheStore.GetSetAsync<string>(indexKey, cancellationToken);
        if (remainingSourceTypes.Count == 0)
        {
            // Remove from master resource type index
            await cacheStore.RemoveFromSetAsync(
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
                var apiDefinition = new PreboundApiDefinition
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
                    await _messageBus.TryPublishAsync(
                        "resource.compress.callback-failed",
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
                    if (compressionPolicy == CompressionPolicy.ALL_REQUIRED)
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
                await _messageBus.TryPublishAsync(
                    "resource.compress.callback-failed",
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

                if (compressionPolicy == CompressionPolicy.ALL_REQUIRED)
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
                    CleanupPolicy = CleanupPolicy.BEST_EFFORT
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
        await _messageBus.TryPublishAsync(
            "resource.compressed",
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

                var apiDefinition = new PreboundApiDefinition
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

        // Publish decompressed event if any callbacks succeeded
        var anySuccess = callbackResults.Any(r => r.Success);
        if (anySuccess)
        {
            await _messageBus.TryPublishAsync(
                "resource.decompressed",
                new ResourceDecompressedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    ResourceType = body.ResourceType,
                    ResourceId = body.ResourceId,
                    ArchiveId = archive.ArchiveId
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
        var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.ResourceCompress);

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
            var resourceTypes = await cacheStore.GetSetAsync<string>(
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

    // =========================================================================
    // Internal Helpers
    // =========================================================================

    /// <summary>
    /// Builds the Redis key for a resource's reference set.
    /// </summary>
    private static string BuildResourceKey(string resourceType, Guid resourceId)
        => $"{resourceType}:{resourceId}:sources";

    /// <summary>
    /// Builds the Redis key for a resource's grace period record.
    /// </summary>
    private static string BuildGraceKey(string resourceType, Guid resourceId)
        => $"{resourceType}:{resourceId}:grace";

    /// <summary>
    /// Builds the Redis key for a cleanup callback definition.
    /// </summary>
    private static string BuildCleanupKey(string resourceType, string sourceType)
        => $"callback:{resourceType}:{sourceType}";

    /// <summary>
    /// Gets all cleanup callbacks for a resource type.
    /// </summary>
    private async Task<List<CleanupCallbackDefinition>> GetCleanupCallbacksAsync(
        string resourceType,
        CancellationToken cancellationToken)
    {
        // We need to enumerate callbacks for this resource type
        // Since we can't use KEYS/SCAN, we need an index
        // For now, we'll use a known set of source types from the callbacks we've registered
        // This is a simplification - in production, we'd maintain an index

        // Get the callback index for this resource type
        var indexKey = $"callback-index:{resourceType}";
        var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.ResourceCleanup);
        var sourceTypes = await cacheStore.GetSetAsync<string>(indexKey, cancellationToken);

        var callbacks = new List<CleanupCallbackDefinition>();
        foreach (var sourceType in sourceTypes)
        {
            var callback = await _cleanupStore.GetAsync(BuildCleanupKey(resourceType, sourceType), cancellationToken);
            if (callback != null)
            {
                callbacks.Add(callback);
            }
        }

        return callbacks;
    }

    // =========================================================================
    // Compression Management Helpers
    // =========================================================================

    /// <summary>
    /// Key for the master index of all resource types that have compression callbacks.
    /// </summary>
    private const string MasterCompressResourceTypeIndexKey = "compress-callback-resource-types";

    /// <summary>
    /// Builds the Redis key for a compression callback definition.
    /// </summary>
    private static string BuildCompressKey(string resourceType, string sourceType)
        => $"compress-callback:{resourceType}:{sourceType}";

    /// <summary>
    /// Builds the Redis key for the compression callback index.
    /// </summary>
    private static string BuildCompressIndexKey(string resourceType)
        => $"compress-callback-index:{resourceType}";

    /// <summary>
    /// Builds the MySQL key for an archive.
    /// </summary>
    private static string BuildArchiveKey(string resourceType, Guid resourceId)
        => $"archive:{resourceType}:{resourceId}";

    /// <summary>
    /// Gets all compression callbacks for a resource type, sorted by priority.
    /// </summary>
    private async Task<List<CompressCallbackDefinition>> GetCompressCallbacksAsync(
        string resourceType,
        CancellationToken cancellationToken)
    {
        var indexKey = BuildCompressIndexKey(resourceType);
        var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.ResourceCompress);
        var sourceTypes = await cacheStore.GetSetAsync<string>(indexKey, cancellationToken);

        var callbacks = new List<CompressCallbackDefinition>();
        foreach (var sourceType in sourceTypes)
        {
            var callback = await _compressStore.GetAsync(BuildCompressKey(resourceType, sourceType), cancellationToken);
            if (callback != null)
            {
                callbacks.Add(callback);
            }
        }

        // Sort by priority (lower = earlier)
        return callbacks.OrderBy(c => c.Priority).ToList();
    }

    /// <summary>
    /// Maintains the compression callback index when defining callbacks.
    /// </summary>
    private async Task MaintainCompressCallbackIndexAsync(
        string resourceType,
        string sourceType,
        CancellationToken cancellationToken)
    {
        var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.ResourceCompress);

        // Add to per-resource-type index
        var indexKey = BuildCompressIndexKey(resourceType);
        await cacheStore.AddToSetAsync(indexKey, sourceType, cancellationToken: cancellationToken);

        // Add to master resource type index
        await cacheStore.AddToSetAsync(MasterCompressResourceTypeIndexKey, resourceType, cancellationToken: cancellationToken);
    }

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
    /// Decompresses base64-encoded GZip data to JSON string.
    /// </summary>
    private static string DecompressJsonData(string base64CompressedData)
    {
        var compressedBytes = Convert.FromBase64String(base64CompressedData);
        using var input = new MemoryStream(compressedBytes);
        using var gzip = new System.IO.Compression.GZipStream(
            input, System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return System.Text.Encoding.UTF8.GetString(output.ToArray());
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
}

// =========================================================================
// Internal POCOs for State Storage
// =========================================================================

/// <summary>
/// Entry in the reference set for a resource.
/// </summary>
internal class ResourceReferenceEntry
{
    /// <summary>
    /// Type of entity holding the reference (opaque identifier).
    /// </summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>
    /// ID of the entity holding the reference (opaque string, supports non-Guid IDs).
    /// </summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>
    /// When this reference was registered.
    /// </summary>
    public DateTimeOffset RegisteredAt { get; set; }

    /// <summary>
    /// Override equality for set operations.
    /// Two references are equal if they have the same source type and ID.
    /// </summary>
    public override bool Equals(object? obj)
    {
        if (obj is not ResourceReferenceEntry other) return false;
        return SourceType == other.SourceType && SourceId == other.SourceId;
    }

    /// <summary>
    /// Override hash code for set operations.
    /// </summary>
    public override int GetHashCode()
        => HashCode.Combine(SourceType, SourceId);
}

/// <summary>
/// Record of when a resource's refcount became zero.
/// </summary>
internal class GracePeriodRecord
{
    /// <summary>
    /// Type of resource (opaque identifier).
    /// </summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>
    /// ID of the resource.
    /// </summary>
    public Guid ResourceId { get; set; }

    /// <summary>
    /// When the refcount became zero.
    /// </summary>
    public DateTimeOffset ZeroTimestamp { get; set; }
}

/// <summary>
/// Definition of a cleanup callback for a resource type.
/// </summary>
internal class CleanupCallbackDefinition
{
    /// <summary>
    /// Type of resource this cleanup handles (opaque identifier).
    /// </summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>
    /// Type of entity that will be cleaned up (opaque identifier).
    /// </summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>
    /// Action to take when the resource is deleted.
    /// CASCADE (default): Delete dependent entities via callback.
    /// RESTRICT: Block deletion if references of this type exist.
    /// DETACH: Nullify references via callback (consumer implements).
    /// </summary>
    public OnDeleteAction OnDeleteAction { get; set; } = OnDeleteAction.CASCADE;

    /// <summary>
    /// Target service name for callback invocation.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// Endpoint path for callback invocation.
    /// </summary>
    public string CallbackEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// JSON template with {{resourceId}} placeholder.
    /// </summary>
    public string PayloadTemplate { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// When this callback was registered.
    /// </summary>
    public DateTimeOffset RegisteredAt { get; set; }
}

/// <summary>
/// Definition of a compression callback for a resource type.
/// </summary>
internal class CompressCallbackDefinition
{
    /// <summary>
    /// Type of resource this compression handles (opaque identifier).
    /// </summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>
    /// Type of data being compressed (opaque identifier, e.g., "character-personality").
    /// </summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>
    /// Target service name for callback invocation.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// Endpoint path for compression callback invocation.
    /// </summary>
    public string CompressEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// JSON template with {{resourceId}} placeholder for compression.
    /// </summary>
    public string CompressPayloadTemplate { get; set; } = string.Empty;

    /// <summary>
    /// Endpoint path for decompression callback invocation (nullable).
    /// </summary>
    public string? DecompressEndpoint { get; set; }

    /// <summary>
    /// JSON template with {{resourceId}} and {{data}} placeholders for decompression.
    /// </summary>
    public string? DecompressPayloadTemplate { get; set; }

    /// <summary>
    /// Execution order (lower = earlier).
    /// </summary>
    public int Priority { get; set; } = 100;

    /// <summary>
    /// Human-readable description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// When this callback was registered.
    /// </summary>
    public DateTimeOffset RegisteredAt { get; set; }
}

/// <summary>
/// Internal model for archive storage in MySQL.
/// </summary>
internal class ResourceArchiveModel
{
    /// <summary>
    /// Unique identifier for this archive.
    /// </summary>
    public Guid ArchiveId { get; set; }

    /// <summary>
    /// Type of resource archived.
    /// </summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>
    /// ID of the resource archived.
    /// </summary>
    public Guid ResourceId { get; set; }

    /// <summary>
    /// Archive version (increments on re-compression).
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Data entries from each compression callback.
    /// </summary>
    public List<ArchiveEntryModel> Entries { get; set; } = new();

    /// <summary>
    /// When this archive was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Whether original source data was deleted after archival.
    /// </summary>
    public bool SourceDataDeleted { get; set; }
}

/// <summary>
/// Single entry in the archive bundle.
/// </summary>
internal class ArchiveEntryModel
{
    /// <summary>
    /// Type of data (e.g., "character-personality").
    /// </summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>
    /// Service that provided the data.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// Base64-encoded gzipped JSON from the service callback.
    /// </summary>
    public string Data { get; set; } = string.Empty;

    /// <summary>
    /// When this entry was compressed.
    /// </summary>
    public DateTimeOffset CompressedAt { get; set; }

    /// <summary>
    /// SHA256 hash for integrity verification.
    /// </summary>
    public string? DataChecksum { get; set; }

    /// <summary>
    /// Size before compression.
    /// </summary>
    public int? OriginalSizeBytes { get; set; }
}
