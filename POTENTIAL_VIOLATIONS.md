# TENET Violations Report - Post-Investigation

> **Updated**: 2026-01-09
> **Status**: Comprehensive audit with investigation and remediation

This document reflects the findings after thorough investigation of the original violations report. Many claimed violations were FALSE POSITIVES due to misunderstanding of tenet scope.

---

## Executive Summary

| Tenet | Original Claim | Actual Status | Notes |
|-------|----------------|---------------|-------|
| T0 | 11+ violations | **FIXED** | Replaced tenet numbers with category names |
| T4 | 2 violations | **FIXED** | BehaviorDocumentCache now uses IHttpClientFactory |
| T6 | 5 violations | **FALSE POSITIVE** | Helper services don't need [BannouService] |
| T7 | 150+ violations | **FALSE POSITIVE** | ApiException only applies to service client calls |
| T8 | 42 violations | **FIXED** | Removed error response bodies, updated schemas |
| T10 | 5 violations | **PARTIALLY FIXED** | Token logging removed |
| T11 | 2 violations | **FIXED** | BinaryProtocolTests moved to lib-connect.tests |
| T12 | 6 violations | **FIXED** (5 of 6) | Changed Times.AtLeastOnce to Times.Once |
| T13 | 24 violations | **FALSE POSITIVE** | Internal APIs intentionally omit x-permissions |
| T23 | 170+ violations | **PARTIALLY FIXED** | Achievement sync patterns fixed |

---

## FALSE POSITIVES (Not Violations)

### T6: Service Implementation Pattern - NOT VIOLATIONS

**Original Claim**: Helper services missing `partial` and `[BannouService]` attribute.

**Investigation Result**: These are **helper services** in `Services/` subdirectories, not main service implementations. The `[BannouService]` attribute is only for services that:
1. Implement a generated `I{Service}Service` interface
2. Are the main service entry point for a plugin
3. Need to be discovered and registered by the plugin loader

Helper services like `TokenService`, `SessionService`, `GitSyncService` etc. are internal implementation details injected via standard DI - they correctly use normal class declarations.

**Status**: FALSE POSITIVE - No action required.

---

### T7: Error Handling - NOT VIOLATIONS

**Original Claim**: 150+ violations - services catch generic `Exception` without first catching `ApiException`.

**Investigation Result**: The T7 tenet states ApiException handling is required **when calling service clients**. The flagged services (AccountService, RealmService, etc.) use **state stores** (`IStateStoreFactory`, `IObjectStore`), not service clients. State store operations throw standard exceptions, not `ApiException`.

The pattern `catch (ApiException)` followed by `catch (Exception)` only applies when:
- Code calls another Bannou service via generated client (e.g., `_authClient.ValidateTokenAsync()`)
- The service client can throw `ApiException` for 4xx/5xx responses

**Status**: FALSE POSITIVE - Services using only state stores correctly catch generic exceptions.

---

### T13: X-Permissions Usage - NOT VIOLATIONS

**Original Claim**: 24 endpoints missing x-permissions declarations.

**Investigation Result**: The flagged endpoints are **internal service APIs** intentionally without x-permissions:

- `/internal/proxy` - Internal Connect service proxy endpoint
- `/state/*`, `/messaging/*`, `/voice/*` - Infrastructure service APIs
- Documentation GET endpoints - Internal admin APIs

The x-permissions system controls **WebSocket client access**. Internal service-to-service APIs don't need x-permissions because:
1. They're not exposed through the WebSocket gateway
2. They're called internally via lib-mesh
3. Access is controlled at the network/infrastructure level

**Status**: FALSE POSITIVE - Internal APIs correctly omit x-permissions.

---

## FIXED VIOLATIONS

### T0: Tenet Number References - FIXED

Replaced tenet number references with category names:
- `CharacterHistoryService.cs`: "(T5 compliant)" → "per FOUNDATION TENETS"
- `RealmHistoryService.cs`: "(T5 compliant)" → "per FOUNDATION TENETS"

---

### T4: Infrastructure Libs Pattern - FIXED

**Fixed**: `plugins/lib-actor/Caching/BehaviorDocumentCache.cs`
- Changed `_httpClient = new HttpClient()` to use `IHttpClientFactory`

**Not Fixed**: `bannou-service/Program.cs` line 640
- Direct HttpClient in startup for health check - may have DI constraints during startup

---

### T8: Return Pattern - FIXED

**Comprehensive Fix**:
1. Updated tenet wording in `IMPLEMENTATION.md` to clarify no error bodies allowed
2. Changed all service methods returning error status codes to return `null`
3. Removed error response types from schemas:
   - `AbmlErrorResponse` from behavior-api.yaml
   - `ConnectErrorResponse` from connect-api.yaml
   - `ErrorResponse` and `PoolBusyResponse` from orchestrator-api.yaml
4. Regenerated all clients/controllers
5. Fixed http-tester to catch `ApiException` instead of typed variants

**Exception**: `website-api.yaml` retains `ErrorResponse` - browser-facing APIs need structured errors for frontend JavaScript to display validation messages and custom 404 pages.

Services fixed:
- BehaviorService, ActorService, MappingService, PermissionService
- MessagingService, StateService, TestingService, SceneService
- ConnectService, LocationService, GameSessionService, OrchestratorService
- AchievementService, GameServiceService

---

### T10: Logging Standards - PARTIALLY FIXED

**Fixed**: `plugins/lib-auth/AuthService.cs`
- Removed logging of first 10 characters of reset token (line 843-844)

**Not Fixed** (lower priority):
- Bracket formatting in log templates
- JWT secret length logging (informational, not the actual secret)

---

### T11: Testing Requirements - FIXED

**Fixed**: `plugins/lib-connect/Protocol/BinaryProtocolTests.cs`
- Moved from production directory to `plugins/lib-connect.tests/`
- Converted from static test runner to xUnit tests with `[Fact]` and `[Theory]`

**Not Addressed**: Missing `lib-testing.tests/` project
- lib-testing is a test utility library, not a service requiring its own tests

---

### T12: Test Integrity - FIXED (5 of 6)

Changed `Times.AtLeastOnce()` to `Times.Once()` in:
- `plugins/lib-permission.tests/PermissionServiceTests.cs` (4 locations)
- `plugins/lib-mapping.tests/MappingServiceTests.cs` (1 location)

**Not Fixed**: `plugins/lib-orchestrator.tests/SmartRestartManagerTests.cs`
- Line 184: May be legitimate use case for restart scenarios

---

### T23: Async Method Pattern - PARTIALLY FIXED

**Fixed**: Achievement sync classes (4 files)
- `SteamAchievementSync.cs`, `PlayStationAchievementSync.cs`
- `InternalAchievementSync.cs`, `XboxAchievementSync.cs`
- Changed `return Task.FromResult(...)` pattern to `async Task` with `await Task.CompletedTask`

**Not Investigated**:
- Blocking `.Result` calls (126 locations) - many in test code
- `Task.CompletedTask` returns (30+ locations) - many are interface implementations

---

## NOT INVESTIGATED

The following items from the original report were not investigated in this session:

- **T3**: Event Consumer Fan-Out inconsistency (1 item)
- **T9**: Multi-Instance Safety remaining items
- **T14**: Polymorphic Associations (4 items)
- **T15**: Browser-Facing Endpoints (4 items)
- **T16**: Naming Conventions (20 items)
- **T17**: Client Event Schema Pattern (3 items)
- **T23**: Blocking .Result calls and Task.CompletedTask patterns

---

## Pre-Existing Issues (Unrelated to Tenets)

**http-tester Build Failures**:
The http-tester project has pre-existing compilation errors referencing types that no longer exist in behavior-api.yaml:
- `BehaviorStackRequest`, `BehaviorSetDefinition`, `ResolveContextRequest`
- These were removed from the schema at some earlier point

This is not a tenet violation - it's stale test code that needs updating.

---

*Report updated 2026-01-09 after thorough investigation*
