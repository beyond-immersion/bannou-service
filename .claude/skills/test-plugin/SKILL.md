---
description: "Generate TDD unit tests from a plugin's implementation map and generated interfaces. Produces the red-phase test suite that implementation must satisfy. Requires L4 (Schema'd) readiness."
argument-hint: "[plugin-name] - Plugin to generate tests for (e.g., 'divine', 'agency'). Must have generated interfaces (L4+)."
disable-model-invocation: true
---

# Plugin Test Generator

Generates unit tests from the **implementation map** (behavioral specification) and **generated interfaces** (type contracts). Tests are TDD red-phase: they compile but FAIL until implementation satisfies them.

## Prerequisites

Requires **L4 (Schema'd)** or higher:
1. **Implementation map** at `docs/maps/{PLUGIN_NAME}.md`
2. **Generated interface** at `plugins/lib-{service}/Generated/I{Service}Service.cs`
3. **Generated models** in `bannou-service/Generated/Models/{Service}Models.cs`

If ANY missing → STOP and recommend `/check-plugin {service}`.

## Rules

1. **The implementation map IS the test specification.** Every test assertion traces to a specific pseudocode line. The deep dive is context, not specification.
2. **Every endpoint in the map gets tests.** A `Get` with READ + null check + RETURN still gets happy-path and not-found tests.
3. **Capture Pattern is mandatory.** Never `Verify(Times.Once)` without capturing and asserting on actual data. See TESTING-PATTERNS.md.
4. **Unit tests only.** No HTTP routing tests, no JSON serialization tests, no permission tests. Those are HTTP/Edge test tiers.
5. **Do NOT modify the service implementation to make tests compile.** If the interface doesn't match expectations, your expectations are wrong.
6. **Do NOT use `null!`.** Use proper test data or `null` where the type allows it.
7. **Do NOT invent mock model types.** If internal model types don't exist yet (pre-implementation), write stub tests (Phase 3a).
8. **Read a reference test project** before writing. Match the exact patterns used by existing tests.

---

## Phase 1: Selection & Validation

Normalize: `PLUGIN_NAME`, `service_name`, `ServiceName`. Verify all 3 prerequisites.

**Pre-Implementation Detection:**
```bash
wc -l plugins/lib-{service}/{ServiceName}ServiceModels.cs 2>/dev/null
grep -c 'class.*Model' plugins/lib-{service}/{ServiceName}ServiceModels.cs 2>/dev/null
```
If models file is missing/empty/no model classes → `PRE_IMPLEMENTATION = true` (Phase 3a stubs).
If models exist with actual properties → `PRE_IMPLEMENTATION = false` (Phase 3 full tests).

## Phase 2: Context Load

**Step 2a:** Load plugin context:
```
prepare_context(profile: "plugin", service: "{service}")
```

**Step 2b:** Read generated files:
```
read_file("plugins/lib-{service}/Generated/I{Service}Service.cs")
```
```
print_models(plugin: "{service}")
```

**Step 2c:** If `PRE_IMPLEMENTATION = false`, read all manual `.cs` files:
```bash
find plugins/lib-{service}/ -name "*.cs" ! -path "*/obj/*" ! -path "*/bin/*" ! -path "*/Generated/*" 2>/dev/null
```

**Step 2d:** Read existing test files (if any):
```bash
find plugins/lib-{service}.tests/ -name "*.cs" ! -path "*/obj/*" ! -path "*/bin/*" 2>/dev/null
```

**Step 2e:** Read a reference test project (MANDATORY). Pick a plugin with similar structure (multiple clients, state stores, events):
```
read_file("plugins/lib-faction.tests/FactionServiceFactionTests.cs")
```
This establishes: class structure, `ServiceTestBase<TConfig>` inheritance, `TestContext.Current.CancellationToken`, `SaveAsync` 4-param mock signature, generated client return type naming.

## Phase 3: Analyze Map & Generate Tests

For each endpoint in the map, extract: method signature, happy path, error paths, state reads/writes, events published, locks, external calls, key patterns.

### Test File Structure

**One test file per logical group.** Small services (< 10 endpoints): single `{ServiceName}ServiceTests.cs`. Larger: group by entity.

**Naming:** `{MethodName}_{Scenario}_{ExpectedResult}`

### Helper Method for Service Construction
```csharp
private ({ServiceName}Service service, Mock<IStateStore<{Model}>> storeMock, ...) CreateService()
{
    var storeFactory = new Mock<IStateStoreFactory>();
    // Setup each store from map's State section
    // Return tuple of service + mocks tests need
}
```

### Capture Pattern (REQUIRED for all WRITE and PUBLISH)

**State saves:**
```csharp
string? savedKey = null;
{ModelType}? savedModel = null;
storeMock.Setup(x => x.SaveAsync(
        It.IsAny<string>(), It.IsAny<{ModelType}>(),
        It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
    .Callback<string, {ModelType}, StateOptions?, CancellationToken>((k, m, _, _) =>
    {
        savedKey = k;
        savedModel = m;
    })
    .ReturnsAsync("etag");
```

**Event publications:**
```csharp
string? capturedTopic = null;
object? capturedEvent = null;
mockMessageBus.Setup(x => x.TryPublishAsync(
        It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
    .Callback<string, object, CancellationToken>((topic, evt, _) =>
    {
        capturedTopic = topic;
        capturedEvent = evt;
    })
    .ReturnsAsync(true);
```

Then ASSERT on captured data field by field, matching map pseudocode.

### Map Every Conditional to a Test
Each `IF` / error `RETURN` in pseudocode → at least one test:
- `IF READ key == null → RETURN NotFound` → `{Method}_NotFound_ReturnsNotFound`
- `IF existing.Code == request.Code → RETURN Conflict` → `{Method}_DuplicateCode_ReturnsConflict`

### Test Size Limits
| Type | Max Lines |
|------|-----------|
| Guard clause / not-found | 15 |
| Happy path | 40 |
| Error path | 30 |
| Complex logic | 60 |

### What NOT to Test
- HTTP routing, JSON serialization, cross-service error propagation → HTTP integration tests
- Permission enforcement → Edge tests
- Null checks for NRT-protected parameters
- Generated code behavior

## Phase 3a: Pre-Implementation Stub Tests (when `PRE_IMPLEMENTATION = true`)

When internal model types don't exist, write **compilable stubs**:
```csharp
[Fact]
public async Task CreateEntityAsync_ValidRequest_ReturnsOkAndSavesState()
{
    // Map: LOCK, READ index -> 409 if exists, WRITE entity + index, PUBLISH created
    // Arrange: mock store null for index, setup lock
    // Act: call CreateEntityAsync with valid request
    // Assert: status == OK, state saved, event published to "service.entity.created"
    // TODO: Implement after EntityModel exists in {Service}ServiceModels.cs
    await Task.CompletedTask;
}
```

One `[Fact]` per happy/error path. Detailed pseudocode comments. Empty async bodies. Skip helper methods and capture pattern (need concrete types).

## Phase 4: Create Test Project

If `plugins/lib-{service}.tests/` doesn't exist, create the csproj. **Read an existing project first** to get correct versions:
```bash
cat plugins/lib-chat.tests/lib-chat.tests.csproj 2>/dev/null || cat plugins/lib-species.tests/lib-species.tests.csproj 2>/dev/null
```

Match: `OutputType: Exe` (xunit v3), `xunit.v3` package, NO `Microsoft.NET.Test.Sdk`, project references to `lib-{service}` and `test-utilities`.

## Phase 5: Write Constructor Test

```csharp
[Fact]
public void {ServiceName}Service_ConstructorIsValid() =>
    ServiceConstructorValidator.ValidateServiceConstructor<{ServiceName}Service>();
```

One test, one file. Mandatory.

## Phase 6: Write Endpoint Tests

Write all tests following Phase 3 analysis. Apply Capture Pattern, size limits, naming conventions.

## Phase 7: Verify Compilation

```bash
dotnet build plugins/lib-{service}.tests/lib-{service}.tests.csproj --no-restore > /tmp/{service}-test-build.txt 2>&1
```

Fix compilation errors (wrong types, missing usings). Max 3 attempts → STOP.

## Phase 8: Report

```markdown
## Test Generation Complete: {PLUGIN_NAME}

### Tests
| Category | Count |
|----------|-------|
| Constructor | 1 |
| Happy path | {N} |
| Error path | {N} |
| **Total** | **{N}** |

### Files Created/Modified
| File | Status |
|------|--------|
| {path} | {Created/Existed} |

### Compilation: {PASS/FAIL}
### Map Coverage: {N}/{M} endpoints covered

### Next Step
`/implement-plugin {service_name}`
```
