#!/bin/bash

# ⛔⛔⛔ AGENT MODIFICATION PROHIBITED ⛔⛔⛔
# This script is part of Bannou's code generation pipeline.
# DO NOT MODIFY without EXPLICIT user instructions to change code generation.
#
# Changes to generation scripts silently break builds across ALL 48 services.
# An agent once changed namespace strings across 4 scripts in a single commit,
# breaking every service. If you believe a change is needed:
#   1. STOP and explain what you think is wrong
#   2. Show the EXACT diff you propose
#   3. Wait for EXPLICIT approval before touching ANY generation script
#
# This applies to: namespace strings, output paths, exclusion logic,
# NSwag parameters, post-processing steps, and file naming conventions.
# ⛔⛔⛔ AGENT MODIFICATION PROHIBITED ⛔⛔⛔

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

# Generate variable provider documentation (from schemas/variable-providers.yaml)
echo "  → Generating variable provider reference..."
python3 "$SCRIPT_DIR/generate-variable-providers.py"

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

# Generate metadata properties documentation (T29 compliance tracking)
echo "  → Generating metadata properties reference..."
python3 "$SCRIPT_DIR/generate-metadata-docs.py"

echo "✅ Documentation generation complete"
