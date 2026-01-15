# Service Enhancement Opportunities

> **Created**: 2026-01-15
> **Purpose**: Track refactoring opportunities identified across plugin service implementations
> **Status**: Planning document - work items to be prioritized

This document catalogs services that are too large, have duplicated code, or would benefit from extracting logic into DI-injectable helper classes for improved testability and maintainability.

---

## Executive Summary

| Priority | Service | Lines | Issue Type | Estimated Reduction |
|----------|---------|-------|------------|---------------------|
| 🔴 P1 | SaveLoadService | 3960 | Too Large | ~1500 lines (38%) |
| 🔴 P1 | AuthService | 2891 | Duplicated Code | ~400 lines (14%) |
| 🔴 P1 | OrchestratorService | 3247 | Too Large | ~600 lines (18%) |
| 🟡 P2 | ConnectService | 3085 | Needs Extraction | ~450 lines (15%) |
| 🟡 P2 | SceneService | 2300 | Needs Extraction | ~900 lines (39%) |
| 🟡 P2 | MappingService | 3051 | Needs Extraction | ~450 lines (15%) |
| 🟡 P2 | MatchmakingService | 2135 | Needs Extraction | ~500 lines (23%) |
| 🟡 P2 | GameSessionService | 2183 | Needs Extraction | ~400 lines (18%) |
| 🟢 OK | AssetService | 3376 | Well-Structured | N/A |
| 🟢 OK | DocumentationService | 3485 | Well-Structured | N/A |
| 🟢 OK | AnalyticsService | 1864 | Well-Structured | N/A |

---

## Reference Patterns (Already Well-Extracted)

These services demonstrate the correct extraction pattern and should be used as references:

### lib-auth Helper Services
```
plugins/lib-auth/Services/
├── TokenService.cs (331 lines) - JWT generation/validation
├── SessionService.cs (438 lines) - Session lifecycle management
└── OAuthProviderService.cs (768 lines) - OAuth provider integrations
```

### lib-documentation Helper Services
```
plugins/lib-documentation/Services/
├── SearchIndexService.cs - Full-text search
├── GitSyncService.cs - Repository synchronization
├── ContentTransformService.cs - Markdown transforms
└── RepositorySyncSchedulerService.cs - Async scheduling
```

### lib-asset Helper Classes
```
plugins/lib-asset/
├── AssetEventEmitter.cs - Event publishing
├── BundleConverter.cs - Format conversion
├── Processing/AssetProcessorPoolManager.cs - Pool operations
└── Webhooks/MinioWebhookHandler.cs - Storage events
```

---

## Priority 1: Critical Refactoring

### 1.1 SaveLoadService (3960 lines) - TOO LARGE

**Location**: `plugins/lib-save-load/SaveLoadService.cs`

**Problem**: 7 distinct domains mixed into single class, making unit testing difficult.

#### Extract: VersionDataLoader (~600 lines)
**What**: Load operations with delta reconstruction
**Lines**: ~706-880, 1277-1340, 2080-2091
**Methods to extract**:
- `LoadAsync` - Main load operation
- `LoadWithDeltasAsync` - Delta reconstruction loading
- `LoadVersionDataAsync` (private) - Core data loading
- `ReconstructFromDeltaChainAsync` (private) - Delta reconstruction
- `FindVersionByCheckpointAsync` (private) - Checkpoint lookup
- `LoadFromAssetServiceAsync` (private) - Asset service integration

**Dependencies**:
```csharp
public class VersionDataLoader
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly SaveLoadServiceConfiguration _configuration;
    private readonly IAssetClient _assetClient;
    private readonly ILogger<VersionDataLoader> _logger;
    private readonly IMessageBus _messageBus;
}
```

#### Extract: VersionCleanupManager (~150 lines)
**What**: Version retention policies and cleanup
**Lines**: ~655-701, 3315-3330
**Methods to extract**:
- `PerformRollingCleanupAsync`
- `CleanupOldVersionsAsync`
- Version retention policy logic
- Pinned version tracking

#### Extract: SaveCompressionHandler (~200 lines)
**What**: Compression/decompression operations (scattered throughout)
**Lines**: 1088-1096, 2122-2123, 3834-3835, and others
**Methods to extract**:
- Compression type resolution
- Compress/decompress operations
- Compression ratio calculations
- Hot cache compression logic

#### Extract: SlotManagementHelper (~300 lines)
**What**: Slot CRUD operations
**Lines**: ~69-400
**Methods to extract**:
- `CreateSlotAsync`
- `GetSlotAsync`
- `ListSlotsAsync`
- `DeleteSlotAsync`
- `RenameSlotAsync`
- `BulkDeleteSlotsAsync`

#### Extract: SaveExportManager (~300 lines)
**What**: Import/export ZIP handling
**Lines**: ~2231-2410
**Methods to extract**:
- `ExportSavesAsync`
- `ImportSavesAsync`
- ZIP archive creation/extraction
- Manifest handling

#### Extract: SaveMigrationHandler (~300 lines)
**What**: Schema migration logic
**Lines**: ~3600-3943
**Methods to extract**:
- `MigrateSaveAsync`
- Schema version resolution
- Migration path computation
- Migrator instantiation

---

### 1.2 AuthService (2891 lines) - DUPLICATED CODE

**Location**: `plugins/lib-auth/AuthService.cs`

**Problem**: AuthService contains ~400 lines of code that DUPLICATES functionality already in TokenService, SessionService, and OAuthProviderService. The helpers exist but aren't being used.

#### Fix: Remove Duplicated Token Generation (~164 lines)
**Lines**: 1163-1326
**Issue**: `GenerateAccessTokenAsync()` duplicates TokenService functionality
**Fix**: Delegate to `_tokenService.GenerateAccessTokenAsync(account, cancellationToken)`

#### Fix: Remove Duplicated Session Management (~307 lines)
**Lines**: 1333-1640
**Issue**: 11 private session methods duplicate SessionService
**Methods to remove/delegate**:
- `StoreRefreshTokenAsync()` (lines 1333-1342)
- `ValidateRefreshTokenAsync()` (lines 1344-1358)
- `RemoveRefreshTokenAsync()` (lines 1360-1381)
- `FindSessionKeyBySessionIdAsync()` (lines 1386-1408)
- `AddSessionIdReverseIndexAsync()` (lines 1413-1440)
- `RemoveSessionIdReverseIndexAsync()` (lines 1445-1468)
- `ExtractSessionKeyFromJWT()` (lines 1473-1489)
- `AddSessionToAccountIndexAsync()` (lines 1494-1535)
- `RemoveSessionFromAccountIndexAsync()` (lines 1540-1640)

#### Fix: Remove Duplicated OAuth Code (~400 lines)
**Lines**: 1872-2271
**Issue**: Full OAuth implementations exist in OAuthProviderService
**Methods to delegate**:
- `ExchangeDiscordCodeAsync()` (lines 1872-1952)
- `ExchangeGoogleCodeAsync()` (lines 1953-2033)
- `ExchangeTwitchCodeAsync()` (lines 2034-2116)
- `ValidateSteamTicketAsync()` (lines 2117-2168)
- `FindOrCreateOAuthAccountAsync()` (lines 2169-2271)

#### NEW Extract: PasswordResetService (~172 lines)
**Lines**: 759-939
**Methods to extract**:
- `RequestPasswordResetAsync()`
- `SendPasswordResetEmailAsync()`
- `ConfirmPasswordResetAsync()`

---

### 1.3 OrchestratorService (3247 lines) - TOO LARGE

**Location**: `plugins/lib-orchestrator/OrchestratorService.cs`

**Problem**: `DeployAsync` method alone is ~600 lines with 5 distinct responsibilities.

#### Extract: DeploymentOrchestrator (split DeployAsync ~600 lines)
**Lines**: 465-1065
**Split into**:

1. **DeploymentBackendSelector** (~52 lines)
   - Backend detection and validation
   - Lines 468-520

2. **TopologyResolver** (~129 lines)
   - Preset/topology resolution
   - Lines 522-651

3. **EnvironmentBuilder** (~66 lines)
   - Multi-layer environment construction
   - Lines 675-741

4. **NodeDeployer** (~193 lines)
   - Individual node deployment with rollback
   - Lines 660-853

5. **DeploymentHealthWaiter** (~92 lines)
   - Heartbeat polling and health verification
   - Lines 858-950

#### Extract: TeardownOrchestrator
**Lines**: 1328+
**What**: Main teardown logic (similar multi-step pattern as Deploy)

---

## Priority 2: Recommended Extractions

### 2.1 ConnectService (3085 lines)

**Location**: `plugins/lib-connect/ConnectService.cs`

#### Extract: CapabilityManifestBuilder (~120 lines)
**Lines**: 2274-2568
**Issue**: Duplicated manifest-building logic in `SendCapabilityManifestAsync()` and `ProcessCapabilitiesAsync()`
**Methods to extract**:
- `BuildManifestForSession()`
- `BuildAvailableApiList()`
- `FilterAndParseEndpoints()`

#### Extract: SessionShortcutManager (~180 lines)
**Lines**: 2659-2930
**Methods to extract**:
- `HandleShortcutPublishedAsync()`
- `HandleShortcutRevokedAsync()`
- `ParseShortcutFromEvent()`
- Shortcut validation logic

#### Extract: PendingRPCManager (~80 lines)
**Lines**: 1034-1037, 1944-2003
**Methods to extract**:
- `RegisterPendingRPC()`
- `TryGetPendingRPC()`
- `ForwardRPCResponseAsync()`
- `CleanupExpiredRPCs()`

#### Extract: SessionEventPublisher (~50 lines)
**What**: Session lifecycle event publishing
**Methods to extract**:
- `PublishSessionConnectedAsync()`
- `PublishSessionReconnectedAsync()`
- `PublishSessionDisconnectedAsync()`

---

### 2.2 SceneService (2300 lines)

**Location**: `plugins/lib-scene/SceneService.cs`

#### Extract: SceneValidationService (~250 lines)
**Lines**: 1458-1707
**Methods to extract**:
- `ValidateSceneStructure()`
- `ApplyGameValidationRulesAsync()`
- `ApplyValidationRule()`
- `MergeValidationResults()`

#### Extract: SceneReferenceResolver (~240 lines)
**Lines**: 1857-2096
**Methods to extract**:
- `ResolveReferencesAsync()`
- `ResolveReferencesRecursiveAsync()` (309 lines - largest method)
- `ExtractSceneReferences()`/`ExtractSceneReferencesRecursive()`
- `ExtractAssetReferences()`/`ExtractAssetReferencesRecursive()`
- `FindReferenceNodes()`/`FindReferenceNodesRecursive()`
- `CollectNodes()`

#### Extract: SceneCheckoutManager (~300 lines)
**Lines**: 680-980
**Methods to extract**:
- `CheckoutSceneAsync()`
- `CommitSceneAsync()`
- `DiscardCheckoutAsync()`
- `HeartbeatCheckoutAsync()`

#### Extract: SceneIndexManager (~130 lines)
**Lines**: 1731-1856
**Methods to extract**:
- `UpdateSceneIndexesAsync()`
- `RemoveFromIndexesAsync()`
- `GetAllSceneIdsAsync()`
- `AddToGlobalSceneIndexAsync()`
- `RemoveFromGlobalSceneIndexAsync()`

#### Extract: SceneVersionManager (~60 lines)
**Lines**: 1394-1456
**Methods to extract**:
- `GetAssetVersionHistoryAsync()`
- `AddVersionHistoryEntryAsync()`
- `DeleteVersionHistoryAsync()`

---

### 2.3 MappingService (3051 lines)

**Location**: `plugins/lib-mapping/MappingService.cs`

#### Extract: SpatialIndexManager (~130 lines)
**Lines**: 1854-1980, 2119-2169
**Methods to extract**:
- `UpdateObjectPositionAsync()`
- `UpdateObjectBoundsAsync()`
- `RemoveObjectPositionAsync()`
- `QueryObjectsInBoundsAsync()`
- `QueryObjectsInRegionAsync()`

#### Extract: AffordanceScorer (~150 lines)
**Lines**: 2204-2469
**Methods to extract**:
- `ScoreAffordance()` (60+ lines)
- `ScoreCustomAffordance()` (70+ lines)
- `GetKindsForAffordanceType()`
- `ExtractRelevantFeatures()`

#### Extract: ObjectChangeProcessor (~100 lines)
**Lines**: 1708-1854
**Methods to extract**:
- `ProcessObjectChangeAsync()`
- `ProcessObjectChangeWithIndexCleanupAsync()`
- `ProcessPayloadsAsync()`

#### Extract: MappingEventPublisher (~100 lines)
**Lines**: 2490-2823
**Methods to extract**:
- `PublishChannelCreatedAsync()`
- `PublishMapUpdatedAsync()`
- `PublishMapObjectsChangedAsync()`
- Move `EventAggregationBuffer` to own file

#### Extract: AuthorityTokenManager (~50 lines)
**Lines**: 200-242
**Methods to extract**:
- `GenerateAuthorityToken()`
- `ParseAuthorityToken()`
- `IsTokenExpired()`

---

### 2.4 MatchmakingService (2135 lines)

**Location**: `plugins/lib-matchmaking/MatchmakingService.cs`

#### Extract: TicketValidator (~37 lines)
**Lines**: 438-475
**What**: Consolidates concurrent ticket limits, exclusive group checks, duplicate queue checks

#### Extract: MatchmakingAlgorithm (~165 lines)
**Lines**: 896-1061
**Methods to extract**:
- `TryMatchTickets()`
- `TryImmediateMatchAsync()`
- `MatchesQuery()`
- Skill window calculations

#### Extract: MatchFormationService (~230 lines)
**Lines**: 1062-1292
**What**: Match creation, session spawning, shortcut publishing

#### Extract: MatchLifecycleManager (~350 lines)
**Lines**: 1173-1525
**Methods to extract**:
- `FinalizeMatchAsync()`
- `CancelMatchAsync()`
- `CancelTicketInternalAsync()`
- Cleanup operations

#### Extract: QueueStatisticsCollector (~175 lines)
**Lines**: 1825-2002
**Methods to extract**:
- `ProcessAllQueuesAsync()`
- `PublishStatsAsync()`

#### Consolidate: MatchmakingStateStore (~150 lines)
**Lines**: 1607-1757
**Issue**: 25+ state store CRUD helpers that are simple wrappers
**Methods to consolidate**:
- `LoadQueueAsync()`, `SaveQueueAsync()`
- `LoadTicketAsync()`, `SaveTicketAsync()`
- `AddToPlayerTicketsAsync()`, `GetQueueTicketCountAsync()`
- etc.

---

### 2.5 GameSessionService (2183 lines)

**Location**: `plugins/lib-game-session/GameSessionService.cs`

#### Extract: SubscriberSessionManager (~200 lines)
**Lines**: 1556-1750
**Methods to extract**:
- `HandleSubscriptionUpdatedInternalAsync()`
- `FetchAndCacheSubscriptionsAsync()`
- `StoreSubscriberSessionAsync()`
- `RemoveSubscriberSessionAsync()`
- `GetSubscriberSessionsAsync()`
- `IsValidSubscriberSessionAsync()`

#### Extract: JoinShortcutPublisher (~120 lines)
**Lines**: 1750-1870
**Methods to extract**:
- `PublishJoinShortcutAsync()`
- `RevokeShortcutsForSessionAsync()`

#### Extract: LobbyManager (~90 lines)
**Lines**: 1871-1960
**Methods to extract**:
- `GetOrCreateLobbySessionAsync()`
- `GetLobbySessionAsync()`

#### Extract: VoiceRoomCoordinator (~25 lines)
**Lines**: 277-301
**What**: Voice room creation (currently embedded in session creation)

#### Fix: Static Cache Issue
**Line**: 64
**Issue**: `_accountSubscriptions` static ConcurrentDictionary could become inconsistent across multiple instances
**Fix**: Move to distributed state store

---

## Shared Patterns for bannou-service

These patterns appear across multiple services and should be extracted to shared utilities:

### Pattern 1: Error Event Publishing
**Services**: ALL
**Current**: Each service has identical `TryPublishErrorAsync` wrapping
**Suggestion**: Create `ServiceErrorPublisher` helper in `bannou-service/`
```csharp
public interface IServiceErrorPublisher
{
    Task PublishErrorAsync(string serviceName, string operation,
        string errorType, string message, object? details = null);
}
```

### Pattern 2: State Store Key Building
**Services**: SaveLoad, Scene, Matchmaking, GameSession, Mapping
**Current**: String concatenation scattered throughout
**Suggestion**: Create `StateKeyBuilder` utility
```csharp
public static class StateKeyBuilder
{
    public static string BuildKey(string prefix, params string[] parts);
    public static string BuildIndexKey(string indexName, string entityId);
}
```

### Pattern 3: Event Publishing Boilerplate
**Services**: All have 5-8 `PublishXxxEventAsync` methods with identical patterns
**Suggestion**: Generic `EventPublisher<T>` pattern or source generator

### Pattern 4: Index Management
**Services**: Scene, Asset, Documentation
**Suggestion**: `IIndexManager<TEntity>` interface
```csharp
public interface IIndexManager<TEntity>
{
    Task AddToIndexAsync(string indexKey, string entityId, CancellationToken ct);
    Task RemoveFromIndexAsync(string indexKey, string entityId, CancellationToken ct);
    Task<IList<string>> GetIndexEntriesAsync(string indexKey, CancellationToken ct);
}
```

### Pattern 5: WebSocket Shortcut Publishing
**Services**: Matchmaking, GameSession, Connect
**Suggestion**: `IWebSocketShortcutPublisher` in lib-mesh or bannou-service
```csharp
public interface IWebSocketShortcutPublisher
{
    Task PublishShortcutAsync(string sessionId, ShortcutData shortcut, CancellationToken ct);
    Task RevokeShortcutAsync(string sessionId, Guid shortcutGuid, CancellationToken ct);
}
```

### Pattern 6: JSON Property Extraction
**Services**: Connect (shortcut parsing), Mapping (affordance scoring)
**Suggestion**: `JsonPropertyHelper` utility
```csharp
public static class JsonPropertyHelper
{
    public static bool TryGetDouble(JsonElement element, string propertyName, out double value);
    public static bool TryGetInt(JsonElement element, string propertyName, out int value);
    public static bool TryGetString(JsonElement element, string propertyName, out string? value);
}
```

---

## Implementation Guidelines

When extracting helper services:

1. **Create interface first**: `IXxxHelper` in the plugin
2. **Implement class**: With proper DI dependencies
3. **Register in DI**: In the plugin's `*ServicePlugin.cs`
4. **Inject into main service**: Via constructor
5. **Add unit tests**: In `lib-xxx.tests/` for the helper
6. **Update main service**: Replace inline code with helper calls

**Example structure**:
```
plugins/lib-save-load/
├── SaveLoadService.cs (main orchestrator, reduced)
├── Helpers/
│   ├── IVersionDataLoader.cs
│   ├── VersionDataLoader.cs
│   ├── IVersionCleanupManager.cs
│   ├── VersionCleanupManager.cs
│   └── ...
└── SaveLoadServicePlugin.cs (register helpers in DI)
```

---

## Progress Tracking

- [ ] **P1.1** SaveLoadService extractions (7 helpers)
- [ ] **P1.2** AuthService duplication fixes
- [ ] **P1.3** OrchestratorService DeployAsync splitting
- [ ] **P2.1** ConnectService extractions (4 helpers)
- [ ] **P2.2** SceneService extractions (5 helpers)
- [ ] **P2.3** MappingService extractions (5 helpers)
- [ ] **P2.4** MatchmakingService extractions (6 helpers)
- [ ] **P2.5** GameSessionService extractions (4 helpers + fix)
- [ ] **Shared** bannou-service shared patterns (6 utilities)

---

*This document is auto-generated from codebase analysis. Update as refactoring progresses.*
