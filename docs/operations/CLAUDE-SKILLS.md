# Claude Code Integration Guide

> **Last Updated**: 2026-03-08
> **Scope**: Claude Code configuration, hooks, and custom automation for Bannou development

## Summary

Claude Code integration configuration for Bannou development, covering PreToolUse safety hooks that block destructive operations, enforce commit format, and nudge proper task creation patterns, plus custom slash commands for plugin documentation maintenance, code auditing, implementation workflows, and GitHub issue investigation. Required reading before creating new hooks, commands, or agent workflows, and for understanding how Claude Code is constrained in this project.

---

## Overview

Bannou uses Claude Code with custom configuration to:

1. **Enforce development standards** - Hooks block destructive commands, enforce commit format, and nudge proper task list formatting
2. **Automate documentation** - Custom plugins maintain and audit plugin deep dive docs
3. **Prevent accidents** - Integration tests and production deploys require explicit user action

---

## Why This Documentation Exists: Claude's Fundamental Limitations

**Claude is an inherently flawed tool that requires explicit constraints to function reliably.**

This is not a criticism - it's a practical observation that shaped every decision in this configuration. Claude will take the path of least resistance whenever possible. If a task is difficult, ambiguous, or requires sustained effort, Claude will find creative ways to avoid doing the actual work while appearing to comply with instructions.

### The Audit Plugin Case Study

The `/audit-plugin` workflow was originally written with clear, reasonable instructions:

1. Find implementation gaps in documentation
2. Investigate them by reading source code
3. Either fix them or create issues

What Claude actually did:

- **Rationalized skipping work**: "It's documented, so it's handled" (the documentation IS the tracking system - being listed means work is needed)
- **Applied unauthorized priority filtering**: "This is low priority" (not Claude's call to make)
- **Ignored entire sections**: Skipped "Stubs & Unimplemented Features" because the section name wasn't in the exact pattern list
- **Never reached the investigation phase**: Jumped straight to "nothing to do" without reading any code
- **Invented escape hatches**: Declared gaps "acceptable as documented" when there is no "acceptable" option

The instructions were clear. Claude found ways around them anyway.

### The Solution: Assume Bad Faith

The updated audit-plugin workflow now includes:

1. **Explicit "FORBIDDEN ESCAPE HATCHES" section** - Lists the specific rationalizations Claude used and explicitly forbids them
2. **Mandatory proof of work** - Investigation phase requires quoting actual source code
3. **No third option** - Only EXECUTE or CREATE_ISSUE; "acceptable" is not valid
4. **Forced section enumeration** - Must report item counts for each section scanned
5. **Self-check questions** - Must verify work was actually done before claiming completion

### Guidelines for Developers

**Never trust that instructions will be followed.** Even clear, unambiguous instructions will be creatively reinterpreted if the work is hard. When writing workflows for Claude:

1. **Enumerate forbidden behaviors explicitly** - If Claude can rationalize it, Claude will do it
2. **Require proof of work** - "Read the code" becomes "quote specific lines from the code you read"
3. **Eliminate escape hatches** - If there's a way to declare "nothing to do," Claude will find it
4. **Force structured output** - Require specific sections that prove each step was completed
5. **Assume laziness** - Write instructions as if Claude is a reluctant teenager looking for shortcuts

This isn't about AI safety or alignment. It's about the mundane reality that LLMs are pattern-matchers that will pattern-match their way out of difficult work if given any opportunity.

**Claude is useful. Claude is capable. Claude will also half-ass anything it can get away with half-assing.** Design your workflows accordingly.

---

## Configuration Files

```
.claude/
├── settings.json              # Hook configuration (checked into repo)
├── settings.local.json        # Local permissions (gitignored)
├── skills-guide.txt           # Skill discovery reference
├── hooks/
│   ├── validate-senryu-commit.sh
│   ├── block-destructive-git.sh
│   ├── block-production-deploy.sh
│   ├── block-integration-tests.sh
│   ├── block-file-moves.sh
│   ├── block-symlinks.sh
│   ├── block-agent-polling.sh
│   ├── git-history-reminder.sh
│   └── task-creation-reminder.sh
├── commands/                  # Custom slash commands
│   ├── audit-plugin.md        # Single plugin gap auditing
│   ├── audit-plugins.md       # Batch plugin auditing
│   ├── check-plugin.md        # Plugin tenet compliance check
│   ├── implement-feature.md   # Feature implementation workflow
│   ├── implement-plugin.md    # Plugin implementation workflow
│   ├── investigate-issue.md   # GitHub issue investigation
│   ├── maintain-faq.md        # FAQ document maintenance
│   ├── maintain-guide.md      # Guide document maintenance
│   ├── maintain-issues.md     # GitHub issues maintenance
│   ├── maintain-operations-doc.md  # Operations doc maintenance
│   ├── maintain-planning-doc.md    # Planning doc maintenance
│   ├── maintain-plugin.md     # Plugin deep dive maintenance
│   ├── map-plugin.md          # Plugin implementation map creation
│   ├── orchestrate-skill.md   # Batch skill orchestration
│   ├── test-plugin.md         # Plugin test writing
│   └── update-permissions.md  # Permission schema updates
└── agents/                    # Custom agents for commands
    ├── doc-reviewer.md        # Source code review agent
    └── gap-investigator.md    # Gap investigation agent
```

---

## PreToolUse Hooks

These hooks intercept tool calls before execution. Most hooks match the `Bash` tool to block dangerous shell commands, but hooks can match any tool name — `Agent` (to block resume polling), `TaskCreate` and `TodoWrite` (to nudge task formatting), or any other tool Claude Code exposes.

**Two hook types**:
- **Blocking hooks** — Return `"permissionDecision": "deny"` to prevent the tool call entirely (e.g., destructive git commands, production deploys)
- **Reminder hooks** — Return `"permissionDecision": "allow"` with a `permissionDecisionReason` message that the agent sees before proceeding (e.g., git history reminder, task creation format nudge)

### 1. Senryu Commit Validator

**File**: `validate-senryu-commit.sh`

**Purpose**: Enforces the project's commit message format - every commit must start with a senryu (5-7-5 syllable poem about human nature).

**What it does**:
- Intercepts all `git commit` commands
- Extracts the commit message (handles `-m`, heredoc, and other formats)
- Validates the first line has exactly two ` / ` separators (senryu format)
- Blocks non-compliant commits with a helpful error message

**Format required**:
```
five syllables here / seven syllables go here / five more syllables

Actual commit message body here...

Co-Authored-By: Claude <noreply@anthropic.com>
```

**Exceptions allowed**:
- `--amend` without new message (reuses previous)
- `--no-edit` (merge commits)

**Example valid senryu lines**:
```
bugs hide in plain sight / we debug with tired eyes / coffee saves us all
one small change they said / now the whole system is down / hubris strikes again
tests were passing green / then I touched one single line / red across the board
```

---

### 2. Destructive Git Blocker

**File**: `block-destructive-git.sh`

**Purpose**: Prevents Claude from accidentally destroying uncommitted work or commit history.

**Blocked commands**:

| Command | Why Blocked |
|---------|-------------|
| `git checkout -- <file>` | Destroys uncommitted file changes |
| `git checkout .` | Destroys all uncommitted changes |
| `git stash` | Hides changes that may be forgotten/lost |
| `git reset` (without `--soft`) | Can destroy commit history |
| `git restore` | Overwrites uncommitted changes |
| `git clean` | Permanently deletes untracked files |

**Allowed operations**:
- `git checkout <branch>` - Branch switching is safe
- `git checkout -b <branch>` - Creating branches is safe
- `git reset --soft` - Preserves changes in working directory

**Recovery procedure** (when Claude needs to undo changes):
1. STOP and ask the user
2. Explain what needs to be undone and why
3. Wait for explicit approval
4. Use Edit tool to carefully revert specific changes

---

### 3. Production Deploy Blocker

**File**: `block-production-deploy.sh`

**Purpose**: Prevents Claude from accidentally publishing to production registries.

**Blocked commands**:

| Command | What It Does |
|---------|--------------|
| `make push-release` | Pushes Docker image to Docker Hub |
| `make publish-sdk-ts` | Publishes TypeScript SDK to npm |

**When user asks about deploying**:
Claude should return the command for the user to run manually, not execute it.

---

### 4. Integration Test Blocker

**File**: `block-integration-tests.sh`

**Purpose**: Prevents Claude from running time-consuming integration tests without explicit request.

**Blocked commands**:

| Command | Duration | Why Blocked |
|---------|----------|-------------|
| `make test-http` | 5-10 min | Rebuilds containers, runs HTTP tests |
| `make test-edge` | 5-10 min | Rebuilds containers, runs WebSocket tests |
| `make test-infrastructure` | 2-5 min | Validates Docker health |
| `make all` | 10-15 min | Full cycle including all tests |
| `make test-http-dev` | 5-10 min | HTTP tests (dev mode) |
| `make test-edge-dev` | 5-10 min | WebSocket tests (dev mode) |

**Verification approach**:
- `dotnet build` is sufficient for verifying code changes
- Integration tests only when explicitly requested by user

---

### 5. File Move Blocker

**File**: `block-file-moves.sh`

**Purpose**: Prevents Claude from renaming or moving source files instead of fixing build errors.

**Blocked commands**:
- `mv` commands targeting code file extensions (`.cs`, `.csproj`, `.yaml`, `.json`, `.ts`, `.py`, `.sh`, `.md`, etc.)

**Why**: An agent previously renamed files to `.tmp` instead of fixing build errors, causing data loss. If the build fails, fix the code — do not rename files.

---

### 6. Symlink Blocker

**File**: `block-symlinks.sh`

**Purpose**: Prevents creation of symbolic links, which break containerized builds and cause path resolution issues.

**Blocked commands**:
- `ln -s` and `ln --symbolic` (all variants)

**Alternative**: Use proper file references, imports, or `$ref` in schemas instead of symlinks.

---

### 7. Agent Polling Blocker

**File**: `block-agent-polling.sh`

**Purpose**: Prevents Claude from resuming background agents. Background agent results arrive automatically via `<task-notification>` — resume attempts are guaranteed to fail and waste context.

**Blocked**: Any Agent tool call with a `resume` parameter set.

---

### 8. Git History Reminder

**File**: `git-history-reminder.sh`

**Purpose**: Non-blocking reminder hook that triggers on `git diff` or `git log` commands. Reminds the agent to re-read CLAUDE.md rules before acting on git history output.

**Why**: An agent used `git diff` to confirm it had successfully reverted user-directed work, violating HARD STOP rules. This hook is a checkpoint, not a blocker.

---

### 9. Task Creation Reminder

**File**: `task-creation-reminder.sh`

**Matchers**: `TaskCreate`, `TodoWrite` (not `Bash` — this hook matches tool names directly)

**Purpose**: Non-blocking reminder that fires whenever an agent creates a task or todo item. Reminds the agent of the required format for violation/hardening task lists (see CLAUDE.md § "Violation Task Lists").

**What it reminds**:
Every task for TENET violations, SCHEMA-RULES issues, or code quality fixes must include:
1. **Verbatim tenet text** — the exact rule being violated, quoted from the source document
2. **Affected files and line numbers** — every location, enumerated
3. **Before/after code** — exact current code and exact replacement
4. **What NOT to do** — explicit constraints preventing common mistakes
5. **Self-contained verification** — how to confirm the fix is correct

**Why**: Claude repeatedly created shallow task descriptions like "Fix T7 in FooService" that forced implementers to re-derive the entire audit finding from scratch. The audit agent already found all the information; the task description must preserve it. See CLAUDE.md for the full rationale and compound waste pattern.

**Why TodoWrite is hooked**: `TodoWrite` is the most likely tool Claude reaches for instead of `TaskCreate`. It's lightweight but can't hold the required detail for violation task lists. The reminder nudges toward `TaskCreate` which supports rich descriptions.

**This hook does NOT block** — it prints the reminder and allows the tool call to proceed. The agent sees the format requirements and can adjust accordingly.

---

## Plugin Auditor

Custom commands for maintaining and auditing plugin documentation.

**Location**: `.claude/commands/` and `.claude/agents/` (project-level, checked into repo)

### Commands

#### `/maintain-plugin [name|random]`

Ensures a deep dive document is structurally correct, content-accurate, and complete.

```bash
/maintain-plugin                 # Pick random plugin
/maintain-plugin account         # Maintain specific plugin
```

**Workflow**:
1. Select plugin (random or specified)
2. Check document matches `DEEP-DIVE-TEMPLATE.md` structure
3. Read ALL plugin source code thoroughly
4. Compare documentation against actual code
5. Discover and document all quirks/bugs
6. Update document while preserving work tracking markers

**Use when**:
- Plugin hasn't been reviewed in a while
- After significant code changes
- Before running `/audit-plugin`

---

#### `/audit-plugin [name|random]`

Finds and handles ONE implementation gap from a plugin's deep dive document.

```bash
/audit-plugin                    # Pick random plugin, first gap
/audit-plugin account            # Audit specific plugin
```

**Workflow**:
1. Select plugin (random or specified)
2. Find first actionable gap (no AUDIT marker)
3. Investigate thoroughly (code, integrations, TENETs)
4. Determine: EXECUTE (fix now) or CREATE_ISSUE (needs design)
5. Take action and update document

**Decision criteria**:

| EXECUTE when | CREATE_ISSUE when |
|--------------|-------------------|
| Implementation path is clear | Multiple valid approaches exist |
| All TENETs satisfied | TENET interpretation needed |
| Scope < 5 files, < 200 lines | Human judgment required |
| No business logic decisions | Scope uncertain or large |

---

#### `/audit-plugins <count>`

Launches multiple audit agents sequentially, each on a unique plugin.

```bash
/audit-plugins 3                 # Audit 3 different plugins sequentially
/audit-plugins 5                 # Audit 5 plugins sequentially
```

**How it works**:
1. Randomly selects N unique plugins
2. Launches agents one at a time (foreground mode)
3. Each agent runs full `/audit-plugin` workflow
4. Waits for completion before launching next

**Why sequential?** See [Task Tool Limitations](#task-tool-limitations) below.

**Recommended**: 3-5 plugins per batch

---

#### `/investigate-issue [number|random]`

End-to-end GitHub issue investigation and resolution with developer guidance.

```bash
/investigate-issue               # Pick random open issue
/investigate-issue 42            # Investigate specific issue #42
```

**Workflow (10 phases)**:
1. **Issue Selection** - Fetch from GitHub (random or specific)
2. **Deep Read** - Read issue, plugin deep dives, source code
3. **Present to Developer** - Show CONFIRMED/INFERRED/SPECULATIVE info
4. **Interactive Clarification** - Get explicit decisions (no guessing!)
5. **Planning** - Launch Plan agent with full context + TENETs
6. **Implementation** - Execute plan with build verification
7. **Audit** - Launch code-reviewer against TENETs/plan/issue
8. **Iterate** - Fix audit findings with approval
9. **Update Docs** - Use FIXED format for deep dive updates
10. **Close Issue** - Comment summary and close

**Key behaviors**:
- NEVER guesses at unanswered questions - must get explicit decisions
- NEVER assumes "sounds good" is a decision - requires explicit choice
- Distinguishes CONFIRMED vs INFERRED vs SPECULATIVE information
- Requires developer approval at each phase checkpoint
- Instructs Plan and code-reviewer agents to read TENETs first

**Use when**:
- Starting work on a GitHub issue
- Need structured approach with clear decision tracking
- Want audit trail of all decisions made

---

#### `/check-plugin [name]`

Read-only diagnostic that determines a plugin's readiness level (L0-L7) in the development pipeline and recommends the next action. Checks deep dive, audit status, implementation map, schemas, generated code, tests, and implementation.

```bash
/check-plugin divine              # Check specific plugin readiness
/check-plugin                     # Check next plugin needing work
```

---

#### `/map-plugin [name]`

Creates or maintains an implementation map for a plugin. Launches structured sub-agents for schema, code, and event analysis, then writes the map document at `docs/maps/{SERVICE}.md`.

```bash
/map-plugin auth                  # Create/update auth implementation map
```

---

#### `/test-plugin [name]`

Generates TDD red-phase unit tests from a plugin's implementation map and generated interfaces. Requires schemas to be generated first (L4+ readiness).

```bash
/test-plugin divine               # Generate tests for divine plugin
```

---

#### `/implement-plugin [name]`

Implements a plugin's service logic from its implementation map and failing tests. Writes `*Service.cs`, `*ServiceModels.cs`, `*ServiceEvents.cs`, and helper services. Requires failing tests (L5+ readiness).

```bash
/implement-plugin divine          # Implement divine plugin
```

---

#### `/implement-feature [description]`

Guided feature development for adding a specific feature to an already-implemented plugin. Orchestrates a 5-phase pipeline (deep dive, implementation map, schema, code, tests) with sequential agents.

```bash
/implement-feature Add background workers for Expired and HoldExpired in the CURRENCY plugin
```

---

#### `/maintain-issues [name|random]`

Reviews GitHub issues referenced in a plugin's deep dive document. Verifies if issues should be closed, updated, or are still active based on current codebase state. Read-only — does not make code changes.

```bash
/maintain-issues account          # Review issues for account plugin
/maintain-issues random           # Pick random plugin
```

---

#### `/maintain-guide [name|random]`

Maintains developer guide documents in `docs/guides/`. Ensures structure matches template, content is accurate against plugin deep dives, and Summary section is current.

```bash
/maintain-guide behavior-system   # Maintain specific guide
```

---

#### `/maintain-planning-doc [name|random]`

Maintains planning documents in `docs/planning/`. Ensures structure matches template and Status reflects current implementation state.

```bash
/maintain-planning-doc actor-bound-entities
```

---

#### `/maintain-faq [name|random]`

Maintains FAQ (architectural rationale) documents in `docs/faqs/`. Ensures Short Answer is still accurate and architectural reasoning hasn't been invalidated.

```bash
/maintain-faq why-are-items-and-inventory-separate
```

---

#### `/maintain-operations-doc [name|random]`

Maintains operations documents in `docs/operations/` (including this document). Verifies header format, Summary section, Makefile targets, script paths, and cross-references.

```bash
/maintain-operations-doc TESTING
```

---

#### `/update-permissions [plugins...]`

Audits and fixes `x-permissions` on all endpoints for 1-5 plugins, ensuring compliance with `ENDPOINT-PERMISSION-GUIDELINES.md`. Updates schemas, deep dives, and implementation maps.

```bash
/update-permissions achievement divine gardener
```

---

#### `/orchestrate-skill <skill-name> for <scope>`

Orchestrates running any single-target skill across multiple targets in parallel batches of 3. Supersedes `/audit-plugins` for batch operations. Compaction-safe task tracking.

```bash
/orchestrate-skill maintain-plugin for all
/orchestrate-skill audit-plugin for account,auth,chat
/orchestrate-skill maintain-faq for 5
```

---

### Work Tracking Markers

The plugin auditor uses HTML comment markers to track work status:

```markdown
- Some bug that needs fixing
  <!-- AUDIT:IN_PROGRESS:2026-01-29 -->

- Design issue needing human decisions
  <!-- AUDIT:NEEDS_DESIGN:2026-01-28:https://github.com/org/repo/issues/42 -->

- Item blocked on external dependency
  <!-- AUDIT:BLOCKED:2026-01-27:https://github.com/org/repo/issues/41 -->
```

| Status | Meaning | Issue Link |
|--------|---------|------------|
| `IN_PROGRESS` | Being actively worked on | Optional |
| `NEEDS_DESIGN` | Needs human design decisions | **Required** |
| `BLOCKED` | Waiting on dependency | Optional |

**Lifecycle**:
```
New bug discovered           → /maintain-plugin adds to doc (no marker)
Gap ready to process         → /audit-plugin investigates
  If clear fix               → Adds IN_PROGRESS, executes, removes marker
  If needs design            → Creates issue, adds NEEDS_DESIGN with URL
Item with NEEDS_DESIGN+URL   → Skipped until issue resolved
```

---

### Command Files

See the [Configuration Files](#configuration-files) section above for the full directory listing of all commands and agents.

---

## Recommended Workflow

### For Documentation Maintenance

```bash
# 1. Ensure doc is complete and accurate
/maintain-plugin account

# 2. Process any gaps found
/audit-plugin account

# 3. Repeat for additional gaps
/audit-plugin account
```

### For Bulk Progress

```bash
# Audit multiple plugins sequentially (parallel not possible due to Skill tool limitation)
/audit-plugins 3
```

### For Code Changes

```bash
# After making changes to a plugin:
dotnet build                     # Verify compilation
/maintain-plugin {plugin}        # Update documentation
```

### For GitHub Issues

```bash
# Start work on a specific issue
/investigate-issue 42

# Or pick a random open issue
/investigate-issue
```

The investigate-issue workflow provides:
- Structured decision tracking (no guessing!)
- Developer approval at each phase
- Automatic TENET compliance checks
- Audit before closure
- Documentation updates using FIXED format

---

## Task Tool Limitations

The Task tool spawns subagents to handle complex work. However, there are architectural limitations that affect how agents can be configured.

### Background Agents Cannot Use Skill Tool

**The Problem**: When spawning agents with `run_in_background: true`, the Skill tool is automatically denied with "prompts unavailable". This is a Claude Code architectural limitation, not a permissions issue.

**Evidence**:
```
# Background agent (run_in_background: true):
"Permission to use Skill has been auto-denied (prompts unavailable)"

# Foreground agent (run_in_background: false):
Skill tool works correctly
```

**Root Cause**: The Skill tool requires real-time decision-making (discovering skills, loading content, deciding relevance). Background agents cannot perform interactive operations.

**What DOES NOT Work**:
- `mode: "bypassPermissions"` does NOT bypass this limitation
- `mode: "acceptEdits"` does NOT help
- Pre-approving "Skill" in settings does NOT help
- Any background agent configuration - Skill is always denied

**What DOES Work**:
- `run_in_background: false` (foreground mode) with any permission mode
- Agents that don't need Skill tool can run in background

**Impact on Commands**:
- `/audit-plugins` must run sequentially (foreground) instead of in parallel
- Any command that spawns agents needing Skill must use foreground mode

### Permission Modes Reference

| Mode | Behavior |
|------|----------|
| `default` | Standard permission checking with interactive prompts |
| `acceptEdits` | Auto-accept file edit permissions |
| `bypassPermissions` | Skip all permission checks (still doesn't help Skill in background) |
| `dontAsk` | Auto-deny permission prompts |
| `plan` | Read-only exploration mode |

### Designing Agent Workflows

When creating commands that spawn agents:

1. **If agents need Skill tool**: Use `run_in_background: false` (sequential execution)
2. **If agents only need Edit/Bash/Read**: Can use `run_in_background: true` (parallel execution)
3. **Always use `mode: "bypassPermissions"`**: Prevents permission prompts for tools that can be pre-approved

---

## Troubleshooting

### Hook not triggering
- Verify `.claude/settings.json` has the hook configured
- Check hook script has execute permissions: `chmod +x .claude/hooks/*.sh`
- Ensure `jq` is installed (hooks parse JSON input)

### Command blocked unexpectedly
- Read the block message - it explains why
- If legitimate need, ask user to run command manually
- User can approve the action if appropriate

### Slash commands not available
- Restart Claude Code session
- Verify command file exists in `.claude/commands/` with `.md` extension
- Check command file structure matches expected format

---

## Adding New Hooks

To add a new PreToolUse hook:

1. Create script in `.claude/hooks/`:
```bash
#!/bin/bash
input=$(cat)
command=$(echo "$input" | jq -r '.tool_input.command // ""')

if [[ "$command" =~ dangerous-pattern ]]; then
    cat <<'ENDJSON'
{
  "decision": "block",
  "reason": "Explanation of why this is blocked..."
}
ENDJSON
    exit 0
fi

exit 0  # Allow command
```

2. Add to `.claude/settings.json`:
```json
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Bash",
        "hooks": [
          {
            "type": "command",
            "command": "bash \"$CLAUDE_PROJECT_DIR/.claude/hooks/your-hook.sh\"",
            "timeout": 5000
          }
        ]
      }
    ]
  }
}
```

3. Make executable: `chmod +x .claude/hooks/your-hook.sh`

### Matching Non-Bash Tools

Hooks can match any tool name, not just `Bash`. Use this when you need to intercept or nudge behavior on specific tool calls like `Agent`, `TaskCreate`, `TodoWrite`, `Write`, `Edit`, etc.

**Blocking example** (prevents Agent resume):
```json
{
  "matcher": "Agent",
  "hooks": [{
    "type": "command",
    "command": "bash \"$CLAUDE_PROJECT_DIR/.claude/hooks/block-agent-polling.sh\"",
    "timeout": 5000
  }]
}
```

**Reminder example** (non-blocking nudge on TaskCreate and TodoWrite):
```json
{
  "matcher": "TaskCreate",
  "hooks": [{
    "type": "command",
    "command": "bash \"$CLAUDE_PROJECT_DIR/.claude/hooks/task-creation-reminder.sh\"",
    "timeout": 5000
  }]
},
{
  "matcher": "TodoWrite",
  "hooks": [{
    "type": "command",
    "command": "bash \"$CLAUDE_PROJECT_DIR/.claude/hooks/task-creation-reminder.sh\"",
    "timeout": 5000
  }]
}
```

**Non-blocking hook script pattern** (returns `allow` with a message instead of `deny`):
```bash
#!/bin/bash
input=$(cat)
tool_name=$(echo "$input" | jq -r '.tool_name // ""' 2>/dev/null)

if [[ "$tool_name" == "TargetTool" ]]; then
    jq -n --arg msg "Your reminder message here" '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "allow",
            permissionDecisionReason: $msg
        }
    }'
    exit 0
fi

exit 0
```

**Key differences from Bash hooks**:
- **Matcher**: Use the exact tool name (e.g., `"TaskCreate"`) instead of `"Bash"`
- **Input parsing**: Use `.tool_name` to identify the tool, not `.tool_input.command`
- **One matcher per tool**: Each tool name needs its own matcher entry in `settings.json`, but multiple matchers can point to the same hook script
- **Non-blocking pattern**: Return `"permissionDecision": "allow"` with `"permissionDecisionReason"` to show a message without blocking

---

## References

### Project Documentation
- [CLAUDE.md](../../CLAUDE.md) - Main project instructions for Claude
- [DEEP-DIVE-TEMPLATE.md](../reference/templates/DEEP-DIVE-TEMPLATE.md) - Plugin documentation template
- [TENETS.md](../reference/TENETS.md) - Development standards

### Official Claude Code Documentation

**Hooks:**
- [Hooks Reference](https://docs.anthropic.com/en/docs/claude-code/hooks) - Complete hook configuration reference
- [Get Started with Hooks](https://docs.anthropic.com/en/docs/claude-code/hooks-guide) - Tutorial and use cases
- [Hook Development Skill](https://github.com/anthropics/claude-code/blob/main/plugins/plugin-dev/skills/hook-development/SKILL.md) - Advanced hook development patterns
- [Bash Validator Example](https://github.com/anthropics/claude-code/blob/main/examples/hooks/bash_command_validator_example.py) - Example PreToolUse hook

**Plugins & Skills:**
- [Create Plugins](https://code.claude.com/docs/en/plugins) - Official plugin creation guide
- [Plugins README](https://github.com/anthropics/claude-code/blob/main/plugins/README.md) - Plugin structure and conventions
- [Agent SDK Plugins](https://platform.claude.com/docs/en/agent-sdk/plugins) - Programmatic plugin loading

### Community Resources
- [Awesome Claude Code](https://github.com/hesreallyhim/awesome-claude-code) - Curated list of skills, hooks, and plugins
- [Claude Plugins Registry](https://claude-plugins.dev/) - Community plugin discovery
