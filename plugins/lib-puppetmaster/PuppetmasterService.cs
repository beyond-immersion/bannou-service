using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Puppetmaster.Caching;
using BeyondImmersion.BannouService.Puppetmaster.Watches;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-puppetmaster.tests")]

namespace BeyondImmersion.BannouService.Puppetmaster;

/// <summary>
/// Puppetmaster service - orchestration for dynamic behaviors, regional watchers, and encounters.
/// Pulls the strings while actors perform on stage.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
/// Generated code (event handlers, permissions) is placed in companion partial classes.
/// </para>
/// <para>
/// <b>Phase 2 Implementation:</b>
/// <list type="bullet">
///   <item>BehaviorDocumentCache: In-memory cache for ABML documents loaded from asset service</item>
///   <item>DynamicBehaviorProvider: IBehaviorDocumentProvider implementation (priority 100)</item>
///   <item>Status endpoint: Returns cache count, watcher count (stub), health status</item>
///   <item>Invalidate endpoint: Clears one or all cached behaviors</item>
///   <item>ListWatchers endpoint: Returns active watchers (stub for Phase 2d)</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("puppetmaster", typeof(IPuppetmasterService), lifetime: ServiceLifetime.Singleton, layer: ServiceLayer.GameFeatures)]
public partial class PuppetmasterService : IPuppetmasterService
{
    private readonly IMessageBus _messageBus;
    private readonly IMessageSubscriber _messageSubscriber;
    private readonly ILogger<PuppetmasterService> _logger;
    private readonly PuppetmasterServiceConfiguration _configuration;
    private readonly IBehaviorDocumentCache _behaviorCache;
    private readonly WatchRegistry _watchRegistry;
    private readonly ResourceEventMapping _resourceEventMapping;
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Registry of active watchers indexed by watcher ID.
    /// Thread-safe for multi-instance safety per IMPLEMENTATION TENETS.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, WatcherInfo> _activeWatchers = new();

    /// <summary>
    /// Index of watchers by realm for fast realm-filtered lookups.
    /// Key is (realmId, watcherType), value is watcherId.
    /// </summary>
    private readonly ConcurrentDictionary<(Guid realmId, string watcherType), Guid> _watchersByRealmAndType = new();

    /// <summary>
    /// Creates a new Puppetmaster service instance.
    /// </summary>
    /// <param name="messageBus">Message bus for event publishing.</param>
    /// <param name="messageSubscriber">Message subscriber for lifecycle events.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Service configuration.</param>
    /// <param name="behaviorCache">Behavior document cache.</param>
    /// <param name="watchRegistry">Watch registry for resource subscriptions.</param>
    /// <param name="resourceEventMapping">Resource event topic mapping.</param>
    /// <param name="scopeFactory">Service scope factory for IActorClient access.</param>
    /// <param name="eventConsumer">Event consumer for pub/sub fan-out.</param>
    public PuppetmasterService(
        IMessageBus messageBus,
        IMessageSubscriber messageSubscriber,
        ILogger<PuppetmasterService> logger,
        PuppetmasterServiceConfiguration configuration,
        IBehaviorDocumentCache behaviorCache,
        WatchRegistry watchRegistry,
        ResourceEventMapping resourceEventMapping,
        IServiceScopeFactory scopeFactory,
        IEventConsumer eventConsumer)
    {
        _messageBus = messageBus;
        _messageSubscriber = messageSubscriber;
        _logger = logger;
        _configuration = configuration;
        _behaviorCache = behaviorCache;
        _watchRegistry = watchRegistry;
        _resourceEventMapping = resourceEventMapping;
        _scopeFactory = scopeFactory;

        // Register event handlers via partial class (PuppetmasterServiceEvents.cs)
        RegisterEventConsumers(eventConsumer);
    }

    /// <summary>
    /// Gets the service status and statistics.
    /// </summary>
    /// <param name="body">Status request (currently empty, reserved for future filters).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Service status response.</returns>
    public Task<(StatusCodes, PuppetmasterStatusResponse?)> GetStatusAsync(
        GetStatusRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting puppetmaster status");

        var response = new PuppetmasterStatusResponse
        {
            CachedBehaviorCount = _behaviorCache.CachedCount,
            ActiveWatcherCount = _activeWatchers.Count,
            IsHealthy = true
        };

        return Task.FromResult<(StatusCodes, PuppetmasterStatusResponse?)>((StatusCodes.OK, response));
    }

    /// <summary>
    /// Invalidates cached behavior documents.
    /// </summary>
    /// <param name="body">Invalidation request with optional specific behavior reference.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Invalidation result with count of invalidated behaviors.</returns>
    public async Task<(StatusCodes, InvalidateBehaviorsResponse?)> InvalidateBehaviorsAsync(
        InvalidateBehaviorsRequest body,
        CancellationToken cancellationToken)
    {
        int invalidatedCount;

        if (string.IsNullOrWhiteSpace(body.BehaviorRef))
        {
            _logger.LogInformation("Invalidating all cached behaviors");
            invalidatedCount = _behaviorCache.InvalidateAll();
        }
        else
        {
            _logger.LogInformation("Invalidating behavior {BehaviorRef}", body.BehaviorRef);
            invalidatedCount = _behaviorCache.Invalidate(body.BehaviorRef) ? 1 : 0;
        }

        // Publish invalidation event
        var evt = new BehaviorInvalidatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            BehaviorRef = body.BehaviorRef,
            InvalidatedCount = invalidatedCount
        };
        await _messageBus.TryPublishAsync(
            "puppetmaster.behavior.invalidated",
            evt,
            cancellationToken: cancellationToken);

        var response = new InvalidateBehaviorsResponse
        {
            InvalidatedCount = invalidatedCount
        };

        return (StatusCodes.OK, response);
    }

    /// <summary>
    /// Lists active regional watchers.
    /// </summary>
    /// <param name="body">List request with optional realm filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of active watchers.</returns>
    public Task<(StatusCodes, ListWatchersResponse?)> ListWatchersAsync(
        ListWatchersRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing watchers (realmId={RealmId})", body.RealmId);

        var watchers = _activeWatchers.Values
            .Where(w => body.RealmId == null || w.RealmId == body.RealmId)
            .ToList();

        var response = new ListWatchersResponse
        {
            Watchers = watchers
        };

        return Task.FromResult<(StatusCodes, ListWatchersResponse?)>((StatusCodes.OK, response));
    }

    /// <summary>
    /// Starts a regional watcher for the specified realm.
    /// </summary>
    /// <param name="body">Start watcher request with realm ID and watcher type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response with watcher info and whether it already existed.</returns>
    public async Task<(StatusCodes, StartWatcherResponse?)> StartWatcherAsync(
        StartWatcherRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Starting watcher for realm {RealmId}, type {WatcherType}",
            body.RealmId,
            body.WatcherType);

        // Check if watcher already exists for this realm and type
        var watcherKey = (body.RealmId, body.WatcherType);
        if (_watchersByRealmAndType.TryGetValue(watcherKey, out var existingWatcherId))
        {
            if (_activeWatchers.TryGetValue(existingWatcherId, out var existingWatcher))
            {
                _logger.LogDebug(
                    "Watcher already exists for realm {RealmId}, type {WatcherType}",
                    body.RealmId,
                    body.WatcherType);

                return (StatusCodes.OK, new StartWatcherResponse
                {
                    Watcher = existingWatcher,
                    AlreadyExisted = true
                });
            }
        }

        // Create new watcher
        var watcherId = Guid.NewGuid();
        var watcher = new WatcherInfo
        {
            WatcherId = watcherId,
            RealmId = body.RealmId,
            WatcherType = body.WatcherType,
            StartedAt = DateTimeOffset.UtcNow,
            BehaviorRef = body.BehaviorRef,
            ActorId = null  // TODO: Spawn actor when actor service integration is ready
        };

        // Register in both indexes
        _activeWatchers[watcherId] = watcher;
        _watchersByRealmAndType[watcherKey] = watcherId;

        // Publish watcher started event
        var evt = new WatcherStartedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            WatcherId = watcherId,
            RealmId = body.RealmId,
            WatcherType = body.WatcherType,
            BehaviorRef = body.BehaviorRef,
            ActorId = null
        };
        await _messageBus.TryPublishAsync(
            "puppetmaster.watcher.started",
            evt,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Started watcher {WatcherId} for realm {RealmId}, type {WatcherType}",
            watcherId,
            body.RealmId,
            body.WatcherType);

        return (StatusCodes.OK, new StartWatcherResponse
        {
            Watcher = watcher,
            AlreadyExisted = false
        });
    }

    /// <summary>
    /// Stops a regional watcher by its ID.
    /// </summary>
    /// <param name="body">Stop watcher request with watcher ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response indicating whether the watcher was stopped.</returns>
    public async Task<(StatusCodes, StopWatcherResponse?)> StopWatcherAsync(
        StopWatcherRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping watcher {WatcherId}", body.WatcherId);

        if (!_activeWatchers.TryRemove(body.WatcherId, out var watcher))
        {
            _logger.LogWarning("Watcher {WatcherId} not found", body.WatcherId);
            return (StatusCodes.OK, new StopWatcherResponse { Stopped = false });
        }

        // Remove from realm/type index
        var watcherKey = (watcher.RealmId, watcher.WatcherType);
        _watchersByRealmAndType.TryRemove(watcherKey, out _);

        // Publish watcher stopped event
        var evt = new WatcherStoppedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            WatcherId = body.WatcherId,
            RealmId = watcher.RealmId,
            WatcherType = watcher.WatcherType,
            Reason = "manual"
        };
        await _messageBus.TryPublishAsync(
            "puppetmaster.watcher.stopped",
            evt,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Stopped watcher {WatcherId} for realm {RealmId}, type {WatcherType}",
            body.WatcherId,
            watcher.RealmId,
            watcher.WatcherType);

        return (StatusCodes.OK, new StopWatcherResponse { Stopped = true });
    }

    /// <summary>
    /// Starts all relevant watchers for a realm.
    /// </summary>
    /// <param name="body">Request with realm ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response with count of started watchers and list of all active watchers for the realm.</returns>
    public async Task<(StatusCodes, StartWatchersForRealmResponse?)> StartWatchersForRealmAsync(
        StartWatchersForRealmRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting all watchers for realm {RealmId}", body.RealmId);

        var defaultWatcherTypes = _configuration.DefaultWatcherTypes;

        var watchersStarted = 0;
        var watchersExisted = 0;
        var resultWatchers = new List<WatcherInfo>();

        foreach (var watcherType in defaultWatcherTypes)
        {
            var (status, response) = await StartWatcherAsync(
                new StartWatcherRequest
                {
                    RealmId = body.RealmId,
                    WatcherType = watcherType,
                    BehaviorRef = null  // Use default behavior for type
                },
                cancellationToken);

            if (status == StatusCodes.OK && response != null)
            {
                resultWatchers.Add(response.Watcher);
                if (response.AlreadyExisted)
                {
                    watchersExisted++;
                }
                else
                {
                    watchersStarted++;
                }
            }
        }

        _logger.LogInformation(
            "Started {Started} watchers, {Existed} already existed for realm {RealmId}",
            watchersStarted,
            watchersExisted,
            body.RealmId);

        return (StatusCodes.OK, new StartWatchersForRealmResponse
        {
            WatchersStarted = watchersStarted,
            WatchersExisted = watchersExisted,
            Watchers = resultWatchers
        });
    }
}
