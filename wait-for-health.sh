#!/bin/bash

ENDPOINT="https://127.0.0.1/health"
MAX_RETRIES=60
RETRY_TIME=5
COUNT=0

while [[ $COUNT -lt $MAX_RETRIES ]]
do
    RESPONSE_CODE=$(curl --connect-timeout 5 -o /dev/null -k -s -w "%{http_code}" $ENDPOINT 2>/dev/null)

    if [[ $RESPONSE_CODE -eq 200 ]]; then
        echo "Service is ready!"
        exit 0
    elif [[ $RESPONSE_CODE -eq 503 ]] || [[ $RESPONSE_CODE -eq 0 ]]; then
        echo "Service is not ready yet. Retrying..."
        sleep $RETRY_TIME
        COUNT=$((COUNT+1))
    else
        echo "Unexpected response code: $RESPONSE_CODE. Exiting."
        exit 1
    fi
done

echo "Service did not become ready in time. Exiting."
exit 1
