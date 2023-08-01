#!/bin/bash

apt-get update
apt-get install curl -y
echo "tools installed"

if curl -k --fail -X GET "https://127.0.0.1:80/testing/run-enabled"; then
  exit 0
fi
exit 1
