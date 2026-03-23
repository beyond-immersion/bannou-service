#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────
# Grant temporary write permission to a frozen directory.
# Writes a bucket-specific sentinel file that the MCP server picks up.
#
# Usage:
#   ./scripts/restricted/grant.sh <bucket> <category> [message]
#   ./scripts/restricted/grant.sh 2 reference "Edit the tenets as discussed"
#   ./scripts/restricted/grant.sh 1 all
#
# Buckets: 1-5 (matches BANNOU_MCP_BUCKET env var per terminal)
#
# Categories:
#   reference   → docs/reference/        (tenets, schema rules, helpers)
#   scripts     → scripts/               (generation pipeline)
#   structural  → structural-tests/      (structural validators)
#   test-utils  → test-utilities/        (shared test infrastructure)
#   hooks       → .claude/hooks/         (enforcement hooks)
#   skills      → .claude/skills/        (skill definitions)
#   settings    → .claude/settings.json  (permission config)
#   agents      → .claude/agents/        (agent definitions)
#   all         → all of the above
#
# Stream Deck: Each button runs this script with bucket + category args.
# ─────────────────────────────────────────────────────────────────────────

set -euo pipefail

# Category → prefix mapping
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

usage() {
  echo "Usage: $0 <bucket> <category> [message]"
  echo ""
  echo "Buckets: 1-5 (matches BANNOU_MCP_BUCKET per terminal)"
  echo ""
  echo "Categories:"
  for key in "${!CATEGORIES[@]}"; do
    printf "  %-12s → %s\n" "$key" "${CATEGORIES[$key]}"
  done
  echo "  all          → all of the above"
  exit 1
}

if [[ $# -lt 2 ]]; then
  usage
fi

BUCKET="$1"
CATEGORY="$2"
MESSAGE="${3:-}"
SENTINEL="/tmp/bannou-mcp-inject-${BUCKET}.json"

if [[ ! "$BUCKET" =~ ^[1-5]$ ]]; then
  echo "Error: Bucket must be 1-5, got '$BUCKET'"
  exit 1
fi

# Build the prefixes array
if [[ "$CATEGORY" == "all" ]]; then
  PREFIXES=("${ALL_PREFIXES[@]}")
  DEFAULT_MSG="All frozen directories unlocked"
elif [[ -v "CATEGORIES[$CATEGORY]" ]]; then
  PREFIXES=("${CATEGORIES[$CATEGORY]}")
  DEFAULT_MSG="${CATEGORIES[$CATEGORY]} unlocked for editing"
else
  echo "Error: Unknown category '$CATEGORY'"
  echo ""
  usage
fi

MSG="${MESSAGE:-$DEFAULT_MSG}"

# Build JSON array of prefixes
JSON_PREFIXES=$(printf '%s\n' "${PREFIXES[@]}" | jq -R . | jq -s .)

# Write sentinel file
jq -n \
  --argjson grants "$JSON_PREFIXES" \
  --arg message "$MSG" \
  '{grantPermissions: $grants, message: $message}' \
  > "$SENTINEL"

echo "✅ Bucket $BUCKET — Granted: ${PREFIXES[*]}"
echo "   Message: $MSG"
