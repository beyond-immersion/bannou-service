---
description: "Create or maintain an implementation map for a plugin. Loads all relevant files into context, then writes the map document from full-context analysis. One plugin per invocation."
argument-hint: "[plugin-name] - Plugin to map (e.g., 'auth', 'chat') or omit to pick the next unmapped plugin by layer order"
disable-model-invocation: true
---

# Implementation Map Builder

Creates or maintains implementation maps — pseudocode behavioral specifications for Bannou plugins. This is a FULL-CONTEXT skill: you load all relevant files and write the map yourself. No delegation, no agents.

## Rules

1. **Every endpoint gets a full pseudocode block.** No abbreviating, no "similar to above", no grouping "simple" methods. A `GetEntity` with READ + null check + RETURN gets its own block.
2. **Pseudocode must be verifiable.** Every `READ`, `WRITE`, `CALL`, `PUBLISH`, `LOCK`, `RETURN` corresponds to an actual operation in source code (or deep dive for aspirational plugins). Do not invent operations. Do not omit operations.
3. **Loading is not reading.** Files in your context must be systematically processed. A file sitting unread is no better than unloaded.
4. **Service files are never "too long to check fully."** If you cannot maintain thoroughness across a 3,000-line file, stop and say so. Do not silently skim.
5. **The template is law.** Follow `IMPLEMENTATION-MAP-TEMPLATE.md` exactly.

## What This Produces

A file at `docs/maps/{SERVICE-NAME}.md` with 11 sections:

1. Header (metadata, layer, deep dive link)
2. Summary Table (endpoint count with non-standard breakdown, state stores, events)
3. State (every key pattern and data type)
4. Dependencies (runtime dependencies with layer and type)
5. Events Published (topic, type, trigger)
6. Events Consumed (topic, handler, action)
7. DI Services (constructor deps, collection-injected providers, DI interfaces implemented)
8. Method Index (scannable overview table with source markers)
9. Methods (full pseudocode per endpoint)
10. Background Services (workers, if any)
11. Non-Standard Implementation Patterns (lifecycle, manual controllers, custom overrides)

---

## Phase 1: Selection & Mode

**If specific name provided:** Verify `docs/plugins/{PLUGIN-NAME}.md` exists. If not → list and STOP.

**If no argument:** Pick the next unmapped plugin by layer order:
```bash
comm -23 \
  <(ls docs/plugins/*.md | xargs -I{} basename {} .md | sort) \
  <(ls docs/maps/*.md 2>/dev/null | xargs -I{} basename {} .md | sort)
```
Select first unmapped from layer order (L0 → L1 → L2 → L3 → L4).

**Determine mode:**
- `docs/maps/{PLUGIN_NAME}.md` does NOT exist → **CREATE mode**
- `docs/maps/{PLUGIN_NAME}.md` exists → **MAINTAIN mode**

**Determine maturity:**
- Schema + code exist → **Implemented** (full context)
- Schema exists, code is stubs → **Schema-only**
- Neither → **Aspirational** (deep dive is sole data source)

Derive naming variants: `{service}`, `{Service}`, `{SERVICE}`.

Announce mode, maturity, and proceed.

## Phase 2: Context Load

**Step 2a:** Load plugin context:
```
prepare_context(profile: "plugin", service: "{service}")
```

**Step 2b:** Read the implementation map template:
```
get_document(path: "reference/templates/IMPLEMENTATION-MAP-TEMPLATE.md")
```

**Step 2c:** Read existing map (MAINTAIN mode only):
```
read_file("docs/maps/{PLUGIN_NAME}.md")
```

**Step 2d:** Discover and read plugin source files (skip for aspirational):
```bash
find plugins/lib-{service}/ -name "*.cs" ! -path "*/obj/*" ! -path "*/bin/*" ! -path "*/Generated/*" -exec wc -l {} + 2>/dev/null | sort -rn
ls plugins/lib-{service}/Generated/ 2>/dev/null
```
Read ALL manual `.cs` files. From Generated/, read: `I{Service}Service.cs`, `{Service}Controller.cs`, `{Service}ServiceConfiguration.cs`, `{Service}EventPublisher.cs`, `{Service}PublishedTopics.cs`. Skip `*Controller.Meta.cs` and `*PermissionRegistration.cs`.

**Step 2e:** Read schemas (skip missing files for aspirational):
```bash
ls schemas/{service}-*.yaml 2>/dev/null
```
Read all found. Also `schemas/common-api.yaml`, `schemas/common-events.yaml`.

**Step 2f:** Read bannou-service generated event models (if they exist):
```bash
ls bannou-service/Generated/Events/{Service}*.cs 2>/dev/null
```

**Step 2g:** Model shapes (skip for aspirational):
```
print_models(plugin: "{service}")
```

**Step 2h:** Cross-reference context for consumed events — for each service whose events this plugin consumes:
- Read the producing service's map: `get_plugin_docs(name: "{producer}")` (if available)
- Or the producing service's event schema: `read_file("schemas/{producer}-service-events.yaml")`

**Step 2i:** GitHub issues (MANDATORY for aspirational, optional for implemented):
```bash
gh issue list --search "{service}" --limit 20 --json number,title,state --jq '.[] | "\(.number): [\(.state)] \(.title)"'
```
For the 5-10 most relevant, read full comments: `gh issue view {NUMBER} --comments`

Issue comments frequently contain implementation details — exact signatures, state key patterns, behavioral decisions — that never made it into the deep dive. These are primary source material.

## Phase 3: Analysis

Systematically extract all data from loaded context. Analysis first, writing second.

### 3a: Endpoint Inventory
**Implemented/schema-only:** From API schema + generated interface, list every endpoint: method name, route, HTTP method, roles, request/response models. Note `x-controller-only` and `x-manual-implementation` markers.
**Aspirational:** Derive from deep dive's API Endpoints section.

### 3b: Non-Interface Endpoint Detection
Check for endpoints outside the generated interface:
1. Controller-only endpoints (`x-controller-only`, `x-manual-implementation`)
2. Manually-registered routes in `{Service}ServicePlugin.cs` (`MapPost`, `MapGet`, etc.)
3. Manual partial controller outside Generated/
4. Explicit `IBannouService.` interface implementations

### 3c: Method-by-Method Behavior
**Implemented:** Read EVERY method body. For each, record: state operations (Get/Save/Delete), service calls, event publications, locks, return paths, control flow. Trace into helpers.
**Aspirational:** Derive from deep dive descriptions. Note `// Behavior not specified in deep dive` where ambiguous.

### 3d: State Store & Key Patterns
Extract from constructor (store acquisition) and method bodies (key patterns). Cross-reference with `schemas/state-stores.yaml`.

### 3e: Dependency & Event Profile
List all dependencies with layer classification, events published/consumed, DI provider/listener interfaces, client events.

### 3f: Background Services & Plugin Lifecycle
Check for background workers, timer-based schedulers, non-trivial lifecycle methods.

### 3g: Consumed Event Topic Verification (MANDATORY)
For EVERY consumed topic, verify the exact string against the producing service's authoritative documentation (map → event schema → deep dive, in order). Wrong topic → correct before writing. Non-existent topic → flag as `// Future: not yet published by {producer}`.

### 3h: Dependent Service Call Signature Verification (skip for aspirational)
For each `CALL` in the code, verify request/response fields against the dependency's model shapes:
```
print_models(plugin: "{dependency}")
```
Check: field names exist, required fields set, types match, calling pattern correct (generated clients return `Task<TResponse>` and throw `ApiException` — NOT tuples).

## Phase 4: Write the Map

**USE THE TEMPLATE. FOLLOW IT EXACTLY.**

### CREATE mode:
Use `write_file` to create `docs/maps/{PLUGIN_NAME}.md`. Build each section from analysis.

**Section 9 (Methods) rules:**
- Pseudocode notation ONLY: `READ`, `WRITE`, `ETAG-WRITE`, `QUERY`, `COUNT`, `DELETE`, `CALL`, `PUBLISH`, `PUSH`, `LOCK`, `RETURN`, `IF`/`ELSE`, `FOREACH`
- 2-space indentation for scope
- `//` comments for notes, `->` for early returns, `<-` for data source, `[with ETag]` for ETag reads
- Method header: `### MethodName` then `POST /route | Roles: [roles]`
- Same order as Method Index table
- Omit: logging, CancellationToken, telemetry spans (implied)

**Aspirational:** Add `> **Status**: Aspirational` to header.

### MAINTAIN mode:
Compare each section against analysis. Use `edit_file` for targeted updates. Do NOT rewrite unless 50%+ of sections need changes.

**MAINTAIN mode validation:** After updating, validate against tenets:
- Event topics follow naming conventions
- State key patterns use `Build*Key()` convention
- Dependencies correct direction and hard/soft classification
- Pseudocode follows error handling and return patterns
- Config references use config properties, no sentinel values
- Background services use `DeprecationCleanupHelper` where applicable

## Phase 5: Post-Write Verification (skip for aspirational)

Run grep checks to catch patterns analysis may have missed:
```bash
grep -rn 'MapPost\|MapGet\|MapPut\|MapDelete\|app\.Map' plugins/lib-{service}/ --include='*.cs' | grep -v '/Generated/' | grep -v '/obj/'
grep -rn 'BackgroundService\|IHostedService\|AddHostedService' plugins/lib-{service}/ --include='*.cs' | grep -v '/Generated/' | grep -v '/obj/'
grep -rn 'IBannouService\.' plugins/lib-{service}/ --include='*.cs' | grep -v '/Generated/' | grep -v '/obj/'
find plugins/lib-{service}/ -name '*Controller.cs' -not -path '*/Generated/*' -not -path '*/obj/*'
grep -rn 'IEnumerable<I' plugins/lib-{service}/ --include='*.cs' | grep -v '/Generated/' | grep -v '/obj/'
```

For each hit not already in the map → add via `edit_file`.

## Phase 6: Cross-Reference Deep Dive

1. Check deep dive header for `**Implementation Map**: [docs/maps/{PLUGIN_NAME}.md](../maps/{PLUGIN_NAME}.md)` — add if missing.
2. **CREATE mode only:** Note operational sections that should be removed per the Migration Checklist. Do NOT remove them — that's `/maintain-plugin`'s job. Report:
   ```
   ## Migration Note
   Deep dive contains operational sections now covered by the map.
   Run `/maintain-plugin {plugin}` to migrate.
   ```

## Phase 7: Report

```markdown
## Implementation Map {Created|Updated}: {PLUGIN_NAME}

### Summary
| Field | Value |
|-------|-------|
| Maturity | {Implemented/Schema-only/Aspirational} |
| Endpoints | {N} ({M} generated + {K} non-standard) |
| State key patterns | {N} |
| Events published/consumed | {N}/{N} |
| Background services | {N} |

### Post-Write Verification
Grep hits: {N} | Already in map: {N} | Added: {N}

### Consumed Event Verification
Verified: {N} | Corrected: {N} | Future/aspirational: {N}

### Call Signature Verification
Dependencies checked: {N} | Findings: {N}

### Verification Checklist
- [ ] Every schema endpoint in Method Index
- [ ] Every non-standard endpoint in Method Index with source marker
- [ ] Every endpoint has full pseudocode
- [ ] Every state key in Methods appears in State table
- [ ] Every PUBLISH in Methods appears in Events Published
- [ ] Every consumed topic verified against producer
- [ ] Every CALL verified against dependency models
- [ ] Summary table counts match actual content
```

**STOP. Do not map another plugin.**
