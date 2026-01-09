# ABML Local Runtime & Behavior Compilation

> **Status**: MOSTLY COMPLETE - Client integration remaining
> **Updated**: 2026-01-08
> **Related**: [ABML Guide](../guides/ABML.md), [Actor System](../guides/ACTOR_SYSTEM.md)

## Implementation Status

| Phase | Status | Description |
|-------|--------|-------------|
| **1. Bytecode Foundation** | COMPLETE | `BehaviorOpcode` (40+ opcodes), `BehaviorModelInterpreter` (509 lines), binary format |
| **2. Full Compilation** | COMPLETE | `BehaviorCompiler`, `SemanticAnalyzer`, `BytecodeEmitter`, `BytecodeOptimizer` |
| **3. Distribution** | COMPLETE | Uses Asset Service pattern - presigned URLs, state store metadata |
| **4. Client Integration** | PARTIAL | `BehaviorDocumentCache` in lib-actor; `BehaviorEvaluationSystem` pending |
| **5. Intent Channels** | COMPLETE | `IntentChannels`, `IntentMerger`, `ContributionTrace` in sdk-sources/ |
| **6. Agent Integration** | COMPLETE | Actors use `ICognitionPipeline.ProcessAsync()`, Event Brain via cognition stages |
| **7. Training System** | PENDING | Proficiency tracking, model tier unlocking, trainer NPCs |
| **8. Polish** | PENDING | Benchmarks, memory pooling, hot reload, debug tooling |

---

## 1. Overview

### The Problem

ABML's tree-walking interpreter (`DocumentExecutor`) has ~50-200ms latency - unsuitable for frame-by-frame combat decisions where characters have ~16ms before the next frame.

### The Solution

**Compile ABML to distributable behavior models** that execute locally on game clients.

```
AUTHORING TIME:
  Designer writes ABML -> Behavior Plugin compiles -> Behavior Model (bytecode)

RUNTIME (Game Client):
  Current State -> Local Runtime evaluates -> Action Intent
  (stamina=50,      Behavior Model          (heavy_attack,
   enemy_dist=2.0,                           target=enemy_1)
   combo_state=1)
```

### Execution Layer Model

| Layer | Location | Latency | Use Case |
|-------|----------|---------|----------|
| Event Brain | Cloud | 100-500ms | Cinematic orchestration, QTE, dramatic pacing |
| Character Agent | Cloud | 50-200ms | Tactical decisions, personality, memory |
| **Local Runtime** | Client | <1ms | Frame-by-frame combat, action selection |
| Game Engine | Client | Per-frame | Animation state machines, physics |

---

## 2. SDK Architecture

**Critical**: The client-side runtime has **zero dependency on ABML source code**. The bytecode format is completely self-contained.

```
SERVER (lib-behavior)              CLIENT (Bannou.SDK)
─────────────────────              ────────────────────

ABML YAML (source)
    │
    ▼
DocumentParser
    │
    ▼
BehaviorCompiler
    │
    ▼
BehaviorModel ═══Binary═══════════▶ BehaviorModel
   (bytes)        Transfer             (bytes)
                                          │
    ║                                     ▼
    ║                              BehaviorModelInterpreter
NO DEPENDENCY                       (bytecode VM)
    ║                                     │
    ╚═══════════════════════════════════▶ IntentChannels
```

**SDK knows NOTHING about**: YAML syntax, `DocumentParser`, `Flow`, `ActionNode`, tree-walking `DocumentExecutor`

**SDK ONLY knows**: Binary format, opcodes, how to execute them

### SDK Package Structure

```
Bannou.SDK/Behavior/
├── Runtime/
│   ├── BehaviorModel.cs              # Binary format deserialization
│   ├── BehaviorModelInterpreter.cs   # Stack-based bytecode VM
│   └── StateSchema.cs                # Input/output schema handling
├── Intent/
│   ├── IntentChannels.cs             # Intent channel management
│   ├── IntentMerger.cs               # Multi-model conflict resolution
│   └── ContributionTrace.cs          # Debug attribution
└── Cache/
    └── BehaviorModelCache.cs         # Hot-swap model management
```

---

## 3. Remaining Work

### Phase 3: Distribution (COMPLETE)

Behaviors follow the **Asset Service pattern** - identical to how game assets are stored and distributed:

```
Compile ABML
    │
    ├─→ Store bytecode in Asset Service (presigned upload to MinIO)
    ├─→ Store metadata in State Store (Redis: behavior-metadata:{id})
    └─→ Publish behavior.created / behavior.updated events

Retrieval:
    GET /cache/get { behaviorId } → presigned download URL or inline bytecode
```

**Key files**: `BehaviorService.cs` (lines 414-471), `BehaviorBundleManager.cs`

**Completed features**:
- [x] Bytecode stored as `.bbm` assets via Asset Service presigned URLs
- [x] Metadata indexed in state store with version tracking
- [x] `/cache/get` endpoint returns download URL or inline bytecode
- [x] Bundle grouping via `bundleId` parameter
- [x] Lifecycle events published for downstream consumers

### Phase 4: Client Integration (PARTIAL)

**Completed**:
- [x] `BehaviorDocumentCache` in lib-actor - caches parsed YAML documents
- [x] `RemoteAssetCache<T>` in SDK - generic asset caching with CRC verification

**Remaining**:
- [ ] `BehaviorEvaluationSystem` - Stride ECS system for per-frame bytecode evaluation
- [ ] Variant selection logic (equipment-based model switching)
- [ ] Fallback chain when specialized variant unavailable

### Phase 6: Agent Integration (COMPLETE)

Cloud agents use the cognition pipeline which incorporates compiled behavior models:
- [x] `ICognitionPipeline.ProcessAsync()` - 5-stage cognition for actors
- [x] Cognition templates define stage handlers (humanoid_base, creature_base, object_base)
- [x] Event Brain orchestration via cognition intention stage

### Phase 7: Training System (PENDING - Game Feature)

- [ ] Proficiency tracking per character/weapon-type
- [ ] Model tier unlocking based on proficiency
- [ ] Trainer NPCs that improve proficiency faster

### Phase 8: Polish (PENDING)

- [ ] Benchmark suite for evaluation performance
- [ ] Memory pooling for hot paths
- [ ] Hot reload in development
- [ ] Debug tooling (bytecode trace, breakpoints)

---

## 4. Design Decisions

| Question | Decision | Rationale |
|----------|----------|-----------|
| Bytecode vs Native | Bytecode | Simpler, portable, sufficient performance |
| Model Granularity | Type/variant hierarchy | Combat/movement/interaction with equipment-driven variants |
| State Schema | Dynamic per model | Different styles need different inputs |
| Randomness | RAND opcode with seed | Client provides frame-based seed for determinism |

---

## 5. Success Criteria

### Technical

| Metric | Target |
|--------|--------|
| Model evaluation time | < 0.5ms per character |
| Model binary size | < 10KB typical, < 50KB complex |
| Sync latency | < 100ms including network |
| Update propagation | < 500ms from change to client |
| Memory per model | < 20KB |

### Experience

- AI characters feel responsive (no "thinking" pauses)
- Seamless transition between basic and cinematic combat
- Equipment changes result in immediate fighting style changes
- Training progression feels meaningful

---

## Reference

**Guides**:
- [ABML Language](../guides/ABML.md) - YAML syntax, nodes, expressions
- [Actor System](../guides/ACTOR_SYSTEM.md) - Cognition pipeline, templates, actor integration

**Key Implementation Files**:
- `lib-behavior/BehaviorService.cs` - Main service, compilation, asset storage
- `lib-behavior/BehaviorBundleManager.cs` - Bundle grouping and management
- `lib-behavior/Compiler/` - YAML→bytecode compilation pipeline
- `lib-actor/Caching/BehaviorDocumentCache.cs` - YAML document caching
- `sdk-sources/Behavior/` - Client-side runtime, intent channels
- `Bannou.Client.SDK/Cache/RemoteAssetCache.cs` - Generic asset caching

**Bytecode**:
- `lib-behavior/Compiler/BehaviorOpcode.cs` - All opcodes
- `lib-behavior/Compiler/BehaviorModelInterpreter.cs` - Stack-based VM

---

*Condensed 2026-01-08. Distribution confirmed complete via Asset Service pattern.*
