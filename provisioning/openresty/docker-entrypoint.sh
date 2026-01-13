#!/bin/bash
# Docker entrypoint for OpenResty with config generation
# Generates configs from templates, then starts nginx
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

echo "=== Bannou OpenResty Entrypoint ==="

# Generate configs from templates
if [ -f "$SCRIPT_DIR/generate-configs.sh" ]; then
    echo "Running config generation..."
    bash "$SCRIPT_DIR/generate-configs.sh"
else
    echo "Warning: generate-configs.sh not found, skipping config generation"
fi

# Validate nginx config
echo ""
echo "Validating nginx configuration..."
if nginx -t; then
    echo "Configuration valid"
else
    echo "Configuration validation failed!"
    exit 1
fi

# Start OpenResty
echo ""
echo "Starting OpenResty..."
exec openresty -g "daemon off;"
