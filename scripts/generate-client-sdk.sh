#!/bin/bash

set -e

echo "🔧 Generating Bannou SDK behavior runtime files..."

# =============================================================================
# BEHAVIOR FILE COPYING WITH NAMESPACE TRANSFORMATION
# =============================================================================
# lib-behavior uses namespace BeyondImmersion.BannouService.Behavior
# SDKs need their own namespaces:
#   - Server SDK: BeyondImmersion.Bannou.SDK.Behavior
#   - Client SDK: BeyondImmersion.Bannou.Client.SDK.Behavior
#
# This script ONLY regenerates the behavior runtime files with namespace
# transformation. The SDK .csproj files are static and committed to git.
# =============================================================================

copy_behavior_files() {
    local target_dir="$1"
    local target_namespace="$2"

    echo "  Copying behavior files to $target_dir with namespace $target_namespace..."

    # Create directory structure
    mkdir -p "$target_dir/Runtime"
    mkdir -p "$target_dir/Intent"

    # Source namespace pattern
    local src_ns="BeyondImmersion.BannouService.Behavior"

    # Copy and transform Runtime files
    # EXCLUDE cloud-side files that depend on server infrastructure:
    #   - CinematicController.cs: Depends on Control/* types (server-side orchestration)
    local exclude_runtime="CinematicController.cs"

    for file in ./plugins/lib-behavior/Runtime/*.cs; do
        if [ -f "$file" ]; then
            local basename=$(basename "$file")
            # Skip excluded files
            if [[ "$exclude_runtime" == *"$basename"* ]]; then
                echo "    Skipping $basename (cloud-side only)"
                continue
            fi
            sed "s/$src_ns/$target_namespace/g" "$file" > "$target_dir/Runtime/$basename"
        fi
    done

    # Copy and transform Intent files
    for file in ./plugins/lib-behavior/Intent/*.cs; do
        if [ -f "$file" ]; then
            local basename=$(basename "$file")
            sed "s/$src_ns/$target_namespace/g" "$file" > "$target_dir/Intent/$basename"
        fi
    done

    # Copy and transform root behavior files (IBehaviorEvaluator, BehaviorEvaluatorBase, BehaviorModelCache)
    for file in IBehaviorEvaluator.cs BehaviorEvaluatorBase.cs BehaviorModelCache.cs; do
        if [ -f "./plugins/lib-behavior/$file" ]; then
            sed "s/$src_ns/$target_namespace/g" "./plugins/lib-behavior/$file" > "$target_dir/$file"
        fi
    done

    echo "  Done copying behavior files."
}

# =============================================================================
# SERVER SDK: Bannou.SDK
# =============================================================================

SERVER_SDK_DIR="Bannou.SDK"
mkdir -p "$SERVER_SDK_DIR/Generated/Behavior"

# Copy behavior files with Server SDK namespace
copy_behavior_files "$SERVER_SDK_DIR/Generated/Behavior" "BeyondImmersion.Bannou.SDK.Behavior"

echo "✅ Server SDK behavior files: $SERVER_SDK_DIR/Generated/Behavior/"

# =============================================================================
# CLIENT SDK: Bannou.Client.SDK
# =============================================================================

CLIENT_SDK_DIR="Bannou.Client.SDK"
mkdir -p "$CLIENT_SDK_DIR/Generated/Behavior"

# Copy behavior files with Client SDK namespace
copy_behavior_files "$CLIENT_SDK_DIR/Generated/Behavior" "BeyondImmersion.Bannou.Client.SDK.Behavior"

echo "✅ Client SDK behavior files: $CLIENT_SDK_DIR/Generated/Behavior/"

# =============================================================================
# SUMMARY
# =============================================================================

echo ""
echo "✅ SDK behavior generation completed successfully!"
echo ""
echo "   Note: SDK .csproj files are static and committed to git."
echo "   This script only regenerates behavior runtime files with namespace transformation."
echo ""
