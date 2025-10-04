#!/bin/sh

# Wait for RabbitMQ to be ready before starting Dapr
# This prevents component initialization failures
# Simple fixed sleep approach (RabbitMQ healthcheck ensures it's ready)

echo "⏳ Waiting 10 seconds for RabbitMQ to become ready..."
sleep 10
echo "✅ RabbitMQ should be ready now!"
