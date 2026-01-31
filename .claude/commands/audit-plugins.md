---
description: Launch multiple audit-plugin agents in parallel, each auditing a different plugin. Orchestrates parallel gap processing across the codebase.
argument-hint: "<count> - Number of plugins to audit in parallel (e.g., '3', '5'). Each agent gets a unique plugin."
---

# Parallel Plugin Auditor Command

You are executing the `/audit-plugins` workflow. This orchestrates multiple `/audit-plugin` runs in parallel, each targeting a unique plugin.

## Purpose

When you want to make progress on multiple plugins simultaneously, this command:
1. Selects N unique plugins at random
2. Launches N agents in parallel
3. Each agent runs the full `/audit-plugin` workflow on its assigned plugin

## Workflow

### Step 1: Parse Argument

The argument MUST be a positive integer.

```
/audit-plugins 3    → Launch 3 agents
/audit-plugins 5    → Launch 5 agents
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
- Report: "Requested {N} but only {M} plugins available. Launching {M} agents."

**Report selection:**
```
## Parallel Audit: {N} Plugins

Selected plugins:
1. {plugin-1}
2. {plugin-2}
3. {plugin-3}
...

Launching {N} agents in parallel...
```

### Step 4: Launch Agents

Use the Task tool to launch multiple agents **in a single message** (parallel execution).

For each selected plugin, launch with:
- `subagent_type`: `"general-purpose"` (must have access to Skill tool)
- `prompt`: Tell the agent to invoke the audit-plugin skill (see Agent Prompt section below)
- `description`: `"Audit {plugin-name} plugin"`
- `run_in_background`: `true` (so they run in parallel)

**Critical: Launch ALL agents in ONE message with multiple Task tool calls.**

**Critical: Use the EXACT prompt format from the Agent Prompt section - do NOT summarize or paraphrase the audit-plugin instructions yourself.**

Example (for 3 plugins):
```
<Task 1>
  subagent_type: "general-purpose"
  description: "Audit account plugin"
  prompt: "Use the Skill tool to invoke the 'audit-plugin' skill with args 'account'"
  run_in_background: true

<Task 2>
  subagent_type: "general-purpose"
  description: "Audit auth plugin"
  prompt: "Use the Skill tool to invoke the 'audit-plugin' skill with args 'auth'"
  run_in_background: true

<Task 3>
  subagent_type: "general-purpose"
  description: "Audit connect plugin"
  prompt: "Use the Skill tool to invoke the 'audit-plugin' skill with args 'connect'"
  run_in_background: true
```

### Step 5: Monitor Progress

After launching, report:

```
## Agents Launched

| Agent | Plugin | Status | Output File |
|-------|--------|--------|-------------|
| 1 | {name} | Running | {path} |
| 2 | {name} | Running | {path} |
| 3 | {name} | Running | {path} |

Use `Read` tool on output files to check progress.
Agents will complete independently.
```

### Step 6: Summary (When All Complete)

If you're still in context when agents complete, or if user asks for status:

```
## Parallel Audit Complete

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

**Why this matters:** The `/audit-plugin` skill has 469 lines of detailed instructions including EXECUTE vs CREATE_ISSUE criteria, forbidden escape hatches, and investigation requirements. Summarizing loses critical context. Let the skill speak for itself.

## Important Notes

- **Parallel execution**: All agents run simultaneously, not sequentially
- **Unique plugins**: Each agent gets a different plugin - no overlap
- **Independent results**: Each agent succeeds or fails independently
- **Background mode**: Agents run in background so you can check on them later
- **Resource consideration**: Don't launch too many (3-5 is reasonable)

## Error Handling

- If a plugin has no gaps: Agent reports "no gaps" and finishes (not an error)
- If an agent fails: Other agents continue independently
- If build fails in one agent: That agent stops, others continue
- If all plugins already have markers: Report "all plugins have active work"

## Limits

Recommended limits:
- **Minimum**: 2 (otherwise just use `/audit-plugin`)
- **Maximum**: 5-7 (resource constraints, parallel context limits)
- **Default suggestion**: 3 if user asks "how many?"
