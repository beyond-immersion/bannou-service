#!/bin/bash
#
# block-worktree-isolation.sh
#
# PreToolUse hook that blocks Agent tool calls with isolation: "worktree".
#
# Worktree isolation causes agents to work on invisible branches that the user
# cannot see, access, or interact with. Changes made in worktrees are silently
# lost or split across locations, creating split-brain states where some work
# lands on the main branch and some is invisible. Multiple agents editing the
# same file in separate worktrees guarantees merge conflicts.
#
# There is NEVER a valid use case for worktree isolation in this project.
#
# INCIDENT LOG:
# 2026-03-11: Three agents launched with isolation: "worktree" for tasks that
#   all edited structural-tests/StructuralTests.cs. One agent wrote 5 of 7
#   service file changes ONLY to the worktree — invisible on the main branch.
#   Structural tests on the main branch reference interfaces that only exist
#   in the worktree. Work was effectively lost.
#
# BLOCKED:
#   Agent tool calls with isolation: "worktree"
#   EnterWorktree tool calls (any form)
#

# Read the hook input from stdin
input=$(cat)

# Extract the tool name
tool_name=$(echo "$input" | jq -r '.tool_name // ""' 2>/dev/null)

# Block EnterWorktree tool entirely
if [[ "$tool_name" == "EnterWorktree" ]]; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "deny",
            permissionDecisionReason: "BLOCKED: EnterWorktree is ABSOLUTELY FORBIDDEN.\n\nWorktree isolation creates invisible branches where work is silently lost.\nThere is NEVER a valid use case for worktrees in this project.\n\nAll work MUST happen on the current branch in the main working directory."
        }
    }'
    exit 0
fi

# Block Agent tool with isolation: "worktree"
if [[ "$tool_name" == "Agent" ]]; then
    isolation=$(echo "$input" | jq -r '.tool_input.isolation // ""' 2>/dev/null)
    if [[ "$isolation" == "worktree" ]]; then
        jq -n '{
            hookSpecificOutput: {
                hookEventName: "PreToolUse",
                permissionDecision: "deny",
                permissionDecisionReason: "BLOCKED: Agent isolation: \"worktree\" is ABSOLUTELY FORBIDDEN.\n\nWorktree isolation causes agents to work on invisible branches.\nChanges are split between the main repo and the worktree, creating\nsplit-brain states where work is silently lost.\n\nAll agents MUST work directly on the current branch.\nRemove the isolation parameter and relaunch."
            }
        }'
        exit 0
    fi
fi

# Allow everything else
exit 0
