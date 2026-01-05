#!/bin/bash

# validate-compose-services.sh
# Validates that specific services are included in the latest Docker image
# Usage: validate-compose-services.sh [service1] [service2] ...
#        If no services specified, shows all services in the image

REQUESTED_SERVICES=("$@")

# Function to get the latest bannou image
get_latest_image() {
    docker images --format "table {{.Repository}}:{{.Tag}}" | grep "bannou" | head -1
}

# Function to read manifest from Docker image
read_manifest_from_image() {
    local image_name="$1"
    docker run --rm --entrypoint cat "$image_name" /PLUGIN_MANIFEST.txt 2>/dev/null
}

# Function to validate specific services
validate_specific_services() {
    local manifest_content="$1"
    shift
    local services=("$@")

    echo "🔍 Validating Docker image contains services: ${services[*]}"
    echo "📋 Checking plugin manifest in Docker image..."
    echo ""

    if [ -z "$manifest_content" ]; then
        echo "❌ Could not read plugin manifest from Docker image"
        echo "💡 Make sure you've built the image with: make build-compose-services SERVICES=\"...\""
        return 1
    fi

    echo "📄 Plugin manifest content:"
    echo "$manifest_content" | sed 's/^/  /'
    echo ""

    local missing_services=""
    for service in "${services[@]}"; do
        if echo "$manifest_content" | grep -q "  - $service"; then
            echo "✅ Service '$service' found in Docker image"
        else
            echo "❌ Service '$service' NOT found in Docker image"
            missing_services="$missing_services $service"
        fi
    done

    if [ -n "$missing_services" ]; then
        echo ""
        echo "❌ Missing services:$missing_services"
        echo "💡 Rebuild the image with: make build-compose-services SERVICES=\"${services[*]}\""
        return 1
    else
        echo ""
        echo "✅ All requested services found in Docker image!"
        return 0
    fi
}

# Function to show all services in image
show_all_services() {
    local manifest_content="$1"

    echo "📋 Showing all plugins in the latest Docker image:"

    if [ -z "$manifest_content" ]; then
        echo "❌ Could not read plugin manifest from Docker image"
        echo "💡 Make sure you've built an image with: make build-compose or make build-compose-services"
        return 1
    fi

    echo "$manifest_content" | sed 's/^/  /'
    echo ""
    echo "📖 Usage: validate-compose-services.sh auth account connect"
    return 0
}

# Main execution
main() {
    # Get the latest image
    local latest_image
    latest_image=$(get_latest_image)

    if [ -z "$latest_image" ]; then
        echo "❌ No bannou Docker image found"
        echo "💡 Build an image first with: make build-compose or make build-compose-services"
        exit 1
    fi

    echo "🐳 Using Docker image: $latest_image"

    # Read manifest from image
    local manifest_content
    manifest_content=$(read_manifest_from_image "$latest_image")

    # Validate or show services
    if [ ${#REQUESTED_SERVICES[@]} -gt 0 ]; then
        validate_specific_services "$manifest_content" "${REQUESTED_SERVICES[@]}"
    else
        show_all_services "$manifest_content"
    fi
}

# Run main function
main "$@"
