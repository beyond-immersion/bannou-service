# Claude Code Integration Guide

> **Scope**: Claude Code configuration, hooks, and custom automation for Bannou development
> **Location**: `.claude/` directory (hooks, commands, agents)

This guide documents the Claude Code tooling configured for the Bannou project, including safety hooks that prevent destructive operations and custom plugins for documentation maintenance.

---

## Overview

Bannou uses Claude Code with custom configuration to:

1. **Enforce development standards** - Hooks block destructive commands and enforce commit format
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
├── hooks/
│   ├── validate-senryu-commit.sh
│   ├── block-destructive-git.sh
│   ├── block-production-deploy.sh
│   └── block-integration-tests.sh
├── commands/                  # Custom slash commands
│   ├── audit-plugin.md
│   ├── audit-plugins.md
│   └── maintain-plugin.md
└── agents/                    # Custom agents for commands
    ├── doc-reviewer.md
    └── gap-investigator.md
```

---

## PreToolUse Hooks

These hooks intercept Bash commands before execution and block dangerous operations.

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
2. Check document matches `DEEP_DIVE_TEMPLATE.md` structure
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

Launches multiple audit agents in parallel, each on a unique plugin.

```bash
/audit-plugins 3                 # Launch 3 agents on 3 different plugins
/audit-plugins 5                 # Launch 5 agents
```

**How it works**:
1. Randomly selects N unique plugins
2. Launches N background agents in parallel
3. Each agent runs full `/audit-plugin` workflow
4. Agents complete independently

**Recommended**: 3-5 agents at a time

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

```
.claude/
├── commands/
│   ├── maintain-plugin.md        # Document maintenance workflow
│   ├── audit-plugin.md           # Single gap processing
│   └── audit-plugins.md          # Parallel gap processing
└── agents/
    ├── doc-reviewer.md           # Source code review agent
    └── gap-investigator.md       # Gap investigation agent
```

These are project-level standalone commands (not a plugin), so they use short names like `/audit-plugin` rather than namespaced names.

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
# Launch parallel agents on multiple plugins
/audit-plugins 3
```

### For Code Changes

```bash
# After making changes to a plugin:
dotnet build                     # Verify compilation
/maintain-plugin {plugin}        # Update documentation
```

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

### Plugin commands not available
- Restart Claude Code after plugin installation
- Verify plugin is registered in `~/.claude/plugins/installed_plugins.json`
- Check plugin structure matches expected format

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

---

## References

### Project Documentation
- [CLAUDE.md](../../CLAUDE.md) - Main project instructions for Claude
- [DEEP_DIVE_TEMPLATE.md](../plugins/DEEP_DIVE_TEMPLATE.md) - Plugin documentation template
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
