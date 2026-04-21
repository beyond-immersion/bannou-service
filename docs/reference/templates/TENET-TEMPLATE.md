# Tenet Document Structure — Canonical Template

> **Purpose**: Declares the exact structure of every tenet and every tenet-carrying markdown file. Parsers (`.claude/mcp/helpers/tenets.mjs` and any future tooling) rely on this shape. Deviations break tooling.

> **Who uses this**: The MCP tenet tools (`list_tenets`, `get_tenet`, `get_tenets`, `list_violations`, `search_tenets`) parse tenet files by sentinel markers defined below. Any tool that adds/edits/removes a tenet MUST produce output that conforms.

> **Frozen**: Like every document under `docs/reference/`, this template is frozen. Changes require explicit user instruction.

---

## The Sentinel Pair

Every tenet body — everywhere it appears across the tenet docs — is wrapped in an HTML-comment sentinel pair keyed on the tenet id:

```markdown
<!-- TENET:T4 -->
## Tenet 4: Infrastructure Libs Pattern (ABSOLUTE)

**Rule**: Services MUST use the three infrastructure libs for all infrastructure concerns...

### Usage Patterns

...rest of body, including subsections, tables, code blocks, cross-references...
<!-- /TENET:T4 -->
```

**HTML comments don't render in markdown** (GitHub, VS Code preview, most static site generators all strip them), so the sentinels are invisible to human readers. They exist only for mechanical parsing.

### Required properties of the sentinel pair

| Rule | Why |
|---|---|
| Id in the open sentinel MUST match id in the close sentinel | Parsers use id as the anchor; mismatches are a structural test failure |
| Sentinels appear on their own lines | Makes the line-based parser trivial; prevents accidental markdown escaping |
| Open sentinel is the line immediately before the `## Tenet N:` heading | Gives the parser a zero-ambiguity start position |
| Close sentinel is the line immediately after the last content line of the body | Gives the parser a zero-ambiguity end position; nothing after the close is part of the tenet |
| A tenet appears in EXACTLY ONE file | Declared by `TENET_CATEGORY_MAP` in `helpers/tenets.mjs`; enforced by the parser |
| Every id in `TENET_CATEGORY_MAP` has exactly one matching pair | No orphans, no duplicates, no cross-file leaks |

---

## The Tenet Block

Between the open and close sentinels, a single tenet MUST have this shape, in this order:

```markdown
<!-- TENET:T{N} -->
## Tenet {N}: {Name} ({SEVERITY})

**Rule**: {One-sentence or two-sentence declaration of what the tenet requires.}

{Optional body: subsections, tables, code blocks, cross-references, examples.
 All subsections MUST be H3 (`###`) or deeper — no H2 headings inside a tenet block.}
<!-- /TENET:T{N} -->
```

### Heading

Format: `## Tenet {N}: {Name} ({SEVERITY})`

| Element | Rule |
|---|---|
| `##` | Always level-2 heading. H3+ headings are for subsections within the body |
| `Tenet` | Literal word "Tenet" |
| `{N}` | Integer, no zero-padding (`4` not `04`) |
| `{Name}` | Human-readable name, title case (`Infrastructure Libs Pattern`) |
| `{SEVERITY}` | Severity token in parentheses, UPPERCASE. Canonical set: `INVIOLABLE`, `ABSOLUTE`, `MANDATORY`, `REQUIRED`, `STANDARDIZED`, `RECOMMENDED`, `DOCUMENTED`, `THREE-TIER`, `CONSOLIDATED`, `FORBIDDEN`, `GUIDELINE`. The parenthesized severity is optional only for `T0` (meta-rule) |

### Rule line

Immediately after the heading (with exactly one blank line between them), the body opens with:

```markdown
**Rule**: {sentence}
```

- Bold literal `**Rule**:` prefix, followed by a space, then the rule text.
- Single physical line. Multi-sentence rules are allowed but must fit one line for parser predictability.
- This line is what `list_tenets` / `search_tenets` returns as the canonical "rule" when no summary is available.

### Body

Free-form content after the Rule line, up to the close sentinel. Allowed elements:

- Prose paragraphs
- H3 (`###`) and H4 (`####`) subsection headings
- Tables
- Fenced code blocks
- Blockquotes
- Cross-references to other tenets by category name (per T0 — **never by number**)

**Forbidden inside a tenet body**:

| Forbidden | Why |
|---|---|
| H2 (`##`) headings | H2 is reserved for tenet heading and file-level structure |
| Another `<!-- TENET:T... -->` sentinel | No nesting; tenets are flat |
| A Quick Reference / violations table (`\| Violation \| Tenet \| Fix \|`) | The master Quick Reference in `TENETS.md` is the single source of truth. Per-tenet violation rows are cross-referenced by the parser automatically |
| Document footer paragraphs (`*This document covers tenets...*`) | Footers are file-level, not tenet-level; they go outside the close sentinel |
| Horizontal rules (`---`) | HRs are file-level separators between tenets or sections; inside a tenet body they create parsing ambiguity |

---

## The Tenet File

A tenet-carrying markdown file has this shape:

```markdown
# {File Title}

> ⛔ **FROZEN DOCUMENT** — ...standard frozen-doc blockquote...

> **Category**: {...}
> **When to Reference**: {...}
> **Tenets**: {list of ids declared in this file}

{Optional introductory prose — no tenet bodies here.}

---

<!-- TENET:TN1 -->
## Tenet N1: ...
...
<!-- /TENET:TN1 -->

---

<!-- TENET:TN2 -->
## Tenet N2: ...
...
<!-- /TENET:TN2 -->

---

{Optional footer paragraph, cross-references, "See also" links.
 Everything here is OUTSIDE all sentinel pairs — parsers ignore it.}
```

### Between-tenet separator

Between the close sentinel of one tenet and the open sentinel of the next, the file MAY contain:

- A horizontal rule (`---`) on its own line
- Blank lines

Nothing else. No prose, no headings, no tables. The separator region is inert to the parser.

### Post-last-tenet content

After the final tenet's close sentinel, the file MAY contain:

- A horizontal rule
- A single footer paragraph (typically italicized: `*This document covers tenets X, Y, Z. See [TENETS.md]...*`)

Nothing else. In particular, per-category Quick Reference / violation tables do NOT appear in category files — the master table in `TENETS.md` is the canonical source of truth.

---

## The Index File (TENETS.md)

`docs/reference/TENETS.md` is the master index. It is a tenet-carrying file (contains T0, T1, T2 bodies wrapped in sentinel pairs like any other tenet) AND the canonical catalog.

Additional structure TENETS.md MUST contain, in addition to the tenet blocks:

1. **Preamble** — version, AI assistant warnings, meta-rules about tenet compliance.
2. **Tenet bodies for T0, T1, T2** — the meta-tenet, the schema-first delegation, and the service-hierarchy delegation. Each wrapped in sentinels per the rules above.
3. **Tenet Categories table** — links to the per-category files.
4. **Per-category summary tables** — one row per tenet: `| **T{N}** | {Name} | {Core Rule} |`. The parser uses the "Core Rule" column as the `summaryRule` field on each parsed tenet.
5. **Quick Reference: Common Violations** — the master violations table, `| Violation | Tenet | Fix |`. Parser extracts every row; each cited tenet id must resolve to a parsed tenet.
6. **Enforcement section** — how the tenets are enforced (code review, CI, etc.).

Only items 2, 4, and 5 are parsed by the tenet tools. The preamble, the Tenet Categories table, and the Enforcement section are informational.

---

## Example (T4 — Foundation)

This is what a normalized tenet block looks like end-to-end:

```markdown
<!-- TENET:T4 -->
## Tenet 4: Infrastructure Libs Pattern (ABSOLUTE)

**Rule**: Services MUST use the three infrastructure libs for all infrastructure concerns. Direct database/cache/queue access is FORBIDDEN in service code.

| Lib | Purpose | Replaces |
|-----|---------|----------|
| **lib-state** | State management (Redis/MySQL) | Direct Redis/MySQL connections |
| **lib-messaging** | Event pub/sub (RabbitMQ) | Direct RabbitMQ channel access |
| **lib-mesh** | Service invocation (YARP) | Direct HTTP client calls |

### Usage Patterns

```csharp
_stateStore = stateStoreFactory.GetStore<MyModel>(StateStoreDefinitions.MyService);
```

### State Store Schema-First Pattern

All stores defined in `schemas/state-stores.yaml`...

### Lua Script Requirements (STRICT)

Lua scripts via `IRedisOperations.ScriptEvaluateAsync()` are a last resort...
<!-- /TENET:T4 -->
```

A parser finding `<!-- TENET:T4 -->` and `<!-- /TENET:T4 -->` knows with certainty that everything between is T4 and nothing else. The close sentinel tells the parser "T4 ends here" regardless of what follows in the file.

---

## Validation

The MCP tool `validate_tenets` (when landed in a future step) will enforce:

1. Every id in `TENET_CATEGORY_MAP` has exactly one `<!-- TENET:TN -->` + matching `<!-- /TENET:TN -->` pair.
2. Every pair is in the declared file for that category.
3. No duplicate pairs anywhere.
4. Sentinel ids are all of the form `T\d+` — no stray comments that look sentinel-shaped but aren't.
5. Every tenet body contains a `## Tenet N: Name (...)` heading whose number matches the sentinel id.
6. Every tenet body contains a `**Rule**:` line (or, for T0/T1/T2, a leading bolded sentence).
7. Summary rules in `TENETS.md`'s per-category tables exist for every id that lives in a category file.
8. Every violation row in the Quick Reference cites tenet ids that exist in the parsed set.

Structural drift is caught mechanically, not by agents re-reading the files and hoping.

---

*This document is frozen. Tenet tooling depends on the exact structure declared here.*
