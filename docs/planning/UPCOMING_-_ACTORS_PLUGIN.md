# Actors Plugin - Distributed Virtual Actor System for NPC Cognitive Processing

> **Status**: PLANNING
> **Last Updated**: 2024-12-27
> **Target Capacity**: 100,000 concurrent NPC actors (minimum production goal)
> **Research Document**: [DISTRIBUTED-ACTORS.md](../research/DISTRIBUTED-ACTORS.md)

---

## Executive Summary

The Actors Plugin implements a **Virtual Actor** pattern for NPC cognitive processing in distributed game simulations. Each NPC character maps 1:1 to an actor instance that:

1. **Receives** sensory events (perception, audio, environmental stimuli)
2. **Processes** information into short-term and long-term memories
3. **Maintains** motivations, knowledge, and emotional state
4. **Emits** objectives for game client execution (not direct movement commands)

The actor is a **directory, not a puppetmaster** - it assigns and reassigns objectives while the game client handles real-time execution (pathfinding, animation, physics).

### Design Principles

- **Virtual Actors**: Actors exist perpetually in a logical sense; activated on-demand, garbage-collected when idle
- **Accessor Pattern**: Access by character ID triggers activation if not already active (similar to statestores)
- **Event-Driven**: Perception flows in via pub/sub, objectives flow out via pub/sub
- **Infrastructure Reuse**: Built entirely on existing lib-mesh, lib-state, lib-messaging, and lib-orchestrator
- **Horizontal Scaling**: 1,000 actors per node, new nodes provisioned automatically via orchestrator

### Capacity Targets

| Metric | Target | Calculation |
|--------|--------|-------------|
| **Actors per Node** | 1,000 | Memory budget: ~10MB per actor × 1000 = 10GB headroom |
| **Production Minimum** | 100,000 actors | 100 nodes × 1,000 actors/node |
| **Peak Load** | 150,000 actors | Buffer for scale-up latency |
| **Activation Latency** | < 50ms | Cold start from persistent storage |
| **Event Processing** | < 10ms p99 | Per-perception event handling |

---

## Architecture Overview

### System Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           GAME CLIENT / ENGINE                               │
│  ┌─────────────────────────┐                    ┌─────────────────────────┐ │
│  │     Perception System    │                    │    Movement System       │ │
│  │  • Visual detection      │                    │  • Pathfinding           │ │
│  │  • Audio detection       │                    │  • Animation             │ │
│  │  • Environmental sense   │                    │  • Physics               │ │
│  └───────────┬─────────────┘                    └───────────▲─────────────┘ │
│              │                                               │               │
│              │ PerceptionEvent                   ObjectiveEvent              │
│              │ (pub/sub)                         (pub/sub)                   │
└──────────────┼───────────────────────────────────────────────┼───────────────┘
               │                                               │
               ▼                                               │
┌──────────────────────────────────────────────────────────────────────────────┐
│                           BANNOU ACTOR PLUGIN                                 │
│                                                                               │
│  ┌──────────────────────────────────────────────────────────────────────┐   │
│  │                     Actor Placement Service                           │   │
│  │  • Consistent hashing by character ID                                 │   │
│  │  • Routes to active node or activates new actor                       │   │
│  │  • Registers with lib-mesh for discovery                              │   │
│  └──────────────────────────────────────────────────────────────────────┘   │
│                                    │                                         │
│         ┌──────────────────────────┼──────────────────────────┐             │
│         ▼                          ▼                          ▼             │
│  ┌──────────────┐          ┌──────────────┐          ┌──────────────┐      │
│  │  Actor Node 1 │          │  Actor Node 2 │          │  Actor Node N │      │
│  │  (1000 actors)│          │  (1000 actors)│          │  (1000 actors)│      │
│  │               │          │               │          │               │      │
│  │ ┌───────────┐│          │ ┌───────────┐│          │ ┌───────────┐│      │
│  │ │ NPC Actor ││          │ │ NPC Actor ││          │ │ NPC Actor ││      │
│  │ │ ┌───────┐ ││          │ │ ┌───────┐ ││          │ │ ┌───────┐ ││      │
│  │ │ │Memory │ ││          │ │ │Memory │ ││          │ │ │Memory │ ││      │
│  │ │ │System │ ││  ...     │ │ │System │ ││  ...     │ │ │System │ ││      │
│  │ │ ├───────┤ ││          │ │ ├───────┤ ││          │ │ ├───────┤ ││      │
│  │ │ │ Goal  │ ││          │ │ │ Goal  │ ││          │ │ │ Goal  │ ││      │
│  │ │ │Planner│ ││          │ │ │Planner│ ││          │ │ │Planner│ ││      │
│  │ │ └───────┘ ││          │ │ └───────┘ ││          │ │ └───────┘ ││      │
│  │ └───────────┘│          │ └───────────┘│          │ └───────────┘│      │
│  └──────────────┘          └──────────────┘          └──────────────┘      │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
               │                                               ▲
               │                                               │
               ▼                                               │
┌──────────────────────────────────────────────────────────────────────────────┐
│                         INFRASTRUCTURE LAYER                                  │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐ │
│  │  lib-state   │  │lib-messaging│  │  lib-mesh   │  │  lib-orchestrator   │ │
│  │ Actor State  │  │   Events    │  │  Routing    │  │  Node Scaling       │ │
│  │ Persistence  │  │  Pub/Sub    │  │  Discovery  │  │  Pool Management    │ │
│  └─────────────┘  └─────────────┘  └─────────────┘  └─────────────────────┘ │
│        │                 │                │                    │             │
│        ▼                 ▼                ▼                    ▼             │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │                    Redis + MySQL + RabbitMQ                              ││
│  └─────────────────────────────────────────────────────────────────────────┘│
└──────────────────────────────────────────────────────────────────────────────┘
```

### Actor Lifecycle

```
                               ┌─────────────────────────────────┐
                               │        Character Created         │
                               │     (exists in game database)    │
                               └────────────────┬────────────────┘
                                                │
                                                ▼
                               ┌─────────────────────────────────┐
                               │       Actor: NOT ACTIVE         │
                               │   (Virtual existence only)      │
                               │   State: Persisted in MySQL     │
                               └────────────────┬────────────────┘
                                                │
                                    First perception event OR
                                    Direct invocation request
                                                │
                                                ▼
                ┌───────────────────────────────────────────────────────┐
                │                   ACTIVATION FLOW                      │
                │  1. Placement service determines target node           │
                │  2. Node loads actor state from MySQL                  │
                │  3. OnActivateAsync() lifecycle hook fires             │
                │  4. Actor added to Active Actors table (Redis)         │
                │  5. Actor begins processing messages                   │
                └───────────────────────────────────────────────────────┘
                                                │
                                                ▼
                               ┌─────────────────────────────────┐
                               │        Actor: ACTIVE             │
                               │   (In-memory, processing)        │
                               │   Hot state: Redis (TTL: 90s)   │
                               │   Cold state: MySQL              │
                               └────────────────┬────────────────┘
                                                │
                                    Idle for configurable period
                                    (default: 5 minutes)
                                                │
                                                ▼
                ┌───────────────────────────────────────────────────────┐
                │                  DEACTIVATION FLOW                     │
                │  1. OnDeactivateAsync() lifecycle hook fires           │
                │  2. Dirty state flushed to MySQL                       │
                │  3. Actor removed from Active Actors table             │
                │  4. In-memory object garbage collected                 │
                │  5. Actor returns to virtual state                     │
                └───────────────────────────────────────────────────────┘
                                                │
                                                ▼
                               ┌─────────────────────────────────┐
                               │       Actor: NOT ACTIVE         │
                               │  (Ready for reactivation)       │
                               └─────────────────────────────────┘
```

---

## Infrastructure Integration

### lib-state: Actor State Persistence

**Two-Tier State Strategy:**

| Tier | Backend | Purpose | TTL | Contents |
|------|---------|---------|-----|----------|
| **Hot** | Redis | Active actor working memory | 90 seconds | Short-term memory, current objectives, processing state |
| **Cold** | MySQL | Persistent character knowledge | None | Long-term memories, semantic knowledge, episodic history |

**State Store Configuration:**

```yaml
# In lib-actor/ActorServicePlugin.cs
actor-hot-statestore:
  backend: Redis
  key_prefix: "actor:hot"
  default_ttl_seconds: 90
  enable_search: false

actor-cold-statestore:
  backend: MySql
  table_name: "actor_state"
  enable_json_queries: true
```

**State Models:**

```csharp
/// <summary>
/// Hot state - loaded into memory during actor activation
/// </summary>
public class ActorHotState
{
    public string CharacterId { get; set; } = string.Empty;
    public DateTimeOffset ActivatedAt { get; set; }
    public DateTimeOffset LastMessageAt { get; set; }

    // Short-term memory (rolling buffer)
    public Queue<PerceptionMemory> RecentPerceptions { get; set; } = new(capacity: 50);

    // Current cognitive state
    public EmotionalState CurrentMood { get; set; } = new();
    public List<ActiveObjective> CurrentObjectives { get; set; } = new();
    public Dictionary<string, float> AttentionWeights { get; set; } = new();

    // Processing state
    public int MessageCount { get; set; }
    public bool IsDirty { get; set; }
}

/// <summary>
/// Cold state - persisted long-term, loaded on activation
/// </summary>
public class ActorColdState
{
    public string CharacterId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastActiveAt { get; set; }

    // Long-term memory (semantic + episodic)
    public List<SemanticMemory> Knowledge { get; set; } = new();
    public List<EpisodicMemory> Experiences { get; set; } = new();
    public List<ProceduralMemory> LearnedBehaviors { get; set; } = new();

    // Personality and motivations
    public PersonalityProfile Personality { get; set; } = new();
    public List<LongTermGoal> LifeGoals { get; set; } = new();
    public Dictionary<string, Relationship> Relationships { get; set; } = new();

    // Character knowledge graph
    public Dictionary<string, object> WorldKnowledge { get; set; } = new();
}
```

**State Operations:**

```csharp
// Hot state access pattern (per-actor, TTL-based)
var hotStore = _stateStoreFactory.GetStore<ActorHotState>("actor-hot-statestore");
await hotStore.SaveAsync($"{characterId}", hotState, new StateOptions { Ttl = 90 });

// Cold state access pattern (durable, queryable)
var coldStore = _stateStoreFactory.GetQueryableStore<ActorColdState>("actor-cold-statestore");
var state = await coldStore.GetAsync(characterId);

// Query for actors with specific knowledge (for social simulation)
var actorsWhoKnow = await coldStore.QueryAsync(
    a => a.WorldKnowledge.ContainsKey("quest:dragon-slayer:completed"));
```

### lib-messaging: Event Flows

**Inbound Events (Game → Actor):**

```yaml
# schemas/actor-events.yaml
components:
  schemas:
    PerceptionEvent:
      type: object
      required: [character_id, perception_type, timestamp]
      properties:
        event_id:
          type: string
          format: uuid
        character_id:
          type: string
          description: Target actor's character ID
        perception_type:
          type: string
          enum: [visual, audio, touch, environmental, temporal]
        timestamp:
          type: string
          format: date-time
        # Visual perception
        entities_seen:
          type: array
          items:
            $ref: '#/components/schemas/PerceivedEntity'
        entities_lost:
          type: array
          items:
            type: string
        # Audio perception
        sounds_heard:
          type: array
          items:
            $ref: '#/components/schemas/PerceivedSound'
        speech_detected:
          $ref: '#/components/schemas/SpeechEvent'
        # Environmental
        environment_change:
          $ref: '#/components/schemas/EnvironmentChange'

    PerceivedEntity:
      type: object
      properties:
        entity_id:
          type: string
        entity_type:
          type: string
          enum: [character, creature, object, vehicle]
        position:
          $ref: '#/components/schemas/Vector3'
        distance:
          type: number
          format: float
        facing_towards:
          type: boolean
        known_identity:
          type: string
          description: Name if recognized, null if unknown
        threat_level:
          type: number
          format: float
          minimum: 0.0
          maximum: 1.0
```

**Outbound Events (Actor → Game):**

```yaml
    ObjectiveEvent:
      type: object
      required: [character_id, objective_type, timestamp]
      properties:
        event_id:
          type: string
          format: uuid
        character_id:
          type: string
        objective_type:
          type: string
          enum: [assigned, updated, cancelled, completed, failed]
        objective:
          $ref: '#/components/schemas/Objective'
        reason:
          type: string
          description: Why the objective changed

    Objective:
      type: object
      properties:
        objective_id:
          type: string
          format: uuid
        objective_class:
          type: string
          enum: [movement, interaction, combat, social, work, idle]
        priority:
          type: integer
          minimum: 0
          maximum: 100
        # Movement objectives
        target_position:
          $ref: '#/components/schemas/Vector3'
        movement_style:
          type: string
          enum: [walk, run, sneak, patrol, wander]
        # Interaction objectives
        target_entity_id:
          type: string
        interaction_type:
          type: string
          enum: [speak, examine, use, pickup, attack, flee]
        # Parameters
        parameters:
          type: object
          additionalProperties: true
        # Constraints
        timeout_seconds:
          type: integer
        abandon_if:
          type: array
          items:
            type: string
            description: Conditions that should cancel this objective
```

**Topic Structure:**

```
# Inbound (partitioned by hash(character_id) % partition_count)
actor.perception.visual.{partition}
actor.perception.audio.{partition}
actor.perception.environment.{partition}
actor.interaction.{partition}
actor.time.tick                      # Global time advancement

# Outbound (partitioned by hash(character_id) % partition_count)
actor.objective.assigned.{partition}
actor.objective.updated.{partition}
actor.objective.cancelled.{partition}

# Internal coordination
actor.activation.{node_id}
actor.deactivation.{node_id}
actor.migration.request
actor.migration.complete
```

**Event Subscription Pattern:**

```csharp
public partial class ActorService
{
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        // Perception events - routed to specific actors
        eventConsumer.RegisterHandler<IActorService, PerceptionEvent>(
            "actor.perception.*",
            async (svc, evt) => await ((ActorService)svc).HandlePerceptionAsync(evt));

        // Time tick - broadcast to all active actors
        eventConsumer.RegisterHandler<IActorService, TimeTickEvent>(
            "actor.time.tick",
            async (svc, evt) => await ((ActorService)svc).HandleTimeTickAsync(evt));

        // Migration requests
        eventConsumer.RegisterHandler<IActorService, ActorMigrationRequest>(
            $"actor.migration.{_nodeId}",
            async (svc, evt) => await ((ActorService)svc).HandleMigrationRequestAsync(evt));
    }
}
```

### lib-mesh: Actor Routing and Discovery

**Actor Node Registration:**

```csharp
// Each actor node registers with mesh for discovery
var endpoint = new MeshEndpoint
{
    InstanceId = Guid.Parse(Program.ServiceGUID),
    AppId = $"actor-node-{_nodeId}",  // e.g., "actor-node-001"
    Host = Environment.GetEnvironmentVariable("MESH_ENDPOINT_HOST") ?? _nodeId,
    Port = 80,
    Status = EndpointStatus.Healthy,
    Services = new List<string> { "actor" },
    MaxConnections = 1000,  // Actor capacity
    CurrentConnections = _activeActorCount,
    RegisteredAt = DateTimeOffset.UtcNow,
    LastSeen = DateTimeOffset.UtcNow
};

await _meshRedisManager.RegisterEndpointAsync(endpoint, ttlSeconds: 90);
```

**Actor-to-Actor Invocation:**

```csharp
// Direct invocation for social interactions
public async Task<SocialResponse> RequestSocialInteractionAsync(
    string targetCharacterId,
    SocialRequest request,
    CancellationToken cancellationToken = default)
{
    // Resolve target actor's node via placement service
    var targetNode = await _actorPlacement.GetActorLocationAsync(targetCharacterId);

    if (targetNode == null)
    {
        // Actor not active - activate it first
        targetNode = await _actorPlacement.ActivateActorAsync(targetCharacterId);
    }

    // Invoke via mesh
    return await _meshClient.InvokeMethodAsync<SocialRequest, SocialResponse>(
        appId: targetNode.AppId,
        methodName: $"actor/{targetCharacterId}/social",
        request: request,
        cancellationToken: cancellationToken);
}
```

### lib-orchestrator: Node Scaling

**Actor Pool Configuration:**

```yaml
# provisioning/orchestrator/presets/actor-processing.yaml
name: actor-processing
description: Actor node pool for NPC cognitive processing
category: processing

processing_pools:
  - pool_type: actor-node
    min_instances: 10        # Minimum nodes (10,000 actors capacity)
    max_instances: 150       # Maximum nodes (150,000 actors capacity)
    scale_up_threshold: 0.8  # Scale up at 80% utilization
    scale_down_threshold: 0.2 # Scale down at 20% utilization
    environment:
      ACTOR_NODE_MODE: worker
      ACTOR_MAX_ACTORS_PER_NODE: "1000"
      ACTOR_IDLE_TIMEOUT_SECONDS: "300"
      ACTOR_STATE_FLUSH_INTERVAL_SECONDS: "30"

topology:
  nodes:
    - name: actor-coordinator
      services: [actor]
      replicas: 1
      environment:
        ACTOR_NODE_MODE: coordinator
        ACTOR_POOL_TYPE: actor-node
```

**Scaling Integration:**

```csharp
public class ActorPlacementService
{
    private readonly IOrchestratorClient _orchestrator;
    private readonly IMeshInvocationClient _meshClient;

    /// <summary>
    /// Called when actor activation fails due to capacity
    /// </summary>
    public async Task<bool> RequestScaleUpAsync(CancellationToken cancellationToken = default)
    {
        var status = await _orchestrator.GetPoolStatusAsync(
            new GetPoolStatusRequest { Pool_type = "actor-node" },
            cancellationToken);

        if (status.Utilization > status.Scale_up_threshold)
        {
            var newTarget = Math.Min(
                status.Total_instances + 5,  // Add 5 nodes at a time
                status.Max_instances);

            await _orchestrator.ScalePoolAsync(new ScalePoolRequest
            {
                Pool_type = "actor-node",
                Target_instances = newTarget
            }, cancellationToken);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Periodic cleanup of underutilized nodes
    /// </summary>
    public async Task PerformScaleDownCheckAsync(CancellationToken cancellationToken = default)
    {
        var status = await _orchestrator.GetPoolStatusAsync(
            new GetPoolStatusRequest { Pool_type = "actor-node" },
            cancellationToken);

        if (status.Utilization < status.Scale_down_threshold
            && status.Total_instances > status.Min_instances)
        {
            // Migrate actors off lowest-utilized node before removal
            var nodeToRemove = await SelectLowestUtilizedNodeAsync();
            await MigrateActorsFromNodeAsync(nodeToRemove);

            await _orchestrator.ScalePoolAsync(new ScalePoolRequest
            {
                Pool_type = "actor-node",
                Target_instances = status.Total_instances - 1
            }, cancellationToken);
        }
    }
}
```

---

## Actor Placement Service

### Consistent Hashing Algorithm

Actor placement uses consistent hashing to ensure:
1. **Deterministic routing**: Same character ID always routes to same node (when topology unchanged)
2. **Minimal redistribution**: Adding/removing nodes only moves ~1/N actors
3. **Load balancing**: Virtual nodes ensure even distribution

```csharp
public class ConsistentHashRing
{
    private readonly SortedDictionary<uint, string> _ring = new();
    private readonly int _virtualNodesPerPhysical = 150;

    public void AddNode(string nodeId)
    {
        for (int i = 0; i < _virtualNodesPerPhysical; i++)
        {
            var virtualKey = $"{nodeId}:{i}";
            var hash = ComputeHash(virtualKey);
            _ring[hash] = nodeId;
        }
    }

    public void RemoveNode(string nodeId)
    {
        var keysToRemove = _ring
            .Where(kvp => kvp.Value == nodeId)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _ring.Remove(key);
        }
    }

    public string GetNode(string characterId)
    {
        if (_ring.Count == 0) return null;

        var hash = ComputeHash(characterId);

        // Find first node with hash >= character hash
        foreach (var kvp in _ring)
        {
            if (kvp.Key >= hash)
                return kvp.Value;
        }

        // Wrap around to first node
        return _ring.First().Value;
    }

    private static uint ComputeHash(string key)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
        return BitConverter.ToUInt32(bytes, 0);
    }
}
```

### Actor Directory (Redis-Backed)

```
Redis Keys:
actor:directory:{characterId}     → NodeId where actor is active (TTL: 90s)
actor:node:{nodeId}:actors        → Set of active character IDs on node
actor:node:{nodeId}:metrics       → Node utilization metrics
actor:hash-ring:version           → Ring topology version (incremented on node changes)
```

**Lookup Flow:**

```
1. Check actor:directory:{characterId}
   ├─ Found: Return cached node location
   └─ Not found: Continue to step 2

2. Compute hash ring position for characterId
   ├─ Get node from consistent hash
   └─ Validate node is healthy via mesh

3. Send activation request to target node
   ├─ Node confirms activation
   ├─ Update actor:directory with TTL
   └─ Return node location
```

---

## Memory Architecture

### Memory Types

Following the CoALA (Cognitive Architectures for Language Agents) framework:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                          ACTOR MEMORY SYSTEM                             │
│                                                                          │
│  ┌─────────────────────────────────────────────────────────────────────┐│
│  │                    SHORT-TERM MEMORY (STM)                          ││
│  │  Storage: In-memory (ActorHotState)                                 ││
│  │  Capacity: Last 50 perceptions                                      ││
│  │  Purpose: Immediate context for decision-making                     ││
│  │                                                                      ││
│  │  ┌───────────────────────────────────────────────────────────────┐  ││
│  │  │ RecentPerceptions: Queue<PerceptionMemory>                    │  ││
│  │  │ [t-0] Saw player_123 approach from east                       │  ││
│  │  │ [t-1] Heard footsteps behind                                  │  ││
│  │  │ [t-2] Weather changed to rain                                 │  ││
│  │  │ ...                                                           │  ││
│  │  └───────────────────────────────────────────────────────────────┘  ││
│  └─────────────────────────────────────────────────────────────────────┘│
│                                    │                                     │
│                     Memory consolidation (periodic)                      │
│                                    ▼                                     │
│  ┌─────────────────────────────────────────────────────────────────────┐│
│  │                    LONG-TERM MEMORY (LTM)                           ││
│  │  Storage: MySQL (ActorColdState)                                    ││
│  │                                                                      ││
│  │  ┌─────────────────────┐  ┌─────────────────────┐                   ││
│  │  │   SEMANTIC MEMORY   │  │   EPISODIC MEMORY   │                   ││
│  │  │   (Facts/Knowledge) │  │   (Experiences)     │                   ││
│  │  │                     │  │                     │                   ││
│  │  │ • World facts       │  │ • Specific events   │                   ││
│  │  │ • Entity knowledge  │  │ • Time-stamped      │                   ││
│  │  │ • Relationships     │  │ • Emotional context │                   ││
│  │  │ • Location data     │  │ • Participants      │                   ││
│  │  └─────────────────────┘  └─────────────────────┘                   ││
│  │                                                                      ││
│  │  ┌─────────────────────┐                                            ││
│  │  │  PROCEDURAL MEMORY  │                                            ││
│  │  │  (Learned Behaviors)│                                            ││
│  │  │                     │                                            ││
│  │  │ • Combat patterns   │                                            ││
│  │  │ • Social scripts    │                                            ││
│  │  │ • Work routines     │                                            ││
│  │  └─────────────────────┘                                            ││
│  └─────────────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────────────┘
```

### Memory Models

```csharp
public class PerceptionMemory
{
    public Guid MemoryId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public PerceptionType Type { get; set; }
    public string Summary { get; set; } = string.Empty;  // Natural language summary
    public float Importance { get; set; }  // 0.0 - 1.0, used for consolidation
    public float EmotionalImpact { get; set; }  // -1.0 to 1.0
    public Dictionary<string, object> Details { get; set; } = new();
}

public class SemanticMemory
{
    public Guid MemoryId { get; set; }
    public string Subject { get; set; } = string.Empty;   // Entity or concept
    public string Predicate { get; set; } = string.Empty; // Relationship type
    public string Object { get; set; } = string.Empty;    // Target
    public float Confidence { get; set; }  // How sure the actor is
    public DateTimeOffset LearnedAt { get; set; }
    public DateTimeOffset LastReinforcedAt { get; set; }
    public int ReinforcementCount { get; set; }
    public string Source { get; set; } = string.Empty;  // How actor learned this
}

public class EpisodicMemory
{
    public Guid MemoryId { get; set; }
    public string NarrativeSummary { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public string Location { get; set; } = string.Empty;
    public List<string> Participants { get; set; } = new();
    public float EmotionalValence { get; set; }  // -1.0 (negative) to 1.0 (positive)
    public float Significance { get; set; }  // How important to the actor
    public List<string> Tags { get; set; } = new();  // Semantic tags for retrieval
}

public class ProceduralMemory
{
    public Guid MemoryId { get; set; }
    public string BehaviorName { get; set; } = string.Empty;
    public string TriggerCondition { get; set; } = string.Empty;  // When to use
    public List<string> ActionSequence { get; set; } = new();  // What to do
    public float Proficiency { get; set; }  // 0.0 (novice) to 1.0 (expert)
    public int ExecutionCount { get; set; }
    public float SuccessRate { get; set; }
}
```

### Memory Retrieval

Following Stanford Smallville's approach, memory retrieval combines three factors:

```csharp
public class MemoryRetriever
{
    /// <summary>
    /// Retrieve relevant memories for current context
    /// </summary>
    public async Task<List<Memory>> RetrieveAsync(
        ActorColdState state,
        string query,
        int limit = 10)
    {
        var candidates = new List<(Memory memory, float score)>();

        foreach (var memory in GetAllMemories(state))
        {
            var score = CalculateRetrievalScore(memory, query);
            candidates.Add((memory, score));
        }

        return candidates
            .OrderByDescending(c => c.score)
            .Take(limit)
            .Select(c => c.memory)
            .ToList();
    }

    private float CalculateRetrievalScore(Memory memory, string query)
    {
        // Three-factor retrieval (Stanford Smallville approach)
        var recency = CalculateRecencyScore(memory.Timestamp);
        var importance = memory.Importance;
        var relevance = CalculateRelevanceScore(memory, query);

        // Weighted combination
        return (0.3f * recency) + (0.3f * importance) + (0.4f * relevance);
    }

    private float CalculateRecencyScore(DateTimeOffset timestamp)
    {
        var hoursSince = (DateTimeOffset.UtcNow - timestamp).TotalHours;
        // Exponential decay: recent memories score higher
        return (float)Math.Exp(-hoursSince / 24.0);  // Half-life of 24 hours
    }

    private float CalculateRelevanceScore(Memory memory, string query)
    {
        // Simple keyword matching (future: vector embedding similarity)
        var queryTerms = query.ToLowerInvariant().Split(' ');
        var memoryText = memory.GetSearchableText().ToLowerInvariant();

        var matches = queryTerms.Count(t => memoryText.Contains(t));
        return (float)matches / queryTerms.Length;
    }
}
```

---

## Actor API Design

### OpenAPI Schema

```yaml
# schemas/actor-api.yaml
openapi: 3.0.3
info:
  title: Bannou Actor Service API
  version: 1.0.0
  description: Virtual actor system for NPC cognitive processing

servers:
  - url: http://localhost:5012

paths:
  /actor/activate:
    post:
      operationId: ActivateActor
      summary: Activate an actor for a character
      x-permissions:
        - actor:activate
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ActivateActorRequest'
      responses:
        '200':
          description: Actor activated successfully
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ActivateActorResponse'
        '503':
          description: No capacity available (all nodes at max)

  /actor/deactivate:
    post:
      operationId: DeactivateActor
      summary: Gracefully deactivate an actor
      x-permissions:
        - actor:deactivate
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

  /actor/{character_id}/state:
    post:
      operationId: GetActorState
      summary: Get current actor state
      x-permissions:
        - actor:read
      parameters:
        - name: character_id
          in: path
          required: true
          schema:
            type: string
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetActorStateRequest'
      responses:
        '200':
          description: Actor state retrieved
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/GetActorStateResponse'
        '404':
          description: Actor not found

  /actor/{character_id}/perception:
    post:
      operationId: SendPerception
      summary: Send perception event to actor
      x-permissions:
        - actor:perception
      parameters:
        - name: character_id
          in: path
          required: true
          schema:
            type: string
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/PerceptionEvent'
      responses:
        '202':
          description: Perception accepted for processing
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PerceptionAcceptedResponse'

  /actor/{character_id}/objectives:
    post:
      operationId: GetActorObjectives
      summary: Get actor's current objectives
      x-permissions:
        - actor:read
      parameters:
        - name: character_id
          in: path
          required: true
          schema:
            type: string
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetObjectivesRequest'
      responses:
        '200':
          description: Objectives retrieved
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/GetObjectivesResponse'

  /actor/{character_id}/memory/query:
    post:
      operationId: QueryActorMemory
      summary: Query actor's memory
      x-permissions:
        - actor:memory:read
      parameters:
        - name: character_id
          in: path
          required: true
          schema:
            type: string
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/MemoryQueryRequest'
      responses:
        '200':
          description: Memory query results
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/MemoryQueryResponse'

  /actor/pool/status:
    post:
      operationId: GetActorPoolStatus
      summary: Get actor node pool status
      x-permissions:
        - actor:admin
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

  /actor/pool/scale:
    post:
      operationId: ScaleActorPool
      summary: Scale actor node pool
      x-permissions:
        - actor:admin:scale
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ScalePoolRequest'
      responses:
        '200':
          description: Scale operation initiated
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ScalePoolResponse'

components:
  schemas:
    ActivateActorRequest:
      type: object
      required: [character_id]
      properties:
        character_id:
          type: string
        force_node:
          type: string
          description: Optional specific node to activate on

    ActivateActorResponse:
      type: object
      properties:
        character_id:
          type: string
        node_id:
          type: string
        app_id:
          type: string
        already_active:
          type: boolean
        activated_at:
          type: string
          format: date-time

    GetActorStateRequest:
      type: object
      properties:
        include_hot_state:
          type: boolean
          default: true
        include_cold_state:
          type: boolean
          default: false
        include_objectives:
          type: boolean
          default: true

    GetActorStateResponse:
      type: object
      properties:
        character_id:
          type: string
        is_active:
          type: boolean
        node_id:
          type: string
        hot_state:
          $ref: '#/components/schemas/ActorHotStateSummary'
        cold_state:
          $ref: '#/components/schemas/ActorColdStateSummary'
        current_objectives:
          type: array
          items:
            $ref: '#/components/schemas/Objective'

    ActorPoolStatus:
      type: object
      properties:
        total_nodes:
          type: integer
        active_nodes:
          type: integer
        total_actor_capacity:
          type: integer
        active_actors:
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
        app_id:
          type: string
        status:
          type: string
          enum: [healthy, degraded, unavailable]
        actor_count:
          type: integer
        max_actors:
          type: integer
        utilization_percent:
          type: number
          format: float
        last_heartbeat:
          type: string
          format: date-time
```

---

## Scaling Mathematics

### Capacity Planning for 100,000 Actors

**Memory Budget per Actor:**

| Component | Size | Notes |
|-----------|------|-------|
| Hot State (in-memory) | ~5 MB | 50 recent perceptions × 100KB avg |
| Cold State (on-demand) | ~10 MB | Loaded on activation, paginated |
| Runtime overhead | ~1 MB | Object references, caches |
| **Total per actor** | **~6 MB active** | Cold state streamed as needed |

**Node Sizing:**

| Metric | Value | Calculation |
|--------|-------|-------------|
| Memory per node | 16 GB available | Container allocation |
| Actor memory budget | 10 GB | 62.5% of node memory |
| Actors per node | 1,000 | 10 GB / 10 MB per actor (with buffer) |
| Nodes for 100K actors | 100 | 100,000 / 1,000 |
| Buffer nodes | 10 | 10% headroom for spikes |
| **Total nodes minimum** | **110** | Production deployment |

**Scaling Thresholds:**

```yaml
scale_up_threshold: 0.8   # 800 actors per node triggers scale-up
scale_down_threshold: 0.2 # 200 actors per node triggers scale-down
min_instances: 10         # Never go below 10 nodes (10K capacity)
max_instances: 150        # Cap at 150 nodes (150K capacity)
```

**Event Throughput:**

| Event Type | Rate per Actor | Total Rate (100K actors) | Notes |
|------------|----------------|--------------------------|-------|
| Visual perception | 2/second | 200,000/second | Most frequent |
| Audio perception | 0.5/second | 50,000/second | When nearby sounds |
| Environmental | 0.1/second | 10,000/second | Weather, time, etc. |
| Objective updates | 0.05/second | 5,000/second | Output events |
| **Total throughput** | **~2.65/second/actor** | **~265,000/second** | System-wide |

**RabbitMQ Partitioning:**

```
Partition count: 100 (1 partition per ~1000 actors)
Messages per partition: ~2,650/second
Consumer threads per node: 10 (handles 10 partitions)
```

---

## Implementation Roadmap

### Phase 1: Foundation (Core Actor Infrastructure)

**Objective**: Basic actor lifecycle with single-node operation

- [ ] Create `lib-actor` plugin scaffold
- [ ] Define actor state models (`ActorHotState`, `ActorColdState`)
- [ ] Implement actor activation/deactivation lifecycle
- [ ] Create state store configuration (hot: Redis, cold: MySQL)
- [ ] Basic perception event handling (visual only)
- [ ] Simple objective emission (movement only)
- [ ] Unit tests for actor state management

**Files to Create:**
```
lib-actor/
├── ActorService.cs              # Core service implementation
├── ActorServicePlugin.cs        # Plugin configuration
├── ActorServiceEvents.cs        # Event handler registration
├── Services/
│   ├── IActorManager.cs         # Actor lifecycle interface
│   ├── ActorManager.cs          # Lifecycle implementation
│   ├── IActorStateManager.cs    # State persistence interface
│   └── ActorStateManager.cs     # State persistence implementation
├── Models/
│   ├── ActorHotState.cs
│   ├── ActorColdState.cs
│   ├── PerceptionMemory.cs
│   └── Objective.cs
└── Generated/                   # Auto-generated from schema
```

**Schema to Create:**
```
schemas/
├── actor-api.yaml
├── actor-events.yaml
├── actor-configuration.yaml
└── actor-client-events.yaml
```

### Phase 2: Distribution (Multi-Node Operation)

**Objective**: Actor placement across multiple nodes with consistent hashing

- [ ] Implement `ConsistentHashRing` for actor placement
- [ ] Create actor directory service (Redis-backed)
- [ ] Mesh registration for actor nodes
- [ ] Actor location lookup and caching
- [ ] Inter-node actor invocation via lib-mesh
- [ ] Actor migration protocol (node shutdown handling)
- [ ] Integration tests with 2-3 nodes

**Files to Create:**
```
lib-actor/Services/
├── IActorPlacementService.cs
├── ActorPlacementService.cs
├── ConsistentHashRing.cs
└── ActorDirectoryService.cs
```

### Phase 3: Memory System

**Objective**: Full memory architecture with retrieval

- [ ] Short-term memory (rolling buffer)
- [ ] Long-term memory models (semantic, episodic, procedural)
- [ ] Memory consolidation (STM → LTM)
- [ ] Memory retrieval with relevance scoring
- [ ] Memory queries via API
- [ ] Personality and relationship models
- [ ] Memory system tests

**Files to Create:**
```
lib-actor/Memory/
├── IMemoryManager.cs
├── MemoryManager.cs
├── MemoryRetriever.cs
├── MemoryConsolidator.cs
├── Models/
│   ├── SemanticMemory.cs
│   ├── EpisodicMemory.cs
│   ├── ProceduralMemory.cs
│   ├── PersonalityProfile.cs
│   └── Relationship.cs
```

### Phase 4: Scaling Integration

**Objective**: Orchestrator integration for elastic scaling

- [ ] Actor pool configuration preset
- [ ] Pool status monitoring
- [ ] Auto-scale triggers (utilization-based)
- [ ] Scale-up flow with actor redistribution
- [ ] Scale-down flow with graceful migration
- [ ] Health checks and degradation handling
- [ ] Load testing with 10,000+ actors

**Files to Create:**
```
lib-actor/Scaling/
├── IActorPoolManager.cs
├── ActorPoolManager.cs
├── ActorScalingPolicy.cs
└── ActorMigrationService.cs

provisioning/orchestrator/presets/
└── actor-processing.yaml
```

### Phase 5: Full Perception Pipeline

**Objective**: Complete perception event handling

- [ ] Visual perception processing
- [ ] Audio perception processing
- [ ] Speech recognition integration hooks
- [ ] Environmental perception
- [ ] Temporal perception (time awareness)
- [ ] Perception fusion (combining multiple senses)
- [ ] Attention system (prioritizing perceptions)

**Files to Create:**
```
lib-actor/Perception/
├── IPerceptionProcessor.cs
├── PerceptionProcessor.cs
├── VisualPerceptionHandler.cs
├── AudioPerceptionHandler.cs
├── EnvironmentalPerceptionHandler.cs
├── PerceptionFusion.cs
└── AttentionSystem.cs
```

### Phase 6: Goal Planning System

**Objective**: GOAP-style objective generation

- [ ] Goal evaluation based on motivations
- [ ] Action planning with preconditions
- [ ] Objective prioritization
- [ ] Objective conflict resolution
- [ ] Reactive objective changes (interrupts)
- [ ] Social objectives (NPC-to-NPC)
- [ ] Planning system tests

**Files to Create:**
```
lib-actor/Planning/
├── IGoalPlanner.cs
├── GoalPlanner.cs
├── ActionLibrary.cs
├── ObjectivePrioritizer.cs
├── InterruptHandler.cs
└── SocialPlanner.cs
```

### Phase 7: Production Hardening

**Objective**: Production-ready with full test coverage

- [ ] HTTP integration tests
- [ ] WebSocket edge tests
- [ ] Chaos testing (node failures)
- [ ] Performance profiling
- [ ] Memory leak detection
- [ ] Documentation updates
- [ ] Deployment guide

---

## Configuration Schema

```yaml
# schemas/actor-configuration.yaml
type: object
x-env-prefix: ACTOR_
properties:
  # Node configuration
  NodeId:
    type: string
    description: Unique identifier for this actor node
    x-env-key: ACTOR_NODE_ID

  NodeMode:
    type: string
    enum: [coordinator, worker]
    default: worker
    x-env-key: ACTOR_NODE_MODE

  MaxActorsPerNode:
    type: integer
    default: 1000
    minimum: 100
    maximum: 5000
    x-env-key: ACTOR_MAX_ACTORS_PER_NODE

  # Actor lifecycle
  IdleTimeoutSeconds:
    type: integer
    default: 300
    description: Deactivate actors after this many seconds of inactivity
    x-env-key: ACTOR_IDLE_TIMEOUT_SECONDS

  StateFlushIntervalSeconds:
    type: integer
    default: 30
    description: Flush dirty state to persistent storage at this interval
    x-env-key: ACTOR_STATE_FLUSH_INTERVAL_SECONDS

  ActivationTimeoutMs:
    type: integer
    default: 5000
    description: Maximum time to wait for actor activation
    x-env-key: ACTOR_ACTIVATION_TIMEOUT_MS

  # Memory configuration
  ShortTermMemoryCapacity:
    type: integer
    default: 50
    description: Maximum perceptions in short-term memory
    x-env-key: ACTOR_STM_CAPACITY

  MemoryConsolidationIntervalSeconds:
    type: integer
    default: 60
    description: How often to consolidate STM to LTM
    x-env-key: ACTOR_MEMORY_CONSOLIDATION_INTERVAL_SECONDS

  # Pool configuration (coordinator only)
  PoolType:
    type: string
    default: actor-node
    x-env-key: ACTOR_POOL_TYPE

  MinInstances:
    type: integer
    default: 10
    x-env-key: ACTOR_MIN_INSTANCES

  MaxInstances:
    type: integer
    default: 150
    x-env-key: ACTOR_MAX_INSTANCES

  ScaleUpThreshold:
    type: number
    format: float
    default: 0.8
    x-env-key: ACTOR_SCALE_UP_THRESHOLD

  ScaleDownThreshold:
    type: number
    format: float
    default: 0.2
    x-env-key: ACTOR_SCALE_DOWN_THRESHOLD
```

---

## Risk Analysis

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Memory exhaustion from actor accumulation | Medium | High | Strict per-node limits, aggressive idle deactivation, memory pressure monitoring |
| Hotspot from uneven actor distribution | Low | Medium | Consistent hashing with 150 virtual nodes ensures even distribution |
| State inconsistency during migration | Medium | Medium | Two-phase migration with distributed lock during transition |
| RabbitMQ backpressure from perception flood | Medium | High | Per-partition rate limiting, perception batching, priority queues |
| Cold start latency spikes | Low | Medium | Pre-warming based on predictive activation, SSD-backed MySQL |
| Orchestrator unavailability | Low | High | Graceful degradation: continue with current capacity, retry scaling |
| Redis failure affecting hot state | Low | Critical | Redis Sentinel/Cluster, cold state fallback, periodic MySQL sync |
| Network partition between nodes | Low | High | Quorum-based directory updates, split-brain detection |

---

## Open Considerations

### Future Enhancements (Not in Initial Scope)

1. **Vector Embeddings for Memory**: Replace keyword retrieval with semantic similarity using embeddings
2. **LLM Integration**: Optional language model for natural conversation and reflection
3. **Behavior Tree Authoring**: Visual tool for defining procedural memories
4. **Memory Sharing**: Cross-actor knowledge propagation (gossip/rumor system)
5. **Time Dilation**: Variable simulation speed per region

### Rejected Alternatives

| Alternative | Reason for Rejection |
|-------------|---------------------|
| Orleans for actor framework | Adds significant dependency; we already have all building blocks in lib-* infrastructure |
| Akka.NET | Same reasoning; prefer native implementation |
| Single-threaded actor model | Too limiting for complex cognitive processing; using turn-based concurrency instead |
| In-process actor activation | Doesn't scale; cloud-native approach with network actors is required |
| Kubernetes StatefulSets | Overkill for actor pods; orchestrator's processor pools are sufficient |

### Design Decisions to Validate

- [ ] Confirm 1,000 actors per node is achievable with memory profiling
- [ ] Validate RabbitMQ can handle 265K events/second with proposed partition count
- [ ] Test consistent hash redistribution latency when adding/removing nodes
- [ ] Benchmark MySQL cold state load time for acceptable activation latency
- [ ] Verify Redis TTL behavior under heavy write load

---

## References

- [DISTRIBUTED-ACTORS.md](../research/DISTRIBUTED-ACTORS.md) - Research compilation
- [Orleans Virtual Actors](https://learn.microsoft.com/en-us/dotnet/orleans/overview) - Virtual actor inspiration
- [Stanford Smallville](https://arxiv.org/abs/2304.03442) - Memory architecture reference
- [lib-orchestrator](../../lib-orchestrator/) - Processor pool patterns
- [lib-state](../../lib-state/) - State persistence patterns
- [lib-mesh](../../lib-mesh/) - Service mesh patterns
- [lib-messaging](../../lib-messaging/) - Event pub/sub patterns

---

*Document created: December 2024*
*For Bannou Service Actor Plugin Development*
