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
1. Selects N unique plugins at random
2. Launches N agents sequentially (one completes before the next starts)
3. Each agent runs the full `/audit-plugin` workflow on its assigned plugin

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

### Step 2: Discover and Select Plugins

Use bash for true randomness - select N unique plugins in one command:
```bash
ls docs/plugins/*.md | grep -v DEEP_DIVE_TEMPLATE | shuf -n {N}
```

Where `{N}` is the requested count from the argument.

If requested count > available plugins:
- The `shuf` command will return all available (fewer than requested)
- Report: "Requested {N} but only {M} plugins available. Auditing {M} plugins."

**Report selection:**
```
## Sequential Audit: {N} Plugins

Selected plugins:
1. {plugin-1}
2. {plugin-2}
3. {plugin-3}
...

Auditing sequentially...
```

### Step 3: Launch Agents Sequentially

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

### Step 4: Track Results

After each agent completes, record its result before launching the next:

```
## Progress

| # | Plugin | Result | Action Taken |
|---|--------|--------|--------------|
| 1 | {name} | {EXECUTED/ISSUE_CREATED/NO_GAPS} | {details} |
| 2 | {name} | (running...) | |
```

### Step 5: Final Summary

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

- If a plugin has no gaps: Agent reports "no gaps" and finishes (not an error)
- If an agent fails: Report the failure, continue with next plugin
- If build fails in one agent: That agent stops, continue with next plugin
- If all plugins already have markers: Report "all plugins have active work"

## Limits

Recommended limits:
- **Minimum**: 2 (otherwise just use `/audit-plugin`)
- **Maximum**: 5-7 (diminishing returns, context limits)
- **Default suggestion**: 3 if user asks "how many?"
