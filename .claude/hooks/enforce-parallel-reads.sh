#!/bin/bash
#
# enforce-parallel-reads.sh
#
# PreToolUse hook that blocks ALL non-read tool calls while a parallel read
# gate is active. Works in tandem with track-parallel-reads.sh (PostToolUse)
# which counts completed reads. Allows both built-in Read (parent sessions)
# and MCP read_file (agents) through the gate.
#
# HOW IT WORKS:
#   1. A skill writes the expected read count to /tmp/.parallel-read-expected
#   2. track-parallel-reads.sh (PostToolUse) appends one character to
#      /tmp/.parallel-read-token per completed Read call
#   3. This hook (PreToolUse) blocks ALL non-Read tools until the token
#      length matches the expected count
#   4. Once the count is met, the expectation file is removed and the gate opens
#
# ACTIVATION:
#   echo 16 > /tmp/.parallel-read-expected
#   rm -f /tmp/.parallel-read-token
#
# DEACTIVATION:
#   Automatic when read count is met.
#   Manual: rm -f /tmp/.parallel-read-expected
#
# WHY THIS EXISTS:
#   The model has a behavioral tendency to serialize Read calls across multiple
#   messages and interleave other work (questions, analysis, Bash commands)
#   between reads. Instructions alone do not prevent this. This hook makes
#   non-compliance physically impossible — every non-Read tool call is rejected
#   until all reads complete.
#

# Read the hook input from stdin
input=$(cat)

tool_name=$(echo "$input" | jq -r '.tool_name // ""' 2>/dev/null)

# If no expectation file exists, the gate is not active — allow everything
[[ ! -f /tmp/.parallel-read-expected ]] && exit 0

# Read calls are always allowed — they're accumulating the token
# Both built-in Read (parent sessions) and MCP read_file (agents) pass through
[[ "$tool_name" == "Read" || "$tool_name" == "mcp__bannou-read__read_file" ]] && exit 0

# Check current count vs expected
expected=$(cat /tmp/.parallel-read-expected 2>/dev/null)
[[ -z "$expected" || "$expected" == "0" ]] && exit 0

current=0
if [[ -f /tmp/.parallel-read-token ]]; then
    current=$(wc -c < /tmp/.parallel-read-token | tr -d ' ')
fi

# If count not met, block the non-Read tool
if [[ "$current" -lt "$expected" ]]; then
    remaining=$((expected - current))
    jq -n \
        --arg expected "$expected" \
        --arg current "$current" \
        --arg remaining "$remaining" \
        --arg tool "$tool_name" \
    '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "block",
            permissionDecisionReason: ("PARALLEL READ GATE: " + $remaining + " of " + $expected + " reads remaining. All tools except read_file are blocked until every read completes. Issue your remaining " + $remaining + " read_file calls now. (Attempted: " + $tool + ")")
        }
    }'
    exit 0
fi

# Count met — clean up the expectation file so the gate opens
rm -f /tmp/.parallel-read-expected

exit 0
