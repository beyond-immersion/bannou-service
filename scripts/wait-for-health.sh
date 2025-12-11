#!/bin/sh

# Wait for bannou and Dapr sidecar health endpoints
# Uses Docker DNS for service discovery (standalone Dapr containers)

# Service hosts - use Docker DNS names (passed via environment or defaults)
BANNOU_HOST="${BANNOU_HOST:-bannou}"
DAPR_HOST="${DAPR_HOST:-bannou-dapr}"

BANNOU_ENDPOINT="http://${BANNOU_HOST}:80/health"
DAPR_ENDPOINT="http://${DAPR_HOST}:3500/v1.0/healthz"
MAX_RETRIES=60
RETRY_TIME=5

# Wait for Bannou service
wait_for_service() {
    local SERVICE_NAME="$1"
    local ENDPOINT="$2"
    local EXPECTED_CODE="$3"
    local COUNT=0

    echo "⏳ Waiting for $SERVICE_NAME to become ready..."

    while [ $COUNT -lt $MAX_RETRIES ]
    do
        RESPONSE_CODE=$(curl --connect-timeout 5 -o /dev/null -s -w "%{http_code}" "$ENDPOINT" 2>/dev/null)

        if [ "$RESPONSE_CODE" = "$EXPECTED_CODE" ]; then
            echo "✅ $SERVICE_NAME is ready!"
            return 0
        elif [ "$RESPONSE_CODE" = "503" ] || [ "$RESPONSE_CODE" = "000" ] || [ "$RESPONSE_CODE" = "0" ]; then
            echo "⏳ $SERVICE_NAME not ready (status: $RESPONSE_CODE). Retrying in ${RETRY_TIME}s..."
            sleep $RETRY_TIME
            COUNT=$((COUNT+1))
        else
            echo "❌ $SERVICE_NAME unexpected response code: $RESPONSE_CODE. Exiting."
            return 1
        fi
    done

    echo "❌ $SERVICE_NAME did not become ready in time (${MAX_RETRIES} retries). Exiting."
    return 1
}

# Wait for Bannou service (expects 200)
wait_for_service "Bannou service" "$BANNOU_ENDPOINT" "200" || exit 1

# Wait for Dapr sidecar (expects 204 - No Content means healthy)
wait_for_service "Dapr sidecar" "$DAPR_ENDPOINT" "204" || exit 1

echo "✅ All services ready!"
exit 0
