#!/bin/bash

apt-get update
apt-get install curl -y
apt-get install jq -y
echo "tools installed"

if curl --fail -X GET "127.0.0.1/testing/run-enabled"; then
  exit 1
fi
exit 0
