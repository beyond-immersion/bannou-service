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

### 1.4 In-Memory Session Caches May Diverge Across Instances (GREEN - RESOLVED)
**File**: `lib-game-session/GameSessionService.cs`
**Status**: ✅ Fixed - Refactored to T9-compliant architecture:

**Removed** (were problematic per-instance state):
- `_connectedSessions` (WebSocket SessionId → AccountId)
- `_accountSessions` (AccountId → Set of WebSocket session IDs)

**Retained** (`_accountSubscriptions` - now T9-compliant):
- Explicitly documented as "local filter cache" for fast event filtering
- Loaded from Subscriptions service at startup via `GameSessionStartupService` (BackgroundService)
- Kept current via `subscription.updated` events
- Authoritative subscriber session state moved to lib-state (`SUBSCRIBER_SESSIONS_PREFIX`)

**Additional fixes**:
- `_serverSalt` now comes from configuration with fail-fast validation (IMPLEMENTATION TENETS)
- Pattern follows T9 "Event-Backed Local Caches" acceptable exception

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

### 2.5 Markdown Rendering Not Implemented (GREEN - RESOLVED)
**File**: `lib-documentation/DocumentationService.cs`
**Status**: ✅ Fixed - Proper markdown rendering implemented with Markdig library:
- `/documentation/view/{slug}` returns fully rendered HTML page (`text/html`)
- `/documentation/raw/{slug}` returns raw markdown content (`text/markdown`)
- Both endpoints use `x-manual-implementation` in schema for manual controller implementation
- Controller uses `[Produces]` attributes to declare content types
- Added Markdig NuGet package with `UseAdvancedExtensions()` pipeline

---

## Category 3: Missing Audit Trail / Metadata

### 3.1 Metadata Fields Silently Ignored (GREEN - RESOLVED)
**Files**: `lib-accounts/AccountsService.cs`
**Status**: ✅ Fixed - Both UpdateAccount and UpdateProfile now properly handle and save metadata fields. Added `MetadataEquals` helper for proper comparison.

### 3.2 Missing Creator Attribution (GREEN - Intentional MVP)
**Files**:
- `lib-asset/AssetService.cs:376` - `SessionId = "system"` hardcoded
- `lib-asset/AssetService.cs:886` - `CreatedBy = null`
- `lib-documentation/DocumentationService.cs:1944` - `CreatedBy = Guid.Empty`

**Status**: ✅ Intentional MVP limitation - waiting for auth integration

**Analysis**: These services don't have `sessionId` in their request schemas. The pattern for attribution requires:
1. Client connects via WebSocket and gets authenticated (SessionId/AccountId)
2. Request schemas include `sessionId` field for attribution
3. Services use the sessionId for audit trail

Asset and Documentation APIs were designed without `sessionId` in requests. The TODO comments explicitly state "Get from context when auth is integrated". This requires schema changes and is deferred to post-MVP auth integration work.

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

### 4.3 Non-Async Task-Returning Methods (T23 Violation) (GREEN - RESOLVED)
**Status**: ✅ Fixed - 354 violations across codebase

**Fixed locations:**
- `lib-state/StateStoreFactory.cs` - 1 method (pass-through Task return)
- `bannou-service/BannouJson.cs` - 1 method (pass-through Task return)
- `http-tester/Program.cs` - 4 methods (MockTestClient)
- `http-tester/Tests/*.cs` - 347 methods (expression-bodied test methods)
- `lib-voice/Services/P2PCoordinator.cs` - Fixed async pattern
- `lib-voice/Services/ScaledTierCoordinator.cs` - Fixed async pattern
- `lib-orchestrator/Backends/*.cs` - Fixed async pattern in backend detectors

**Pattern**: All Task-returning methods now have `async` keyword and contain at least one `await`.

---

## Category 5: Inconsistent DI Patterns

### 5.1 Optional Service Resolution for Critical Services (GREEN - RESOLVED)
**File**: `bannou-service/Program.cs`
**Status**: ✅ Fixed - Changed to `GetRequiredService` for `IMeshInvocationClient` and `IServiceAppMappingResolver`. Core infrastructure should fail fast.

### 5.2 Dead TryAddSingleton Comment (GREEN - RESOLVED)
**File**: `bannou-service/Program.cs`
**Status**: ✅ Fixed - Removed stale comment referencing non-existent `AddBannouServices()` method.

### 5.3 Optional Constructor Parameters (DI Anti-Pattern) (GREEN - RESOLVED)
**File**: `lib-voice/Clients/RtpEngineClient.cs`
**Status**: ✅ Fixed - Removed default value from `timeoutSeconds` parameter

**Issue**: Constructor had `int timeoutSeconds = 5` which is problematic for DI - optional parameters can accidentally be set to null/default by the DI container.

**Fix**: Made parameter required, updated all callers (tests and VoiceServicePlugin) to pass explicit value.

### 5.4 43 GetService Calls Throughout Codebase (GREEN - RESOLVED)
**Status**: ✅ Fixed - Comprehensive audit completed:

**Fixed (14 calls changed to GetRequiredService + fail-fast):**
- `Program.cs`: 2 calls (MeshInvocationClient, ServiceAppMappingResolver)
- `RepositorySyncSchedulerService.cs`: 3 calls (StateStoreFactory, DocumentationService, MessageBus)
- `SubscriptionExpirationService.cs`: 3 calls (StateStoreFactory, MessageBus x2)
- `MeshServicePlugin.cs`: 3 calls + changed degraded mode to fail-fast
- `OrchestratorServicePlugin.cs`: 1 call + changed degraded mode to fail-fast
- `ActorServicePlugin.cs`: 2 calls
- `StandardServicePlugin.cs`: 1 call (base class for all plugins)

**Acceptable (remaining ~29 calls):**
- Tests (22 calls): Intentionally testing optional resolution
- Plugin ConfigureServices bootstrap (6 calls): T21 exception - before SP built
- `BaseBannouPlugin.GetLoggerFactory`: Logging is optional early in startup

---

## Category 6: Test Empty Catch Block

### 6.1 Silent JSON Parse Exception in Test (GREEN - RESOLVED)
**File**: `edge-tester/Tests/ConnectWebSocketTestHandler.cs:1537`
**Status**: ✅ Fixed - Now catches `JsonException` specifically and logs diagnostic info (error message and truncated payload) to help debug test failures.

---

## Category 7: Documented Known Limitations (For Reference)

These were documented with "known" keywords and may be intentional:

### 7.1 Character Event Schema Gap (GREEN - RESOLVED)
**File**: `lib-character/CharacterService.cs`
**Status**: ✅ Fixed - Both `CharacterUpdatedEvent` and `CharacterDeletedEvent` now populate all required schema fields:
- CharacterId, Name, RealmId, SpeciesId, BirthDate, Status, CreatedAt, UpdatedAt, ChangedFields
- Test updated to verify full event population

### 7.2 MVP Memory Relevance Uses Keyword Matching (GREEN?)
**File**: `docs/planning/UPCOMING_-_ACTORS_PLUGIN_V3.md:419-426`
```
The current memory store implementation uses **keyword-based relevance matching**, not semantic embeddings. This is an MVP approach with known limitations.
```
**Issue**: Documented MVP limitation. Likely intentional for now.

### 7.3 ~~Dynamic HTTP Subscriptions Lost on Restart~~ (RESOLVED)
**File**: `lib-messaging/MessagingService.cs`
**Resolution**: External HTTP callback subscriptions are now persisted to lib-state keyed by app-id.
- `ExternalSubscriptionData` model stores subscription metadata
- Subscriptions are recovered automatically on restart via `MessagingSubscriptionRecoveryService`
- TTL-based cleanup (24 hours) ensures orphaned subscriptions don't accumulate
- Internal (IMessageBus via DI) subscriptions remain ephemeral by design - plugins re-subscribe on startup

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
| Silent Failures | 0 | 0 | 4 |
| Unimplemented Features | 0 | 3 | 2 |
| Missing Audit | 0 | 0 | 2 |
| Tenet Violations | 0 | 0 | 3 |
| DI Patterns | 0 | 0 | 4 |
| Test Issues | 0 | 0 | 1 |
| Documented Limitations | 0 | 1 | 3 |
| Verified Intentional | 0 | 0 | 14 |
| String.Empty Usage | 0 | 326 | 0 |
| Null-Forgiving Exceptions | 0 | 0 | 4 |
| **TOTAL** | **0** | **330** | **37** |

---

## Category 9: String.Empty Usage Patterns (YELLOW - Needs Investigation)

**Total Instances**: 326 across functional code (excluding tests and generated files)

### 9.1 High-Density Files (10+ occurrences)
| File | Count | Notes |
|------|-------|-------|
| `lib-documentation/DocumentationService.cs` | 30 | |
| `lib-auth/AuthService.cs` | 21 | |
| `lib-auth/Services/OAuthProviderService.cs` | 16 | |
| `lib-asset/AssetService.cs` | 14 | |
| `lib-behavior/BehaviorService.cs` | 12 | |
| `lib-testing/TestingController.cs` | 11 | |
| `lib-voice/Clients/KamailioClient.cs` | 10 | |
| `lib-species/SpeciesService.cs` | 10 | |
| `lib-realm/RealmService.cs` | 10 | |
| `lib-location/LocationService.cs` | 10 | |
| `lib-behavior/Cognition/IMemoryStore.cs` | 10 | |
| `lib-orchestrator/OrchestratorService.cs` | 9 | |

### 9.2 Common Patterns Found
1. **Null-coalescing fallbacks**: `value ?? string.Empty` - hiding potential null issues
2. **Default property values**: `public string Prop { get; set; } = string.Empty;`
3. **Error message placeholders**: `ErrorMessage = string.Empty` when no error
4. **Comparison patterns**: `if (value == string.Empty)` instead of `string.IsNullOrEmpty()`

**Investigation Needed**: Determine which patterns are:
- Intentional defaults (OK)
- Hiding null issues (FIX)
- Inappropriate replacements for null (RETHINK)

---

## Category 10: Null-Forgiving Operator Exceptions (GREEN - Tenet Exceptions)

These patterns use null-forgiving (`!`) but are safe due to compiler limitations tracking nullability across method/property boundaries:

### 10.1 LINQ Chain Filtering Pattern (SAFE)
```csharp
// Example: OrchestratorService.cs:2269
.Where(s => !string.IsNullOrEmpty(s.AppId))
.Select(s => s.AppId!)  // Compiler can't track that Where filtered nulls
```
**Why Safe**: The `.Where()` guarantees non-null; compiler just can't see through LINQ chains.

### 10.2 Property Accessor After EnsureX() Pattern (SAFE)
```csharp
// Example: BannouBundleReader.cs Manifest/Index properties
public BundleManifest Manifest {
    get { EnsureHeaderRead(); return _manifest!; }
}
```
**Why Safe**: Method guarantees initialization; compiler can't track field assignment in called methods.

### 10.3 Post-Null-Check Value Access (SAFE)
```csharp
// Example: RedisStateStore.cs:59
if (value.IsNullOrEmpty) { return null; }
return BannouJson.Deserialize<TValue>(value!);
```
**Why Safe**: Explicit null check; compiler can't track struct nullability after check.

### 10.4 EF Core Entity Initialization (ACCEPTED PATTERN)
```csharp
// Example: StateEntry.cs:21
[Required]
public string StoreName { get; set; } = default!;
```
**Why Accepted**: Standard EF Core pattern - framework initializes properties, not constructor.

---

## Next Steps

1. ~~Review RED items~~ ✅ **ALL RED ITEMS RESOLVED**
2. Discuss YELLOW items - determine if intentional or needs fixing
3. GREEN items are resolved and verified
4. **NEW**: Investigate string.Empty patterns for hidden null issues
5. **NEW**: Consider tenet exception for safe null-forgiving LINQ patterns
