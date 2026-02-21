// ═══════════════════════════════════════════════════════════════════════════
// ABML Compiled Expression
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.Bannou.BehaviorExpressions.Compiler;

/// <summary>
/// A compiled ABML expression ready for execution.
/// </summary>
public sealed class CompiledExpression
{
    /// <summary>Gets the bytecode instructions.</summary>
    public Instruction[] Code { get; }

    /// <summary>Gets the constant pool.</summary>
    public object[] Constants { get; }

    /// <summary>Gets the number of registers needed.</summary>
    public int RegisterCount { get; }

    /// <summary>Gets the original expression text.</summary>
    public string SourceText { get; }

    /// <summary>Gets the expected return type.</summary>
    public Type? ExpectedType { get; }

    /// <summary>Creates a new compiled expression.</summary>
    public CompiledExpression(Instruction[] code, object[] constants, int registerCount, string sourceText, Type? expectedType = null)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Constants = constants ?? throw new ArgumentNullException(nameof(constants));
        RegisterCount = registerCount;
        SourceText = sourceText ?? throw new ArgumentNullException(nameof(sourceText));
        ExpectedType = expectedType;
    }

    /// <inheritdoc/>
    public override string ToString() => $"CompiledExpression[{SourceText}] ({Code.Length} instructions)";
}

/// <summary>
/// Builder for creating CompiledExpression instances.
/// </summary>
public sealed class CompiledExpressionBuilder
{
    private readonly List<Instruction> _code = new();
    private readonly ConstantPool _constants = new();
    private readonly RegisterAllocator _registers = new();
    private string _sourceText = "";
    private Type? _expectedType;

    /// <summary>Gets the constant pool.</summary>
    public ConstantPool Constants => _constants;

    /// <summary>Gets the register allocator.</summary>
    public RegisterAllocator Registers => _registers;

    /// <summary>Gets current instruction count.</summary>
    public int CodeLength => _code.Count;

    /// <summary>Sets the source text.</summary>
    public CompiledExpressionBuilder SetSourceText(string sourceText)
    {
        _sourceText = sourceText ?? throw new ArgumentNullException(nameof(sourceText));
        return this;
    }

    /// <summary>Sets the expected return type.</summary>
    public CompiledExpressionBuilder SetExpectedType(Type? type)
    {
        _expectedType = type;
        return this;
    }

    /// <summary>Emits an instruction.</summary>
    public int Emit(Instruction instruction)
    {
        var index = _code.Count;
        _code.Add(instruction);
        return index;
    }

    /// <summary>Emits an instruction with operands.</summary>
    public int Emit(OpCode op, byte a = 0, byte b = 0, byte c = 0) =>
        Emit(new Instruction(op, a, b, c));

    /// <summary>Patches a jump instruction.</summary>
    public void PatchJump(int index, int targetIndex)
    {
        if (index < 0 || index >= _code.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var oldInstr = _code[index];
        var newInstr = Instruction.CreateJump(oldInstr.Op, oldInstr.A, targetIndex);
        _code[index] = newInstr;
    }

    /// <summary>Builds the compiled expression.</summary>
    public CompiledExpression Build() => new(_code.ToArray(), _constants.ToArray(), _registers.UsedCount, _sourceText, _expectedType);

    /// <summary>Resets the builder.</summary>
    public void Reset()
    {
        _code.Clear();
        _constants.Clear();
        _registers.Reset();
        _sourceText = "";
        _expectedType = null;
    }
}
