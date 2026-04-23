#!/bin/bash
# Forward user prompts and assistant responses to the dashboard emitter.
# Reads hook JSON from stdin, POSTs to the HTTP ingest endpoint.
# Fire-and-forget — exit 0 always. Dashboard being down must never block Claude.

BUCKET="${BANNOU_MCP_BUCKET:-1}"
PORT=$((9600 + BUCKET))

INPUT=$(cat)

curl -s -X POST "http://localhost:${PORT}/event" \
  -H "Content-Type: application/json" \
  -d "$INPUT" \
  > /dev/null 2>&1 || true

exit 0
