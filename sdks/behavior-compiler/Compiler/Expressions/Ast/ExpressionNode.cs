// ═══════════════════════════════════════════════════════════════════════════
// ABML Abstract Syntax Tree Nodes
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.Bannou.BehaviorCompiler.Compiler.Ast;

/// <summary>
/// Base class for all ABML expression AST nodes.
/// </summary>
public abstract class ExpressionNode
{
    /// <summary>Gets the source position for error reporting.</summary>
    public int Position { get; init; }

    /// <summary>Accepts a visitor for tree traversal.</summary>
    public abstract T Accept<T>(IExpressionVisitor<T> visitor);
}

/// <summary>
/// Visitor interface for expression nodes.
/// </summary>
public interface IExpressionVisitor<T>
{
    /// <summary>Visits a literal value node.</summary>
    T VisitLiteral(LiteralNode node);
    /// <summary>Visits a variable reference node.</summary>
    T VisitVariable(VariableNode node);
    /// <summary>Visits a unary operation node.</summary>
    T VisitUnary(UnaryNode node);
    /// <summary>Visits a binary operation node.</summary>
    T VisitBinary(BinaryNode node);
    /// <summary>Visits a ternary conditional node.</summary>
    T VisitTernary(TernaryNode node);
    /// <summary>Visits a property access node.</summary>
    T VisitPropertyAccess(PropertyAccessNode node);
    /// <summary>Visits an index access node.</summary>
    T VisitIndexAccess(IndexAccessNode node);
    /// <summary>Visits a function call node.</summary>
    T VisitFunctionCall(FunctionCallNode node);
    /// <summary>Visits a null coalesce node.</summary>
    T VisitNullCoalesce(NullCoalesceNode node);
    /// <summary>Visits an array literal node.</summary>
    T VisitArrayLiteral(ArrayLiteralNode node);
}

/// <summary>
/// Literal value (number, string, boolean, null).
/// </summary>
public sealed class LiteralNode : ExpressionNode
{
    /// <summary>Gets the literal value.</summary>
    public object? Value { get; }

    /// <summary>Creates a new literal node.</summary>
    public LiteralNode(object? value) => Value = value;

    /// <inheritdoc/>
    public override T Accept<T>(IExpressionVisitor<T> visitor) => visitor.VisitLiteral(this);

    /// <inheritdoc/>
    public override string ToString() => Value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        bool b => b ? "true" : "false",
        _ => Value.ToString() ?? "null"
    };
}

/// <summary>
/// Variable reference (identifier).
/// </summary>
public sealed class VariableNode : ExpressionNode
{
    /// <summary>Gets the variable name.</summary>
    public string Name { get; }

    /// <summary>Creates a new variable node.</summary>
    public VariableNode(string name) => Name = name ?? throw new ArgumentNullException(nameof(name));

    /// <inheritdoc/>
    public override T Accept<T>(IExpressionVisitor<T> visitor) => visitor.VisitVariable(this);

    /// <inheritdoc/>
    public override string ToString() => Name;
}

/// <summary>
/// Unary operation type.
/// </summary>
public enum UnaryOperator
{
    /// <summary>Logical negation (!).</summary>
    Not,
    /// <summary>Arithmetic negation (-).</summary>
    Negate
}

/// <summary>
/// Unary operation (!, -).
/// </summary>
public sealed class UnaryNode : ExpressionNode
{
    /// <summary>Gets the operator.</summary>
    public UnaryOperator Operator { get; }

    /// <summary>Gets the operand.</summary>
    public ExpressionNode Operand { get; }

    /// <summary>Creates a new unary node.</summary>
    public UnaryNode(UnaryOperator op, ExpressionNode operand)
    {
        Operator = op;
        Operand = operand ?? throw new ArgumentNullException(nameof(operand));
    }

    /// <inheritdoc/>
    public override T Accept<T>(IExpressionVisitor<T> visitor) => visitor.VisitUnary(this);

    /// <inheritdoc/>
    public override string ToString() => Operator switch
    {
        UnaryOperator.Not => $"!{Operand}",
        UnaryOperator.Negate => $"-{Operand}",
        _ => $"?{Operand}"
    };
}

/// <summary>
/// Binary operation type.
/// </summary>
public enum BinaryOperator
{
    // Arithmetic
    /// <summary>Addition (+).</summary>
    Add,
    /// <summary>Subtraction (-).</summary>
    Subtract,
    /// <summary>Multiplication (*).</summary>
    Multiply,
    /// <summary>Division (/).</summary>
    Divide,
    /// <summary>Modulo (%).</summary>
    Modulo,

    // Comparison
    /// <summary>Equality (==).</summary>
    Equal,
    /// <summary>Inequality (!=).</summary>
    NotEqual,
    /// <summary>Less than (&lt;).</summary>
    LessThan,
    /// <summary>Less than or equal (&lt;=).</summary>
    LessOrEqual,
    /// <summary>Greater than (&gt;).</summary>
    GreaterThan,
    /// <summary>Greater than or equal (&gt;=).</summary>
    GreaterOrEqual,

    // Logical
    /// <summary>Logical AND (&amp;&amp;).</summary>
    And,
    /// <summary>Logical OR (||).</summary>
    Or,

    // Membership
    /// <summary>Membership test (in).</summary>
    In
}

/// <summary>
/// Binary operation.
/// </summary>
public sealed class BinaryNode : ExpressionNode
{
    /// <summary>Gets the operator.</summary>
    public BinaryOperator Operator { get; }

    /// <summary>Gets the left operand.</summary>
    public ExpressionNode Left { get; }

    /// <summary>Gets the right operand.</summary>
    public ExpressionNode Right { get; }

    /// <summary>Creates a new binary node.</summary>
    public BinaryNode(BinaryOperator op, ExpressionNode left, ExpressionNode right)
    {
        Operator = op;
        Left = left ?? throw new ArgumentNullException(nameof(left));
        Right = right ?? throw new ArgumentNullException(nameof(right));
    }

    /// <inheritdoc/>
    public override T Accept<T>(IExpressionVisitor<T> visitor) => visitor.VisitBinary(this);

    /// <inheritdoc/>
    public override string ToString()
    {
        var opStr = Operator switch
        {
            BinaryOperator.Add => "+",
            BinaryOperator.Subtract => "-",
            BinaryOperator.Multiply => "*",
            BinaryOperator.Divide => "/",
            BinaryOperator.Modulo => "%",
            BinaryOperator.Equal => "==",
            BinaryOperator.NotEqual => "!=",
            BinaryOperator.LessThan => "<",
            BinaryOperator.LessOrEqual => "<=",
            BinaryOperator.GreaterThan => ">",
            BinaryOperator.GreaterOrEqual => ">=",
            BinaryOperator.And => "&&",
            BinaryOperator.Or => "||",
            BinaryOperator.In => "in",
            _ => "?"
        };
        return $"({Left} {opStr} {Right})";
    }
}

/// <summary>
/// Ternary conditional (condition ? then : else).
/// </summary>
public sealed class TernaryNode : ExpressionNode
{
    /// <summary>Gets the condition expression.</summary>
    public ExpressionNode Condition { get; }

    /// <summary>Gets the then expression.</summary>
    public ExpressionNode ThenBranch { get; }

    /// <summary>Gets the else expression.</summary>
    public ExpressionNode ElseBranch { get; }

    /// <summary>Creates a new ternary node.</summary>
    public TernaryNode(ExpressionNode condition, ExpressionNode thenBranch, ExpressionNode elseBranch)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        ThenBranch = thenBranch ?? throw new ArgumentNullException(nameof(thenBranch));
        ElseBranch = elseBranch ?? throw new ArgumentNullException(nameof(elseBranch));
    }

    /// <inheritdoc/>
    public override T Accept<T>(IExpressionVisitor<T> visitor) => visitor.VisitTernary(this);

    /// <inheritdoc/>
    public override string ToString() => $"({Condition} ? {ThenBranch} : {ElseBranch})";
}

/// <summary>
/// Property access (obj.prop or obj?.prop).
/// </summary>
public sealed class PropertyAccessNode : ExpressionNode
{
    /// <summary>Gets the object expression.</summary>
    public ExpressionNode Object { get; }

    /// <summary>Gets the property name.</summary>
    public string PropertyName { get; }

    /// <summary>Gets whether this is a null-safe access (?.).</summary>
    public bool IsNullSafe { get; }

    /// <summary>Creates a new property access node.</summary>
    public PropertyAccessNode(ExpressionNode obj, string propertyName, bool isNullSafe = false)
    {
        Object = obj ?? throw new ArgumentNullException(nameof(obj));
        PropertyName = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
        IsNullSafe = isNullSafe;
    }

    /// <inheritdoc/>
    public override T Accept<T>(IExpressionVisitor<T> visitor) => visitor.VisitPropertyAccess(this);

    /// <inheritdoc/>
    public override string ToString() => IsNullSafe ? $"{Object}?.{PropertyName}" : $"{Object}.{PropertyName}";
}

/// <summary>
/// Index access (obj[index] or obj?[index]).
/// </summary>
public sealed class IndexAccessNode : ExpressionNode
{
    /// <summary>Gets the object expression.</summary>
    public ExpressionNode Object { get; }

    /// <summary>Gets the index expression.</summary>
    public ExpressionNode Index { get; }

    /// <summary>Gets whether this is a null-safe access (?[]).</summary>
    public bool IsNullSafe { get; }

    /// <summary>Creates a new index access node.</summary>
    public IndexAccessNode(ExpressionNode obj, ExpressionNode index, bool isNullSafe = false)
    {
        Object = obj ?? throw new ArgumentNullException(nameof(obj));
        Index = index ?? throw new ArgumentNullException(nameof(index));
        IsNullSafe = isNullSafe;
    }

    /// <inheritdoc/>
    public override T Accept<T>(IExpressionVisitor<T> visitor) => visitor.VisitIndexAccess(this);

    /// <inheritdoc/>
    public override string ToString() => IsNullSafe ? $"{Object}?[{Index}]" : $"{Object}[{Index}]";
}

/// <summary>
/// Function call (func(args)).
/// </summary>
public sealed class FunctionCallNode : ExpressionNode
{
    /// <summary>Gets the function name.</summary>
    public string FunctionName { get; }

    /// <summary>Gets the argument expressions.</summary>
    public IReadOnlyList<ExpressionNode> Arguments { get; }

    /// <summary>Creates a new function call node.</summary>
    public FunctionCallNode(string functionName, IReadOnlyList<ExpressionNode> arguments)
    {
        FunctionName = functionName ?? throw new ArgumentNullException(nameof(functionName));
        Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
    }

    /// <inheritdoc/>
    public override T Accept<T>(IExpressionVisitor<T> visitor) => visitor.VisitFunctionCall(this);

    /// <inheritdoc/>
    public override string ToString() => $"{FunctionName}({string.Join(", ", Arguments)})";
}

/// <summary>
/// Null coalesce (left ?? right).
/// </summary>
public sealed class NullCoalesceNode : ExpressionNode
{
    /// <summary>Gets the left operand.</summary>
    public ExpressionNode Left { get; }

    /// <summary>Gets the right operand (default value).</summary>
    public ExpressionNode Right { get; }

    /// <summary>Creates a new null coalesce node.</summary>
    public NullCoalesceNode(ExpressionNode left, ExpressionNode right)
    {
        Left = left ?? throw new ArgumentNullException(nameof(left));
        Right = right ?? throw new ArgumentNullException(nameof(right));
    }

    /// <inheritdoc/>
    public override T Accept<T>(IExpressionVisitor<T> visitor) => visitor.VisitNullCoalesce(this);

    /// <inheritdoc/>
    public override string ToString() => $"({Left} ?? {Right})";
}

/// <summary>
/// Array literal ([a, b, c]).
/// </summary>
public sealed class ArrayLiteralNode : ExpressionNode
{
    /// <summary>Gets the array elements.</summary>
    public IReadOnlyList<ExpressionNode> Elements { get; }

    /// <summary>Creates a new array literal node.</summary>
    public ArrayLiteralNode(IReadOnlyList<ExpressionNode> elements)
    {
        Elements = elements ?? throw new ArgumentNullException(nameof(elements));
    }

    /// <inheritdoc/>
    public override T Accept<T>(IExpressionVisitor<T> visitor) => visitor.VisitArrayLiteral(this);

    /// <inheritdoc/>
    public override string ToString() => $"[{string.Join(", ", Elements)}]";
}
