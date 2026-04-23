#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────
# UserPromptSubmit hook: detect pending sentinel injection.
#
# Checks if the bucket-specific sentinel file exists. If so, outputs
# context that tells the agent to call an MCP tool to trigger processing.
#
# Uses BANNOU_MCP_BUCKET env var (default: 1) to check the right file.
# Cost: one `test -f` per user prompt (negligible).
# ─────────────────────────────────────────────────────────────────────────

BUCKET="${BANNOU_MCP_BUCKET:-1}"
SENTINEL="/tmp/bannou-mcp-inject-${BUCKET}.json"

if [ ! -f "$SENTINEL" ]; then
  exit 0
fi

# Sentinel exists — peek at the message if there is one
MSG=$(jq -r '.message // empty' "$SENTINEL" 2>/dev/null)

if [ -n "$MSG" ]; then
  echo "📨 External injection pending: $MSG — Call any MCP tool (e.g., run_command) to process it."
else
  echo "📨 External injection pending — Call any MCP tool (e.g., run_command) to process it."
fi

# Audible confirmation via Windows TTS (WSL2 interop) — fire and forget
if command -v powershell.exe &>/dev/null; then
  powershell.exe -Command "Add-Type -AssemblyName System.Speech; (New-Object System.Speech.Synthesis.SpeechSynthesizer).Speak('Dev context loaded, bucket ${BUCKET}')" &>/dev/null &
fi

exit 0
