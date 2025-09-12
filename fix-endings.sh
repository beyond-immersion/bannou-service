#!/bin/bash
# Fix line ending issues for all project files
# Handles .cs, .md, .json, .yml, .yaml, .sh, and other text files
# Converts CRLF to LF and adds final newlines where needed

echo "🔧 Fixing line endings and final newlines for all project files..."

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

FIXED_COUNT=0

# Function to fix a file's line ending and whitespace compliance
fix_line_endings() {
    local file="$1"
    local needs_fix=false

    if [ ! -f "$file" ]; then
        return
    fi

    # Check and fix CRLF line endings
    if file "$file" | grep -q "CRLF" 2>/dev/null; then
        echo -e "${YELLOW}  Converting CRLF→LF: $file${NC}"
        # Use sed to convert CRLF to LF (cross-platform compatible)
        sed -i 's/\r$//' "$file" 2>/dev/null || {
            # Fallback for systems where sed -i behaves differently
            sed 's/\r$//' "$file" > "$file.tmp" && mv "$file.tmp" "$file"
        }
        needs_fix=true
    fi

    # For .cs files only: check and remove trailing whitespace + whitespace-only lines
    if [[ "$file" == *.cs ]]; then
        # Remove trailing whitespace
        if grep -q '[[:space:]]\+$' "$file" 2>/dev/null; then
            echo -e "${YELLOW}  Removing trailing whitespace: $file${NC}"
            sed -i 's/[[:space:]]*$//' "$file" 2>/dev/null || {
                # Fallback for systems where sed -i behaves differently
                sed 's/[[:space:]]*$//' "$file" > "$file.tmp" && mv "$file.tmp" "$file"
            }
            needs_fix=true
        fi
        
        # Remove whitespace-only lines (but preserve truly empty lines)
        if grep -q '^[[:space:]]\+$' "$file" 2>/dev/null; then
            echo -e "${YELLOW}  Cleaning whitespace-only lines: $file${NC}"
            sed -i 's/^[[:space:]]*$//' "$file" 2>/dev/null || {
                # Fallback for systems where sed -i behaves differently
                sed 's/^[[:space:]]*$//' "$file" > "$file.tmp" && mv "$file.tmp" "$file"
            }
            needs_fix=true
        fi
    fi

    # Check and add final newline if missing
    if [ -s "$file" ] && [ "$(tail -c1 "$file" 2>/dev/null | wc -l)" -eq 0 ]; then
        echo -e "${YELLOW}  Adding final newline: $file${NC}"
        echo "" >> "$file"
        needs_fix=true
    fi

    if [ "$needs_fix" = true ]; then
        ((FIXED_COUNT++))
        echo -e "${GREEN}  ✅ Fixed whitespace issues: $file${NC}"
    fi
}

echo "📋 Processing all text files (excluding build artifacts)..."

# File patterns to fix line endings for
FILE_PATTERNS=(
    "*.cs"      # C# source files
    "*.md"      # Markdown files
    "*.json"    # JSON configuration files
    "*.yml"     # YAML files
    "*.yaml"    # YAML files
    "*.sh"      # Shell scripts
    "*.txt"     # Text files
    "*.xml"     # XML files
    "*.csproj"  # Project files
    "*.sln"     # Solution files
)

# Process each file pattern
for pattern in "${FILE_PATTERNS[@]}"; do
    find . -name "$pattern" -not -path "./*/obj/*" -not -path "./*/bin/*" -not -path "./*/node_modules/*" | while read -r file; do
        fix_line_endings "$file"
    done
done

# Wait for the subshell to complete and get the count
wait

# Count total files processed
TOTAL_FILES=0
for pattern in "${FILE_PATTERNS[@]}"; do
    COUNT=$(find . -name "$pattern" -not -path "./*/obj/*" -not -path "./*/bin/*" -not -path "./*/node_modules/*" | wc -l)
    TOTAL_FILES=$((TOTAL_FILES + COUNT))
done

echo ""
echo -e "${GREEN}📊 Line ending fixes completed!${NC}"
echo -e "  📁 Total files processed: $TOTAL_FILES"
echo -e "  🎯 File types: .cs, .md, .json, .yml/.yaml, .sh, .txt, .xml, .csproj, .sln"
echo -e "  ✅ All files now have LF line endings and proper final newlines"

echo ""
echo -e "${GREEN}💡 Why this script is needed alongside dotnet format:${NC}"
echo "  • dotnet format: Handles C# code formatting (spacing, braces, etc.)"
echo "  • fix-endings.sh: Handles line endings and final newlines for ALL file types"
echo "  • Git/EditorConfig requires consistent line endings across entire repository"

echo -e "${GREEN}🎉 All project files now have consistent line endings!${NC}"
