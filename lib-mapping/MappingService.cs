using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
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

    // Track active ingest subscriptions per channel
    private static readonly ConcurrentDictionary<Guid, IDisposable> IngestSubscriptions = new();

    private const string STATE_STORE = "mapping-statestore";

    // Key prefixes for state storage
    private const string CHANNEL_PREFIX = "map:channel:";
    private const string AUTHORITY_PREFIX = "map:authority:";
    private const string OBJECT_PREFIX = "map:object:";
    private const string SPATIAL_INDEX_PREFIX = "map:index:";
    private const string TYPE_INDEX_PREFIX = "map:type-index:";
    private const string CHECKOUT_PREFIX = "map:checkout:";
    private const string VERSION_PREFIX = "map:version:";
    private const string AFFORDANCE_CACHE_PREFIX = "map:affordance-cache:";

    /// <summary>
    /// Initializes a new instance of the MappingService.
    /// </summary>
    /// <param name="messageBus">Message bus for event publishing.</param>
    /// <param name="messageSubscriber">Message subscriber for event subscriptions.</param>
    /// <param name="stateStoreFactory">Factory for creating state stores.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Service configuration.</param>
    /// <param name="eventConsumer">Event consumer for registering handlers.</param>
    public MappingService(
        IMessageBus messageBus,
        IMessageSubscriber messageSubscriber,
        IStateStoreFactory stateStoreFactory,
        ILogger<MappingService> logger,
        MappingServiceConfiguration configuration,
        IEventConsumer eventConsumer)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _messageSubscriber = messageSubscriber ?? throw new ArgumentNullException(nameof(messageSubscriber));
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        // Register event handlers via partial class (MappingServiceEvents.cs)
        ((IBannouService)this).RegisterEventConsumers(eventConsumer);
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
            return (false, Guid.Empty, default);
        }
    }

    #endregion

    #region Authority Management

    /// <inheritdoc />
    public async Task<(StatusCodes, AuthorityGrant?)> CreateChannelAsync(CreateChannelRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Creating channel for region {RegionId}, kind {Kind}", body.RegionId, body.Kind);

        try
        {
            // Generate channel ID from region + kind (deterministic)
            var channelId = GenerateChannelId(body.RegionId, body.Kind);
            var channelKey = BuildChannelKey(channelId);

            // Check if channel already exists
            var existingChannel = await _stateStoreFactory.GetStore<ChannelRecord>(STATE_STORE)
                .GetAsync(channelKey, cancellationToken);

            if (existingChannel != null)
            {
                // Channel exists - check if authority is available
                var authorityKey = BuildAuthorityKey(channelId);
                var existingAuthority = await _stateStoreFactory.GetStore<AuthorityRecord>(STATE_STORE)
                    .GetAsync(authorityKey, cancellationToken);

                if (existingAuthority != null && existingAuthority.ExpiresAt > DateTimeOffset.UtcNow)
                {
                    _logger.LogWarning("Channel {ChannelId} already has active authority", channelId);
                    return (StatusCodes.Conflict, null);
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
                AlertConfig = body.AlertConfig,
                Version = 1,
                CreatedAt = existingChannel?.CreatedAt ?? now,
                UpdatedAt = now
            };

            await _stateStoreFactory.GetStore<ChannelRecord>(STATE_STORE)
                .SaveAsync(channelKey, channel, cancellationToken: cancellationToken);

            // Create authority record
            var authority = new AuthorityRecord
            {
                ChannelId = channelId,
                AuthorityToken = authorityToken,
                ExpiresAt = expiresAt,
                CreatedAt = now
            };

            var authorityRecordKey = BuildAuthorityKey(channelId);
            await _stateStoreFactory.GetStore<AuthorityRecord>(STATE_STORE)
                .SaveAsync(authorityRecordKey, authority, cancellationToken: cancellationToken);

            // Initialize version counter
            var versionKey = BuildVersionKey(channelId);
            await _stateStoreFactory.GetStore<long>(STATE_STORE)
                .SaveAsync(versionKey, 1L, cancellationToken: cancellationToken);

            // Process initial snapshot if provided
            if (body.InitialSnapshot != null && body.InitialSnapshot.Count > 0)
            {
                await ProcessPayloadsAsync(channelId, body.RegionId, body.Kind, body.InitialSnapshot, cancellationToken);
            }

            // Subscribe to ingest topic for this channel
            await SubscribeToIngestTopicAsync(channelId, ingestTopic, cancellationToken);

            // Publish channel created event
            await PublishChannelCreatedEventAsync(channel, cancellationToken);

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating channel for region {RegionId}", body.RegionId);
            await _messageBus.TryPublishErrorAsync(
                "mapping", "CreateChannel", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/mapping/create-channel",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ReleaseAuthorityResponse?)> ReleaseAuthorityAsync(ReleaseAuthorityRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Releasing authority for channel {ChannelId}", body.ChannelId);

        try
        {
            var (valid, tokenChannelId, _) = ParseAuthorityToken(body.AuthorityToken);
            if (!valid || tokenChannelId != body.ChannelId)
            {
                _logger.LogWarning("Invalid authority token for channel {ChannelId}", body.ChannelId);
                return (StatusCodes.Unauthorized, new ReleaseAuthorityResponse { Released = false });
            }

            var authorityKey = BuildAuthorityKey(body.ChannelId);
            var authority = await _stateStoreFactory.GetStore<AuthorityRecord>(STATE_STORE)
                .GetAsync(authorityKey, cancellationToken);

            if (authority == null || authority.AuthorityToken != body.AuthorityToken)
            {
                _logger.LogWarning("Authority token mismatch for channel {ChannelId}", body.ChannelId);
                return (StatusCodes.Unauthorized, new ReleaseAuthorityResponse { Released = false });
            }

            // Delete authority record
            await _stateStoreFactory.GetStore<AuthorityRecord>(STATE_STORE)
                .DeleteAsync(authorityKey, cancellationToken);

            // Unsubscribe from ingest topic
            if (IngestSubscriptions.TryRemove(body.ChannelId, out var subscription))
            {
                subscription.Dispose();
            }

            _logger.LogInformation("Released authority for channel {ChannelId}", body.ChannelId);
            return (StatusCodes.OK, new ReleaseAuthorityResponse { Released = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing authority for channel {ChannelId}", body.ChannelId);
            await _messageBus.TryPublishErrorAsync(
                "mapping", "ReleaseAuthority", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/mapping/release-authority",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, AuthorityHeartbeatResponse?)> AuthorityHeartbeatAsync(AuthorityHeartbeatRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Processing heartbeat for channel {ChannelId}", body.ChannelId);

        try
        {
            var (valid, tokenChannelId, _) = ParseAuthorityToken(body.AuthorityToken);
            if (!valid || tokenChannelId != body.ChannelId)
            {
                return (StatusCodes.Unauthorized, new AuthorityHeartbeatResponse
                {
                    Valid = false,
                    ExpiresAt = default,
                    Warning = "Invalid authority token"
                });
            }

            var authorityKey = BuildAuthorityKey(body.ChannelId);
            var authority = await _stateStoreFactory.GetStore<AuthorityRecord>(STATE_STORE)
                .GetAsync(authorityKey, cancellationToken);

            if (authority == null || authority.AuthorityToken != body.AuthorityToken)
            {
                return (StatusCodes.Unauthorized, new AuthorityHeartbeatResponse
                {
                    Valid = false,
                    ExpiresAt = default,
                    Warning = "Authority not found or token mismatch"
                });
            }

            // Check if authority has expired
            if (authority.ExpiresAt < DateTimeOffset.UtcNow)
            {
                return (StatusCodes.Unauthorized, new AuthorityHeartbeatResponse
                {
                    Valid = false,
                    ExpiresAt = authority.ExpiresAt,
                    Warning = "Authority has expired"
                });
            }

            // Extend authority
            var newExpiresAt = DateTimeOffset.UtcNow.AddSeconds(_configuration.AuthorityTimeoutSeconds);
            authority.ExpiresAt = newExpiresAt;

            await _stateStoreFactory.GetStore<AuthorityRecord>(STATE_STORE)
                .SaveAsync(authorityKey, authority, cancellationToken: cancellationToken);

            string? warning = null;
            var remainingSeconds = (newExpiresAt - DateTimeOffset.UtcNow).TotalSeconds;
            if (remainingSeconds < _configuration.AuthorityGracePeriodSeconds)
            {
                warning = "Authority expiring soon, increase heartbeat frequency";
            }

            return (StatusCodes.OK, new AuthorityHeartbeatResponse
            {
                Valid = true,
                ExpiresAt = newExpiresAt,
                Warning = warning
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing heartbeat for channel {ChannelId}", body.ChannelId);
            await _messageBus.TryPublishErrorAsync(
                "mapping", "AuthorityHeartbeat", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/mapping/authority-heartbeat",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Publishing

    /// <inheritdoc />
    public async Task<(StatusCodes, PublishMapUpdateResponse?)> PublishMapUpdateAsync(PublishMapUpdateRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Publishing map update to channel {ChannelId}", body.ChannelId);

        try
        {
            // Validate authority
            var (isValid, channel, warning) = await ValidateAuthorityAsync(body.ChannelId, body.AuthorityToken, cancellationToken);

            if (!isValid && channel != null)
            {
                // Handle non-authority publish based on channel config
                return await HandleNonAuthorityPublishAsync(channel, body.Payload, warning, cancellationToken);
            }

            if (!isValid || channel == null)
            {
                return (StatusCodes.Unauthorized, new PublishMapUpdateResponse
                {
                    Accepted = false,
                    Version = 0,
                    Warning = warning
                });
            }

            // Process the payload
            var payloads = new List<MapPayload> { body.Payload };
            var version = await ProcessPayloadsAsync(channel.ChannelId, channel.RegionId, channel.Kind, payloads, cancellationToken);

            // Publish update event
            await PublishMapUpdatedEventAsync(channel, body.Bounds, version, body.DeltaType, body.Payload, cancellationToken);

            return (StatusCodes.OK, new PublishMapUpdateResponse
            {
                Accepted = true,
                Version = version,
                Warning = null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing map update to channel {ChannelId}", body.ChannelId);
            await _messageBus.TryPublishErrorAsync(
                "mapping", "PublishMapUpdate", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/mapping/publish",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, PublishObjectChangesResponse?)> PublishObjectChangesAsync(PublishObjectChangesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Publishing {Count} object changes to channel {ChannelId}", body.Changes.Count, body.ChannelId);

        try
        {
            // Validate authority
            var (isValid, channel, warning) = await ValidateAuthorityAsync(body.ChannelId, body.AuthorityToken, cancellationToken);

            if (!isValid || channel == null)
            {
                return (StatusCodes.Unauthorized, new PublishObjectChangesResponse
                {
                    Accepted = false,
                    AcceptedCount = 0,
                    RejectedCount = body.Changes.Count,
                    Version = 0
                });
            }

            var acceptedCount = 0;
            var rejectedCount = 0;
            var changes = new List<ObjectChangeEvent>();

            foreach (var change in body.Changes)
            {
                try
                {
                    var processed = await ProcessObjectChangeAsync(channel.RegionId, channel.Kind, change, cancellationToken);
                    if (processed)
                    {
                        acceptedCount++;
                        changes.Add(new ObjectChangeEvent
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
            if (changes.Count > 0)
            {
                await PublishMapObjectsChangedEventAsync(channel, version, changes, cancellationToken);
            }

            return (StatusCodes.OK, new PublishObjectChangesResponse
            {
                Accepted = acceptedCount > 0,
                AcceptedCount = acceptedCount,
                RejectedCount = rejectedCount,
                Version = version
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing object changes to channel {ChannelId}", body.ChannelId);
            await _messageBus.TryPublishErrorAsync(
                "mapping", "PublishObjectChanges", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/mapping/publish-objects",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, RequestSnapshotResponse?)> RequestSnapshotAsync(RequestSnapshotRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Requesting snapshot for region {RegionId}", body.RegionId);

        try
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
                var version = await _stateStoreFactory.GetStore<long>(STATE_STORE).GetAsync(versionKey, cancellationToken);
                if (version > maxVersion)
                {
                    maxVersion = version;
                }
            }

            // Check if payload is too large for inline response
            string? payloadRef = null;
            if (objects.Count > 1000)
            {
                // For large payloads, we would store in lib-asset and return reference
                // For now, we'll return inline but note this limitation
                _logger.LogWarning("Snapshot contains {Count} objects, consider using payloadRef for large snapshots", objects.Count);
            }

            return (StatusCodes.OK, new RequestSnapshotResponse
            {
                RegionId = body.RegionId,
                Objects = objects,
                PayloadRef = payloadRef,
                Version = maxVersion
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting snapshot for region {RegionId}", body.RegionId);
            await _messageBus.TryPublishErrorAsync(
                "mapping", "RequestSnapshot", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/mapping/request-snapshot",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Queries

    /// <inheritdoc />
    public async Task<(StatusCodes, QueryPointResponse?)> QueryPointAsync(QueryPointRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Querying point at ({X}, {Y}, {Z}) in region {RegionId}",
            body.Position.X, body.Position.Y, body.Position.Z, body.RegionId);

        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying point in region {RegionId}", body.RegionId);
            await _messageBus.TryPublishErrorAsync(
                "mapping", "QueryPoint", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/mapping/query/point",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, QueryBoundsResponse?)> QueryBoundsAsync(QueryBoundsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Querying bounds in region {RegionId}", body.RegionId);

        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying bounds in region {RegionId}", body.RegionId);
            await _messageBus.TryPublishErrorAsync(
                "mapping", "QueryBounds", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/mapping/query/bounds",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, QueryObjectsByTypeResponse?)> QueryObjectsByTypeAsync(QueryObjectsByTypeRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Querying objects of type {ObjectType} in region {RegionId}", body.ObjectType, body.RegionId);

        try
        {
            var typeIndexKey = BuildTypeIndexKey(body.RegionId, body.ObjectType);
            var objectIds = await _stateStoreFactory.GetStore<List<Guid>>(STATE_STORE)
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
                var obj = await _stateStoreFactory.GetStore<MapObject>(STATE_STORE)
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying objects by type in region {RegionId}", body.RegionId);
            await _messageBus.TryPublishErrorAsync(
                "mapping", "QueryObjectsByType", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/mapping/query/objects-by-type",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, AffordanceQueryResponse?)> QueryAffordanceAsync(AffordanceQueryRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Querying affordance {AffordanceType} in region {RegionId}", body.AffordanceType, body.RegionId);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Check cache if freshness allows
            if (body.Freshness != AffordanceFreshness.Fresh)
            {
                var cachedResult = await TryGetCachedAffordanceAsync(body, cancellationToken);
                if (cachedResult != null)
                {
                    stopwatch.Stop();
                    cachedResult.QueryMetadata = new AffordanceQueryMetadata
                    {
                        KindsSearched = new List<string>(),
                        ObjectsEvaluated = 0,
                        CandidatesGenerated = 0,
                        SearchDurationMs = (int)stopwatch.ElapsedMilliseconds,
                        CacheHit = true
                    };
                    return (StatusCodes.OK, cachedResult);
                }
            }

            // Determine which map kinds to search based on affordance type
            var kindsToSearch = GetKindsForAffordanceType(body.AffordanceType);
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
                    var excluded = body.ExcludePositions.Any(p =>
                        Math.Abs(p.X - candidate.Position.X) < 1.0 &&
                        Math.Abs(p.Y - candidate.Position.Y) < 1.0 &&
                        Math.Abs(p.Z - candidate.Position.Z) < 1.0);
                    if (excluded)
                    {
                        continue;
                    }
                }

                var score = ScoreAffordance(candidate, body.AffordanceType, body.CustomAffordance, body.ActorCapabilities);

                if (score >= body.MinScore)
                {
                    scoredLocations.Add(new AffordanceLocation
                    {
                        Position = candidate.Position ?? new Position3D { X = 0, Y = 0, Z = 0 },
                        Bounds = candidate.Bounds,
                        Score = score,
                        Features = ExtractFeatures(candidate, body.AffordanceType),
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying affordance in region {RegionId}", body.RegionId);
            await _messageBus.TryPublishErrorAsync(
                "mapping", "QueryAffordance", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/mapping/query/affordance",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Authoring

    /// <inheritdoc />
    public async Task<(StatusCodes, AuthoringCheckoutResponse?)> CheckoutForAuthoringAsync(AuthoringCheckoutRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Checkout for authoring - region {RegionId}, kind {Kind}, editor {EditorId}",
            body.RegionId, body.Kind, body.EditorId);

        try
        {
            var checkoutKey = BuildCheckoutKey(body.RegionId, body.Kind);
            var existingCheckout = await _stateStoreFactory.GetStore<CheckoutRecord>(STATE_STORE)
                .GetAsync(checkoutKey, cancellationToken);

            // Check if already locked
            if (existingCheckout != null)
            {
                // Check if lock has expired
                if (existingCheckout.ExpiresAt > DateTimeOffset.UtcNow)
                {
                    return (StatusCodes.Conflict, new AuthoringCheckoutResponse
                    {
                        Success = false,
                        AuthorityToken = null,
                        ExpiresAt = null,
                        LockedBy = existingCheckout.EditorId,
                        LockedAt = existingCheckout.CreatedAt
                    });
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

            await _stateStoreFactory.GetStore<CheckoutRecord>(STATE_STORE)
                .SaveAsync(checkoutKey, checkout, cancellationToken: cancellationToken);

            _logger.LogInformation("Checkout acquired for region {RegionId}, kind {Kind} by editor {EditorId}",
                body.RegionId, body.Kind, body.EditorId);

            return (StatusCodes.OK, new AuthoringCheckoutResponse
            {
                Success = true,
                AuthorityToken = authorityToken,
                ExpiresAt = expiresAt,
                LockedBy = null,
                LockedAt = null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking out for authoring");
            await _messageBus.TryPublishErrorAsync(
                "mapping", "CheckoutForAuthoring", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/mapping/authoring/checkout",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, AuthoringCommitResponse?)> CommitAuthoringAsync(AuthoringCommitRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Committing authoring changes - region {RegionId}, kind {Kind}", body.RegionId, body.Kind);

        try
        {
            var checkoutKey = BuildCheckoutKey(body.RegionId, body.Kind);
            var checkout = await _stateStoreFactory.GetStore<CheckoutRecord>(STATE_STORE)
                .GetAsync(checkoutKey, cancellationToken);

            if (checkout == null || checkout.AuthorityToken != body.AuthorityToken)
            {
                return (StatusCodes.Unauthorized, new AuthoringCommitResponse
                {
                    Success = false,
                    Version = null
                });
            }

            // Increment version and release lock
            var channelId = GenerateChannelId(body.RegionId, body.Kind);
            var version = await IncrementVersionAsync(channelId, cancellationToken);

            // Release the checkout
            await _stateStoreFactory.GetStore<CheckoutRecord>(STATE_STORE)
                .DeleteAsync(checkoutKey, cancellationToken);

            _logger.LogInformation("Committed authoring changes for region {RegionId}, kind {Kind}, version {Version}",
                body.RegionId, body.Kind, version);

            return (StatusCodes.OK, new AuthoringCommitResponse
            {
                Success = true,
                Version = version
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error committing authoring changes");
            await _messageBus.TryPublishErrorAsync(
                "mapping", "CommitAuthoring", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/mapping/authoring/commit",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, AuthoringReleaseResponse?)> ReleaseAuthoringAsync(AuthoringReleaseRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Releasing authoring checkout - region {RegionId}, kind {Kind}", body.RegionId, body.Kind);

        try
        {
            var checkoutKey = BuildCheckoutKey(body.RegionId, body.Kind);
            var checkout = await _stateStoreFactory.GetStore<CheckoutRecord>(STATE_STORE)
                .GetAsync(checkoutKey, cancellationToken);

            if (checkout == null || checkout.AuthorityToken != body.AuthorityToken)
            {
                return (StatusCodes.Unauthorized, new AuthoringReleaseResponse { Released = false });
            }

            await _stateStoreFactory.GetStore<CheckoutRecord>(STATE_STORE)
                .DeleteAsync(checkoutKey, cancellationToken);

            _logger.LogInformation("Released authoring checkout for region {RegionId}, kind {Kind}",
                body.RegionId, body.Kind);

            return (StatusCodes.OK, new AuthoringReleaseResponse { Released = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing authoring checkout");
            await _messageBus.TryPublishErrorAsync(
                "mapping", "ReleaseAuthoring", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/mapping/authoring/release",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
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

    private async Task<(bool isValid, ChannelRecord? channel, string? warning)> ValidateAuthorityAsync(
        Guid channelId, string authorityToken, CancellationToken cancellationToken)
    {
        var channelKey = BuildChannelKey(channelId);
        var channel = await _stateStoreFactory.GetStore<ChannelRecord>(STATE_STORE)
            .GetAsync(channelKey, cancellationToken);

        if (channel == null)
        {
            return (false, null, "Channel not found");
        }

        var (valid, tokenChannelId, expiresAt) = ParseAuthorityToken(authorityToken);
        if (!valid || tokenChannelId != channelId)
        {
            return (false, channel, "Invalid authority token");
        }

        if (expiresAt < DateTimeOffset.UtcNow)
        {
            return (false, channel, "Authority token has expired");
        }

        var authorityKey = BuildAuthorityKey(channelId);
        var authority = await _stateStoreFactory.GetStore<AuthorityRecord>(STATE_STORE)
            .GetAsync(authorityKey, cancellationToken);

        if (authority == null || authority.AuthorityToken != authorityToken)
        {
            return (false, channel, "Authority token not recognized");
        }

        return (true, channel, null);
    }

    private async Task<(StatusCodes, PublishMapUpdateResponse?)> HandleNonAuthorityPublishAsync(
        ChannelRecord channel, MapPayload payload, string? warning, CancellationToken cancellationToken)
    {
        var mode = channel.NonAuthorityHandling;

        switch (mode)
        {
            case NonAuthorityHandlingMode.Reject_silent:
                return (StatusCodes.Unauthorized, new PublishMapUpdateResponse
                {
                    Accepted = false,
                    Version = 0,
                    Warning = warning
                });

            case NonAuthorityHandlingMode.Reject_and_alert:
                await PublishUnauthorizedWarningAsync(channel, payload, false, cancellationToken);
                return (StatusCodes.Unauthorized, new PublishMapUpdateResponse
                {
                    Accepted = false,
                    Version = 0,
                    Warning = warning
                });

            case NonAuthorityHandlingMode.Accept_and_alert:
                await PublishUnauthorizedWarningAsync(channel, payload, true, cancellationToken);
                // Process the payload anyway
                var payloads = new List<MapPayload> { payload };
                var version = await ProcessPayloadsAsync(channel.ChannelId, channel.RegionId, channel.Kind, payloads, cancellationToken);
                return (StatusCodes.OK, new PublishMapUpdateResponse
                {
                    Accepted = true,
                    Version = version,
                    Warning = "Published despite lacking authority (accept_and_alert mode)"
                });

            default:
                return (StatusCodes.Unauthorized, new PublishMapUpdateResponse
                {
                    Accepted = false,
                    Version = 0,
                    Warning = warning
                });
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

            // Save object
            var objectKey = BuildObjectKey(regionId, objectId);
            await _stateStoreFactory.GetStore<MapObject>(STATE_STORE)
                .SaveAsync(objectKey, mapObject, cancellationToken: cancellationToken);

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
                await _stateStoreFactory.GetStore<MapObject>(STATE_STORE)
                    .SaveAsync(objectKey, newObject, cancellationToken: cancellationToken);

                if (change.Position != null)
                {
                    await UpdateSpatialIndexAsync(regionId, kind, change.ObjectId, change.Position, cancellationToken);
                }
                if (!string.IsNullOrEmpty(change.ObjectType))
                {
                    await UpdateTypeIndexAsync(regionId, change.ObjectType, change.ObjectId, cancellationToken);
                }
                return true;

            case ObjectAction.Updated:
                var existing = await _stateStoreFactory.GetStore<MapObject>(STATE_STORE)
                    .GetAsync(objectKey, cancellationToken);
                if (existing == null)
                {
                    return false;
                }
                if (change.Position != null) existing.Position = change.Position;
                if (change.Bounds != null) existing.Bounds = change.Bounds;
                if (change.Data != null) existing.Data = change.Data;
                existing.Version++;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                await _stateStoreFactory.GetStore<MapObject>(STATE_STORE)
                    .SaveAsync(objectKey, existing, cancellationToken: cancellationToken);
                return true;

            case ObjectAction.Deleted:
                await _stateStoreFactory.GetStore<MapObject>(STATE_STORE)
                    .DeleteAsync(objectKey, cancellationToken);
                // Note: Index cleanup would be handled separately
                return true;

            default:
                return false;
        }
    }

    private async Task UpdateSpatialIndexAsync(Guid regionId, MapKind kind, Guid objectId, Position3D position, CancellationToken cancellationToken)
    {
        var cell = GetCellCoordinates(position);
        var indexKey = BuildSpatialIndexKey(regionId, kind, cell.cellX, cell.cellY, cell.cellZ);
        var objectIds = await _stateStoreFactory.GetStore<List<Guid>>(STATE_STORE)
            .GetAsync(indexKey, cancellationToken) ?? new List<Guid>();

        if (!objectIds.Contains(objectId))
        {
            objectIds.Add(objectId);
            await _stateStoreFactory.GetStore<List<Guid>>(STATE_STORE)
                .SaveAsync(indexKey, objectIds, cancellationToken: cancellationToken);
        }
    }

    private async Task UpdateSpatialIndexForBoundsAsync(Guid regionId, MapKind kind, Guid objectId, Bounds bounds, CancellationToken cancellationToken)
    {
        var cells = GetCellsForBounds(bounds);
        foreach (var cell in cells)
        {
            var indexKey = BuildSpatialIndexKey(regionId, kind, cell.cellX, cell.cellY, cell.cellZ);
            var objectIds = await _stateStoreFactory.GetStore<List<Guid>>(STATE_STORE)
                .GetAsync(indexKey, cancellationToken) ?? new List<Guid>();

            if (!objectIds.Contains(objectId))
            {
                objectIds.Add(objectId);
                await _stateStoreFactory.GetStore<List<Guid>>(STATE_STORE)
                    .SaveAsync(indexKey, objectIds, cancellationToken: cancellationToken);
            }
        }
    }

    private async Task UpdateTypeIndexAsync(Guid regionId, string objectType, Guid objectId, CancellationToken cancellationToken)
    {
        var indexKey = BuildTypeIndexKey(regionId, objectType);
        var objectIds = await _stateStoreFactory.GetStore<List<Guid>>(STATE_STORE)
            .GetAsync(indexKey, cancellationToken) ?? new List<Guid>();

        if (!objectIds.Contains(objectId))
        {
            objectIds.Add(objectId);
            await _stateStoreFactory.GetStore<List<Guid>>(STATE_STORE)
                .SaveAsync(indexKey, objectIds, cancellationToken: cancellationToken);
        }
    }

    private async Task<long> IncrementVersionAsync(Guid channelId, CancellationToken cancellationToken)
    {
        var versionKey = BuildVersionKey(channelId);
        var currentVersion = await _stateStoreFactory.GetStore<long>(STATE_STORE)
            .GetAsync(versionKey, cancellationToken);
        var newVersion = currentVersion + 1;
        await _stateStoreFactory.GetStore<long>(STATE_STORE)
            .SaveAsync(versionKey, newVersion, cancellationToken: cancellationToken);
        return newVersion;
    }

    private async Task<List<MapObject>> QueryObjectsInRegionAsync(Guid regionId, MapKind kind, Bounds? bounds, int maxObjects, CancellationToken cancellationToken)
    {
        if (bounds != null)
        {
            return await QueryObjectsInBoundsAsync(regionId, kind, bounds, maxObjects, cancellationToken);
        }

        // Without bounds, we need to scan all cells - this is expensive
        // In production, you'd want pagination or a region index
        var objects = new List<MapObject>();
        // For now, return empty list when no bounds specified
        // A full implementation would need region-level indexing
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
            var objectIds = await _stateStoreFactory.GetStore<List<Guid>>(STATE_STORE)
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
                var obj = await _stateStoreFactory.GetStore<MapObject>(STATE_STORE)
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

    #region Affordance Helpers

    private static List<MapKind> GetKindsForAffordanceType(AffordanceType type)
    {
        return type switch
        {
            AffordanceType.Ambush => new List<MapKind> { MapKind.Static_geometry, MapKind.Dynamic_objects, MapKind.Navigation },
            AffordanceType.Shelter => new List<MapKind> { MapKind.Static_geometry, MapKind.Dynamic_objects },
            AffordanceType.Vista => new List<MapKind> { MapKind.Terrain, MapKind.Static_geometry, MapKind.Points_of_interest },
            AffordanceType.Choke_point => new List<MapKind> { MapKind.Navigation, MapKind.Static_geometry },
            AffordanceType.Gathering_spot => new List<MapKind> { MapKind.Points_of_interest, MapKind.Static_geometry },
            AffordanceType.Dramatic_reveal => new List<MapKind> { MapKind.Points_of_interest, MapKind.Terrain },
            AffordanceType.Hidden_path => new List<MapKind> { MapKind.Navigation, MapKind.Static_geometry },
            AffordanceType.Defensible_position => new List<MapKind> { MapKind.Static_geometry, MapKind.Terrain, MapKind.Navigation },
            AffordanceType.Custom => Enum.GetValues<MapKind>().ToList(),
            _ => Enum.GetValues<MapKind>().ToList()
        };
    }

    private double ScoreAffordance(MapObject candidate, AffordanceType type, CustomAffordance? custom, ActorCapabilities? actor)
    {
        // Base score from object data
        var score = 0.5;

        if (candidate.Data is IDictionary<string, object> data)
        {
            // Check for common affordance-related properties
            if (data.TryGetValue("cover_rating", out var coverRating) && coverRating is double cr)
            {
                if (type == AffordanceType.Ambush || type == AffordanceType.Shelter || type == AffordanceType.Defensible_position)
                {
                    score += cr * 0.3;
                }
            }

            if (data.TryGetValue("elevation", out var elevation) && elevation is double el)
            {
                if (type == AffordanceType.Vista || type == AffordanceType.Dramatic_reveal)
                {
                    score += Math.Min(el / 100.0, 0.3);
                }
            }

            if (data.TryGetValue("sightlines", out var sightlines) && sightlines is int sl)
            {
                if (type == AffordanceType.Ambush || type == AffordanceType.Vista)
                {
                    score += Math.Min(sl * 0.05, 0.2);
                }
            }
        }

        // Apply actor capability modifiers
        if (actor != null)
        {
            // Size affects cover requirements
            if (type == AffordanceType.Shelter || type == AffordanceType.Ambush)
            {
                score *= actor.Size switch
                {
                    ActorSize.Tiny => 1.2,
                    ActorSize.Small => 1.1,
                    ActorSize.Medium => 1.0,
                    ActorSize.Large => 0.9,
                    ActorSize.Huge => 0.8,
                    _ => 1.0
                };
            }

            // Stealth rating affects ambush scoring
            if (type == AffordanceType.Ambush && actor.StealthRating.HasValue)
            {
                score *= 1.0 + actor.StealthRating.Value * 0.2;
            }
        }

        // Handle custom affordance
        if (type == AffordanceType.Custom && custom != null)
        {
            score = ScoreCustomAffordance(candidate, custom);
        }

        return Math.Clamp(score, 0.0, 1.0);
    }

    private double ScoreCustomAffordance(MapObject candidate, CustomAffordance custom)
    {
        var score = 0.5;

        if (candidate.Data is IDictionary<string, object> data && custom.Requires is IDictionary<string, object> requires)
        {
            // Check required criteria
            foreach (var (key, value) in requires)
            {
                if (key == "objectTypes" && value is IList<object> types)
                {
                    if (!types.Contains(candidate.ObjectType))
                    {
                        return 0.0; // Required criteria not met
                    }
                }
                else if (data.TryGetValue(key, out var candidateValue))
                {
                    if (value is IDictionary<string, object> constraint)
                    {
                        if (constraint.TryGetValue("min", out var min) && candidateValue is double cv && min is double mv)
                        {
                            if (cv < mv) return 0.0;
                        }
                    }
                }
            }
        }

        // Apply preferences (boost but don't require)
        if (candidate.Data is IDictionary<string, object> dataDict && custom.Prefers is IDictionary<string, object> prefers)
        {
            foreach (var (key, _) in prefers)
            {
                if (dataDict.ContainsKey(key))
                {
                    score += 0.1;
                }
            }
        }

        // Apply exclusions
        if (candidate.Data is IDictionary<string, object> excludeData && custom.Excludes is IDictionary<string, object> excludes)
        {
            foreach (var (key, _) in excludes)
            {
                if (excludeData.ContainsKey(key))
                {
                    return 0.0; // Excluded
                }
            }
        }

        return Math.Clamp(score, 0.0, 1.0);
    }

    private static object? ExtractFeatures(MapObject candidate, AffordanceType type)
    {
        var features = new Dictionary<string, object>();

        if (candidate.Data is IDictionary<string, object> data)
        {
            // Extract relevant features based on affordance type
            var relevantKeys = type switch
            {
                AffordanceType.Ambush => new[] { "cover_rating", "sightlines", "concealment" },
                AffordanceType.Shelter => new[] { "cover_rating", "protection", "capacity" },
                AffordanceType.Vista => new[] { "elevation", "visibility_range", "sightlines" },
                AffordanceType.Choke_point => new[] { "width", "defensibility", "exit_count" },
                AffordanceType.Gathering_spot => new[] { "capacity", "comfort", "accessibility" },
                AffordanceType.Dramatic_reveal => new[] { "elevation", "view_target", "approach_direction" },
                AffordanceType.Hidden_path => new[] { "concealment", "width", "traversability" },
                AffordanceType.Defensible_position => new[] { "cover_rating", "sightlines", "exit_count", "elevation" },
                _ => Array.Empty<string>()
            };

            foreach (var key in relevantKeys)
            {
                if (data.TryGetValue(key, out var value))
                {
                    features[key] = value;
                }
            }
        }

        features["objectType"] = candidate.ObjectType;
        return features.Count > 1 ? features : null;
    }

    private async Task<AffordanceQueryResponse?> TryGetCachedAffordanceAsync(AffordanceQueryRequest body, CancellationToken cancellationToken)
    {
        var boundsHash = body.Bounds != null
            ? $"{body.Bounds.Min.X},{body.Bounds.Min.Y},{body.Bounds.Min.Z}:{body.Bounds.Max.X},{body.Bounds.Max.Y},{body.Bounds.Max.Z}"
            : "all";
        var cacheKey = BuildAffordanceCacheKey(body.RegionId, body.AffordanceType, boundsHash);

        var cached = await _stateStoreFactory.GetStore<CachedAffordanceResult>(STATE_STORE)
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

        await _stateStoreFactory.GetStore<CachedAffordanceResult>(STATE_STORE)
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
            NonAuthorityHandling = channel.NonAuthorityHandling.ToString(),
            Version = channel.Version,
            CreatedAt = channel.CreatedAt,
            UpdatedAt = channel.UpdatedAt
        };

        await _messageBus.TryPublishAsync("mapping.channel.created", eventData, cancellationToken: cancellationToken);
    }

    private async Task PublishMapUpdatedEventAsync(ChannelRecord channel, Bounds? bounds, long version, DeltaType deltaType, MapPayload payload, CancellationToken cancellationToken)
    {
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
            DeltaType = deltaType == DeltaType.Snapshot ? MapUpdatedEventDeltaType.Snapshot : MapUpdatedEventDeltaType.Delta,
            Payload = payload.Data
        };

        var topic = $"map.{channel.RegionId}.{channel.Kind}.updated";
        await _messageBus.TryPublishAsync(topic, eventData, cancellationToken: cancellationToken);
    }

    private async Task PublishMapObjectsChangedEventAsync(ChannelRecord channel, long version, List<ObjectChangeEvent> changes, CancellationToken cancellationToken)
    {
        var eventData = new MapObjectsChangedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RegionId = channel.RegionId,
            Kind = channel.Kind.ToString(),
            ChannelId = channel.ChannelId,
            Version = version,
            Changes = changes
        };

        var topic = $"map.{channel.RegionId}.{channel.Kind}.objects.changed";
        await _messageBus.TryPublishAsync(topic, eventData, cancellationToken: cancellationToken);
    }

    private async Task PublishUnauthorizedWarningAsync(ChannelRecord channel, MapPayload payload, bool accepted, CancellationToken cancellationToken)
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
            AttemptedPublisher = "unknown", // Would come from session context
            CurrentAuthority = null,
            HandlingMode = channel.NonAuthorityHandling switch
            {
                NonAuthorityHandlingMode.Reject_and_alert => MapUnauthorizedPublishWarningHandlingMode.Reject_and_alert,
                NonAuthorityHandlingMode.Accept_and_alert => MapUnauthorizedPublishWarningHandlingMode.Accept_and_alert,
                NonAuthorityHandlingMode.Reject_silent => MapUnauthorizedPublishWarningHandlingMode.Reject_silent,
                _ => MapUnauthorizedPublishWarningHandlingMode.Reject_and_alert
            },
            PublishAccepted = accepted,
            PayloadSummary = alertConfig?.IncludePayloadSummary == true ? payload.ObjectType : null
        };

        var topic = alertConfig?.AlertTopic ?? "map.warnings.unauthorized_publish";
        await _messageBus.TryPublishAsync(topic, warning, cancellationToken: cancellationToken);
    }

    private async Task SubscribeToIngestTopicAsync(Guid channelId, string ingestTopic, CancellationToken cancellationToken)
    {
        // Subscribe to ingest events for this channel
        var subscription = await _messageSubscriber.SubscribeAsync<MapIngestEvent>(
            ingestTopic,
            async (evt, ct) => await HandleIngestEventAsync(channelId, evt, ct),
            cancellationToken);

        IngestSubscriptions[channelId] = subscription;
        _logger.LogDebug("Subscribed to ingest topic {Topic} for channel {ChannelId}", ingestTopic, channelId);
    }

    internal async Task HandleIngestEventAsync(Guid channelId, MapIngestEvent evt, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Handling ingest event for channel {ChannelId} with {Count} payloads",
            channelId, evt.Payloads.Count);

        try
        {
            // Validate authority token
            var authorityKey = BuildAuthorityKey(channelId);
            var authority = await _stateStoreFactory.GetStore<AuthorityRecord>(STATE_STORE)
                .GetAsync(authorityKey, cancellationToken);

            if (authority == null || authority.AuthorityToken != evt.AuthorityToken)
            {
                _logger.LogWarning("Invalid authority token in ingest event for channel {ChannelId}", channelId);
                return;
            }

            if (authority.ExpiresAt < DateTimeOffset.UtcNow)
            {
                _logger.LogWarning("Expired authority token in ingest event for channel {ChannelId}", channelId);
                return;
            }

            // Get channel info
            var channelKey = BuildChannelKey(channelId);
            var channel = await _stateStoreFactory.GetStore<ChannelRecord>(STATE_STORE)
                .GetAsync(channelKey, cancellationToken);

            if (channel == null)
            {
                _logger.LogWarning("Channel not found for ingest event: {ChannelId}", channelId);
                return;
            }

            // Process payloads
            var mapPayloads = evt.Payloads.Select(p => new MapPayload
            {
                ObjectId = p.ObjectId ?? Guid.NewGuid(),
                ObjectType = p.ObjectType,
                Position = p.Position != null ? new Position3D { X = p.Position.X, Y = p.Position.Y, Z = p.Position.Z } : null,
                Bounds = p.Bounds != null ? new Bounds
                {
                    Min = new Position3D { X = p.Bounds.Min.X, Y = p.Bounds.Min.Y, Z = p.Bounds.Min.Z },
                    Max = new Position3D { X = p.Bounds.Max.X, Y = p.Bounds.Max.Y, Z = p.Bounds.Max.Z }
                } : null,
                Data = p.Data
            }).ToList();

            var version = await ProcessPayloadsAsync(channelId, channel.RegionId, channel.Kind, mapPayloads, cancellationToken);

            _logger.LogDebug("Processed {Count} payloads from ingest event, version {Version}",
                evt.Payloads.Count, version);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling ingest event for channel {ChannelId}", channelId);
        }
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
    public async Task RegisterServicePermissionsAsync()
    {
        _logger.LogInformation("Registering Mapping service permissions...");
        await MappingPermissionRegistration.RegisterViaEventAsync(_messageBus, _logger);
    }

    #endregion

    #region Internal Records

    internal class ChannelRecord
    {
        public Guid ChannelId { get; set; }
        public Guid RegionId { get; set; }
        public MapKind Kind { get; set; }
        public NonAuthorityHandlingMode NonAuthorityHandling { get; set; }
        public NonAuthorityAlertConfig? AlertConfig { get; set; }
        public long Version { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    internal class AuthorityRecord
    {
        public Guid ChannelId { get; set; }
        public string AuthorityToken { get; set; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
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

    #endregion
}
