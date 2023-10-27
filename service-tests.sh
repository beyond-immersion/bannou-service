#!/bin/bash

set -e

for i in {1..5}; do
    apt-get update && apt-get install -y curl && break || sleep 15
done
echo "tools installed"

if curl --fail -X GET "127.0.0.1/testing/run-enabled"; then
  exit 0
fi
exit 1
