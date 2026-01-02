// =============================================================================
// Behavior Model Opcodes
// Stack-based instruction set for the local behavior runtime.
//
// This file is the CANONICAL source for behavior opcodes.
// Both server and SDK use this definition.
// =============================================================================

namespace BeyondImmersion.BannouService.Behavior.Runtime;

/// <summary>
/// Stack-based bytecode opcodes for behavior model execution.
/// Different from the register-based expression opcodes in bannou-service.
/// </summary>
/// <remarks>
/// Instruction encoding: [Opcode:8][Operand1:8][Operand2:8][Operand3:8]
/// Most instructions are 1-2 bytes. Jump targets use 16-bit offsets.
/// </remarks>
public enum BehaviorOpcode : byte
{
    // =========================================================================
    // STACK OPERATIONS (0x00-0x0F)
    // =========================================================================

    /// <summary>Push constant from pool. [PUSH_CONST idx:8]</summary>
    PushConst = 0x00,

    /// <summary>Push input variable value. [PUSH_INPUT idx:8]</summary>
    PushInput = 0x01,

    /// <summary>Push local variable value. [PUSH_LOCAL idx:8]</summary>
    PushLocal = 0x02,

    /// <summary>Pop and store to local variable. [STORE_LOCAL idx:8]</summary>
    StoreLocal = 0x03,

    /// <summary>Discard top of stack. [POP]</summary>
    Pop = 0x04,

    /// <summary>Duplicate top of stack. [DUP]</summary>
    Dup = 0x05,

    /// <summary>Swap top two stack values. [SWAP]</summary>
    Swap = 0x06,

    /// <summary>Push string from string table. [PUSH_STRING idx:16]</summary>
    PushString = 0x07,

    // =========================================================================
    // ARITHMETIC OPERATIONS (0x10-0x1F)
    // =========================================================================

    /// <summary>Pop a, b; push a + b. [ADD]</summary>
    Add = 0x10,

    /// <summary>Pop a, b; push a - b. [SUB]</summary>
    Sub = 0x11,

    /// <summary>Pop a, b; push a * b. [MUL]</summary>
    Mul = 0x12,

    /// <summary>Pop a, b; push a / b. [DIV]</summary>
    Div = 0x13,

    /// <summary>Pop a, b; push a % b. [MOD]</summary>
    Mod = 0x14,

    /// <summary>Pop a; push -a. [NEG]</summary>
    Neg = 0x15,

    // =========================================================================
    // COMPARISON OPERATIONS (0x20-0x2F)
    // =========================================================================

    /// <summary>Pop a, b; push 1.0 if a == b, else 0.0. [EQ]</summary>
    Eq = 0x20,

    /// <summary>Pop a, b; push 1.0 if a != b, else 0.0. [NE]</summary>
    Ne = 0x21,

    /// <summary>Pop a, b; push 1.0 if a &lt; b, else 0.0. [LT]</summary>
    Lt = 0x22,

    /// <summary>Pop a, b; push 1.0 if a &lt;= b, else 0.0. [LE]</summary>
    Le = 0x23,

    /// <summary>Pop a, b; push 1.0 if a &gt; b, else 0.0. [GT]</summary>
    Gt = 0x24,

    /// <summary>Pop a, b; push 1.0 if a &gt;= b, else 0.0. [GE]</summary>
    Ge = 0x25,

    // =========================================================================
    // LOGICAL OPERATIONS (0x30-0x3F)
    // =========================================================================

    /// <summary>Pop a, b; push 1.0 if both truthy, else 0.0. [AND]</summary>
    And = 0x30,

    /// <summary>Pop a, b; push 1.0 if either truthy, else 0.0. [OR]</summary>
    Or = 0x31,

    /// <summary>Pop a; push 1.0 if falsy, else 0.0. [NOT]</summary>
    Not = 0x32,

    // =========================================================================
    // CONTROL FLOW (0x40-0x4F)
    // =========================================================================

    /// <summary>Unconditional jump. [JMP offset:16]</summary>
    Jmp = 0x40,

    /// <summary>Jump if top of stack is truthy. [JMP_IF offset:16]</summary>
    JmpIf = 0x41,

    /// <summary>Jump if top of stack is falsy. [JMP_UNLESS offset:16]</summary>
    JmpUnless = 0x42,

    /// <summary>Call subroutine. [CALL flow_idx:16]</summary>
    Call = 0x43,

    /// <summary>Return from subroutine. [RET]</summary>
    Ret = 0x44,

    /// <summary>Stop execution. [HALT]</summary>
    Halt = 0x45,

    /// <summary>Jump table for switch-like constructs. [SWITCH_JMP cases:8]</summary>
    SwitchJmp = 0x46,

    // =========================================================================
    // OUTPUT OPERATIONS (0x50-0x5F)
    // =========================================================================

    /// <summary>Pop value and store in output variable. [SET_OUTPUT idx:8]</summary>
    SetOutput = 0x50,

    /// <summary>
    /// Emit action intent with urgency. [EMIT_INTENT channel:8]
    /// Expects stack: [action_string_idx, urgency]
    /// </summary>
    EmitIntent = 0x51,

    // =========================================================================
    // SPECIAL OPERATIONS (0x60-0x6F)
    // =========================================================================

    /// <summary>Push random float [0, 1). [RAND]</summary>
    Rand = 0x60,

    /// <summary>Push random int in range. [RAND_INT] Stack: [min, max]</summary>
    RandInt = 0x61,

    /// <summary>Linear interpolation. [LERP] Stack: [a, b, t] -> a + (b - a) * t</summary>
    Lerp = 0x62,

    /// <summary>Clamp value to range. [CLAMP] Stack: [value, min, max]</summary>
    Clamp = 0x63,

    /// <summary>Absolute value. [ABS] Stack: [a] -> |a|</summary>
    Abs = 0x64,

    /// <summary>Floor. [FLOOR] Stack: [a] -> floor(a)</summary>
    Floor = 0x65,

    /// <summary>Ceiling. [CEIL] Stack: [a] -> ceil(a)</summary>
    Ceil = 0x66,

    /// <summary>Minimum of two values. [MIN] Stack: [a, b]</summary>
    Min = 0x67,

    /// <summary>Maximum of two values. [MAX] Stack: [a, b]</summary>
    Max = 0x68,

    // =========================================================================
    // STREAMING COMPOSITION (0x70-0x7F)
    // =========================================================================

    /// <summary>
    /// Pause for possible extension attachment.
    /// [CONTINUATION_POINT cp_idx:16]
    /// Interpreter checks for attached extension at this point.
    /// If extension available: transfers control to extension flow.
    /// If no extension and timeout expires: jumps to default flow.
    /// </summary>
    ContinuationPoint = 0x70,

    /// <summary>
    /// Check if extension is available.
    /// [EXTENSION_AVAILABLE cp_idx:16]
    /// Push 1.0 if extension attached, 0.0 otherwise.
    /// </summary>
    ExtensionAvailable = 0x71,

    /// <summary>
    /// Transfer control to attached extension.
    /// [YIELD_TO_EXTENSION cp_idx:16]
    /// Used internally when extension is attached.
    /// </summary>
    YieldToExtension = 0x72,

    // =========================================================================
    // DEBUG OPERATIONS (0xF0-0xFE) - Only in debug builds
    // =========================================================================

    /// <summary>Debug breakpoint. [BREAKPOINT]</summary>
    Breakpoint = 0xF0,

    /// <summary>Debug trace with string. [TRACE str_idx:16]</summary>
    Trace = 0xF1,

    // =========================================================================
    // RESERVED (0xFF)
    // =========================================================================

    /// <summary>No-operation. [NOP]</summary>
    Nop = 0xFF,
}

/// <summary>
/// Flags for behavior model header.
/// </summary>
[Flags]
public enum BehaviorModelFlags : ushort
{
    /// <summary>No special flags.</summary>
    None = 0,

    /// <summary>Model includes debug info section.</summary>
    HasDebugInfo = 1 << 0,

    /// <summary>Model has continuation points for streaming composition.</summary>
    HasContinuationPoints = 1 << 1,

    /// <summary>Model is an extension that attaches to another model.</summary>
    IsExtension = 1 << 2,

    /// <summary>Model uses deterministic random (seeded RNG).</summary>
    DeterministicRandom = 1 << 3,
}

/// <summary>
/// Variable types supported in state schemas.
/// </summary>
public enum BehaviorVariableType : byte
{
    /// <summary>Boolean (stored as 0.0 or 1.0).</summary>
    Bool = 0,

    /// <summary>Integer (stored as double).</summary>
    Int = 1,

    /// <summary>Floating point.</summary>
    Float = 2,

    /// <summary>String (stored as string table index).</summary>
    String = 3,

    /// <summary>Enum value (stored as int, validated against enum def).</summary>
    Enum = 4,

    /// <summary>Entity reference (stored as int ID).</summary>
    EntityId = 5,

    /// <summary>Vector3 (stored as 3 consecutive doubles).</summary>
    Vector3 = 6,
}

/// <summary>
/// Intent channel identifiers for multi-model coordination.
/// </summary>
public enum IntentChannel : byte
{
    /// <summary>Primary action channel (attack, block, use, talk).</summary>
    Action = 0,

    /// <summary>Movement/locomotion channel.</summary>
    Locomotion = 1,

    /// <summary>Attention/gaze direction channel.</summary>
    Attention = 2,

    /// <summary>Combat stance channel.</summary>
    Stance = 3,

    /// <summary>Vocalization channel.</summary>
    Vocalization = 4,
}
