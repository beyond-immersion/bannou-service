---
description: "Maintain a plugin deep dive document. Loads all relevant files, then performs holistic validation — template compliance, tenet design validation with iterative re-validation, three-way consistency (deep dive ↔ map ↔ code), mechanical tenet violation fixes, issue creation for design questions, and work tracking verification."
argument-hint: "[plugin-name|random] - Plugin to maintain (e.g., 'account', 'auth') or 'random' to pick one at random"
disable-model-invocation: true
---

# Maintain Plugin

Holistic validation and documentation quality process for plugin deep dive documents. This workflow's PRIMARY output is the deep dive document, with optional mechanical code fixes and issue creation.

## Rules

1. **This workflow produces edits, not reports.** If you complete without `edit_file` calls to the deep dive, you have failed (unless the document is already perfect — extremely rare).
2. **Tenet violations are NOT "probably intentional."** Document them and let the human decide. You do not filter findings by perceived intent.
3. **Aspirational deep dives still require full design validation.** A plugin that doesn't exist yet can still have a design that violates tenets.
4. **Edit immediately when you find an issue.** Do not batch edits for the end — batching leads to forgotten edits.
5. **Describe ≠ edit.** Use `edit_file`, not prose describing what should change.
6. **Loading is not reading.** Files in your context must be systematically processed during validation. A file sitting unread in context is worthless.
7. **No file is too long to read fully.** If you cannot maintain thoroughness across a 3,000-line service file, stop and say so. Do not silently skim.
8. **No plugin is independent.** Every L4 plugin composes with L2 foundations. Every event publisher has consumers. Read related plugin documentation.
9. **Catalog summaries are for deciding WHAT to read, not substitutes FOR reading.** If a catalog entry is relevant, read the full document.
10. **Code fixes requiring schema changes are NOT mechanical.** Write them as Bugs in the deep dive for `/audit-plugin`.

## Marker Rules

### AUDIT Markers (preserve unless linked GH issue is closed)
```markdown
<!-- AUDIT:IN_PROGRESS:... -->
<!-- AUDIT:NEEDS_DESIGN:...:https://... -->
<!-- AUDIT:BLOCKED:... -->
```

### Strikethrough Items (require verification)
```markdown
N. ~~**Title**~~: **FIXED** (YYYY-MM-DD) - Description.
N. ~~**Title**~~: **IMPLEMENTED** (YYYY-MM-DD) - Description.
N. ~~**Title**~~: **MOVED TO QUIRKS** (YYYY-MM-DD) - Description.
```

**RESOLVED design considerations are NOT strikethrough items.** They contain engineering specifications (sizing, data structure choices, performance analysis) needed for implementation. Never delete a RESOLVED design consideration. Phase 5d's CONFIRMED/NOT CONFIRMED logic applies ONLY to FIXED/IMPLEMENTED/MOVED items in Bugs and Stubs.

---

## Phase 0: Plugin Selection

**If argument is "random" or no argument:**
```bash
ls docs/plugins/*.md | grep -v DEEP-DIVE-TEMPLATE | shuf -n 1
```
Announce: "Maintaining: {plugin-name}" and proceed.

**If specific plugin name:** Verify `docs/plugins/{PLUGIN}.md` exists. If not, list available and STOP.

Derive naming variants:
- `{service}` — lowercase, potentially hyphenated
- `{Service}` — PascalCase (confirm via `ls plugins/lib-{service}/Generated/`)
- `{SERVICE}` — UPPERCASE with hyphens

## Phase 1: Context Load

**Step 1a:** Load plugin context (tenets, patterns, deep dive, implementation map):
```
prepare_context(profile: "plugin", service: "{service}")
```

**Step 1b:** Discover and read ALL plugin source files:
```bash
find plugins/lib-{service}/ -name "*.cs" ! -path "*/obj/*" ! -path "*/bin/*" ! -path "*/Generated/*" -exec wc -l {} + 2>/dev/null | sort -rn
```
Read every manual `.cs` file found. Also read selectively from Generated/:
- `I{Service}Service.cs` (interface — essential)
- `{Service}Controller.cs` (routing — essential)
- `{Service}ServiceConfiguration.cs`, `{Service}EventPublisher.cs`, `{Service}PublishedTopics.cs`, `{Service}ReferenceTracking.cs`, `{Service}CompressionCallbacks.cs`, `{Service}ClientEventsModels.cs` (if they exist)
- **Skip**: `*Controller.Meta.cs` (~79K tokens wasted), `*PermissionRegistration.cs` (~5K wasted)

**Step 1c:** Read test files:
```bash
find plugins/lib-{service}.tests/ -name "*.cs" ! -path "*/obj/*" ! -path "*/bin/*" -exec wc -l {} + 2>/dev/null | sort -rn
```
Read every test file (skip files under 10 lines — boilerplate).

**Step 1d:** Read schemas:
```bash
ls schemas/{service}-*.yaml 2>/dev/null
```
Read all schema files found. Also read `schemas/common-api.yaml` and `schemas/common-events.yaml`.

**Step 1e:** Read bannou-service generated context (if they exist):
- `bannou-service/Generated/Events/{Service}EventsModels.cs`
- `bannou-service/Generated/Events/{Service}LifecycleEvents.cs`

**Step 1f:** Read the deep dive template:
```
get_document(path: "reference/templates/DEEP-DIVE-TEMPLATE.md")
```

**Step 1g:** Ecosystem context — load document catalogs and identify relevant docs:
```
list_documents()
```
From the catalogs, identify up to 5 documents (planning/guides/FAQs) relevant to this plugin. Read each in full via `get_document`.

**Step 1h:** Related plugin documentation — for the 3-5 most directly related plugins (from Dependencies/Dependents):
```
get_plugin_docs(name: "{related-plugin}")
```

**Step 1i:** GitHub issues:
```bash
gh issue list --search "{plugin-name}" --limit 20 --json number,title,state --jq '.[] | "\(.number): [\(.state)] \(.title)"'
```
Read the 3-5 most relevant issues with full comments: `gh issue view {NUMBER} --comments`

**Step 1j:** Model shapes:
```
print_models(plugin: "{service}")
```
Also load shapes for key dependencies (3-5 services from constructor's `I*Client` injections):
```
print_models(plugin: "{dependency}")
```

## Phase 2: Structural Compliance

Walk through the deep dive against DEEP-DIVE-TEMPLATE.md. **Fix every issue immediately.**

### Header Check
- Title: `# {Name} Plugin Deep Dive` (not "Service Deep Dive")
- Uses `**Plugin**:` not `**Service**:`
- Has `**Layer**:`, `**Short**:` (last line of header blockquote)
- No dependency fields in header
- Aspirational plugins have `**Status**:` field
- If map exists, has `**Implementation Map**:` link

### Required Sections (when map exists)
1. Header → 2. Overview → 3. Visual Aid → 4. Stubs & Unimplemented Features → 5. Potential Extensions → 6. Known Quirks & Caveats (Bug/Intentional/Design sub-sections) → 7. Work Tracking

If no map exists, operational sections are also expected: Dependencies, State Storage, Events, Configuration, DI Services, API Endpoints.

### Implementation Map Migration (when map exists)
Per DEEP-DIVE-TEMPLATE rule 6, these sections move to the map: Dependencies, State Storage, Events, DI Services, API Endpoints. Before removing: move visual aids to Visual Aid section, architectural context to Overview, quirks to Known Quirks.

### Non-Template Sections
- **Implementation Plan** → remove when map exists
- **Prerequisites** → remove when map exists
- **Issues to Create** → evaluate: still valid → Work Tracking; redundant → remove

**Scope gate**: If Phase 2 requires 10+ edits, complete what you can and note what remains for a follow-up invocation.

## Phase 3: Design Validation Against Tenets

Systematically validate the deep dive's DESIGN against the tenets. Cross-reference design descriptions against loaded code — if the deep dive describes a pattern the code doesn't use, or vice versa, that is a finding.

**Walk through each section:**

| Section | Validate Against | What to Check |
|---|---|---|
| Overview | Foundation Tenets | Correct scope for layer? Prohibited patterns described? Constructor deps match? |
| Overview | Implementation-Data — Config-First | Hardcoded tunables that should be config? Magic numbers in code? |
| Overview | Helpers & Common Patterns | Custom solutions where established helpers exist? |
| Stubs | Foundation Tenets | Would implementing as described violate infrastructure/event/communication rules? |
| Stubs | Implementation-Behavior — Deprecation | Is category (A vs B) correct? |
| Stubs | Implementation-Data — Type Safety | String-typed enums, GUIDs, or sentinel values proposed? |
| Extensions | Foundation Tenets | Layer hierarchy, communication discipline, cleanup patterns respected? |
| Extensions | Foundation — No Metadata Bags | Convention-based cross-service data sharing proposed? |
| Bugs | All + loaded code | Bugs correctly identified? Code-level issues visible that deep dive doesn't mention? |
| Intentional Quirks | All | Any actually tenet violations misclassified as intentional? |
| Design Considerations | All | Trade-offs correctly framed? Proposed solutions violate tenets? |
| Dependency descriptions | Service Hierarchy | Direction valid? Hard/soft correct? Cross-reference with related plugin docs. |
| Events | Quality — Naming | Topic naming Pattern A or C? Consumed topics match what producers publish? |
| Configuration | Implementation-Data — Config-First | Every tunable has a config property? Proper env var naming? No dead config? |

**Classification rule — apply mechanically:**
- Single tenet unambiguously prescribes fix → **Bug**
- Failing test or described functionality not performed → **Bug**
- Two tenet rules genuinely conflict → **Design Consideration**
- Tenets are silent, but design trade-off exists → **Design Consideration**
- Scaling concern with no prescribed solution → **Design Consideration**
- Purely additive feature not required by core functionality → **Potential Extension**

**Always use tenet CATEGORY names** (Foundation Tenets, Implementation Tenets, Quality Tenets, Service Hierarchy), never specific numbers.

### Phase 3a: Re-Validation Loop

After the validation pass, re-read the deep dive. For every correction, check for cascading issues:
- Did the fix introduce a new dependency? → check layer hierarchy
- Did it add an event subscription? → check topic name, verify producer publishes it
- Did it add configuration? → check env var naming, defaults, constraints
- Did it change deprecation category? → verify all required patterns for new category
- Did it propose a new DI interface? → check Provider/Listener patterns

If re-validation finds new issues, fix and re-pass. Continue until a pass finds zero new issues (typically 2-3 passes).

### Phase 3b: Mechanical Code Fixes

For each Bug identified, assess if you can fix it directly. **A fix is mechanical if ALL true:**
- Prescribed by a specific tenet with zero ambiguity
- ≤3 lines of code in a single file
- Does NOT change method signatures, parameters, or APIs
- Does NOT require schema changes or code regeneration
- Does NOT require adding new dependencies or state stores

**Examples:** adding missing `StartActivity` span, changing `Task.FromResult` to `async`, replacing `JsonSerializer` with `BannouJson`, fixing `private static` to `internal static` on key builder.

**NOT mechanical:** adding methods, schema changes, multi-file refactoring, anything requiring a build to verify. Write as Bug for `/audit-plugin`.

For each mechanical fix: edit the code, then update the Bug to strikethrough FIXED format.

## Phase 4: Three-Way Consistency (Deep Dive ↔ Map ↔ Code)

### 4a: Deep Dive ↔ Code
Walk the deep dive's claims against actual code:
- Does Overview describe capabilities the code confirms? Omit capabilities the code has?
- Are there background services, helpers, providers in code not mentioned?
- Do described patterns match what code actually does?

Skip for aspirational plugins with no code.

### 4b: Deep Dive ↔ Map
**Skip if no map exists.**

**Map issues are NOT deep dive issues.** When you find map-vs-code discrepancies (wrong topic, outdated handler, missing method), note them in a running list for the report. Do not add them to the deep dive's Bugs section.

Check: Does Overview match map's capabilities? Are Stubs still stubs or has the map/code resolved them? Do map CALL signatures match actual generated client models (verify via `print_models`)?

### 4c: Reconcile Audit Findings
For each open finding, check actual code:
- Code implements required behavior → mark `**FIXED** ({date}) - Verified in source code`
- Only map specifies it, code doesn't → leave as-is
- Genuine design question → leave as-is

### 4d: Verify Strikethrough Items
**Scope: ONLY FIXED/IMPLEMENTED/MOVED items in Bugs and Stubs. NOT RESOLVED design considerations.**

For every strikethrough item, check the code:
- **CONFIRMED** (code shows the fix) → delete the line. If fix introduced non-obvious behavior, add to Intentional Quirks.
- **NOT CONFIRMED** (code doesn't show it) → restore as active bug/stub
- **CANNOT DETERMINE** (aspirational, requires runtime) → leave as-is, note in Work Tracking

Renumber remaining items after deletions.

## Phase 5: Issue Management

### 5a: Issue Search
Review issues found in Phase 1i. For open issues:
- Bug/gap existing in code → note for Phase 3 (if not already found)
- Settled decision contradicting deep dive → edit deep dive
- Constraint deep dive doesn't account for → add to Design Considerations

### 5b: Linked Issue Verification
Collect all issue numbers from the deep dive. For 1-5 issues, verify directly:
```bash
gh issue view {NUMBER} --json state,stateReason,comments
```

**Disposition table:**

| Disposition | Criteria | Deep Dive Action |
|---|---|---|
| STILL_ACTIVE | Open and relevant | Keep item and marker |
| COMPLETED | Closed with fix confirmed in code | Delete item and marker |
| REJECTED | Closed as not-planned/wrong direction | Delete item and marker |
| DEFERRED | Closed but deprioritized | Keep text, remove marker |

### 5c: Issue Creation for Design Questions
For Design Considerations without existing issues, evaluate whether an issue would help (blocks implementation or needs human decision). If warranted:
1. Create via `gh issue create` with context, open question, options
2. Add `<!-- AUDIT:NEEDS_DESIGN:{DATE}:{ISSUE_URL} -->` to the deep dive

Clean up Work Tracking: delete completed entries, remove historical clutter, leave section header.

## Phase 6: Report

```markdown
## Maintenance Complete: {Plugin Name}

### Summary
- Validation passes: {count}
- Edit calls made: {count}
- Tenet violations found: {count}
- Mechanical code fixes: {count}
- Code accuracy issues: {count}
- Map inconsistencies: {count} (or "No map exists")
- Issues verified: {count}
- Issues created: {count}
- Strikethrough items processed: {count}

### Edits Made
| # | Target | Section/File | Change |
|---|--------|-------------|--------|
| 1 | Deep dive | {section} | {what changed} |

### Tenet Violations Added
{List new bugs with tenet category reference}

### Mechanical Code Fixes
{List code changes with file, before/after, tenet category}

### Design Considerations Added
{List with justification for why NOT a tenet violation}

### Ecosystem Findings
{Constraints or contradictions from related plugins, planning docs, GH issues}

### Map Follow-Up
{If map inconsistencies found: list each, recommend `/map-plugin {plugin}` in maintain mode}

### Document Status
- Template compliance: {Full/Partial}
- Strikethrough items remaining: **0**
- Ready for /audit-plugin: {Yes/No}
```
