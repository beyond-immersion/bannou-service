// ═══════════════════════════════════════════════════════════════════════════
// ABML Bytecode Virtual Machine
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorCompiler.Compiler;
using BeyondImmersion.BannouService.Abml.Compiler;
using BeyondImmersion.BannouService.Abml.Exceptions;
using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.Abml.Functions;

namespace BeyondImmersion.BannouService.Abml.Runtime;

/// <summary>
/// Register-based bytecode virtual machine for ABML expressions.
/// </summary>
public sealed class ExpressionVm
{
    private readonly object?[] _registers = new object?[VmConfig.MaxRegisters];
    private readonly IFunctionRegistry _functions;

    /// <summary>
    /// Creates a new VM instance with the specified function registry.
    /// </summary>
    public ExpressionVm(IFunctionRegistry? functions = null)
    {
        _functions = functions ?? new FunctionRegistry();
    }

    /// <summary>
    /// Executes a compiled expression with the given variable scope.
    /// </summary>
    /// <param name="expression">The compiled expression to execute.</param>
    /// <param name="scope">The variable scope for variable lookups.</param>
    /// <returns>The expression result.</returns>
    public object? Execute(CompiledExpression expression, IVariableScope scope)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(scope);

        var code = expression.Code;
        var constants = expression.Constants;
        var registerCount = expression.RegisterCount;

        // Clear only the registers we'll use
        Array.Clear(_registers, 0, registerCount);

        var pc = 0;
        while (pc < code.Length)
        {
            var instr = code[pc];
            var op = instr.Op;
            var a = instr.A;
            var b = instr.B;
            var c = instr.C;

            switch (op)
            {
                // ═══════════════════════════════════════════════════════════
                // LOADS
                // ═══════════════════════════════════════════════════════════
                case OpCode.LoadConst:
                    _registers[a] = constants[b];
                    break;

                case OpCode.LoadVar:
                    var varName = (string)constants[b];
                    _registers[a] = scope.GetValue(varName);
                    break;

                case OpCode.LoadNull:
                    _registers[a] = null;
                    break;

                case OpCode.LoadTrue:
                    _registers[a] = true;
                    break;

                case OpCode.LoadFalse:
                    _registers[a] = false;
                    break;

                case OpCode.Move:
                    _registers[a] = _registers[b];
                    break;

                // ═══════════════════════════════════════════════════════════
                // PROPERTY ACCESS
                // ═══════════════════════════════════════════════════════════
                case OpCode.GetProp:
                    {
                        var obj = _registers[b];
                        var propName = (string)constants[c];
                        if (obj == null)
                        {
                            throw new AbmlRuntimeException($"Cannot access property '{propName}' of null");
                        }
                        _registers[a] = AbmlTypeCoercion.GetProperty(obj, propName);
                    }
                    break;

                case OpCode.GetPropSafe:
                    {
                        var obj = _registers[b];
                        var propName = (string)constants[c];
                        _registers[a] = obj == null ? null : AbmlTypeCoercion.GetProperty(obj, propName);
                    }
                    break;

                case OpCode.GetIndex:
                    {
                        var obj = _registers[b];
                        var index = _registers[c];
                        if (obj == null)
                        {
                            throw new AbmlRuntimeException("Cannot index into null");
                        }
                        _registers[a] = AbmlTypeCoercion.GetIndex(obj, index);
                    }
                    break;

                case OpCode.GetIndexSafe:
                    {
                        var obj = _registers[b];
                        var index = _registers[c];
                        _registers[a] = obj == null ? null : AbmlTypeCoercion.GetIndex(obj, index);
                    }
                    break;

                // ═══════════════════════════════════════════════════════════
                // ARITHMETIC
                // ═══════════════════════════════════════════════════════════
                case OpCode.Add:
                    _registers[a] = AbmlTypeCoercion.Add(_registers[b], _registers[c]);
                    break;

                case OpCode.Sub:
                    _registers[a] = AbmlTypeCoercion.Subtract(_registers[b], _registers[c]);
                    break;

                case OpCode.Mul:
                    _registers[a] = AbmlTypeCoercion.Multiply(_registers[b], _registers[c]);
                    break;

                case OpCode.Div:
                    {
                        var divisor = AbmlTypeCoercion.ToDouble(_registers[c]);
                        if (divisor == 0)
                        {
                            throw AbmlDivisionByZeroException.Division();
                        }
                        _registers[a] = AbmlTypeCoercion.Divide(_registers[b], _registers[c]);
                    }
                    break;

                case OpCode.Mod:
                    {
                        var divisor = AbmlTypeCoercion.ToDouble(_registers[c]);
                        if (divisor == 0)
                        {
                            throw AbmlDivisionByZeroException.Modulo();
                        }
                        _registers[a] = AbmlTypeCoercion.Modulo(_registers[b], _registers[c]);
                    }
                    break;

                case OpCode.Neg:
                    _registers[a] = AbmlTypeCoercion.Negate(_registers[b]);
                    break;

                // ═══════════════════════════════════════════════════════════
                // COMPARISON
                // ═══════════════════════════════════════════════════════════
                case OpCode.Eq:
                    _registers[a] = AbmlTypeCoercion.Equals(_registers[b], _registers[c]);
                    break;

                case OpCode.Ne:
                    _registers[a] = !AbmlTypeCoercion.Equals(_registers[b], _registers[c]);
                    break;

                case OpCode.Lt:
                    _registers[a] = AbmlTypeCoercion.Compare(_registers[b], _registers[c]) < 0;
                    break;

                case OpCode.Le:
                    _registers[a] = AbmlTypeCoercion.Compare(_registers[b], _registers[c]) <= 0;
                    break;

                case OpCode.Gt:
                    _registers[a] = AbmlTypeCoercion.Compare(_registers[b], _registers[c]) > 0;
                    break;

                case OpCode.Ge:
                    _registers[a] = AbmlTypeCoercion.Compare(_registers[b], _registers[c]) >= 0;
                    break;

                // ═══════════════════════════════════════════════════════════
                // LOGICAL
                // ═══════════════════════════════════════════════════════════
                case OpCode.Not:
                    _registers[a] = !AbmlTypeCoercion.IsTrue(_registers[b]);
                    break;

                case OpCode.And:
                    _registers[a] = AbmlTypeCoercion.IsTrue(_registers[b]) && AbmlTypeCoercion.IsTrue(_registers[c]);
                    break;

                case OpCode.Or:
                    _registers[a] = AbmlTypeCoercion.IsTrue(_registers[b]) || AbmlTypeCoercion.IsTrue(_registers[c]);
                    break;

                case OpCode.ToBool:
                    _registers[a] = AbmlTypeCoercion.IsTrue(_registers[b]);
                    break;

                // ═══════════════════════════════════════════════════════════
                // CONTROL FLOW
                // ═══════════════════════════════════════════════════════════
                case OpCode.Jump:
                    pc = instr.GetJumpOffset() - 1; // -1 because we increment at end
                    break;

                case OpCode.JumpIfTrue:
                    if (AbmlTypeCoercion.IsTrue(_registers[a]))
                    {
                        pc = instr.GetJumpOffset() - 1;
                    }
                    break;

                case OpCode.JumpIfFalse:
                    if (!AbmlTypeCoercion.IsTrue(_registers[a]))
                    {
                        pc = instr.GetJumpOffset() - 1;
                    }
                    break;

                case OpCode.JumpIfNull:
                    if (_registers[a] == null)
                    {
                        pc = instr.GetJumpOffset() - 1;
                    }
                    break;

                case OpCode.JumpIfNotNull:
                    if (_registers[a] != null)
                    {
                        pc = instr.GetJumpOffset() - 1;
                    }
                    break;

                // ═══════════════════════════════════════════════════════════
                // FUNCTIONS
                // ═══════════════════════════════════════════════════════════
                case OpCode.CallArgs:
                    // Just store arg count for next Call instruction
                    // A = argCount, we'll read it from the next Call
                    // This is handled by reading the previous instruction in Call
                    break;

                case OpCode.Call:
                    {
                        // Look at previous instruction for CallArgs to get arg count
                        var argCount = 0;
                        if (pc > 0 && code[pc - 1].Op == OpCode.CallArgs)
                        {
                            argCount = code[pc - 1].A;
                        }

                        var funcName = (string)constants[b];
                        var argStart = c;  // Args start at R[C]

                        // Collect arguments from registers R[C]..R[C+argCount-1]
                        var args = new object?[argCount];
                        for (var i = 0; i < argCount; i++)
                        {
                            args[i] = _registers[argStart + i];
                        }

                        _registers[a] = _functions.Invoke(funcName, args);
                    }
                    break;

                // ═══════════════════════════════════════════════════════════
                // NULL HANDLING
                // ═══════════════════════════════════════════════════════════
                case OpCode.Coalesce:
                    _registers[a] = _registers[b] ?? _registers[c];
                    break;

                // ═══════════════════════════════════════════════════════════
                // STRING
                // ═══════════════════════════════════════════════════════════
                case OpCode.In:
                    _registers[a] = AbmlTypeCoercion.Contains(_registers[c], _registers[b]);
                    break;

                case OpCode.Concat:
                    _registers[a] = AbmlTypeCoercion.Concat(_registers[b], _registers[c]);
                    break;

                // ═══════════════════════════════════════════════════════════
                // RESULT
                // ═══════════════════════════════════════════════════════════
                case OpCode.Return:
                    return _registers[a];

                default:
                    throw new AbmlRuntimeException($"Unknown opcode: {op}");
            }

            pc++;
        }

        // If we reach here without a Return, return null
        return null;
    }

    /// <summary>
    /// Gets a debug trace of execution.
    /// </summary>
    public string GetDebugTrace(CompiledExpression expression, IVariableScope scope)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(scope);

        var trace = new System.Text.StringBuilder();
        trace.AppendLine($"Executing: {expression.SourceText}");

        var code = expression.Code;
        var constants = expression.Constants;
        var registerCount = expression.RegisterCount;

        Array.Clear(_registers, 0, registerCount);

        var pc = 0;
        while (pc < code.Length)
        {
            var instr = code[pc];
            trace.Append($"  [{pc}] {instr}");

            // Execute instruction (simplified - just show registers after key ops)
            switch (instr.Op)
            {
                case OpCode.Return:
                    trace.AppendLine($" -> {FormatValue(_registers[instr.A])}");
                    return trace.ToString();

                default:
                    // Execute and show result register
                    ExecuteSingleInstruction(instr, constants, scope);
                    trace.AppendLine($" -> R{instr.A}={FormatValue(_registers[instr.A])}");
                    break;
            }

            pc++;
        }

        return trace.ToString();
    }

    private void ExecuteSingleInstruction(Instruction instr, object[] constants, IVariableScope scope)
    {
        var a = instr.A;
        var b = instr.B;
        var c = instr.C;

        switch (instr.Op)
        {
            // LOADS
            case OpCode.LoadConst:
                _registers[a] = constants[b];
                break;
            case OpCode.LoadVar:
                _registers[a] = scope.GetValue((string)constants[b]);
                break;
            case OpCode.LoadNull:
                _registers[a] = null;
                break;
            case OpCode.LoadTrue:
                _registers[a] = true;
                break;
            case OpCode.LoadFalse:
                _registers[a] = false;
                break;
            case OpCode.Move:
                _registers[a] = _registers[b];
                break;

            // PROPERTY ACCESS
            case OpCode.GetProp:
            case OpCode.GetPropSafe:
                _registers[a] = _registers[b] == null ? null : AbmlTypeCoercion.GetProperty(_registers[b], (string)constants[c]);
                break;
            case OpCode.GetIndex:
            case OpCode.GetIndexSafe:
                _registers[a] = _registers[b] == null ? null : AbmlTypeCoercion.GetIndex(_registers[b], _registers[c]);
                break;

            // ARITHMETIC
            case OpCode.Add:
                _registers[a] = AbmlTypeCoercion.Add(_registers[b], _registers[c]);
                break;
            case OpCode.Sub:
                _registers[a] = AbmlTypeCoercion.Subtract(_registers[b], _registers[c]);
                break;
            case OpCode.Mul:
                _registers[a] = AbmlTypeCoercion.Multiply(_registers[b], _registers[c]);
                break;
            case OpCode.Div:
                _registers[a] = AbmlTypeCoercion.Divide(_registers[b], _registers[c]);
                break;
            case OpCode.Mod:
                _registers[a] = AbmlTypeCoercion.Modulo(_registers[b], _registers[c]);
                break;
            case OpCode.Neg:
                _registers[a] = AbmlTypeCoercion.Negate(_registers[b]);
                break;

            // COMPARISON
            case OpCode.Eq:
                _registers[a] = AbmlTypeCoercion.Equals(_registers[b], _registers[c]);
                break;
            case OpCode.Ne:
                _registers[a] = !AbmlTypeCoercion.Equals(_registers[b], _registers[c]);
                break;
            case OpCode.Lt:
                _registers[a] = AbmlTypeCoercion.Compare(_registers[b], _registers[c]) < 0;
                break;
            case OpCode.Le:
                _registers[a] = AbmlTypeCoercion.Compare(_registers[b], _registers[c]) <= 0;
                break;
            case OpCode.Gt:
                _registers[a] = AbmlTypeCoercion.Compare(_registers[b], _registers[c]) > 0;
                break;
            case OpCode.Ge:
                _registers[a] = AbmlTypeCoercion.Compare(_registers[b], _registers[c]) >= 0;
                break;

            // LOGICAL
            case OpCode.Not:
                _registers[a] = !AbmlTypeCoercion.IsTrue(_registers[b]);
                break;
            case OpCode.And:
                _registers[a] = AbmlTypeCoercion.IsTrue(_registers[b]) && AbmlTypeCoercion.IsTrue(_registers[c]);
                break;
            case OpCode.Or:
                _registers[a] = AbmlTypeCoercion.IsTrue(_registers[b]) || AbmlTypeCoercion.IsTrue(_registers[c]);
                break;
            case OpCode.ToBool:
                _registers[a] = AbmlTypeCoercion.IsTrue(_registers[b]);
                break;

            // NULL HANDLING
            case OpCode.Coalesce:
                _registers[a] = _registers[b] ?? _registers[c];
                break;

            // STRING/MEMBERSHIP
            case OpCode.In:
                _registers[a] = AbmlTypeCoercion.Contains(_registers[c], _registers[b]);
                break;
            case OpCode.Concat:
                _registers[a] = AbmlTypeCoercion.Concat(_registers[b], _registers[c]);
                break;

            // FUNCTIONS
            case OpCode.CallArgs:
                // No-op for trace, arg count is read by Call
                break;
            case OpCode.Call:
                {
                    var funcName = (string)constants[b];
                    var argStart = c;
                    // For trace, get arg count from previous CallArgs if present
                    var argCount = 0;
                    // Note: In trace mode we can't easily look back, assume 0 args for display
                    var args = new object?[argCount];
                    for (var i = 0; i < argCount; i++)
                    {
                        args[i] = _registers[argStart + i];
                    }
                    _registers[a] = _functions.Invoke(funcName, args);
                }
                break;

            // CONTROL FLOW - no state change in trace mode
            case OpCode.Jump:
            case OpCode.JumpIfTrue:
            case OpCode.JumpIfFalse:
            case OpCode.JumpIfNull:
            case OpCode.JumpIfNotNull:
            case OpCode.Return:
                break;
        }
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        bool b => b ? "true" : "false",
        _ => value.ToString() ?? "null"
    };
}
