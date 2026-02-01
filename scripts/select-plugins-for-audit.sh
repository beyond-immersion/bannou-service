#!/bin/bash
# Select plugins with actionable gaps for batch auditing
# Usage: ./select-plugins-for-audit.sh [count]
#   count: number of plugins to randomly select (default: all)
#
# Output format:
#   SCAN: PLUGIN.md|TOTAL=N AUDIT=N FIXED=N ACTIONABLE=N|EXIT_CODE
#   ...
#   SELECTED: plugin-name
#   ...
#   SUMMARY: TOTAL_SCANNED=N WITH_GAPS=N SELECTED=N

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PLUGINS_DIR="$REPO_ROOT/docs/plugins"
CHECK_SCRIPT="$SCRIPT_DIR/check-plugin-gaps.sh"

# Validate check script exists
if [[ ! -x "$CHECK_SCRIPT" ]]; then
    echo "ERROR: check-plugin-gaps.sh not found or not executable at $CHECK_SCRIPT" >&2
    exit 1
fi

# Parse count argument (0 = all)
COUNT="${1:-0}"
if ! [[ "$COUNT" =~ ^[0-9]+$ ]]; then
    echo "ERROR: Count must be a non-negative integer, got: $COUNT" >&2
    exit 1
fi

# Skip list
SKIP_PLUGINS=("DEEP_DIVE_TEMPLATE.md" "WEBSITE.md")

should_skip() {
    local name="$1"
    for skip in "${SKIP_PLUGINS[@]}"; do
        [[ "$name" == "$skip" ]] && return 0
    done
    return 1
}

# Arrays to collect results
declare -a PLUGINS_WITH_GAPS=()
TOTAL_SCANNED=0

# Scan all plugins
for f in "$PLUGINS_DIR"/*.md; do
    [[ ! -f "$f" ]] && continue
    name=$(basename "$f")

    # Skip excluded plugins
    if should_skip "$name"; then
        continue
    fi

    TOTAL_SCANNED=$((TOTAL_SCANNED + 1))

    # Run check script and capture output + exit code
    # Note: We need to capture the exit code properly, not swallow it with || true
    set +e
    output=$("$CHECK_SCRIPT" "$f" 2>/dev/null)
    exitcode=$?
    set -e

    # Output scan line
    echo "SCAN: $name|$output|$exitcode"

    # Track plugins with gaps (exit code 0)
    if [[ $exitcode -eq 0 ]]; then
        # Extract plugin name without .md extension for the skill argument
        plugin_name="${name%.md}"
        # Convert to lowercase for consistency
        plugin_name=$(echo "$plugin_name" | tr '[:upper:]' '[:lower:]')
        PLUGINS_WITH_GAPS+=("$plugin_name")
    fi
done

WITH_GAPS=${#PLUGINS_WITH_GAPS[@]}

# Determine how many to select
if [[ $COUNT -eq 0 ]] || [[ $COUNT -gt $WITH_GAPS ]]; then
    SELECT_COUNT=$WITH_GAPS
else
    SELECT_COUNT=$COUNT
fi

# Randomly select plugins
if [[ $SELECT_COUNT -gt 0 ]] && [[ $WITH_GAPS -gt 0 ]]; then
    # Write to temp file, shuffle, take N
    TEMP_FILE=$(mktemp)
    printf '%s\n' "${PLUGINS_WITH_GAPS[@]}" > "$TEMP_FILE"

    # Use shuf to randomly select
    SELECTED=$(shuf -n "$SELECT_COUNT" "$TEMP_FILE")
    rm -f "$TEMP_FILE"

    # Output selected plugins
    while IFS= read -r plugin; do
        [[ -n "$plugin" ]] && echo "SELECTED: $plugin"
    done <<< "$SELECTED"
fi

# Output summary
echo "SUMMARY: TOTAL_SCANNED=$TOTAL_SCANNED WITH_GAPS=$WITH_GAPS SELECTED=$SELECT_COUNT"
