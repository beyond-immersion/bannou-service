// =============================================================================
// Compilation Context
// Holds state during behavior model compilation.
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Compiler.Codegen;
using BeyondImmersion.Bannou.Behavior.Compiler.Output;
using BeyondImmersion.BannouService.Abml.Bytecode;

namespace BeyondImmersion.Bannou.Behavior.Compiler;

/// <summary>
/// Holds compilation state during behavior model compilation.
/// Thread-unsafe: create a new instance for each compilation.
/// </summary>
public sealed class CompilationContext
{
    /// <summary>
    /// Bytecode emitter for generating instructions.
    /// </summary>
    public BytecodeEmitter Emitter { get; } = new();

    /// <summary>
    /// Constant pool builder for numeric literals.
    /// </summary>
    public ConstantPoolBuilder Constants { get; } = new();

    /// <summary>
    /// String table builder for string literals.
    /// </summary>
    public StringTableBuilder Strings { get; } = new();

    /// <summary>
    /// Label manager for jump targets and flow offsets.
    /// </summary>
    public LabelManager Labels { get; } = new();

    /// <summary>
    /// Model builder for final output.
    /// </summary>
    public BehaviorModelBuilder ModelBuilder { get; } = new();

    /// <summary>
    /// Compilation options.
    /// </summary>
    public CompilationOptions Options { get; }

    /// <summary>
    /// Compilation errors accumulated during compilation.
    /// </summary>
    public List<CompilationError> Errors { get; } = new();

    /// <summary>
    /// Debug line map (bytecode offset -> source line).
    /// </summary>
    public Dictionary<int, int> DebugLineMap { get; } = new();

    /// <summary>
    /// Source file path for debug info.
    /// </summary>
    public string? SourcePath { get; set; }

    // Symbol tables
    private readonly Dictionary<string, byte> _inputVariables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte> _outputVariables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte> _localVariables = new(StringComparer.Ordinal);
    private byte _nextLocalIndex;

    // Continuation points
    private readonly List<ContinuationPointInfo> _continuationPoints = new();

    /// <summary>
    /// Creates a new compilation context.
    /// </summary>
    /// <param name="options">Compilation options.</param>
    public CompilationContext(CompilationOptions? options = null)
    {
        Options = options ?? CompilationOptions.Default;
        Constants.AddCommonConstants(); // Pre-add 0, 1, -1
    }

    /// <summary>
    /// Registers an input variable and returns its index.
    /// </summary>
    /// <param name="name">Variable name.</param>
    /// <param name="defaultValue">Default value.</param>
    /// <returns>The input variable index.</returns>
    public byte RegisterInput(string name, double defaultValue = 0.0)
    {
        if (_inputVariables.TryGetValue(name, out var existing))
        {
            return existing;
        }

        var index = (byte)_inputVariables.Count;
        _inputVariables[name] = index;
        ModelBuilder.AddInput(name, defaultValue);
        return index;
    }

    /// <summary>
    /// Registers an output variable and returns its index.
    /// </summary>
    /// <param name="name">Variable name.</param>
    /// <param name="defaultValue">Default value.</param>
    /// <returns>The output variable index.</returns>
    public byte RegisterOutput(string name, double defaultValue = 0.0)
    {
        if (_outputVariables.TryGetValue(name, out var existing))
        {
            return existing;
        }

        var index = (byte)_outputVariables.Count;
        _outputVariables[name] = index;
        ModelBuilder.AddOutput(name, defaultValue);
        return index;
    }

    /// <summary>
    /// Gets or allocates a local variable index.
    /// </summary>
    /// <param name="name">Variable name.</param>
    /// <returns>The local variable index.</returns>
    public byte GetOrAllocateLocal(string name)
    {
        if (_localVariables.TryGetValue(name, out var existing))
        {
            return existing;
        }

        var index = _nextLocalIndex++;
        _localVariables[name] = index;
        return index;
    }

    /// <summary>
    /// Tries to get an input variable index.
    /// </summary>
    public bool TryGetInput(string name, out byte index)
    {
        return _inputVariables.TryGetValue(name, out index);
    }

    /// <summary>
    /// Tries to get an output variable index.
    /// </summary>
    public bool TryGetOutput(string name, out byte index)
    {
        return _outputVariables.TryGetValue(name, out index);
    }

    /// <summary>
    /// Tries to get a local variable index.
    /// </summary>
    public bool TryGetLocal(string name, out byte index)
    {
        return _localVariables.TryGetValue(name, out index);
    }

    /// <summary>
    /// Registers a continuation point.
    /// </summary>
    /// <param name="name">Continuation point name.</param>
    /// <param name="timeoutMs">Timeout in milliseconds.</param>
    /// <param name="bytecodeOffset">Bytecode offset of the continuation point.</param>
    /// <returns>The continuation point index.</returns>
    public ushort RegisterContinuationPoint(string name, uint timeoutMs, uint bytecodeOffset)
    {
        var index = (ushort)_continuationPoints.Count;
        _continuationPoints.Add(new ContinuationPointInfo(name, timeoutMs, bytecodeOffset));
        return index;
    }

    /// <summary>
    /// Sets the default flow offset for a continuation point.
    /// </summary>
    /// <param name="index">Continuation point index.</param>
    /// <param name="defaultFlowOffset">Bytecode offset of default flow.</param>
    public void SetContinuationPointDefaultFlow(ushort index, uint defaultFlowOffset)
    {
        var cp = _continuationPoints[index];
        _continuationPoints[index] = cp with { DefaultFlowOffset = defaultFlowOffset };
    }

    /// <summary>
    /// Adds a compilation error.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <param name="line">Source line number (optional).</param>
    public void AddError(string message, int? line = null)
    {
        Errors.Add(new CompilationError(message, line));
    }

    /// <summary>
    /// Records debug line info for the current bytecode position.
    /// </summary>
    /// <param name="sourceLine">Source line number.</param>
    public void RecordDebugLine(int sourceLine)
    {
        if (Options.IncludeDebugInfo)
        {
            DebugLineMap[Emitter.CurrentOffset] = sourceLine;
        }
    }

    /// <summary>
    /// Whether compilation has errors.
    /// </summary>
    public bool HasErrors => Errors.Count > 0;

    /// <summary>
    /// Finalizes compilation and returns the built model bytes.
    /// </summary>
    /// <returns>The compiled model as raw bytes.</returns>
    public byte[] Finalize()
    {
        // Finalize continuation points
        foreach (var cp in _continuationPoints)
        {
            ModelBuilder.AddContinuationPoint(
                cp.Name,
                cp.TimeoutMs,
                cp.DefaultFlowOffset,
                cp.BytecodeOffset);
        }

        // Add debug info if enabled
        if (Options.IncludeDebugInfo && SourcePath != null)
        {
            ModelBuilder.WithDebugInfo(SourcePath, DebugLineMap);
        }

        return ModelBuilder
            .WithConstantPool(Constants)
            .WithStringTable(Strings)
            .WithBytecode(Emitter)
            .Build();
    }

    private record struct ContinuationPointInfo(
        string Name,
        uint TimeoutMs,
        uint BytecodeOffset,
        uint DefaultFlowOffset = 0);
}

/// <summary>
/// Compilation options.
/// </summary>
public sealed class CompilationOptions
{
    /// <summary>
    /// Whether to include debug information.
    /// </summary>
    public bool IncludeDebugInfo { get; init; }

    /// <summary>
    /// Whether to enable optimizations.
    /// </summary>
    public bool EnableOptimizations { get; init; }

    /// <summary>
    /// Whether to skip semantic analysis.
    /// </summary>
    public bool SkipSemanticAnalysis { get; init; }

    /// <summary>
    /// Model ID to use (null for auto-generated).
    /// </summary>
    public Guid? ModelId { get; init; }

    /// <summary>
    /// Default compilation options.
    /// </summary>
    public static CompilationOptions Default { get; } = new();

    /// <summary>
    /// Debug compilation options.
    /// </summary>
    public static CompilationOptions Debug { get; } = new() { IncludeDebugInfo = true };

    /// <summary>
    /// Release compilation options with optimizations.
    /// </summary>
    public static CompilationOptions Release { get; } = new() { EnableOptimizations = true };
}

/// <summary>
/// A compilation error.
/// </summary>
/// <param name="Message">Error message.</param>
/// <param name="Line">Source line number (if available).</param>
public sealed record CompilationError(string Message, int? Line = null);
