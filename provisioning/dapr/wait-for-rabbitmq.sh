#!/bin/sh
# Wait for infrastructure services to be ready before starting Dapr sidecar
# Total timeout: 60 seconds across all services
# All service names configurable via environment variables

# Service configuration (all configurable via ENV)
# All state stores consolidated to bannou-redis (Redis 8 includes RediSearch built-in)
RABBITMQ_HOST="${RABBITMQ_HOST:-rabbitmq}"
RABBITMQ_PORT="${RABBITMQ_PORT:-5672}"
REDIS_HOST="${REDIS_HOST:-bannou-redis}"
REDIS_PORT="${REDIS_PORT:-6379}"
MYSQL_HOST="${MYSQL_HOST:-account-db}"
MYSQL_PORT="${MYSQL_PORT:-3306}"

# Timeout configuration
TOTAL_TIMEOUT="${TOTAL_TIMEOUT:-60}"
CHECK_INTERVAL="${CHECK_INTERVAL:-1}"

start_time=$(date +%s)

time_remaining() {
    current_time=$(date +%s)
    elapsed=$((current_time - start_time))
    remaining=$((TOTAL_TIMEOUT - elapsed))
    echo $remaining
}

wait_for_service() {
    local host="$1"
    local port="$2"
    local name="$3"

    # Skip if host is empty or explicitly disabled
    if [ -z "$host" ] || [ "$host" = "disabled" ]; then
        echo "Skipping ${name} (disabled)"
        return 0
    fi

    echo "Waiting for ${name} at ${host}:${port}..."

    while [ $(time_remaining) -gt 0 ]; do
        if nc -z "$host" "$port" 2>/dev/null; then
            echo "${name} is ready at ${host}:${port}"
            return 0
        fi
        sleep "$CHECK_INTERVAL"
    done

    echo "WARNING: ${name} not ready within timeout. Starting Dapr anyway..."
    return 1
}

echo "Infrastructure wait script starting (${TOTAL_TIMEOUT}s total timeout)..."

# Wait for RabbitMQ (pub/sub) - required
wait_for_service "$RABBITMQ_HOST" "$RABBITMQ_PORT" "RabbitMQ"

# Wait for Redis (statestore) - required for all state stores
wait_for_service "$REDIS_HOST" "$REDIS_PORT" "Redis"

# Wait for MySQL (accounts-statestore) - optional
wait_for_service "$MYSQL_HOST" "$MYSQL_PORT" "MySQL"

elapsed=$(($(date +%s) - start_time))
echo "Infrastructure ready check completed in ${elapsed}s"

# Start Dapr with all passed arguments
echo "Starting Dapr sidecar..."
exec /daprd "$@"
