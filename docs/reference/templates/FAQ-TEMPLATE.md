# Document Template: FAQ Documents

> ⛔ **FROZEN DOCUMENT** — Defines an authoritative template. AI agents MUST NOT modify any content without explicit user instruction. See CLAUDE.md § "Reference Documents Are Frozen."

> This document defines the structure for FAQ (architectural rationale) documents.
> Each FAQ lives at: `docs/faqs/{QUESTION-SLUG}.md`

---

## Header Format

All FAQ documents MUST use this header format:

```markdown
# {Question as Title Case}

> **Last Updated**: {YYYY-MM-DD}
> **Related Plugins**: {comma-separated plugin names, e.g., Item (L2), Inventory (L2)}
> **Short Answer**: {1-3 sentences answering the question directly. This is
> extracted by the documentation generation pipeline for GENERATED-FAQ-CATALOG.md.
> Must be self-contained — a reader of only this block should get a useful answer.}
```

**Required fields:**
- **Last Updated**: Date of last substantive content change (YYYY-MM-DD format)
- **Related Plugins**: Which plugins this FAQ is about. Use `{Name} (L{n})` format.
- **Short Answer**: The extractable summary. See rules below.

**The Short Answer IS the summary section.** Unlike other document types that use a separate `## Summary` section, FAQs use the Short Answer blockquote field. The documentation generation pipeline extracts this field verbatim.

---

## Short Answer Rules

1. **1-3 sentences maximum** — extracted by the documentation generation pipeline
2. Must **directly answer the question** posed in the title — not defer to the body
3. **No markdown links, code blocks, or formatting** — plain text only
4. Must be **self-contained** — a reader who only sees the short answer should get a useful, accurate response
5. Multi-line short answers continue with `> ` blockquote prefix on each line

**Example:**
```markdown
> **Short Answer**: Because "what a thing is" and "where a thing is" are
> fundamentally different concerns with different consumers, different scaling
> characteristics, and different mutation patterns. Item manages definitions and
> instances. Inventory manages containers and placement.
```

---

## Document Body

After the header, a horizontal rule (`---`), then the detailed answer:

```markdown
---

## The Detailed Answer

{Full explanation with context, diagrams, examples, and architectural reasoning.}
```

### Recommended Sections

- **The Detailed Answer** — Full explanation of the architectural reasoning
- **The Alternative** — What the rejected alternative looks like and why it fails
- **What This Enables** — Concrete benefits of the current design
- **Related** — Links to deep dives, guides, other FAQs

**Guidelines:**
- FAQs explain **why**, not **how** — for "how", link to guides
- The question in the title should be genuine — something a developer would actually ask
- Keep the detailed answer focused on architectural reasoning, not implementation details
- Use plugin deep dives as source of truth for current capabilities
- Update `Last Updated` when making substantive changes

---

## File Naming Convention

FAQ files use the question as a slug: `WHY-{REST-OF-QUESTION}.md` or `WHAT-{REST-OF-QUESTION}.md` or `HOW-{REST-OF-QUESTION}.md`

**Examples:**
- `WHY-ARE-ITEMS-AND-INVENTORY-SEPARATE-SERVICES.md`
- `WHAT-IS-THE-VARIABLE-PROVIDER-FACTORY-PATTERN.md`
- `HOW-DO-NPCS-THINK.md`

---

## Maintenance Workflow

The `/maintain-faq` skill maintains these documents by:
1. Verifying the header matches this template's format
2. Verifying the Short Answer is still accurate against current deep dives
3. Cross-referencing architectural claims against the actual codebase state
4. Checking if Related Plugins list is complete and layers are correct
5. Updating `Last Updated` when changes are made
