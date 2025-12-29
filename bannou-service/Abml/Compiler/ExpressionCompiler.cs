// ═══════════════════════════════════════════════════════════════════════════
// ABML Expression Compiler (AST to Bytecode)
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Compiler.Ast;
using BeyondImmersion.BannouService.Abml.Exceptions;

namespace BeyondImmersion.BannouService.Abml.Compiler;

/// <summary>
/// Compiles ABML expression AST into bytecode.
/// </summary>
public sealed class ExpressionCompiler : IExpressionVisitor<byte>
{
    private readonly ExpressionParser _parser = new();
    private CompiledExpressionBuilder _builder = null!;

    /// <summary>
    /// Compiles an expression string to bytecode.
    /// </summary>
    /// <param name="expression">The expression source text.</param>
    /// <returns>The compiled expression.</returns>
    public CompiledExpression Compile(string expression)
    {
        var ast = _parser.Parse(expression);
        return Compile(ast, expression);
    }

    /// <summary>
    /// Compiles an AST to bytecode.
    /// </summary>
    /// <param name="ast">The expression AST.</param>
    /// <param name="sourceText">The original source text.</param>
    /// <returns>The compiled expression.</returns>
    public CompiledExpression Compile(ExpressionNode ast, string sourceText)
    {
        _builder = new CompiledExpressionBuilder();
        _builder.SetSourceText(sourceText);

        var resultReg = ast.Accept(this);
        _builder.Emit(OpCode.Return, resultReg);

        return _builder.Build();
    }

    /// <inheritdoc/>
    public byte VisitLiteral(LiteralNode node)
    {
        var destReg = _builder.Registers.Allocate();

        if (node.Value == null)
        {
            _builder.Emit(OpCode.LoadNull, destReg);
        }
        else if (node.Value is bool b)
        {
            _builder.Emit(b ? OpCode.LoadTrue : OpCode.LoadFalse, destReg);
        }
        else
        {
            var constIdx = _builder.Constants.Add(node.Value);
            _builder.Emit(OpCode.LoadConst, destReg, constIdx);
        }

        return destReg;
    }

    /// <inheritdoc/>
    public byte VisitVariable(VariableNode node)
    {
        var destReg = _builder.Registers.Allocate();
        var nameIdx = _builder.Constants.Add(node.Name);
        _builder.Emit(OpCode.LoadVar, destReg, nameIdx);
        return destReg;
    }

    /// <inheritdoc/>
    public byte VisitUnary(UnaryNode node)
    {
        var operandReg = node.Operand.Accept(this);
        var destReg = _builder.Registers.Allocate();

        var opcode = node.Operator switch
        {
            UnaryOperator.Not => OpCode.Not,
            UnaryOperator.Negate => OpCode.Neg,
            _ => throw new AbmlCompilationException($"Unknown unary operator: {node.Operator}")
        };

        _builder.Emit(opcode, destReg, operandReg);
        _builder.Registers.Free(operandReg);

        return destReg;
    }

    /// <inheritdoc/>
    public byte VisitBinary(BinaryNode node)
    {
        // Short-circuit evaluation for && and ||
        if (node.Operator == BinaryOperator.And)
        {
            return CompileShortCircuitAnd(node);
        }
        if (node.Operator == BinaryOperator.Or)
        {
            return CompileShortCircuitOr(node);
        }

        var leftReg = node.Left.Accept(this);
        var rightReg = node.Right.Accept(this);
        var destReg = _builder.Registers.Allocate();

        var opcode = node.Operator switch
        {
            BinaryOperator.Add => OpCode.Add,
            BinaryOperator.Subtract => OpCode.Sub,
            BinaryOperator.Multiply => OpCode.Mul,
            BinaryOperator.Divide => OpCode.Div,
            BinaryOperator.Modulo => OpCode.Mod,
            BinaryOperator.Equal => OpCode.Eq,
            BinaryOperator.NotEqual => OpCode.Ne,
            BinaryOperator.LessThan => OpCode.Lt,
            BinaryOperator.LessOrEqual => OpCode.Le,
            BinaryOperator.GreaterThan => OpCode.Gt,
            BinaryOperator.GreaterOrEqual => OpCode.Ge,
            BinaryOperator.In => OpCode.In,
            _ => throw new AbmlCompilationException($"Unknown binary operator: {node.Operator}")
        };

        _builder.Emit(opcode, destReg, leftReg, rightReg);
        _builder.Registers.Free(leftReg);
        _builder.Registers.Free(rightReg);

        return destReg;
    }

    private byte CompileShortCircuitAnd(BinaryNode node)
    {
        // left && right (always returns boolean):
        // 1. eval left -> R0
        // 2. if falsy, jump to FALSE case
        // 3. eval right -> R1
        // 4. convert right to bool -> result
        // 5. jump to END
        // 6. FALSE: load false
        // 7. END:

        var leftReg = node.Left.Accept(this);

        // Short-circuit: if left is falsy, skip right evaluation
        var jumpToFalseIdx = _builder.Emit(OpCode.JumpIfFalse, leftReg, 0, 0);
        _builder.Registers.Free(leftReg);

        // Evaluate right side
        var rightReg = node.Right.Accept(this);
        var resultReg = _builder.Registers.Allocate();

        // Convert right to boolean using explicit ToBool
        _builder.Emit(OpCode.ToBool, resultReg, rightReg);
        _builder.Registers.Free(rightReg);

        // Jump over the false case
        var jumpToEndIdx = _builder.Emit(OpCode.Jump, 0, 0, 0);

        // False case: load false
        var falseCaseIdx = _builder.CodeLength;
        _builder.PatchJump(jumpToFalseIdx, falseCaseIdx);
        _builder.Emit(OpCode.LoadFalse, resultReg);

        // End
        var endIdx = _builder.CodeLength;
        _builder.PatchJump(jumpToEndIdx, endIdx);

        return resultReg;
    }

    private byte CompileShortCircuitOr(BinaryNode node)
    {
        // left || right (always returns boolean):
        // 1. eval left -> R0
        // 2. if truthy, jump to TRUE case
        // 3. eval right -> R1
        // 4. convert right to bool -> result
        // 5. jump to END
        // 6. TRUE: load true
        // 7. END:

        var leftReg = node.Left.Accept(this);

        // Short-circuit: if left is truthy, skip right evaluation
        var jumpToTrueIdx = _builder.Emit(OpCode.JumpIfTrue, leftReg, 0, 0);
        _builder.Registers.Free(leftReg);

        // Evaluate right side
        var rightReg = node.Right.Accept(this);
        var resultReg = _builder.Registers.Allocate();

        // Convert right to boolean using explicit ToBool
        _builder.Emit(OpCode.ToBool, resultReg, rightReg);
        _builder.Registers.Free(rightReg);

        // Jump over the true case
        var jumpToEndIdx = _builder.Emit(OpCode.Jump, 0, 0, 0);

        // True case: load true
        var trueCaseIdx = _builder.CodeLength;
        _builder.PatchJump(jumpToTrueIdx, trueCaseIdx);
        _builder.Emit(OpCode.LoadTrue, resultReg);

        // End
        var endIdx = _builder.CodeLength;
        _builder.PatchJump(jumpToEndIdx, endIdx);

        return resultReg;
    }

    /// <inheritdoc/>
    public byte VisitTernary(TernaryNode node)
    {
        // condition ? then : else
        // eval condition -> R0
        // if !R0 jump to ELSE
        // eval then -> R1
        // jump to END
        // ELSE: eval else -> R1
        // END:

        var condReg = node.Condition.Accept(this);
        var jumpToElseIdx = _builder.Emit(OpCode.JumpIfFalse, condReg, 0, 0);
        _builder.Registers.Free(condReg);

        var thenReg = node.ThenBranch.Accept(this);
        var jumpToEndIdx = _builder.Emit(OpCode.Jump, 0, 0, 0);

        var elseStartIdx = _builder.CodeLength;
        _builder.PatchJump(jumpToElseIdx, elseStartIdx);

        // Free then register before else to allow reuse
        var resultReg = thenReg;
        _builder.Registers.Free(thenReg);

        var elseReg = node.ElseBranch.Accept(this);

        // Ensure result is in same register
        if (elseReg != resultReg)
        {
            resultReg = elseReg;
        }

        var endIdx = _builder.CodeLength;
        _builder.PatchJump(jumpToEndIdx, endIdx);

        return resultReg;
    }

    /// <inheritdoc/>
    public byte VisitPropertyAccess(PropertyAccessNode node)
    {
        var objReg = node.Object.Accept(this);
        var destReg = _builder.Registers.Allocate();
        var propIdx = _builder.Constants.Add(node.PropertyName);

        var opcode = node.IsNullSafe ? OpCode.GetPropSafe : OpCode.GetProp;
        _builder.Emit(opcode, destReg, objReg, propIdx);

        _builder.Registers.Free(objReg);
        return destReg;
    }

    /// <inheritdoc/>
    public byte VisitIndexAccess(IndexAccessNode node)
    {
        var objReg = node.Object.Accept(this);
        var indexReg = node.Index.Accept(this);
        var destReg = _builder.Registers.Allocate();

        var opcode = node.IsNullSafe ? OpCode.GetIndexSafe : OpCode.GetIndex;
        _builder.Emit(opcode, destReg, objReg, indexReg);

        _builder.Registers.Free(objReg);
        _builder.Registers.Free(indexReg);

        return destReg;
    }

    /// <inheritdoc/>
    public byte VisitFunctionCall(FunctionCallNode node)
    {
        var argCount = node.Arguments.Count;

        // For nested calls to work correctly, we need args in contiguous registers
        // that don't conflict with other computations. We use AllocateRange to
        // guarantee a contiguous block, avoiding issues where individual Allocate
        // calls might return non-contiguous registers from the freeList.
        var argStartReg = (byte)0;

        if (argCount > 0)
        {
            // Reserve contiguous block for arguments
            argStartReg = _builder.Registers.AllocateRange(argCount);

            // Compile each argument into its designated register
            for (var i = 0; i < argCount; i++)
            {
                var targetReg = (byte)(argStartReg + i);
                var valueReg = node.Arguments[i].Accept(this);
                if (valueReg != targetReg)
                {
                    _builder.Emit(OpCode.Move, targetReg, valueReg, 0);
                    _builder.Registers.Free(valueReg);
                }
            }
        }

        // Allocate destination register
        var destReg = _builder.Registers.Allocate();
        var funcIdx = _builder.Constants.Add(node.FunctionName);

        // Emit CallArgs to specify arg count, then Call with start register
        _builder.Emit(OpCode.CallArgs, (byte)argCount);
        _builder.Emit(OpCode.Call, destReg, funcIdx, argStartReg);

        // Free argument registers after call
        if (argCount > 0)
        {
            _builder.Registers.FreeRange(argStartReg, argCount);
        }

        return destReg;
    }

    /// <inheritdoc/>
    public byte VisitNullCoalesce(NullCoalesceNode node)
    {
        // left ?? right
        // eval left -> R0
        // if R0 != null jump to END
        // eval right -> R0
        // END:

        var leftReg = node.Left.Accept(this);
        var jumpIfNotNullIdx = _builder.Emit(OpCode.JumpIfNotNull, leftReg, 0, 0);

        _builder.Registers.Free(leftReg);
        var rightReg = node.Right.Accept(this);

        var endIdx = _builder.CodeLength;
        _builder.PatchJump(jumpIfNotNullIdx, endIdx);

        return rightReg;
    }
}
