#!/bin/bash
#
# block-agent-polling.sh
#
# PreToolUse hook that blocks Agent resume attempts.
#
# Claude repeatedly tried to resume still-running background agents,
# burning 20% of context on failed resume attempts that were guaranteed
# to fail. The automatic task-notification system delivers results when
# agents complete — resume is never needed.
#
# RULE: Background agent results arrive via <task-notification>.
# Read the results from the notification. If follow-up work is needed,
# launch a NEW agent with the relevant context. Do not resume.
#
# BLOCKED:
#   Agent tool calls with "resume" parameter set
#

# Read the hook input from stdin
input=$(cat)

# Extract the resume field from the Agent tool input
resume=$(echo "$input" | jq -r '.tool_input.resume // ""' 2>/dev/null)

# If no resume field, allow the call (it's a new agent launch)
if [[ -z "$resume" ]]; then
    exit 0
fi

# Block all Agent resume attempts
jq -n '{
    hookSpecificOutput: {
        hookEventName: "PreToolUse",
        permissionDecision: "deny",
        permissionDecisionReason: "BLOCKED: Agent resume is prohibited.\n\nBackground agents deliver results via <task-notification> automatically.\nYou do NOT need to resume agents to get their results.\n\nIf you need follow-up work on a completed agent'\''s results:\n1. Read the results from the <task-notification>\n2. Launch a NEW agent with the relevant context\n3. Do NOT attempt to resume the completed agent\n\nThis hook exists because Claude burned 20% of a session'\''s context\nrepeatedly attempting to resume still-running agents in a polling loop."
    }
}'
exit 0
