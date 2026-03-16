# x-event-subscriptions

> **Version**: 1.0
> **Status**: Implemented
> **Last Updated**: 2026-03-16
> **Schema Scope**: `*-service-events.yaml`
> **Generated Output**: `{Service}ServiceEvents.cs` (one-time template with `RegisterEventConsumers` method and handler stubs)
> **Related Specifications**: [x-event-publications](X-EVENT-PUBLICATIONS.md)
> **Tenet References**: T3 (IMPLEMENTATION-BEHAVIOR), T5 (FOUNDATION)

---

## Summary

Declares events a service consumes from other services and generates subscription handler scaffolding in the service's event consumer file. The generator resolves event type names across all event schemas at generation time, enabling cross-service event consumption without inline model copies or schema cross-references. Use when a service needs to react to events published by other services.

---

## Schema Syntax

### Basic Subscription Declaration

Subscriptions are declared under `info.x-event-subscriptions` in the service's events schema:

```yaml
info:
  title: Auth Service Events
  version: 1.0.0
  x-event-subscriptions:
    - topic: account.deleted
      event: AccountDeletedEvent
      handler: HandleAccountDeleted
    - topic: account.updated
      event: AccountUpdatedEvent
      handler: HandleAccountUpdated
  x-event-publications:
    # ...
```

### Empty Subscription List

Services that do not consume any external events declare an empty array:

```yaml
info:
  x-event-subscriptions: []
  x-event-publications:
    # ...
```

---

## Field Reference

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `topic` | string | Yes | -- | RabbitMQ routing key for the event. Must match the topic string used by the publishing service's `x-event-publications` entry. |
| `event` | string | Yes | -- | Event model class name. This is a class name, not a `$ref`. The generator searches ALL `*-service-events.yaml` `components/schemas` entries for a matching schema definition at generation time. |
| `handler` | string | Yes | -- | Handler method name without the `Async` suffix. The generator appends `Async` when producing the method stub. |

---

## Generated Output

### File: `{Service}ServiceEvents.cs`

This is a **one-time template** -- the generator creates it only if the file does not already exist. Once generated, it becomes a manual file that developers extend with business logic. The generator never overwrites an existing events file.

The generated file contains:

1. A `RegisterEventConsumers` method that wires up all declared subscriptions
2. Handler method stubs for each subscription entry

```csharp
public partial class AuthService
{
    /// <summary>
    /// Registers event consumers for cross-service event handling.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IAuthService, AccountDeletedEvent>(
            "account.deleted",
            async (svc, evt) => await ((AuthService)svc).HandleAccountDeletedAsync(evt));

        eventConsumer.RegisterHandler<IAuthService, AccountUpdatedEvent>(
            "account.updated",
            async (svc, evt) => await ((AuthService)svc).HandleAccountUpdatedAsync(evt));
    }

    /// <summary>
    /// Handles account.deleted events.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleAccountDeletedAsync(AccountDeletedEvent evt)
    {
        // TODO: Implement handler
        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles account.updated events.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleAccountUpdatedAsync(AccountUpdatedEvent evt)
    {
        // TODO: Implement handler
        await Task.CompletedTask;
    }
}
```

### Cross-Service Event Resolution

The `event` field is a class name, not a `$ref` path. The generator searches all `*-service-events.yaml` files' `components/schemas` for a matching entry. This means:

- `AccountDeletedEvent` in auth's subscriptions resolves to the schema defined in `account-service-events.yaml`
- The generated C# code uses the event model class from `bannou-service/Generated/Events/AccountEventsModels.cs`
- No `$ref` or cross-file schema references are needed in the events YAML

### IEventConsumer Integration

The `RegisterEventConsumers` method uses `IEventConsumer.RegisterHandler<TService, TEvent>()` which:

1. Subscribes to the specified RabbitMQ topic
2. Deserializes incoming messages to the typed event model
3. Resolves the service from DI and invokes the handler
4. Handles multi-plugin fan-out when multiple services subscribe to the same topic (per IMPLEMENTATION TENETS)

---

## Runtime Behavior

### Subscription Registration

At service startup, `RegisterEventConsumers` is called during service initialization. Each `RegisterHandler` call creates a RabbitMQ subscription with:

- A queue named after the consuming service (ensures each service gets its own copy)
- The specified routing key (topic string)
- Typed deserialization to the declared event model

### Handler Invocation

When a matching event arrives:

1. The message is deserialized to the declared event type using `BannouJson`
2. The consuming service is resolved from the DI container (scoped)
3. The handler method is invoked with the deserialized event
4. Errors in handlers are caught and logged but do not crash the subscription

### Edge Cases

- **Missing event schema**: If the `event` class name does not match any `components/schemas` entry across all events schemas, generation fails with an error
- **Topic mismatch**: If the `topic` does not match any publisher's declared topic, the subscription is valid but will never receive events. The `Events_PublishedTopicsWithoutSubscribers` structural test helps identify orphaned topics
- **Multiple consumers**: Multiple services can subscribe to the same topic -- each gets its own copy via separate RabbitMQ queues

---

## Structural Tests

| Test Name | Validates |
|---|---|
| `Services_WithEventSubscriptions_MustRegisterConsumers` | Every service that declares `x-event-subscriptions` entries has a `RegisterEventConsumers` method in its `*ServiceEvents.cs` file |
| `Service_CallsAllGeneratedEventPublishers` | Services use generated `Publish*Async` extension methods rather than inline topic strings (validates the publishing side that subscriptions consume) |

---

## Examples

### Example 1: Auth Service Subscribing to Account Events

Auth service reacts to account lifecycle events to invalidate sessions and clean up OAuth links.

**Schema** (`auth-service-events.yaml`):
```yaml
info:
  title: Auth Service Events
  version: 1.0.0
  x-event-subscriptions:
    - topic: account.deleted
      event: AccountDeletedEvent
      handler: HandleAccountDeleted
    - topic: account.updated
      event: AccountUpdatedEvent
      handler: HandleAccountUpdated
  x-event-publications:
    - topic: session.invalidated
      event: SessionInvalidatedEvent
      description: Published when sessions are invalidated
```

**Generated** (`AuthServiceEvents.cs` -- one-time template):
```csharp
protected void RegisterEventConsumers(IEventConsumer eventConsumer)
{
    eventConsumer.RegisterHandler<IAuthService, AccountDeletedEvent>(
        "account.deleted",
        async (svc, evt) => await ((AuthService)svc).HandleAccountDeletedAsync(evt));

    eventConsumer.RegisterHandler<IAuthService, AccountUpdatedEvent>(
        "account.updated",
        async (svc, evt) => await ((AuthService)svc).HandleAccountUpdatedAsync(evt));
}
```

### Example 2: Leaderboard Service Subscribing to Analytics Events

Leaderboard ingests score and rating updates from the Analytics service.

**Schema** (`leaderboard-service-events.yaml`):
```yaml
info:
  title: Leaderboard Service Events
  version: 1.0.0
  x-event-subscriptions:
    - topic: analytics.score.updated
      event: AnalyticsScoreUpdatedEvent
      handler: HandleScoreUpdated
    - topic: analytics.rating.updated
      event: AnalyticsRatingUpdatedEvent
      handler: HandleRatingUpdated
  x-event-publications:
    - topic: leaderboard.definition.created
      event: LeaderboardDefinitionCreatedEvent
      description: Published when a new leaderboard definition is created
```

**Generated** (`LeaderboardServiceEvents.cs` -- one-time template):
```csharp
protected void RegisterEventConsumers(IEventConsumer eventConsumer)
{
    eventConsumer.RegisterHandler<ILeaderboardService, AnalyticsScoreUpdatedEvent>(
        "analytics.score.updated",
        async (svc, evt) => await ((LeaderboardService)svc).HandleScoreUpdatedAsync(evt));

    eventConsumer.RegisterHandler<ILeaderboardService, AnalyticsRatingUpdatedEvent>(
        "analytics.rating.updated",
        async (svc, evt) => await ((LeaderboardService)svc).HandleRatingUpdatedAsync(evt));
}
```

---

## Edge Cases & Restrictions

### Forbidden Patterns

| Restriction | Reason |
|---|---|
| Using `$ref` for the `event` field | The field is a class name resolved at generation time across all event schemas, not a YAML `$ref` |
| Subscribing to events from lower layers for cleanup | Use `x-references` and lib-resource cleanup callbacks instead (per FOUNDATION TENETS, Resource-Managed Cleanup). Account deletion is the sole exception. |
| Omitting `handler` field | All three fields are required. The handler name determines the generated method stub. |
| Including `Async` suffix in `handler` value | The generator appends `Async` automatically. `handler: HandleAccountDeletedAsync` would produce `HandleAccountDeletedAsyncAsync`. |

### Scoping Rules

- **Subscriptions are per-service**: Each service's `*-service-events.yaml` declares only that service's subscriptions
- **Cross-schema resolution is read-only**: The generator reads all event schemas to resolve types but does not modify them. No cross-file `$ref` dependencies are created.
- **One-time generation**: The `*ServiceEvents.cs` file is generated once. Subsequent schema changes to `x-event-subscriptions` require manual updates to the events file. The structural test `Services_WithEventSubscriptions_MustRegisterConsumers` catches drift.

### Interaction with Other Extension Attributes

- **x-event-publications**: The publishing service's `x-event-publications` defines the topic strings that subscribers reference. Topic strings must match exactly.
- **x-lifecycle**: Lifecycle events (created, updated, deleted) are auto-generated from `x-lifecycle` and should also appear in the publisher's `x-event-publications`. Subscribers reference them by topic like any other event.
- **x-references**: For non-account entity cleanup, prefer `x-references` cleanup callbacks over event subscriptions. Event subscriptions for cleanup are a tenet violation (per FOUNDATION TENETS).
