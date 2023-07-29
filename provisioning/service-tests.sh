#!/bin/bash

apt-get update
apt-get install curl -y
echo "tools installed"

response=$(curl --silent --show-error --fail -X GET "127.0.0.1/testing/run-enabled")
if [ -z "$response" ]
then
  exit 0
fi
exit 1
