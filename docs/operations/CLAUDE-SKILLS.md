# Claude Code Integration Guide

> **Last Updated**: 2026-03-23
> **Scope**: Claude Code configuration, hooks, and custom automation for Bannou development

## Summary

Claude Code integration configuration for Bannou development. Built around a custom MCP server (`bannou-read`) that replaces Claude's built-in Read, Edit, and Bash tools with a sandboxed tool set — eliminating entire categories of failure by making them structurally impossible rather than behaviorally discouraged. Includes context preparation (`prepare_context`) for efficient bulk documentation loading with profile-based file sets and sentinel-triggered external activation. Also covers PreToolUse hooks (behavioral reminders, commit format enforcement), a permission canary fail-fast gate for skills, 16 custom skills for plugin documentation/auditing/implementation/schema work, and project-aware agent types. Required reading before creating new hooks, skills, or agent workflows.

---

## Overview

Bannou uses Claude Code with custom configuration organized in four layers:

1. **MCP Server** — A custom file operations server (`bannou-read`) that replaces Claude's built-in Read, Edit, and Bash tools. Agents operate exclusively through this sandboxed tool set: `read_file`, `edit_file`, `write_file`, `write_script`, `move_lines`, `validate_structure`, `prepare_context`, and `run_command` (whitelisted commands only). This is the foundation — by controlling what tools are available, entire categories of misbehavior become structurally impossible.
2. **Hooks** — Real-time procedural gates that fire on specific tool calls. Some block (destructive git, production deploys), some remind (frozen files, task format, language patterns). Many hooks that were critical before the MCP server are now redundant for agents — the MCP server's tool restrictions achieve the same result with zero interruption cost.
3. **Skills** — Mechanical checklists for complex workflows (auditing, implementation, testing). Each skill is a step-by-step procedure with verification at each stage, stored as `.claude/skills/{name}/SKILL.md` with shared fragments in `.claude/skills/_shared/`.
4. **Agents** — Project-aware sub-agents that pre-load Bannou context (tenets, patterns, schema rules) before doing work. Agent tool lists are defined in `.claude/agents/*.md` and include only MCP tools — agents cannot access built-in Read, Edit, or Bash.

---

## Design Philosophy

This configuration is built on a principle validated by [Anthropic's own prompt engineering research](https://platform.claude.com/docs/en/build-with-claude/prompt-engineering/claude-4-best-practices): **thoroughness is a property of the procedure, not the agent's disposition.**

Abstract warnings ("be careful," "never do X") do not reliably change behavior. What works:

- **Procedural gates at the point of action**: A hook that fires when Claude is about to edit a frozen file is more reliable than a rule in CLAUDE.md saying "don't edit frozen files." The hook fires at the moment the decision matters; the rule depends on recall from context loaded thousands of tokens ago.
- **Mechanical checklists in skills**: A skill that says "Step 4: run `make print-models` and verify every type name" creates a procedural checkpoint. A general instruction saying "verify type names" does not.
- **Positive framing over negative framing**: [Research shows](https://gadlet.com/posts/negative-prompting/) that positive instructions ("read the full file") actively boost the probability of desired behavior, while negative instructions ("don't skip content") only slightly reduce the probability of unwanted behavior. All hooks and instructions in this project use positive framing.
- **WHY context improves compliance**: Anthropic's docs state that explaining the motivation behind an instruction helps Claude generalize. "Read before you write — in a 78-plugin codebase, the cost of re-doing work from incorrect assumptions far exceeds the cost of reading first" is more effective than "Read before you write."

For the history of specific incidents that shaped individual hooks, see `docs/reference/INCIDENT-HISTORY.md`.

### Guidelines for Writing New Hooks and Skills

1. **Tell Claude what to do, not what not to do** — "Read the full file" over "Don't skip content"
2. **Use calm, specific reminders** — A one-sentence reminder at the point of action beats a paragraph of warnings loaded at session start
3. **Require proof of work through procedure** — "Quote specific lines from the code you read" is a procedural checkpoint; "be thorough" is a vibe
4. **Use structured output to verify completeness** — Require specific sections that prove each step was completed
5. **Explain why** — A brief rationale makes rules sticky: "Complete every checklist step — checklists catch specific errors that general awareness misses"

---

## MCP File Operations Server

The most impactful improvement to the Claude workflow is a custom MCP server (`.claude/mcp/server.mjs`) that replaces Claude Code's built-in Read, Edit, and Bash tools with a sandboxed alternative. This shifts the enforcement model from **behavioral restriction** (hooks that detect and block bad actions) to **capability restriction** (tools that simply don't offer bad actions).

### Why It Exists

Claude Code's built-in tools have three problems this project cannot tolerate:

| Built-in Tool | Problem | Impact |
|---------------|---------|--------|
| **Read** | Truncates files over ~2000 lines, injects "use limit/offset" messages | Agents chunk reads unnecessarily, skip content, silently degrade quality |
| **Edit** | Requires built-in Read to have been called first (internal tracking) | MCP-based reads cannot satisfy this gate — Edit refuses to work |
| **Bash** | Unrestricted shell access | Agents can `cat` files (bypassing read tracking), `sed`/`awk` edit files (bypassing edit tracking), run arbitrary scripts, and improvise solutions the instructions never anticipated |

The MCP server solves all three by owning the entire file operation space:

### What It Provides

| Tool | Purpose | Key Behavior |
|------|---------|-------------|
| `read_file` | Full file reads with line numbers | Never truncates. Large files split into continuation parts with preserved line numbers. Tracks which files have been read. |
| `edit_file` | Exact string replacement | Requires `read_file` first (MCP's own tracking). Validates uniqueness. Rejects edits to files with unread continuation parts. |
| `write_file` | Complete file writes | Requires `read_file` first for existing files (no gate for new files). Content preserved to `/tmp/` on any failure. Race condition detection via `expected_size_bytes`. |
| `write_script` | Sandboxed script creation | Scripts written to `/tmp/bannou-scripts/` only. Auto chmod +x. Syntax validation (bash -n, py_compile, node --check). |
| `move_lines` | Line-range relocation between files | Atomic dual-file write. Safety anchors catch off-by-one errors. Automatic C# structure validation post-move. |
| `validate_structure` | C# structural integrity check | Balanced braces, `#region`/`#endregion`, `#if`/`#endif`. Used automatically by `move_lines`, available standalone. |
| `prepare_context` | Context composite creation | Packs documentation into optimally-sized composites (≤64KB). Pre-registers originals as read. Gates all tools until composites are read. See [Context Preparation](#context-preparation). |
| `run_command` | Whitelisted shell execution | Only allows specific command prefixes (see below). Blocked patterns prevent file writes outside `/tmp/`, destructive git, command injection, and ad-hoc scripting. |

### The Command Whitelist

`run_command` replaces unrestricted Bash access with a prefix whitelist:

```
gh *                    — GitHub CLI (issues, PRs, API queries)
dotnet build/test       — .NET build and test
make *                  — Makefile targets
scripts/*               — Code generation scripts
ls, find, wc, comm, shuf — File discovery (read-only)
echo, cat /tmp/*, rm -f /tmp/* — Temp file operations
git status/diff/log     — Read-only git queries
```

Everything else is rejected. Blocked patterns additionally catch:
- File write redirects outside `/tmp/` (`> /path` but not `> /tmp/path`)
- Destructive git (`git reset`, `git restore`, `git checkout --`, `git clean`, `git push --force`)
- Command injection (`$()`, backticks, `eval`, `source`)
- Ad-hoc scripting (`python3 -c`, `node -e`, `curl`, `wget`)
- File operations (`rm` outside `/tmp/`, `mv`, `cp`, `chmod`, `chown`)

### Large File Handling

Claude Code has a platform-level output size cap (~100KB serialized). MCP responses exceeding this are persisted to disk with only a 2KB preview reaching the agent. The server detects this and splits large files:

- **Part 1**: Returned directly (fits under the cap, ~70KB formatted content)
- **Part 2+**: Written to `/tmp/` as continuation files with original line numbers preserved

The edit gate blocks modifications to any file with unread continuation parts — the agent must read ALL parts before it can edit. `run_command` is also blocked globally while any split-file reads are incomplete.

### Context Preparation

The `prepare_context` tool replaces behavioral "read these N files" instructions with a mechanical tool. Instead of agents individually reading 8+ reference documents (each requiring a separate tool call and MCP response), the server reads all files at once, packs them into optimally-sized composites, and gates the agent until they read the composites.

#### How It Works

1. Agent calls `prepare_context(profile: "dev")` (or `plugin`, `schema`, `custom`)
2. Server resolves the profile to a file list (defined in `.claude/mcp/profiles.mjs`)
3. Server reads all files, adds line numbers (same format as `read_file`)
4. Files are packed into composites ≤64KB each (fits in a single `read_file` response)
5. Original files are pre-registered in `readFiles` (enabling immediate `edit_file` without re-reading)
6. Composites are added to `requiredReading` — all tools except `read_file` and `prepare_context` are gated
7. Agent reads each composite with `read_file` — as each is read, it's removed from the gate
8. When all composites are read, the gate clears and all tools become available

#### Profiles

Profiles are defined in `.claude/mcp/profiles.mjs` with inheritance support:

| Profile | Inherits | Static Files | Dynamic Files | Description |
|---------|----------|-------------|---------------|-------------|
| `dev` | — | CLAUDE.md, CLAUDE-PRACTICES.md, HELPERS-AND-COMMON-PATTERNS.md, 5 tenet files | — | Core development context |
| `plugin` | `dev` | — | `docs/plugins/{SERVICE}.md`, `docs/maps/{SERVICE}.md` | Plugin-specific context (requires `service` option) |
| `schema` | `dev` | SCHEMA-RULES.md, specifications catalog | — | Schema work context |
| `custom` | — | — | Arbitrary file list from `files` option | Ad-hoc context loading |

Adding a new profile: add an entry to `CONTEXT_PROFILES` in `profiles.mjs`. Use `extends` for inheritance, `files` for static paths, `dynamicFiles(options)` for option-dependent paths, and `requiresOption` to enforce required parameters.

#### Sentinel-Triggered Context Preparation

Context preparation can also be triggered externally via sentinel injection — the user writes a JSON file that the MCP server processes on the next tool call. This enables one-button context loading from a Stream Deck or terminal:

```json
{
  "prepareContext": { "profile": "dev" },
  "message": "Context prepared: dev"
}
```

For plugin-specific context:
```json
{
  "prepareContext": { "profile": "plugin", "service": "account" },
  "message": "Context prepared: account plugin"
}
```

When triggered via sentinel, the same composite-and-gate mechanism applies. A `UserPromptSubmit` hook notifies the agent that an injection is pending, prompting it to call any MCP tool to process the sentinel file. The composites are created, the required reading gate activates, and the agent reads each composite to clear the gate.

#### Idempotency and Stacking

- **Idempotent**: If all files for a profile are already read, `prepare_context` returns immediately with no composites. Calling it twice is a no-op.
- **Stackable**: Calling `prepare_context` while a gate is active (from a previous call) adds new composites to the existing gate. This enables layered context loading — e.g., first load `dev`, then stack `plugin` context.
- **Skip logic**: Files already in `readFiles` (from previous reads or prepare_context calls) are skipped — only new files are packed into composites.

### The Philosophical Shift: Capability vs. Behavior

The MCP server represents a fundamental change in how we manage Claude's behavior:

**Before (behavioral restriction)**:
- Hook detects Claude using `cat` to read a file → fires reminder to use Read tool
- Hook detects Claude using `sed` to edit → fires reminder to use Edit tool
- Hook detects Claude running `git reset` → blocks the action
- Hook detects Claude using limit/offset on Read → fires reminder to read fully
- Each hook is an interruption. Each interruption costs attention. The accumulated weight of 15+ hooks firing on every tool call creates cognitive overhead that degrades performance.

**After (capability restriction)**:
- Agent's tool list contains `read_file`, `edit_file`, `run_command` — no `cat`, no `sed`, no `git reset`
- Agent literally cannot consider these actions because the tools don't exist in its action space
- Zero interruptions. Zero negative reinforcement. The agent stays focused on the actual work.

This approach is also **future-proof against model behavior changes**. Claude's system prompt includes efficiency directives ("be concise," "try the simplest approach") and there appears to be an internal weight that increases pressure to finish as sessions grow longer — likely an infrastructure optimization to reduce compute costs on long conversations. These pressures are at direct odds with the 1M context window and a 78-plugin codebase that demands thoroughness. Behavioral hooks can be undermined by shifting weights; capability restrictions cannot. The agent cannot take a shortcut through a tool that doesn't exist, regardless of how much internal pressure it feels to wrap up.

### Agent Tool Configuration

Agent definitions in `.claude/agents/*.md` specify their tool lists explicitly. All project agents include only MCP tools:

```yaml
tools: Glob, Grep, LS, mcp__bannou-read__read_file, mcp__bannou-read__edit_file,
       mcp__bannou-read__move_lines, mcp__bannou-read__validate_structure,
       mcp__bannou-read__run_command, WebFetch, TodoWrite, WebSearch, Write,
       TaskCreate, TaskList, TaskGet, TaskUpdate, Agent
```

Note the absence of `Read`, `Edit`, and `Bash` from agent tool lists. The built-in search tools (`Glob`, `Grep`, `LS`) are retained because they are read-only and optimized for their purpose. `Write` is retained for creating new files (MCP `edit_file` handles modifications to existing files).

### Which Hooks Are Now Redundant for Agents

With agents restricted to MCP tools, several hooks that were previously essential are now structurally unnecessary for agent sessions:

| Hook | Why It's Redundant for Agents |
|------|------------------------------|
| `block-destructive-git.sh` | `run_command` whitelist doesn't include destructive git |
| `block-production-deploy.sh` | `run_command` whitelist doesn't include publish commands |
| `block-integration-tests.sh` | `run_command` whitelist doesn't include Docker commands |
| `block-file-moves.sh` | `run_command` blocked patterns reject `mv` |
| `block-symlinks.sh` | `run_command` blocked patterns reject `ln` |
| `block-read-limit.sh` | MCP `read_file` has no limit/offset parameters |

These hooks remain configured in `settings.json` because they still protect the **parent session** (which retains access to built-in tools). They fire zero times during agent execution — zero cost, zero interruption.

---

## Configuration Files

```
.claude/
├── settings.json              # Hook configuration (checked into repo)
├── settings.local.json        # Local permissions (gitignored)
├── permission-canary.txt      # Permission gate canary file (see below)
├── system-prompt-original.md  # Original Claude Code system prompt (reference)
├── system-prompt-modified.md  # Modified system prompt (edit here, sync to config)
├── skills-guide.txt           # Anthropic's skill authoring reference
├── mcp/                               # MCP server (file operations sandbox)
│   ├── server.mjs             # Entry point: all tool registrations
│   ├── state.mjs              # Shared mutable state (read tracking, gates, constants)
│   ├── profiles.mjs           # Context profile definitions (dev, plugin, schema)
│   ├── helpers/
│   │   ├── file-ops.mjs       # Read tracking, gate checks, chunking
│   │   ├── structure.mjs      # C# brace/region/preprocessor validation
│   │   ├── commands.mjs       # Command whitelist, blocked patterns, execution
│   │   ├── sentinel.mjs       # External state injection (Stream Deck, manual)
│   │   ├── scripts.mjs        # Sandboxed script writing with syntax validation
│   │   └── context.mjs        # Context preparation, composite packing
│   ├── package.json           # Dependencies (@modelcontextprotocol/sdk)
│   └── node_modules/          # Installed dependencies
├── hooks/
│   ├── frozen-file-check.sh           # PreToolUse: frozen dir + test weakening check
│   ├── no-minimizing-language.sh      # PreToolUse: language pattern detection (8 categories)
│   ├── block-backwards-compatibility.sh # PreToolUse: backwards compat language (5 categories)
│   ├── block-all-agents.sh            # PreToolUse: restricts to project-aware agent types
│   ├── block-worktree-isolation.sh    # PreToolUse: blocks worktree isolation
│   ├── block-destructive-git.sh       # PreToolUse: blocks destructive git (parent session only)
│   ├── block-production-deploy.sh     # PreToolUse: blocks production publish (parent session only)
│   ├── block-integration-tests.sh     # PreToolUse: blocks long-running tests (parent session only)
│   ├── block-file-moves.sh            # PreToolUse: blocks mv on code files (parent session only)
│   ├── block-symlinks.sh              # PreToolUse: blocks symbolic link creation (parent session only)
│   ├── block-read-limit.sh            # PreToolUse: reminds about full file reads (parent session only)
│   ├── validate-senryu-commit.sh      # PreToolUse: enforces senryu commit format
│   ├── enforce-parallel-reads.sh      # PreToolUse: blocks all non-read tools during read gate
│   ├── git-history-reminder.sh        # PreToolUse: reminds about git history usage
│   ├── task-creation-reminder.sh      # PreToolUse: reminds about task list format
│   ├── track-parallel-reads.sh        # PostToolUse: silent read counter for skills
│   └── post-edit-reminder.sh          # RETIRED (replaced by frozen-file-check.sh)
├── skills/                            # Custom skills (SKILL.md format)
│   ├── _shared/                       # Shared fragments included via !`cat ...`
│   │   ├── permission-canary.md       # Edit gate: toggle canary file to verify permissions
│   │   ├── required-tools-gate.md     # Tool availability check
│   │   ├── reading-gate.md            # Parallel read gate protocol
│   │   ├── context-carryover.md       # Same-session file reuse rules
│   │   ├── decision-checkpoints.md    # When to stop and ask the user
│   │   ├── common-pitfalls-preamble.md # Intro for pitfall sections
│   │   └── self-check-preamble.md     # Intro for self-check sections
│   ├── audit-plugin/SKILL.md
│   ├── check-plugin/SKILL.md
│   ├── implement-feature/SKILL.md
│   ├── implement-plugin/SKILL.md
│   ├── investigate-issue/SKILL.md
│   ├── maintain-faq/SKILL.md
│   ├── maintain-guide/SKILL.md
│   ├── maintain-operations-doc/SKILL.md
│   ├── maintain-planning-doc/SKILL.md
│   ├── maintain-plugin/SKILL.md
│   ├── map-plugin/SKILL.md
│   ├── orchestrate-skill/SKILL.md
│   ├── propose/SKILL.md
│   ├── schema-plugin/SKILL.md
│   ├── test-plugin/SKILL.md
│   └── update-permissions/SKILL.md
├── agents/                            # Custom agent definitions (MCP tools only)
│   ├── bannou.md              # General project awareness (CLAUDE.md + CLAUDE-PRACTICES.md)
│   ├── bannou-dev.md          # Development (above + tenets + patterns)
│   ├── bannou-limited.md      # Minimal awareness (CLAUDE.md + CLAUDE-PRACTICES.md, reduced tools)
│   ├── bannou-schema.md       # Schema work (above + schema rules + specifications)
│   └── bannou-code-reviewer.md  # Read-only code review (tenets + all plugin source)
└── rules/                             # Contextual rules (loaded near matching files)
    ├── frozen-files.md        # Loaded when editing near frozen directories
    ├── schema-rules.md        # Loaded when editing in schemas/
    └── testing-patterns.md    # Loaded when editing in test projects
```

---

## System Prompt Customization

The Claude Code system prompt has been customized for Bannou's needs. The modified prompt is maintained in two locations:

- **Source of truth**: `.claude/system-prompt-modified.md` (human-readable, edit here)
- **Live config**: `~/.claude/config.json` (`systemPrompt` field, minified JSON string)

After editing the markdown source, regenerate the config with:
```bash
python3 -c "
import json
with open('.claude/system-prompt-modified.md') as f:
    print(json.dumps({'systemPrompt': f.read()}, indent=2))
" > ~/.claude/config.json
```

**Key modifications from the default Claude Code prompt** (see `.claude/system-prompt-original.md` for comparison):

| Area | Default | Modified | Why |
|------|---------|----------|-----|
| Efficiency section | "Go straight to the point. Be extra concise." | Removed entirely | Creates pressure to finish fast, which in a complex codebase produces incorrect output |
| Tone | "Short and concise" | "Clarity, not brevity for its own sake" | Match detail to the task rather than minimizing output |
| Reading | "Read it first" | "Read it in full first, all files in parallel" | Prevents partial reads in a 78-plugin codebase |
| Quality | "Avoid over-engineering" | "Do things properly. Follow documented patterns." | Over-engineering directives caused under-engineering in practice |
| Blocked approaches | "Do not brute force" | "When blocked, try a different approach" | Positive framing boosts compliance per research |
| Checklists | Not mentioned | "Complete every step — checklists catch specific errors that general awareness misses" | WHY context improves compliance |

---

## PreToolUse Hooks

Hooks intercept tool calls before execution. Two types:

- **Blocking** (`permissionDecision: "deny"`) — Prevents the action entirely. Used for genuinely dangerous operations.
- **Reminder** (`permissionDecision: "allow"` with `permissionDecisionReason`) — Shows a message, then allows the action. Claude self-assesses whether the reminder applies. Used for contextual guidance at the point of action.

### Blocking Hooks

> **MCP impact**: Hooks marked with † only protect the parent session. Agents use MCP `run_command` which enforces the same restrictions via its command whitelist — these hooks fire zero times during agent execution.

| Hook | Matcher | What It Blocks |
|------|---------|----------------|
| `validate-senryu-commit.sh` | Bash | Commits without senryu first line (5-7-5 format with ` / ` separators) |
| `block-destructive-git.sh` † | Bash | `git restore`, `git checkout --`, `git checkout .`, `git stash`, `git reset`, `git clean` |
| `block-production-deploy.sh` † | Bash | `make push-release`, `make publish-sdk-ts` |
| `block-integration-tests.sh` † | Bash | `make test-http`, `make test-edge`, `make test-infrastructure`, `make all` |
| `block-file-moves.sh` † | Bash | `mv` targeting code file extensions |
| `block-symlinks.sh` † | Bash | `ln -s` / `ln --symbolic` |
| `block-worktree-isolation.sh` | Agent, EnterWorktree | Agent calls with `isolation: "worktree"`, EnterWorktree tool |
| `block-all-agents.sh` | Agent | Non-approved agent types (only `bannou`, `bannou-dev`, `bannou-limited`, `bannou-schema`, `bannou-code-reviewer` allowed) |
| `enforce-parallel-reads.sh` | .* (all) | ALL non-read tools while a parallel read gate is active (skill-activated, auto-clears when read count met) |

### Reminder Hooks

> **MCP impact**: Hooks marked with † only fire in the parent session. Agents using MCP tools are unaffected — either because the MCP tool doesn't offer the capability being guarded, or because the agent doesn't have the built-in tool in its tool list.

| Hook | Matcher | What It Reminds |
|------|---------|-----------------|
| `frozen-file-check.sh` | Edit, Write | "This file is in a frozen directory. Proceed if you have explicit authorization." Also checks test files for weakening patterns. |
| `no-minimizing-language.sh` | .* (all) | Detects 8 categories of language that signal rationalization, minimization, self-authorization, exception-invention, precedent-mining, work-avoidance, or corner-cutting |
| `block-backwards-compatibility.sh` | .* (all) | Detects backwards-compatibility language, breaking-change anxiety, compatibility shims, soft removal, consumer anxiety |
| `block-read-limit.sh` † | Read | "Read full files — you have a 1M token context window" when limit/offset parameters are used. MCP `read_file` has no limit/offset — structurally impossible. |
| `git-history-reminder.sh` † | Bash | "Use git history to understand changes, not to justify reverting completed work" on `git diff`/`git log` |
| `task-creation-reminder.sh` | TaskCreate, TodoWrite | Reminds of the 5-element format for violation task lists |

### PostToolUse Hooks

| Hook | Matcher | What It Does |
|------|---------|--------------|
| `track-parallel-reads.sh` | Read, mcp__bannou-read__read_file | Silently appends a character to `/tmp/.parallel-read-token` on each read. Works with both built-in Read and MCP `read_file`. Skills clear the file before parallel read phases and check the count after. |

### The Parallel Read Gate

The `enforce-parallel-reads.sh` and `track-parallel-reads.sh` hooks work together to enforce bulk file reading in skills. This prevents a behavioral tendency to serialize reads across multiple messages and interleave analysis between them.

```
Skill activates gate:  echo 16 > /tmp/.parallel-read-expected
                       rm -f /tmp/.parallel-read-token

Each read_file call:   track-parallel-reads.sh appends 1 char to token file

Any non-read tool:     enforce-parallel-reads.sh checks: token length < expected?
                       YES → block ("12 of 16 reads remaining")
                       NO  → allow (gate clears, deletes expected file)
```

The gate is physically enforced — there is no override. This is a capability restriction: during the read phase, the only available tool is `read_file`. This maps naturally to the MCP philosophy — rather than reminding Claude to read all files before analyzing, the hook makes it impossible to do anything else until reading is complete.

---

## Permission Canary (Fail-Fast Permission Gate)

Every skill begins with a zero-consequence Edit that verifies write permissions before doing any work.

```
Skill invoked → Edit .claude/permission-canary.txt (toggle "canary" ↔ "canarY")
  ├── Succeeds → proceed with workflow
  └── Denied → HARD STOP, zero work performed
```

**Why**: Without the canary, a skill without Edit permissions would read dozens of files, analyze code, then fail at its first Edit 10+ minutes in. The canary catches this in 2 seconds.

The canary block is present in all 16 skill files. When creating a new skill, paste the block immediately after the YAML frontmatter.

---

## Custom Skills

Skills use the `.claude/skills/{name}/SKILL.md` format with YAML frontmatter for metadata and markdown body for instructions. Skills are invoked via `/skill-name` in the Claude Code CLI.

### Shared Fragments

Common patterns are extracted into `.claude/skills/_shared/` and included via `!`cat .claude/skills/_shared/{name}.md`` (Anthropic's shell inclusion syntax). This ensures consistency across all 16 skills:

| Fragment | Purpose | Used By |
|----------|---------|---------|
| `permission-canary.md` | Edit gate: toggle canary file to verify write permissions before work begins | All skills |
| `required-tools-gate.md` | Verify Edit/Write tool availability, fail fast if unavailable | Skills that create files |
| `reading-gate.md` | Parallel read gate protocol (activate, read, verify, prove comprehension) | Skills with bulk read phases |
| `context-carryover.md` | Same-session file reuse rules (skip re-reading files still in context) | Skills chained in one session |
| `decision-checkpoints.md` | When to stop work and present the situation to the user | All skills |
| `common-pitfalls-preamble.md` | Intro for pitfall sections ("each entry exists because the mistake actually happened") | Skills with pitfall lists |
| `self-check-preamble.md` | Intro for self-check gates ("most common failure mode is declaring completion before all phases are done") | Skills with self-check gates |

### Plugin Pipeline Commands

| Command | Purpose | Readiness |
|---------|---------|-----------|
| `/check-plugin [name]` | Read-only diagnostic: plugin readiness level (L0-L7), next action | Any |
| `/maintain-plugin [name\|random]` | Ensure deep dive doc is structurally correct and content-accurate | Any |
| `/audit-plugin [name\|random]` | Find and handle ONE implementation gap from a deep dive | Post-maintain |
| `/map-plugin [name]` | Create/maintain implementation map (`docs/maps/{SERVICE}.md`) | Post-audit |
| `/schema-plugin [name]` | Create/maintain OpenAPI schemas from implementation map | Post-map |
| `/test-plugin [name]` | Generate TDD red-phase unit tests from interface and map | L4+ (schemas generated) |
| `/implement-plugin [name]` | Implement service logic from map and failing tests | L5+ (tests exist) |

### Feature & Issue Commands

| Command | Purpose |
|---------|---------|
| `/implement-feature [description]` | Guided feature development: deep dive → map → schema → code → tests |
| `/investigate-issue [number\|random]` | 10-phase GitHub issue investigation with structured decision tracking |
| `/propose [description]` | Design proposal for new features or architectural changes |

### Documentation Commands

| Command | Purpose |
|---------|---------|
| `/maintain-faq [name\|random]` | Maintain FAQ docs in `docs/faqs/` |
| `/maintain-guide [name\|random]` | Maintain developer guides in `docs/guides/` |
| `/maintain-planning-doc [name\|random]` | Maintain planning docs in `docs/planning/` |
| `/maintain-operations-doc [name\|random]` | Maintain operations docs in `docs/operations/` |
| `/update-permissions [plugins...]` | Audit and fix `x-permissions` on 1-5 plugins |

### Orchestration

| Command | Purpose |
|---------|---------|
| `/orchestrate-skill <skill> for <scope>` | Run any single-target skill across multiple targets in parallel batches of 3 |

```bash
/orchestrate-skill maintain-plugin for all
/orchestrate-skill audit-plugin for account,auth,chat
/orchestrate-skill maintain-faq for 5
```

---

## Custom Agent Types

| Agent | Context Loading | Use For |
|-------|----------------|---------|
| `bannou` | Manual reads (CLAUDE.md, CLAUDE-PRACTICES.md) | General tasks needing project awareness |
| `bannou-dev` | `prepare_context(profile: "dev")` or `prepare_context(profile: "plugin", service: "name")` | Implementation, code review, auditing, tenet compliance |
| `bannou-limited` | Manual reads (CLAUDE.md, CLAUDE-PRACTICES.md), reduced tool set | Lightweight tasks needing basic project awareness |
| `bannou-schema` | `prepare_context(profile: "schema")` | Schema work, generation, extension attributes |
| `bannou-code-reviewer` | `prepare_context(profile: "plugin", service: "name")` + all plugin source | Read-only deep code review for tenet violations |

All agents use MCP tools exclusively for file operations and command execution. Their tool lists are defined in `.claude/agents/*.md` and deliberately exclude built-in `Read`, `Edit`, and `Bash`. Non-approved agent types (general-purpose, Explore, Plan, feature-dev:*, etc.) are blocked by `block-all-agents.sh`.

Agents that load reference documents (`bannou-dev`, `bannou-schema`, `bannou-code-reviewer`) use `prepare_context` instead of individual `read_file` calls. This is more efficient: the server reads files once, packs them into composites optimized for the MCP output size cap, and pre-registers originals for immediate `edit_file` — reducing 8+ individual reads to 1 tool call + a handful of composite reads.

### Why Agents Cannot Use Built-in Tools

The agent tool list restriction is the single most impactful configuration decision in this project. By giving agents only MCP tools:

1. **No truncation**: MCP `read_file` returns complete files. Built-in `Read` truncates at ~2000 lines and injects "use limit/offset" which causes progressive quality degradation as sessions grow longer.
2. **No arbitrary shell**: MCP `run_command` only allows whitelisted commands. Built-in `Bash` allows anything — `cat` bypasses read tracking, `sed` bypasses edit tracking, improvised scripts bypass all guardrails.
3. **No edit gate confusion**: MCP `edit_file` requires MCP `read_file` (same tracking space). Built-in `Edit` requires built-in `Read` — an MCP read cannot satisfy this gate, causing mysterious "file not read" errors.
4. **Self-enforcing read completeness**: MCP `edit_file` blocks edits on files with unread continuation parts. The agent must fully read large files before modifying them — no configuration, no hooks, no reminders needed.
5. **Structural safety**: MCP `move_lines` automatically validates C# structure after every move. No separate tool call needed, no possibility of forgetting.

---

## Work Tracking Markers

The plugin auditor uses HTML comment markers to track work status in deep dive documents:

```markdown
- Some bug that needs fixing
  <!-- AUDIT:IN_PROGRESS:2026-01-29 -->

- Design issue needing human decisions
  <!-- AUDIT:NEEDS_DESIGN:2026-01-28:https://github.com/org/repo/issues/42 -->
```

| Status | Meaning | Issue Link |
|--------|---------|------------|
| `IN_PROGRESS` | Being actively worked on | Optional |
| `NEEDS_DESIGN` | Needs human design decisions | Required |
| `BLOCKED` | Waiting on dependency | Optional |

---

## Recommended Workflow

### Plugin Development Pipeline

```bash
/check-plugin divine          # Determine readiness level, get next action
/maintain-plugin divine       # Ensure deep dive is complete and accurate
/audit-plugin divine          # Process one gap (repeat for more)
/map-plugin divine            # Create implementation map
/schema-plugin divine         # Generate schemas
/test-plugin divine           # Write failing tests
/implement-plugin divine      # Implement service logic
```

### Documentation Maintenance

```bash
/maintain-plugin account      # Update deep dive after code changes
/orchestrate-skill maintain-faq for 5    # Batch-maintain 5 random FAQs
```

### GitHub Issues

```bash
/investigate-issue 42         # Structured investigation of specific issue
/investigate-issue            # Pick random open issue
```

---

## Adding New Hooks

### Before Creating a Hook: Consider MCP First

Before writing a new hook, ask: **can this be enforced by the MCP server's tool design instead?**

| If the goal is... | Prefer |
|-------------------|--------|
| Preventing a shell command | Add to `run_command`'s blocked patterns or remove from the whitelist |
| Preventing file reads without full content | Already handled — MCP `read_file` never truncates |
| Preventing edits without reading first | Already handled — MCP `edit_file` requires `read_file` |
| Preventing arbitrary file manipulation | Already handled — agents don't have `Bash` |
| Detecting behavioral language patterns | Hook is appropriate — language patterns are in tool_input text, not tool capability |
| Enforcing workflow ordering | Hook is appropriate — parallel read gates, permission canaries |

Hooks are the right tool for **behavioral** guidance (language patterns, workflow enforcement). The MCP server is the right tool for **capability** restriction (what operations are possible).

### Blocking Hook (PreToolUse)

```bash
#!/bin/bash
input=$(cat)
command=$(echo "$input" | jq -r '.tool_input.command // ""' 2>/dev/null)

if echo "$command" | grep -qE 'dangerous-pattern'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "deny",
            permissionDecisionReason: "Clear explanation of why this is blocked and what to do instead."
        }
    }'
    exit 0
fi

exit 0
```

### Reminder Hook (PreToolUse)

```bash
#!/bin/bash
input=$(cat)
tool_input=$(echo "$input" | jq -r '.tool_input | tostring' 2>/dev/null)

if echo "$tool_input" | grep -qiE 'pattern-to-detect'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "allow",
            permissionDecisionReason: "Calm one-sentence reminder. Claude self-assesses and proceeds or adjusts."
        }
    }'
    exit 0
fi

exit 0
```

### Registration (settings.json)

```json
{
  "matcher": "Bash",
  "hooks": [{
    "type": "command",
    "command": "bash \"$CLAUDE_PROJECT_DIR/.claude/hooks/your-hook.sh\"",
    "timeout": 5000
  }]
}
```

Hooks can match any tool name: `Bash`, `Agent`, `Edit`, `Write`, `Read`, `TaskCreate`, `TodoWrite`, `EnterWorktree`, or `.*` for all tools.

### Language Guidelines for Hook Messages

Per the research findings that shaped this configuration:
- **One sentence** for reminder hooks. Claude reads it and self-assesses.
- **Positive framing**: "Read the full file" over "Don't skip content"
- **No alarm language**: No ⛔, "STOP", "FORBIDDEN", "CRITICAL". Calm reminders are more effective.
- **No incident history in messages**: Reference `docs/reference/INCIDENT-HISTORY.md` in the script comments, not in the message Claude sees.

---

## Platform Limitations and Workarounds

### Background Agents Cannot Use Skill Tool

Background agents (`run_in_background: true`) have the Skill tool auto-denied ("prompts unavailable"). This is a Claude Code architectural limitation. Workaround: `/orchestrate-skill` embeds full skill content verbatim in agent prompts via `TaskCreate` descriptions instead of using the Skill tool.

### MCP Output Size Cap

Claude Code has a platform-level output size cap (~100KB serialized). MCP responses exceeding this are persisted to disk with only a 2KB preview reaching the agent. The MCP server works around this by splitting large file reads into continuation parts, each under the cap. The edit gate ensures all parts are read before modifications are allowed.

### Permission Modes

| Mode | Behavior |
|------|----------|
| `default` | Standard permission checking with interactive prompts |
| `acceptEdits` | Auto-accept file edit permissions |
| `bypassPermissions` | Skip all permission checks (still doesn't help Skill in background) |

MCP tools operate in their own permission space (configured in `settings.local.json`). Once approved, all MCP tool calls bypass Claude Code's built-in permission prompts — this eliminates the interactive permission overhead that slows down agent workflows.

---

## Troubleshooting

| Issue | Resolution |
|-------|------------|
| Hook not triggering | Verify `settings.json` has the hook configured; check `chmod +x`; ensure `jq` is installed |
| Command blocked unexpectedly | Read the block message — it explains why. Ask user to run manually if legitimate |
| Skill not available | Restart Claude Code session; verify `SKILL.md` exists in `.claude/skills/{name}/` |
| MCP tool "file not read" error | Read the file with MCP `read_file` first (not built-in Read). For split files, read all continuation parts. |
| MCP `run_command` rejected | Command doesn't match the whitelist. Check allowed prefixes in `server.mjs`. |
| Agent using wrong tools | Verify the agent definition in `.claude/agents/*.md` lists only MCP tools, not built-in Read/Edit/Bash |

---

## References

- [CLAUDE.md](../../CLAUDE.md) — Main project instructions (MCP tool usage, generation commands, workflow)
- [CLAUDE-PRACTICES.md](../../CLAUDE-PRACTICES.md) — Behavioral practices (surface problems, execute completely, frozen artifacts)
- [MCP Server Source](../../.claude/mcp/server.mjs) — The MCP server implementation (read_file, edit_file, move_lines, validate_structure, run_command)
- [INCIDENT-HISTORY.md](../reference/INCIDENT-HISTORY.md) — Hook incident history (why specific hooks exist)
- [TENETS.md](../reference/TENETS.md) — Development standards
- [Anthropic Prompt Engineering Best Practices](https://platform.claude.com/docs/en/build-with-claude/prompt-engineering/claude-4-best-practices) — Official Claude 4.6 guidance
- [Hooks Reference](https://docs.anthropic.com/en/docs/claude-code/hooks) — Official hook documentation
- [MCP Specification](https://modelcontextprotocol.io/) — Model Context Protocol standard
- [Best Practices for Claude Code](https://code.claude.com/docs/en/best-practices) — Official Claude Code guidance
- [Anthropic Skills Guide](../../.claude/skills-guide.txt) — Official skill authoring reference
