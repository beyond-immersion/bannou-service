// =============================================================================
// Stack Expression Compiler
// Compiles expression AST to stack-based bytecode.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Compiler.Expressions.Ast;
using BeyondImmersion.Bannou.BehaviorCompiler.Runtime;

namespace BeyondImmersion.Bannou.BehaviorCompiler.Compiler.Expressions;

/// <summary>
/// Compiles expression AST nodes to stack-based bytecode.
/// Reuses the existing ExpressionParser for AST generation.
/// </summary>
public sealed class StackExpressionCompiler : IExpressionVisitor<Unit>
{
    private readonly CompilationContext _context;
    private readonly ExpressionParser _parser = new();

    /// <summary>
    /// Creates a new stack expression compiler.
    /// </summary>
    /// <param name="context">The compilation context.</param>
    public StackExpressionCompiler(CompilationContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Compiles an expression string, leaving result on stack.
    /// </summary>
    /// <param name="expression">The expression source text.</param>
    public void Compile(string expression)
    {
        var ast = _parser.Parse(expression);
        ast.Accept(this);
    }

    /// <summary>
    /// Compiles an AST node, leaving result on stack.
    /// </summary>
    /// <param name="node">The AST node.</param>
    public void CompileNode(ExpressionNode node)
    {
        node.Accept(this);
    }

    /// <inheritdoc/>
    public Unit VisitLiteral(LiteralNode node)
    {
        var emitter = _context.Emitter;

        if (node.Value == null)
        {
            // Push 0 for null
            var idx = _context.Constants.GetOrAdd(0.0);
            emitter.EmitPushConst(idx);
        }
        else if (node.Value is bool b)
        {
            var idx = _context.Constants.GetOrAdd(b ? 1.0 : 0.0);
            emitter.EmitPushConst(idx);
        }
        else if (node.Value is double d)
        {
            var idx = _context.Constants.GetOrAdd(d);
            emitter.EmitPushConst(idx);
        }
        else if (node.Value is int i)
        {
            var idx = _context.Constants.GetOrAdd((double)i);
            emitter.EmitPushConst(idx);
        }
        else if (node.Value is long l)
        {
            var idx = _context.Constants.GetOrAdd((double)l);
            emitter.EmitPushConst(idx);
        }
        else if (node.Value is float f)
        {
            var idx = _context.Constants.GetOrAdd((double)f);
            emitter.EmitPushConst(idx);
        }
        else if (node.Value is string s)
        {
            // For strings, push the string table index
            var strIdx = _context.Strings.GetOrAdd(s);
            emitter.EmitPushString(strIdx);
        }
        else
        {
            // Try to convert to double
            if (double.TryParse(node.Value.ToString(), out var parsed))
            {
                var idx = _context.Constants.GetOrAdd(parsed);
                emitter.EmitPushConst(idx);
            }
            else
            {
                _context.AddError($"Unsupported literal type: {node.Value.GetType().Name}");
                emitter.EmitPushConst(_context.Constants.GetOrAdd(0.0));
            }
        }

        return Unit.Value;
    }

    /// <inheritdoc/>
    public Unit VisitVariable(VariableNode node)
    {
        var emitter = _context.Emitter;
        var name = node.Name;

        // Check input variables first
        if (_context.TryGetInput(name, out var inputIdx))
        {
            emitter.EmitPushInput(inputIdx);
            return Unit.Value;
        }

        // Check local variables
        if (_context.TryGetLocal(name, out var localIdx))
        {
            emitter.EmitPushLocal(localIdx);
            return Unit.Value;
        }

        // Unknown variable - treat as input with auto-registration
        // This allows forward references during compilation
        var newInputIdx = _context.RegisterInput(name);
        emitter.EmitPushInput(newInputIdx);

        return Unit.Value;
    }

    /// <inheritdoc/>
    public Unit VisitUnary(UnaryNode node)
    {
        var emitter = _context.Emitter;

        // Compile operand (pushes to stack)
        node.Operand.Accept(this);

        // Apply unary operator
        switch (node.Operator)
        {
            case UnaryOperator.Not:
                emitter.Emit(BehaviorOpcode.Not);
                break;
            case UnaryOperator.Negate:
                emitter.Emit(BehaviorOpcode.Neg);
                break;
            default:
                _context.AddError($"Unknown unary operator: {node.Operator}");
                break;
        }

        return Unit.Value;
    }

    /// <inheritdoc/>
    public Unit VisitBinary(BinaryNode node)
    {
        var emitter = _context.Emitter;

        // Handle short-circuit operators specially
        if (node.Operator == BinaryOperator.And)
        {
            CompileShortCircuitAnd(node);
            return Unit.Value;
        }

        if (node.Operator == BinaryOperator.Or)
        {
            CompileShortCircuitOr(node);
            return Unit.Value;
        }

        // Handle 'in' operator with static set expansion
        if (node.Operator == BinaryOperator.In)
        {
            CompileInOperator(node);
            return Unit.Value;
        }

        // Compile left operand (pushes to stack)
        node.Left.Accept(this);

        // Compile right operand (pushes to stack)
        node.Right.Accept(this);

        // Apply binary operator (pops 2, pushes 1)
        var opcode = node.Operator switch
        {
            BinaryOperator.Add => BehaviorOpcode.Add,
            BinaryOperator.Subtract => BehaviorOpcode.Sub,
            BinaryOperator.Multiply => BehaviorOpcode.Mul,
            BinaryOperator.Divide => BehaviorOpcode.Div,
            BinaryOperator.Modulo => BehaviorOpcode.Mod,
            BinaryOperator.Equal => BehaviorOpcode.Eq,
            BinaryOperator.NotEqual => BehaviorOpcode.Ne,
            BinaryOperator.LessThan => BehaviorOpcode.Lt,
            BinaryOperator.LessOrEqual => BehaviorOpcode.Le,
            BinaryOperator.GreaterThan => BehaviorOpcode.Gt,
            BinaryOperator.GreaterOrEqual => BehaviorOpcode.Ge,
            BinaryOperator.In => throw new NotSupportedException("'in' operator not supported in behavior bytecode"),
            _ => throw new NotSupportedException($"Unknown binary operator: {node.Operator}")
        };

        emitter.Emit(opcode);
        return Unit.Value;
    }

    /// <summary>
    /// Compiles the 'in' operator by expanding static array literals to OR chains.
    /// For example: x in ['a', 'b', 'c'] becomes x == 'a' || x == 'b' || x == 'c'
    /// </summary>
    private void CompileInOperator(BinaryNode node)
    {
        // Check if RHS is an array literal with static elements
        if (node.Right is not ArrayLiteralNode arrayNode)
        {
            _context.AddError(
                "'in' operator requires an array literal on the right-hand side in bytecode. " +
                "For dynamic collections, use cloud-side execution or pre-compute a boolean input flag.");
            // Push false as fallback
            var falseIdx = _context.Constants.GetOrAdd(0.0);
            _context.Emitter.EmitPushConst(falseIdx);
            return;
        }

        var elements = arrayNode.Elements;

        // Empty array - always false
        if (elements.Count == 0)
        {
            var falseIdx = _context.Constants.GetOrAdd(0.0);
            _context.Emitter.EmitPushConst(falseIdx);
            return;
        }

        // Limit expansion to prevent bytecode bloat (max 16 elements)
        const int maxElements = 16;
        if (elements.Count > maxElements)
        {
            _context.AddError(
                $"'in' operator with array literal exceeds maximum of {maxElements} elements. " +
                "Consider using multiple conditions or pre-computing a boolean input flag.");
            var falseIdx = _context.Constants.GetOrAdd(0.0);
            _context.Emitter.EmitPushConst(falseIdx);
            return;
        }

        // Single element - just equality check
        if (elements.Count == 1)
        {
            node.Left.Accept(this);
            elements[0].Accept(this);
            _context.Emitter.Emit(BehaviorOpcode.Eq);
            return;
        }

        // Multiple elements - expand to OR chain with short-circuit evaluation
        // x in [a, b, c] => x == a || x == b || x == c
        //
        // We need to evaluate the left-hand side once and compare against each element.
        // For efficiency, we store LHS in a local variable to avoid re-evaluation.

        var emitter = _context.Emitter;
        var labels = _context.Labels;

        // Allocate a local for the LHS value (to avoid re-evaluation)
        var lhsLocal = _context.GetOrAllocateLocal($"__in_lhs_{labels.AllocateLabel()}");

        // Compile LHS and store in local
        node.Left.Accept(this);
        emitter.EmitStoreLocal(lhsLocal);

        var endLabel = labels.AllocateLabel();
        var trueLabel = labels.AllocateLabel();

        // For each element except the last, check equality and jump to true if match
        for (var i = 0; i < elements.Count - 1; i++)
        {
            // Push LHS from local
            emitter.EmitPushLocal(lhsLocal);
            // Compile element
            elements[i].Accept(this);
            // Compare
            emitter.Emit(BehaviorOpcode.Eq);
            // If true, jump to true label (short-circuit)
            emitter.EmitJmpIf(trueLabel);
        }

        // Last element - just check equality, result stays on stack
        emitter.EmitPushLocal(lhsLocal);
        elements[^1].Accept(this);
        emitter.Emit(BehaviorOpcode.Eq);
        // Jump to end (result is on stack)
        emitter.EmitJmp(endLabel);

        // True label - push true value
        emitter.DefineLabel(trueLabel);
        var trueIdx = _context.Constants.GetOrAdd(1.0);
        emitter.EmitPushConst(trueIdx);

        // End label
        emitter.DefineLabel(endLabel);
    }

    private void CompileShortCircuitAnd(BinaryNode node)
    {
        var emitter = _context.Emitter;
        var labels = _context.Labels;

        var endLabel = labels.AllocateLabel();

        // Compile left operand
        node.Left.Accept(this);

        // Duplicate for test and result
        emitter.Emit(BehaviorOpcode.Dup);

        // If falsy, skip right operand (result is already on stack)
        emitter.EmitJmpUnless(endLabel);

        // Pop the duplicate (we'll use right's result)
        emitter.Emit(BehaviorOpcode.Pop);

        // Compile right operand
        node.Right.Accept(this);

        // End label
        emitter.DefineLabel(endLabel);
    }

    private void CompileShortCircuitOr(BinaryNode node)
    {
        var emitter = _context.Emitter;
        var labels = _context.Labels;

        var endLabel = labels.AllocateLabel();

        // Compile left operand
        node.Left.Accept(this);

        // Duplicate for test and result
        emitter.Emit(BehaviorOpcode.Dup);

        // If truthy, skip right operand (result is already on stack)
        emitter.EmitJmpIf(endLabel);

        // Pop the duplicate (we'll use right's result)
        emitter.Emit(BehaviorOpcode.Pop);

        // Compile right operand
        node.Right.Accept(this);

        // End label
        emitter.DefineLabel(endLabel);
    }

    /// <inheritdoc/>
    public Unit VisitTernary(TernaryNode node)
    {
        var emitter = _context.Emitter;
        var labels = _context.Labels;

        var elseLabel = labels.AllocateLabel();
        var endLabel = labels.AllocateLabel();

        // Compile condition
        node.Condition.Accept(this);

        // Jump to else if falsy
        emitter.EmitJmpUnless(elseLabel);

        // Compile then branch
        node.ThenBranch.Accept(this);
        emitter.EmitJmp(endLabel);

        // Else branch
        emitter.DefineLabel(elseLabel);
        node.ElseBranch.Accept(this);

        // End
        emitter.DefineLabel(endLabel);

        return Unit.Value;
    }

    /// <inheritdoc/>
    public Unit VisitPropertyAccess(PropertyAccessNode node)
    {
        // Property access is not directly supported in the simple stack-based VM
        // For behavior models, we flatten nested structures at compile time
        _context.AddError($"Property access '{node.PropertyName}' not supported in behavior bytecode. Use flat variable names.");

        // Push 0 as placeholder
        var idx = _context.Constants.GetOrAdd(0.0);
        _context.Emitter.EmitPushConst(idx);

        return Unit.Value;
    }

    /// <inheritdoc/>
    public Unit VisitIndexAccess(IndexAccessNode node)
    {
        // Index access is not directly supported
        _context.AddError("Index access not supported in behavior bytecode. Use flat variable names.");

        // Push 0 as placeholder
        var idx = _context.Constants.GetOrAdd(0.0);
        _context.Emitter.EmitPushConst(idx);

        return Unit.Value;
    }

    /// <inheritdoc/>
    public Unit VisitFunctionCall(FunctionCallNode node)
    {
        var emitter = _context.Emitter;
        var name = node.FunctionName.ToLowerInvariant();

        switch (name)
        {
            case "rand" or "random":
                if (node.Arguments.Count == 0)
                {
                    emitter.Emit(BehaviorOpcode.Rand);
                }
                else if (node.Arguments.Count == 2)
                {
                    // rand(min, max) -> RandInt
                    node.Arguments[0].Accept(this);
                    node.Arguments[1].Accept(this);
                    emitter.Emit(BehaviorOpcode.RandInt);
                }
                else
                {
                    _context.AddError($"rand() expects 0 or 2 arguments, got {node.Arguments.Count}");
                    emitter.Emit(BehaviorOpcode.Rand);
                }
                break;

            case "lerp":
                if (node.Arguments.Count == 3)
                {
                    node.Arguments[0].Accept(this); // a
                    node.Arguments[1].Accept(this); // b
                    node.Arguments[2].Accept(this); // t
                    emitter.Emit(BehaviorOpcode.Lerp);
                }
                else
                {
                    _context.AddError($"lerp() expects 3 arguments, got {node.Arguments.Count}");
                    emitter.EmitPushConst(_context.Constants.GetOrAdd(0.0));
                }
                break;

            case "clamp":
                if (node.Arguments.Count == 3)
                {
                    node.Arguments[0].Accept(this); // value
                    node.Arguments[1].Accept(this); // min
                    node.Arguments[2].Accept(this); // max
                    emitter.Emit(BehaviorOpcode.Clamp);
                }
                else
                {
                    _context.AddError($"clamp() expects 3 arguments, got {node.Arguments.Count}");
                    emitter.EmitPushConst(_context.Constants.GetOrAdd(0.0));
                }
                break;

            case "abs":
                CompileUnaryFunction(node, BehaviorOpcode.Abs);
                break;

            case "floor":
                CompileUnaryFunction(node, BehaviorOpcode.Floor);
                break;

            case "ceil" or "ceiling":
                CompileUnaryFunction(node, BehaviorOpcode.Ceil);
                break;

            case "min":
                CompileBinaryFunction(node, BehaviorOpcode.Min);
                break;

            case "max":
                CompileBinaryFunction(node, BehaviorOpcode.Max);
                break;

            default:
                _context.AddError($"Unknown function: {node.FunctionName}");
                emitter.EmitPushConst(_context.Constants.GetOrAdd(0.0));
                break;
        }

        return Unit.Value;
    }

    private void CompileUnaryFunction(FunctionCallNode node, BehaviorOpcode opcode)
    {
        if (node.Arguments.Count == 1)
        {
            node.Arguments[0].Accept(this);
            _context.Emitter.Emit(opcode);
        }
        else
        {
            _context.AddError($"{node.FunctionName}() expects 1 argument, got {node.Arguments.Count}");
            _context.Emitter.EmitPushConst(_context.Constants.GetOrAdd(0.0));
        }
    }

    private void CompileBinaryFunction(FunctionCallNode node, BehaviorOpcode opcode)
    {
        if (node.Arguments.Count == 2)
        {
            node.Arguments[0].Accept(this);
            node.Arguments[1].Accept(this);
            _context.Emitter.Emit(opcode);
        }
        else
        {
            _context.AddError($"{node.FunctionName}() expects 2 arguments, got {node.Arguments.Count}");
            _context.Emitter.EmitPushConst(_context.Constants.GetOrAdd(0.0));
        }
    }

    /// <inheritdoc/>
    public Unit VisitNullCoalesce(NullCoalesceNode node)
    {
        // In numeric context, null coalesce is just "use left, or right if left is 0/null"
        // For simplicity, treat as: left != 0 ? left : right
        var emitter = _context.Emitter;
        var labels = _context.Labels;

        var useRightLabel = labels.AllocateLabel();
        var endLabel = labels.AllocateLabel();

        // Compile left
        node.Left.Accept(this);

        // Duplicate for test
        emitter.Emit(BehaviorOpcode.Dup);

        // If falsy (0), use right
        emitter.EmitJmpUnless(useRightLabel);

        // Left is truthy, keep it
        emitter.EmitJmp(endLabel);

        // Use right
        emitter.DefineLabel(useRightLabel);
        emitter.Emit(BehaviorOpcode.Pop); // Pop left
        node.Right.Accept(this);

        emitter.DefineLabel(endLabel);

        return Unit.Value;
    }

    /// <inheritdoc/>
    public Unit VisitArrayLiteral(ArrayLiteralNode node)
    {
        // Array literals are only supported as the RHS of 'in' operator (handled by CompileInOperator).
        // Standalone array literals are not supported in bytecode.
        _context.AddError(
            "Array literals are only supported with the 'in' operator in bytecode " +
            "(e.g., 'x in [1, 2, 3]'). Standalone arrays require cloud-side execution.");

        // Push 0 as placeholder
        var idx = _context.Constants.GetOrAdd(0.0);
        _context.Emitter.EmitPushConst(idx);

        return Unit.Value;
    }
}

/// <summary>
/// Unit type for void-returning visitor methods.
/// </summary>
public readonly struct Unit
{
    /// <summary>
    /// Singleton value.
    /// </summary>
    public static Unit Value => default;
}
