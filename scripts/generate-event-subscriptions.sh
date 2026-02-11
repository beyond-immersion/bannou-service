#!/bin/bash

# ⛔⛔⛔ AGENT MODIFICATION PROHIBITED ⛔⛔⛔
# This script is part of Bannou's code generation pipeline.
# DO NOT MODIFY without EXPLICIT user instructions to change code generation.
#
# Changes to generation scripts silently break builds across ALL 48 services.
# An agent once changed namespace strings across 4 scripts in a single commit,
# breaking every service. If you believe a change is needed:
#   1. STOP and explain what you think is wrong
#   2. Show the EXACT diff you propose
#   3. Wait for EXPLICIT approval before touching ANY generation script
#
# This applies to: namespace strings, output paths, exclusion logic,
# NSwag parameters, post-processing steps, and file naming conventions.
# ⛔⛔⛔ AGENT MODIFICATION PROHIBITED ⛔⛔⛔

# Event subscription code generator
# Usage: ./generate-event-subscriptions.sh <service-name> <schema-file>
# Extracts x-event-subscriptions from {service}-events.yaml (preferred) or
# x-subscribes-to from {service}-api.yaml (legacy fallback) and generates:
# - {Service}ServiceEvents.cs - Partial class with handler stubs (only if doesn't exist)
#
# NOTE: EventsController generation was removed - events flow via IEventConsumer/MassTransit,
# not HTTP endpoints. The [Topic] attribute and HTTP event controllers are dead code.

set -e

# Change to scripts directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Source common utilities
source "./common.sh"

if [ $# -lt 2 ]; then
    echo -e "${RED}Usage: $0 <service-name> <schema-file>${NC}"
    exit 1
fi

SERVICE_NAME="$1"
SCHEMA_FILE="$2"

# Determine the events yaml file path
EVENTS_SCHEMA="${SCRIPT_DIR}/../schemas/${SERVICE_NAME}-events.yaml"

# Helper function to convert topic to route name (e.g., "session.connected" -> "handle-session-connected")
topic_to_route() {
    local topic="$1"
    echo "handle-$(echo "$topic" | tr '.' '-')"
}

# Helper function to convert topic to method name (e.g., "session.connected" -> "HandleSessionConnectedAsync")
topic_to_method() {
    local topic="$1"
    local parts=$(echo "$topic" | tr '.' ' ')
    local result="Handle"
    for part in $parts; do
        result="${result}$(echo "${part:0:1}" | tr '[:lower:]' '[:upper:]')${part:1}"
    done
    echo "${result}Async"
}

SERVICE_PASCAL=$(to_pascal_case "$SERVICE_NAME")
PROJECT_DIR="../plugins/lib-${SERVICE_NAME}"
GENERATED_DIR="${PROJECT_DIR}/Generated"
SERVICE_EVENTS_FILE="${PROJECT_DIR}/${SERVICE_PASCAL}ServiceEvents.cs"

# Clean up dead EventsController files if they exist (no longer generated)
OLD_CONTROLLER_FILE="${GENERATED_DIR}/${SERVICE_PASCAL}EventsController.cs"
OLD_CONTROLLER_FILE_LEGACY="${GENERATED_DIR}/${SERVICE_PASCAL}EventsController.Generated.cs"
if [ -f "$OLD_CONTROLLER_FILE" ]; then
    rm "$OLD_CONTROLLER_FILE"
    echo -e "${YELLOW}  🗑️  Removed dead EventsController: $OLD_CONTROLLER_FILE${NC}"
fi
if [ -f "$OLD_CONTROLLER_FILE_LEGACY" ]; then
    rm "$OLD_CONTROLLER_FILE_LEGACY"
fi

echo -e "${BLUE}📡 Generating event subscriptions for: $SERVICE_NAME${NC}"

# Check if schema file exists
if [ ! -f "$SCHEMA_FILE" ]; then
    echo -e "${RED}❌ Schema file not found: $SCHEMA_FILE${NC}"
    exit 1
fi

# Check if project directory exists
if [ ! -d "$PROJECT_DIR" ]; then
    echo -e "${YELLOW}⚠️  Project directory not found: $PROJECT_DIR - skipping event subscription generation${NC}"
    exit 0
fi

# Ensure Generated directory exists
mkdir -p "$GENERATED_DIR"

# Use Python to parse YAML and extract x-event-subscriptions (preferred) or x-subscribes-to (legacy)
PYTHON_SCRIPT=$(cat << 'PYEOF'
import sys
import json
import os

try:
    import yaml
except ImportError:
    print("PYYAML_NOT_AVAILABLE", file=sys.stderr)
    sys.exit(2)

events_schema_file = sys.argv[1]  # events yaml (preferred source)
api_schema_file = sys.argv[2]      # api yaml (fallback source)

subscriptions = []
source_file = None

# First, try to read x-event-subscriptions from events yaml
if os.path.exists(events_schema_file):
    try:
        with open(events_schema_file, 'r') as f:
            schema = yaml.safe_load(f)
        info = schema.get('info', {})
        subs = info.get('x-event-subscriptions', [])
        if subs:
            subscriptions = subs
            source_file = events_schema_file
    except Exception:
        pass

# Fall back to x-subscribes-to from api yaml (legacy pattern)
if not subscriptions and os.path.exists(api_schema_file):
    try:
        with open(api_schema_file, 'r') as f:
            schema = yaml.safe_load(f)
        info = schema.get('info', {})
        subs = info.get('x-subscribes-to', [])
        if subs:
            subscriptions = subs
            source_file = api_schema_file
    except Exception:
        pass

# Output as JSON with source file info
result = {
    'subscriptions': subscriptions,
    'source': source_file
}
print(json.dumps(result))
PYEOF
)

# Try Python extraction
SUBSCRIPTIONS_RESULT=""
SUBSCRIPTIONS_JSON=""
SOURCE_FILE=""
if command -v python3 &> /dev/null; then
    SUBSCRIPTIONS_RESULT=$(echo "$PYTHON_SCRIPT" | python3 - "$EVENTS_SCHEMA" "$SCHEMA_FILE" 2>/dev/null) || true
    if [ -n "$SUBSCRIPTIONS_RESULT" ]; then
        SUBSCRIPTIONS_JSON=$(echo "$SUBSCRIPTIONS_RESULT" | python3 -c "import sys,json; print(json.dumps(json.load(sys.stdin).get('subscriptions', [])))" 2>/dev/null) || true
        SOURCE_FILE=$(echo "$SUBSCRIPTIONS_RESULT" | python3 -c "import sys,json; print(json.load(sys.stdin).get('source', '') or '')" 2>/dev/null) || true
    fi
fi

# If no subscriptions found, exit gracefully
if [ -z "$SUBSCRIPTIONS_JSON" ] || [ "$SUBSCRIPTIONS_JSON" == "[]" ]; then
    # Check both files for subscription definitions
    HAS_EVENTS_SUBS=false
    HAS_API_SUBS=false
    if [ -f "$EVENTS_SCHEMA" ] && grep -q "x-event-subscriptions:" "$EVENTS_SCHEMA" 2>/dev/null; then
        HAS_EVENTS_SUBS=true
    fi
    if grep -q "x-subscribes-to:" "$SCHEMA_FILE" 2>/dev/null; then
        HAS_API_SUBS=true
    fi

    if [ "$HAS_EVENTS_SUBS" = false ] && [ "$HAS_API_SUBS" = false ]; then
        echo -e "${YELLOW}  No event subscriptions found - skipping${NC}"
        exit 0
    else
        echo -e "${YELLOW}  Could not parse event subscriptions (Python/PyYAML required)${NC}"
        exit 0
    fi
fi

# Report which source was used
if [ -n "$SOURCE_FILE" ]; then
    SOURCE_BASENAME=$(basename "$SOURCE_FILE")
    echo -e "  📄 Source: $SOURCE_BASENAME"
fi

# Count subscriptions
SUBSCRIPTION_COUNT=$(echo "$SUBSCRIPTIONS_JSON" | python3 -c "import sys,json; print(len(json.load(sys.stdin)))" 2>/dev/null || echo "0")
echo -e "  📊 Found $SUBSCRIPTION_COUNT event subscription(s)"

# Generate {Service}ServiceEvents.cs (only if it doesn't exist)
# NOTE: EventsController generation was removed - events flow via IEventConsumer/MassTransit
if [ -f "$SERVICE_EVENTS_FILE" ]; then
    echo -e "${YELLOW}📝 ServiceEvents file already exists, skipping: $SERVICE_EVENTS_FILE${NC}"
    echo -e "${YELLOW}    To regenerate, delete the file and run this script again.${NC}"
else
    echo -e "${YELLOW}🔄 Generating ServiceEvents partial class...${NC}"

    cat > "$SERVICE_EVENTS_FILE" << CSHARP_EVENTS_HEADER
using BeyondImmersion.BannouService.Events;

namespace BeyondImmersion.BannouService.${SERVICE_PASCAL};

/// <summary>
/// Partial class for ${SERVICE_PASCAL}Service event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class ${SERVICE_PASCAL}Service
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
CSHARP_EVENTS_HEADER

    # Generate registration calls
    echo "$SUBSCRIPTIONS_JSON" | python3 -c "
import sys
import json

data = json.load(sys.stdin)
service_pascal = '$SERVICE_PASCAL'

for sub in data:
    topic = sub.get('topic', '')
    event_type = sub.get('event', '')
    handler = sub.get('handler', '')

    if not topic or not event_type or not handler:
        continue

    print(f'        eventConsumer.RegisterHandler<I{service_pascal}Service, {event_type}>(')
    print(f'            \"{topic}\",')
    print(f'            async (svc, evt) => await (({service_pascal}Service)svc).{handler}Async(evt));')
    print()
" >> "$SERVICE_EVENTS_FILE"

    cat >> "$SERVICE_EVENTS_FILE" << 'CSHARP_EVENTS_MIDDLE'
    }
CSHARP_EVENTS_MIDDLE

    # Generate handler method stubs
    echo "$SUBSCRIPTIONS_JSON" | python3 -c "
import sys
import json

data = json.load(sys.stdin)

for sub in data:
    topic = sub.get('topic', '')
    event_type = sub.get('event', '')
    handler = sub.get('handler', '')

    if not topic or not event_type or not handler:
        continue

    print(f'''
    /// <summary>
    /// Handles {topic} events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name=\"evt\">The event data.</param>
    public Task {handler}Async({event_type} evt)
    {{
        // TODO: Implement {topic} event handling
        _logger.LogInformation(\"[EVENT] Received {topic} event\");
        return Task.CompletedTask;
    }}''')
" >> "$SERVICE_EVENTS_FILE"

    # Close the class
    cat >> "$SERVICE_EVENTS_FILE" << 'CSHARP_EVENTS_FOOTER'
}
CSHARP_EVENTS_FOOTER

    echo -e "${GREEN}✅ Generated: $SERVICE_EVENTS_FILE${NC}"
fi

echo -e "${GREEN}✅ Event subscription generation completed${NC}"
