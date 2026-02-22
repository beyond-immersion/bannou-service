# Generated Client Events Reference

> **Source**: `schemas/*-client-events.yaml`
> **Do not edit manually** - regenerate with `make generate-docs`

This document lists all typed events available for subscription in the Bannou Client SDK.

## Quick Reference

| Event Type | Event Name | Description |
|------------|------------|-------------|
| `BundleCreationCompleteEvent` | `asset.bundle.creation.complete` | Sent when bundle creation from asset_ids completes. |
| `BundleValidationCompleteEvent` | `asset.bundle.validation.complete` | Sent when a bundle upload has been validated and processed. |
| `BundleValidationFailedEvent` | `asset.bundle.validation.failed` | Sent when bundle validation fails. Includes detailed error i... |
| `MetabundleCreationCompleteEvent` | `asset.metabundle.creation.complete` | Sent when async metabundle creation job completes (success o... |
| `AssetProcessingCompleteEvent` | `asset.processing.complete` | Sent when asset processing (e.g., texture mipmaps, model LOD... |
| `AssetProcessingFailedEvent` | `asset.processing.failed` | Sent when asset processing fails. Includes retry information... |
| `AssetReadyEvent` | `asset.ready` | Final notification that an asset is ready for use. |
| `AssetUploadCompleteEvent` | `asset.upload.complete` | Sent when an asset upload has completed (success or failure)... |
| `ChatMessageDeletedClientEvent` | `chat.message_deleted` | Sent to room participants when a message is deleted. |
| `ChatMessagePinnedEvent` | `chat.message_pinned` | Sent to room participants when a message is pinned or unpinn... |
| `ChatMessageReceivedEvent` | `chat.message_received` | Sent to room participants when a new message is received. |
| `ChatParticipantBannedClientEvent` | `chat.participant_banned` | Sent to all room participants when someone is banned. |
| `ChatParticipantJoinedClientEvent` | `chat.participant_joined` | Sent to room participants when someone joins the room. |
| `ChatParticipantKickedClientEvent` | `chat.participant_kicked` | Sent to all room participants when someone is kicked. |
| `ChatParticipantLeftClientEvent` | `chat.participant_left` | Sent to room participants when someone leaves the room. |
| `ChatParticipantMutedClientEvent` | `chat.participant_muted` | Sent to all room participants when someone is muted. |
| `ChatParticipantUnmutedClientEvent` | `chat.participant_unmuted` | Sent to all room participants when someone is unmuted. |
| `ChatRoomDeletedClientEvent` | `chat.room_deleted` | Sent to room participants when the room is being deleted. |
| `ChatRoomLockedClientEvent` | `chat.room_locked` | Sent to room participants when the room is locked. |
| `ChatTypingStartedClientEvent` | `chat.typing_started` | Sent to room participants when someone starts typing. |
| `ChatTypingStoppedClientEvent` | `chat.typing_stopped` | Sent to room participants when someone stops typing. |
| `CapabilityManifestEvent` | `connect.capability_manifest` | Sent to client when their available API capabilities change. |
| `DisconnectNotificationEvent` | `connect.disconnect_notification` | Sent to client before WebSocket connection is closed. |
| `GameActionResultEvent` | `game_session.action_result` | Sent to relevant players when a game action produces results... |
| `SessionChatReceivedEvent` | `game_session.chat_received` | Sent to recipients when a chat message is posted in the sess... |
| `PlayerJoinedEvent` | `game_session.player_joined` | Sent to all session participants when a new player joins. |
| `PlayerKickedEvent` | `game_session.player_kicked` | Sent to all session participants when a player is kicked. |
| `PlayerLeftEvent` | `game_session.player_left` | Sent to all session participants when a player leaves volunt... |
| `SessionStateChangedEvent` | `game_session.state_changed` | Sent to all session participants when the session state chan... |
| `GameStateUpdatedEvent` | `game_session.state_updated` | Sent when game state changes that all players should see. |
| `MatchmakingCancelledEvent` | `matchmaking.cancelled` | Sent when matchmaking is cancelled for any reason. |
| `MatchConfirmedEvent` | `matchmaking.match_confirmed` | Sent to all match participants when all players have accepte... |
| `MatchDeclinedEvent` | `matchmaking.match_declined` | Sent to all match participants when someone declines. |
| `MatchFoundEvent` | `matchmaking.match_found` | Sent to all matched players when a match is formed. |
| `MatchPlayerAcceptedEvent` | `matchmaking.player_accepted` | Sent to all match participants when a player accepts. |
| `QueueJoinedEvent` | `matchmaking.queue_joined` | Sent to the player when they successfully join a matchmaking... |
| `MatchmakingStatusUpdateEvent` | `matchmaking.status_update` | Periodic status update sent to players in queue. |
| `SystemErrorEvent` | `system.error` | Generic error notification sent to client. |
| `SystemNotificationEvent` | `system.notification` | Generic notification event for system-level messages. |
| `VoiceBroadcastConsentRequestEvent` | `voice.broadcast_consent_request` | Sent to all room participants when someone requests broadcas... |
| `VoiceBroadcastConsentUpdateEvent` | `voice.broadcast_consent_update` | Sent to all room participants when the broadcast consent sta... |
| `VoicePeerJoinedEvent` | `voice.peer_joined` | Sent to existing room participants when a new peer joins. |
| `VoicePeerLeftEvent` | `voice.peer_left` | Sent to remaining room participants when a peer leaves. |
| `VoicePeerUpdatedEvent` | `voice.peer_updated` | Sent when a peer updates their SIP endpoint (e.g., ICE candi... |
| `VoiceRoomClosedEvent` | `voice.room_closed` | Sent to all room participants when the voice room is closed. |
| `VoiceRoomStateEvent` | `voice.room_state` | Sent to a client when they join a voice room. |
| `VoiceTierUpgradeEvent` | `voice.tier_upgrade` | Sent to all room participants when the voice tier upgrades. |
| `WorldstateTimeSyncEvent` | `worldstate.time_sync` | Published on period-changed boundaries, ratio changes, admin... |

---

## Usage Pattern

```csharp
using BeyondImmersion.Bannou.Client;

var client = new BannouClient();
await client.ConnectWithTokenAsync(url, token);

// Subscribe to typed events - returns a disposable handle
using var subscription = client.OnEvent<ChatMessageReceivedEvent>(evt =>
{
    Console.WriteLine($"[{evt.SenderId}]: {evt.Message}");
});

// Or subscribe to multiple event types
using var matchSub = client.OnEvent<MatchFoundEvent>(evt =>
{
    Console.WriteLine($"Match found! Players: {evt.PlayerCount}");
});

// Use ClientEventRegistry for runtime type discovery
var eventName = ClientEventRegistry.GetEventName<ChatMessageReceivedEvent>();
// Returns: "game_session.chat_received"
```

---

## Asset Client Events API

Server-to-client push events for the Asset service. These events notify clients of upload completions, processing results, bundle validations, and asset availability delivered via WebSocket.

### `BundleCreationCompleteEvent`

**Event Name**: `asset.bundle.creation.complete`

Sent when bundle creation from asset_ids completes.

**Properties**:

| Property | Description |
|----------|-------------|
| `assetCount` | Number of assets in the bundle |
| `bundleId` | Human-readable ID of the created bundle |
| `downloadUrl` | Pre-signed download URL (on success) |
| `errorCode` | Error code on failure |
| `errorMessage` | Human-readable error description |
| `size` | Bundle file size in bytes |
| `success` | Whether creation completed successfully |

### `BundleValidationCompleteEvent`

**Event Name**: `asset.bundle.validation.complete`

Sent when a bundle upload has been validated and processed.

**Properties**:

| Property | Description |
|----------|-------------|
| `assetsRegistered` | Number of assets extracted and registered |
| `bundleId` | Assigned human-readable bundle ID on success |
| `duplicatesSkipped` | Assets with matching hash already in storage |
| `success` | Whether validation passed |
| `uploadId` | Correlates with the bundle upload request |
| `warnings` | Non-fatal warnings encountered during validation |

### `BundleValidationFailedEvent`

**Event Name**: `asset.bundle.validation.failed`

Sent when bundle validation fails. Includes detailed error information.

**Properties**:

| Property | Description |
|----------|-------------|
| `errors` | List of validation errors that caused the failure |
| `uploadId` | Correlates with the bundle upload request |

### `MetabundleCreationCompleteEvent`

**Event Name**: `asset.metabundle.creation.complete`

Sent when async metabundle creation job completes (success or failure).

**Properties**:

| Property | Description |
|----------|-------------|
| `assetCount` | Total number of assets in the metabundle |
| `downloadUrl` | Pre-signed download URL (on success) |
| `errorCode` | Error code on failure |
| `errorMessage` | Human-readable error description |
| `jobId` | Job ID from the original async creation request |
| `metabundleId` | Human-readable ID of the metabundle |
| `processingTimeMs` | Total processing time in milliseconds |
| `sizeBytes` | Metabundle file size in bytes |
| `standaloneAssetCount` | Number of standalone assets included |
| `status` | Final job status |
| `success` | Whether creation completed successfully |

### `AssetProcessingCompleteEvent`

**Event Name**: `asset.processing.complete`

Sent when asset processing (e.g., texture mipmaps, model LODs) completes.

**Properties**:

| Property | Description |
|----------|-------------|
| `assetId` | ID of the asset that was processed |
| `errorCode` | Error code indicating the type of processing failu |
| `errorMessage` | Human-readable error description |
| `outputs` | Generated derivative assets (mipmaps, LODs, etc.) |
| `processingType` | Type of processing that was performed on the asset |
| `success` | Whether processing completed successfully |

### `AssetProcessingFailedEvent`

**Event Name**: `asset.processing.failed`

Sent when asset processing fails. Includes retry information.

**Properties**:

| Property | Description |
|----------|-------------|
| `assetId` | ID of the asset that failed processing |
| `errorCode` | Error code indicating the type of processing failu |
| `errorMessage` | Human-readable error description |
| `retryAfterMs` | Suggested retry delay in milliseconds |
| `retryAvailable` | Whether the operation can be retried |

### `AssetReadyEvent`

**Event Name**: `asset.ready`

Final notification that an asset is ready for use.

**Properties**:

| Property | Description |
|----------|-------------|
| `assetId` | ID of the asset that is now ready |
| `contentHash` | SHA256 hash of the asset |
| `contentType` | MIME content type |
| `metadata` | Asset metadata. No Bannou plugin reads specific ke |
| `size` | File size in bytes |
| `versionId` | Version ID of this asset |

### `AssetUploadCompleteEvent`

**Event Name**: `asset.upload.complete`

Sent when an asset upload has completed (success or failure).

**Properties**:

| Property | Description |
|----------|-------------|
| `assetId` | Asset ID assigned on success |
| `contentHash` | SHA256 hash of uploaded content |
| `errorCode` | Error code indicating the type of upload failure |
| `errorMessage` | Human-readable error description |
| `size` | File size in bytes |
| `success` | Whether the upload completed successfully |
| `uploadId` | Correlates with the upload request |

---

## Chat Client Events API

Server-to-client push events for the Chat service. These events notify connected clients of real-time chat activity: messages received, participant changes, and room state updates. Delivered via We...

### `ChatMessageDeletedClientEvent`

**Event Name**: `chat.message_deleted`

Sent to room participants when a message is deleted.

**Properties**:

| Property | Description |
|----------|-------------|
| `deletedByDisplayName` | Display name of the user who deleted the message |
| `messageId` | Unique identifier of the deleted message |
| `roomId` | Room the message was deleted from |

### `ChatMessagePinnedEvent`

**Event Name**: `chat.message_pinned`

Sent to room participants when a message is pinned or unpinned.

**Properties**:

| Property | Description |
|----------|-------------|
| `isPinned` | Whether the message is now pinned (true) or unpinn |
| `messageId` | Unique identifier of the pinned/unpinned message |
| `roomId` | Room containing the message |

### `ChatMessageReceivedEvent`

**Event Name**: `chat.message_received`

Sent to room participants when a new message is received.

**Properties**:

| Property | Description |
|----------|-------------|
| `customPayload` | Custom JSON payload string (populated for Custom f |
| `displayName` | Display name of the message sender |
| `emojiCode` | Emoji code string (populated for Emoji format mess |
| `emojiSetId` | Optional emoji set identifier (populated for Emoji |
| `messageFormat` | Message format type |
| `messageId` | Unique identifier for the message |
| `roomId` | Room the message was sent to |
| `senderId` | Opaque sender entity ID |
| `senderType` | Opaque sender type identifier (e.g., "account", "n |
| `sentimentCategory` | Sentiment category (populated for Sentiment format |
| `sentimentIntensity` | Sentiment intensity from 0.0 to 1.0 (populated for |
| `textContent` | Text content (populated for Text format messages) |

### `ChatParticipantBannedClientEvent`

**Event Name**: `chat.participant_banned`

Sent to all room participants when someone is banned.

**Properties**:

| Property | Description |
|----------|-------------|
| `reason` | Optional reason provided for the ban |
| `roomId` | Room the participant was banned from |
| `targetDisplayName` | Display name of the banned participant |
| `targetSessionId` | Connect session ID of the banned participant |

### `ChatParticipantJoinedClientEvent`

**Event Name**: `chat.participant_joined`

Sent to room participants when someone joins the room.

**Properties**:

| Property | Description |
|----------|-------------|
| `currentCount` | Total participant count after this join |
| `displayName` | Display name of the joining participant |
| `participantSessionId` | Connect session ID of the joining participant |
| `role` | Role assigned to the participant |
| `roomId` | Room the participant joined |
| `senderId` | Opaque sender entity ID |
| `senderType` | Opaque sender type identifier |

### `ChatParticipantKickedClientEvent`

**Event Name**: `chat.participant_kicked`

Sent to all room participants when someone is kicked.

**Properties**:

| Property | Description |
|----------|-------------|
| `reason` | Optional reason provided for the kick |
| `roomId` | Room the participant was kicked from |
| `targetDisplayName` | Display name of the kicked participant |
| `targetSessionId` | Connect session ID of the kicked participant |

### `ChatParticipantLeftClientEvent`

**Event Name**: `chat.participant_left`

Sent to room participants when someone leaves the room.

**Properties**:

| Property | Description |
|----------|-------------|
| `displayName` | Display name of the departing participant |
| `participantSessionId` | Connect session ID of the departing participant |
| `remainingCount` | Remaining participant count after this departure |
| `roomId` | Room the participant left |

### `ChatParticipantMutedClientEvent`

**Event Name**: `chat.participant_muted`

Sent to all room participants when someone is muted.

**Properties**:

| Property | Description |
|----------|-------------|
| `durationMinutes` | Mute duration in minutes (null for permanent mute) |
| `roomId` | Room where the participant was muted |
| `targetDisplayName` | Display name of the muted participant |
| `targetSessionId` | Connect session ID of the muted participant |

### `ChatParticipantUnmutedClientEvent`

**Event Name**: `chat.participant_unmuted`

Sent to all room participants when someone is unmuted.

**Properties**:

| Property | Description |
|----------|-------------|
| `roomId` | Room where the participant was unmuted |
| `targetDisplayName` | Display name of the unmuted participant |
| `targetSessionId` | Connect session ID of the unmuted participant |

### `ChatRoomDeletedClientEvent`

**Event Name**: `chat.room_deleted`

Sent to room participants when the room is being deleted.

**Properties**:

| Property | Description |
|----------|-------------|
| `reason` | Optional reason for deletion (e.g., contract actio |
| `roomId` | Room that is being deleted |

### `ChatRoomLockedClientEvent`

**Event Name**: `chat.room_locked`

Sent to room participants when the room is locked.

**Properties**:

| Property | Description |
|----------|-------------|
| `reason` | Reason the room was locked |
| `roomId` | Room that was locked |

### `ChatTypingStartedClientEvent`

**Event Name**: `chat.typing_started`

Sent to room participants when someone starts typing.

**Properties**:

| Property | Description |
|----------|-------------|
| `displayName` | Display name of the typing participant |
| `participantSessionId` | Connect session ID of the typing participant |
| `roomId` | Room where typing is occurring |

### `ChatTypingStoppedClientEvent`

**Event Name**: `chat.typing_stopped`

Sent to room participants when someone stops typing.

**Properties**:

| Property | Description |
|----------|-------------|
| `participantSessionId` | Connect session ID of the participant who stopped  |
| `roomId` | Room where typing stopped |

---

## Common Client Events API

Base schemas for server-to-client push events delivered via WebSocket. All client events inherit from BaseClientEvent and are routed through session-specific RabbitMQ channels (CONNECT_SESSION_{ses...

### `CapabilityManifestEvent`

**Event Name**: `connect.capability_manifest`

Sent to client when their available API capabilities change.

**Properties**:

| Property | Description |
|----------|-------------|
| `availableApis` | List of available API endpoints with their GUIDs |
| `peerGuid` | Unique GUID for peer-to-peer routing on this Conne |
| `reason` | Reason for manifest update (e.g., "initial_connect |
| `sessionId` | Session ID this manifest applies to |
| `version` | Manifest version number (incremented on each chang |

### `DisconnectNotificationEvent`

**Event Name**: `connect.disconnect_notification`

Sent to client before WebSocket connection is closed.

**Properties**:

| Property | Description |
|----------|-------------|
| `expiresAt` | When the reconnection token expires |
| `reason` | Reason for disconnection: |
| `reconnectable` | Whether this session can be reconnected |
| `reconnectionToken` | Token for reconnecting to the same session (valid  |

### `SystemErrorEvent`

**Event Name**: `system.error`

Generic error notification sent to client.

**Properties**:

| Property | Description |
|----------|-------------|
| `details` | Additional error details (service-specific). No Ba |
| `errorCode` | Machine-readable error code (e.g., "SERVICE_UNAVAI |
| `message` | Human-readable error message |
| `recoverable` | Whether the client can retry the operation |

### `SystemNotificationEvent`

**Event Name**: `system.notification`

Generic notification event for system-level messages.

**Properties**:

| Property | Description |
|----------|-------------|
| `actionUrl` | Optional URL for more information |
| `expiresAt` | When this notification expires/becomes irrelevant |
| `message` | Human-readable notification message |
| `notificationType` | Type of notification |
| `title` | Optional notification title |

---

## Game Session Client Events API

Server-to-client push events for the Game Session service. These events notify clients of game session state changes, player actions, chat messages, and game state updates delivered via WebSocket.

### `GameActionResultEvent`

**Event Name**: `game_session.action_result`

Sent to relevant players when a game action produces results.

**Properties**:

| Property | Description |
|----------|-------------|
| `actionId` | ID of the action that produced this result |
| `actionType` | Type of action performed |
| `actorId` | Account ID of player who performed the action |
| `resultData` | Action-specific result data. No Bannou plugin read |
| `sessionId` | ID of the game session |
| `success` | Whether the action succeeded |
| `visibleEffects` | Visual/audio effects other players should see |

### `SessionChatReceivedEvent`

**Event Name**: `game_session.chat_received`

Sent to recipients when a chat message is posted in the session.

**Properties**:

| Property | Description |
|----------|-------------|
| `isWhisperToMe` | True if this is a whisper directed at the receivin |
| `message` | Chat message content |
| `messageId` | Unique identifier for this chat message |
| `messageType` | Type of chat message |
| `senderId` | Account ID of message sender |
| `senderName` | Display name of sender |
| `sessionId` | ID of the game session |

### `PlayerJoinedEvent`

**Event Name**: `game_session.player_joined`

Sent to all session participants when a new player joins.

**Properties**:

| Property | Description |
|----------|-------------|
| `currentPlayerCount` | Total players in session after join |
| `maxPlayers` | Maximum allowed players |
| `player` | Information about the player who joined the sessio |
| `sessionId` | ID of the game session |

### `PlayerKickedEvent`

**Event Name**: `game_session.player_kicked`

Sent to all session participants when a player is kicked.

**Properties**:

| Property | Description |
|----------|-------------|
| `kickedBy` | Account ID of user who performed the kick |
| `kickedPlayerId` | Account ID of player who was kicked |
| `kickedPlayerName` | Display name for UI notification |
| `reason` | Reason provided for the kick |
| `sessionId` | ID of the game session |

### `PlayerLeftEvent`

**Event Name**: `game_session.player_left`

Sent to all session participants when a player leaves voluntarily.

**Properties**:

| Property | Description |
|----------|-------------|
| `currentPlayerCount` | Total players remaining after departure |
| `displayName` | Display name for UI notification |
| `newOwnerId` | New session owner if ownership transferred |
| `playerId` | Account ID of player who left |
| `sessionId` | ID of the game session |

### `SessionStateChangedEvent`

**Event Name**: `game_session.state_changed`

Sent to all session participants when the session state changes.

**Properties**:

| Property | Description |
|----------|-------------|
| `changedBy` | Account ID of user who triggered the change (if ap |
| `newState` | State after the change |
| `previousState` | State before the change |
| `reason` | Reason for state change (e.g., "host_started", "ti |
| `sessionId` | ID of the game session |

### `GameStateUpdatedEvent`

**Event Name**: `game_session.state_updated`

Sent when game state changes that all players should see.

**Properties**:

| Property | Description |
|----------|-------------|
| `sequenceNumber` | Monotonically increasing sequence for ordering sta |
| `sessionId` | ID of the game session |
| `stateDelta` | Partial game state changes. No Bannou plugin reads |
| `triggeredBy` | Account ID that triggered the state change |
| `updateType` | Type of update (game-specific, e.g., "turn_changed |

---

## Matchmaking Client Events API

Server-to-client push events for the Matchmaking service. These events notify clients of matchmaking status changes, match formation, and cancellation delivered via WebSocket.

### `MatchmakingCancelledEvent`

**Event Name**: `matchmaking.cancelled`

Sent when matchmaking is cancelled for any reason.

**Properties**:

| Property | Description |
|----------|-------------|
| `canRequeue` | Whether the player can immediately requeue |
| `queueId` | Queue the ticket was in |
| `reason` | Reason for cancellation |
| `reasonDetail` | Additional detail about the cancellation |
| `ticketId` | Ticket that was cancelled |
| `waitTimeSeconds` | How long the player was waiting before cancellatio |

### `MatchConfirmedEvent`

**Event Name**: `matchmaking.match_confirmed`

Sent to all match participants when all players have accepted.

**Properties**:

| Property | Description |
|----------|-------------|
| `gameSessionId` | ID of the created game session |
| `joinDeadlineSeconds` | Seconds to join before reservation expires |
| `matchId` | Match identifier |
| `reservationToken` | Token to claim reservation when joining session (n |

### `MatchDeclinedEvent`

**Event Name**: `matchmaking.match_declined`

Sent to all match participants when someone declines.

**Properties**:

| Property | Description |
|----------|-------------|
| `autoRequeued` | Whether you have been automatically requeued |
| `declinedByOther` | Whether someone else declined (false if you declin |
| `matchId` | Match that was declined |
| `newTicketId` | New ticket ID if auto-requeued |
| `queueId` | Queue the match was from |

### `MatchFoundEvent`

**Event Name**: `matchmaking.match_found`

Sent to all matched players when a match is formed.

**Properties**:

| Property | Description |
|----------|-------------|
| `acceptDeadline` | Deadline to accept/decline the match |
| `acceptTimeoutSeconds` | Seconds remaining to accept |
| `averageSkillRating` | Average skill rating of matched players (if using  |
| `matchId` | Unique identifier for this match |
| `partyMembersMatched` | Account IDs of party members who are also in this  |
| `playerCount` | Total number of players in the match |
| `queueDisplayName` | Human-readable queue name |
| `queueId` | Queue the match was formed from |
| `teamAssignment` | Team number the player is assigned to (for team ga |

### `MatchPlayerAcceptedEvent`

**Event Name**: `matchmaking.player_accepted`

Sent to all match participants when a player accepts.

**Properties**:

| Property | Description |
|----------|-------------|
| `acceptedCount` | Number of players who have accepted |
| `allAccepted` | Whether all players have accepted |
| `matchId` | Match identifier |
| `totalCount` | Total players who need to accept |

### `QueueJoinedEvent`

**Event Name**: `matchmaking.queue_joined`

Sent to the player when they successfully join a matchmaking queue.

**Properties**:

| Property | Description |
|----------|-------------|
| `estimatedWaitSeconds` | Estimated wait time in seconds (null if unknown) |
| `position` | Approximate position in queue (null if not tracked |
| `queueDisplayName` | Human-readable queue name |
| `queueId` | ID of the queue joined |
| `ticketId` | Unique identifier for the matchmaking ticket |

### `MatchmakingStatusUpdateEvent`

**Event Name**: `matchmaking.status_update`

Periodic status update sent to players in queue.

**Properties**:

| Property | Description |
|----------|-------------|
| `currentSkillRange` | Current skill matching range |
| `estimatedWaitSeconds` | Updated wait estimate |
| `intervalsElapsed` | Number of processing intervals elapsed |
| `maxIntervals` | Maximum intervals before timeout/relaxation |
| `position` | Current queue position (if tracked) |
| `queueId` | Queue identifier |
| `ticketId` | Ticket identifier |

---

## Voice Client Events API

Server-to-client push events for the Voice service. These events notify clients of voice room state changes, peer connections, and tier upgrades delivered via WebSocket.

### `VoiceBroadcastConsentRequestEvent`

**Event Name**: `voice.broadcast_consent_request`

Sent to all room participants when someone requests broadcast consent. Each participant should re...

**Properties**:

| Property | Description |
|----------|-------------|
| `requestedByDisplayName` | Display name of requester for UI |
| `requestedBySessionId` | Who initiated the broadcast request (null if serve |
| `roomId` | Voice room ID |

### `VoiceBroadcastConsentUpdateEvent`

**Event Name**: `voice.broadcast_consent_update`

Sent to all room participants when the broadcast consent state changes (someone consented, someon...

**Properties**:

| Property | Description |
|----------|-------------|
| `consentedCount` | How many participants have consented |
| `declinedByDisplayName` | If declined, who declined (for UI feedback) |
| `roomId` | Voice room ID |
| `state` | Current broadcast consent state |
| `totalCount` | Total participants who need to consent |

### `VoicePeerJoinedEvent`

**Event Name**: `voice.peer_joined`

Sent to existing room participants when a new peer joins.

**Properties**:

| Property | Description |
|----------|-------------|
| `currentParticipantCount` | Total participants after join |
| `peer` | New peer's connection details |
| `roomId` | Voice room ID |

### `VoicePeerLeftEvent`

**Event Name**: `voice.peer_left`

Sent to remaining room participants when a peer leaves.

**Properties**:

| Property | Description |
|----------|-------------|
| `displayName` | Display name for UI notification |
| `peerSessionId` | WebSocket session ID of the peer who left |
| `remainingParticipantCount` | Participants remaining after departure |
| `roomId` | Voice room ID |

### `VoicePeerUpdatedEvent`

**Event Name**: `voice.peer_updated`

Sent when a peer updates their SIP endpoint (e.g., ICE candidate change).

**Properties**:

| Property | Description |
|----------|-------------|
| `peer` | Updated peer connection details |
| `roomId` | Voice room ID |

### `VoiceRoomClosedEvent`

**Event Name**: `voice.room_closed`

Sent to all room participants when the voice room is closed.

**Properties**:

| Property | Description |
|----------|-------------|
| `reason` | Reason the room was closed |
| `roomId` | Voice room ID that was closed |

### `VoiceRoomStateEvent`

**Event Name**: `voice.room_state`

Sent to a client when they join a voice room.

**Properties**:

| Property | Description |
|----------|-------------|
| `codec` | Audio codec to use |
| `peers` | List of peers to connect to (P2P mode) |
| `roomId` | Voice room ID |
| `rtpServerUri` | RTP server URI (scaled mode only) |
| `sessionId` | Associated game session ID |
| `stunServers` | STUN server URIs for NAT traversal |
| `tier` | Current voice tier |

### `VoiceTierUpgradeEvent`

**Event Name**: `voice.tier_upgrade`

Sent to all room participants when the voice tier upgrades.

**Properties**:

| Property | Description |
|----------|-------------|
| `migrationDeadlineMs` | Milliseconds to complete migration before P2P disc |
| `newTier` | New tier after upgrade (always scaled for upgrade  |
| `previousTier` | Previous tier (always p2p for upgrade events) |
| `roomId` | Voice room ID |
| `rtpServerUri` | RTP server URI to connect to |
| `sipCredentials` | Credentials for SIP registration with Kamailio |

---

## Worldstate Client Events

Server-to-client push events for the Worldstate service. Published via IClientEventPublisher to connected WebSocket sessions through the Entity Session Registry pattern.

### `WorldstateTimeSyncEvent`

**Event Name**: `worldstate.time_sync`

Published on period-changed boundaries, ratio changes, admin clock

**Properties**:

| Property | Description |
|----------|-------------|
| `dayOfMonth` | 1-based day within current month |
| `dayOfYear` | 1-based day within current year |
| `hour` | Current game hour (0 to gameHoursPerDay-1) |
| `isDaylight` | Whether current period has sunlight (from calendar |
| `minute` | Current game minute (0-59) |
| `monthCode` | Month code from calendar template (e.g., "greenlea |
| `monthIndex` | 0-based index into calendar months |
| `period` | Current day period code (e.g., "dawn", "morning") |
| `previousPeriod` | Period before transition (null on TriggerSync, Rat |
| `realmId` | Realm this sync applies to |
| `season` | Current season code (e.g., "spring") |
| `seasonIndex` | Season ordinal (0-based) |
| `seasonProgress` | 0.0-1.0 progress through current season, computed  |
| `syncReason` | Why this sync was published (PeriodChanged, RatioC |
| `timeRatio` | Current game-seconds per real-second (used by clie |
| `totalGameSecondsSinceEpoch` | Absolute game-seconds since realm epoch |
| `year` | Current game year (0-based from realm epoch) |

---

## Summary

- **Total event types**: 48
- **Services with events**: 7

---

*This file is auto-generated from event schemas. See [TENETS.md](reference/TENETS.md) for architectural context.*
