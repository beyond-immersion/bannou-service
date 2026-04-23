#!/bin/bash
#
# track-parallel-reads.sh
#
# PostToolUse hook on Read/read_file that appends one random character to a token file.
# Used by skills to verify parallel reading compliance.
#
# HOW IT WORKS:
#   Every read call (built-in Read or MCP read_file) appends one random alphanumeric
#   character to /tmp/.parallel-read-token. If 15 reads fire, the file contains 15
#   characters. Skills clear the file before their parallel read phase, then check
#   its length after. The model must report the token to prove it completed the reads.
#
# This hook does NOT block or notify. It silently accumulates the token.
#

# Read the hook input from stdin
input=$(cat)

tool_name=$(echo "$input" | jq -r '.tool_name // ""' 2>/dev/null)

if [[ "$tool_name" == "Read" || "$tool_name" == "mcp__bannou-read__read_file" ]]; then
    # Append one random alphanumeric character
    char=$(head -c 100 /dev/urandom | tr -dc 'a-z0-9' | head -c 1)
    echo -n "$char" >> /tmp/.parallel-read-token
fi

# Always allow — this is a silent tracker
exit 0
