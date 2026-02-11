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
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<MappingService> _logger;
    private readonly MappingServiceConfiguration _configuration;
    private readonly IAssetClient _assetClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAffordanceScorer _affordanceScorer;

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
    private const string DEFINITION_INDEX_KEY = "map:definition-index";

    // Authority event topics
    private const string AUTHORITY_GRANTED_TOPIC = "mapping.authority.granted";
    private const string AUTHORITY_RELEASED_TOPIC = "mapping.authority.released";
    private const string AUTHORITY_EXPIRED_TOPIC = "mapping.authority.expired";

    /// <summary>
    /// Initializes a new instance of the MappingService.
    /// </summary>
    /// <param name="messageBus">Message bus for event publishing.</param>
    /// <param name="messageSubscriber">Message subscriber for event subscriptions.</param>
    /// <param name="stateStoreFactory">Factory for creating state stores.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Service configuration.</param>
    /// <param name="eventConsumer">Event consumer for registering handlers.</param>
    /// <param name="assetClient">Asset client for large payload storage.</param>
    /// <param name="httpClientFactory">HTTP client factory for uploading to presigned URLs.</param>
    /// <param name="affordanceScorer">Affordance scoring helper for affordance queries.</param>
    public MappingService(
        IMessageBus messageBus,
        IMessageSubscriber messageSubscriber,
        IStateStoreFactory stateStoreFactory,
        ILogger<MappingService> logger,
        MappingServiceConfiguration configuration,
        IEventConsumer eventConsumer,
        IAssetClient assetClient,
        IHttpClientFactory httpClientFactory,
        IAffordanceScorer affordanceScorer)
    {
        _messageBus = messageBus;
        _messageSubscriber = messageSubscriber;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;
        _assetClient = assetClient;
        _httpClientFactory = httpClientFactory;
        _affordanceScorer = affordanceScorer;

        // Register event handlers via partial class (MappingServiceEvents.cs)
        RegisterEventConsumers(eventConsumer);
    }

    #region Key Building Helpers

    private static string BuildChannelKey(Guid channelId) => $"{CHANNEL_PREFIX}{channelId}";
    private static string BuildAuthorityKey(Guid channelId) => $"{AUTHORITY_PREFIX}{channelId}";
    private static string BuildObjectKey(Guid regionId, Guid objectId) => $"{OBJECT_PREFIX}{regionId}:{objectId}";
    private static string BuildSpatialIndexKey(Guid regionId, MapKind kind, int cellX, int cellY, int cellZ) =>
        $"{SPATIAL_INDEX_PREFIX}{regionId}:{kind}:{cellX}_{cellY}_{cellZ}";
    private static string BuildTypeIndexKey(Guid regionId, string objectType) =>
        $"{TYPE_INDEX_PREFIX}{regionId}:{objectType}";
    private static string BuildCheckoutKey(Guid regionId, MapKind kind) => $"{CHECKOUT_PREFIX}{regionId}:{kind}";
    private static string BuildVersionKey(Guid channelId) => $"{VERSION_PREFIX}{channelId}";
    private static string BuildAffordanceCacheKey(Guid regionId, AffordanceType type, string boundsHash) =>
        $"{AFFORDANCE_CACHE_PREFIX}{regionId}:{type}:{boundsHash}";
    private static string BuildDefinitionKey(Guid definitionId) => $"{DEFINITION_PREFIX}{definitionId}";

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

    private static (bool valid, Guid channelId, DateTimeOffset expiresAt) ParseAuthorityToken(string token)
    {
        try
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = decoded.Split(':');
            if (parts.Length < 2)
            {
                return (false, Guid.Empty, default);
            }

            if (!Guid.TryParse(parts[0], out var channelId))
            {
                return (false, Guid.Empty, default);
            }

            if (!DateTimeOffset.TryParse(parts[1], out var expiresAt))
            {
                return (false, Guid.Empty, default);
            }

            return (true, channelId, expiresAt);
        }
        catch
        {
            // Intentionally swallowing: any decode/parse exception means token is invalid
            // Callers handle the (false, _, _) return and log appropriately
            return (false, Guid.Empty, default);
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
            var existingChannel = await _stateStoreFactory.GetStore<ChannelRecord>(StateStoreDefinitions.Mapping)
                .GetAsync(channelKey, cancellationToken);

            // Determine takeover mode (default to preserve_and_diff)
            var takeoverMode = body.TakeoverMode;

            if (existingChannel != null)
            {
                // Channel exists - check if authority is available
                var authorityKey = BuildAuthorityKey(channelId);
                var existingAuthority = await _stateStoreFactory.GetStore<AuthorityRecord>(StateStoreDefinitions.Mapping)
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

            await _stateStoreFactory.GetStore<ChannelRecord>(StateStoreDefinitions.Mapping)
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
            await _stateStoreFactory.GetStore<AuthorityRecord>(StateStoreDefinitions.Mapping)
                .SaveAsync(authorityRecordKey, authority, cancellationToken: cancellationToken);

            // Initialize version counter
            var versionKey = BuildVersionKey(channelId);
            await _stateStoreFactory.GetStore<LongWrapper>(StateStoreDefinitions.Mapping)
                .SaveAsync(versionKey, new LongWrapper { Value = 1L }, cancellationToken: cancellationToken);

            // Process initial snapshot if provided
            if (body.InitialSnapshot != null && body.InitialSnapshot.Count > 0)
            {
                await ProcessPayloadsAsync(channelId, body.RegionId, body.Kind, body.InitialSnapshot, cancellationToken);
            }

            // Subscribe to ingest topic for this channel
            await SubscribeToIngestTopicAsync(channelId, ingestTopic, cancellationToken);

            // Publish channel created event
            await PublishChannelCreatedEventAsync(channel, cancellationToken);

            // Publish authority granted event
            await _messageBus.TryPublishAsync(AUTHORITY_GRANTED_TOPIC, new MappingAuthorityGrantedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ChannelId = channelId,
                RegionId = body.RegionId,
                Kind = body.Kind.ToString(),
                AuthorityAppId = body.SourceAppId,
                ExpiresAt = expiresAt,
                IsNewChannel = existingChannel == null
            }, cancellationToken: cancellationToken);

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
    public async Task<(StatusCodes, ReleaseAuthorityResponse?)> ReleaseAuthorityAsync(ReleaseAuthorityRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Releasing authority for channel {ChannelId}", body.ChannelId);

        {
            var (valid, tokenChannelId, _) = ParseAuthorityToken(body.AuthorityToken);
            if (!valid || tokenChannelId != body.ChannelId)
            {
                _logger.LogWarning("Invalid authority token for channel {ChannelId}", body.ChannelId);
                return (StatusCodes.Unauthorized, null);
            }

            var authorityKey = BuildAuthorityKey(body.ChannelId);
            var authority = await _stateStoreFactory.GetStore<AuthorityRecord>(StateStoreDefinitions.Mapping)
                .GetAsync(authorityKey, cancellationToken);

            if (authority == null || authority.AuthorityToken != body.AuthorityToken)
            {
                _logger.LogWarning("Authority token mismatch for channel {ChannelId}", body.ChannelId);
                return (StatusCodes.Unauthorized, null);
            }

            // Delete authority record
            await _stateStoreFactory.GetStore<AuthorityRecord>(StateStoreDefinitions.Mapping)
                .DeleteAsync(authorityKey, cancellationToken);

            // Unsubscribe from ingest topic
            if (IngestSubscriptions.TryRemove(body.ChannelId, out var subscription))
            {
                await subscription.DisposeAsync();
            }

            // Publish authority released event
            var channelKey = BuildChannelKey(body.ChannelId);
            var channel = await _stateStoreFactory.GetStore<ChannelRecord>(StateStoreDefinitions.Mapping)
                .GetAsync(channelKey, cancellationToken);
            await _messageBus.TryPublishAsync(AUTHORITY_RELEASED_TOPIC, new MappingAuthorityReleasedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ChannelId = body.ChannelId,
                RegionId = channel?.RegionId,
                Kind = channel?.Kind.ToString(),
                AuthorityAppId = authority.AuthorityAppId
            }, cancellationToken: cancellationToken);

            _logger.LogInformation("Released authority for channel {ChannelId}", body.ChannelId);
            return (StatusCodes.OK, new ReleaseAuthorityResponse { Released = true });
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, AuthorityHeartbeatResponse?)> AuthorityHeartbeatAsync(AuthorityHeartbeatRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Processing heartbeat for channel {ChannelId}", body.ChannelId);

        {
            var (valid, tokenChannelId, _) = ParseAuthorityToken(body.AuthorityToken);
            if (!valid || tokenChannelId != body.ChannelId)
            {
                _logger.LogWarning("Invalid authority token for heartbeat on channel {ChannelId}", body.ChannelId);
                return (StatusCodes.Unauthorized, null);
            }

            var authorityKey = BuildAuthorityKey(body.ChannelId);
            var authority = await _stateStoreFactory.GetStore<AuthorityRecord>(StateStoreDefinitions.Mapping)
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

            await _stateStoreFactory.GetStore<AuthorityRecord>(StateStoreDefinitions.Mapping)
                .SaveAsync(authorityKey, authority, cancellationToken: cancellationToken);

            return (StatusCodes.OK, new AuthorityHeartbeatResponse
            {
                Valid = true,
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

        {
            // Check payload size limit (MVP: reject large payloads; full impl would use lib-asset)
            var payloadSize = BannouJson.Serialize(body.Payload).Length;
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
                Accepted = true,
                Version = version,
                Warning = null
            });
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, PublishObjectChangesResponse?)> PublishObjectChangesAsync(PublishObjectChangesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Publishing {Count} object changes to channel {ChannelId}", body.Changes.Count, body.ChannelId);

        {
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
    }

    private async Task<(StatusCodes, PublishObjectChangesResponse?)> ProcessAuthorizedObjectChangesAsync(
        ChannelRecord channel, ICollection<ObjectChange> changes, string? sourceAppId, CancellationToken cancellationToken)
    {
        var acceptedCount = 0;
        var rejectedCount = 0;
        var changeEvents = new List<ObjectChangeEvent>();

        foreach (var change in changes)
        {
            try
            {
                var processed = await ProcessObjectChangeAsync(channel.RegionId, channel.Kind, change, cancellationToken);
                if (processed)
                {
                    acceptedCount++;
                    changeEvents.Add(new ObjectChangeEvent
                    {
                        ObjectId = change.ObjectId,
                        Action = MapObjectActionToEventAction(change.Action),
                        ObjectType = change.ObjectType,
                        Position = change.Position != null ? new EventPosition3D { X = change.Position.X, Y = change.Position.Y, Z = change.Position.Z } : null,
                        Bounds = change.Bounds != null ? new EventBounds
                        {
                            Min = new EventPosition3D { X = change.Bounds.Min.X, Y = change.Bounds.Min.Y, Z = change.Bounds.Min.Z },
                            Max = new EventPosition3D { X = change.Bounds.Max.X, Y = change.Bounds.Max.Y, Z = change.Bounds.Max.Z }
                        } : null,
                        Data = change.Data
                    });
                }
                else
                {
                    rejectedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process object change for {ObjectId}", change.ObjectId);
                rejectedCount++;
            }
        }

        // Increment version
        var version = await IncrementVersionAsync(channel.ChannelId, cancellationToken);

        // Publish objects changed event
        if (changeEvents.Count > 0)
        {
            await PublishMapObjectsChangedEventAsync(channel, version, changeEvents, sourceAppId, cancellationToken);
        }

        return (StatusCodes.OK, new PublishObjectChangesResponse
        {
            Accepted = acceptedCount > 0,
            AcceptedCount = acceptedCount,
            RejectedCount = rejectedCount,
            Version = version
        });
    }

    private async Task<(StatusCodes, PublishObjectChangesResponse?)> HandleNonAuthorityObjectChangesAsync(
        ChannelRecord channel, ICollection<ObjectChange> changes, string? warning, CancellationToken cancellationToken)
    {
        var mode = channel.NonAuthorityHandling;

        switch (mode)
        {
            case NonAuthorityHandlingMode.RejectSilent:
                _logger.LogDebug("Non-authority object changes rejected silently for channel {ChannelId}", channel.ChannelId);
                return (StatusCodes.Unauthorized, null);

            case NonAuthorityHandlingMode.RejectAndAlert:
                await PublishUnauthorizedObjectChangesWarningAsync(channel, changes, accepted: false, cancellationToken);
                _logger.LogWarning("Non-authority object changes rejected with alert for channel {ChannelId}", channel.ChannelId);
                return (StatusCodes.Unauthorized, null);

            case NonAuthorityHandlingMode.AcceptAndAlert:
                await PublishUnauthorizedObjectChangesWarningAsync(channel, changes, accepted: true, cancellationToken);
                // Process the changes anyway - use null for sourceAppId since this is unauthorized
                var (status, response) = await ProcessAuthorizedObjectChangesAsync(channel, changes, null, cancellationToken);
                if (response != null)
                {
                    response.Warning = "Published despite lacking authority (accept_and_alert mode)";
                }
                return (status, response);

            default:
                _logger.LogDebug("Non-authority object changes rejected (default mode) for channel {ChannelId}", channel.ChannelId);
                return (StatusCodes.Unauthorized, null);
        }
    }

    private async Task PublishUnauthorizedObjectChangesWarningAsync(
        ChannelRecord channel, ICollection<ObjectChange> changes, bool accepted, CancellationToken cancellationToken)
    {
        var alertConfig = channel.AlertConfig;
        if (alertConfig != null && !alertConfig.Enabled)
        {
            return;
        }

        // Summarize the object types being changed
        var objectTypes = changes
            .Select(c => c.ObjectType)
            .Where(t => t != null)
            .Distinct()
            .Take(_configuration.MaxSpatialQueryResults);
        var payloadSummary = alertConfig?.IncludePayloadSummary == true
            ? $"{changes.Count} objects ({string.Join(", ", objectTypes)})"
            : null;

        var warning = new MapUnauthorizedPublishWarning
        {
            Timestamp = DateTimeOffset.UtcNow,
            ChannelId = channel.ChannelId,
            RegionId = channel.RegionId,
            Kind = channel.Kind.ToString(),
            AttemptedPublisher = "unknown",
            CurrentAuthority = null,
            HandlingMode = channel.NonAuthorityHandling,
            PublishAccepted = accepted,
            PayloadSummary = payloadSummary
        };

        var topic = alertConfig?.AlertTopic ?? "map.warnings.unauthorized_publish";
        await _messageBus.TryPublishAsync(topic, warning, cancellationToken: cancellationToken);
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
                var versionWrapper = await _stateStoreFactory.GetStore<LongWrapper>(StateStoreDefinitions.Mapping).GetAsync(versionKey, cancellationToken);
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
                        RegionId = body.RegionId,
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
                RegionId = body.RegionId,
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

        {
            var objects = new List<MapObject>();
            var radius = body.Radius ?? _configuration.DefaultSpatialCellSize;
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
                Position = body.Position,
                Radius = radius
            });
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, QueryBoundsResponse?)> QueryBoundsAsync(QueryBoundsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Querying bounds in region {RegionId}", body.RegionId);

        {
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
                Bounds = body.Bounds,
                Truncated = truncated
            });
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, QueryObjectsByTypeResponse?)> QueryObjectsByTypeAsync(QueryObjectsByTypeRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Querying objects of type {ObjectType} in region {RegionId}", body.ObjectType, body.RegionId);

        {
            var typeIndexKey = BuildTypeIndexKey(body.RegionId, body.ObjectType);
            var objectIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Mapping)
                .GetAsync(typeIndexKey, cancellationToken) ?? new List<Guid>();

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
                var obj = await _stateStoreFactory.GetStore<MapObject>(StateStoreDefinitions.Mapping)
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
                ObjectType = body.ObjectType,
                Truncated = truncated
            });
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, AffordanceQueryResponse?)> QueryAffordanceAsync(AffordanceQueryRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Querying affordance {AffordanceType} in region {RegionId}", body.AffordanceType, body.RegionId);
        var stopwatch = Stopwatch.StartNew();

        {
            // Check cache if freshness allows
            if (body.Freshness != AffordanceFreshness.Fresh)
            {
                var cachedResult = await TryGetCachedAffordanceAsync(body, cancellationToken);
                if (cachedResult != null)
                {
                    stopwatch.Stop();
                    // Preserve original query stats from cached result, update cache-specific fields
                    if (cachedResult.QueryMetadata != null)
                    {
                        cachedResult.QueryMetadata.SearchDurationMs = (int)stopwatch.ElapsedMilliseconds;
                        cachedResult.QueryMetadata.CacheHit = true;
                    }
                    else
                    {
                        cachedResult.QueryMetadata = new AffordanceQueryMetadata
                        {
                            KindsSearched = new List<string>(),
                            ObjectsEvaluated = 0,
                            CandidatesGenerated = 0,
                            SearchDurationMs = (int)stopwatch.ElapsedMilliseconds,
                            CacheHit = true
                        };
                    }
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
                QueryMetadata = new AffordanceQueryMetadata
                {
                    KindsSearched = kindsToSearch.Select(k => k.ToString()).ToList(),
                    ObjectsEvaluated = objectsEvaluated,
                    CandidatesGenerated = candidatesGenerated,
                    SearchDurationMs = (int)stopwatch.ElapsedMilliseconds,
                    CacheHit = false
                }
            };

            // Cache result if allowed
            if (body.Freshness != AffordanceFreshness.Fresh)
            {
                await CacheAffordanceResultAsync(body, response, cancellationToken);
            }

            return (StatusCodes.OK, response);
        }
    }

    #endregion

    #region Authoring

    /// <inheritdoc />
    public async Task<(StatusCodes, AuthoringCheckoutResponse?)> CheckoutForAuthoringAsync(AuthoringCheckoutRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Checkout for authoring - region {RegionId}, kind {Kind}, editor {EditorId}",
            body.RegionId, body.Kind, body.EditorId);

        {
            var checkoutKey = BuildCheckoutKey(body.RegionId, body.Kind);
            var existingCheckout = await _stateStoreFactory.GetStore<CheckoutRecord>(StateStoreDefinitions.Mapping)
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

            await _stateStoreFactory.GetStore<CheckoutRecord>(StateStoreDefinitions.Mapping)
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
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, AuthoringCommitResponse?)> CommitAuthoringAsync(AuthoringCommitRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Committing authoring changes - region {RegionId}, kind {Kind}", body.RegionId, body.Kind);

        {
            var checkoutKey = BuildCheckoutKey(body.RegionId, body.Kind);
            var checkout = await _stateStoreFactory.GetStore<CheckoutRecord>(StateStoreDefinitions.Mapping)
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
            await _stateStoreFactory.GetStore<CheckoutRecord>(StateStoreDefinitions.Mapping)
                .DeleteAsync(checkoutKey, cancellationToken);

            _logger.LogInformation("Committed authoring changes for region {RegionId}, kind {Kind}, version {Version}",
                body.RegionId, body.Kind, version);

            return (StatusCodes.OK, new AuthoringCommitResponse
            {
                Version = version
            });
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, AuthoringReleaseResponse?)> ReleaseAuthoringAsync(AuthoringReleaseRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Releasing authoring checkout - region {RegionId}, kind {Kind}", body.RegionId, body.Kind);

        {
            var checkoutKey = BuildCheckoutKey(body.RegionId, body.Kind);
            var checkout = await _stateStoreFactory.GetStore<CheckoutRecord>(StateStoreDefinitions.Mapping)
                .GetAsync(checkoutKey, cancellationToken);

            if (checkout == null || checkout.AuthorityToken != body.AuthorityToken)
            {
                _logger.LogWarning("Invalid authority token for authoring release on region {RegionId}", body.RegionId);
                return (StatusCodes.Unauthorized, null);
            }

            await _stateStoreFactory.GetStore<CheckoutRecord>(StateStoreDefinitions.Mapping)
                .DeleteAsync(checkoutKey, cancellationToken);

            _logger.LogInformation("Released authoring checkout for region {RegionId}, kind {Kind}",
                body.RegionId, body.Kind);

            return (StatusCodes.OK, new AuthoringReleaseResponse { Released = true });
        }
    }

    #endregion

    #region Definition CRUD

    /// <inheritdoc />
    public async Task<(StatusCodes, MapDefinition?)> CreateDefinitionAsync(CreateDefinitionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Creating map definition: {Name}", body.Name);

        {
            // Check for duplicate name by scanning existing definitions
            var indexStore = _stateStoreFactory.GetStore<DefinitionIndexEntry>(StateStoreDefinitions.Mapping);
            var existingIndex = await indexStore.GetAsync(DEFINITION_INDEX_KEY, cancellationToken);

            if (existingIndex != null && existingIndex.DefinitionIds.Any())
            {
                var definitionStore = _stateStoreFactory.GetStore<DefinitionRecord>(StateStoreDefinitions.Mapping);
                foreach (var id in existingIndex.DefinitionIds)
                {
                    var existing = await definitionStore.GetAsync(BuildDefinitionKey(id), cancellationToken);
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
            await _stateStoreFactory.GetStore<DefinitionRecord>(StateStoreDefinitions.Mapping)
                .SaveAsync(key, record, cancellationToken: cancellationToken);

            // Update index
            var newIndex = existingIndex ?? new DefinitionIndexEntry { DefinitionIds = new List<Guid>() };
            newIndex.DefinitionIds.Add(definitionId);
            await indexStore.SaveAsync(DEFINITION_INDEX_KEY, newIndex, cancellationToken: cancellationToken);

            _logger.LogInformation("Created map definition {DefinitionId} ({Name})", definitionId, body.Name);

            return (StatusCodes.OK, MapRecordToDefinition(record));
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, MapDefinition?)> GetDefinitionAsync(GetDefinitionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting map definition: {DefinitionId}", body.DefinitionId);

        {
            var key = BuildDefinitionKey(body.DefinitionId);
            var record = await _stateStoreFactory.GetStore<DefinitionRecord>(StateStoreDefinitions.Mapping)
                .GetAsync(key, cancellationToken);

            if (record == null)
            {
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, MapRecordToDefinition(record));
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ListDefinitionsResponse?)> ListDefinitionsAsync(ListDefinitionsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing map definitions with filter: {Filter}", body.NameFilter);

        {
            var indexStore = _stateStoreFactory.GetStore<DefinitionIndexEntry>(StateStoreDefinitions.Mapping);
            var index = await indexStore.GetAsync(DEFINITION_INDEX_KEY, cancellationToken);

            var definitions = new List<MapDefinition>();
            var total = 0;

            if (index != null && index.DefinitionIds.Any())
            {
                var definitionStore = _stateStoreFactory.GetStore<DefinitionRecord>(StateStoreDefinitions.Mapping);
                var allRecords = new List<DefinitionRecord>();

                foreach (var id in index.DefinitionIds)
                {
                    var record = await definitionStore.GetAsync(BuildDefinitionKey(id), cancellationToken);
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
                Offset = body.Offset,
                Limit = body.Limit
            });
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, MapDefinition?)> UpdateDefinitionAsync(UpdateDefinitionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Updating map definition: {DefinitionId}", body.DefinitionId);

        {
            var key = BuildDefinitionKey(body.DefinitionId);
            var record = await _stateStoreFactory.GetStore<DefinitionRecord>(StateStoreDefinitions.Mapping)
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

            await _stateStoreFactory.GetStore<DefinitionRecord>(StateStoreDefinitions.Mapping)
                .SaveAsync(key, record, cancellationToken: cancellationToken);

            _logger.LogInformation("Updated map definition {DefinitionId}", body.DefinitionId);

            return (StatusCodes.OK, MapRecordToDefinition(record));
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, DeleteDefinitionResponse?)> DeleteDefinitionAsync(DeleteDefinitionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting map definition: {DefinitionId}", body.DefinitionId);

        {
            var key = BuildDefinitionKey(body.DefinitionId);
            var definitionStore = _stateStoreFactory.GetStore<DefinitionRecord>(StateStoreDefinitions.Mapping);
            var record = await definitionStore.GetAsync(key, cancellationToken);

            if (record == null)
            {
                _logger.LogDebug("Map definition {DefinitionId} not found for deletion", body.DefinitionId);
                return (StatusCodes.NotFound, null);
            }

            // Delete the definition
            await definitionStore.DeleteAsync(key, cancellationToken);

            // Update index
            var indexStore = _stateStoreFactory.GetStore<DefinitionIndexEntry>(StateStoreDefinitions.Mapping);
            var index = await indexStore.GetAsync(DEFINITION_INDEX_KEY, cancellationToken);
            if (index != null)
            {
                index.DefinitionIds.Remove(body.DefinitionId);
                await indexStore.SaveAsync(DEFINITION_INDEX_KEY, index, cancellationToken: cancellationToken);
            }

            _logger.LogInformation("Deleted map definition {DefinitionId}", body.DefinitionId);

            return (StatusCodes.OK, new DeleteDefinitionResponse { Deleted = true });
        }
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

    #region Private Helpers

    private static Guid GenerateChannelId(Guid regionId, MapKind kind)
    {
        // Generate deterministic channel ID from region + kind
        var input = $"{regionId}:{kind}";
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        // Use first 16 bytes as GUID
        return new Guid(hash.Take(16).ToArray());
    }

    private async Task<(bool isValid, ChannelRecord? channel, string? authorityAppId, string? warning)> ValidateAuthorityAsync(
        Guid channelId, string authorityToken, CancellationToken cancellationToken)
    {
        var channelKey = BuildChannelKey(channelId);
        var channel = await _stateStoreFactory.GetStore<ChannelRecord>(StateStoreDefinitions.Mapping)
            .GetAsync(channelKey, cancellationToken);

        if (channel == null)
        {
            return (false, null, null, "Channel not found");
        }

        // Opaque token validation: parse only to extract channelId for basic validation,
        // but expiry is checked ONLY against AuthorityRecord.ExpiresAt (which is updated by heartbeat)
        var (valid, tokenChannelId, _) = ParseAuthorityToken(authorityToken);
        if (!valid || tokenChannelId != channelId)
        {
            return (false, channel, null, "Invalid authority token");
        }

        var authorityKey = BuildAuthorityKey(channelId);
        var authority = await _stateStoreFactory.GetStore<AuthorityRecord>(StateStoreDefinitions.Mapping)
            .GetAsync(authorityKey, cancellationToken);

        if (authority == null || authority.AuthorityToken != authorityToken)
        {
            return (false, channel, null, "Authority token not recognized");
        }

        // Check expiry against the stored AuthorityRecord (updated by heartbeat), not the token
        if (authority.ExpiresAt < DateTimeOffset.UtcNow)
        {
            // Publish authority expired event (fire-and-forget for monitoring)
            _ = _messageBus.TryPublishAsync(AUTHORITY_EXPIRED_TOPIC, new MappingAuthorityExpiredEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ChannelId = channelId,
                RegionId = channel.RegionId,
                Kind = channel.Kind.ToString(),
                ExpiredAuthorityAppId = authority.AuthorityAppId,
                ExpiredAt = authority.ExpiresAt
            });
            return (false, channel, null, "Authority has expired");
        }

        // Check if authority was granted with require_consume mode and hasn't consumed yet
        if (authority.RequiresConsumeBeforePublish)
        {
            return (false, channel, null, "Authority requires RequestSnapshot before publishing (require_consume takeover mode)");
        }

        return (true, channel, authority.AuthorityAppId, null);
    }

    private async Task<(StatusCodes, PublishMapUpdateResponse?)> HandleNonAuthorityPublishAsync(
        ChannelRecord channel, MapPayload payload, string? attemptedPublisher, string? warning, CancellationToken cancellationToken)
    {
        var mode = channel.NonAuthorityHandling;

        switch (mode)
        {
            case NonAuthorityHandlingMode.RejectSilent:
                _logger.LogDebug("Non-authority publish rejected silently for channel {ChannelId}", channel.ChannelId);
                return (StatusCodes.Unauthorized, null);

            case NonAuthorityHandlingMode.RejectAndAlert:
                await PublishUnauthorizedWarningAsync(channel, payload, attemptedPublisher, false, cancellationToken);
                _logger.LogDebug("Non-authority publish rejected with alert for channel {ChannelId}", channel.ChannelId);
                return (StatusCodes.Unauthorized, null);

            case NonAuthorityHandlingMode.AcceptAndAlert:
                await PublishUnauthorizedWarningAsync(channel, payload, attemptedPublisher, true, cancellationToken);
                // Process the payload anyway
                var payloads = new List<MapPayload> { payload };
                var version = await ProcessPayloadsAsync(channel.ChannelId, channel.RegionId, channel.Kind, payloads, cancellationToken);
                _logger.LogInformation("Published despite lacking authority (accept_and_alert mode) for channel {ChannelId}", channel.ChannelId);
                return (StatusCodes.OK, new PublishMapUpdateResponse
                {
                    Accepted = true,
                    Version = version,
                    Warning = null
                });

            default:
                _logger.LogDebug("Non-authority publish rejected (unknown mode) for channel {ChannelId}", channel.ChannelId);
                return (StatusCodes.Unauthorized, null);
        }
    }

    private async Task<long> ProcessPayloadsAsync(Guid channelId, Guid regionId, MapKind kind,
        ICollection<MapPayload> payloads, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var version = await IncrementVersionAsync(channelId, cancellationToken);

        foreach (var payload in payloads)
        {
            var objectId = payload.ObjectId != Guid.Empty ? payload.ObjectId : Guid.NewGuid();

            var mapObject = new MapObject
            {
                ObjectId = objectId,
                RegionId = regionId,
                Kind = kind,
                ObjectType = payload.ObjectType,
                Position = payload.Position,
                Bounds = payload.Bounds,
                Data = payload.Data,
                Version = version,
                CreatedAt = now,
                UpdatedAt = now
            };

            // Save object with TTL based on kind
            var objectKey = BuildObjectKey(regionId, objectId);
            var stateOptions = GetStateOptionsForKind(kind);
            await _stateStoreFactory.GetStore<MapObject>(StateStoreDefinitions.Mapping)
                .SaveAsync(objectKey, mapObject, stateOptions, cancellationToken);

            // Update spatial index
            if (payload.Position != null)
            {
                await UpdateSpatialIndexAsync(regionId, kind, objectId, payload.Position, cancellationToken);
            }
            else if (payload.Bounds != null)
            {
                await UpdateSpatialIndexForBoundsAsync(regionId, kind, objectId, payload.Bounds, cancellationToken);
            }

            // Update type index
            await UpdateTypeIndexAsync(regionId, payload.ObjectType, objectId, cancellationToken);
        }

        return version;
    }

    private async Task<bool> ProcessObjectChangeAsync(Guid regionId, MapKind kind, ObjectChange change, CancellationToken cancellationToken)
    {
        // Delegate to the version with index cleanup
        return await ProcessObjectChangeWithIndexCleanupAsync(regionId, kind, change, cancellationToken);
    }

    private async Task<bool> ProcessObjectChangeWithIndexCleanupAsync(Guid regionId, MapKind kind, ObjectChange change, CancellationToken cancellationToken)
    {
        var objectKey = BuildObjectKey(regionId, change.ObjectId);

        switch (change.Action)
        {
            case ObjectAction.Created:
                if (string.IsNullOrEmpty(change.ObjectType))
                {
                    return false;
                }
                var newObject = new MapObject
                {
                    ObjectId = change.ObjectId,
                    RegionId = regionId,
                    Kind = kind,
                    ObjectType = change.ObjectType,
                    Position = change.Position,
                    Bounds = change.Bounds,
                    Data = change.Data,
                    Version = 1,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                var createStateOptions = GetStateOptionsForKind(kind);
                await _stateStoreFactory.GetStore<MapObject>(StateStoreDefinitions.Mapping)
                    .SaveAsync(objectKey, newObject, createStateOptions, cancellationToken);

                // Add to region index for snapshot queries without bounds
                await AddToRegionIndexAsync(regionId, kind, change.ObjectId, cancellationToken);

                if (change.Position != null)
                {
                    await UpdateSpatialIndexAsync(regionId, kind, change.ObjectId, change.Position, cancellationToken);
                }
                else if (change.Bounds != null)
                {
                    await UpdateSpatialIndexForBoundsAsync(regionId, kind, change.ObjectId, change.Bounds, cancellationToken);
                }
                if (!string.IsNullOrEmpty(change.ObjectType))
                {
                    await UpdateTypeIndexAsync(regionId, change.ObjectType, change.ObjectId, cancellationToken);
                }
                return true;

            case ObjectAction.Updated:
                var existing = await _stateStoreFactory.GetStore<MapObject>(StateStoreDefinitions.Mapping)
                    .GetAsync(objectKey, cancellationToken);
                if (existing == null)
                {
                    // Object doesn't exist - treat as upsert (create)
                    return await ProcessObjectChangeWithIndexCleanupAsync(regionId, kind, new ObjectChange
                    {
                        ObjectId = change.ObjectId,
                        ObjectType = change.ObjectType ?? "unknown",
                        Action = ObjectAction.Created,
                        Position = change.Position,
                        Bounds = change.Bounds,
                        Data = change.Data
                    }, cancellationToken);
                }

                // Clean up old spatial indexes if position/bounds changed
                if (change.Position != null && existing.Position != null)
                {
                    await RemoveFromSpatialIndexAsync(regionId, kind, change.ObjectId, existing.Position, cancellationToken);
                }
                if (change.Bounds != null && existing.Bounds != null)
                {
                    await RemoveFromSpatialIndexForBoundsAsync(regionId, kind, change.ObjectId, existing.Bounds, cancellationToken);
                }

                // Clean up old type index if type changed
                if (!string.IsNullOrEmpty(change.ObjectType) && existing.ObjectType != change.ObjectType)
                {
                    await RemoveFromTypeIndexAsync(regionId, existing.ObjectType, change.ObjectId, cancellationToken);
                }

                // Update object (preserve CreatedAt)
                if (change.Position != null) existing.Position = change.Position;
                if (change.Bounds != null) existing.Bounds = change.Bounds;
                if (change.Data != null) existing.Data = change.Data;
                if (!string.IsNullOrEmpty(change.ObjectType)) existing.ObjectType = change.ObjectType;
                existing.Version++;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                // CreatedAt is preserved (not modified)

                var updateStateOptions = GetStateOptionsForKind(kind);
                await _stateStoreFactory.GetStore<MapObject>(StateStoreDefinitions.Mapping)
                    .SaveAsync(objectKey, existing, updateStateOptions, cancellationToken);

                // Add to new spatial indexes
                if (change.Position != null)
                {
                    await UpdateSpatialIndexAsync(regionId, kind, change.ObjectId, change.Position, cancellationToken);
                }
                else if (change.Bounds != null)
                {
                    await UpdateSpatialIndexForBoundsAsync(regionId, kind, change.ObjectId, change.Bounds, cancellationToken);
                }

                // Add to new type index
                if (!string.IsNullOrEmpty(change.ObjectType))
                {
                    await UpdateTypeIndexAsync(regionId, change.ObjectType, change.ObjectId, cancellationToken);
                }
                return true;

            case ObjectAction.Deleted:
                var toDelete = await _stateStoreFactory.GetStore<MapObject>(StateStoreDefinitions.Mapping)
                    .GetAsync(objectKey, cancellationToken);

                if (toDelete != null)
                {
                    // Clean up all indexes for this object
                    await RemoveFromRegionIndexAsync(regionId, kind, change.ObjectId, cancellationToken);

                    if (toDelete.Position != null)
                    {
                        await RemoveFromSpatialIndexAsync(regionId, kind, change.ObjectId, toDelete.Position, cancellationToken);
                    }
                    if (toDelete.Bounds != null)
                    {
                        await RemoveFromSpatialIndexForBoundsAsync(regionId, kind, change.ObjectId, toDelete.Bounds, cancellationToken);
                    }
                    if (!string.IsNullOrEmpty(toDelete.ObjectType))
                    {
                        await RemoveFromTypeIndexAsync(regionId, toDelete.ObjectType, change.ObjectId, cancellationToken);
                    }
                }

                await _stateStoreFactory.GetStore<MapObject>(StateStoreDefinitions.Mapping)
                    .DeleteAsync(objectKey, cancellationToken);
                return true;

            default:
                return false;
        }
    }

    private async Task UpdateSpatialIndexAsync(Guid regionId, MapKind kind, Guid objectId, Position3D position, CancellationToken cancellationToken)
    {
        var cell = GetCellCoordinates(position);
        var indexKey = BuildSpatialIndexKey(regionId, kind, cell.cellX, cell.cellY, cell.cellZ);
        var objectIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Mapping)
            .GetAsync(indexKey, cancellationToken) ?? new List<Guid>();

        if (!objectIds.Contains(objectId))
        {
            objectIds.Add(objectId);
            await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Mapping)
                .SaveAsync(indexKey, objectIds, cancellationToken: cancellationToken);
        }
    }

    private async Task UpdateSpatialIndexForBoundsAsync(Guid regionId, MapKind kind, Guid objectId, Bounds bounds, CancellationToken cancellationToken)
    {
        var cells = GetCellsForBounds(bounds);
        foreach (var cell in cells)
        {
            var indexKey = BuildSpatialIndexKey(regionId, kind, cell.cellX, cell.cellY, cell.cellZ);
            var objectIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Mapping)
                .GetAsync(indexKey, cancellationToken) ?? new List<Guid>();

            if (!objectIds.Contains(objectId))
            {
                objectIds.Add(objectId);
                await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Mapping)
                    .SaveAsync(indexKey, objectIds, cancellationToken: cancellationToken);
            }
        }
    }

    private async Task UpdateTypeIndexAsync(Guid regionId, string objectType, Guid objectId, CancellationToken cancellationToken)
    {
        var indexKey = BuildTypeIndexKey(regionId, objectType);
        var objectIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Mapping)
            .GetAsync(indexKey, cancellationToken) ?? new List<Guid>();

        if (!objectIds.Contains(objectId))
        {
            objectIds.Add(objectId);
            await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Mapping)
                .SaveAsync(indexKey, objectIds, cancellationToken: cancellationToken);
        }
    }

    private static string BuildRegionIndexKey(Guid regionId, MapKind kind) => $"map:region-index:{regionId}:{kind}";

    private async Task AddToRegionIndexAsync(Guid regionId, MapKind kind, Guid objectId, CancellationToken cancellationToken)
    {
        var indexKey = BuildRegionIndexKey(regionId, kind);
        var objectIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Mapping)
            .GetAsync(indexKey, cancellationToken) ?? new List<Guid>();

        if (!objectIds.Contains(objectId))
        {
            objectIds.Add(objectId);
            await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Mapping)
                .SaveAsync(indexKey, objectIds, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromRegionIndexAsync(Guid regionId, MapKind kind, Guid objectId, CancellationToken cancellationToken)
    {
        var indexKey = BuildRegionIndexKey(regionId, kind);
        var objectIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Mapping)
            .GetAsync(indexKey, cancellationToken);

        if (objectIds != null && objectIds.Contains(objectId))
        {
            objectIds.Remove(objectId);
            await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Mapping)
                .SaveAsync(indexKey, objectIds, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromSpatialIndexAsync(Guid regionId, MapKind kind, Guid objectId, Position3D position, CancellationToken cancellationToken)
    {
        var cell = GetCellCoordinates(position);
        var indexKey = BuildSpatialIndexKey(regionId, kind, cell.cellX, cell.cellY, cell.cellZ);
        var objectIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Mapping)
            .GetAsync(indexKey, cancellationToken);

        if (objectIds != null && objectIds.Contains(objectId))
        {
            objectIds.Remove(objectId);
            await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Mapping)
                .SaveAsync(indexKey, objectIds, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromSpatialIndexForBoundsAsync(Guid regionId, MapKind kind, Guid objectId, Bounds bounds, CancellationToken cancellationToken)
    {
        var cells = GetCellsForBounds(bounds);
        foreach (var cell in cells)
        {
            var indexKey = BuildSpatialIndexKey(regionId, kind, cell.cellX, cell.cellY, cell.cellZ);
            var objectIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Mapping)
                .GetAsync(indexKey, cancellationToken);

            if (objectIds != null && objectIds.Contains(objectId))
            {
                objectIds.Remove(objectId);
                await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Mapping)
                    .SaveAsync(indexKey, objectIds, cancellationToken: cancellationToken);
            }
        }
    }

    private async Task RemoveFromTypeIndexAsync(Guid regionId, string objectType, Guid objectId, CancellationToken cancellationToken)
    {
        var indexKey = BuildTypeIndexKey(regionId, objectType);
        var objectIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Mapping)
            .GetAsync(indexKey, cancellationToken);

        if (objectIds != null && objectIds.Contains(objectId))
        {
            objectIds.Remove(objectId);
            await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Mapping)
                .SaveAsync(indexKey, objectIds, cancellationToken: cancellationToken);
        }
    }

    private const string MapDataContentType = "application/json";

    private async Task<string?> UploadLargePayloadToAssetAsync(byte[] data, string filename, Guid regionId, CancellationToken cancellationToken)
    {
        try
        {
            var uploadRequest = new UploadRequest
            {
                Owner = "mapping",
                Filename = filename,
                Size = data.Length,
                ContentType = MapDataContentType,
                Metadata = new AssetMetadataInput
                {
                    AssetType = AssetType.Other,
                    Tags = new List<string> { "mapping", "snapshot", regionId.ToString() },
                    Realm = "shared"
                }
            };

            var uploadResponse = await _assetClient.RequestUploadAsync(uploadRequest, cancellationToken);

            // Upload the data to the presigned URL
            using var httpClient = _httpClientFactory.CreateClient();
            using var content = new ByteArrayContent(data);
            content.Headers.ContentType = new MediaTypeHeaderValue(MapDataContentType);

            var uploadResult = await httpClient.PutAsync(uploadResponse.UploadUrl, content, cancellationToken);
            if (!uploadResult.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to upload large payload to presigned URL: {StatusCode}", uploadResult.StatusCode);
                return null;
            }

            // Complete the upload
            var completeRequest = new CompleteUploadRequest
            {
                UploadId = uploadResponse.UploadId
            };

            var assetMetadata = await _assetClient.CompleteUploadAsync(completeRequest, cancellationToken);

            _logger.LogDebug("Uploaded large payload as asset {AssetId} for region {RegionId}", assetMetadata.AssetId, regionId);
            return assetMetadata.AssetId.ToString();
        }
        catch (ApiException apiEx)
        {
            _logger.LogError(apiEx, "Asset service error uploading large payload for region {RegionId}: {Status}",
                regionId, apiEx.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading large payload to lib-asset for region {RegionId}", regionId);
            return null;
        }
    }

    private async Task ClearRequiresConsumeForAuthorityAsync(Guid regionId, IEnumerable<MapKind> kinds, string authorityToken, CancellationToken cancellationToken)
    {
        foreach (var kind in kinds)
        {
            var channelId = GenerateChannelId(regionId, kind);
            var authorityKey = BuildAuthorityKey(channelId);
            var authority = await _stateStoreFactory.GetStore<AuthorityRecord>(StateStoreDefinitions.Mapping)
                .GetAsync(authorityKey, cancellationToken);

            if (authority != null && authority.AuthorityToken == authorityToken && authority.RequiresConsumeBeforePublish)
            {
                authority.RequiresConsumeBeforePublish = false;
                await _stateStoreFactory.GetStore<AuthorityRecord>(StateStoreDefinitions.Mapping)
                    .SaveAsync(authorityKey, authority, cancellationToken: cancellationToken);
                _logger.LogDebug("Cleared RequiresConsumeBeforePublish flag for channel {ChannelId}", channelId);
            }
        }
    }

    private async Task ClearChannelDataAsync(Guid channelId, Guid regionId, MapKind kind, CancellationToken cancellationToken)
    {
        // Get the region index to find all objects
        var regionIndexKey = BuildRegionIndexKey(regionId, kind);
        var objectIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Mapping)
            .GetAsync(regionIndexKey, cancellationToken) ?? new List<Guid>();

        _logger.LogDebug("Clearing {Count} objects from channel {ChannelId}", objectIds.Count, channelId);

        // Delete all objects and their index entries
        var objectStore = _stateStoreFactory.GetStore<MapObject>(StateStoreDefinitions.Mapping);
        foreach (var objectId in objectIds)
        {
            var objectKey = BuildObjectKey(regionId, objectId);
            var obj = await objectStore.GetAsync(objectKey, cancellationToken);

            if (obj != null)
            {
                // Clean up spatial indexes
                if (obj.Position != null)
                {
                    await RemoveFromSpatialIndexAsync(regionId, kind, objectId, obj.Position, cancellationToken);
                }
                if (obj.Bounds != null)
                {
                    await RemoveFromSpatialIndexForBoundsAsync(regionId, kind, objectId, obj.Bounds, cancellationToken);
                }

                // Clean up type index
                if (!string.IsNullOrEmpty(obj.ObjectType))
                {
                    await RemoveFromTypeIndexAsync(regionId, obj.ObjectType, objectId, cancellationToken);
                }
            }

            await objectStore.DeleteAsync(objectKey, cancellationToken);
        }

        // Clear region index
        await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Mapping)
            .DeleteAsync(regionIndexKey, cancellationToken);

        // Reset version counter
        var versionKey = BuildVersionKey(channelId);
        await _stateStoreFactory.GetStore<LongWrapper>(StateStoreDefinitions.Mapping)
            .SaveAsync(versionKey, new LongWrapper { Value = 0L }, cancellationToken: cancellationToken);
    }

    private async Task<long> IncrementVersionAsync(Guid channelId, CancellationToken cancellationToken)
    {
        var versionKey = BuildVersionKey(channelId);
        var versionWrapper = await _stateStoreFactory.GetStore<LongWrapper>(StateStoreDefinitions.Mapping)
            .GetAsync(versionKey, cancellationToken);
        var currentVersion = versionWrapper?.Value ?? 0;
        var newVersion = currentVersion + 1;
        await _stateStoreFactory.GetStore<LongWrapper>(StateStoreDefinitions.Mapping)
            .SaveAsync(versionKey, new LongWrapper { Value = newVersion }, cancellationToken: cancellationToken);
        return newVersion;
    }

    private async Task<List<MapObject>> QueryObjectsInRegionAsync(Guid regionId, MapKind kind, Bounds? bounds, int maxObjects, CancellationToken cancellationToken)
    {
        if (bounds != null)
        {
            return await QueryObjectsInBoundsAsync(regionId, kind, bounds, maxObjects, cancellationToken);
        }

        // Full region query - use region index that tracks all objects in a region+kind
        var regionIndexKey = BuildRegionIndexKey(regionId, kind);
        var objectIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Mapping)
            .GetAsync(regionIndexKey, cancellationToken) ?? new List<Guid>();

        var objects = new List<MapObject>();
        foreach (var objectId in objectIds.Take(maxObjects))
        {
            var objectKey = BuildObjectKey(regionId, objectId);
            var obj = await _stateStoreFactory.GetStore<MapObject>(StateStoreDefinitions.Mapping)
                .GetAsync(objectKey, cancellationToken);
            if (obj != null)
            {
                objects.Add(obj);
            }
        }

        return objects;
    }

    private async Task<List<MapObject>> QueryObjectsInBoundsAsync(Guid regionId, MapKind kind, Bounds bounds, int maxObjects, CancellationToken cancellationToken)
    {
        var cells = GetCellsForBounds(bounds);
        var seenObjectIds = new HashSet<Guid>();
        var objects = new List<MapObject>();

        foreach (var cell in cells)
        {
            if (objects.Count >= maxObjects)
            {
                break;
            }

            var indexKey = BuildSpatialIndexKey(regionId, kind, cell.cellX, cell.cellY, cell.cellZ);
            var objectIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Mapping)
                .GetAsync(indexKey, cancellationToken) ?? new List<Guid>();

            foreach (var objectId in objectIds)
            {
                if (objects.Count >= maxObjects)
                {
                    break;
                }

                if (seenObjectIds.Contains(objectId))
                {
                    continue;
                }
                seenObjectIds.Add(objectId);

                var objectKey = BuildObjectKey(regionId, objectId);
                var obj = await _stateStoreFactory.GetStore<MapObject>(StateStoreDefinitions.Mapping)
                    .GetAsync(objectKey, cancellationToken);

                if (obj != null)
                {
                    // Verify object is actually within bounds
                    if (obj.Position != null && BoundsContainsPoint(bounds, obj.Position))
                    {
                        objects.Add(obj);
                    }
                    else if (obj.Bounds != null && BoundsIntersect(bounds, obj.Bounds))
                    {
                        objects.Add(obj);
                    }
                }
            }
        }

        return objects;
    }

    private static bool BoundsContainsPoint(Bounds bounds, Position3D point)
    {
        return point.X >= bounds.Min.X && point.X <= bounds.Max.X &&
                point.Y >= bounds.Min.Y && point.Y <= bounds.Max.Y &&
                point.Z >= bounds.Min.Z && point.Z <= bounds.Max.Z;
    }

    private static bool BoundsIntersect(Bounds a, Bounds b)
    {
        return a.Min.X <= b.Max.X && a.Max.X >= b.Min.X &&
                a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y &&
                a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;
    }

    private static bool BoundsIntersectsRadius(Bounds bounds, Position3D center, double radius)
    {
        // Find closest point in bounds to center
        var closestX = Math.Max(bounds.Min.X, Math.Min(center.X, bounds.Max.X));
        var closestY = Math.Max(bounds.Min.Y, Math.Min(center.Y, bounds.Max.Y));
        var closestZ = Math.Max(bounds.Min.Z, Math.Min(center.Z, bounds.Max.Z));

        var distanceSquared =
            Math.Pow(closestX - center.X, 2) +
            Math.Pow(closestY - center.Y, 2) +
            Math.Pow(closestZ - center.Z, 2);

        return distanceSquared <= radius * radius;
    }

    #endregion

    #region Affordance Caching

    private async Task<AffordanceQueryResponse?> TryGetCachedAffordanceAsync(AffordanceQueryRequest body, CancellationToken cancellationToken)
    {
        var boundsHash = body.Bounds != null
            ? $"{body.Bounds.Min.X},{body.Bounds.Min.Y},{body.Bounds.Min.Z}:{body.Bounds.Max.X},{body.Bounds.Max.Y},{body.Bounds.Max.Z}"
            : "all";
        var cacheKey = BuildAffordanceCacheKey(body.RegionId, body.AffordanceType, boundsHash);

        var cached = await _stateStoreFactory.GetStore<CachedAffordanceResult>(StateStoreDefinitions.Mapping)
            .GetAsync(cacheKey, cancellationToken);

        if (cached == null)
        {
            return null;
        }

        var maxAge = body.MaxAgeSeconds ?? _configuration.AffordanceCacheTimeoutSeconds;
        if ((DateTimeOffset.UtcNow - cached.CachedAt).TotalSeconds > maxAge)
        {
            return null;
        }

        return cached.Response;
    }

    private async Task CacheAffordanceResultAsync(AffordanceQueryRequest body, AffordanceQueryResponse response, CancellationToken cancellationToken)
    {
        var boundsHash = body.Bounds != null
            ? $"{body.Bounds.Min.X},{body.Bounds.Min.Y},{body.Bounds.Min.Z}:{body.Bounds.Max.X},{body.Bounds.Max.Y},{body.Bounds.Max.Z}"
            : "all";
        var cacheKey = BuildAffordanceCacheKey(body.RegionId, body.AffordanceType, boundsHash);

        var cached = new CachedAffordanceResult
        {
            Response = response,
            CachedAt = DateTimeOffset.UtcNow
        };

        await _stateStoreFactory.GetStore<CachedAffordanceResult>(StateStoreDefinitions.Mapping)
            .SaveAsync(cacheKey, cached, cancellationToken: cancellationToken);
    }

    #endregion

    #region Event Publishing

    private async Task PublishChannelCreatedEventAsync(ChannelRecord channel, CancellationToken cancellationToken)
    {
        var eventData = new MappingChannelCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ChannelId = channel.ChannelId,
            RegionId = channel.RegionId,
            Kind = channel.Kind.ToString(),
            NonAuthorityHandling = channel.NonAuthorityHandling,
            Version = channel.Version,
            CreatedAt = channel.CreatedAt,
            UpdatedAt = channel.UpdatedAt
        };

        await _messageBus.TryPublishAsync("mapping.channel.created", eventData, cancellationToken: cancellationToken);
    }

    private async Task PublishMapUpdatedEventAsync(ChannelRecord channel, Bounds? bounds, long version, DeltaType deltaType, MapPayload payload, string? sourceAppId, CancellationToken cancellationToken)
    {
        // MapUpdatedEvent publishes immediately - payload-level coalescing is complex.
        // Event aggregation is implemented for MapObjectsChangedEvent which has discrete changes.
        var eventData = new MapUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RegionId = channel.RegionId,
            Kind = channel.Kind.ToString(),
            ChannelId = channel.ChannelId,
            Bounds = bounds != null ? new EventBounds
            {
                Min = new EventPosition3D { X = bounds.Min.X, Y = bounds.Min.Y, Z = bounds.Min.Z },
                Max = new EventPosition3D { X = bounds.Max.X, Y = bounds.Max.Y, Z = bounds.Max.Z }
            } : null,
            Version = version,
            DeltaType = deltaType,
            SourceAppId = sourceAppId,
            Payload = payload.Data
        };

        var topic = $"map.{channel.RegionId}.{channel.Kind}.updated";
        await _messageBus.TryPublishAsync(topic, eventData, cancellationToken: cancellationToken);
    }

    private async Task PublishMapObjectsChangedEventAsync(ChannelRecord channel, long version, List<ObjectChangeEvent> changes, string? sourceAppId, CancellationToken cancellationToken)
    {
        var windowMs = _configuration.EventAggregationWindowMs;

        if (windowMs <= 0)
        {
            // Aggregation disabled - publish immediately
            await PublishMapObjectsChangedEventDirectAsync(channel, version, changes, sourceAppId, cancellationToken);
            return;
        }

        // Aggregation enabled - buffer the changes
        var maxRetries = _configuration.MaxBufferFlushRetries;
        var buffer = EventAggregationBuffers.GetOrAdd(
            channel.ChannelId,
            _ => new EventAggregationBuffer(
                channel.ChannelId,
                windowMs,
                maxRetries,
                async (channelId, bufferedChanges, bufferedVersion, bufferedSourceAppId, ct) =>
                {
                    // Retrieve channel record for publishing (may have been updated)
                    var channelKey = BuildChannelKey(channelId);
                    var currentChannel = await _stateStoreFactory.GetStore<ChannelRecord>(StateStoreDefinitions.Mapping)
                        .GetAsync(channelKey, ct);

                    if (currentChannel != null)
                    {
                        await PublishMapObjectsChangedEventDirectAsync(currentChannel, bufferedVersion, bufferedChanges, bufferedSourceAppId, ct);
                    }
                },
                cId => { EventAggregationBuffers.TryRemove(cId, out EventAggregationBuffer? _); },
                async (channelId, discardedCount, ex) =>
                {
                    _logger.LogError(ex, "Failed to flush {DiscardedCount} spatial changes for channel {ChannelId} after {MaxRetries} retries, changes discarded",
                        discardedCount, channelId, maxRetries);
                    await _messageBus.TryPublishErrorAsync(
                        "mapping", "EventAggregationBuffer", "flush_failed", ex.Message,
                        dependency: "messaging", endpoint: "internal:buffer-flush",
                        details: $"ChannelId={channelId}, DiscardedChanges={discardedCount}, MaxRetries={maxRetries}",
                        stack: ex.StackTrace);
                }));

        buffer.AddChanges(changes, version, sourceAppId);
    }

    private async Task PublishMapObjectsChangedEventDirectAsync(ChannelRecord channel, long version, List<ObjectChangeEvent> changes, string? sourceAppId, CancellationToken cancellationToken)
    {
        var eventData = new MapObjectsChangedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RegionId = channel.RegionId,
            Kind = channel.Kind.ToString(),
            ChannelId = channel.ChannelId,
            Version = version,
            SourceAppId = sourceAppId,
            Changes = changes
        };

        var topic = $"map.{channel.RegionId}.{channel.Kind}.objects.changed";
        await _messageBus.TryPublishAsync(topic, eventData, cancellationToken: cancellationToken);
    }

    private async Task PublishUnauthorizedWarningAsync(ChannelRecord channel, MapPayload payload, string? attemptedPublisher, bool accepted, CancellationToken cancellationToken)
    {
        var alertConfig = channel.AlertConfig;
        if (alertConfig != null && !alertConfig.Enabled)
        {
            return;
        }

        var warning = new MapUnauthorizedPublishWarning
        {
            Timestamp = DateTimeOffset.UtcNow,
            ChannelId = channel.ChannelId,
            RegionId = channel.RegionId,
            Kind = channel.Kind.ToString(),
            AttemptedPublisher = attemptedPublisher ?? "unknown",
            CurrentAuthority = null,
            HandlingMode = channel.NonAuthorityHandling,
            PublishAccepted = accepted,
            PayloadSummary = alertConfig?.IncludePayloadSummary == true ? payload.ObjectType : null
        };

        var topic = alertConfig?.AlertTopic ?? "map.warnings.unauthorized_publish";
        await _messageBus.TryPublishAsync(topic, warning, cancellationToken: cancellationToken);
    }

    private async Task SubscribeToIngestTopicAsync(Guid channelId, string ingestTopic, CancellationToken cancellationToken)
    {
        // Dispose prior subscription if re-creating channel (prevents leaking subscriptions)
        if (IngestSubscriptions.TryRemove(channelId, out var existingSubscription))
        {
            await existingSubscription.DisposeAsync();
            _logger.LogDebug("Disposed existing subscription for channel {ChannelId} before re-creating", channelId);
        }

        // Subscribe to ingest events for this channel
        var subscription = await _messageSubscriber.SubscribeDynamicAsync<MapIngestEvent>(
            ingestTopic,
            async (evt, ct) => await HandleIngestEventAsync(channelId, evt, ct),
            exchange: null,
            exchangeType: SubscriptionExchangeType.Topic,
            cancellationToken: cancellationToken);

        IngestSubscriptions[channelId] = subscription;
        _logger.LogDebug("Subscribed to ingest topic {Topic} for channel {ChannelId}", ingestTopic, channelId);
    }

    internal async Task HandleIngestEventAsync(Guid channelId, MapIngestEvent evt, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Handling ingest event for channel {ChannelId} with {Count} payloads",
            channelId, evt.Payloads.Count);

        try
        {
            // Get channel info first (needed for NonAuthorityHandling check)
            var channelKey = BuildChannelKey(channelId);
            var channel = await _stateStoreFactory.GetStore<ChannelRecord>(StateStoreDefinitions.Mapping)
                .GetAsync(channelKey, cancellationToken);

            if (channel == null)
            {
                _logger.LogWarning("Channel not found for ingest event: {ChannelId}", channelId);
                return;
            }

            // Validate authority token
            var authorityKey = BuildAuthorityKey(channelId);
            var authority = await _stateStoreFactory.GetStore<AuthorityRecord>(StateStoreDefinitions.Mapping)
                .GetAsync(authorityKey, cancellationToken);

            var isValidAuthority = authority != null &&
                                    authority.AuthorityToken == evt.AuthorityToken &&
                                    authority.ExpiresAt >= DateTimeOffset.UtcNow;

            if (!isValidAuthority)
            {
                // Handle non-authority publish based on channel configuration
                await HandleNonAuthorityIngestAsync(channel, evt, cancellationToken);
                return;
            }

            // Enforce MaxPayloadsPerPublish
            if (evt.Payloads.Count > _configuration.MaxPayloadsPerPublish)
            {
                _logger.LogWarning("Ingest event exceeds MaxPayloadsPerPublish ({Max}), truncating from {Count}",
                    _configuration.MaxPayloadsPerPublish, evt.Payloads.Count);
            }

            var payloadsToProcess = evt.Payloads.Take(_configuration.MaxPayloadsPerPublish).ToList();

            // Process each payload according to its action
            var changes = new List<ObjectChangeEvent>();
            foreach (var payload in payloadsToProcess)
            {
                var objectId = payload.ObjectId ?? Guid.NewGuid();
                var position = payload.Position != null
                    ? new Position3D { X = payload.Position.X, Y = payload.Position.Y, Z = payload.Position.Z }
                    : null;
                var bounds = payload.Bounds != null
                    ? new Bounds
                    {
                        Min = new Position3D { X = payload.Bounds.Min.X, Y = payload.Bounds.Min.Y, Z = payload.Bounds.Min.Z },
                        Max = new Position3D { X = payload.Bounds.Max.X, Y = payload.Bounds.Max.Y, Z = payload.Bounds.Max.Z }
                    }
                    : null;

                var change = new ObjectChange
                {
                    ObjectId = objectId,
                    ObjectType = payload.ObjectType,
                    Action = MapIngestActionToObjectAction(payload.Action),
                    Position = position,
                    Bounds = bounds,
                    Data = payload.Data
                };

                var success = await ProcessObjectChangeWithIndexCleanupAsync(channel.RegionId, channel.Kind, change, cancellationToken);
                if (success)
                {
                    changes.Add(new ObjectChangeEvent
                    {
                        ObjectId = objectId,
                        Action = MapIngestActionToEventAction(payload.Action),
                        ObjectType = payload.ObjectType,
                        Position = payload.Position,
                        Bounds = payload.Bounds,
                        Data = payload.Data
                    });
                }
            }

            if (changes.Count == 0)
            {
                return;
            }

            // Increment version
            var version = await IncrementVersionAsync(channelId, cancellationToken);

            // Publish both layer-level and object-level events with authority's app-id as source
            var authorityAppId = authority?.AuthorityAppId;
            await PublishMapUpdatedEventAsync(channel, bounds: null, version, DeltaType.Delta, new MapPayload { ObjectType = "ingest" }, authorityAppId, cancellationToken);
            await PublishMapObjectsChangedEventAsync(channel, version, changes, authorityAppId, cancellationToken);

            _logger.LogDebug("Processed {Count} payloads from ingest event, version {Version}",
                changes.Count, version);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling ingest event for channel {ChannelId}", channelId);
            await _messageBus.TryPublishErrorAsync(
                "mapping", "HandleIngestEvent", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: $"event:map.ingest.{channelId}",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
        }
    }

    private async Task HandleNonAuthorityIngestAsync(ChannelRecord channel, MapIngestEvent evt, CancellationToken cancellationToken)
    {
        var mode = channel.NonAuthorityHandling;

        switch (mode)
        {
            case NonAuthorityHandlingMode.RejectSilent:
                _logger.LogDebug("Rejecting non-authority ingest silently for channel {ChannelId}", channel.ChannelId);
                return;

            case NonAuthorityHandlingMode.RejectAndAlert:
                _logger.LogWarning("Rejecting non-authority ingest with alert for channel {ChannelId}", channel.ChannelId);
                await PublishUnauthorizedIngestWarningAsync(channel, evt, accepted: false, cancellationToken);
                return;

            case NonAuthorityHandlingMode.AcceptAndAlert:
                _logger.LogWarning("Accepting non-authority ingest with alert for channel {ChannelId}", channel.ChannelId);
                await PublishUnauthorizedIngestWarningAsync(channel, evt, accepted: true, cancellationToken);
                // Process the ingest anyway (recursively but with force flag would be complex, so inline)
                // sourceAppId is null since this is a non-authority publish
                await ProcessIngestPayloadsAsync(channel, evt.Payloads, sourceAppId: null, cancellationToken);
                return;

            default:
                _logger.LogDebug("Unknown NonAuthorityHandlingMode, rejecting ingest for channel {ChannelId}", channel.ChannelId);
                return;
        }
    }

    private async Task ProcessIngestPayloadsAsync(ChannelRecord channel, ICollection<IngestPayload> payloads, string? sourceAppId, CancellationToken cancellationToken)
    {
        var changes = new List<ObjectChangeEvent>();
        foreach (var payload in payloads.Take(_configuration.MaxPayloadsPerPublish))
        {
            var objectId = payload.ObjectId ?? Guid.NewGuid();
            var position = payload.Position != null
                ? new Position3D { X = payload.Position.X, Y = payload.Position.Y, Z = payload.Position.Z }
                : null;
            var bounds = payload.Bounds != null
                ? new Bounds
                {
                    Min = new Position3D { X = payload.Bounds.Min.X, Y = payload.Bounds.Min.Y, Z = payload.Bounds.Min.Z },
                    Max = new Position3D { X = payload.Bounds.Max.X, Y = payload.Bounds.Max.Y, Z = payload.Bounds.Max.Z }
                }
                : null;

            var change = new ObjectChange
            {
                ObjectId = objectId,
                ObjectType = payload.ObjectType,
                Action = MapIngestActionToObjectAction(payload.Action),
                Position = position,
                Bounds = bounds,
                Data = payload.Data
            };

            var success = await ProcessObjectChangeWithIndexCleanupAsync(channel.RegionId, channel.Kind, change, cancellationToken);
            if (success)
            {
                changes.Add(new ObjectChangeEvent
                {
                    ObjectId = objectId,
                    Action = MapIngestActionToEventAction(payload.Action),
                    ObjectType = payload.ObjectType,
                    Position = payload.Position,
                    Bounds = payload.Bounds,
                    Data = payload.Data
                });
            }
        }

        if (changes.Count > 0)
        {
            var version = await IncrementVersionAsync(channel.ChannelId, cancellationToken);
            await PublishMapUpdatedEventAsync(channel, bounds: null, version, DeltaType.Delta, new MapPayload { ObjectType = "ingest" }, sourceAppId, cancellationToken);
            await PublishMapObjectsChangedEventAsync(channel, version, changes, sourceAppId, cancellationToken);
        }
    }

    private async Task PublishUnauthorizedIngestWarningAsync(ChannelRecord channel, MapIngestEvent evt, bool accepted, CancellationToken cancellationToken)
    {
        var alertConfig = channel.AlertConfig;
        if (alertConfig != null && !alertConfig.Enabled)
        {
            return;
        }

        // For ingest events via RabbitMQ, we don't have caller identity - use "unknown"
        var warning = new MapUnauthorizedPublishWarning
        {
            Timestamp = DateTimeOffset.UtcNow,
            ChannelId = channel.ChannelId,
            RegionId = channel.RegionId,
            Kind = channel.Kind.ToString(),
            AttemptedPublisher = "unknown",
            CurrentAuthority = null,
            HandlingMode = channel.NonAuthorityHandling,
            PublishAccepted = accepted,
            PayloadSummary = alertConfig?.IncludePayloadSummary == true ? $"ingest:{evt.Payloads.Count} payloads" : null
        };

        var topic = alertConfig?.AlertTopic ?? "map.warnings.unauthorized_publish";
        await _messageBus.TryPublishAsync(topic, warning, cancellationToken: cancellationToken);
    }

    private static ObjectAction MapIngestActionToObjectAction(IngestPayloadAction action)
    {
        return action switch
        {
            IngestPayloadAction.Create => ObjectAction.Created,
            IngestPayloadAction.Update => ObjectAction.Updated,
            IngestPayloadAction.Delete => ObjectAction.Deleted,
            _ => ObjectAction.Updated
        };
    }

    private static ObjectChangeEventAction MapIngestActionToEventAction(IngestPayloadAction action)
    {
        return action switch
        {
            IngestPayloadAction.Create => ObjectChangeEventAction.Created,
            IngestPayloadAction.Update => ObjectChangeEventAction.Updated,
            IngestPayloadAction.Delete => ObjectChangeEventAction.Deleted,
            _ => ObjectChangeEventAction.Updated
        };
    }

    private static ObjectChangeEventAction MapObjectActionToEventAction(ObjectAction action)
    {
        return action switch
        {
            ObjectAction.Created => ObjectChangeEventAction.Created,
            ObjectAction.Updated => ObjectChangeEventAction.Updated,
            ObjectAction.Deleted => ObjectChangeEventAction.Deleted,
            _ => ObjectChangeEventAction.Updated
        };
    }

    #endregion

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permission service on startup.
    /// Uses generated permission data from x-permissions sections in the OpenAPI schema.
    /// </summary>
    public async Task RegisterServicePermissionsAsync(string appId)
    {
        _logger.LogInformation("Registering Mapping service permissions...");
        await MappingPermissionRegistration.RegisterViaEventAsync(_messageBus, appId, _logger);
    }

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
        private readonly Func<Guid, List<ObjectChangeEvent>, long, string?, CancellationToken, Task> _flushCallback;
        private readonly Action<Guid> _removeCallback;
        private readonly Func<Guid, int, Exception, Task> _errorCallback;
        private readonly int _maxRetries;
        private List<ObjectChangeEvent> _pendingChanges = new();
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
            Func<Guid, List<ObjectChangeEvent>, long, string?, CancellationToken, Task> flushCallback,
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
        public void AddChanges(List<ObjectChangeEvent> changes, long version, string? sourceAppId)
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
            List<ObjectChangeEvent> changesToPublish;
            long version;
            string? sourceAppId;

            lock (_lock)
            {
                if (_disposed || _pendingChanges.Count == 0) return;
                changesToPublish = _pendingChanges;
                _pendingChanges = new List<ObjectChangeEvent>();
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
