#!/bin/bash
#
# block-symlinks.sh
#
# PreToolUse hook that blocks symbolic link creation.
# Symlinks can break containerized builds, cause circular references,
# and create subtle path resolution issues in the generation pipeline.
#
# RULE: NEVER create symlinks. Use proper file references or copies instead.

# Read the hook input from stdin
input=$(cat)

# Extract the command from the JSON input
command=$(echo "$input" | jq -r '.tool_input.command // ""' 2>/dev/null)

# If we couldn't parse the input or there's no command, allow it through
if [[ -z "$command" ]]; then
    exit 0
fi

# Block ln -s / ln --symbolic commands (symlink creation)
# Matches: ln -s, ln --symbolic, ln -sf, ln -sfn, ln -sv, etc.
if echo "$command" | grep -qE '(^|[;&|]\s*)ln\s+(-[a-zA-Z]*s|-{2}symbolic)'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "deny",
            permissionDecisionReason: "BLOCKED: Creating symbolic links is FORBIDDEN.\n\nSymlinks cause problems in containerized builds, break path resolution in the code generation pipeline, and create subtle cross-platform issues.\n\nInstead of symlinks:\n- Use proper file references (imports, $ref in schemas)\n- Copy the file if a duplicate is truly needed\n- Reference the canonical location directly"
        }
    }'
    exit 0
fi

# Command is safe, allow it through
exit 0
