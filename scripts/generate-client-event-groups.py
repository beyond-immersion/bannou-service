#!/usr/bin/env python3
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

"""
Generate service-grouped event subscription classes for the Bannou Client SDK.

This script reads client event schemas from schemas/*-client-events.yaml and generates
C# classes that group event subscriptions by service for better discoverability.

Example generated usage:
    client.Events.GameSession.OnChatReceived(evt => Console.WriteLine(evt.Message));
    client.Events.Voice.OnPeerJoined(evt => HandlePeer(evt));

Architecture:
- Reads x-client-event marked schemas from YAML
- Groups events by service (derived from schema filename)
- Generates per-service subscription classes
- Generates BannouClientEvents container class

Output:
- sdks/client/Generated/Events/{Service}EventSubscriptions.cs (per service)
- sdks/client/Generated/Events/BannouClientEvents.cs (container)

Usage:
    python3 scripts/generate-client-event-groups.py
"""

import sys
from pathlib import Path
from typing import List

try:
    from ruamel.yaml import YAML
    yaml = YAML()
    yaml.preserve_quotes = True
except ImportError:
    print("ERROR: ruamel.yaml is required. Install with: pip install ruamel.yaml")
    sys.exit(1)


def to_pascal_case(name: str) -> str:
    """Convert kebab-case or snake_case to PascalCase."""
    parts = name.replace('_', '-').split('-')
    return ''.join(p.capitalize() for p in parts)


def get_method_name_from_event(event_name: str) -> str:
    """Convert event type name to subscription method name.

    Examples:
        ChatMessageReceivedEvent -> OnChatMessageReceived
        PlayerJoinedEvent -> OnPlayerJoined
        VoiceRoomStateEvent -> OnRoomState (strips service prefix)
    """
    # Remove 'ClientEvent' or 'Event' suffix
    base = event_name
    if base.endswith('ClientEvent'):
        base = base[:-11]
    elif base.endswith('Event'):
        base = base[:-5]

    return f"On{base}"


class EventInfo:
    """Information about a client event."""

    def __init__(self, type_name: str, event_name: str, description: str):
        self.type_name = type_name
        self.event_name = event_name
        self.description = description

    @property
    def method_name(self) -> str:
        """Get the subscription method name."""
        return get_method_name_from_event(self.type_name)


class ServiceEvents:
    """Events grouped by service."""

    def __init__(self, service_name: str, pascal_name: str, namespace: str):
        self.service_name = service_name  # e.g., "game-session"
        self.pascal_name = pascal_name    # e.g., "GameSession"
        self.namespace = namespace        # e.g., "BeyondImmersion.Bannou.GameSession.ClientEvents"
        self.events: List[EventInfo] = []


def extract_events_from_schema(schema_path: Path) -> ServiceEvents | None:
    """Extract client event information from a schema file."""

    with open(schema_path) as f:
        try:
            schema = yaml.load(f)
        except Exception as e:
            print(f"  Warning: Failed to parse {schema_path.name}: {e}")
            return None

    if schema is None or 'components' not in schema or 'schemas' not in schema['components']:
        return None

    # Determine service info based on file
    filename = schema_path.stem

    # Skip common-client-events - those are system events, not service-specific
    if filename == 'common-client-events':
        # Still extract for "System" or "Connect" group
        service_name = "common"
        pascal_name = "System"
        namespace = "BeyondImmersion.BannouService.ClientEvents"
    else:
        # e.g., "game-session-client-events" -> "game-session" -> "GameSession"
        service_name = filename.replace('-client-events', '')
        pascal_name = to_pascal_case(service_name)
        namespace = f"BeyondImmersion.Bannou.{pascal_name}.ClientEvents"

    service_events = ServiceEvents(service_name, pascal_name, namespace)

    schemas = schema['components']['schemas']
    for schema_name, schema_def in schemas.items():
        if not isinstance(schema_def, dict):
            continue

        # Skip BaseClientEvent and non-event types
        if schema_name == 'BaseClientEvent':
            continue
        if not schema_name.endswith('Event'):
            continue

        # Check for x-client-event marker
        if not schema_def.get('x-client-event'):
            continue

        # Skip internal events (not exposed to clients)
        if schema_def.get('x-internal'):
            continue

        # Get description
        description = schema_def.get('description', '').strip()
        if description:
            # Take first line only
            description = description.split('\n')[0].strip()

        # Get eventName from properties
        props = schema_def.get('properties', {})
        event_name_prop = props.get('eventName', {})
        event_name = event_name_prop.get('default', '')

        if event_name:
            service_events.events.append(EventInfo(schema_name, event_name, description))

    if not service_events.events:
        return None

    return service_events


def generate_service_subscriptions_class(service: ServiceEvents) -> str:
    """Generate a service-specific event subscriptions class."""

    lines = [
        "// <auto-generated>",
        "// This code was generated by generate-client-event-groups.py",
        "// Do not edit this file manually.",
        "// </auto-generated>",
        "",
        "#nullable enable",
        "",
        "using BeyondImmersion.Bannou.Core;",
        f"using {service.namespace};",
        "",
        "namespace BeyondImmersion.Bannou.Client.Events;",
        "",
        "/// <summary>",
        f"/// Event subscriptions for {service.pascal_name} service events.",
        "/// Provides strongly-typed subscription methods for each event type.",
        "/// </summary>",
        f"public sealed class {service.pascal_name}EventSubscriptions",
        "{",
        "    private readonly BannouClient _client;",
        "",
        f"    internal {service.pascal_name}EventSubscriptions(BannouClient client)",
        "    {",
        "        _client = client ?? throw new ArgumentNullException(nameof(client));",
        "    }",
    ]

    for event in sorted(service.events, key=lambda e: e.type_name):
        lines.extend([
            "",
            "    /// <summary>",
            f"    /// Subscribe to <see cref=\"{event.type_name}\"/> events.",
        ])
        if event.description:
            # Escape XML entities
            desc = event.description.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;')
            lines.append(f"    /// {desc}")
        lines.extend([
            "    /// </summary>",
            "    /// <param name=\"handler\">Handler invoked when the event is received.</param>",
            "    /// <returns>Subscription handle. Dispose to unsubscribe.</returns>",
            f"    public IEventSubscription {event.method_name}(Action<{event.type_name}> handler)",
            "    {",
            f"        return _client.OnEvent<{event.type_name}>(handler);",
            "    }",
        ])

    lines.extend([
        "}",
        "",
    ])

    return '\n'.join(lines)


def generate_container_class(services: List[ServiceEvents]) -> str:
    """Generate the BannouClientEvents container class."""

    lines = [
        "// <auto-generated>",
        "// This code was generated by generate-client-event-groups.py",
        "// Do not edit this file manually.",
        "// </auto-generated>",
        "",
        "#nullable enable",
        "",
        "namespace BeyondImmersion.Bannou.Client.Events;",
        "",
        "/// <summary>",
        "/// Container for service-grouped event subscriptions.",
        "/// Access via <c>client.Events.{Service}.On{Event}(handler)</c>.",
        "/// </summary>",
        "public sealed class BannouClientEvents",
        "{",
        "    private readonly BannouClient _client;",
    ]

    # Add backing fields
    for service in sorted(services, key=lambda s: s.pascal_name):
        lines.append(f"    private {service.pascal_name}EventSubscriptions? _{service.pascal_name.lower()};")

    lines.extend([
        "",
        "    internal BannouClientEvents(BannouClient client)",
        "    {",
        "        _client = client ?? throw new ArgumentNullException(nameof(client));",
        "    }",
    ])

    # Add properties
    for service in sorted(services, key=lambda s: s.pascal_name):
        field_name = f"_{service.pascal_name.lower()}"
        lines.extend([
            "",
            "    /// <summary>",
            f"    /// Event subscriptions for {service.pascal_name} service.",
            "    /// </summary>",
            f"    public {service.pascal_name}EventSubscriptions {service.pascal_name} =>",
            f"        {field_name} ??= new {service.pascal_name}EventSubscriptions(_client);",
        ])

    lines.extend([
        "}",
        "",
    ])

    return '\n'.join(lines)


def generate_client_extension() -> str:
    """Generate the BannouClient partial class extension for Events property."""

    return '''// <auto-generated>
// This code was generated by generate-client-event-groups.py
// Do not edit this file manually.
// </auto-generated>

#nullable enable

using BeyondImmersion.Bannou.Client.Events;

namespace BeyondImmersion.Bannou.Client;

/// <summary>
/// Extension adding service-grouped event subscriptions to BannouClient.
/// </summary>
public partial class BannouClient
{
    private BannouClientEvents? _events;

    /// <summary>
    /// Service-grouped event subscriptions.
    /// Provides organized access to event subscriptions by service.
    /// </summary>
    /// <example>
    /// <code>
    /// client.Events.GameSession.OnChatMessageReceived(evt => Console.WriteLine(evt.Message));
    /// client.Events.Voice.OnPeerJoined(evt => HandleNewPeer(evt));
    /// </code>
    /// </example>
    public BannouClientEvents Events => _events ??= new BannouClientEvents(this);
}
'''


def main():
    """Process all client event schemas and generate grouped subscription classes."""
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schema_dir = repo_root / 'schemas'
    output_dir = repo_root / 'sdks' / 'client' / 'Generated' / 'Events'

    if not schema_dir.exists():
        print(f"ERROR: Schema directory not found: {schema_dir}")
        sys.exit(1)

    # Create output directory
    output_dir.mkdir(parents=True, exist_ok=True)

    print("Generating service-grouped event subscriptions...")
    print(f"  Reading from: schemas/*-client-events.yaml")
    print(f"  Writing to: sdks/client/Generated/Events/")
    print()

    all_services: List[ServiceEvents] = []
    errors = []

    for schema_path in sorted(schema_dir.glob('*-client-events.yaml')):
        try:
            service = extract_events_from_schema(schema_path)
            if service:
                all_services.append(service)
                print(f"  {schema_path.name}: {service.pascal_name} ({len(service.events)} events)")

        except Exception as e:
            errors.append(f"{schema_path.name}: {e}")

    if errors:
        print("\nErrors:")
        for error in errors:
            print(f"  {error}")
        sys.exit(1)

    if not all_services:
        print("\nNo client events found to generate subscription classes for")
        sys.exit(0)

    # Generate per-service subscription classes
    for service in all_services:
        class_code = generate_service_subscriptions_class(service)
        output_file = output_dir / f"{service.pascal_name}EventSubscriptions.cs"

        with open(output_file, 'w', newline='\n') as f:
            f.write(class_code)

        print(f"  Generated {output_file.name}")

    # Generate container class
    container_code = generate_container_class(all_services)
    container_file = output_dir / "BannouClientEvents.cs"

    with open(container_file, 'w', newline='\n') as f:
        f.write(container_code)

    print(f"  Generated {container_file.name}")

    # Generate BannouClient extension
    extension_code = generate_client_extension()
    extension_file = output_dir / "BannouClientEventsExtension.cs"

    with open(extension_file, 'w', newline='\n') as f:
        f.write(extension_code)

    print(f"  Generated {extension_file.name}")

    total_events = sum(len(s.events) for s in all_services)
    print(f"\n  Generated {len(all_services)} service groups with {total_events} total events")


if __name__ == '__main__':
    main()
