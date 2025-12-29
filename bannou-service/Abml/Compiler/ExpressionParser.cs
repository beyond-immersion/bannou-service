// ═══════════════════════════════════════════════════════════════════════════
// ABML Expression Parser (Parlot-based)
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Compiler.Ast;
using BeyondImmersion.BannouService.Abml.Exceptions;
using Parlot.Fluent;
using static Parlot.Fluent.Parsers;

namespace BeyondImmersion.BannouService.Abml.Compiler;

/// <summary>
/// Parses ABML expressions into AST nodes using Parlot.
/// </summary>
/// <remarks>
/// Grammar (precedence low to high):
/// <code>
/// expression     = ternary
/// ternary        = nullCoalesce ("?" expression ":" expression)?
/// nullCoalesce   = or ("??" or)*
/// or             = and ("||" and)*
/// and            = equality ("&amp;&amp;" equality)*
/// equality       = comparison (("==" | "!=") comparison)*
/// comparison     = term (("&lt;" | "&lt;=" | "&gt;" | "&gt;=") term)*
/// term           = factor (("+" | "-") factor)*
/// factor         = unary (("*" | "/" | "%") unary)*
/// unary          = ("!" | "-") unary | membership
/// membership     = postfix ("in" postfix)?
/// postfix        = primary (("." IDENT) | ("[" expression "]") | ("?." IDENT) | ("?[" expression "]") | ("(" args ")"))*
/// primary        = "null" | "true" | "false" | NUMBER | STRING | IDENT | "(" expression ")"
/// </code>
/// </remarks>
public sealed class ExpressionParser
{
    private static readonly Parser<ExpressionNode> _parser;

    static ExpressionParser()
    {
        _parser = BuildParser();
    }

    // Postfix operation types
    private abstract record PostfixOp;
    private sealed record PropOp(string Name, bool Safe) : PostfixOp;
    private sealed record IndexOp(ExpressionNode Index, bool Safe) : PostfixOp;
    private sealed record CallOp(IReadOnlyList<ExpressionNode> Args) : PostfixOp;

    private static Parser<ExpressionNode> BuildParser()
    {
        // Forward declaration for recursive rules
        var expression = Deferred<ExpressionNode>();

        // ═══════════════════════════════════════════════════════════════════
        // LITERALS
        // ═══════════════════════════════════════════════════════════════════

        // Keywords - use Terms.Text which auto-skips whitespace
        var nullLiteral = Terms.Text("null")
            .Then<ExpressionNode>(static _ => new LiteralNode(null));
        var trueLiteral = Terms.Text("true")
            .Then<ExpressionNode>(static _ => new LiteralNode(true));
        var falseLiteral = Terms.Text("false")
            .Then<ExpressionNode>(static _ => new LiteralNode(false));

        // Numbers (integer and floating point)
        var number = Terms.Decimal()
            .Then<ExpressionNode>(static d => new LiteralNode((double)d));

        // String literals (single or double quoted with escape sequences)
        var singleString = Terms.String(StringLiteralQuotes.Single)
            .Then<ExpressionNode>(static s => new LiteralNode(s.ToString()));
        var doubleString = Terms.String(StringLiteralQuotes.Double)
            .Then<ExpressionNode>(static s => new LiteralNode(s.ToString()));
        var stringLiteral = singleString.Or(doubleString);

        // Identifier (variable names, property names, function names)
        // Filter out keywords using When
        var identifier = Terms.Identifier()
            .When(static (_, id) =>
            {
                var s = id.ToString();
                return s != "null" && s != "true" && s != "false" && s != "in";
            })
            .Then(static id => id.ToString());

        // Variable reference
        var variable = identifier.Then<ExpressionNode>(static id => new VariableNode(id));

        // ═══════════════════════════════════════════════════════════════════
        // PRIMARY EXPRESSIONS
        // ═══════════════════════════════════════════════════════════════════

        // Grouped expression
        var openParen = Terms.Char('(');
        var closeParen = Terms.Char(')');
        var groupedExpression = Between(openParen, expression, closeParen);

        // Primary expressions (ordered for efficiency - keywords first)
        var primary = nullLiteral
            .Or(trueLiteral)
            .Or(falseLiteral)
            .Or(number)
            .Or(stringLiteral)
            .Or(groupedExpression)
            .Or(variable);

        // ═══════════════════════════════════════════════════════════════════
        // POSTFIX OPERATORS
        // ═══════════════════════════════════════════════════════════════════

        // Property name (allow any identifier, including keywords for properties)
        var propName = Terms.Identifier().Then(static id => id.ToString());

        // Property access: .identifier
        var propAccess = Terms.Char('.').SkipAnd(propName)
            .Then<PostfixOp>(static p => new PropOp(p, false));

        // Safe property access: ?.identifier
        var safePropAccess = Terms.Text("?.").SkipAnd(propName)
            .Then<PostfixOp>(static p => new PropOp(p, true));

        // Index access: [expr]
        var indexAccess = Between(Terms.Char('['), expression, Terms.Char(']'))
            .Then<PostfixOp>(static e => new IndexOp(e, false));

        // Safe index access: ?[expr]
        var safeIndexAccess = Terms.Text("?[").SkipAnd(expression).AndSkip(Terms.Char(']'))
            .Then<PostfixOp>(static e => new IndexOp(e, true));

        // Function call: (args)
        var args = Separated(Terms.Char(','), expression);
        var emptyArgs = openParen.AndSkip(closeParen)
            .Then<PostfixOp>(static _ => new CallOp(Array.Empty<ExpressionNode>()));
        var argsCall = Between(openParen, args, closeParen)
            .Then<PostfixOp>(static a => new CallOp(a));
        var funcCall = emptyArgs.Or(argsCall);

        // Combine all postfix operations - order matters: ?. before ., ?[ before [
        var postfixOp = safePropAccess
            .Or(propAccess)
            .Or(safeIndexAccess)
            .Or(indexAccess)
            .Or(funcCall);

        // Build postfix with iterative application
        var postfix = primary.And(ZeroOrMany(postfixOp))
            .Then(static pair =>
            {
                ExpressionNode node = pair.Item1;
                foreach (var op in pair.Item2)
                {
                    node = op switch
                    {
                        PropOp p => new PropertyAccessNode(node, p.Name, p.Safe),
                        IndexOp i => new IndexAccessNode(node, i.Index, i.Safe),
                        CallOp c when node is VariableNode v => new FunctionCallNode(v.Name, c.Args),
                        CallOp => throw new AbmlCompilationException("Function call requires identifier"),
                        _ => node
                    };
                }
                return node;
            });

        // ═══════════════════════════════════════════════════════════════════
        // MEMBERSHIP OPERATOR
        // ═══════════════════════════════════════════════════════════════════

        var inKeyword = Terms.Text("in");
        var membership = postfix.And(inKeyword.SkipAnd(postfix).Optional())
            .Then<ExpressionNode>(static pair =>
            {
                if (pair.Item2.HasValue)
                {
                    return new BinaryNode(BinaryOperator.In, pair.Item1, pair.Item2.Value);
                }
                return pair.Item1;
            });

        // ═══════════════════════════════════════════════════════════════════
        // UNARY OPERATORS
        // ═══════════════════════════════════════════════════════════════════

        var unary = Deferred<ExpressionNode>();

        unary.Parser = Terms.Char('!').SkipAnd(unary)
            .Then<ExpressionNode>(static operand => new UnaryNode(UnaryOperator.Not, operand))
            .Or(Terms.Char('-').SkipAnd(unary)
                .Then<ExpressionNode>(static operand => new UnaryNode(UnaryOperator.Negate, operand)))
            .Or(membership);

        // ═══════════════════════════════════════════════════════════════════
        // BINARY OPERATORS (using LeftAssociative helper)
        // ═══════════════════════════════════════════════════════════════════

        // Factor: *, /, %
        // Use Terms.Text for consistency
        var factor = unary.LeftAssociative(
            (Terms.Text("*"), static (ExpressionNode a, ExpressionNode b) => (ExpressionNode)new BinaryNode(BinaryOperator.Multiply, a, b)),
            (Terms.Text("/"), static (ExpressionNode a, ExpressionNode b) => (ExpressionNode)new BinaryNode(BinaryOperator.Divide, a, b)),
            (Terms.Text("%"), static (ExpressionNode a, ExpressionNode b) => (ExpressionNode)new BinaryNode(BinaryOperator.Modulo, a, b))
        );

        // Term: +, -
        var term = factor.LeftAssociative(
            (Terms.Text("+"), static (ExpressionNode a, ExpressionNode b) => (ExpressionNode)new BinaryNode(BinaryOperator.Add, a, b)),
            (Terms.Text("-"), static (ExpressionNode a, ExpressionNode b) => (ExpressionNode)new BinaryNode(BinaryOperator.Subtract, a, b))
        );

        // Comparison: <=, >=, <, > (longer operators first)
        var comparison = term.LeftAssociative(
            (Terms.Text("<="), static (ExpressionNode a, ExpressionNode b) => (ExpressionNode)new BinaryNode(BinaryOperator.LessOrEqual, a, b)),
            (Terms.Text(">="), static (ExpressionNode a, ExpressionNode b) => (ExpressionNode)new BinaryNode(BinaryOperator.GreaterOrEqual, a, b)),
            (Terms.Text("<"), static (ExpressionNode a, ExpressionNode b) => (ExpressionNode)new BinaryNode(BinaryOperator.LessThan, a, b)),
            (Terms.Text(">"), static (ExpressionNode a, ExpressionNode b) => (ExpressionNode)new BinaryNode(BinaryOperator.GreaterThan, a, b))
        );

        // Equality: ==, !=
        var equality = comparison.LeftAssociative(
            (Terms.Text("=="), static (ExpressionNode a, ExpressionNode b) => (ExpressionNode)new BinaryNode(BinaryOperator.Equal, a, b)),
            (Terms.Text("!="), static (ExpressionNode a, ExpressionNode b) => (ExpressionNode)new BinaryNode(BinaryOperator.NotEqual, a, b))
        );

        // Logical AND: &&
        var and = equality.LeftAssociative(
            (Terms.Text("&&"), static (ExpressionNode a, ExpressionNode b) => (ExpressionNode)new BinaryNode(BinaryOperator.And, a, b))
        );

        // Logical OR: ||
        var or = and.LeftAssociative(
            (Terms.Text("||"), static (ExpressionNode a, ExpressionNode b) => (ExpressionNode)new BinaryNode(BinaryOperator.Or, a, b))
        );

        // ═══════════════════════════════════════════════════════════════════
        // NULL COALESCE: ??
        // ═══════════════════════════════════════════════════════════════════

        var nullCoalesce = or.And(ZeroOrMany(Terms.Text("??").SkipAnd(or)))
            .Then<ExpressionNode>(static pair =>
            {
                ExpressionNode result = pair.Item1;
                foreach (var right in pair.Item2)
                {
                    result = new NullCoalesceNode(result, right);
                }
                return result;
            });

        // ═══════════════════════════════════════════════════════════════════
        // TERNARY CONDITIONAL: ? :
        // ═══════════════════════════════════════════════════════════════════

        // Ternary: condition ? then : else
        var ternaryBranches = Terms.Char('?').SkipAnd(expression).AndSkip(Terms.Char(':')).And(expression);
        var ternary = nullCoalesce.And(ternaryBranches.Optional())
            .Then<ExpressionNode>(static pair =>
            {
                if (pair.Item2.HasValue)
                {
                    return new TernaryNode(pair.Item1, pair.Item2.Value.Item1, pair.Item2.Value.Item2);
                }
                return pair.Item1;
            });

        // Set the expression to the full grammar
        expression.Parser = ternary;

        // Require end of input - reject incomplete expressions
        return expression.Eof();
    }

    /// <summary>
    /// Parses an expression string into an AST.
    /// </summary>
    /// <param name="expression">The expression to parse.</param>
    /// <returns>The parsed AST root node.</returns>
    /// <exception cref="AbmlCompilationException">Thrown when parsing fails.</exception>
    public ExpressionNode Parse(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        // Handle ${...} wrapper
        var toParse = expression;
        if (expression.StartsWith("${") && expression.EndsWith("}"))
        {
            toParse = expression[2..^1];
        }

        if (_parser.TryParse(toParse, out var result) && result != null)
        {
            return result;
        }

        throw new AbmlCompilationException($"Failed to parse expression: {expression}");
    }

    /// <summary>
    /// Tries to parse an expression string into an AST.
    /// </summary>
    /// <param name="expression">The expression to parse.</param>
    /// <param name="result">The parsed AST if successful.</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    public bool TryParse(string expression, out ExpressionNode? result)
    {
        try
        {
            result = Parse(expression);
            return true;
        }
        catch (AbmlCompilationException)
        {
            result = null;
            return false;
        }
    }
}
