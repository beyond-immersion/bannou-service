---
description: "Parse test results, identify failures, and create one task per component. Process each task inline — read deep dive + map, investigate, apply mechanical fixes or document design questions. One component at a time."
argument-hint: "[unit|structural|http|edge|sdks|all] - Test tier to iterate (default: unit)"
disable-model-invocation: true
---

# Test Iterator

Parses test results, groups failures by component, creates one task per component, then processes each task inline. No sub-agents — all work done directly.

## Rules

1. **No sub-agents.** All work is done by you directly.
2. **One task at a time.** Fully resolve or block before considering the next.
3. **Tests are presumed correct.** If a test appears wrong → STOP the workflow, present the finding, wait for user direction. Do NOT modify tests without explicit approval.
4. **Read the deep dive and map BEFORE investigating.** Even for "obviously mechanical" fixes.
5. **When you hit a blocking outcome (test-in-error, decision checkpoint, entire service unimplemented) → STOP THE WORKFLOW.** End your response. Do not continue to the next task. The user's response may change your approach to ALL remaining tasks.

---

## Test Result Sources

| Filter | Source | You Run It? | Re-Run After Fix? |
|--------|--------|------------|-------------------|
| `unit` | `test-logs/unit-tests.log` | No — read existing | Yes — individual plugin only |
| `structural` | `make test-structural` | Yes → `/tmp/` | Yes — `make test-structural` |
| `http` | `test-logs/http-tester.log` | No — read existing | No — user re-tests |
| `edge` | `test-logs/edge-tester.log` | No — read existing | No — user re-tests |
| `sdks` | `make test-sdks` | Yes → `/tmp/` | Yes — individual SDK |
| `all` | All sources combined | Mix of above | Per-tier rules |

---

## Phase 1: Context & Argument

**Step 1a:** Load development context:
```
prepare_context(profile: "dev")
```

**Step 1b:** Validate argument. Accepted: `unit`, `structural`, `http`, `edge`, `sdks`, `all`. Default: `unit`.

## Phase 2: Acquire Test Results

**For `unit`/`http`/`edge`:** Read from `test-logs/`. If file missing → STOP with instructions for user to generate it.

**For `structural`:**
```bash
make test-structural > /tmp/test-structural-output.txt 2>&1
```

**For `sdks`:**
```bash
make test-sdks > /tmp/test-sdks-output.txt 2>&1
```

**For `all`:** Process each tier in sequence. Missing http/edge logs → note but continue.

## Phase 3: Parse & Group Failures

Group failures by **component** (plugin, SDK, or structural):

- Plugin: namespace `BeyondImmersion.BannouService.{ServicePascalCase}.Tests` → component `{service}`
- Structural: namespace `BeyondImmersion.BannouService.StructuralTests` → when filter is `unit`: **SKIP ALL**. When `structural`/`all`: group by affected plugin from failure message, or `structural` if cross-cutting.
- SDK: project path `sdks/{sdk-name}.tests/` → component `{sdk-name}`
- bannou-service: component `bannou-service`, deep dive `docs/BANNOU-DEEP-DIVE.md`

**Report:**
```
## Test Failures: {FILTER}

| Component | Type | Failures |
|-----------|------|----------|
| {name} | Plugin/SDK | {count} |

Total: {N} failures across {M} components
```

If zero failures → "All tests passed." STOP.

## Phase 4: Create Tasks

One task per component:

```
TaskCreate(
  subject: "Fix {TIER} test failures: {COMPONENT} ({COUNT} failures)",
  activeForm: "Fixing {COMPONENT} test failures",
  description: "{FAILURE_DETAILS — actual test names and error messages}",
  metadata: {
    "tier": "{TIER}",
    "component": "{COMPONENT}",
    "componentType": "{plugin|sdk|structural}",
    "failureCount": {COUNT},
    "testProjectPath": "{path}"
  }
)
```

## Phase 5: Process Tasks

Work through each task in ID order:

### Per-Task Procedure

**Step 1:** Read context for the component:
```
get_plugin_docs(name: "{component}")  // loads deep dive + map
```

**Step 2:** Read test source code and service implementation to understand what each failing test asserts and what the code currently does.

**Step 3:** Determine: is the TEST in error, or the CODE?

**If TEST in error → ⛔ STOP WORKFLOW:**
- Present: what the test expects, why you believe it's wrong, what the correct expectation should be
- List remaining tasks
- End your response. Wait for user direction.

**Step 4:** If CODE is in error, classify the fix:

**MECHANICAL FIX** (apply directly) — clearly prescribed by:
- The deep dive (resolved items are pre-approved)
- The implementation map (specification says what code should do)
- The tenets (in context from `prepare_context`)
- SCHEMA-RULES

**DESIGN QUESTION** (do NOT fix) — requires human judgment, cross-service coordination. Add to deep dive's `#### Bugs (Fix Immediately)` section.

**Step 5:** Apply mechanical fixes. Schema changes first → generation → code. Build:
```bash
dotnet build plugins/lib-{service}/lib-{service}.csproj --no-restore > /tmp/{service}-build.txt 2>&1
```

**Step 6:** Re-run tests (unit/structural/sdk tiers only — NOT http/edge):
```bash
dotnet test --project {TEST_PROJECT_PATH} --no-restore > /tmp/test-{COMPONENT}.txt 2>&1
```

**Step 7:** Mark task complete when: all mechanical fixes verified, design questions documented, test-in-error findings presented.

### Outcome Gate (after every task)

| Outcome | Continue? |
|---------|-----------|
| Mechanical fix applied, tests pass | ✅ Next task |
| Design question documented in deep dive | ✅ Next task |
| Test appears wrong | ⛔ STOP WORKFLOW |
| Decision checkpoint / ambiguity | ⛔ STOP WORKFLOW |
| Entire service unimplemented (all NotImplemented) | ⛔ STOP WORKFLOW |

## Phase 6: Report

```markdown
## Test Iteration Complete: {FILTER}

| Outcome | Count |
|---------|-------|
| Mechanical fixes | {N} |
| Design questions documented | {N} |
| Test errors flagged | {N} |

### Components Fixed
{list with brief description of each fix}

### Design Questions Added
{list with bug titles added to deep dives}

### Pending User Decision
{test-in-error findings, if any}
```
