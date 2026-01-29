#!/bin/bash
#
# block-production-deploy.sh
#
# PreToolUse hook that blocks AI agents from running production deployment
# and publishing commands. These commands push artifacts to production
# registries and should ONLY be run by humans.
#
# Blocked commands:
#   - make push-release (promotes development image to Docker Hub)
#   - make publish-sdk-ts (publishes TypeScript SDK to npm)
#
# If the developer asks about deploying/publishing, return the command for
# them to run manually rather than executing it.

# Read the hook input from stdin
input=$(cat)

# Extract the command from the JSON input
command=$(echo "$input" | jq -r '.tool_input.command // ""' 2>/dev/null)

# If we couldn't parse the input or there's no command, allow it through
if [[ -z "$command" ]]; then
    exit 0
fi

# Block make push-release (Docker production deployment)
if [[ "$command" =~ make[[:space:]]+push-release ]] || \
   [[ "$command" =~ make[[:space:]].*push-release ]]; then
    cat <<'ENDJSON'
{
  "decision": "block",
  "reason": "🛑 STOP! Production deployment commands are BLOCKED for AI agents!\n\n`make push-release` deploys Docker images to production (Docker Hub).\n\nThis command should ONLY be run by a human developer.\n\nIf the developer asked you to deploy:\n  Tell them to run this command themselves:\n  \n    make push-release\n\nDo NOT attempt to run this command or any variation of it."
}
ENDJSON
    exit 0
fi

# Block make publish-sdk-ts (npm publishing)
if [[ "$command" =~ make[[:space:]]+publish-sdk-ts ]] || \
   [[ "$command" =~ make[[:space:]].*publish-sdk-ts ]]; then
    cat <<'ENDJSON'
{
  "decision": "block",
  "reason": "🛑 STOP! NPM publishing commands are BLOCKED for AI agents!\n\n`make publish-sdk-ts` publishes TypeScript SDK packages to npm.\n\nThis command should ONLY be run by a human developer.\n\nIf the developer asked you to publish:\n  Tell them to run this command themselves:\n  \n    make publish-sdk-ts\n\nDo NOT attempt to run this command or any variation of it."
}
ENDJSON
    exit 0
fi

# Command is safe, allow it through
exit 0
