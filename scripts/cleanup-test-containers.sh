#!/bin/bash
# =============================================================================
# BANNOU TEST CONTAINER CLEANUP SCRIPT
# =============================================================================
# Removes leftover containers from failed/interrupted test runs.
# Targets both docker-compose managed containers and dynamically deployed
# containers created by the orchestrator service.
#
# Usage:
#   ./scripts/cleanup-test-containers.sh         # Cleanup all test containers
#   ./scripts/cleanup-test-containers.sh --dry-run  # Show what would be removed
#   ./scripts/cleanup-test-containers.sh --force    # Force remove without confirmation
# =============================================================================

set -e

DRY_RUN=false
FORCE=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --dry-run)
            DRY_RUN=true
            shift
            ;;
        --force)
            FORCE=true
            shift
            ;;
        *)
            echo "Unknown option: $1"
            echo "Usage: $0 [--dry-run] [--force]"
            exit 1
            ;;
    esac
done

echo "🧹 Bannou Test Container Cleanup"
echo "================================="

# Pattern matching for containers to remove:
# - bannou-test-* : docker-compose test stacks (test-http, test-edge, test-infra)
# - bannou-bannou-* : orchestrator dynamically deployed containers
# - bannou-*-dapr : dapr sidecars for orchestrator containers
# - bannou-main*, bannou-auth*, bannou-edge* : split routing test containers

PATTERNS=(
    "bannou-test-"
    "bannou-bannou-"
    "bannou-main"
    "bannou-auth"
    "bannou-edge"
)

# Build grep pattern
GREP_PATTERN=$(IFS="|"; echo "${PATTERNS[*]}")

# Find containers matching our patterns
CONTAINERS=$(docker ps -a --format "{{.Names}}" 2>/dev/null | grep -E "$GREP_PATTERN" || true)

if [ -z "$CONTAINERS" ]; then
    echo "✅ No test containers found to clean up"
    exit 0
fi

# Count containers
CONTAINER_COUNT=$(echo "$CONTAINERS" | wc -l)
echo ""
echo "Found $CONTAINER_COUNT container(s) to clean up:"
echo "$CONTAINERS" | while read -r name; do
    STATUS=$(docker inspect --format '{{.State.Status}}' "$name" 2>/dev/null || echo "unknown")
    echo "  - $name ($STATUS)"
done

if [ "$DRY_RUN" = true ]; then
    echo ""
    echo "🔍 Dry run - no containers were removed"
    echo "   Run without --dry-run to actually remove containers"
    exit 0
fi

# Confirm unless --force
if [ "$FORCE" != true ]; then
    echo ""
    read -p "Remove these containers? [y/N] " -n 1 -r
    echo ""
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        echo "❌ Cleanup cancelled"
        exit 0
    fi
fi

echo ""
echo "🗑️  Removing containers..."

# Stop and remove containers
echo "$CONTAINERS" | while read -r name; do
    if [ -n "$name" ]; then
        echo "   Removing: $name"
        docker stop "$name" 2>/dev/null || true
        docker rm -f "$name" 2>/dev/null || true
    fi
done

# Also clean up any orphaned networks from test runs
echo ""
echo "🔌 Cleaning up orphaned networks..."
NETWORKS=$(docker network ls --format "{{.Name}}" 2>/dev/null | grep -E "bannou-test-|bannou_default" || true)
if [ -n "$NETWORKS" ]; then
    echo "$NETWORKS" | while read -r net; do
        if [ -n "$net" ]; then
            echo "   Removing network: $net"
            docker network rm "$net" 2>/dev/null || true
        fi
    done
fi

echo ""
echo "✅ Cleanup complete"

# Show any remaining containers (for debugging)
REMAINING=$(docker ps -a --format "{{.Names}}" 2>/dev/null | grep -E "$GREP_PATTERN" || true)
if [ -n "$REMAINING" ]; then
    echo ""
    echo "⚠️  Some containers could not be removed:"
    echo "$REMAINING"
fi
