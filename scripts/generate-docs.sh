#!/bin/bash
#
# Generate Documentation from Schemas and Components
#
# This script generates auto-maintained documentation files:
# - docs/GENERATED-STATE-STORES.md - State store components reference
# - docs/GENERATED-EVENTS.md - Event schemas reference
#
# Usage:
#   ./scripts/generate-docs.sh
#   make generate-docs
#
# Prerequisites:
#   pip install ruamel.yaml
#

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"

echo "📚 Generating documentation..."

# Ensure docs directory exists
mkdir -p "$REPO_ROOT/docs"

# Generate state store documentation (from schemas/state-stores.yaml)
echo "  → Generating state store reference..."
python3 "$SCRIPT_DIR/generate-state-stores.py"

# Generate events documentation
echo "  → Generating events reference..."
python3 "$SCRIPT_DIR/generate-event-docs.py"

# Generate configuration documentation
echo "  → Generating configuration reference..."
python3 "$SCRIPT_DIR/generate-config-docs.py"

# Generate service details documentation
echo "  → Generating service details reference..."
python3 "$SCRIPT_DIR/generate-service-details-docs.py"

# Generate client API documentation
echo "  → Generating client API reference..."
python3 "$SCRIPT_DIR/generate-client-api-docs.py"

# Generate client events documentation
echo "  → Generating client events reference..."
python3 "$SCRIPT_DIR/generate-client-events-docs.py"

echo "✅ Documentation generation complete"
