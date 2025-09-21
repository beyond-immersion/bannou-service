#!/usr/bin/env python3
"""
Extract x-permissions sections from OpenAPI schemas and generate service registration code.

This script parses OpenAPI schemas, extracts x-permissions sections from endpoints,
and generates C# code for automatic service permission registration.

Data Structure Generated:
State -> Role -> Methods[]
Example: "authenticated" -> "user" -> ["GET:/accounts", "POST:/accounts"]
"""

import sys
import yaml
import json
from typing import Dict, List, Set, Tuple, Optional
from collections import defaultdict

def extract_permissions_from_schema(schema_file: str, service_name: str) -> Optional[Dict[str, Dict[str, List[str]]]]:
    """
    Extract permissions from OpenAPI schema x-permissions sections.

    Returns: Dictionary structured as State -> Role -> Methods[]
    """
    try:
        with open(schema_file, 'r') as f:
            schema = yaml.safe_load(f)
    except Exception as e:
        print(f"Error loading schema {schema_file}: {e}", file=sys.stderr)
        return None

    if 'paths' not in schema:
        print(f"No paths found in schema {schema_file}", file=sys.stderr)
        return None

    # Structure: state -> role -> set of methods
    permissions_matrix = defaultdict(lambda: defaultdict(set))

    # Process each endpoint
    for path, path_data in schema['paths'].items():
        for http_method, method_data in path_data.items():
            if not isinstance(method_data, dict):
                continue

            # Look for x-permissions section
            x_permissions = method_data.get('x-permissions', [])
            if not x_permissions:
                continue

            # Parse permissions for this endpoint
            endpoint_states = set()
            endpoint_roles = set()

            for permission in x_permissions:
                perm_type = permission.get('type')
                perm_value = permission.get('value')

                if perm_type == 'role' and perm_value:
                    endpoint_roles.add(perm_value)
                elif perm_type == 'session_state' and perm_value:
                    endpoint_states.add(perm_value)
                # Note: Ignoring 'context' permissions for now as they're more complex

            # Default values if not specified
            if not endpoint_states:
                endpoint_states.add('anonymous')  # Default state
            if not endpoint_roles:
                endpoint_roles.add('user')  # Default role

            # Create method identifier
            method_identifier = f"{http_method.upper()}:{path}"

            # Add to permissions matrix for each state/role combination
            for state in endpoint_states:
                for role in endpoint_roles:
                    permissions_matrix[state][role].add(method_identifier)

    # Convert sets to lists for JSON serialization
    result = {}
    for state, roles in permissions_matrix.items():
        result[state] = {}
        for role, methods in roles.items():
            result[state][role] = sorted(list(methods))

    return result if result else None

def generate_registration_method(service_name: str, permissions_matrix: Dict[str, Dict[str, List[str]]]) -> str:
    """
    Generate C# RegisterServicePermissionsAsync method.
    """
    service_pascal = ''.join(word.capitalize() for word in service_name.replace('-', '_').split('_'))

    # Build the permissions dictionary structure using simple dictionaries
    permissions_code_lines = []

    for state, roles in permissions_matrix.items():
        state_lines = []
        for role, methods in roles.items():
            methods_array = ', '.join(f'"{method}"' for method in methods)
            state_lines.append(f'                ["{role}"] = new List<string> {{ {methods_array} }}')

        state_code = 'new Dictionary<string, List<string>>\n            {\n' + ',\n'.join(state_lines) + '\n            }'
        permissions_code_lines.append(f'            ["{state}"] = {state_code}')

    permissions_dict_code = 'new Dictionary<string, Dictionary<string, List<string>>>\n        {\n' + ',\n'.join(permissions_code_lines) + '\n        }'

    method_code = f'''
    /// <summary>
    /// Registers service permissions extracted from x-permissions sections in the OpenAPI schema.
    /// This method is automatically generated and called during service startup.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {{
        try
        {{
            var serviceName = GetType().GetServiceName() ?? "{service_name}";
            _logger?.LogInformation("Registering permissions for {{ServiceName}} service", serviceName);

            // Build endpoints directly from x-permissions data
            var endpoints = CreateServiceEndpoints();

            // Publish service registration event to Permissions service
            await _daprClient.PublishEventAsync(
                "bannou-pubsub",
                "bannou-service-registered",
                new ServiceRegistrationEvent
                {{
                    EventId = Guid.NewGuid().ToString(),
                    Timestamp = DateTime.UtcNow,
                    ServiceId = serviceName,
                    Version = "1.0.0", // TODO: Extract from schema info.version
                    AppId = "bannou", // Default routing
                    Endpoints = endpoints,
                    Metadata = new Dictionary<string, object>
                    {{
                        {{ "generatedFrom", "x-permissions" }},
                        {{ "extractedAt", DateTime.UtcNow }},
                        {{ "endpointCount", endpoints.Count }}
                    }}
                }});

            _logger?.LogInformation("Successfully registered {{Count}} permission rules for {{ServiceName}}",
                endpoints.Count, serviceName);
        }}
        catch (Exception ex)
        {{
            _logger?.LogError(ex, "Failed to register permissions for service {{ServiceName}}", GetType().GetServiceName() ?? "{service_name}");
            // Don't throw - permission registration failure shouldn't crash the service
        }}
    }}

    /// <summary>
    /// Create ServiceEndpoint objects from extracted x-permissions data.
    /// </summary>
    private List<ServiceEndpoint> CreateServiceEndpoints()
    {{
        var endpoints = new List<ServiceEndpoint>();

        // Permission mapping extracted from x-permissions sections:
        // State -> Role -> Methods
        var permissionData = {permissions_dict_code};

        foreach (var stateEntry in permissionData)
        {{
            var stateName = stateEntry.Key;
            var statePermissions = stateEntry.Value;

            foreach (var roleEntry in statePermissions)
            {{
                var roleName = roleEntry.Key;
                var methods = roleEntry.Value;

                foreach (var method in methods)
                {{
                    var parts = method.Split(':', 2);
                    if (parts.Length == 2 && Enum.TryParse<ServiceEndpointMethod>(parts[0], out var httpMethod))
                    {{
                        endpoints.Add(new ServiceEndpoint
                        {{
                            Path = parts[1],
                            Method = httpMethod,
                            Permissions = new List<PermissionRequirement>
                            {{
                                new PermissionRequirement
                                {{
                                    Role = roleName,
                                    RequiredStates = new Dictionary<string, string>
                                    {{
                                        {{ "{service_name}", stateName }}
                                    }}
                                }}
                            }},
                            Description = $"{{httpMethod}} {{parts[1]}} ({{roleName}} in {{stateName}} state)",
                            Category = "{service_name}"
                        }});
                    }}
                }}
            }}
        }}

        return endpoints;
    }}'''

    return method_code

def main():
    if len(sys.argv) != 3:
        print("Usage: extract-permissions.py <service-name> <schema-file>", file=sys.stderr)
        sys.exit(1)

    service_name = sys.argv[1]
    schema_file = sys.argv[2]

    permissions_matrix = extract_permissions_from_schema(schema_file, service_name)

    if not permissions_matrix:
        print(f"# No x-permissions found in {schema_file}")
        sys.exit(0)

    print(f"# Found x-permissions in {schema_file}")
    print(f"# Extracted {len(permissions_matrix)} states with permissions")

    # Generate the method
    method_code = generate_registration_method(service_name, permissions_matrix)
    print(method_code)

if __name__ == "__main__":
    main()