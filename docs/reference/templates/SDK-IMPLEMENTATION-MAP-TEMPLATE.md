# SDK Implementation Map Template

> ⛔ **FROZEN DOCUMENT** — Defines an authoritative template. AI agents MUST NOT modify any content without explicit user instruction. See CLAUDE.md § "Reference Documents Are Frozen."

> This document defines the structure for per-SDK implementation maps.
> Each SDK gets one map: `docs/sdks/maps/{sdk-name}.md`

---

## What Is an SDK Implementation Map?

An SDK implementation map is a pseudo-code-level behavioral specification for a Bannou pure-computation SDK. It describes **what each public method does** — what data it reads and transforms, what algorithms it applies, what it validates, and what it returns — using a structured notation adapted for pure computation (no state stores, no messaging, no HTTP).

SDK implementation maps serve the same three purposes as plugin maps:

1. **Single-file reference** for implemented SDKs — eliminates the need to read multiple source files
2. **Design specification** for pre-implementation SDKs — the pseudo-code IS the implementation plan
3. **Agent orientation** — gives AI agents complete behavioral context without code exploration

SDK maps complement SDK deep dives. The **deep dive** covers WHAT, WHY, WHO. The **map** covers HOW, method by method.

---

## Process Instructions

### For Implemented SDKs

1. **Read all source code thoroughly**: Every public method, algorithm, data structure. Every branch, every validation.
2. **Build the document structure**: Fill in sections 1-8 based on verifiable source code.
3. **Verify completeness**: Every public method must appear in the API Index and Methods sections.

### For Aspirational SDKs (Pre-Implementation)

1. **Read the deep dive and design document**: Understand the intended computation and data model.
2. **Build the document structure**: Fill in sections 1-8 based on design intent.
3. **Write pseudo-code as specification**: The Methods section describes what the implementation SHOULD do. This IS the implementation plan.

---

## Document Structure

### 1. Header

```markdown
# {SDK Name} SDK Implementation Map

> **SDK**: {sdk-name}
> **Location**: `sdks/{sdk-name}/`
> **Layer**: {Theory / Storyteller / Composer / Bridge / Infrastructure}
> **Domain**: {domain}
> **Deep Dive**: [docs/sdks/{SDK-NAME}.md](../{SDK-NAME}.md)
```

For aspirational SDKs, add:
```markdown
> **Status**: Aspirational — pseudo-code represents intended behavior, not verified implementation
```

---

### 2. Summary Table

```markdown
| Field | Value |
|-------|-------|
| SDK | {sdk-name} |
| Layer | {Theory/Storyteller/Composer/Bridge} |
| Public Types | {count} ({N} classes, {M} structs, {K} interfaces, {J} enums) |
| Public Methods | {count} |
| Dependencies | {other SDKs or "None"} |
| Deterministic | {Yes (seeded) / Yes (pure) / No} |
| Allocation-Free Hot Paths | {list or "None"} |
```

---

### 3. Data Structures

Document every primary data structure — the SDK's equivalent of state stores. These are the types that hold the SDK's internal representation.

```markdown
### {TypeName}

**Kind**: Class / Struct / Record
**Thread Safety**: {Concurrent-read / Single-writer / Immutable / None}

| Field | Type | Purpose |
|-------|------|---------|
| `{Name}` | `{Type}` | {What it holds} |
```

For struct types where memory layout matters (e.g., `Voxel` at 2 bytes), document the byte layout.

---

### 4. Dependencies

What this SDK relies on at compile time and runtime.

```markdown
| Dependency | Type | Usage |
|------------|------|-------|
| {sdk-name} | SDK (project ref) | {What types/methods it uses} |
| {package} | NuGet | {What capability it provides} |
| System.{lib} | BCL | {What it uses from the base class library} |
```

If the SDK has zero dependencies beyond BCL, state: "No external dependencies. Pure BCL computation."

---

### 5. API Index

One row per public method, providing a scannable overview. Group by class/interface.

```markdown
#### {ClassName}

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| {Name} | `({params}) → {return}` | Yes/No | {Free/Minimal/Allocating} | {Brief} |
```

**Deterministic** column: Whether `same input → same output` is guaranteed.

**Allocation** column: Performance hint for hot-path consumers.
- `Free` — No heap allocations on the hot path
- `Minimal` — Only unavoidable allocations (return value, etc.)
- `Allocating` — Creates collections, strings, or other heap objects

---

### 6. Methods

The core of the implementation map. Every public method gets a structured pseudo-code block.

#### Method Block Format

```markdown
### ClassName.MethodName
`({param}: {Type}, ...) → {ReturnType}`

VALIDATE {precondition}                    → throw if violated
COMPUTE {intermediate} FROM {input}
ITERATE {collection}
  TRANSFORM {element} USING {algorithm}
IF {condition}
  {branch logic}
RETURN {result}
```

#### Formatting Rules

- **One block per public method** — no grouping or abbreviating "simple" methods
- **Method header**: `### ClassName.MethodName` followed by signature line
- **Pseudo-code body**: Uses the notation vocabulary defined below
- **Indentation**: 2 spaces for scope (loop body, conditional branches)
- **Comments**: `//` prefix for clarifying notes
- **Omit**: Logging, telemetry, CancellationToken — these are implied

#### Ordering

Methods should appear grouped by class, in the same order as the API Index.

---

### 7. Algorithms

For SDKs with non-trivial algorithms, dedicate a section to each one with:
- **Name and purpose**
- **Complexity**: Time and space
- **Input/Output types**
- **Step-by-step pseudo-code** (more detailed than the method block, showing the actual algorithm)
- **References**: Papers, articles, or well-known algorithm names

```markdown
### {AlgorithmName}

**Purpose**: {What it computes}
**Complexity**: O({time}) time, O({space}) space
**Reference**: {Paper/article/algorithm name}

INPUT: {description}
OUTPUT: {description}

{Detailed algorithmic pseudo-code}
```

If the SDK has no non-trivial algorithms (pure data model + validation), state: "No non-trivial algorithms. This SDK is primarily a data model with validation."

---

### 8. Serialization Formats

For SDKs that define binary or structured formats, document the byte-level layout.

```markdown
### {FormatName} (.{ext})

**Purpose**: {What this format stores}
**Byte order**: Little-endian / Big-endian

| Offset | Size | Field | Type | Description |
|--------|------|-------|------|-------------|
| 0 | 4 | Magic | char[4] | Format identifier |
| 4 | 2 | Version | uint16 | Format version |
| ... | ... | ... | ... | ... |
```

If the SDK has no custom formats, state: "No custom serialization formats."

---

## Pseudo-Code Notation Reference

Every keyword maps to a specific computation pattern. This notation is the only vocabulary permitted in method blocks.

### Data Access

| Keyword | Description |
|---------|-------------|
| `GET` | Access a field or element from a data structure |
| `SET` | Assign a value to a field or element |
| `LOOKUP` | Dictionary/map lookup by key |
| `INDEX` | Array/list access by index |

### Computation

| Keyword | Description |
|---------|-------------|
| `COMPUTE` | Calculate a derived value from inputs |
| `TRANSFORM` | Apply a transformation to data (mapping, conversion) |
| `ACCUMULATE` | Aggregate values (sum, collect, merge) |
| `INTERPOLATE` | Blend between values |
| `CLAMP` | Constrain a value to a range |

### Validation

| Keyword | Description |
|---------|-------------|
| `VALIDATE` | Check a precondition; throws if violated |
| `CHECK` | Check a condition; returns error/null if violated (non-throwing) |
| `ASSERT` | Internal invariant check (debug-only) |

### Flow Control

| Keyword | Description |
|---------|-------------|
| `IF` / `ELSE` | Conditional branch |
| `FOREACH` | Iteration over a collection |
| `ITERATE` | Indexed iteration (when index matters) |
| `WHILE` | Loop with condition |
| `RETURN` | Method return |
| `YIELD` | Iterator/enumerator yield |
| `BREAK` | Exit loop |

### Construction

| Keyword | Description |
|---------|-------------|
| `CREATE` | Instantiate a new object |
| `ALLOCATE` | Allocate a buffer or array |
| `COPY` | Deep or shallow copy |
| `CLONE` | Clone a data structure |

### Algorithm-Specific

| Keyword | Description |
|---------|-------------|
| `PROPAGATE` | Wave/constraint propagation (WFC, etc.) |
| `BACKTRACK` | Revert to previous state (search algorithms) |
| `ENQUEUE` / `DEQUEUE` | Priority queue operations |
| `PUSH` / `POP` | Stack operations |
| `EMIT` | Produce output element (mesh vertices, etc.) |

### Formatting Symbols

| Symbol | Meaning | Example |
|--------|---------|---------|
| `→` | Result or consequence | `VALIDATE x > 0 → ArgumentException if not` |
| `←` | Source of data | `result ← COMPUTE hash FROM params` |
| `//` | Comment | `// Greedy merge pass` |

---

## Rules

1. **One map per SDK** — placed at `docs/sdks/maps/{sdk-name}.md`
2. **Every public method must appear** — each gets a full pseudo-code block
3. **Pseudo-code must be verifiable** — every operation must correspond to actual code (or intended code for aspirational maps)
4. **No implementation language syntax** — only the notation vocabulary above. No C#, no variable declarations
5. **Aspirational maps are first-class** — the pseudo-code IS the design specification
6. **Preserve AUDIT markers** — same rule as deep dives
7. **Cross-reference deep dive** — header must link to the deep dive document
8. **Algorithms get dedicated sections** — non-trivial algorithms (meshing, WFC, noise generation, etc.) get their own blocks in section 7 with full detail, complexity analysis, and references

---

## Relationship to Other Documents

| Document | Purpose | Source of Truth For |
|----------|---------|---------------------|
| **SDK Deep Dive** | Architecture & context | Why it exists, who uses it, data model overview |
| **SDK Implementation Map** | Behavioral specification | How each method/algorithm works, pseudo-code |
| **Planning Document** | Design rationale | Architectural decisions, phase plans |
| **SDK Overview Guide** | Ecosystem catalog | How SDKs relate to each other and to plugins |
