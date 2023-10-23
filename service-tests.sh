#!/bin/bash

apt-get update
apt-get install curl -y
echo "tools installed"

if curl --fail -X GET "127.0.0.1/testing/run-enabled"; then
  exit 0
fi
exit 1
