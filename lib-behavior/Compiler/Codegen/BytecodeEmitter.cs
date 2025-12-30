// =============================================================================
// Bytecode Emitter
// Low-level bytecode emission for behavior model compilation.
// =============================================================================

using BeyondImmersion.BannouService.Abml.Bytecode;

namespace BeyondImmersion.Bannou.Behavior.Compiler.Codegen;

/// <summary>
/// Low-level bytecode emitter for behavior model compilation.
/// Handles instruction encoding and operand formatting.
/// </summary>
public sealed class BytecodeEmitter
{
    private readonly List<byte> _bytecode = new(256);
    private readonly Dictionary<int, int> _labelOffsets = new();
    private readonly List<(int offset, int labelId)> _pendingJumps = new();

    /// <summary>
    /// Current bytecode offset (next instruction position).
    /// </summary>
    public int CurrentOffset => _bytecode.Count;

    /// <summary>
    /// Gets the raw bytecode buffer for reading.
    /// </summary>
    public IReadOnlyList<byte> Bytecode => _bytecode;

    /// <summary>
    /// Emits a single opcode with no operands.
    /// </summary>
    /// <param name="opcode">The opcode to emit.</param>
    public void Emit(BehaviorOpcode opcode)
    {
        _bytecode.Add((byte)opcode);
    }

    /// <summary>
    /// Emits an opcode with a single byte operand.
    /// </summary>
    /// <param name="opcode">The opcode to emit.</param>
    /// <param name="operand">The byte operand.</param>
    public void EmitWithByte(BehaviorOpcode opcode, byte operand)
    {
        _bytecode.Add((byte)opcode);
        _bytecode.Add(operand);
    }

    /// <summary>
    /// Emits an opcode with a 16-bit unsigned operand (little-endian).
    /// </summary>
    /// <param name="opcode">The opcode to emit.</param>
    /// <param name="operand">The 16-bit operand.</param>
    public void EmitWithUInt16(BehaviorOpcode opcode, ushort operand)
    {
        _bytecode.Add((byte)opcode);
        _bytecode.Add((byte)(operand & 0xFF));
        _bytecode.Add((byte)((operand >> 8) & 0xFF));
    }

    /// <summary>
    /// Emits an opcode with a byte and a 16-bit operand.
    /// </summary>
    /// <param name="opcode">The opcode to emit.</param>
    /// <param name="byteOperand">The byte operand.</param>
    /// <param name="uint16Operand">The 16-bit operand.</param>
    public void EmitWithByteAndUInt16(BehaviorOpcode opcode, byte byteOperand, ushort uint16Operand)
    {
        _bytecode.Add((byte)opcode);
        _bytecode.Add(byteOperand);
        _bytecode.Add((byte)(uint16Operand & 0xFF));
        _bytecode.Add((byte)((uint16Operand >> 8) & 0xFF));
    }

    /// <summary>
    /// Emits a PushConst instruction.
    /// </summary>
    /// <param name="constantIndex">Index into constant pool.</param>
    public void EmitPushConst(byte constantIndex)
    {
        EmitWithByte(BehaviorOpcode.PushConst, constantIndex);
    }

    /// <summary>
    /// Emits a PushInput instruction.
    /// </summary>
    /// <param name="inputIndex">Index of input variable.</param>
    public void EmitPushInput(byte inputIndex)
    {
        EmitWithByte(BehaviorOpcode.PushInput, inputIndex);
    }

    /// <summary>
    /// Emits a PushLocal instruction.
    /// </summary>
    /// <param name="localIndex">Index of local variable.</param>
    public void EmitPushLocal(byte localIndex)
    {
        EmitWithByte(BehaviorOpcode.PushLocal, localIndex);
    }

    /// <summary>
    /// Emits a StoreLocal instruction.
    /// </summary>
    /// <param name="localIndex">Index of local variable.</param>
    public void EmitStoreLocal(byte localIndex)
    {
        EmitWithByte(BehaviorOpcode.StoreLocal, localIndex);
    }

    /// <summary>
    /// Emits a PushString instruction.
    /// </summary>
    /// <param name="stringIndex">Index into string table.</param>
    public void EmitPushString(ushort stringIndex)
    {
        EmitWithUInt16(BehaviorOpcode.PushString, stringIndex);
    }

    /// <summary>
    /// Emits a SetOutput instruction.
    /// </summary>
    /// <param name="outputIndex">Index of output variable.</param>
    public void EmitSetOutput(byte outputIndex)
    {
        EmitWithByte(BehaviorOpcode.SetOutput, outputIndex);
    }

    /// <summary>
    /// Emits an EmitIntent instruction.
    /// </summary>
    /// <param name="channel">Intent channel (0=action, 1=locomotion, etc.).</param>
    public void EmitEmitIntent(byte channel)
    {
        EmitWithByte(BehaviorOpcode.EmitIntent, channel);
    }

    /// <summary>
    /// Emits a ContinuationPoint instruction.
    /// </summary>
    /// <param name="continuationIndex">Index into continuation point table.</param>
    public void EmitContinuationPoint(ushort continuationIndex)
    {
        EmitWithUInt16(BehaviorOpcode.ContinuationPoint, continuationIndex);
    }

    /// <summary>
    /// Emits an ExtensionAvailable instruction.
    /// </summary>
    /// <param name="continuationIndex">Index into continuation point table.</param>
    public void EmitExtensionAvailable(ushort continuationIndex)
    {
        EmitWithUInt16(BehaviorOpcode.ExtensionAvailable, continuationIndex);
    }

    /// <summary>
    /// Emits a YieldToExtension instruction.
    /// </summary>
    /// <param name="continuationIndex">Index into continuation point table.</param>
    public void EmitYieldToExtension(ushort continuationIndex)
    {
        EmitWithUInt16(BehaviorOpcode.YieldToExtension, continuationIndex);
    }

    /// <summary>
    /// Emits a Trace instruction for debugging.
    /// </summary>
    /// <param name="stringIndex">Index into string table for trace message.</param>
    public void EmitTrace(ushort stringIndex)
    {
        EmitWithUInt16(BehaviorOpcode.Trace, stringIndex);
    }

    /// <summary>
    /// Defines a label at the current position.
    /// </summary>
    /// <param name="labelId">The label identifier.</param>
    public void DefineLabel(int labelId)
    {
        _labelOffsets[labelId] = _bytecode.Count;
    }

    /// <summary>
    /// Emits a jump to a label (may be forward reference).
    /// </summary>
    /// <param name="opcode">Jump opcode (Jmp, JmpIf, JmpUnless).</param>
    /// <param name="labelId">Target label identifier.</param>
    public void EmitJumpToLabel(BehaviorOpcode opcode, int labelId)
    {
        _bytecode.Add((byte)opcode);

        if (_labelOffsets.TryGetValue(labelId, out var offset))
        {
            // Backward jump - target is known
            _bytecode.Add((byte)(offset & 0xFF));
            _bytecode.Add((byte)((offset >> 8) & 0xFF));
        }
        else
        {
            // Forward jump - record for patching
            _pendingJumps.Add((_bytecode.Count, labelId));
            _bytecode.Add(0); // Placeholder
            _bytecode.Add(0);
        }
    }

    /// <summary>
    /// Emits an unconditional jump to a label.
    /// </summary>
    /// <param name="labelId">Target label identifier.</param>
    public void EmitJmp(int labelId)
    {
        EmitJumpToLabel(BehaviorOpcode.Jmp, labelId);
    }

    /// <summary>
    /// Emits a conditional jump (jump if top of stack is truthy).
    /// </summary>
    /// <param name="labelId">Target label identifier.</param>
    public void EmitJmpIf(int labelId)
    {
        EmitJumpToLabel(BehaviorOpcode.JmpIf, labelId);
    }

    /// <summary>
    /// Emits a conditional jump (jump if top of stack is falsy).
    /// </summary>
    /// <param name="labelId">Target label identifier.</param>
    public void EmitJmpUnless(int labelId)
    {
        EmitJumpToLabel(BehaviorOpcode.JmpUnless, labelId);
    }

    /// <summary>
    /// Emits a Call instruction.
    /// </summary>
    /// <param name="targetOffset">Bytecode offset of target flow.</param>
    public void EmitCall(ushort targetOffset)
    {
        EmitWithUInt16(BehaviorOpcode.Call, targetOffset);
    }

    /// <summary>
    /// Patches all forward jump references.
    /// Must be called after all code has been emitted.
    /// </summary>
    public void PatchJumps()
    {
        foreach (var (offset, labelId) in _pendingJumps)
        {
            if (!_labelOffsets.TryGetValue(labelId, out var targetOffset))
            {
                throw new InvalidOperationException($"Undefined label: {labelId}");
            }

            _bytecode[offset] = (byte)(targetOffset & 0xFF);
            _bytecode[offset + 1] = (byte)((targetOffset >> 8) & 0xFF);
        }

        _pendingJumps.Clear();
    }

    /// <summary>
    /// Gets the finalized bytecode array.
    /// Automatically patches any pending jumps.
    /// </summary>
    /// <returns>The bytecode array.</returns>
    public byte[] ToArray()
    {
        PatchJumps();
        return _bytecode.ToArray();
    }

    /// <summary>
    /// Resets the emitter to initial state.
    /// </summary>
    public void Reset()
    {
        _bytecode.Clear();
        _labelOffsets.Clear();
        _pendingJumps.Clear();
    }
}
