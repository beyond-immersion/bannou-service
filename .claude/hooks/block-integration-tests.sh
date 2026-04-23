#!/bin/bash
#
# block-integration-tests.sh
#
# PreToolUse hook that blocks integration test commands.
# These commands take 5-10 minutes, rebuild containers, and should NEVER
# be run by Claude without explicit user request.
#
# Blocked commands:
#   - make test-http
#   - make test-edge
#   - make test-http-dev
#   - make test-edge-dev
#   - make test-http-logs
#   - make test-edge-logs
#   - make test-infrastructure
#   - make all (includes integration tests)

# Read the hook input from stdin
input=$(cat)

# Extract the command from the JSON input
command=$(echo "$input" | jq -r '.tool_input.command // ""' 2>/dev/null)

# If we couldn't parse the input or there's no command, allow it through
if [[ -z "$command" ]]; then
    exit 0
fi

# Block make test-http variants
if [[ "$command" =~ make[[:space:]]+test-http ]] || \
   [[ "$command" =~ make[[:space:]]+-C[[:space:]]+[^[:space:]]+[[:space:]]+test-http ]]; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "deny",
            permissionDecisionReason: "BLOCKED: Integration tests are NOT allowed without explicit user request.\n\nPer CLAUDE.md:\n- NEVER run make test-http, make test-edge, make test-infrastructure, or make all\n- These take 5-10 minutes and rebuild containers\n- A successful dotnet build is sufficient verification\n- The user will ask for tests when they want tests"
        }
    }'
    exit 0
fi

# Block make test-edge variants
if [[ "$command" =~ make[[:space:]]+test-edge ]] || \
   [[ "$command" =~ make[[:space:]]+-C[[:space:]]+[^[:space:]]+[[:space:]]+test-edge ]]; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "deny",
            permissionDecisionReason: "BLOCKED: Integration tests are NOT allowed without explicit user request.\n\nPer CLAUDE.md:\n- NEVER run make test-http, make test-edge, make test-infrastructure, or make all\n- These take 5-10 minutes and rebuild containers\n- A successful dotnet build is sufficient verification\n- The user will ask for tests when they want tests"
        }
    }'
    exit 0
fi

# Block make test-infrastructure
if [[ "$command" =~ make[[:space:]]+test-infrastructure ]] || \
   [[ "$command" =~ make[[:space:]]+-C[[:space:]]+[^[:space:]]+[[:space:]]+test-infrastructure ]]; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "deny",
            permissionDecisionReason: "BLOCKED: Integration tests are NOT allowed without explicit user request.\n\nPer CLAUDE.md:\n- NEVER run make test-http, make test-edge, make test-infrastructure, or make all\n- These take 5-10 minutes and rebuild containers\n- A successful dotnet build is sufficient verification\n- The user will ask for tests when they want tests"
        }
    }'
    exit 0
fi

# Block make all (includes integration tests)
if [[ "$command" =~ make[[:space:]]+all([[:space:]]|$) ]] || \
   [[ "$command" =~ make[[:space:]]+-C[[:space:]]+[^[:space:]]+[[:space:]]+all([[:space:]]|$) ]]; then
    jq -n '{
        hookSpecificOutput: {
            hookEventName: "PreToolUse",
            permissionDecision: "deny",
            permissionDecisionReason: "BLOCKED: make all includes integration tests and is NOT allowed without explicit user request.\n\nPer CLAUDE.md:\n- NEVER run make test-http, make test-edge, make test-infrastructure, or make all\n- These take 5-10 minutes and rebuild containers\n- A successful dotnet build is sufficient verification\n- The user will ask for tests when they want tests"
        }
    }'
    exit 0
fi

# Command is safe, allow it through
exit 0
