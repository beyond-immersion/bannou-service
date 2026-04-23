#!/bin/bash
#
# task-creation-reminder.sh
#
# PreToolUse hook that triggers on TaskCreate and TodoWrite.
# Does NOT block - just reminds the agent of the required format
# for violation/hardening task lists.
#
# WHY: Claude repeatedly created shallow task descriptions that
# forced implementers to re-read tenets, re-discover affected files,
# and re-derive the fix — duplicating hours of audit work. Tasks for
# tenet violations, schema rule issues, and code quality fixes MUST
# be self-contained: an implementer who has never seen the codebase
# should be able to execute the task from the description alone.

# Read the hook input from stdin
input=$(cat)

# Extract tool name
tool_name=$(echo "$input" | jq -r '.tool_name // ""' 2>/dev/null)

# If we couldn't parse the input, allow through
if [[ -z "$tool_name" ]]; then
    exit 0
fi

MESSAGE="REMINDER: If you are creating a task list for TENET violations, SCHEMA-RULES issues, or code quality/consistency fixes, each task MUST be self-contained and implementable without additional reads.

Required task description format:
1. VERBATIM TENET TEXT: Quote the exact rule being violated (from the tenet document)
2. AFFECTED FILES: Every file path and line number
3. BEFORE/AFTER CODE: Exact code snippets showing current state and required fix
4. WHAT NOT TO DO: Explicit constraints preventing common mistakes
5. SELF-CONTAINED: An implementer who has never seen the codebase or tenets should be able to execute from the description alone, with zero additional reads required

Use TaskCreate (not TodoWrite) for violation task lists — TaskCreate supports rich descriptions."

# Check which tool triggered this hook
if [[ "$tool_name" == "TodoWrite" ]]; then
    jq -n --arg msg "$MESSAGE" '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "allow",
            permissionDecisionReason: $msg
        }
    }'
    exit 0
fi

if [[ "$tool_name" == "TaskCreate" ]]; then
    jq -n --arg msg "$MESSAGE" '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "allow",
            permissionDecisionReason: $msg
        }
    }'
    exit 0
fi

# Allow everything else
exit 0
