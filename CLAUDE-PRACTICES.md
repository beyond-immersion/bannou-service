# Bannou Development Practices

> Behavioral practices for AI agents. `@included` from `CLAUDE.md`, loaded every conversation.

**The core practice**: When you encounter a problem, surface it clearly, present options, and wait for direction. The procedures below make this concrete.

---

## 1. Surface Problems Clearly

When you find something wrong — a tenet violation, a bug, a design gap — state it directly and let the user decide.

**Tenet audits are mechanical**: Read the tenet. Does the code comply? No = finding. Yes = not a finding. Do not search for other violations to contextualize. Do not assess severity or blast radius. Present the finding with the tenet text and the code.

**When a code review identifies a violation**: The fix is to change the code. Present the finding to the user. If you believe the finding is a false positive, cite the specific tenet text that defines the exception. Three things qualify as false positives: (1) the tenet explicitly defines an exception covering this case, (2) the finding is factually wrong, (3) the tenet applies to a different category of code.

**Design considerations are open questions**: If a deep dive says "needs profiling at scale," that is an open question you cannot answer from code inspection. Create an issue. You cannot measure performance at 100K NPC scale.

**Hooks enforce language patterns**: `no-minimizing-language.sh` fires when it detects minimization, rationalization, self-authorization, exception-invention, precedent-mining, work-avoidance, or efficiency corner-cutting. If a hook fires, re-read its message and adjust your approach.

---

## 2. Execute Decisions Completely

This project is pre-release with zero external consumers.

When told to change X, change X everywhere. When told to remove Y, delete Y completely. Use grep to find all references and update them in the same commit. The generated clients are regenerated from schemas. There are no mystery callers.

**Hook-enforced**: `block-backwards-compatibility.sh` catches compatibility shims, soft removal, breaking-change anxiety, and consumer anxiety language. If the hook fires, you are partially executing a decision.

---

## 3. Follow Instructions as Given

- Do what was asked, not what you think is better. Do not substitute judgment for explicit instructions.
- "All" means all. "Read" means use the Read tool yourself (not an agent summary).
- Tenets are law. Any conflict must be presented to the user before proceeding.
- If you believe an instruction is wrong, say so before deviating. You do not have permission to silently skip instructions.

---

## 4. Decision Checkpoints

When you hit any of these situations, stop work and present the situation to the user:

| Situation | What to present |
|-----------|-----------------|
| You changed X, expected Y, got Z | What you changed, what you expected, what happened, and your options |
| Instructions depend on data you don't have | What data is missing and why you need it |
| Instructions require a tool or capability you don't have | What capability is missing, what you cannot do without it, and why you cannot proceed as instructed |
| The code you'd write would be a no-op or tautology | What the test protects and what infrastructure is missing |
| Implementation requires a behavioral choice not in instructions | The decision point, the options, and their consequences |

Do not attempt to work around any of these. The first workaround creates a new problem requiring a second workaround. Each layer makes reverting harder.

---

## 5. Frozen Artifacts

Frozen directories: `scripts/`, `docs/reference/`, `structural-tests/`, `test-utilities/`, `.claude/hooks/`, `.claude/skills/`, `.claude/settings.json`.

Present concerns about frozen files and wait for explicit, in-conversation instruction before modifying. Detailed rules load contextually via `.claude/rules/frozen-files.md`. Schemas (`schemas/*.yaml`) are not frozen.

---

## 6. Agent Practices

- All agents work on the current branch in the main working directory (hook-enforced: `block-worktree-isolation.sh`).
- After launching background agents, end your response. You will be notified via `<task-notification>` when they complete (hook-enforced: `block-agent-polling.sh`).
- Maximum 3 concurrent agents per message. Present the batching plan if more work is needed.
- Every agent prompt includes a tool use budget. Defaults: simple lookup 5-8, focused research 10-15, broad exploration 15-25, implementation 20-30.
- Violation task lists use `TaskCreate` with: (1) verbatim tenet text, (2) affected file:line, (3) before/after code, (4) what not to do, (5) verification command.

---

## 7. Tool Practices

- Read a file before editing it. Use the original source, not temp/persisted output files.
- Redirect heavy commands (>10 seconds) to file: `command > /tmp/output.txt 2>&1`, then Read. Run once, read the output as many times as needed.
- Use scoped builds for single-plugin changes: `dotnet build plugins/lib-{service}/lib-{service}.csproj --no-restore`.
- Destructive git commands require explicit approval (hook-enforced: `block-destructive-git.sh`).

---

## 8. Code Practices

### Null Safety
- Use explicit null checks with meaningful exceptions or proper test data. Null-forgiving operators (`!`, `null!`, `default!`) are not used in this codebase.
- `?? string.Empty` has two accepted patterns (compiler satisfaction and external service defensive coding), both requiring explanatory comments.

### Identifiers
- Every type name, method name, and property name comes from one of three sources: (1) the implementation map, (2) a `make inspect-*` or `make print-models` call, (3) reading the identifier in source code. If you don't have the exact name from one of these sources, look it up before writing code.

### Environment & Testing
- Use .env files for configuration. Integration tests run only when the user asks (hook-enforced: `block-integration-tests.sh`).
- Testing rules load contextually via `.claude/rules/testing-patterns.md` when working in test projects.
- Structural test failures are implementation gaps, not test problems. Implement the missing logic — do not write stubs unless the entire service is in a pre-implementation state.

### Cross-Service Data
- `additionalProperties: true` is not a data contract between services. See FOUNDATION TENETS (T29) for the full rule. Issue #308 tracks existing violations.

---

## Reference

For the history of incidents that shaped these practices and the hooks that enforce them, see `docs/reference/INCIDENT-HISTORY.md`. Consult it when performing code reviews or audits.
