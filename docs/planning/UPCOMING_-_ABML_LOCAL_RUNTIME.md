# ABML Local Runtime & Behavior Compilation

> **Status**: PLANNING DOCUMENT
> **Created**: 2025-12-29
> **Related**: [THE_DREAM.md](./THE_DREAM.md), [ABML_V2_DESIGN_PROPOSAL.md](./ABML_V2_DESIGN_PROPOSAL.md), [UPCOMING_-_BEHAVIOR_PLUGIN_V2.md](./UPCOMING_-_BEHAVIOR_PLUGIN_V2.md)

---

## 1. Overview

### 1.1 The Problem

ABML is designed as a behavior authoring language, but the current tree-walking interpreter (`DocumentExecutor`) has latency characteristics unsuitable for frame-by-frame combat decisions. When a character needs to decide "block or dodge?" they have ~16ms before the next frame - not 50-200ms for a cloud round-trip.

Yet we want ONE language for all behavior authoring - from "swing sword when enemy is close" to "orchestrate dramatic cinematic exchange." Two different systems means two different mental models, two different toolchains, and a jarring seam between basic and cinematic combat.

### 1.2 The Solution

**Compile ABML to distributable behavior models** that execute locally on game clients.

The compiled model is essentially: "Here is the complete decision tree of all actions this character would take given various conditions." The client fills in current state values (stamina, enemy distance, combo state, etc.) and evaluates the tree to get the next action intent.

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        AUTHORING TIME                                   │
│                                                                         │
│  Designer writes ABML ──► Behavior Plugin compiles ──► Behavior Model   │
│                                                         (bytecode)      │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                            Distribution
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                         RUNTIME (Game Client)                           │
│                                                                         │
│  Current State ──► Local Runtime evaluates ──► Action Intent            │
│  (stamina=50,       Behavior Model              (heavy_attack,          │
│   enemy_dist=2.0,                                target=enemy_1)        │
│   combo_state=1)                                                        │
└─────────────────────────────────────────────────────────────────────────┘
```

### 1.3 Key Principles

1. **ABML remains the source of truth** - Designers author in YAML, never touch bytecode
2. **Compilation is deterministic** - Same ABML always produces same model
3. **Models are portable** - Run on any client regardless of platform
4. **Execution is fast** - Sub-millisecond evaluation for combat decisions
5. **State is external** - Model doesn't store state, client provides it each evaluation
6. **Updates are atomic** - New model replaces old completely, no partial updates

---

## 2. Architecture

### 2.1 Layer Model

```
┌─────────────────────────────────────────────────────────────────────────┐
│  LAYER 4: EVENT BRAIN (Cloud, 100-500ms)                                │
│  ─────────────────────────────────────────                              │
│  Full ABML interpretation with dynamic generation                       │
│  Cinematic orchestration, QTE presentation, dramatic pacing             │
│  Uses: DocumentExecutor (tree-walking interpreter)                      │
├─────────────────────────────────────────────────────────────────────────┤
│  LAYER 3: CHARACTER AGENT (Cloud, 50-200ms)                             │
│  ─────────────────────────────────────────                              │
│  Full ABML interpretation for tactical decisions                        │
│  Personality, emotions, memory-informed choices                         │
│  Uses: DocumentExecutor + full expression evaluation                    │
├─────────────────────────────────────────────────────────────────────────┤
│  LAYER 2: LOCAL BEHAVIOR RUNTIME (Client, <1ms)                         │
│  ─────────────────────────────────────────────                          │
│  Compiled behavior model execution                                      │
│  Combat decisions, action selection, combo management                   │
│  Uses: BehaviorModelInterpreter (bytecode VM)                           │
├─────────────────────────────────────────────────────────────────────────┤
│  LAYER 1: GAME ENGINE (Client, per-frame)                               │
│  ─────────────────────────────────────────                              │
│  Animation state machines, physics, collision                           │
│  Responds to action intents from Layer 2                                │
│  Uses: Native Stride systems                                            │
└─────────────────────────────────────────────────────────────────────────┘
```

### 2.2 Component Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         BEHAVIOR PLUGIN (Bannou)                        │
│                                                                         │
│  ┌──────────────┐   ┌──────────────┐   ┌──────────────┐                │
│  │    Parser    │──►│   Compiler   │──►│ Distribution │                │
│  │   (YAML →    │   │  (AST →      │   │   Service    │                │
│  │    AST)      │   │   Bytecode)  │   │              │                │
│  └──────────────┘   └──────────────┘   └──────┬───────┘                │
│         │                                      │                        │
│         │ Validation                           │ Push/Pull              │
│         ▼                                      ▼                        │
│  ┌──────────────┐                     ┌──────────────┐                 │
│  │    Schema    │                     │   Model      │                 │
│  │   Registry   │                     │   Storage    │                 │
│  └──────────────┘                     │  (lib-state) │                 │
│                                       └──────────────┘                 │
└─────────────────────────────────────────────────────────────────────────┘
                                              │
                              WebSocket / HTTP │
                                              ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                          GAME CLIENT (Stride)                           │
│                                                                         │
│  ┌──────────────┐   ┌──────────────┐   ┌──────────────┐                │
│  │   Model      │──►│   Runtime    │──►│    Intent    │                │
│  │   Cache      │   │ Interpreter  │   │    System    │                │
│  └──────────────┘   └──────────────┘   └──────┬───────┘                │
│                            ▲                   │                        │
│                            │                   ▼                        │
│                     ┌──────────────┐   ┌──────────────┐                │
│                     │    State     │   │  Animation   │                │
│                     │   Provider   │   │   System     │                │
│                     └──────────────┘   └──────────────┘                │
└─────────────────────────────────────────────────────────────────────────┘
```

### 2.3 SDK Architecture & Compilation Boundary

A critical architectural decision: the **client-side runtime has zero dependency on ABML source code or the bannou-service ABML libraries**. The bytecode format is completely self-contained.

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     COMPILATION VS RUNTIME BOUNDARY                     │
│                                                                         │
│  SERVER (lib-behavior + bannou-service)         CLIENT (Bannou.SDK)     │
│  ─────────────────────────────────────         ────────────────────     │
│                                                                         │
│  ┌──────────────┐                                                       │
│  │  ABML YAML   │                                                       │
│  │  (source)    │                                                       │
│  └──────┬───────┘                                                       │
│         │                                                               │
│         ▼                                                               │
│  ┌──────────────┐                                                       │
│  │DocumentParser│  ← bannou-service/Abml/                               │
│  │ AbmlDocument │    (parser, AST, expressions)                         │
│  └──────┬───────┘                                                       │
│         │                                                               │
│         ▼                                                               │
│  ┌──────────────┐                                                       │
│  │   Behavior   │  ← lib-behavior (NEW)                                 │
│  │   Compiler   │    (semantic analysis, optimization, codegen)         │
│  └──────┬───────┘                                                       │
│         │                                                               │
│         ▼                                                               │
│  ┌──────────────┐         Binary            ┌──────────────┐           │
│  │BehaviorModel │ ════════════════════════► │BehaviorModel │           │
│  │   (bytes)    │         Transfer          │   (bytes)    │           │
│  └──────────────┘                           └──────┬───────┘           │
│                                                    │                    │
│         ║                                          ▼                    │
│         ║                                   ┌──────────────┐           │
│    NO DEPENDENCY                            │  Interpreter │           │
│         ║                                   │ (bytecode VM)│           │
│         ╚══════════════════════════════════►└──────────────┘           │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

**SDK knows NOTHING about:**
- ABML YAML syntax or structure
- `DocumentParser`, `AbmlDocument`, `Flow`, `ActionNode`
- `ExpressionEvaluator`, `ExpressionCompiler`
- Tree-walking `DocumentExecutor`
- Any bannou-service/Abml/* code

**SDK ONLY knows:**
- Binary format (header, schema, constants, strings, bytecode)
- Opcode definitions (PUSH_INPUT, ADD, JMP_IF, SET_OUTPUT, etc.)
- How to execute those opcodes

This is analogous to how the JVM doesn't need the Java compiler, or .NET runtime doesn't need the C# compiler. The bytecode is the contract.

#### SDK Package Structure

```
Bannou.SDK/
├── Behavior/
│   ├── Runtime/
│   │   ├── BehaviorModel.cs              # Binary format deserialization
│   │   ├── BehaviorModelInterpreter.cs   # Stack-based bytecode VM
│   │   ├── StateSchema.cs                # Input/output schema handling
│   │   ├── Opcodes.cs                    # Instruction definitions
│   │   └── BehaviorOutput.cs             # Structured output from evaluation
│   ├── Intent/
│   │   ├── IntentMerger.cs               # Urgency-based channel merging
│   │   ├── MergedIntent.cs               # Unified output structure
│   │   ├── IntentChannels.cs             # Channel definitions
│   │   └── ContributionTrace.cs          # Debug support (DEBUG builds)
│   └── BehaviorModelCache.cs             # Model caching and variant lookup
```

#### Component Placement Decisions

| Component | Location | Rationale |
|-----------|----------|-----------|
| `BehaviorCompiler` | lib-behavior | Compilation is server-side only |
| `BehaviorDistributionService` | lib-behavior | Manages storage, sync, versioning |
| `BehaviorModel` | Bannou.SDK | Clients need to deserialize models |
| `BehaviorModelInterpreter` | Bannou.SDK | Portable, game-agnostic VM |
| `StateSchema` | Bannou.SDK | Shared format understanding |
| `IntentMerger` | Bannou.SDK | Generic urgency logic, no game specifics |
| `BehaviorModelCache` | Bannou.SDK* | Generic enough for SDK (see note) |
| `BehaviorEvaluationSystem` | Game Client | Stride-specific game system |
| `IntentSystem` | Game Client | Maps intents → animations/physics |
| `IVariantSelector` impls | Game Client | Game-specific equipment logic |
| State providers | Game Client | Game-specific state mapping |

**\*Note on `BehaviorModelCache`**: This component is generic enough to live in the SDK - it just manages model storage, interpreter instances, and variant lookup. However, games with specific caching needs (memory budgets, streaming priorities, LOD-based model selection) may choose to implement their own. The SDK version serves as a solid default and reference implementation.

#### Benefits of This Separation

1. **SDK stays lightweight** - No parser, no AST, no compiler overhead
2. **Fast client startup** - Just deserialize bytes and execute
3. **No accidental coupling** - Can't accidentally reference server-side ABML types
4. **Independent evolution** - Server compiler can improve without SDK updates (bytecode format is versioned)
5. **Multiple compilers possible** - Could have optimized compilers for different use cases
6. **Offline capable** - Client can run cached models without server connection

#### Format as Contract

The only "shared" knowledge between server and client is the binary format specification:

| Element | Server | Client |
|---------|--------|--------|
| Magic bytes "ABML" | Writes | Validates |
| Version field | Sets | Checks compatibility |
| Opcode values | Emits | Interprets |
| Schema encoding | Serializes | Deserializes |
| Constant pool format | Writes | Reads |

This is documented agreement, not code sharing. The format specification (Section 3.2) is the contract.

---

## 3. Behavior Model Format

### 3.1 Design Goals

The compiled behavior model format must be:

- **Compact**: Minimize network transfer and memory footprint
- **Fast to parse**: Clients load models frequently as characters enter/exit view
- **Version-tolerant**: Old clients can skip unknown opcodes gracefully
- **Debuggable**: Source mapping for development builds

### 3.2 Model Structure

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         BEHAVIOR MODEL BINARY                           │
├─────────────────────────────────────────────────────────────────────────┤
│  HEADER (32 bytes)                                                      │
│  ├── Magic: "ABML" (4 bytes)                                           │
│  ├── Version: uint16 (2 bytes)                                         │
│  ├── Flags: uint16 (2 bytes)                                           │
│  │   ├── Bit 0: Has debug info                                         │
│  │   ├── Bit 1: Compressed                                             │
│  │   ├── Bit 2: Has continuation points                                │
│  │   └── Bit 3: Is extension (attaches to another model)               │
│  ├── Model ID: GUID (16 bytes)                                         │
│  ├── Checksum: uint32 (4 bytes)                                        │
│  └── Reserved (4 bytes)                                                │
├─────────────────────────────────────────────────────────────────────────┤
│  EXTENSION HEADER (only if Is Extension flag set)                       │
│  ├── Parent Model ID: GUID (16 bytes)                                  │
│  ├── Attach Point Name Hash: uint32                                    │
│  └── Replacement Flow Offset: uint32                                   │
├─────────────────────────────────────────────────────────────────────────┤
│  STATE SCHEMA                                                           │
│  ├── Input variable count: uint16                                      │
│  ├── For each input variable:                                          │
│  │   ├── Name hash: uint32                                             │
│  │   ├── Type: uint8 (bool, int, float, enum)                          │
│  │   └── Default value: varies by type                                 │
│  ├── Output variable count: uint16                                     │
│  └── For each output variable:                                         │
│      ├── Name hash: uint32                                             │
│      └── Type: uint8                                                   │
├─────────────────────────────────────────────────────────────────────────┤
│  CONTINUATION POINTS TABLE (only if Has Continuation Points flag set)   │
│  ├── Continuation point count: uint16                                  │
│  └── For each continuation point:                                      │
│      ├── Name hash: uint32                                             │
│      ├── Timeout ms: uint32 (max wait for extension)                   │
│      ├── Default flow offset: uint32 (fallback if no extension)        │
│      └── Bytecode offset: uint32 (where CONTINUATION_POINT opcode is)  │
├─────────────────────────────────────────────────────────────────────────┤
│  CONSTANT POOL                                                          │
│  ├── Constant count: uint16                                            │
│  └── For each constant:                                                │
│      ├── Type: uint8                                                   │
│      └── Value: varies                                                 │
├─────────────────────────────────────────────────────────────────────────┤
│  STRING TABLE                                                           │
│  ├── String count: uint16                                              │
│  └── For each string:                                                  │
│      ├── Length: uint16                                                │
│      └── UTF-8 bytes                                                   │
├─────────────────────────────────────────────────────────────────────────┤
│  BYTECODE                                                               │
│  ├── Bytecode length: uint32                                           │
│  └── Instructions: byte[]                                              │
├─────────────────────────────────────────────────────────────────────────┤
│  DEBUG INFO (optional, if debug flag set)                               │
│  ├── Source file reference                                             │
│  └── Line number mapping                                               │
└─────────────────────────────────────────────────────────────────────────┘
```

See [Section 3.6](#36-streaming-composition--continuation-points) for continuation point semantics.

### 3.3 Behavior Types and Variants

Characters have multiple **behavior types** (combat, movement, interaction), and within each type can have multiple **variants** representing different styles or equipment loadouts.

```
Character Behaviors
├── combat (type)
│   ├── sword-and-shield (variant)   ← Defensive, protects allies
│   ├── dual-wield (variant)         ← Aggressive, high mobility
│   ├── archer (variant)             ← Ranged, positioning-focused
│   ├── two-handed (variant)         ← Heavy hits, commitment
│   └── unarmed (variant)            ← Fallback, always available
├── movement (type)
│   ├── standard (variant)
│   └── mounted (variant)
└── interaction (type)
    └── default (variant)
```

**Why Variants Matter**:

A shield doesn't just enable "block" - it changes the **entire approach** to combat:

| Sword-and-Shield Style | Dual-Wield Style |
|------------------------|------------------|
| Moves to protect allies | Flanks for backstabs |
| Absorbs hits with shield | Dodges everything |
| Holds ground, controls space | Mobile, repositions constantly |
| Shield bash as offense | Relentless attack chains |
| Defensive stance default | Aggressive stance default |

These are fundamentally different decision trees, not just branches on `has_shield`. Separate variant models are cleaner to author, smaller to transfer, and allow different "personalities" per weapon.

**Variant Selection**:

The game client determines which variant to use based on current equipment:

```csharp
private string DetermineCombatVariant(Equipment equipment)
{
    if (equipment.HasBow && equipment.HasArrows)
        return "archer";
    if (equipment.HasShield)
        return "sword-and-shield";
    if (equipment.IsDualWielding)
        return "dual-wield";
    if (equipment.HasTwoHandedWeapon)
        return "two-handed";
    if (equipment.HasOneHandedWeapon)
        return "one-handed";
    return "unarmed";
}
```

**Fallback Chain**:

If a character doesn't have a specific variant, the system falls back:
1. Requested variant (e.g., "dual-wield")
2. "default" variant for that type
3. "unarmed" for combat (always available)

### 3.4 State Schema

The state schema defines what inputs the model expects and what outputs it produces.

**Input Variables** (provided by game client each frame):
```yaml
# Example input schema
inputs:
  # Combat state
  stamina: float          # Current stamina (0-100)
  health_percent: float   # Current health (0-1)
  mana: float            # Current mana (0-100)

  # Positioning
  enemy_distance: float   # Distance to current target
  enemy_angle: float      # Angle to enemy (-180 to 180)
  near_wall: bool         # Within wall slam range
  near_ledge: bool        # Near environmental hazard

  # Enemy state
  enemy_attacking: bool   # Enemy in attack animation
  enemy_staggered: bool   # Enemy is vulnerable
  enemy_blocking: bool    # Enemy is blocking

  # Combo state
  combo_state: int        # Current combo step (0 = none)
  combo_window: bool      # In cancel window

  # Timing
  reaction_window_ms: int # Time to react to incoming attack
```

**Output Variables** (produced by model evaluation):
```yaml
# Example output schema
outputs:
  action: string          # Intent action name
  target_type: string     # "enemy", "position", "object"
  target_id: string       # Specific target identifier
  priority: float         # Action urgency (for queuing)
```

### 3.4 Instruction Set

The bytecode uses a stack-based virtual machine with these instruction categories:

#### Stack Operations
| Opcode | Mnemonic | Description |
|--------|----------|-------------|
| 0x01 | PUSH_CONST | Push constant from pool |
| 0x02 | PUSH_INPUT | Push input variable value |
| 0x03 | PUSH_LOCAL | Push local variable |
| 0x04 | POP | Discard top of stack |
| 0x05 | DUP | Duplicate top of stack |
| 0x06 | SWAP | Swap top two stack values |

#### Arithmetic Operations
| Opcode | Mnemonic | Description |
|--------|----------|-------------|
| 0x10 | ADD | a + b |
| 0x11 | SUB | a - b |
| 0x12 | MUL | a * b |
| 0x13 | DIV | a / b |
| 0x14 | MOD | a % b |
| 0x15 | NEG | -a |

#### Comparison Operations
| Opcode | Mnemonic | Description |
|--------|----------|-------------|
| 0x20 | EQ | a == b |
| 0x21 | NE | a != b |
| 0x22 | LT | a < b |
| 0x23 | LE | a <= b |
| 0x24 | GT | a > b |
| 0x25 | GE | a >= b |

#### Logical Operations
| Opcode | Mnemonic | Description |
|--------|----------|-------------|
| 0x30 | AND | a && b |
| 0x31 | OR | a \|\| b |
| 0x32 | NOT | !a |

#### Control Flow
| Opcode | Mnemonic | Description |
|--------|----------|-------------|
| 0x40 | JMP | Unconditional jump |
| 0x41 | JMP_IF | Jump if top of stack is true |
| 0x42 | JMP_UNLESS | Jump if top of stack is false |
| 0x43 | CALL | Call subroutine |
| 0x44 | RET | Return from subroutine |
| 0x45 | HALT | Stop execution |

#### Output Operations
| Opcode | Mnemonic | Description |
|--------|----------|-------------|
| 0x50 | SET_OUTPUT | Set output variable |
| 0x51 | EMIT_INTENT | Emit action intent (shorthand for common pattern) |

#### Special Operations
| Opcode | Mnemonic | Description |
|--------|----------|-------------|
| 0x60 | RAND | Push random float (0-1) |
| 0x61 | RAND_INT | Push random int in range |
| 0x62 | LERP | Linear interpolation |
| 0x63 | CLAMP | Clamp value to range |

#### Streaming Composition Operations
| Opcode | Mnemonic | Description |
|--------|----------|-------------|
| 0x70 | CONTINUATION_POINT | Pause for possible extension (see §3.6) |
| 0x71 | YIELD_TO_EXTENSION | Transfer control to attached extension |
| 0x72 | EXTENSION_AVAILABLE | Push 1 if extension attached, 0 otherwise |

### 3.5 Example Compilation

**ABML Source:**
```yaml
flows:
  combat_decision:
    - cond:
        - when: "${enemy_staggered && stamina > 30}"
          then:
            - emit_intent: { action: heavy_attack }
        - when: "${enemy_attacking && reaction_window_ms > 200}"
          then:
            - emit_intent: { action: parry }
        - otherwise:
            - emit_intent: { action: quick_attack }
```

**Compiled Bytecode (annotated):**
```
; Check: enemy_staggered && stamina > 30
0000: PUSH_INPUT    0        ; enemy_staggered
0002: JMP_UNLESS    0x0014   ; skip to next condition if false
0005: PUSH_INPUT    1        ; stamina
0007: PUSH_CONST    0        ; 30.0
0009: GT                     ; stamina > 30
000A: JMP_UNLESS    0x0014   ; skip to next condition if false

; Action: heavy_attack
000D: PUSH_CONST    1        ; "heavy_attack"
000F: SET_OUTPUT    0        ; action = "heavy_attack"
0011: HALT

; Check: enemy_attacking && reaction_window_ms > 200
0014: PUSH_INPUT    2        ; enemy_attacking
0016: JMP_UNLESS    0x0028   ; skip to otherwise if false
0019: PUSH_INPUT    3        ; reaction_window_ms
001B: PUSH_CONST    2        ; 200
001D: GT                     ; reaction_window_ms > 200
001E: JMP_UNLESS    0x0028   ; skip to otherwise if false

; Action: parry
0021: PUSH_CONST    3        ; "parry"
0023: SET_OUTPUT    0        ; action = "parry"
0025: HALT

; Otherwise: quick_attack
0028: PUSH_CONST    4        ; "quick_attack"
002A: SET_OUTPUT    0        ; action = "quick_attack"
002C: HALT
```

### 3.6 Streaming Composition & Continuation Points

Streaming composition enables **runtime extension** of executing behaviors. A game server receives a complete, executable cinematic/behavior, starts executing immediately, and can optionally receive extensions that attach to named points.

#### Why Streaming Composition?

THE_DREAM requires: *"The game engine is always capable of completing the cinematic with what it was initially given. Event Agent enriches but isn't required for completion."*

This means:
- Initial delivery must be **complete** - can execute without server contact
- Extensions are **optional enhancements** - arrival is not guaranteed
- Missing extensions must **degrade gracefully** - default behavior plays

Import/include (design-time) and service calls (synchronous, blocking) don't satisfy these requirements.

#### ABML Syntax: Continuation Points

```yaml
# Cinematic with continuation point
version: "2.0"
metadata:
  id: "dramatic_duel"
  type: "cinematic"

flows:
  main:
    - parallel:
        camera: { flow: camera_track }
        hero: { flow: hero_actions }
        villain: { flow: villain_actions }

    - continuation_point:
        name: "before_resolution"
        timeout: 2s                    # Wait up to 2s for extension
        default_flow: default_ending   # Fallback if no extension arrives

  default_ending:
    - parallel:
        camera: { flow: stalemate_camera }
        hero: { flow: hero_backs_off }
        villain: { flow: villain_retreats }
```

#### ABML Syntax: Extensions

```yaml
# Extension that attaches to a continuation point
version: "2.0"
metadata:
  id: "dramatic_finish_chandelier"
  type: "cinematic_extension"
  extends: "dramatic_duel"            # Parent model ID
  attach_point: "before_resolution"   # Which continuation point

flows:
  main:  # Replaces default_ending when attached
    - parallel:
        camera: { flow: chandelier_focus }
        hero: { flow: hero_uses_chandelier }
        villain: { flow: villain_crushed }
```

#### Bytecode Representation

The `CONTINUATION_POINT` opcode (0x70) takes two operands:
- `continuation_point_index` (uint16) - Index into continuation points table
- Implicit: timeout and default flow offset from table

```
; At continuation point
0100: CONTINUATION_POINT 0    ; Index 0 in continuation points table
                              ; Interpreter pauses, checks for extension
                              ; If extension attached: jumps to extension
                              ; If timeout expires: jumps to default flow
```

#### Runtime Behavior

```
Timeline:
0s     Game Server receives Cinematic (complete, standalone)
0-10s  Executes Act 1, Act 2
8s     Event Brain decides to extend based on player action
8.5s   Extension arrives, attached to "before_resolution"
10s    Execution hits CONTINUATION_POINT
       → Extension available → transfers control to extension
10-20s Extended Act 3 plays seamlessly

Alternative (extension doesn't arrive):
10s    Execution hits CONTINUATION_POINT
       → No extension → waits up to timeout (2s)
12s    Timeout expires → jumps to default_ending
12-17s Default Act 3 plays (stalemate)
```

#### Graceful Degradation

| Scenario | Behavior |
|----------|----------|
| Extension arrives early | Used immediately at continuation point |
| Extension arrives within timeout | Used (waited for it) |
| Extension arrives late | Ignored, default already playing |
| Extension never arrives | Default plays after timeout |
| Network failure | Default plays, complete experience |

#### Control Handoff for Cinematics

Cinematics that take control of characters include control metadata:

```yaml
metadata:
  id: "dramatic_duel"
  type: "cinematic"
  takes_control_of: ["character-123", "character-456"]
  control_mode: exclusive    # Suspends basic behavior evaluation
  on_complete: resume_basic  # Returns control after cinematic
```

When a cinematic arrives with `takes_control_of`, the game server:
1. Pauses basic behavior evaluation for listed characters
2. Executes cinematic (which controls those characters)
3. On completion, resumes basic behavior evaluation

---

## 4. Local Runtime Interpreter

### 4.1 Design Goals

The local runtime interpreter must be:

- **Allocation-free** during evaluation (no GC pressure)
- **Branchless** where possible (predictable performance)
- **Cache-friendly** (sequential memory access)
- **Deterministic** (same inputs = same outputs, always)

### 4.2 Core Interface

```csharp
/// <summary>
/// Lightweight interpreter for compiled ABML behavior models.
/// Designed for per-frame evaluation with zero allocations.
/// </summary>
public sealed class BehaviorModelInterpreter
{
    private readonly byte[] _bytecode;
    private readonly object[] _constantPool;
    private readonly string[] _stringTable;
    private readonly StateSchema _inputSchema;
    private readonly StateSchema _outputSchema;

    // Pre-allocated evaluation state (reused across evaluations)
    private readonly double[] _stack;
    private readonly double[] _locals;
    private int _stackPointer;
    private int _instructionPointer;

    /// <summary>
    /// Creates an interpreter for the given behavior model.
    /// </summary>
    public BehaviorModelInterpreter(BehaviorModel model)
    {
        _bytecode = model.Bytecode;
        _constantPool = model.ConstantPool;
        _stringTable = model.StringTable;
        _inputSchema = model.InputSchema;
        _outputSchema = model.OutputSchema;

        // Pre-allocate stack and locals based on model requirements
        _stack = new double[model.MaxStackDepth];
        _locals = new double[model.LocalCount];
    }

    /// <summary>
    /// Evaluates the behavior model with the given input state.
    /// Returns the output state (action intent).
    ///
    /// This method is allocation-free after initial setup.
    /// </summary>
    /// <param name="inputState">Current game state values.</param>
    /// <param name="outputState">Pre-allocated output buffer.</param>
    public void Evaluate(
        ReadOnlySpan<double> inputState,
        Span<double> outputState)
    {
        // Reset evaluation state
        _stackPointer = 0;
        _instructionPointer = 0;

        // Main evaluation loop
        while (_instructionPointer < _bytecode.Length)
        {
            var opcode = (Opcode)_bytecode[_instructionPointer++];

            switch (opcode)
            {
                case Opcode.PUSH_INPUT:
                    var inputIndex = _bytecode[_instructionPointer++];
                    _stack[_stackPointer++] = inputState[inputIndex];
                    break;

                case Opcode.PUSH_CONST:
                    var constIndex = _bytecode[_instructionPointer++];
                    _stack[_stackPointer++] = (double)_constantPool[constIndex];
                    break;

                case Opcode.GT:
                    var b = _stack[--_stackPointer];
                    var a = _stack[--_stackPointer];
                    _stack[_stackPointer++] = a > b ? 1.0 : 0.0;
                    break;

                case Opcode.AND:
                    var rb = _stack[--_stackPointer] != 0.0;
                    var ra = _stack[--_stackPointer] != 0.0;
                    _stack[_stackPointer++] = (ra && rb) ? 1.0 : 0.0;
                    break;

                case Opcode.JMP_UNLESS:
                    var target = ReadUInt16();
                    if (_stack[--_stackPointer] == 0.0)
                        _instructionPointer = target;
                    break;

                case Opcode.SET_OUTPUT:
                    var outputIndex = _bytecode[_instructionPointer++];
                    outputState[outputIndex] = _stack[--_stackPointer];
                    break;

                case Opcode.HALT:
                    return;

                // ... other opcodes
            }
        }
    }

    private ushort ReadUInt16()
    {
        var value = (ushort)(_bytecode[_instructionPointer] |
                            (_bytecode[_instructionPointer + 1] << 8));
        _instructionPointer += 2;
        return value;
    }
}
```

### 4.3 Cinematic Interpreter (Streaming Composition)

For cinematics with continuation points, a specialized interpreter handles extension attachment:

```csharp
/// <summary>
/// Interpreter for cinematics that supports streaming extension attachment.
/// Unlike BehaviorModelInterpreter (single-frame evaluation), this executes
/// over time and can receive extensions mid-execution.
/// </summary>
public sealed class CinematicInterpreter
{
    private BehaviorModel _currentModel;
    private readonly Dictionary<uint, BehaviorModel> _attachedExtensions = new();
    private readonly List<ContinuationPoint> _continuationPoints;
    private PendingContinuation? _pendingContinuation;
    private int _instructionPointer;

    public CinematicInterpreter(BehaviorModel baseModel)
    {
        _currentModel = baseModel;
        _continuationPoints = baseModel.ContinuationPoints.ToList();
    }

    /// <summary>
    /// Attach an extension to a continuation point.
    /// Thread-safe - can be called while cinematic is executing.
    /// </summary>
    public void AttachExtension(BehaviorModel extension)
    {
        if (!extension.IsExtension)
            throw new ArgumentException("Model is not an extension");

        lock (_attachedExtensions)
        {
            _attachedExtensions[extension.AttachPointHash] = extension;
        }

        // If we're waiting at this continuation point, signal arrival
        if (_pendingContinuation?.Point.NameHash == extension.AttachPointHash)
        {
            _pendingContinuation.ExtensionArrived = true;
        }
    }

    /// <summary>
    /// Called when execution reaches a CONTINUATION_POINT opcode.
    /// </summary>
    private ExecutionState HandleContinuationPoint(ushort cpIndex)
    {
        var cp = _continuationPoints[cpIndex];

        // Check if extension already attached
        lock (_attachedExtensions)
        {
            if (_attachedExtensions.TryGetValue(cp.NameHash, out var extension))
            {
                // Extension available - transfer control
                _currentModel = extension;
                _instructionPointer = extension.MainFlowOffset;
                return ExecutionState.Running;
            }
        }

        // No extension yet - set up wait
        _pendingContinuation = new PendingContinuation
        {
            Point = cp,
            Deadline = DateTime.UtcNow.AddMilliseconds(cp.TimeoutMs),
            ExtensionArrived = false
        };

        return ExecutionState.WaitingForExtension;
    }

    /// <summary>
    /// Called each frame while waiting at a continuation point.
    /// Returns true if we should continue execution.
    /// </summary>
    public bool CheckContinuationPoint()
    {
        if (_pendingContinuation == null)
            return true;  // Not waiting

        // Extension arrived?
        if (_pendingContinuation.ExtensionArrived)
        {
            var cp = _pendingContinuation.Point;
            _pendingContinuation = null;

            lock (_attachedExtensions)
            {
                var extension = _attachedExtensions[cp.NameHash];
                _currentModel = extension;
                _instructionPointer = extension.MainFlowOffset;
            }
            return true;  // Continue with extension
        }

        // Timeout expired?
        if (DateTime.UtcNow >= _pendingContinuation.Deadline)
        {
            var cp = _pendingContinuation.Point;
            _pendingContinuation = null;

            // Use default flow
            _instructionPointer = cp.DefaultFlowOffset;
            return true;  // Continue with default
        }

        return false;  // Still waiting
    }

    private sealed class PendingContinuation
    {
        public ContinuationPoint Point { get; init; }
        public DateTime Deadline { get; init; }
        public bool ExtensionArrived { get; set; }
    }
}
```

**Key Differences from BehaviorModelInterpreter**:

| Aspect | BehaviorModelInterpreter | CinematicInterpreter |
|--------|--------------------------|----------------------|
| Execution model | Single-frame evaluation | Runs over time |
| Extensions | Not supported | Can attach mid-execution |
| State | Resets each evaluation | Persists across frames |
| Use case | Per-frame combat decisions | Multi-second cinematics |

### 4.4 State Provider Pattern

The game client implements a state provider that maps game state to model inputs:

```csharp
/// <summary>
/// Provides current game state values for behavior model evaluation.
/// </summary>
public interface IBehaviorStateProvider
{
    /// <summary>
    /// Fills the input state buffer with current values.
    /// Called each frame before behavior evaluation.
    /// </summary>
    void GetCurrentState(Span<double> inputState, StateSchema schema);
}

/// <summary>
/// Combat-focused state provider for character entities.
/// </summary>
public class CombatStateProvider : IBehaviorStateProvider
{
    private readonly CharacterEntity _character;
    private readonly CombatSystem _combatSystem;

    public void GetCurrentState(Span<double> inputState, StateSchema schema)
    {
        // Map game state to model inputs by schema index
        for (int i = 0; i < schema.InputCount; i++)
        {
            var varName = schema.GetInputName(i);
            inputState[i] = varName switch
            {
                "stamina" => _character.Stamina,
                "health_percent" => _character.Health / _character.MaxHealth,
                "enemy_distance" => _combatSystem.GetDistanceToTarget(_character),
                "enemy_attacking" => _combatSystem.IsTargetAttacking(_character) ? 1.0 : 0.0,
                "enemy_staggered" => _combatSystem.IsTargetStaggered(_character) ? 1.0 : 0.0,
                "combo_state" => _character.ComboState,
                "combo_window" => _character.InComboWindow ? 1.0 : 0.0,
                "reaction_window_ms" => _combatSystem.GetReactionWindow(_character),
                _ => schema.GetDefaultValue(i)
            };
        }
    }
}
```

### 4.5 Intent System Integration

The output of behavior evaluation is an **action intent** - a request for the game engine to perform an action:

```csharp
/// <summary>
/// An action intent produced by behavior model evaluation.
/// </summary>
public readonly struct ActionIntent
{
    public readonly StringHash Action;      // "heavy_attack", "parry", etc.
    public readonly StringHash TargetType;  // "enemy", "position", "object"
    public readonly int TargetId;           // Entity ID or -1
    public readonly float Priority;         // For intent queuing

    public static ActionIntent FromOutput(
        ReadOnlySpan<double> output,
        string[] stringTable)
    {
        return new ActionIntent
        {
            Action = new StringHash(stringTable[(int)output[0]]),
            TargetType = new StringHash(stringTable[(int)output[1]]),
            TargetId = (int)output[2],
            Priority = (float)output[3]
        };
    }
}

/// <summary>
/// System that processes action intents from behavior evaluation.
/// </summary>
public class IntentSystem
{
    private readonly AnimationSystem _animation;
    private readonly CombatSystem _combat;

    /// <summary>
    /// Process an action intent, triggering appropriate game systems.
    /// </summary>
    public void ProcessIntent(CharacterEntity character, ActionIntent intent)
    {
        // Map intent to game action
        switch (intent.Action.Value)
        {
            case "heavy_attack":
                if (_combat.CanPerformAction(character, CombatAction.HeavyAttack))
                {
                    _animation.TriggerAttack(character, AttackType.Heavy);
                    _combat.StartAttack(character, CombatAction.HeavyAttack);
                }
                break;

            case "parry":
                if (_combat.CanPerformAction(character, CombatAction.Parry))
                {
                    _animation.TriggerParry(character);
                    _combat.StartParry(character);
                }
                break;

            case "quick_attack":
                if (_combat.CanPerformAction(character, CombatAction.QuickAttack))
                {
                    _animation.TriggerAttack(character, AttackType.Quick);
                    _combat.StartAttack(character, CombatAction.QuickAttack);
                }
                break;

            // ... other actions
        }
    }
}
```

---

## 5. Behavior Plugin Responsibilities

### 5.1 Expanded Role

The Behavior Plugin's responsibilities expand from "behavior execution" to "behavior lifecycle management":

```
┌─────────────────────────────────────────────────────────────────────────┐
│                      BEHAVIOR PLUGIN RESPONSIBILITIES                   │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  1. AUTHORING SUPPORT                                                   │
│     ├── ABML parsing and validation                                    │
│     ├── Schema registry for type imports                               │
│     └── Error reporting with source locations                          │
│                                                                         │
│  2. COMPILATION                                                         │
│     ├── AST to bytecode compilation                                    │
│     ├── Optimization passes                                            │
│     ├── Debug info generation                                          │
│     └── Model serialization                                            │
│                                                                         │
│  3. STORAGE & VERSIONING                                                │
│     ├── Model persistence (lib-state)                                  │
│     ├── Version tracking per character                                 │
│     ├── Dependency tracking (imported behaviors)                       │
│     └── Garbage collection of unused models                            │
│                                                                         │
│  4. DISTRIBUTION                                                        │
│     ├── Model delivery to game clients                                 │
│     ├── Delta updates (changed behaviors only)                         │
│     ├── Priority-based distribution (nearby characters first)          │
│     └── Bandwidth management                                           │
│                                                                         │
│  5. CLOUD-SIDE EXECUTION                                                │
│     ├── Full ABML interpretation (DocumentExecutor)                    │
│     ├── Character agent decision-making                                │
│     ├── Event Brain orchestration                                      │
│     └── GOAP planning integration                                      │
│                                                                         │
│  6. SYNCHRONIZATION                                                     │
│     ├── Character learns new behavior → compile → distribute           │
│     ├── Behavior modified by gameplay → recompile → push update        │
│     └── Client reconnect → send current model set                      │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### 5.2 New API Endpoints

```yaml
# Additions to behavior-api.yaml

paths:
  # ═══════════════════════════════════════════════════════════════════════
  # COMPILATION ENDPOINTS
  # ═══════════════════════════════════════════════════════════════════════

  /behavior/compile:
    post:
      summary: Compile ABML document to behavior model
      operationId: CompileBehavior
      tags: [Compilation]
      x-permissions:
        - role: service
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CompileBehaviorRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CompileBehaviorResponse'

  /behavior/validate:
    post:
      summary: Validate ABML document without compiling
      operationId: ValidateBehavior
      tags: [Compilation]
      x-permissions:
        - role: service
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ValidateBehaviorRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ValidateBehaviorResponse'

  # ═══════════════════════════════════════════════════════════════════════
  # MODEL MANAGEMENT ENDPOINTS
  # ═══════════════════════════════════════════════════════════════════════

  /behavior/models/get:
    post:
      summary: Get compiled behavior model by ID
      operationId: GetBehaviorModel
      tags: [Models]
      x-permissions:
        - role: service
        - role: client
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetModelRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/GetModelResponse'

  /behavior/models/list:
    post:
      summary: List behavior models for a character
      operationId: ListBehaviorModels
      tags: [Models]
      x-permissions:
        - role: service
        - role: client
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListModelsRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ListModelsResponse'

  /behavior/models/sync:
    post:
      summary: Sync behavior models for characters in view
      description: |
        Called by game client to get/update behavior models for
        characters that have entered the client's view.
      operationId: SyncBehaviorModels
      tags: [Models]
      x-permissions:
        - role: client
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/SyncModelsRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SyncModelsResponse'

components:
  schemas:
    CompileBehaviorRequest:
      type: object
      required:
        - source
        - compile_target
      properties:
        source:
          type: string
          description: ABML YAML source code
        compile_target:
          type: string
          enum: [local_runtime, cloud_interpreter]
          description: Target execution environment
        include_debug_info:
          type: boolean
          default: false
          description: Include source mapping for debugging
        optimization_level:
          type: integer
          minimum: 0
          maximum: 3
          default: 1
          description: Optimization level (0=none, 3=aggressive)

    CompileBehaviorResponse:
      type: object
      required:
        - success
      properties:
        success:
          type: boolean
        model_id:
          type: string
          format: uuid
          description: ID of compiled model (if successful)
        model_bytes:
          type: string
          format: byte
          description: Base64-encoded model binary (if successful)
        errors:
          type: array
          items:
            $ref: '#/components/schemas/CompilationError'
        warnings:
          type: array
          items:
            $ref: '#/components/schemas/CompilationWarning'

    CompilationError:
      type: object
      required:
        - message
        - line
        - column
      properties:
        message:
          type: string
        line:
          type: integer
        column:
          type: integer
        source_snippet:
          type: string

    SyncModelsRequest:
      type: object
      required:
        - character_ids
      properties:
        character_ids:
          type: array
          description: Characters that need behavior models
          items:
            type: string
        known_versions:
          type: object
          description: Model IDs the client already has (for delta sync)
          additionalProperties:
            type: string

    SyncModelsResponse:
      type: object
      required:
        - models
      properties:
        models:
          type: array
          items:
            $ref: '#/components/schemas/CharacterBehaviorSet'

    CharacterBehaviorSet:
      type: object
      required:
        - character_id
        - behavior_types
      properties:
        character_id:
          type: string
        behavior_types:
          type: array
          description: All behavior types this character has
          items:
            $ref: '#/components/schemas/BehaviorTypeSet'

    BehaviorTypeSet:
      type: object
      required:
        - behavior_type
        - variants
      properties:
        behavior_type:
          type: string
          enum: [combat, movement, interaction, idle]
        active_variant:
          type: string
          description: Currently selected variant (based on equipment/state)
        variants:
          type: array
          description: All variants the character knows for this type
          items:
            $ref: '#/components/schemas/BehaviorVariant'

    BehaviorVariant:
      type: object
      required:
        - variant_id
        - model_id
      properties:
        variant_id:
          type: string
          description: Variant identifier (e.g., "sword-and-shield", "dual-wield")
        model_id:
          type: string
          format: uuid
        version:
          type: integer
        proficiency:
          type: number
          format: float
          minimum: 0.0
          maximum: 1.0
          description: How well the character knows this style (affects model selection)
        triggers:
          type: array
          description: Conditions that activate this variant
          items:
            type: string
          example: ["equipment.has_shield", "equipment.is_dual_wielding"]
        model_bytes:
          type: string
          format: byte
          description: Only included if client doesn't have this version

    # Legacy/simple format for backwards compatibility
    BehaviorModelRef:
      type: object
      required:
        - model_id
        - behavior_type
      properties:
        model_id:
          type: string
          format: uuid
        behavior_type:
          type: string
          enum: [combat, movement, interaction, idle]
        variant_id:
          type: string
          description: Optional variant identifier
        version:
          type: integer
        model_bytes:
          type: string
          format: byte
          description: Only included if client doesn't have this version
```

### 5.3 Compilation Pipeline

```csharp
/// <summary>
/// Compiles ABML documents to behavior models.
/// </summary>
public interface IBehaviorCompiler
{
    /// <summary>
    /// Compiles an ABML document to a behavior model.
    /// </summary>
    CompilationResult Compile(
        AbmlDocument document,
        CompilationOptions options);
}

/// <summary>
/// Standard ABML to bytecode compiler.
/// </summary>
public sealed class BehaviorCompiler : IBehaviorCompiler
{
    private readonly ISchemaRegistry _schemas;

    public CompilationResult Compile(
        AbmlDocument document,
        CompilationOptions options)
    {
        var result = new CompilationResult();

        // Phase 1: Semantic analysis
        var analyzer = new SemanticAnalyzer(_schemas);
        var analysisResult = analyzer.Analyze(document);
        if (analysisResult.HasErrors)
        {
            result.Errors.AddRange(analysisResult.Errors);
            return result;
        }

        // Phase 2: Build intermediate representation
        var irBuilder = new IrBuilder();
        var ir = irBuilder.Build(analysisResult.TypedAst);

        // Phase 3: Optimization (if requested)
        if (options.OptimizationLevel > 0)
        {
            var optimizer = new IrOptimizer(options.OptimizationLevel);
            ir = optimizer.Optimize(ir);
        }

        // Phase 4: Code generation
        var codeGen = new BytecodeGenerator();
        var bytecode = codeGen.Generate(ir);

        // Phase 5: Build model
        var model = new BehaviorModel
        {
            Id = Guid.NewGuid(),
            Version = 1,
            InputSchema = BuildInputSchema(analysisResult),
            OutputSchema = BuildOutputSchema(analysisResult),
            ConstantPool = codeGen.ConstantPool,
            StringTable = codeGen.StringTable,
            Bytecode = bytecode,
            DebugInfo = options.IncludeDebugInfo
                ? BuildDebugInfo(ir, codeGen)
                : null
        };

        result.Success = true;
        result.Model = model;
        return result;
    }
}
```

### 5.4 Distribution Service

```csharp
/// <summary>
/// Manages distribution of behavior models to game clients.
/// </summary>
public interface IBehaviorDistributionService
{
    /// <summary>
    /// Called when a character's behavior changes.
    /// Triggers recompilation and distribution to interested clients.
    /// </summary>
    Task OnBehaviorChangedAsync(
        string characterId,
        string behaviorType,
        AbmlDocument newBehavior,
        CancellationToken ct);

    /// <summary>
    /// Gets behavior models for characters.
    /// Supports delta sync - only returns models the client doesn't have.
    /// </summary>
    Task<SyncModelsResponse> SyncModelsAsync(
        SyncModelsRequest request,
        CancellationToken ct);
}

/// <summary>
/// Implementation that integrates with lib-state and lib-messaging.
/// </summary>
public sealed class BehaviorDistributionService : IBehaviorDistributionService
{
    private readonly IStateStore<BehaviorModelMetadata> _modelMetadata;
    private readonly IStateStore<byte[]> _modelBinaries;
    private readonly IBehaviorCompiler _compiler;
    private readonly IMessageBus _messageBus;

    public async Task OnBehaviorChangedAsync(
        string characterId,
        string behaviorType,
        AbmlDocument newBehavior,
        CancellationToken ct)
    {
        // Compile the new behavior
        var result = _compiler.Compile(newBehavior, new CompilationOptions
        {
            CompileTarget = CompileTarget.LocalRuntime,
            OptimizationLevel = 2
        });

        if (!result.Success)
        {
            _logger.LogError("Failed to compile behavior for {CharacterId}: {Errors}",
                characterId, result.Errors);
            return;
        }

        // Store the model
        var modelKey = $"{characterId}:{behaviorType}";
        await _modelMetadata.SaveAsync(modelKey, new BehaviorModelMetadata
        {
            ModelId = result.Model.Id,
            CharacterId = characterId,
            BehaviorType = behaviorType,
            Version = result.Model.Version,
            CompiledAt = DateTimeOffset.UtcNow
        }, ct);

        await _modelBinaries.SaveAsync(
            result.Model.Id.ToString(),
            result.Model.Serialize(),
            ct);

        // Notify interested clients
        await _messageBus.PublishAsync(
            $"behavior.model.updated.{characterId}",
            new BehaviorModelUpdatedEvent
            {
                CharacterId = characterId,
                BehaviorType = behaviorType,
                ModelId = result.Model.Id,
                Version = result.Model.Version
            },
            ct);
    }

    public async Task<SyncModelsResponse> SyncModelsAsync(
        SyncModelsRequest request,
        CancellationToken ct)
    {
        var response = new SyncModelsResponse { Models = new List<CharacterBehaviorSet>() };

        foreach (var characterId in request.CharacterIds)
        {
            var behaviorSet = new CharacterBehaviorSet
            {
                CharacterId = characterId,
                Behaviors = new List<BehaviorModelRef>()
            };

            // Get all behavior types for this character
            var metadata = await _modelMetadata.GetByPrefixAsync($"{characterId}:", ct);

            foreach (var meta in metadata)
            {
                var modelRef = new BehaviorModelRef
                {
                    ModelId = meta.ModelId,
                    BehaviorType = meta.BehaviorType,
                    Version = meta.Version
                };

                // Check if client already has this version
                var clientVersion = request.KnownVersions?.GetValueOrDefault(
                    $"{characterId}:{meta.BehaviorType}");

                if (clientVersion != meta.ModelId.ToString())
                {
                    // Client needs the full model
                    var binary = await _modelBinaries.GetAsync(
                        meta.ModelId.ToString(), ct);
                    modelRef.ModelBytes = binary;
                }

                behaviorSet.Behaviors.Add(modelRef);
            }

            response.Models.Add(behaviorSet);
        }

        return response;
    }
}
```

---

## 6. Client-Side Integration

### 6.1 Model Cache with Variant Support

```csharp
/// <summary>
/// Caches behavior models on the game client, organized by character/type/variant.
/// </summary>
public sealed class BehaviorModelCache
{
    // Model storage: modelId -> model
    private readonly Dictionary<Guid, BehaviorModel> _models = new();

    // Interpreter cache: "charId:type:variant" -> interpreter
    private readonly Dictionary<string, BehaviorModelInterpreter> _interpreters = new();

    // Variant metadata: "charId:type" -> list of known variants
    private readonly Dictionary<string, List<VariantInfo>> _variantsByType = new();

    /// <summary>
    /// Adds or updates a behavior model variant in the cache.
    /// </summary>
    public void UpdateModel(
        string characterId,
        string behaviorType,
        string variantId,
        BehaviorModel model,
        float proficiency = 1.0f)
    {
        _models[model.Id] = model;

        // Create interpreter for this variant
        var key = $"{characterId}:{behaviorType}:{variantId}";
        _interpreters[key] = new BehaviorModelInterpreter(model);

        // Track variant metadata
        var typeKey = $"{characterId}:{behaviorType}";
        if (!_variantsByType.TryGetValue(typeKey, out var variants))
        {
            variants = new List<VariantInfo>();
            _variantsByType[typeKey] = variants;
        }

        // Update or add variant info
        var existing = variants.FindIndex(v => v.VariantId == variantId);
        var info = new VariantInfo
        {
            VariantId = variantId,
            ModelId = model.Id,
            Proficiency = proficiency
        };

        if (existing >= 0)
            variants[existing] = info;
        else
            variants.Add(info);
    }

    /// <summary>
    /// Gets the interpreter for a specific character/type/variant.
    /// </summary>
    public BehaviorModelInterpreter? GetInterpreter(
        string characterId,
        string behaviorType,
        string variantId)
    {
        var key = $"{characterId}:{behaviorType}:{variantId}";
        return _interpreters.GetValueOrDefault(key);
    }

    /// <summary>
    /// Gets the interpreter for a character/type, using fallback chain if needed.
    /// </summary>
    public BehaviorModelInterpreter? GetInterpreterWithFallback(
        string characterId,
        string behaviorType,
        string preferredVariant)
    {
        // Try preferred variant first
        var interpreter = GetInterpreter(characterId, behaviorType, preferredVariant);
        if (interpreter != null)
            return interpreter;

        // Try "default" variant
        interpreter = GetInterpreter(characterId, behaviorType, "default");
        if (interpreter != null)
            return interpreter;

        // For combat, try "unarmed" as final fallback
        if (behaviorType == "combat")
        {
            interpreter = GetInterpreter(characterId, behaviorType, "unarmed");
            if (interpreter != null)
                return interpreter;
        }

        return null;
    }

    /// <summary>
    /// Gets all known variants for a character's behavior type.
    /// </summary>
    public IReadOnlyList<VariantInfo> GetKnownVariants(
        string characterId,
        string behaviorType)
    {
        var key = $"{characterId}:{behaviorType}";
        return _variantsByType.GetValueOrDefault(key)
            ?? (IReadOnlyList<VariantInfo>)Array.Empty<VariantInfo>();
    }

    /// <summary>
    /// Checks if a character has any behaviors loaded.
    /// </summary>
    public bool HasCharacter(string characterId)
    {
        return _variantsByType.Keys.Any(k => k.StartsWith(characterId + ":"));
    }

    /// <summary>
    /// Gets version info for sync requests.
    /// </summary>
    public Dictionary<string, string> GetKnownVersions(IEnumerable<string> characterIds)
    {
        var versions = new Dictionary<string, string>();
        foreach (var charId in characterIds)
        {
            foreach (var (key, interpreter) in _interpreters)
            {
                if (key.StartsWith(charId + ":"))
                {
                    versions[key] = interpreter.ModelId.ToString();
                }
            }
        }
        return versions;
    }
}

/// <summary>
/// Metadata about a behavior variant.
/// </summary>
public readonly struct VariantInfo
{
    public string VariantId { get; init; }
    public Guid ModelId { get; init; }
    public float Proficiency { get; init; }
}
```

### 6.2 Per-Frame Evaluation with Variant Selection

```csharp
/// <summary>
/// System that evaluates behavior models each frame for AI-controlled characters.
/// Handles dynamic variant selection based on equipment state.
/// </summary>
public sealed class BehaviorEvaluationSystem : GameSystem
{
    private readonly BehaviorModelCache _modelCache;
    private readonly IntentSystem _intentSystem;
    private readonly IVariantSelector _variantSelector;

    // Pre-allocated buffers (avoid allocations in hot loop)
    private readonly double[] _inputBuffer = new double[64];
    private readonly double[] _outputBuffer = new double[16];

    public override void Update(GameTime gameTime)
    {
        // Only evaluate for characters that need AI decisions
        var aiCharacters = GetAIControlledCharacters();

        foreach (var character in aiCharacters)
        {
            // Skip if player is controlling this character
            if (character.IsPlayerControlled)
                continue;

            // Determine which combat variant to use based on equipment
            var combatVariant = _variantSelector.SelectCombatVariant(character);

            // Get the interpreter for that variant (with fallback chain)
            var interpreter = _modelCache.GetInterpreterWithFallback(
                character.Id,
                "combat",
                combatVariant);

            if (interpreter == null)
                continue; // No behavior loaded yet

            // Fill input state from current game state
            character.StateProvider.GetCurrentState(
                _inputBuffer.AsSpan(),
                interpreter.InputSchema);

            // Evaluate behavior model
            interpreter.Evaluate(
                _inputBuffer.AsSpan(),
                _outputBuffer.AsSpan());

            // Convert output to action intent
            var intent = ActionIntent.FromOutput(
                _outputBuffer.AsSpan(),
                interpreter.StringTable);

            // Process the intent
            _intentSystem.ProcessIntent(character, intent);
        }
    }
}

/// <summary>
/// Selects behavior variants based on character state.
/// </summary>
public interface IVariantSelector
{
    string SelectCombatVariant(CharacterEntity character);
    string SelectMovementVariant(CharacterEntity character);
}

/// <summary>
/// Standard variant selector based on equipment.
/// </summary>
public sealed class EquipmentBasedVariantSelector : IVariantSelector
{
    public string SelectCombatVariant(CharacterEntity character)
    {
        var equipment = character.Equipment;

        // Check equipment in priority order
        if (equipment.HasBow && character.Inventory.GetAmmoCount("arrow") > 0)
            return "archer";

        if (equipment.HasShield)
            return "sword-and-shield";

        if (equipment.IsDualWielding)
            return "dual-wield";

        if (equipment.HasTwoHandedWeapon)
            return "two-handed";

        if (equipment.HasOneHandedWeapon)
            return "one-handed";

        return "unarmed";
    }

    public string SelectMovementVariant(CharacterEntity character)
    {
        if (character.IsMounted)
            return "mounted";

        if (character.IsSwimming)
            return "swimming";

        if (character.IsClimbing)
            return "climbing";

        return "standard";
    }
}
```

### 6.3 Handling Model Updates with Variants

```csharp
/// <summary>
/// Handles behavior model updates from the server.
/// Supports the variant-aware sync protocol.
/// </summary>
public sealed class BehaviorSyncHandler
{
    private readonly BannouClient _client;
    private readonly BehaviorModelCache _modelCache;

    public BehaviorSyncHandler(BannouClient client, BehaviorModelCache modelCache)
    {
        _client = client;
        _modelCache = modelCache;

        // Subscribe to model update events
        _client.OnEvent<BehaviorModelUpdatedEvent>(HandleModelUpdated);
    }

    /// <summary>
    /// Called when characters enter the client's view.
    /// Requests all behavior models (types and variants) from server.
    /// </summary>
    public async Task OnCharactersEnteredViewAsync(IEnumerable<string> characterIds)
    {
        var request = new SyncModelsRequest
        {
            CharacterIds = characterIds.ToList(),
            KnownVersions = _modelCache.GetKnownVersions(characterIds)
        };

        var response = await _client.Behavior.SyncModelsAsync(request);

        foreach (var characterSet in response.Models)
        {
            // Process each behavior type
            foreach (var typeSet in characterSet.BehaviorTypes)
            {
                // Process each variant within this type
                foreach (var variant in typeSet.Variants)
                {
                    if (variant.ModelBytes != null)
                    {
                        // Deserialize and cache the model
                        var model = BehaviorModel.Deserialize(variant.ModelBytes);
                        _modelCache.UpdateModel(
                            characterSet.CharacterId,
                            typeSet.BehaviorType,
                            variant.VariantId,
                            model,
                            variant.Proficiency);
                    }
                }
            }
        }
    }

    private void HandleModelUpdated(BehaviorModelUpdatedEvent evt)
    {
        // If we have this character loaded, fetch the new model
        if (_modelCache.HasCharacter(evt.CharacterId))
        {
            _ = FetchUpdatedModelAsync(
                evt.CharacterId,
                evt.BehaviorType,
                evt.VariantId,  // Variant-aware update
                evt.ModelId);
        }
    }

    private async Task FetchUpdatedModelAsync(
        string characterId,
        string behaviorType,
        string variantId,
        Guid modelId)
    {
        var response = await _client.Behavior.GetModelAsync(new GetModelRequest
        {
            ModelId = modelId
        });

        if (response.ModelBytes != null)
        {
            var model = BehaviorModel.Deserialize(response.ModelBytes);
            _modelCache.UpdateModel(
                characterId,
                behaviorType,
                variantId,
                model,
                response.Proficiency);
        }
    }
}
```

### 6.4 Intent Channels and Multi-Model Coordination

When multiple behavior types run simultaneously (combat, movement, interaction), they can produce conflicting outputs. The **Intent Channel** architecture resolves this without needing a complex arbitration AI.

#### The Problem

```
Movement Model: "Walk to the tavern"
Combat Model:   "Attack the bandit blocking the path"
Interaction:    "Wave hello to the passing merchant"

Who wins? And what if they're partially compatible?
```

#### Intent Channels

Characters have multiple **channels** that can be controlled independently. Behavior models output to specific channels, not a single monolithic "action":

```
┌─────────────────────────────────────────────────────────────────────────┐
│                      INTENT CHANNEL ARCHITECTURE                        │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ LOCOMOTION CHANNEL                                               │   │
│  │ - Where should the character move?                               │   │
│  │ - Contributors: Movement, Combat (positioning), Interaction      │   │
│  │ - Blendable: Can interpolate destinations based on urgency       │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ ACTION CHANNEL                                                   │   │
│  │ - What should the character DO? (attack, block, use, talk)       │   │
│  │ - Contributors: Combat, Interaction                              │   │
│  │ - Exclusive: Can only do ONE action at a time                    │   │
│  │ - Resolved by: Highest urgency wins                              │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ ATTENTION CHANNEL                                                │   │
│  │ - Where should the character LOOK?                               │   │
│  │ - Contributors: All behavior types                               │   │
│  │ - Blendable: Can glance at threat while walking toward goal      │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │ STANCE CHANNEL                                                   │   │
│  │ - What posture/readiness level?                                  │   │
│  │ - Contributors: Combat, Movement                                 │   │
│  │ - Resolved by: Highest urgency wins                              │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

#### Uniform Output Structure

**Critical Design Principle**: All behavior types output to the **same channel structure**. The merger doesn't know or care which behavior type produced an output - it only sees urgency values per channel.

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    ALL MODELS → SAME CHANNELS                           │
│                                                                         │
│  Combat Model Output:                                                   │
│    locomotion: "close_distance"  urgency: 0.8                          │
│    action:     "attack"          urgency: 0.9                          │
│    attention:  enemy_42          urgency: 0.7                          │
│    stance:     "aggressive"      urgency: 0.8                          │
│                                                                         │
│  Movement Model Output:                                                 │
│    locomotion: "walk_to"         urgency: 0.6                          │
│    action:     (none)            urgency: 0.0  ← abstains from action  │
│    attention:  destination       urgency: 0.4                          │
│    stance:     "relaxed"         urgency: 0.3                          │
│                                                                         │
│  Idle Model Output:                                                     │
│    locomotion: "idle"            urgency: 0.1                          │
│    action:     "look_around"     urgency: 0.2                          │
│    attention:  random_point      urgency: 0.1                          │
│    stance:     "relaxed"         urgency: 0.1                          │
│                                                                         │
│  The merger sees THREE identical output structures, not "combat vs     │
│  movement vs idle". It simply picks highest urgency per channel.       │
└─────────────────────────────────────────────────────────────────────────┘
```

**Urgency = 0 means abstention**: When a behavior model has no opinion about a channel, it outputs `urgency=0`. This is different from "I don't support this channel" - it explicitly says "I have nothing to contribute here right now."

- **Movement model during combat**: Outputs `action_urgency=0` because it doesn't do combat actions
- **Combat model while idle**: Outputs `locomotion_urgency=0` because there's no combat positioning needed
- **Idle model always**: Provides low-urgency fallbacks so characters don't freeze if no other model has strong opinions

This uniform structure means:
1. **No special-casing**: The merger doesn't need to know "combat models produce actions, movement models don't"
2. **Any model can contribute to any channel**: A movement model could output high action urgency if needed (e.g., "stop to rest")
3. **Graceful composition**: Models naturally layer without explicit coordination logic

#### Channel Extensibility

New channels can be added without breaking existing models:

- **Missing output = urgency 0**: If a model doesn't output to a channel, the merger treats it as `urgency=0` (no contribution)
- **Old models keep working**: They simply don't compete for channels they don't know about
- **Graceful defaults**: A low-priority model (like idle) can provide baseline behavior for new channels that only gets overridden when something more urgent wants control

**Example**: Adding a `VOCALIZATIONS` channel later:

```
Old combat model:  (no vocalizations output)     → urgency = 0
New combat model:  vocalizations="battle_cry"    → urgency = 0.7
Idle model:        vocalizations="humming"       → urgency = 0.3

Merger: New combat wins (0.7 > 0.3 > 0)
        Old combat model still works, just silent
```

This allows the channel architecture to evolve without mass recompilation of existing behavior models.

#### Output Schema with Urgency

Each behavior model outputs intents with **urgency values** (0.0 to 1.0):

```yaml
# Combat model output schema
outputs:
  action_intent: string           # ACTION channel: "attack", "block", "flee"
  action_urgency: float           # How important is this action?
  locomotion_intent: string       # LOCOMOTION channel: "close_distance", "retreat"
  locomotion_target: vector3      # Where to move
  locomotion_urgency: float       # How important is this movement?
  attention_target: entity_id     # ATTENTION channel: who to look at
  attention_urgency: float
  stance: string                  # STANCE channel: "aggressive", "defensive"
  stance_urgency: float

# Movement model output schema
outputs:
  locomotion_intent: string       # "walk_to", "run_to", "follow"
  locomotion_target: vector3
  locomotion_urgency: float
  attention_target: entity_id     # Usually "forward" or "destination"
  attention_urgency: float
  stance: string                  # "relaxed", "alert"
  stance_urgency: float
```

#### Urgency Calculation Includes Personality

The key insight: **personality affects urgency, not arbitration**. A coward's combat model outputs different urgencies than a brave character's:

```yaml
# Combat model with personality-driven urgency
flows:
  evaluate_threat:
    - set:
        base_urgency: 0.7

    # Personality modifiers
    - cond:
        - when: "${personality.brave}"
          then:
            - set: { urgency_mod: 1.3 }      # Brave = eager to fight
        - when: "${personality.coward}"
          then:
            - set: { urgency_mod: 0.4 }      # Coward = reluctant
        - otherwise:
            - set: { urgency_mod: 1.0 }

    # Situational modifiers
    - cond:
        - when: "${protecting_loved_one}"
          then:
            - set: { urgency_mod: "${urgency_mod * 2.0}" }  # Even cowards fight for family
        - when: "${outnumbered > 3}"
          then:
            - set: { urgency_mod: "${urgency_mod * 0.5}" }  # Bad odds reduce eagerness

    - emit_intent:
        action: "${chosen_action}"
        action_urgency: "${clamp(base_urgency * urgency_mod, 0, 1)}"
```

A coward doesn't need a meta-model saying "ignore combat" - their combat model outputs "flee" with high urgency and "attack" with low urgency.

#### The Intent Merger

A lightweight, deterministic system combines outputs from all active models:

```csharp
/// <summary>
/// Merges intents from multiple behavior models into a unified output.
/// Uses urgency values for conflict resolution - no AI needed.
/// </summary>
public sealed class IntentMerger
{
    /// <summary>
    /// Merge intents from all active behavior models.
    /// </summary>
    public MergedIntent Merge(
        BehaviorOutput? combat,
        BehaviorOutput? movement,
        BehaviorOutput? interaction,
        BehaviorOutput? idle)
    {
        var merged = new MergedIntent();

        // ACTION CHANNEL: Exclusive - highest urgency wins
        merged.Action = SelectHighestUrgency(
            (combat?.ActionIntent, combat?.ActionUrgency ?? 0),
            (interaction?.ActionIntent, interaction?.ActionUrgency ?? 0),
            (idle?.ActionIntent, idle?.ActionUrgency ?? 0));

        // LOCOMOTION CHANNEL: Blendable based on urgency weights
        merged.Locomotion = BlendLocomotion(
            (combat?.LocomotionIntent, combat?.LocomotionTarget, combat?.LocomotionUrgency ?? 0),
            (movement?.LocomotionIntent, movement?.LocomotionTarget, movement?.LocomotionUrgency ?? 0),
            (interaction?.LocomotionIntent, interaction?.LocomotionTarget, interaction?.LocomotionUrgency ?? 0));

        // ATTENTION CHANNEL: Blendable with weights
        merged.Attention = BlendAttention(
            (combat?.AttentionTarget, combat?.AttentionUrgency ?? 0),
            (movement?.AttentionTarget, movement?.AttentionUrgency ?? 0),
            (interaction?.AttentionTarget, interaction?.AttentionUrgency ?? 0));

        // STANCE CHANNEL: Highest urgency wins
        merged.Stance = SelectHighestUrgency(
            (combat?.Stance, combat?.StanceUrgency ?? 0),
            (movement?.Stance, movement?.StanceUrgency ?? 0),
            (interaction?.Stance, interaction?.StanceUrgency ?? 0));

        return merged;
    }

    private T? SelectHighestUrgency<T>(params (T? value, float urgency)[] options)
    {
        return options
            .Where(o => o.value != null)
            .OrderByDescending(o => o.urgency)
            .Select(o => o.value)
            .FirstOrDefault();
    }

    private LocomotionIntent BlendLocomotion(
        params (string? intent, Vector3? target, float urgency)[] options)
    {
        var validOptions = options.Where(o => o.intent != null && o.urgency > 0).ToList();

        if (validOptions.Count == 0)
            return LocomotionIntent.None;

        if (validOptions.Count == 1)
            return new LocomotionIntent(validOptions[0].intent!, validOptions[0].target);

        // Blend based on urgency weights
        // The highest-urgency intent name wins, but target position is blended
        var totalUrgency = validOptions.Sum(o => o.urgency);
        var blendedTarget = Vector3.Zero;

        foreach (var opt in validOptions)
        {
            if (opt.target.HasValue)
            {
                var weight = opt.urgency / totalUrgency;
                blendedTarget += opt.target.Value * weight;
            }
        }

        var primaryIntent = validOptions.OrderByDescending(o => o.urgency).First();
        return new LocomotionIntent(primaryIntent.intent!, blendedTarget);
    }
}

/// <summary>
/// The merged result of all behavior model outputs.
/// </summary>
public readonly struct MergedIntent
{
    public string? Action { get; init; }
    public LocomotionIntent Locomotion { get; init; }
    public EntityId? AttentionTarget { get; init; }
    public string? Stance { get; init; }

#if DEBUG
    /// <summary>
    /// In debug builds, tracks which model won each channel for diagnostics.
    /// </summary>
    public ContributionTrace? Trace { get; init; }
#endif
}
```

#### Debugging Intent Merging

Understanding why a character chose a particular action requires visibility into the merge process. In debug builds, the system captures detailed traces of intent resolution.

**Contribution Trace Structure**:

```csharp
/// <summary>
/// Debug information showing how each channel was resolved.
/// Only available in debug builds.
/// </summary>
public sealed class ContributionTrace
{
    /// <summary>
    /// Per-channel breakdown of all contributions and resolution.
    /// </summary>
    public Dictionary<string, ChannelResolution> Channels { get; init; } = new();

    /// <summary>
    /// Timestamp for correlation with other debug systems.
    /// </summary>
    public long FrameNumber { get; init; }

    /// <summary>
    /// Character ID for filtering in multi-character scenarios.
    /// </summary>
    public string CharacterId { get; init; } = "";
}

/// <summary>
/// How a single channel was resolved.
/// </summary>
public sealed class ChannelResolution
{
    /// <summary>
    /// All contributions to this channel, sorted by urgency descending.
    /// </summary>
    public List<ChannelContribution> Contributions { get; init; } = new();

    /// <summary>
    /// The winning contribution (highest urgency, or blend result).
    /// </summary>
    public ChannelContribution Winner { get; init; }

    /// <summary>
    /// Resolution method used (exclusive vs blend).
    /// </summary>
    public ResolutionMethod Method { get; init; }
}

/// <summary>
/// A single model's contribution to a channel.
/// </summary>
public readonly struct ChannelContribution
{
    public string BehaviorType { get; init; }      // "combat", "movement", etc.
    public string VariantId { get; init; }         // "sword-and-shield", "standard", etc.
    public string IntentValue { get; init; }       // "attack", "walk_to", etc.
    public float Urgency { get; init; }            // 0.0 to 1.0
    public object? TargetData { get; init; }       // Vector3, EntityId, etc.
}

public enum ResolutionMethod
{
    /// <summary>Highest urgency wins completely.</summary>
    Exclusive,

    /// <summary>Values were blended based on urgency weights.</summary>
    Blended,

    /// <summary>No contributions with urgency > 0.</summary>
    NoContribution
}
```

**Debug Logging Example**:

```
[Frame 42871] Character: elena-123
  LOCOMOTION (Blended):
    → combat/sword-and-shield: "close_distance" urgency=0.80 target=(12.5, 0, 8.3)
      movement/standard:        "walk_to"        urgency=0.60 target=(45.0, 0, 22.1)
      idle/default:             "idle"           urgency=0.10
    RESULT: "close_distance" blended_target=(18.7, 0, 11.2)

  ACTION (Exclusive):
    → combat/sword-and-shield: "attack"         urgency=0.90
      idle/default:             "look_around"   urgency=0.20
    RESULT: "attack"

  ATTENTION (Blended):
    → combat/sword-and-shield: entity_42        urgency=0.70
      movement/standard:        destination     urgency=0.40
    RESULT: entity_42 (highest urgency, no blend for entity targets)

  STANCE (Exclusive):
    → combat/sword-and-shield: "aggressive"     urgency=0.80
      movement/standard:        "relaxed"       urgency=0.30
    RESULT: "aggressive"
```

**Integration with Game Tooling**:

```csharp
/// <summary>
/// In debug builds, writes merge traces to a ring buffer for inspection.
/// </summary>
#if DEBUG
public sealed class IntentMergeDebugger
{
    private readonly RingBuffer<ContributionTrace> _traceHistory;
    private readonly ILogger _logger;

    public IntentMergeDebugger(int historySize = 300)
    {
        // Keep ~5 seconds of history at 60fps
        _traceHistory = new RingBuffer<ContributionTrace>(historySize);
    }

    /// <summary>
    /// Record a merge result for later inspection.
    /// </summary>
    public void RecordMerge(string characterId, long frameNumber, ContributionTrace trace)
    {
        trace.CharacterId = characterId;
        trace.FrameNumber = frameNumber;
        _traceHistory.Add(trace);
    }

    /// <summary>
    /// Get recent traces for a character (for in-game debug overlay).
    /// </summary>
    public IEnumerable<ContributionTrace> GetRecentTraces(string characterId, int count = 10)
    {
        return _traceHistory
            .Where(t => t.CharacterId == characterId)
            .TakeLast(count);
    }

    /// <summary>
    /// Find frames where a specific channel had contested resolution.
    /// Useful for debugging "why didn't combat win?" scenarios.
    /// </summary>
    public IEnumerable<ContributionTrace> FindContestedFrames(
        string characterId,
        string channel,
        float minSecondPlaceUrgency = 0.5f)
    {
        return _traceHistory
            .Where(t => t.CharacterId == characterId)
            .Where(t => t.Channels.TryGetValue(channel, out var res)
                && res.Contributions.Count > 1
                && res.Contributions[1].Urgency >= minSecondPlaceUrgency);
    }

    /// <summary>
    /// Export traces to JSON for offline analysis.
    /// </summary>
    public void ExportToFile(string path)
    {
        var json = BannouJson.Serialize(_traceHistory.ToList());
        File.WriteAllText(path, json);
    }
}
#endif
```

**In-Game Debug Overlay Ideas**:

- **Floating urgency bars**: Above character's head, show current urgency per channel from each active model
- **Winner highlighting**: Color-code which model is "winning" each channel
- **History scrubbing**: Pause game and step through recent merge decisions frame-by-frame
- **"Why?" query**: Click on a character's action and see the full merge trace for that frame
- **Conflict alerts**: Visual indicator when multiple models have urgency > 0.7 on same exclusive channel

**Log Level Configuration**:

```yaml
# In development configuration
logging:
  intent_merge:
    enabled: true
    level: verbose        # trace, verbose, summary, off
    characters: ["*"]     # or specific IDs like ["elena-123", "guard-*"]
    channels: ["*"]       # or specific channels like ["action", "stance"]
    min_urgency: 0.0      # only log contributions above this threshold
```

#### Example: Walking to Tavern, Bandit Appears

| Frame | Movement Model | Combat Model | Merged Result |
|-------|---------------|--------------|---------------|
| 1. Peaceful | locomotion=tavern (0.6) | (inactive) | Walk to tavern, relaxed |
| 2. Spotted | locomotion=tavern (0.6) | attention=bandit (0.8) | Walk to tavern, **look at bandit**, alert |
| 3a. Brave | locomotion=tavern (0.6) | locomotion=close (0.85) | **Move toward bandit**, ready weapon |
| 3b. Coward | locomotion=tavern (0.6) | locomotion=flee (0.95) | **Flee** toward tavern |
| 4. Engaged | locomotion=tavern (0.4) | locomotion=circle (0.8), action=attack (0.9) | Circle enemy, attack |

Key insight: Personality affects **urgency values within models**, not arbitration logic. A coward's combat model outputs flee(0.95), a brave character's outputs attack(0.9).

#### Emotional State as Urgency Modifier

The Character Agent's emotional state modifies urgency calculations across all models:

```yaml
# Emotional state is an input to all models
inputs:
  emotional_state: enum(calm, alert, panicked, berserk, focused)

# Models adjust urgency based on emotional state
- cond:
    - when: "${emotional_state == 'panicked'}"
      then:
        # Panic: flee urgency boosted, attack urgency reduced
        - set: { flee_urgency_mod: 1.5, attack_urgency_mod: 0.3 }
    - when: "${emotional_state == 'berserk'}"
      then:
        # Berserk: attack urgency boosted, defense/flee reduced
        - set: { attack_urgency_mod: 1.5, flee_urgency_mod: 0.2, block_urgency_mod: 0.5 }
    - when: "${emotional_state == 'focused'}"
      then:
        # Focused: current primary goal urgency boosted
        - set: { primary_goal_urgency_mod: 1.3, distraction_urgency_mod: 0.5 }
```

This means emotional states naturally affect behavior without a meta-arbitration layer:
- **Panicked** characters flee because their flee urgency is very high
- **Berserk** characters attack recklessly because their attack urgency overwhelms defense
- **Focused** characters ignore distractions because side tasks have low urgency

#### Integration with Evaluation System

The `BehaviorEvaluationSystem` evaluates all active models and merges their outputs:

```csharp
public sealed class BehaviorEvaluationSystem : GameSystem
{
    private readonly BehaviorModelCache _modelCache;
    private readonly IntentMerger _intentMerger;
    private readonly IntentSystem _intentSystem;
    private readonly IVariantSelector _variantSelector;

    public override void Update(GameTime gameTime)
    {
        foreach (var character in GetAIControlledCharacters())
        {
            if (character.IsPlayerControlled)
                continue;

            // Evaluate all behavior types
            var combatOutput = EvaluateBehavior(character, "combat");
            var movementOutput = EvaluateBehavior(character, "movement");
            var interactionOutput = EvaluateBehavior(character, "interaction");
            var idleOutput = EvaluateBehavior(character, "idle");

            // Merge into unified intent
            var merged = _intentMerger.Merge(
                combatOutput,
                movementOutput,
                interactionOutput,
                idleOutput);

            // Process the merged intent
            _intentSystem.ProcessMergedIntent(character, merged);
        }
    }

    private BehaviorOutput? EvaluateBehavior(CharacterEntity character, string behaviorType)
    {
        // Select variant based on current state
        var variant = behaviorType switch
        {
            "combat" => _variantSelector.SelectCombatVariant(character),
            "movement" => _variantSelector.SelectMovementVariant(character),
            _ => "default"
        };

        var interpreter = _modelCache.GetInterpreterWithFallback(
            character.Id, behaviorType, variant);

        if (interpreter == null)
            return null;

        // Fill state and evaluate
        character.StateProvider.GetCurrentState(_inputBuffer, interpreter.InputSchema);
        interpreter.Evaluate(_inputBuffer, _outputBuffer);

        return BehaviorOutput.FromRawOutput(_outputBuffer, interpreter.OutputSchema);
    }
}
```

---

## 7. Connection to THE_DREAM

### 7.1 Auto-Mode Enabled by Local Runtime

THE_DREAM's "co-pilot" pattern works because character agents use the **same behavior model** for both QTE defaults and frame-by-frame combat:

**QTE Timeout Flow**: Event Brain presents options A, B, C → Character Agent queries local behavior model → Agent responds "I would do B" → If player times out, B executes automatically.

The agent's answer comes from the same model that runs every frame for non-cinematic combat. This is why auto-mode feels consistent with manual play.

### 7.2 Seamless Combat Escalation

Both basic and cinematic combat use ABML-authored behaviors, enabling smooth transitions:

1. **Basic Combat** (Local Runtime): Frame-by-frame behavior model evaluation → intents (attack, dodge, block)
2. **→ Escalation**: Regional Watcher detects interesting situation → spawns Event Agent
3. **Cinematic Combat** (Event Brain): Queries character agents (using same models) → QTE with agent-preferred default
4. **→ Resolution**: Event Agent terminates → returns to basic combat

No jarring transition because both layers use the same behavior vocabulary.

### 7.3 Event Brain Option Generation

THE_DREAM Section 7.4 describes the Event Brain querying character agents. With this architecture:

```csharp
// Event Brain asks: "Given this combat context, what can you do?"
var queryRequest = new QueryCombatOptionsRequest
{
    AgentId = participant.AgentId,
    CombatContext = currentContext,
    NearbyAffordances = environmentalObjects,
    TimePressure = TimePressure.High
};

// Character agent implementation uses the behavior model
public async Task<QueryCombatOptionsResponse> HandleQueryCombatOptions(
    QueryCombatOptionsRequest request,
    CancellationToken ct)
{
    // Load the character's combat behavior model
    var model = await _modelStore.GetAsync(request.AgentId, "combat", ct);
    var interpreter = new BehaviorModelInterpreter(model);

    // Build input state from combat context
    var inputState = BuildInputState(request.CombatContext);
    var outputState = new double[model.OutputSchema.Count];

    // Get all possible options by varying conditions
    var options = new List<CombatOption>();
    foreach (var scenario in GenerateScenarios(request.NearbyAffordances))
    {
        ApplyScenario(inputState, scenario);
        interpreter.Evaluate(inputState, outputState);

        var action = ExtractAction(outputState);
        if (!options.Any(o => o.Id == action.Id))
        {
            options.Add(new CombatOption
            {
                Id = action.Id,
                Capability = action.Action,
                Label = GenerateLabel(action, scenario),
                Score = action.Priority
            });
        }
    }

    // Get the preferred option (current state, no modifications)
    interpreter.Evaluate(BuildInputState(request.CombatContext), outputState);
    var preferred = ExtractAction(outputState);

    return new QueryCombatOptionsResponse
    {
        AvailableOptions = options,
        PreferredOption = preferred.Id,
        OptionPreferences = BuildPreferenceMap(options, _personality),
        Confidence = 0.85f
    };
}
```

### 7.4 Variant Selection in Combat Context

When equipment changes mid-combat, the variant switches immediately:

**Example**: Shield destroyed mid-combat
- Frame N: Using "sword-and-shield" variant (defensive, shield bash combos)
- Frame N+1: `VariantSelector` detects `HasShield=false` → switches to "one-handed"
- Frame N+2: Using "one-handed" variant (dodge instead of block, different combos)

No model reload delay - client had both variants cached. Event Brain also uses the current variant when querying character agents for cinematic options.

### 7.5 Learning Fighting Styles (Gameplay Integration)

The variant system integrates with gameplay - characters **learn** fighting styles:

| Style | Proficiency | Behavior |
|-------|-------------|----------|
| unarmed | 100% | Default fallback |
| sword-and-shield | 90% | Full technique access |
| dual-wield | 30% | Basic moves only, clumsy execution |
| two-handed | 0% (not learned) | Falls back to unarmed (swings like club) |

**Key mechanic**: Unlearned styles fall back to unarmed - incentive to find trainers.

**Storage Schema for Learned Styles**:

```yaml
# Stored per-character in lib-state
character_combat_styles:
  char-elena-123:
    styles:
      - variant_id: "unarmed"
        proficiency: 1.0
        model_id: "abc-111"       # Full unarmed model
        learned_from: "innate"
        learned_at: "2025-01-15"

      - variant_id: "sword-and-shield"
        proficiency: 0.9
        model_id: "abc-222"       # Advanced S&S techniques
        learned_from: "knight-mentor-45"
        learned_at: "2025-02-20"

      - variant_id: "dual-wield"
        proficiency: 0.3
        model_id: "abc-333"       # Basic dual-wield only
        learned_from: "rogue-trainer-78"
        learned_at: "2025-03-01"
        # Note: Different model than a master dual-wielder
        # This model has simpler decision tree
```

**Proficiency Affects Execution**:

Option A: **Model Complexity Scales with Proficiency**
- Low proficiency = simpler model with basic techniques
- High proficiency = complex model with advanced combos, counters, feints
- Training unlocks progressively better models

Option B: **Same Model, Proficiency Modifies Execution**
- All characters get the same "dual-wield" model
- Proficiency is an input to the model
- Low proficiency gates advanced techniques via conditions:
  ```yaml
  - when: "${proficiency > 0.7 && can_riposte}"
    then:
      - emit_intent: { action: riposte }
  - when: "${proficiency > 0.4 && stamina > 20}"
    then:
      - emit_intent: { action: combo_attack }
  - otherwise:
      - emit_intent: { action: basic_attack }
  ```

Option C: **Hybrid** (Recommended)
- Base model handles fundamentals
- Advanced models unlock with training
- Proficiency affects timing windows and success rates in game engine

**Training Gameplay Loop**:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         TRAINING LOOP                                   │
│                                                                         │
│  1. Character finds trainer NPC who knows a style                       │
│  2. Training session (could be mini-game, sparring, time-skip)          │
│  3. On success: Behavior Plugin compiles new/upgraded variant model     │
│  4. Model distributed to client                                         │
│  5. Character can now use that fighting style                           │
│                                                                         │
│  EXAMPLE:                                                               │
│  - Elena asks Rogue NPC to teach dual-wielding                          │
│  - Training session increases proficiency 0.3 → 0.5                     │
│  - Behavior Plugin compiles intermediate dual-wield model               │
│  - New techniques now available in combat                               │
│  - Character fights noticeably better with dual daggers                 │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

**Character Agents Know Their Limitations**:

When the Event Brain queries a character agent for combat options, the agent only offers what they actually know:

```csharp
public async Task<QueryCombatOptionsResponse> HandleQueryCombatOptions(
    QueryCombatOptionsRequest request,
    CancellationToken ct)
{
    // Get the character's KNOWN styles
    var knownStyles = await _styleStore.GetKnownStylesAsync(request.AgentId, ct);

    // Only query models for styles the character actually knows
    var currentVariant = DetermineCombatVariant(request.CombatContext.Equipment);

    if (!knownStyles.Any(s => s.VariantId == currentVariant))
    {
        // Character doesn't know this style!
        // Fall back to best known alternative
        currentVariant = FindBestFallback(knownStyles, request.CombatContext);
    }

    var style = knownStyles.First(s => s.VariantId == currentVariant);
    var model = await _modelStore.GetAsync(request.AgentId, "combat", currentVariant, ct);

    // Include proficiency in the response - affects option scoring
    // A character with 30% dual-wield proficiency won't prefer risky combos
    // ...
}
```

---

## 8. Implementation Phases

| Phase | Goal | Key Deliverables |
|-------|------|------------------|
| **1. Bytecode Foundation** | Basic compilation working | Bytecode format, `BehaviorModelInterpreter`, test harness |
| **2. Full Compilation** | Complete ABML → bytecode | Semantic analyzer, IR builder, optimization passes |
| **3. Distribution** | Models flow to clients | lib-state storage, `/behavior/models/sync`, delta sync |
| **4. Client Integration** | Stride executes models | `BehaviorModelCache`, `BehaviorEvaluationSystem`, variant selection |
| **5. Intent Channels** | Multi-model coordination | Channel definitions, `IntentMerger`, urgency-based resolution |
| **6. Agent Integration** | Cloud agents use models | `/behavior/agent/query-combat-options`, Event Brain integration |
| **7. Training System** | Characters learn styles | Proficiency tracking, model tier unlocking, trainer NPCs |
| **8. Polish** | Production-ready | Benchmarks, memory pooling, hot reload, debug tooling |

---

## 9. Design Decisions

| Question | Decision | Rationale |
|----------|----------|-----------|
| **Bytecode vs Native** | Bytecode first | Simpler, portable, sufficient performance; JIT later if needed |
| **Model Granularity** | Type/variant hierarchy | Combat/movement/interaction types with equipment-driven variants; fallback chain handles missing |
| **State Schema** | Dynamic per model | Different styles need different inputs; schema embedded in model |
| **Randomness** | RAND opcode with seed | Client provides frame-based seed for deterministic replays |

---

## 10. Success Criteria

### Technical Criteria

| Metric | Target |
|--------|--------|
| Model evaluation time | < 0.5ms per character |
| Model binary size | < 10KB typical, < 50KB complex |
| Sync latency (new character) | < 100ms including network |
| Update propagation time | < 500ms from change to client |
| Memory per cached model | < 20KB including interpreter |

### Experience Criteria

- AI characters feel responsive (no "thinking" pauses)
- Auto-mode behavior matches manual play patterns
- Seamless transition between basic and cinematic combat
- Character personality visible in combat choices
- No noticeable difference between local and remote characters
- Equipment changes result in immediate fighting style changes
- Characters without training fight noticeably worse with unfamiliar weapons
- Training progression feels meaningful (new techniques unlock)

### Development Criteria

- Designers author combat behaviors in ABML without C# knowledge
- Compilation errors have clear source locations
- Debug builds show bytecode execution trace
- Hot reload of behaviors in development mode
- Performance profiling tools for behavior optimization

---

*Document Status: PLANNING - Informing behavior plugin development priorities*

## Related Documents

- [THE_DREAM.md](./THE_DREAM.md) - Procedural cinematic exchanges vision
- [THE_DREAM_GAP_ANALYSIS.md](./THE_DREAM_GAP_ANALYSIS.md) - Gap analysis for THE_DREAM
- [ABML_V2_DESIGN_PROPOSAL.md](./ABML_V2_DESIGN_PROPOSAL.md) - ABML language specification
- [UPCOMING_-_BEHAVIOR_PLUGIN_V2.md](./UPCOMING_-_BEHAVIOR_PLUGIN_V2.md) - Behavior plugin design
