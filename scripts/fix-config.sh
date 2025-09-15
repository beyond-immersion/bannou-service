#!/bin/bash

# fix-config.sh
# Comprehensive EditorConfig fixing using eclint

echo "🔧 Fixing common EditorConfig issues with eclint..."

# Check if eclint is available
if ! command -v eclint &> /dev/null; then
    echo "❌ eclint not found. Install with: npm install -g eclint"
    exit 1
fi

# Fix C# files
echo "🔧 Fixing C# files..."
find . -name "*.cs" \
    -not -path "./bin/*" \
    -not -path "./obj/*" \
    -not -path "./**/Generated/*" \
    -not -path "./Bannou.Client.SDK/*" \
    -not -path "./**/obj/*" \
    -not -path "./**/bin/*" \
    | xargs eclint fix

# Fix Markdown files
echo "🔧 Fixing Markdown files..."
find . -name "*.md" \
    -not -path "./bin/*" \
    -not -path "./obj/*" \
    -not -path "./.git/*" \
    -not -path "./node_modules/*" \
    -not -path "./**/Generated/*" \
    -not -path "./Bannou.Client.SDK/*" \
    | xargs eclint fix

# Fix YAML files
echo "🔧 Fixing YAML files..."
find . \( -name "*.yml" -o -name "*.yaml" \) \
    | grep -v "/bin/" \
    | grep -v "/obj/" \
    | grep -v "/.git/" \
    | xargs eclint fix

echo "✅ EditorConfig issues fixed"
