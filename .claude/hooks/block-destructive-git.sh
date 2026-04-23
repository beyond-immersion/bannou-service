#!/bin/bash
#
# block-destructive-git.sh
#
# PreToolUse hook that blocks destructive git commands.
# An agent previously used `git restore` to revert files without user approval,
# violating the FORBIDDEN DESTRUCTIVE COMMANDS policy in CLAUDE.md.
#
# BLOCKED COMMANDS:
#   git checkout (file paths) - Destroys uncommitted work
#   git restore              - Destroys uncommitted work (modern equivalent of checkout --)
#   git stash                - Hides changes that may be lost
#   git reset                - Can destroy commit history
#
# These commands require EXPLICIT user approval before use.

# Read the hook input from stdin
input=$(cat)

# Extract the command from the JSON input
command=$(echo "$input" | jq -r '.tool_input.command // ""' 2>/dev/null)

# If we couldn't parse the input or there's no command, allow it through
if [[ -z "$command" ]]; then
    exit 0
fi

# Block git restore (any form)
if echo "$command" | grep -qE '(^|[;&|])(\s*)git\s+restore(\s|$)'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "deny",
            permissionDecisionReason: "BLOCKED: git restore destroys uncommitted work.\n\nPer CLAUDE.md FORBIDDEN DESTRUCTIVE COMMANDS:\n- git restore is functionally identical to git checkout -- and MUST NOT be used without explicit user approval.\n- \"Since this was my own damage\" is NOT an acceptable justification.\n\nIF YOU CAUSED THE PROBLEM:\n1. STOP and tell the user what happened\n2. Explain what you need to undo and why\n3. Wait for explicit approval\n4. Use the least destructive method possible"
        }
    }'
    exit 0
fi

# Block git checkout when used to discard changes (with -- or file paths)
# Allow: git checkout <branch>, git checkout -b <branch>
# Block: git checkout -- <file>, git checkout ., git checkout <file>
if echo "$command" | grep -qE '(^|[;&|])(\s*)git\s+checkout\s+--\s'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "deny",
            permissionDecisionReason: "BLOCKED: git checkout -- destroys uncommitted work.\n\nPer CLAUDE.md FORBIDDEN DESTRUCTIVE COMMANDS:\n- git checkout is ABSOLUTELY FORBIDDEN without explicit user approval.\n\nAsk the user before discarding any changes."
        }
    }'
    exit 0
fi

# Block git checkout . (discard all changes)
if echo "$command" | grep -qE '(^|[;&|])(\s*)git\s+checkout\s+\.(\s|$)'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "deny",
            permissionDecisionReason: "BLOCKED: git checkout . destroys ALL uncommitted work.\n\nPer CLAUDE.md FORBIDDEN DESTRUCTIVE COMMANDS:\n- git checkout is ABSOLUTELY FORBIDDEN without explicit user approval."
        }
    }'
    exit 0
fi

# Block git stash (any form)
if echo "$command" | grep -qE '(^|[;&|])(\s*)git\s+stash(\s|$)'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "deny",
            permissionDecisionReason: "BLOCKED: git stash hides changes that may be lost.\n\nPer CLAUDE.md FORBIDDEN DESTRUCTIVE COMMANDS:\n- git stash is ABSOLUTELY FORBIDDEN without explicit user approval.\n\nAsk the user before stashing any changes."
        }
    }'
    exit 0
fi

# Block git reset (any form)
if echo "$command" | grep -qE '(^|[;&|])(\s*)git\s+reset(\s|$)'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "deny",
            permissionDecisionReason: "BLOCKED: git reset can destroy commit history.\n\nPer CLAUDE.md FORBIDDEN DESTRUCTIVE COMMANDS:\n- git reset is ABSOLUTELY FORBIDDEN without explicit user approval.\n\nAsk the user before resetting any commits or staging."
        }
    }'
    exit 0
fi

# Block git clean (removes untracked files)
if echo "$command" | grep -qE '(^|[;&|])(\s*)git\s+clean(\s|$)'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "deny",
            permissionDecisionReason: "BLOCKED: git clean removes untracked files permanently.\n\nThis is a destructive command that MUST NOT be used without explicit user approval."
        }
    }'
    exit 0
fi

# Block xargs git restore (the exact pattern that triggered this hook's creation)
if echo "$command" | grep -qE 'xargs\s+git\s+restore'; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "deny",
            permissionDecisionReason: "BLOCKED: Piping file lists to git restore destroys uncommitted work at scale.\n\nThis is the exact pattern that caused this hook to be created. STOP."
        }
    }'
    exit 0
fi

# Command is safe, allow it through
exit 0
