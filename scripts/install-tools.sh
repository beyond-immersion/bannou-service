#!/bin/bash

attempts=${1:-5}
success=0

for (( i=1; i<=attempts; i++ )); do
    if apt-get update && apt-get install -y curl; then
        echo "Curl successfully installed."
        success=1
        break
    else
        echo "Attempt $i of $attempts failed. Retrying in 15 seconds..."
        sleep 15
    fi
done

if [ $success -eq 1 ]; then
    echo "Tools installed successfully."
    exit 0
else
    echo "Failed to install tools after $attempts attempts."
    exit 1
fi
