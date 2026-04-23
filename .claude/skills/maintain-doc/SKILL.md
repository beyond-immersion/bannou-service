---
description: "Maintain documentation files (FAQ, guide, planning, or operations). Verifies structure against template, content accuracy against source-of-truth documents, and updates Last Updated timestamp. First invocation builds a task queue for all documents in the category sorted oldest-reviewed first; each subsequent invocation processes the next document."
argument-hint: "<faq|guide|planning|ops> [name] - Category and optional document name (e.g., 'faq', 'guide economy-system', 'ops testing')"
disable-model-invocation: true
---

# Maintain Document

Each invocation maintains exactly ONE document. The first invocation creates a task queue for the entire category (sorted oldest-reviewed first). Run the skill repeatedly to work through the queue.

## Phase 1: Parse & Context

**Step 1a:** Parse the argument. First word is the category:

| Category | Directory | Template |
|---|---|---|
| `faq` | `docs/faqs/` | `docs/reference/templates/FAQ-TEMPLATE.md` |
| `guide` | `docs/guides/` | `docs/reference/templates/GUIDE-TEMPLATE.md` |
| `planning` | `docs/planning/` | `docs/reference/templates/PLANNING-TEMPLATE.md` |
| `ops` | `docs/operations/` | `docs/reference/templates/OPERATIONS-TEMPLATE.md` |

Second word (optional): specific document name to process instead of the oldest.

If no category or unrecognized category → list the 4 categories and STOP.

**Step 1b:** Load context:
```
prepare_context(profile: "dev")
```

**Step 1c:** Read the template for this category:
```
get_document(path: "reference/templates/{TEMPLATE}")
```

**Step 1d:** Load all document catalogs (cross-reference index):
```
list_documents()
```

## Phase 2: Task Queue

```
TaskList()
```

If pending tasks exist with metadata `category: "{category}"` → skip to Phase 3.

**If no tasks exist, build the queue:**

**Step 2a:** List all documents with their Last Updated dates:
```bash
for f in docs/{directory}/*.md; do
  date=$(grep -m1 'Last Updated' "$f" | grep -oP '\d{4}-\d{2}-\d{2}' || echo "1970-01-01")
  echo "$date|$(basename "$f" .md)|$f"
done | sort
```

**Step 2b:** Create one task per document, oldest-reviewed first.

First task (will be processed this invocation):
```
TaskCreate(
  subject: "maintain-doc: {name}",
  description: "Oldest-reviewed document. Processing now.",
  metadata: { "category": "{category}", "file": "{path}", "name": "{name}" }
)
```

All subsequent tasks:
```
TaskCreate(
  subject: "maintain-doc: {name}",
  description: "⛔ DO NOT CONTINUE TO THIS TASK — run /maintain-doc {category} again.",
  metadata: { "category": "{category}", "file": "{path}", "name": "{name}" }
)
```

**Step 2c:** Present the queue and proceed:
```
## Document Queue: {category} ({N} documents, sorted oldest-reviewed first)

| # | Document | Last Updated |
|---|----------|-------------|
| 1 | {name} | {date} |
...
```

## Phase 3: Select & Load

If a specific name was given in the argument, find that task. Otherwise take the first pending task.

Mark it in_progress:
```
TaskUpdate(taskId: "{id}", status: "in_progress")
```

**Step 3a:** Read the document:
```
get_document(path: "{file path from task metadata}")
```

**Step 3b:** Load source-of-truth based on category:

| Category | What to Load |
|---|---|
| `faq` | `get_plugin_docs(name: "{plugin}")` for each plugin in Related Plugins |
| `guide` | `get_plugin_docs(name: "{plugin}")` for each plugin in Key Plugins |
| `planning` | `get_plugin_docs(name: "{plugin}")` for each in Related Plugins, plus `ls schemas/{plugin}-api.yaml` to check implementation status |
| `ops` | `read_file("Makefile")` plus any workflow/compose files referenced in the document |

## Phase 4: Verify & Fix

### 4a: Structure

Verify header fields against the template. Fix immediately with `edit_file`. The template defines the required fields — check each one mechanically.

### 4b: Content (category-specific)

**FAQ:**
- Short Answer: directly answers the question? Factually correct per deep dives? 1-3 sentences, plain text, self-contained?
- Reasoning: layer numbers correct? Dependency direction claims valid? Design rationale still relevant? Capability claims match deep dives?

**Guide:**
- Summary section: exists? 2-4 sentences, plain text, self-contained, accurate?
- Content: capability claims match deep dives? Status claims match reality? Layer references correct?

**Planning:**
- Status accuracy: `Aspirational` + schemas exist → update to `Active`. `Active` + all plugins implemented → `Implemented`. `Superseded` → verify superseding doc exists. `Draft` + substantive content → update appropriately.
- Content: plugin capability claims match deep dives? Composition respects layer hierarchy? North Star references accurate?

**Operations:**
- Commands: for every `make {target}`, verify with `grep "^{target}:" Makefile`. For every script path, verify with `ls {path}`.
- Procedures: file paths still exist? Docker references valid?

**For ALL categories — the edit rule:**
- **Objective facts** (wrong number, renamed file, moved path, broken link) → fix directly with `edit_file`
- **Everything else** (reasoning validity, design relevance, capability interpretation) → flag for human review in report

### 4c: Completeness

- [ ] Related/Key Plugins field covers all plugins discussed in body
- [ ] Cross-references to other docs are present where appropriate
- [ ] No broken links to moved or deleted documents

## Phase 5: Finalize

**Step 5a:** Update `Last Updated` to today's date — always, even if no other changes were needed. This marks the review date for audit ordering.

**Step 5b:** Category-specific metadata:
- **Guide**: bump minor version if content beyond metadata was changed. Update Status if plugin status changed.
- **Planning**: update Status if implementation state changed (per 4b check).

**Step 5c:** Mark task complete:
```
TaskUpdate(taskId: "{id}", status: "completed")
```

**Step 5d:** Report:

```
## Maintained: {name} ({category})

Changes: {list of edits, or "Reviewed — no changes needed"}
Flagged: {items needing human judgment, or "None"}
Remaining: {N} documents in queue
Next: /maintain-doc {category}
```

**STOP. Do not process the next document.**
