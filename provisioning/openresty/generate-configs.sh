#!/bin/bash
# Generate nginx configs from templates using environment variables
# Run this script before starting OpenResty
set -e

# Default values matching Bannou defaults
export BANNOU_SERVICE_DOMAIN="${BANNOU_SERVICE_DOMAIN:-localhost}"
export ASSET_STORAGE_ENDPOINT="${ASSET_STORAGE_ENDPOINT:-minio:9000}"
export SSL_CERT_PATH="${SSL_CERT_PATH:-/etc/nginx/ssl/cert.pem}"
export SSL_KEY_PATH="${SSL_KEY_PATH:-/etc/nginx/ssl/key.pem}"

# Resolve script directory
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
TEMPLATE_DIR="$SCRIPT_DIR/templates"
OUTPUT_DIR="$SCRIPT_DIR/generated"

# Create output directory
mkdir -p "$OUTPUT_DIR"

echo "=== OpenResty Config Generation ==="
echo "BANNOU_SERVICE_DOMAIN: $BANNOU_SERVICE_DOMAIN"
echo "ASSET_STORAGE_ENDPOINT: $ASSET_STORAGE_ENDPOINT"
echo "SSL_CERT_PATH: $SSL_CERT_PATH"
echo "SSL_KEY_PATH: $SSL_KEY_PATH"
echo ""

# Variables to substitute (must be explicitly listed for envsubst)
VARS='${BANNOU_SERVICE_DOMAIN} ${ASSET_STORAGE_ENDPOINT} ${SSL_CERT_PATH} ${SSL_KEY_PATH}'

# Process each template
for template in "$TEMPLATE_DIR"/*.conf.template; do
    [ -f "$template" ] || continue

    output_name="$(basename "$template" .template)"
    output_path="$OUTPUT_DIR/$output_name"

    echo "Generating: $output_name"
    envsubst "$VARS" < "$template" > "$output_path"
done

echo ""
echo "=== Generation Complete ==="
echo "Generated configs in: $OUTPUT_DIR"
ls -la "$OUTPUT_DIR"/*.conf 2>/dev/null || echo "(no configs generated)"
