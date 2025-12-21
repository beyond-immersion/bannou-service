# TENET Violations Audit Report

> **Generated**: 2025-12-19
> **Last Updated**: 2025-12-19
> **Scope**: All Bannou service plugins and infrastructure
> **Purpose**: Track violations requiring remediation

---

## Intentional Exceptions

> **Note**: The following plugins are intentionally exempt from certain tenets for specific purposes.

### lib-testing (Exception to Tenet 1)

The `lib-testing` plugin is **intentionally manually implemented** without a corresponding `testing-api.yaml` schema. This is by design:

- **Purpose**: Tests infrastructure and code generation systems
- **Rationale**: Changes to fundamental service mappings, eventing, or testing infrastructure should cause tests to break, catching issues early
- **Consequence**: This plugin serves as a canary for infrastructure changes

This is NOT a violation - it's a deliberate architectural decision to ensure test infrastructure stability.

---

## Evaluated and Acceptable Patterns

> **Note**: The following patterns were initially flagged as potential violations but after evaluation are acceptable.

### ConnectionState Local Locks (Tenet 4 - Acceptable)
**File**: `lib-connect/Protocol/ConnectionState.cs`
**Lines**: 13, 107-124, 130-143, 148-153

**Initial Concern**: Uses local `lock()` statements which don't coordinate across service instances.

**Evaluation**: This is per-connection in-memory state, not shared across instances:
- Each WebSocket connection has its own `ConnectionState` instance (created per-session in `ConnectService.cs:436`)
- Locks protect against concurrent access within a single connection's lifecycle (e.g., capability updates while routing messages)
- Distributed session state is handled separately via Dapr state store (`connect-statestore`)
- Each Connect service instance manages its own set of WebSocket connections

**Conclusion**: ✅ Pattern is appropriate - local locks for per-connection in-memory state.

---

### ServiceHealthMonitor Local Locks (Tenet 4 - Acceptable)
**File**: `lib-orchestrator/ServiceHealthMonitor.cs`
**Line**: 36

**Initial Concern**: Uses local `lock()` for routing change tracking.

**Evaluation**: The lock only coordinates timer vs event-driven publication:
- Orchestrator is designed as single-instance (not horizontally scaled)
- The lock protects a simple boolean flag `_routingChanged`
- Coordinates between timer-triggered periodic publication and event-triggered immediate publication
- Actual distributed state (service routings) is stored in Redis via `IOrchestratorRedisManager`
- Uses `ConcurrentDictionary` for the in-memory routing cache

**Conclusion**: ✅ Pattern is appropriate - local lock for single-instance timer coordination.

---

### GetSafeHeaders Plain Dictionary (Tenet 4 - Acceptable)
**File**: `lib-testing/TestingController.cs`
**Lines**: 385-404

**Initial Concern**: Uses `Dictionary<string, string>` instead of `ConcurrentDictionary`.

**Evaluation**: This is request-scoped local state with no concurrent access:
- Dictionary is created fresh on each method call (line 387: `new Dictionary<string, string>()`)
- Only written to in a single-threaded foreach loop
- Returned as a result - never shared across requests or threads
- No concurrent read/write scenario exists

**Conclusion**: ✅ Pattern is appropriate - request-scoped dictionary with single-threaded access.

---

## Fixed Violations

### ~~VIOLATION 1: Missing IErrorEventEmitter in WebsiteService (Tenet 7)~~ ✅ FIXED
**Status**: Fixed 2025-12-19
**File**: `lib-website/WebsiteService.cs`

All 16 methods now:
- Use `async` properly (no `Task.FromResult`)
- Emit error events via `_errorEventEmitter.TryPublishAsync()` in catch blocks
- Include null guards in constructor (also fixes VIOLATION 7)

---

### ~~VIOLATION 2: Missing IErrorEventEmitter in BehaviorService (Tenet 7)~~ ✅ FIXED
**Status**: Fixed 2025-12-19
**File**: `lib-behavior/BehaviorService.cs`

All 6 methods now:
- Use `async` properly (no `Task.FromResult`)
- Emit error events via `_errorEventEmitter.TryPublishAsync()` in catch blocks
- Include null guards in constructor (also fixes VIOLATION 8)

---

### ~~VIOLATION 5: String Interpolation in Logging (Tenet 8)~~ ✅ FIXED
**Status**: Fixed 2025-12-19
**File**: `lib-testing/TestingController.cs`

All 8 logging statements converted to message template format:
- Line 83: `"Found {HandlerCount} test handlers"`
- Line 89: `"Executing tests from {HandlerName}"`
- Line 95: `"Running test: {TestName}"`
- Line 106: `"PASSED: {TestName}"`
- Line 179: `"All infrastructure tests passed: {PassedTests}/{TotalTests}"`
- Line 450: `"Found {HandlerTypeCount} test handler types"`
- Line 456: `"Creating instance of {HandlerTypeName}"`
- Line 465: `"Failed to create instance of {HandlerTypeName}"`

---

### ~~VIOLATION 7: Missing Null Guards in WebsiteService (Tenet 5)~~ ✅ FIXED
**Status**: Fixed 2025-12-19 (as part of VIOLATION 1 fix)

---

### ~~VIOLATION 8: Missing Null Guards in BehaviorService (Tenet 5)~~ ✅ FIXED
**Status**: Fixed 2025-12-19 (as part of VIOLATION 2 fix)

---

## Remaining Violations

None - all violations have been addressed.

---

## Summary by Tenet

| Tenet | Violations | Status | Count |
|-------|-----------|--------|-------|
| Tenet 4 (Multi-Instance) | Local locks | ✅ Evaluated as acceptable | 2 |
| Tenet 4 (Multi-Instance) | Plain Dictionary | ✅ Evaluated as acceptable | 1 |
| Tenet 5 (Service Pattern) | Missing null guards | ✅ Fixed | 2 |
| Tenet 7 (Error Handling) | Missing IErrorEventEmitter | ✅ Fixed | 22 methods |
| Tenet 8 (Logging) | String interpolation | ✅ Fixed | 8 instances |

---

## Remediation Priority

### Phase 1 (Immediate) - ✅ COMPLETE
1. ✅ Inject `IErrorEventEmitter` into WebsiteService and BehaviorService
2. ✅ Convert TestingController logging to message templates

### Phase 2 (This Sprint) - ✅ COMPLETE
3. ✅ Evaluate ConnectionState locks for distributed deployment - Found acceptable
4. ✅ Add null guards to service constructors - Fixed
5. ✅ Evaluate Dictionary in GetSafeHeaders() - Found acceptable (request-scoped)

### Phase 3 (Backlog)
6. Audit remaining services for similar patterns
7. Add automated linting rules for common violations

---

## Code Generation Opportunities

> **Purpose**: Document potential code generation improvements identified during the audit.

### Key Findings

**Already Generated (One-Time, Skip If Exists)**:
- `{Service}ServicePlugin.cs` - Generated by `scripts/generate-plugin.sh` (lines 46-49 check if exists)
- `{Service}Service.cs` - Generated by `scripts/generate-implementation.sh` (lines 46-49 check if exists)

These files are located outside `/Generated/` because they're "ready to edit" after initial generation. The original agent analysis incorrectly identified these as manual boilerplate.

### EventsController Generation Opportunity

**Assessment**: Manual `*EventsController.cs` files have varying levels of boilerplate:

| File | Lines | Boilerplate % | Generation Candidate? |
|------|-------|---------------|----------------------|
| `OrchestratorEventsController.cs` | 36 | 95% | ✅ Good candidate |
| `ServiceErrorEventsController.cs` | 59 | 85% | ✅ Good candidate (stub) |
| `ServiceMappingEventsController.cs` | 142 | 90% | ✅ Good candidate |
| `AuthEventsController.cs` | 171 | 50% | ⚠️ Maybe |
| `ConnectEventsController.cs` | 153 | 40% | ❌ Custom JSON parsing |
| `PermissionsEventsController.cs` | 499 | 20% | ❌ Complex business logic |

**Common Boilerplate Pattern** (can potentially be generated):
```csharp
[Topic("bannou-pubsub", "{topic}")]
[HttpPost("handle-{event-name}")]
public async Task<IActionResult> Handle{Event}Async()
{
    var @event = await DaprEventHelper.ReadEventAsync<{EventType}>(Request);
    if (@event == null) return BadRequest("Invalid event");

    var result = await _service.Handle{Event}Async(@event);
    return result.Item1 == StatusCodes.OK ? Ok() : StatusCode((int)result.Item1);
}
```

**Recommendation**: Future work - generate abstract base class with common error handling. Low priority since simple handlers are already simple, and complex handlers require manual implementation.

---

*This document should be updated as violations are remediated. Check off items and add completion dates.*
