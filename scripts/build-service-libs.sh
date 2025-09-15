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

    # Build each service project explicitly to ensure all DLLs are created
    for service in "${services_to_build[@]}"; do
        echo "🔨 Building lib-$service..."
        dotnet build "lib-$service/lib-$service.csproj" --configuration Release -nologo --verbosity quiet -p:GenerateNewServices=false -p:GenerateUnitTests=false
        if [ $? -ne 0 ]; then
            echo "❌ Failed to build lib-$service"
            exit 1
        fi
    done

    # Give the build a moment to finish writing all files (especially in Docker)
    sleep 1

    # Verify all expected DLLs exist before trying to copy
    echo "🔍 Verifying DLLs were built..."
    for service in "${services_to_build[@]}"; do
        service_dll="lib-$service/bin/Release/$TARGET_FRAMEWORK/lib-$service.dll"
        if [[ ! -f "$service_dll" ]]; then
            echo "❌ Expected DLL not found after build: $service_dll"
            echo "📁 Contents of lib-$service/bin/Release/$TARGET_FRAMEWORK/:"
            ls -la "lib-$service/bin/Release/$TARGET_FRAMEWORK/" 2>/dev/null || echo "Directory does not exist"
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
    local service_dll="$service_proj_dir/bin/Release/$TARGET_FRAMEWORK/lib-$service_name.dll"

    if [[ ! -f "$service_dll" ]]; then
        echo "❌ Service DLL not found: $service_dll"
        exit 1
    fi

    # Create service-specific directory
    service_dir="$PLUGINS_DIR/$service_name"
    mkdir -p "$service_dir"

    # Copy only the service DLL (not all dependencies)
    cp "$service_dll" "$service_dir/"

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
