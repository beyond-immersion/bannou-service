---
description: Investigate a GitHub issue end-to-end. Reads issue, understands context, clarifies with developer, plans, implements, audits, and closes.
argument-hint: "[issue-number|random] - GitHub issue number (e.g., '42') or 'random'/omit to pick one at random"
---

# Issue Investigator Command

You are executing the `/investigate-issue` workflow. This is a comprehensive, developer-guided process for taking a GitHub issue from discovery through implementation to closure.

## ⛔ CRITICAL BEHAVIORAL RULES ⛔

**These rules override all other instructions:**

1. **NEVER GUESS AT UNANSWERED QUESTIONS**
   - If a design question hasn't been explicitly answered by the developer, you MUST ask it
   - "The developer probably wants X" is NOT an answer - ASK
   - Context from previous conversations is NOT confirmation - CONFIRM

2. **NEVER ASSUME DECISIONS FROM SUMMARIES**
   - If you summarized options and the developer said "sounds good", that is NOT a decision
   - You must get explicit: "Use approach A" or "Yes, go with option 2"
   - Ambiguous approval requires clarification

3. **NEVER SKIP PHASES**
   - Each phase has a checkpoint that requires developer acknowledgment
   - You cannot proceed to implementation without explicit plan approval
   - You cannot close the issue without audit completion

4. **ALWAYS DISTINGUISH FACTS FROM SPECULATION**
   - When presenting information, clearly label what is:
     - **CONFIRMED**: From the issue, code, or developer
     - **INFERRED**: Your analysis based on code reading
     - **SPECULATIVE**: Assumptions that need verification

## ⛔ FORBIDDEN OPERATIONS ⛔

**These commands are ABSOLUTELY FORBIDDEN without explicit user approval:**

| Command | Why Forbidden |
|---------|---------------|
| `git checkout -- <file>` | Destroys uncommitted work |
| `git checkout .` | Destroys all uncommitted changes |
| `git stash` | Hides changes that may be lost |
| `git reset` (without `--soft`) | Can destroy commit history |
| `git restore` | Overwrites uncommitted changes |
| `git clean` | Permanently deletes untracked files |
| `mv` (for code files) | Can lose files or break references |

**If you need to undo changes:**
1. STOP and ask the developer
2. Explain what needs undoing and why
3. Wait for explicit approval
4. Use the Edit tool to carefully revert specific changes

## ⛔ CODING CONSTRAINTS ⛔

**These rules apply to ALL code written during implementation:**

1. **Null-Forgiving Operators PROHIBITED**
   - ❌ variable!, property!, method()!, null!, default!, (Type)null
   - ✅ `variable ?? throw new ArgumentNullException(nameof(variable))`
   - These cause segmentation faults and hide null reference exceptions

2. **NEVER Run Integration Tests**
   - ❌ `make test-http`, `make test-edge`, `make test-infrastructure`, `make all`
   - ✅ `dotnet build` is sufficient verification
   - You may ADD test files, but never run http/edge tests

3. **Testing Architecture (if adding tests)**
   - MUST read `docs/guides/TESTING.md` FIRST
   - Plugin isolation rules are strict and violations cause build failures
   - Respond with "I have referred to the service testing document" to confirm

4. **NEVER Commit or Format**
   - Do NOT run `make format`, `git add`, `git commit`, or any git write operations
   - The developer handles all git operations

## Workflow Phases

### Phase 1: Issue Selection

**If argument is "random" or no argument provided:**
```bash
gh issue list --state open --limit 100 --json number,title,labels | jq -r '.[] | "\(.number): \(.title)"' | shuf -n 1
```
Extract the issue number and announce: "Investigating Issue #{number}: {title}"

**If specific issue number provided:**
```bash
gh issue view {number} --json number,title,body,labels,comments,assignees
```
If the issue doesn't exist or is closed, report the error and stop.

**Output:**
```markdown
## Issue Selected

**Issue:** #{number}
**Title:** {title}
**Labels:** {labels}
**Status:** Open
```

### Phase 2: Deep Read & Context Gathering

This phase requires thorough reading. Do NOT skim.

**Step 2a: Read the full issue**
- Read the entire issue body
- Read ALL comments (may contain clarifications, decisions, or scope changes)
- Note any linked issues or PRs

**Step 2b: Identify affected plugin(s)**
From the issue content, identify which plugin(s) are involved. Look for:
- Explicit mentions of services/plugins
- File paths mentioned
- API endpoints referenced
- Event names mentioned

**Step 2c: Read plugin deep dive(s)**
For each identified plugin, read the FULL deep dive document:
```
docs/plugins/{PLUGIN}.md
```

Extract and note:
- Overview (what the plugin does)
- Dependencies (what it relies on)
- State stores (data patterns)
- Events (pub/sub patterns)
- **Stubs & Unimplemented Features** (relevant gaps)
- **Known Quirks & Caveats** (gotchas to avoid)
- **Design Considerations** (architectural context)

**Step 2d: Read relevant source code**
Based on the issue, read the actual implementation files:
```
plugins/lib-{service}/{Service}Service.cs
```

Understand the current state of the code related to the issue.

**Output:**
```markdown
## Context Gathered

### Issue Summary
{Your understanding of what the issue is asking for}

### Affected Plugins
| Plugin | Role in Issue |
|--------|--------------|
| {name} | {how it's involved} |

### Relevant Quirks & Constraints
{List quirks from deep dives that affect this work}

### Current Implementation State
{What exists today, what's missing}
```

### Phase 3: Present to Developer

**⛔ THIS IS A CHECKPOINT - DO NOT PROCEED WITHOUT DEVELOPER ACKNOWLEDGMENT ⛔**

Present a comprehensive briefing that clearly distinguishes facts from speculation:

```markdown
## Issue Briefing: #{number}

### The Issue
**What is being requested:**
{Clear statement of the ask}

**Why it matters:**
{Business/technical motivation if stated, or "Not specified in issue"}

### Scope Analysis

#### CONFIRMED (from issue/code):
- {Fact 1 - with source}
- {Fact 2 - with source}

#### INFERRED (my analysis):
- {Inference 1 - reasoning}
- {Inference 2 - reasoning}

#### SPECULATIVE (needs your input):
- {Assumption 1 - why I think this, but need confirmation}
- {Assumption 2 - why I think this, but need confirmation}

### Design Questions

The following questions MUST be answered before I can proceed:

1. **{Question about approach/scope}**
   - Option A: {description}
   - Option B: {description}
   - My recommendation: {if any, with reasoning}

2. **{Question about behavior}**
   - {options or open-ended}

3. **{Question about constraints}**
   - {options or open-ended}

### Dependencies & Risks

- **Blocks:** {what this depends on}
- **Blocked by:** {what depends on this}
- **Risk areas:** {potential issues}

### Estimated Scope
- Files to modify: {rough count}
- Schema changes: {Yes/No}
- Test changes: {Yes/No}
- Doc changes: {Yes/No}

---

**Please review the above and:**
1. Confirm or correct my understanding
2. Answer the design questions
3. Flag any missing considerations

I will not proceed until you explicitly confirm.
```

**Wait for developer response. Do NOT proceed until they have:**
- Acknowledged the briefing
- Answered ALL design questions (or explicitly deferred them)
- Confirmed scope understanding

### Phase 4: Interactive Clarification

This phase continues until ALL questions are resolved.

**Rules for this phase:**

1. **Track answered vs. unanswered questions**
   ```markdown
   ### Decision Tracker

   | # | Question | Status | Decision |
   |---|----------|--------|----------|
   | 1 | {question} | ✅ DECIDED | {explicit answer} |
   | 2 | {question} | ❓ PENDING | Awaiting answer |
   | 3 | {question} | 🔄 CLARIFYING | Need more detail |
   ```

2. **When developer gives ambiguous response:**
   - Do NOT interpret it as a decision
   - Ask for explicit clarification
   - Example: "You said 'that sounds reasonable' - to confirm, should I proceed with Option A (X) or Option B (Y)?"

3. **When new questions arise:**
   - Add them to the tracker
   - Do not proceed past them

4. **Exit criteria for this phase:**
   - ALL questions marked ✅ DECIDED
   - Developer explicitly says "proceed to planning" or equivalent

**Template for clarification requests:**
```markdown
### Clarification Needed

I need explicit decisions on the following before proceeding:

**Question {N}: {question}**
Your response was: "{quote their response}"
I'm not certain if this means:
- (A) {interpretation 1}
- (B) {interpretation 2}

Which is correct?
```

### Phase 5: Planning

**Only enter this phase when ALL questions from Phase 4 are resolved.**

Launch a planning agent with full context.

**⚠️ CRITICAL: The Plan agent does not know about project-specific rules. You MUST include explicit instructions to read the TENETs.**

```markdown
## Planning Request

### ⛔ MANDATORY FIRST STEP ⛔
Before creating any plan, you MUST read and understand the project's development guidelines:
- **Read `docs/reference/TENETS.md`** - This contains inviolable development rules
- **Read `docs/reference/SCHEMA-RULES.md`** - Schema-first development requirements
- All planned changes MUST comply with these TENETs

Key TENETs to be aware of:
- T1: Schema-first development (NEVER edit Generated/ files)
- T4: Infrastructure libs pattern (use lib-state, lib-messaging, lib-mesh)
- T5: Event-driven architecture (typed events, not anonymous objects)
- T7: Error handling patterns
- T8: Return pattern (StatusCodes tuples)
- T21: Configuration-first (no hardcoded tunables)

---

## Planning Context

### Issue
- Number: #{number}
- Title: {title}
- Link: {url}

### Decisions Made
{Copy the completed Decision Tracker with all ✅ items}

### Affected Plugins
{From Phase 2}

### Constraints & Quirks
{From Phase 2 deep dive reading}

### Scope
{Confirmed scope from Phase 3/4}

---

Create a detailed implementation plan that:
1. Lists specific files to create/modify
2. Specifies the order of changes (schema-first per T1)
3. Identifies test requirements
4. Notes documentation updates needed
5. Explicitly notes which TENETs apply to each change
```

**Use the Plan agent:**
```
Task tool with subagent_type=Plan
```

**Present the plan to the developer for approval before implementation.**

### Phase 6: Implementation

**Only enter this phase when the plan is approved.**

**⚠️ REMEMBER THE CODING CONSTRAINTS (see above for details):**
- NO null-forgiving operators (the ! suffix) - use null coalescing with exceptions
- NO running integration tests - dotnet build is sufficient verification
- If plan includes adding tests, you MUST read docs/guides/TESTING.md first

Execute the plan:

1. **Schema changes first** (if any)
   - Edit schema files in `/schemas/` directory
   - Run `scripts/generate-all-services.sh`
   - Verify generation succeeded
   - NEVER edit files in `*/Generated/` directories

2. **Implementation changes**
   - Follow the plan order strictly
   - Make incremental changes
   - Verify build after significant changes: `dotnet build`
   - NO null-forgiving operators - use `?? throw new ArgumentNullException()`

3. **Test changes** (if adding tests as part of plan)
   - FIRST: Read `docs/guides/TESTING.md` and confirm "I have referred to the service testing document"
   - Follow plugin isolation rules strictly
   - Add/modify test files as specified in plan
   - Do NOT run `make test-http`, `make test-edge`, or `make all`

4. **Build verification**
   ```bash
   dotnet build     # Verify compilation
   ```
   Must pass before proceeding.

**Report progress:**
```markdown
## Implementation Progress

### Completed
- [x] {change 1}
- [x] {change 2}

### In Progress
- [ ] {change 3}

### Build Status
{Pass/Fail with details}
```

### Phase 7: Audit

**Only enter this phase when implementation is complete and building.**

Launch an audit agent to review the work.

**⚠️ CRITICAL: The code-reviewer agent does not know about project-specific rules. You MUST include explicit instructions to read the TENETs.**

```markdown
## Audit Request

### ⛔ MANDATORY FIRST STEP ⛔
Before reviewing any code, you MUST read and understand the project's development guidelines:
- **Read `docs/reference/TENETS.md`** - This contains inviolable development rules that ALL code must follow
- **Read `docs/reference/SCHEMA-RULES.md`** - Schema-first development requirements

These are NOT suggestions - they are mandatory rules. Any violation is a finding.

Key TENETs to audit against:
- **T1**: Schema-first (NEVER edit Generated/ files, all APIs defined in schemas)
- **T4**: Infrastructure libs (must use lib-state, lib-messaging, lib-mesh - no direct Redis/RabbitMQ/HTTP)
- **T5**: Event-driven (typed events, no anonymous objects)
- **T7**: Error handling (try-catch with ApiException distinction, TryPublishErrorAsync)
- **T8**: Return pattern (all methods return `(StatusCodes, TResponse?)` tuples)
- **T9**: Multi-instance safety (no in-memory authoritative state, use distributed locks)
- **T20**: JSON serialization (always use BannouJson, never direct JsonSerializer)
- **T21**: Configuration-first (use generated config classes, no hardcoded tunables)
- **T22**: Warning suppression (forbidden except specific exceptions)

---

### Original Issue
{Issue number and summary}

### Plan Document
{The approved plan}

### Changes Made
{List of files modified with brief description}

---

### Audit Checklist

**Against TENETs (read docs/reference/TENETS.md for full details):**
- [ ] T1: Schema-first compliance (no manual edits to Generated/, schemas updated first)
- [ ] T4: Infrastructure libs used correctly (lib-state, lib-messaging, lib-mesh)
- [ ] T5: Event-driven patterns followed (typed events defined in schema)
- [ ] T7: Error handling correct (ApiException vs Exception, TryPublishErrorAsync)
- [ ] T8: Return pattern correct (StatusCodes enum, not HTTP status codes)
- [ ] T9: Multi-instance safe (no static mutable state, distributed locks where needed)
- [ ] T20: BannouJson used (not System.Text.Json directly)
- [ ] T21: Configuration from schema (no Environment.GetEnvironmentVariable, no magic numbers)
- [ ] T22: No forbidden warning suppressions

**Against Plan:**
- [ ] All planned changes implemented
- [ ] No unplanned changes introduced
- [ ] Order of operations followed (schema-first)

**Against Issue:**
- [ ] Issue requirements satisfied
- [ ] Scope not exceeded without approval
- [ ] Edge cases considered

**Code Quality:**
- [ ] No obvious bugs or race conditions
- [ ] No security issues (injection, auth bypass, etc.)
- [ ] Consistent with existing patterns in the codebase
- [ ] Proper null handling (no null-forgiving operators per CLAUDE.md)
```

**Use a code review agent:**
```
Task tool with subagent_type=feature-dev:code-reviewer
```

### Phase 8: Iterate on Audit Findings

If the audit identifies issues:

1. **Present findings to developer**
   ```markdown
   ## Audit Findings

   | Finding | Severity | Recommendation |
   |---------|----------|----------------|
   | {issue} | {High/Medium/Low} | {fix} |
   ```

2. **Get developer approval for fixes**
   - Do NOT auto-fix without approval
   - Some findings may be acceptable

3. **Implement approved fixes**
   - Return to Phase 6 for implementation
   - Return to Phase 7 for re-audit

4. **Exit criteria:**
   - All High severity findings resolved
   - Developer approves remaining findings (if any)

### Phase 9: Update Documentation

Update the plugin deep dive document(s):

**For bug fixes:**
Use the FIXED format in the appropriate section:
```markdown
N. ~~**Original bug description**~~: **FIXED** (YYYY-MM-DD) - Brief description of fix.
```

**For new features:**
Add to appropriate sections:
- New endpoints → API Endpoints section
- New events → Events section
- New state patterns → State Storage section
- New quirks → Known Quirks section

**For resolved design considerations:**
- Remove from "Design Considerations" if fully addressed
- Or update with the decision made

**Update Work Tracking:**
```markdown
## Work Tracking

### Completed
- **YYYY-MM-DD**: Issue #{number} - {brief description}
```

### Phase 10: Close Issue

Create a closing comment and close the issue:

```bash
gh issue comment {number} --body "$(cat <<'EOF'
## Resolution Summary

### What was done
{Brief description of the implementation}

### Key Decisions Made
| Decision | Choice | Rationale |
|----------|--------|-----------|
| {topic} | {choice} | {why} |

### Files Changed
- `{file1}` - {what changed}
- `{file2}` - {what changed}

### Testing
{How this was verified}

### Documentation Updated
- `docs/plugins/{PLUGIN}.md` - {what was added/updated}

---
Resolved by Claude Code with developer guidance.
EOF
)"

gh issue close {number}
```

**Final Report:**
```markdown
## Issue #{number} Closed

### Summary
{One paragraph summary}

### Decisions Made
{Key decisions and their rationale}

### Work Completed
- Implementation: {files changed}
- Tests: {if any}
- Documentation: {updates made}

### Follow-up Items
{Any deferred work or related issues to create}
```

## Error Handling

**If GitHub CLI fails:**
- Verify `gh auth status`
- Report the error and stop

**If build fails during implementation:**
- Do NOT proceed
- Report the failure
- Work with developer to resolve

**If audit finds critical issues:**
- Do NOT close the issue
- Return to implementation phase

**If developer becomes unavailable mid-workflow:**
- Save state in a clear summary
- Note which phase you're in
- List pending questions/decisions

## Important Reminders

- **This is a collaborative workflow** - the developer is the decision-maker
- **You are the investigator and implementer** - but you don't make product decisions
- **When in doubt, ask** - it's better to over-clarify than to assume
- **Track everything** - decisions, changes, rationale
- **The issue is not closed until the audit passes** - don't rush to completion
- **NEVER use destructive git commands** - no checkout/stash/reset/restore/clean
- **NEVER commit or format** - no git add, git commit, make format (developer handles these)
- **NEVER run integration tests** - no make test-http/test-edge/all (you may ADD test files)
- **NEVER use null-forgiving operators** (the ! suffix) - they cause segfaults
- **ALWAYS read TESTING.md** before adding tests - plugin isolation is strict
