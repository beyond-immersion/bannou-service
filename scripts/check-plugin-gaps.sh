#!/bin/bash
# Check if a plugin deep dive has actionable gaps (gaps without AUDIT markers)
#
# Usage: ./check-plugin-gaps.sh <plugin-deep-dive.md>
#
# Output: TOTAL=N AUDIT=N FIXED=N ACTIONABLE=N
#
# Exit codes:
#   0 = Actionable gaps exist (ready to audit)
#   1 = No actionable gaps (skip this plugin)
#   2 = Invalid usage

PLUGIN_FILE="$1"

if [ -z "$PLUGIN_FILE" ] || [ ! -f "$PLUGIN_FILE" ]; then
    echo "Usage: $0 <plugin-deep-dive.md>"
    exit 2
fi

# Count total gap items (numbered lists like "1. **text**") in gap sections
# Sections scanned:
#   - Stubs & Unimplemented Features
#   - Potential Extensions
#   - Implementation Gaps
#   - Bugs (Fix Immediately)
#   - Design Considerations (Requires Planning)
#
# NOT scanned (these are not gaps):
#   - Intentional Quirks (Documented Behavior)
#   - Known Quirks & Caveats (parent section)
TOTAL_ITEMS=$(awk '
/^## Stubs|^### Stubs|^## Potential Extensions|^### Potential Extensions|^## Implementation Gaps|^### Implementation Gaps|^## Bugs|^### Bugs|^#### Bugs|^### Design Considerations|^#### Design Considerations/ { in_section=1; next }
/^##[^#]|^###[^#]|^---$/ { in_section=0 }
# Match both regular items (1. **text**) and strikethrough items (1. ~~**text**~~)
in_section && /^[0-9]+\. (\*\*|~~\*\*)/ { count++ }
END { print count+0 }
' "$PLUGIN_FILE")

# Count AUDIT markers (items that are already being handled)
AUDIT_MARKERS=$(grep -c "<!-- AUDIT:" "$PLUGIN_FILE" 2>/dev/null)
AUDIT_MARKERS=${AUDIT_MARKERS:-0}

# Count FIXED markers (completed items)
FIXED_MARKERS=$(grep -c '\*\*FIXED\*\*' "$PLUGIN_FILE" 2>/dev/null)
FIXED_MARKERS=${FIXED_MARKERS:-0}

# Actionable = total items - audit markers - fixed markers
ACTIONABLE=$((TOTAL_ITEMS - AUDIT_MARKERS - FIXED_MARKERS))

# Ensure non-negative
if [ "$ACTIONABLE" -lt 0 ]; then
    ACTIONABLE=0
fi

echo "TOTAL=$TOTAL_ITEMS AUDIT=$AUDIT_MARKERS FIXED=$FIXED_MARKERS ACTIONABLE=$ACTIONABLE"

# Exit 0 if actionable gaps exist (ready to audit)
# Exit 1 if no actionable gaps (skip this plugin)
if [ "$ACTIONABLE" -gt 0 ]; then
    exit 0
else
    exit 1
fi
