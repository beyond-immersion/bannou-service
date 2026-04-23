#!/bin/bash
#
# frozen-file-check.sh
#
# PreToolUse hook that fires on Edit and Write tools.
# Two safety checks before a file is modified:
#
#   1. Frozen directory check — is the target in a restricted path?
#   2. Test weakening check — does the edit introduce patterns that
#      typically indicate a test was weakened to pass?
#
# Both are SOFT reminders (permissionDecision: "allow"), not blocks.
# Claude self-assesses and proceeds or stops accordingly.
#
# HISTORY:
#   Originally the frozen artifact check was PostToolUse (fire after
#   edit, demand reversal). Moved to PreToolUse so Claude can assess
#   authorization BEFORE editing, avoiding the disruptive "undo what
#   you just did" pattern. Test weakening check migrated from
#   post-edit-reminder.sh for the same reason.
#

# Read the hook input from stdin
input=$(cat)

# Extract file path from tool_input
file_path=$(echo "$input" | jq -r '.tool_input.file_path // .tool_input.filePath // ""' 2>/dev/null)

if [[ -z "$file_path" ]]; then
    exit 0
fi

# ── Check 1: Frozen directory ─────────────────────────────────────
if echo "$file_path" | grep -qE '/(scripts|structural-tests|test-utilities)/|/docs/reference/|/\.claude/(hooks|commands)/|/\.claude/settings\.json$'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "allow",
            permissionDecisionReason: "This file is in a frozen directory. Frozen files are modified only with explicit in-conversation user instruction. If the user asked you to modify this file, proceed. If not, stop and ask before editing."
        }
    }'
    exit 0
fi

# ── Check 2: Test file weakening patterns ─────────────────────────
if echo "$file_path" | grep -qE 'Tests?\.cs$'; then
    # Extract the new content being written
    edit_content=$(echo "$input" | jq -r '.tool_input.new_string // .tool_input.content // ""' 2>/dev/null)

    if echo "$edit_content" | grep -qiE 'Times\.(AtLeastOnce|AtMost|Between)|It\.IsAny<|Skip|Pending|AllowedViolation|KnownIssue|\?\? |fallback|workaround'; then
        jq -n '{
            hookSpecificOutput: {
                hookEventName: "PreToolUse",
                permissionDecision: "allow",
                permissionDecisionReason: "This edit to a test file contains patterns that sometimes indicate test weakening (Times.AtLeastOnce, It.IsAny, Skip, AllowedViolation, fallback, workaround). A failing test with correct assertions means the implementation needs fixing, not the test. Verify this edit maintains or strengthens test coverage before proceeding."
            }
        }'
        exit 0
    fi
fi

# No concerns — allow silently
exit 0
