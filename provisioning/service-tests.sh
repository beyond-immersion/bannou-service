#!/bin/bash

apt-get update
apt-get upgrade -y
apt-get install curl -y
echo "tools installed"

response=$(curl --silent --show-error --fail -X GET "127.0.0.1/testing/run/basic")
if [$response == 200]; then
  exit
fi
exit 1
