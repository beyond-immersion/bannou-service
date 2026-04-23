---
description: "Investigate a GitHub issue end-to-end. Reads issue with ALL comments, gathers context, clarifies with developer, plans, resolves, audits, and closes. Resolution scope matches the plugin's current artifact level."
argument-hint: "[issue-number|random] - GitHub issue number (e.g., '42') or 'random'/omit for random selection"
disable-model-invocation: true
---

# Investigate Issue

Developer-guided process for taking a GitHub issue from discovery through resolution to closure. Resolution may be documentation updates, schema changes, code fixes, or any combination — depending on what artifacts exist.

## Rules

1. **ALWAYS read the FULL issue AND every comment.** Comments contain design decisions, scope changes, and constraints that override the body. A summary or body alone is NEVER sufficient. If you cannot retrieve comments → HARD STOP.
2. **NEVER guess at unanswered questions.** "The developer probably wants X" is not an answer — ASK.
3. **NEVER assume decisions from ambiguous approval.** "Sounds good" is not a decision. Get explicit option selection.
4. **NEVER skip phases.** Each phase has a checkpoint requiring developer acknowledgment.
5. **Distinguish CONFIRMED (from issue/code/developer) from INFERRED (your analysis) from SPECULATIVE (needs verification).**
6. **Resolution scope matches artifact level.** You update UP TO the highest artifact that exists (deep dive → map → schemas → code) — never beyond.

---

## Phase 1: Issue Selection

**Random:** `gh issue list --state open --limit 100 --json number,title | jq -r '.[] | "\(.number): \(.title)"' | shuf -n 1`
**Specific:** `gh issue view {number} --json number,title,body,labels,comments,assignees`

If issue doesn't exist or is closed → report and STOP.

## Phase 2: Deep Read & Context

**Step 2a:** Load development context:
```
prepare_context(profile: "dev")
```

Read vision documents for high-level alignment:
```
read_file("docs/reference/VISION.md")
read_file("docs/reference/PLAYER-VISION.md")
```

**Step 2b:** Read the FULL issue AND ALL comments:
```bash
gh issue view {number} --json number,title,body,labels,comments,assignees
```

Read every comment. State: "I have read the full issue body and all N comments." If comments are truncated: `gh api repos/{owner}/{repo}/issues/{number}/comments --paginate`

**If you CANNOT retrieve comments → HARD STOP.** Do not proceed with only the issue body.

**Step 2c:** Identify affected plugin(s) from issue content.

**Step 2d:** Load plugin context for each affected plugin:
```
prepare_context(profile: "plugin", service: "{service}")
```

Also read the plugin source code:
```bash
find plugins/lib-{service}/ -name "*.cs" ! -path "*/obj/*" ! -path "*/bin/*" ! -path "*/Generated/*" -exec wc -l {} + 2>/dev/null | sort -rn
```
Read all manual `.cs` files found.

**Step 2e:** Determine resolution scope:

| If this exists | Resolution can update up to |
|---|---|
| `plugins/lib-{service}/{Service}Service.cs` | Deep dive, map, schemas, code, tests |
| `schemas/{service}-api.yaml` | Deep dive, map, schemas |
| `docs/maps/{SERVICE}.md` | Deep dive, map |
| `docs/plugins/{SERVICE}.md` only | Deep dive |

## Phase 3: Present to Developer

**⛔ CHECKPOINT — do not proceed without developer acknowledgment.**

```markdown
## Issue Briefing: #{number}

### The Issue
**What:** {clear statement of the ask}
**Why:** {motivation, or "Not specified"}

### Scope Analysis

#### CONFIRMED (from issue/code):
- {facts with source}

#### INFERRED (my analysis):
- {inferences with reasoning}

#### SPECULATIVE (needs your input):
- {assumptions needing confirmation}

### Design Questions
{Numbered list — MUST be answered before proceeding}

### Estimated Scope
Files: {count} | Schema changes: {Y/N} | Tests: {Y/N} | Docs: {Y/N}
Resolution scope: {Code/Schemas/Map/Deep Dive}

**Please review, answer design questions, and confirm to proceed.**
```

Wait for developer response. ALL design questions must be answered.

## Phase 4: Interactive Clarification

Track decisions:
```
| # | Question | Status | Decision |
|---|----------|--------|----------|
| 1 | {question} | ✅ DECIDED | {answer} |
| 2 | {question} | ❓ PENDING | Awaiting |
```

Ambiguous responses → ask for explicit clarification. New questions → add to tracker.

Exit when ALL questions are ✅ and developer says "proceed."

## Phase 5: Planning

Write the plan yourself to `~/.claude/plans/{plan-name}.md`:

```markdown
# Implementation Plan: Issue #{number} - {title}

## Decisions Made
| Question | Decision |
|----------|----------|
| {from Phase 4} | {answer} |

## Phase 1: {First Phase}
{Detailed steps with files, changes}

## Files to Create/Modify
| File | Action | Description |
|------|--------|-------------|
| {path} | {Create/Modify} | {what changes} |

## Order of Operations
1. Schema changes first
2. Regenerate
3. Implementation
4. Build verification
5. Tests
```

Present plan for developer approval.

## Phase 6: Resolution

**Only after plan is approved.** Follow the resolution scope from Phase 2e.

**Documentation-only** (deep dive/map scope): Edit `.md` files per plan. Verify consistency.

**Schema-level**: Edit schemas → run most granular generation script → verify build.

**Code-level**: Schema changes first → regenerate → implement per plan → build → tests.

Implementation rules:
- No `null!` — use null coalescing with exceptions
- No top-level try-catch — controller provides boundary
- `BannouJson` for serialization
- Constructor-cached state stores
- Generated `Publish*Async` extensions
- `StartActivity` on async helpers

```bash
dotnet build > /tmp/issue-{number}-build.txt 2>&1
```

## Phase 7: Self-Audit

Verify against tenets (already in context from `prepare_context`):
- [ ] Schema-first compliance
- [ ] Infrastructure libs used correctly
- [ ] Event-driven patterns followed
- [ ] Error handling correct (return tuples, no top-level catch)
- [ ] Multi-instance safe
- [ ] All planned changes implemented
- [ ] No unplanned changes
- [ ] Issue requirements satisfied

## Phase 8: Iterate on Findings

If audit finds issues → present to developer with severity. Get approval for fixes. Fix → re-audit.

## Phase 9: Update Documentation

**Bug fixes:** `~~**Original description**~~: **FIXED** (YYYY-MM-DD) - Brief description.`
**New features:** Update deep dive (high-level) + map (pseudocode) if they exist.
**Resolved design considerations:** `~~**Title**~~: **RESOLVED** (YYYY-MM-DD) — {decision}` — do NOT delete these.
**Rejected concerns:** Remove item entirely.

Update Work Tracking: `**YYYY-MM-DD**: Issue #{number} - {description}`

## Phase 10: Close Issue

Write closing comment to `/tmp/gh-comment-{number}.md` via `write_file`, then:
```bash
gh issue comment {number} --body-file /tmp/gh-comment-{number}.md
gh issue close {number}
```

Comment includes: what was done, key decisions, files changed, how verified, docs updated.

**Final report:**
```markdown
## Issue #{number} Closed

**Summary**: {one paragraph}
**Decisions**: {key decisions}
**Work**: {files changed, tests added, docs updated}
**Follow-up**: {deferred work or related issues, if any}
```
