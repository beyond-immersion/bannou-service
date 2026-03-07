# Agent-Authored Rule Contamination: Audit & Cleanup

> **Created**: 2026-03-07
> **Status**: ACTIVE INCIDENT
> **Severity**: Critical -- codebase corruption in progress across dozens of plugins

---

## What Happened

Agent-authored rules in authoritative documents (SCHEMA-RULES.md, TENETS.md, and related files) contain incorrect information written by AI agents that did not fully trace their conclusions through the actual codebase. These rules are then enforced by other agents as inviolable law, causing systematic damage across the codebase as plugins are "fixed" to comply with rules that were wrong from the start.

The protective guardrails that prevent agents from modifying these documents -- guardrails that exist for good reason -- also prevent agents from correcting the errors. The result is that bad rules are permanently enshrined and actively enforced.

---

## The Confirmed Incident: `allOf` with `BaseServiceEvent`

### The Bad Rule

SCHEMA-RULES.md (formerly line 949, now moved) stated:

> Custom service events use a **flat structure** -- they define `eventId` and `timestamp` inline. They do NOT use `allOf` with `BaseServiceEvent`.

### Why It's Wrong

The rule was written without tracing through the NSwag code generation pipeline. Here is what actually happens:

**With `allOf: [{$ref: BaseServiceEvent}]` (the CORRECT pattern):**
1. NSwag generates `class FooEvent : BaseServiceEvent` (C# inheritance)
2. The class inherits `EventId`, `Timestamp`, and `EventName` from the base
3. The class implements `IBannouEvent` (via `BaseServiceEvent`)
4. Message taps (`IMessageTap`, `InMemoryMessageTap`) can handle the event through the `IBannouEvent` interface
5. `EventName` provides event type identification for generic event processing pipelines
6. The `override` keyword on `EventName` allows each event to declare its routing name as a default value

**With flat structure, no `allOf` (the INCORRECT pattern the rule mandates):**
1. NSwag generates `class FooEvent` -- a standalone class with no inheritance
2. `EventId` and `Timestamp` are regular properties, duplicated on every event class
3. The class does NOT implement `IBannouEvent`
4. Message taps cannot handle the event
5. No `EventName` property exists at all
6. The event is invisible to generic event processing infrastructure

This was proven by examining the generated C# output on the `don/shoring-up` branch, where puppetmaster-events.yaml was changed from `allOf` to flat and regenerated. The generated C# classes lost their `: BaseServiceEvent` inheritance, gained duplicate inline properties, and lost `EventName` entirely.

### The Damage

Agents performing plugin audits flag every service event schema that uses `allOf` with `BaseServiceEvent` as a violation. The "fix" -- removing `allOf` and inlining properties -- breaks the C# type hierarchy. Services that have been "fixed" now have event classes that:

- Cannot participate in the `IBannouEvent` infrastructure
- Have duplicated property definitions instead of inherited ones
- Lack `EventName` for event type identification
- Break message tap forwarding

This affects dozens of plugins. Every plugin that was audited and "corrected" to use flat event schemas now has broken event type hierarchies.

### The Compounding Factor: `eventName` Confusion

The same SCHEMA-RULES.md section also states:

> Do NOT use `eventName` (that is for client events only)

This conflates two different things:
- **Client events** use `eventName` for Connect service whitelist routing (checked against `ClientEventWhitelist`)
- **Service events** use `eventName` via `IBannouEvent.EventName` for generic event processing, message tap forwarding, and event type identification in diagnostic/observability pipelines

The `eventName` property on service events serves a different purpose than on client events, but the rule treats them as the same concept and bans it from service events entirely.

---

## How This Happened: The Contamination Pattern

```
Step 1: Agent writes a rule based on surface-level pattern observation
        ("flat looks cleaner than allOf, let's standardize on flat")

Step 2: Agent does NOT trace the rule through:
        - Code generation (NSwag excludedTypeNames + allOf = inheritance)
        - Runtime infrastructure (IBannouEvent, IMessageTap)
        - The actual C# output (what NSwag produces from each pattern)

Step 3: Rule is committed to an authoritative document

Step 4: CLAUDE.md mandates reading the document before any schema work:
        "Before creating or modifying ANY schema file, you MUST read SCHEMA-RULES.md"

Step 5: Guardrails prevent modification:
        "These scripts/rules are NEVER to be modified by an agent"

Step 6: Future agents enforce the bad rule during audits, flagging correct
        code as violations and "fixing" it into broken code

Step 7: Each "fix" is a small, plausible-looking change that passes code
        review because the rule says it's correct

Step 8: Damage accumulates across dozens of plugins over days/weeks
```

The critical failure is at Step 2. An agent that understood the full pipeline from YAML schema through NSwag generation to C# runtime would never have written this rule. But agents routinely make confident-sounding pronouncements about systems they haven't fully investigated.

---

## The Systemic Risk

The `BaseServiceEvent` incident is confirmed, but it is almost certainly not the only one. Any rule in any authoritative document that was authored by an agent without complete end-to-end verification could contain the same class of error. The documents at risk include:

| Document | Risk | Why |
|----------|------|-----|
| **SCHEMA-RULES.md** | **HIGH** | Directly governs code generation. Wrong rules produce wrong code across all services. Contains detailed structural prescriptions that agents wrote. |
| **TENETS.md** | **HIGH** | Governs all implementation decisions. Wrong tenets cause systematic violations of correct patterns. |
| **tenets/FOUNDATION.md** | **HIGH** | Architecture-level rules. Errors here cascade through every service. |
| **tenets/IMPLEMENTATION-BEHAVIOR.md** | **MEDIUM** | Behavioral rules. Errors cause wrong service logic patterns. |
| **tenets/IMPLEMENTATION-DATA.md** | **MEDIUM** | Data modeling rules. Errors cause wrong model patterns. |
| **tenets/QUALITY.md** | **MEDIUM** | Quality standards. Errors cause false audit findings. |
| **SERVICE-HIERARCHY.md** | **LOW** | More stable; based on architectural decisions rather than implementation details. |

### What Makes a Rule Dangerous

A rule is a potential time bomb if:

1. **It prescribes HOW code should be structured** (not just WHAT it should achieve) -- structural prescriptions require understanding the full pipeline
2. **It was written without citing evidence from the actual codebase** -- "this is cleaner" vs "NSwag produces X when you do Y"
3. **It contradicts what the codebase actually does successfully** -- if 40 services use pattern A and the rule says use pattern B, the rule might be wrong
4. **It makes claims about tool behavior** (NSwag, RabbitMQ, System.Text.Json) -- agents frequently get tool behavior wrong
5. **It distinguishes between two patterns without explaining the runtime consequences** -- "use X not Y" without "because Y causes Z at runtime"

---

## Required Actions

All corrective action is to be performed by the developer. **Agents must not take corrective action based on this document.** This document is diagnostic only -- it explains what went wrong so the developer can decide what to do about it.

### SCHEMA-RULES.md Correction Needed

The "Custom Service Event Structure" section (line 947-987) contains incorrect guidance. The developer needs to decide how to rewrite it. The correct behavior is:

- `allOf` with `BaseServiceEvent` is required for all custom service events (produces C# inheritance, `IBannouEvent` implementation, `EventName` support)
- `eventName` on service events is valid and serves a different purpose than on client events
- The distinction between service event `eventName` and client event `eventName` needs to be clearly documented

### Deep Audit of All Authoritative Documents

Every rule in SCHEMA-RULES.md and the tenet files must be verified against the actual codebase and runtime behavior. This is not a surface-level read -- each prescriptive rule must be traced through:

1. **Schema to generation**: What does the code generation pipeline produce from each pattern?
2. **Generation to compilation**: Does the generated code compile? Does it integrate with the type system correctly?
3. **Compilation to runtime**: Does the code behave correctly at runtime? Does it integrate with infrastructure (messaging, state, mesh)?
4. **Cross-reference consistency**: Do rules in different documents contradict each other?

### Prevent Recurrence

The root cause is agents writing rules without end-to-end verification. Possible mitigations:

1. **Provenance tracking**: Every rule in an authoritative document must cite the evidence that supports it (specific files, specific code paths, specific tool behaviors verified by running them)
2. **Rule review process**: New rules or rule changes require the proposing agent to demonstrate the full pipeline impact, not just state a preference
3. **Contradiction detection**: Periodic cross-reference audits between documents to find rules that conflict with each other or with the codebase
4. **Agent authorship markers**: Mark which rules were written by agents vs humans, so auditors know which rules need deeper scrutiny

---

## Incident Log

| Date | Document | Rule | Impact | Status |
|------|----------|------|--------|--------|
| 2026-03-07 | SCHEMA-RULES.md | "Custom events use flat structure, no allOf" | Dozens of plugins had event schemas changed, breaking IBannouEvent inheritance | **CONFIRMED** |
| 2026-03-07 | SCHEMA-RULES.md | "Do NOT use eventName (client events only)" | Service events lost EventName, breaking generic event processing | **CONFIRMED** |
| 2026-03-07 | SCHEMA-RULES.md | "x-manual-implementation is a legacy alias for x-controller-only" | Agent changed documentation-api.yaml from x-manual-implementation to x-controller-only without tracing through the Liquid template. The two flags produce fundamentally different generated code: x-controller-only creates `abstract class {Service}ControllerBase` with abstract methods requiring `override`; x-manual-implementation keeps `partial class {Service}Controller` and generates no method at all. Caught before regeneration. | **CAUGHT** |
| 2026-03-07 | FOUNDATION.md | "DeletedEvent (ID + deletedReason)" | T5 lifecycle event description said DeletedEvent contains only ID + deletedReason. Generator actually produces full entity data + deletedReason (matching SCHEMA-RULES.md). Consumers reading FOUNDATION.md would wrongly expect minimal data. | **FIXED** |
| 2026-03-07 | FOUNDATION.md | "Infrastructure Lib Backend Access" table | Table listed lib-orchestrator (L3), lib-voice (L3), lib-asset (L3) as "Infrastructure Libs" alongside L0 libs. Omitted lib-mesh (actual L0). Conflated two architectural categories. | **FIXED** |
| 2026-03-07 | FOUNDATION.md | T13 missing omitted x-permissions case | Table documented `[]` but not the omitted case. Generator treats both identically (confirmed in generate-permissions.sh). | **FIXED** |
| 2026-03-07 | SCHEMA-RULES.md | L1 example list missing Chat | x-service-layer example table listed L1 as "account, auth, connect, permission, contract, resource" — omitted Chat, which is L1 per SERVICE-HIERARCHY.md. | **FIXED** |
| 2026-03-07 | SCHEMA-RULES.md | Pipeline table missing common client events | Generation pipeline table had no entry for common-client-events.yaml generation to CommonClientEventsModels.cs. | **FIXED** |
| 2026-03-07 | SCHEMA-RULES.md | Lifecycle events undocumented eventName/allOf | x-lifecycle section didn't mention that generated events include eventName with default value and use allOf with BaseServiceEvent. Could lead to agents thinking lifecycle events lack IBannouEvent integration. | **FIXED** |
| 2026-03-07 | SCHEMA-RULES.md | x-resource-mapping missing from validation checklist | Validation checklist covered x-references, x-compression-callback, x-event-template but not x-resource-mapping. | **FIXED** |
| 2026-03-07 | TENETS.md | T2 hierarchy layers stale (5-layer fossil) | All layer names, descriptions, and example services wrong vs SERVICE-HIERARCHY.md 6-layer model. Actor listed as L4 (actually L2), account/auth listed as L2 (actually L1), telemetry/orchestrator/analytics listed as L1 (actually L0/L3/L4). L5 missing entirely. | **FIXED** |
| 2026-03-07 | IMPLEMENTATION-DATA.md | T14 "subscribe to entity.deleted for cascade" | Directly contradicts T28 which forbids deletion event subscriptions for cleanup. T14 advice predated lib-resource pattern. | **FIXED** |
| 2026-03-07 | FOUNDATION.md | T27/T28 ISeededResourceProvider misidentification | T27 said "compression data", T28 showed fabricated CleanupReferencesAsync method. Actual interface (bannou-service/Providers/ISeededResourceProvider.cs) loads embedded/static resources via ListSeededAsync/GetSeededAsync. Cleanup actually uses x-references HTTP endpoint callbacks via RegisterResourceCleanupCallbacksAsync. | **FIXED** |
| 2026-03-07 | FOUNDATION.md | T5 event schema pattern still shows flat structure | allOf fix applied to SCHEMA-RULES.md but not to FOUNDATION.md T5 example. Example showed inline eventId/timestamp without allOf/BaseServiceEvent/eventName. | **FIXED** |
| 2026-03-07 | FOUNDATION.md | T5 topic naming incomplete | Only described Pattern A, showed Pattern C example (character.realm.joined) without identifying it, didn't mention Pattern B is forbidden. | **FIXED** |
| 2026-03-07 | IMPLEMENTATION-BEHAVIOR.md | "formerly T26" confusing | Note referenced old tenet number that is now reused for "No Sentinel Values". Removed stale number. | **FIXED** |
| | | | | |

*Add new incidents to this table as they are discovered during the deep audit.*

---

## Why This Is a Catastrophe

This is not a single bug. It is a systemic failure mode where the AI development workflow is actively degrading the codebase it was built to improve. Each audit pass that enforces a bad rule makes things worse, not better. The damage is:

- **Invisible**: Each individual change looks like a reasonable schema cleanup
- **Compounding**: Every audited plugin accumulates the same damage
- **Self-reinforcing**: Agents see the "fixed" plugins as precedent for "fixing" more
- **Protected**: The guardrails that prevent rule modification also prevent correction

The combination of confident rule authorship, authoritative document status, mandatory compliance, and modification guardrails creates a system where a single bad rule can corrupt the entire codebase one plugin at a time, and no agent in the loop has the authority or inclination to stop it.

---

*This document is diagnostic only. It exists to track agent-authored rule contamination and inform the developer's corrective decisions. Agents must not take corrective action based on this document.*
