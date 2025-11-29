#!/bin/bash

# Support: scripts/build-service-libs.sh [service1] [service2] ...
# If no arguments provided, builds all services

# Check if required commands are installed
commands=("xmllint" "rsync")
for cmd in "${commands[@]}"; do
    if ! command -v $cmd &> /dev/null; then
        echo "$cmd could not be found, and is required for building service libs. Skipping..."
        exit 1
    fi
done

REQUESTED_SERVICES=("$@")  # Get command line arguments
PLUGINS_DIR="plugins"
TARGET_FRAMEWORK="net9.0"

echo "🔧 Building service libraries..."

# Show what we're building
if [[ ${#REQUESTED_SERVICES[@]} -gt 0 ]]; then
    echo "📦 Building specific services: ${REQUESTED_SERVICES[*]}"
else
    echo "📦 Building all available services"
fi

mkdir -p $PLUGINS_DIR

projects=($(find . -name '*.csproj'))
services_built=()
services_skipped=()

# Removed complex dependency analysis - not needed since we use single 'dotnet build'

# Build all services efficiently in one go, then copy service-specific DLLs
build_all_services_efficiently() {
    local services_to_build=("$@")

    if [[ ${#services_to_build[@]} -eq 0 ]]; then
        echo "⚠️  No services to build"
        return 0
    fi

    echo "🔨 Building all services efficiently..."

    # First restore all dependencies
    echo "📦 Restoring dependencies..."
    dotnet restore --verbosity quiet

    # Publish each service project to ensure all dependencies are copied
    for service in "${services_to_build[@]}"; do
        echo "🔨 Publishing lib-$service..."
        dotnet publish "lib-$service/lib-$service.csproj" --configuration Release -nologo --verbosity quiet -p:GenerateNewServices=false -p:GenerateUnitTests=false --no-restore
        if [ $? -ne 0 ]; then
            echo "❌ Failed to publish lib-$service"
            exit 1
        fi
    done

    # Give the build a moment to finish writing all files (especially in Docker)
    sleep 1

    # Verify all expected DLLs exist before trying to copy
    echo "🔍 Verifying DLLs were published..."
    for service in "${services_to_build[@]}"; do
        service_dll="lib-$service/bin/Release/$TARGET_FRAMEWORK/publish/lib-$service.dll"
        if [[ ! -f "$service_dll" ]]; then
            echo "❌ Expected DLL not found after publish: $service_dll"
            echo "📁 Contents of lib-$service/bin/Release/$TARGET_FRAMEWORK/publish/:"
            ls -la "lib-$service/bin/Release/$TARGET_FRAMEWORK/publish/" 2>/dev/null || echo "Directory does not exist"
            exit 1
        fi
    done

    # Now copy service-specific DLLs only
    for service in "${services_to_build[@]}"; do
        copy_service_dll "$service"
    done
}

# Copy service-specific DLL to plugins directory
copy_service_dll() {
    local service_name="$1"

    # If specific services requested and this isn't one of them, skip
    if [[ ${#REQUESTED_SERVICES[@]} -gt 0 ]]; then
        if [[ ! " ${REQUESTED_SERVICES[@]} " =~ " ${service_name} " ]]; then
            echo "⏭️  Skipping service '$service_name' (not in build list)"
            services_skipped+=("$service_name")
            return 0
        fi
    fi

    # Find the service project directory
    local service_proj_dir="lib-$service_name"
    local service_publish_dir="$service_proj_dir/bin/Release/$TARGET_FRAMEWORK/publish"
    local service_dll="$service_publish_dir/lib-$service_name.dll"

    if [[ ! -f "$service_dll" ]]; then
        echo "❌ Service DLL not found: $service_dll"
        exit 1
    fi

    # Create service-specific directory
    service_dir="$PLUGINS_DIR/$service_name"
    mkdir -p "$service_dir"

    # Copy the service DLL and all its dependencies to avoid assembly loading conflicts
    # Use the publish directory which contains all dependencies

    # Copy main service DLL
    cp "$service_dll" "$service_dir/"

    # Copy all dependencies (all DLLs in the publish directory except the main service DLL)
    find "$service_publish_dir" -name "*.dll" ! -name "lib-$service_name.dll" -exec cp {} "$service_dir/" \;

    echo "✅ Service '$service_name' DLL copied -> $service_dir"
    services_built+=("$service_name")
}

# Since we're using efficient 'dotnet build' (not individual publishes),
# dependency order doesn't matter for building - just get all services
echo "🔍 Discovering services..."

all_services=()
for proj in "${projects[@]}"; do
    if grep -q '<ServiceLib>' "$proj" 2>/dev/null; then
        service_name=$(grep '<ServiceLib>' "$proj" | sed 's/.*<ServiceLib>//g' | sed 's/<\/ServiceLib>.*//g' | tr -d ' ')
        if [[ -n "$service_name" ]]; then
            all_services+=("$service_name")
        fi
    fi
done

if [[ ${#all_services[@]} -eq 0 ]]; then
    echo "⚠️  No services found to build"
    exit 0
fi

echo "📋 Services to build: ${all_services[*]}"

# Use efficient build approach - build all at once, then copy individual DLLs
build_all_services_efficiently "${all_services[@]}"

# Deduplicate identical DLLs to reduce image size
deduplicate_dlls() {
    echo "🔍 Scanning for duplicate DLLs to deduplicate..."

    # Create common directory for shared DLLs
    common_dir="$PLUGINS_DIR/common"
    mkdir -p "$common_dir"

    # Hash to track DLLs by name and size
    declare -A dll_info
    declare -A dll_locations

    # First pass: collect all DLL information
    for service_dir in "$PLUGINS_DIR"/*/; do
        if [[ "$(basename "$service_dir")" == "common" ]]; then
            continue  # Skip the common directory itself
        fi

        for dll_file in "$service_dir"*.dll; do
            if [[ -f "$dll_file" ]]; then
                dll_name=$(basename "$dll_file")

                # NEVER move main plugin DLLs - they must stay in their service directories
                if [[ "$dll_name" =~ ^lib-.*\.dll$ ]]; then
                    echo "🔒 Preserving plugin DLL in service directory: $dll_name"
                    continue
                fi

                dll_size=$(stat -c%s "$dll_file" 2>/dev/null || stat -f%z "$dll_file" 2>/dev/null)
                dll_key="${dll_name}_${dll_size}"

                if [[ -n "${dll_info[$dll_key]}" ]]; then
                    # Found a duplicate - add to the list
                    dll_info[$dll_key]="${dll_info[$dll_key]}|$dll_file"
                else
                    # First occurrence
                    dll_info[$dll_key]="$dll_file"
                fi
            fi
        done
    done

    duplicates_found=0
    space_saved=0

    # Second pass: move duplicates to common directory
    for dll_key in "${!dll_info[@]}"; do
        IFS='|' read -ra dll_files <<< "${dll_info[$dll_key]}"

        if [[ ${#dll_files[@]} -gt 1 ]]; then
            # We have duplicates
            dll_name=$(basename "${dll_files[0]}")
            dll_size=$(echo "$dll_key" | cut -d'_' -f2-)

            echo "📦 Found ${#dll_files[@]} copies of $dll_name (${dll_size} bytes each)"

            # Move the first copy to common directory
            first_dll="${dll_files[0]}"
            cp "$first_dll" "$common_dir/"

            # Remove all copies from service directories
            for dll_file in "${dll_files[@]}"; do
                rm "$dll_file"
                ((duplicates_found++))
            done

            # Calculate space saved (size * (copies - 1))
            copies_removed=$((${#dll_files[@]} - 1))
            space_saved_this_dll=$((dll_size * copies_removed))
            space_saved=$((space_saved + space_saved_this_dll))

            echo "   Moved to common/, removed $copies_removed duplicates, saved $space_saved_this_dll bytes"
        fi
    done

    if [[ $duplicates_found -gt 0 ]]; then
        # Convert bytes to human readable format
        if [[ $space_saved -gt 1048576 ]]; then
            space_mb=$((space_saved / 1048576))
            echo "✅ Deduplicated $duplicates_found DLL files, saved approximately ${space_mb}MB"
        elif [[ $space_saved -gt 1024 ]]; then
            space_kb=$((space_saved / 1024))
            echo "✅ Deduplicated $duplicates_found DLL files, saved approximately ${space_kb}KB"
        else
            echo "✅ Deduplicated $duplicates_found DLL files, saved ${space_saved} bytes"
        fi

        echo "📁 Common DLLs moved to: $common_dir"
        echo "ℹ️  Assembly resolution in Program.cs will check common/ directory for shared dependencies"
    else
        echo "ℹ️  No duplicate DLLs found to deduplicate"
        # Remove empty common directory
        rmdir "$common_dir" 2>/dev/null || true
    fi
}

# Run deduplication
deduplicate_dlls

# Summary
echo ""
echo "📋 Build Summary:"
if [[ ${#services_built[@]} -gt 0 ]]; then
    echo "✅ Services built: ${services_built[*]}"
else
    echo "⚠️  No services were built"
fi

if [[ ${#services_skipped[@]} -gt 0 ]]; then
    echo "⏭️  Services skipped: ${services_skipped[*]}"
fi

# Create plugin manifest file for Docker validation
manifest_file="$PLUGINS_DIR/PLUGIN_MANIFEST.txt"
echo "# Bannou Plugin Manifest" > "$manifest_file"
echo "# Generated on: $(date -u +"%Y-%m-%d %H:%M:%S UTC")" >> "$manifest_file"
echo "# Build command: $0 $*" >> "$manifest_file"
echo "" >> "$manifest_file"

if [[ ${#services_built[@]} -gt 0 ]]; then
    echo "PLUGINS_INCLUDED:" >> "$manifest_file"
    for service in "${services_built[@]}"; do
        echo "  - $service" >> "$manifest_file"
    done
else
    echo "PLUGINS_INCLUDED: none" >> "$manifest_file"
fi

echo "" >> "$manifest_file"
echo "TOTAL_PLUGINS: ${#services_built[@]}" >> "$manifest_file"

echo "📄 Plugin manifest created: $manifest_file"
echo "🏁 Service library build complete"
