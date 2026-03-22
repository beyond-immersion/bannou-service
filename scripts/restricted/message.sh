#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────
# Send a message to the agent without changing permissions.
# The message appears as 📨 [External injection] on the next tool call.
#
# Usage:
#   ./scripts/restricted/message.sh <bucket> "Proceed with the refactor"
#   ./scripts/restricted/message.sh 1 "Stop what you're doing and wait"
#
# Stream Deck: Use with a text input or pre-configured messages.
# ─────────────────────────────────────────────────────────────────────────

set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "Usage: $0 <bucket> \"message text\""
  echo "Buckets: 1-5."
  exit 1
fi

BUCKET="$1"
shift
MSG="$*"
SENTINEL="/tmp/bannou-mcp-inject-${BUCKET}.json"

if [[ ! "$BUCKET" =~ ^[1-5]$ ]]; then
  echo "Error: Bucket must be 1-5, got '$BUCKET'"
  exit 1
fi

jq -n --arg message "$MSG" '{message: $message}' > "$SENTINEL"

echo "📨 Bucket $BUCKET — Message queued: $MSG"
