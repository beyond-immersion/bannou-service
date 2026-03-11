# Bannou Development Restrictions & Mandatory Constraints

> This file contains all behavioral restrictions, mandatory constraints, and "never do X" rules for AI agents working in the Bannou codebase. It is `@included` from `CLAUDE.md` and loaded automatically in every conversation.

---

## ⛔ FOLLOW INSTRUCTIONS AS GIVEN ⛔

**You are REQUIRED to follow the instructions you are given, exactly as stated.** Do not substitute your own judgment for what was explicitly requested. Do not assume you already know the answer to something the user told you to go find out. Do not skip steps because you think you know what the result will be.

**If you are told to read all files in a directory, LIST THE DIRECTORY FIRST and read every file.** Do not assume you know what files exist based on references you've seen elsewhere. "All" means all -- not "the ones I'm aware of."

**If you believe an instruction is unnecessary, wrong, or should be modified**: Say so explicitly BEFORE deviating. You do not have permission to silently decide that an instruction doesn't need to be followed. Either follow the instruction or explain why you aren't. There is no third option.

**Why this rule exists:** Claude has repeatedly ignored clear, direct instructions by substituting assumptions for actual work. This causes wasted time, missed context, and broken trust. Following instructions is not optional.

---

## ⛔ TENETS ARE LAW ⛔

**ALWAYS REFER TO AND FOLLOW THE TENETS WITHOUT EXCEPTION. ANY SITUATION WHICH CALLS INTO QUESTION ONE OF THE TENETS MUST BE EXPLICITLY PRESENTED TO THE USER, CONTEXT PROVIDED, AND THEN APPROVED TO CONTINUE.**

### ⛔ TENET AUDIT INTEGRITY (MANDATORY) ⛔

**When auditing code against tenets, the tenet text is the SOLE AUTHORITY. The codebase is the subject being judged, not a source of counter-evidence.**

**The forbidden pattern**: Reading a tenet rule, searching the codebase for code that contradicts it, finding violations in other files, and concluding "this is an established pattern, so it's not a violation." This is backwards — finding more violations proves the problem is widespread, it does NOT prove the tenet is wrong.

**Rules**:
1. **NEVER search the codebase to validate or invalidate a tenet finding.** If the QUALITY TENETS naming conventions say `{entity}.{action}` and the code uses `entity.sub.action`, that is a violation. Period. You do not get to grep for other three-part topics to build a case that the tenet "doesn't really mean that."
2. **If existing code contradicts a tenet, that is an ADDITIONAL violation to report**, not evidence that the original finding is a false positive.
3. **A "false positive" means the tenet genuinely does not apply to the situation** (e.g., the code is in a category the tenet explicitly exempts). It does NOT mean "other code also does this" or "this seems like it should be okay."
4. **Do not soften findings.** Do not downgrade violations to "quality improvements" or "informational" or "medium-priority." If the tenet says X and the code does not-X, it is a violation at the severity the tenet defines.

### ⛔ EVALUATING AUDIT FINDINGS IS NOT A JUDGMENT CALL ⛔

**When a code review agent reports a tenet violation, you do NOT get to decide whether it's "actually a problem." You evaluate it against the tenet text ONLY. If the tenet says X and the code does not-X, it goes on the task list. Period.**

**The forbidden evaluation pattern**: An agent reports a finding. You grep the codebase to see if "other services do it too." They do. You mark it "false positive — established pattern." **This is the single most destructive thing you can do during a hardening pass.** You just validated the violation, gave it a clean bill of health, and ensured it will never be fixed — not in this plugin, not in the ones you checked, not anywhere. You turned an audit into a rubber stamp.

**What happened**: During character-encounter hardening, a code review agent correctly reported that write endpoints had `x-permissions: []` (X-Permissions violation — anonymous access to mutation endpoints). Instead of putting it on the task list, Claude grepped `character-personality-api.yaml` and `character-history-api.yaml`, found the same `x-permissions: []`, and concluded "this is the established pattern for internal L4 services" — dismissing the finding as false positive. The result: three plugins with the same security gap, now officially blessed as "correct." The hardening pass that was supposed to catch problems instead certified them.

**The evaluation rule is mechanical, not judgmental**:
1. Read the tenet text
2. Does the code comply with what the tenet says? YES → not a finding. NO → it's a finding.
3. There is no step 3. You do not get to decide the finding is "acceptable" or "intentional" or "by design" or "a concern to document rather than fix." Those are decisions for the human.

**What IS a false positive** (exhaustive list):
- The tenet explicitly defines an exception that covers this case (cite the exception text)
- The finding is factually wrong (the code actually does comply — the agent misread it)
- The tenet applies to a different category of code than what was found (e.g., IMPLEMENTATION TENETS spans on synchronous methods — the telemetry tenet explicitly says "Only async methods need spans")

**What is NOT a false positive**:
- "Other services do it this way" — that's more violations, not fewer
- "It's intentional" — you don't know that; present it to the user
- "The design justifies it" — the tenets ARE the design; code that doesn't match is wrong
- "It's a concern, not a violation" — if the tenet mandates it, it's a violation
- "The blast radius is small" — irrelevant to whether it's a violation
- "It would be inconsistent to fix only this service" — then note ALL affected services

**Why this is the most important rule**: Every other rule in this document protects against adding bad code. This rule protects against **certifying existing bad code as correct**. A missed violation is bad; a missed violation stamped "false positive" is catastrophic, because it immunizes the violation against future audits. When the next hardening pass finds the same issue, it will see your "false positive" evaluation and skip it again. The violation becomes permanent.

**Incident log** (add new incidents here):
1. QUALITY TENETS three-part event topics: Grepped actor/asset/puppetmaster, found same pattern, dismissed as "established." Result: topic naming violations in 3+ services blessed as correct.
2. ~~FOUNDATION TENETS x-permissions on L4 write endpoints~~: **RETRACTED** (2026-03-06) — Original finding was based on a documentation error in SCHEMA-RULES.md that described `x-permissions: []` as "Explicitly public (rare)." Investigation of issue #580 revealed `[]` actually means "not exposed to WebSocket clients" (service-to-service only). The `[]` on L4 write endpoints was correct all along — those endpoints are called by event handlers via lib-mesh, not by WebSocket clients. The documentation error has been fixed in SCHEMA-RULES.md, FOUNDATION.md, and TENETS.md.

---

## ⛔ CHUNKED FILE READING (MANDATORY) ⛔

**Always read files in chunks of 300 lines max using the `limit` and `offset` parameters.** Never call `Read` on a file without specifying `limit`. When a file requires multiple chunks, read them sequentially using `offset`.

**If the Read tool output is truncated and saved to a temp file: NEVER read the temp file.** Go back to the original source file and read it with `offset`/`limit` parameters instead. Temp files contain duplicated line-number prefixes that make them even larger, guaranteeing the same truncation will happen again.

**Why this rule exists:** Claude's Read tool has an output buffer smaller than 2000 lines of dense markdown. When a file exceeds the buffer, the system truncates it, saves the full output to a temp file, and returns only a 2KB preview. Claude then reads the temp file, which also gets truncated, wasting multiple rounds of tool calls and 3x+ the context window on content that should have been read once. This has repeatedly destroyed session context budgets during audit work.

**Rules:**
1. **Always specify `limit: 300`** (or smaller) on every Read call
2. **For files you know are small** (< 200 lines), `limit: 300` still works and costs nothing
3. **For multi-chunk reads**, use parallel Read calls with different offsets when the chunks are independent
4. **Never re-read temp/persisted output files** — always go back to the original source path
5. **If you don't know a file's size**, start with `limit: 300` from offset 0 and continue as needed
6. **Before using Edit on any file, call Read on it first** — the Edit tool requires a prior Read call even if you already saw the file contents via Bash, Grep, or other tools. A failed Edit wastes the carefully prepared change and the retry is often lower quality.

---

## ⛔ FORBIDDEN DESTRUCTIVE COMMANDS ⛔

**The following commands are ABSOLUTELY FORBIDDEN without explicit user approval:**

- `git checkout` - Destroys uncommitted work
- `git stash` - Hides changes that may be lost
- `git reset` - Can destroy commit history
- `mv` (for code files) - Can lose files or break references

**Why these are forbidden:**
These commands can destroy work in progress, hide changes, or cause data loss. Claude has repeatedly caused damage by using these commands without understanding the consequences.

**If you need to undo changes:**
1. ASK the user first
2. Explain what you want to undo and why
3. Wait for explicit approval
4. Use the least destructive method possible

**Principle**: Understand before acting. Never assume reverting is safe.

---

## ⛔ HEAVY COMMAND OUTPUT CAPTURE (MANDATORY) ⛔

**When running any command that takes more than 10 seconds or produces substantial output, you MUST redirect all output to a file FIRST, then read the file.**

**Pattern:**
```bash
command > /tmp/output.txt 2>&1
```
Then use the Read tool on `/tmp/output.txt`.

**Rules:**
1. **NEVER run a heavy command twice.** If you need to examine different parts of the output, read the file with different offsets — do not re-execute the command.
2. **NEVER re-run a command just to grep its output differently.** Capture once, read many times.
3. **Set timeouts proportional to the command.** Full regeneration (`generate-all-services.sh`) needs 300000ms minimum. Full builds need 120000ms. Do not use the default 120000ms timeout for commands you know are longer.
4. **If a background command completed, read its output file** — do not re-run the command to see what happened.

**Why this rule exists:** Claude repeatedly ran `generate-all-services.sh` (a 3+ minute command touching 76+ services) three times in succession — once to generate, then twice more just to grep different patterns from the output. Each re-run wasted 3+ minutes and put unnecessary load on the system. One redirect-to-file would have made the output available for unlimited examination at zero cost.

**The pattern to avoid:**
```bash
# WRONG: Run heavy command, see partial output, run it AGAIN with grep
heavy_command 2>&1 | tail -30        # Run 1: see end
heavy_command 2>&1 | grep "FAIL"     # Run 2: find failures (WHY ARE YOU RUNNING IT AGAIN?)
heavy_command 2>&1 | grep -v SUCCESS # Run 3: filter differently (STOP)

# CORRECT: Capture once, examine freely
heavy_command > /tmp/output.txt 2>&1  # Run once
# Then use Read tool on /tmp/output.txt with offset/limit as needed
```

---

## ⛔ UNEXPECTED CONSEQUENCES = HARD STOP ⛔

**If a change you made produces unexpected errors, unexpected behavior, or unexpected side effects: STOP IMMEDIATELY. Do not attempt to work around it. Do not layer fixes on top of a surprise. Present the unexpected result to the user and ask for directions.**

**The trigger is simple**: You made a change. You expected outcome X. You got outcome Y instead. **STOP.**

**What "stop" means**:
1. Do NOT attempt to "fix" the unexpected consequence
2. Do NOT add workarounds, aliases, shims, or compatibility layers
3. Do NOT continue down the current path hoping the next fix will resolve it
4. DO explain: what you changed, what you expected, what actually happened, and what the options are
5. DO wait for explicit direction before proceeding

**Why this rule exists**: An agent moved 6 enum definitions between schema files (a seemingly correct schema-first fix). Code generation produced duplicate types across two namespaces — an unexpected consequence. Instead of stopping, the agent spent multiple build-fix-rebuild cycles layering increasingly complex C# `using` alias workarounds, each failing in a new way (file-level aliases lost to parent namespace resolution, namespace-scoped aliases triggered CS0576 conflicts, namespace alias prefixes required touching 16+ call sites). Every "fix" made the situation worse and harder to revert. **One hard stop at the first unexpected build failure would have saved all of that.**

**The compound damage pattern**:
- Workaround #1 seems small and reasonable
- Workaround #1 creates a new problem requiring workaround #2
- Each layer makes reverting harder and understanding the state more difficult
- By workaround #3+ you are debugging your workarounds, not the original problem
- **The first workaround is already one too many without user approval**

**This applies to**:
- Build failures from schema/generation changes you didn't predict
- Runtime behavior that differs from what you expected
- Test failures that don't match your mental model
- Any situation where reality diverged from your expectation

**Principle**: Surprises mean your mental model is wrong. When your model is wrong, more actions based on that model make things worse, not better. Stop, report, and let the human recalibrate.

---

## ⛔ MISSING INFORMATION = HARD STOP ⛔

**If you do not have the information required to perform a task, STOP IMMEDIATELY. Do not attempt to work around it. Do not silently substitute a "best effort" approach. Do not re-derive information that was already produced. Tell the user what you're missing and wait for direction.**

**The trigger is simple**: You were given instructions that depend on specific data (a gap list, a specification, a set of requirements, a prior analysis). You do not have that data. **STOP.**

**What "stop" means**:
1. Do NOT attempt to "discover" or "re-derive" the missing information on your own
2. Do NOT launch agents or run searches to reconstruct what was lost
3. Do NOT silently adjust the task to work without the missing data
4. Do NOT present a workaround as if it were the original plan
5. DO state exactly what information you're missing and why you need it
6. DO explain how the information was lost (compaction, context limit, etc.) if you know
7. DO wait for the user to provide the data or tell you how to recover it

**This is NOT a judgment call.** You do not get to decide "I can probably figure it out" or "a discovery phase will be quick." If the instructions say "use this list" and you don't have the list, you are blocked. Period. The user decides how to unblock you, not you.

**Why this rule exists**: Claude lost detailed gap lists (produced by audit agents over hours of work) when the conversation context was compacted. Instead of reporting "I no longer have the gap lists needed for these tasks," Claude silently pivoted to giving agents open-ended discovery instructions — re-doing hours of already-completed work, burning 20+ minutes on a single plugin with no useful output, and turning a hardening task into active waste. One sentence — "I lost the gap lists during compaction, how should I recover them?" — would have resolved the problem in under a minute. Instead, the silent workaround wasted time, destroyed trust, and produced nothing.

**The compound damage pattern**:
- You lack data needed for Step 1
- Instead of stopping, you substitute Step 0.5 ("let me figure it out first")
- Step 0.5 takes far longer than expected because you're re-doing prior work
- The results of Step 0.5 may not match the original data (different gaps found, different priorities)
- Meanwhile the user believes you're executing the original plan
- When the user discovers the substitution, all work from Step 0.5 onward is suspect

**This applies to**:
- Task descriptions that reference data no longer in context (compacted away)
- Instructions that depend on prior analysis you can no longer see
- Agent prompts that require specific lists, specifications, or findings you don't have
- Any situation where you would need to guess, re-derive, or approximate what was explicitly provided before

**Principle**: Executing without required data is worse than not executing at all. Wrong execution wastes time AND produces damage. A hard stop wastes nothing.

---

## ⛔ CODE GENERATION SCRIPTS ARE FROZEN ⛔

**The `scripts/` directory contains the code generation pipeline. These scripts are NEVER to be modified by an agent without EXPLICIT user instructions to change code generation behavior.**

**What this covers**: ALL files in `scripts/` — shell scripts (`generate-*.sh`, `common.sh`), Python scripts (`generate-*.py`, `resolve-*.py`, `extract-*.py`, `embed-*.py`), and NSwag templates (`templates/nswag/`).

**Why this rule exists**: An agent once changed namespace strings across 4 generation scripts in a single commit, silently breaking all 76+ services. The agent believed `.Common` was the correct namespace for common types when it was actually `.BannouService` — a mistake that cascaded into 22 compile errors and hours of debugging. Generation scripts are the foundation of the entire codebase; a wrong namespace, output path, or exclusion rule breaks everything downstream.

**Rules**:
1. **NEVER modify generation scripts** unless the user explicitly says "change the code generation to..."
2. **NEVER "fix" what you think is wrong** in generation scripts — present your concern and wait
3. **NEVER change**: namespace strings, output paths, exclusion lists, NSwag parameters, post-processing logic, or file naming conventions
4. **If generation output looks wrong**: the bug is almost certainly in a SCHEMA file, not in the generation scripts. Fix the schema first.
5. **If you must propose a script change**: show the EXACT diff, explain WHY, and wait for explicit approval

**The generation pipeline is correct until proven otherwise. Schemas are the source of truth; scripts are the transformation layer.**

---

## ⛔ REFERENCE DOCUMENTS ARE FROZEN ⛔

**The `docs/reference/` directory contains the authoritative rules, tenets, and design principles for the entire codebase. These documents are NEVER to be modified by an agent without EXPLICIT user instructions to change specific rules or content.**

**What this covers**: ALL files in `docs/reference/` and `docs/reference/tenets/`:
- **Tenets**: `TENETS.md`, `tenets/FOUNDATION.md`, `tenets/IMPLEMENTATION-BEHAVIOR.md`, `tenets/IMPLEMENTATION-DATA.md`, `tenets/QUALITY.md`, `tenets/TESTING-PATTERNS.md`, `tenets/IMPLEMENTATION.md`
- **Rules**: `SCHEMA-RULES.md`, `ENDPOINT-PERMISSION-GUIDELINES.md`
- **Architecture**: `SERVICE-HIERARCHY.md`, `ORCHESTRATION-PATTERNS.md`
- **Vision**: `VISION.md`, `PLAYER-VISION.md`
- **Templates & Reference**: `templates/DEEP-DIVE-TEMPLATE.md`, `templates/IMPLEMENTATION-MAP-TEMPLATE.md`, `templates/PLAN-EXAMPLE.md`, `COMPRESSION-CHARTS.md`

**Why this rule exists**: An agent wrote an incorrect rule into SCHEMA-RULES.md stating that service events should NOT use `allOf` with `BaseServiceEvent`. This was factually wrong — NSwag requires `allOf` for C# inheritance. But because it was written into an authoritative document, other agents enforced it as inviolable law, systematically "fixing" dozens of event schemas to comply with the bad rule. The protective guardrails that prevent casual modification also prevented agents from correcting the error. Bad rules were permanently enshrined and actively enforced, causing cascading damage across the codebase.

**The fundamental problem**: Unlike code, where bad changes are caught by compilation or tests, **a bad rule change is invisible**. It becomes indistinguishable from intentional rules. Every future agent reads it, trusts it, and enforces it. A single incorrect sentence in TENETS.md can corrupt dozens of services over weeks before a human notices. The damage is multiplicative and silent.

**Rules**:
1. **NEVER modify any file in `docs/reference/` or `docs/reference/tenets/`** unless the user explicitly says "change the rule to...", "update the tenet...", or similar direct instruction to change rule content
2. **NEVER "fix" what you think is wrong** in a reference document — present your concern and explain why you think the document is incorrect, then wait for explicit approval
3. **NEVER add new tenets, rules, exceptions, or carve-outs** — these require explicit human approval
4. **NEVER remove, weaken, or reinterpret existing rules** — if you believe a rule is wrong, report it as a finding and wait
5. **If you find an inconsistency** between a reference document and the codebase: the document is presumed correct and the code is presumed wrong. Present both to the user and wait for direction.
6. **If you must propose a rule change**: present the EXACT proposed change as a diff, explain WHY, cite the evidence, and wait for explicit approval

**The reference documents are correct until proven otherwise. When in doubt, enforce what the document says, not what you think it should say.**

---

## ⛔ STRUCTURAL TESTS AND TEST UTILITIES ARE FROZEN ⛔

**The `structural-tests/` and `test-utilities/` directories contain the codebase-wide structural validation infrastructure. These files are NEVER to be modified by an agent without EXPLICIT user instructions to change test behavior.**

**What this covers**: ALL files in `structural-tests/` (primarily `StructuralTests.cs`) and `test-utilities/` (all `*Validator.cs` files, `AssemblyMetadataScanner.cs`, `TestAssemblyDiscovery.cs`, `TestConfigurationHelper.cs`, and any future additions).

**Why this rule exists**: Structural tests validate patterns across ALL 76 services — constructor patterns, key builder patterns, hierarchy compliance, permission registration, event publisher completeness, resource cleanup methods, JSON serialization discipline, configuration class instantiation, and more. A single change to a validator heuristic (widening a match, narrowing a filter, adding an exception) affects the validity of ~979 test cases simultaneously. When an agent modifies a structural test to make a failing test pass, it may silently weaken validation for every other service that was previously passing correctly.

**The specific danger**: An agent assigned to "fix test failures" faces a choice: fix the code to comply with the test, or fix the test to accept the code. For normal unit tests this is a judgment call. **For structural tests it is NEVER acceptable to change the test.** Structural tests encode the tenets themselves — changing them is equivalent to changing a tenet, which is forbidden without explicit approval. The correct response to a structural test failure is ALWAYS to fix the code, not the test.

**Rules**:
1. **NEVER modify any file in `structural-tests/` or `test-utilities/`** unless the user explicitly says "change the structural test to...", "update the validator to...", or gives direct instruction to change test behavior
2. **NEVER widen or narrow validator heuristics** to make tests pass — present the failure and wait
3. **NEVER add exceptions, allowlists, or skip lists** to structural tests — those are the user's decision
4. **NEVER modify assertion thresholds, expected counts, or match patterns** without explicit approval
5. **If a structural test is producing a false positive**: present the evidence (which test, which service, why you believe it's false) and wait for the user to decide whether to adjust the test or fix the code
6. **If you must propose a structural test change**: show the EXACT diff, explain what it affects (how many services, which test cases), and wait for explicit approval

**"Explicit" means in-memory, in-conversation, direct instruction.** A summary context from a compacted conversation saying "fix the tests" does NOT qualify. A task description saying "resolve test failures" does NOT qualify. Only a direct, current-conversation instruction from the user to modify a specific structural test or validator qualifies.

**The structural tests are correct until proven otherwise. When a structural test fails, the code is presumed wrong — not the test.**

---

## ⛔ NO CONVENTION-BASED CROSS-SERVICE DATA SHARING ⛔

**Follow FOUNDATION TENETS (No Metadata Bag Contracts) in `docs/reference/tenets/FOUNDATION.md` TO THE LETTER.** The No Metadata Bag Contracts tenet covers the eight failures of metadata bag contracts, the only two legitimate uses for `additionalProperties: true`, the correct service-owned binding pattern, per-layer scenario guidance, and detection/enforcement rules. It is comprehensive and authoritative.

**Known existing violations** (tracked for remediation, not precedent): GitHub Issue #308 tracks the systemic `additionalProperties: true` problem. Existing violations in affix metadata, contract CustomTerms, and others are tracked for migration to typed schemas. **These are technical debt to fix, not patterns to follow.**

---

## ⛔ WORKTREE ISOLATION IS ABSOLUTELY FORBIDDEN ⛔

**NEVER use `isolation: "worktree"` on the Agent tool. NEVER use `EnterWorktree` or `ExitWorktree`. There is NO valid use case for worktrees in this project. Period.**

**Rules:**
1. **ALL agents work on the current branch in the main working directory.** No exceptions.
2. **NEVER set `isolation: "worktree"` on any Agent tool call** — the hook will block it, but you should never attempt it in the first place.
3. **NEVER use `EnterWorktree`** — the hook will block it, but you should never attempt it.
4. **There is no scenario where worktree isolation is beneficial.** Do not reason about whether "this particular case" might benefit from isolation. The answer is always no.

**Why this rule exists**: Claude launched 3 agents with `isolation: "worktree"` for tasks that all edited `structural-tests/StructuralTests.cs`. One agent wrote 5 of 7 service file changes ONLY to the invisible worktree branch, while the structural tests referencing those changes were written to the main branch. The result: tests that reference interfaces that only exist on an invisible branch the user cannot see or access. Work was effectively lost. The user had no way to know this happened until the damage was discovered.

**The fundamental problem with worktrees**: They create invisible branches. The user works on the main branch. Changes in worktrees are invisible to `git status`, `git diff`, and every normal workflow. When an agent writes some changes to the main branch and other changes to a worktree, the result is a split-brain state that is impossible to diagnose without specifically knowing to look for it. Worktrees are damage, not a feature.

**Enforcement**: PreToolUse hook `block-worktree-isolation.sh` hard-blocks both `isolation: "worktree"` on Agent tool calls and `EnterWorktree` tool calls.

**Incident log**:
1. 2026-03-11: 3 agents launched with `isolation: "worktree"` for structural test tasks. Task #2 wrote `IAccountDeletionCleanupRequired` to 7 service files in the worktree but only 2 reached the main branch. 5 service changes silently lost on invisible branch.

---

## ⛔ BACKGROUND AGENT POLLING IS FORBIDDEN ⛔

**When you launch background agents (`run_in_background: true`), you are FINISHED until they complete. Period.**

**Rules:**
1. After launching background agents, tell the user what you launched and END YOUR RESPONSE
2. Do NOT attempt to `resume` agents — you will be notified automatically when they complete
3. Do NOT read agent output files to "check progress"
4. Do NOT call `tail` on agent output files
5. Do NOT attempt any action related to the agents until you receive a `<task-notification>` with `status: completed`
6. When you receive completion notifications, THEN resume agents to get their results
7. If the user messages you while agents are running, respond to the user — do NOT use it as an excuse to poll agents

**What "wait" means**: Literally do nothing. Say "waiting for agents to complete" and stop generating output. The next thing you do related to agents is AFTER a completion notification arrives.

**Why this rule exists**: Claude once burned 10+ rounds of context repeatedly attempting to resume still-running agents, ignoring both system errors AND explicit user instructions to stop. Every failed resume attempt wastes context and produces nothing. The automatic notification system works — trust it.

---

## ⛔ MAXIMUM 3 CONCURRENT AGENTS (MANDATORY) ⛔

**You may NEVER launch more than 3 agents in a single message. This is a hard limit with zero exceptions.**

**Rules:**
1. **Maximum 3 Agent tool calls per message.** If you need more work done, launch 3, wait for them to complete, then launch the next batch.
2. **This applies to ALL agent launches** — foreground, background, any `subagent_type`, any purpose.
3. **If a task requires more than 3 agents**, break it into sequential batches of 3. Present the batching plan to the user before starting.
4. **Do not try to "optimize" by launching more.** The rate limit applies to the account, not per-agent. Excess agents hit the limit simultaneously, return nothing, and waste the entire budget.

**Why this rule exists**: Claude launched 13 agents in a single message to fix test compilation errors. Every agent beyond the first few hit the API rate limit and returned zero useful output — literally "You've hit your limit." The entire day's usage budget was burned in one message with nothing to show for it. The user got no work done and lost an entire session. This is the equivalent of burning money.

**The math is simple**: 13 agents × full context window each = 13× the token cost of doing the work sequentially in batches. When rate-limited, 100% of that cost is waste. Three agents at a time stays well within rate limits and actually completes work.

**Incident log**:
1. 2026-03-09: 13 agents launched to fix test compilation errors across tools/. All but 2-3 hit rate limits. Entire day's usage destroyed with zero deliverables.

---

## ⛔ VIOLATION TASK LISTS (MANDATORY) ⛔

**When creating a task list to fix TENET violations, SCHEMA-RULES issues, or other code quality/consistency problems, every task description MUST be fully self-contained. An implementer who has never seen the codebase or the tenets must be able to execute the task from the description alone, with zero additional file reads required.**

**Use `TaskCreate` for violation task lists** — not `TodoWrite`, not markdown files, not inline text lists. `TaskCreate` supports rich descriptions that can hold the required detail.

**Required task description format** (all five elements are mandatory):

1. **VERBATIM TENET TEXT**: Quote the exact rule being violated, copied from the tenet document. Do not paraphrase, summarize, or reference by number alone. The implementer must see the actual rule text to understand what compliance looks like.

2. **AFFECTED FILES AND LINE NUMBERS**: List every file path and line number where the violation occurs. Do not say "several files" or "multiple locations" — enumerate them all. Format: `plugins/lib-foo/FooService.cs:142`.

3. **BEFORE/AFTER CODE**: Show the exact current code (the violation) and the exact replacement code (the fix). If the fix requires new code rather than replacement, show where it goes and what surrounds it. If the fix is a schema change + regeneration, show the schema diff and note the regeneration command.

4. **WHAT NOT TO DO**: List explicit constraints that prevent common mistakes. Examples:
   - "Do NOT add a top-level try-catch — the generated controller already provides the catch-all boundary"
   - "Do NOT grep other services to see if they also violate this — that's more violations, not justification"
   - "Do NOT use `?? string.Empty` without a comment explaining why the coalesce can never execute"
   - "Do NOT move this to a different file — fix it in place"

5. **SELF-CONTAINED VERIFICATION**: State how the implementer verifies the fix is correct. Usually: "Run `dotnet build plugins/lib-foo/lib-foo.csproj --no-restore` — must compile with zero errors and zero new warnings."

**Why this rule exists**: Claude repeatedly created shallow task descriptions like "Fix error handling violation in AccountService" or "Update error handling per IMPLEMENTATION TENETS." These forced every implementer (human or agent) to re-read the tenet documents, re-discover the affected files, re-identify the line numbers, and re-derive the fix from scratch — duplicating hours of audit work that had already been done. The audit agent already found all of this information; the task description must preserve it. A task that requires additional research to execute is not a task — it's a second audit disguised as a task.

**The compound waste pattern**:
- Audit agent spends 5 minutes finding violation, reading tenet, identifying files and lines, understanding the fix
- Task is created as "Fix error handling in FooService"
- Implementing agent spends 5 minutes re-reading the error handling tenet, re-finding the files, re-understanding the violation
- That's 10 minutes for a 5-minute task — 50% waste, and the implementing agent may miss context the audit agent had

**Note**: PreToolUse hooks on `TaskCreate` and `TodoWrite` will remind you of this format. The hooks do not block — they are nudges, not gates.

---

## 🚨 Critical Development Constraints

**MANDATORY**: Never declare success or move to the next step until the current task is 100% complete and verified.

**COMPLETION VERIFICATION RULES**:
- All code must compile without errors (`dotnet build` succeeds)
- Architectural problems must be fully resolved, not worked around

**STEP-BY-STEP ENFORCEMENT**:
- Complete each step fully before proceeding
- Verify each step works in isolation
- Never skip ahead due to complexity or difficulty
- Ask for clarification rather than making assumptions that break the architecture

**SUCCESS DECLARATION PROHIBITION**:
- Never claim success when compilation errors remain
- Never call a task "complete" when core functionality is broken
- Never mark todos as complete when issues persist
- Always demonstrate working functionality before claiming success

---

## Code Prohibitions

### NEVER Repeat Commands
If a command succeeds, DO NOT run it again to "verify" or "see more output". If you need both the head and tail of command output, redirect to a file first (`command > /tmp/output.txt 2>&1`) then read the file. Repeating builds, tests, or any command wastes time and resources. Trust the output you already received.

### Scoped Builds ONLY
When you have only modified files in a single plugin, NEVER run a broad `dotnet build` (full solution). Build ONLY the specific plugin project: `dotnet build plugins/lib-{service}/lib-{service}.csproj --no-restore`. Full solution builds (`dotnet build` with no project path) are ONLY acceptable when changes span multiple projects or shared code (bannou-service/, schemas/, etc.). This saves significant build time and avoids unnecessary recompilation of 50+ unrelated projects.

### Null-Forgiving Operators Are Forbidden
Never use null-forgiving operators (`!`) or cast null to non-nullable types anywhere in Bannou code as they cause segmentation faults and hide null reference exceptions.

- **Prohibited**: `variable!`, `property!`, `method()!`, `null!`, `default!`, `(Type)null`
- **Required**: Always use explicit null checks with meaningful exceptions or proper test data
- **Example Correct**: `var value = variable ?? throw new ArgumentNullException(nameof(variable));`
- **Example Incorrect**: `var value = variable!;` or `var value = (Type)null;`
- **Test Rule**: Tests should use real data, not null casts. If testing null handling, use nullable types properly.
- **Principle**: Explicit null safety prevents segmentation faults and provides clear error messages

### Null-Coalescing to Empty String (`?? string.Empty`)

**General Rule**: Avoid `?? string.Empty` as it hides bugs by silently coercing null to empty string. Instead:
- Make the property nullable if empty is meaningless
- Throw an exception if null indicates a programming error or data corruption
- Validate and fail early at system boundaries

**Two Acceptable Patterns** (must include explanatory comment):

1. **Compiler Satisfaction**: When the coalesce can NEVER execute because the value is already validated non-null, but the compiler's nullable flow analysis can't track it:
   ```csharp
   // GetString() returns string? but cannot return null when ValueKind is String;
   // coalesce satisfies compiler's nullable analysis (will never execute)
   JsonValueKind.String => element.GetString() ?? string.Empty,

   // validDocuments only contains docs with non-null Content (filtered above)
   // The null-coalesce satisfies the compiler but will never execute
   Content = d.Content ?? string.Empty,
   ```

2. **External Service Defensive Coding**: When receiving data from third-party services (MinIO, Kamailio, etc.) where we have no control over the response. Must include:
   - Error log when unexpected null is encountered (not warning - this is a third-party failure, not user error)
   - Error event publication for monitoring
   - Comment explaining the defensive nature
   ```csharp
   // Defensive coding for external service: MinIO should always provide ETag,
   // but we handle null gracefully since this is third-party webhook data
   if (string.IsNullOrEmpty(etag))
   {
       _logger.LogError("MinIO webhook: Missing ETag for upload {UploadId}", uploadId);
       await _messageBus.TryPublishErrorAsync(...);
   }
   ETag = etag?.Trim('"') ?? string.Empty, // Defensive: external service may omit ETag
   ```

**Unacceptable Patterns**:
- Silent coercion without validation: `Name = request.Name ?? string.Empty`
- Hiding required field nullability: `StubName = subscription.StubName ?? string.Empty` (should validate and fail)
- Configuration defaults: `ConnectionString = config.DbConnection ?? string.Empty` (should throw on missing config)

### NEVER Export Environment Variables
Never use `export` commands to set environment variables on the local machine. This confuses containerization workflows and creates debugging issues. Use .env files and Docker Compose environment configuration instead.

### ⛔ NEVER Run Integration Tests Unless Explicitly Asked
**MANDATORY**: NEVER run `make test-http`, `make test-edge`, `make test-infrastructure`, or `make all` unless the user EXPLICITLY asks you to run tests.
- **Verification for code changes**: A successful `dotnet build` is sufficient verification for refactoring, bug fixes, and feature implementation
- **DO NOT** add "test" or "rebuild and test" to your todo lists unless the user specifically requested testing
- **DO NOT** run container-based tests to "verify" your changes work - the build verifies compilation
- **The user will ask for tests when they want tests** - do not assume testing is needed
- **Why this matters**: Integration tests take 5-10 minutes, rebuild containers, and are disruptive. Running them without being asked wastes significant time.

### ⚠️ MANDATORY REFERENCE - TESTING.md for ALL Testing Tasks
**CRITICAL**: For ANY task involving tests, testing architecture, or test placement, you MUST ALWAYS reference the testing documentation (`docs/operations/TESTING.md`) FIRST and IN FULL before proceeding with any work.

**MANDATORY TESTING WORKFLOW**:
1. Read `docs/operations/TESTING.md` completely to understand plugin isolation boundaries
2. Use the decision guide to determine correct test placement
3. Follow architectural constraints (unit-tests cannot reference plugins, lib-testing cannot reference other plugins, etc.)
4. ALWAYS respond with "I have referred to the service testing document" to confirm you read it

**⚠️ MANDATORY REFERENCE TRIGGERS** - You MUST reference `docs/operations/TESTING.md` for:
- ANY task involving writing, modifying, or debugging tests
- Questions about where to place tests (unit tests vs infrastructure tests vs integration tests)
- Testing configuration classes, service functionality, or cross-service communication
- Debugging test failures or compilation errors in test projects
- Understanding test isolation boundaries and plugin loading constraints
- ANY testing-related architectural decisions

**TESTING ARCHITECTURE RULES** (from TESTING.md):
- `unit-tests/`: Can ONLY reference `bannou-service`. CANNOT reference ANY `lib-*` plugins
- `lib-*.tests/`: Can ONLY reference their own `lib-*` plugin + `bannou-service`. CANNOT reference other `lib-*` plugins
- `lib-testing/`: Can ONLY reference `bannou-service`. CANNOT reference ANY other `lib-*` plugins
- `http-tester/`: Can reference all services via generated clients
- `edge-tester/`: Can test all services via WebSocket protocol

**VIOLATION PREVENTION**: Never attempt to reference AuthServiceConfiguration from lib-testing, never reference plugin types from unit-tests, never test business logic in lib-testing

### Always Check GitHub Actions for Testing Workflows
**MANDATORY**: Before attempting any integration testing work, ALWAYS check `.github/workflows/ci.integration.yml` first to understand the proper testing approach.
- The GitHub Actions workflow defines the authoritative 10-step testing pipeline
- Local testing should mirror the CI approach, not invent new approaches
- Infrastructure testing, HTTP testing, and WebSocket testing all have established patterns
