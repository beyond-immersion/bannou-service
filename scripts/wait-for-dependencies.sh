#!/bin/sh

# Wait for service dependencies to be ready
# Used by test configurations to ensure infrastructure is ready before starting services

set -e

echo "⏳ Waiting for service dependencies to become ready..."

# Wait for RabbitMQ (needed for Dapr pubsub component)
MAX_ATTEMPTS=30
ATTEMPT=0
while [ $ATTEMPT -lt $MAX_ATTEMPTS ]; do
    if nc -z rabbitmq 5672 2>/dev/null || timeout 1 sh -c "cat < /dev/null > /dev/tcp/rabbitmq/5672" 2>/dev/null; then
        echo "✅ RabbitMQ is ready!"
        break
    fi
    ATTEMPT=$((ATTEMPT + 1))
    if [ $ATTEMPT -eq $MAX_ATTEMPTS ]; then
        echo "❌ RabbitMQ failed to become ready after ${MAX_ATTEMPTS} attempts"
        exit 1
    fi
    echo "   Waiting for RabbitMQ... ($ATTEMPT/$MAX_ATTEMPTS)"
    sleep 2
done

# Wait for Redis (needed for Dapr state store)
ATTEMPT=0
while [ $ATTEMPT -lt $MAX_ATTEMPTS ]; do
    if nc -z bannou-redis 6379 2>/dev/null || timeout 1 sh -c "cat < /dev/null > /dev/tcp/bannou-redis/6379" 2>/dev/null; then
        echo "✅ Redis is ready!"
        break
    fi
    ATTEMPT=$((ATTEMPT + 1))
    if [ $ATTEMPT -eq $MAX_ATTEMPTS ]; then
        echo "❌ Redis failed to become ready after ${MAX_ATTEMPTS} attempts"
        exit 1
    fi
    echo "   Waiting for Redis... ($ATTEMPT/$MAX_ATTEMPTS)"
    sleep 2
done

echo "✅ All dependencies ready!"
