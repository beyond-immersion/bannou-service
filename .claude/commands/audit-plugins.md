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
- `subagent_type`: `"Explore"` or appropriate agent type
- `prompt`: The full audit-plugin workflow instructions for that specific plugin
- `description`: `"Audit {plugin-name} plugin"`
- `run_in_background`: `true` (so they run in parallel)

**Critical: Launch ALL agents in ONE message with multiple Task tool calls.**

Example (for 3 plugins):
```
<Task 1>
  description: "Audit account plugin"
  prompt: "Execute /audit-plugin workflow for the 'account' plugin. [full instructions]"
  run_in_background: true

<Task 2>
  description: "Audit auth plugin"
  prompt: "Execute /audit-plugin workflow for the 'auth' plugin. [full instructions]"
  run_in_background: true

<Task 3>
  description: "Audit connect plugin"
  prompt: "Execute /audit-plugin workflow for the 'connect' plugin. [full instructions]"
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

## Agent Prompt Template

Each launched agent should receive this prompt (with plugin name filled in):

```
You are running the /audit-plugin workflow for the '{PLUGIN_NAME}' plugin.

## Your Task
1. Read `docs/plugins/{PLUGIN_NAME}.md`
2. Find the FIRST actionable gap (no AUDIT marker, no issue link)
3. If no gaps found, report "No actionable gaps" and finish
4. If gap found, investigate thoroughly:
   - Read all relevant source code in `plugins/lib-{service}/`
   - Check integration points, TENET compliance, scope
5. Make determination: EXECUTE or CREATE_ISSUE
6. Take action:
   - EXECUTE: Mark doc, implement fix, verify build
   - CREATE_ISSUE: Mark doc, create GitHub issue with investigation
7. Report results

## Critical Rules
- Handle ONE gap only
- Preserve existing AUDIT markers
- Follow all Bannou TENETs
- Verify build after any code changes

## Reference Files
- Template: docs/plugins/DEEP_DIVE_TEMPLATE.md
- TENETs: docs/reference/TENETS.md
```

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
