#!/bin/bash
# Generate nginx configs from templates using environment variables
# Run this script before starting OpenResty
set -e

# Default values matching Bannou defaults
export BANNOU_SERVICE_DOMAIN="${BANNOU_SERVICE_DOMAIN:-localhost}"
export ASSET_STORAGE_ENDPOINT="${ASSET_STORAGE_ENDPOINT:-minio:9000}"
# Default SSL paths match docker-compose.ingress.yml mount: ./certificates:/certs
export SSL_CERT_PATH="${SSL_CERT_PATH:-/certs/cert.pem}"
export SSL_KEY_PATH="${SSL_KEY_PATH:-/certs/key.pem}"

# Resolve script directory
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
TEMPLATE_DIR="$SCRIPT_DIR/templates"
OUTPUT_DIR="$SCRIPT_DIR/generated"

# Check if SSL certificates exist (local path: ../certificates/ maps to /certs/ in container)
LOCAL_CERT_PATH="$SCRIPT_DIR/../certificates/cert.pem"
SSL_AVAILABLE=false
if [ -f "$LOCAL_CERT_PATH" ]; then
    SSL_AVAILABLE=true
fi

# Create output directory
mkdir -p "$OUTPUT_DIR"

echo "=== OpenResty Config Generation ==="
echo "BANNOU_SERVICE_DOMAIN: $BANNOU_SERVICE_DOMAIN"
echo "ASSET_STORAGE_ENDPOINT: $ASSET_STORAGE_ENDPOINT"
echo "SSL_CERT_PATH: $SSL_CERT_PATH"
echo "SSL_KEY_PATH: $SSL_KEY_PATH"
echo "SSL_AVAILABLE: $SSL_AVAILABLE"
echo ""

# Variables to substitute (must be explicitly listed for envsubst)
VARS='${BANNOU_SERVICE_DOMAIN} ${ASSET_STORAGE_ENDPOINT} ${SSL_CERT_PATH} ${SSL_KEY_PATH}'

# Templates that require SSL certificates
SSL_TEMPLATES="ssl-termination.conf.template storage-proxy.conf.template"

# Process each template
for template in "$TEMPLATE_DIR"/*.conf.template; do
    [ -f "$template" ] || continue

    template_name="$(basename "$template")"
    output_name="$(basename "$template" .template)"
    output_path="$OUTPUT_DIR/$output_name"

    # Skip SSL-requiring templates if certs don't exist
    if [ "$SSL_AVAILABLE" = false ]; then
        case "$SSL_TEMPLATES" in
            *"$template_name"*)
                echo "Skipping (no SSL certs): $output_name"
                # Remove any previously generated SSL config
                rm -f "$output_path"
                continue
                ;;
        esac
    fi

    echo "Generating: $output_name"
    envsubst "$VARS" < "$template" > "$output_path"
done

echo ""
echo "=== Generation Complete ==="
echo "Generated configs in: $OUTPUT_DIR"
ls -la "$OUTPUT_DIR"/*.conf 2>/dev/null || echo "(no configs generated)"
