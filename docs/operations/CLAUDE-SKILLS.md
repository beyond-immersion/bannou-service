# Claude Code Integration Guide

> **Last Updated**: 2026-03-19
> **Scope**: Claude Code configuration, hooks, and custom automation for Bannou development

## Summary

Claude Code integration configuration for Bannou development. Covers PreToolUse hooks (blocking dangerous operations, enforcing commit format, providing contextual reminders), a permission canary fail-fast gate for skills, and 16 custom slash commands for plugin documentation, auditing, implementation, schema work, and issue investigation. Required reading before creating new hooks, commands, or agent workflows.

---

## Overview

Bannou uses Claude Code with custom configuration organized in three layers:

1. **Hooks** — Real-time procedural gates that fire on specific tool calls. Some block (destructive git, production deploys), some remind (frozen files, task format, language patterns). Hooks enforce rules at the moment of action rather than relying on Claude remembering rules from thousands of tokens ago.
2. **Skills** — Mechanical checklists for complex workflows (auditing, implementation, testing). Each skill is a step-by-step procedure with verification at each stage.
3. **Agents** — Project-aware sub-agents that pre-load Bannou context (tenets, patterns, schema rules) before doing work.

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

## Configuration Files

```
.claude/
├── settings.json              # Hook configuration (checked into repo)
├── settings.local.json        # Local permissions (gitignored)
├── permission-canary.txt      # Permission gate canary file (see below)
├── system-prompt-original.md  # Original Claude Code system prompt (reference)
├── system-prompt-modified.md  # Modified system prompt (edit here, sync to config)
├── skills-guide.txt           # Skill discovery reference
├── hooks/
│   ├── frozen-file-check.sh           # PreToolUse: frozen dir + test weakening check
│   ├── no-minimizing-language.sh      # PreToolUse: language pattern detection (8 categories)
│   ├── block-backwards-compatibility.sh # PreToolUse: backwards compat language (5 categories)
│   ├── block-all-agents.sh            # PreToolUse: restricts to project-aware agent types
│   ├── block-worktree-isolation.sh    # PreToolUse: blocks worktree isolation
│   ├── block-agent-polling.sh         # PreToolUse: blocks agent resume attempts
│   ├── block-destructive-git.sh       # PreToolUse: blocks destructive git commands
│   ├── block-production-deploy.sh     # PreToolUse: blocks production publish commands
│   ├── block-integration-tests.sh     # PreToolUse: blocks long-running test suites
│   ├── block-file-moves.sh            # PreToolUse: blocks mv on code files
│   ├── block-symlinks.sh              # PreToolUse: blocks symbolic link creation
│   ├── block-read-limit.sh            # PreToolUse: reminds about full file reads
│   ├── validate-senryu-commit.sh      # PreToolUse: enforces senryu commit format
│   ├── git-history-reminder.sh        # PreToolUse: reminds about git history usage
│   ├── task-creation-reminder.sh      # PreToolUse: reminds about task list format
│   ├── track-parallel-reads.sh        # PostToolUse: silent read counter for skills
│   └── post-edit-reminder.sh          # RETIRED (replaced by frozen-file-check.sh)
├── commands/                          # Custom slash commands (skills)
│   ├── audit-plugin.md
│   ├── check-plugin.md
│   ├── implement-feature.md
│   ├── implement-plugin.md
│   ├── investigate-issue.md
│   ├── maintain-faq.md
│   ├── maintain-guide.md
│   ├── maintain-operations-doc.md
│   ├── maintain-planning-doc.md
│   ├── maintain-plugin.md
│   ├── map-plugin.md
│   ├── orchestrate-skill.md
│   ├── propose.md
│   ├── schema-plugin.md
│   ├── test-plugin.md
│   └── update-permissions.md
├── agents/                            # Custom agent definitions
│   ├── bannou.md              # General project awareness (CLAUDE.md + CLAUDE-PRACTICES.md)
│   ├── bannou-dev.md          # Development (above + tenets + patterns)
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

| Hook | Matcher | What It Blocks |
|------|---------|----------------|
| `validate-senryu-commit.sh` | Bash | Commits without senryu first line (5-7-5 format with ` / ` separators) |
| `block-destructive-git.sh` | Bash | `git restore`, `git checkout --`, `git checkout .`, `git stash`, `git reset`, `git clean` |
| `block-production-deploy.sh` | Bash | `make push-release`, `make publish-sdk-ts` |
| `block-integration-tests.sh` | Bash | `make test-http`, `make test-edge`, `make test-infrastructure`, `make all` |
| `block-file-moves.sh` | Bash | `mv` targeting code file extensions |
| `block-symlinks.sh` | Bash | `ln -s` / `ln --symbolic` |
| `block-worktree-isolation.sh` | Agent, EnterWorktree | Agent calls with `isolation: "worktree"`, EnterWorktree tool |
| `block-agent-polling.sh` | Agent | Agent calls with `resume` parameter |
| `block-all-agents.sh` | Agent | Non-approved agent types (only `bannou`, `bannou-dev`, `bannou-schema`, `bannou-code-reviewer` allowed) |

### Reminder Hooks

| Hook | Matcher | What It Reminds |
|------|---------|-----------------|
| `frozen-file-check.sh` | Edit, Write | "This file is in a frozen directory. Proceed if you have explicit authorization." Also checks test files for weakening patterns. |
| `no-minimizing-language.sh` | .* (all) | Detects 8 categories of language that signal rationalization, minimization, self-authorization, exception-invention, precedent-mining, work-avoidance, or corner-cutting |
| `block-backwards-compatibility.sh` | .* (all) | Detects backwards-compatibility language, breaking-change anxiety, compatibility shims, soft removal, consumer anxiety |
| `block-read-limit.sh` | Read | "Read full files — you have a 1M token context window" when limit/offset parameters are used |
| `git-history-reminder.sh` | Bash | "Use git history to understand changes, not to justify reverting completed work" on `git diff`/`git log` |
| `task-creation-reminder.sh` | TaskCreate, TodoWrite | Reminds of the 5-element format for violation task lists |

### PostToolUse Hooks

| Hook | Matcher | What It Does |
|------|---------|--------------|
| `track-parallel-reads.sh` | Read | Silently appends a character to `/tmp/.parallel-read-token` on each Read. Skills clear the file before parallel read phases and check the count after. |

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

## Custom Slash Commands (Skills)

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

| Agent | Pre-reads | Use For |
|-------|-----------|---------|
| `bannou` | CLAUDE.md, CLAUDE-PRACTICES.md | General tasks needing project awareness |
| `bannou-dev` | Above + all tenet files + HELPERS-AND-COMMON-PATTERNS.md | Implementation, code review, auditing, tenet compliance |
| `bannou-schema` | Above + SCHEMA-RULES.md + specifications catalog + scripts catalog | Schema work, generation, extension attributes |
| `bannou-code-reviewer` | Above + all tenet files + all plugin source code | Read-only deep code review for tenet violations |

All agents are restricted to safe tool sets. Non-approved agent types (general-purpose, Explore, Plan, feature-dev:*, etc.) are blocked by `block-all-agents.sh`.

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

## Task Tool Limitations

### Background Agents Cannot Use Skill Tool

Background agents (`run_in_background: true`) have the Skill tool auto-denied ("prompts unavailable"). This is a Claude Code architectural limitation. Workaround: `/orchestrate-skill` embeds full skill content in agent prompts instead of using the Skill tool.

### Permission Modes

| Mode | Behavior |
|------|----------|
| `default` | Standard permission checking with interactive prompts |
| `acceptEdits` | Auto-accept file edit permissions |
| `bypassPermissions` | Skip all permission checks (still doesn't help Skill in background) |

---

## Troubleshooting

| Issue | Resolution |
|-------|------------|
| Hook not triggering | Verify `settings.json` has the hook configured; check `chmod +x`; ensure `jq` is installed |
| Command blocked unexpectedly | Read the block message — it explains why. Ask user to run manually if legitimate |
| Slash commands not available | Restart Claude Code session; verify `.md` file exists in `.claude/commands/` |

---

## References

- [CLAUDE.md](../../CLAUDE.md) — Main project instructions
- [CLAUDE-PRACTICES.md](../../CLAUDE-PRACTICES.md) — Behavioral practices
- [INCIDENT-HISTORY.md](../reference/INCIDENT-HISTORY.md) — Hook incident history
- [TENETS.md](../reference/TENETS.md) — Development standards
- [Anthropic Prompt Engineering Best Practices](https://platform.claude.com/docs/en/build-with-claude/prompt-engineering/claude-4-best-practices) — Official Claude 4.6 guidance
- [Hooks Reference](https://docs.anthropic.com/en/docs/claude-code/hooks) — Official hook documentation
- [Best Practices for Claude Code](https://code.claude.com/docs/en/best-practices) — Official Claude Code guidance
