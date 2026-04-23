---
description: "Audit a plugin's deep dive for implementation gaps. Investigates ONE gap per invocation — reads actual code, then either executes the fix or creates an issue for human review. Run repeatedly to work through all gaps."
argument-hint: "[plugin-name|random] - Plugin to audit (e.g., 'account', 'auth') or 'random'/omit for random selection"
disable-model-invocation: true
---

# Audit Plugin

Each invocation investigates and resolves exactly ONE gap. Run repeatedly to work through all gaps.

## The Deep Dive IS The Tracking System

Items in a deep dive are NOT resolved by being documented. The documentation TRACKS what needs work:

- **Stubs & Unimplemented Features** = incomplete code → gaps
- **Potential Extensions** = missing features → gaps
- **Design Considerations** = architectural questions → gaps
- **Bugs** = broken behavior → gaps

If an item lacks an `AUDIT:` marker, it is actionable.

## Rules

1. **ONE gap per invocation.** Investigate, resolve, STOP. Run the skill again for the next gap.
2. **Your only two outputs are EXECUTE or CREATE_ISSUE.** "No action needed," "acceptable as-is," "low priority," and "can be deferred" are NOT valid outputs. If something is truly working as intended, it shouldn't be in a gap section — cleaning up the doc IS an EXECUTE action.
3. **Phase 3 (Investigation) is mandatory for every gap.** You MUST read actual source code. "This seems like it would need X" without having read the code means you have not investigated.
4. **CREATE_ISSUE is for genuine design questions only** — situations where a human must decide WHAT the system should do. It is NOT a fallback for unfamiliarity, complexity, or reluctance. If the question is only about HOW to wire up existing abstractions, that's EXECUTE.
5. **"Documented" does not mean "resolved."** Being listed in the deep dive means it needs work, not that work is done. "It's a known limitation" means it IS a gap, by definition.
6. **Use abstractions without understanding their internals.** If an interface accepts your data and returns what you need, call it. "I don't know how IEntitySessionRegistry works behind the scenes" is not a design question — it's confusing ignorance of an implementation detail with a missing decision. DI handles the wiring. Zero providers registered = graceful degradation = correct behavior.

## Marker Formats

### In-Progress (HTML comments)
```markdown
<!-- AUDIT:IN_PROGRESS:YYYY-MM-DD -->
<!-- AUDIT:NEEDS_DESIGN:YYYY-MM-DD:ISSUE_URL -->
<!-- AUDIT:BLOCKED:YYYY-MM-DD -->
```

### Completed (inline strikethrough)
```markdown
N. ~~**Original title**~~: **FIXED** (YYYY-MM-DD) - Brief description of fix and any quirks.
```

`/maintain-plugin` later verifies these and either removes them or moves quirks to Intentional Quirks.

---

## Phase 1: Context & Selection

**Step 1a:** Load plugin context:
```
prepare_context(profile: "plugin", service: "{plugin-name}")
```

This loads all tenets, helpers, the deep dive, and implementation map.

**Step 1b:** Select plugin:
- If `random` or no argument: `ls docs/plugins/*.md | grep -v DEEP-DIVE-TEMPLATE | shuf -n 1`
- If specific name: verify `docs/plugins/{PLUGIN}.md` exists; if not, list available and STOP

**Step 1c:** Read the plugin's source code:
```bash
find plugins/lib-{service}/ -name "*.cs" ! -path "*/obj/*" ! -path "*/bin/*" ! -path "*/Generated/*" -exec wc -l {} + 2>/dev/null | sort -rn
```
Read ALL manual `.cs` files found. Also read:
- `plugins/lib-{service}/Generated/I{Service}Service.cs` (interface)
- `plugins/lib-{service}/Generated/{Service}Controller.cs` (error boundary)

**Step 1d:** Read schemas:
```bash
ls schemas/{service}-*.yaml 2>/dev/null
```
Read all schema files found.

## Phase 2: Gap Scan

Scan the deep dive for ALL gap sections. Use judgment on section names — any section describing incomplete, missing, stub, placeholder, or future work contains gaps.

**Must scan:**
- Stubs & Unimplemented Features
- Potential Extensions
- Bugs (Fix Immediately)
- Design Considerations (Requires Planning)
- Any `- [ ]` checkboxes, `FIXME:`, `TODO:` markers

**For each item:** Check for `AUDIT:` markers.
- `AUDIT:IN_PROGRESS` → skip (someone is working on it)
- `AUDIT:NEEDS_DESIGN` with issue URL → skip (already investigated)
- `AUDIT:BLOCKED` → note but skip
- No marker → actionable

**Report the scan:**
```
## Gap Scan: {Plugin Name}

Sections scanned: {list each with item count}
Total gaps: {N}
Already marked: {N}
Actionable (no marker): {N}

First actionable gap: {description} (section: {section})
```

If zero actionable gaps → report "No actionable gaps" and STOP.

### Pre-Implementation Gate

If the deep dive's Status is "Aspirational" or the plugin has no schema/code, the plugin is **pre-implementation**. Valid outcomes are limited to:

1. **FIX DOCUMENTATION** — edit the deep dive to fix doc issues (missing sections, incorrect tenet references, template non-compliance)
2. **CREATE_ISSUE** — genuine design question requiring human judgment
3. **SKIP** — gap is a planned implementation phase, correctly documented, no defects

You do NOT create schemas, write code, or perform implementation work for pre-implementation plugins. That's the `/check-plugin` → `/implement-plugin` pipeline.

## Phase 3: Investigation

**This phase is mandatory. You cannot skip it.**

Announce the gap and investigate:

1. **Code Location** — find the specific methods/classes involved. Read actual source files.
2. **Integration Points** — what services, events, state stores, clients are involved?
3. **TENET Compliance** — does the fix require schema changes? Will it use infrastructure libs correctly? Are proper events defined?
4. **Scope** — single-file or multi-file? Test implications? Doc updates beyond deep dive?
5. **Open Questions** — list ANY uncertainties

**Investigation output must include proof of code reading:**
```
## Investigation: {Gap Title}

**Primary file(s):** {actual paths you read}
**Methods involved:** {actual method names from code}
**Current implementation:** {what the code currently does}
**What's missing:** {specific description}

### Proof of Code Reading
{Quote 2-3 relevant lines from actual source code}

### Integration Points
Services: {list} | Events: {list} | State stores: {list}

### TENET Compliance
Schema changes: {Y/N} | Infrastructure libs: {OK/needs work} | Events: {exist/need creation}

### Open Questions
{Numbered list, or "None — implementation path is clear"}
```

**If your investigation lacks specific file paths and method names, you have not completed it. Go back and read the code.**

## Phase 4: Determination

**EXECUTE** if ALL true:
- No open questions remain
- All TENET requirements are clear
- Implementation path is unambiguous
- No human design decisions needed

**CREATE_ISSUE** if ANY true:
- Open questions requiring human judgment
- Multiple valid architectural approaches
- Integration with unimplemented services needed
- Empirical validation required (profiling, load testing) that you cannot perform

**These are NOT valid reasons for CREATE_ISSUE:**
- "I don't know how the interface works internally" → read the contract, call it
- "I don't know who provides the other side" → DI handles it; zero providers = graceful degradation
- "Multiple services might need to coordinate" → check if an abstraction already handles it (IEntitySessionRegistry, ICollectionUnlockListener, IVariableProviderFactory)
- "I'm not sure this works for all cases" → if the interface is generic, it handles all cases

**The litmus test:** "Is there a decision only a human can make about WHAT the system should do?" If the question is only about HOW to wire up existing abstractions → EXECUTE.

**Documentation cleanup as EXECUTE:** Only when the gap is **factually incorrect** — the code already does what the gap says it doesn't, and you can cite the specific method/line proving it. "The code works correctly today" does NOT make a performance/scale concern factually incorrect.

## Phase 5a: Execute Path

1. **Mark the document:** Add `<!-- AUDIT:IN_PROGRESS:YYYY-MM-DD -->` after the gap
2. **Structural change gate:** If the fix adds methods, files, events, state stores, or dependencies → update `docs/maps/{SERVICE}.md` BEFORE writing code (map-first design)
3. **Implement the fix:** Schema changes first if needed → run generation → edit service files → follow all TENETS
4. **Build verification** (only if code changed):
   ```bash
   dotnet build plugins/lib-{service}/lib-{service}.csproj --no-restore > /tmp/{service}-build.txt 2>&1
   ```
5. **Update deep dive:** Remove `AUDIT:IN_PROGRESS` marker, apply FIXED format:
   ```markdown
   N. ~~**Original title**~~: **FIXED** (YYYY-MM-DD) - Brief description.
   ```
6. **Close related GH issues** if the fix fully resolves them:
   ```bash
   gh issue view {number} --json state,comments,body
   ```
   Auto-close if: implementation confirmed, no unresolved work in comments. Flag to user if: comments reference additional work or broader scope.

## Phase 5b: Create Issue Path

1. **Mark the document:** Add `<!-- AUDIT:NEEDS_DESIGN:YYYY-MM-DD:ISSUE_URL_PLACEHOLDER -->`
2. **Create issue** via `gh issue create` with: source file, investigation findings, TENET considerations, numbered open questions, recommended next steps
3. **Update marker** with actual issue URL

## Phase 6: Report

```markdown
## Audit Complete: {Plugin Name}

**Gap:** {title}
**Path:** {EXECUTE|CREATE_ISSUE}
**Result:** {outcome summary}
**Gaps remaining:** {N}

Run `/audit-plugin {plugin-name}` for the next gap.
```

**STOP. Do not process another gap.**
