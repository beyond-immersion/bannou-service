using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Puppetmaster.Caching;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<PuppetmasterService> _logger;
    private readonly PuppetmasterServiceConfiguration _configuration;
    private readonly BehaviorDocumentCache _behaviorCache;

    // TODO: Phase 2d - Add watcher registry for self-orchestration
    // private readonly ConcurrentDictionary<Guid, WatcherInfo> _activeWatchers = new();

    /// <summary>
    /// Creates a new Puppetmaster service instance.
    /// </summary>
    /// <param name="messageBus">Message bus for event publishing.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Service configuration.</param>
    /// <param name="behaviorCache">Behavior document cache.</param>
    public PuppetmasterService(
        IMessageBus messageBus,
        ILogger<PuppetmasterService> logger,
        PuppetmasterServiceConfiguration configuration,
        BehaviorDocumentCache behaviorCache)
    {
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        _behaviorCache = behaviorCache;
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
            ActiveWatcherCount = 0,  // TODO: Phase 2d - return _activeWatchers.Count
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
    /// <returns>List of active watchers (currently stub returning empty list).</returns>
    public Task<(StatusCodes, ListWatchersResponse?)> ListWatchersAsync(
        ListWatchersRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing watchers (realmId={RealmId})", body.RealmId);

        // TODO: Phase 2d - Implement watcher tracking
        // var watchers = _activeWatchers.Values
        //     .Where(w => body.RealmId == null || w.RealmId == body.RealmId)
        //     .ToList();

        var response = new ListWatchersResponse
        {
            Watchers = new List<WatcherInfo>()  // Empty until Phase 2d
        };

        return Task.FromResult<(StatusCodes, ListWatchersResponse?)>((StatusCodes.OK, response));
    }
}
