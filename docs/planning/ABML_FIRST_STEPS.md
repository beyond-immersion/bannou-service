# ABML First Steps - Implementation Guide

> **Status**: ✅ IMPLEMENTED
> **Created**: 2025-12-28
> **Updated**: 2025-12-28
> **Purpose**: Authoritative reference for ABML runtime implementation
> **Related**: [ABML_V2_DESIGN_PROPOSAL.md](./ABML_V2_DESIGN_PROPOSAL.md), [BEHAVIOR_PLUGIN_V2.md](./UPCOMING_-_BEHAVIOR_PLUGIN_V2.md)

This document provides the authoritative reference for the ABML runtime implementation, including architecture decisions, key patterns, and implementation details.

## Implementation Status

| Component | Status | Tests |
|-----------|--------|-------|
| Expression Parser & Compiler | ✅ Done | 100+ |
| Register-based VM | ✅ Done | 50+ |
| Variable Scope System | ✅ Done | 30+ |
| Control Flow Handlers | ✅ Done | 40+ |
| Error Handling (`on_error` + `_error_handled`) | ✅ Done | 37+ |
| Multi-Channel Execution | ✅ Done | 15+ |
| **Total** | **✅ Complete** | **414** |

---

## 1. Why ABML First?

The implementation order for Bannou's behavior system should be:

```
ABML Runtime (Phase 1)     <- START HERE
       │
       ▼
Behavior Plugin (Phase 2)  <- GOAP + Cognition
       │
       ▼
Actor Plugin (Phase 3)     <- Distributed runtime
       │
       ▼
THE DREAM (Phase 4)        <- Combat system
```

### 1.1 Dependency Analysis

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        DEPENDENCY GRAPH                                      │
│                                                                              │
│                         THE DREAM                                            │
│                    (Event Brain, Combat)                                     │
│                            │                                                 │
│              ┌─────────────┼─────────────┐                                  │
│              │             │             │                                  │
│              ▼             ▼             ▼                                  │
│     ┌─────────────┐ ┌─────────────┐ ┌─────────────┐                        │
│     │   ACTORS    │ │  BEHAVIOR   │ │    MAP      │                        │
│     │   PLUGIN    │ │   PLUGIN    │ │  SERVICE    │                        │
│     └──────┬──────┘ └──────┬──────┘ └─────────────┘                        │
│            │               │                                                │
│            │        ┌──────┴──────┐                                        │
│            │        │             │                                        │
│            │        ▼             ▼                                        │
│            │  ┌─────────┐  ┌─────────────┐                                 │
│            │  │  GOAP   │  │    ABML     │◄─── THE FOUNDATION              │
│            │  │ Planner │  │   RUNTIME   │                                 │
│            │  └────┬────┘  └──────┬──────┘                                 │
│            │       │              │                                        │
│            │       └──────┬───────┘                                        │
│            │              │                                                │
│            │              ▼                                                │
│            │    ┌──────────────────┐                                       │
│            └───►│  lib-state       │                                       │
│                 │  lib-messaging   │  (Already exist)                      │
│                 │  lib-mesh        │                                       │
│                 └──────────────────┘                                       │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Key insight**: Actors need ABML to define how they "act". An actor without a behavior is just a stateful mailbox - it doesn't *know what to do*. The actor is the runtime container; ABML is the soul.

### 1.2 What Can Be Tested in Isolation?

| Component | Can Test in Isolation? | External Dependencies |
|-----------|------------------------|----------------------|
| ABML Parser | Yes - pure transformation | YamlDotNet only |
| Expression Evaluator | Yes - pure computation | Parlot only |
| ABML Executor | Yes - with mock handlers | None |
| Multi-Channel Sync | Yes - pure coordination | None |
| GOAP Planner | Yes - pure algorithm | None |
| Cognition Pipeline | Partially - needs memory mocks | Memory service |
| Actor Runtime | Partially - needs state/messaging | lib-state, lib-messaging |

**ABML and GOAP are the only pieces that can be exhaustively tested with zero infrastructure.**

### 1.3 Reasons to Start with ABML

1. **It's the Core Innovation** - The DSL that defines how everything behaves. Combat choreography, NPC routines, cutscenes, dialogues - all expressed in ABML.

2. **Pure Unit Testing Paradise** - Can write hundreds of tests with zero infrastructure (no Docker, Redis, RabbitMQ).

3. **Validates the Language Design** - Building first forces confronting design questions before dependencies exist.

4. **Standalone Value** - A working ABML runtime enables cutscenes, dialogues, quests, tutorials even without actors.

---

## 2. VM Architecture Decision: Register-Based

### 2.1 The Core Tradeoff

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                     STACK-BASED vs REGISTER-BASED                           │
│                                                                              │
│  Expression: ${entity.health < 0.3 ? 'critical' : 'stable'}                │
│                                                                              │
│  STACK-BASED                          REGISTER-BASED                        │
│  ────────────                         ──────────────                        │
│                                                                              │
│  LOAD_VAR "entity"    ; [entity]      LOAD_VAR R0, "entity"                │
│  GET_PROP "health"    ; [health]      GET_PROP R1, R0, "health"            │
│  LOAD_CONST 0.3       ; [health,0.3]  LOAD_CONST R2, 0.3                   │
│  LT                   ; [bool]        LT R3, R1, R2                         │
│  JUMP_IF_FALSE L1                     JUMP_IF_FALSE R3, L1                  │
│  LOAD_CONST "critical"; [str]         LOAD_CONST R0, "critical"            │
│  RETURN                               RETURN R0                             │
│  L1:                                  L1:                                   │
│  LOAD_CONST "stable"  ; [str]         LOAD_CONST R0, "stable"              │
│  RETURN                               RETURN R0                             │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 2.2 ABML Expression Characteristics

```yaml
# Pattern 1: Deep path navigation (very common)
value: "${npc.inventory.items[0].properties.weight}"

# Pattern 2: Null-safe chains (critical for game state)
greeting: "${npc?.relationship[player.id]?.title ?? 'stranger'}"

# Pattern 3: Multi-value comparison
eligible: "${level >= 10 && reputation > 0.5 && !is_banned}"

# Pattern 4: Arithmetic with reuse
damage: "${base * modifier * modifier}"  # modifier used twice

# Pattern 5: Function calls with multiple args
in_range: "${distance_to(target) < weapon.range}"

# Pattern 6: Complex combat formula
score: "${(capability.damage * opportunity.exposure) + (luck_roll * variance)}"
```

**Key observations:**
- Lots of property lookups - more loads than arithmetic
- Null-safety is critical - `?.` and `??` are everywhere in game state
- Short-circuit evaluation needed - `&&`, `||`, ternary
- Occasional value reuse - same variable used multiple times
- Typical size: 3-15 operations - not huge programs

### 2.3 Why Register-Based Wins for ABML

#### Null-Safety is Clean

Stack-based null-safety is a nightmare:

```
# ${a?.b?.c ?? default} - STACK-BASED

LOAD_VAR "a"           ; [a]
DUP                    ; [a, a]
JUMP_IF_NULL L_check1  ; [a]
GET_PROP "b"           ; [b]
DUP                    ; [b, b]
JUMP_IF_NULL L_check2  ; [b]
GET_PROP "c"           ; [c]
JUMP L_done
L_check1:
POP                    ; []  <- Need to clean up the DUP'd value
JUMP L_default
L_check2:
POP                    ; []  <- Need to clean up the DUP'd value
L_default:
LOAD_VAR "default"     ; [default]
L_done:
```

Every conditional path needs stack depth management. Bugs here cause stack corruption.

Register-based is elegant:

```
# ${a?.b?.c ?? default} - REGISTER-BASED

LOAD_VAR R0, "a"
JUMP_IF_NULL R0, L_default
GET_PROP R1, R0, "b"
JUMP_IF_NULL R1, L_default
GET_PROP R2, R1, "c"
JUMP_IF_NULL R2, L_default
RETURN R2
L_default:
LOAD_VAR R0, "default"
RETURN R0
```

No stack management. Each register holds its value. Jump wherever you want.

#### Value Reuse is Free

```
# ${a * b + a * c} - REGISTER-BASED

LOAD_VAR R0, "a"        ; Load 'a' once
LOAD_VAR R1, "b"
MUL R2, R0, R1          ; a * b
LOAD_VAR R3, "c"
MUL R4, R0, R3          ; a * c (reuse R0!)
ADD R5, R2, R4
```

Stack-based would need to load 'a' twice or use DUP/SWAP gymnastics.

#### Debugging Shows Named State

```
# Register-based debugging output:
"Register state at instruction 5:"
"  R0 = entity (Character)"
"  R1 = health (0.25)"
"  R2 = threshold (0.3)"
"  R3 = is_critical (true)"

# Stack-based debugging output:
"Stack at instruction 5: [0.3, true, 'hello']"
# What do these values mean? Which expression parts?
```

### 2.4 Decision Summary

| Factor | Stack | Register | Winner |
|--------|-------|----------|--------|
| Null-safety patterns | Awkward stack management | Clean conditional jumps | **Register** |
| Value reuse | DUP/SWAP gymnastics | Just reference the register | **Register** |
| Debugging | "Stack: [0.3, true, ???]" | "R2=health (0.25)" | **Register** |
| Future optimization | Limited | CSE, constant prop, etc. | **Register** |
| Implementation | Simpler | Slightly more complex | Stack |
| Instruction size | Smaller | Larger (cached anyway) | Stack |

**Recommendation: Register-based** - The complexity cost is minimal for ABML's small expressions, and the benefits for null-safety and debugging are significant.

---

## 3. Instruction Set Design

### 3.1 Instruction Encoding

```csharp
/// <summary>
/// ABML bytecode instruction.
/// 32-bit encoding: [opcode:8][A:8][B:8][C:8]
/// </summary>
public readonly struct Instruction
{
    private readonly uint _packed;

    public OpCode Op => (OpCode)(_packed >> 24);
    public byte A => (byte)((_packed >> 16) & 0xFF);  // Usually destination
    public byte B => (byte)((_packed >> 8) & 0xFF);   // Source 1 or constant index
    public byte C => (byte)(_packed & 0xFF);          // Source 2 or constant index

    public Instruction(OpCode op, byte a, byte b = 0, byte c = 0)
    {
        _packed = ((uint)op << 24) | ((uint)a << 16) | ((uint)b << 8) | c;
    }
}
```

### 3.2 OpCode Reference

```csharp
public enum OpCode : byte
{
    // ═══════════════════════════════════════════════════════════════════════
    // LOADS - Move values into registers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>R[A] = constants[B]</summary>
    LoadConst,

    /// <summary>R[A] = scope.GetVariable(constants[B])</summary>
    LoadVar,

    /// <summary>R[A] = null</summary>
    LoadNull,

    /// <summary>R[A] = true</summary>
    LoadTrue,

    /// <summary>R[A] = false</summary>
    LoadFalse,

    // ═══════════════════════════════════════════════════════════════════════
    // PROPERTY ACCESS - Navigate object graphs
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>R[A] = R[B].GetProperty(constants[C])</summary>
    GetProp,

    /// <summary>R[A] = R[B]?.GetProperty(constants[C]) - null if R[B] is null</summary>
    GetPropSafe,

    /// <summary>R[A] = R[B][R[C]]</summary>
    GetIndex,

    /// <summary>R[A] = R[B]?[R[C]] - null if R[B] is null</summary>
    GetIndexSafe,

    // ═══════════════════════════════════════════════════════════════════════
    // ARITHMETIC - Math operations
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>R[A] = R[B] + R[C]</summary>
    Add,

    /// <summary>R[A] = R[B] - R[C]</summary>
    Sub,

    /// <summary>R[A] = R[B] * R[C]</summary>
    Mul,

    /// <summary>R[A] = R[B] / R[C]</summary>
    Div,

    /// <summary>R[A] = R[B] % R[C]</summary>
    Mod,

    /// <summary>R[A] = -R[B]</summary>
    Neg,

    // ═══════════════════════════════════════════════════════════════════════
    // COMPARISON - Produce boolean results
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>R[A] = R[B] == R[C]</summary>
    Eq,

    /// <summary>R[A] = R[B] != R[C]</summary>
    Ne,

    /// <summary>R[A] = R[B] &lt; R[C]</summary>
    Lt,

    /// <summary>R[A] = R[B] &lt;= R[C]</summary>
    Le,

    /// <summary>R[A] = R[B] &gt; R[C]</summary>
    Gt,

    /// <summary>R[A] = R[B] &gt;= R[C]</summary>
    Ge,

    // ═══════════════════════════════════════════════════════════════════════
    // LOGICAL - Boolean operations
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>R[A] = !R[B]</summary>
    Not,

    /// <summary>R[A] = R[B] &amp;&amp; R[C] (non-short-circuit, use jumps for short-circuit)</summary>
    And,

    /// <summary>R[A] = R[B] || R[C] (non-short-circuit)</summary>
    Or,

    // ═══════════════════════════════════════════════════════════════════════
    // CONTROL FLOW - Conditional execution
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>PC = (B &lt;&lt; 8) | C (16-bit jump offset)</summary>
    Jump,

    /// <summary>if R[A] is truthy then PC = (B &lt;&lt; 8) | C</summary>
    JumpIfTrue,

    /// <summary>if R[A] is falsy then PC = (B &lt;&lt; 8) | C</summary>
    JumpIfFalse,

    /// <summary>if R[A] == null then PC = (B &lt;&lt; 8) | C</summary>
    JumpIfNull,

    /// <summary>if R[A] != null then PC = (B &lt;&lt; 8) | C</summary>
    JumpIfNotNull,

    // ═══════════════════════════════════════════════════════════════════════
    // FUNCTIONS - Built-in function calls
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>R[A] = Call(constants[B], args starting at R[C], arg count in next byte)</summary>
    Call,

    // ═══════════════════════════════════════════════════════════════════════
    // NULL HANDLING - Coalescing operations
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>R[A] = R[B] ?? R[C]</summary>
    Coalesce,

    // ═══════════════════════════════════════════════════════════════════════
    // STRING - String operations
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>R[A] = R[B] in R[C] (membership test)</summary>
    In,

    /// <summary>R[A] = concat(R[B], R[C])</summary>
    Concat,

    // ═══════════════════════════════════════════════════════════════════════
    // RESULT - Return from expression
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Return R[A] as expression result</summary>
    Return,
}
```

### 3.3 Additional OpCodes (Implemented)

The following opcodes were added during implementation:

```csharp
// Boolean conversion
/// <summary>R[A] = IsTrue(R[B]) - explicit boolean conversion</summary>
ToBool,

// Function calling
/// <summary>Set arg count for next Call instruction</summary>
CallArgs,

// Range allocation for function arguments
/// <summary>Allocate contiguous registers for function args</summary>
// Uses RegisterAllocator.AllocateRange(count)
```

### 3.4 Compiled Expression Structure

```csharp
/// <summary>
/// A compiled ABML expression ready for execution.
/// Immutable and thread-safe - can be cached and shared.
/// </summary>
public sealed class CompiledExpression
{
    /// <summary>The bytecode instructions.</summary>
    public required Instruction[] Code { get; init; }

    /// <summary>Constant pool (strings, numbers, property names).</summary>
    public required object[] Constants { get; init; }

    /// <summary>Number of registers needed for execution.</summary>
    public required int RegisterCount { get; init; }

    /// <summary>Original expression text (for debugging).</summary>
    public required string SourceText { get; init; }

    /// <summary>Expected return type (for validation).</summary>
    public Type? ExpectedType { get; init; }
}
```

---

## 4. Variable Scope System

### 4.1 Scope Semantics

The variable scope system provides three distinct write modes to prevent accidental variable clobbering:

```csharp
public interface IVariableScope
{
    /// <summary>Get a variable value, searching up scope chain.</summary>
    object? GetValue(string name);

    /// <summary>Set a variable, searching up scope chain. Creates locally if not found.</summary>
    void SetValue(string name, object? value);

    /// <summary>Set a variable in THIS scope only, shadowing any parent.</summary>
    void SetLocalValue(string name, object? value);

    /// <summary>Set a variable in the root (document) scope.</summary>
    void SetGlobalValue(string name, object? value);

    /// <summary>Create a child scope.</summary>
    IVariableScope CreateChild();
}
```

### 4.2 Scope Behavior

| Operation | Behavior |
|-----------|----------|
| `SetValue` | Search up chain, update if found, else create locally |
| `SetLocalValue` | Always create/update in current scope (shadows parent) |
| `SetGlobalValue` | Always write to root scope |
| `GetValue` | Search up chain, return first match or null |

### 4.3 ForEach Loop Variable Isolation

Loop variables use `SetLocalValue` to prevent clobbering outer scope:

```yaml
# Outer 'i' is preserved after loop
- set: { variable: i, value: 100 }
- for_each:
    variable: i           # Uses SetLocalValue, shadows outer 'i'
    collection: "${items}"
    do:
      - log: "${i}"       # Logs item values
- log: "${i}"             # Still 100, not clobbered
```

### 4.4 Call Handler Scope Isolation

Called flows execute in a child scope:

```csharp
// CallHandler creates isolated scope
var callScope = currentScope.CreateChild();
context.CallStack.Push(flowName, callScope);
```

This prevents called flows from accidentally modifying caller's variables.

---

## 5. Error Handling System

### 5.1 Three-Level Error Chain

Errors are handled through a hierarchical chain:

```
Action-level on_error → Flow-level on_error → Document-level on_error
```

### 5.2 Error Handler Syntax

```yaml
version: "2.0"
metadata:
  id: error_handling_example

# Document-level error handler
on_error: handle_fatal_error

flows:
  start:
    # Flow-level error handler
    on_error:
      - log: { message: "Flow error: ${_error.message}", level: error }
      - set: { variable: _error_handled, value: "${true}" }

    actions:
      # Action-level error handling
      - query_service:
          target: economy
          on_error:
            - log: "Service unavailable, using cached data"
            - set: { variable: _error_handled, value: "${true}" }

  handle_fatal_error:
    actions:
      - log: { message: "Fatal: ${_error.message}", level: error }
```

### 5.3 The `_error_handled` Opt-In Pattern

By default, error handling **stops** flow execution. To continue to the next action (try/catch semantics), explicitly set `_error_handled = true`:

| Scenario | `_error_handled` | Result |
|----------|------------------|--------|
| Default (not set) | N/A | Flow completes successfully but **stops** |
| Set to `true` | `${true}` | Flow **continues** to next action |
| Set to `false` | `${false}` | Flow completes successfully but **stops** |
| Set to truthy string | `"yes"` | Flow completes successfully but **stops** |

**Graceful Degradation Pattern (THE DREAM):**

```yaml
flows:
  start:
    actions:
      - query_environment:
          bounds: "${combat_bounds}"
          result_variable: affordances
      - for_each:
          variable: affordance
          collection: "${affordances}"
          do:
            - process: "${affordance}"
    on_error:
      - log: "Query failed, using cached data"
      - set: { variable: affordances, value: "${cached_affordances}" }
      - set: { variable: _error_handled, value: "${true}" }  # Continue!
```

### 5.4 Error Context Variable

When an error occurs, `_error` is set with error details:

```yaml
on_error:
  - log: { message: "Error in ${_error.flow}: ${_error.message}" }
  - log: { message: "Failed action: ${_error.action}" }
```

---

## 6. Multi-Channel Execution

### 6.1 Cooperative Round-Robin Scheduling

Channels execute cooperatively on a single thread with deterministic interleaving:

```
Tick 1:  camera[0] → actors[0] → effects[0]
Tick 2:  camera[1] → actors[1] → effects[1]
Tick 3:  camera[2] → actors[WAIT] → effects[2]  (actors waiting)
Tick 4:  camera[3] → effects[3]                  (actors still waiting)
Tick 5:  camera[EMIT] → actors[2] → effects[4]  (actors wake)
```

### 6.2 Sync Points: emit and wait_for

```yaml
channels:
  camera:
    - log: "Camera: Establishing shot"
    - wait_for: @actors.in_position
    - log: "Camera: Crane up"
    - emit: crane_complete

  actors:
    - log: "Actors: Walking to marks"
    - emit: in_position
    - wait_for: @camera.crane_complete
    - log: "Actors: Starting dialogue"
```

### 6.3 Wait Modes

```yaml
# Wait for all signals (default)
- wait_for:
    signals:
      - @channel_a.signal1
      - @channel_b.signal2
    mode: all_of

# Wait for any signal
- wait_for:
    signals:
      - @channel_a.done
      - @channel_b.done
    mode: any_of
```

### 6.4 Deadlock Detection

The scheduler detects when all active channels are waiting for signals that will never arrive:

```yaml
# DEADLOCK - detected at runtime
channels:
  a:
    - wait_for: @b.signal  # A waits for B
  b:
    - wait_for: @a.signal  # B waits for A
```

### 6.5 Channel Scope Isolation

Each channel has its own scope (child of document scope). Channels can:
- Read from document scope
- Write locally without affecting other channels
- Communicate via sync points, not shared variables

---

## 7. VM Implementation

### 7.1 Core VM Structure

```csharp
/// <summary>
/// Register-based VM for ABML expression evaluation.
/// </summary>
public sealed class ExpressionVM
{
    private const int MaxRegisters = 256;

    // Register file - reused across evaluations (not thread-safe, use per-thread or pool)
    private readonly object?[] _registers = new object?[MaxRegisters];

    private readonly IFunctionRegistry _functions;
    private readonly ILogger<ExpressionVM> _logger;

    public ExpressionVM(IFunctionRegistry functions, ILogger<ExpressionVM> logger)
    {
        _functions = functions;
        _logger = logger;
    }

    /// <summary>
    /// Execute a compiled expression with the given variable scope.
    /// </summary>
    public object? Execute(CompiledExpression expr, IVariableScope scope)
    {
        var code = expr.Code;
        var constants = expr.Constants;
        var pc = 0;

        // Clear registers that will be used
        Array.Clear(_registers, 0, expr.RegisterCount);

        while (pc < code.Length)
        {
            var instr = code[pc];

            switch (instr.Op)
            {
                // ═══════════════════════════════════════════════════════════
                // LOADS
                // ═══════════════════════════════════════════════════════════

                case OpCode.LoadConst:
                    _registers[instr.A] = constants[instr.B];
                    pc++;
                    break;

                case OpCode.LoadVar:
                    _registers[instr.A] = scope.GetValue((string)constants[instr.B]);
                    pc++;
                    break;

                case OpCode.LoadNull:
                    _registers[instr.A] = null;
                    pc++;
                    break;

                case OpCode.LoadTrue:
                    _registers[instr.A] = true;
                    pc++;
                    break;

                case OpCode.LoadFalse:
                    _registers[instr.A] = false;
                    pc++;
                    break;

                // ═══════════════════════════════════════════════════════════
                // PROPERTY ACCESS
                // ═══════════════════════════════════════════════════════════

                case OpCode.GetProp:
                    _registers[instr.A] = GetProperty(
                        _registers[instr.B] ?? throw new NullReferenceException(
                            $"Cannot access property '{constants[instr.C]}' on null"),
                        (string)constants[instr.C]);
                    pc++;
                    break;

                case OpCode.GetPropSafe:
                    var obj = _registers[instr.B];
                    _registers[instr.A] = obj == null ? null : GetProperty(obj, (string)constants[instr.C]);
                    pc++;
                    break;

                case OpCode.GetIndex:
                    _registers[instr.A] = GetIndex(
                        _registers[instr.B] ?? throw new NullReferenceException("Cannot index null"),
                        _registers[instr.C]);
                    pc++;
                    break;

                case OpCode.GetIndexSafe:
                    var indexTarget = _registers[instr.B];
                    _registers[instr.A] = indexTarget == null ? null : GetIndex(indexTarget, _registers[instr.C]);
                    pc++;
                    break;

                // ═══════════════════════════════════════════════════════════
                // ARITHMETIC
                // ═══════════════════════════════════════════════════════════

                case OpCode.Add:
                    _registers[instr.A] = Add(_registers[instr.B], _registers[instr.C]);
                    pc++;
                    break;

                case OpCode.Sub:
                    _registers[instr.A] = Subtract(_registers[instr.B], _registers[instr.C]);
                    pc++;
                    break;

                case OpCode.Mul:
                    _registers[instr.A] = Multiply(_registers[instr.B], _registers[instr.C]);
                    pc++;
                    break;

                case OpCode.Div:
                    _registers[instr.A] = Divide(_registers[instr.B], _registers[instr.C]);
                    pc++;
                    break;

                case OpCode.Mod:
                    _registers[instr.A] = Modulo(_registers[instr.B], _registers[instr.C]);
                    pc++;
                    break;

                case OpCode.Neg:
                    _registers[instr.A] = Negate(_registers[instr.B]);
                    pc++;
                    break;

                // ═══════════════════════════════════════════════════════════
                // COMPARISON
                // ═══════════════════════════════════════════════════════════

                case OpCode.Eq:
                    _registers[instr.A] = Equals(_registers[instr.B], _registers[instr.C]);
                    pc++;
                    break;

                case OpCode.Ne:
                    _registers[instr.A] = !Equals(_registers[instr.B], _registers[instr.C]);
                    pc++;
                    break;

                case OpCode.Lt:
                    _registers[instr.A] = Compare(_registers[instr.B], _registers[instr.C]) < 0;
                    pc++;
                    break;

                case OpCode.Le:
                    _registers[instr.A] = Compare(_registers[instr.B], _registers[instr.C]) <= 0;
                    pc++;
                    break;

                case OpCode.Gt:
                    _registers[instr.A] = Compare(_registers[instr.B], _registers[instr.C]) > 0;
                    pc++;
                    break;

                case OpCode.Ge:
                    _registers[instr.A] = Compare(_registers[instr.B], _registers[instr.C]) >= 0;
                    pc++;
                    break;

                // ═══════════════════════════════════════════════════════════
                // LOGICAL
                // ═══════════════════════════════════════════════════════════

                case OpCode.Not:
                    _registers[instr.A] = !IsTrue(_registers[instr.B]);
                    pc++;
                    break;

                case OpCode.And:
                    _registers[instr.A] = IsTrue(_registers[instr.B]) && IsTrue(_registers[instr.C]);
                    pc++;
                    break;

                case OpCode.Or:
                    _registers[instr.A] = IsTrue(_registers[instr.B]) || IsTrue(_registers[instr.C]);
                    pc++;
                    break;

                // ═══════════════════════════════════════════════════════════
                // CONTROL FLOW
                // ═══════════════════════════════════════════════════════════

                case OpCode.Jump:
                    pc = (instr.B << 8) | instr.C;
                    break;

                case OpCode.JumpIfTrue:
                    pc = IsTrue(_registers[instr.A]) ? (instr.B << 8) | instr.C : pc + 1;
                    break;

                case OpCode.JumpIfFalse:
                    pc = !IsTrue(_registers[instr.A]) ? (instr.B << 8) | instr.C : pc + 1;
                    break;

                case OpCode.JumpIfNull:
                    pc = _registers[instr.A] == null ? (instr.B << 8) | instr.C : pc + 1;
                    break;

                case OpCode.JumpIfNotNull:
                    pc = _registers[instr.A] != null ? (instr.B << 8) | instr.C : pc + 1;
                    break;

                // ═══════════════════════════════════════════════════════════
                // FUNCTIONS
                // ═══════════════════════════════════════════════════════════

                case OpCode.Call:
                    var funcName = (string)constants[instr.B];
                    var argStart = instr.C;
                    var argCount = code[++pc].A;  // Next instruction holds arg count

                    var args = new object?[argCount];
                    for (int i = 0; i < argCount; i++)
                    {
                        args[i] = _registers[argStart + i];
                    }

                    _registers[instr.A] = _functions.Call(funcName, args);
                    pc++;
                    break;

                // ═══════════════════════════════════════════════════════════
                // NULL HANDLING
                // ═══════════════════════════════════════════════════════════

                case OpCode.Coalesce:
                    _registers[instr.A] = _registers[instr.B] ?? _registers[instr.C];
                    pc++;
                    break;

                // ═══════════════════════════════════════════════════════════
                // STRING
                // ═══════════════════════════════════════════════════════════

                case OpCode.In:
                    _registers[instr.A] = Contains(_registers[instr.C], _registers[instr.B]);
                    pc++;
                    break;

                case OpCode.Concat:
                    _registers[instr.A] = Concat(_registers[instr.B], _registers[instr.C]);
                    pc++;
                    break;

                // ═══════════════════════════════════════════════════════════
                // RESULT
                // ═══════════════════════════════════════════════════════════

                case OpCode.Return:
                    return _registers[instr.A];

                default:
                    throw new InvalidOperationException($"Unknown opcode: {instr.Op}");
            }
        }

        throw new InvalidOperationException("Expression did not return a value");
    }

    // ... helper methods below
}
```

### 7.2 Type Coercion Helpers

```csharp
// ═══════════════════════════════════════════════════════════════════════════
// Helper methods for type coercion and operations
// ═══════════════════════════════════════════════════════════════════════════

private static object? GetProperty(object target, string name)
{
    return target switch
    {
        IDictionary<string, object?> dict => dict.TryGetValue(name, out var v) ? v : null,
        _ => target.GetType().GetProperty(name)?.GetValue(target)
    };
}

private static object? GetIndex(object target, object? index)
{
    return target switch
    {
        IList list when index is int i => i >= 0 && i < list.Count ? list[i] : null,
        IDictionary<string, object?> dict when index is string s => dict.TryGetValue(s, out var v) ? v : null,
        _ => throw new InvalidOperationException($"Cannot index {target.GetType().Name} with {index?.GetType().Name ?? "null"}")
    };
}

private static bool IsTrue(object? value)
{
    return value switch
    {
        null => false,
        bool b => b,
        int i => i != 0,
        float f => f != 0f,
        double d => d != 0d,
        string s => s.Length > 0,
        ICollection c => c.Count > 0,
        _ => true
    };
}

private static object Add(object? left, object? right)
{
    return (left, right) switch
    {
        (int l, int r) => l + r,
        (float l, float r) => l + r,
        (double l, double r) => l + r,
        (int l, float r) => l + r,
        (float l, int r) => l + r,
        (string l, _) => l + right?.ToString(),
        (_, string r) => left?.ToString() + r,
        _ => Convert.ToDouble(left) + Convert.ToDouble(right)
    };
}

private static object Subtract(object? left, object? right)
{
    return (left, right) switch
    {
        (int l, int r) => l - r,
        (float l, float r) => l - r,
        (double l, double r) => l - r,
        _ => Convert.ToDouble(left) - Convert.ToDouble(right)
    };
}

private static object Multiply(object? left, object? right)
{
    return (left, right) switch
    {
        (int l, int r) => l * r,
        (float l, float r) => l * r,
        (double l, double r) => l * r,
        _ => Convert.ToDouble(left) * Convert.ToDouble(right)
    };
}

private static object Divide(object? left, object? right)
{
    return (left, right) switch
    {
        (int l, int r) => l / r,
        (float l, float r) => l / r,
        (double l, double r) => l / r,
        _ => Convert.ToDouble(left) / Convert.ToDouble(right)
    };
}

private static object Modulo(object? left, object? right)
{
    return (left, right) switch
    {
        (int l, int r) => l % r,
        _ => Convert.ToDouble(left) % Convert.ToDouble(right)
    };
}

private static object Negate(object? value)
{
    return value switch
    {
        int i => -i,
        float f => -f,
        double d => -d,
        _ => -Convert.ToDouble(value)
    };
}

private static int Compare(object? left, object? right)
{
    return (left, right) switch
    {
        (null, null) => 0,
        (null, _) => -1,
        (_, null) => 1,
        (IComparable l, _) => l.CompareTo(Convert.ChangeType(right, l.GetType())),
        _ => throw new InvalidOperationException($"Cannot compare {left.GetType().Name} and {right?.GetType().Name}")
    };
}

private static bool Contains(object? collection, object? item)
{
    return collection switch
    {
        string s when item is string sub => s.Contains(sub),
        IList list => list.Contains(item),
        IDictionary<string, object?> dict when item is string key => dict.ContainsKey(key),
        _ => false
    };
}

private static string Concat(object? left, object? right)
{
    return (left?.ToString() ?? "") + (right?.ToString() ?? "");
}
```

### 7.3 Debugging Support

```csharp
/// <summary>
/// Execute with register state tracing for debugging.
/// </summary>
public object? ExecuteWithTrace(CompiledExpression expr, IVariableScope scope, ILogger logger)
{
    var code = expr.Code;
    var constants = expr.Constants;
    var pc = 0;

    Array.Clear(_registers, 0, expr.RegisterCount);

    logger.LogDebug("Executing: {Expression}", expr.SourceText);
    logger.LogDebug("Constants: {Constants}", string.Join(", ", constants));

    while (pc < code.Length)
    {
        var instr = code[pc];
        var beforePc = pc;

        // Execute instruction (same switch as above)
        // ...

        // Log state after instruction
        logger.LogDebug(
            "  [{PC}] {Op,-15} R{A}={Value}",
            beforePc,
            instr.Op,
            instr.A,
            FormatValue(_registers[instr.A]));
    }

    // ...
}

private static string FormatValue(object? value)
{
    return value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        bool b => b ? "true" : "false",
        _ => value.ToString() ?? "null"
    };
}
```

**Example trace output:**

```
Executing: ${entity?.health < 0.3 ? 'critical' : 'stable'}
Constants: entity, health, 0.3, critical, stable
  [0] LoadVar         R0=Character { Id="npc_123" }
  [1] JumpIfNull      R0=Character { Id="npc_123" }
  [2] GetProp         R1=0.25
  [3] LoadConst       R2=0.3
  [4] Lt              R3=true
  [5] JumpIfFalse     R3=true
  [6] LoadConst       R0="critical"
  [7] Return          R0="critical"
```

---

## 8. Compilation Example

### 8.1 Expression to Bytecode

Expression: `${entity?.health < 0.3 ? 'critical' : 'stable'}`

```
Constants:
  0: "entity"
  1: "health"
  2: 0.3
  3: "critical"
  4: "stable"

Code:
  0: LoadVar R0, 0           ; R0 = scope["entity"]
  1: JumpIfNull R0, 8        ; if R0 == null goto 8
  2: GetProp R1, R0, 1       ; R1 = R0.health
  3: LoadConst R2, 2         ; R2 = 0.3
  4: Lt R3, R1, R2           ; R3 = R1 < R2
  5: JumpIfFalse R3, 8       ; if !R3 goto 8
  6: LoadConst R0, 3         ; R0 = "critical"
  7: Return R0
  8: LoadConst R0, 4         ; R0 = "stable"
  9: Return R0

RegisterCount: 4
```

### 8.2 Code Representation

```csharp
var compiled = new CompiledExpression
{
    Constants = new object[] { "entity", "health", 0.3, "critical", "stable" },
    Code = new Instruction[]
    {
        new(OpCode.LoadVar, 0, 0),        // R0 = entity
        new(OpCode.JumpIfNull, 0, 0, 8),  // if null goto 8
        new(OpCode.GetProp, 1, 0, 1),     // R1 = R0.health
        new(OpCode.LoadConst, 2, 2),      // R2 = 0.3
        new(OpCode.Lt, 3, 1, 2),          // R3 = R1 < R2
        new(OpCode.JumpIfFalse, 3, 0, 8), // if !R3 goto 8
        new(OpCode.LoadConst, 0, 3),      // R0 = "critical"
        new(OpCode.Return, 0),            // return R0
        new(OpCode.LoadConst, 0, 4),      // R0 = "stable"
        new(OpCode.Return, 0),            // return R0
    },
    RegisterCount = 4,
    SourceText = "${entity?.health < 0.3 ? 'critical' : 'stable'}"
};
```

---

## 9. Implementation Summary

### Components Implemented

```
bannou-service/Abml/
├── Compiler/
│   ├── ExpressionCompiler.cs      # AST → Bytecode
│   ├── OpCode.cs                  # Instruction definitions
│   ├── RegisterAllocator.cs       # Register management
│   └── InstructionBuilder.cs      # Bytecode generation
│
├── Runtime/
│   ├── ExpressionVm.cs            # Bytecode execution
│   └── ExpressionCache.cs         # Compiled expression cache
│
├── Parser/
│   ├── ExpressionParser.cs        # Parlot-based parser
│   ├── DocumentParser.cs          # YAML document parsing
│   └── ExpressionLexer.cs         # Tokenization
│
├── Documents/
│   ├── AbmlDocument.cs            # Document model
│   ├── Flow.cs                    # Flow definition
│   └── Actions/                   # Action node types
│
├── Execution/
│   ├── DocumentExecutor.cs        # Main execution engine
│   ├── ExecutionContext.cs        # Runtime context
│   ├── CallStack.cs               # Flow call tracking
│   ├── Channel/
│   │   ├── ChannelScheduler.cs    # Multi-channel coordinator
│   │   └── ChannelState.cs        # Per-channel state
│   └── Handlers/                  # Action handlers
│       ├── SetHandler.cs
│       ├── CallHandler.cs
│       ├── GotoHandler.cs
│       ├── CondHandler.cs
│       ├── ForEachHandler.cs
│       ├── RepeatHandler.cs
│       ├── LogHandler.cs
│       ├── EmitHandler.cs
│       ├── WaitForHandler.cs
│       └── SyncHandler.cs
│
└── Expressions/
    ├── VariableScope.cs           # Scope chain implementation
    ├── ExpressionEvaluator.cs     # High-level evaluation API
    └── AbmlTypeCoercion.cs        # Type conversion helpers
```

### Test Coverage

| Component | Tests | Status |
|-----------|-------|--------|
| Expression Parser | 100+ | ✅ |
| Expression Compiler | 50+ | ✅ |
| Expression VM | 50+ | ✅ |
| Variable Scoping | 30+ | ✅ |
| Control Flow Handlers | 40+ | ✅ |
| Error Handling | 37+ | ✅ |
| Multi-Channel | 15+ | ✅ |
| Edge Cases | 50+ | ✅ |
| **Total** | **414** | ✅ |

---

## 10. Example: Complete ABML Document

The ABML runtime can execute complex behaviors like:

```yaml
# Complex behavior with all features
version: "2.0.0"
metadata:
  id: "blacksmith_morning_routine"

flows:
  start:
    actions:
      - set: { variable: energy, value: 100 }
      - call: { flow: check_inventory }
      - cond:
          - when: "${needs_supplies}"
            then:
              - call: { flow: go_to_market }
          - else:
            - call: { flow: open_shop }

  check_inventory:
    actions:
      - set: { variable: iron_count, value: "${entity.inventory.iron ?? 0}" }
      - set: { variable: needs_supplies, value: "${iron_count < 5}" }

  go_to_market:
    actions:
      - log: { message: "Heading to market for supplies" }
      # Would call service in Phase 2

  open_shop:
    actions:
      - log: { message: "Opening shop for business" }

# Multi-channel cutscene
channels:
  camera:
    - wait_for: @actors.positions_set
    - log: { message: "Camera: Starting crane shot" }
    - emit: crane_complete

  actors:
    - log: { message: "Actors: Moving to marks" }
    - emit: positions_set
    - wait_for: @camera.crane_complete
    - log: { message: "Actors: Starting dialogue" }
```

All testable with zero infrastructure.

---

## 11. Key Design Principles

### 11.1 Expression Evaluation

- **Cache compiled expressions** - Parse/compile once, execute many times
- **256 registers is enough** - ABML expressions are small, never need spilling
- **Null-safety first** - `?.` and `??` are critical for game state access
- **Type coercion is permissive** - Like JavaScript, auto-convert when reasonable

### 11.2 Execution Model

- **Tree-walk for control flow** - Simple, debuggable, hot-reload friendly
- **Bytecode for expressions** - Compiled for performance where it matters
- **Mock handlers for testing** - Action handlers can be mocked for unit tests

### 11.3 Testing Philosophy

- **Pure unit tests** - Everything testable with zero infrastructure
- **Edge cases matter** - Division by zero, null paths, overflow
- **Deadlock detection at runtime** - Scheduler detects and reports

### 11.4 Error Handling Philosophy

- **Safe default** - Error handling stops flow (prevents cascading failures)
- **Explicit opt-in** - Set `_error_handled = true` to continue (try/catch)
- **Graceful degradation** - THE DREAM pattern for cached fallback data

---

*This document is the authoritative reference for ABML runtime implementation.*

*Related documents:*
- *[ABML_V2_DESIGN_PROPOSAL.md](./ABML_V2_DESIGN_PROPOSAL.md) - Language specification*
- *[BEHAVIOR_PLUGIN_V2.md](./UPCOMING_-_BEHAVIOR_PLUGIN_V2.md) - Runtime integration*
- *[THE_DREAM_GAP_ANALYSIS.md](./THE_DREAM_GAP_ANALYSIS.md) - Future extensions*
- *[THE_DREAM.md](./THE_DREAM.md) - Long-term vision*
