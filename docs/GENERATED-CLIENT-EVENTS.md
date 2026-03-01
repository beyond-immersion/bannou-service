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
| `AuthDeviceLoginClientEvent` | `auth.device_login` | Sent to all existing sessions when a new login occurs on the... |
| `AuthExternalAccountLinkedClientEvent` | `auth.external_account_linked` | Sent to all sessions when a new OAuth provider is linked to ... |
| `AuthMfaDisabledClientEvent` | `auth.mfa_disabled` | Sent to all sessions when MFA is disabled on the account. |
| `AuthMfaEnabledClientEvent` | `auth.mfa_enabled` | Sent to all sessions when MFA is enabled on the account. |
| `AuthPasswordChangedClientEvent` | `auth.password_changed` | Sent to all sessions when the account's password is reset. |
| `AuthSessionTerminatedClientEvent` | `auth.session_terminated` | Sent to remaining sessions when a session is remotely termin... |
| `AuthSuspiciousLoginClientEvent` | `auth.suspicious_login` | Sent to all sessions when a failed login attempt occurs with... |
| `ChatMessageDeletedClientEvent` | `chat.message_deleted` | Sent to room participants when a message is deleted. |
| `ChatMessagePinnedEvent` | `chat.message_pinned` | Sent to room participants when a message is pinned or unpinn... |
| `ChatMessageReceivedEvent` | `chat.message_received` | Sent to room participants when a new message is received. |
| `ChatParticipantBannedClientEvent` | `chat.participant_banned` | Sent to all room participants when someone is banned. |
| `ChatParticipantJoinedClientEvent` | `chat.participant_joined` | Sent to room participants when someone joins the room. |
| `ChatParticipantKickedClientEvent` | `chat.participant_kicked` | Sent to all room participants when someone is kicked. |
| `ChatParticipantLeftClientEvent` | `chat.participant_left` | Sent to room participants when someone leaves the room. |
| `ChatParticipantMutedClientEvent` | `chat.participant_muted` | Sent to all room participants when someone is muted. |
| `ChatParticipantRoleChangedClientEvent` | `chat.participant_role_changed` | Sent to room participants when a participant's role changes. |
| `ChatParticipantUnbannedClientEvent` | `chat.participant_unbanned` | Sent to room participants when a ban is lifted. |
| `ChatParticipantUnmutedClientEvent` | `chat.participant_unmuted` | Sent to all room participants when someone is unmuted. |
| `ChatRoomDeletedClientEvent` | `chat.room_deleted` | Sent to room participants when the room is being deleted. |
| `ChatRoomLockedClientEvent` | `chat.room_locked` | Sent to room participants when the room is locked. |
| `ChatRoomUpdatedClientEvent` | `chat.room_updated` | Sent to room participants when room settings change. |
| `ChatTypingStartedClientEvent` | `chat.typing_started` | Sent to room participants when someone starts typing. |
| `ChatTypingStoppedClientEvent` | `chat.typing_stopped` | Sent to room participants when someone stops typing. |
| `CollectionDiscoveryAdvancedClientEvent` | `collection.discovery_advanced` | Sent to the collection owner when progressive discovery adva... |
| `CollectionEntryUnlockedClientEvent` | `collection.entry_unlocked` | Sent to the collection owner when an entry is unlocked in th... |
| `CollectionMilestoneReachedClientEvent` | `collection.milestone_reached` | Sent to the collection owner when a completion milestone is ... |
| `CapabilityManifestEvent` | `connect.capability_manifest` | Sent to client when their available API capabilities change. |
| `DisconnectNotificationEvent` | `connect.disconnect_notification` | Sent to client before WebSocket connection is closed. |
| `CurrencyBalanceChangedEvent` | `currency.balance_changed` | Sent to the wallet owner when any balance mutation occurs: c... |
| `CurrencyWalletFrozenEvent` | `currency.wallet_frozen` | Sent to the wallet owner when their wallet is frozen (escrow... |
| `CurrencyWalletUnfrozenEvent` | `currency.wallet_unfrozen` | Sent to the wallet owner when their wallet is unfrozen and u... |
| `GameActionResultEvent` | `game_session.action_result` | Sent to relevant players when a game action produces results... |
| `SessionChatReceivedEvent` | `game_session.chat_received` | Sent to recipients when a chat message is posted in the sess... |
| `PlayerJoinedEvent` | `game_session.player_joined` | Sent to all session participants when a new player joins. |
| `PlayerKickedEvent` | `game_session.player_kicked` | Sent to all session participants when a player is kicked. |
| `PlayerLeftEvent` | `game_session.player_left` | Sent to all session participants when a player leaves volunt... |
| `SessionCancelledEvent` | `game_session.session_cancelled` | Sent to players who claimed their reservation when a matchma... |
| `SessionStateChangedEvent` | `game_session.state_changed` | Sent to all session participants when the session state chan... |
| `GameStateUpdatedEvent` | `game_session.state_updated` | Sent when game state changes that all players should see. |
| `InventoryContainerFullClientEvent` | `inventory.container_full` | Sent to sessions observing a container owner when one of the... |
| `InventoryItemChangedClientEvent` | `inventory.item_changed` | Sent to sessions observing a container owner when an item is... |
| `InventoryItemTransferredClientEvent` | `inventory.item_transferred` | Sent to sessions observing either the source or target conta... |
| `MatchmakingCancelledEvent` | `matchmaking.cancelled` | Sent when matchmaking is cancelled for any reason. |
| `MatchConfirmedEvent` | `matchmaking.match_confirmed` | Sent to all match participants when all players have accepte... |
| `MatchDeclinedEvent` | `matchmaking.match_declined` | Sent to all match participants when someone declines. |
| `MatchFoundEvent` | `matchmaking.match_found` | Sent to all matched players when a match is formed. |
| `MatchPlayerAcceptedEvent` | `matchmaking.player_accepted` | Sent to all match participants when a player accepts. |
| `QueueJoinedEvent` | `matchmaking.queue_joined` | Sent to the player when they successfully join a matchmaking... |
| `MatchmakingStatusUpdateEvent` | `matchmaking.status_update` | Periodic status update sent to players in queue. |
| `StatusEffectChangedClientEvent` | `status.effect_changed` | Sent to sessions observing an entity when a status effect is... |
| `SystemErrorEvent` | `system.error` | Generic error notification sent to client. |
| `SystemNotificationEvent` | `system.notification` | Generic notification event for system-level messages. |
| `TransitConnectionStatusChangedEvent` | `transit.connection_status_changed` | A connection's operational status changed -- pushed to sessi... |
| `TransitDiscoveryRevealedEvent` | `transit.discovery_revealed` | A discoverable connection was revealed -- pushed to the disc... |
| `TransitJourneyUpdatedEvent` | `transit.journey_updated` | Journey state changed -- pushed to the traveling entity's bo... |
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

## Auth Client Events API

Server-to-client push events for the Auth service. These events provide real-time multi-device security notifications delivered via WebSocket to all of an account's connected sessions.

### `AuthDeviceLoginClientEvent`

**Event Name**: `auth.device_login`

Sent to all existing sessions when a new login occurs on the account.

**Properties**:

| Property | Description |
|----------|-------------|
| `ipAddress` | IP address of the new login request |
| `loginSessionId` | Session ID of the new login that triggered this ev |
| `userAgent` | User agent string from the new login request |

### `AuthExternalAccountLinkedClientEvent`

**Event Name**: `auth.external_account_linked`

Sent to all sessions when a new OAuth provider is linked to the account.

**Properties**:

| Property | Description |
|----------|-------------|
| `provider` | OAuth provider that was linked |

### `AuthMfaDisabledClientEvent`

**Event Name**: `auth.mfa_disabled`

Sent to all sessions when MFA is disabled on the account.

**Properties**:

| Property | Description |
|----------|-------------|
| `disabledBy` | Who triggered the MFA disable (Self or Admin) |

### `AuthMfaEnabledClientEvent`

**Event Name**: `auth.mfa_enabled`

Sent to all sessions when MFA is enabled on the account.

### `AuthPasswordChangedClientEvent`

**Event Name**: `auth.password_changed`

Sent to all sessions when the account's password is reset.

### `AuthSessionTerminatedClientEvent`

**Event Name**: `auth.session_terminated`

Sent to remaining sessions when a session is remotely terminated.

**Properties**:

| Property | Description |
|----------|-------------|
| `reason` | Reason why the session was terminated |
| `terminatedSessionId` | ID of the specific session that was terminated (nu |

### `AuthSuspiciousLoginClientEvent`

**Event Name**: `auth.suspicious_login`

Sent to all sessions when a failed login attempt occurs with a known account.

**Properties**:

| Property | Description |
|----------|-------------|
| `attemptCount` | Number of failed attempts in the current rate-limi |
| `ipAddress` | IP address of the failed login attempt |
| `userAgent` | User agent string from the failed login request |

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

### `ChatParticipantRoleChangedClientEvent`

**Event Name**: `chat.participant_role_changed`

Sent to room participants when a participant's role changes.

**Properties**:

| Property | Description |
|----------|-------------|
| `changedByDisplayName` | Display name of who changed the role (null for aut |
| `changedBySessionId` | Session ID of who changed the role (null for autom |
| `displayName` | Display name of the participant whose role changed |
| `newRole` | New role |
| `oldRole` | Previous role |
| `participantSessionId` | Connect session ID of the participant whose role c |
| `roomId` | Room where the role change occurred |

### `ChatParticipantUnbannedClientEvent`

**Event Name**: `chat.participant_unbanned`

Sent to room participants when a ban is lifted.

**Properties**:

| Property | Description |
|----------|-------------|
| `roomId` | Room the participant was unbanned from |
| `targetSessionId` | Connect session ID of the unbanned participant |
| `unbannedByDisplayName` | Display name of the moderator who removed the ban |

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

### `ChatRoomUpdatedClientEvent`

**Event Name**: `chat.room_updated`

Sent to room participants when room settings change.

**Properties**:

| Property | Description |
|----------|-------------|
| `changedFields` | List of field names that changed in this update |
| `createdAt` | When the room was created |
| `displayName` | Current room display name |
| `isArchived` | Whether the room has been archived |
| `maxParticipants` | Maximum allowed participants |
| `participantCount` | Current number of participants |
| `roomId` | Room that was updated |
| `roomTypeCode` | Room type code |
| `status` | Current room lifecycle status |

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

## Collection Client Events API

Server-to-client push events for the Collection service. These events notify clients of collection entry unlocks, milestone completions, and discovery advancements delivered via WebSocket.

### `CollectionDiscoveryAdvancedClientEvent`

**Event Name**: `collection.discovery_advanced`

Sent to the collection owner when progressive discovery advances for an entry.

**Properties**:

| Property | Description |
|----------|-------------|
| `collectionId` | Collection containing the entry |
| `entryCode` | Code of the entry whose discovery advanced |
| `newDiscoveryLevel` | New discovery level after advancement |
| `revealedKeys` | Information keys revealed at the new discovery lev |

### `CollectionEntryUnlockedClientEvent`

**Event Name**: `collection.entry_unlocked`

Sent to the collection owner when an entry is unlocked in their collection.

**Properties**:

| Property | Description |
|----------|-------------|
| `category` | Category of the unlocked entry |
| `collectionId` | Collection where the entry was unlocked |
| `collectionType` | Type of collection (e.g., "voice_gallery", "bestia |
| `displayName` | Human-readable display name of the entry |
| `entryCode` | Code of the unlocked entry template |
| `isFirstGlobal` | Whether this is the first time any player has unlo |

### `CollectionMilestoneReachedClientEvent`

**Event Name**: `collection.milestone_reached`

Sent to the collection owner when a completion milestone is crossed.

**Properties**:

| Property | Description |
|----------|-------------|
| `collectionId` | Collection that reached the milestone |
| `collectionType` | Type of collection |
| `completionPercentage` | Precise completion percentage at time of milestone |
| `milestone` | Milestone label (e.g., "25%", "50%", "75%", "100%" |

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

## Currency Client Events API

Server-to-client push events for the Currency service. These events notify clients of real-time wallet balance changes and wallet lifecycle state transitions delivered via WebSocket.

### `CurrencyBalanceChangedEvent`

**Event Name**: `currency.balance_changed`

Sent to the wallet owner when any balance mutation occurs: credits, debits,

**Properties**:

| Property | Description |
|----------|-------------|
| `amount` | Signed balance delta (positive for gains, negative |
| `currencyCode` | Human-readable currency code (e.g., "gold", "gems" |
| `currencyDefinitionId` | Currency definition that changed |
| `newBalance` | Wallet balance after the change |
| `transactionType` | Semantic classification of the balance change sour |
| `walletId` | The wallet whose balance changed |

### `CurrencyWalletFrozenEvent`

**Event Name**: `currency.wallet_frozen`

Sent to the wallet owner when their wallet is frozen (escrow dispute,

**Properties**:

| Property | Description |
|----------|-------------|
| `reason` | Human-readable reason for the freeze |
| `walletId` | The wallet that was frozen |

### `CurrencyWalletUnfrozenEvent`

**Event Name**: `currency.wallet_unfrozen`

Sent to the wallet owner when their wallet is unfrozen and usable again.

**Properties**:

| Property | Description |
|----------|-------------|
| `walletId` | The wallet that was unfrozen |

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

### `SessionCancelledEvent`

**Event Name**: `game_session.session_cancelled`

Sent to players who claimed their reservation when a matchmade session

**Properties**:

| Property | Description |
|----------|-------------|
| `reason` | Reason for cancellation |
| `sessionId` | ID of the cancelled game session |

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

## Inventory Client Events API

Server-to-client push events for the Inventory service. These events notify clients of real-time container content changes delivered via WebSocket through the Entity Session Registry.

### `InventoryContainerFullClientEvent`

**Event Name**: `inventory.container_full`

Sent to sessions observing a container owner when one of their containers

**Properties**:

| Property | Description |
|----------|-------------|
| `constraintType` | Which capacity constraint was reached (slots, weig |
| `containerId` | Container that reached capacity |
| `containerType` | Game-defined container type for client-side contex |

### `InventoryItemChangedClientEvent`

**Event Name**: `inventory.item_changed`

Sent to sessions observing a container owner when an item is placed,

**Properties**:

| Property | Description |
|----------|-------------|
| `changeType` | Type of change that occurred |
| `containerId` | Container where the change occurred |
| `containerType` | Game-defined container type for client-side filter |
| `instanceId` | Item instance that was affected |
| `quantity` | Current quantity after the change (for stacking an |
| `slotIndex` | Slot position after the change (for slot-based con |
| `slotX` | Grid X position after the change (for grid contain |
| `slotY` | Grid Y position after the change (for grid contain |
| `templateId` | Item template ID for icon and name lookup |

### `InventoryItemTransferredClientEvent`

**Event Name**: `inventory.item_transferred`

Sent to sessions observing either the source or target container owner

**Properties**:

| Property | Description |
|----------|-------------|
| `instanceId` | Item instance that was transferred |
| `quantityTransferred` | Quantity of items transferred |
| `sourceContainerId` | Container the item was transferred from |
| `sourceContainerType` | Source container type |
| `targetContainerId` | Container the item was transferred to |
| `targetContainerType` | Target container type |
| `templateId` | Item template ID for icon and name lookup |

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

## Status Client Events API

Server-to-client push events for the Status service. These events notify clients of real-time status effect changes delivered via WebSocket through the Entity Session Registry.

### `StatusEffectChangedClientEvent`

**Event Name**: `status.effect_changed`

Sent to sessions observing an entity when a status effect is granted,

**Properties**:

| Property | Description |
|----------|-------------|
| `category` | Status category (buff, debuff, death, etc.) for cl |
| `changeType` | Type of status change that occurred |
| `entityId` | Entity that the status effect applies to |
| `entityType` | Type of entity (character, account, etc.) |
| `expiresAt` | When the status will expire (for granted/stacked c |
| `sourceId` | Source entity that granted the status (for granted |
| `stackCount` | Current stack count after the change (for stacking |
| `statusInstanceId` | Specific status instance that was affected |
| `statusTemplateCode` | Template code identifying the status effect type |

---

## Transit Client Events

Server-to-client WebSocket push events for the Transit service

### `TransitConnectionStatusChangedEvent`

**Event Name**: `transit.connection_status_changed`

A connection's operational status changed -- pushed to sessions in the affected realm

**Properties**:

| Property | Description |
|----------|-------------|
| `connectionId` | The connection whose status changed |
| `fromLocationId` | Origin location of the connection |
| `newStatus` | Status after the change |
| `previousStatus` | Status before the change |
| `reason` | Reason for the status change |
| `toLocationId` | Destination location of the connection |

### `TransitDiscoveryRevealedEvent`

**Event Name**: `transit.discovery_revealed`

A discoverable connection was revealed -- pushed to the discovering entity's session

**Properties**:

| Property | Description |
|----------|-------------|
| `connectionId` | The connection that was discovered |
| `connectionName` | Human-readable connection name for UI display |
| `fromLocationId` | Origin location of the discovered connection |
| `source` | How the connection was discovered (e.g., travel, g |
| `toLocationId` | Destination location of the discovered connection |

### `TransitJourneyUpdatedEvent`

**Event Name**: `transit.journey_updated`

Journey state changed -- pushed to the traveling entity's bound session

**Properties**:

| Property | Description |
|----------|-------------|
| `currentLegIndex` | Index of the current leg (0-based) |
| `currentLocationId` | Where the entity currently is |
| `destinationLocationId` | Final destination of the journey |
| `entityId` | The traveling entity |
| `estimatedArrivalGameTime` | Estimated arrival in game-time units |
| `journeyId` | The journey that changed state |
| `primaryModeCode` | Primary transit mode code for the journey |
| `remainingLegs` | Number of legs remaining in the journey |
| `status` | Current journey status |

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

- **Total event types**: 72
- **Services with events**: 13

---

*This file is auto-generated from event schemas. See [TENETS.md](reference/TENETS.md) for architectural context.*
