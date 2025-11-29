#!/bin/bash

# Extract Bannou Connect Protocol for Client SDK
# Usage: ./extract-protocol-for-sdk.sh [target-directory] [target-namespace]

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROTOCOL_DIR="$SCRIPT_DIR/Protocol"

# Default values
TARGET_DIR="${1:-./BannouProtocol}"
TARGET_NAMESPACE="${2:-BeyondImmersion.BannouService.Connect.Protocol}"

echo "🚀 Extracting Bannou Connect Protocol for Client SDK"
echo "   Source: $PROTOCOL_DIR"
echo "   Target: $TARGET_DIR"
echo "   Namespace: $TARGET_NAMESPACE"
echo ""

# Create target directory
mkdir -p "$TARGET_DIR"

# Protocol files to extract (dependency-free only)
PROTOCOL_FILES=(
    "BinaryMessage.cs"
    "MessageFlags.cs"
    "ResponseCodes.cs"
    "ConnectionState.cs"
    "MessageRouter.cs"
    "GuidGenerator.cs"
    "README.md"
)

# Copy and optionally transform files
for file in "${PROTOCOL_FILES[@]}"; do
    source_file="$PROTOCOL_DIR/$file"
    target_file="$TARGET_DIR/$file"

    if [ ! -f "$source_file" ]; then
        echo "⚠️  Warning: $source_file not found, skipping"
        continue
    fi

    echo "📄 Copying $file"

    if [[ "$file" == *.cs ]] && [[ "$TARGET_NAMESPACE" != "BeyondImmersion.BannouService.Connect.Protocol" ]]; then
        # Replace namespace in C# files
        sed "s/namespace BeyondImmersion\.BannouService\.Connect\.Protocol;/namespace $TARGET_NAMESPACE;/g" \
            "$source_file" > "$target_file"
        echo "   ✅ Namespace updated to $TARGET_NAMESPACE"
    else
        # Copy file as-is
        cp "$source_file" "$target_file"
        echo "   ✅ Copied as-is"
    fi
done

echo ""
echo "🎉 Protocol extraction completed!"
echo ""
echo "📋 Extracted files:"
ls -la "$TARGET_DIR"
echo ""

# Validate extracted C# files have zero external dependencies
echo "🔍 Validating dependencies..."
if command -v grep >/dev/null 2>&1; then
    external_deps=$(grep -r "using " "$TARGET_DIR"/*.cs 2>/dev/null | grep -v "using System" | grep -v "using var" | grep -v "//" || true)

    if [ -n "$external_deps" ]; then
        echo "❌ Warning: External dependencies found:"
        echo "$external_deps"
        echo ""
        echo "   These should be reviewed to ensure Client SDK compatibility"
    else
        echo "✅ All C# files use only System.* dependencies - Client SDK ready!"
    fi
else
    echo "   (grep not available for dependency validation)"
fi

echo ""
echo "📖 Next steps for Client SDK integration:"
echo "   1. Copy the extracted files to your client project"
echo "   2. Update namespaces if needed (already done if you provided TARGET_NAMESPACE)"
echo "   3. Test binary protocol compatibility with server"
echo "   4. Add client-specific extensions while maintaining core compatibility"
echo ""
echo "📚 See README.md in the extracted files for usage examples and integration notes."
