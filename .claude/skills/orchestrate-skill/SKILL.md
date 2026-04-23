---
description: Orchestrate running any single-target skill across multiple targets in parallel batches of 3. Reads the target skill completely, discovers targets, creates tracked task list with baked-in full prompt templates (compaction-safe), and launches agents 3-at-a-time. For audit-plugin, auto-filters to plugins with actionable gaps.
argument-hint: "<skill-name> for <all|N|name1,name2,...> - Skill and scope (e.g., 'maintain-operations-doc for all', 'audit-plugin for account,auth,chat', 'maintain-faq for 5')"
disable-model-invocation: true
---

# Orchestrate Skill Command

You are executing the `/orchestrate-skill` workflow. This orchestrates running ANY single-target skill across multiple targets in parallel batches of 3, with tracking that survives context compaction.

**You are the orchestrator.** You do NOT do the skill's work yourself. You read the skill, discover targets, create a tracked task list with full prompt templates baked into every entry, and launch agents that execute the skill's instructions. You manage concurrency, collect results, run a final audit, and report.

**Why this exists**: Individual skills like `/maintain-plugin` or `/audit-plugin` operate on one target at a time. This workflow runs them across many targets with minimal orchestrator context usage — each agent gets pristine context with the full skill instructions, and the TaskCreate task list with baked-in prompts survives context compaction so the operation can continue indefinitely.

## Required Tools

This workflow REQUIRES: **Agent**, **edit_file** (MCP), **read_file** (MCP), **TaskCreate**, **TaskUpdate**, **TaskList**, **TaskGet** tools.

**Before proceeding with ANY phase:**

1. Verify you can use the Agent, Edit, Read, TaskCreate, TaskUpdate, TaskList, and TaskGet tools
2. If ANY tool is unavailable, **STOP IMMEDIATELY** with this exact message:
 ```
 ## ORCHESTRATION FAILED: Required Tools Unavailable

 This workflow requires Agent, Edit, Read, and Task* tools (TaskCreate, TaskUpdate, TaskList, TaskGet).
 Missing: {list unavailable tools}

 **To fix:** Ensure all tools are available and retry.
 ```
3. Do NOT proceed to any phase
4. Do NOT generate a "report" describing what you would have done
5. Do NOT offer to "do the work manually instead"

**There is no fallback. If you cannot create task lists and launch agents, you cannot orchestrate. Fail honestly.**

## Common Pitfalls

**The #1 failure mode of this workflow**: You encounter an obstacle, and instead of stopping, you silently degrade into doing the work yourself. **This defeats the entire purpose of the workflow.** If you cannot orchestrate, you FAIL — you do not compensate by becoming a worker.

1. **"I'll just run the skill myself instead of launching agents"** — NO. HARD STOP. The entire point is parallel agent execution with context isolation. Each agent gets pristine context with the full skill content. You doing the work yourself defeats the purpose, exhausts your context window, and produces inferior results because you lack the pristine context each agent would have had. **If the Agent tool is unavailable, you FAIL. You do not pivot to doing the work.**

2. **"I'll summarize the skill instead of copying it fully into the prompt"** — NO. HARD STOP. Agents CANNOT execute skills via the Skill tool. The COMPLETE, VERBATIM skill content must be in every agent's prompt. Summarizing loses critical instructions, phase ordering, common pitfalls, and validation steps. There is no acceptable abbreviation. **If the skill content is too long for a prompt, you FAIL and report the size. You do not summarize.**

3. **"I'll paraphrase/rewrite the skill content to be more concise"** — NO. HARD STOP. The skill content goes in VERBATIM. Character for character. You are a copier, not an editor. Rewriting changes meaning, drops nuance, and loses the skill author's carefully constructed phase ordering and validation gates. **Copy-paste. No rewording.**

4. **"I'll poll the agents to check progress"** — NO. Wait for completion notifications. Per CLAUDE.md, background agent polling is handled by automatic notifications. You will be automatically notified.

5. **"I'll resume completed agents to get their results"** — NO. Completion notifications include results. Do not resume completed agents.

6. **"I'll launch all agents at once"** — NO. Maximum 3 concurrent. This prevents resource exhaustion and allows incremental progress tracking.

7. **"I'll skip the task list — I can track in my head"** — NO. HARD STOP. The task list (via TaskCreate) with baked-in templates is the compaction survival mechanism. Without it, context compaction kills the operation. Every entry must be independently launchable. **If TaskCreate fails, you FAIL. You do not proceed without a task list.**

8. **"I'll skip the audit phase — the agents already did the work"** — NO. Individual agents work in isolation. The audit catches cross-document inconsistencies and template compliance issues that isolated agents cannot detect.

9. **"The skill is short, I don't need the full content in the prompt"** — NO. ALL skill content goes in the prompt. No exceptions. No summarizing. No "the important parts." The skill's own common pitfalls, phase ordering, and validation logic are ALL critical. **Length is not a factor in this decision. Full content. Always.**

10. **"I'll do the audit myself instead of launching an agent"** — NO. You are the orchestrator. Launch an audit agent with fresh context. Your context is for orchestration, not for reading and evaluating every modified document.

11. **"I'll handle the major audit fixes myself — it's faster"** — NO. Major fixes get a fresh agent with pristine context. Your context has been consumed by orchestration. A fresh agent will do better work.

12. **"I'll describe what I would have done / generate a plan instead"** — NO. HARD STOP. Describing work is not doing work. If you cannot execute the workflow, say so in one sentence and stop. Do not generate multi-paragraph descriptions of what the workflow "would" do. **A report about hypothetical work is not a deliverable.**

13. **"I'll launch agents one at a time instead of in batches"** — NO. Launch up to 3 in parallel in a single message. Sequential single-agent launches waste time and defeat the concurrency design. The only exception is when fewer than 3 targets remain.

14. **"An agent failed, so I'll redo its work in my context"** — NO. Mark the failure, continue the batch, and include the failure in the final report. The user can re-run failed targets with a subsequent `/orchestrate-skill` invocation. **You never compensate for agent failures by doing the work yourself.**

## Recognizing Failure Patterns

**If ANY of these conditions are true, the workflow has FAILED. Output the failure message and STOP. Do not continue.**

| Condition | Failure Message |
|-----------|----------------|
| Agent tool unavailable | `ORCHESTRATION FAILED: Agent tool unavailable. Cannot launch worker agents.` |
| TaskCreate tool unavailable | `ORCHESTRATION FAILED: TaskCreate tool unavailable. Cannot create compaction-safe task list.` |
| Skill file not found | `ORCHESTRATION FAILED: Skill not found at .claude/commands/{skill-name}.md` |
| No targets discovered | `ORCHESTRATION FAILED: No targets found in {directory}` |
| Cannot identify operating directory | `ORCHESTRATION FAILED: Cannot determine target directory from skill content. Specify manually.` |
| Cannot parse command format | `ORCHESTRATION FAILED: Cannot parse command. Expected: <skill-name> for <all\|N\|name1,name2,...>` |
| Task list creation fails | `ORCHESTRATION FAILED: TaskCreate calls failed. Cannot proceed without task list.` |
| >50% of agents fail | `ORCHESTRATION FAILED: Systemic failure — {count}/{total} agents failed. Environment issue suspected.` |

**After any failure message: STOP. Do not offer alternatives. Do not suggest workarounds. Do not "try a different approach." The workflow failed. Period.**

---

## Phase 0: Parse Command and Ingest Skill

### Step 1: Parse the Command

Extract from the user's input:
- **Skill name**: The skill to orchestrate (e.g., `maintain-operations-doc`, `audit-plugin`)
- **Scope**: One of:
 - `all` — Every target in the skill's operating directory
 - A number N (e.g., `5`) — N randomly selected targets
 - A comma-separated list (e.g., `testing,deployment,ci`) — Those specific targets

If the command cannot be parsed into these components, STOP and ask the user to clarify. Show the expected format: `<skill-name> for <all|N|name1,name2,...>`

### Step 2: Read the Skill File

Read `.claude/skills/{skill-name}/SKILL.md` IN FULL using the MCP read tool (read_file).

**If the file doesn't exist**, STOP and report: `Skill not found: .claude/skills/{skill-name}/SKILL.md`

Store the COMPLETE content **after** the YAML frontmatter closing `---` as `SKILL_CONTENT`. This is everything from the first markdown heading onward. The frontmatter (`description`, `argument-hint`) is metadata, not instructions — do NOT include it in agent prompts.

Also extract the `description` from the frontmatter for reporting.

### Step 3: Analyze the Skill

From reading the skill content, identify these three things:

**1. Operating directory** — Where does this skill find its targets? Look for directory paths in the skill's selection/discovery phase. Common patterns:

| Skill Pattern | Operating Directory | Target Type |
|---------------|-------------------|-------------|
| `maintain-doc` (faq) | `docs/faqs/` | Document filename |
| `maintain-doc` (guide) | `docs/guides/` | Document filename |
| `maintain-doc` (planning) | `docs/planning/` | Document filename |
| `maintain-doc` (ops) | `docs/operations/` | Document filename |
| `maintain-plugin` | `docs/plugins/` | Document filename |
| `audit-plugin` | `docs/plugins/` | Plugin name (derived from filename) |
| `map-plugin` | `docs/maps/` | Plugin name |
| `check-plugin` | `plugins/lib-*/` | Plugin name |
| `test-plugin` | `plugins/lib-*/` | Plugin name |
| `implement-plugin` | `plugins/lib-*/` | Plugin name |
| `maintain-issues` | `docs/plugins/` | Plugin name |

If the skill doesn't match these patterns, read the skill more carefully to identify where it discovers its targets. If truly ambiguous, STOP and ask the user to specify the directory.

**2. Target type** — What does the skill's argument represent?
- **Document filename**: The target is a file in the operating directory (e.g., `TESTING.md`)
- **Plugin/service name**: The target is a service name derived from filenames (e.g., `account` from `ACCOUNT.md` or `lib-account/`)

**3. Agent type** — Which bannou agent type should execute this skill? Match based on what context the skill needs:

| Skill | Agent Type | Why |
|-------|-----------|-----|
| `maintain-plugin`, `audit-plugin`, `implement-plugin`, `test-plugin`, `map-plugin` | `bannou-dev` | Needs tenets + implementation patterns |
| `schema-plugin` | `bannou-schema` | Needs schema rules + specifications |
| `maintain-doc`, `check-plugin`, `update-permissions` | `bannou` | General project awareness sufficient |

If the skill doesn't appear in this table, use `bannou` (general project context). Store the selected type as `AGENT_TYPE`.

**4. Audit reference documents** — What templates, tenets, or rules does the skill instruct agents to read? Look for the skill's "Phase 0", "Mandatory First Step", or "Internalize Template" section. Note ALL referenced documents. Common references:

| Document | When Referenced |
|----------|----------------|
| `docs/reference/templates/OPERATIONS-TEMPLATE.md` | Operations docs |
| `docs/reference/templates/DEEP-DIVE-TEMPLATE.md` | Plugin deep dives |
| `docs/reference/templates/FAQ-TEMPLATE.md` | FAQ documents |
| `docs/reference/templates/IMPLEMENTATION-MAP-TEMPLATE.md` | Implementation maps |
| `docs/reference/templates/GUIDE-TEMPLATE.md` | Guide documents |
| `docs/reference/templates/PLANNING-TEMPLATE.md` | Planning documents |
| `docs/reference/TENETS.md` | Code/schema audits |
| `docs/reference/tenets/*.md` | Specific tenet categories |
| `docs/reference/SCHEMA-RULES.md` | Schema audits |

### Step 4: Announce Analysis

```
## Skill Analysis

**Skill**: {skill-name}
**Description**: {description from frontmatter}
**Operating directory**: {directory}
**Target type**: {document filename | plugin name}
**Agent type**: {AGENT_TYPE} (project context pre-loaded)
**Audit references**: {list of reference documents}
**Scope**: {all | N random | specific list}

Proceeding to target discovery...
```

---

## Phase 1: Target Discovery

### For scope `all`:

List everything in the operating directory:
```bash
ls {operating-directory}
```

For **document-based** targets: collect all `.md` filenames.
For **plugin-based** targets: derive service names from filenames (strip extension, lowercase, convert underscores/spaces to hyphens).

**Special case — `audit-plugin` with scope `all` or N (random)**: The audit-plugin skill processes one gap per invocation, so launching it against plugins with zero actionable gaps wastes agents. When the skill is `audit-plugin` AND the scope is `all` or a number, run the selection script to pre-filter to plugins with actionable gaps:

```bash
scripts/select-plugins-for-audit.sh 999
```

Parse `SELECTED:` lines from the output to get the filtered target list. If scope is a number N, take at most N from the filtered list. If scope is `all`, use all selected plugins. If the script reports zero plugins with actionable gaps, report "All plugins are fully audited — no actionable gaps remaining" and stop.

This pre-filtering does NOT apply when scope is a specific comma-separated list (the user explicitly chose those targets).

### For scope N (a number):

List the directory and pick N at random:
```bash
ls {operating-directory} | shuf -n {N}
```

### For a specific comma-separated list:

Validate each target exists:
- For document targets: check the file exists in the operating directory
- For plugin targets: check the corresponding file or directory exists

**If any target doesn't exist**, report it and remove from the list. Continue with valid targets.

### Store and Announce

Store the final list as `TARGETS` (ordered alphabetically).

```
**Targets discovered**: {count}
{numbered list of target names}

Proceeding to create task list...
```

---

## Phase 2: Create Tracked Task List (Compaction Survival)

This is the most critical phase for long-running operations. Every task's `description` field must contain the FULL prompt so that if context compacts mid-execution, the orchestrator can read the task list via `TaskList` + `TaskGet` and continue without any information loss.

### Step 1: Construct the Agent Prompt for Each Target

For each target in `TARGETS`, construct a complete, self-contained prompt by combining three parts:

**Part 1 — Batch context and conciseness wrapper:**

```
You are executing a batch-orchestrated skill workflow. You are one of multiple agents running in parallel, each handling a different target.

## CONCISENESS REQUIREMENT (MANDATORY)
- Only report PROBLEMS, SURPRISES, or issues requiring HUMAN JUDGMENT
- If a phase completed successfully with no issues, report ONE LINE: "Phase N: OK"
- Do NOT describe routine successful operations in detail
- Do NOT list files you read unless something was wrong with them
- Do NOT repeat the skill instructions back in your output
- If everything went smoothly, your entire output should be under 20 lines
- ONLY expand on items that genuinely need attention

## TARGET FOR THIS INVOCATION
The argument for this skill invocation is: {target-name}
```

**Part 2 — Complete skill content (VERBATIM, NO CHANGES):**

```
---

{SKILL_CONTENT — the entire skill file content, copied exactly as read}
```

**Part 3 — Closing reminder:**

```
---

REMINDER: You are running in a batch. Be concise. Only report problems or noteworthy surprises. One-line summaries for successful phases. Do not waste context on routine success descriptions.
```

### Step 2: Create Task List

Use `TaskCreate` to create one task per target. Each task uses:

- **`subject`**: `"{skill-name}: {target-name}"` — short identifier for tracking
- **`description`**: The COMPLETE constructed prompt for that target (Part 1 + Part 2 + Part 3, with `{target-name}` substituted)
- **`metadata`**: `{ "target": "{target-name}", "skill": "{skill-name}" }` — for easy filtering

All tasks are created with `pending` status (the default).

**⚠️ CRITICAL**: Each task's `description` field MUST be independently launchable as an agent prompt. It contains the full skill instructions, the target name, and the conciseness wrapper. If every other piece of context is lost to compaction, any single task has everything an agent needs — just read it with `TaskGet` and pass the `description` as the agent's `prompt`.

### Step 3: Announce

```
**Task list created**: {count} entries, all pending
Each entry contains the full {skill-name} prompt ({approximate line count} lines per entry)

Proceeding to batch execution...
```

---

## Phase 3: Batch Execution

Execute agents in rolling batches of 3. Maintain 3 concurrent agents at all times until all targets are processed.

### Initial Launch

1. Use `TaskList` to find up to 3 tasks with `pending` status
2. For EACH, use `TaskGet` to read the full task, then launch an agent:
 - **Tool**: Agent
 - **`subagent_type`**: `{AGENT_TYPE}` (from Phase 0 Step 3 — ensures project instructions are pre-loaded)
 - **`run_in_background`**: `true`
 - **`description`**: The task's `subject` field
 - **`prompt`**: The task's `description` field (this IS the full prompt)
3. Use `TaskUpdate` to set each launched task's status to `in_progress`
4. Send ALL launches in a SINGLE message (parallel tool calls)

### Wait

After launching, **STOP generating output**. Tell the user:
```
Launched {count} agents. Waiting for completion notifications...
```

Do NOT:
- Poll agents
- Resume agents
- Read agent output files
- Sleep or check progress
- Launch more agents preemptively

### Process Completions

When notified of agent completion:

1. Read the agent's result from the notification
2. Use `TaskUpdate` on the corresponding task:
 - Set status → `completed` if successful
 - Set status → `completed` and add a note to `description` if the agent reported problems
 - Note any problems, surprises, or issues the agent reported
3. Use `TaskList` to check for remaining `pending` tasks; launch the next agent(s) to fill back up to 3 concurrent
4. Send status updates and new launches in the SAME message

### Progress Reporting

After processing each batch of completions, briefly report:
```
Progress: {completed}/{total} | {failed} problems | {pending} remaining
{one-line per problem reported by completed agents, if any}
```

### Completion

When all entries are `completed`:
```
All {total} agents complete. {problems} reported problems.
Proceeding to audit...
```

### Error Handling During Execution

| Situation | Action |
|-----------|--------|
| Agent fails or returns errors | Mark as completed with problem note. Continue batch. |
| Agent times out | Mark as completed with timeout note. Continue batch. |
| Agent reports surprises | Note them. Continue batch. Include in report. |
| >50% of agents fail | **STOP**. Report systemic failure. Something is wrong. |

**Do NOT relaunch failed agents** during this phase. Failed targets are addressed in the report and can be re-run with a subsequent `/orchestrate-skill` invocation targeting only the failures.

---

## Phase 4: Audit

After all execution agents complete, launch ONE audit agent to verify the batch results.

### Launch Audit Agent

Launch a FOREGROUND agent (NOT background — you need the results to proceed):

**Agent type**: `bannou-code-reviewer`
**Description**: `"Audit {skill-name} batch results"`

**Prompt** (construct from Phase 0 analysis):

```
You are auditing the results of a batch {skill-name} operation that processed {count} targets.

## YOUR MISSION

{count} agents each independently ran the {skill-name} workflow on a different target. Each agent worked in isolation. Your job is to verify:
1. Results are consistent across all modified documents
2. All documents comply with their template
3. No cross-document issues that isolated agents could not detect

## TARGETS PROCESSED
{numbered list of all targets, noting any that had problems}

## REFERENCE DOCUMENTS — READ THESE FIRST
Read these IN FULL before auditing (use the MCP read tool):

{list each reference document identified in Phase 0, e.g.:
1. `docs/reference/templates/OPERATIONS-TEMPLATE.md` — The template all documents must match
2. `docs/reference/TENETS.md` — Development standards (skim index, read relevant categories)
}

## WHAT TO CHECK

1. **Template compliance**: Does each modified document match its template structure? Check headers, Summary sections, required sections.
2. **Cross-document consistency**: Are naming conventions, formatting, date formats, and terminology consistent across ALL documents?
3. **Completeness**: Did any agent miss sections that should have been updated? Compare against the template.
4. **Accuracy spot-check**: For 3-5 documents, verify a specific claim (does a referenced file exist? does a referenced command work?).

## HOW TO AUDIT EFFICIENTLY

- Read the template FIRST so you know what to look for
- For each target document, scan the header, Summary, and section structure — deep-read only if something looks wrong
- Use Grep to check for patterns across all documents at once (e.g., date format consistency, missing Summary sections)
- You do NOT need to read every line of every document

## OUTPUT FORMAT

### Audit Results

**Documents checked**: {count}
**Template compliance**: {pass count}/{total}
**Cross-document issues**: {count}

#### Findings (ONLY if problems exist)

| # | Document | Issue | Severity |
|---|----------|-------|----------|
| 1 | {name} | {description} | minor/major |

#### Suggested Fixes (ONLY if problems exist)

| # | Document | Fix |
|---|----------|-----|
| 1 | {name} | {specific edit needed} |

**If no issues found**: "All {count} documents pass audit. No issues found."
```

### Process Audit Results

Read the audit agent's findings carefully.

---

## Phase 5: Fix

Based on audit findings, apply fixes:

### No Findings
If the audit returned "No issues found," skip to Phase 6.

### Minor Fixes (Apply Directly) — EXHAUSTIVE LIST

Handle these yourself using the Edit tool. **Only these categories qualify as minor — if a finding doesn't fit one of these exactly, it is major:**
- Date format fixes (wrong date format → correct YYYY-MM-DD)
- Header field additions (missing Last Updated, Scope, Version → add with correct value)
- Whitespace/indentation normalization
- Template section ordering (sections exist but in wrong order → reorder)
- Missing Summary section (add based on document content)
- Casing fixes (wrong capitalization in field names or headers)

### Major Fixes (Launch Fresh Agent)

**Everything not in the minor list above is major.** This includes: factual corrections, rewritten content, added/removed sections, structural reorganization, cross-reference updates across multiple documents. Launch a fresh agent for these:

Launch ONE foreground agent:
- **Description**: `"Fix {skill-name} audit findings"`
- **Prompt**: Include the specific findings, affected documents, relevant template/tenets, and instructions to fix ONLY the identified issues

### Report Fixes

```
Fixes applied: {count} minor (direct edit), {count} major (agent)
{list of fixes}
```

---

## Phase 6: Report

Present the final comprehensive report:

```
## Orchestration Complete

**Skill**: /orchestrate-skill {skill-name} for {scope}
**Date**: {today's date}
**Targets**: {total count}

### Execution Summary

| # | Target | Status | Notes |
|---|--------|--------|-------|
| 1 | {name} | Pass | {one-line summary or "clean"} |
| 2 | {name} | Pass | {one-line summary or "clean"} |
| 3 | {name} | Problem | {what the agent reported} |

### Statistics
- **Targets processed**: {count}
- **Clean passes**: {count}
- **Problems reported**: {count}
- **Audit findings**: {count} ({minor} minor, {major} major)
- **Fixes applied**: {count}

### Problems Requiring Attention
{list any unresolved issues, failed targets, or items needing human judgment}
{if none: "None — all targets processed cleanly."}

### Recommended Follow-Up
- [ ] Review changes with `git diff`
- [ ] Run `make format` before committing
{if failures exist:}
- [ ] Re-run failed targets: `/orchestrate-skill {skill-name} for {failed-target-csv}`
{if problems need human judgment:}
- [ ] Address the {count} items listed above that need human judgment
```

---

## Compaction Recovery Protocol

**If context compaction occurs mid-execution**, the task list contains everything needed to continue:

1. **Call `TaskList`** — see all tasks and their statuses
2. **Identify `pending` tasks** — these still need agents launched
3. **Identify `in_progress` tasks** — these may have completed while context was being compacted; check for completion notifications you may have missed
4. **Use `TaskGet` on any pending task** — its `description` field contains the FULL agent prompt, ready to launch
5. **Resume the Phase 3 execution loop** — launch agents for pending tasks, maintain 3 concurrent
6. **Do NOT re-launch `completed` tasks** — their work is done

**This is why the full prompt is baked into every task's `description`.** After compaction, you may have lost the skill content, the target list, the analysis, and the progress notes from memory. But every task is self-contained and independently launchable — `TaskGet` it, pass `description` as the agent `prompt`, done. The task list persists across compaction.

If the task list itself is somehow empty (catastrophic — should not happen), STOP and tell the user: "Task list lost during compaction. Cannot determine which targets have been processed. Please check `git diff` for completed work and re-run with remaining targets."

---

## Edge Cases

| Situation | Action |
|-----------|--------|
| Skill file not found | STOP. Report: `Skill not found: .claude/skills/{skill-name}/SKILL.md` |
| No targets found in directory | STOP. Report: `No targets found in {directory}` |
| Operating directory not identifiable from skill | STOP. Ask user: "I cannot determine the target directory from the skill content. Please specify." |
| Only 1-2 targets (scope `all` on small directory) | Run normally with fewer than 3 concurrent. No minimum batch size. |
| Target list has duplicates | Deduplicate silently. |
| Skill has no template/tenet references for audit | Run audit against general quality only (formatting, consistency). Note in report. |
| User specifies N larger than available targets | Use all available targets. Note: "Requested {N}, found {available}. Processing all." |
| Scope is `0` or negative | STOP. Report: "Invalid scope: {value}. Use `all`, a positive number, or a comma-separated list." |
