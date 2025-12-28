# Actors Plugin Design - Issues and Resolutions

> **Created**: 2024-12-28
> **Updated**: 2024-12-28
> **Status**: RESOLVED - Ready for Redesign
> **Related**: [ACTORS_PLUGIN.md](./UPCOMING_-_ACTORS_PLUGIN.md), [DISTRIBUTED-ACTORS.md](../research/DISTRIBUTED-ACTORS.md)

This document captures issues identified during design review and their resolutions.

---

## Critical Reframing: Actors Are General-Purpose

### The Problem with Original Design

The original ACTORS_PLUGIN.md was **too NPC-specific**. It baked in:
- Memory systems (semantic, episodic, procedural)
- Perception processing and attention
- Objective generation and GOAP planning
- Emotional/personality models

This conflates the **Actor infrastructure** with the **NPC brain use case**.

### The Corrected Understanding

**Actors are general-purpose distributed processing units** that can be used for:

| Use Case | Actor Role |
|----------|------------|
| **NPC Brains** | Cognitive processor that receives perception, emits objectives |
| **CRON Jobs** | Scheduled task that runs exactly once across the network |
| **Chat Agents** | AI assistant with conversation state and tool access |
| **Game Session Coordinator** | Manages game state for one match/session |
| **Workflow Orchestrator** | Multi-step process with checkpoints |

**What ALL actors share:**
- Addressable by type + ID
- Guaranteed single-instance (only one active per ID across all nodes)
- Access to infrastructure libs (lib-state, lib-messaging, lib-mesh)
- Turn-based message processing (inbox/mailbox)
- Lifecycle hooks (activate/deactivate)
- State persistence
- Personal event channel (like client events)

**What is use-case specific (NOT in Actor plugin):**
- Memory consolidation → Behavior plugin
- Perception interpretation → Behavior plugin
- GOAP planning → Behavior plugin
- Personality/emotions → Behavior plugin
- Specific state shapes → Each actor type defines its own

---

## Resolved Questions

### ✅ Question 1: Why Actor Migration? → NOT NEEDED

**Resolution**: Migration is over-engineering.

From arcadia-kb:
> "Agent is a long-running microservice... State is persistent (survives restarts)"

Pattern:
1. Actor deactivates → state persisted to lib-state
2. Next message activates actor on any available node
3. State loads from lib-state

**No live migration protocol needed.** The deactivate/reactivate pattern is sufficient.

**Action**: Remove from design:
- `actor.migration.request` / `actor.migration.complete` topics
- All migration-related code
- Node-to-node state transfer logic

---

### ✅ Question 4: Attention System Scope → BEHAVIOR PLUGIN

**Resolution**: Confirmed via arcadia-kb review.

**Plugin Boundary:**

| Actor Plugin (Infrastructure) | Behavior Plugin (Logic) |
|-------------------------------|-------------------------|
| Actor lifecycle management | ABML YAML parsing/compilation |
| State persistence (hot/cold) | Behavior stack merging |
| Event routing | Context variable interpolation |
| Node placement/scaling | GOAP planning & action sequencing |
| Personal event channels | Perception interpretation |
| Service invocation access | Memory significance & consolidation |
| Turn-based processing | Attention allocation |

**Actor plugin routes events. Behavior plugin interprets them.**

---

### ✅ Question 5: Scaling Thresholds → SIZED CORRECTLY

**Resolution**: Plan for 50% active as normal, 100% as peak.

- Rate limits are safety valves, not management tools
- Buffer nodes handle peak load
- No "thundering herd" panic design needed

---

### ✅ Question 6: Plugin Boundaries → CLEAR

**Resolution**: arcadia-kb documents Avatar + Agent architecture.

- **Avatar** (game-side): Sensory collection, no interpretation
- **Agent** (Actor plugin): Lifecycle, state, routing, scaling
- **Behavior** (Behavior plugin): ABML, GOAP, memory, cognition

Actor plugin is **infrastructure for agents**. Behavior plugin is **logic for NPC agents**.

---

## Remaining Technical Issues

### Issue 1: x-permissions Format Mismatch

**Status**: Must fix before schema generation.

**Problem**:
```yaml
# WRONG
x-permissions:
  - actor:activate
```

**Required**:
```yaml
# CORRECT (per Tenet 13)
x-permissions:
  - role: developer
```

---

### Issue 2: lib-state Atomic Conditional Save

**Status**: Need to verify lib-state capability.

**Requirement**: Actor activation needs atomic "set-if-not-exists" to prevent race conditions.

```csharp
// Required capability
var result = await _stateStore.TrySaveIfNotExistsAsync(
    $"actor:active:{actorId}",
    new ActorActivation { NodeId = _nodeId },
    new StateOptions { Ttl = TimeSpan.FromSeconds(90) }
);
```

**Action**: Check if lib-state supports this. If not, add enhancement.

---

### Issue 3: Direct Redis/MySQL References

**Status**: Design must be rewritten to use lib-state only.

All examples in original design using direct Redis operations violate Tenet 4.

---

## Items Removed from Actor Plugin Scope

These were in the original design but belong elsewhere:

| Item | Now Belongs In |
|------|----------------|
| `ActorHotState.RecentPerceptions` | Actor-type-specific state |
| `ActorColdState.Knowledge/Experiences` | Behavior plugin + actor-type state |
| `MemoryRetriever` | Behavior plugin |
| `PerceptionEvent` processing logic | Behavior plugin |
| `ObjectiveEvent` generation | Behavior plugin |
| `EmotionalState` | NPC actor type definition |
| `PersonalityProfile` | NPC actor type definition |
| `Relationship` | NPC actor type definition |
| `AttentionSystem` | Behavior plugin |

---

## New Design Requirements

### What Actor Plugin MUST Provide

1. **Actor Registration** - Define actor types with their message handlers
2. **Actor Addressing** - Locate/invoke actor by type + ID
3. **Single-Instance Guarantee** - Only one active per ID across all nodes
4. **Turn-Based Processing** - Mailbox pattern, sequential message handling
5. **Lifecycle Hooks** - OnActivate, OnDeactivate, OnMessage
6. **State Persistence** - Hot (Redis TTL) and cold (MySQL) via lib-state
7. **Event Subscription** - Subscribe to shared exchanges via lib-messaging
8. **Personal Channel** - Per-actor event channel (like client events)
9. **Service Access** - Call any internal API via lib-mesh
10. **Placement/Scaling** - Consistent hashing, orchestrator integration

### What Actor Plugin MUST NOT Hardcode

- Specific state shapes (memory, perceptions, objectives)
- Event interpretation logic
- Goal/action planning
- Any NPC-specific concepts

---

## Next Steps

1. [x] Review arcadia-kb for Behavior plugin documentation
2. [x] Resolve migration question → NOT NEEDED
3. [x] Check lib-state for atomic conditional save support
4. [ ] Rewrite ACTORS_PLUGIN.md as general-purpose design
5. [ ] Define actor type registration pattern
6. [ ] Show NPC actor as ONE EXAMPLE, not the core design

---

## Reference: arcadia-kb Key Documents

- `/home/lysander/repos/arcadia-kb/05 - NPC AI Design/Character-Behavior-Systems-Architecture.md`
- `/home/lysander/repos/arcadia-kb/05 - NPC AI Design/Distributed Agent Architecture.md`
- `/home/lysander/repos/arcadia-kb/06 - Technical Architecture/NPC-Behavior-ABML-Specification.md`
- `/home/lysander/repos/arcadia-kb/06 - Technical Architecture/NPC-Behavior-GOAP-Planning-Integration.md`

---

*Document updated after arcadia-kb review and architectural reframing.*
