# Bannou Development Restrictions

> Behavioral restrictions and mandatory constraints for AI agents. `@included` from `CLAUDE.md`, loaded every conversation.

**The meta-rule**: Every restriction here exists because Claude did the bad thing at least once. The recurring failure mode is: encounter a problem → hide/silence/workaround the problem → present "clean" output. The correct response to every problem is: surface it, present it, wait for direction.

---

## 1. Never Hide Problems

These rules share one principle: **signals that indicate problems must never be silenced.**

- [ ] **No whitelists in structural tests.** Never add `PendingExceptions`, `AllowedViolations`, `KnownIssues`, skip attributes, or any mechanism that causes violations to not appear in output. A failing structural test is a working test. Only the human adds exception lists.
- [ ] **No warning/analyzer suppression.** Never add `<NoWarn>`, `#pragma warning disable`, `dotnet_diagnostic.*.severity = none`, or `[SuppressMessage]`. Fix the code or present the situation to the user. Existing `CS8620;CS8619` suppressions for Moq were added by the human.
- [ ] **No softening of tenet findings.** When auditing code against tenets, the tenet text is the sole authority. Do not downgrade violations to "quality improvements" or "informational." Do not search the codebase for other violations to argue "established pattern."
- [ ] **Tenet audit is mechanical**: (1) Read tenet. (2) Does code comply? No → finding. Yes → not a finding. (3) There is no step 3.

**What IS a false positive** (exhaustive):
- Tenet explicitly defines an exception covering this case (cite the text)
- Finding is factually wrong (code actually complies)
- Tenet applies to a different category of code

**What is NOT a false positive**:
- "Other services do it this way" — more violations, not fewer
- "It's intentional/by design" — you don't know that; present to user
- "The blast radius is small" — irrelevant to compliance
- "It would be inconsistent to fix only this service" — note ALL affected services

**The forbidden audit pattern**: Finding code that contradicts a tenet, then searching for other code that also contradicts it, and concluding "established pattern, not a violation." Finding more violations proves the problem is widespread — it does NOT prove the tenet is wrong. A missed violation stamped "false positive" is catastrophic — it immunizes the violation against all future audits permanently.

**Incidents**: (1) Claude whitelisted violations in every structural test it ever wrote. (2) Claude added `xUnit1051` to `<NoWarn>` in `Directory.Build.props` to silence 7,000 analyzer errors. (3) Claude grepped 3 services for the same topic naming violation, found it everywhere, dismissed as "established pattern."

---

## 2. Follow Instructions Exactly

- [ ] **Do what was asked, not what you think is better.** Do not substitute judgment for explicit instructions. Do not skip steps because you think you know the result.
- [ ] **"All" means all.** If told to read all files in a directory, list the directory first and read every file. Do not assume you know what exists.
- [ ] **"Read" means you read it.** Use the Read tool yourself. Do not launch an agent to "read X and summarize the key points." Summaries lose information — the point is having full content in YOUR context. Exception: user explicitly says to use agents for reading.
- [ ] **Tenets are law.** Follow them without exception. Any conflict with a tenet must be presented to the user with context before proceeding.
- [ ] **If you believe an instruction is wrong**, say so BEFORE deviating. You do not have permission to silently skip instructions.

---

## 3. Hard Stop Triggers

**When any trigger fires: STOP. Do not workaround. Present the situation. Wait for direction.**

| Trigger | Detection | What to report |
|---------|-----------|----------------|
| **A: Unexpected Consequence** | You changed X, expected Y, got Z | What you changed, expected, got, and options |
| **B: Missing Information** | Instructions depend on data you don't have | What data is missing and why you need it |
| **C: Infrastructure Gap** | Code you'd write to pass a test would be a no-op/tautology | What the test protects, what infra is missing |
| **D: Unspecified Design Decision** | Implementation requires a behavioral choice not in instructions | The decision point, options, and consequences |

**When a trigger fires, do NOT**:
- (A) Attempt to "fix" the unexpected consequence. Do NOT add workarounds, aliases, shims, or compatibility layers.
- (B) Attempt to "discover" or "re-derive" missing information on your own. Do NOT silently adjust the task.
- (C) Write meaningless code that technically passes a test. If your fix would be a no-op or tautology, that IS Trigger C.
- (D) Make the design decision yourself. If you are about to write code that changes observable behavior and the user did not specify that behavior, that IS Trigger D.

**The compound damage pattern**: Workaround #1 creates a new problem requiring workaround #2. Each layer makes reverting harder. By workaround #3 you are debugging workarounds, not the original problem. **The first workaround is already one too many without user approval.**

**Incidents**: (A) Agent moved enums between schemas, got duplicate types, spent multiple cycles layering C# `using` alias workarounds. (B) Claude lost gap lists during compaction, silently re-launched discovery agents instead of asking. (C) Claude wrote tautological enum roundtrip tests when `EnumMappingValidator` lacked string-to-enum methods. (D) Claude autonomously decided `deleteByCharacter` should delete all quest instance records without reading the cleanup contract.

---

## 4. Frozen Artifacts

**Never modify without explicit, in-conversation user instruction. Present concerns and wait.**

Frozen directories: `scripts/`, `docs/reference/`, `structural-tests/`, `test-utilities/`, `.claude/hooks/`, `.claude/commands/`, `.claude/settings.json`. Detailed rules and incident history load contextually via `.claude/rules/frozen-files.md` when working near these paths.

- [ ] Never modify, never add exceptions/allowlists, never "fix" what you think is wrong
- [ ] "Explicit" means in-memory, in-conversation, direct instruction — not a compacted summary or task description
- [ ] **Schemas are NOT frozen.** `schemas/*.yaml` is the primary artifact developers edit. Fix schema → regenerate → generated artifact appears → code uses it → test passes. Never hand-write what generation should produce.

---

## 5. Agent Discipline

- [ ] **No worktrees.** Never use `isolation: "worktree"`, `EnterWorktree`, or `ExitWorktree`. All agents work on the current branch in the main working directory. Hook-enforced.
- [ ] **No background polling.** After launching background agents, END YOUR RESPONSE. Do not resume, read output files, or `tail` progress. You will be notified via `<task-notification>` when agents complete.
- [ ] **Maximum 3 concurrent agents per message.** If more work is needed, batch sequentially. Present the batching plan first.
- [ ] **Every agent gets a tool use budget in its prompt.** Defaults: simple lookup 5-8, focused research 10-15, broad exploration 15-25, implementation 20-30. Web research agents cap at 10-15. Unbounded agents are forbidden.
- [ ] **Violation task lists use `TaskCreate`** with five mandatory elements: (1) verbatim tenet text, (2) every affected file:line, (3) before/after code, (4) what NOT to do, (5) verification command.

---

## 6. Tool & Command Discipline

- [ ] **Chunked file reading.** Always specify `limit: 300` on Read calls. Never read temp/persisted output files — go back to the original source. Read a file before using Edit on it.
- [ ] **Heavy command output capture.** Commands >10 seconds or with substantial output → redirect to file (`command > /tmp/output.txt 2>&1`), then Read. Never run a heavy command twice. Set timeouts proportional to the command (generation: 300000ms, builds: 120000ms).
- [ ] **Never repeat commands.** If a command succeeded, do not run it again to "verify." Capture once, read many times.
- [ ] **Scoped builds only.** Single-plugin changes → `dotnet build plugins/lib-{service}/lib-{service}.csproj --no-restore`. Full solution builds only when changes span multiple projects.
- [ ] **Destructive git commands require explicit approval**: `git checkout`, `git stash`, `git reset`, `mv` (code files). Ask first, explain why, wait for approval.

---

## 7. Code Safety

### Null Safety
- [ ] **Null-forgiving operators (`!`) are forbidden.** No `variable!`, `null!`, `default!`, `(Type)null`. Use explicit null checks with meaningful exceptions or proper test data.
- [ ] **`?? string.Empty` is forbidden** except two patterns (both require explanatory comments):
  1. **Compiler satisfaction**: Coalesce can never execute but compiler can't prove it
  2. **External service defensive coding**: Third-party data with error logging + error event publication

### Environment & Testing
- [ ] **Never `export` environment variables.** Use .env files and Docker Compose configuration.
- [ ] **Never run integration tests unless explicitly asked.** No `make test-http`, `make test-edge`, `make test-infrastructure`, `make all`. A successful `dotnet build` is sufficient verification.
- [ ] **Testing rules load contextually** via `.claude/rules/testing-patterns.md` when working in test projects. Check `.github/workflows/ci.integration.yml` before integration testing work.

### Cross-Service Data
- [ ] **No metadata bag contracts.** Follow FOUNDATION TENETS (No Metadata Bag Contracts) in `docs/reference/tenets/FOUNDATION.md` to the letter. `additionalProperties: true` is never a data contract between services. Issue #308 tracks existing violations — they are tech debt, not precedent.
