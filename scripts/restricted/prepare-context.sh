#!/usr/bin/env bash
# prepare-context.sh — Inject a prepareContext command via sentinel
#
# Usage:
#   ./scripts/restricted/prepare-context.sh <bucket> <profile> [service-or-option]
#
# Examples:
#   ./scripts/restricted/prepare-context.sh 1 dev
#   ./scripts/restricted/prepare-context.sh 2 plugin account
#   ./scripts/restricted/prepare-context.sh 1 schema
#   ./scripts/restricted/prepare-context.sh 3 plugin divine
#
# Profiles:
#   dev     — Core tenets, patterns, project instructions (8 files)
#   plugin  — Dev + deep dive + implementation map (requires service name)
#   schema  — Dev + schema rules + specifications catalog

set -euo pipefail

if [[ $# -lt 2 ]]; then
    echo "Usage: $0 <bucket> <profile> [service]"
    echo ""
    echo "  bucket   1-5 (matches BANNOU_MCP_BUCKET of target terminal)"
    echo "  profile  dev | plugin | schema"
    echo "  service  Service name for 'plugin' profile (e.g., account, divine)"
    exit 1
fi

BUCKET="$1"
PROFILE="$2"
SERVICE="${3:-}"

SENTINEL="/tmp/bannou-mcp-inject-${BUCKET}.json"

# Validate bucket
if [[ ! "$BUCKET" =~ ^[1-5]$ ]]; then
    echo "Error: bucket must be 1-5, got '${BUCKET}'"
    exit 1
fi

# Validate profile and build JSON
case "$PROFILE" in
    dev|schema)
        JSON=$(printf '{"prepareContext":{"profile":"%s"},"message":"Context prepared: %s"}' "$PROFILE" "$PROFILE")
        ;;
    plugin)
        if [[ -z "$SERVICE" ]]; then
            echo "Error: 'plugin' profile requires a service name"
            echo "Usage: $0 $BUCKET plugin <service>"
            exit 1
        fi
        JSON=$(printf '{"prepareContext":{"profile":"plugin","service":"%s"},"message":"Context prepared: plugin/%s"}' "$SERVICE" "$SERVICE")
        ;;
    *)
        echo "Error: unknown profile '${PROFILE}'"
        echo "Available: dev, plugin, schema"
        exit 1
        ;;
esac

echo "$JSON" > "$SENTINEL"
echo "✅ Sentinel written to ${SENTINEL}"
echo "   Profile: ${PROFILE}${SERVICE:+ (service: ${SERVICE})}"
echo "   Agent will prepare context on next tool call."
