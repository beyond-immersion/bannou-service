#!/bin/bash
#
# block-read-limit.sh
#
# PreToolUse hook that blocks Read tool calls with limit or offset parameters.
#
# Claude MUST read full files. Using limit/offset means Claude is skipping
# content, which leads to incomplete understanding and incorrect downstream
# work. If a file is too large, the Read tool handles pagination internally
# (persisted output). Claude never needs to manually paginate.
#
# The ONLY acceptable use of limit/offset is when explicitly instructed by
# the user in-conversation (e.g., "read lines 500-600 of that file").
#
# INCIDENT LOG:
# 2026-03-18: During /map-plugin genesis, Claude progressively reduced the
#   limit parameter on Read calls from full files down to 80 lines as it
#   read 12 dependency maps. 6 of 12 files were read partially. Claude
#   presented the work as complete without disclosing the partial reads.
#   The failure was silent — limit parameters are visible in tool calls
#   but Claude never announced it was cutting corners.
#
# BLOCKED:
#   Read tool calls with "limit" parameter set
#   Read tool calls with "offset" parameter set
#

# Read the hook input from stdin
input=$(cat)

tool_name=$(echo "$input" | jq -r '.tool_name // ""' 2>/dev/null)

if [[ "$tool_name" == "Read" ]]; then
    limit=$(echo "$input" | jq -r '.tool_input.limit // empty' 2>/dev/null)
    offset=$(echo "$input" | jq -r '.tool_input.offset // empty' 2>/dev/null)

    if [[ -n "$limit" ]]; then
        jq -n '{
            hookSpecificOutput: {
                hookEventName: "PreToolUse",
                permissionDecision: "allow",
                permissionDecisionReason: "You are using a limit parameter on Read. Read full files — you have a 1M token context window. The Read tool handles files over 2000 lines via pagination automatically."
            }
        }'
        exit 0
    fi

    if [[ -n "$offset" ]]; then
        jq -n '{
            hookSpecificOutput: {
                hookEventName: "PreToolUse",
                permissionDecision: "allow",
                permissionDecisionReason: "You are using an offset parameter on Read. Read full files from the beginning. The Read tool handles large files via pagination automatically."
            }
        }'
        exit 0
    fi
fi

# Allow everything else
exit 0
