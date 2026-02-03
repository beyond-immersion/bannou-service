---
description: Review GitHub issues referenced in a plugin's deep dive document. Verify if issues should be closed, updated, or are still active based on current codebase state.
argument-hint: "[plugin-name|random] - Plugin to review issues for (e.g., 'account', 'auth') or 'random' to pick one at random"
---

# Plugin Issue Maintainer Command

You are executing the `/maintain-issues` workflow. This is a focused review of GitHub issues referenced in a plugin's deep dive document.

## Purpose

This command ensures GitHub issues are:
1. **Still relevant** - The issue hasn't been fixed by recent changes
2. **Accurately described** - The issue description matches current code state
3. **Properly triaged** - Issues are closed, updated, or confirmed active

**IMPORTANT: This is ISSUE REVIEW, not implementation. Do NOT:**
- Run `dotnet build` or any build commands
- Run tests
- Make code changes
- Fix the issues yourself

You are reviewing issue validity against the codebase, not implementing fixes.

## Workflow Phases

### Phase 1: Plugin Selection

**If argument is "random" or no argument provided:**
Use bash for true randomness:
```bash
ls docs/plugins/*.md | grep -v DEEP_DIVE_TEMPLATE | shuf -n 1
```
Extract the plugin name from the result and announce: "Reviewing issues for: {plugin-name}" then proceed immediately.

**If specific plugin name provided:**
1. Verify `docs/plugins/{PLUGIN}.md` exists
2. If not found, list available plugins and ask user to choose
3. Once valid, proceed immediately

### Phase 2: Issue Discovery

Read the deep dive document (`docs/plugins/{PLUGIN}.md`) thoroughly.

**Look for GitHub issue references in these formats:**
- `#123` - Issue number shorthand
- `GH #123` - Explicit GitHub reference
- `Issue #123` - Issue reference
- `https://github.com/.../issues/123` - Full URL
- HTML comments: `<!-- AUDIT:NEEDS_DESIGN:...:https://github.com/.../issues/123 -->`
- References in "Work Tracking", "Known Quirks", "Bugs", "Design Considerations" sections

**Create an Issue Inventory:**
```markdown
## Issue Inventory

| Issue # | Section Found | Context in Doc |
|---------|---------------|----------------|
| #123 | Work Tracking | "Needs design decision" |
| #456 | Bugs | "Race condition in X" |
| #789 | Design Considerations | "Future: support Y" |
```

### Phase 3: Issue Status Check

For EACH issue found, use the GitHub CLI to check its current status:

```bash
gh issue view <number> --json title,state,body,labels,comments
```

**Categorize each issue:**

1. **CLOSED** - Issue is already closed in GitHub
   - Note when/why it was closed
   - Check if the doc reference should be removed

2. **OPEN - Potentially Fixed** - Issue is open but code may have fixed it
   - Requires Phase 4 verification

3. **OPEN - Still Active** - Issue is open and clearly still relevant
   - No action needed

4. **OPEN - Needs Update** - Issue is open but description is outdated
   - Identify what needs updating

### Phase 4: Codebase Verification

For each "OPEN - Potentially Fixed" issue, verify against the actual codebase.

**Read the relevant source files:**
```
plugins/lib-{service}/
├── {Service}Service.cs              # Primary implementation
├── {Service}ServiceEvents.cs        # Event handlers
├── Services/*.cs                    # Helper services
└── Generated/
    └── I{Service}Service.cs         # Interface

schemas/
├── {service}-api.yaml
├── {service}-events.yaml
└── {service}-configuration.yaml
```

**For each issue, determine:**
1. Does the code now handle the case the issue describes?
2. Is there a TODO/FIXME comment referencing this issue?
3. Does a test now cover the scenario?
4. Was a workaround implemented instead of a proper fix?

**Verification Report:**
```markdown
### Issue #123: "Race condition in CreateWidget"

**Status in GitHub:** Open
**Status in Code:** FIXED

**Evidence:**
- `WidgetService.cs:245` now uses `DistributedLock` around the read-modify-write
- Commit `abc1234` on 2026-01-15: "Fix race condition in widget creation"
- Test `WidgetServiceTests.CreateWidget_ConcurrentCalls_NoRace` validates fix

**Recommendation:** Close issue with comment referencing the fix
```

### Phase 5: Action Recommendations

For each issue, provide a clear recommendation:

**Close with Comment:**
```markdown
**Issue #123** - RECOMMEND CLOSE
- Reason: Fixed in `WidgetService.cs:245` using distributed lock
- Suggested close comment:
  > Fixed in commit abc1234. Widget creation now uses distributed locking
  > to prevent race conditions. See `WidgetServiceTests.CreateWidget_ConcurrentCalls_NoRace`.
```

**Update Description:**
```markdown
**Issue #456** - RECOMMEND UPDATE
- Current description mentions: "Affects all widget types"
- Code shows: Only affects `StackableWidget` type
- Suggested update:
  > Update scope: This only affects `StackableWidget` (other types use different code path)
```

**Simplify/Narrow Scope:**
```markdown
**Issue #789** - RECOMMEND SIMPLIFY
- Original: 5 sub-issues listed
- 3 of 5 are now fixed
- Suggest updating issue to track only remaining 2 items
```

**Confirm Active:**
```markdown
**Issue #999** - CONFIRMED ACTIVE
- Code still has the problem described
- Location: `WidgetService.cs:178` - same TODO comment exists
- No workaround or partial fix found
```

### Phase 6: Execute Actions (WITH USER APPROVAL)

**IMPORTANT: Ask user before making any changes to GitHub issues.**

Present all recommendations in a summary table, then ask:

```markdown
## Recommended Actions

| Issue | Current State | Recommendation | Ready? |
|-------|---------------|----------------|--------|
| #123 | Open | Close (fixed) | Yes |
| #456 | Open | Update scope | Yes |
| #789 | Open | Simplify | Yes |
| #999 | Open | No action | N/A |

Would you like me to:
1. Close issue #123 with the suggested comment?
2. Add a comment to #456 about the updated scope?
3. Update #789 to remove completed items?

Please confirm which actions to take (e.g., "1, 2" or "all" or "none").
```

**If user approves, execute using gh CLI:**

```bash
# Close an issue
gh issue close <number> --comment "Fixed in commit abc1234..."

# Add a comment
gh issue comment <number> --body "Update: This now only affects..."

# Edit issue body (if needed)
gh issue edit <number> --body "Updated description..."
```

### Phase 7: Document Updates (if needed)

After GitHub actions are taken, update the deep dive document to:

1. **Remove references to closed issues** - If an issue is closed and the fix is clean
2. **Update references** - If issue scope changed
3. **Add notes** - If a workaround was implemented instead of a full fix

Use Edit tool for document changes.

### Phase 8: Final Report

```markdown
## Issue Maintenance Complete: {Plugin Name}

### Summary
- **Issues found in document:** {count}
- **Already closed:** {count}
- **Closed during this review:** {count}
- **Updated during this review:** {count}
- **Still active:** {count}

### Actions Taken

| Issue | Action | Result |
|-------|--------|--------|
| #123 | Closed | Successfully closed with fix reference |
| #456 | Commented | Scope clarification added |
| #789 | N/A | User declined |

### Document Updates
- Removed reference to #123 from Work Tracking section
- Updated #456 description in Bugs section

### Remaining Active Issues
| Issue | Summary | Location in Doc |
|-------|---------|-----------------|
| #999 | Orphaned state cleanup | Bugs section |
```

## Important Guidelines

### MUST DO:
- Read the FULL deep dive document for issue references
- Check each issue's CURRENT status in GitHub before making assumptions
- Verify fixes in the ACTUAL codebase, not just based on doc claims
- Get user approval before closing/updating issues
- Provide evidence (file:line, commit hash) for fix claims

### MUST NOT DO:
- Close issues without user approval
- Assume an issue is fixed just because it's marked FIXED in the doc
- Make code changes to fix issues
- Create new issues (this is maintenance, not triage)
- Skip codebase verification for "potentially fixed" issues

### When Uncertain:
- If fix status is unclear, mark as "Needs Investigation" and don't recommend closing
- If issue scope is ambiguous, suggest a clarifying comment rather than closing
- When in doubt, leave the issue open - false closes are worse than delayed closes

## Error Recovery

1. **No issues found in doc:** Report "No GitHub issues referenced in {plugin} deep dive"
2. **Issue not found in GitHub:** It may have been moved/deleted - note this and skip
3. **Can't verify fix in code:** Don't recommend closing - mark as "Unable to verify"
4. **User declines all actions:** Report findings without taking action
