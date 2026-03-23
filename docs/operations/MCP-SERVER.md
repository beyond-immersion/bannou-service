# Local MCP Server Operations Guide

> **Last Updated**: 2026-03-22
> **Scope**: Local MCP server architecture, tool inventory, sentinel injection system, frozen-directory protection, and Stream Deck integration
> **Companion**: [CLAUDE-SKILLS.md](CLAUDE-SKILLS.md) covers hooks, skills, and agent types. This document covers the MCP server infrastructure they operate on.

## Summary

The Bannou MCP server (`.claude/mcp/`) is a modular Node.js server that replaces Claude Code's built-in Read, Edit, Write, and Bash tools with a sandboxed alternative. It enforces read-before-write gates, preserves content on all write failures, validates C# structure automatically, restricts shell commands to a whitelist, sandboxes script creation, and manages frozen-directory permissions through an external injection system designed for Stream Deck integration. This document covers the current architecture, every tool, and the permission management workflow.

---

## Architecture

```
.claude/mcp/
├── server.mjs              ← Entry point: tool registrations (table of contents)
├── state.mjs               ← Shared mutable state (ES module singleton)
├── package.json
├── helpers/
│   ├── file-ops.mjs        ← Read tracking, gate checks, chunking
│   ├── structure.mjs       ← C# brace/region/preprocessor validation
│   ├── commands.mjs        ← Command whitelist, blocked patterns, execution
│   ├── sentinel.mjs        ← External state injection + frozen-directory protection
│   ├── scripts.mjs         ← Sandboxed script writing with syntax validation
│   ├── context.mjs         ← Context preparation, composite packing, profile resolution
│   ├── plugins.mjs         ← Plugin catalog, documentation retrieval
│   ├── docs.mjs            ← Documentation catalog, search, retrieval
│   ├── schemas.mjs         ← Schema catalog, retrieval
│   ├── infrastructure.mjs  ← Service details, events, state stores, model/interface shapes
│   ├── tool-output.mjs     ← Gated tool response (shared oversized output handler)
│   └── seed.mjs            ← (planned) Seed bundle generation orchestration
└── node_modules/
```

### Design Principles

1. **`server.mjs` is the table of contents.** Every tool is registered here as a thin wrapper calling a helper function. Scan this one file to see all available tools at a glance.

2. **`helpers/` contains the logic.** Each file is a single concern with pure functions (except `file-ops.mjs` which manages read-tracking state). Helpers are independently callable from other helpers and from future seed generation tools.

3. **`state.mjs` is the shared singleton.** ES modules are evaluated once and cached. Every file importing `state.mjs` gets the same object references. This is how `readFiles`, `pendingContinuations`, `grantedPermissions`, etc. are shared across all helpers without parameter passing.

4. **Shared helper pattern.** `validateStructure()` is called by both `move_lines` (automatic post-move check) and `validate_structure` (standalone tool). All helpers follow this pattern: callable from tools AND from orchestration tools like seed generation.

5. **Sentinel processing on every tool call.** The `registerTool` wrapper in `server.mjs` intercepts every tool invocation to check for a sentinel file before the handler runs. This is how external permission grants and messages are injected without agent involvement.

---

## Tool Inventory

### File Operations

| Tool | Purpose | Key Safety Features |
|------|---------|-------------------|
| `read_file` | Full file read with line numbers | Never truncates. Large files split into continuation parts. Tracks reads for edit/write gates. |
| `edit_file` | Exact string replacement | Requires `read_file` first. Validates uniqueness. Blocks edits on files with unread continuations. Checks frozen-directory protection. |
| `write_file` | Complete file write/overwrite | Requires `read_file` for existing files (not for new files). **Content preserved to `/tmp/` on any failure** — gate failure, frozen-path rejection, race condition, or unexpected error. Optional `expected_size_bytes` for race detection. Optional `dry_run` for gate testing. Auto-registers file as read after write. |
| `write_script` | Sandboxed script creation | Writes to `/tmp/bannou-scripts/` ONLY. Auto-chmod +x. Auto-shebang. Syntax validated (bash -n, py_compile, node --check). Allowed extensions: `.sh`, `.py`, `.mjs`. |
| `move_lines` | Line-range relocation between files | Both files must be read first. Safety anchors catch off-by-one errors. Automatic C# structure validation post-move. |
| `validate_structure` | C# structural integrity check | Balanced braces, `#region`/`#endregion`, `#if`/`#endif`. Runs automatically inside `move_lines`. |
| `run_command` | Whitelisted shell execution | Prefix whitelist + blocked patterns. Includes `/tmp/bannou-scripts/` for executing agent-created scripts. |

### Content Preservation Guarantee

`write_file` preserves content to `/tmp/bannou-write-recovery-{hash}.txt` on **every** failure type:

| Failure Type | Content Preserved? | Recovery Path Returned? |
|-------------|-------------------|----------------------|
| Read gate (file not read yet) | ✅ | ✅ |
| Frozen-directory protection | ✅ | ✅ |
| Race condition (size mismatch) | ✅ | ✅ |
| Unexpected error (filesystem, etc.) | ✅ (best-effort) | ✅ |
| Dry run | N/A (no write attempted) | N/A |

This means agent work is **never lost** due to a tool rejection. The content exists in `/tmp/` and the error message includes the exact path. The agent can re-read the target file and retry, or the user can retrieve the content manually.

---

## Frozen-Directory Protection

### Two-Tier System

The MCP server replicates the frozen-files list from `CLAUDE-PRACTICES.md` with a two-tier protection system:

**Tier 1 — Never Overridable** (not even with `grantPermissions`):

| Path | Why Permanent |
|------|--------------|
| `/tmp/bannou-mcp-inject-*` | Sentinel injection files — agent access would allow self-granting permissions |
| `scripts/restricted/` | Stream Deck scripts — agent modification would compromise the permission system |

**Tier 2 — Frozen by Default, Grantable** (via sentinel injection):

| Category | Path | What It Protects |
|----------|------|-----------------|
| `reference` | `docs/reference/` | Tenets, schema rules, helpers, vision docs |
| `scripts` | `scripts/` | Code generation pipeline |
| `structural` | `structural-tests/` | Structural validators |
| `test-utils` | `test-utilities/` | Shared test infrastructure |
| `hooks` | `.claude/hooks/` | Enforcement hooks |
| `skills` | `.claude/skills/` | Skill definitions |
| `settings` | `.claude/settings.json` | Permission config |
| `agents` | `.claude/agents/` | Agent definitions |

When an agent attempts to `edit_file` or `write_file` to a frozen path, it receives:

```
Error: /path/to/file is in a frozen directory (docs/reference/).
Present your concern to the user and ask them to grant temporary write permission before proceeding.
```

The error message deliberately does not mention the sentinel mechanism — agents should ask the user, not try to work around the system.

### Why MCP-Level Protection Matters

The existing PreToolUse hook `frozen-file-check.sh` protects against the built-in Edit/Write tools. But agents use MCP tools (`edit_file`, `write_file`) which bypass PreToolUse hooks entirely — they operate in a separate permission space. The MCP-level frozen-directory check closes this gap.

---

## Sentinel Injection System

### Overview

The sentinel system enables humans to inject state changes into the MCP server without agent involvement. It is the foundation for the Stream Deck permission workflow.

**How it works:**

1. An external process (Stream Deck button, terminal command, script) writes a JSON file to `/tmp/bannou-mcp-inject-{bucket}.json`
2. A `UserPromptSubmit` hook (`check-sentinel.sh`) detects the file when the user sends their next message and injects context telling the agent to process it
3. On the agent's next MCP tool call, the server reads the sentinel file, processes the commands, and deletes it
4. The agent sees the injected message (e.g., "📨 [External injection]: docs/reference/ unlocked for editing")

### Permission Buckets

Each Claude Code terminal runs its own MCP server process. The `BANNOU_MCP_BUCKET` environment variable (1-5, default 1) determines which sentinel file the server watches:

| Bucket | Sentinel File | Terminal |
|--------|--------------|---------|
| 1 | `/tmp/bannou-mcp-inject-1.json` | Default / Terminal 1 |
| 2 | `/tmp/bannou-mcp-inject-2.json` | Terminal 2 |
| 3 | `/tmp/bannou-mcp-inject-3.json` | Terminal 3 |
| 4 | `/tmp/bannou-mcp-inject-4.json` | Terminal 4 |
| 5 | `/tmp/bannou-mcp-inject-5.json` | Terminal 5 |

Permission grants on bucket 2 do not affect bucket 1. Each server process maintains its own `grantedPermissions` state (in-memory, per-process). This means granting `docs/reference/` edit permission on terminal 2 only unlocks that terminal — terminals 1, 3, 4, 5 remain frozen.

**Setting the bucket:** Launch Claude Code with the environment variable:
```bash
BANNOU_MCP_BUCKET=2 claude
```

### Sentinel File Format

```json
{
  "markRead": ["/path/to/file1", "/path/to/file2"],
  "grantPermissions": ["docs/reference/", "scripts/"],
  "revokePermissions": ["scripts/"],
  "message": "Optional message displayed to the agent"
}
```

| Command | Effect |
|---------|--------|
| `markRead` | Adds paths to the `readFiles` set — files can be edited without calling `read_file` first |
| `grantPermissions` | Temporarily unlocks frozen directories for writing (until revoked or server restarts) |
| `revokePermissions` | Re-freezes previously granted directories |
| `message` | Displayed to the agent as `📨 [External injection]: {message}` on the next tool call |

All commands are optional. Multiple commands can be combined in a single sentinel file.

### Security Model

- **Agents cannot create or modify sentinel files.** All `/tmp/bannou-mcp-inject-*` paths are in `NEVER_OVERRIDABLE` — both `write_file` and `edit_file` reject them unconditionally.
- **Agents cannot modify the permission scripts.** `scripts/restricted/` is in `NEVER_OVERRIDABLE`.
- **Grants are temporary.** They persist only in the MCP server's in-memory state. Restarting the server (or the terminal) clears all grants.
- **The agent doesn't know how grants work.** Error messages say "ask the user to grant permission" — not "write to the sentinel file." The mechanism is invisible to the agent.

---

## Stream Deck Integration

### Permission Scripts

Three scripts in `scripts/restricted/` handle all Stream Deck actions:

**`grant.sh <bucket> <category> [message]`** — Grant write permission:
```bash
./scripts/restricted/grant.sh 2 reference "Edit the tenets as discussed"
./scripts/restricted/grant.sh 1 all       # Unlock everything on bucket 1
```

**`revoke.sh <bucket> [category ...]`** — Revoke permissions:
```bash
./scripts/restricted/revoke.sh 2          # Revoke all grants on bucket 2
./scripts/restricted/revoke.sh 1 scripts  # Revoke only scripts/ on bucket 1
```

**`message.sh <bucket> "text"`** — Send a message to the agent:
```bash
./scripts/restricted/message.sh 2 "Proceed with the refactor"
```

### Stream Deck Layout Example

```
Folder: Terminal 1 (bucket 1)
  [Grant Reference]  → grant.sh 1 reference
  [Grant Scripts]    → grant.sh 1 scripts
  [Grant Structural] → grant.sh 1 structural
  [Grant Hooks]      → grant.sh 1 hooks
  [Grant Agents]     → grant.sh 1 agents
  [Grant Settings]   → grant.sh 1 settings
  [REVOKE ALL]       → revoke.sh 1
  [Message]          → message.sh 1 "Proceed"

Folder: Terminal 2 (bucket 2)
  [Grant Reference]  → grant.sh 2 reference
  ... (same pattern)
```

### Workflow Example

1. Agent encounters a frozen file and reports: "I need to edit `docs/reference/TENETS.md` but it's in a frozen directory."
2. You review the request and press **[Grant Reference]** on your Stream Deck for the appropriate terminal
3. You send any message (even "go ahead") — the `UserPromptSubmit` hook detects the sentinel and tells the agent to process it
4. The agent calls any MCP tool → sentinel is consumed → `docs/reference/` is unlocked
5. The agent edits the file
6. You press **[REVOKE ALL]** when done

The entire flow requires zero typing of commands — just button presses and conversation.

---

## The Transition: From Behavioral to Mechanical

This MCP server represents an ongoing transition from **behavioral restrictions** (rules the agent must remember and follow) to **mechanical restrictions** (constraints that are structurally impossible to violate).

### What's Already Mechanical

| Concern | Old Approach (Behavioral) | New Approach (Mechanical) |
|---------|--------------------------|--------------------------|
| File truncation | "Read the full file" instruction in CLAUDE.md | `read_file` has no truncation — it's not an option |
| Arbitrary shell | "Don't use bash for file operations" instruction | `run_command` only allows whitelisted commands |
| Edit without reading | "Always read before editing" instruction | `edit_file` gate — literally blocked until `read_file` is called |
| Content loss on write failure | Hope the agent retries | `write_file` preserves to `/tmp/` automatically |
| Frozen file modification | Hook reminder + agent judgment | `isPathProtected()` gate — blocked until human grants permission |
| Script creation outside sandbox | "Don't create scripts" instruction | `write_script` only writes to `/tmp/bannou-scripts/` |
| Permission self-granting | "Don't modify permission files" instruction | Sentinel path is in `NEVER_OVERRIDABLE` — rejected unconditionally |

### What's Planned

| Concern | Current Approach | Planned Mechanical Approach |
|---------|-----------------|---------------------------|
| Agent context loading | Instructions say "read these 8 files first" (logic gate) | `initialize_dev_context` tool adds files to required reading list (mechanical gate) |
| Context batching | Agent reads 8 files in 8 tool calls | Tool batches files into optimally-packed `/tmp/` composites, pre-registers originals as read |
| Documentation search | Agent greps file names and guesses relevance | `find_relevant_docs` tool does weighted tokenized search, returns deterministic file set |
| Seed data generation | Manual process | `generate_docs_seed` tool iterates shared helpers, writes bundle |

### Why This Matters for Sub-Agents

The primary reason sub-agents have been restricted to read-only code review is the risk of unsupervised writes. An agent working "out of sight" that encounters an unexpected situation will improvise — and improvisation in a 78-plugin codebase with strict tenets produces cascading damage that's expensive to find and fix.

Mechanical rails change the calculus:

- **Frozen directories** mean a sub-agent literally cannot modify tenets, scripts, structural tests, or hooks — even if it convinces itself it should
- **Content preservation** means a failed write doesn't silently disappear — the work is recoverable
- **Sandboxed scripts** mean a sub-agent can create utility scripts but only in a controlled location with syntax validation
- **Permission buckets** mean granting write access to one terminal doesn't affect agents on other terminals

With these rails in place, sub-agents can safely do implementation work — they have the tools they need (read, edit, write, validate, run commands) but cannot access the controls that govern how they operate. The permission system is external to the agent, invisible to the agent, and controlled exclusively by the human.

---

## Troubleshooting

| Issue | Resolution |
|-------|------------|
| Sentinel not processing | Check bucket: is `BANNOU_MCP_BUCKET` set in the terminal? Does the grant script target the right bucket? |
| Grant not taking effect | Send a message after pressing the Stream Deck button — the `UserPromptSubmit` hook detects the sentinel and primes the agent to call a tool |
| Permission persists after revoke | Check that `revoke.sh` targets the correct bucket. Grants are per-process — verify the right terminal |
| `write_file` says "frozen directory" | Ask the user to grant permission, or check if a previous grant was revoked |
| Content lost on write failure | It wasn't — check the error message for the `/tmp/bannou-write-recovery-*.txt` path |
| New terminal doesn't see grants | Correct — each terminal has its own MCP server process. Grants don't cross buckets. Set `BANNOU_MCP_BUCKET` and grant separately. |
| Script won't execute | Verify it was created with `write_script` (which auto-chmod). Scripts in `/tmp/bannou-scripts/` only. |
| Hook not detecting sentinel | Verify `check-sentinel.sh` is executable (`chmod +x`). Verify `jq` is installed. Check `BANNOU_MCP_BUCKET` env var propagates to hook subprocess. |

---

## References

- [CLAUDE-SKILLS.md](CLAUDE-SKILLS.md) — Hooks, skills, and agent types
- [CLAUDE.md](../../CLAUDE.md) — Main project instructions (MCP tool usage, generation commands)
- [CLAUDE-PRACTICES.md](../../CLAUDE-PRACTICES.md) — Behavioral practices (frozen artifacts list)
- [MCP Server Source](../../.claude/mcp/server.mjs) — Tool registrations (table of contents)
- [Sentinel Helper](../../.claude/mcp/helpers/sentinel.mjs) — Protection logic and sentinel processing
- [State Module](../../.claude/mcp/state.mjs) — Shared state, frozen prefixes, bucket configuration
- [Stream Deck Scripts](../../scripts/restricted/) — grant.sh, revoke.sh, message.sh
- [MCP-SERVER.md (Planning)](../planning/MCP-SERVER.md) — Runtime lib-mcp plugin plan (separate from local MCP server)
