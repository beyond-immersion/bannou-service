#!/bin/bash

# show-help-inline.sh
# Alternative help system that parses inline ## comments from Makefile
# Usage: show-help-inline.sh [Makefile]

MAKEFILE="${1:-Makefile}"

if [ ! -f "$MAKEFILE" ]; then
    echo "❌ Makefile not found: $MAKEFILE"
    exit 1
fi

echo "🔧 Available Make Commands"
echo ""

# Parse Makefile for targets with ## comments
grep -E '^[a-zA-Z_-]+:.*##' "$MAKEFILE" | \
    sed 's/\(.*\):.*## \(.*\)/\1|\2/' | \
    column -t -s '|' | \
    sed 's/^/  /' | \
    sort

echo ""
echo "💡 Run 'make help' for organized categorized help"
