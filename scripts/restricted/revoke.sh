#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────
# Revoke write permissions granted via grant.sh.
# Writes a bucket-specific sentinel file that the MCP server picks up.
#
# Usage:
#   ./scripts/restricted/revoke.sh <bucket>              # Revoke ALL grants
#   ./scripts/restricted/revoke.sh <bucket> reference     # Revoke specific
#   ./scripts/restricted/revoke.sh <bucket> scripts hooks # Revoke multiple
#
# Stream Deck: "REVOKE ALL" button runs this with bucket + no category.
# ─────────────────────────────────────────────────────────────────────────

set -euo pipefail

# Category → prefix mapping (must match grant.sh)
declare -A CATEGORIES=(
  [reference]="docs/reference/"
  [scripts]="scripts/"
  [structural]="structural-tests/"
  [test-utils]="test-utilities/"
  [hooks]=".claude/hooks/"
  [skills]=".claude/skills/"
  [settings]=".claude/settings.json"
  [agents]=".claude/agents/"
)

ALL_PREFIXES=()
for prefix in "${CATEGORIES[@]}"; do
  ALL_PREFIXES+=("$prefix")
done

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 <bucket> [category ...]"
  echo "Buckets: 1-5. No category = revoke all."
  exit 1
fi

BUCKET="$1"
shift
SENTINEL="/tmp/bannou-mcp-inject-${BUCKET}.json"

if [[ ! "$BUCKET" =~ ^[1-5]$ ]]; then
  echo "Error: Bucket must be 1-5, got '$BUCKET'"
  exit 1
fi

# Build the prefixes to revoke
if [[ $# -eq 0 ]]; then
  PREFIXES=("${ALL_PREFIXES[@]}")
  MSG="All write permissions revoked"
else
  PREFIXES=()
  for arg in "$@"; do
    if [[ -v "CATEGORIES[$arg]" ]]; then
      PREFIXES+=("${CATEGORIES[$arg]}")
    else
      echo "Warning: Unknown category '$arg', skipping"
    fi
  done
  if [[ ${#PREFIXES[@]} -eq 0 ]]; then
    echo "Error: No valid categories specified"
    exit 1
  fi
  MSG="Revoked: ${PREFIXES[*]}"
fi

JSON_PREFIXES=$(printf '%s\n' "${PREFIXES[@]}" | jq -R . | jq -s .)

jq -n \
  --argjson revokes "$JSON_PREFIXES" \
  --arg message "$MSG" \
  '{revokePermissions: $revokes, message: $message}' \
  > "$SENTINEL"

echo "🔒 Bucket $BUCKET — Revoked: ${PREFIXES[*]}"
