// =============================================================================
// Behavior Model Interpreter
// Stack-based bytecode virtual machine for behavior model execution.
// =============================================================================

using BeyondImmersion.Bannou.SDK.Behavior.Intent;

namespace BeyondImmersion.Bannou.SDK.Behavior.Runtime;

/// <summary>
/// Lightweight interpreter for compiled ABML behavior models.
/// Designed for per-frame evaluation with zero allocations after initialization.
/// </summary>
/// <remarks>
/// <para>
/// This interpreter uses a stack-based architecture for simplicity and cache efficiency.
/// All values are stored as doubles to avoid boxing and enable SIMD-friendly operations.
/// </para>
/// <para>
/// The interpreter is NOT thread-safe. Each character should have its own interpreter instance.
/// </para>
/// </remarks>
public sealed class BehaviorModelInterpreter : IBehaviorModelInterpreter
{
    private readonly BehaviorModel _model;
    private readonly byte[] _bytecode;
    private readonly IReadOnlyList<double> _constants;
    private readonly IReadOnlyList<string> _strings;
    private readonly StateSchema _schema;

    // Pre-allocated evaluation state (reused across evaluations)
    private readonly double[] _stack;
    private readonly double[] _locals;

    // Execution state
    private int _stackPointer;
    private int _instructionPointer;

    // Random number generator (seeded for deterministic replay support)
    private Random _random;

    // Pause state for continuation point support
    private bool _isPaused;
    private int _pausedAtCpIndex = -1;
    private uint _pausedCpTimeout;

    /// <summary>
    /// The model being interpreted.
    /// </summary>
    public BehaviorModel Model => _model;

    /// <summary>
    /// Model ID for identification.
    /// </summary>
    public Guid ModelId => _model.Id;

    /// <summary>
    /// Input schema for this model.
    /// </summary>
    public StateSchema InputSchema => _schema;

    /// <summary>
    /// String table for output string lookup.
    /// </summary>
    public IReadOnlyList<string> StringTable => _strings;

    /// <summary>
    /// Creates an interpreter for the given behavior model.
    /// </summary>
    /// <param name="model">The compiled behavior model to interpret.</param>
    public BehaviorModelInterpreter(BehaviorModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _bytecode = model.Bytecode;
        _constants = model.ConstantPool;
        _strings = model.StringTable;
        _schema = model.Schema;

        // Pre-allocate stack and locals based on model requirements
        _stack = new double[model.MaxStackDepth];
        _locals = new double[model.LocalCount];

        // Initialize RNG (can be reseeded for deterministic replay)
        _random = new Random();
    }

    /// <summary>
    /// Sets the random seed for deterministic execution.
    /// Call before Evaluate() for reproducible results.
    /// </summary>
    /// <param name="seed">Random seed (e.g., frame number for replays).</param>
    public void SetRandomSeed(int seed)
    {
        _random = new Random(seed);
    }

    /// <summary>
    /// Evaluates the behavior model with the given input state.
    /// Returns the output state (action intent).
    /// </summary>
    /// <remarks>
    /// This method is allocation-free after initial setup.
    /// </remarks>
    /// <param name="inputState">Current game state values (must match input schema).</param>
    /// <param name="outputState">Pre-allocated output buffer (must match output schema).</param>
    public void Evaluate(ReadOnlySpan<double> inputState, Span<double> outputState)
    {
        // Reset evaluation state
        _stackPointer = 0;
        _instructionPointer = 0;

        // Clear locals
        Array.Clear(_locals, 0, _locals.Length);

        // Clear outputs
        outputState.Clear();

        // Main evaluation loop
        while (_instructionPointer < _bytecode.Length)
        {
            var opcode = (BehaviorOpcode)_bytecode[_instructionPointer++];

            switch (opcode)
            {
                // =========================================================
                // STACK OPERATIONS
                // =========================================================

                case BehaviorOpcode.PushConst:
                    {
                        var idx = _bytecode[_instructionPointer++];
                        _stack[_stackPointer++] = _constants[idx];
                    }
                    break;

                case BehaviorOpcode.PushInput:
                    {
                        var idx = _bytecode[_instructionPointer++];
                        _stack[_stackPointer++] = idx < inputState.Length
                            ? inputState[idx]
                            : _schema.GetInputDefault(idx);
                    }
                    break;

                case BehaviorOpcode.PushLocal:
                    {
                        var idx = _bytecode[_instructionPointer++];
                        _stack[_stackPointer++] = _locals[idx];
                    }
                    break;

                case BehaviorOpcode.StoreLocal:
                    {
                        var idx = _bytecode[_instructionPointer++];
                        _locals[idx] = _stack[--_stackPointer];
                    }
                    break;

                case BehaviorOpcode.Pop:
                    _stackPointer--;
                    break;

                case BehaviorOpcode.Dup:
                    _stack[_stackPointer] = _stack[_stackPointer - 1];
                    _stackPointer++;
                    break;

                case BehaviorOpcode.Swap:
                    {
                        var tmp = _stack[_stackPointer - 1];
                        _stack[_stackPointer - 1] = _stack[_stackPointer - 2];
                        _stack[_stackPointer - 2] = tmp;
                    }
                    break;

                case BehaviorOpcode.PushString:
                    {
                        var idx = ReadUInt16();
                        _stack[_stackPointer++] = idx; // Store string index as double
                    }
                    break;

                // =========================================================
                // ARITHMETIC OPERATIONS
                // =========================================================

                case BehaviorOpcode.Add:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = a + b;
                    }
                    break;

                case BehaviorOpcode.Sub:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = a - b;
                    }
                    break;

                case BehaviorOpcode.Mul:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = a * b;
                    }
                    break;

                case BehaviorOpcode.Div:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = b != 0 ? a / b : 0;
                    }
                    break;

                case BehaviorOpcode.Mod:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = b != 0 ? a % b : 0;
                    }
                    break;

                case BehaviorOpcode.Neg:
                    _stack[_stackPointer - 1] = -_stack[_stackPointer - 1];
                    break;

                // =========================================================
                // COMPARISON OPERATIONS
                // =========================================================

                case BehaviorOpcode.Eq:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        // Use epsilon for float comparison
                        _stack[_stackPointer++] = Math.Abs(a - b) < 1e-10 ? 1.0 : 0.0;
                    }
                    break;

                case BehaviorOpcode.Ne:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = Math.Abs(a - b) >= 1e-10 ? 1.0 : 0.0;
                    }
                    break;

                case BehaviorOpcode.Lt:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = a < b ? 1.0 : 0.0;
                    }
                    break;

                case BehaviorOpcode.Le:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = a <= b ? 1.0 : 0.0;
                    }
                    break;

                case BehaviorOpcode.Gt:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = a > b ? 1.0 : 0.0;
                    }
                    break;

                case BehaviorOpcode.Ge:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = a >= b ? 1.0 : 0.0;
                    }
                    break;

                // =========================================================
                // LOGICAL OPERATIONS
                // =========================================================

                case BehaviorOpcode.And:
                    {
                        var b = _stack[--_stackPointer] != 0.0;
                        var a = _stack[--_stackPointer] != 0.0;
                        _stack[_stackPointer++] = (a && b) ? 1.0 : 0.0;
                    }
                    break;

                case BehaviorOpcode.Or:
                    {
                        var b = _stack[--_stackPointer] != 0.0;
                        var a = _stack[--_stackPointer] != 0.0;
                        _stack[_stackPointer++] = (a || b) ? 1.0 : 0.0;
                    }
                    break;

                case BehaviorOpcode.Not:
                    _stack[_stackPointer - 1] = _stack[_stackPointer - 1] == 0.0 ? 1.0 : 0.0;
                    break;

                // =========================================================
                // CONTROL FLOW
                // =========================================================

                case BehaviorOpcode.Jmp:
                    _instructionPointer = ReadUInt16();
                    break;

                case BehaviorOpcode.JmpIf:
                    {
                        var target = ReadUInt16();
                        if (_stack[--_stackPointer] != 0.0)
                        {
                            _instructionPointer = target;
                        }
                    }
                    break;

                case BehaviorOpcode.JmpUnless:
                    {
                        var target = ReadUInt16();
                        if (_stack[--_stackPointer] == 0.0)
                        {
                            _instructionPointer = target;
                        }
                    }
                    break;

                case BehaviorOpcode.Halt:
                    return;

                // =========================================================
                // OUTPUT OPERATIONS
                // =========================================================

                case BehaviorOpcode.SetOutput:
                    {
                        var idx = _bytecode[_instructionPointer++];
                        if (idx < outputState.Length)
                        {
                            outputState[idx] = _stack[--_stackPointer];
                        }
                        else
                        {
                            _stackPointer--;
                        }
                    }
                    break;

                case BehaviorOpcode.EmitIntent:
                    {
                        // EmitIntent sets standardized output slots per IntentSlotLayout.
                        // Stack order: [action_string_idx, urgency]
                        var channel = (IntentChannel)_bytecode[_instructionPointer++];
                        var urgency = _stack[--_stackPointer];
                        var actionIdx = _stack[--_stackPointer];

                        // Write to output slots using IntentSlotLayout conventions:
                        // - IntentSlot: string table index for the intent name
                        // - UrgencySlot: urgency value [0.0 - 1.0]
                        var intentSlot = IntentSlotLayout.IntentSlot(channel);
                        var urgencySlot = IntentSlotLayout.UrgencySlot(channel);
                        if (urgencySlot < outputState.Length)
                        {
                            outputState[intentSlot] = actionIdx;
                            outputState[urgencySlot] = urgency;
                        }
                    }
                    break;

                // =========================================================
                // SPECIAL OPERATIONS
                // =========================================================

                case BehaviorOpcode.Rand:
                    _stack[_stackPointer++] = _random.NextDouble();
                    break;

                case BehaviorOpcode.RandInt:
                    {
                        var max = (int)_stack[--_stackPointer];
                        var min = (int)_stack[--_stackPointer];
                        _stack[_stackPointer++] = _random.Next(min, max + 1);
                    }
                    break;

                case BehaviorOpcode.Lerp:
                    {
                        var t = _stack[--_stackPointer];
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = a + (b - a) * t;
                    }
                    break;

                case BehaviorOpcode.Clamp:
                    {
                        var max = _stack[--_stackPointer];
                        var min = _stack[--_stackPointer];
                        var value = _stack[--_stackPointer];
                        _stack[_stackPointer++] = Math.Clamp(value, min, max);
                    }
                    break;

                case BehaviorOpcode.Abs:
                    _stack[_stackPointer - 1] = Math.Abs(_stack[_stackPointer - 1]);
                    break;

                case BehaviorOpcode.Floor:
                    _stack[_stackPointer - 1] = Math.Floor(_stack[_stackPointer - 1]);
                    break;

                case BehaviorOpcode.Ceil:
                    _stack[_stackPointer - 1] = Math.Ceiling(_stack[_stackPointer - 1]);
                    break;

                case BehaviorOpcode.Min:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = Math.Min(a, b);
                    }
                    break;

                case BehaviorOpcode.Max:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = Math.Max(a, b);
                    }
                    break;

                // =========================================================
                // STREAMING COMPOSITION (handled by CinematicInterpreter)
                // =========================================================

                case BehaviorOpcode.ContinuationPoint:
                    {
                        // In basic interpreter, continuation points are no-ops
                        // CinematicInterpreter overrides this behavior
                        var cpIdx = ReadUInt16();
                        // Skip to default flow
                        var cp = _model.ContinuationPoints[cpIdx];
                        _instructionPointer = (int)cp.DefaultFlowOffset;
                    }
                    break;

                case BehaviorOpcode.ExtensionAvailable:
                    {
                        // In basic interpreter, extensions are never available
                        var _ = ReadUInt16();
                        _stack[_stackPointer++] = 0.0;
                    }
                    break;

                case BehaviorOpcode.YieldToExtension:
                    {
                        // In basic interpreter, this is a no-op (should not be reached)
                        var _ = ReadUInt16();
                    }
                    break;

                // =========================================================
                // DEBUG (only in debug builds)
                // =========================================================

                case BehaviorOpcode.Breakpoint:
                    // Could trigger debugger here
                    break;

                case BehaviorOpcode.Trace:
                    {
                        var strIdx = ReadUInt16();
                        // Could log here: _strings[strIdx]
                    }
                    break;

                case BehaviorOpcode.Nop:
                    break;

                default:
                    throw new InvalidOperationException($"Unknown opcode: 0x{(byte)opcode:X2}");
            }
        }
    }

    /// <summary>
    /// Reads a 16-bit unsigned integer from bytecode (little-endian).
    /// </summary>
    private ushort ReadUInt16()
    {
        var value = (ushort)(_bytecode[_instructionPointer] |
                            (_bytecode[_instructionPointer + 1] << 8));
        _instructionPointer += 2;
        return value;
    }

    /// <summary>
    /// Gets the current stack depth (for debugging).
    /// </summary>
    public int CurrentStackDepth => _stackPointer;

    /// <summary>
    /// Gets the current instruction pointer (for debugging).
    /// </summary>
    public int CurrentInstructionPointer => _instructionPointer;

    /// <summary>
    /// Whether evaluation is currently paused at a continuation point.
    /// </summary>
    public bool IsPaused => _isPaused;

    /// <summary>
    /// Index of the continuation point where evaluation is paused (-1 if not paused).
    /// </summary>
    public int PausedContinuationPointIndex => _pausedAtCpIndex;

    /// <summary>
    /// Evaluates the behavior model with continuation point pause support.
    /// When a CONTINUATION_POINT opcode is reached, evaluation pauses and
    /// returns a result indicating the pause. Call Resume methods to continue.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method is allocation-free after initial setup, maintaining the same
    /// performance characteristics as <see cref="Evaluate"/>.
    /// </para>
    /// <para>
    /// When resuming from a paused state, the instruction pointer continues from
    /// where it left off. The stack and locals are preserved across evaluations.
    /// </para>
    /// </remarks>
    /// <param name="inputState">Current game state values (must match input schema).</param>
    /// <param name="outputState">Pre-allocated output buffer (must match output schema).</param>
    /// <returns>Result indicating whether evaluation completed or paused.</returns>
    public EvaluationResult EvaluateWithPause(ReadOnlySpan<double> inputState, Span<double> outputState)
    {
        // If not resuming from pause, reset evaluation state
        if (!_isPaused)
        {
            _stackPointer = 0;
            _instructionPointer = 0;
            Array.Clear(_locals, 0, _locals.Length);
            outputState.Clear();
        }
        else
        {
            // Clear pause state, we're resuming
            _isPaused = false;
        }

        // Main evaluation loop
        while (_instructionPointer < _bytecode.Length)
        {
            var opcode = (BehaviorOpcode)_bytecode[_instructionPointer++];

            switch (opcode)
            {
                // =========================================================
                // STACK OPERATIONS
                // =========================================================

                case BehaviorOpcode.PushConst:
                    {
                        var idx = _bytecode[_instructionPointer++];
                        _stack[_stackPointer++] = _constants[idx];
                    }
                    break;

                case BehaviorOpcode.PushInput:
                    {
                        var idx = _bytecode[_instructionPointer++];
                        _stack[_stackPointer++] = idx < inputState.Length
                            ? inputState[idx]
                            : _schema.GetInputDefault(idx);
                    }
                    break;

                case BehaviorOpcode.PushLocal:
                    {
                        var idx = _bytecode[_instructionPointer++];
                        _stack[_stackPointer++] = _locals[idx];
                    }
                    break;

                case BehaviorOpcode.StoreLocal:
                    {
                        var idx = _bytecode[_instructionPointer++];
                        _locals[idx] = _stack[--_stackPointer];
                    }
                    break;

                case BehaviorOpcode.Pop:
                    _stackPointer--;
                    break;

                case BehaviorOpcode.Dup:
                    _stack[_stackPointer] = _stack[_stackPointer - 1];
                    _stackPointer++;
                    break;

                case BehaviorOpcode.Swap:
                    {
                        var tmp = _stack[_stackPointer - 1];
                        _stack[_stackPointer - 1] = _stack[_stackPointer - 2];
                        _stack[_stackPointer - 2] = tmp;
                    }
                    break;

                case BehaviorOpcode.PushString:
                    {
                        var idx = ReadUInt16();
                        _stack[_stackPointer++] = idx;
                    }
                    break;

                // =========================================================
                // ARITHMETIC OPERATIONS
                // =========================================================

                case BehaviorOpcode.Add:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = a + b;
                    }
                    break;

                case BehaviorOpcode.Sub:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = a - b;
                    }
                    break;

                case BehaviorOpcode.Mul:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = a * b;
                    }
                    break;

                case BehaviorOpcode.Div:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = b != 0 ? a / b : 0;
                    }
                    break;

                case BehaviorOpcode.Mod:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = b != 0 ? a % b : 0;
                    }
                    break;

                case BehaviorOpcode.Neg:
                    _stack[_stackPointer - 1] = -_stack[_stackPointer - 1];
                    break;

                // =========================================================
                // COMPARISON OPERATIONS
                // =========================================================

                case BehaviorOpcode.Eq:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = Math.Abs(a - b) < 1e-10 ? 1.0 : 0.0;
                    }
                    break;

                case BehaviorOpcode.Ne:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = Math.Abs(a - b) >= 1e-10 ? 1.0 : 0.0;
                    }
                    break;

                case BehaviorOpcode.Lt:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = a < b ? 1.0 : 0.0;
                    }
                    break;

                case BehaviorOpcode.Le:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = a <= b ? 1.0 : 0.0;
                    }
                    break;

                case BehaviorOpcode.Gt:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = a > b ? 1.0 : 0.0;
                    }
                    break;

                case BehaviorOpcode.Ge:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = a >= b ? 1.0 : 0.0;
                    }
                    break;

                // =========================================================
                // LOGICAL OPERATIONS
                // =========================================================

                case BehaviorOpcode.And:
                    {
                        var b = _stack[--_stackPointer] != 0.0;
                        var a = _stack[--_stackPointer] != 0.0;
                        _stack[_stackPointer++] = (a && b) ? 1.0 : 0.0;
                    }
                    break;

                case BehaviorOpcode.Or:
                    {
                        var b = _stack[--_stackPointer] != 0.0;
                        var a = _stack[--_stackPointer] != 0.0;
                        _stack[_stackPointer++] = (a || b) ? 1.0 : 0.0;
                    }
                    break;

                case BehaviorOpcode.Not:
                    _stack[_stackPointer - 1] = _stack[_stackPointer - 1] == 0.0 ? 1.0 : 0.0;
                    break;

                // =========================================================
                // CONTROL FLOW
                // =========================================================

                case BehaviorOpcode.Jmp:
                    {
                        var offset = ReadUInt16();
                        _instructionPointer = offset;
                    }
                    break;

                case BehaviorOpcode.JmpIf:
                    {
                        var offset = ReadUInt16();
                        var condition = _stack[--_stackPointer];
                        if (condition != 0.0)
                        {
                            _instructionPointer = offset;
                        }
                    }
                    break;

                case BehaviorOpcode.JmpUnless:
                    {
                        var offset = ReadUInt16();
                        var condition = _stack[--_stackPointer];
                        if (condition == 0.0)
                        {
                            _instructionPointer = offset;
                        }
                    }
                    break;

                case BehaviorOpcode.Call:
                    {
                        var offset = ReadUInt16();
                        _stack[_stackPointer++] = _instructionPointer;
                        _instructionPointer = offset;
                    }
                    break;

                case BehaviorOpcode.Ret:
                    {
                        var returnAddr = (int)_stack[--_stackPointer];
                        _instructionPointer = returnAddr;
                    }
                    break;

                case BehaviorOpcode.Halt:
                    return EvaluationResult.Completed;

                case BehaviorOpcode.SwitchJmp:
                    {
                        var caseCount = _bytecode[_instructionPointer++];
                        var value = (int)_stack[--_stackPointer];
                        var matched = false;

                        for (var i = 0; i < caseCount; i++)
                        {
                            var caseValue = ReadUInt16();
                            var caseOffset = ReadUInt16();
                            if (value == caseValue)
                            {
                                _instructionPointer = caseOffset;
                                matched = true;
                                break;
                            }
                        }

                        if (!matched)
                        {
                            var defaultOffset = ReadUInt16();
                            _instructionPointer = defaultOffset;
                        }
                    }
                    break;

                // =========================================================
                // MATH FUNCTIONS
                // =========================================================

                case BehaviorOpcode.Rand:
                    _stack[_stackPointer++] = _random.NextDouble();
                    break;

                case BehaviorOpcode.RandInt:
                    {
                        var max = (int)_stack[--_stackPointer];
                        var min = (int)_stack[--_stackPointer];
                        _stack[_stackPointer++] = _random.Next(min, max + 1);
                    }
                    break;

                case BehaviorOpcode.Lerp:
                    {
                        var t = _stack[--_stackPointer];
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = a + (b - a) * t;
                    }
                    break;

                case BehaviorOpcode.Clamp:
                    {
                        var max = _stack[--_stackPointer];
                        var min = _stack[--_stackPointer];
                        var val = _stack[--_stackPointer];
                        _stack[_stackPointer++] = Math.Clamp(val, min, max);
                    }
                    break;

                case BehaviorOpcode.Abs:
                    _stack[_stackPointer - 1] = Math.Abs(_stack[_stackPointer - 1]);
                    break;

                case BehaviorOpcode.Floor:
                    _stack[_stackPointer - 1] = Math.Floor(_stack[_stackPointer - 1]);
                    break;

                case BehaviorOpcode.Ceil:
                    _stack[_stackPointer - 1] = Math.Ceiling(_stack[_stackPointer - 1]);
                    break;

                case BehaviorOpcode.Min:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = Math.Min(a, b);
                    }
                    break;

                case BehaviorOpcode.Max:
                    {
                        var b = _stack[--_stackPointer];
                        var a = _stack[--_stackPointer];
                        _stack[_stackPointer++] = Math.Max(a, b);
                    }
                    break;

                // =========================================================
                // OUTPUT
                // =========================================================

                case BehaviorOpcode.SetOutput:
                    {
                        var idx = _bytecode[_instructionPointer++];
                        var value = _stack[--_stackPointer];
                        if (idx < outputState.Length)
                        {
                            outputState[idx] = value;
                        }
                    }
                    break;

                case BehaviorOpcode.EmitIntent:
                    {
                        // EmitIntent sets standardized output slots per IntentSlotLayout.
                        // Stack order: [action_string_idx, urgency]
                        var channel = (IntentChannel)_bytecode[_instructionPointer++];
                        var urgency = _stack[--_stackPointer];
                        var actionIdx = _stack[--_stackPointer];

                        // Write to output slots using IntentSlotLayout conventions:
                        // - IntentSlot: string table index for the intent name
                        // - UrgencySlot: urgency value [0.0 - 1.0]
                        var intentSlot = IntentSlotLayout.IntentSlot(channel);
                        var urgencySlot = IntentSlotLayout.UrgencySlot(channel);
                        if (urgencySlot < outputState.Length)
                        {
                            outputState[intentSlot] = actionIdx;
                            outputState[urgencySlot] = urgency;
                        }
                    }
                    break;

                // =========================================================
                // STREAMING COMPOSITION
                // =========================================================

                case BehaviorOpcode.ContinuationPoint:
                    {
                        var cpIdx = ReadUInt16();
                        var cp = _model.ContinuationPoints[cpIdx];

                        // PAUSE: Store state and return
                        _isPaused = true;
                        _pausedAtCpIndex = cpIdx;
                        _pausedCpTimeout = cp.TimeoutMs;

                        return EvaluationResult.PausedAt(cpIdx, cp.TimeoutMs);
                    }

                case BehaviorOpcode.ExtensionAvailable:
                    {
                        // In basic interpreter with pause, extensions are never available
                        // The CinematicInterpreter handles extension checking externally
                        var _ = ReadUInt16();
                        _stack[_stackPointer++] = 0.0;
                    }
                    break;

                case BehaviorOpcode.YieldToExtension:
                    {
                        // In basic interpreter, this is a no-op (should not be reached)
                        var _ = ReadUInt16();
                    }
                    break;

                // =========================================================
                // DEBUG (only in debug builds)
                // =========================================================

                case BehaviorOpcode.Breakpoint:
                    break;

                case BehaviorOpcode.Trace:
                    {
                        var strIdx = ReadUInt16();
                        // Could log here: _strings[strIdx]
                    }
                    break;

                case BehaviorOpcode.Nop:
                    break;

                default:
                    throw new InvalidOperationException($"Unknown opcode: 0x{(byte)opcode:X2}");
            }
        }

        return EvaluationResult.Completed;
    }

    /// <summary>
    /// Resumes evaluation with the default flow after pausing at a continuation point.
    /// </summary>
    /// <remarks>
    /// Call this when no extension is available or the timeout has expired.
    /// </remarks>
    /// <param name="inputState">Current game state values.</param>
    /// <param name="outputState">Pre-allocated output buffer.</param>
    /// <returns>Result of continued evaluation.</returns>
    /// <exception cref="InvalidOperationException">Thrown if not paused at a continuation point.</exception>
    public EvaluationResult ResumeWithDefaultFlow(ReadOnlySpan<double> inputState, Span<double> outputState)
    {
        if (!_isPaused || _pausedAtCpIndex < 0)
        {
            throw new InvalidOperationException("Not paused at a continuation point");
        }

        var cp = _model.ContinuationPoints[_pausedAtCpIndex];
        _instructionPointer = (int)cp.DefaultFlowOffset;
        // Keep _isPaused true so EvaluateWithPause skips the reset and preserves our IP
        _pausedAtCpIndex = -1;
        _pausedCpTimeout = 0;

        // Continue evaluation from default flow
        // _isPaused is still true, so EvaluateWithPause won't reset the IP
        return EvaluateWithPause(inputState, outputState);
    }

    /// <summary>
    /// Resumes evaluation by executing an extension model instead of the default flow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The extension interpreter runs to completion, and its output replaces
    /// the current output state. After the extension completes, the base model
    /// continues from after the continuation point's default flow.
    /// </para>
    /// </remarks>
    /// <param name="extensionInterpreter">The extension interpreter to execute.</param>
    /// <param name="inputState">Current game state values.</param>
    /// <param name="outputState">Pre-allocated output buffer.</param>
    /// <exception cref="InvalidOperationException">Thrown if not paused at a continuation point.</exception>
    public void ResumeWithExtension(
        IBehaviorModelInterpreter extensionInterpreter,
        ReadOnlySpan<double> inputState,
        Span<double> outputState)
    {
        if (!_isPaused)
        {
            throw new InvalidOperationException("Not paused at a continuation point");
        }

        _isPaused = false;
        _pausedAtCpIndex = -1;
        _pausedCpTimeout = 0;

        // Execute extension model
        extensionInterpreter.Evaluate(inputState, outputState);
    }

    /// <summary>
    /// Clears the pause state without resuming execution.
    /// </summary>
    /// <remarks>
    /// Use this to abandon a paused evaluation without continuing.
    /// </remarks>
    public void ClearPauseState()
    {
        _isPaused = false;
        _pausedAtCpIndex = -1;
        _pausedCpTimeout = 0;
    }
}
