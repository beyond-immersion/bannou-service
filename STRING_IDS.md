# String ID Fields Analysis

This document tracks all ID-like fields in Bannou schemas and whether they should have `format: uuid`.

## Summary

| Status | Count | Description |
|--------|-------|-------------|
| Already Correct | ~60+ | Already had `format: uuid` |
| Fixed | 25 | Added `format: uuid` in this session |
| Not UUIDs | 7 | Confirmed to be non-UUID strings |

---

## Fields Fixed with `format: uuid`

These fields are confirmed to hold UUIDs and have been updated with `format: uuid`.

### auth-api.yaml

| Schema | Property | Evidence | Status |
|--------|----------|----------|--------|
| ValidateTokenResponse | sessionId | `Guid.NewGuid().ToString()` in AuthService.cs:1113 | ✅ FIXED |
| SessionInfo | sessionId | Same session ID from auth | ✅ FIXED |

### game-session-api.yaml

| Schema | Property | Evidence | Status |
|--------|----------|----------|--------|
| GameSessionResponse | sessionId | `Guid.NewGuid()` in GameSessionService.cs:203 | ✅ FIXED |
| JoinGameSessionResponse | sessionId | Same session ID from game-session | ✅ FIXED |

### game-session-events.yaml

| Schema | Property | Evidence | Status |
|--------|----------|----------|--------|
| GameSessionPlayerJoinedEvent | eventId | Event IDs are UUIDs | ✅ FIXED |
| GameSessionPlayerJoinedEvent | sessionId | `Guid.NewGuid()` in service | ✅ FIXED |
| GameSessionPlayerJoinedEvent | accountId | Account IDs are UUIDs | ✅ FIXED |
| GameSessionPlayerLeftEvent | eventId | Event IDs are UUIDs | ✅ FIXED |
| GameSessionPlayerLeftEvent | sessionId | Same session ID | ✅ FIXED |
| GameSessionPlayerLeftEvent | accountId | Account IDs are UUIDs | ✅ FIXED |
| x-lifecycle.GameSession.model | sessionId | `Guid.NewGuid()` in service | ✅ FIXED |

### permissions-api.yaml

| Schema | Property | Evidence | Status |
|--------|----------|----------|--------|
| CapabilityRequest | sessionId | Auth service generates as UUID | ✅ FIXED |
| CapabilityResponse | sessionId | Same session ID | ✅ FIXED |
| ValidationRequest | sessionId | Same session ID | ✅ FIXED |
| ValidationResponse | sessionId | Same session ID | ✅ FIXED |
| SessionStateUpdate | sessionId | Same session ID | ✅ FIXED |
| SessionRoleUpdate | sessionId | Same session ID | ✅ FIXED |
| ClearSessionStateRequest | sessionId | Same session ID | ✅ FIXED |
| SessionUpdateResponse | sessionId | Same session ID | ✅ FIXED |
| SessionInfoRequest | sessionId | Same session ID | ✅ FIXED |
| SessionInfo | sessionId | Same session ID | ✅ FIXED |

### common-client-events.yaml

| Schema | Property | Evidence | Status |
|--------|----------|----------|--------|
| CapabilityManifestEvent | sessionId | Auth service generates as UUID | ✅ FIXED |
| SessionCapabilitiesEvent | sessionId | Same session ID | ✅ FIXED |
| ShortcutPublishedEvent | sessionId | Same session ID | ✅ FIXED |
| ShortcutRevokedEvent | sessionId | Same session ID | ✅ FIXED |

---

## Confirmed Non-UUID IDs

These fields end with "Id" but are confirmed to NOT be UUIDs:

| Schema/File | Property | Actual Format | Generation Method |
|-------------|----------|---------------|-------------------|
| behavior-api.yaml | behaviorId | `behavior-{16hex}` | SHA256 hash of bytecode (first 16 chars) |
| behavior-api.yaml | assetId | `{type}-{hash12}` | Delegated to Asset service |
| behavior-api.yaml | bundleId | Any string | Client-provided (e.g., "blacksmith-behaviors-v1") |
| asset-api.yaml | assetId | `{type}-{hash12}` | Content-derived from hash |
| asset-api.yaml | bundleId | Any string | Client-assigned identifier |
| voice-client-events.yaml | peerSessionId | String | Ephemeral WebSocket session identifier |
| orchestrator-api.yaml | processorId | String | Backend processor identifier |

---

## Fields Already Correctly Typed (format: uuid)

These fields already have proper UUID format declarations.

### accounts-api.yaml
- All accountId, methodId fields - CORRECT

### auth-api.yaml
- accountId in AuthResponse, ValidateTokenResponse - CORRECT
- sessionId in TerminateSessionRequest - CORRECT

### game-session-api.yaml (requests only)
- GetGameSessionRequest.sessionId - CORRECT
- JoinGameSessionRequest.sessionId - CORRECT
- LeaveGameSessionRequest.sessionId - CORRECT

### character-api.yaml
- All characterId, realmId, speciesId fields - CORRECT

### realm-api.yaml
- All realmId fields - CORRECT

### location-api.yaml
- All locationId, realmId, parentLocationId fields - CORRECT

### species-api.yaml
- All speciesId fields - CORRECT

### relationship-api.yaml
- All relationshipId, relationshipTypeId fields - CORRECT

### relationship-type-api.yaml
- All relationshipTypeId, parentTypeId, inverseTypeId fields - CORRECT

### asset-api.yaml
- uploadId in InitiateUploadResponse, CompleteUploadRequest, etc. - CORRECT

### common-client-events.yaml
- BaseClientEvent.eventId - CORRECT
- ClientCapabilityEntry.serviceId - CORRECT
- SessionShortcut.routeGuid - CORRECT
- SessionShortcut.targetGuid - CORRECT

### voice-client-events.yaml
- All roomId fields - CORRECT
- VoiceRoomStateEvent.sessionId - CORRECT

### game-session-client-events.yaml
- All sessionId, playerId, accountId, etc. fields - CORRECT

### asset-client-events.yaml
- All uploadId fields - CORRECT

---

## Service Investigation Results

### Auth Service (lib-auth)
**Status:** ✅ COMPLETE
**Findings:**
- `sessionId` generated via `Guid.NewGuid().ToString()` at line 1113
- Returned in `ValidateTokenResponse` and `SessionInfo`
- `TerminateSessionRequest.sessionId` correctly has `format: uuid`
- `SessionInfo.sessionId` is MISSING `format: uuid`

### Game Session Service (lib-game-session)
**Status:** ✅ COMPLETE
**Findings:**
- `sessionId` generated via `Guid.NewGuid()` at line 203
- Request DTOs correctly have `format: uuid`
- Response DTOs (`GameSessionResponse`, `JoinGameSessionResponse`) MISSING `format: uuid`

### Behavior Service (lib-behavior)
**Status:** ✅ COMPLETE
**Findings:**
- `behaviorId`: NOT UUID - SHA256 hash, format `behavior-{16hex}`
- `assetId`: NOT UUID - delegated to Asset service
- `bundleId`: NOT UUID - client-provided semantic string

### Permissions Service (lib-permissions)
**Status:** ✅ COMPLETE
**Findings:**
- `sessionId` comes from Auth service (which generates UUIDs)
- ALL `sessionId` fields in schema are MISSING `format: uuid`
- common-events.yaml correctly defines sessionId with `format: uuid`

### Connect Service (lib-connect)
**Status:** ✅ COMPLETE
**Findings:**
- `sessionId` received from Auth service validation
- Auth generates via `Guid.NewGuid().ToString()`
- `ValidateTokenResponse.sessionId` needs `format: uuid`

### Asset Service (lib-asset)
**Status:** ✅ COMPLETE
**Findings:**
- `uploadId`: IS UUID - `Guid.NewGuid()` - already has `format: uuid`
- `assetId`: NOT UUID - content-derived `{type}-{hash12}`
- `bundleId`: NOT UUID - client-assigned string

---

## Action Items

1. [x] ~~Investigate auth service sessionId usage~~
2. [x] ~~Investigate game-session service sessionId usage~~
3. [x] ~~Investigate behavior service ID patterns~~
4. [x] ~~Investigate permissions service sessionId usage~~
5. [x] ~~Investigate connect service sessionId usage~~
6. [x] ~~Investigate asset service ID patterns~~
7. [x] ~~Update auth-api.yaml: Add `format: uuid` to SessionInfo.sessionId, ValidateTokenResponse.sessionId~~
8. [x] ~~Update game-session-api.yaml: Add `format: uuid` to response sessionId fields~~
9. [x] ~~Update game-session-events.yaml: Add `format: uuid` to event sessionId/accountId/eventId fields~~
10. [x] ~~Update permissions-api.yaml: Add `format: uuid` to all sessionId fields (10 fields)~~
11. [x] ~~Update common-client-events.yaml: Add `format: uuid` to sessionId fields (4 fields)~~

**All schema fixes completed!**
