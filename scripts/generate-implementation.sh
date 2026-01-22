#!/bin/bash

# Generate default service implementation template (if it doesn't exist)
# Usage: ./generate-implementation.sh <service-name> [schema-file]

set -e  # Exit on any error

# Source common utilities
source "$(dirname "$0")/common.sh"

# Validate arguments
if [ $# -lt 1 ]; then
    log_error "Usage: $0 <service-name> [schema-file]"
    echo "Example: $0 account"
    echo "Example: $0 account ../schemas/account-api.yaml"
    exit 1
fi

SERVICE_NAME="$1"
SCHEMA_FILE="${2:-../schemas/${SERVICE_NAME}-api.yaml}"

SERVICE_PASCAL=$(to_pascal_case "$SERVICE_NAME")
SERVICE_DIR="../plugins/lib-${SERVICE_NAME}"
IMPLEMENTATION_FILE="$SERVICE_DIR/${SERVICE_PASCAL}Service.cs"

echo -e "${YELLOW}🔧 Generating service implementation for: $SERVICE_NAME${NC}"
echo -e "  📋 Schema: $SCHEMA_FILE"
echo -e "  📁 Output: $IMPLEMENTATION_FILE"

# Validate schema file exists
if [ ! -f "$SCHEMA_FILE" ]; then
    echo -e "${RED}❌ Schema file not found: $SCHEMA_FILE${NC}"
    exit 1
fi

# Check if implementation already exists
if [ -f "$IMPLEMENTATION_FILE" ]; then
    echo -e "${YELLOW}📝 Service implementation already exists, skipping: $IMPLEMENTATION_FILE${NC}"
    exit 0
fi

# Ensure service directory exists
mkdir -p "$SERVICE_DIR"

echo -e "${YELLOW}🔄 Creating service implementation template...${NC}"

# Create service implementation template
cat > "$IMPLEMENTATION_FILE" << EOF
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-${SERVICE_NAME}.tests")]

namespace BeyondImmersion.BannouService.$SERVICE_PASCAL;

/// <summary>
/// Implementation of the $SERVICE_PASCAL service.
/// This class contains the business logic for all $SERVICE_PASCAL operations.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
/// Generated code (event handlers, permissions) is placed in companion partial classes.
/// </para>
/// <para>
/// <b>RELATED FILES:</b>
/// <list type="bullet">
///   <item>Request/Response models: bannou-service/Generated/Models/${SERVICE_PASCAL}Models.cs</item>
///   <item>Event models: bannou-service/Generated/Events/${SERVICE_PASCAL}EventsModels.cs</item>
///   <item>Lifecycle events: bannou-service/Generated/Events/${SERVICE_PASCAL}LifecycleEvents.cs</item>
/// </list>
/// </para>
/// <para>
/// Standard structure:
/// <list type="bullet">
///   <item>${SERVICE_PASCAL}Service.cs (this file) - Business logic</item>
///   <item>${SERVICE_PASCAL}ServiceEvents.cs - Event consumer handlers (generated)</item>
///   <item>Generated/${SERVICE_PASCAL}PermissionRegistration.cs - Permission registration (generated)</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("$SERVICE_NAME", typeof(I${SERVICE_PASCAL}Service), lifetime: ServiceLifetime.Scoped)]
public partial class ${SERVICE_PASCAL}Service : I${SERVICE_PASCAL}Service
{
    private readonly IMessageBus _messageBus;
    private readonly IServiceNavigator _navigator;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<${SERVICE_PASCAL}Service> _logger;
    private readonly ${SERVICE_PASCAL}ServiceConfiguration _configuration;

    private const string STATE_STORE = "${SERVICE_NAME}-statestore";

    public ${SERVICE_PASCAL}Service(
        IMessageBus messageBus,
        IServiceNavigator navigator,
        IStateStoreFactory stateStoreFactory,
        ILogger<${SERVICE_PASCAL}Service> logger,
        ${SERVICE_PASCAL}ServiceConfiguration configuration)
    {
        _messageBus = messageBus;
        _navigator = navigator;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;
    }

EOF

# Generate implementation methods directly from schema (schema-first approach)
echo -e "${YELLOW}🔄 Generating implementation methods from schema...${NC}"

python3 -c "
import re
import sys
import yaml

schema_file = '$SCHEMA_FILE'

def convert_openapi_type_to_csharp(openapi_type, format_type=None, nullable=False, items=None):
    '''Convert OpenAPI type to C# type'''
    type_mapping = {
        'string': 'string',
        'integer': 'int',
        'number': 'double',
        'boolean': 'bool',
        'array': 'ICollection',
        'object': 'object'
    }

    if openapi_type == 'string' and format_type == 'uuid':
        base_type = 'Guid'
    elif openapi_type == 'array' and items:
        item_type = convert_openapi_type_to_csharp(items.get('type', 'object'))
        base_type = f'ICollection<{item_type}>'
    else:
        base_type = type_mapping.get(openapi_type, 'object')

    # Handle nullable types
    if nullable and base_type not in ['string', 'object'] and not base_type.startswith('ICollection'):
        base_type += '?'

    return base_type

def convert_operation_id_to_method_name(operation_id):
    '''Convert camelCase operationId to PascalCase method name'''
    return operation_id[0].upper() + operation_id[1:] if operation_id else ''

try:
    with open(schema_file, 'r') as schema_f:
        schema = yaml.safe_load(schema_f)

    if 'paths' not in schema:
        print('    # Warning: No paths found in schema', file=sys.stderr)
        sys.exit(0)

    # Process each path and method
    for path, path_data in schema['paths'].items():
        for http_method, method_data in path_data.items():
            if not isinstance(method_data, dict):
                continue

            operation_id = method_data.get('operationId', '')
            if not operation_id:
                continue

            method_name = convert_operation_id_to_method_name(operation_id)

            # Skip methods marked as controller-only
            if method_data.get('x-controller-only') is True:
                continue

            # Determine return type from responses
            return_type = 'object'
            if 'responses' in method_data:
                success_responses = ['200', '201', '202']
                for status_code in success_responses:
                    if status_code in method_data['responses']:
                        response = method_data['responses'][status_code]
                        if 'content' in response and 'application/json' in response['content']:
                            content_schema = response['content']['application/json'].get('schema', {})
                            if '\$ref' in content_schema:
                                # Extract model name from reference
                                ref_parts = content_schema['\$ref'].split('/')
                                return_type = ref_parts[-1] if ref_parts else 'object'
                            elif 'type' in content_schema:
                                return_type = convert_openapi_type_to_csharp(
                                    content_schema['type'],
                                    content_schema.get('format'),
                                    content_schema.get('nullable', False),
                                    content_schema.get('items')
                                )
                        break

            # Build parameter list
            param_parts = []
            if 'parameters' in method_data:
                for param in method_data['parameters']:
                    param_name = param.get('name', '')
                    param_schema = param.get('schema', {})
                    param_type = convert_openapi_type_to_csharp(
                        param_schema.get('type', 'string'),
                        param_schema.get('format'),
                        not param.get('required', True),  # If not required, make nullable
                        param_schema.get('items')
                    )

                    # For implementation, remove default values - just the parameter type and name
                    param_parts.append(f'{param_type} {param_name}')

            # Handle request body parameter
            if 'requestBody' in method_data:
                request_body = method_data['requestBody']
                if 'content' in request_body and 'application/json' in request_body['content']:
                    content_schema = request_body['content']['application/json'].get('schema', {})
                    if '\$ref' in content_schema:
                        # Extract model name from reference
                        ref_parts = content_schema['\$ref'].split('/')
                        body_type = ref_parts[-1] if ref_parts else 'object'
                        param_parts.append(f'{body_type} body')

            # Always add CancellationToken
            param_parts.append('CancellationToken cancellationToken')

            # Generate implementation method stub
            params_str = ', '.join(param_parts)

            print(f'''    /// <summary>
    /// Implementation of {method_name} operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, {return_type})> {method_name}Async({params_str})
    {{
        _logger.LogInformation(\"Executing {method_name} operation\");

        try
        {{
            // TODO: Implement your business logic here
            throw new NotImplementedException(\"Method {method_name} not yet implemented\");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync(\"topic.name\", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh via IServiceNavigator):
            // var (status, result) = await _navigator.Account.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // if (_navigator.HasClientContext)
            //     await _navigator.PublishToRequesterAsync(new YourClientEvent {{ ... }}, cancellationToken);
        }}
        catch (Exception ex)
        {{
            _logger.LogError(ex, \"Error executing {method_name} operation\");
            await _messageBus.TryPublishErrorAsync(
                \"${SERVICE_NAME}\",
                \"{method_name}\",
                \"unexpected_exception\",
                ex.Message,
                dependency: null,
                endpoint: \"{http_method}:/{path.strip('/')}\",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }}
    }}
''')

except Exception as e:
    print(f'    # Error processing schema: {e}', file=sys.stderr)
    sys.exit(1)
" >> "$IMPLEMENTATION_FILE"

# Close the class
cat >> "$IMPLEMENTATION_FILE" << EOF
}
EOF

# Check if generation succeeded
if [ -f "$IMPLEMENTATION_FILE" ]; then
    FILE_SIZE=$(wc -l < "$IMPLEMENTATION_FILE" 2>/dev/null || echo "0")
    echo -e "${GREEN}✅ Generated service implementation template ($FILE_SIZE lines)${NC}"
    echo -e "${YELLOW}⚠️  Remember to implement the TODO methods with actual business logic${NC}"
    exit 0
else
    echo -e "${RED}❌ Failed to generate service implementation${NC}"
    exit 1
fi
