#!/bin/bash
# Fix line endings for NSwag generated files

echo "Fixing line endings for NSwag generated files..."

# Find all generated .cs files and add final newlines if missing
find . -name "*.Generated.cs" -o -path "*/Generated/*.cs" | while read -r file; do
    if [ -f "$file" ] && [ "$(tail -c1 "$file" | wc -l)" -eq 0 ]; then
        echo "Adding final newline to: $file"
        echo "" >> "$file"
    fi
done

echo "Line ending fixes completed."