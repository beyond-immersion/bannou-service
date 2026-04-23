---
description: "Determine a plugin's readiness level (L0-L7) in the development pipeline and recommend the next action. Read-only diagnostic — no file modifications."
argument-hint: "[plugin-name] - Plugin to check (e.g., 'divine', 'agency') or omit to find the next plugin needing work"
disable-model-invocation: true
---

# Check Plugin Readiness

Read-only diagnostic. Determines where a plugin stands in the 7-stage pipeline and recommends the next action.

## Readiness Levels

| Level | Name | Gate Criteria |
|-------|------|---------------|
| **L0** | Concept | No deep dive exists |
| **L1** | Designed | Deep dive exists; may have unresolved audit findings or design questions |
| **L2** | Audited | Deep dive has no unresolved critical findings; design questions have issues or are resolved |
| **L3** | Mapped | Implementation map exists with pseudocode for every endpoint |
| **L4** | Schema'd | API schema exists; generated code exists in `plugins/lib-{service}/Generated/` |
| **L5** | Tested | Unit test project exists; tests reference the generated interface; tests compile |
| **L6** | Implemented | `{Service}Service.cs` exists with real implementation (not all NotImplemented) |
| **L7** | Production-Ready | All unit tests pass; map reflects actual behavior; deep dive is current |

## Phase 1: Select Plugin

**If a specific name was provided:**
1. Normalize: `PLUGIN_NAME` (uppercase for docs), `service_name` (lowercase for code)
2. Verify `docs/plugins/{PLUGIN_NAME}.md` exists
3. If not found → list available plugins and STOP

**If no argument:**
Find the plugin most needing attention:
```bash
for f in docs/plugins/*.md; do
  name=$(basename "$f" .md)
  [[ "$name" == "DEEP-DIVE-TEMPLATE" ]] && continue
  lower=$(echo "$name" | tr '[:upper:]' '[:lower:]')
  has_map=$([[ -f "docs/maps/$name.md" ]] && echo "yes" || echo "no")
  has_schema=$([[ -f "schemas/${lower}-api.yaml" ]] && echo "yes" || echo "no")
  has_tests=$([[ -d "plugins/lib-${lower}.tests" ]] && echo "yes" || echo "no")
  echo "$name map=$has_map schema=$has_schema tests=$has_tests"
done
```
Pick the first plugin not at L7, preferring lower readiness levels. Use service hierarchy for tiebreaking (L0 Infrastructure first, then L1, L2, L3, L4).

Announce: "Checking readiness for: **{PLUGIN_NAME}**"

## Phase 2: Load Context

Read the deep dive (needed for L1→L2 audit status check):
```
get_plugin_docs(name: "{plugin-name}")
```

## Phase 3: Sequential Level Assessment

Check each level IN ORDER. Stop at the first level whose gate criteria are NOT met. Verify each check by reading actual files.

---

**L0 → L1: Does the deep dive exist?**
```bash
ls docs/plugins/{PLUGIN_NAME}.md
```
Read the header — confirm it's a real deep dive (has Overview section), not a stub.
If no deep dive → report **L0** and STOP.

---

**L1 → L2: Is the deep dive audited?**

Scan the deep dive (already loaded) for:
1. **Stubs & Unimplemented Features**: items WITHOUT `AUDIT:` markers or `~~strikethrough~~`
2. **Design Considerations**: items WITHOUT `AUDIT:NEEDS_DESIGN` markers or issue links
3. **Bugs**: items WITHOUT `AUDIT:` markers or `~~strikethrough~~`
4. Count `<!-- AUDIT:` markers (shows audit has been run)

- If zero audit markers AND gap sections have items → **L1** (needs audit)
- If critical unresolved items > 0 → **L1** ({N} critical findings)
- If design questions without issues > 0 → **L1** ({N} design questions need issues)
- Otherwise → proceed to L2

---

**L2 → L3: Does the implementation map exist?**
```bash
ls docs/maps/{PLUGIN_NAME}.md
```
If exists, scan for Summary Table and Methods sections with pseudocode blocks.
- If no map → **L2** and STOP
- If map exists but incomplete → **L2** (map incomplete: {details})

---

**L3 → L4: Do schemas and generated code exist?**
```bash
ls schemas/{service_name}-api.yaml
ls plugins/lib-{service_name}/Generated/
```
Check: API schema exists, generated controller exists, generated interface exists.
- If no schema → **L3** and STOP. Recommend: `/schema-plugin {service_name}`
- If schema but no generated code → **L3** (needs code generation)

---

**L4 → L5: Do tests exist?**
```bash
ls plugins/lib-{service_name}.tests/ 2>/dev/null
```
If test project exists: verify at least one real test file exists and tests compile:
```bash
dotnet build plugins/lib-{service_name}.tests/lib-{service_name}.tests.csproj --no-restore > /tmp/check-plugin-build.txt 2>&1
```
- If no test project → **L4** and STOP
- If tests don't compile → **L4** (tests exist but don't compile)

---

**L5 → L6: Does real implementation exist?**

Read `plugins/lib-{service_name}/{ServiceName}Service.cs` — scan method bodies.
- If all methods are NotImplemented/stubbed → **L5** (all endpoints stubbed)
- If some methods have real implementation → proceed to L6

---

**L6 → L7: Is everything current?**

Assess based on available evidence (do NOT run tests — just inspect):
1. Does the implementation map reflect actual behavior?
2. Is the deep dive current? (No stale stubs that have been implemented)
3. Count implemented vs stubbed endpoints

- If implementation appears complete and map exists → **L6**. Note: "Run tests to verify L7"
- If all indicators suggest production readiness → **L7** (verify with test run)

## Phase 4: Report

```markdown
## Plugin Readiness: {PLUGIN_NAME}

### Current Level: L{N} ({Level Name})

**Assessment:** {1-3 sentences}

### Artifacts

| Artifact | Status | Path |
|----------|--------|------|
| Deep Dive | {Exists/Missing} | `docs/plugins/{PLUGIN_NAME}.md` |
| Audit Status | {Clean/N critical/Never run} | — |
| Implementation Map | {Exists/Missing} | `docs/maps/{PLUGIN_NAME}.md` |
| API Schema | {Exists/Missing} | `schemas/{service}-api.yaml` |
| Generated Code | {Exists/Missing} | `plugins/lib-{service}/Generated/` |
| Test Project | {Exists/Missing/Won't compile} | `plugins/lib-{service}.tests/` |
| Implementation | {Real/Stubbed/Partial ({N}/{M})/Missing} | `plugins/lib-{service}/{Service}Service.cs` |

### Recommended Next Action

**Command:** `/{command} {service_name}`
**Why:** {1-2 sentences}

### Pipeline

```
[X] L0 Concept
[X] L1 Designed
[ ] L2 Audited        <-- YOU ARE HERE
[ ] L3 Mapped
[ ] L4 Schema'd
[ ] L5 Tested
[ ] L6 Implemented
[ ] L7 Production-Ready
```
```

### Already-Implemented Services

Many services (Account, Auth, Character, etc.) entered the pipeline at L6. Their path is:
```
L6 (already implemented) → /map-plugin → /audit-plugin → /test-plugin → L7
```

### Aspirational Services

Services with deep dives but no schemas/code (Agency, Disposition, etc.) start at L1/L2. The implementation map specifies intended behavior from the deep dive, not existing code.
