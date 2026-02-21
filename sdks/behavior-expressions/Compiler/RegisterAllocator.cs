// ═══════════════════════════════════════════════════════════════════════════
// ABML Register Allocator
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorCompiler.Compiler;

namespace BeyondImmersion.Bannou.BehaviorExpressions.Compiler;

/// <summary>
/// Manages register allocation during expression compilation.
/// </summary>
public sealed class RegisterAllocator
{
    private readonly Stack<byte> _freeList = new();
    private byte _nextRegister;
    private byte _maxUsed;

    /// <summary>Gets the total number of registers used.</summary>
    public int UsedCount => _maxUsed;

    /// <summary>Gets current allocated count.</summary>
    public int AllocatedCount => _nextRegister - _freeList.Count;

    /// <summary>Allocates a new register.</summary>
    public byte Allocate()
    {
        if (_freeList.Count > 0) return _freeList.Pop();
        if (_nextRegister == byte.MaxValue)
            throw new InvalidOperationException($"Register limit exceeded");
        var reg = _nextRegister++;
        if (_nextRegister > _maxUsed) _maxUsed = _nextRegister;
        return reg;
    }

    /// <summary>Allocates a contiguous range of registers.</summary>
    public byte AllocateRange(int count)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (_nextRegister + count > VmConfig.MaxRegisters)
            throw new InvalidOperationException($"Cannot allocate {count} contiguous registers");
        var startReg = _nextRegister;
        _nextRegister += (byte)count;
        if (_nextRegister > _maxUsed) _maxUsed = _nextRegister;
        return startReg;
    }

    /// <summary>Frees a register for reuse.</summary>
    public void Free(byte register)
    {
        if (register >= _nextRegister) throw new ArgumentOutOfRangeException(nameof(register));
        _freeList.Push(register);
    }

    /// <summary>Frees a range of registers.</summary>
    public void FreeRange(byte startRegister, int count)
    {
        for (var i = 0; i < count; i++) Free((byte)(startRegister + i));
    }

    /// <summary>Resets the allocator.</summary>
    public void Reset()
    {
        _freeList.Clear();
        _nextRegister = 0;
        _maxUsed = 0;
    }
}
