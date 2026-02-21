// ═══════════════════════════════════════════════════════════════════════════
// ABML Bytecode Instruction
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorCompiler.Compiler;

namespace BeyondImmersion.Bannou.BehaviorExpressions.Compiler;

/// <summary>
/// ABML bytecode instruction. 32-bit encoding: [opcode:8][A:8][B:8][C:8]
/// </summary>
public readonly struct Instruction : IEquatable<Instruction>
{
    private readonly uint _packed;

    /// <summary>Gets the operation code.</summary>
    public OpCode Op => (OpCode)(_packed >> 24);

    /// <summary>Gets the A operand (usually destination register).</summary>
    public byte A => (byte)((_packed >> 16) & 0xFF);

    /// <summary>Gets the B operand (source 1 or constant index).</summary>
    public byte B => (byte)((_packed >> 8) & 0xFF);

    /// <summary>Gets the C operand (source 2 or constant index).</summary>
    public byte C => (byte)(_packed & 0xFF);

    /// <summary>Gets the packed 32-bit representation.</summary>
    public uint Packed => _packed;

    /// <summary>Creates a new instruction.</summary>
    public Instruction(OpCode op, byte a, byte b = 0, byte c = 0) =>
        _packed = ((uint)op << 24) | ((uint)a << 16) | ((uint)b << 8) | c;

    /// <summary>Creates an instruction from packed value.</summary>
    public Instruction(uint packed) => _packed = packed;

    /// <summary>Gets the 16-bit jump offset.</summary>
    public int GetJumpOffset() => (B << 8) | C;

    /// <summary>Creates a jump instruction.</summary>
    public static Instruction CreateJump(OpCode op, byte register, int offset)
    {
        if (offset < 0 || offset > VmConfig.MaxJumpOffset)
            throw new ArgumentOutOfRangeException(nameof(offset));
        return new Instruction(op, register, (byte)(offset >> 8), (byte)(offset & 0xFF));
    }

    /// <summary>Creates an unconditional jump.</summary>
    public static Instruction CreateJump(int offset) => CreateJump(OpCode.Jump, 0, offset);

    /// <inheritdoc/>
    public override string ToString() => Op switch
    {
        OpCode.LoadConst => $"LoadConst R{A}, {B}",
        OpCode.LoadVar => $"LoadVar R{A}, {B}",
        OpCode.LoadNull => $"LoadNull R{A}",
        OpCode.LoadTrue => $"LoadTrue R{A}",
        OpCode.LoadFalse => $"LoadFalse R{A}",
        OpCode.GetProp => $"GetProp R{A}, R{B}, {C}",
        OpCode.GetPropSafe => $"GetPropSafe R{A}, R{B}, {C}",
        OpCode.Add => $"Add R{A}, R{B}, R{C}",
        OpCode.Sub => $"Sub R{A}, R{B}, R{C}",
        OpCode.Jump => $"Jump {GetJumpOffset()}",
        OpCode.JumpIfFalse => $"JumpIfFalse R{A}, {GetJumpOffset()}",
        OpCode.Return => $"Return R{A}",
        _ => $"{Op} R{A}, R{B}, R{C}"
    };

    /// <inheritdoc/>
    public bool Equals(Instruction other) => _packed == other._packed;
    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Instruction other && Equals(other);
    /// <inheritdoc/>
    public override int GetHashCode() => (int)_packed;
    /// <summary>Equality operator.</summary>
    public static bool operator ==(Instruction left, Instruction right) => left.Equals(right);
    /// <summary>Inequality operator.</summary>
    public static bool operator !=(Instruction left, Instruction right) => !left.Equals(right);
}
