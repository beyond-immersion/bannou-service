// ═══════════════════════════════════════════════════════════════════════════
// ABML Expression Parser Tests
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Compiler;
using BeyondImmersion.Bannou.BehaviorCompiler.Compiler.Ast;
using BeyondImmersion.BannouService.Abml.Exceptions;
using Xunit;

namespace BeyondImmersion.BannouService.Tests.Abml;

/// <summary>
/// Tests for the ExpressionParser.
/// </summary>
public class ExpressionParserTests
{
    private readonly ExpressionParser _parser = new();

    // ═══════════════════════════════════════════════════════════════════════
    // Literal Parsing Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_Null_ReturnsLiteralNode()
    {
        var result = _parser.Parse("null");
        Assert.IsType<LiteralNode>(result);
        Assert.Null(((LiteralNode)result).Value);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Parse_Boolean_ReturnsLiteralNode(string expr, bool expected)
    {
        var result = _parser.Parse(expr);
        Assert.IsType<LiteralNode>(result);
        Assert.Equal(expected, ((LiteralNode)result).Value);
    }

    [Theory]
    [InlineData("42", 42.0)]
    [InlineData("3.14", 3.14)]
    [InlineData("0", 0.0)]
    [InlineData("123.456", 123.456)]
    public void Parse_Number_ReturnsLiteralNode(string expr, double expected)
    {
        var result = _parser.Parse(expr);
        Assert.IsType<LiteralNode>(result);
        Assert.Equal(expected, ((LiteralNode)result).Value);
    }

    [Theory]
    [InlineData("'hello'", "hello")]
    [InlineData("\"world\"", "world")]
    [InlineData("''", "")]
    [InlineData("\"\"", "")]
    public void Parse_String_ReturnsLiteralNode(string expr, string expected)
    {
        var result = _parser.Parse(expr);
        Assert.IsType<LiteralNode>(result);
        Assert.Equal(expected, ((LiteralNode)result).Value);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Variable Parsing Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("x")]
    [InlineData("foo")]
    [InlineData("myVariable")]
    [InlineData("_private")]
    [InlineData("item1")]
    public void Parse_Variable_ReturnsVariableNode(string expr)
    {
        var result = _parser.Parse(expr);
        Assert.IsType<VariableNode>(result);
        Assert.Equal(expr, ((VariableNode)result).Name);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property Access Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_PropertyAccess_ReturnsPropertyAccessNode()
    {
        var result = _parser.Parse("obj.name");
        Assert.IsType<PropertyAccessNode>(result);
        var prop = (PropertyAccessNode)result;
        Assert.Equal("name", prop.PropertyName);
        Assert.False(prop.IsNullSafe);
    }

    [Fact]
    public void Parse_NullSafePropertyAccess_ReturnsPropertyAccessNode()
    {
        var result = _parser.Parse("obj?.name");
        Assert.IsType<PropertyAccessNode>(result);
        var prop = (PropertyAccessNode)result;
        Assert.Equal("name", prop.PropertyName);
        Assert.True(prop.IsNullSafe);
    }

    [Fact]
    public void Parse_ChainedPropertyAccess_ReturnsNestedPropertyNodes()
    {
        var result = _parser.Parse("a.b.c");
        Assert.IsType<PropertyAccessNode>(result);
        var prop = (PropertyAccessNode)result;
        Assert.Equal("c", prop.PropertyName);
        Assert.IsType<PropertyAccessNode>(prop.Object);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Index Access Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_IndexAccess_ReturnsIndexAccessNode()
    {
        var result = _parser.Parse("arr[0]");
        Assert.IsType<IndexAccessNode>(result);
        var index = (IndexAccessNode)result;
        Assert.False(index.IsNullSafe);
    }

    [Fact]
    public void Parse_NullSafeIndexAccess_ReturnsIndexAccessNode()
    {
        var result = _parser.Parse("arr?[0]");
        Assert.IsType<IndexAccessNode>(result);
        var index = (IndexAccessNode)result;
        Assert.True(index.IsNullSafe);
    }

    [Fact]
    public void Parse_StringKeyIndex_ReturnsIndexAccessNode()
    {
        var result = _parser.Parse("dict['key']");
        Assert.IsType<IndexAccessNode>(result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Function Call Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_FunctionCall_NoArgs_ReturnsFunctionCallNode()
    {
        var result = _parser.Parse("func()");
        Assert.IsType<FunctionCallNode>(result);
        var func = (FunctionCallNode)result;
        Assert.Equal("func", func.FunctionName);
        Assert.Empty(func.Arguments);
    }

    [Fact]
    public void Parse_FunctionCall_OneArg_ReturnsFunctionCallNode()
    {
        var result = _parser.Parse("length(str)");
        Assert.IsType<FunctionCallNode>(result);
        var func = (FunctionCallNode)result;
        Assert.Equal("length", func.FunctionName);
        Assert.Single(func.Arguments);
    }

    [Fact]
    public void Parse_FunctionCall_MultipleArgs_ReturnsFunctionCallNode()
    {
        var result = _parser.Parse("max(a, b)");
        Assert.IsType<FunctionCallNode>(result);
        var func = (FunctionCallNode)result;
        Assert.Equal("max", func.FunctionName);
        Assert.Equal(2, func.Arguments.Count);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Unary Operator Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_Not_ReturnsUnaryNode()
    {
        var result = _parser.Parse("!flag");
        Assert.IsType<UnaryNode>(result);
        var unary = (UnaryNode)result;
        Assert.Equal(UnaryOperator.Not, unary.Operator);
    }

    [Fact]
    public void Parse_Negate_ReturnsUnaryNode()
    {
        var result = _parser.Parse("-x");
        Assert.IsType<UnaryNode>(result);
        var unary = (UnaryNode)result;
        Assert.Equal(UnaryOperator.Negate, unary.Operator);
    }

    [Fact]
    public void Parse_DoubleNot_ReturnsNestedUnaryNodes()
    {
        var result = _parser.Parse("!!flag");
        Assert.IsType<UnaryNode>(result);
        var outer = (UnaryNode)result;
        Assert.IsType<UnaryNode>(outer.Operand);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Binary Operator Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("1 + 2", BinaryOperator.Add)]
    [InlineData("1 - 2", BinaryOperator.Subtract)]
    [InlineData("1 * 2", BinaryOperator.Multiply)]
    [InlineData("1 / 2", BinaryOperator.Divide)]
    [InlineData("1 % 2", BinaryOperator.Modulo)]
    public void Parse_ArithmeticOperators_ReturnsBinaryNode(string expr, BinaryOperator expected)
    {
        var result = _parser.Parse(expr);
        Assert.IsType<BinaryNode>(result);
        Assert.Equal(expected, ((BinaryNode)result).Operator);
    }

    [Theory]
    [InlineData("a == b", BinaryOperator.Equal)]
    [InlineData("a != b", BinaryOperator.NotEqual)]
    [InlineData("a < b", BinaryOperator.LessThan)]
    [InlineData("a <= b", BinaryOperator.LessOrEqual)]
    [InlineData("a > b", BinaryOperator.GreaterThan)]
    [InlineData("a >= b", BinaryOperator.GreaterOrEqual)]
    public void Parse_ComparisonOperators_ReturnsBinaryNode(string expr, BinaryOperator expected)
    {
        var result = _parser.Parse(expr);
        Assert.IsType<BinaryNode>(result);
        Assert.Equal(expected, ((BinaryNode)result).Operator);
    }

    [Theory]
    [InlineData("a && b", BinaryOperator.And)]
    [InlineData("a || b", BinaryOperator.Or)]
    public void Parse_LogicalOperators_ReturnsBinaryNode(string expr, BinaryOperator expected)
    {
        var result = _parser.Parse(expr);
        Assert.IsType<BinaryNode>(result);
        Assert.Equal(expected, ((BinaryNode)result).Operator);
    }

    [Fact]
    public void Parse_InOperator_ReturnsBinaryNode()
    {
        var result = _parser.Parse("x in collection");
        Assert.IsType<BinaryNode>(result);
        Assert.Equal(BinaryOperator.In, ((BinaryNode)result).Operator);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Precedence Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_MultiplicationBeforeAddition()
    {
        var result = _parser.Parse("1 + 2 * 3");
        Assert.IsType<BinaryNode>(result);
        var add = (BinaryNode)result;
        Assert.Equal(BinaryOperator.Add, add.Operator);
        // Right side should be the multiplication
        Assert.IsType<BinaryNode>(add.Right);
        Assert.Equal(BinaryOperator.Multiply, ((BinaryNode)add.Right).Operator);
    }

    [Fact]
    public void Parse_ParenthesesOverridePrecedence()
    {
        var result = _parser.Parse("(1 + 2) * 3");
        Assert.IsType<BinaryNode>(result);
        var mul = (BinaryNode)result;
        Assert.Equal(BinaryOperator.Multiply, mul.Operator);
        // Left side should be the addition
        Assert.IsType<BinaryNode>(mul.Left);
        Assert.Equal(BinaryOperator.Add, ((BinaryNode)mul.Left).Operator);
    }

    [Fact]
    public void Parse_ComparisonBeforeLogical()
    {
        var result = _parser.Parse("a < b && c > d");
        Assert.IsType<BinaryNode>(result);
        var and = (BinaryNode)result;
        Assert.Equal(BinaryOperator.And, and.Operator);
        Assert.IsType<BinaryNode>(and.Left);
        Assert.IsType<BinaryNode>(and.Right);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Null Coalesce Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_NullCoalesce_ReturnsNullCoalesceNode()
    {
        var result = _parser.Parse("x ?? y");
        Assert.IsType<NullCoalesceNode>(result);
    }

    [Fact]
    public void Parse_ChainedNullCoalesce_ReturnsNestedNodes()
    {
        var result = _parser.Parse("a ?? b ?? c");
        Assert.IsType<NullCoalesceNode>(result);
        var outer = (NullCoalesceNode)result;
        Assert.IsType<NullCoalesceNode>(outer.Left);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Ternary Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_Ternary_ReturnsTernaryNode()
    {
        var result = _parser.Parse("a ? b : c");
        Assert.IsType<TernaryNode>(result);
        var ternary = (TernaryNode)result;
        Assert.IsType<VariableNode>(ternary.Condition);
        Assert.IsType<VariableNode>(ternary.ThenBranch);
        Assert.IsType<VariableNode>(ternary.ElseBranch);
    }

    [Fact]
    public void Parse_NestedTernary_ReturnsNestedNodes()
    {
        var result = _parser.Parse("a ? b : c ? d : e");
        Assert.IsType<TernaryNode>(result);
        var outer = (TernaryNode)result;
        Assert.IsType<TernaryNode>(outer.ElseBranch);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ${...} Wrapper Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_ExpressionWrapper_StripsWrapper()
    {
        var result = _parser.Parse("${42}");
        Assert.IsType<LiteralNode>(result);
        Assert.Equal(42.0, ((LiteralNode)result).Value);
    }

    [Fact]
    public void Parse_ComplexExpressionWrapper_StripsWrapper()
    {
        var result = _parser.Parse("${a + b}");
        Assert.IsType<BinaryNode>(result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Error Handling Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyExpression_Throws(string expr)
    {
        Assert.Throws<AbmlCompilationException>(() => _parser.Parse(expr));
    }

    [Theory]
    [InlineData("1 +")]
    [InlineData("+ 1")]
    [InlineData("a ?")]
    [InlineData("a ? b")]
    public void Parse_IncompleteExpression_Throws(string expr)
    {
        Assert.Throws<AbmlCompilationException>(() => _parser.Parse(expr));
    }

    [Fact]
    public void TryParse_ValidExpression_ReturnsTrue()
    {
        var success = _parser.TryParse("1 + 2", out var result);
        Assert.True(success);
        Assert.NotNull(result);
    }

    [Fact]
    public void TryParse_InvalidExpression_ReturnsFalse()
    {
        var success = _parser.TryParse("1 +", out var result);
        Assert.False(success);
        Assert.Null(result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Array Literal Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_EmptyArray_ReturnsArrayLiteralNode()
    {
        var result = _parser.Parse("[]");
        Assert.IsType<ArrayLiteralNode>(result);
        var array = (ArrayLiteralNode)result;
        Assert.Empty(array.Elements);
    }

    [Fact]
    public void Parse_SingleElementArray_ReturnsArrayLiteralNode()
    {
        var result = _parser.Parse("['a']");
        Assert.IsType<ArrayLiteralNode>(result);
        var array = (ArrayLiteralNode)result;
        Assert.Single(array.Elements);
        Assert.IsType<LiteralNode>(array.Elements[0]);
        Assert.Equal("a", ((LiteralNode)array.Elements[0]).Value);
    }

    [Fact]
    public void Parse_MultiElementArray_ReturnsArrayLiteralNode()
    {
        var result = _parser.Parse("['a', 'b', 'c']");
        Assert.IsType<ArrayLiteralNode>(result);
        var array = (ArrayLiteralNode)result;
        Assert.Equal(3, array.Elements.Count);
    }

    [Fact]
    public void Parse_MixedTypeArray_ReturnsArrayLiteralNode()
    {
        var result = _parser.Parse("[1, 'hello', true, null]");
        Assert.IsType<ArrayLiteralNode>(result);
        var array = (ArrayLiteralNode)result;
        Assert.Equal(4, array.Elements.Count);

        Assert.IsType<LiteralNode>(array.Elements[0]);
        Assert.Equal(1.0, ((LiteralNode)array.Elements[0]).Value);

        Assert.IsType<LiteralNode>(array.Elements[1]);
        Assert.Equal("hello", ((LiteralNode)array.Elements[1]).Value);

        Assert.IsType<LiteralNode>(array.Elements[2]);
        Assert.Equal(true, ((LiteralNode)array.Elements[2]).Value);

        Assert.IsType<LiteralNode>(array.Elements[3]);
        Assert.Null(((LiteralNode)array.Elements[3]).Value);
    }

    [Fact]
    public void Parse_InWithArrayLiteral_ReturnsBinaryNodeWithArrayRhs()
    {
        var result = _parser.Parse("x in ['idle', 'walking']");
        Assert.IsType<BinaryNode>(result);
        var binary = (BinaryNode)result;
        Assert.Equal(BinaryOperator.In, binary.Operator);
        Assert.IsType<VariableNode>(binary.Left);
        Assert.IsType<ArrayLiteralNode>(binary.Right);

        var array = (ArrayLiteralNode)binary.Right;
        Assert.Equal(2, array.Elements.Count);
    }

    [Fact]
    public void Parse_InWithEmptyArray_ReturnsBinaryNodeWithEmptyArrayRhs()
    {
        var result = _parser.Parse("x in []");
        Assert.IsType<BinaryNode>(result);
        var binary = (BinaryNode)result;
        Assert.Equal(BinaryOperator.In, binary.Operator);
        Assert.IsType<ArrayLiteralNode>(binary.Right);
        Assert.Empty(((ArrayLiteralNode)binary.Right).Elements);
    }

    [Fact]
    public void Parse_ArrayWithExpressions_ReturnsNestedExpressions()
    {
        var result = _parser.Parse("[1 + 2, a * b]");
        Assert.IsType<ArrayLiteralNode>(result);
        var array = (ArrayLiteralNode)result;
        Assert.Equal(2, array.Elements.Count);
        Assert.IsType<BinaryNode>(array.Elements[0]);
        Assert.IsType<BinaryNode>(array.Elements[1]);
    }

    [Fact]
    public void Parse_ArrayIndexAccessAfterArray_DistinguishesFromArrayLiteral()
    {
        // arr[0] should be index access, not array literal
        var result = _parser.Parse("arr[0]");
        Assert.IsType<IndexAccessNode>(result);
    }
}
