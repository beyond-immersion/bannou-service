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
        // 2. convert to bool: And R0, R0, R0 (IsTrue(left))
        // 3. if false, jump to END (result already false)
        // 4. eval right -> R1
        // 5. convert to bool: And R0, R1, R1 (IsTrue(right))
        // 6. END: result in R0

        var leftReg = node.Left.Accept(this);

        // Convert left to boolean: And R0, R0, R0 produces IsTrue(left)
        _builder.Emit(OpCode.And, leftReg, leftReg, leftReg);

        // Short-circuit: if false, skip right evaluation (result is already false)
        var jumpIfFalseIdx = _builder.Emit(OpCode.JumpIfFalse, leftReg, 0, 0);

        var rightReg = node.Right.Accept(this);

        // Convert right to boolean and store in leftReg
        _builder.Emit(OpCode.And, leftReg, rightReg, rightReg);
        _builder.Registers.Free(rightReg);

        // Patch the jump to here
        var endIdx = _builder.CodeLength;
        _builder.PatchJump(jumpIfFalseIdx, endIdx);

        return leftReg;
    }

    private byte CompileShortCircuitOr(BinaryNode node)
    {
        // left || right (always returns boolean):
        // 1. eval left -> R0
        // 2. convert to bool: Or R0, R0, R0 (IsTrue(left))
        // 3. if true, jump to END (result already true)
        // 4. eval right -> R1
        // 5. convert to bool: Or R0, R1, R1 (IsTrue(right))
        // 6. END: result in R0

        var leftReg = node.Left.Accept(this);

        // Convert left to boolean: Or R0, R0, R0 produces IsTrue(left)
        _builder.Emit(OpCode.Or, leftReg, leftReg, leftReg);

        // Short-circuit: if true, skip right evaluation (result is already true)
        var jumpIfTrueIdx = _builder.Emit(OpCode.JumpIfTrue, leftReg, 0, 0);

        var rightReg = node.Right.Accept(this);

        // Convert right to boolean and store in leftReg
        _builder.Emit(OpCode.Or, leftReg, rightReg, rightReg);
        _builder.Registers.Free(rightReg);

        // Patch the jump to here
        var endIdx = _builder.CodeLength;
        _builder.PatchJump(jumpIfTrueIdx, endIdx);

        return leftReg;
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

        // Compile each argument first (they'll go into arbitrary registers)
        var argRegisters = new byte[argCount];
        for (var i = 0; i < argCount; i++)
        {
            argRegisters[i] = node.Arguments[i].Accept(this);
        }

        // Move arguments to R0..R(argCount-1) as required by Call convention
        // We need to be careful about overwrites - copy in reverse if there are conflicts
        for (var i = 0; i < argCount; i++)
        {
            var targetReg = (byte)i;
            var sourceReg = argRegisters[i];
            if (sourceReg != targetReg)
            {
                _builder.Emit(OpCode.Move, targetReg, sourceReg, 0);
            }
        }

        // Free the original argument registers (those above argCount)
        for (var i = 0; i < argCount; i++)
        {
            if (argRegisters[i] >= argCount)
            {
                _builder.Registers.Free(argRegisters[i]);
            }
        }

        // Allocate destination register (must be >= argCount to avoid conflict)
        var destReg = _builder.Registers.Allocate();
        var funcIdx = _builder.Constants.Add(node.FunctionName);

        // Call instruction: dest = call(funcName, argCount)
        // Convention: args are in R0..R(argCount-1)
        _builder.Emit(OpCode.Call, destReg, funcIdx, (byte)argCount);

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
