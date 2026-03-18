#!/bin/bash

# ⛔⛔⛔ AGENT MODIFICATION PROHIBITED ⛔⛔⛔
# This script is part of Bannou's code generation pipeline.
# DO NOT MODIFY without EXPLICIT user instructions to change code generation.
#
# Changes to generation scripts silently break builds across ALL 76+ services.
# An agent once changed namespace strings across 4 scripts in a single commit,
# breaking every service. If you believe a change is needed:
#   1. STOP and explain what you think is wrong
#   2. Show the EXACT diff you propose
#   3. Wait for EXPLICIT approval before touching ANY generation script
#
# This applies to: namespace strings, output paths, exclusion logic,
# NSwag parameters, post-processing steps, and file naming conventions.
# ⛔⛔⛔ AGENT MODIFICATION PROHIBITED ⛔⛔⛔

# Audit file sizes across the codebase to find splitting candidates
# Usage: ./scripts/audit-file-sizes.sh [options]
#   --top N          Show top N largest non-generated C# files (default: 5)
#   --cs-threshold N Show all non-generated C# files over N lines (default: 1400)
#   --doc-threshold N Show docs over N lines (default: 1400)
#   --faq-threshold N Show FAQs over N lines (default: 300)
#   --all            Show all categories (default if no category flags given)
#   --cs             Show C# file reports only
#   --docs           Show documentation reports only
#   --summary        Show summary statistics only

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Defaults
TOP_N=5
CS_THRESHOLD=1400
DOC_THRESHOLD=1400
FAQ_THRESHOLD=300
SHOW_CS=false
SHOW_DOCS=false
SHOW_SUMMARY=false
EXPLICIT_CATEGORY=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case "$1" in
        --top)
            TOP_N="$2"
            shift 2
            ;;
        --cs-threshold)
            CS_THRESHOLD="$2"
            shift 2
            ;;
        --doc-threshold)
            DOC_THRESHOLD="$2"
            shift 2
            ;;
        --faq-threshold)
            FAQ_THRESHOLD="$2"
            shift 2
            ;;
        --cs)
            SHOW_CS=true
            EXPLICIT_CATEGORY=true
            shift
            ;;
        --docs)
            SHOW_DOCS=true
            EXPLICIT_CATEGORY=true
            shift
            ;;
        --summary)
            SHOW_SUMMARY=true
            EXPLICIT_CATEGORY=true
            shift
            ;;
        --all)
            SHOW_CS=true
            SHOW_DOCS=true
            SHOW_SUMMARY=true
            shift
            ;;
        -h|--help)
            echo "Usage: $0 [options]"
            echo ""
            echo "Options:"
            echo "  --top N            Top N largest non-generated C# files (default: 5)"
            echo "  --cs-threshold N   C# files over N lines (default: 1400)"
            echo "  --doc-threshold N  Docs over N lines (default: 1400)"
            echo "  --faq-threshold N  FAQs over N lines (default: 300)"
            echo "  --cs               Show C# file reports only"
            echo "  --docs             Show documentation reports only"
            echo "  --summary          Show summary statistics only"
            echo "  --all              Show all categories (default)"
            echo ""
            echo "Examples:"
            echo "  $0                         # Show everything with defaults"
            echo "  $0 --cs --top 10           # Top 10 C# files only"
            echo "  $0 --docs --doc-threshold 1000  # Docs over 1000 lines"
            echo "  $0 --cs-threshold 2000     # Only C# files over 2000 lines"
            exit 0
            ;;
        *)
            echo "Unknown option: $1 (use --help for usage)"
            exit 1
            ;;
    esac
done

# If no explicit category, show all
if [ "$EXPLICIT_CATEGORY" = false ]; then
    SHOW_CS=true
    SHOW_DOCS=true
    SHOW_SUMMARY=true
fi

# Colors
RED='\033[0;31m'
YELLOW='\033[1;33m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m'
BOLD='\033[1m'

# Helper: find non-generated C# files
find_cs_files() {
    find "$REPO_ROOT" -name "*.cs" -type f \
        -not -path "*/Generated/*" \
        -not -path "*/bin/*" \
        -not -path "*/obj/*" \
        -not -path "*/.git/*" \
        -not -path "*/.claude/*" \
        -not -path "*/node_modules/*"
}

# Helper: count lines and format output
# Args: file_path
format_file_line() {
    local file="$1"
    local lines
    lines=$(wc -l < "$file")
    local rel_path="${file#$REPO_ROOT/}"
    printf "%6d  %s\n" "$lines" "$rel_path"
}

echo -e "${BOLD}Bannou File Size Audit${NC}"
echo "=============================="
echo ""

# ============================================================
# C# SOURCE FILES
# ============================================================
if [ "$SHOW_CS" = true ]; then

    echo -e "${CYAN}── C# Source Files (non-generated) ──${NC}"
    echo ""

    # Top N largest
    echo -e "${BOLD}Top $TOP_N largest C# files:${NC}"
    echo ""
    find_cs_files | while read -r f; do
        lines=$(wc -l < "$f")
        rel="${f#$REPO_ROOT/}"
        printf "%6d  %s\n" "$lines" "$rel"
    done | sort -rn | head -"$TOP_N"
    echo ""

    # All over threshold
    echo -e "${BOLD}C# files over $CS_THRESHOLD lines:${NC}"
    echo ""

    OVER_COUNT=0
    OVER_OUTPUT=$(find_cs_files | while read -r f; do
        lines=$(wc -l < "$f")
        if [ "$lines" -gt "$CS_THRESHOLD" ]; then
            rel="${f#$REPO_ROOT/}"
            printf "%6d  %s\n" "$lines" "$rel"
        fi
    done | sort -rn)

    if [ -n "$OVER_OUTPUT" ]; then
        echo "$OVER_OUTPUT"
        OVER_COUNT=$(echo "$OVER_OUTPUT" | wc -l)
        echo ""
        echo -e "  ${YELLOW}$OVER_COUNT file(s) over $CS_THRESHOLD lines${NC}"
    else
        echo -e "  ${GREEN}No C# files over $CS_THRESHOLD lines${NC}"
    fi
    echo ""
fi

# ============================================================
# DOCUMENTATION FILES
# ============================================================
if [ "$SHOW_DOCS" = true ]; then

    echo -e "${CYAN}── Documentation ──${NC}"
    echo ""

    # Reference docs
    echo -e "${BOLD}Reference docs over $DOC_THRESHOLD lines:${NC}  (docs/reference/)"
    echo ""
    REF_OUTPUT=$(find "$REPO_ROOT/docs/reference" -name "*.md" -type f | while read -r f; do
        lines=$(wc -l < "$f")
        if [ "$lines" -gt "$DOC_THRESHOLD" ]; then
            rel="${f#$REPO_ROOT/}"
            printf "%6d  %s\n" "$lines" "$rel"
        fi
    done | sort -rn)

    if [ -n "$REF_OUTPUT" ]; then
        echo "$REF_OUTPUT"
    else
        echo -e "  ${GREEN}None over $DOC_THRESHOLD lines${NC}"
    fi
    echo ""

    # Generated docs
    echo -e "${BOLD}Generated docs over $DOC_THRESHOLD lines:${NC}  (docs/generated/)"
    echo ""
    GEN_OUTPUT=$(find "$REPO_ROOT/docs/generated" -name "*.md" -type f | while read -r f; do
        lines=$(wc -l < "$f")
        if [ "$lines" -gt "$DOC_THRESHOLD" ]; then
            rel="${f#$REPO_ROOT/}"
            printf "%6d  %s\n" "$lines" "$rel"
        fi
    done | sort -rn)

    if [ -n "$GEN_OUTPUT" ]; then
        echo "$GEN_OUTPUT"
    else
        echo -e "  ${GREEN}None over $DOC_THRESHOLD lines${NC}"
    fi
    echo ""

    # Deep dives
    echo -e "${BOLD}Deep dives over $DOC_THRESHOLD lines:${NC}  (docs/plugins/)"
    echo ""
    DD_OUTPUT=$(find "$REPO_ROOT/docs/plugins" -name "*.md" -type f | while read -r f; do
        lines=$(wc -l < "$f")
        if [ "$lines" -gt "$DOC_THRESHOLD" ]; then
            rel="${f#$REPO_ROOT/}"
            printf "%6d  %s\n" "$lines" "$rel"
        fi
    done | sort -rn)

    if [ -n "$DD_OUTPUT" ]; then
        echo "$DD_OUTPUT"
    else
        echo -e "  ${GREEN}None over $DOC_THRESHOLD lines${NC}"
    fi
    echo ""

    # Implementation maps
    echo -e "${BOLD}Implementation maps over $DOC_THRESHOLD lines:${NC}  (docs/maps/)"
    echo ""
    MAP_OUTPUT=""
    if [ -d "$REPO_ROOT/docs/maps" ]; then
        MAP_OUTPUT=$(find "$REPO_ROOT/docs/maps" -name "*.md" -type f | while read -r f; do
            lines=$(wc -l < "$f")
            if [ "$lines" -gt "$DOC_THRESHOLD" ]; then
                rel="${f#$REPO_ROOT/}"
                printf "%6d  %s\n" "$lines" "$rel"
            fi
        done | sort -rn)
    fi

    if [ -n "$MAP_OUTPUT" ]; then
        echo "$MAP_OUTPUT"
    else
        echo -e "  ${GREEN}None over $DOC_THRESHOLD lines${NC}"
    fi
    echo ""

    # FAQs (lower threshold)
    echo -e "${BOLD}FAQs over $FAQ_THRESHOLD lines:${NC}  (docs/faq/)"
    echo ""
    FAQ_OUTPUT=""
    if [ -d "$REPO_ROOT/docs/faq" ]; then
        FAQ_OUTPUT=$(find "$REPO_ROOT/docs/faq" -name "*.md" -type f | while read -r f; do
            lines=$(wc -l < "$f")
            if [ "$lines" -gt "$FAQ_THRESHOLD" ]; then
                rel="${f#$REPO_ROOT/}"
                printf "%6d  %s\n" "$lines" "$rel"
            fi
        done | sort -rn)
    fi

    if [ -n "$FAQ_OUTPUT" ]; then
        echo "$FAQ_OUTPUT"
    else
        echo -e "  ${GREEN}None over $FAQ_THRESHOLD lines${NC}"
    fi
    echo ""
fi

# ============================================================
# SUMMARY STATISTICS
# ============================================================
if [ "$SHOW_SUMMARY" = true ]; then

    echo -e "${CYAN}── Summary Statistics ──${NC}"
    echo ""

    # C# stats
    CS_TOTAL=$(find_cs_files | wc -l)
    CS_OVER=$(find_cs_files | while read -r f; do
        lines=$(wc -l < "$f")
        if [ "$lines" -gt "$CS_THRESHOLD" ]; then echo "$f"; fi
    done | wc -l)

    echo -e "  Non-generated C# files:  ${BOLD}$CS_TOTAL${NC} total, ${YELLOW}$CS_OVER${NC} over $CS_THRESHOLD lines"

    # Doc category stats
    for dir_label in "docs/reference:Reference docs" "docs/generated:Generated docs" "docs/plugins:Deep dives" "docs/maps:Implementation maps"; do
        dir="${dir_label%%:*}"
        label="${dir_label##*:}"
        full_dir="$REPO_ROOT/$dir"
        if [ -d "$full_dir" ]; then
            total=$(find "$full_dir" -name "*.md" -type f | wc -l)
            over=$(find "$full_dir" -name "*.md" -type f | while read -r f; do
                lines=$(wc -l < "$f")
                if [ "$lines" -gt "$DOC_THRESHOLD" ]; then echo "$f"; fi
            done | wc -l)
            printf "  %-24s ${BOLD}%d${NC} total, ${YELLOW}%d${NC} over %d lines\n" "$label:" "$total" "$over" "$DOC_THRESHOLD"
        fi
    done

    # FAQ stats (separate threshold)
    if [ -d "$REPO_ROOT/docs/faq" ]; then
        faq_total=$(find "$REPO_ROOT/docs/faq" -name "*.md" -type f | wc -l)
        faq_over=$(find "$REPO_ROOT/docs/faq" -name "*.md" -type f | while read -r f; do
            lines=$(wc -l < "$f")
            if [ "$lines" -gt "$FAQ_THRESHOLD" ]; then echo "$f"; fi
        done | wc -l)
        printf "  %-24s ${BOLD}%d${NC} total, ${YELLOW}%d${NC} over %d lines\n" "FAQs:" "$faq_total" "$faq_over" "$FAQ_THRESHOLD"
    fi

    echo ""
fi

echo -e "${GREEN}Audit complete.${NC}"
