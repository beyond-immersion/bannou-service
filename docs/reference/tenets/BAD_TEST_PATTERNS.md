# Bad Test Patterns Audit

> **Created**: 2026-01-03
> **Purpose**: Document problematic test patterns discovered during codebase audit
> **Status**: COMPLETE - Categorization finished, remediation pending

This document catalogs test anti-patterns found across the Bannou test suite, with remediation strategies.

---

## Critical Anti-Patterns Identified

### 1. Mock-Only Verification (CRITICAL)

**Pattern**: Tests that only call `.Verify()` on mocks without asserting actual behavior.

```csharp
// BAD: Only verifies mock was called
_mockMessageBus.Verify(x => x.TryPublishAsync(
    It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()),
    Times.Once);

// GOOD: Capture and verify actual event data
object? capturedEvent = null;
_mockMessageBus.Setup(x => x.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
    .Callback<string, object, CancellationToken>((topic, evt, _) => capturedEvent = evt)
    .ReturnsAsync(true);

// ... act ...

Assert.NotNull(capturedEvent);
var typedEvent = Assert.IsType<AccountCreatedEvent>(capturedEvent);
Assert.Equal(expectedAccountId, typedEvent.AccountId);
```

**Why it's bad**: Implementation could publish wrong event type, wrong data, or wrong topic - test still passes.

**Files affected**:
- `lib-account.tests/AccountEventPublisherTests.cs`
- `lib-actor.tests/ActorServiceTests.cs`
- `lib-species.tests/SpeciesServiceTests.cs`
- `lib-relationship.tests/RelationshipServiceTests.cs`

---

### 2. Excessive Arrange:Assert Ratio (>10:1)

**Pattern**: Tests with 50+ lines of mock setup and only 3-5 assertions.

```csharp
// BAD: 57 lines of setup for 4 assertions
[Fact]
public async Task CompleteUploadAsync_WithValidSingleUpload_ShouldReturnCreated()
{
    // Arrange - 57 lines of mock setup
    _mockStorageProvider.Setup(...).ReturnsAsync(...);
    _mockAssetStore.Setup(...).ReturnsAsync(...);
    _mockIndexStore.Setup(...).ReturnsAsync(...);
    // ... 50 more lines ...

    // Act - 1 line
    var result = await service.CompleteUploadAsync(request);

    // Assert - 4 lines
    Assert.Equal(StatusCodes.Created, result.status);
    Assert.NotNull(result.response);
}
```

**Why it's bad**: The test is testing mock orchestration, not service logic. Deleting half the implementation might still pass.

**Threshold**: If Arrange > 60% of test, consider splitting or questioning what's being tested.

**Files affected**:
- `lib-asset.tests/AssetServiceTests.cs`
- `lib-permission.tests/PermissionServiceTests.cs`
- `lib-relationship-type.tests/RelationshipTypeServiceTests.cs`

---

### 3. Response-Only Assertions

**Pattern**: Tests that only verify response structure without verifying side effects (saves, events, indices).

```csharp
// BAD: Only checks response, not that data was saved
var (status, response) = await service.CreateSpeciesAsync(request);
Assert.Equal(StatusCodes.Created, status);
Assert.NotNull(response);
Assert.Equal(request.Code, response.Code);
// Missing: Assert that species was actually saved to state store
// Missing: Assert that indices were updated
// Missing: Assert that event was published with correct data

// GOOD: Verify side effects
string? savedKey = null;
SpeciesModel? savedModel = null;
_mockStore.Setup(x => x.SaveAsync(It.IsAny<string>(), It.IsAny<SpeciesModel>(), It.IsAny<CancellationToken>()))
    .Callback<string, SpeciesModel, CancellationToken>((k, m, _) => { savedKey = k; savedModel = m; })
    .ReturnsAsync("etag");

var (status, response) = await service.CreateSpeciesAsync(request);

Assert.Equal($"species:{response.SpeciesId}", savedKey);
Assert.Equal(request.Code.ToUpperInvariant(), savedModel.Code);
```

**Files affected**:
- `lib-game-session.tests/GameSessionServiceTests.cs`
- `lib-character.tests/CharacterServiceTests.cs`
- `lib-species.tests/SpeciesServiceTests.cs`

---

### 4. Mocking The Class Under Test

**Pattern**: Using reflection or test doubles to inject mocks INTO the service being tested.

```csharp
// BAD: Reflection injection of mock into service under test
var service = new ConnectService(...);
var connectionManagerMock = new Mock<IWebSocketConnectionManager>();
typeof(ConnectService)
    .GetField("_connectionManager", BindingFlags.NonPublic | BindingFlags.Instance)
    .SetValue(service, connectionManagerMock.Object);

// This tests the mock, not the service
```

**Why it's bad**: You're no longer testing the actual service - you're testing mock behavior.

**Files affected**:
- `lib-connect.tests/ConnectServiceTests.cs` (lines 677-704)
- `lib-voice.tests/VoiceServiceTests.cs` (lines 624-638)

---

### 5. Cascading Mocks Without Chain Validation

**Pattern**: Multiple interdependent mocks that don't validate the dependency chain.

```csharp
// BAD: Setup two mocks independently without validating chain
_mockCodeIndex.Setup(x => x.GetAsync("CODE", ...)).ReturnsAsync(typeId);
_mockTypeStore.Setup(x => x.GetAsync($"type:{typeId}", ...)).ReturnsAsync(model);

// If service never calls code index, test still passes
// If service uses wrong key format, test still passes
```

**Files affected**:
- `lib-relationship-type.tests/RelationshipTypeServiceTests.cs`
- `lib-relationship.tests/RelationshipServiceTests.cs`

---

### 6. Untested Critical Paths

**Pattern**: Complex business logic with zero test coverage.

| Service | Untested Code | Risk Level |
|---------|---------------|------------|
| `RelationshipTypeService` | `MergeRelationshipTypeAsync` | HIGH - External service calls, pagination |
| `RelationshipTypeService` | `SeedRelationshipTypesAsync` | HIGH - Ordering, deduplication |
| `RelationshipTypeService` | Hierarchy traversal loop | MEDIUM - Recursion bugs |

---

### 7. It.IsAny<> Overuse

**Pattern**: Using `It.IsAny<T>()` when specific values should be validated.

```csharp
// BAD: Accepts any parameters
_mockStore.Setup(x => x.SaveAsync(It.IsAny<string>(), It.IsAny<Model>(), ...))

// GOOD: Validate specific key format
_mockStore.Setup(x => x.SaveAsync(
    It.Is<string>(k => k.StartsWith("species:")),
    It.Is<SpeciesModel>(m => m.Code == expectedCode),
    ...))
```

---

## Overall Statistics

| Category | Count | Description |
|----------|-------|-------------|
| **G (Good)** | ~400 tests | Properly test behavior with capture patterns |
| **T (Tweaks)** | ~90 tests | Sound tests with minor gaps |
| **P (Partial)** | ~50 tests | Missing key assertions (state/events) |
| **W (Worthless)** | ~60 tests | Mostly constructor null-checks on non-complex services |

**Note on Constructor Tests**: Constructor null-check tests catch AI-introduced DI mistakes before expensive integration testing. They are NOT worthless but were duplicative. **SOLUTION**: Use `ServiceConstructorValidator` (see Phase 4 below) - one line per service that catches MORE issues than N individual tests.

---

## Test Categorization By Project

### Legend
- **W** = Worthless (consider removing)
- **P** = Partially useful (needs more assertions)
- **T** = Useful but needs tweaks
- **G** = Good (keep as-is)

---

### lib-actor.tests (122 tests)
**Summary**: 75 G, 36 T, 8 P, 3 W

| Test File | Summary |
|-----------|---------|
| ActorServiceTests.cs | P - CRUD tests need save verification |
| ActorRunnerTests.cs | G - Tests actual behavior with real timing |
| ActorRegistryTests.cs | G - Tests actual ConcurrentDictionary behavior |
| ActorStateTests.cs | G - Tests actual state management |
| Pool/ActorPoolManagerTests.cs | P - Needs assertion on stored state |
| Pool/PoolHealthMonitorTests.cs | P - Needs event capture |
| PoolNode/ActorPoolNodeWorkerTests.cs | T - Good structure, minor mock improvements needed |
| PoolNode/HeartbeatEmitterTests.cs | T - Good pattern, minor timing issues |

---

### lib-auth.tests (45 tests)
**Summary**: W=11 (constructors), P=8, T=20, G=6

| Test | Cat | Notes |
|------|-----|-------|
| Constructor_WithValidParameters_ShouldNotThrow | W | Mere existence check |
| Constructor_WithNull*_ShouldThrow (x11) | W | DI null-checks - valid for safety net |
| AuthServiceConfiguration_ShouldBindFromEnvironmentVariables | P | Missing assertion on how config is used |
| AuthPermissionRegistration_* (x5) | T | Brittle endpoint counts, should use >= |
| LoginAsync_With*_ShouldReturnBadRequest (x3) | T | Basic validation - good but doesn't capture error details |
| ValidateTokenAsync_With*_ShouldReturnUnauthorized (x3) | T | Basic validation |
| OnEventReceivedAsync_AccountDeleted_* (x2) | G | Captures event, checks session lookup |
| RequestPasswordResetAsync_WithNonExistentEmail_ShouldReturnOkToPreventEnumeration | G | Tests security pattern |

---

### lib-auth.tests/TokenServiceTests.cs (30 tests)

| Test | Cat | Notes |
|------|-----|-------|
| Constructor_WithNull*_ShouldThrow (x5) | W | DI null-checks |
| GenerateRefreshToken_* (x3) | T | Tests non-empty and uniqueness |
| GenerateSecureToken_ShouldReturnUrlSafeBase64 | T | Tests base64 safety |
| StoreRefreshTokenAsync_ShouldCallStateStoreWithCorrectKey | G | Captures TTL via Verify |
| ValidateRefreshTokenAsync_WithValidToken_ShouldReturnAccountId | G | Mocks state, verifies key lookup |
| GenerateAccessTokenAsync_ShouldSaveSessionWithCorrectData | G | **Excellent** - Captures session data via Callback |
| RemoveRefreshTokenAsync_ShouldCallDeleteWithCorrectKey | G | Verifies delete key format |

---

### lib-auth.tests/SessionServiceTests.cs (25 tests)
**Summary**: G=18, T=5, W=5

| Test | Cat | Notes |
|------|-----|-------|
| GetSessionAsync_WithExistingSession_ShouldReturnSessionData | G | Proper capture |
| AddSessionToAccountIndexAsync_* (x2) | G | Tests idempotence correctly |
| InvalidateAllSessionsForAccountAsync_WithSessions_* | G | **Excellent** - Event capture |
| Constructor_WithNull*_ShouldThrow (x5) | W | DI null-checks |

---

### lib-account.tests (40 tests)
**Summary**: G=15, T=8, P=5, W=12

| Test | Cat | Notes |
|------|-----|-------|
| CreateAccountAsync_AssignsAdminRole_* (x4) | G | Full flow with state capture |
| UpdatePasswordHashAsync_PublishesAccountUpdatedEvent | G | Event verification |
| ListAccountsAsync_With*Parameter_* (x4) | G | Tests normalization logic |
| CreateAccountAsync_ShouldGenerate*_* (x5) | G | Tests ID uniqueness, timestamps |
| Constructor_WithNull*_ShouldThrow (x6) | W | DI null-checks |
| AccountPermissionRegistration_* (x4) | T | Brittle counts |

---

### lib-account.tests/AccountEventPublisherTests.cs (15 tests)
**Summary**: G=10, T=3, W=2

| Test | Cat | Notes |
|------|-----|-------|
| PublishAccountCreatedAsync_ShouldPublishToCorrectTopic | G | Detailed payload verification |
| PublishAccountCreatedAsync_WithNullDisplayName_* | G | Edge case handling |
| PublishAccountUpdatedAsync_ShouldIncludeAccountState | G | Full state capture |
| PublishAccountDeletedAsync_ShouldIncludeAccountStateAtDeletion | G | **Excellent** snapshot test |
| Constructor_With*_ShouldThrow (x2) | W | DI null-checks |

---

### lib-permission.tests (45 tests)
**Summary**: G=35, T=2, W=8

| Test | Cat | Notes |
|------|-----|-------|
| RegisterServicePermissionsAsync_LockAcquisitionFails_* | G | Tests lock failure |
| RegisterServicePermissionsAsync_StoresPermissionMatrix | G | Comprehensive integration |
| UpdateSessionRoleAsync_IncludesLowerRoleEndpoints | G | **Excellent** - State capture, role inheritance |
| RecompilePermissions_* (x4) | G | Tests state-based permission compilation |
| AdminRole_GetsAdminOnlyEndpoints_UserRole_DoesNot | G | **Excellent** RBAC test |
| HandleSessionConnectedAsync_* (x8) | G | Session state management |
| HandleSessionDisconnectedAsync_* (x4) | G | Cleanup verification |
| RegisterServicePermissionsAsync_PublishesOnlyToActiveConnections | G | **Critical** Phase 6 bug fix test |
| Constructor_WithNull*_ShouldThrow (x8) | W | DI null-checks |

---

### lib-connect.tests (70 tests)
**Summary**: G=59, P=8, T=2, W=1

| Test File | Summary |
|-----------|---------|
| ConnectServiceTests.cs | G - Excellent protocol/routing tests |
| SessionShortcutTests.cs | G - All 19 tests are good |
| WebSocketConnectionManagerTests.cs | G - 24/25 good, 1 worthless |
| ClientEventTests.cs | G - All 10 tests good |
| BannouSessionManagerTests.cs | G/P - Some error-swallowing tests lack logging verification |

**Critical Issues**:
- `HasConnection_WithExistingConnection_ShouldReturnTrue` (P) - Uses reflection injection
- `HasConnection_WithNonExistentConnection_ShouldReturnFalse` (P) - Tests mock behavior
- `BroadcastMessageAsync_WithNoConnections_ShouldCompleteWithoutError` (W) - Uses `Assert.True(true)`

**Good Patterns**:
- Binary protocol round-trip tests
- GUID determinism tests
- Thread-safety tests with parallel operations
- Expiration handling tests

---

### lib-orchestrator.tests (150+ tests)
**Summary**: G=140+, P=5-10, T=1-2

| Test File | Summary |
|-----------|---------|
| OrchestratorServiceTests.cs | G - Comprehensive health/deployment/routing |
| SmartRestartManagerTests.cs | G/P - Some tests acknowledge Docker failure |
| ServiceHealthMonitor tests | G - Event capture pattern used well |
| OrchestratorResetToDefaultTests | G - Complex deployment scenarios |

**Good Patterns**:
- Event-driven testing with capture
- Complex topology scenario testing
- Routing protection logic

---

### lib-asset.tests (90+ tests)
**Summary**: G=85+, P=5

| Test File | Summary |
|-----------|---------|
| AssetServiceTests.cs | G - Excellent validation and storage tests |
| MinioWebhookHandlerTests.cs | G - Payload handling |
| AssetEventEmitterTests.cs | G - Client event emission |

**Good Patterns**:
- Comprehensive validation testing
- Multipart upload handling
- Storage integration verification

---

### lib-behavior.tests
| Test File | Category | Notes |
|-----------|----------|-------|
| BehaviorServiceTests.cs | P | GOAP tests need parameter verification |
| BehaviorBundleManagerTests.cs | G | Excellent dual verification pattern |
| Goap/GoapPlannerTests.cs | G | Tests actual planning logic |

---

### lib-documentation.tests
| Test File | Category | Notes |
|-----------|----------|-------|
| DocumentationServiceTests.cs | P | Good input validation, needs state verification |

---

### lib-character.tests
| Test File | Category | Notes |
|-----------|----------|-------|
| CharacterServiceTests.cs | P | 80%+ mock setup, needs state capture |

---

### lib-species.tests
| Test File | Category | Notes |
|-----------|----------|-------|
| SpeciesServiceTests.cs | P | God-mock helper, needs refactoring |

---

### lib-realm.tests
| Test File | Category | Notes |
|-----------|----------|-------|
| RealmServiceTests.cs | G | Good assertion ratios |

---

### lib-location.tests
| Test File | Category | Notes |
|-----------|----------|-------|
| LocationServiceTests.cs | G | Proper state assertions |

---

### lib-relationship.tests
| Test File | Category | Notes |
|-----------|----------|-------|
| RelationshipServiceTests.cs | P | Index updates not verified |

---

### lib-relationship-type.tests
| Test File | Category | Notes |
|-----------|----------|-------|
| RelationshipTypeServiceTests.cs | P | Hierarchy/merge untested |

---

### lib-game-session.tests
| Test File | Category | Notes |
|-----------|----------|-------|
| GameSessionServiceTests.cs | T | Some good patterns, some response-only |

---

### lib-voice.tests
| Test File | Category | Notes |
|-----------|----------|-------|
| VoiceServiceTests.cs | P | Mocks class under test (reflection) |
| KamailioClientTests.cs | G | Proper HTTP mocking |
| RtpEngineClientTests.cs | G | Minimal mocking |
| ScaledTierCoordinatorTests.cs | P | Needs argument verification |

---

### lib-subscription.tests
| Test File | Category | Notes |
|-----------|----------|-------|
| SubscriptionServiceTests.cs | G | Good balance |

---

### lib-service.tests
| Test File | Category | Notes |
|-----------|----------|-------|
| ServiceServiceTests.cs | G | Good state capture |

---

### lib-website.tests
| Test File | Category | Notes |
|-----------|----------|-------|
| WebsiteServiceTests.cs | G | Minimal but honest |

---

## Remediation Priority

### Phase 1: ✅ COMPLETE - Delete/Fix Broken Tests

**Resolution Date**: 2026-01-03

1. ✅ `Assert.True(true)` patterns replaced with `Record.ExceptionAsync` + `Assert.Null(exception)` in:
   - `BannouSessionManagerTests.cs` (4 instances)
   - `WebSocketConnectionManagerTests.cs` (1 instance)

2. ✅ Reflection-based tests in `ConnectServiceTests.cs` fixed:
   - Removed `CreateConnectServiceWithConnectionManager` helper (used reflection to inject mock)
   - Removed `HasConnection_*` tests (tested mock behavior, not service)
   - Removed `SendMessageAsync_WithValidConnection_ShouldReturnTrue` (tested mock behavior)
   - Removed `StaticHeaders_ShouldBeProperlyInitialized` (tested implementation details)
   - Updated event processing tests to use normal constructor

3. ✅ `VoiceServiceTests.cs` - No reflection patterns found (already fixed or stale line references)

4. ✅ Moq callback signatures in Pool/PoolNode tests - Reviewed and found valid:
   - `.Callback(() =>` is valid when parameters aren't needed (e.g., just counting calls)
   - `.Callback<T1,T2,T3>((a,b,c) =>` used when capturing event data
   - All 10 HeartbeatEmitter tests pass

### Phase 2: ✅ COMPLETE - Add Missing Coverage

**Resolution Date**: 2026-01-03

Added 21 new tests for previously untested high-risk methods:

**MergeRelationshipTypeAsync tests (7 tests):**
- Source type not found → NotFound
- Source type not deprecated → BadRequest
- Target type not found → NotFound
- No relationships to migrate → OK with 0 migrated
- Single page of relationships → migrates all, verifies update calls
- Multiple pages → pagination working correctly
- Partial failure → continues with remaining, reports 2 migrated

**SeedRelationshipTypesAsync tests (8 tests):**
- Empty list → OK with zero counts
- New type → creates successfully
- Existing type without UpdateExisting → skips
- Existing type with UpdateExisting → updates
- Types with parent hierarchy → processes parent before child (order verification)
- Unresolvable parent → reports error
- Multi-level hierarchy (grandchild->child->parent) → correct ordering
- Mixed create/skip/update → correct counts

**Deep hierarchy traversal tests (6 tests):**
- GetChildRelationshipTypesAsync with direct children
- GetChildRelationshipTypesAsync recursive with grandchildren
- MatchesHierarchyAsync with direct parent → depth 1
- MatchesHierarchyAsync with grandparent → depth 2
- MatchesHierarchyAsync with unrelated type → returns false
- GetAncestorsAsync with multiple ancestors → returns in order

**All 46 RelationshipTypeService tests now pass.**

### Phase 3: Upgrade P→T Tests
- Add state capture callbacks to all "Partially useful" tests
- Verify saved data, not just response structure
- Key candidates:
  - `lib-species.tests/SpeciesServiceTests.cs`
  - `lib-character.tests/CharacterServiceTests.cs`
  - `lib-relationship.tests/RelationshipServiceTests.cs`

### Phase 4: ✅ SOLVED - Constructor Null Checks Removed

**Resolution Date**: 2026-01-03

**What happened**: Constructor null-checks (`?? throw new ArgumentNullException()`) were removed from all 25 service files. With nullable reference types enabled across all 56 csproj files and ASP.NET Core DI guarantees, these checks were dead code:

1. DI container throws `InvalidOperationException` if service not registered (before constructor runs)
2. Compiler warns about null assignment to non-nullable parameters
3. CLAUDE.md forbids `null!` operator, making bypass impossible

**Preserved**: Configuration property validations (e.g., `ServerSalt` in GameSessionService/ConnectService) - these check runtime values, not DI parameters.

**Test Impact**: All `Constructor_WithNull*_ShouldThrow` tests (marked W) should be **deleted** - they test behavior that no longer exists.

**Services cleaned**:
- AccountService, AuthService, PermissionService, BehaviorService, CharacterService
- SpeciesService, RealmService, LocationService, RelationshipService, RelationshipTypeService
- SubscriptionService, ServiceService, StateService, MeshService, WebsiteService
- TestingService, MessagingService, SubscriptionExpirationService, AssetService
- GameSessionService, ConnectService, VoiceService, OrchestratorService, ActorService
- DocumentationService

---

## Exemplary Tests to Reference

These tests demonstrate the patterns other tests should follow:

1. **TokenService.GenerateAccessTokenAsync_ShouldSaveSessionWithCorrectData** - Perfect capture pattern
2. **PermissionService.UpdateSessionRoleAsync_IncludesLowerRoleEndpoints** - State capture with role inheritance
3. **PermissionService.RegisterServicePermissionsAsync_PublishesOnlyToActiveConnections** - Tests critical bug fix
4. **SessionService.InvalidateAllSessionsForAccountAsync_WithSessions_ShouldDeleteAllAndPublishEvent** - Event capture
5. **ConnectService.GuidGenerator_GenerateServiceGuid_ShouldBeDeterministic** - Determinism testing
6. **SessionShortcutTests.ShortcutOperations_ShouldBeThreadSafe** - Concurrency testing

---

*Categorization complete. See TESTING_PATTERNS.md for guidance on writing new tests.*
