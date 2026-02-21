#!/bin/bash

# list-services.sh
# Shows available services that can be built as plugins

echo "📋 Available services for plugin-based builds:"

# Find all projects with ServiceLib property
found_services=()

while IFS= read -r -d '' proj; do
    service_name=$(xmllint --xpath "Project/PropertyGroup/ServiceLib/text()" "$proj" 2>/dev/null)

    if [ -n "$service_name" ] && [ "$service_name" != "unknown" ]; then
        project_dir="$(dirname "$proj")"
        project_name="$(basename "$project_dir")"

        echo "  🔧 $service_name (from $project_name)"
        found_services+=("$service_name")
    fi
done < <(find ./plugins -name '*.csproj' -print0 | xargs -0 grep -l '<ServiceLib>' | tr '\n' '\0')

echo ""

if [ ${#found_services[@]} -eq 0 ]; then
    echo "⚠️  No services with <ServiceLib> property found"
    echo "💡 Make sure your service projects have the <ServiceLib>service-name</ServiceLib> property"
else
    echo "📊 Total services found: ${#found_services[@]}"
    echo "🏷️  Service names: ${found_services[*]}"
fi

echo ""
echo "📖 Usage examples:"
echo "  make build-plugins SERVICES=\"auth account\""
echo "  make build-compose-services SERVICES=\"auth account connect\""
echo "  make validate-compose-services SERVICES=\"auth account connect\""
echo "  docker build --build-arg BANNOU_SERVICES=\"auth account\" -f provisioning/Dockerfile ."
