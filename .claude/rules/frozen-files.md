---
globs: scripts/**, structural-tests/**, test-utilities/**, docs/reference/**, .claude/**
---

# Frozen Artifacts — DO NOT MODIFY

You are reading or working near a **frozen artifact**. These files are NEVER to be modified by an agent without EXPLICIT, in-conversation user instruction.

**"Explicit" means**: The user, in THIS conversation, directly told you to modify this specific file. A compacted summary, a task description, or your own judgment do NOT qualify.

## What Is Frozen

| Directory | What It Contains | Presumption |
|-----------|-----------------|-------------|
| `scripts/` | Code generation pipeline: shell scripts, Python scripts, NSwag templates | Scripts are correct. If generation output looks wrong, fix the **schema**, not the script |
| `docs/reference/`, `docs/reference/tenets/` | Tenets, rules (SCHEMA-RULES.md, ENDPOINT-PERMISSION-GUIDELINES.md), architecture (SERVICE-HIERARCHY.md), vision | Documents are correct. If code contradicts a document, the **code** is presumed wrong |
| `structural-tests/`, `test-utilities/` | Structural validators, test infrastructure | Tests are correct. If a structural test fails, fix the **code or schema**, not the test |
| `.claude/` | Enforcement hooks, skill definitions, permission config | Claude Code configuration is human-controlled |

## What You MUST NOT Do

- **NEVER modify** any file in these directories without explicit user instruction
- **NEVER add exceptions, allowlists, or carve-outs** to structural tests — those are the user's decision
- **NEVER "fix" what you think is wrong** in these files — present your concern and wait for approval
- **NEVER add `<NoWarn>`, `#pragma warning disable`, or `[SuppressMessage]`** — fix the code instead
- **If you must propose a change**: show the EXACT diff, explain what it affects, and wait for explicit approval

## Why These Are Frozen — Incident History

These rules exist because Claude caused real damage modifying frozen artifacts:

1. **Scripts**: An agent changed namespace strings across 4 generation scripts, silently breaking all 76+ services. The agent believed `.Common` was the correct namespace when it was actually `.BannouService` — cascading into 22 compile errors and hours of debugging.

2. **Reference docs**: An agent wrote an incorrect rule into SCHEMA-RULES.md. Because it was in an authoritative document, other agents enforced it as law, systematically "fixing" dozens of event schemas to comply with the bad rule. A bad rule change is invisible — it becomes indistinguishable from intentional rules and gets enforced forever.

3. **Structural tests**: Structural tests validate patterns across ALL 76 services (~979 test cases). A single heuristic change affects every service simultaneously. Claude has added violation whitelists to every structural test it has ever written — PendingExceptions, AllowedViolations, KnownIssues, Skip attributes — defeating the entire purpose of the test.

4. **Warning suppression**: Claude encountered ~7,000 xunit v3 analyzer errors and immediately added `xUnit1051` to `<NoWarn>` in `Directory.Build.props` — a frozen infrastructure file — silencing every single occurrence permanently.

## Schemas Are NOT Frozen

`schemas/*.yaml` is the primary artifact developers edit. When a structural test fails, the fix chain is: fix schema → regenerate → generated artifact appears → code uses it → test passes. Never hand-write what generation should produce. Never work around a missing generated artifact.
