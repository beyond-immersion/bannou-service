# Distributed Actor Systems for Game Simulations

> Research compiled for Bannou Actor Plugin development - an infrastructure for NPC cognitive processing, sensory event handling, and objective management in distributed game simulations.

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [The Actor Model: Foundations](#the-actor-model-foundations)
3. [Virtual Actors: The Evolution](#virtual-actors-the-evolution)
4. [Framework Comparison](#framework-comparison)
5. [Game Development Use Cases](#game-development-use-cases)
6. [Memory Architecture for Simulated Agents](#memory-architecture-for-simulated-agents)
7. [Scaling and Distribution Strategies](#scaling-and-distribution-strategies)
8. [Bannou Actor Plugin Design Considerations](#bannou-actor-plugin-design-considerations)
9. [Key Resources](#key-resources)

---

## Executive Summary

This document explores distributed actor systems with a focus on their application to game simulations and NPC intelligence. The research covers:

- **Virtual Actor Model**: Pioneered by Microsoft Orleans, this pattern treats actors as perpetually-existing logical entities that are automatically activated on demand and garbage-collected when idle
- **Key Insight**: Virtual actors provide an ideal abstraction for NPC "brains" - they maintain state across deactivation cycles, respond to stimuli via message passing, and scale horizontally across cluster nodes
- **Accessor Pattern**: Similar to statestores, actors are addressed by type + ID; first access activates the actor on an available node, subsequent accesses route to the same instance

The core value proposition for our use case: **NPC cognitive processing units can be modeled as virtual actors that receive sensory events, build memories, and emit objectives - without the game client needing to know where the processing happens.**

---

## The Actor Model: Foundations

### Origins and Core Concepts

The actor model, developed by Carl Hewitt in 1973, treats **actors** as the universal primitives of concurrent computation. Each actor is:

- An isolated, independent unit of **compute** and **state**
- Capable of **single-threaded execution** only
- Communicating exclusively via **asynchronous message passing**

In response to a message, an actor can:
1. Make local decisions
2. Create more actors
3. Send more messages
4. Determine how to respond to the next message

> "Actors may modify their own private state, but can only affect each other through messages (avoiding the need for any locks)."

### Key Properties

| Property | Description |
|----------|-------------|
| **Encapsulation** | State, behavior, mailbox, children, and supervisor strategy behind a reference |
| **Location Transparency** | Actor addresses work the same locally or across network |
| **Fault Isolation** | Actor failures don't cascade; supervisors handle recovery |
| **Turn-Based Access** | Single-threaded execution eliminates synchronization complexity |

### Traditional Actor Limitations

Traditional actor systems (like early Akka) require:
- Explicit actor creation and destruction
- Manual lifecycle management
- Direct PID (Process ID) references
- Developer-managed state persistence

---

## Virtual Actors: The Evolution

### The Orleans Innovation

Microsoft Orleans, created by the Xbox Live team, introduced the **Virtual Actor** abstraction to solve the challenges of building Halo's cloud services for millions of concurrent players.

> "Orleans notably invented the Virtual Actor abstraction, where actors exist perpetually. An Orleans actor always exists, virtually - it cannot be explicitly created or destroyed."

### Virtual Actor Properties

1. **Perpetual Existence**: Actors are purely logical entities that always exist virtually
2. **Automatic Instantiation**: First message to an actor ID triggers activation on an available node
3. **Location Transparency**: Callers address actors by type + ID; framework handles routing
4. **Transparent Recovery**: Failures are invisible to callers; actors reactivate automatically
5. **Automatic Garbage Collection**: Unused actors are deactivated to free memory
6. **State Persistence**: Actor state outlives the in-memory object

### The Accessor Pattern

Virtual actors use an accessor pattern remarkably similar to statestores:

```
Statestore:  statestore.Get<PlayerData>("player-123")
Actor:       actorProxy.ForActor<IPlayerActor>("player-123")
```

**What happens on first access:**
1. Caller requests actor with ID "player-123"
2. Placement service determines target node (consistent hashing)
3. If not active, runtime creates new instance on target node
4. Actor's state is loaded from persistent storage
5. `OnActivateAsync()` lifecycle hook fires
6. Actor added to "Active Actors" table
7. Message is delivered

**Subsequent accesses:**
1. Caller requests actor with ID "player-123"
2. Cached routing information directs to active instance
3. Message delivered directly

### Lifecycle Management

```
┌─────────────┐     First      ┌─────────────┐
│  Not Active │ ──────────────▶│   Active    │
│  (Virtual)  │    Message     │ (In-Memory) │
└─────────────┘                └─────────────┘
       ▲                              │
       │                              │ Idle Timeout
       │      Garbage Collection      │ (default: 60m)
       └──────────────────────────────┘
```

**Deactivation Process:**
1. Actor idle for configurable period (default 60 minutes)
2. `OnDeactivateAsync()` lifecycle hook fires
3. Timers are cleared
4. Actor removed from Active Actors table
5. In-memory object garbage collected
6. **State remains in persistent storage**

**Reactivation Process:**
1. New message arrives for deactivated actor
2. New instance created (possibly on different node)
3. State restored from persistent storage
4. Actor resumes as if never deactivated

---

## Framework Comparison

### Overview of Major Frameworks

| Framework | Language | Virtual Actors | State Persistence | Performance |
|-----------|----------|----------------|-------------------|-------------|
| **Orleans** | .NET | Yes (Grains) | Built-in, pluggable | High |
| **Dapr Actors** | Multi-language | Yes | Via state stores | High |
| **Akka.NET** | .NET | Via Cluster Sharding | Akka.Persistence | Very High |
| **Proto.Actor** | .NET, Go, Java | Yes (Grains) | Pluggable | Ultra-High |
| **Service Fabric** | .NET | Yes | Reliable Collections | High |

### Orleans (Microsoft)

**Best For:** Cloud-native .NET applications, game backends

**Key Features:**
- Invented virtual actors (grains)
- Built-in clustering and grain directory
- Streams for event processing
- Distributed ACID transactions
- Used by Halo, Gears of War, Xbox, Skype, PlayFab

**Grain Definition:**
```csharp
public interface IPlayerGrain : IGrainWithStringKey
{
    Task<PlayerState> GetStateAsync();
    Task ProcessEventAsync(SensoryEvent evt);
}

public class PlayerGrain : Grain, IPlayerGrain
{
    private readonly IPersistentState<PlayerState> _state;

    public Task<PlayerState> GetStateAsync() => Task.FromResult(_state.State);

    public async Task ProcessEventAsync(SensoryEvent evt)
    {
        _state.State.ProcessEvent(evt);
        await _state.WriteStateAsync();
    }
}
```

**Resources:**
- [Orleans Overview (Microsoft Learn)](https://learn.microsoft.com/en-us/dotnet/orleans/overview)
- [Orleans Whitepaper (Microsoft Research)](https://www.microsoft.com/en-us/research/wp-content/uploads/2016/02/Orleans-MSR-TR-2014-41.pdf)
- [Creating Scalable Game Backends with Orleans](https://www.gamedeveloper.com/programming/creating-scalable-backends-for-games-using-open-source-orleans-framework)

### Dapr Actors

**Best For:** Polyglot microservices, Kubernetes-native deployments

**Key Features:**
- Language-agnostic HTTP/gRPC API
- Swappable state stores (Redis, CosmosDB, etc.)
- Reminders persist across deactivations
- Kubernetes-native placement service
- Sidecar architecture

**Actor Addressing:**
```
POST /v1.0/actors/{actorType}/{actorId}/method/{methodName}
```

**Turn-Based Concurrency:**
> "Turn-based access greatly simplifies concurrent systems as there is no need for synchronization mechanisms for data access."

**Resources:**
- [Dapr Actors Overview](https://docs.dapr.io/developing-applications/building-blocks/actors/actors-overview/)
- [Dapr Actor Runtime Features](https://docs.dapr.io/developing-applications/building-blocks/actors/actors-features-concepts/)
- [Understanding Dapr Actors for AI Agents (Diagrid)](https://www.diagrid.io/blog/understanding-dapr-actors-for-scalable-workflows-and-ai-agents)

### Proto.Actor

**Best For:** Ultra-high performance, cross-language clusters

**Key Features:**
- 2+ million messages/second between nodes
- gRPC with HTTP/2 streams
- Protobuf serialization
- Supports both traditional and virtual actors
- .NET, Go, Java/Kotlin support

**Performance Claim:**
> "Proto.Actor currently manages to pass over two million messages per second between nodes using only two actors... six times more than Akka's Artery transport, and 30 times faster than Akka.NET."

**Resources:**
- [Proto.Actor Official Site](https://proto.actor/)
- [Proto.Actor Documentation](https://asynkron.se/docs/protoactor/)
- [Proto.Actor .NET GitHub](https://github.com/asynkron/protoactor-dotnet)

### Akka.NET

**Best For:** Event sourcing, complex distributed systems

**Key Features:**
- Akka.Persistence for event-sourced actors
- Akka.Cluster.Sharding for virtual actor behavior
- Extremely mature ecosystem
- Tens of millions of messages per second in-memory

**Event Sourcing Pattern:**
> "Only changes to an actor's internal state are persisted, not the current state directly. These changes are only ever appended to storage, nothing is ever mutated."

**Resources:**
- [Akka.NET Documentation](https://getakka.net/)
- [Akka.NET Persistence](https://getakka.net/articles/persistence/architecture.html)
- [Akka.NET GitHub](https://github.com/akkadotnet/akka.net)

---

## Game Development Use Cases

### Halo Backend (Orleans Case Study)

The Halo 4 and Halo 5 cloud services demonstrate Orleans at scale:

**Architecture:**
- **Game Grains**: Aggregate statistics for game sessions
- **Player Grains**: Persist individual player statistics
- **Presence Service**: Track online status for millions of players

**Performance:**
- Linear scalability as servers increase
- 6.5ms median latency for heartbeat calls
- Thousands of simultaneous games
- Millions of concurrent player updates

**Key Pattern:**
> "The separation into game and player grains, combined with distributed cloud storage, provided a scalable foundation that could process thousands of simultaneous games and millions of concurrent player updates."

**Resources:**
- [About Halo's Backend (CleverHeap)](https://cleverheap.com/posts/about-halo-backend/)
- [How Halo Scaled to 10+ Million Players (ByteByteGo)](https://blog.bytebytego.com/p/how-halo-on-xbox-scaled-to-10-million)

### Actor Model in Game Engines

**Appropriate Use Cases:**
- Player state management
- Matchmaking and lobbies
- Inventory and progression
- Guild/clan systems
- Chat and social features
- NPC cognitive processing (our focus)

**Inappropriate Use Cases:**
- Real-time physics (60+ updates/second)
- Position synchronization
- Collision detection
- Frame-by-frame animation

> "High communication overhead from serialization/deserialization makes the architecture unsuitable for very high-frequency messages (e.g., updating character positions 60 times per second)."

**Hybrid Architecture:**
The actor model and ECS (Entity Component System) are complementary:
- **ECS**: High-frequency, data-oriented batch processing (physics, rendering)
- **Actors**: Low-frequency, behavior-oriented individual processing (AI, state)

**Resources:**
- [The Actor Model in Game Development](https://vhlam.com/article/the-actor-model-in-game-development)
- [Designing a Concurrent Game Engine with Actor Model (GameDev.net)](https://www.gamedev.net/forums/topic/677173-designing-a-concurrent-game-engine-with-actor-model/)

### EVE Online's Time Dilation

EVE Online handles massive fleet battles using **Time Dilation**:
> "When system load skyrockets, the server proactively slows down in-game time."

This demonstrates that even with optimal architecture, there are practical limits to real-time processing at scale.

---

## Memory Architecture for Simulated Agents

### Stanford Smallville: Generative Agents

Stanford's "Generative Agents" research (2023) created a town of 25 AI characters with believable human behavior. This is directly relevant to our NPC cognitive processing goals.

**Architecture Components:**

1. **Memory Stream**: Complete record of agent's experiences in natural language
2. **Retrieval System**: Dynamic memory access based on relevance, recency, importance
3. **Reflection**: Higher-level synthesis of memories over time
4. **Planning**: Behavior generation from memories and goals

**Emergent Behaviors:**
> "Starting with only a single user-specified notion that one agent wants to throw a Valentine's Day party, the agents autonomously spread invitations over the next two days, make new acquaintances, ask each other out on dates, and coordinate to show up together at the right time."

**Resources:**
- [Generative Agents Paper (arXiv)](https://arxiv.org/abs/2304.03442)
- [Generative Agents GitHub](https://github.com/joonspk-research/generative_agents)
- [Stanford HAI Announcement](https://hai.stanford.edu/news/computational-agents-exhibit-believable-humanlike-behavior)

### Memory Types for AI Agents

Following the CoALA (Cognitive Architectures for Language Agents) framework:

#### Short-Term Memory (STM)
- Recent inputs for immediate decision-making
- Rolling buffer or context window
- Limited capacity, frequently overwritten
- **Implementation**: In-memory queue, actor local state

#### Long-Term Memory (LTM)

**Semantic Memory** ("What"):
- Facts and knowledge about the world
- Persistent, queryable
- **Implementation**: Vector database, knowledge graph

**Episodic Memory** ("When" and "Where"):
- Specific past experiences
- Time-stamped events
- **Implementation**: Event log, timeline storage

**Procedural Memory** ("How"):
- Internalized rules and behaviors
- Action patterns
- **Implementation**: Behavior tree/GOAP configuration

**Resources:**
- [AI Agent Memory (IBM)](https://www.ibm.com/think/topics/ai-agent-memory)
- [Memory in AI Agents (AWS)](https://aws.amazon.com/blogs/machine-learning/building-smarter-ai-agents-agentcore-long-term-memory-deep-dive/)
- [Build Smarter AI Agents with Redis](https://redis.io/blog/build-smarter-ai-agents-manage-short-term-and-long-term-memory-with-redis/)

### Perception and Sensory Systems

Game engines implement perception systems that feed sensory data to AI:

**Unreal Engine AI Perception:**
- AIPerceptionComponent acts as stimuli listener
- AIPerceptionStimuliSourceComponent on observable actors
- Senses: Sight, Hearing, Damage, Touch, Team, Prediction
- Events fire when stimuli detected/lost

**Unity Sensory System:**
- Aspects (what can be sensed): Enemy, Ally, Resource
- Senses (how things are sensed): Sight, Sound, Touch
- Continuous polling or event-driven updates

**Key Insight for Our Design:**
> "An AI character system needs to be aware of its environment... The quality of an NPC's AI completely depends on the information it can get from the environment."

The **game client/engine** handles real-time perception. The **actor** processes those perceptions into:
- Memory formation
- Goal evaluation
- Objective assignment

**Resources:**
- [Unreal AI Perception Documentation](https://dev.epicgames.com/documentation/en-us/unreal-engine/ai-perception-in-unreal-engine)
- [AI for Unity: Emulating Senses (Packt)](https://www.packtpub.com/en-us/learning/how-to-tutorials/ai-unity-game-developers-emulate-real-world-senses)

---

## Scaling and Distribution Strategies

### Placement Strategies

**Orleans Placement Options:**
- **Random**: Distribute evenly across silos
- **Prefer Local**: Activate on calling silo if possible
- **Activate on Every Silo**: For stateless workers
- **Resource-Based**: Consider CPU/memory utilization
- **Custom Logic**: Application-specific placement

**Dapr Placement Service:**
- Consistent hashing for actor placement
- Automatic rebalancing on cluster changes
- Global placement table across sidecars

### Kubernetes Integration

**Horizontal Pod Autoscaling (HPA):**
- Scale pods based on CPU/memory metrics
- Can use custom metrics for actor count

**KEDA (Event-Driven Autoscaling):**
- Scale to zero when no events
- 70+ built-in scalers (queues, databases, custom)
- Ideal for actor workloads

**Agones (Game Server Orchestrator):**
- Purpose-built for game servers
- Fleet autoscaling based on player demand
- "Packed" scheduling for cost optimization

**Resources:**
- [KEDA (Kubernetes Event-Driven Autoscaling)](https://keda.sh/)
- [Agones Scheduling and Autoscaling](https://agones.dev/site/docs/advanced/scheduling-and-autoscaling/)
- [Google Cloud: Auto-scale Game Backends](https://cloud.google.com/blog/products/containers-kubernetes/auto-scale-your-game-back-end-to-save-time-and-money)

### Our Scaling Model

Given Bannou's orchestrator plugin and container management capabilities:

```
┌─────────────────────────────────────────────────────────────┐
│                    Orchestrator Plugin                       │
│  - Monitors actor node capacity                              │
│  - Triggers new node deployment when threshold reached       │
│  - Routes actor requests via consistent hashing              │
└─────────────────────────────────────────────────────────────┘
                              │
         ┌────────────────────┼────────────────────┐
         ▼                    ▼                    ▼
┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐
│  Actor Node 1   │  │  Actor Node 2   │  │  Actor Node N   │
│  (1000 actors)  │  │  (1000 actors)  │  │  (1000 actors)  │
└─────────────────┘  └─────────────────┘  └─────────────────┘
```

**Capacity-Based Scaling:**
- Define max actors per node (e.g., 1000)
- Orchestrator monitors active actor count
- New nodes provisioned as needed (similar to asset processing)
- Actor placement uses consistent hashing for locality

---

## Bannou Actor Plugin Design Considerations

### Actor as NPC Cognitive Processor

Based on our research, here's how the actor model maps to our NPC simulation needs:

```
┌─────────────────────────────────────────────────────────────┐
│                    Game Client/Engine                        │
│  ┌─────────────────┐  ┌─────────────────┐                   │
│  │ Perception Sys  │  │ Movement Sys    │                   │
│  │ (sight/sound)   │  │ (pathfinding)   │                   │
│  └────────┬────────┘  └────────▲────────┘                   │
│           │ Events             │ Objectives                  │
└───────────┼────────────────────┼────────────────────────────┘
            │                    │
            ▼                    │
┌───────────────────────────────────────────────────────────┐
│                    Actor Plugin (Bannou)                   │
│  ┌───────────────────────────────────────────────────────┐│
│  │                  NPC Actor (per character)            ││
│  │  ┌─────────────┐  ┌─────────────┐  ┌───────────────┐ ││
│  │  │ Short-Term  │  │ Long-Term   │  │  Goal/GOAP    │ ││
│  │  │   Memory    │  │   Memory    │  │   Planner     │ ││
│  │  │ (recent     │  │ (semantic,  │  │ (objectives,  │ ││
│  │  │  events)    │  │  episodic)  │  │  priorities)  │ ││
│  │  └─────────────┘  └─────────────┘  └───────────────┘ ││
│  └───────────────────────────────────────────────────────┘│
└───────────────────────────────────────────────────────────┘
```

### Key Design Principles

1. **Actor = Character Brain**: One actor per NPC, identified by character ID
2. **Event-Driven Input**: Perception events flow from game to actor via messaging
3. **Objective-Based Output**: Actor emits objectives, not direct movement commands
4. **State Persistence**: Memory persists across actor deactivation
5. **Turn-Based Processing**: Single-threaded execution simplifies cognitive modeling

### Actor API Sketch

```csharp
public interface INpcActor : IActorWithStringKey
{
    // Sensory input
    Task ProcessPerceptionAsync(PerceptionEvent evt);
    Task ProcessAudioAsync(AudioEvent evt);

    // Memory queries
    Task<IEnumerable<Memory>> RecallAsync(string query, int limit);
    Task<CharacterKnowledge> GetKnowledgeAsync();

    // Objective management
    Task<IEnumerable<Objective>> GetActiveObjectivesAsync();
    Task SetPriorityAsync(string objectiveId, int priority);

    // State
    Task<NpcState> GetStateAsync();
}
```

### Comparison with Asset Processing Pattern

| Aspect | Asset Plugin | Actor Plugin |
|--------|--------------|--------------|
| **Unit of Work** | Asset (texture, audio) | NPC Character |
| **Input** | Raw file bytes | Perception events |
| **Output** | Processed asset | Objectives/decisions |
| **State** | Stateless processing | Stateful (memory) |
| **Lifecycle** | Process and terminate | Long-running with idle GC |
| **Scaling** | Job-based | Capacity-based |

### Message Types

**Inbound (Game → Actor):**
- `PerceptionEvent`: Visual stimulus (entity seen/lost)
- `AudioEvent`: Sound detected (speech, noise)
- `InteractionEvent`: Physical interaction
- `TimeEvent`: Time passage, schedule triggers
- `EnvironmentEvent`: Weather, lighting changes

**Outbound (Actor → Game):**
- `ObjectiveAssigned`: New goal for character
- `ObjectiveCancelled`: Goal abandoned
- `ObjectivePriorityChanged`: Re-prioritization
- `EmotionalStateChanged`: Mood shift
- `SocialIntentionEvent`: Desire to interact with other NPC

---

## Key Resources

### Foundational Papers

- [Orleans: Distributed Virtual Actors for Programmability and Scalability (Microsoft Research)](https://www.microsoft.com/en-us/research/wp-content/uploads/2016/02/Orleans-MSR-TR-2014-41.pdf)
- [Generative Agents: Interactive Simulacra of Human Behavior (Stanford)](https://arxiv.org/abs/2304.03442)
- [Building the AI of F.E.A.R. with GOAP (Orkin)](https://www.gamedeveloper.com/design/building-the-ai-of-f-e-a-r-with-goal-oriented-action-planning)

### Framework Documentation

- [Microsoft Orleans Overview](https://learn.microsoft.com/en-us/dotnet/orleans/overview)
- [Dapr Actors Documentation](https://docs.dapr.io/developing-applications/building-blocks/actors/)
- [Proto.Actor](https://proto.actor/)
- [Akka.NET](https://getakka.net/)

### Game Development

- [Creating Scalable Game Backends with Orleans](https://www.gamedeveloper.com/programming/creating-scalable-backends-for-games-using-open-source-orleans-framework)
- [The Actor Model in Game Development](https://vhlam.com/article/the-actor-model-in-game-development)
- [Unreal AI Perception System](https://dev.epicgames.com/documentation/en-us/unreal-engine/ai-perception-in-unreal-engine)

### AI Agent Architecture

- [AI Agent Memory (IBM)](https://www.ibm.com/think/topics/ai-agent-memory)
- [AWS AgentCore Long-Term Memory](https://aws.amazon.com/blogs/machine-learning/building-smarter-ai-agents-agentcore-long-term-memory-deep-dive/)
- [GOAP Theory](https://goap.crashkonijn.com/readme/theory)

### Scaling and Infrastructure

- [KEDA (Kubernetes Event-Driven Autoscaling)](https://keda.sh/)
- [Agones Game Server Orchestration](https://agones.dev/)
- [Orleans Redis Persistence](https://github.com/OrleansContrib/Orleans.Redis)

---

## Next Steps

1. **Design Actor Plugin Schema**: Define OpenAPI specification for actor management endpoints
2. **Implement Actor Placement Service**: Consistent hashing with capacity-based scaling
3. **Define State Store Integration**: Leverage lib-state for actor persistence
4. **Create Memory Subsystem**: Short-term buffer + long-term vector storage
5. **Integrate with Orchestrator**: Capacity monitoring and node provisioning
6. **Define Event Contracts**: Perception events and objective messages

---

*Document compiled: December 2024*
*For Bannou Service Actor Plugin Development*
