#!/bin/sh

set -e

echo "Running integration tests..."

if curl --fail -X GET "127.0.0.1/testing/run-enabled"; then
  echo "Integration test passed!"
  exit 0
fi
echo "Integration test failed!"
exit 1
