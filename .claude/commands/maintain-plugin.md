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

## Critical Rules: Markers and Fixed Items

### Preserve AUDIT Markers (Active Work)

**NEVER remove or modify these HTML comment markers:**
```markdown
<!-- AUDIT:IN_PROGRESS:... -->
<!-- AUDIT:NEEDS_DESIGN:...:https://... -->
<!-- AUDIT:BLOCKED:... -->
```

These are managed by `/audit-plugin` and represent active work tracking.

### Process FIXED Items (Completed Work)

**ALWAYS verify and process these inline markers:**
```markdown
N. ~~**Original title**~~: **FIXED** (YYYY-MM-DD) - Description of fix.
```

These were completed by `/audit-plugin` and need verification:
1. Verify the fix exists in source code
2. If fix has non-obvious behavior → Move to "Intentional Quirks"
3. If fix is clean with no quirks → Remove entirely
4. If fix is missing (bug still exists!) → Restore as active bug

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

Compare the deep dive document against source code findings:

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

Systematically review the code for issues. Check for:

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

**Compare findings against existing doc sections:**
- Are all known bugs still present in code? (remove fixed ones)
- Are there new bugs not yet documented?
- Are "intentional quirks" actually intentional? (check comments/tests)
- Are "design considerations" still relevant?

### Phase 5b: Verify Fixed Items

Scan for items marked with the `/audit-plugin` FIXED format:

```markdown
N. ~~**Original title**~~: **FIXED** (YYYY-MM-DD) - Description of the fix.
```

**For each FIXED item, you MUST:**

1. **Verify the fix exists in code**
   - Read the relevant source code
   - Confirm the described fix is actually implemented
   - If the fix is NOT present, remove the FIXED marker and restore it as an active bug

2. **Assess if non-obvious behavior remains**
   Ask: "Would a developer using this API be surprised by the current behavior?"

   **Non-obvious behavior examples:**
   - Uses unconventional lock ordering (alphabetical, GUID-based, etc.)
   - Has unusual error handling (warnings instead of errors, silent failures)
   - Modifies state in unexpected places
   - Has performance characteristics worth noting
   - Behavior differs from similar methods in the same service

   **NOT non-obvious (just remove entirely):**
   - Standard fix that makes behavior match expectations
   - Bug was internal implementation detail users never saw
   - Fix aligns with how other similar code works

3. **Take action:**

   **If fix verified AND no non-obvious behavior remains:**
   - DELETE the entire line from the Bugs section
   - This keeps the doc clean - fixed bugs with standard behavior don't need documentation

   **If fix verified AND non-obvious behavior remains:**
   - MOVE to "Intentional Quirks (Documented Behavior)" section
   - REWRITE as a quirk (not a bug), explaining the current behavior:
   ```markdown
   N. **Descriptive title of current behavior**: {Explanation of what happens and why it matters to developers}
   ```
   - Example transformation:
     - Before (in Bugs): `~~**MergeStacks only locks source container**~~: **FIXED** (2026-01-30) - Now locks both containers using deterministic GUID ordering.`
     - After (in Quirks): `**MergeStacks uses deterministic lock ordering**: When merging stacks across containers, locks are acquired in GUID order (smaller first) to prevent deadlocks. Operations may briefly conflict if another operation is locking in the opposite order.`

4. **Update Work Tracking:**
   - Move the "Completed" entry from Work Tracking to a "Historical" subsection if you want to preserve history
   - Or simply remove it if the doc is getting cluttered

**FIXED Item Verification Report Format:**
```markdown
### FIXED Item Verification

| Item | Fix Verified | Has Quirks | Action |
|------|-------------|------------|--------|
| MergeStacks locking | Yes | Yes (lock ordering) | Move to Quirks |
| SplitStack validation | Yes | No | Remove |
| Cache TTL bug | No (still broken!) | N/A | Restore as Bug |
```

### Phase 6: Document Update

Based on Phases 2-5, update the document:

**Structure fixes:**
- Add missing sections with appropriate content
- Fix section ordering to match template
- Fix table formatting issues

**Content updates:**
- Add missing dependencies/dependents
- Add missing state key patterns
- Add missing events
- Add missing configuration properties
- Update outdated information

**Quirks updates:**
- Add newly discovered bugs to "Bugs (Fix Immediately)"
- Add newly discovered quirks to appropriate section
- Process FIXED items per Phase 5b (remove clean fixes, move quirky fixes to Intentional Quirks)
- Preserve all AUDIT markers exactly as they are (IN_PROGRESS, NEEDS_DESIGN, BLOCKED)

**Work Tracking section:**
- Add section 14 if missing
- Do NOT modify existing markers
- If items with markers have been fixed, note this but do not remove marker (let audit-plugin handle it)

### Phase 7: Quality Check

**IMPORTANT: This is documentation maintenance only. Do NOT build code or run tests.**
All builds and tests are assumed passing. You are only updating the markdown documentation.

Before finalizing, verify:

1. **Check for marker preservation:**
   - Count AUDIT markers before and after
   - Must be equal (or more if you added new work tracking section)

2. **Verify all tables are valid markdown:**
   - Consistent column counts
   - Proper alignment syntax
   - No broken pipe characters

### Phase 8: Report

**Final Report Format:**
```markdown
## Maintenance Complete: {Plugin Name}

### Summary
- **Sections added:** {count}
- **Sections updated:** {count}
- **Bugs discovered:** {count}
- **Quirks documented:** {count}
- **AUDIT markers preserved:** {count}
- **FIXED items processed:** {count}
  - Removed (clean fixes): {count}
  - Moved to Quirks: {count}
  - Restored as bugs (fix missing): {count}

### Changes Made

#### Structural
{List of structure changes}

#### Content
{List of content updates}

#### Quirks
{List of new items added to quirks sections}

#### FIXED Items Processed
| Item | Action | Notes |
|------|--------|-------|
| {title} | {Removed/Moved to Quirks/Restored} | {brief note} |

### Document Status
- Template compliance: {Full/Partial}
- Content accuracy: {Verified/Needs follow-up}
- Ready for /audit-plugin: {Yes/No}

### Recommendations
{Any follow-up actions needed}
```

## Important Guidelines

### DO:
- Read every line of service code
- Verify claims against actual source
- Add newly discovered issues
- Preserve existing AUDIT markers (IN_PROGRESS, NEEDS_DESIGN, BLOCKED)
- Process FIXED items by verifying the fix exists in code
- Move FIXED items with non-obvious behavior to Intentional Quirks
- Remove FIXED items that are clean fixes with no quirks
- Use tables for structured data

### DO NOT:
- Remove AUDIT markers (IN_PROGRESS, NEEDS_DESIGN, BLOCKED - ever!)
- Leave FIXED items unprocessed (they must be verified and either removed or moved)
- Guess at behavior without reading code
- Copy from generated docs verbatim
- Remove bug entries without verifying fix in actual code
- Add speculative information
- Skip reading test files

### When Uncertain:
- If behavior is unclear, note it as a quirk with "Needs investigation"
- If fix status is unclear, leave AUDIT markers in place
- If section content is ambiguous, add a comment like `<!-- TODO: verify X -->`

## Error Recovery

If you encounter issues:

1. **Can't find service files:** Check for typos, verify plugin exists
2. **Schema missing:** Plugin may use shared schema or be infrastructure-only
3. **Tests missing:** Note in doc that test coverage is lacking
4. **Build fails:** Do not proceed with updates until build is fixed
5. **Conflicting information:** Prefer source code over existing documentation
