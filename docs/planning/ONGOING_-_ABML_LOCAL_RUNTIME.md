# ABML Local Runtime & Behavior Compilation

> **Status**: PARTIAL IMPLEMENTATION
> **Updated**: 2026-01-08
> **Related**: [ABML Guide](../guides/ABML.md), [BEHAVIOR_PLUGIN_V2.md](./ONGOING_-_BEHAVIOR_PLUGIN_V2.md)

## Implementation Status

| Phase | Status | Description |
|-------|--------|-------------|
| **1. Bytecode Foundation** | COMPLETE | `BehaviorOpcode` (40+ opcodes), `BehaviorModelInterpreter` (509 lines), binary format |
| **2. Full Compilation** | COMPLETE | `BehaviorCompiler`, `SemanticAnalyzer`, `BytecodeEmitter`, `BytecodeOptimizer` |
| **3. Distribution** | PENDING | Model sync service, lib-state storage, delta sync |
| **4. Client Integration** | PARTIAL | `BehaviorModelCache` exists; `BehaviorEvaluationSystem`, variant selection pending |
| **5. Intent Channels** | COMPLETE | `IntentChannels`, `IntentMerger`, `ContributionTrace` in sdk-sources/ |
| **6. Agent Integration** | PENDING | Cloud agents use models, Event Brain integration |
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

### Phase 3: Distribution (PENDING)

- [ ] Model storage in lib-state (Redis) with version tracking
- [ ] `/behavior/models/sync` endpoint for client sync
- [ ] Delta sync for efficient updates
- [ ] Character-specific model assignment

### Phase 4: Client Integration (PARTIAL)

- [ ] `BehaviorEvaluationSystem` - Stride ECS system for per-frame evaluation
- [ ] Variant selection logic (equipment-based)
- [ ] Fallback chain when specialized variant unavailable

### Phase 6: Agent Integration (PENDING)

- [ ] `/behavior/agent/query-combat-options` - Cloud agents query compiled models
- [ ] Event Brain integration for cinematic combat

### Phase 7: Training System (PENDING)

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

- **ABML Language**: [docs/guides/ABML.md](../guides/ABML.md)
- **Implemented code**: `lib-behavior/Compiler/`, `sdk-sources/Behavior/`
- **Bytecode opcodes**: `BehaviorOpcode.cs` in lib-behavior

---

*Original ~2870-line document condensed 2026-01-08. Implementation details now in codebase.*
