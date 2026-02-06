// ═══════════════════════════════════════════════════════════════════════════
// ABML Expression Compiler (AST to Bytecode)
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorCompiler.Compiler;
using BeyondImmersion.Bannou.BehaviorCompiler.Compiler.Ast;
using BeyondImmersion.Bannou.BehaviorCompiler.Exceptions;
using BeyondImmersion.BannouService.Abml.Exceptions;

namespace BeyondImmersion.BannouService.Abml.Compiler;

/// <summary>
/// Compiles ABML expression AST into bytecode.
/// Thread-safe: each compilation uses its own state.
/// </summary>
public sealed class ExpressionCompiler
{
    private readonly ExpressionParser _parser = new();

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
        var visitor = new CompilerVisitor();
        visitor.Builder.SetSourceText(sourceText);

        var resultReg = ast.Accept(visitor);
        visitor.Builder.Emit(OpCode.Return, resultReg);

        return visitor.Builder.Build();
    }
}

/// <summary>
/// Internal visitor that holds compilation state.
/// A new instance is created for each compilation to ensure thread safety.
/// </summary>
internal sealed class CompilerVisitor : IExpressionVisitor<byte>
{
    public CompiledExpressionBuilder Builder { get; } = new();

    /// <inheritdoc/>
    public byte VisitLiteral(LiteralNode node)
    {
        var destReg = Builder.Registers.Allocate();

        if (node.Value == null)
        {
            Builder.Emit(OpCode.LoadNull, destReg);
        }
        else if (node.Value is bool b)
        {
            Builder.Emit(b ? OpCode.LoadTrue : OpCode.LoadFalse, destReg);
        }
        else
        {
            var constIdx = Builder.Constants.Add(node.Value);
            Builder.Emit(OpCode.LoadConst, destReg, constIdx);
        }

        return destReg;
    }

    /// <inheritdoc/>
    public byte VisitVariable(VariableNode node)
    {
        var destReg = Builder.Registers.Allocate();
        var nameIdx = Builder.Constants.Add(node.Name);
        Builder.Emit(OpCode.LoadVar, destReg, nameIdx);
        return destReg;
    }

    /// <inheritdoc/>
    public byte VisitUnary(UnaryNode node)
    {
        var operandReg = node.Operand.Accept(this);
        var destReg = Builder.Registers.Allocate();

        var opcode = node.Operator switch
        {
            UnaryOperator.Not => OpCode.Not,
            UnaryOperator.Negate => OpCode.Neg,
            _ => throw new AbmlCompilationException($"Unknown unary operator: {node.Operator}")
        };

        Builder.Emit(opcode, destReg, operandReg);
        Builder.Registers.Free(operandReg);

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
        var destReg = Builder.Registers.Allocate();

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

        Builder.Emit(opcode, destReg, leftReg, rightReg);
        Builder.Registers.Free(leftReg);
        Builder.Registers.Free(rightReg);

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
        var jumpToFalseIdx = Builder.Emit(OpCode.JumpIfFalse, leftReg, 0, 0);
        Builder.Registers.Free(leftReg);

        // Evaluate right side
        var rightReg = node.Right.Accept(this);
        var resultReg = Builder.Registers.Allocate();

        // Convert right to boolean using explicit ToBool
        Builder.Emit(OpCode.ToBool, resultReg, rightReg);
        Builder.Registers.Free(rightReg);

        // Jump over the false case
        var jumpToEndIdx = Builder.Emit(OpCode.Jump, 0, 0, 0);

        // False case: load false
        var falseCaseIdx = Builder.CodeLength;
        Builder.PatchJump(jumpToFalseIdx, falseCaseIdx);
        Builder.Emit(OpCode.LoadFalse, resultReg);

        // End
        var endIdx = Builder.CodeLength;
        Builder.PatchJump(jumpToEndIdx, endIdx);

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
        var jumpToTrueIdx = Builder.Emit(OpCode.JumpIfTrue, leftReg, 0, 0);
        Builder.Registers.Free(leftReg);

        // Evaluate right side
        var rightReg = node.Right.Accept(this);
        var resultReg = Builder.Registers.Allocate();

        // Convert right to boolean using explicit ToBool
        Builder.Emit(OpCode.ToBool, resultReg, rightReg);
        Builder.Registers.Free(rightReg);

        // Jump over the true case
        var jumpToEndIdx = Builder.Emit(OpCode.Jump, 0, 0, 0);

        // True case: load true
        var trueCaseIdx = Builder.CodeLength;
        Builder.PatchJump(jumpToTrueIdx, trueCaseIdx);
        Builder.Emit(OpCode.LoadTrue, resultReg);

        // End
        var endIdx = Builder.CodeLength;
        Builder.PatchJump(jumpToEndIdx, endIdx);

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
        var jumpToElseIdx = Builder.Emit(OpCode.JumpIfFalse, condReg, 0, 0);
        Builder.Registers.Free(condReg);

        var thenReg = node.ThenBranch.Accept(this);
        var jumpToEndIdx = Builder.Emit(OpCode.Jump, 0, 0, 0);

        var elseStartIdx = Builder.CodeLength;
        Builder.PatchJump(jumpToElseIdx, elseStartIdx);

        // Free then register before else to allow reuse
        var resultReg = thenReg;
        Builder.Registers.Free(thenReg);

        var elseReg = node.ElseBranch.Accept(this);

        // Ensure result is in same register
        if (elseReg != resultReg)
        {
            resultReg = elseReg;
        }

        var endIdx = Builder.CodeLength;
        Builder.PatchJump(jumpToEndIdx, endIdx);

        return resultReg;
    }

    /// <inheritdoc/>
    public byte VisitPropertyAccess(PropertyAccessNode node)
    {
        var objReg = node.Object.Accept(this);
        var destReg = Builder.Registers.Allocate();
        var propIdx = Builder.Constants.Add(node.PropertyName);

        var opcode = node.IsNullSafe ? OpCode.GetPropSafe : OpCode.GetProp;
        Builder.Emit(opcode, destReg, objReg, propIdx);

        Builder.Registers.Free(objReg);
        return destReg;
    }

    /// <inheritdoc/>
    public byte VisitIndexAccess(IndexAccessNode node)
    {
        var objReg = node.Object.Accept(this);
        var indexReg = node.Index.Accept(this);
        var destReg = Builder.Registers.Allocate();

        var opcode = node.IsNullSafe ? OpCode.GetIndexSafe : OpCode.GetIndex;
        Builder.Emit(opcode, destReg, objReg, indexReg);

        Builder.Registers.Free(objReg);
        Builder.Registers.Free(indexReg);

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
            argStartReg = Builder.Registers.AllocateRange(argCount);

            // Compile each argument into its designated register
            for (var i = 0; i < argCount; i++)
            {
                var targetReg = (byte)(argStartReg + i);
                var valueReg = node.Arguments[i].Accept(this);
                if (valueReg != targetReg)
                {
                    Builder.Emit(OpCode.Move, targetReg, valueReg, 0);
                    Builder.Registers.Free(valueReg);
                }
            }
        }

        // Allocate destination register
        var destReg = Builder.Registers.Allocate();
        var funcIdx = Builder.Constants.Add(node.FunctionName);

        // Emit CallArgs to specify arg count, then Call with start register
        Builder.Emit(OpCode.CallArgs, (byte)argCount);
        Builder.Emit(OpCode.Call, destReg, funcIdx, argStartReg);

        // Free argument registers after call
        if (argCount > 0)
        {
            Builder.Registers.FreeRange(argStartReg, argCount);
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
        var jumpIfNotNullIdx = Builder.Emit(OpCode.JumpIfNotNull, leftReg, 0, 0);

        Builder.Registers.Free(leftReg);
        var rightReg = node.Right.Accept(this);

        var endIdx = Builder.CodeLength;
        Builder.PatchJump(jumpIfNotNullIdx, endIdx);

        return rightReg;
    }

    /// <inheritdoc/>
    public byte VisitArrayLiteral(ArrayLiteralNode node)
    {
        // For cloud-side execution, we build the array at runtime.
        // Each element is evaluated and stored, then combined into an array.
        var destReg = Builder.Registers.Allocate();

        if (node.Elements.Count == 0)
        {
            // Empty array - load as constant
            var emptyArrayIdx = Builder.Constants.Add(Array.Empty<object>());
            Builder.Emit(OpCode.LoadConst, destReg, emptyArrayIdx);
            return destReg;
        }

        // For static arrays (all literals), we can pre-build the array
        if (AllLiterals(node.Elements, out var values))
        {
            var arrayIdx = Builder.Constants.Add(values);
            Builder.Emit(OpCode.LoadConst, destReg, arrayIdx);
            return destReg;
        }

        // For dynamic arrays, we need runtime array building
        // This is more complex - for now, we only support all-literal arrays
        throw new AbmlCompilationException(
            "Array literals with non-literal elements are not supported. " +
            "Use only literals in array expressions (e.g., ['a', 'b', 1, 2]).");
    }

    /// <summary>
    /// Checks if all elements are literals and extracts their values.
    /// </summary>
    private static bool AllLiterals(IReadOnlyList<ExpressionNode> elements, out object?[] values)
    {
        values = new object?[elements.Count];
        for (var i = 0; i < elements.Count; i++)
        {
            if (elements[i] is LiteralNode literal)
            {
                values[i] = literal.Value;
            }
            else
            {
                values = Array.Empty<object?>();
                return false;
            }
        }
        return true;
    }
}
