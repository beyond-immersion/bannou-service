---
description: "Add a feature to existing, already-implemented components through a 5-phase pipeline (deep dive, map, schema, code, tests). Handles single-plugin and cross-cutting features spanning common infrastructure and multiple plugins."
argument-hint: "[description or issue number] - Natural language description of the feature, or a GitHub issue number (e.g., '545')"
disable-model-invocation: true
---

# Implement Feature

Adds a **specific feature** to one or more **already-implemented** components through a full lifecycle: deep dive → map → schema → code → tests. Each component gets one complete 5-phase cycle.

**This is NOT `/implement-plugin`** (which implements from scratch). This assumes targets already have working code, passing tests, and current documentation.

## Prerequisites

Each target component must be at **L6 (Implemented)** or higher.

**Plugins:** deep dive, map, schemas, generated code, implementation (non-stub), test project.
**bannou-service/common:** deep dive, schemas, test project.

If ANY prerequisite missing → STOP and recommend `/check-plugin {service}`.

## Rules

1. **Deep dive before map. Map before schema. Schema before code. Code before tests.** The 5 phases are sequential because each produces the specification for the next.
2. **Surprise = decision checkpoint.** Unexpected build failure, design conflict, tenet violation → stop and present to user. Do not work around surprises.
3. **Capture Pattern is mandatory** for all new state saves and event publications in tests.
4. **Common changes first.** Common schemas generate into every service. If the common change doesn't build, nothing builds.

---

## Phase 0: Parse, Load Context, Decompose

### Step 1: Parse the Feature Request
Extract: all affected components, feature summary, scope per component, dependency order.

If input is a GitHub issue number: `gh issue view {number} --comments`

### Step 2: Verify Prerequisites
For each plugin: verify all 6 prerequisites. For bannou-service: verify deep dive, schemas, test project.

### Step 3: Check GitHub Issues
```bash
gh issue list --search "{feature keywords}" --limit 20
```
Read each related issue IN FULL including ALL comments.

### Step 4: Load Context

**For each plugin involved:**
```
prepare_context(profile: "plugin", service: "{service}")
```

Read all manual `.cs` files, generated interface, generated controller, generated config, schemas, test files:
```bash
find plugins/lib-{service}/ -name "*.cs" ! -path "*/obj/*" ! -path "*/bin/*" ! -path "*/Generated/*" -exec wc -l {} + 2>/dev/null | sort -rn
find plugins/lib-{service}.tests/ -name "*.cs" ! -path "*/obj/*" ! -path "*/bin/*" -exec wc -l {} + 2>/dev/null | sort -rn
```
Read all found files. Also read `I{Service}Service.cs`, `{Service}Controller.cs`, `{Service}ServiceConfiguration.cs` from Generated/.

**For bannou-service/common** (if applicable):
Read `docs/BANNOU-DEEP-DIVE.md`, common schema files, relevant source files.

### Step 4.5: Design Fidelity Checkpoint (MANDATORY)

Before decomposing tasks or writing code, verify your implementation plan matches what the planning docs prescribe. **This step exists because of Incident #19 (`docs/reference/INCIDENT-HISTORY.md`): an agent shipped a reflection-based `DirectDispatchHelper` when `docs/planning/BANNOU-EMBEDDED.md § Section 3` prescribed statically-typed delegate dispatch per generated client method. Functionally equivalent, structurally opposite, AOT-hostile. The divergence was never flagged.**

Per QUALITY TENETS (T33 Design Specification Fidelity), when a planning doc prescribes a technical approach, the implementation MUST match it STRUCTURALLY — not just functionally. Divergence must be flagged to the user BEFORE coding begins.

**Protocol:**

1. **Identify the planning doc(s)** prescribing this feature's technical approach. For a feature like "add X to service Y," check:
   - `docs/planning/*.md` whose title or Summary matches feature keywords
   - The plugin's deep dive Design Considerations + implementation map
   - Any doc linked from the GH issue body or comments
   - `search_docs(query: "{feature keywords}")` when the doc isn't obvious

2. **Read each planning doc in full** via `get_document`. Summaries are not sufficient.

3. **Write the plan in 3-5 bullets.** For each bullet, quote the specific planning-doc line that prescribes that shape. If no line prescribes it, mark `NO PRESCRIPTION`.

4. **Inconvenience is a signal, not a license.** If a prescribed approach requires template changes, source generators, or generic delegates the existing infrastructure doesn't make ergonomic, flag it. Do not silently substitute an easier approach.

5. **If any bullet has `NO PRESCRIPTION` or is `FLAGGED`**, STOP and present to the user. Do not proceed to Step 5 until the user has given direction.

**Output format before continuing:**

```
## Design Fidelity Check

**Planning docs consulted:** {paths}

**Implementation plan (per affected component):**
1. {bullet} — prescribed: "{quoted line from {doc}}"
2. {bullet} — NO PRESCRIPTION — design choice because {reason}
3. {bullet} — prescribed but INCONVENIENT: {doc} says X, but doing so requires {Y}. Flagging.

**Divergences flagged to user:** {list or "none"}
```

### Step 5: Decompose into Tasks

Create tasks via `TaskCreate` in dependency order:
1. Common/bannou-service task first (if applicable) — blocks plugin tasks
2. Plugin tasks — one per plugin, dependency-ordered (lower layers first)

Each task description includes: what this component contributes, the 5 phases with concrete file paths, what schemas/code/tests change.

Present the task list. Set first task to `in_progress`.

---

## The 5 Phases (per component)

### Phase 1: Deep Dive Update

Update the component's deep dive to reflect the feature at a HIGH LEVEL (WHAT and WHY, not HOW).

Update as applicable: Overview, Background Services, Events, Configuration, Dependencies, Quirks.

Do NOT add pseudocode (that's the map). Do NOT modify unrelated sections.

### Phase 2: Implementation Map Update

Update with detailed pseudocode for new behavior. **Skip** (with "Phase 2: N/A") for type-definition-only changes.

Update: Summary Table, State, Dependencies, Events, DI Services, Method Index, Methods (full pseudocode), Background Services.

Use ONLY standard notation: `READ`, `WRITE`, `CALL`, `PUBLISH`, `LOCK`, `RETURN`, `IF`/`ELSE`/`FOREACH`.

After updating, verify: every new method has pseudocode, summary counts match, new events appear in both Events Published AND publishing methods, new state keys appear in both State and methods.

### Phase 3: Schema Changes & Regeneration

Based on updated map:
1. API schema — new endpoints, models, enums
2. Event schemas — new events, `x-event-publications`, `topic-params`
3. Configuration schema — new properties with `description`, `type`, `default`, `env`
4. Client events — if specified
5. State stores — new entries if needed

**Generation** — use most granular script per CLAUDE.md Generation Script Selection Guide:
- Common schema changes → `scripts/generate-all-services.sh`
- Plugin-only → `cd scripts && ./generate-service.sh {service}` (or more granular)

Build after: `dotnet build {project} --no-restore > /tmp/{component}-build.txt 2>&1`

### Phase 4: Implementation

Write code following the updated map. All standard implementation rules apply (constructor-cached stores, generated publishers, proper types, no sentinels, no top-level try-catch, StartActivity on helpers).

Build after.

### Phase 5: Testing

Add unit tests for new functionality. Capture Pattern mandatory. Test naming: `MethodName_Condition_ExpectedResult`. Match existing test patterns in the project.

```bash
dotnet build {test_project} --no-restore > /tmp/{component}-test-build.txt 2>&1
dotnet test {test_project} --no-restore > /tmp/{component}-test-run.txt 2>&1
```

After all 5 phases → `TaskUpdate(taskId: "{id}", status: "completed")` → next task.

---

## Final Report

```markdown
## Feature Implementation Complete

**Feature**: {summary}
**Related Issues**: {numbers or "None"}
**Components**: {count}

### Task Results
| # | Component | Status | Summary |
|---|-----------|--------|---------|
| 1 | {component} | PASS | {what was done} |

### Files Modified
{list ALL files across ALL components}

### Build: {per-component status}
### Tests: {per-component status}

### Recommended Follow-Up
- [ ] `git diff` to review
- [ ] `make format` before committing
- [ ] Close GitHub issue #{N} if applicable
```
