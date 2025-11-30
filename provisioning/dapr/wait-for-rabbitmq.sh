#!/bin/sh
# Wait for RabbitMQ to be ready before starting Dapr sidecar
# This avoids the need for depends_on in docker-compose while ensuring
# the pubsub component can initialize successfully

RABBITMQ_HOST="${RABBITMQ_HOST:-rabbitmq}"
RABBITMQ_PORT="${RABBITMQ_PORT:-5672}"
MAX_RETRIES="${RABBITMQ_MAX_RETRIES:-30}"
RETRY_INTERVAL="${RABBITMQ_RETRY_INTERVAL:-2}"

echo "Waiting for RabbitMQ at ${RABBITMQ_HOST}:${RABBITMQ_PORT}..."

retry_count=0
while [ $retry_count -lt $MAX_RETRIES ]; do
    # Try to connect to RabbitMQ port using nc (netcat) or timeout with /dev/tcp
    if nc -z "$RABBITMQ_HOST" "$RABBITMQ_PORT" 2>/dev/null; then
        echo "RabbitMQ is ready at ${RABBITMQ_HOST}:${RABBITMQ_PORT}"
        break
    fi

    retry_count=$((retry_count + 1))
    echo "RabbitMQ not ready, retrying... (${retry_count}/${MAX_RETRIES})"
    sleep "$RETRY_INTERVAL"
done

if [ $retry_count -eq $MAX_RETRIES ]; then
    echo "WARNING: RabbitMQ not ready after ${MAX_RETRIES} attempts. Starting Dapr anyway..."
fi

# Start Dapr with all passed arguments
echo "Starting Dapr sidecar..."
exec /daprd "$@"
