#!/bin/bash

# Event subscription code generator
# Usage: ./generate-event-subscriptions.sh <service-name> <schema-file>
# Extracts x-event-subscriptions from {service}-events.yaml (preferred) or
# x-subscribes-to from {service}-api.yaml (legacy fallback) and generates:
# 1. Generated/{Service}EventsController.cs - Dapr topic handlers
# 2. {Service}ServiceEvents.cs - Partial class with handler stubs (only if doesn't exist)

set -e

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
BLUE='\033[0;34m'
NC='\033[0m'

if [ $# -lt 2 ]; then
    echo -e "${RED}Usage: $0 <service-name> <schema-file>${NC}"
    exit 1
fi

SERVICE_NAME="$1"
SCHEMA_FILE="$2"

# Determine the events yaml file path
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
EVENTS_SCHEMA="${SCRIPT_DIR}/../schemas/${SERVICE_NAME}-events.yaml"

# Helper function to convert to PascalCase
to_pascal_case() {
    local input="$1"
    echo "$input" | sed 's/-/ /g' | awk '{for(i=1;i<=NF;i++) $i=toupper(substr($i,1,1)) tolower(substr($i,2))} 1' | sed 's/ //g'
}

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
PROJECT_DIR="../lib-${SERVICE_NAME}"
GENERATED_DIR="${PROJECT_DIR}/Generated"
CONTROLLER_FILE="${GENERATED_DIR}/${SERVICE_PASCAL}EventsController.cs"

# Clean up old .Generated.cs file if it exists (legacy naming)
OLD_CONTROLLER_FILE="${GENERATED_DIR}/${SERVICE_PASCAL}EventsController.Generated.cs"
if [ -f "$OLD_CONTROLLER_FILE" ]; then
    rm "$OLD_CONTROLLER_FILE"
fi
SERVICE_EVENTS_FILE="${PROJECT_DIR}/${SERVICE_PASCAL}ServiceEvents.cs"

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

# Generate the EventsController.cs
echo -e "${YELLOW}🔄 Generating EventsController...${NC}"

# Determine comment based on source file
if [[ "$SOURCE_FILE" == *"-events.yaml" ]]; then
    SOURCE_COMMENT="This file was generated by generate-event-subscriptions.sh from ${SERVICE_NAME}-events.yaml"
    EDIT_COMMENT="To modify event subscriptions, edit x-event-subscriptions in ${SERVICE_NAME}-events.yaml"
else
    SOURCE_COMMENT="This file was generated by generate-event-subscriptions.sh from ${SERVICE_NAME}-api.yaml"
    EDIT_COMMENT="To modify event subscriptions, edit x-subscribes-to in ${SERVICE_NAME}-api.yaml (legacy) or migrate to ${SERVICE_NAME}-events.yaml"
fi

cat > "$CONTROLLER_FILE" << CSHARP_HEADER
// <auto-generated>
// ${SOURCE_COMMENT}
// Do not edit this file directly - changes will be overwritten.
// ${EDIT_COMMENT}
// </auto-generated>

#nullable enable

using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.${SERVICE_PASCAL};

/// <summary>
/// Generated controller for handling pubsub events for ${SERVICE_PASCAL} service.
/// Dispatches events to registered handlers via IEventConsumer.
/// </summary>
[ApiController]
[Route("[controller]")]
public class ${SERVICE_PASCAL}EventsController : ControllerBase
{
    private readonly ILogger<${SERVICE_PASCAL}EventsController> _logger;
    private readonly IEventConsumer _eventConsumer;

    /// <summary>
    /// Creates a new ${SERVICE_PASCAL}EventsController instance.
    /// </summary>
    public ${SERVICE_PASCAL}EventsController(
        ILogger<${SERVICE_PASCAL}EventsController> logger,
        IEventConsumer eventConsumer)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventConsumer = eventConsumer ?? throw new ArgumentNullException(nameof(eventConsumer));
    }
CSHARP_HEADER

# Generate handler methods for each subscription
echo "$SUBSCRIPTIONS_JSON" | python3 -c "
import sys
import json

data = json.load(sys.stdin)
service_pascal = '$SERVICE_PASCAL'

for sub in data:
    topic = sub.get('topic', '')
    event_type = sub.get('event', '')
    handler = sub.get('handler', '')

    if not topic or not event_type:
        continue

    # Convert topic to route and method names
    route_name = 'handle-' + topic.replace('.', '-')
    # Split on both . and - to create valid C# method name
    import re
    parts = re.split(r'[.\-]', topic)
    method_name = 'Handle' + ''.join(word.capitalize() for word in parts) + 'Async'

    print(f'''
    /// <summary>
    /// Handle {topic} events via Dapr pubsub.
    /// Dispatches to all registered handlers via IEventConsumer.
    /// </summary>
    [Topic(\"bannou-pubsub\", \"{topic}\")]
    [HttpPost(\"{route_name}\")]
    public async Task<IActionResult> {method_name}()
    {{
        try
        {{
            var evt = await DaprEventHelper.ReadEventAsync<{event_type}>(Request);

            if (evt == null)
            {{
                _logger.LogWarning(\"Failed to parse {event_type} from request body\");
                return BadRequest(\"Invalid event data\");
            }}

            _logger.LogDebug(\"Dispatching {topic} event\");

            await _eventConsumer.DispatchAsync(\"{topic}\", evt, HttpContext.RequestServices);

            return Ok();
        }}
        catch (Exception ex)
        {{
            _logger.LogError(ex, \"Failed to dispatch {topic} event\");
            return StatusCode(500, \"Internal server error processing event\");
        }}
    }}''')
" >> "$CONTROLLER_FILE"

# Close the controller class
cat >> "$CONTROLLER_FILE" << 'CSHARP_FOOTER'
}
CSHARP_FOOTER

echo -e "${GREEN}✅ Generated: $CONTROLLER_FILE${NC}"

# Generate {Service}ServiceEvents.cs (only if it doesn't exist)
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
    /// Registers event consumers for Dapr pub/sub events this service handles.
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
