---
description: "Audit and fix x-permissions on all endpoints for 1-5 plugins. Applies the decision framework from ENDPOINT-PERMISSION-GUIDELINES.md, then fixes schemas, deep dives, and maps."
argument-hint: "<plugin-name> [plugin-name...] - 1 to 5 plugins (e.g., 'achievement', 'divine gardener matchmaking')"
disable-model-invocation: true
---

# Update Permissions

Audits every endpoint's `x-permissions` against ENDPOINT-PERMISSION-GUIDELINES.md and fixes mismatches across schemas, deep dives, and maps. All work done inline — no agents.

## Rules

1. **Run the complete decision framework for EVERY endpoint.** "Looks reasonable" is not an evaluation methodology.
2. **Missing x-permissions IS a finding.** Every endpoint MUST declare x-permissions.
3. **Fix all three file types.** Schema fix without doc updates creates drift.
4. **A mismatch is a mismatch.** If the guideline says X and the schema says not-X, fix it. If you believe the guideline itself is wrong, flag as NEEDS REVIEW.
5. **Surgical edits only.** Change x-permissions values. Do not rewrite files.

---

## Phase 1: Parse & Load

**Step 1a:** Parse argument. 1-5 plugin names, space-separated. Normalize to lowercase with hyphens.
- No argument → ask and STOP
- More than 5 → report error and STOP

**Step 1b:** Verify each plugin's schema exists:
```bash
ls schemas/{plugin}-api.yaml
```
If any API schema missing → report and STOP. Missing deep dive/map → warn but continue.

**Step 1c:** Load context:
```
prepare_context(profile: "dev")
get_document(path: "reference/ENDPOINT-PERMISSION-GUIDELINES.md")
```

**Step 1d:** For each plugin, load its context:
```
get_plugin_docs(name: "{plugin}")
get_schema(name: "{plugin}")
```

## Phase 2: Audit

For EACH plugin, for EVERY endpoint in `schemas/{plugin}-api.yaml`:

1. Find the current `x-permissions` value
2. Apply the Decision Framework from ENDPOINT-PERMISSION-GUIDELINES.md:
   - Browser-facing? (exception)
   - Pre-auth? (anonymous — Auth only)
   - Only called by other services? (service-to-service `[]`)
   - WebSocket client needs it? If not → `[]`
   - System admin? → `role: admin`
   - Content authoring/live ops? → `role: developer`
   - State-dependent access? → `role: user` + states
   - Always valid for authenticated players? → `role: user`
3. Consider the deep dive's description of who calls each endpoint
4. Consider the map's caller tracing (if map exists)
5. Check Entry/Context/Exit pattern for session-like services

**Per-endpoint output:**
```
#### {path}
- Current: {exact value}
- Should be: {guideline says}
- Status: MATCH | MISMATCH
- Reason: {specific guideline level and rule}
```

**Summary after all plugins:**
```
## Audit Results

| Plugin | Endpoints | Matching | Mismatched |
|--------|----------|----------|------------|
| {name} | {N} | {N} | {N} |

### All Mismatches:
{plugin / path / current → should-be / reason}
```

If zero mismatches → "All compliant." STOP.

## Phase 3: Fix

For each mismatch:

1. **Fix the API schema** (`schemas/{plugin}-api.yaml`): update x-permissions with `edit_file`
2. **Fix the deep dive** (`docs/plugins/{PLUGIN}.md`): if it lists endpoint permissions, update them
3. **Fix the implementation map** (`docs/maps/{PLUGIN}.md`): if the Method Index has a Roles column, update it

## Phase 4: Verify

Re-read each fixed schema and verify:
- [ ] x-permissions matches expected value
- [ ] YAML syntax valid
- [ ] No other content accidentally modified
- [ ] Deep dive and map references match schema (if they were updated)

## Phase 5: Report

```markdown
## Permission Update Complete

### Plugins Audited: {N}
- Total endpoints: {N}
- Already compliant: {N}
- Fixed: {N}

### Changes Made
{per-plugin list of endpoint → old → new}

### Verification: {PASSED/FAILED}

### Next Step
Run code generation for affected plugins:
cd scripts && ./generate-service.sh {service1}
cd scripts && ./generate-service.sh {service2}
```
