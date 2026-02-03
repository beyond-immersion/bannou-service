---
description: Maintain and update a plugin deep dive document. Ensures structure matches template, content is accurate, and all quirks/bugs are captured. Preserves existing work tracking markers.
argument-hint: "[plugin-name|random] - Plugin to maintain (e.g., 'account', 'auth') or 'random' to pick one at random"
---

# Plugin Maintainer Command

You are executing the `/maintain-plugin` workflow. This is a comprehensive review and update process for plugin deep dive documents.

## Purpose

This command ensures deep dive documents are:
1. **Structurally correct** - All sections from DEEP_DIVE_TEMPLATE.md present
2. **Content accurate** - Information matches actual source code
3. **Complete** - All quirks, bugs, and gaps are captured
4. **Ready for auditing** - Properly formatted for `/audit-plugin` to process

**IMPORTANT: This is DOCUMENTATION ONLY. Do NOT:**
- Run `dotnet build` or any build commands
- Run tests
- Make code changes

All builds and tests are assumed passing. You are reading code to update docs, not modifying code.

## ⛔ CRITICAL: THIS WORKFLOW REQUIRES EDITING THE DOCUMENT

**This workflow is NOT just verification and reporting. You MUST edit the document.**

If you complete this workflow without making any Edit tool calls to the deep dive document, **you have failed the workflow**. The only exception is if the document is already perfect (extremely rare).

**"Process" means EDIT, not "verify and report":**
- "Process FIXED items" = verify in code, then DELETE or MOVE the line using Edit tool
- "Update the document" = use Edit tool to change the file
- "Remove entirely" = use Edit tool to delete the line

**Verification without editing is a workflow failure.**

## Critical Rules: Markers and Fixed Items

### Preserve AUDIT Markers (Active Work)

**NEVER remove or modify these HTML comment markers:**
```markdown
<!-- AUDIT:IN_PROGRESS:... -->
<!-- AUDIT:NEEDS_DESIGN:...:https://... -->
<!-- AUDIT:BLOCKED:... -->
```

These are managed by `/audit-plugin` and represent active work tracking.

### Process FIXED/IMPLEMENTED/MOVED Items (Completed Work)

**You MUST verify AND edit (not just verify) these inline markers:**

**FIXED items** (in Bugs, Stubs, Design Considerations):
```markdown
N. ~~**Original title**~~: **FIXED** (YYYY-MM-DD) - Description of fix.
```

**IMPLEMENTED items** (in Potential Extensions, Stubs):
```markdown
N. ~~**Feature name**~~: **IMPLEMENTED** (YYYY-MM-DD) - Description.
```

**MOVED items** (in Design Considerations):
```markdown
N. ~~**Issue title**~~: **MOVED TO QUIRKS** (YYYY-MM-DD) - Reason.
```

**For ALL of these, you MUST:**
1. Verify the fix/implementation/move exists in source code or document
2. **DELETE the line entirely using the Edit tool** (if clean with no quirks)
3. Or **MOVE to Intentional Quirks using Edit tool** (if non-obvious behavior remains)
4. Or **RESTORE as active item** (if the fix is missing from code)

**Leaving strikethrough items in place is a workflow failure.**

## Workflow Phases

### Phase 1: Plugin Selection

**If argument is "random" or no argument provided:**
Use bash for true randomness:
```bash
ls docs/plugins/*.md | grep -v DEEP_DIVE_TEMPLATE | shuf -n 1
```
Extract the plugin name from the result and announce: "Maintaining: {plugin-name}" then proceed immediately.

**If specific plugin name provided:**
1. Verify `docs/plugins/{PLUGIN}.md` exists
2. If not found, list available plugins and ask user to choose
3. Once valid, proceed immediately

### Phase 2: Template Compliance Check

Read `docs/plugins/DEEP_DIVE_TEMPLATE.md` to get the expected structure.

Required sections (must exist):
1. Header (with Plugin, Schema, Version, State Store metadata)
2. Overview
3. Dependencies
4. Dependents
5. State Storage
6. Events (Published and Consumed subsections)
7. Configuration
8. DI Services & Helpers
9. API Endpoints
10. Visual Aid
11. Stubs & Unimplemented Features
12. Potential Extensions
13. Known Quirks & Caveats (with Bug/Intentional/Design subsections)
14. Work Tracking (new section - may need to be added)

**Compliance Report Format:**
```markdown
## Template Compliance

| Section | Status | Notes |
|---------|--------|-------|
| Header | {Present/Missing/Incomplete} | {details} |
| Overview | {Present/Missing/Incomplete} | {details} |
...

### Missing Sections
{List of sections that need to be added}

### Structural Issues
{List of formatting/structure problems}
```

### Phase 3: Source Code Deep Read

This is the most important phase. You must READ EVERY relevant file thoroughly.

**Files to read:**
```
plugins/lib-{service}/
├── {Service}Service.cs              # PRIMARY - read completely
├── {Service}ServiceEvents.cs        # Event handlers
├── Services/*.cs                    # All helper services
└── Generated/
    ├── {Service}ServiceConfiguration.cs  # Configuration class
    └── I{Service}Service.cs              # Interface (for method signatures)

schemas/
├── {service}-api.yaml               # API definition
├── {service}-events.yaml            # Event definitions
├── {service}-configuration.yaml     # Config schema
└── {service}-client-events.yaml     # Client events (if exists)

plugins/lib-{service}.tests/
└── *.cs                             # All test files (for intended behavior)
```

**What to extract:**
- All state store key patterns used
- All events published (topic + type)
- All events consumed (subscriptions)
- All service client calls
- All configuration properties used
- All helper services and their roles
- Non-obvious implementation details
- Potential bugs, quirks, edge cases

### Phase 4: Content Accuracy Verification

Compare the deep dive document against source code findings.

**NOTE: This phase IDENTIFIES inaccuracies. You will FIX them in Phase 6 using the Edit tool.**

**For each section, verify:**

| Section | Verification |
|---------|-------------|
| Dependencies | Match actual injected services |
| Dependents | Cross-reference with other plugin code |
| State Storage | All key patterns from code are listed |
| Events Published | All PublishAsync calls are captured |
| Events Consumed | All [EventSubscription] handlers listed |
| Configuration | All config properties documented |
| DI Services | All constructor parameters listed |

**Accuracy Report Format:**
```markdown
## Content Accuracy

### Dependencies
- [x] lib-state - Verified in constructor
- [ ] lib-mesh - **MISSING from doc** (calls IAccountClient)
...

### State Storage
- [x] `account:{id}` - Documented
- [ ] `account:email:{email}` - **MISSING from doc**
...

### Events
- [x] `account.created` - Documented
- [ ] `account.merged` - **MISSING from doc**
...
```

### Phase 5: Quirks & Bugs Discovery

Systematically review the code for issues.

**NOTE: This phase IDENTIFIES new issues. You will ADD them to the document in Phase 6 using the Edit tool.**

Check for:

**Code Quality Issues:**
- [ ] `NotImplemented` returns or throws
- [ ] TODO/FIXME comments
- [ ] Empty catch blocks
- [ ] Swallowed exceptions
- [ ] Missing null checks
- [ ] Unused parameters or variables

**Logic Issues:**
- [ ] Off-by-one errors
- [ ] Race conditions (non-atomic read-modify-write)
- [ ] Missing validation
- [ ] Incorrect status code returns
- [ ] Missing event publications

**State Management Issues:**
- [ ] Orphaned state (created but never cleaned up)
- [ ] Missing index updates
- [ ] Stale cache potential
- [ ] Missing distributed locks

**TENET Violations:**
- [ ] Direct Redis/MySQL access (should use lib-state)
- [ ] Direct RabbitMQ access (should use lib-messaging)
- [ ] Missing x-permissions on endpoints
- [ ] Hardcoded values that should be config

**Orphaned Configuration (T21 Violations):**
- [ ] Configuration properties defined in schema but never referenced in service code
- [ ] Properties that were bypassed by a fix (e.g., code now uses request parameter instead of config)
- [ ] Properties that exist in generated config class but `_configuration.PropertyName` never appears in service code

**IMPORTANT**: When a fix changes behavior from "use configuration" to "use request parameter", the configuration property often becomes orphaned. Always check if the original configuration property is still used elsewhere. If not, add it as a Bug: "Orphaned configuration: `PropertyName` is defined but never used (T21 violation)".

**Compare findings against existing doc sections:**
- Are all known bugs still present in code? (remove fixed ones)
- Are there new bugs not yet documented?
- Are "intentional quirks" actually intentional? (check comments/tests)
- Are "design considerations" still relevant?

### Phase 5b: Process Strikethrough Items (FIXED/IMPLEMENTED/MOVED)

Scan for ALL items with strikethrough formatting. These include:

```markdown
N. ~~**Title**~~: **FIXED** (YYYY-MM-DD) - Description.
N. ~~**Title**~~: **IMPLEMENTED** (YYYY-MM-DD) - Description.
N. ~~**Title**~~: **MOVED TO QUIRKS** (YYYY-MM-DD) - Description.
```

**⛔ CRITICAL: For EACH strikethrough item, you MUST make an Edit tool call.**

"Verify and report" is NOT sufficient. You must EDIT the document.

**For each strikethrough item:**

1. **Verify in source code** that the fix/implementation/move actually exists

2. **Decide the action:**
   - **DELETE** (most common): Fix is clean, no quirks, just remove the line
   - **MOVE TO QUIRKS**: Fix introduced non-obvious behavior worth documenting
   - **RESTORE AS ACTIVE**: Fix is missing from code, remove strikethrough

3. **IMMEDIATELY make the Edit tool call** - do NOT batch these for later

**Non-obvious behavior (warrants MOVE TO QUIRKS):**
- Uses unconventional lock ordering
- Has unusual error handling (warnings instead of errors, silent failures)
- Behavior differs from similar methods in the same service
- Performance characteristics worth noting

**NOT non-obvious (just DELETE):**
- Standard fix that makes behavior match expectations
- Bug was internal implementation detail users never saw
- Fix aligns with how other similar code works

**Example DELETE edit:**
```
Old: 1. ~~**Broken validation**~~: **FIXED** (2026-01-30) - Added null check.
New: (delete the entire line)
```

**Example MOVE TO QUIRKS edit:**
Delete from Bugs section:
```
Old: 1. ~~**MergeStacks locking**~~: **FIXED** (2026-01-30) - Now uses GUID ordering.
New: (delete the line)
```
Add to Intentional Quirks section:
```
N. **MergeStacks uses deterministic lock ordering**: Locks acquired in GUID order (smaller first) to prevent deadlocks.
```

**Work Tracking cleanup (MANDATORY):**
- DELETE all entries in "Completed" subsection that correspond to processed strikethrough items
- If Work Tracking becomes empty, leave just the section header
- Do NOT keep historical clutter - the git history preserves this

**After processing ALL strikethrough items, create this table:**
```markdown
### Strikethrough Items Processed

| Item | Location | Action Taken | Edit Made |
|------|----------|--------------|-----------|
| Broken validation | Bugs #1 | Deleted | Yes |
| MergeStacks locking | Bugs #2 | Moved to Quirks #5 | Yes |
| Store metrics | Potential Extensions #1 | Deleted | Yes |
```

**⛔ If the "Edit Made" column has any "No" entries, you have failed this phase.**

### Phase 6: Document Update (MANDATORY EDITS)

**⛔ This phase REQUIRES Edit tool calls. If you have nothing to edit, re-check your work.**

Based on Phases 2-5, make ALL necessary edits to the document:

**Structure fixes (use Edit tool):**
- Add missing sections with appropriate content
- Fix section ordering to match template
- Fix table formatting issues

**Content updates (use Edit tool):**
- Add missing dependencies/dependents
- Add missing state key patterns
- Add missing events
- Add missing configuration properties
- Update outdated information
- Remove obsolete information that no longer matches code

**Quirks updates (use Edit tool):**
- Add newly discovered bugs to "Bugs (Fix Immediately)"
- Add newly discovered quirks to appropriate section
- **Strikethrough items MUST already be processed in Phase 5b** - if any remain, go back
- Preserve all AUDIT markers exactly as they are (IN_PROGRESS, NEEDS_DESIGN, BLOCKED)

**Work Tracking section (use Edit tool):**
- Add section 14 if missing
- DELETE all "Completed" entries that correspond to processed strikethrough items
- Do NOT modify AUDIT markers (IN_PROGRESS, NEEDS_DESIGN, BLOCKED)

**⛔ CHECKPOINT before proceeding to Phase 7:**

Count your Edit tool calls in this session. If the count is ZERO and you found ANY of the following, STOP and go back:
- Strikethrough items (FIXED/IMPLEMENTED/MOVED)
- Missing sections
- Inaccurate content
- Outdated information
- Clutter in Work Tracking

A maintenance workflow with zero edits is almost always a failure.

### Phase 7: Quality Check (VERIFY EDITS WERE MADE)

**IMPORTANT: This is documentation maintenance only. Do NOT build code or run tests.**

Before finalizing, verify:

1. **⛔ MANDATORY: Verify edits were made:**
   - Count Edit tool calls made to the deep dive document in this session
   - If count is ZERO, **STOP** - you likely missed something, go back to Phase 5b/6
   - The only valid reason for zero edits is a perfect document (extremely rare)

2. **Verify NO strikethrough items remain:**
   - Search the document for `~~**` pattern
   - If ANY strikethrough items remain, **STOP** - go back to Phase 5b
   - Strikethrough items must be DELETED or MOVED, never left in place

3. **Check for marker preservation:**
   - Count AUDIT markers (IN_PROGRESS, NEEDS_DESIGN, BLOCKED) before and after
   - Must be equal (or more if you added new work tracking section)
   - These are the ONLY markers that should remain

4. **Verify Work Tracking is clean:**
   - "Completed" subsection should NOT contain entries for items you just processed
   - Historical clutter should be removed

5. **Verify all tables are valid markdown:**
   - Consistent column counts
   - Proper alignment syntax
   - No broken pipe characters

### Phase 8: Report

**⛔ Before generating this report, verify:**
- You made Edit tool calls to the document (check your tool call history)
- No strikethrough items remain in the document
- Work Tracking "Completed" section is cleaned up

**Final Report Format:**
```markdown
## Maintenance Complete: {Plugin Name}

### Summary
- **Edit tool calls made:** {count} ← MUST be > 0 unless document was perfect
- **Strikethrough items found:** {count}
- **Strikethrough items processed:** {count} ← MUST equal found count
- **AUDIT markers preserved:** {count}

### Edits Made (REQUIRED - list actual edits)

| Edit # | Section | Change |
|--------|---------|--------|
| 1 | Bugs | Deleted line: "~~**Broken validation**~~: **FIXED**..." |
| 2 | Quirks | Added: "**Lock ordering behavior**: ..." |
| 3 | Work Tracking | Deleted 3 completed entries |

### Strikethrough Items Processed

| Item | Original Location | Action | New Location (if moved) |
|------|-------------------|--------|-------------------------|
| Broken validation | Bugs #1 | Deleted | N/A |
| Lock ordering | Bugs #2 | Moved | Quirks #5 |
| Store metrics | Potential Extensions #1 | Deleted | N/A |

### Content Corrections (if any)
{List any factual corrections made to match source code}

### Document Status
- Template compliance: {Full/Partial}
- Strikethrough items remaining: **0** ← MUST be 0
- Ready for /audit-plugin: {Yes/No}
```

**⛔ WORKFLOW FAILURE CONDITIONS:**
- "Edit tool calls made: 0" with strikethrough items found > 0
- "Strikethrough items processed" < "Strikethrough items found"
- "Strikethrough items remaining" > 0
- Empty "Edits Made" table when changes were needed

If any of these conditions are true, DO NOT report success. Go back and fix.

## Important Guidelines

### MUST DO (workflow fails without these):
- **EDIT the document** - verification without editing is failure
- **DELETE all strikethrough items** (FIXED/IMPLEMENTED/MOVED) - none may remain
- **CLEAN UP Work Tracking** - remove processed "Completed" entries
- Read every line of service code
- Verify claims against actual source
- Preserve AUDIT markers (IN_PROGRESS, NEEDS_DESIGN, BLOCKED)

### MUST NOT DO (these are workflow failures):
- **Leave strikethrough items in document** - they must be deleted or moved
- **Report "0 edits" when strikethrough items exist** - you missed the point
- **Say "Already documented" without editing** - if it's marked FIXED, delete the mark
- **Verify without acting** - verification is step 1, editing is step 2
- Remove AUDIT markers (IN_PROGRESS, NEEDS_DESIGN, BLOCKED)
- Guess at behavior without reading code
- Add speculative information

### Common Failures to Avoid:
- ❌ "I verified the fix exists" → but didn't delete the FIXED line
- ❌ "Already in Work Tracking" → Work Tracking should be cleaned up too
- ❌ "All content accurate" → but left 5 strikethrough items in place
- ❌ "No changes needed" → when document has FIXED/IMPLEMENTED markers
- ❌ Generating a report without making any Edit tool calls

### When Uncertain:
- If behavior is unclear, note it as a quirk with "Needs investigation"
- If fix status is unclear, leave AUDIT markers in place (but still delete FIXED markers)
- If section content is ambiguous, add a comment like `<!-- TODO: verify X -->`

## Error Recovery

If you encounter issues:

1. **Can't find service files:** Check for typos, verify plugin exists
2. **Schema missing:** Plugin may use shared schema or be infrastructure-only
3. **Tests missing:** Note in doc that test coverage is lacking
4. **Build fails:** Do not proceed with updates until build is fixed
5. **Conflicting information:** Prefer source code over existing documentation
6. **Made zero edits but found strikethrough items:** GO BACK. You failed Phase 5b. Strikethrough items MUST be deleted or moved using the Edit tool. "Verifying" them is not enough.
7. **Reported "no changes needed" but document has FIXED/IMPLEMENTED markers:** GO BACK. These markers are instructions to delete/move the lines, not to verify and leave in place.
8. **Work Tracking has long "Completed" section:** DELETE entries corresponding to strikethrough items you processed. Don't preserve clutter.
