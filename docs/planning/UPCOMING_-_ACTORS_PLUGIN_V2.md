# Actors Plugin - General-Purpose Distributed Processing Units

> **Status**: PLANNING (v2.1 - Aligned with Behavior Plugin V2)
> **Last Updated**: 2024-12-28
> **Supersedes**: UPCOMING_-_ACTORS_PLUGIN.md
> **Aligned With**: [BEHAVIOR_PLUGIN_V2.md](./UPCOMING_-_BEHAVIOR_PLUGIN_V2.md), [ABML_V2_DESIGN_PROPOSAL.md](./ABML_V2_DESIGN_PROPOSAL.md)

---

## Executive Summary

The Actors Plugin provides **general-purpose distributed processing units** for Bannou. An actor is:

- **Addressable**: Located by type + ID (e.g., `npc-brain:character-123`)
- **Single-instance**: Exactly one active instance per ID across the entire cluster
- **Stateful**: Maintains state that persists across activations
- **Turn-based**: Processes messages sequentially (no concurrent access to state)
- **Infrastructure-connected**: Has access to lib-state, lib-messaging, lib-mesh

Actors are **NOT** specific to any use case. They are primitives that can be used for:

| Use Case | Example |
|----------|---------|
| NPC Cognitive Processing | Actor receives perception events, emits objectives |
| Scheduled Tasks (CRON) | Actor activates on schedule, runs once across cluster |
| Chat/AI Agents | Actor maintains conversation state, calls LLM APIs |
| Game Session Coordinator | Actor manages one match's state and lifecycle |
| Workflow Orchestrator | Actor tracks multi-step process with checkpoints |
| IoT Device Controller | Actor represents one device, handles commands/telemetry |

### Design Principles

1. **Simple Core**: Actors provide lifecycle, messaging, and state - nothing more
2. **Use-Case Agnostic**: No NPC-specific, chat-specific, or game-specific concepts in the core
3. **Infrastructure Leverage**: Built entirely on existing lib-state, lib-messaging, lib-mesh
4. **Familiar Patterns**: Follows Bannou's schema-first, plugin-based architecture
5. **Horizontal Scale**: 1,000+ actors per node, auto-scaling via orchestrator (plan for 50% active as normal, 100% as peak)

---

## Core Concepts

### Actor Identity

Every actor has a unique identity consisting of:

```
{actor-type}:{actor-id}
```

Examples:
- `npc-brain:character-abc123` - NPC cognitive processor
- `daily-cleanup:global` - Singleton scheduled task
- `chat-agent:session-xyz789` - Chat conversation handler
- `game-session:match-456` - Game match coordinator

**Actor Type**: Defined by code (the actor class). Determines behavior.
**Actor ID**: Provided at runtime. Determines which instance.

### Actor Lifecycle

```
                                ┌─────────────────────────────┐
                                │         NOT ACTIVE          │
                                │   (Virtual - state in DB)   │
                                └──────────────┬──────────────┘
                                               │
                           First message OR scheduled trigger
                                               │
                                               ▼
                        ┌──────────────────────────────────────────┐
                        │              ACTIVATING                   │
                        │  1. Claim instance (atomic, cluster-wide) │
                        │  2. Load state from lib-state             │
                        │  3. Call OnActivateAsync()                │
                        │  4. Start message processing loop         │
                        └──────────────────────────────────────────┘
                                               │
                                               ▼
                                ┌─────────────────────────────┐
                                │           ACTIVE            │
                                │   (Processing messages)     │
                                └──────────────┬──────────────┘
                                               │
                            Idle timeout OR explicit deactivate
                                               │
                                               ▼
                        ┌──────────────────────────────────────────┐
                        │             DEACTIVATING                  │
                        │  1. Drain inbox (finish pending messages) │
                        │  2. Call OnDeactivateAsync()              │
                        │  3. Persist state to lib-state            │
                        │  4. Release instance claim                │
                        └──────────────────────────────────────────┘
                                               │
                                               ▼
                                ┌─────────────────────────────┐
                                │         NOT ACTIVE          │
                                │   (Ready for reactivation)  │
                                └─────────────────────────────┘
```

### Turn-Based Processing

Each actor has a **mailbox** (bounded channel). Messages are processed one at a time:

```
┌─────────────────────────────────────────────────────────────┐
│                      ACTOR INSTANCE                          │
│                                                              │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐  │
│  │   MAILBOX    │───▶│  PROCESSOR   │───▶│    STATE     │  │
│  │  (Channel)   │    │ (Sequential) │    │  (Private)   │  │
│  └──────────────┘    └──────────────┘    └──────────────┘  │
│         ▲                                                    │
│         │                                                    │
└─────────┼────────────────────────────────────────────────────┘
          │
    Messages from:
    - Direct invocation
    - Event subscriptions
    - Scheduled callbacks
    - Personal channel
```

**Guarantees:**
- Messages processed in order received
- No concurrent access to actor state
- If mailbox full, oldest messages dropped (configurable)

---

## Defining Actor Types (Schema-First)

Following Bannou's schema-first architecture, actor types are defined via YAML schemas and code is generated.

### Actor Type Schema

```yaml
# schemas/actor-types/npc-brain.yaml
openapi: 3.0.3
info:
  title: NPC Brain Actor
  version: 1.0.0
  description: Cognitive processing actor for NPC characters

x-actor-type:
  name: npc-brain
  idle_timeout_seconds: 300
  mailbox_capacity: 100
  mailbox_full_mode: drop_oldest  # drop_oldest | drop_newest | block

  # State schema (generates typed state class)
  state:
    $ref: '#/components/schemas/NpcBrainState'

  # Event subscriptions (auto-subscribed on activation)
  subscriptions:
    - topic: game.time.tick
      schema:
        $ref: '#/components/schemas/GameTimeTickEvent'

  # Scheduled callbacks
  schedules:
    - name: memory-consolidation
      interval_seconds: 60
      message_type: consolidate-memory

# Message handlers (generates abstract methods to implement)
paths:
  /perception:
    post:
      operationId: HandlePerception
      x-actor-message: perception
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/PerceptionEvent'
      responses:
        '200':
          description: Perception processed

  /query-state:
    post:
      operationId: HandleQueryState
      x-actor-message: query-state
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/QueryStateRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/NpcBrainState'

  /consolidate-memory:
    post:
      operationId: HandleConsolidateMemory
      x-actor-message: consolidate-memory
      x-scheduled: true  # Called by scheduler, not external
      responses:
        '200':
          description: Memory consolidated

components:
  schemas:
    NpcBrainState:
      type: object
      description: |
        State for NPC brain actors. Aligned with Behavior Plugin V2 expectations
        for GOAP planning and cognition pipeline integration.
      properties:
        last_perception_at:
          type: string
          format: date-time
          description: Timestamp of last perception processed
        behavior_stack_id:
          type: string
          description: ID of compiled ABML behavior document for this NPC
        current_goals:
          type: array
          description: Active GOAP goals ordered by priority
          items:
            $ref: '#/components/schemas/GoapGoal'
        current_plan:
          description: Currently executing GOAP plan (null if idle)
          nullable: true
          $ref: '#/components/schemas/GoapPlan'
        current_plan_index:
          type: integer
          default: 0
          description: Index of next action to execute in current plan
        relationships:
          type: object
          description: Relationship scores with other entities
          additionalProperties:
            type: number
            format: float

    GoapGoal:
      type: object
      required: [name, priority, conditions]
      properties:
        name:
          type: string
        priority:
          type: integer
        conditions:
          type: object
          additionalProperties: true

    GoapPlan:
      type: object
      required: [actions, total_cost, goal_id]
      properties:
        actions:
          type: array
          items:
            $ref: '#/components/schemas/PlannedAction'
        total_cost:
          type: number
        goal_id:
          type: string

    PlannedAction:
      type: object
      required: [action_id, name, cost]
      properties:
        action_id:
          type: string
        name:
          type: string
        cost:
          type: number

    PerceptionEvent:
      type: object
      required: [perception_type, timestamp]
      properties:
        perception_type:
          type: string
          enum: [visual, audio, environmental]
        timestamp:
          type: string
          format: date-time
        entities:
          type: array
          items:
            $ref: '#/components/schemas/PerceivedEntity'

    # ... other schemas
```

### Generated Code

Running `scripts/generate-actor-types.sh` produces:

```
lib-actor-types/
├── Generated/
│   ├── NpcBrainActor.Generated.cs      # Abstract base with infrastructure
│   ├── NpcBrainActorState.cs           # Typed state class
│   ├── NpcBrainActorMessages.cs        # Message types
│   └── INpcBrainActor.cs               # Actor interface
└── NpcBrainActor.cs                    # Manual implementation (business logic)
```

**Generated abstract base:**

```csharp
// Generated/NpcBrainActor.Generated.cs (DO NOT EDIT)
public abstract partial class NpcBrainActorBase : ActorBase
{
    // Configuration from schema
    public override string ActorTypeName => "npc-brain";
    public override int IdleTimeoutSeconds => 300;
    public override int MailboxCapacity => 100;
    public override MailboxFullMode FullMode => MailboxFullMode.DropOldest;

    // Typed state access (aligned with Behavior Plugin V2)
    protected NpcBrainState State { get; private set; } = new();

    // Infrastructure (injected)
    protected IMessageBus MessageBus { get; }
    protected IMeshInvocationClient MeshClient { get; }
    protected ILogger<NpcBrainActorBase> Logger { get; }

    // Generated service clients (per Tenet 4 - use generated clients)
    protected IBehaviorClient BehaviorClient { get; }

    // Lifecycle (sealed - handles state loading/saving)
    public sealed override async Task OnActivateAsync(CancellationToken ct)
    {
        State = await LoadStateAsync<NpcBrainState>(ct) ?? new NpcBrainState();
        await SubscribeAsync<GameTimeTickEvent>("game.time.tick", ct);
        await ScheduleRecurringAsync("consolidate-memory", TimeSpan.FromSeconds(60), ct);
        await OnActorActivatedAsync(ct);  // User override point
    }

    public sealed override async Task OnDeactivateAsync(CancellationToken ct)
    {
        await OnActorDeactivatingAsync(ct);  // User override point
        await SaveStateAsync(State, ct);
    }

    // User lifecycle hooks
    protected virtual Task OnActorActivatedAsync(CancellationToken ct) => Task.CompletedTask;
    protected virtual Task OnActorDeactivatingAsync(CancellationToken ct) => Task.CompletedTask;

    // Abstract message handlers (user must implement)
    public abstract Task HandlePerceptionAsync(PerceptionEvent message, CancellationToken ct);
    public abstract Task<NpcBrainState> HandleQueryStateAsync(QueryStateRequest message, CancellationToken ct);
    protected abstract Task HandleConsolidateMemoryAsync(CancellationToken ct);
}
```

### Manual Implementation

The developer only writes business logic:

```csharp
// NpcBrainActor.cs (MANUAL - business logic only)
public partial class NpcBrainActor : NpcBrainActorBase
{
    public override async Task HandlePerceptionAsync(PerceptionEvent perception, CancellationToken ct)
    {
        // 1. Run cognition pipeline via Behavior service (using generated client per Tenet 4)
        var cognitionResult = await BehaviorClient.ProcessCognitionAsync(
            new ProcessCognitionRequest
            {
                AgentId = ActorId,
                Perception = perception,
                CurrentState = GetWorldState(),
                CurrentGoals = State.CurrentGoals
            }, ct);

        // 2. Update state based on cognition result
        State.LastPerceptionAt = DateTimeOffset.UtcNow;
        if (cognitionResult.MemoriesStored?.Count > 0)
        {
            Logger.LogDebug(
                "Stored {Count} new memories for {ActorId}",
                cognitionResult.MemoriesStored.Count,
                ActorId);
        }

        // 3. Check if replanning needed
        if (cognitionResult.RequiresReplan)
        {
            await ReplanAsync(cognitionResult.UpdatedGoals ?? State.CurrentGoals, ct);
        }
        // 4. Continue current plan if valid
        else if (State.CurrentPlan != null)
        {
            await ContinuePlanExecutionAsync(ct);
        }
    }

    private async Task ReplanAsync(List<GoapGoal> goals, CancellationToken ct)
    {
        // Get available actions from compiled behaviors
        var compiledBehavior = await BehaviorClient.GetCachedBehaviorAsync(
            new GetCachedBehaviorRequest { BehaviorId = State.BehaviorStackId }, ct);

        var availableActions = compiledBehavior?.GoapActions ?? new List<GoapAction>();

        // Select highest priority unsatisfied goal
        var targetGoal = goals
            .Where(g => !GetWorldState().SatisfiesGoal(g))
            .OrderByDescending(g => g.Priority)
            .FirstOrDefault();

        if (targetGoal == null)
        {
            Logger.LogDebug("No unsatisfied goals for {ActorId}", ActorId);
            State.CurrentPlan = null;
            return;
        }

        // Plan via GOAP (can use local planner or Behavior service)
        var planResult = await BehaviorClient.GenerateGoapPlanAsync(
            new GoapPlanRequest
            {
                AgentId = ActorId,
                Goal = targetGoal,
                WorldState = GetWorldState().ToDictionary(),
                BehaviorId = State.BehaviorStackId
            }, ct);

        if (planResult?.Success == true && planResult.Plan != null)
        {
            State.CurrentPlan = planResult.Plan;
            State.CurrentPlanIndex = 0;
            State.CurrentGoals = goals;

            // Emit plan started event (typed event per Tenet 5)
            await MessageBus.PublishAsync(
                $"actor.objective.{ActorId}",
                new ObjectivesUpdatedEvent
                {
                    ActorId = ActorId,
                    CurrentGoal = targetGoal.Name,
                    PlannedActions = planResult.Plan.Actions.Select(a => a.Name).ToList()
                },
                cancellationToken: ct);
        }
    }

    private async Task ContinuePlanExecutionAsync(CancellationToken ct)
    {
        if (State.CurrentPlan == null || State.CurrentPlanIndex >= State.CurrentPlan.Actions.Count)
        {
            // Plan complete, trigger replanning
            await ReplanAsync(State.CurrentGoals, ct);
            return;
        }

        var currentAction = State.CurrentPlan.Actions[State.CurrentPlanIndex];

        // Execute action via Behavior service
        var result = await BehaviorClient.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                AgentId = ActorId,
                ActionId = currentAction.ActionId,
                Context = GetWorldState().ToDictionary()
            }, ct);

        if (result?.Success == true)
        {
            State.CurrentPlanIndex++;
        }
        else
        {
            // Action failed, trigger replanning
            Logger.LogWarning(
                "Action {Action} failed for {ActorId}, replanning",
                currentAction.Name,
                ActorId);
            await ReplanAsync(State.CurrentGoals, ct);
        }
    }

    private WorldState GetWorldState()
    {
        // Build world state from actor state for GOAP planning
        return new WorldState();
    }

    public override Task<NpcBrainState> HandleQueryStateAsync(QueryStateRequest message, CancellationToken ct)
    {
        return Task.FromResult(State);
    }

    protected override async Task HandleConsolidateMemoryAsync(CancellationToken ct)
    {
        // Periodic memory consolidation via Behavior service
        Logger.LogDebug("Consolidating memory for {ActorId}", ActorId);
        await BehaviorClient.ConsolidateMemoryAsync(
            new ConsolidateMemoryRequest { AgentId = ActorId }, ct);
    }
}
```

### Actor Base Class (Runtime)

The runtime provides the base class that generated actors extend:

```csharp
/// <summary>
/// Base class for all actor implementations.
/// Provides infrastructure access and lifecycle management.
/// </summary>
public abstract class ActorBase
{
    // Identity
    public abstract string ActorTypeName { get; }
    public string ActorId { get; internal set; } = string.Empty;
    public string ActorAddress => $"{ActorTypeName}:{ActorId}";

    // Configuration (overridden by generated code)
    public virtual int IdleTimeoutSeconds => 300;
    public virtual int MailboxCapacity => 100;
    public virtual MailboxFullMode FullMode => MailboxFullMode.DropOldest;

    // Infrastructure (injected by runtime)
    internal IStateStoreFactory StateStoreFactory { get; set; } = null!;
    internal IMessageBus MessageBusInternal { get; set; } = null!;
    internal IMeshInvocationClient MeshClientInternal { get; set; } = null!;
    internal ILogger LoggerInternal { get; set; } = null!;

    // Lifecycle hooks
    public virtual Task OnActivateAsync(CancellationToken ct) => Task.CompletedTask;
    public virtual Task OnDeactivateAsync(CancellationToken ct) => Task.CompletedTask;

    // State persistence
    protected async Task<TState?> LoadStateAsync<TState>(CancellationToken ct) where TState : class
    {
        var store = StateStoreFactory.GetStore<TState>("actor-state");
        return await store.GetAsync(ActorAddress, ct);
    }

    protected async Task SaveStateAsync<TState>(TState state, CancellationToken ct) where TState : class
    {
        var store = StateStoreFactory.GetStore<TState>("actor-state");
        await store.SaveAsync(ActorAddress, state, cancellationToken: ct);
    }

    // Event subscription
    protected Task SubscribeAsync<TEvent>(string topic, CancellationToken ct);
    protected Task UnsubscribeAsync(string topic, CancellationToken ct);

    // Personal channel
    protected string PersonalChannel => $"actor.personal.{ActorTypeName}.{ActorId}";

    // Scheduling
    protected Task ScheduleOnceAsync(string messageType, TimeSpan delay, CancellationToken ct);
    protected Task ScheduleRecurringAsync(string messageType, TimeSpan interval, CancellationToken ct);
    protected Task CancelScheduleAsync(string messageType, CancellationToken ct);
}

public enum MailboxFullMode
{
    DropOldest,
    DropNewest,
    Block
}
```

---

## Additional Actor Type Examples

These show different use cases - all follow the same schema-first pattern.

### Scheduled Task Actor

Schema:

```yaml
# schemas/actor-types/daily-cleanup.yaml
x-actor-type:
  name: daily-cleanup
  idle_timeout_seconds: 60
  schedules:
    - name: execute
      interval_seconds: 86400  # 24 hours
      message_type: execute

paths:
  /execute:
    post:
      operationId: HandleExecute
      x-actor-message: execute
      x-scheduled: true
      responses:
        '200':
          description: Cleanup executed
```

Implementation:

```csharp
// DailyCleanupActor.cs
public partial class DailyCleanupActor : DailyCleanupActorBase
{
    protected override async Task HandleExecuteAsync(CancellationToken ct)
    {
        Logger.LogInformation("Running daily cleanup");

        // Call service APIs to do cleanup
        await MeshClient.InvokeMethodAsync<CleanupRequest, CleanupResponse>(
            "accounts", "cleanup-inactive", new CleanupRequest { DaysInactive = 90 }, ct);

        await MeshClient.InvokeMethodAsync<CleanupRequest, CleanupResponse>(
            "sessions", "cleanup-expired", new CleanupRequest(), ct);

        Logger.LogInformation("Daily cleanup completed");
    }
}
```

### Chat Agent Actor

Schema:

```yaml
# schemas/actor-types/chat-agent.yaml
x-actor-type:
  name: chat-agent
  idle_timeout_seconds: 1800  # 30 min
  state:
    $ref: '#/components/schemas/ChatConversationState'

paths:
  /user-message:
    post:
      operationId: HandleUserMessage
      x-actor-message: user-message
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/UserMessageRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ChatResponse'

components:
  schemas:
    ChatConversationState:
      type: object
      properties:
        messages:
          type: array
          items:
            $ref: '#/components/schemas/ChatMessage'
        available_tools:
          type: array
          items:
            type: string
```

Implementation:

```csharp
// ChatAgentActor.cs
public partial class ChatAgentActor : ChatAgentActorBase
{
    public override async Task<ChatResponse> HandleUserMessageAsync(
        UserMessageRequest req, CancellationToken ct)
    {
        // Add to conversation history
        State.Messages.Add(new ChatMessage { Role = "user", Content = req.Content });

        // Call LLM service (via mesh)
        var llmResponse = await MeshClient.InvokeMethodAsync<LlmRequest, LlmResponse>(
            "llm-gateway", "complete", new LlmRequest
            {
                Messages = State.Messages,
                Tools = State.AvailableTools
            }, ct);

        // Add response to history
        State.Messages.Add(new ChatMessage { Role = "assistant", Content = llmResponse.Content });

        return new ChatResponse { Content = llmResponse.Content };
    }
}
```

---

## Actor Runtime

### Actor Service

The actor plugin provides a service that manages all actor instances:

```csharp
public interface IActorService
{
    /// <summary>Send a message to an actor (fire-and-forget).</summary>
    Task SendAsync(string actorAddress, string messageType, object message, CancellationToken ct);

    /// <summary>Invoke an actor and wait for response.</summary>
    Task<TResponse> InvokeAsync<TResponse>(string actorAddress, string messageType, object message, CancellationToken ct);

    /// <summary>Explicitly activate an actor.</summary>
    Task<ActorActivationResult> ActivateAsync(string actorAddress, CancellationToken ct);

    /// <summary>Explicitly deactivate an actor.</summary>
    Task<ActorDeactivationResult> DeactivateAsync(string actorAddress, CancellationToken ct);

    /// <summary>Get actor status.</summary>
    Task<ActorStatus> GetStatusAsync(string actorAddress, CancellationToken ct);

    /// <summary>List active actors of a type.</summary>
    Task<ActorListResult> ListActiveAsync(string actorType, int limit, string? cursor, CancellationToken ct);
}
```

### Actor Registration

Actor types are discovered at startup via assembly scanning (like services):

```csharp
// In lib-actor/ActorServicePlugin.cs
public class ActorServicePlugin : IBannouServicePlugin
{
    public void ConfigureServices(IServiceCollection services, PluginContext context)
    {
        // Scan for [Actor] attributed classes
        var actorTypes = AssemblyScanner.FindActorsInLoadedAssemblies();

        foreach (var actorType in actorTypes)
        {
            services.AddTransient(actorType.ImplementationType);
        }

        // Register actor runtime
        services.AddSingleton<IActorRuntime, ActorRuntime>();
        services.AddScoped<IActorService, ActorService>();
    }
}
```

---

## Infrastructure Integration

> **TENET Compliance**: All actor infrastructure follows Bannou TENETS:
> - **Tenet 4**: Use lib-state, lib-messaging, lib-mesh exclusively (no direct Redis/RabbitMQ/HTTP)
> - **Tenet 5**: All events use typed schemas (no anonymous objects)
> - **Tenet 20**: All JSON serialization uses `BannouJson` for consistent behavior
> - **Tenet 4**: Service calls use generated clients (e.g., `IBehaviorClient`) not raw mesh invocation

### State Persistence (lib-state)

Actors use lib-state for persistence. The actor plugin provides helpers, but **does not dictate state shape**:

```csharp
// Actor plugin provides these helpers
protected async Task<TState?> LoadStateAsync<TState>(CancellationToken ct)
{
    var store = GetStateStore<TState>("actor-state");
    return await store.GetAsync(ActorAddress, ct);
}

protected async Task SaveStateAsync<TState>(TState state, CancellationToken ct)
{
    var store = GetStateStore<TState>("actor-state");
    await store.SaveAsync(ActorAddress, state, cancellationToken: ct);
}
```

**State store configuration:**
```yaml
# provisioning/statestore-actor.yaml
actor-state:
  backend: mysql     # Persistent for actor state
  table: actor_state
  key_column: actor_address
```

### Event Subscription (lib-messaging)

Actors can subscribe to shared exchanges:

```csharp
// Subscribe to topic (all instances of this actor type get the event)
await SubscribeAsync<GameTimeTickEvent>("game.time.tick", ct);

// Unsubscribe
await UnsubscribeAsync("game.time.tick", ct);
```

### Personal Channel (like client events)

Each actor has a personal channel for direct messaging:

```
actor.personal.{actor-type}.{actor-id}
```

Other services or actors can send to this channel:

```csharp
// From another service
await _messageBus.PublishAsync(
    $"actor.personal.npc-brain.{characterId}",
    new PerceptionEvent { ... },
    cancellationToken: ct);
```

This uses the same pattern as client events (direct exchange, per-actor routing).

### Service Invocation (lib-mesh)

Actors have full access to call any internal service:

```csharp
var result = await MeshClient.InvokeMethodAsync<Request, Response>(
    "some-service", "some-method", request, ct);
```

---

## Placement Architecture

### Deployment Modes

The actor system supports two deployment modes:

#### Development Mode (Single Instance)

In development, one Bannou instance handles everything - just like the "bannou" omnipotent mode:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    SINGLE BANNOU INSTANCE                                │
│                       (Development Mode)                                 │
│                                                                          │
│  ┌─────────────────────────────────────────────────────────────────────┐│
│  │                      lib-actor Plugin                                ││
│  │                                                                      ││
│  │  ┌─────────────────────┐    ┌─────────────────────────────────────┐ ││
│  │  │  Placement Service   │    │  Actor Runtime                      │ ││
│  │  │  (local mode)        │───▶│  (hosts actors directly)            │ ││
│  │  │                      │    │                                      │ ││
│  │  │  • In-memory map     │    │  • All actors run locally           │ ││
│  │  │  • No network hops   │    │  • Same process, direct calls       │ ││
│  │  └─────────────────────┘    └─────────────────────────────────────┘ ││
│  └─────────────────────────────────────────────────────────────────────┘│
│                                                                          │
└──────────────────────────────────────────────────────────────────────────┘
```

Configuration:
```yaml
ACTOR_MODE: local  # Single instance handles placement + actors
```

#### Production Mode (Distributed)

In production, placement runs in control plane while actor nodes scale horizontally:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           CONTROL PLANE                                  │
│                    (Primary Bannou Instance)                             │
│                                                                          │
│  ┌─────────────────────┐    ┌─────────────────────┐                     │
│  │  Placement Service   │    │  Orchestrator       │                     │
│  │  (lib-actor)         │    │  (lib-orchestrator) │                     │
│  │                      │    │                      │                     │
│  │  • Actor→Node map    │    │  • Node scaling      │                     │
│  │  • Activation routing│    │  • Health monitoring │                     │
│  │  • Load balancing    │    │  • Pool management   │                     │
│  └──────────┬───────────┘    └──────────────────────┘                     │
│             │                                                             │
└─────────────┼─────────────────────────────────────────────────────────────┘
              │
              │ Actor assignments / Node registrations
              │
┌─────────────┼─────────────────────────────────────────────────────────────┐
│             ▼                                                             │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐           │
│  │  Actor Node 1   │  │  Actor Node 2   │  │  Actor Node N   │           │
│  │  (1000 actors)  │  │  (1000 actors)  │  │  (1000 actors)  │           │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘           │
│                                                                           │
│                          ACTOR NODES                                      │
│                    (Scaled by Orchestrator)                               │
└───────────────────────────────────────────────────────────────────────────┘
```

Configuration:
```yaml
# Control plane
ACTOR_MODE: control-plane  # Runs placement service only

# Actor nodes
ACTOR_MODE: worker  # Runs actor runtime only
ACTOR_PLACEMENT_ENDPOINT: http://control-plane:5012
```

**Why centralized placement?**

Unlike Dapr which requires a separate placement container, we leverage Bannou's existing patterns:

- **Single source of truth**: No distributed consensus needed
- **Simple data structures**: ConcurrentDictionary for actor→node mapping
- **No race conditions**: All placement decisions go through one service
- **Follows orchestrator pattern**: Same control-plane-only deployment model
- **Development simplicity**: Single instance mode for local development

### Placement Service

The Placement Service runs **only in the control plane** (same as orchestrator):

```csharp
/// <summary>
/// Centralized actor placement service.
/// Runs only on control plane - single source of truth for actor locations.
/// </summary>
[BannouService("actor-placement", typeof(IActorPlacementService))]
public class ActorPlacementService : IActorPlacementService
{
    // Single source of truth - no distributed state needed
    private readonly ConcurrentDictionary<string, ActorPlacement> _placements = new();
    private readonly ConcurrentDictionary<string, ActorNodeInfo> _nodes = new();
    private readonly object _placementLock = new();

    /// <summary>
    /// Get or assign placement for an actor.
    /// </summary>
    public Task<ActorPlacement> GetOrAssignPlacementAsync(
        string actorAddress,
        CancellationToken ct)
    {
        // Fast path: already placed
        if (_placements.TryGetValue(actorAddress, out var existing))
        {
            return Task.FromResult(existing);
        }

        // Slow path: assign to least-loaded node
        lock (_placementLock)
        {
            // Double-check after lock
            if (_placements.TryGetValue(actorAddress, out existing))
            {
                return Task.FromResult(existing);
            }

            var targetNode = SelectLeastLoadedNode();
            var placement = new ActorPlacement
            {
                ActorAddress = actorAddress,
                NodeId = targetNode.NodeId,
                AssignedAt = DateTimeOffset.UtcNow
            };

            _placements[actorAddress] = placement;
            targetNode.ActiveActorCount++;

            return Task.FromResult(placement);
        }
    }

    /// <summary>
    /// Register actor node with placement service.
    /// </summary>
    public Task RegisterNodeAsync(ActorNodeRegistration registration, CancellationToken ct)
    {
        _nodes[registration.NodeId] = new ActorNodeInfo
        {
            NodeId = registration.NodeId,
            Endpoint = registration.Endpoint,
            Capacity = registration.Capacity,
            ActiveActorCount = 0,
            LastHeartbeat = DateTimeOffset.UtcNow
        };
        return Task.CompletedTask;
    }

    /// <summary>
    /// Actor deactivated - remove placement.
    /// </summary>
    public Task RemovePlacementAsync(string actorAddress, CancellationToken ct)
    {
        if (_placements.TryRemove(actorAddress, out var placement))
        {
            if (_nodes.TryGetValue(placement.NodeId, out var node))
            {
                Interlocked.Decrement(ref node.ActiveActorCount);
            }
        }
        return Task.CompletedTask;
    }

    private ActorNodeInfo SelectLeastLoadedNode()
    {
        return _nodes.Values
            .Where(n => n.ActiveActorCount < n.Capacity)
            .OrderBy(n => n.ActiveActorCount)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No available actor nodes");
    }
}
```

### Single-Instance Guarantee

With centralized placement, single-instance is guaranteed by the placement service itself:

1. **All activations go through placement service** (single source of truth)
2. **Placement service uses local lock** (no distributed locking needed)
3. **Actor nodes confirm activation** via IDistributedLockProvider (defense in depth)

```csharp
// On actor node - defense in depth using existing IDistributedLockProvider
public async Task<ActivationResult> ActivateActorAsync(string actorAddress, CancellationToken ct)
{
    // Atomic claim as backup (in case placement service has stale data)
    var lockResponse = await _lockProvider.LockAsync(
        storeName: "actor-claims",
        resourceId: actorAddress,
        lockOwner: _nodeId,
        expiryInSeconds: 90,
        ct);

    if (!lockResponse.Success)
    {
        // Another node already has this actor
        return new ActivationResult { Success = false, Reason = "Already active" };
    }

    // Activation successful - store lock for later release
    var actor = await CreateActorAsync(actorAddress, lockResponse, ct);
    return new ActivationResult { Success = true, Actor = actor };
}
```

### Message Routing Flow

```
1. Client sends message to actor "npc-brain:abc123"
           │
           ▼
2. Actor Service receives message
           │
           ▼
3. Call Placement Service: "Where is npc-brain:abc123?"
           │
           ├─► If placed: Route to assigned node
           │
           └─► If not placed: Assign to least-loaded node
                      │
                      ▼
4. Target node activates actor (if not already active)
           │
           ▼
5. Message delivered to actor's mailbox
```

### Orchestrator Integration

Actor nodes register with orchestrator for scaling (same pattern as other services):

```yaml
# provisioning/orchestrator/presets/actor-nodes.yaml
name: actor-nodes
description: Actor processing node pool

processing_pools:
  - pool_type: actor-node
    min_instances: 10
    max_instances: 150
    scale_up_threshold: 0.8
    scale_down_threshold: 0.2
    actors_per_node: 1000
    environment:
      ACTOR_NODE_MODE: worker
      ACTOR_PLACEMENT_ENDPOINT: ${CONTROL_PLANE_ENDPOINT}
```

### Node Failure Handling

When an actor node fails:

1. **Heartbeat timeout** - Placement service marks node unhealthy
2. **Placements invalidated** - All actors on failed node removed from map
3. **Next message triggers reactivation** - Actor placed on healthy node
4. **State reloaded from lib-state** - No data loss (state was persisted)

No migration protocol needed - actors are transient over persistent state.

---

## Actor API Schema

```yaml
# schemas/actor-api.yaml
openapi: 3.0.3
info:
  title: Bannou Actor Service API
  version: 1.0.0
  description: General-purpose distributed actor management

servers:
  - url: http://localhost:5012

paths:
  /actor/send:
    post:
      operationId: SendToActor
      summary: Send a message to an actor (fire-and-forget)
      x-permissions:
        - role: user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/SendToActorRequest'
      responses:
        '202':
          description: Message accepted
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SendToActorResponse'

  /actor/invoke:
    post:
      operationId: InvokeActor
      summary: Invoke an actor and wait for response
      x-permissions:
        - role: user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/InvokeActorRequest'
      responses:
        '200':
          description: Actor response
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/InvokeActorResponse'

  /actor/activate:
    post:
      operationId: ActivateActor
      summary: Explicitly activate an actor
      x-permissions:
        - role: developer
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ActivateActorRequest'
      responses:
        '200':
          description: Actor activated
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ActivateActorResponse'

  /actor/deactivate:
    post:
      operationId: DeactivateActor
      summary: Explicitly deactivate an actor
      x-permissions:
        - role: developer
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/DeactivateActorRequest'
      responses:
        '200':
          description: Actor deactivated
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/DeactivateActorResponse'

  /actor/status:
    post:
      operationId: GetActorStatus
      summary: Get actor status
      x-permissions:
        - role: user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetActorStatusRequest'
      responses:
        '200':
          description: Actor status
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ActorStatus'

  /actor/list:
    post:
      operationId: ListActors
      summary: List active actors
      x-permissions:
        - role: developer
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListActorsRequest'
      responses:
        '200':
          description: Actor list
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ListActorsResponse'

  /actor/pool/status:
    post:
      operationId: GetActorPoolStatus
      summary: Get actor node pool status
      x-permissions:
        - role: admin
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetPoolStatusRequest'
      responses:
        '200':
          description: Pool status
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ActorPoolStatus'

components:
  schemas:
    SendToActorRequest:
      type: object
      required: [actor_address, message_type, message]
      properties:
        actor_address:
          type: string
          description: "Actor address in format {type}:{id}"
          example: "npc-brain:character-abc123"
        message_type:
          type: string
          description: "Message type (matches [ActorMessage] attribute)"
          example: "perception"
        message:
          type: object
          description: "Message payload (JSON)"

    SendToActorResponse:
      type: object
      properties:
        accepted:
          type: boolean
        actor_node:
          type: string
          description: "Node handling this actor"

    InvokeActorRequest:
      type: object
      required: [actor_address, message_type, message]
      properties:
        actor_address:
          type: string
        message_type:
          type: string
        message:
          type: object
        timeout_ms:
          type: integer
          default: 30000

    InvokeActorResponse:
      type: object
      properties:
        response:
          type: object
          description: "Actor's response payload"

    ActivateActorRequest:
      type: object
      required: [actor_address]
      properties:
        actor_address:
          type: string

    ActivateActorResponse:
      type: object
      properties:
        actor_address:
          type: string
        node_id:
          type: string
        already_active:
          type: boolean
        activated_at:
          type: string
          format: date-time

    DeactivateActorRequest:
      type: object
      required: [actor_address]
      properties:
        actor_address:
          type: string
        force:
          type: boolean
          default: false
          description: "Force immediate deactivation without draining inbox"

    DeactivateActorResponse:
      type: object
      properties:
        actor_address:
          type: string
        was_active:
          type: boolean
        messages_drained:
          type: integer

    GetActorStatusRequest:
      type: object
      required: [actor_address]
      properties:
        actor_address:
          type: string

    ActorStatus:
      type: object
      properties:
        actor_address:
          type: string
        is_active:
          type: boolean
        node_id:
          type: string
        activated_at:
          type: string
          format: date-time
        last_message_at:
          type: string
          format: date-time
        messages_processed:
          type: integer
        mailbox_size:
          type: integer

    ListActorsRequest:
      type: object
      properties:
        actor_type:
          type: string
          description: "Filter by actor type (optional)"
        active_only:
          type: boolean
          default: true
        limit:
          type: integer
          default: 100
        cursor:
          type: string

    ListActorsResponse:
      type: object
      properties:
        actors:
          type: array
          items:
            $ref: '#/components/schemas/ActorStatus'
        next_cursor:
          type: string

    GetPoolStatusRequest:
      type: object
      properties: {}

    ActorPoolStatus:
      type: object
      properties:
        total_nodes:
          type: integer
        healthy_nodes:
          type: integer
        total_actors_active:
          type: integer
        total_capacity:
          type: integer
        utilization_percent:
          type: number
          format: float
        nodes:
          type: array
          items:
            $ref: '#/components/schemas/ActorNodeStatus'

    ActorNodeStatus:
      type: object
      properties:
        node_id:
          type: string
        status:
          type: string
          enum: [healthy, degraded, unavailable]
        actors_active:
          type: integer
        actors_capacity:
          type: integer
        last_heartbeat:
          type: string
          format: date-time
```

---

## Configuration Schema

```yaml
# schemas/actor-configuration.yaml
x-service-configuration:
  properties:
    NodeId:
      type: string
      description: Unique identifier for this actor node
      env: ACTOR_NODE_ID

    ActorsPerNode:
      type: integer
      default: 1000
      description: Maximum actors per node
      env: ACTOR_MAX_PER_NODE

    DefaultIdleTimeoutSeconds:
      type: integer
      default: 300
      description: Default idle timeout for actors
      env: ACTOR_DEFAULT_IDLE_TIMEOUT_SECONDS

    DefaultMailboxCapacity:
      type: integer
      default: 100
      description: Default mailbox capacity
      env: ACTOR_DEFAULT_MAILBOX_CAPACITY

    ClaimTtlSeconds:
      type: integer
      default: 90
      description: TTL for actor instance claims
      env: ACTOR_CLAIM_TTL_SECONDS

    ClaimRefreshIntervalSeconds:
      type: integer
      default: 30
      description: How often to refresh claim TTL
      env: ACTOR_CLAIM_REFRESH_INTERVAL_SECONDS
```

---

## Implementation Roadmap

### Phase 1: Core Runtime

- [ ] Actor base class and attributes
- [ ] Actor registration via assembly scanning
- [ ] Actor activation/deactivation lifecycle
- [ ] Single-instance guarantee (claim mechanism)
- [ ] Mailbox pattern (Channel<T>)
- [ ] State persistence helpers
- [ ] Basic API endpoints (send, invoke, activate, deactivate, status)
- [ ] Unit tests

### Phase 2: Infrastructure Integration

- [ ] Event subscription for actors
- [ ] Personal channel per actor
- [ ] Scheduling (one-time and recurring)
- [ ] lib-mesh access for service calls
- [ ] Integration tests

### Phase 3: Placement and Scaling

- [ ] Consistent hashing for placement
- [ ] Node registration with orchestrator
- [ ] Pool status monitoring
- [ ] Auto-scaling based on utilization
- [ ] HTTP integration tests
- [ ] Edge tests

### Phase 4: Example Actors

- [ ] NPC brain actor (demonstrates Behavior plugin integration)
- [ ] Scheduled task actor
- [ ] Chat agent actor
- [ ] Documentation with examples

---

## What This Design Does NOT Include

These are explicitly **out of scope** for the Actor plugin:

| Concept | Why Excluded | Where It Belongs |
|---------|--------------|------------------|
| Memory (semantic, episodic) | NPC-specific | Behavior plugin or actor-type state |
| Perception interpretation | NPC-specific | Behavior plugin |
| GOAP planning | NPC-specific | Behavior plugin |
| Emotional models | NPC-specific | Actor-type state |
| Personality profiles | NPC-specific | Actor-type state |
| Migration protocol | Not needed | Deactivate/reactivate is sufficient |
| Attention system | NPC-specific | Behavior plugin |

---

## Resolved Questions

### ✅ Q1: Single-instance guarantee mechanism

**Resolved**: Use existing `IDistributedLockProvider` from lib-state.

The `RedisDistributedLockProvider` already implements atomic SET NX EX pattern. No lib-state enhancement needed.

### ✅ Q2: How do scheduled actors work with placement?

**Resolved**: Placement service handles this naturally.

- Scheduled actor (e.g., `daily-cleanup:global`) is assigned to one node by placement service
- If that node dies, placement is invalidated
- Next scheduled trigger activates on a healthy node
- Distributed lock (IDistributedLockProvider) ensures only one execution

### ✅ Q3: Placement architecture

**Resolved**: Centralized placement service in control plane.

- Single source of truth (ConcurrentDictionary)
- No distributed consensus needed
- Follows orchestrator pattern (control-plane only)
- Actor nodes register with placement service

---

## All Questions Resolved

### ✅ Q4: Should actor types be defined via schema or code?

**Decision**: Schema-based (YAML), following Bannou's established patterns.

Actor types defined in `schemas/actor-types/*.yaml` with code generation via `scripts/generate-actor-types.sh`.

### ✅ Q5: Should placement service be in lib-actor or lib-orchestrator?

**Decision**: lib-actor, with mode-based activation.

- `ACTOR_MODE=local` - Single instance (placement + actors together)
- `ACTOR_MODE=control-plane` - Placement service only
- `ACTOR_MODE=worker` - Actor runtime only

---

## References

### Design Documents
- [BEHAVIOR_PLUGIN_V2.md](./UPCOMING_-_BEHAVIOR_PLUGIN_V2.md) - Companion logic layer for NPC behaviors
- [ABML_V2_DESIGN_PROPOSAL.md](./ABML_V2_DESIGN_PROPOSAL.md) - ABML language specification
- [TENETS.md](../reference/TENETS.md) - Bannou development tenets (compliance required)

### Research
- [DISTRIBUTED-ACTORS.md](../research/DISTRIBUTED-ACTORS.md) - Research on actor systems
- [arcadia-kb: Distributed Agent Architecture](../../../arcadia-kb/05%20-%20NPC%20AI%20Design/Distributed%20Agent%20Architecture.md) - Avatar + Agent pattern
- [Orleans Virtual Actors](https://learn.microsoft.com/en-us/dotnet/orleans/overview) - Inspiration

---

*Document created: 2024-12-28*
*This is v2 of the Actors Plugin design, reframed as general-purpose.*
