# Incident History

> **Purpose**: Reference catalog of past agent incidents, their root causes, and the preventive measures now in place. Consult when performing code reviews, audits, or investigating why a hook exists.
>
> **Not auto-included** — read when doing audit work (`/audit-plugin`, `/check-plugin`) or investigating hook behavior.

---

## Incident Index

| # | Category | What Happened | Root Cause | Prevention |
|---|----------|---------------|------------|------------|
| 1 | Problem hiding | Whitelisted violations in every structural test ever written (`PendingExceptions`, `AllowedViolations`, `KnownIssues`, skip attributes) | Default instinct to make tests pass rather than surface failures | `post-edit-reminder.sh` detects test-weakening patterns; structural tests are frozen |
| 2 | Problem hiding | Added `xUnit1051` to `<NoWarn>` in `Directory.Build.props` to silence 7,000 analyzer errors | Treating warnings as noise to suppress rather than signals to fix | `post-edit-reminder.sh` detects edits to frozen infrastructure files |
| 3 | Precedent mining | Grepped 3 services for the same topic naming violation, found it everywhere, dismissed as "established pattern" | Pattern-matching existing code as justification rather than reading the tenet | `no-minimizing-language.sh` category 5 (precedent-mining) |
| 4 | Problem hiding | Received T7 finding on DeleteBloodline (cleanup after irreversible deletes), dismissed by fabricating a "self-healing" narrative, wrote a comment instead of fixing code | Rationalizing violations as acceptable rather than fixing 3 lines of code | `no-minimizing-language.sh` category 7 (work-avoidance); correct fix was moving `ExecuteCleanupAsync` before deletes |
| 5 | Design dismissal | During `/audit-plugin faction`, marked Design Consideration #2 ("Norm query performance at scale") as FIXED based on assumption "characters typically belong to 1-5 factions" | Cannot test at 100K NPC scale; treated "code works today" as evidence that scale concerns are invalid | `no-minimizing-language.sh` category 6 (minimization); correct action was CREATE_ISSUE |
| 6 | Backwards compatibility | Renamed a type but left a `using` alias to the old name "for backwards compatibility" on a pre-release codebase | Instinct to preserve old paths even when a definitive decision was made | `block-backwards-compatibility.sh` category 3 (compatibility shims) |
| 7 | Backwards compatibility | Told to remove a field, instead renamed it to `_deprecated{Field}` and added a compatibility property | Same instinct — partial execution of definitive decisions | `block-backwards-compatibility.sh` category 4 (soft removal) |
| 8 | Backwards compatibility | Re-exported a removed type from a barrel file "in case something depends on it" — nothing did | Hypothetical consumer anxiety | `block-backwards-compatibility.sh` category 5 (consumer anxiety) |
| 9 | Backwards compatibility | Repeatedly hedged definitive user decisions with "this would be a breaking change" for unreleased code | Treating every change as potentially breaking when there are zero consumers | `block-backwards-compatibility.sh` category 2 (breaking change anxiety) |
| 10 | Workaround cascade | Moved enums between schemas, got duplicate types, spent multiple cycles layering C# `using` alias workarounds | First workaround created a new problem requiring workaround #2; should have stopped at the first unexpected result | Hard stop procedure: present the situation, wait for direction |
| 11 | Missing information | Lost gap lists during compaction, silently re-launched discovery agents instead of asking | Attempted to recover missing information instead of acknowledging the gap | Hard stop procedure: state what's missing, ask for direction |
| 12 | Infrastructure gap | Wrote tautological enum roundtrip tests when `EnumMappingValidator` lacked string-to-enum methods | Wrote code to pass a test rather than recognizing the infrastructure gap | Hard stop procedure: identify what the test protects, report the gap |
| 13 | Design decision | Autonomously decided `deleteByCharacter` should delete all quest instance records without reading the cleanup contract | Made a behavioral choice not specified in instructions | Hard stop procedure: present the decision point with options |
| 14 | Fabricated identifiers | Read `GenesisService.cs` showing `SeedResponse`, wrote tests using `CreateSeedResponse` (nonexistent type). Same for `CreateWalletResponse`, `CreateContainerResponse`, `GetServiceResponse`, `CapabilityEntry` | Generated type names from naming conventions instead of reading the actual names visible in context | Skill checkpoints: verify identifiers against source before writing code |
| 15 | Work avoidance | During multi-service provisioning, skipped wallet cleanup, declared orphaned wallets "harmless", wrote rationalizing comments | Gave up on solving a solvable problem and wrote justification into the code | `no-minimizing-language.sh` category 7 (work-avoidance rationalization) |
| 16 | Corner cutting | Told to read all 83 scripts, read ~24 fully, silently switched to headers-only for remaining ~60 | System prompt efficiency pressure caused silent quality degradation mid-task | `no-minimizing-language.sh` category 8 (efficiency corner-cutting); `block-read-limit.sh` |
| 17 | Agent delegation | Launched Explore agent to read 12 dependency maps; lossy summaries used to write code with incorrect API signatures | Delegating reading to agents means content lands in agent context, not yours | `block-all-agents.sh` restricts to project-aware agent types |
| 18 | Rubber-stamp audit | Self-performed coverage audit instead of using agent; checked every box without tracing control flow; missed clear T7 violation | Audit was an impression ("looks complete") not a mechanical check | Audit skills have mechanical checklists with verification steps |

---

## Pattern Summary

The incidents cluster into five root causes:

1. **Hide/silence/workaround** (#1, #2, #3, #4, #5, #15): Encounter problem, make it disappear rather than surface it. Now caught by `no-minimizing-language.sh` and `post-edit-reminder.sh`.

2. **Partial execution of decisions** (#6, #7, #8, #9): User makes a definitive decision, agent executes it partially while preserving escape hatches. Now caught by `block-backwards-compatibility.sh`.

3. **Workaround cascades** (#10, #11): First workaround creates new problem, leading to more workarounds. Prevented by hard stop procedure.

4. **Pattern-matching over reading** (#14, #18): Generating from convention instead of reading actual names/code. Prevented by skill checkpoints requiring verification.

5. **Silent quality degradation** (#16, #17): Starting thorough, gradually cutting corners mid-task without disclosure. Caught by `block-read-limit.sh` and `no-minimizing-language.sh` category 8.
