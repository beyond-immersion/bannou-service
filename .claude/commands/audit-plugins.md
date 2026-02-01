---
description: Launch multiple audit-plugin agents sequentially, each auditing a different plugin. Orchestrates batch gap processing across the codebase.
argument-hint: "<count> - Number of plugins to audit sequentially (e.g., '3', '5'). Each agent gets a unique plugin."
---

# Sequential Plugin Auditor Command

You are executing the `/audit-plugins` workflow. This orchestrates multiple `/audit-plugin` runs sequentially, each targeting a unique plugin.

## Why Sequential (Not Parallel)

Background agents cannot use the Skill tool - it gets auto-denied with "prompts unavailable". This is a Claude Code architectural limitation. Foreground agents work correctly, so we run them one at a time.

## Purpose

When you want to make progress on multiple plugins in a batch, this command:
1. Scans ALL plugins to find ones with actionable gaps
2. Selects N unique plugins at random FROM THOSE WITH GAPS
3. Launches N agents sequentially (one completes before the next starts)
4. Each agent runs the full `/audit-plugin` workflow on its assigned plugin

## Workflow

### Step 1: Parse Argument

The argument MUST be a positive integer.

```
/audit-plugins 3    → Audit 3 plugins sequentially
/audit-plugins 5    → Audit 5 plugins sequentially
```

If no argument or invalid argument:
- Report error: "Usage: /audit-plugins <count> (e.g., /audit-plugins 3)"
- Do not proceed

### Step 2: Run Selection Script

Run this command (replace `{N}` with the count from the argument):

```bash
scripts/select-plugins-for-audit.sh {N}
```

The script outputs three types of lines:
- `SCAN: PLUGIN.md|TOTAL=N AUDIT=N FIXED=N ACTIONABLE=N|EXIT_CODE` - One per plugin
- `SELECTED: plugin-name` - One per selected plugin (lowercase, no extension)
- `SUMMARY: TOTAL_SCANNED=N WITH_GAPS=N SELECTED=N` - Final counts

### Step 3: Parse Output and Report

Parse the script output to build the scan table and selection list.

**Report format:**
```
## Plugin Gap Scan

| Plugin | Total | Audit | Fixed | Actionable | Status |
|--------|-------|-------|-------|------------|--------|
| ACCOUNT.md | 6 | 5 | 0 | 1 | ✓ |
| AUTH.md | 5 | 5 | 0 | 0 | (skip) |
| ... | ... | ... | ... | ... | ... |

**Summary:** {TOTAL_SCANNED} plugins scanned, {WITH_GAPS} with actionable gaps

## Selected for Audit: {SELECTED} Plugins

1. {plugin-1}
2. {plugin-2}
3. {plugin-3}

Auditing sequentially...
```

**Edge cases:**
- If `WITH_GAPS=0`: Report "All plugins are fully audited! No actionable gaps remaining." and exit successfully.
- If `SELECTED < requested count`: Report "Requested {N} but only {WITH_GAPS} plugins have actionable gaps. Auditing {WITH_GAPS} plugins."

### Step 4: Launch Agents Sequentially

For each `SELECTED:` plugin from the script output, launch ONE agent at a time and wait for completion before the next.

**Agent configuration:**
- `subagent_type`: `"general-purpose"` (must have access to Skill tool)
- `mode`: `"bypassPermissions"` (REQUIRED - agents need full permissions)
- `description`: `"Audit {plugin-name} plugin"`
- `prompt`: `"Use the Skill tool to invoke the 'audit-plugin' skill with args '{plugin-name}'"`
- `run_in_background`: `false` (REQUIRED - Skill tool only works in foreground)

**Critical: Launch ONE agent, wait for completion, then launch the next.**

**Critical: Do NOT use `run_in_background: true` - the Skill tool will be denied.**

Example for 3 selected plugins (mesh, messaging, escrow):
```
Agent 1: prompt = "Use the Skill tool to invoke the 'audit-plugin' skill with args 'mesh'"
(wait for completion)
Agent 2: prompt = "Use the Skill tool to invoke the 'audit-plugin' skill with args 'messaging'"
(wait for completion)
Agent 3: prompt = "Use the Skill tool to invoke the 'audit-plugin' skill with args 'escrow'"
```

### Step 5: Track Results

After each agent completes, record its result before launching the next:

```
## Progress

| # | Plugin | Result | Action Taken |
|---|--------|--------|--------------|
| 1 | mesh | EXECUTED | Fixed 2 gaps |
| 2 | messaging | (running...) | |
| 3 | escrow | (pending) | |
```

### Step 6: Final Summary

After all agents complete:

```
## Sequential Audit Complete

| Plugin | Result | Action Taken |
|--------|--------|--------------|
| mesh | EXECUTED | Fixed 2 gaps |
| messaging | ISSUE_CREATED | Issue #123 for design question |
| escrow | NO_GAPS | All gaps already marked |

### Summary
- Gaps fixed: {N}
- Issues created: {N}
- No gaps found: {N}
```

## Important Notes

- **Sequential execution**: Agents run one at a time, each completing before the next starts
- **Unique plugins**: Each agent gets a different plugin - no overlap
- **Foreground mode required**: Background agents cannot use Skill tool
- **Full permissions**: `bypassPermissions` mode ensures Edit/Bash/etc work without prompting
- **WEBSITE skipped**: The Website plugin is entirely stub code - the script excludes it automatically

## Error Handling

- **All plugins fully audited**: Report success and exit
- **Plugin has no gaps** (shouldn't happen with pre-filtering): Agent reports "no gaps" and finishes
- **Agent fails**: Report the failure, continue with next plugin
- **Build fails in one agent**: That agent stops, continue with next plugin

## Limits

Recommended limits:
- **Minimum**: 2 (otherwise just use `/audit-plugin`)
- **Maximum**: 5-7 (diminishing returns, context limits)
- **Default suggestion**: 3 if user asks "how many?"
