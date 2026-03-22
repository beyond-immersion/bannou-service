using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Mapping.Helpers;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

[assembly: InternalsVisibleTo("lib-mapping.tests")]

namespace BeyondImmersion.BannouService.Mapping;

/// <summary>
/// Implementation of the Mapping service.
/// Manages spatial data for game worlds including authority management,
/// high-throughput publishing, spatial queries, and affordance queries.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
/// Generated code (event handlers, permissions) is placed in companion partial classes.
/// </para>
/// <para>
/// Standard structure:
/// <list type="bullet">
///   <item>MappingService.cs (this file) - Business logic</item>
///   <item>MappingServiceEvents.cs - Event consumer handlers (generated)</item>
///   <item>Generated/MappingPermissionRegistration.cs - Permission registration (generated)</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("mapping", typeof(IMappingService), lifetime: ServiceLifetime.Scoped)]
public partial class MappingService : IMappingService
{
    private readonly IMessageBus _messageBus;
    private readonly IMessageSubscriber _messageSubscriber;
    private readonly ILogger<MappingService> _logger;
    private readonly MappingServiceConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAffordanceScorer _affordanceScorer;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>State store for channel records (authority management, channel metadata).</summary>
    private readonly IStateStore<ChannelRecord> _channelStore;

    /// <summary>State store for authority records (authority tokens, expiry tracking).</summary>
    private readonly IStateStore<AuthorityRecord> _authorityStore;

    /// <summary>State store for map objects (spatial entities with position/bounds).</summary>
    private readonly IStateStore<MapObject> _objectStore;

    /// <summary>Cacheable store for GUID set indexes (spatial, type, and region indexes).</summary>
    private readonly ICacheableStateStore<MapObject> _indexStore;

    /// <summary>Redis operations for atomic version counters.</summary>
    private readonly IRedisOperations? _redisOps;

    /// <summary>State store for version counters (InMemory fallback only).</summary>
    private readonly IStateStore<LongWrapper> _versionStore;

    /// <summary>State store for checkout records (authoring lock management).</summary>
    private readonly IStateStore<CheckoutRecord> _checkoutStore;

    /// <summary>State store for definition index entries (definition ID tracking).</summary>
    private readonly IStateStore<DefinitionIndexEntry> _definitionIndexStore;

    /// <summary>State store for definition records (map definition CRUD).</summary>
    private readonly IStateStore<DefinitionRecord> _definitionStore;

    /// <summary>State store for cached affordance query results.</summary>
    private readonly IStateStore<CachedAffordanceResult> _affordanceCacheStore;

    // Track active ingest subscriptions per channel
    private static readonly ConcurrentDictionary<Guid, IAsyncDisposable> IngestSubscriptions = new();

    // Event aggregation buffers per channel - used to coalesce rapid updates
    private static readonly ConcurrentDictionary<Guid, EventAggregationBuffer> EventAggregationBuffers = new();

    // Key prefixes for state storage
    private const string CHANNEL_PREFIX = "map:channel:";
    private const string AUTHORITY_PREFIX = "map:authority:";
    private const string OBJECT_PREFIX = "map:object:";
    private const string SPATIAL_INDEX_PREFIX = "map:index:";
    private const string TYPE_INDEX_PREFIX = "map:type-index:";
    private const string CHECKOUT_PREFIX = "map:checkout:";
    private const string VERSION_PREFIX = "map:version:";
    private const string AFFORDANCE_CACHE_PREFIX = "map:affordance-cache:";
    private const string DEFINITION_PREFIX = "map:definition:";
    private const string REGION_INDEX_PREFIX = "map:region-index:";
    private const string DEFINITION_INDEX_KEY = "map:definition-index";

    /// <summary>
    /// Initializes a new instance of the MappingService.
    /// </summary>
    /// <param name="messageBus">Message bus for event publishing.</param>
    /// <param name="messageSubscriber">Message subscriber for event subscriptions.</param>
    /// <param name="stateStoreFactory">Factory for creating state stores.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Service configuration.</param>
    /// <param name="eventConsumer">Event consumer for registering handlers.</param>
    /// <param name="serviceProvider">Service provider for resolving optional L3 dependencies.</param>
    /// <param name="httpClientFactory">HTTP client factory for uploading to presigned URLs.</param>
    /// <param name="affordanceScorer">Affordance scoring helper for affordance queries.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    public MappingService(
        IMessageBus messageBus,
        IMessageSubscriber messageSubscriber,
        IStateStoreFactory stateStoreFactory,
        ILogger<MappingService> logger,
        MappingServiceConfiguration configuration,
        IEventConsumer eventConsumer,
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        IAffordanceScorer affordanceScorer,
        ITelemetryProvider telemetryProvider)
    {
        _messageBus = messageBus;
        _messageSubscriber = messageSubscriber;
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _affordanceScorer = affordanceScorer;
        _telemetryProvider = telemetryProvider;

        // Constructor-cache all state store references per FOUNDATION TENETS
        _channelStore = stateStoreFactory.GetStore<ChannelRecord>(StateStoreDefinitions.Mapping);
        _authorityStore = stateStoreFactory.GetStore<AuthorityRecord>(StateStoreDefinitions.Mapping);
        _objectStore = stateStoreFactory.GetStore<MapObject>(StateStoreDefinitions.Mapping);
        _indexStore = stateStoreFactory.GetCacheableStore<MapObject>(StateStoreDefinitions.Mapping);
        _redisOps = stateStoreFactory.GetRedisOperations();
        _versionStore = stateStoreFactory.GetStore<LongWrapper>(StateStoreDefinitions.Mapping);
        _checkoutStore = stateStoreFactory.GetStore<CheckoutRecord>(StateStoreDefinitions.Mapping);
        _definitionIndexStore = stateStoreFactory.GetStore<DefinitionIndexEntry>(StateStoreDefinitions.Mapping);
        _definitionStore = stateStoreFactory.GetStore<DefinitionRecord>(StateStoreDefinitions.Mapping);
        _affordanceCacheStore = stateStoreFactory.GetStore<CachedAffordanceResult>(StateStoreDefinitions.Mapping);

        // Register event handlers via partial class (MappingServiceEvents.cs)
        RegisterEventConsumers(eventConsumer);
    }

    #region Key Building Helpers

    internal static string BuildChannelKey(Guid channelId) => $"{CHANNEL_PREFIX}{channelId}";
    internal static string BuildAuthorityKey(Guid channelId) => $"{AUTHORITY_PREFIX}{channelId}";
    internal static string BuildObjectKey(Guid regionId, Guid objectId) => $"{OBJECT_PREFIX}{regionId}:{objectId}";
    internal static string BuildSpatialIndexKey(Guid regionId, MapKind kind, int cellX, int cellY, int cellZ) =>
        $"{SPATIAL_INDEX_PREFIX}{regionId}:{kind}:{cellX}_{cellY}_{cellZ}";
    internal static string BuildTypeIndexKey(Guid regionId, string objectType) =>
        $"{TYPE_INDEX_PREFIX}{regionId}:{objectType}";
    internal static string BuildCheckoutKey(Guid regionId, MapKind kind) => $"{CHECKOUT_PREFIX}{regionId}:{kind}";
    internal static string BuildVersionKey(Guid channelId) => $"{VERSION_PREFIX}{channelId}";
    internal static string BuildAffordanceCacheKey(Guid regionId, AffordanceType type, string boundsHash) =>
        $"{AFFORDANCE_CACHE_PREFIX}{regionId}:{type}:{boundsHash}";
    internal static string BuildDefinitionKey(Guid definitionId) => $"{DEFINITION_PREFIX}{definitionId}";

    private (int cellX, int cellY, int cellZ) GetCellCoordinates(Position3D position)
    {
        var cellSize = _configuration.DefaultSpatialCellSize;
        return (
            (int)Math.Floor(position.X / cellSize),
            (int)Math.Floor(position.Y / cellSize),
            (int)Math.Floor(position.Z / cellSize)
        );
    }

    private List<(int cellX, int cellY, int cellZ)> GetCellsForBounds(Bounds bounds)
    {
        var cellSize = _configuration.DefaultSpatialCellSize;
        var minCell = GetCellCoordinates(bounds.Min);
        var maxCell = GetCellCoordinates(bounds.Max);

        var cells = new List<(int, int, int)>();
        for (var x = minCell.cellX; x <= maxCell.cellX; x++)
        {
            for (var y = minCell.cellY; y <= maxCell.cellY; y++)
            {
                for (var z = minCell.cellZ; z <= maxCell.cellZ; z++)
                {
                    cells.Add((x, y, z));
                }
            }
        }
        return cells;
    }

    /// <summary>
    /// Gets the configured TTL in seconds for the specified MapKind.
    /// Returns null for durable kinds (terrain, static_geometry, navigation, ownership).
    /// </summary>
    /// <param name="kind">The map kind.</param>
    /// <returns>TTL in seconds, or null if no TTL (durable storage).</returns>
    private int? GetTtlSecondsForKind(MapKind kind)
    {
        var ttl = kind switch
        {
            MapKind.Terrain => _configuration.TtlTerrain,
            MapKind.StaticGeometry => _configuration.TtlStaticGeometry,
            MapKind.Navigation => _configuration.TtlNavigation,
            MapKind.Resources => _configuration.TtlResources,
            MapKind.SpawnPoints => _configuration.TtlSpawnPoints,
            MapKind.PointsOfInterest => _configuration.TtlPointsOfInterest,
            MapKind.DynamicObjects => _configuration.TtlDynamicObjects,
            MapKind.Hazards => _configuration.TtlHazards,
            MapKind.WeatherEffects => _configuration.TtlWeatherEffects,
            MapKind.Ownership => _configuration.TtlOwnership,
            MapKind.CombatEffects => _configuration.TtlCombatEffects,
            MapKind.VisualEffects => _configuration.TtlVisualEffects,
            _ => _configuration.DefaultLayerCacheTtlSeconds
        };

        // -1 means no TTL (durable), 0 means use default
        if (ttl == -1) return null;
        if (ttl == 0) return _configuration.DefaultLayerCacheTtlSeconds;
        return ttl;
    }

    /// <summary>
    /// Gets StateOptions with the appropriate TTL for the specified MapKind.
    /// Returns null if no TTL is needed (durable kinds).
    /// </summary>
    /// <param name="kind">The map kind.</param>
    /// <returns>StateOptions with TTL, or null for durable kinds.</returns>
    private StateOptions? GetStateOptionsForKind(MapKind kind)
    {
        var ttl = GetTtlSecondsForKind(kind);
        return ttl.HasValue ? new StateOptions { Ttl = ttl.Value } : null;
    }

    #endregion

    #region Authority Token Generation

    private static string GenerateAuthorityToken(Guid channelId, DateTimeOffset expiresAt)
    {
        // Generate a cryptographically secure token that includes channel ID and expiration
        var randomBytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }

        var tokenData = $"{channelId}:{expiresAt:O}:{Convert.ToBase64String(randomBytes)}";
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(tokenData));
    }

    private static (Guid? channelId, DateTimeOffset? expiresAt) ParseAuthorityToken(string token)
    {
        try
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = decoded.Split(':');
            if (parts.Length < 2)
            {
                return (null, null);
            }

            if (!Guid.TryParse(parts[0], out var channelId))
            {
                return (null, null);
            }

            if (!DateTimeOffset.TryParse(parts[1], out var expiresAt))
            {
                return (null, null);
            }

            return (channelId, expiresAt);
        }
        catch
        {
            // Intentionally swallowing: any decode/parse exception means token is invalid
            // Callers handle the (null, _) return and log appropriately
            return (null, null);
        }
    }

    #endregion

    #region Authority Management

    /// <inheritdoc />
    public async Task<(StatusCodes, AuthorityGrant?)> CreateChannelAsync(CreateChannelRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Creating channel for region {RegionId}, kind {Kind}", body.RegionId, body.Kind);

        {
            // Generate channel ID from region + kind (deterministic)
            var channelId = GenerateChannelId(body.RegionId, body.Kind);
            var channelKey = BuildChannelKey(channelId);

            // Check if channel already exists
            var existingChannel = await _channelStore
                .GetAsync(channelKey, cancellationToken);

            // Determine takeover mode (default to preserve_and_diff)
            var takeoverMode = body.TakeoverMode;

            if (existingChannel != null)
            {
                // Channel exists - check if authority is available
                var authorityKey = BuildAuthorityKey(channelId);
                var existingAuthority = await _authorityStore
                    .GetAsync(authorityKey, cancellationToken);

                if (existingAuthority != null && existingAuthority.ExpiresAt > DateTimeOffset.UtcNow)
                {
                    _logger.LogWarning("Channel {ChannelId} already has active authority", channelId);
                    return (StatusCodes.Conflict, null);
                }

                // Authority expired - apply takeover policy
                _logger.LogInformation("Authority expired for channel {ChannelId}, applying takeover mode {TakeoverMode}",
                    channelId, takeoverMode);

                if (takeoverMode == AuthorityTakeoverMode.Reset)
                {
                    // Clear all channel data before new authority takes over
                    await ClearChannelDataAsync(channelId, body.RegionId, body.Kind, cancellationToken);
                    _logger.LogInformation("Cleared channel data for reset takeover on channel {ChannelId}", channelId);
                }
            }

            var now = DateTimeOffset.UtcNow;
            var expiresAt = now.AddSeconds(_configuration.AuthorityTimeoutSeconds);
            var authorityToken = GenerateAuthorityToken(channelId, expiresAt);
            var ingestTopic = $"map.ingest.{channelId}";

            // Create or update channel record
            var channel = new ChannelRecord
            {
                ChannelId = channelId,
                RegionId = body.RegionId,
                Kind = body.Kind,
                NonAuthorityHandling = body.NonAuthorityHandling,
                TakeoverMode = takeoverMode,
                AlertConfig = body.AlertConfig,
                Version = 1,
                CreatedAt = existingChannel?.CreatedAt ?? now,
                UpdatedAt = now
            };

            await _channelStore
                .SaveAsync(channelKey, channel, cancellationToken: cancellationToken);

            // Create authority record with require_consume flag if needed
            var requiresConsume = takeoverMode == AuthorityTakeoverMode.RequireConsume && existingChannel != null;
            var authority = new AuthorityRecord
            {
                ChannelId = channelId,
                AuthorityToken = authorityToken,
                AuthorityAppId = body.SourceAppId,
                ExpiresAt = expiresAt,
                CreatedAt = now,
                RequiresConsumeBeforePublish = requiresConsume
            };

            var authorityRecordKey = BuildAuthorityKey(channelId);
            await _authorityStore
                .SaveAsync(authorityRecordKey, authority, cancellationToken: cancellationToken);

            // Initialize version counter
            var versionKey = BuildVersionKey(channelId);
            await _versionStore
                .SaveAsync(versionKey, new LongWrapper { Value = 1L }, cancellationToken: cancellationToken);

            // Process initial snapshot if provided
            if (body.InitialSnapshot != null && body.InitialSnapshot.Count > 0)
            {
                await ProcessPayloadsAsync(channelId, body.RegionId, body.Kind, body.InitialSnapshot, cancellationToken);
            }

            // Subscribe to ingest topic for this channel
            await SubscribeToIngestTopicAsync(channelId, ingestTopic, cancellationToken);

            // Publish channel lifecycle event
            if (existingChannel != null)
            {
                await PublishChannelUpdatedEventAsync(channel, existingChannel, cancellationToken);
            }
            else
            {
                await PublishChannelCreatedEventAsync(channel, cancellationToken);
            }

            // Publish authority granted event
            await _messageBus.PublishMappingAuthorityGrantedAsync(new MappingAuthorityGrantedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ChannelId = channelId,
                RegionId = body.RegionId,
                Kind = body.Kind,
                AuthorityAppId = body.SourceAppId,
                ExpiresAt = expiresAt,
                IsNewChannel = existingChannel == null
            }, cancellationToken);

            _logger.LogInformation("Created channel {ChannelId} for region {RegionId}, kind {Kind}",
                channelId, body.RegionId, body.Kind);

            return (StatusCodes.OK, new AuthorityGrant
            {
                ChannelId = channelId,
                AuthorityToken = authorityToken,
                IngestTopic = ingestTopic,
                ExpiresAt = expiresAt,
                RegionId = body.RegionId,
                Kind = body.Kind
            });
        }
    }

    /// <inheritdoc />
    public async Task<StatusCodes> DeleteChannelAsync(DeleteChannelRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting channel {ChannelId}", body.ChannelId);

        var channelKey = BuildChannelKey(body.ChannelId);
        var channel = await _channelStore
            .GetAsync(channelKey, cancellationToken);

        if (channel == null)
        {
            _logger.LogWarning("Channel {ChannelId} not found for deletion", body.ChannelId);
            return StatusCodes.NotFound;
        }

        // Check for active authority — must release first
        var authorityKey = BuildAuthorityKey(body.ChannelId);
        var authority = await _authorityStore
            .GetAsync(authorityKey, cancellationToken);

        if (authority != null && authority.ExpiresAt > DateTimeOffset.UtcNow)
        {
            _logger.LogWarning("Channel {ChannelId} has active authority, release before deleting", body.ChannelId);
            return StatusCodes.Conflict;
        }

        // Clear all spatial data, indexes, and objects
        await ClearChannelDataAsync(body.ChannelId, channel.RegionId, channel.Kind, cancellationToken);

        // Delete channel record
        await _channelStore
            .DeleteAsync(channelKey, cancellationToken);

        // Delete authority record (may be expired but still stored)
        if (authority != null)
        {
            await _authorityStore
                .DeleteAsync(authorityKey, cancellationToken);
        }

        // Delete version counter
        var versionKey = BuildVersionKey(body.ChannelId);
        await _versionStore
            .DeleteAsync(versionKey, cancellationToken);

        // Dispose ingest subscription if still active
        if (IngestSubscriptions.TryRemove(body.ChannelId, out var subscription))
        {
            await subscription.DisposeAsync();
        }

        // Dispose event aggregation buffer if active
        if (EventAggregationBuffers.TryRemove(body.ChannelId, out var buffer))
        {
            buffer.Dispose();
        }

        // Publish channel deleted event
        await _messageBus.PublishMappingChannelDeletedAsync(new MappingChannelDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ChannelId = body.ChannelId,
            RegionId = channel.RegionId,
            Kind = channel.Kind,
            AuthorityAppId = authority?.AuthorityAppId,
            AuthorityToken = authority?.AuthorityToken,
            AuthorityExpiresAt = authority?.ExpiresAt,
            NonAuthorityHandling = channel.NonAuthorityHandling,
            Version = channel.Version,
            CreatedAt = channel.CreatedAt,
            UpdatedAt = channel.UpdatedAt
        }, cancellationToken);

        _logger.LogInformation("Deleted channel {ChannelId} for region {RegionId}, kind {Kind}",
            body.ChannelId, channel.RegionId, channel.Kind);

        return StatusCodes.OK;
    }

    /// <inheritdoc />
    public async Task<StatusCodes> ReleaseAuthorityAsync(ReleaseAuthorityRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Releasing authority for channel {ChannelId}", body.ChannelId);

        {
            var (tokenChannelId, _) = ParseAuthorityToken(body.AuthorityToken);
            if (tokenChannelId == null || tokenChannelId != body.ChannelId)
            {
                _logger.LogWarning("Invalid authority token for channel {ChannelId}", body.ChannelId);
                return StatusCodes.Unauthorized;
            }

            var authorityKey = BuildAuthorityKey(body.ChannelId);
            var authority = await _authorityStore
                .GetAsync(authorityKey, cancellationToken);

            if (authority == null || authority.AuthorityToken != body.AuthorityToken)
            {
                _logger.LogWarning("Authority token mismatch for channel {ChannelId}", body.ChannelId);
                return StatusCodes.Unauthorized;
            }

            // Delete authority record
            await _authorityStore
                .DeleteAsync(authorityKey, cancellationToken);

            // Unsubscribe from ingest topic
            if (IngestSubscriptions.TryRemove(body.ChannelId, out var subscription))
            {
                await subscription.DisposeAsync();
            }

            // Publish authority released event
            var channelKey = BuildChannelKey(body.ChannelId);
            var channel = await _channelStore
                .GetAsync(channelKey, cancellationToken);
            await _messageBus.PublishMappingAuthorityReleasedAsync(new MappingAuthorityReleasedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ChannelId = body.ChannelId,
                RegionId = channel?.RegionId,
                Kind = channel?.Kind,
                AuthorityAppId = authority.AuthorityAppId
            }, cancellationToken);

            _logger.LogInformation("Released authority for channel {ChannelId}", body.ChannelId);
            return StatusCodes.OK;
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, AuthorityHeartbeatResponse?)> AuthorityHeartbeatAsync(AuthorityHeartbeatRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Processing heartbeat for channel {ChannelId}", body.ChannelId);

        {
            var (tokenChannelId, _) = ParseAuthorityToken(body.AuthorityToken);
            if (tokenChannelId == null || tokenChannelId != body.ChannelId)
            {
                _logger.LogWarning("Invalid authority token for heartbeat on channel {ChannelId}", body.ChannelId);
                return (StatusCodes.Unauthorized, null);
            }

            var authorityKey = BuildAuthorityKey(body.ChannelId);
            var authority = await _authorityStore
                .GetAsync(authorityKey, cancellationToken);

            if (authority == null || authority.AuthorityToken != body.AuthorityToken)
            {
                _logger.LogWarning("Authority not found or token mismatch for channel {ChannelId}", body.ChannelId);
                return (StatusCodes.Unauthorized, null);
            }

            // Check if authority has expired
            if (authority.ExpiresAt < DateTimeOffset.UtcNow)
            {
                _logger.LogWarning("Authority has expired for channel {ChannelId}", body.ChannelId);
                return (StatusCodes.Unauthorized, null);
            }

            // Check remaining time before extension to detect late heartbeats
            string? warning = null;
            var remainingBeforeExtend = (authority.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds;
            if (remainingBeforeExtend < _configuration.AuthorityGracePeriodSeconds)
            {
                warning = "Authority expiring soon, increase heartbeat frequency";
            }

            // Extend authority
            var newExpiresAt = DateTimeOffset.UtcNow.AddSeconds(_configuration.AuthorityTimeoutSeconds);
            authority.ExpiresAt = newExpiresAt;

            await _authorityStore
                .SaveAsync(authorityKey, authority, cancellationToken: cancellationToken);

            return (StatusCodes.OK, new AuthorityHeartbeatResponse
            {
                ExpiresAt = newExpiresAt,
                Warning = warning
            });
        }
    }

    #endregion

    #region Publishing

    /// <inheritdoc />
    public async Task<(StatusCodes, PublishMapUpdateResponse?)> PublishMapUpdateAsync(PublishMapUpdateRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Publishing map update to channel {ChannelId}", body.ChannelId);

        // Check payload size limit (MVP: reject large payloads; full impl would use lib-asset)
        var payloadSize = System.Text.Encoding.UTF8.GetByteCount(BannouJson.Serialize(body.Payload));
        if (payloadSize > _configuration.InlinePayloadMaxBytes)
        {
            _logger.LogWarning("Payload size {Size} exceeds limit {Limit} for channel {ChannelId}",
                payloadSize, _configuration.InlinePayloadMaxBytes, body.ChannelId);
            return (StatusCodes.BadRequest, null);
        }

        // Validate authority
        var (isValid, channel, authorityAppId, warning) = await ValidateAuthorityAsync(body.ChannelId, body.AuthorityToken, cancellationToken);

        if (!isValid && channel != null)
        {
            // Handle non-authority publish based on channel config
            return await HandleNonAuthorityPublishAsync(channel, body.Payload, body.SourceAppId, warning, cancellationToken);
        }

        if (!isValid || channel == null)
        {
            _logger.LogWarning("Authority validation failed for channel {ChannelId}: {Warning}", body.ChannelId, warning);
            return (StatusCodes.Unauthorized, null);
        }

        // Process the payload
        var payloads = new List<MapPayload> { body.Payload };
        var version = await ProcessPayloadsAsync(channel.ChannelId, channel.RegionId, channel.Kind, payloads, cancellationToken);

        // Publish update event with authority's app-id as source
        await PublishMapUpdatedEventAsync(channel, body.Bounds, version, body.DeltaType, body.Payload, authorityAppId, cancellationToken);

        return (StatusCodes.OK, new PublishMapUpdateResponse
        {
            Version = version,
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, PublishObjectChangesResponse?)> PublishObjectChangesAsync(PublishObjectChangesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Publishing {Count} object changes to channel {ChannelId}", body.Changes.Count, body.ChannelId);

        // Validate authority
        var (isValid, channel, authorityAppId, warning) = await ValidateAuthorityAsync(body.ChannelId, body.AuthorityToken, cancellationToken);

        if (!isValid && channel != null)
        {
            // Authority invalid but channel exists - apply NonAuthorityHandlingMode
            return await HandleNonAuthorityObjectChangesAsync(channel, body.Changes, warning, cancellationToken);
        }

        if (channel == null)
        {
            _logger.LogDebug("Channel {ChannelId} not found for object changes", body.ChannelId);
            return (StatusCodes.NotFound, null);
        }

        // Authority is valid - process changes normally
        return await ProcessAuthorizedObjectChangesAsync(channel, body.Changes, authorityAppId, cancellationToken);
    }


    /// <inheritdoc />
    public async Task<(StatusCodes, RequestSnapshotResponse?)> RequestSnapshotAsync(RequestSnapshotRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Requesting snapshot for region {RegionId}", body.RegionId);

        {
            var objects = new List<MapObject>();
            var kindsToQuery = body.Kinds ?? Enum.GetValues<MapKind>().ToList();

            foreach (var kind in kindsToQuery)
            {
                var kindObjects = await QueryObjectsInRegionAsync(body.RegionId, kind, body.Bounds, _configuration.MaxObjectsPerQuery, cancellationToken);
                objects.AddRange(kindObjects);
            }

            // Get max version across all channels
            long maxVersion = 0;
            foreach (var kind in kindsToQuery)
            {
                var channelId = GenerateChannelId(body.RegionId, kind);
                var versionKey = BuildVersionKey(channelId);
                var versionWrapper = await _versionStore.GetAsync(versionKey, cancellationToken);
                var version = versionWrapper?.Value ?? 0;
                if (version > maxVersion)
                {
                    maxVersion = version;
                }
            }

            // If authority token provided, clear RequiresConsumeBeforePublish flag
            if (!string.IsNullOrEmpty(body.AuthorityToken))
            {
                await ClearRequiresConsumeForAuthorityAsync(body.RegionId, kindsToQuery, body.AuthorityToken, cancellationToken);
            }

            // Check if payload is too large for inline response
            string? payloadRef = null;
            var serializedData = BannouJson.Serialize(objects);
            var dataBytes = System.Text.Encoding.UTF8.GetBytes(serializedData);

            if (dataBytes.Length > _configuration.InlinePayloadMaxBytes)
            {
                _logger.LogDebug("Snapshot size {Size} exceeds inline limit {Limit}, uploading to lib-asset",
                    dataBytes.Length, _configuration.InlinePayloadMaxBytes);

                payloadRef = await UploadLargePayloadToAssetAsync(
                    dataBytes,
                    $"snapshot-{body.RegionId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json",
                    body.RegionId,
                    cancellationToken);

                if (payloadRef != null)
                {
                    // Return ref without inline objects
                    return (StatusCodes.OK, new RequestSnapshotResponse
                    {
                        Objects = new List<MapObject>(), // Empty - use payloadRef
                        PayloadRef = payloadRef,
                        Version = maxVersion
                    });
                }

                // Fallback: upload failed, return inline with warning
                _logger.LogWarning("Failed to upload large snapshot to lib-asset, returning inline");
            }

            return (StatusCodes.OK, new RequestSnapshotResponse
            {
                Objects = objects,
                PayloadRef = payloadRef,
                Version = maxVersion
            });
        }
    }

    #endregion

    #region Queries

    /// <inheritdoc />
    public async Task<(StatusCodes, QueryPointResponse?)> QueryPointAsync(QueryPointRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Querying point at ({X}, {Y}, {Z}) in region {RegionId}",
            body.Position.X, body.Position.Y, body.Position.Z, body.RegionId);

        var objects = new List<MapObject>();
        var radius = (float)(body.Radius ?? _configuration.DefaultSpatialCellSize);
        var kindsToQuery = body.Kinds ?? Enum.GetValues<MapKind>().ToList();

        // Calculate cells to query based on position and radius
        var bounds = new Bounds
        {
            Min = new Position3D
            {
                X = body.Position.X - radius,
                Y = body.Position.Y - radius,
                Z = body.Position.Z - radius
            },
            Max = new Position3D
            {
                X = body.Position.X + radius,
                Y = body.Position.Y + radius,
                Z = body.Position.Z + radius
            }
        };

        foreach (var kind in kindsToQuery)
        {
            var kindObjects = await QueryObjectsInBoundsAsync(body.RegionId, kind, bounds, _configuration.MaxObjectsPerQuery, cancellationToken);

            // Filter by actual distance
            foreach (var obj in kindObjects)
            {
                if (obj.Position != null)
                {
                    var distance = Math.Sqrt(
                        Math.Pow(obj.Position.X - body.Position.X, 2) +
                        Math.Pow(obj.Position.Y - body.Position.Y, 2) +
                        Math.Pow(obj.Position.Z - body.Position.Z, 2)
                    );
                    if (distance <= radius)
                    {
                        objects.Add(obj);
                    }
                }
                else if (obj.Bounds != null)
                {
                    // Check if point is within bounds or bounds intersect radius
                    if (BoundsContainsPoint(obj.Bounds, body.Position) ||
                        BoundsIntersectsRadius(obj.Bounds, body.Position, radius))
                    {
                        objects.Add(obj);
                    }
                }
            }
        }

        return (StatusCodes.OK, new QueryPointResponse
        {
            Objects = objects,
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, QueryBoundsResponse?)> QueryBoundsAsync(QueryBoundsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Querying bounds in region {RegionId}", body.RegionId);

        var objects = new List<MapObject>();
        var kindsToQuery = body.Kinds ?? Enum.GetValues<MapKind>().ToList();
        var maxObjects = body.MaxObjects;
        var truncated = false;

        foreach (var kind in kindsToQuery)
        {
            if (objects.Count >= maxObjects)
            {
                truncated = true;
                break;
            }

            var remaining = maxObjects - objects.Count;
            var kindObjects = await QueryObjectsInBoundsAsync(body.RegionId, kind, body.Bounds, remaining, cancellationToken);
            objects.AddRange(kindObjects);

            if (kindObjects.Count >= remaining)
            {
                truncated = true;
            }
        }

        return (StatusCodes.OK, new QueryBoundsResponse
        {
            Objects = objects,
            Truncated = truncated
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, QueryObjectsByTypeResponse?)> QueryObjectsByTypeAsync(QueryObjectsByTypeRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Querying objects of type {ObjectType} in region {RegionId}", body.ObjectType, body.RegionId);

        var typeIndexKey = BuildTypeIndexKey(body.RegionId, body.ObjectType);
        var objectIds = await _indexStore
            .GetSetAsync<Guid>(typeIndexKey, cancellationToken);

        var objects = new List<MapObject>();
        var truncated = false;

        foreach (var objectId in objectIds)
        {
            if (objects.Count >= body.MaxObjects)
            {
                truncated = true;
                break;
            }

            var objectKey = BuildObjectKey(body.RegionId, objectId);
            var obj = await _objectStore
                .GetAsync(objectKey, cancellationToken);

            if (obj != null)
            {
                // Apply bounds filter if specified
                if (body.Bounds != null)
                {
                    if (obj.Position != null && !BoundsContainsPoint(body.Bounds, obj.Position))
                    {
                        continue;
                    }
                    if (obj.Bounds != null && !BoundsIntersect(body.Bounds, obj.Bounds))
                    {
                        continue;
                    }
                }
                objects.Add(obj);
            }
        }

        return (StatusCodes.OK, new QueryObjectsByTypeResponse
        {
            Objects = objects,
            Truncated = truncated
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, AffordanceQueryResponse?)> QueryAffordanceAsync(AffordanceQueryRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Querying affordance {AffordanceType} in region {RegionId}", body.AffordanceType, body.RegionId);
        var stopwatch = Stopwatch.StartNew();

        // Check cache if freshness allows
        if (body.Freshness != AffordanceFreshness.Fresh)
        {
            var cachedResult = await TryGetCachedAffordanceAsync(body, cancellationToken);
            if (cachedResult != null)
            {
                stopwatch.Stop();
                return (StatusCodes.OK, cachedResult);
            }
        }

        // Determine which map kinds to search based on affordance type
        var kindsToSearch = _affordanceScorer.GetKindsForAffordanceType(body.AffordanceType);
        var objectsEvaluated = 0;
        var candidatesGenerated = 0;

        // Gather candidate objects
        var candidates = new List<MapObject>();
        foreach (var kind in kindsToSearch)
        {
            var objects = await QueryObjectsInRegionAsync(body.RegionId, kind, body.Bounds,
                _configuration.MaxAffordanceCandidates, cancellationToken);
            candidates.AddRange(objects);
            objectsEvaluated += objects.Count;
        }

        // Score candidates based on affordance type
        var scoredLocations = new List<AffordanceLocation>();

        foreach (var candidate in candidates)
        {
            candidatesGenerated++;

            // Skip excluded positions
            if (body.ExcludePositions != null && candidate.Position != null)
            {
                var tolerance = _configuration.AffordanceExclusionToleranceUnits;
                var excluded = body.ExcludePositions.Any(p =>
                    Math.Abs(p.X - candidate.Position.X) < tolerance &&
                    Math.Abs(p.Y - candidate.Position.Y) < tolerance &&
                    Math.Abs(p.Z - candidate.Position.Z) < tolerance);
                if (excluded)
                {
                    continue;
                }
            }

            var score = _affordanceScorer.ScoreAffordance(candidate, body.AffordanceType, body.CustomAffordance, body.ActorCapabilities);

            if (score >= body.MinScore)
            {
                scoredLocations.Add(new AffordanceLocation
                {
                    Position = candidate.Position ?? new Position3D { X = 0, Y = 0, Z = 0 },
                    Bounds = candidate.Bounds,
                    Score = score,
                    Features = _affordanceScorer.ExtractFeatures(candidate, body.AffordanceType),
                    ObjectIds = new List<Guid> { candidate.ObjectId }
                });
            }
        }

        // Sort by score descending and take top results
        var results = scoredLocations
            .OrderByDescending(l => l.Score)
            .Take(body.MaxResults)
            .ToList();

        stopwatch.Stop();

        var response = new AffordanceQueryResponse
        {
            Locations = results,
        };

        // Cache result if allowed
        if (body.Freshness != AffordanceFreshness.Fresh)
        {
            await CacheAffordanceResultAsync(body, response, cancellationToken);
        }

        return (StatusCodes.OK, response);
    }

    #endregion

    #region Authoring

    /// <inheritdoc />
    public async Task<(StatusCodes, AuthoringCheckoutResponse?)> CheckoutForAuthoringAsync(AuthoringCheckoutRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Checkout for authoring - region {RegionId}, kind {Kind}, editor {EditorId}",
            body.RegionId, body.Kind, body.EditorId);

        var checkoutKey = BuildCheckoutKey(body.RegionId, body.Kind);
        var existingCheckout = await _checkoutStore
            .GetAsync(checkoutKey, cancellationToken);

        // Check if already locked
        if (existingCheckout != null)
        {
            // Check if lock has expired
            if (existingCheckout.ExpiresAt > DateTimeOffset.UtcNow)
            {
                _logger.LogDebug("Region {RegionId} kind {Kind} already locked by {EditorId}",
                    body.RegionId, body.Kind, existingCheckout.EditorId);
                return (StatusCodes.Conflict, null);
            }
            // Lock expired, we can take it
        }

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddSeconds(_configuration.MaxCheckoutDurationSeconds);
        var channelId = GenerateChannelId(body.RegionId, body.Kind);
        var authorityToken = GenerateAuthorityToken(channelId, expiresAt);

        var checkout = new CheckoutRecord
        {
            RegionId = body.RegionId,
            Kind = body.Kind,
            EditorId = body.EditorId,
            AuthorityToken = authorityToken,
            ExpiresAt = expiresAt,
            CreatedAt = now
        };

        await _checkoutStore
            .SaveAsync(checkoutKey, checkout, cancellationToken: cancellationToken);

        _logger.LogInformation("Checkout acquired for region {RegionId}, kind {Kind} by editor {EditorId}",
            body.RegionId, body.Kind, body.EditorId);

        return (StatusCodes.OK, new AuthoringCheckoutResponse
        {
            AuthorityToken = authorityToken,
            ExpiresAt = expiresAt,
            LockedBy = null,
            LockedAt = null
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, AuthoringCommitResponse?)> CommitAuthoringAsync(AuthoringCommitRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Committing authoring changes - region {RegionId}, kind {Kind}", body.RegionId, body.Kind);

        var checkoutKey = BuildCheckoutKey(body.RegionId, body.Kind);
        var checkout = await _checkoutStore
            .GetAsync(checkoutKey, cancellationToken);

        if (checkout == null || checkout.AuthorityToken != body.AuthorityToken)
        {
            _logger.LogWarning("Invalid authority token for authoring commit on region {RegionId}", body.RegionId);
            return (StatusCodes.Unauthorized, null);
        }

        // Increment version and release lock
        var channelId = GenerateChannelId(body.RegionId, body.Kind);
        var version = await IncrementVersionAsync(channelId, cancellationToken);

        // Release the checkout
        await _checkoutStore
            .DeleteAsync(checkoutKey, cancellationToken);

        _logger.LogInformation("Committed authoring changes for region {RegionId}, kind {Kind}, version {Version}",
            body.RegionId, body.Kind, version);

        return (StatusCodes.OK, new AuthoringCommitResponse
        {
            Version = version
        });
    }

    /// <inheritdoc />
    public async Task<StatusCodes> ReleaseAuthoringAsync(AuthoringReleaseRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Releasing authoring checkout - region {RegionId}, kind {Kind}", body.RegionId, body.Kind);

        var checkoutKey = BuildCheckoutKey(body.RegionId, body.Kind);
        var checkout = await _checkoutStore
            .GetAsync(checkoutKey, cancellationToken);

        if (checkout == null || checkout.AuthorityToken != body.AuthorityToken)
        {
            _logger.LogWarning("Invalid authority token for authoring release on region {RegionId}", body.RegionId);
            return StatusCodes.Unauthorized;
        }

        await _checkoutStore
            .DeleteAsync(checkoutKey, cancellationToken);

        _logger.LogInformation("Released authoring checkout for region {RegionId}, kind {Kind}",
            body.RegionId, body.Kind);

        return StatusCodes.OK;
    }

    #endregion

    #region Definition CRUD

    /// <inheritdoc />
    public async Task<(StatusCodes, MapDefinition?)> CreateDefinitionAsync(CreateDefinitionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Creating map definition: {Name}", body.Name);

        // Check for duplicate name by scanning existing definitions
        var existingIndex = await _definitionIndexStore.GetAsync(DEFINITION_INDEX_KEY, cancellationToken);

        if (existingIndex != null && existingIndex.DefinitionIds.Any())
        {
            foreach (var id in existingIndex.DefinitionIds)
            {
                var existing = await _definitionStore.GetAsync(BuildDefinitionKey(id), cancellationToken);
                if (existing != null && existing.Name.Equals(body.Name, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Definition with name {Name} already exists", body.Name);
                    return (StatusCodes.Conflict, null);
                }
            }
        }

        var now = DateTimeOffset.UtcNow;
        var definitionId = Guid.NewGuid();

        var record = new DefinitionRecord
        {
            DefinitionId = definitionId,
            Name = body.Name,
            Description = body.Description,
            Layers = body.Layers,
            DefaultBounds = body.DefaultBounds,
            Metadata = body.Metadata,
            CreatedAt = now,
            UpdatedAt = null
        };

        var key = BuildDefinitionKey(definitionId);
        await _definitionStore
            .SaveAsync(key, record, cancellationToken: cancellationToken);

        // Update index
        var newIndex = existingIndex ?? new DefinitionIndexEntry { DefinitionIds = new List<Guid>() };
        newIndex.DefinitionIds.Add(definitionId);
        await _definitionIndexStore.SaveAsync(DEFINITION_INDEX_KEY, newIndex, cancellationToken: cancellationToken);

        _logger.LogInformation("Created map definition {DefinitionId} ({Name})", definitionId, body.Name);

        return (StatusCodes.OK, MapRecordToDefinition(record));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, MapDefinition?)> GetDefinitionAsync(GetDefinitionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting map definition: {DefinitionId}", body.DefinitionId);

        var key = BuildDefinitionKey(body.DefinitionId);
        var record = await _definitionStore
            .GetAsync(key, cancellationToken);

        if (record == null)
        {
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, MapRecordToDefinition(record));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ListDefinitionsResponse?)> ListDefinitionsAsync(ListDefinitionsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing map definitions with filter: {Filter}", body.NameFilter);

        var index = await _definitionIndexStore.GetAsync(DEFINITION_INDEX_KEY, cancellationToken);

        var definitions = new List<MapDefinition>();
        var total = 0;

        if (index != null && index.DefinitionIds.Any())
        {
            var allRecords = new List<DefinitionRecord>();

            foreach (var id in index.DefinitionIds)
            {
                var record = await _definitionStore.GetAsync(BuildDefinitionKey(id), cancellationToken);
                if (record != null)
                {
                    // Apply name filter
                    if (string.IsNullOrEmpty(body.NameFilter) ||
                        record.Name.Contains(body.NameFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        allRecords.Add(record);
                    }
                }
            }

            total = allRecords.Count;

            // Apply pagination
            var offset = body.Offset;
            var limit = body.Limit;
            definitions = allRecords
                .OrderBy(r => r.Name)
                .Skip(offset)
                .Take(limit)
                .Select(MapRecordToDefinition)
                .ToList();
        }

        return (StatusCodes.OK, new ListDefinitionsResponse
        {
            Definitions = definitions,
            Total = total,
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, MapDefinition?)> UpdateDefinitionAsync(UpdateDefinitionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Updating map definition: {DefinitionId}", body.DefinitionId);

        var key = BuildDefinitionKey(body.DefinitionId);
        var record = await _definitionStore
            .GetAsync(key, cancellationToken);

        if (record == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Update fields if provided
        if (!string.IsNullOrEmpty(body.Name))
        {
            record.Name = body.Name;
        }
        if (body.Description != null)
        {
            record.Description = body.Description;
        }
        if (body.Layers != null)
        {
            record.Layers = body.Layers;
        }
        if (body.DefaultBounds != null)
        {
            record.DefaultBounds = body.DefaultBounds;
        }
        if (body.Metadata != null)
        {
            record.Metadata = body.Metadata;
        }
        record.UpdatedAt = DateTimeOffset.UtcNow;

        await _definitionStore
            .SaveAsync(key, record, cancellationToken: cancellationToken);

        _logger.LogInformation("Updated map definition {DefinitionId}", body.DefinitionId);

        return (StatusCodes.OK, MapRecordToDefinition(record));
    }

    /// <inheritdoc />
    public async Task<StatusCodes> DeleteDefinitionAsync(DeleteDefinitionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting map definition: {DefinitionId}", body.DefinitionId);

        var key = BuildDefinitionKey(body.DefinitionId);
        var record = await _definitionStore.GetAsync(key, cancellationToken);

        if (record == null)
        {
            _logger.LogDebug("Map definition {DefinitionId} not found for deletion", body.DefinitionId);
            return StatusCodes.NotFound;
        }

        // Delete the definition
        await _definitionStore.DeleteAsync(key, cancellationToken);

        // Update index
        var index = await _definitionIndexStore.GetAsync(DEFINITION_INDEX_KEY, cancellationToken);
        if (index != null)
        {
            index.DefinitionIds.Remove(body.DefinitionId);
            await _definitionIndexStore.SaveAsync(DEFINITION_INDEX_KEY, index, cancellationToken: cancellationToken);
        }

        _logger.LogInformation("Deleted map definition {DefinitionId}", body.DefinitionId);

        return StatusCodes.OK;
    }

    private static MapDefinition MapRecordToDefinition(DefinitionRecord record)
    {
        return new MapDefinition
        {
            DefinitionId = record.DefinitionId,
            Name = record.Name,
            Description = record.Description,
            Layers = record.Layers,
            DefaultBounds = record.DefaultBounds,
            Metadata = record.Metadata,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt
        };
    }

    #endregion




    #region Permission Registration

    #endregion

    #region Internal Records

    internal class ChannelRecord
    {
        public Guid ChannelId { get; set; }
        public Guid RegionId { get; set; }
        public MapKind Kind { get; set; }
        public NonAuthorityHandlingMode NonAuthorityHandling { get; set; }
        public AuthorityTakeoverMode TakeoverMode { get; set; }
        public NonAuthorityAlertConfig? AlertConfig { get; set; }
        public long Version { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    internal class AuthorityRecord
    {
        public Guid ChannelId { get; set; }
        public string AuthorityToken { get; set; } = string.Empty;
        public string AuthorityAppId { get; set; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public bool RequiresConsumeBeforePublish { get; set; }
    }

    internal class DefinitionRecord
    {
        public Guid DefinitionId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public ICollection<LayerDefinition>? Layers { get; set; }
        public Bounds? DefaultBounds { get; set; }
        public object? Metadata { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
    }

    internal class DefinitionIndexEntry
    {
        public List<Guid> DefinitionIds { get; set; } = new();
    }

    internal class CheckoutRecord
    {
        public Guid RegionId { get; set; }
        public MapKind Kind { get; set; }
        public string EditorId { get; set; } = string.Empty;
        public string AuthorityToken { get; set; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    internal class CachedAffordanceResult
    {
        public AffordanceQueryResponse Response { get; set; } = new();
        public DateTimeOffset CachedAt { get; set; }
    }

    /// <summary>
    /// Wrapper class for storing long values in IStateStore (since value types need a class wrapper).
    /// </summary>
    internal class LongWrapper
    {
        public long Value { get; set; }
    }

    /// <summary>
    /// Buffer for aggregating events within a time window before publishing.
    /// Uses a timer to flush events after the configured window expires.
    /// Retries on flush failure with exponential backoff before discarding changes.
    /// </summary>
    internal sealed class EventAggregationBuffer : IDisposable
    {
        private readonly object _lock = new();
        private readonly Timer _flushTimer;
        private readonly Guid _channelId;
        private readonly Func<Guid, List<ObjectChangeRecord>, long, string?, CancellationToken, Task> _flushCallback;
        private readonly Action<Guid> _removeCallback;
        private readonly Func<Guid, int, Exception, Task> _errorCallback;
        private readonly int _maxRetries;
        private List<ObjectChangeRecord> _pendingChanges = new();
        private long _latestVersion;
        private string? _sourceAppId;
        private bool _disposed;

        /// <summary>
        /// Creates a new EventAggregationBuffer with retry support.
        /// </summary>
        /// <param name="channelId">Channel ID for this buffer.</param>
        /// <param name="windowMs">Aggregation window in milliseconds before flushing.</param>
        /// <param name="maxRetries">Maximum flush retry attempts before discarding changes.</param>
        /// <param name="flushCallback">Callback to publish aggregated changes.</param>
        /// <param name="removeCallback">Callback to remove this buffer from the global dictionary.</param>
        /// <param name="errorCallback">Callback to report flush failures (channelId, discardedChangeCount, exception).</param>
        public EventAggregationBuffer(
            Guid channelId,
            int windowMs,
            int maxRetries,
            Func<Guid, List<ObjectChangeRecord>, long, string?, CancellationToken, Task> flushCallback,
            Action<Guid> removeCallback,
            Func<Guid, int, Exception, Task> errorCallback)
        {
            _channelId = channelId;
            _maxRetries = maxRetries;
            _flushCallback = flushCallback;
            _removeCallback = removeCallback;
            _errorCallback = errorCallback;
            _flushTimer = new Timer(OnTimerElapsed, null, windowMs, Timeout.Infinite);
        }

        /// <summary>
        /// Adds changes to the pending buffer for aggregation.
        /// </summary>
        public void AddChanges(List<ObjectChangeRecord> changes, long version, string? sourceAppId)
        {
            lock (_lock)
            {
                if (_disposed) return;
                _pendingChanges.AddRange(changes);
                _latestVersion = version;
                _sourceAppId = sourceAppId;
            }
        }

        private void OnTimerElapsed(object? state)
        {
            List<ObjectChangeRecord> changesToPublish;
            long version;
            string? sourceAppId;

            lock (_lock)
            {
                if (_disposed || _pendingChanges.Count == 0) return;
                changesToPublish = _pendingChanges;
                _pendingChanges = new List<ObjectChangeRecord>();
                version = _latestVersion;
                sourceAppId = _sourceAppId;
            }

            // Fire and forget with retry — timer callback cannot await directly
            _ = Task.Run(async () =>
            {
                var attempts = 0;
                while (attempts <= _maxRetries)
                {
                    try
                    {
                        await _flushCallback(_channelId, changesToPublish, version, sourceAppId, CancellationToken.None);
                        _removeCallback(_channelId);
                        Dispose();
                        return;
                    }
                    catch (Exception ex)
                    {
                        attempts++;
                        if (attempts > _maxRetries)
                        {
                            // All retries exhausted — report error and discard changes
                            try
                            {
                                await _errorCallback(_channelId, changesToPublish.Count, ex);
                            }
                            catch
                            {
                                // Error callback itself failed (e.g., message bus down) — nothing more we can do
                            }

                            _removeCallback(_channelId);
                            Dispose();
                            return;
                        }

                        // Exponential backoff: 100ms, 200ms, 400ms, ...
                        var delayMs = 100 * (1 << (attempts - 1));
                        await Task.Delay(delayMs);
                    }
                }
            });
        }

        /// <summary>
        /// Disposes the buffer and its flush timer.
        /// </summary>
        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                _flushTimer.Dispose();
            }
        }
    }

    #endregion
}
