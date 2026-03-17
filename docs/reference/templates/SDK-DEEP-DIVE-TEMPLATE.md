# SDK Deep Dive Template

> ⛔ **FROZEN DOCUMENT** — Defines an authoritative template. AI agents MUST NOT modify any content without explicit user instruction. See CLAUDE.md § "Reference Documents Are Frozen."

> This document defines the structure for per-SDK deep-dive documents.
> Each SDK gets one document: `docs/sdks/{sdk-name}.md`

---

## What Is an SDK Deep Dive?

An SDK deep dive is the architectural and design document for a Bannou pure-computation SDK. It describes **what the SDK computes**, **why it exists**, **who consumes it**, and **what's non-obvious about it** — the same role as a plugin deep dive, adapted for libraries that have no HTTP endpoints, no state stores, no messaging, and no service registration.

SDK deep dives complement SDK implementation maps. The **deep dive** covers WHAT the SDK is, WHY it's designed that way, WHO uses it, and WHAT's weird about it. The **implementation map** covers HOW it works, method by method with pseudo-code.

---

## How SDKs Differ From Plugins

| Concern | Plugin | SDK |
|---------|--------|-----|
| Endpoints | HTTP POST routes | Public methods on classes |
| State | Redis/MySQL via lib-state | In-memory data structures |
| Events | IMessageBus publish/subscribe | None (callers handle side effects) |
| Configuration | Generated config class, env vars | Constructor parameters, options objects |
| DI Registration | `[BannouService]` attribute | Manual DI registration or direct construction |
| Deployment | Loaded as plugin assembly | Referenced as project/NuGet dependency |
| Determinism | Not required | Often required (same input + seed = same output) |
| Dependencies | Other plugins via generated clients | Other SDKs via project references |

---

## Process Instructions

### For Implemented SDKs

1. **Read all source code thoroughly**: Every public type, method, algorithm. Understand the computation pipeline.
2. **Build the document structure**: Fill in sections 1-10 based on verifiable source code.
3. **Compile quirks list**: Non-obvious behaviors, performance cliffs, precision issues, API footguns.

### For Aspirational SDKs (Pre-Implementation)

1. **Read the design document**: Understand the intended computation, data model, and consumers.
2. **Build the document structure**: Fill in sections 1-10 based on design intent.
3. **Mark as aspirational**: Use the aspirational header format.

---

## Document Structure

### 1. Header

Two header formats depending on implementation status.

#### Implemented SDK Header

```markdown
# {SDK Name} SDK Deep Dive

> **SDK**: {sdk-name}
> **Location**: `sdks/{sdk-name}/`
> **Layer**: {Theory / Storyteller / Composer / Bridge / Infrastructure}
> **Domain**: {Music / Storyline / Cinematic / Voxel / Scene / Behavior / Connectivity / Asset}
> **Dependencies**: {other SDKs, or "None (pure computation)"}
> **Short**: {one-line description, ~120 chars max}
```

Optional additional fields (add after Dependencies, in this order):
- `> **Implementation Map**: [docs/sdks/maps/{SDK-NAME}.md](maps/{SDK-NAME}.md)` — link to the implementation map if one exists
- `> **Consumers**: {comma-separated list of plugins and SDKs that depend on this}` — high-level consumer list
- `> **Academic Foundations**: {key references}` — if grounded in formal theory

#### Aspirational SDK Header

```markdown
# {SDK Name} SDK Deep Dive

> **SDK**: {sdk-name} (not yet created)
> **Location**: `sdks/{sdk-name}/` (planned)
> **Layer**: {Theory / Storyteller / Composer / Bridge / Infrastructure}
> **Domain**: {domain}
> **Dependencies**: {other SDKs, or "None (pure computation)"}
> **Status**: Aspirational — no code exists.
> **Short**: {one-line description, ~120 chars max}
```

**Layer values** (maps to the three-layer creative domain pattern):
- **Theory**: Pure computation primitives (pitches, grids, narrative grammar). Zero dependencies on other creative SDKs.
- **Storyteller**: GOAP-driven or parametric procedural generation. Depends on the domain's theory SDK.
- **Composer**: Interactive authoring with undo/redo, validation, serialization, engine bridge abstraction. Depends on theory SDK.
- **Bridge**: Engine-specific integration (Godot, Stride, Unity). Depends on the domain's composer SDK.
- **Infrastructure**: Connectivity, serialization, asset pipeline. Not part of the creative three-layer pattern.

---

### 2. Overview

A concise description of what the SDK computes, its role in the Bannou ecosystem, and why it exists as a separate library rather than being inline in a plugin. This should answer: "What does this SDK do and why is it a standalone library?" in 2-4 sentences.

---

### 3. Consumers

A table of what depends on this SDK — other SDKs, plugins, engine bridges, and tools.

```markdown
| Consumer | Type | Usage |
|----------|------|-------|
| lib-{plugin} | Plugin | Calls {method} for {purpose} |
| {other-sdk} | SDK | Uses {types} for {purpose} |
| {sdk}-{engine} | Bridge | Renders {data} via engine-specific implementation |
| Content pipeline | Tool | Imports/exports via {methods} |
```

---

### 4. Public API Surface

The main types, interfaces, and their roles. Not every type — the important ones that define how consumers interact with the SDK.

```markdown
| Type | Kind | Purpose |
|------|------|---------|
| `{ClassName}` | Class | {What it does} |
| `I{Interface}` | Interface | {Contract it defines} |
| `{StructName}` | Struct | {What data it holds} |
| `{EnumName}` | Enum | {What it discriminates} |
```

Group by functional area if the SDK has distinct subsystems.

---

### 5. Data Model

Core data structures that define the SDK's internal representation. Include field-level detail for the primary types. Use code blocks for type definitions where the structure matters.

---

### 6. Computation Pipeline

How data flows through the SDK. This is the SDK equivalent of a plugin's "how requests flow through the service." Show the transformation chain from input to output.

```markdown
Input → {Step 1} → {Step 2} → ... → Output
```

For SDKs with multiple computation paths (e.g., meshing has three strategies), show each path.

---

### 7. Determinism Contract

If the SDK has a determinism requirement (same input + seed = identical output), document it explicitly:

- What inputs affect the output
- Whether a seed is required/optional
- What "identical output" means (bitwise? structural?)
- Any exceptions (floating-point platform differences, etc.)

If the SDK has no determinism requirement, state: "No determinism contract — output may vary between invocations."

---

### 8. Performance Targets

Operation-level performance budgets. Include both client-side (frame budget constrained) and server-side (throughput constrained) targets where applicable.

```markdown
| Operation | Target | Context | Notes |
|-----------|--------|---------|-------|
| {operation} | < {time} | Client | {constraints} |
| {operation} | < {time} | Server | {constraints} |
```

---

### 9. Format Support (Optional)

For SDKs that import/export external formats. Document each format with its structure, import/export mapping, and licensing.

```markdown
### {Format Name} (.{ext})

**License**: {license of the format/tool}
**Direction**: Import / Export / Both

{Format structure description}

**Import mapping**: {how format elements map to SDK types}
**Export mapping**: {how SDK types map to format elements}
```

If the SDK defines a custom binary format, document the byte layout here.

Omit this section if the SDK has no format I/O.

---

### SDK-Specific Sections (Optional)

SDKs may include additional `##` sections for domain-specific content. Same rules as plugin deep dives:

**Position A: Between Computation Pipeline (6) and Determinism Contract (7)** — for foundational context.
**Position B: Between Performance Targets (8) and Known Quirks (10)** — for supplementary content.

Common SDK-specific sections:
- **Academic Foundations**: Theory papers and how they map to SDK types (for theory-layer SDKs)
- **Engine Bridge Pattern**: How the bridge abstraction works (for composer-layer SDKs)
- **Generation Strategies**: Algorithm descriptions (for storyteller-layer SDKs)
- **Wire Format**: Binary protocol details (for infrastructure SDKs)

---

### 10. Known Quirks & Caveats

Same three categories as plugin deep dives:

#### Bugs (Fix Immediately)
Clear defects with unambiguous fixes.

#### Intentional Quirks (Documented Behavior)
Non-obvious behaviors that are deliberate. Permanent documentation.

#### Design Considerations (Requires Planning)
Issues where the fix involves trade-offs that need discussion.

---

### 11. Open Questions

Unresolved design decisions. Each entry should describe the question, the options, and the consequences of each option.

---

### 12. Work Tracking

Same marker format as plugin deep dives:

```markdown
- Some issue
  <!-- AUDIT:IN_PROGRESS:2026-01-29 -->
```

---

## Rules

1. **One deep dive per SDK** — placed at `docs/sdks/{sdk-name}.md`
2. **No speculative information** — every statement must be verified against source code (for implemented SDKs)
3. **Keep it scannable** — tables over prose where possible
4. **Public API must be complete** — every public type in the SDK should appear in the Public API Surface section
5. **Preserve AUDIT markers** — same rule as plugin deep dives
6. **Cross-reference implementation map** — if a map exists, the header must link to it

---

## Relationship to Other Documents

| Document | Purpose | Source of Truth For |
|----------|---------|---------------------|
| **SDK Deep Dive** | Architecture & context | Why it exists, who uses it, what's non-obvious |
| **SDK Implementation Map** | Behavioral specification | How each method/algorithm works, pseudo-code |
| **SDK Overview Guide** | Ecosystem catalog | How SDKs relate to each other and to plugins |
| **Planning Document** | Design rationale | Architectural decisions, phase plans, research |
