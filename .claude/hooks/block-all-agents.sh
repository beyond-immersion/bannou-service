#!/bin/bash
#
# block-all-agents.sh
#
# PreToolUse hook that blocks Agent tool calls EXCEPT for approved
# project-aware agent types that have restricted tool access.
#
# ALLOWED agent types (project-aware, safe tool sets):
#   - bannou            — General project awareness (Read, Grep, Glob, Bash)
#   - bannou-dev        — Tenets + implementation patterns (Read, Grep, Glob, Bash)
#   - bannou-schema     — Schema rules + specifications (Read, Grep, Glob, Bash)
#   - bannou-code-reviewer — Read-only review (LS, Read only)
#
# BLOCKED: ALL other agent types (general-purpose, Explore, Plan,
#   feature-dev:*, doc-reviewer, gap-investigator, etc.)
#
# INCIDENT LOG:
# 2026-03-18: During /map-plugin genesis, Claude launched an Explore agent
#   to "read all 12 dependency implementation maps and extract CALL signatures."
#   The agent's lossy summary was used to write implementation code with
#   incorrect API signatures, wasting an entire implementation session.
# 2026-03-19: During /implement-plugin genesis, Claude self-performed the
#   coverage audit instead of delegating to an agent (agents were fully blocked).
#   The self-audit was a rubber stamp — every checkbox was checked without
#   actually tracing method control flow against T7 compensation rules.
#   DestroyEntityCoreAsync had a clear T7 Strategy 3 violation (fallible calls
#   swallowed, then entity record deleted unconditionally) that went undetected
#   through 3 rounds of user correction.
#

# Read the hook input from stdin
input=$(cat)

tool_name=$(echo "$input" | jq -r '.tool_name // ""' 2>/dev/null)

if [[ "$tool_name" == "Agent" ]]; then
    # Extract the subagent_type from the tool input
    subagent_type=$(echo "$input" | jq -r '.tool_input.subagent_type // ""' 2>/dev/null)

    # Allow the four approved project-aware agent types
    case "$subagent_type" in
        bannou|bannou-dev|bannou-schema|bannou-code-reviewer|bannou-worker)
            # Approved — allow through
            exit 0
            ;;
        *)
            # All other types blocked
            jq -n '{
                hookSpecificOutput: {
                    hookEventName: "PreToolUse",
                    permissionDecision: "deny",
                    permissionDecisionReason: "BLOCKED: Agent tool is FORBIDDEN.\n\nYou MUST do your own reading. Delegating to agents means the content\nlands in the agent context, not yours. Use Read, Grep, Glob, and Bash\ndirectly. There are no exceptions.\n\nINCIDENT: 2026-03-18 — Agent launched to read 12 dependency maps\nduring /map-plugin. Lossy summaries corrupted the entire session."
                }
            }'
            exit 0
            ;;
    esac
fi

# Allow everything else
exit 0
