---
description: "Implement a plugin's service logic from its implementation map and failing tests. Iterative loop: implement, self-audit coverage against map, fix gaps, verify tests. One plugin per invocation."
argument-hint: "[plugin-name] - Plugin to implement (e.g., 'divine', 'agency'). Must have failing tests (L5+)."
disable-model-invocation: true
---

# Plugin Implementation

Implements a Bannou plugin's business logic from its **implementation map** (the behavioral specification) and **failing tests** (the acceptance criteria). The map says WHAT each method does; the tests verify it DOES that; your job is to write the code that makes it true.

## Prerequisites

Requires **L5 (Tested)** or higher:
1. **Implementation map** at `docs/maps/{PLUGIN_NAME}.md`
2. **Generated interface** at `plugins/lib-{service}/Generated/I{Service}Service.cs`
3. **Generated controller** at `plugins/lib-{service}/Generated/{Service}Controller.cs`
4. **Generated models** in `bannou-service/Generated/Models/{Service}Models.cs`
5. **Unit tests** at `plugins/lib-{service}.tests/`

If ANY missing → STOP and recommend `/check-plugin {service}`.

## Rules

1. **The implementation map is the specification.** If map and your implementation disagree, YOUR IMPLEMENTATION IS WRONG. If you discover the map is genuinely wrong, stop — report as a Map Observation, do not silently deviate.
2. **Implement exactly what the map specifies.** No bonus features, no "while I'm here" improvements, no skipping "simple" helpers.
3. **Every helper service the map lists must exist.** If the map says `BlessingCalculator` exists, create `BlessingCalculator.cs`. Helper services are part of the specification.
4. **Error paths are part of the specification.** The map shows every conditional RETURN. The tests assert on every error status code. Implement complete methods, not partial ones.
5. **Use proper types from the first line.** Enums, Guids, DateTimeOffset — no "fix later."
6. **No `null!`.** Explicit null checks with meaningful exceptions.
7. **No top-level try-catch.** Generated controller provides the boundary.
8. **Constructor-cache all store references.** `readonly` fields, resolved in constructor via `stateStoreFactory.GetStore<T>()`.
9. **No sentinel values.** Nullable types for absence. Never `Guid.Empty`, `-1`, or empty string.
10. **Add `StartActivity` on all async methods from the start.** Retrofitting is always forgotten.
11. **If approaching context limits, say so.** Do not declare premature victory. The coverage audit determines completeness, not feelings about progress.

---

## Phase 1: Selection & Validation

**If specific name:** Normalize `PLUGIN_NAME`, `service_name`, `ServiceName`. Verify all 5 prerequisites.
**If no argument:** STOP with "Plugin name required."

## Phase 2: Context Load

**Step 2a:** Load plugin context:
```
prepare_context(profile: "plugin", service: "{service}")
```

**Step 2b:** Read generated interface and controller:
```
read_file("plugins/lib-{service}/Generated/I{Service}Service.cs")
read_file("plugins/lib-{service}/Generated/{Service}Controller.cs")
```

**Step 2c:** Discover and read all existing manual code + test files:
```bash
find plugins/lib-{service}/ -name "*.cs" ! -path "*/obj/*" ! -path "*/bin/*" ! -path "*/Generated/*" -exec wc -l {} + 2>/dev/null | sort -rn
find plugins/lib-{service}.tests/ -name "*.cs" ! -path "*/obj/*" ! -path "*/bin/*" -exec wc -l {} + 2>/dev/null | sort -rn
```
Read ALL files found (skip test files under 10 lines).

**After loading, you MUST know:** every method signature, state store and key pattern, event topic, dependency, error condition, and what tests assert on.

## Phase 2.5: Design Fidelity Checkpoint (MANDATORY)

Before extracting the work plan, verify that the implementation map's prescribed approach matches any governing planning doc, and verify your planned implementation matches the map STRUCTURALLY.

**Why this exists:** Incident #19 (`docs/reference/INCIDENT-HISTORY.md`): an agent shipped a reflection-based `DirectDispatchHelper` when `docs/planning/BANNOU-EMBEDDED.md § Section 3` prescribed statically-typed delegate dispatch per generated client method. Functionally equivalent; structurally opposite; AOT-hostile. The divergence was never flagged.

Per QUALITY TENETS (T33 Design Specification Fidelity), when a planning doc or implementation map prescribes a technical approach, the implementation MUST match it STRUCTURALLY — not just functionally.

**Protocol:**

1. **Identify any governing planning doc** beyond the map. Check:
   - The plugin's deep dive for `**Related Planning**:` links or referenced `docs/planning/*.md` files
   - `search_docs(query: "{plugin-name}")` if the deep dive doesn't surface one
   - Any GH issue referenced in the map or deep dive

2. **Read each identified planning doc in full** via `get_document`. If the doc prescribes a specific technical approach (delegate shape, dispatch mechanism, data-flow topology), record the prescription.

3. **Cross-check the map against the plan.** If the planning doc says X and the map says something structurally different, that's a MAP INCONSISTENCY — stop, report it as a Map Observation per Phase 7's Map Observation protocol. Do not proceed until resolved.

4. **When you reach Phase 4 (Staged Implementation)**, the map's pseudocode is the spec for code shape. If the pseudocode shows `READ → WRITE → PUBLISH → RETURN tuple`, the code shows tuple deconstruction and typed calls — not reflection, not MakeGenericMethod, not MethodInfo.Invoke. IMPLEMENTATION TENETS (T34 AOT Compatibility) forbids reflection shortcuts in bannou-service/ and plugins/*/ paths regardless of whether the map notices them.

5. **If you catch yourself reasoning "functional equivalent is good enough"**, you are about to create Incident #20. Stop, present the divergence, wait for direction.

## Phase 3: Work Plan

Before writing any code, extract a mechanical checklist from the implementation map:

**A. Methods** (from Method Index):
```
| # | Method | Route | Mutates? | Events | Status |
|---|--------|-------|----------|--------|--------|
```

**B. Internal Models** (from State section):
```
| # | Model | State Store | Key Pattern | Status |
|---|-------|-------------|-------------|--------|
```

**C. Helper Services** (from DI Services — plugin-specific only, not ILogger/IMessageBus):
```
| # | Helper Service | Purpose | Status |
|---|---------------|---------|--------|
```

**D. Event Handlers** (from Events Consumed):
```
| # | Event Topic | Handler Method | Status |
|---|-------------|---------------|--------|
```

**E. Key Builders** (from State key patterns):
```
| # | Key Builder | Pattern | Status |
|---|-------------|---------|--------|
```

Write this plan to `/tmp/{service_name}-work-plan.md` (survives context pressure).

## Phase 4: Staged Implementation

### Stage A: Internal Models (`{Service}Service.Models.cs`)
From the map's State section, create model classes:
- Proper types (Guid, enums, DateTimeOffset), nullable for optional fields
- Records for immutable data, classes for mutable state
- `Build*Key()` methods as `internal static` with `const` prefixes

Build: `dotnet build plugins/lib-{service}/lib-{service}.csproj --no-restore > /tmp/{service}-build.txt 2>&1`
Must pass before proceeding.

### Stage B: Main Service (`{Service}Service.cs`)
Create the service class per FOUNDATION TENETS pattern:
- `[BannouService]` attribute, `partial class`, implements `I{Service}Service`
- Constructor-cached store references, all dependencies from map's DI Services
- **EVERY method from the work plan**, following map pseudocode line by line
- Return tuples: `(StatusCodes.OK, new Response { ... })` / `(StatusCodes.NotFound, null)`
- Generated `Publish*Async` extensions (NOT inline topic strings)
- Structured logging with message templates
- `StartActivity` span on all async helper methods (NOT on primary interface methods)

Build after Stage B.

### Stage C: Helper Services (`Services/*.cs`) — if map requires
Create each helper from the map's DI Services section. Each gets: own file, constructor injection, `ITelemetryProvider`, XML docs.

Build after Stage C.

### Stage D: Event Handlers (`{Service}Service.Events.cs`) — if map requires
Create events partial class with `RegisterEventConsumers` and handler methods per map pseudocode.

Build after Stage D.

Update work plan status columns. Write to `/tmp/{service_name}-work-plan.md`.

## Phase 5: Coverage Audit (THE CRITICAL GATE)

**You perform this audit yourself. No agents.**

Compare your implementation against the map mechanically:

### Methods ({N}/{M} complete)
For EACH method in the map's Method Index:
- [ ] Method exists with real implementation (not stub/NotImplemented)?
- [ ] Follows map pseudocode? (READ/WRITE/LOCK/PUBLISH/RETURN all present?)
- [ ] Has telemetry span?
- [ ] Returns `(StatusCodes, TResponse?)` tuple?
- [ ] Uses generated `Publish*Async` (not inline topic strings)?

### Helper Services ({N}/{M} complete)
For EACH in map's DI Services:
- [ ] Class file exists in Services/?
- [ ] Has methods the map describes?
- [ ] Injected into main service constructor?

### Event Handlers ({N}/{M} complete)
For EACH in map's Events Consumed:
- [ ] Handler method exists?
- [ ] Registered in `RegisterEventConsumers`?
- [ ] Real implementation per map pseudocode?

### Internal Models ({N}/{M} complete)
For EACH in map's State:
- [ ] Model class exists with correct fields and proper types?

### Key Builders
For EACH key pattern:
- [ ] `Build*Key()` method exists (internal static)?

**If gaps remain:** fix them, rebuild, re-audit. Max 3 rounds. If gaps persist after 3 rounds → HARD STOP and report.

## Phase 6: Tenet Compliance Audit

**After coverage is 100%, audit for tenet compliance yourself.**

### FOUNDATION (T4/T5/T6)
- [ ] All state via constructor-cached `IStateStore<T>` fields
- [ ] `IStateStoreFactory` NOT stored as field
- [ ] All state changes publish typed events via generated extensions
- [ ] No inline topic strings, no anonymous event objects
- [ ] `partial class`, `Build*Key()` methods `internal static` with `const` prefix

### BEHAVIOR (T7/T8/T9/T30)
- [ ] No top-level try-catch (controller provides boundary)
- [ ] `ApiException` catch only for inter-service calls
- [ ] All methods return `(StatusCodes, TResponse?)` tuples
- [ ] No in-memory authoritative state
- [ ] `StartActivity` on all async helpers (NOT primary interface methods)

### DATA (T14/T20/T21/T23/T24/T25/T26)
- [ ] No string fields for enums/Guids in any model
- [ ] No sentinel values (Guid.Empty, -1, empty string)
- [ ] `BannouJson` for serialization (not direct JsonSerializer)
- [ ] All Task-returning methods are `async` with `await`
- [ ] Config values from generated config class

### QUALITY (T10/T16/T19/T22)
- [ ] Structured logging with message templates
- [ ] XML documentation on public members
- [ ] No `#pragma warning disable`

Fix violations, rebuild. Max 2 tenet audit rounds.

## Phase 7: Test Verification

```bash
dotnet test plugins/lib-{service}.tests/lib-{service}.tests.csproj --no-restore > /tmp/{service}-test.txt 2>&1
```

**If all pass** → proceed to report.

**If failures:**
1. Compare test expectation with implementation AND map pseudocode
2. **Implementation bug** (most common) → fix implementation
3. **Map ambiguity** → implement what test expects, note as Map Observation
4. **Test appears wrong** → HARD STOP. Report: "Test `{Name}` expects `{X}` but map specifies `{Y}`."

Max 5 fix iterations → STOP and report.

## Phase 8: Report

```markdown
## Implementation Complete: {PLUGIN_NAME}

### Summary
| Metric | Count |
|--------|-------|
| Methods implemented | {N} |
| Internal models | {N} |
| Event handlers | {N} |
| Helper services | {N} |
| Coverage audit rounds | {N} |
| Tenet audit rounds | {N} |

### Files Created/Modified
| File | Status | Purpose |
|------|--------|---------|
| {path} | {Created/Modified} | {purpose} |

### Build: PASS | Tests: {ALL PASS / N of M}

### Coverage Audit: {100% / gaps remaining}
### Tenet Compliance: {CLEAN / violations listed}

### Map Observations for Human Review
| # | Observation | Map Section |
|---|-------------|-------------|
{or "No map issues discovered."}

### Recommended Next Steps
1. `/check-plugin {service}` to verify L7 readiness
2. Review Map Observations above
```
