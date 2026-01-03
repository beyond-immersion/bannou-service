# Known Gaps and Suspicious Patterns

> **Generated**: 2026-01-03
> **Status**: Needs comprehensive review with user
> **Purpose**: Catalog all "known gap", "this is expected", TODO, and suspicious patterns found in the codebase

This document catalogs patterns found through comprehensive codebase searches. Items are categorized by confidence level:

- **RED**: Likely actual bugs/gaps that need fixing
- **YELLOW**: Needs investigation - unclear if intentional
- **GREEN**: Verified intentional design decisions (can be removed from review)

---

## Category 1: Silent Failures and Data Loss

### 1.1 HTTP Callback Delivery Silently Drops Messages (GREEN - RESOLVED)
**File**: `lib-messaging/MessagingService.cs`
**Status**: ✅ Fixed - Added configurable retry (network failures only), LogError, and error event publishing via TryPublishErrorAsync. Uses `CallbackRetryMaxAttempts` and `CallbackRetryDelayMs` configuration.

### 1.2 Relationship Type Migration Partial Failures (GREEN - RESOLVED)
**File**: `lib-relationship-type/RelationshipTypeService.cs`
**Status**: ✅ Fixed - Response now includes `relationshipsFailed` count and `migrationErrors` array. Individual failures logged as LogError. Error event published when any migrations fail.

### 1.3 Event Publishing Failures Logged But Operation Returns Success (GREEN - INTENTIONAL)
**Files**:
- `lib-realm/RealmService.cs:746-749, 781-784, 815-818`
- `lib-relationship-type/RelationshipTypeService.cs:1149-1150, 1188-1189, 1212-1213`
- `lib-mesh/MeshService.cs:616-617, 647-648`

**Status**: ✅ Intentional "eventually consistent" design. `TryPublishAsync` never throws - it buffers failed messages and retries internally. State (Redis/MySQL) is authoritative; events are notifications. If RabbitMQ is down, messages buffer and retry. If buffer overflows, node crashes for orchestrator restart. Alternative (fail if events fail) would mean RabbitMQ outage breaks ALL CRUD operations.

### 1.4 In-Memory Session Caches May Diverge Across Instances (YELLOW - Needs Implementation)
**File**: `lib-game-session/GameSessionService.cs:66-88`
**Issue**: Three in-memory caches may diverge across instances:
- `_connectedSessions`: WebSocket SessionId -> AccountId
- `_accountSubscriptions`: AccountId -> Set of subscribed stubNames
- `_accountSessions`: AccountId -> Set of WebSocket session IDs

**Required Changes**:
1. **Move `_connectedSessions` to lib-state** - These are clients connected to game service; requests not guaranteed to hit same node
2. **Add periodic cache refresh** - .NET 9+ long-running task pattern to periodically sync subscription cache
3. **Add Connect endpoint** - "Get all connected sessions for account" internal endpoint (if not exists)
4. **Use lib-state sessions for cleanup** - The Redis-stored session set should drive cleanup, not local cache

---

## Category 2: Unimplemented Core Features

### 2.1 GetClientCapabilitiesAsync Returns Dummy Data (GREEN - RESOLVED)
**File**: `lib-connect/ConnectService.cs`
**Status**: ✅ Fixed - Now properly implements capability lookup by session ID. Debugging endpoint that returns actual capabilities and shortcuts for connected WebSocket sessions. Request requires `sessionId` parameter.

### 2.2 RPC Response Forwarding Not Implemented (YELLOW - Feature Gap)
**File**: `lib-connect/ConnectService.cs:1764`
```
// TODO: Implement response timeout and forwarding back to service
```
**Issue**: Service-to-client RPC calls don't get responses back to originating service.

### 2.3 Asset Processors Are Pass-Through Only (YELLOW)
**Files**:
- `lib-asset/Processing/AudioProcessor.cs:134` - "TODO: Implement actual audio processing"
- `lib-asset/Processing/TextureProcessor.cs:121` - "TODO: Implement actual texture processing"
- `lib-asset/Processing/ModelProcessor.cs:151` - "TODO: Implement actual model processing"

**Issue**: All asset processors do file copy instead of actual processing. Is this MVP-acceptable or blocking?

### 2.4 GetBundle Endpoint Not Implemented (YELLOW)
**File**: `lib-asset/AssetService.cs:914`
```
/// TODO: Implement in Phase 5 (Bundle System).
```
**Issue**: GetBundle endpoint returns not implemented. Phase 5 timing unclear.

### 2.5 Markdown Rendering Not Implemented (YELLOW)
**File**: `lib-documentation/DocumentationService.cs:1645`
```
// TODO: Implement proper markdown rendering (e.g., with Markdig)
```
**Issue**: Documentation HTML rendering returns raw markdown.

---

## Category 3: Missing Audit Trail / Metadata

### 3.1 Metadata Fields Silently Ignored (GREEN - RESOLVED)
**Files**: `lib-accounts/AccountsService.cs`
**Status**: ✅ Fixed - Both UpdateAccount and UpdateProfile now properly handle and save metadata fields. Added `MetadataEquals` helper for proper comparison.

### 3.2 Missing Creator Attribution (YELLOW)
**Files**:
- `lib-asset/AssetService.cs:376` - `SessionId = "system"` hardcoded
- `lib-asset/AssetService.cs:886` - `CreatedBy = null`
- `lib-documentation/DocumentationService.cs:1944` - `CreatedBy = Guid.Empty`

**Issue**: Cannot track which user/session performed operations. Is this blocking for audit requirements?

---

## Category 4: Tenet Violations

### 4.1 Direct Environment.GetEnvironmentVariable Calls (T21 Violation) (GREEN - RESOLVED)
**Status**: ✅ Fixed and documented

**Fixed**:
- `lib-mesh/MeshServicePlugin.cs` - MESH_ENDPOINT_HOST/PORT now use `MeshServiceConfiguration`
- `bannou-service/Program.cs` - HEARTBEAT_ENABLED now uses `AppConfiguration.HeartbeatEnabled`

**Documented Exception (see T21 in IMPLEMENTATION.md)**:
- `bannou-service/Services/IBannouService.cs` - SERVICES_ENABLED, X_SERVICE_ENABLED/DISABLED
- `bannou-service/Plugins/PluginLoader.cs` - same variables
- **Reason**: These are checked during plugin loading before service-specific configuration classes are available. The service names are dynamic (e.g., `AUTH_SERVICE_ENABLED`, `ACCOUNTS_SERVICE_ENABLED`) and cannot be predefined in a typed configuration class.

### 4.2 Null-Forgiving Operator Returns (CLAUDE.md Violation) (GREEN - RESOLVED)
**File**: `lib-servicedata/ServicedataService.cs`
**Status**: ✅ Fixed - All 9 instances of `null!` replaced with `null`

---

## Category 5: Inconsistent DI Patterns

### 5.1 Optional Service Resolution for Critical Services (YELLOW)
**File**: `bannou-service/Program.cs:374`
```csharp
MeshInvocationClient = webApp.Services.GetService<IMeshInvocationClient>();
```
**Issue**: Uses `GetService<T>()` (optional) instead of `GetRequiredService<T>()`. No null check. Line 369 uses `GetRequiredService` for IServiceAppMappingResolver - inconsistent.

### 5.2 Dead TryAddSingleton Comment (YELLOW)
**File**: `bannou-service/Program.cs:196-197`
```
// NOTE: mesh client is already registered by AddBannouServices() above with proper serializer config
// Do NOT call Addmesh client() here - it would be ignored due to TryAddSingleton pattern
```
**Issue**: References non-existent `AddBannouServices()` method. Stale documentation.

### 5.3 43 GetService Calls Throughout Codebase (YELLOW)
**Issue**: Mix of optional and required service resolution creates opportunities for silent failures. Needs audit.

---

## Category 6: Test Empty Catch Block

### 6.1 Silent JSON Parse Exception in Test (GREEN - RESOLVED)
**File**: `edge-tester/Tests/ConnectWebSocketTestHandler.cs:1537`
**Status**: ✅ Fixed - Now catches `JsonException` specifically and logs diagnostic info (error message and truncated payload) to help debug test failures.

---

## Category 7: Documented Known Limitations (For Reference)

These were documented with "known" keywords and may be intentional:

### 7.1 Character Event Schema Gap (YELLOW)
**File**: `lib-character.tests/CharacterServiceTests.cs:293`
```
// only populates CharacterId and ChangedFields. This is a known gap.
```
**Issue**: CharacterChanged event schema defines Name/Status properties, but service only populates CharacterId and ChangedFields.

### 7.2 MVP Memory Relevance Uses Keyword Matching (GREEN?)
**File**: `docs/planning/UPCOMING_-_ACTORS_PLUGIN_V3.md:419-426`
```
The current memory store implementation uses **keyword-based relevance matching**, not semantic embeddings. This is an MVP approach with known limitations.
```
**Issue**: Documented MVP limitation. Likely intentional for now.

### 7.3 Dynamic HTTP Subscriptions Lost on Restart (GREEN?)
**File**: `lib-messaging/MessagingService.cs:30-36`
```
/// WARNING: In-memory storage - subscriptions will be lost on service restart.
/// This is a known limitation for dynamic HTTP callback subscriptions.
```
**Issue**: Documented limitation. May be acceptable for current use cases.

### 7.4 Context Variables Not Initialized from Imported Documents (GREEN?)
**File**: `unit-tests/Abml/DocumentLoaderTests.cs:1044`
```
// This is a known limitation - imported documents don't get their context initialized
```
**Issue**: Documented ABML limitation. Needs review if this affects real use cases.

---

## Category 8: Verified Intentional Design (GREEN - No Action Needed)

These patterns were found but are legitimately intentional:

1. **Variable scope propagation** - `DocumentLoaderTests.cs:718` - By design
2. **SetValue modifies parent if exists** - `DocumentLoaderTests.cs:770` - By design
3. **Deploy failure for unavailable backend** - `OrchestratorTestHandler.cs:707` - Expected
4. **Session subsume on reconnect** - `ConnectWebSocketTestHandler.cs:1166-1177` - Expected
5. **Constructor null check validation skipped** - `ServiceConstructorValidator.cs:61` - DI handles this
6. **GOAP planner 1ms timeout** - `GoapPlannerTests.cs:458` - Test-specific
7. **Session shortcut APIs have no x-permissions** - `ConnectWebSocketTestHandler.cs:2118` - By design
8. **Intent channels fixed configuration** - `IntentChannels.cs:14-18` - Intentional simplicity
9. **Video not supported in multi-peer voice** - `IVideoPeerConnection.cs:7` - Bandwidth limitation
10. **Kamailio client uses snake_case** - `KamailioClient.cs:19` - External API requirement
11. **Portainer client uses camelCase** - `PortainerOrchestrator.cs:40` - External API requirement
12. **Invalid shortcut in test** - `SessionShortcutTests.cs:481` - Test validation
13. **Meta messages have empty payloads** - `MessageRouter.cs:27` - Protocol design
14. **Heartbeat disabled in infrastructure tests** - `.env.test.infrastructure:36-40` - Test configuration

---

## Summary for Review

| Category | RED (Fix) | YELLOW (Investigate) | GREEN (OK) |
|----------|-----------|---------------------|------------|
| Silent Failures | 0 | 2 | 2 |
| Unimplemented Features | 0 | 4 | 1 |
| Missing Audit | 0 | 1 | 1 |
| Tenet Violations | 0 | 0 | 2 |
| DI Patterns | 0 | 3 | 0 |
| Test Issues | 0 | 0 | 1 |
| Documented Limitations | 0 | 2 | 2 |
| Verified Intentional | 0 | 0 | 14 |
| **TOTAL** | **0** | **12** | **23** |

---

## Next Steps

1. ~~Review RED items~~ ✅ **ALL RED ITEMS RESOLVED**
2. Discuss YELLOW items - determine if intentional or needs fixing
3. GREEN items are resolved and verified
