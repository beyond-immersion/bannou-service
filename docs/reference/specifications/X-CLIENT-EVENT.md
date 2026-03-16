# x-client-event

> **Version**: 1.0
> **Status**: Implemented
> **Last Updated**: 2026-03-16
> **Schema Scope**: `*-client-events.yaml`
> **Generated Output**: Client event models in `lib-{service}/Generated/{Service}ClientEventsModels.cs`, typed subscription groups, C# and TypeScript event registries, Unreal Engine event types

---

## Summary

Marks a schema as a server-to-client WebSocket push event delivered through per-session RabbitMQ queues managed by the Connect service. Events are published via IClientEventPublisher and consumed by multiple code generators for typed subscription APIs across .NET, TypeScript, and Unreal Engine SDKs. The optional x-internal companion flag excludes infrastructure events from game client SDK exposure.

---

## Schema Syntax

### Basic Client Event

Every client event schema sets `x-client-event: true` and uses `allOf` to inherit from `BaseClientEvent` in `common-client-events.yaml`:

```yaml
# status-client-events.yaml
components:
  schemas:
    StatusEffectChangedClientEvent:
      allOf:
        - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
      type: object
      additionalProperties: false
      x-client-event: true
      description: Sent when a status effect changes on an observed entity.
      required:
        - eventName
        - entityId
        - changeType
      properties:
        eventName:
          type: string
          default: "status.effect-changed"
          description: Fixed event type identifier
        entityId:
          type: string
          format: uuid
          description: The entity whose status changed
        changeType:
          type: string
          description: Nature of the status change
```

### Internal Client Event

Infrastructure events used by Connect/Permission but not exposed to game client SDKs add `x-internal: true` alongside `x-client-event: true`:

```yaml
# common-client-events.yaml
components:
  schemas:
    CapabilityManifestClientEvent:
      allOf:
        - $ref: '#/components/schemas/BaseClientEvent'
      type: object
      additionalProperties: false
      x-client-event: true
      x-internal: true
      description: Sent to client when their available API capabilities change.
      required:
        - sessionId
        - availableApis
        - version
      properties:
        eventName:
          type: string
          default: "connect.capability-manifest"
          description: Fixed event type identifier
        sessionId:
          type: string
          format: uuid
          description: Session this manifest applies to
        availableApis:
          type: array
          items:
            type: object
          description: List of available API endpoints
        version:
          type: integer
          description: Manifest version for staleness detection
```

---

## Field Reference

### Extension Attribute

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `x-client-event` | boolean | Yes | — | Must be `true` to mark the schema as a client event |
| `x-internal` | boolean | No | `false` | When `true`, the event is excluded from game client SDK generation (used for Connect/Permission infrastructure events) |

### Required Schema Structure

| Requirement | Description |
|---|---|
| `allOf` with `BaseClientEvent` | All client events must inherit from `BaseClientEvent` in `common-client-events.yaml` via `allOf` |
| `eventName` property with `default` | Each event must declare `eventName` with a default string value used for routing and whitelist validation |
| `additionalProperties: false` | Client events must not allow extra properties |
| `type: object` | Client events must be object schemas |

### eventName Convention

Event names follow the pattern `{service}.{event-description}` using dot-separated namespace and kebab-case for compound names:
- `status.effect-changed`
- `chat.message-received`
- `connect.capability-manifest`
- `matchmaking.match-found`

---

## Generated Output

### Client Event Models

The `generate-client-events.sh` script generates C# model classes into `lib-{service}/Generated/{Service}ClientEventsModels.cs`:

```csharp
// lib-status/Generated/StatusClientEventsModels.cs (generated — do not edit)

/// <summary>
/// Sent when a status effect changes on an observed entity.
/// </summary>
public partial class StatusEffectChangedClientEvent : BaseClientEvent
{
    [JsonPropertyName("changeType")]
    public string ChangeType { get; set; }

    [JsonPropertyName("entityId")]
    public Guid EntityId { get; set; }
}
```

### Typed Subscription Groups

`generate-client-event-groups.py` produces typed subscription group classes enabling SDK consumers to subscribe by service:

```csharp
// Typed subscription API — consumers subscribe to events by service group
client.Status.OnEffectChanged += (sender, evt) => { /* handle */ };
```

### Event Registries

Two registry generators produce type-to-eventName mappings:

- **C# Registry** (`generate-client-event-registry.py`): Maps C# types to `eventName` strings for deserialization dispatch
- **TypeScript Registry** (`generate-client-event-registry-ts.py`): Equivalent mapping for the TypeScript SDK

### Unreal Engine Types

`generate-unreal-types.py` scans `x-client-event: true` schemas and generates USTRUCTs for each client event, included in the Unreal Engine header files.

---

## Runtime Behavior

### Publishing Flow

Services publish client events via `IClientEventPublisher` (not `IMessageBus`). The publisher serializes the event and routes it to the per-session RabbitMQ queue (`CONNECT_SESSION_{sessionId}`):

1. Service calls `IClientEventPublisher.PublishAsync(sessionId, event)`
2. Event is serialized and published to the session-specific RabbitMQ queue
3. Connect service consumes from the queue and forwards to the client's WebSocket connection
4. Client SDK dispatches the event to typed handlers using the `eventName` field

### Whitelist Validation

Connect validates incoming events against a generated whitelist. Only events with `eventName` values matching registered `x-client-event: true` schemas are forwarded. Unregistered event names are rejected.

### Internal Events

Events marked `x-internal: true` are processed by Connect and Permission infrastructure but are excluded from:
- Game client SDK typed subscription groups
- Client-facing documentation
- External event registry exports

They remain in the C# models for internal service use.

---

## Structural Tests

| Test Name | Validates |
|---|---|
| `SchemaClientEvents_UseAllOfWithBaseClientEvent` | Every client event schema (except `BaseClientEvent` itself) uses `allOf` referencing `BaseClientEvent` from `common-client-events.yaml` |

---

## Examples

### Example 1: Chat Message Received

A client event notifying the player of a new chat message in a channel they are observing.

**Schema** (`chat-client-events.yaml`):
```yaml
components:
  schemas:
    ChatMessageReceivedClientEvent:
      allOf:
        - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
      type: object
      additionalProperties: false
      x-client-event: true
      description: Sent when a new message is posted to a channel the client is observing.
      required:
        - eventName
        - channelId
        - messageId
        - senderEntityId
        - content
      properties:
        eventName:
          type: string
          default: "chat.message-received"
          description: Fixed event type identifier
        channelId:
          type: string
          format: uuid
          description: Channel the message was posted to
        messageId:
          type: string
          format: uuid
          description: Unique message identifier
        senderEntityId:
          type: string
          format: uuid
          description: Entity that sent the message
        content:
          type: string
          description: Message content
```

**Generated C#**:
```csharp
public partial class ChatMessageReceivedClientEvent : BaseClientEvent
{
    [JsonPropertyName("channelId")]
    public Guid ChannelId { get; set; }

    [JsonPropertyName("messageId")]
    public Guid MessageId { get; set; }

    [JsonPropertyName("senderEntityId")]
    public Guid SenderEntityId { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; }
}
```

### Example 2: Internal Infrastructure Event (Capability Manifest)

An infrastructure event sent by Connect when a client's API permissions change. Marked `x-internal: true` to exclude from game client SDKs.

**Schema** (`common-client-events.yaml`):
```yaml
components:
  schemas:
    CapabilityManifestClientEvent:
      allOf:
        - $ref: '#/components/schemas/BaseClientEvent'
      type: object
      additionalProperties: false
      x-client-event: true
      x-internal: true
      description: Sent to client when their available API capabilities change.
      required:
        - sessionId
        - availableApis
        - version
      properties:
        eventName:
          type: string
          default: "connect.capability-manifest"
          description: Fixed event type identifier
        sessionId:
          type: string
          format: uuid
          description: Session this manifest applies to
        availableApis:
          type: array
          items:
            type: object
          description: List of available API endpoints
        version:
          type: integer
          description: Manifest version for staleness detection
```

**Runtime behavior**: This event is generated and included in C# models but excluded from typed subscription groups in game client SDKs. Connect uses it internally to push permission updates to connected clients.

---

## Edge Cases & Restrictions

### Forbidden Combinations

| Restriction | Reason |
|---|---|
| `x-client-event: true` in `*-api.yaml` or `*-events.yaml` | Client events belong exclusively in `*-client-events.yaml` files |
| Client event without `allOf` referencing `BaseClientEvent` | All client events must inherit the base fields (`eventName`, `eventId`, `timestamp`) |
| Client event without `eventName` default value | The default value is used by registries and whitelist generation for routing |
| Publishing via `IMessageBus` instead of `IClientEventPublisher` | Client events route through per-session queues; `IMessageBus` publishes to service-to-service topics |
| `x-internal: true` without `x-client-event: true` | `x-internal` is a modifier on client events, not a standalone attribute |

### Scoping Rules

- `x-client-event` applies only to schemas in `*-client-events.yaml` files. The structural test enforces this boundary.
- Each service has at most one client events file: `{service}-client-events.yaml`.
- Client event schemas do not cross file boundaries. A service's events reference `BaseClientEvent` from `common-client-events.yaml` but do not reference other services' client event schemas.

### Interaction with Other Extension Attributes

| Attribute | Interaction |
|---|---|
| `x-internal` | Companion attribute that restricts SDK visibility. Only meaningful when `x-client-event: true` is also present. |
| `x-permissions` | Not applicable. Client events are push-only; they do not have endpoint permissions. |
| `x-lifecycle` | Not applicable. Client events do not participate in lifecycle event generation. |

### Known Limitations

- Client event schemas are not validated for `eventName` uniqueness across services at generation time. Duplicate event names across different services would cause routing ambiguity at runtime.
- The `x-internal` flag is a build-time exclusion only. At runtime, internal events use the same RabbitMQ routing path as public events.
