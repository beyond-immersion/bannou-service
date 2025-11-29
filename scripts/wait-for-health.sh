#!/bin/sh

# Wait for bannou health endpoint
# Uses 127.0.0.1 because this script runs with network_mode: service:bannou
ENDPOINT="http://127.0.0.1:80/health"
MAX_RETRIES=60
RETRY_TIME=5
COUNT=0

echo "⏳ Waiting for Bannou service to become ready..."

while [ $COUNT -lt $MAX_RETRIES ]
do
    RESPONSE_CODE=$(curl --connect-timeout 5 -o /dev/null -s -w "%{http_code}" $ENDPOINT 2>/dev/null)

    if [ $RESPONSE_CODE -eq 200 ]; then
        echo "✅ Bannou service is ready!"
        exit 0
    elif [ $RESPONSE_CODE -eq 503 ] || [ $RESPONSE_CODE -eq 0 ]; then
        echo "⏳ Service not ready (status: $RESPONSE_CODE). Retrying in ${RETRY_TIME}s..."
        sleep $RETRY_TIME
        COUNT=$((COUNT+1))
    else
        echo "❌ Unexpected response code: $RESPONSE_CODE. Exiting."
        exit 1
    fi
done

echo "❌ Service did not become ready in time (${MAX_RETRIES} retries). Exiting."
exit 1
