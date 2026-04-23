#!/bin/bash
#
# git-history-reminder.sh
#
# PreToolUse hook that triggers on git diff or git log commands.
# Does NOT block - just reminds the agent to re-read CLAUDE.md
# and acknowledge the rules before proceeding.
#
# WHY: An agent used git diff to confirm it had successfully reverted
# work that was dictated by the user, violating HARD STOP rules.
# Every git diff/log call is a checkpoint to re-read instructions.

# Read the hook input from stdin
input=$(cat)

# Extract the command from the JSON input
command=$(echo "$input" | jq -r '.tool_input.command // ""' 2>/dev/null)

# If we couldn't parse the input or there's no command, allow it through
if [[ -z "$command" ]]; then
    exit 0
fi

# Check if the command contains git diff or git log
if echo "$command" | grep -qE '\bgit\s+(diff|log)\b'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "allow",
            permissionDecisionReason: "You are reviewing git history. Use this to understand changes, not to justify reverting or undoing completed work. If something looks wrong, present it to the user rather than acting on it."
        }
    }'
    exit 0
fi

# Allow everything else
exit 0
