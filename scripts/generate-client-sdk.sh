#!/bin/bash

# ⛔⛔⛔ AGENT MODIFICATION PROHIBITED ⛔⛔⛔
# This script is part of Bannou's code generation pipeline.
# DO NOT MODIFY without EXPLICIT user instructions to change code generation.
#
# Changes to generation scripts silently break builds across ALL 48 services.
# An agent once changed namespace strings across 4 scripts in a single commit,
# breaking every service. If you believe a change is needed:
#   1. STOP and explain what you think is wrong
#   2. Show the EXACT diff you propose
#   3. Wait for EXPLICIT approval before touching ANY generation script
#
# This applies to: namespace strings, output paths, exclusion logic,
# NSwag parameters, post-processing steps, and file naming conventions.
# ⛔⛔⛔ AGENT MODIFICATION PROHIBITED ⛔⛔⛔

set -e

echo "🔧 Generating Bannou SDK behavior runtime files..."

# =============================================================================
# BEHAVIOR FILE COPYING WITH NAMESPACE TRANSFORMATION
# =============================================================================
# The behavior-compiler SDK uses namespace BeyondImmersion.Bannou.BehaviorCompiler
# Server/Client SDKs need their own namespaces:
#   - Server SDK: BeyondImmersion.Bannou.Server.Behavior
#   - Client SDK: BeyondImmersion.Bannou.Client.Behavior
#
# This script copies files from the behavior-compiler SDK and transforms
# namespaces for the server and client SDKs.
# =============================================================================

BEHAVIOR_SDK_DIR="sdks/behavior-compiler"

copy_behavior_files() {
    local target_dir="$1"
    local target_namespace="$2"

    echo "  Copying behavior files to $target_dir with namespace $target_namespace..."

    # Create directory structure (clean first to remove stale files)
    rm -rf "$target_dir/Runtime" "$target_dir/Intent" "$target_dir/Archetypes" "$target_dir/Goap"
    mkdir -p "$target_dir/Runtime"
    mkdir -p "$target_dir/Intent"
    mkdir -p "$target_dir/Archetypes"
    mkdir -p "$target_dir/Goap"

    # Source namespace pattern from the behavior-compiler SDK
    local src_ns="BeyondImmersion.Bannou.BehaviorCompiler"

    # Copy and transform Runtime files
    # EXCLUDE cloud-side files that depend on server infrastructure:
    #   - CinematicInterpreter.cs: Server-side only (lives in behavior-compiler SDK but not needed in client)
    local exclude_runtime="CinematicInterpreter.cs"

    for file in "$BEHAVIOR_SDK_DIR/Runtime/"*.cs; do
        if [ -f "$file" ]; then
            local basename=$(basename "$file")
            # Skip excluded files for client SDK
            if [[ "$target_namespace" == *"Client"* ]] && [[ "$exclude_runtime" == *"$basename"* ]]; then
                echo "    Skipping $basename (server-side only)"
                continue
            fi
            sed "s/$src_ns/$target_namespace/g" "$file" > "$target_dir/Runtime/$basename"
        fi
    done

    # Copy and transform Intent files
    for file in "$BEHAVIOR_SDK_DIR/Intent/"*.cs; do
        if [ -f "$file" ]; then
            local basename=$(basename "$file")
            sed "s/$src_ns/$target_namespace/g" "$file" > "$target_dir/Intent/$basename"
        fi
    done

    # Copy and transform Archetypes files
    for file in "$BEHAVIOR_SDK_DIR/Archetypes/"*.cs; do
        if [ -f "$file" ]; then
            local basename=$(basename "$file")
            sed "s/$src_ns/$target_namespace/g" "$file" > "$target_dir/Archetypes/$basename"
        fi
    done

    # Copy and transform Goap files
    for file in "$BEHAVIOR_SDK_DIR/Goap/"*.cs; do
        if [ -f "$file" ]; then
            local basename=$(basename "$file")
            sed "s/$src_ns/$target_namespace/g" "$file" > "$target_dir/Goap/$basename"
        fi
    done

    # Copy and transform service-level behavior files from lib-behavior
    # These are the evaluator interfaces that depend on the runtime types
    local lib_behavior_ns="BeyondImmersion.BannouService.Behavior"
    for file in IBehaviorEvaluator.cs BehaviorEvaluatorBase.cs BehaviorModelCache.cs; do
        if [ -f "./plugins/lib-behavior/$file" ]; then
            sed "s/$lib_behavior_ns/$target_namespace/g" "./plugins/lib-behavior/$file" > "$target_dir/$file"
        fi
    done

    echo "  Done copying behavior files."
}

# =============================================================================
# SERVER SDK: sdks/server
# =============================================================================

SERVER_SDK_DIR="sdks/server"
mkdir -p "$SERVER_SDK_DIR/Generated/Behavior"

# Copy behavior files with Server SDK namespace
copy_behavior_files "$SERVER_SDK_DIR/Generated/Behavior" "BeyondImmersion.Bannou.Server.Behavior"

echo "✅ Server SDK behavior files: $SERVER_SDK_DIR/Generated/Behavior/"

# =============================================================================
# CLIENT SDK: sdks/client
# =============================================================================

CLIENT_SDK_DIR="sdks/client"
mkdir -p "$CLIENT_SDK_DIR/Generated/Behavior"

# Copy behavior files with Client SDK namespace
copy_behavior_files "$CLIENT_SDK_DIR/Generated/Behavior" "BeyondImmersion.Bannou.Client.Behavior"

echo "✅ Client SDK behavior files: $CLIENT_SDK_DIR/Generated/Behavior/"

# =============================================================================
# SUMMARY
# =============================================================================

echo ""
echo "✅ SDK behavior generation completed successfully!"
echo ""
echo "   Source: $BEHAVIOR_SDK_DIR (behavior-compiler SDK)"
echo "   Server: $SERVER_SDK_DIR/Generated/Behavior/"
echo "   Client: $CLIENT_SDK_DIR/Generated/Behavior/"
echo ""
