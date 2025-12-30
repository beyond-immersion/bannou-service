// =============================================================================
// Behavior Opcode Definitions (Server-Side)
// Stack-based instruction set for compiled ABML behavior models.
// This file must stay in sync with sdk-sources/Behavior/Runtime/BehaviorOpcode.cs
// =============================================================================

namespace BeyondImmersion.BannouService.Abml.Bytecode;

/// <summary>
/// Opcodes for the behavior model bytecode interpreter.
/// Stack-based instruction set designed for per-frame evaluation.
/// </summary>
/// <remarks>
/// <para>
/// All values are stored as doubles to avoid boxing and enable simple stack operations.
/// Booleans are represented as 0.0 (false) and 1.0 (true).
/// </para>
/// <para>
/// IMPORTANT: This enum must stay byte-compatible with the client SDK version
/// in sdk-sources/Behavior/Runtime/BehaviorOpcode.cs
/// </para>
/// </remarks>
public enum BehaviorOpcode : byte
{
    // =========================================================================
    // STACK OPERATIONS (0x00-0x0F)
    // =========================================================================

    /// <summary>Push constant from pool. Operand: pool index (1 byte).</summary>
    PushConst = 0x00,

    /// <summary>Push input variable. Operand: input index (1 byte).</summary>
    PushInput = 0x01,

    /// <summary>Push local variable. Operand: local index (1 byte).</summary>
    PushLocal = 0x02,

    /// <summary>Store to local variable. Operand: local index (1 byte).</summary>
    StoreLocal = 0x03,

    /// <summary>Pop and discard top of stack.</summary>
    Pop = 0x04,

    /// <summary>Duplicate top of stack.</summary>
    Dup = 0x05,

    /// <summary>Swap top two stack values.</summary>
    Swap = 0x06,

    /// <summary>Push string index. Operand: string table index (2 bytes).</summary>
    PushString = 0x07,

    // =========================================================================
    // ARITHMETIC OPERATIONS (0x10-0x1F)
    // =========================================================================

    /// <summary>Add: push(pop() + pop()).</summary>
    Add = 0x10,

    /// <summary>Subtract: push(a - b) where b=pop(), a=pop().</summary>
    Sub = 0x11,

    /// <summary>Multiply: push(pop() * pop()).</summary>
    Mul = 0x12,

    /// <summary>Divide: push(a / b) where b=pop(), a=pop(). Returns 0 if b=0.</summary>
    Div = 0x13,

    /// <summary>Modulo: push(a % b) where b=pop(), a=pop(). Returns 0 if b=0.</summary>
    Mod = 0x14,

    /// <summary>Negate: push(-pop()).</summary>
    Neg = 0x15,

    // =========================================================================
    // COMPARISON OPERATIONS (0x20-0x2F)
    // =========================================================================

    /// <summary>Equal: push(a == b ? 1.0 : 0.0).</summary>
    Eq = 0x20,

    /// <summary>Not equal: push(a != b ? 1.0 : 0.0).</summary>
    Ne = 0x21,

    /// <summary>Less than: push(a &lt; b ? 1.0 : 0.0).</summary>
    Lt = 0x22,

    /// <summary>Less or equal: push(a &lt;= b ? 1.0 : 0.0).</summary>
    Le = 0x23,

    /// <summary>Greater than: push(a &gt; b ? 1.0 : 0.0).</summary>
    Gt = 0x24,

    /// <summary>Greater or equal: push(a &gt;= b ? 1.0 : 0.0).</summary>
    Ge = 0x25,

    // =========================================================================
    // LOGICAL OPERATIONS (0x30-0x3F)
    // =========================================================================

    /// <summary>Logical AND: push((a != 0 &amp;&amp; b != 0) ? 1.0 : 0.0).</summary>
    And = 0x30,

    /// <summary>Logical OR: push((a != 0 || b != 0) ? 1.0 : 0.0).</summary>
    Or = 0x31,

    /// <summary>Logical NOT: push(pop() == 0 ? 1.0 : 0.0).</summary>
    Not = 0x32,

    // =========================================================================
    // CONTROL FLOW (0x40-0x4F)
    // =========================================================================

    /// <summary>Unconditional jump. Operand: target offset (2 bytes).</summary>
    Jmp = 0x40,

    /// <summary>Jump if truthy. Operand: target offset (2 bytes). Pops condition.</summary>
    JmpIf = 0x41,

    /// <summary>Jump if falsy. Operand: target offset (2 bytes). Pops condition.</summary>
    JmpUnless = 0x42,

    /// <summary>Call subroutine. Operand: target offset (2 bytes).</summary>
    Call = 0x43,

    /// <summary>Return from subroutine.</summary>
    Ret = 0x44,

    /// <summary>Halt execution.</summary>
    Halt = 0x45,

    /// <summary>Switch/jump table. Operand: case count (1 byte), followed by offsets.</summary>
    SwitchJmp = 0x46,

    // =========================================================================
    // OUTPUT OPERATIONS (0x50-0x5F)
    // =========================================================================

    /// <summary>Set output variable. Operand: output index (1 byte). Pops value.</summary>
    SetOutput = 0x50,

    /// <summary>Emit intent. Operand: channel (1 byte). Pops [action_idx, urgency].</summary>
    EmitIntent = 0x51,

    // =========================================================================
    // SPECIAL OPERATIONS (0x60-0x6F)
    // =========================================================================

    /// <summary>Push random value [0.0, 1.0).</summary>
    Rand = 0x60,

    /// <summary>Push random integer [min, max]. Pops [min, max].</summary>
    RandInt = 0x61,

    /// <summary>Linear interpolation. Pops [a, b, t], pushes a + (b-a)*t.</summary>
    Lerp = 0x62,

    /// <summary>Clamp value. Pops [value, min, max], pushes clamped result.</summary>
    Clamp = 0x63,

    /// <summary>Absolute value: push(|pop()|).</summary>
    Abs = 0x64,

    /// <summary>Floor: push(floor(pop())).</summary>
    Floor = 0x65,

    /// <summary>Ceiling: push(ceil(pop())).</summary>
    Ceil = 0x66,

    /// <summary>Minimum: push(min(pop(), pop())).</summary>
    Min = 0x67,

    /// <summary>Maximum: push(max(pop(), pop())).</summary>
    Max = 0x68,

    // =========================================================================
    // STREAMING COMPOSITION (0x70-0x7F)
    // =========================================================================

    /// <summary>
    /// Continuation point for streaming composition.
    /// Operand: continuation point index (2 bytes).
    /// </summary>
    ContinuationPoint = 0x70,

    /// <summary>
    /// Check if extension is available at continuation point.
    /// Operand: continuation point index (2 bytes).
    /// Pushes 1.0 if available, 0.0 otherwise.
    /// </summary>
    ExtensionAvailable = 0x71,

    /// <summary>
    /// Yield control to attached extension.
    /// Operand: continuation point index (2 bytes).
    /// </summary>
    YieldToExtension = 0x72,

    // =========================================================================
    // DEBUG (0xF0-0xFF)
    // =========================================================================

    /// <summary>Breakpoint (debug builds only).</summary>
    Breakpoint = 0xF0,

    /// <summary>Trace message. Operand: string table index (2 bytes).</summary>
    Trace = 0xF1,

    /// <summary>No operation.</summary>
    Nop = 0xFF,
}

/// <summary>
/// Flags for behavior model binary format.
/// </summary>
[Flags]
public enum BehaviorModelFlags : ushort
{
    /// <summary>No special flags.</summary>
    None = 0,

    /// <summary>Model includes debug information (source line mappings).</summary>
    HasDebugInfo = 1 << 0,

    /// <summary>Model includes continuation points for streaming composition.</summary>
    HasContinuationPoints = 1 << 1,

    /// <summary>Model is an extension that attaches to another model.</summary>
    IsExtension = 1 << 2,

    /// <summary>Model uses deterministic random (seeded RNG).</summary>
    DeterministicRandom = 1 << 3,
}

/// <summary>
/// Binary format constants for behavior models.
/// </summary>
public static class BehaviorModelFormat
{
    /// <summary>Magic bytes: "ABML" in little-endian.</summary>
    public const uint Magic = 0x4C4D4241;

    /// <summary>Current format version.</summary>
    public const ushort CurrentVersion = 1;

    /// <summary>Header size in bytes.</summary>
    public const int HeaderSize = 32;

    /// <summary>Extension header size in bytes.</summary>
    public const int ExtensionHeaderSize = 24;
}

/// <summary>
/// Intent channel identifiers for behavior output.
/// </summary>
public enum IntentChannel : byte
{
    /// <summary>Primary action intent (attack, block, dodge, etc.).</summary>
    Action = 0,

    /// <summary>Movement/locomotion intent.</summary>
    Locomotion = 1,

    /// <summary>Attention/focus target.</summary>
    Attention = 2,

    /// <summary>Stance/posture.</summary>
    Stance = 3,
}
