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
[BannouService("resource", typeof(IResourceService), lifetime: ServiceLifetime.Scoped)]
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

        if (preCheckResult.RefCount > 0)
        {
            stopwatch.Stop();
            return (StatusCodes.OK, new ExecuteCleanupResponse
            {
                ResourceType = body.ResourceType,
                ResourceId = body.ResourceId,
                Success = false,
                AbortReason = $"Resource has {preCheckResult.RefCount} active reference(s)",
                CallbackResults = new List<CleanupCallbackResult>(),
                CleanupDurationMs = (int)stopwatch.ElapsedMilliseconds
            });
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
                AbortReason = "Failed to acquire cleanup lock (another cleanup in progress?)",
                CallbackResults = new List<CleanupCallbackResult>(),
                CleanupDurationMs = (int)stopwatch.ElapsedMilliseconds
            });
        }

        // Re-validate under lock
        var refCount = await _refStore.SetCountAsync(resourceKey, cancellationToken);
        if (refCount > 0)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "Cleanup aborted: refcount changed to {RefCount} for {ResourceType}:{ResourceId}",
                refCount, body.ResourceType, body.ResourceId);

            return (StatusCodes.OK, new ExecuteCleanupResponse
            {
                ResourceType = body.ResourceType,
                ResourceId = body.ResourceId,
                Success = false,
                AbortReason = $"Reference count changed to {refCount} during cleanup",
                CallbackResults = new List<CleanupCallbackResult>(),
                CleanupDurationMs = (int)stopwatch.ElapsedMilliseconds
            });
        }

        // Get and execute cleanup callbacks
        var callbacks = await GetCleanupCallbacksAsync(body.ResourceType, cancellationToken);
        var context = new Dictionary<string, object?>
        {
            ["resourceId"] = body.ResourceId.ToString(),
            ["resourceType"] = body.ResourceType
        };

        var callbackResults = new List<CleanupCallbackResult>();
        var cleanupPolicy = body.CleanupPolicy ?? _configuration.DefaultCleanupPolicy;

        if (callbacks.Count > 0)
        {
            var apiDefinitions = callbacks.Select(c => new PreboundApiDefinition
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
                var callback = callbacks[i];

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
            body.ResourceType, body.ResourceId, callbacks.Count, stopwatch.ElapsedMilliseconds);

        return (StatusCodes.OK, new ExecuteCleanupResponse
        {
            ResourceType = body.ResourceType,
            ResourceId = body.ResourceId,
            Success = true,
            CallbackResults = callbackResults,
            CleanupDurationMs = (int)stopwatch.ElapsedMilliseconds
        });
    }

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
