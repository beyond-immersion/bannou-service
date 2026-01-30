---
description: Audit plugin deep dive documents for implementation gaps. Investigates gaps thoroughly and either executes fixes or creates GitHub issues.
argument-hint: "[plugin-name|random] - Plugin to audit (e.g., 'account', 'auth') or 'random'/omit to pick one at random"
---

# Plugin Auditor Command

You are executing the `/audit-plugin` workflow. This is a structured, predictable process for finding and addressing implementation gaps in Bannou plugin documentation.

## ⛔ FORBIDDEN ESCAPE HATCHES ⛔

**You MUST NOT use any of these rationalizations to avoid work:**

1. **"It's documented, so it's handled"** - FALSE. The deep dive IS the tracking document. Being listed there means it NEEDS work, not that work is done.

2. **"It's low priority"** - NOT YOUR CALL. You do not filter gaps by priority. You investigate and either EXECUTE or CREATE_ISSUE. The human decides priority.

3. **"It's a known limitation"** - STILL A GAP. Known limitations are implementation gaps by definition. Investigate them.

4. **"It's in Stubs/Potential Extensions, not Bugs"** - ALL OF THESE ARE GAPS. Stubs describe incomplete implementations. Potential Extensions describe missing features. Both are work that needs investigation.

5. **"The section name doesn't match the patterns exactly"** - USE YOUR BRAIN. Any section describing incomplete, missing, placeholder, stub, mock, or future work contains gaps.

6. **"It seems acceptable as-is"** - THERE IS NO "ACCEPTABLE" OPTION. Your only two outputs are EXECUTE or CREATE_ISSUE. If you think something is acceptable, that's a CREATE_ISSUE with "Verify this is intentional" as the open question.

7. **"I'll skip Phase 4 since the gap is obvious"** - NEVER SKIP PHASES. Phase 4 Investigation is mandatory for EVERY gap. You cannot make EXECUTE vs CREATE_ISSUE determination without reading the actual code.

## The Deep Dive Document IS The Tracking System

**Critical understanding:** Items in a deep dive document are NOT resolved by virtue of being documented. The documentation TRACKS what needs to be done:

- **Stubs & Unimplemented Features** = Code that is incomplete → Gaps
- **Potential Extensions** = Features that don't exist yet → Gaps
- **Design Considerations** = Architectural questions → Gaps
- **Bugs** = Broken behavior → Gaps
- **Known Issues / TODO** = Acknowledged problems → Gaps

If an item is listed in ANY of these sections without an `AUDIT:` marker, it is an actionable gap that requires investigation.

## Marker Format

Implementation gaps in deep dive documents use this marker format:

```markdown
<!-- AUDIT:STATUS:DATE:ISSUE_URL -->
```

Where:
- `STATUS` is one of: `IN_PROGRESS`, `BLOCKED`, `NEEDS_DESIGN`
- `DATE` is ISO format (YYYY-MM-DD)
- `ISSUE_URL` is the GitHub issue link (required for NEEDS_DESIGN, optional for others)

**Examples:**
```markdown
<!-- AUDIT:IN_PROGRESS:2026-01-29 -->
<!-- AUDIT:NEEDS_DESIGN:2026-01-28:https://github.com/beyond-immersion/bannou-service/issues/42 -->
```

## Workflow Phases

Execute these phases in strict order. Do not skip phases.

### Phase 1: Plugin Selection

**If argument is "random" or no argument provided:**
Use bash for true randomness:
```bash
ls docs/plugins/*.md | grep -v DEEP_DIVE_TEMPLATE | shuf -n 1
```
Extract the plugin name from the result and announce: "Auditing: {plugin-name}" then proceed immediately.

**If specific plugin name provided:**
1. Verify `docs/plugins/{PLUGIN}.md` exists
2. If not found, list available plugins and ask user to choose
3. Once valid, proceed immediately

### Phase 2: Gap Scan

Scan the selected plugin's document for ALL of these section patterns. **Do not skip sections because the name doesn't match exactly - use judgment.**

**Explicit gap sections (MUST scan):**
- `## Stubs & Unimplemented Features` or `### Stubs & Unimplemented Features` - **THESE ARE GAPS**
- `## Potential Extensions` or `### Potential Extensions` - **THESE ARE GAPS**
- `## Implementation Gaps` or `### Implementation Gaps`
- `## Known Issues` or `### Known Issues`
- `## TODO` or `### TODO`
- `## Bugs` or `### Bugs`
- `#### Bugs (Fix Immediately)` (from Known Quirks section)
- `#### Design Considerations (Requires Planning)` (from Known Quirks section)

**Inline markers (MUST scan):**
- Lines starting with `- [ ]` (unchecked checkboxes)
- Lines containing `FIXME:` or `TODO:`

**Additional patterns to look for:**
- Any section mentioning "stub", "mock", "placeholder", "incomplete", "unimplemented"
- Any section mentioning "future", "planned", "not yet", "missing"
- Any numbered/bulleted list of things that don't work yet

**IMPORTANT:** Every item in "Stubs & Unimplemented Features" and "Potential Extensions" is a gap. Do not skip them because they're "just documentation" - they ARE the gaps you're looking for.

For each gap found, check:
1. **Is it marked with an AUDIT marker?**
   - If `AUDIT:IN_PROGRESS` - Skip (someone is working on it)
   - If `AUDIT:NEEDS_DESIGN` with issue URL - Skip (already investigated, needs design decisions)
   - If `AUDIT:BLOCKED` - Note but skip
   - If no marker - This is a candidate for investigation

2. **Extract gap details:**
   - Gap title/description
   - Section it appears in
   - Any context provided

**Report format (MUST use this exact format):**
```
## Gap Scan: {Plugin Name}

### Sections Scanned:
- Stubs & Unimplemented Features: {N items found / "Section not present"}
- Potential Extensions: {N items found / "Section not present"}
- Design Considerations: {N items found / "Section not present"}
- Bugs: {N items found / "No bugs identified"}
- Other gap sections: {list any found}

### Gap Counts:
- **Total gaps identified:** N
- **Already marked IN_PROGRESS:** N
- **Already marked NEEDS_DESIGN with issue:** N
- **Actionable (no marker):** N

### First Actionable Gap:
{Gap description} (line {N}, section: {section})
```

**CRITICAL:** You MUST list each section and count its items. Do not summarize as "0 actionable gaps" without showing your work. If Stubs & Unimplemented Features has 2 items and Potential Extensions has 5 items, you have 7 gaps to report.

**Only report "No actionable gaps" if ALL of these are true:**
1. You scanned every section listed above
2. You reported the item count for each section
3. Every item found has an AUDIT marker

If any item lacks a marker, it is actionable. Proceed to Phase 3.

### Phase 3: Gap Selection

Announce the first actionable gap and proceed immediately:

```
## Investigating Gap

**Plugin:** {name}
**Gap:** {description}
**Section:** {section}
**Line:** {N}
```

Proceed directly to Phase 4.

**Note:** This workflow handles ONE gap per invocation. Run `/audit-plugin` again to process additional gaps.

### Phase 4: Investigation

**⛔ THIS PHASE IS MANDATORY. YOU CANNOT SKIP IT. ⛔**

You MUST actually read the source code files. You CANNOT make a determination without:
1. Opening and reading the service implementation file(s)
2. Finding the specific methods/code related to the gap
3. Understanding what exists vs. what's missing

**If you find yourself saying "this seems like it would need X" without having READ the code, STOP. Go read the code.**

For the selected gap, investigate:

1. **Code Location**
   - Find the service implementation file(s)
   - Identify the specific methods/classes involved
   - Trace the code path

2. **Integration Points**
   - What other services does this touch?
   - What events are published/consumed?
   - What state stores are involved?
   - What clients are called?

3. **TENET Compliance Check**
   Read `docs/reference/TENETS.md` and verify:
   - [ ] Schema-first: Does this require schema changes?
   - [ ] Infrastructure libs: Will this use lib-state/lib-messaging/lib-mesh correctly?
   - [ ] Event-driven: Are proper typed events defined?
   - [ ] Error handling: Is the error pattern clear?
   - [ ] Configuration: Are config requirements understood?

4. **Scope Assessment**
   - Is this a single-file fix or multi-file change?
   - Are there test implications?
   - Are there documentation updates needed beyond the deep dive?

5. **Open Questions**
   - List ANY uncertainties or design decisions needed
   - Be thorough - if you're not 100% sure, note it

**Investigation Output Template (MUST include proof of code reading):**
```markdown
## Investigation: {Gap Title}

### Code Analysis
- **Primary file(s):** {paths - MUST be actual paths you read}
- **Methods involved:** {list - MUST be actual method names from the code}
- **Current implementation:** {Brief description of what the code currently does}
- **What's missing:** {Specific description of what needs to be added/changed}
- **Lines of code estimated:** {N}

### Proof of Code Reading
{Quote 2-3 relevant lines from the actual source code that relate to this gap. If you cannot quote actual code, you have not read it.}

### Integration Points
- **Services called:** {list or "None"}
- **Events published:** {list or "None"}
- **Events consumed:** {list or "None"}
- **State stores:** {list or "None"}

### TENET Compliance
- Schema changes needed: {Yes/No - details}
- Infrastructure libs: {Compliant/Needs work - details}
- Event definitions: {Exist/Need creation}
- Error handling: {Clear/Needs design}
- Configuration: {Sufficient/Needs additions}

### Scope
- Files to modify: {count}
- Test changes: {Yes/No}
- Doc changes: {Yes/No}

### Open Questions
{Numbered list of any uncertainties, or "None - implementation path is clear"}
```

**If your "Code Analysis" section is vague or lacks specific file paths and method names, you have not completed Phase 4. Go back and read the code.**

### Phase 5: Determination

Based on Phase 4 investigation, make a **BINARY** decision. There are exactly two valid outputs:

1. **EXECUTE** - You will implement the fix right now
2. **CREATE_ISSUE** - You will create a GitHub issue for human review

**⛔ FORBIDDEN OUTPUTS:**
- "No action needed" - INVALID
- "This is acceptable as documented" - INVALID
- "This is low priority" - INVALID
- "This can be deferred" - INVALID
- "This is working as intended" - INVALID (if it's working as intended, it shouldn't be in a gap section - that's a doc cleanup EXECUTE)

**EXECUTE if ALL of these are true:**
- [ ] No open questions remain
- [ ] All TENET requirements are clear
- [ ] Implementation path is unambiguous
- [ ] No design decisions needed from a human developer
- [ ] Scope is well-bounded

**CREATE ISSUE if ANY of these are true:**
- [ ] Open questions exist that require human judgment
- [ ] Multiple valid architectural approaches exist
- [ ] TENET compliance is unclear or requires interpretation
- [ ] Integration with unimplemented services is needed
- [ ] Business logic decisions are required

**Note on "acceptable" items:** If you determine an item is actually fine as-is and shouldn't be in a gap section, the action is EXECUTE with the fix being "remove from gap section / move to Intentional Quirks." Documentation cleanup IS a valid execute action.

**Report your determination clearly:**
```
### Determination: {EXECUTE|CREATE_ISSUE}

**Rationale:** {1-2 sentence explanation}

**Checklist verification:**
- [ ] I read the actual source code (Phase 4 complete)
- [ ] I can quote specific code from the files
- [ ] My determination is one of the two valid options
```

### Phase 6a: Execute Path

If determination is EXECUTE:

1. **Mark the document**
   Add marker immediately after the gap description:
   ```markdown
   {gap description}
   <!-- AUDIT:IN_PROGRESS:YYYY-MM-DD -->
   ```

2. **Create implementation plan**
   Based on the investigation findings, create a detailed plan:
   - List specific files to modify with exact changes
   - Include test requirements
   - Reference relevant TENETS that apply
   - Note any schema changes needed (schema-first!)

3. **Implement the fix**
   Execute the plan directly in this context:
   - Make schema changes first if needed (`scripts/generate-all-services.sh`)
   - Edit service implementation files
   - Update tests if required
   - Run `dotnet build` to verify
   - Follow all TENETS (especially schema-first, infrastructure libs, error handling)

4. **Update documentation**
   After successful implementation:
   - Remove the `AUDIT:IN_PROGRESS` marker
   - Update the gap section (mark complete or remove)
   - Add to "Recent Changes" if the doc has that section

5. **Report completion**
   ```
   ## Execution Complete

   - **Gap addressed:** {title}
   - **Files modified:** {list}
   - **Tests added/updated:** {Yes/No}
   - **Build status:** {Pass/Fail}
   ```

### Phase 6b: Create Issue Path

If determination is CREATE_ISSUE:

1. **Mark the document**
   Add marker with placeholder (you'll update with issue URL after creation):
   ```markdown
   {gap description}
   <!-- AUDIT:NEEDS_DESIGN:YYYY-MM-DD:ISSUE_URL_PLACEHOLDER -->
   ```

2. **Create GitHub issue**
   Use `gh issue create` with this template:

   ```markdown
   ## Implementation Gap: {Gap Title}

   **Source:** `docs/plugins/{PLUGIN}.md`
   **Identified:** {DATE}

   ## Summary
   {Brief description of the gap}

   ## Investigation Findings

   ### Code Analysis
   {From Phase 4}

   ### Integration Points
   {From Phase 4}

   ### TENET Considerations
   {From Phase 4}

   ## Open Questions

   The following questions require human judgment before implementation:

   {Numbered list from Phase 4}

   ## Recommended Next Steps

   1. {Specific action item}
   2. {Specific action item}
   ...

   ---
   *Generated by plugin-auditor*
   ```

3. **Update the marker**
   Replace `ISSUE_URL_PLACEHOLDER` with the actual issue URL

4. **Report**
   ```
   ## Issue Created

   - **Gap:** {title}
   - **Issue:** {URL}
   - **Open questions:** {count}
   ```

### Phase 7: Report

Provide a final summary:

```markdown
## Audit Complete

### Action Taken
- **Plugin:** {name}
- **Gap:** {title}
- **Path:** {EXECUTE|CREATE_ISSUE}
- **Result:** {outcome}

### Remaining Work
- **Gaps still pending in this plugin:** {count}
- **Gaps pending across all plugins:** {count} (if full scan was done)

### Next Steps
{Recommendation for what to do next}
```

## Error Handling

If any phase fails:
1. Do NOT continue to the next phase
2. Report the failure clearly
3. Do NOT mark anything as in-progress if you couldn't complete the work
4. Suggest how to recover

## Important Notes

- **Never skip the investigation phase** - superficial analysis leads to bad issues or broken implementations
- **Be conservative with EXECUTE** - when in doubt, create an issue
- **Always verify build after changes** - run `dotnet build` before declaring success
- **Follow all TENETS** - especially schema-first development
- **Preserve existing markers** - don't remove IN_PROGRESS markers from other developers
- **Stubs and Potential Extensions ARE gaps** - they describe incomplete/missing functionality
- **"Documented" does NOT mean "resolved"** - the doc is the tracking system, not the completion marker
- **You do not filter by priority** - investigate everything, let humans prioritize
- **Every gap gets EXECUTE or CREATE_ISSUE** - there is no third option

## Self-Check Before Completing

Before you report "No actionable gaps" or skip any item, verify:

1. Did I scan Stubs & Unimplemented Features? Did I count the items?
2. Did I scan Potential Extensions? Did I count the items?
3. Did I scan Design Considerations? Did I count the items?
4. For every item I'm skipping, does it have an AUDIT marker?
5. Am I rationalizing a skip based on perceived priority or acceptability?

If you answered "no" to any of these, go back and complete the work.
