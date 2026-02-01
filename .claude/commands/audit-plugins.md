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

### Step 2: Scan and Filter Plugins

**CRITICAL: Only select plugins that have actionable gaps.**

Use the `scripts/check-plugin-gaps.sh` script to filter plugins:

```bash
# Find all plugins with actionable gaps
for f in docs/plugins/*.md; do
  [ "$(basename "$f")" = "DEEP_DIVE_TEMPLATE.md" ] && continue
  if scripts/check-plugin-gaps.sh "$f" >/dev/null 2>&1; then
    echo "$f"
  fi
done
```

**Report the scan results:**
```
## Plugin Gap Scan

| Plugin | Total | Audit | Fixed | Actionable |
|--------|-------|-------|-------|------------|
| ACCOUNT.md | 6 | 5 | 0 | 1 |
| AUTH.md | 5 | 5 | 0 | 0 (skip) |
| ... | ... | ... | ... | ... |

**Plugins with actionable gaps:** N
**Plugins fully marked/fixed:** M
```

### Step 3: Select Plugins

From the filtered list (plugins WITH actionable gaps), randomly select N:

```bash
# Get list of plugins with gaps, then shuffle and take N
for f in docs/plugins/*.md; do
  [ "$(basename "$f")" = "DEEP_DIVE_TEMPLATE.md" ] && continue
  if scripts/check-plugin-gaps.sh "$f" >/dev/null 2>&1; then
    echo "$f"
  fi
done | shuf -n {N}
```

**If requested count > available plugins with gaps:**
- Report: "Requested {N} but only {M} plugins have actionable gaps. Auditing {M} plugins."
- Select all available plugins with gaps

**If NO plugins have actionable gaps:**
- Report: "All plugins are fully audited! No actionable gaps remaining."
- Exit successfully (this is a good outcome)

**Report selection:**
```
## Sequential Audit: {N} Plugins

Selected plugins (from {M} with actionable gaps):
1. {plugin-1}
2. {plugin-2}
3. {plugin-3}
...

Auditing sequentially...
```

### Step 4: Launch Agents Sequentially

For each selected plugin, launch ONE agent at a time and wait for it to complete before launching the next.

For each plugin, launch with:
- `subagent_type`: `"general-purpose"` (must have access to Skill tool)
- `mode`: `"bypassPermissions"` (REQUIRED - agents need full permissions)
- `prompt`: Tell the agent to invoke the audit-plugin skill (see Agent Prompt section below)
- `description`: `"Audit {plugin-name} plugin"`
- `run_in_background`: `false` (REQUIRED - Skill tool only works in foreground)

**Critical: Launch ONE agent, wait for completion, then launch the next.**

**Critical: Do NOT use `run_in_background: true` - the Skill tool will be denied.**

Example (for 3 plugins - run these ONE AT A TIME):
```
<Task 1 - wait for completion>
  subagent_type: "general-purpose"
  mode: "bypassPermissions"
  description: "Audit account plugin"
  prompt: "Use the Skill tool to invoke the 'audit-plugin' skill with args 'account'"
  run_in_background: false

<Task 2 - after Task 1 completes>
  subagent_type: "general-purpose"
  mode: "bypassPermissions"
  description: "Audit auth plugin"
  prompt: "Use the Skill tool to invoke the 'audit-plugin' skill with args 'auth'"
  run_in_background: false

<Task 3 - after Task 2 completes>
  subagent_type: "general-purpose"
  mode: "bypassPermissions"
  description: "Audit connect plugin"
  prompt: "Use the Skill tool to invoke the 'audit-plugin' skill with args 'connect'"
  run_in_background: false
```

### Step 5: Track Results

After each agent completes, record its result before launching the next:

```
## Progress

| # | Plugin | Result | Action Taken |
|---|--------|--------|--------------|
| 1 | {name} | {EXECUTED/ISSUE_CREATED/NO_GAPS} | {details} |
| 2 | {name} | (running...) | |
```

### Step 6: Final Summary

After all agents complete:

```
## Sequential Audit Complete

| Plugin | Result | Action Taken |
|--------|--------|--------------|
| {name} | {EXECUTED/ISSUE_CREATED/NO_GAPS} | {details} |
| {name} | {EXECUTED/ISSUE_CREATED/NO_GAPS} | {details} |
| {name} | {EXECUTED/ISSUE_CREATED/NO_GAPS} | {details} |

### Summary
- Gaps fixed: {N}
- Issues created: {N}
- No gaps found: {N}
```

## Agent Prompt

Each agent should invoke the actual `/audit-plugin` skill - do NOT summarize or paraphrase the skill instructions.

**The prompt for each agent is exactly this (with plugin name filled in):**

```
Use the Skill tool to invoke the 'audit-plugin' skill with args '{PLUGIN_NAME}'
```

**Example for 3 plugins:**
```
Agent 1 prompt: "Use the Skill tool to invoke the 'audit-plugin' skill with args 'account'"
Agent 2 prompt: "Use the Skill tool to invoke the 'audit-plugin' skill with args 'auth'"
Agent 3 prompt: "Use the Skill tool to invoke the 'audit-plugin' skill with args 'connect'"
```

**Why this matters:** The `/audit-plugin` skill has detailed instructions including EXECUTE vs CREATE_ISSUE criteria, forbidden escape hatches, and investigation requirements. Summarizing loses critical context. Let the skill speak for itself.

## Important Notes

- **Sequential execution**: Agents run one at a time, each completing before the next starts
- **Unique plugins**: Each agent gets a different plugin - no overlap
- **Foreground mode required**: Background agents cannot use Skill tool
- **Full permissions**: `bypassPermissions` mode ensures Edit/Bash/etc work without prompting

## Error Handling

- **All plugins fully audited**: Report success: "All plugins are fully audited! No actionable gaps remaining."
- **Plugin has no gaps** (shouldn't happen with pre-filtering): Agent reports "no gaps" and finishes
- **Agent fails**: Report the failure, continue with next plugin
- **Build fails in one agent**: That agent stops, continue with next plugin
- **Fewer plugins with gaps than requested**: Audit all available plugins with gaps, report the adjusted count

## Limits

Recommended limits:
- **Minimum**: 2 (otherwise just use `/audit-plugin`)
- **Maximum**: 5-7 (diminishing returns, context limits)
- **Default suggestion**: 3 if user asks "how many?"
