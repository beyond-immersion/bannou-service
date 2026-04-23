#!/bin/bash
#
# stub-reminder.sh
#
# PreToolUse hook (Edit|Write) that gently reminds the agent to reconsider
# when writing code containing "stub" language in source files.
#
# Does NOT block — just nudges re-evaluation. "stub" appears legitimately
# in pre-implementation services, comments about existing stubs, etc.
#
# WHY: An agent wrote empty no-op handlers to make structural tests pass,
# corrupting the test signal. Structural test failures are implementation
# gaps — the fix is implementing the logic, not hiding the gap.

# Read the hook input from stdin
input=$(cat)

# Extract file path and new content
file_path=$(echo "$input" | jq -r '.tool_input.file_path // .tool_input.filePath // ""' 2>/dev/null)
new_content=$(echo "$input" | jq -r '.tool_input.content // .tool_input.new_string // ""' 2>/dev/null)

# Only check source code files
if [[ ! "$file_path" =~ \.(cs|yaml)$ ]]; then
    exit 0
fi

# Skip Generated/ directories
if [[ "$file_path" =~ /Generated/ ]]; then
    exit 0
fi

# Check for stub language in the new content (case-insensitive)
if echo "$new_content" | grep -qiE '\bstubs?\b'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "allow",
            permissionDecisionReason: "This edit contains the word \"stub\". Quick check: are you writing an empty handler to make a test pass? If so, stop and implement the real logic instead. Structural test failures are implementation gaps. If this is a legitimate reference to pre-implementation state, carry on."
        }
    }'
    exit 0
fi

# Allow silently
exit 0
