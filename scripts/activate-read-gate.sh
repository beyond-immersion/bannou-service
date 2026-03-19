#!/bin/bash
#
# activate-read-gate.sh
#
# Activates the parallel read gate, expecting a specified number of Read calls.
# Once activated, the enforce-parallel-reads.sh PreToolUse hook blocks ALL
# non-Read tool calls until the expected number of reads complete.
#
# Usage: scripts/activate-read-gate.sh <count>
#
# The companion PostToolUse hook (track-parallel-reads.sh) counts each Read.
# The PreToolUse hook (enforce-parallel-reads.sh) blocks everything else.
# When the count is met, the gate clears automatically.
#

count="${1:?Usage: scripts/activate-read-gate.sh <count>}"

if ! [[ "$count" =~ ^[0-9]+$ ]] || [[ "$count" -lt 1 ]]; then
    echo "ERROR: Count must be a positive integer, got: $count"
    exit 1
fi

echo "$count" > /tmp/.parallel-read-expected
rm -f /tmp/.parallel-read-token

echo "Read gate activated: expecting $count file reads."
echo "All non-Read tools are now blocked until all $count reads complete."
