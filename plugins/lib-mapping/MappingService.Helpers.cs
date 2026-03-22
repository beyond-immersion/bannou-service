using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace BeyondImmersion.BannouService.Mapping;

// =============================================================================
// MappingService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by MappingService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (MappingService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IMappingService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (MappingService.Helpers.cs):
//     Contains all private/internal helper methods, core logic extracted
//     from endpoints, event publishing helpers, query builders, mapping
//     functions, and any other non-public methods. Every async method in
//     this file MUST call ITelemetryProvider.StartActivity to ensure
//     sub-operations are properly instrumented.
//
// Structural tests enforce both rules:
//   - Services_PrimaryFile_DoesNotCallStartActivity
//   - Services_HelperFiles_HaveStartActivityWhenAsync
//
// WHAT GOES HERE:
//   - Private async helper methods (with StartActivity spans)
//   - Private sync helper methods (query builders, mappers, validators)
//   - Internal static key builders (already in primary file by convention,
//     but may be moved here if the primary file is large)
//   - Event publishing helper methods
//   - Any extracted "core" logic (e.g., CreateAccountCoreAsync)
//
// WHAT STAYS IN THE PRIMARY FILE:
//   - Public interface method implementations (/// <inheritdoc/> methods)
//   - Constructor and field declarations
//   - Constants and key prefix definitions
//
// See: docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md (T30)
// See: docs/reference/HELPERS-AND-COMMON-PATTERNS.md
// =============================================================================

/// <summary>
/// Private and internal helper methods for MappingService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class MappingService
{
    private async Task<(StatusCodes, PublishObjectChangesResponse?)> ProcessAuthorizedObjectChangesAsync(
        ChannelRecord channel, ICollection<ObjectChange> changes, string? sourceAppId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.ProcessAuthorizedObjectChangesAsync");
        var acceptedCount = 0;
        var rejectedCount = 0;
        var changeEvents = new List<ObjectChangeRecord>();

        foreach (var change in changes)
        {
            try
            {
                var processed = await ProcessObjectChangeAsync(channel.RegionId, channel.Kind, change, cancellationToken);
                if (processed)
                {
                    acceptedCount++;
                    changeEvents.Add(new ObjectChangeRecord
                    {
                        ObjectId = change.ObjectId,
                        Action = change.Action,
                        ObjectType = change.ObjectType,
                        Position = change.Position,
                        Bounds = change.Bounds,
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
            AcceptedCount = acceptedCount,
            RejectedCount = rejectedCount,
            Version = version
        });
    }

    private async Task<(StatusCodes, PublishObjectChangesResponse?)> HandleNonAuthorityObjectChangesAsync(
        ChannelRecord channel, ICollection<ObjectChange> changes, string? warning, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.HandleNonAuthorityObjectChangesAsync");
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
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.PublishUnauthorizedObjectChangesWarningAsync");
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
            Kind = channel.Kind,
            AttemptedPublisher = "unknown",
            CurrentAuthority = null,
            HandlingMode = channel.NonAuthorityHandling,
            PublishAccepted = accepted,
            PayloadSummary = payloadSummary
        };

        await _messageBus.PublishMapUnauthorizedPublishWarningAsync(warning, cancellationToken);
    }
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
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.ValidateAuthorityAsync");
        var channelKey = BuildChannelKey(channelId);
        var channel = await _channelStore
            .GetAsync(channelKey, cancellationToken);

        if (channel == null)
        {
            return (false, null, null, "Channel not found");
        }

        // Opaque token validation: parse only to extract channelId for basic validation,
        // but expiry is checked ONLY against AuthorityRecord.ExpiresAt (which is updated by heartbeat)
        var (tokenChannelId, _) = ParseAuthorityToken(authorityToken);
        if (tokenChannelId == null || tokenChannelId != channelId)
        {
            return (false, channel, null, "Invalid authority token");
        }

        var authorityKey = BuildAuthorityKey(channelId);
        var authority = await _authorityStore
            .GetAsync(authorityKey, cancellationToken);

        if (authority == null || authority.AuthorityToken != authorityToken)
        {
            return (false, channel, null, "Authority token not recognized");
        }

        // Check expiry against the stored AuthorityRecord (updated by heartbeat), not the token
        if (authority.ExpiresAt < DateTimeOffset.UtcNow)
        {
            // Publish authority expired event (fire-and-forget for monitoring)
            _ = _messageBus.PublishMappingAuthorityExpiredAsync(new MappingAuthorityExpiredEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ChannelId = channelId,
                RegionId = channel.RegionId,
                Kind = channel.Kind,
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
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.HandleNonAuthorityPublishAsync");
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
                    Version = version,
                });

            default:
                _logger.LogDebug("Non-authority publish rejected (unknown mode) for channel {ChannelId}", channel.ChannelId);
                return (StatusCodes.Unauthorized, null);
        }
    }

    private async Task<long> ProcessPayloadsAsync(Guid channelId, Guid regionId, MapKind kind,
        ICollection<MapPayload> payloads, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.ProcessPayloadsAsync");
        var now = DateTimeOffset.UtcNow;
        var version = await IncrementVersionAsync(channelId, cancellationToken);

        foreach (var payload in payloads)
        {
            var objectId = payload.ObjectId ?? Guid.NewGuid();

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
            await _objectStore
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
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.ProcessObjectChangeAsync");
        // Delegate to the version with index cleanup
        return await ProcessObjectChangeWithIndexCleanupAsync(regionId, kind, change, cancellationToken);
    }

    private async Task<bool> ProcessObjectChangeWithIndexCleanupAsync(Guid regionId, MapKind kind, ObjectChange change, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.ProcessObjectChangeWithIndexCleanupAsync");
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
                await _objectStore
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
                var existing = await _objectStore
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
                await _objectStore
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
                var toDelete = await _objectStore
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

                await _objectStore
                    .DeleteAsync(objectKey, cancellationToken);
                return true;

            default:
                return false;
        }
    }

    private async Task UpdateSpatialIndexAsync(Guid regionId, MapKind kind, Guid objectId, Position3D position, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.UpdateSpatialIndexAsync");
        var cell = GetCellCoordinates(position);
        var indexKey = BuildSpatialIndexKey(regionId, kind, cell.cellX, cell.cellY, cell.cellZ);
        await _indexStore.AddToSetAsync<Guid>(indexKey, objectId, cancellationToken: cancellationToken);
    }

    private async Task UpdateSpatialIndexForBoundsAsync(Guid regionId, MapKind kind, Guid objectId, Bounds bounds, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.UpdateSpatialIndexForBoundsAsync");
        var cells = GetCellsForBounds(bounds);
        foreach (var cell in cells)
        {
            var indexKey = BuildSpatialIndexKey(regionId, kind, cell.cellX, cell.cellY, cell.cellZ);
            await _indexStore.AddToSetAsync<Guid>(indexKey, objectId, cancellationToken: cancellationToken);
        }
    }

    private async Task UpdateTypeIndexAsync(Guid regionId, string objectType, Guid objectId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.UpdateTypeIndexAsync");
        var indexKey = BuildTypeIndexKey(regionId, objectType);
        await _indexStore.AddToSetAsync<Guid>(indexKey, objectId, cancellationToken: cancellationToken);
    }

    internal static string BuildRegionIndexKey(Guid regionId, MapKind kind) => $"{REGION_INDEX_PREFIX}{regionId}:{kind}";

    private async Task AddToRegionIndexAsync(Guid regionId, MapKind kind, Guid objectId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.AddToRegionIndexAsync");
        var indexKey = BuildRegionIndexKey(regionId, kind);
        await _indexStore.AddToSetAsync<Guid>(indexKey, objectId, cancellationToken: cancellationToken);
    }

    private async Task RemoveFromRegionIndexAsync(Guid regionId, MapKind kind, Guid objectId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.RemoveFromRegionIndexAsync");
        var indexKey = BuildRegionIndexKey(regionId, kind);
        await _indexStore.RemoveFromSetAsync<Guid>(indexKey, objectId, cancellationToken: cancellationToken);
    }

    private async Task RemoveFromSpatialIndexAsync(Guid regionId, MapKind kind, Guid objectId, Position3D position, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.RemoveFromSpatialIndexAsync");
        var cell = GetCellCoordinates(position);
        var indexKey = BuildSpatialIndexKey(regionId, kind, cell.cellX, cell.cellY, cell.cellZ);
        await _indexStore.RemoveFromSetAsync<Guid>(indexKey, objectId, cancellationToken: cancellationToken);
    }

    private async Task RemoveFromSpatialIndexForBoundsAsync(Guid regionId, MapKind kind, Guid objectId, Bounds bounds, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.RemoveFromSpatialIndexForBoundsAsync");
        var cells = GetCellsForBounds(bounds);
        foreach (var cell in cells)
        {
            var indexKey = BuildSpatialIndexKey(regionId, kind, cell.cellX, cell.cellY, cell.cellZ);
            await _indexStore.RemoveFromSetAsync<Guid>(indexKey, objectId, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromTypeIndexAsync(Guid regionId, string objectType, Guid objectId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.RemoveFromTypeIndexAsync");
        var indexKey = BuildTypeIndexKey(regionId, objectType);
        await _indexStore.RemoveFromSetAsync<Guid>(indexKey, objectId, cancellationToken: cancellationToken);
    }

    private const string MapDataContentType = "application/json";

    private async Task<string?> UploadLargePayloadToAssetAsync(byte[] data, string filename, Guid regionId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.UploadLargePayloadToAssetAsync");

        // Asset service is L3 (optional) - graceful degradation if unavailable
        var assetClient = _serviceProvider.GetService<IAssetClient>();
        if (assetClient == null)
        {
            _logger.LogDebug("Asset service not available, skipping large payload upload for region {RegionId}", regionId);
            return null;
        }

        try
        {
            var uploadRequest = new UploadRequest
            {
                CreatedBy = "mapping",
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

            var uploadResponse = await assetClient.RequestUploadAsync(uploadRequest, cancellationToken);

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

            var assetMetadata = await assetClient.CompleteUploadAsync(completeRequest, cancellationToken);

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
            await _messageBus.TryPublishErrorAsync(
                "mapping", "UploadLargePayloadToAsset", "upload_failed", ex.Message,
                dependency: "asset", endpoint: "presigned-url-upload",
                details: $"RegionId={regionId}", stack: ex.StackTrace);
            return null;
        }
    }

    private async Task ClearRequiresConsumeForAuthorityAsync(Guid regionId, IEnumerable<MapKind> kinds, string authorityToken, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.ClearRequiresConsumeForAuthorityAsync");
        foreach (var kind in kinds)
        {
            var channelId = GenerateChannelId(regionId, kind);
            var authorityKey = BuildAuthorityKey(channelId);
            var authority = await _authorityStore
                .GetAsync(authorityKey, cancellationToken);

            if (authority != null && authority.AuthorityToken == authorityToken && authority.RequiresConsumeBeforePublish)
            {
                authority.RequiresConsumeBeforePublish = false;
                await _authorityStore
                    .SaveAsync(authorityKey, authority, cancellationToken: cancellationToken);
                _logger.LogDebug("Cleared RequiresConsumeBeforePublish flag for channel {ChannelId}", channelId);
            }
        }
    }

    private async Task ClearChannelDataAsync(Guid channelId, Guid regionId, MapKind kind, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.ClearChannelDataAsync");
        // Get the region index to find all objects
        var regionIndexKey = BuildRegionIndexKey(regionId, kind);
        var objectIds = await _indexStore
            .GetSetAsync<Guid>(regionIndexKey, cancellationToken);

        _logger.LogDebug("Clearing {Count} objects from channel {ChannelId}", objectIds.Count, channelId);

        // Delete all objects and their index entries
        foreach (var objectId in objectIds)
        {
            var objectKey = BuildObjectKey(regionId, objectId);
            var obj = await _objectStore.GetAsync(objectKey, cancellationToken);

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

            await _objectStore.DeleteAsync(objectKey, cancellationToken);
        }

        // Clear region index
        await _indexStore
            .DeleteSetAsync(regionIndexKey, cancellationToken);

        // Reset version counter
        var versionKey = BuildVersionKey(channelId);
        await _versionStore
            .SaveAsync(versionKey, new LongWrapper { Value = 0L }, cancellationToken: cancellationToken);
    }

    private async Task<long> IncrementVersionAsync(Guid channelId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.IncrementVersionAsync");
        var versionKey = BuildVersionKey(channelId);

        // Atomic increment via Redis INCR — multi-instance safe per IMPLEMENTATION TENETS
        if (_redisOps != null)
        {
            return await _redisOps.IncrementAsync(versionKey, cancellationToken: cancellationToken);
        }

        // InMemory fallback: non-atomic read-write (acceptable for single-instance testing only)
        var versionWrapper = await _versionStore.GetAsync(versionKey, cancellationToken);
        var newVersion = (versionWrapper?.Value ?? 0) + 1;
        await _versionStore.SaveAsync(versionKey, new LongWrapper { Value = newVersion }, cancellationToken: cancellationToken);
        return newVersion;
    }

    private async Task<List<MapObject>> QueryObjectsInRegionAsync(Guid regionId, MapKind kind, Bounds? bounds, int maxObjects, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.QueryObjectsInRegionAsync");
        if (bounds != null)
        {
            return await QueryObjectsInBoundsAsync(regionId, kind, bounds, maxObjects, cancellationToken);
        }

        // Full region query - use region index that tracks all objects in a region+kind
        var regionIndexKey = BuildRegionIndexKey(regionId, kind);
        var objectIds = await _indexStore
            .GetSetAsync<Guid>(regionIndexKey, cancellationToken);

        var objects = new List<MapObject>();
        foreach (var objectId in objectIds.Take(maxObjects))
        {
            var objectKey = BuildObjectKey(regionId, objectId);
            var obj = await _objectStore
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
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.QueryObjectsInBoundsAsync");
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
            var objectIds = await _indexStore
                .GetSetAsync<Guid>(indexKey, cancellationToken);

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
                var obj = await _objectStore
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
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.TryGetCachedAffordanceAsync");
        var boundsHash = body.Bounds != null
            ? $"{body.Bounds.Min.X},{body.Bounds.Min.Y},{body.Bounds.Min.Z}:{body.Bounds.Max.X},{body.Bounds.Max.Y},{body.Bounds.Max.Z}"
            : "all";
        var cacheKey = BuildAffordanceCacheKey(body.RegionId, body.AffordanceType, boundsHash);

        var cached = await _affordanceCacheStore
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
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.CacheAffordanceResultAsync");
        var boundsHash = body.Bounds != null
            ? $"{body.Bounds.Min.X},{body.Bounds.Min.Y},{body.Bounds.Min.Z}:{body.Bounds.Max.X},{body.Bounds.Max.Y},{body.Bounds.Max.Z}"
            : "all";
        var cacheKey = BuildAffordanceCacheKey(body.RegionId, body.AffordanceType, boundsHash);

        var cached = new CachedAffordanceResult
        {
            Response = response,
            CachedAt = DateTimeOffset.UtcNow
        };

        await _affordanceCacheStore
            .SaveAsync(cacheKey, cached, cancellationToken: cancellationToken);
    }

    #endregion
    #region Event Publishing

    private async Task PublishChannelCreatedEventAsync(ChannelRecord channel, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.PublishChannelCreatedEventAsync");
        var eventData = new MappingChannelCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ChannelId = channel.ChannelId,
            RegionId = channel.RegionId,
            Kind = channel.Kind,
            NonAuthorityHandling = channel.NonAuthorityHandling,
            Version = channel.Version,
            CreatedAt = channel.CreatedAt,
            UpdatedAt = channel.UpdatedAt
        };

        await _messageBus.PublishMappingChannelCreatedAsync(eventData, cancellationToken);
    }

    private async Task PublishChannelUpdatedEventAsync(ChannelRecord channel, ChannelRecord previousChannel, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.PublishChannelUpdatedEventAsync");

        var changedFields = new List<string>();
        if (channel.NonAuthorityHandling != previousChannel.NonAuthorityHandling) changedFields.Add("nonAuthorityHandling");
        if (channel.Kind != previousChannel.Kind) changedFields.Add("kind");
        if (channel.Version != previousChannel.Version) changedFields.Add("version");
        if (channel.UpdatedAt != previousChannel.UpdatedAt) changedFields.Add("updatedAt");

        var eventData = new MappingChannelUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ChannelId = channel.ChannelId,
            RegionId = channel.RegionId,
            Kind = channel.Kind,
            NonAuthorityHandling = channel.NonAuthorityHandling,
            Version = channel.Version,
            CreatedAt = channel.CreatedAt,
            UpdatedAt = channel.UpdatedAt,
            ChangedFields = changedFields
        };

        await _messageBus.PublishMappingChannelUpdatedAsync(eventData, cancellationToken);
    }

    private async Task PublishMapUpdatedEventAsync(ChannelRecord channel, Bounds? bounds, long version, DeltaType deltaType, MapPayload payload, string? sourceAppId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.PublishMapUpdatedEventAsync");
        // MapUpdatedEvent publishes immediately - payload-level coalescing is complex.
        // Event aggregation is implemented for MapObjectsChangedEvent which has discrete changes.
        var eventData = new MapUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RegionId = channel.RegionId,
            Kind = channel.Kind,
            ChannelId = channel.ChannelId,
            Bounds = bounds,
            Version = version,
            DeltaType = deltaType,
            SourceAppId = sourceAppId,
            Payload = payload.Data
        };

        await _messageBus.PublishMapUpdatedAsync(eventData, channel.RegionId, channel.Kind, cancellationToken);
    }

    private async Task PublishMapObjectsChangedEventAsync(ChannelRecord channel, long version, List<ObjectChangeRecord> changes, string? sourceAppId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.PublishMapObjectsChangedEventAsync");
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
                    var currentChannel = await _channelStore
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

    private async Task PublishMapObjectsChangedEventDirectAsync(ChannelRecord channel, long version, List<ObjectChangeRecord> changes, string? sourceAppId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.PublishMapObjectsChangedEventDirectAsync");
        var eventData = new MapObjectsChangedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RegionId = channel.RegionId,
            Kind = channel.Kind,
            ChannelId = channel.ChannelId,
            Version = version,
            SourceAppId = sourceAppId,
            Changes = changes
        };

        await _messageBus.PublishMapObjectsChangedAsync(eventData, channel.RegionId, channel.Kind, cancellationToken);
    }

    private async Task PublishUnauthorizedWarningAsync(ChannelRecord channel, MapPayload payload, string? attemptedPublisher, bool accepted, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.PublishUnauthorizedWarningAsync");
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
            Kind = channel.Kind,
            AttemptedPublisher = attemptedPublisher ?? "unknown",
            CurrentAuthority = null,
            HandlingMode = channel.NonAuthorityHandling,
            PublishAccepted = accepted,
            PayloadSummary = alertConfig?.IncludePayloadSummary == true ? payload.ObjectType : null
        };

        await _messageBus.PublishMapUnauthorizedPublishWarningAsync(warning, cancellationToken);
    }

    private async Task SubscribeToIngestTopicAsync(Guid channelId, string ingestTopic, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.SubscribeToIngestTopicAsync");
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
            exchangeType: ExchangeType.Topic,
            cancellationToken: cancellationToken);

        IngestSubscriptions[channelId] = subscription;
        _logger.LogDebug("Subscribed to ingest topic {Topic} for channel {ChannelId}", ingestTopic, channelId);
    }

    internal async Task HandleIngestEventAsync(Guid channelId, MapIngestEvent evt, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.HandleIngestEventAsync");
        _logger.LogDebug("Handling ingest event for channel {ChannelId} with {Count} payloads",
            channelId, evt.Payloads.Count);

        try
        {
            // Get channel info first (needed for NonAuthorityHandling check)
            var channelKey = BuildChannelKey(channelId);
            var channel = await _channelStore
                .GetAsync(channelKey, cancellationToken);

            if (channel == null)
            {
                _logger.LogWarning("Channel not found for ingest event: {ChannelId}", channelId);
                return;
            }

            // Validate authority token
            var authorityKey = BuildAuthorityKey(channelId);
            var authority = await _authorityStore
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
            var changes = new List<ObjectChangeRecord>();
            foreach (var payload in payloadsToProcess)
            {
                var objectId = payload.ObjectId ?? Guid.NewGuid();
                var position = payload.Position != null
                    ? new Position3D { X = payload.Position.X, Y = payload.Position.Y, Z = payload.Position.Z }
                    : (Position3D?)null;
                var bounds = payload.Bounds != null
                    ? new Bounds
                    {
                        Min = new Position3D { X = payload.Bounds.Min.X, Y = payload.Bounds.Min.Y, Z = payload.Bounds.Min.Z },
                        Max = new Position3D { X = payload.Bounds.Max.X, Y = payload.Bounds.Max.Y, Z = payload.Bounds.Max.Z }
                    }
                    : (Bounds?)null;

                var change = new ObjectChange
                {
                    ObjectId = objectId,
                    ObjectType = payload.ObjectType,
                    Action = payload.Action,
                    Position = position,
                    Bounds = bounds,
                    Data = payload.Data
                };

                var success = await ProcessObjectChangeWithIndexCleanupAsync(channel.RegionId, channel.Kind, change, cancellationToken);
                if (success)
                {
                    changes.Add(new ObjectChangeRecord
                    {
                        ObjectId = objectId,
                        Action = payload.Action,
                        ObjectType = payload.ObjectType,
                        Position = position,
                        Bounds = bounds,
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
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.HandleNonAuthorityIngestAsync");
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
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.ProcessIngestPayloadsAsync");
        var changes = new List<ObjectChangeRecord>();
        foreach (var payload in payloads.Take(_configuration.MaxPayloadsPerPublish))
        {
            var objectId = payload.ObjectId ?? Guid.NewGuid();
            var position = payload.Position != null
                ? new Position3D { X = payload.Position.X, Y = payload.Position.Y, Z = payload.Position.Z }
                : (Position3D?)null;
            var bounds = payload.Bounds != null
                ? new Bounds
                {
                    Min = new Position3D { X = payload.Bounds.Min.X, Y = payload.Bounds.Min.Y, Z = payload.Bounds.Min.Z },
                    Max = new Position3D { X = payload.Bounds.Max.X, Y = payload.Bounds.Max.Y, Z = payload.Bounds.Max.Z }
                }
                : (Bounds?)null;

            var change = new ObjectChange
            {
                ObjectId = objectId,
                ObjectType = payload.ObjectType,
                Action = payload.Action,
                Position = position,
                Bounds = bounds,
                Data = payload.Data
            };

            var success = await ProcessObjectChangeWithIndexCleanupAsync(channel.RegionId, channel.Kind, change, cancellationToken);
            if (success)
            {
                changes.Add(new ObjectChangeRecord
                {
                    ObjectId = objectId,
                    Action = payload.Action,
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
        using var activity = _telemetryProvider.StartActivity("bannou.mapping", "MappingService.PublishUnauthorizedIngestWarningAsync");
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
            Kind = channel.Kind,
            AttemptedPublisher = "unknown",
            CurrentAuthority = null,
            HandlingMode = channel.NonAuthorityHandling,
            PublishAccepted = accepted,
            PayloadSummary = alertConfig?.IncludePayloadSummary == true ? $"ingest:{evt.Payloads.Count} payloads" : null
        };

        await _messageBus.PublishMapUnauthorizedPublishWarningAsync(warning, cancellationToken);
    }


    #endregion
}
