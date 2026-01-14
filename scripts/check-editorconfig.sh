#!/bin/bash

# Simple EditorConfig compliance checker for common issues
# This is a backup for when Docker/Super Linter is not available

set -e

errors=0

# Check for files with CRLF line endings (should be LF) - only source files
echo "📋 Checking line endings (should be LF)..."
crlf_files=$(find . -type f \( -name "*.cs" -o -name "*.md" -o -name "*.sh" -o -name "*.txt" -o -name "*.yml" -o -name "*.yaml" \) \
   -not -path "./bin/*" -not -path "./obj/*" -not -path "./.git/*" -not -path "./node_modules/*" \
   -not -path "./**/Generated/*" -not -path "./sdks/*" \
   -not -path "./**/obj/*" -not -path "./**/bin/*" \
   -exec grep -l $'\r' {} \; 2>/dev/null | head -5)

if [ -n "$crlf_files" ]; then
    echo "❌ Found source files with CRLF line endings (should be LF):"
    echo "$crlf_files"
    errors=$((errors + 1))
fi

# Check for files missing final newline - only source files (exclude generated JSON)
echo "📋 Checking final newlines..."
missing_newline_files=$(find . -type f \( -name "*.cs" -o -name "*.md" -o -name "*.yml" -o -name "*.yaml" -o -name "*.sh" \) \
   -not -path "./bin/*" -not -path "./obj/*" -not -path "./.git/*" -not -path "./node_modules/*" \
   -not -path "./**/Generated/*" -not -path "./sdks/*" \
   -not -path "./**/obj/*" -not -path "./**/bin/*" \
   -exec sh -c 'test "$(tail -c1 "$1")" && echo "$1"' _ {} \; 2>/dev/null | head -5)

if [ -n "$missing_newline_files" ]; then
    echo "❌ Found source files missing final newline:"
    echo "$missing_newline_files"
    errors=$((errors + 1))
fi

# Check for tab characters in C# files (should use 4 spaces)
echo "📋 Checking for tabs in C# files (should use 4 spaces)..."
tab_files=$(find . -name "*.cs" \
   -not -path "./bin/*" -not -path "./obj/*" -not -path "./.git/*" \
   -not -path "./**/Generated/*" -not -path "./sdks/*" \
   -not -path "./**/obj/*" -not -path "./**/bin/*" \
   -exec grep -l $'\t' {} \; 2>/dev/null | head -5)

if [ -n "$tab_files" ]; then
    echo "❌ Found C# files with tab characters (should use 4 spaces):"
    echo "$tab_files"
    errors=$((errors + 1))
fi

# Check for common indentation issues (lines that don't start with multiples of 4 spaces)
echo "📋 Checking for inconsistent indentation..."
inconsistent_files=$(find . -name "*.cs" \
   -not -path "./bin/*" -not -path "./obj/*" -not -path "./.git/*" \
   -not -path "./**/Generated/*" -not -path "./sdks/*" \
   -not -path "./**/obj/*" -not -path "./**/bin/*" \
   -exec awk '
   /^[ ]*[^ ]/ {
       # Count leading spaces
       spaces = 0
       for(i=1; i<=length($0); i++) {
           if(substr($0,i,1) == " ") spaces++
           else break
       }
       # Check if indentation is multiple of 4 (ignoring empty lines and comments)
       if(spaces > 0 && spaces % 4 != 0 && !/^[ ]*\/\// && !/^[ ]*\*/ && !/^[ ]*$/) {
           print FILENAME ":" NR ":" spaces " spaces (should be multiple of 4)"
           errors++
           if(errors > 3) exit  # Limit output
       }
   }
   END { if(errors > 0) exit 1 }
   ' {} \; 2>/dev/null | head -10)

if [ -n "$inconsistent_files" ]; then
    echo "⚠️ Found potential indentation issues:"
    echo "$inconsistent_files"
    echo "💡 These might be false positives, but check alignment with EditorConfig rules"
    errors=$((errors + 1))
fi

if [ $errors -eq 0 ]; then
    echo "✅ Basic EditorConfig checks passed"
    exit 0
else
    echo "❌ Found $errors EditorConfig issue(s)"
    echo "💡 Run 'make fix-config' to fix automatically with eclint"
    echo "💡 For comprehensive validation, use 'make check-ci' (requires Docker)"
    exit 1
fi
