using BeyondImmersion.Bannou.Chat.ClientEvents;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Helpers;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Permission;
using BeyondImmersion.BannouService.Protocol;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace BeyondImmersion.BannouService.Chat;

// =============================================================================
// ChatService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by ChatService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (ChatService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IChatService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (ChatService.Helpers.cs):
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
/// Private and internal helper methods for ChatService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class ChatService
{
    /// <summary>
    /// Executes a contract room action (Lock, Archive, Delete, Continue) on a room.
    /// Used by contract event handlers in ChatServiceEvents.cs.
    /// </summary>
    internal async Task ExecuteContractRoomActionAsync(
        ChatRoomModel room, ContractRoomAction action, ChatRoomLockReason lockReason, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.chat", "ChatService.ExecuteContractRoomAction");

        var roomKey = $"{ROOM_KEY_PREFIX}{room.RoomId}";

        switch (action)
        {
            case ContractRoomAction.Lock:
                room.Status = ChatRoomStatus.Locked;
                await _roomStore.SaveAsync(roomKey, room, cancellationToken: ct);
                await _roomCache.SaveAsync(roomKey, room, cancellationToken: ct);

                await _messageBus.PublishChatRoomLockedAsync(new ChatRoomLockedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    RoomId = room.RoomId,
                    Reason = lockReason,
                }, ct);

                var lockParticipants = await GetParticipantsAsync(room.RoomId, ct);
                var lockSessionIds = lockParticipants.Select(p => p.SessionId.ToString()).ToList();
                await _clientEventPublisher.PublishToSessionsAsync(lockSessionIds, new ChatRoomLockedClientEvent
                {
                    RoomId = room.RoomId,
                    Reason = lockReason,
                }, ct);
                break;

            case ContractRoomAction.Archive:
                try
                {
                    var compressResponse = await _resourceClient.ExecuteCompressAsync(
                        new ExecuteCompressRequest { ResourceType = "chat-room", ResourceId = room.RoomId }, ct);

                    room.IsArchived = true;
                    room.Status = ChatRoomStatus.Archived;
                    await _roomStore.SaveAsync(roomKey, room, cancellationToken: ct);
                    await _roomCache.SaveAsync(roomKey, room, cancellationToken: ct);

                    var contractArchiveId = compressResponse.ArchiveId ?? throw new InvalidOperationException(
                        $"Resource service returned null ArchiveId for room {room.RoomId}");

                    await _messageBus.PublishChatRoomArchivedAsync(new ChatRoomArchivedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        RoomId = room.RoomId,
                        ArchiveId = contractArchiveId,
                    }, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to archive room {RoomId} via contract action", room.RoomId);
                }
                break;

            case ContractRoomAction.Delete:
                var deleteParticipants = await GetParticipantsAsync(room.RoomId, ct);
                if (deleteParticipants.Count > 0)
                {
                    var deleteSessionIds = deleteParticipants.Select(p => p.SessionId.ToString()).ToList();
                    await _clientEventPublisher.PublishToSessionsAsync(deleteSessionIds, new ChatRoomDeletedClientEvent
                    {
                        RoomId = room.RoomId,
                        Reason = "Contract action",
                    }, ct);

                    foreach (var p in deleteParticipants)
                    {
                        await ClearParticipantPermissionStateAsync(p.SessionId, ct);
                    }
                }

                await DeleteAllParticipantsAsync(room.RoomId, ct);
                await _roomStore.DeleteAsync(roomKey, ct);
                await _roomCache.DeleteAsync(roomKey, ct);

                await _messageBus.PublishChatRoomDeletedAsync(new ChatRoomDeletedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    RoomId = room.RoomId,
                    RoomTypeCode = room.RoomTypeCode,
                    DisplayName = room.DisplayName,
                    Status = room.Status,
                    ParticipantCount = 0,
                    IsArchived = room.IsArchived,
                    CreatedAt = room.CreatedAt,
                    DeletedReason = "Contract action",
                }, ct);
                break;

            case ContractRoomAction.Continue:
                // No-op
                break;
        }
    }

    private async Task<ChatRoomTypeModel?> FindRoomTypeByCodeAsync(string code, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.chat", "ChatService.FindRoomTypeByCode");

        // Try to find by querying for the code (searches across all scopes)
        var conditions = new List<QueryCondition>
        {
            new() { Path = "$.Code", Operator = QueryOperator.Equals, Value = code }
        };
        var result = await _roomTypeStore.JsonQueryPagedAsync(conditions, 0, 1, cancellationToken: ct);
        return result.Items.FirstOrDefault()?.Value;
    }

    private async Task<ChatRoomModel?> GetRoomWithCacheAsync(Guid roomId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.chat", "ChatService.GetRoomWithCache");

        var roomKey = $"{ROOM_KEY_PREFIX}{roomId}";

        // Try cache first
        var cached = await _roomCache.GetAsync(roomKey, ct);
        if (cached != null)
        {
            return cached;
        }

        // Fall back to MySQL
        var model = await _roomStore.GetAsync(roomKey, ct);
        if (model != null)
        {
            await _roomCache.SaveAsync(roomKey, model, cancellationToken: ct);
        }
        return model;
    }

    private async Task SetParticipantPermissionStateAsync(Guid sessionId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.chat", "ChatService.SetParticipantPermissionState");

        try
        {
            await _permissionClient.UpdateSessionStateAsync(new SessionStateUpdate
            {
                SessionId = sessionId,
                ServiceId = "chat",
                NewState = "in_room",
            }, ct);
        }
        catch (ApiException apiEx)
        {
            _logger.LogWarning(apiEx, "Permission service error when setting state for session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set permission state for session {SessionId}", sessionId);
        }
    }

    private async Task ClearParticipantPermissionStateAsync(Guid sessionId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.chat", "ChatService.ClearParticipantPermissionState");

        try
        {
            await _permissionClient.ClearSessionStateAsync(new ClearSessionStateRequest
            {
                SessionId = sessionId,
                ServiceId = "chat",
            }, ct);
        }
        catch (ApiException apiEx)
        {
            _logger.LogWarning(apiEx, "Permission service error when clearing state for session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear permission state for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Publish typing and end-typing session shortcuts to a participant.
    /// </summary>
    private async Task PublishTypingShortcutsAsync(Guid sessionId, Guid roomId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.chat", "ChatService.PublishTypingShortcuts");

        var sessionIdStr = sessionId.ToString();
        var roomIdStr = roomId.ToString();

        // --- Typing shortcut ---
        var typingRouteGuid = GuidGenerator.GenerateSessionShortcutGuid(
            sessionIdStr, $"chat_typing_{roomIdStr}", "chat", _serverSalt);
        var typingTargetGuid = GuidGenerator.GenerateServiceGuid(
            sessionIdStr, "chat/typing", _serverSalt);

        var typingPayload = new TypingRequest { RoomId = roomId, SessionId = sessionId };

        await _clientEventPublisher.PublishToSessionAsync(sessionIdStr, new ShortcutPublishedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = sessionId,
            Shortcut = new SessionShortcut
            {
                RouteGuid = typingRouteGuid,
                TargetGuid = typingTargetGuid,
                BoundPayload = BannouJson.Serialize(typingPayload),
                Metadata = new SessionShortcutMetadata
                {
                    Name = $"chat_typing_{roomIdStr}",
                    SourceService = "chat",
                    TargetService = "chat",
                    TargetMethod = "POST",
                    TargetEndpoint = "/chat/typing",
                    CreatedAt = DateTimeOffset.UtcNow,
                }
            },
            ReplaceExisting = false
        }, ct);

        // --- End-typing shortcut ---
        var endTypingRouteGuid = GuidGenerator.GenerateSessionShortcutGuid(
            sessionIdStr, $"chat_end_typing_{roomIdStr}", "chat", _serverSalt);
        var endTypingTargetGuid = GuidGenerator.GenerateServiceGuid(
            sessionIdStr, "chat/end-typing", _serverSalt);

        var endTypingPayload = new EndTypingRequest { RoomId = roomId, SessionId = sessionId };

        await _clientEventPublisher.PublishToSessionAsync(sessionIdStr, new ShortcutPublishedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = sessionId,
            Shortcut = new SessionShortcut
            {
                RouteGuid = endTypingRouteGuid,
                TargetGuid = endTypingTargetGuid,
                BoundPayload = BannouJson.Serialize(endTypingPayload),
                Metadata = new SessionShortcutMetadata
                {
                    Name = $"chat_end_typing_{roomIdStr}",
                    SourceService = "chat",
                    TargetService = "chat",
                    TargetMethod = "POST",
                    TargetEndpoint = "/chat/end-typing",
                    CreatedAt = DateTimeOffset.UtcNow,
                }
            },
            ReplaceExisting = false
        }, ct);

        _logger.LogDebug("Published typing shortcuts for session {SessionId} in room {RoomId}",
            sessionId, roomId);
    }

    /// <summary>
    /// Revoke typing and end-typing shortcuts for a departing participant.
    /// Uses per-GUID revocation (not RevokeByService) because a user can be in multiple rooms.
    /// </summary>
    private async Task RevokeTypingShortcutsAsync(Guid sessionId, Guid roomId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.chat", "ChatService.RevokeTypingShortcuts");

        var sessionIdStr = sessionId.ToString();
        var roomIdStr = roomId.ToString();

        var typingRouteGuid = GuidGenerator.GenerateSessionShortcutGuid(
            sessionIdStr, $"chat_typing_{roomIdStr}", "chat", _serverSalt);
        var endTypingRouteGuid = GuidGenerator.GenerateSessionShortcutGuid(
            sessionIdStr, $"chat_end_typing_{roomIdStr}", "chat", _serverSalt);

        await _clientEventPublisher.PublishToSessionAsync(sessionIdStr, new ShortcutRevokedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = sessionId,
            RouteGuid = typingRouteGuid,
            Reason = $"Left room {roomId}"
        }, ct);

        await _clientEventPublisher.PublishToSessionAsync(sessionIdStr, new ShortcutRevokedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = sessionId,
            RouteGuid = endTypingRouteGuid,
            Reason = $"Left room {roomId}"
        }, ct);

        _logger.LogDebug("Revoked typing shortcuts for session {SessionId} in room {RoomId}",
            sessionId, roomId);
    }

    /// <summary>
    /// Remove typing state from sorted set and publish stop event if was active.
    /// </summary>
    private async Task ClearTypingStateAsync(Guid sessionId, Guid roomId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.chat", "ChatService.ClearTypingState");

        var member = $"{roomId:N}:{sessionId:N}";
        var removed = await _participantStore.SortedSetRemoveAsync("typing:active", member, ct);

        if (removed)
        {
            _logger.LogDebug("Cleared typing state for session {SessionId} in room {RoomId}",
                sessionId, roomId);

            await _entitySessionRegistry.PublishToEntitySessionsAsync(
                "chat-room", roomId,
                new ChatTypingStoppedClientEvent
                {
                    RoomId = roomId,
                    ParticipantSessionId = sessionId,
                }, ct);
        }
    }
}
