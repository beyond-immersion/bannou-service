---
description: "Resolve open questions for a plugin one at a time. Loads comprehensive context, discovers unanswered design considerations/bugs/extensions, investigates each deeply with cross-cutting context, presents options grounded in documentation, and applies the user's decision to all affected documents."
argument-hint: "[plugin-name] - Plugin to resolve open questions for (e.g., 'divine', 'quest', 'status')"
disable-model-invocation: true
---

# Resolve Plugin Open Questions

Each invocation resolves exactly ONE open question. Run repeatedly to work through all items.

## Rules

1. **ONE item per invocation.** Present options, get a decision, apply it, STOP.
2. **HARD STOP before the user decides.** Present the item and options. End your response. Do not continue until the user explicitly chooses. "Sounds good" is not an option selection — ask which option specifically.
3. **Read ALL related context.** For cross-cutting concerns, full-read deep dives and implementation maps of EVERY involved plugin via `get_plugin_docs`. Not summaries. Not greps.
4. **Each item is atomic.** Every design consideration, every bug, every extension gets its own task. Do not group related items.
5. **Recommend, don't punt.** The documentation almost certainly contains the answer. Ground every recommendation in a specific document you can cite.
6. **No general knowledge.** Ground every recommendation in specific Bannou documentation. Cite the document and section. If you can't cite it, you haven't researched enough.
7. **No shortcuts.** Investigation (Phase 3) is mandatory for every item. Simple-seeming questions often have cross-cutting implications at 100k scale.

---

## Phase 0: Context Loading

**Step 0a:** Load plugin context:
```
prepare_context(profile: "plugin", service: "{plugin-name}")
```

**Step 0b:** Big Brain Mode — read the vision documents:
```
read_file("docs/reference/VISION.md")
read_file("docs/reference/PLAYER-VISION.md")
```

**Step 0c:** Load document catalogs for broad search:
```
list_documents()
```

---

## Phase 1: Discovery & Task Building

```
TaskList()
```
If pending tasks exist with metadata `plugin: "{plugin-name}"`, skip to Phase 2.

**If no tasks exist, build the task list:**

**Step 1a: Search GitHub issues**
```bash
gh issue list --search "{plugin-name}" --state open --limit 50 --json number,title,labels
gh issue list --search "{plugin-name}" --state closed --limit 50 --json number,title,labels
```

For each issue involving this plugin, read the FULL issue AND ALL comments:
```bash
gh issue view {number} --json number,title,body,labels,comments
```

**Step 1b: Scan the deep dive**

Find ALL unresolved items:

| Section | Unresolved = |
|---|---|
| Design Considerations | No `~~strikethrough~~`, no `AUDIT:RESOLVED`, no `AUDIT:CONFIRMED`, no `AUDIT:DESIGN_RESOLVED`. Items with `AUDIT:NEEDS_DESIGN` still lacking resolution. |
| Bugs | Unfixed bugs where fix approach needs a design decision |
| Potential Extensions | Each extension = 1 task. **Each. One.** |
| Stubs | Each stub with an open design question |

**Step 1c:** Cross-reference and deduplicate (GH issue + deep dive item = one task).

**Step 1d:** Create tasks:
```
TaskCreate(
  subject: "{Brief description}",
  description: "Source: {section / GH#}\n\nContext: {actual text}\n\nGH Issues: {numbers or 'none'}",
  metadata: { "plugin": "{plugin-name}", "source": "{section}", "type": "{design-question|bug|extension|stub}" }
)
```

**Present the full list:**
```
## Open Questions for {Plugin}: {N} items

| # | Type | Item | Source |
|---|------|------|--------|
| {id} | {type} | {title} | {source} |

Processing item #{first-id} next.
```

---

## Phase 2: Select Next Item

```
TaskList()
```
Find the first pending task with metadata `plugin: "{plugin-name}"`.

If none remain:
```
## All Open Questions Resolved for {Plugin Name}
```
STOP.

Mark in-progress: `TaskUpdate(taskId: "{id}", status: "in_progress")`

---

## Phase 3: Deep Investigation

**Mandatory for every item.**

**Step 3a:** Identify ALL involved plugins (explicitly mentioned, in composition, in dependency chain, publishing/consuming relevant events).

**Step 3b:** Full-read ALL related plugin context:
```
get_plugin_docs(name: "{related-plugin}")
```
For EACH related plugin beyond the primary. Non-negotiable — if the concern involves Currency and Seed and Collection, read all three.

**Step 3c:** Search for related documentation:
```
search_docs(query: "{relevant keywords}")
```
Any document used in your analysis MUST be full-read via `get_document`. Never cite grep snippets.

**Step 3d:** Check GH issues — re-read referenced issues, search for related:
```bash
gh issue list --search "{relevant keywords}" --state all --limit 20
```

**Step 3e:** Synthesize:
1. What exactly is the question?
2. What do VISION.md / PLAYER-VISION.md say about this area?
3. What do the TENETS require?
4. What do COMMON PATTERNS suggest?
5. What existing infrastructure already addresses parts of this?
6. What does this look like at 100k actor scale?
7. What are the 2-3 most reasonable options?

---

## Phase 4: Present to User

```markdown
## Item {N}/{total}: {Title}

**Source:** {deep dive § section / GH #{number} / both}
**Type:** {Design question / Bug / Extension / Stub}
**Related Plugins:** {all investigated}

### Context
{Full description — what the question is, why it exists, what's at stake}

### What the Documentation Says
{Relevant excerpts — cite specific documents by name and section}

### Analysis
{Synthesis at 100k scale, content flywheel context, grounded in vision docs}

### Options

**Option A: {Name}**
{Description, pros, cons, tenet alignment, behavior at 100k scale}

**Option B: {Name}**
{Description, pros, cons, tenet alignment, behavior at 100k scale}

{Option C if applicable — never more than 3}

### Recommendation
**{Option X}** — {1-3 sentences grounded in specific documentation}

### Documents That Will Be Updated
- `docs/plugins/{PLUGIN}.md` § {section} — {what changes}
- `GH #{number}` — {comment / close}
- {other affected docs}
```

**⛔ STOP. End your response.**

Do not continue until the user explicitly chooses an option.
- "Sounds good" → ask which option specifically
- Follow-up question → answer it, do not apply any decision
- "Go with A" → proceed to Phase 5

---

## Phase 5: Apply Decision

**Only after the user has explicitly chosen.**

**Step 5a:** Update the primary deep dive:

| Decision type | Format |
|---|---|
| Design question answered | `~~**Title**~~: **RESOLVED** (YYYY-MM-DD) — {decision with rationale}` |
| Bug with design answer | `~~**Title**~~: **RESOLVED** (YYYY-MM-DD) — {resolution}` |
| Extension accepted | Update description; add `<!-- AUDIT:CONFIRMED:YYYY-MM-DD -->` |
| Extension rejected | Remove the item entirely |
| Concern dismissed | Remove the item entirely |

**Do NOT delete RESOLVED design considerations.** The resolution text is the implementation blueprint.

**Step 5b:** Update connected documents — check all docs read in Phase 3 for references to this question. Update other deep dives, planning docs, implementation maps as needed.

**Step 5c:** Update GitHub issues:
1. Write resolution to `/tmp/gh-comment-{number}.md` via `write_file`
2. Post: `gh issue comment {number} --body-file /tmp/gh-comment-{number}.md`
3. If fully resolved → `gh issue close {number}`

**Step 5d:** Mark task complete: `TaskUpdate(taskId: "{id}", status: "completed")`

---

## Phase 6: Done

```
## Resolved: {Title}

**Decision:** {option chosen with rationale}
**Documents updated:** {list}
**GH issues updated:** {list or "none"}
**Remaining items:** {N}

Run `/resolve-plugin {plugin-name}` for the next item.
```

**STOP. Do not proceed to the next item.**
