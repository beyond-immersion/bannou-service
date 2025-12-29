// ═══════════════════════════════════════════════════════════════════════════
// ABML Expression Evaluator Tests
// End-to-end tests for expression evaluation.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.Abml.Exceptions;
using BeyondImmersion.BannouService.Abml.Runtime;
using Xunit;

namespace BeyondImmersion.BannouService.UnitTests.Abml;

/// <summary>
/// End-to-end tests for the expression evaluator.
/// </summary>
public class ExpressionEvaluatorTests
{
    private readonly IExpressionEvaluator _evaluator = ExpressionEvaluator.CreateDefault();

    private IVariableScope CreateScope(params (string name, object? value)[] variables)
    {
        var scope = new VariableScope();
        foreach (var (name, value) in variables)
        {
            scope.SetValue(name, value);
        }
        return scope;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Literal Evaluation Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("null", null)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("42", 42.0)]
    [InlineData("3.14", 3.14)]
    [InlineData("'hello'", "hello")]
    [InlineData("\"world\"", "world")]
    public void Evaluate_Literals(string expr, object? expected)
    {
        var result = _evaluator.Evaluate(expr, new VariableScope());
        Assert.Equal(expected, result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Arithmetic Evaluation Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("1 + 2", 3.0)]
    [InlineData("5 - 3", 2.0)]
    [InlineData("4 * 3", 12.0)]
    [InlineData("10 / 4", 2.5)]
    [InlineData("10 % 3", 1.0)]
    [InlineData("-5", -5.0)]
    [InlineData("--5", 5.0)]
    public void Evaluate_Arithmetic(string expr, double expected)
    {
        var result = _evaluator.Evaluate(expr, new VariableScope());
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("1 + 2 * 3", 7.0)]
    [InlineData("(1 + 2) * 3", 9.0)]
    [InlineData("10 - 2 - 3", 5.0)]
    [InlineData("2 + 3 * 4 + 5", 19.0)]
    public void Evaluate_ArithmeticPrecedence(string expr, double expected)
    {
        var result = _evaluator.Evaluate(expr, new VariableScope());
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Evaluate_DivisionByZero_Throws()
    {
        Assert.Throws<AbmlDivisionByZeroException>(
            () => _evaluator.Evaluate("10 / 0", new VariableScope()));
    }

    [Fact]
    public void Evaluate_ModuloByZero_Throws()
    {
        Assert.Throws<AbmlDivisionByZeroException>(
            () => _evaluator.Evaluate("10 % 0", new VariableScope()));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Comparison Evaluation Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("1 == 1", true)]
    [InlineData("1 == 2", false)]
    [InlineData("1 != 2", true)]
    [InlineData("1 != 1", false)]
    [InlineData("1 < 2", true)]
    [InlineData("2 < 1", false)]
    [InlineData("1 <= 1", true)]
    [InlineData("1 <= 2", true)]
    [InlineData("2 > 1", true)]
    [InlineData("1 > 2", false)]
    [InlineData("2 >= 2", true)]
    [InlineData("2 >= 1", true)]
    public void Evaluate_Comparison(string expr, bool expected)
    {
        var result = _evaluator.Evaluate(expr, new VariableScope());
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("'a' < 'b'", true)]
    [InlineData("'abc' == 'abc'", true)]
    [InlineData("'abc' != 'def'", true)]
    public void Evaluate_StringComparison(string expr, bool expected)
    {
        var result = _evaluator.Evaluate(expr, new VariableScope());
        Assert.Equal(expected, result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Logical Evaluation Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("true && true", true)]
    [InlineData("true && false", false)]
    [InlineData("false && true", false)]
    [InlineData("false && false", false)]
    [InlineData("true || true", true)]
    [InlineData("true || false", true)]
    [InlineData("false || true", true)]
    [InlineData("false || false", false)]
    [InlineData("!true", false)]
    [InlineData("!false", true)]
    public void Evaluate_Logical(string expr, bool expected)
    {
        var result = _evaluator.Evaluate(expr, new VariableScope());
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("1 && 2", true)]
    [InlineData("0 && 2", false)]
    [InlineData("'' && 'a'", false)]
    [InlineData("'a' || ''", true)]
    public void Evaluate_LogicalWithTruthiness(string expr, bool expected)
    {
        var result = _evaluator.Evaluate(expr, new VariableScope());
        Assert.Equal(expected, result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Variable Evaluation Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_SimpleVariable()
    {
        var scope = CreateScope(("x", 42));
        var result = _evaluator.Evaluate("x", scope);
        Assert.Equal(42, result);
    }

    [Fact]
    public void Evaluate_VariableInExpression()
    {
        var scope = CreateScope(("x", 10), ("y", 5));
        var result = _evaluator.Evaluate("x + y", scope);
        // Variables are ints, so result should be int 15 (not double)
        Assert.Equal(15, result);
    }

    [Fact]
    public void Evaluate_UndefinedVariable_ReturnsNull()
    {
        var result = _evaluator.Evaluate("undefined", new VariableScope());
        Assert.Null(result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property Access Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_PropertyAccess_Dictionary()
    {
        var obj = new Dictionary<string, object?> { ["name"] = "test", ["value"] = 42 };
        var scope = CreateScope(("obj", obj));

        Assert.Equal("test", _evaluator.Evaluate("obj.name", scope));
        Assert.Equal(42, _evaluator.Evaluate("obj.value", scope));
    }

    [Fact]
    public void Evaluate_PropertyAccess_Null_Throws()
    {
        var scope = CreateScope(("obj", null));
        Assert.Throws<AbmlRuntimeException>(
            () => _evaluator.Evaluate("obj.name", scope));
    }

    [Fact]
    public void Evaluate_NullSafePropertyAccess_Null_ReturnsNull()
    {
        var scope = CreateScope(("obj", null));
        var result = _evaluator.Evaluate("obj?.name", scope);
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_ChainedPropertyAccess()
    {
        var inner = new Dictionary<string, object?> { ["value"] = 100 };
        var outer = new Dictionary<string, object?> { ["inner"] = inner };
        var scope = CreateScope(("obj", outer));

        var result = _evaluator.Evaluate("obj.inner.value", scope);
        Assert.Equal(100, result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Index Access Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_IndexAccess_Array()
    {
        var arr = new[] { "a", "b", "c" };
        var scope = CreateScope(("arr", arr));

        Assert.Equal("a", _evaluator.Evaluate("arr[0]", scope));
        Assert.Equal("b", _evaluator.Evaluate("arr[1]", scope));
        Assert.Equal("c", _evaluator.Evaluate("arr[2]", scope));
    }

    [Fact]
    public void Evaluate_IndexAccess_Dictionary()
    {
        var dict = new Dictionary<string, object?> { ["key"] = "value" };
        var scope = CreateScope(("dict", dict));

        var result = _evaluator.Evaluate("dict['key']", scope);
        Assert.Equal("value", result);
    }

    [Fact]
    public void Evaluate_NullSafeIndexAccess_Null_ReturnsNull()
    {
        var scope = CreateScope(("arr", null));
        var result = _evaluator.Evaluate("arr?[0]", scope);
        Assert.Null(result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Null Coalesce Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_NullCoalesce_LeftNotNull_ReturnsLeft()
    {
        var scope = CreateScope(("x", 42), ("y", 100));
        var result = _evaluator.Evaluate("x ?? y", scope);
        Assert.Equal(42, result);
    }

    [Fact]
    public void Evaluate_NullCoalesce_LeftNull_ReturnsRight()
    {
        var scope = CreateScope(("x", null), ("y", 100));
        var result = _evaluator.Evaluate("x ?? y", scope);
        Assert.Equal(100, result);
    }

    [Fact]
    public void Evaluate_NullCoalesce_Chain()
    {
        var scope = CreateScope(("a", null), ("b", null), ("c", 42));
        var result = _evaluator.Evaluate("a ?? b ?? c", scope);
        Assert.Equal(42, result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Ternary Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_Ternary_ConditionTrue()
    {
        var result = _evaluator.Evaluate("true ? 'yes' : 'no'", new VariableScope());
        Assert.Equal("yes", result);
    }

    [Fact]
    public void Evaluate_Ternary_ConditionFalse()
    {
        var result = _evaluator.Evaluate("false ? 'yes' : 'no'", new VariableScope());
        Assert.Equal("no", result);
    }

    [Fact]
    public void Evaluate_Ternary_WithVariables()
    {
        var scope = CreateScope(("health", 0.3));
        var result = _evaluator.Evaluate("health < 0.5 ? 'low' : 'high'", scope);
        Assert.Equal("low", result);
    }

    [Fact]
    public void Evaluate_NestedTernary()
    {
        var scope = CreateScope(("x", 5));
        var result = _evaluator.Evaluate("x > 10 ? 'high' : x > 5 ? 'medium' : 'low'", scope);
        Assert.Equal("low", result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // In Operator Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_In_StringContains()
    {
        var scope = CreateScope(("text", "hello world"));
        var result = _evaluator.Evaluate("'world' in text", scope);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Evaluate_In_ListContains()
    {
        var scope = CreateScope(("list", new List<int> { 1, 2, 3 }));
        var result = _evaluator.Evaluate("2 in list", scope);
        Assert.Equal(true, result);
    }

    [Fact]
    public void Evaluate_In_ListNotContains()
    {
        var scope = CreateScope(("list", new List<int> { 1, 2, 3 }));
        var result = _evaluator.Evaluate("5 in list", scope);
        Assert.Equal(false, result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Function Call Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("length('hello')", 5)]
    [InlineData("length('')", 0)]
    public void Evaluate_Length(string expr, int expected)
    {
        var result = _evaluator.Evaluate(expr, new VariableScope());
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("upper('hello')", "HELLO")]
    [InlineData("lower('HELLO')", "hello")]
    [InlineData("trim('  hello  ')", "hello")]
    public void Evaluate_StringFunctions(string expr, string expected)
    {
        var result = _evaluator.Evaluate(expr, new VariableScope());
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("min(5, 3)", 3.0)]
    [InlineData("max(5, 3)", 5.0)]
    [InlineData("abs(-5)", 5.0)]
    [InlineData("floor(3.7)", 3.0)]
    [InlineData("ceil(3.2)", 4.0)]
    [InlineData("round(3.5)", 4.0)]
    public void Evaluate_MathFunctions(string expr, double expected)
    {
        var result = _evaluator.Evaluate(expr, new VariableScope());
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("is_null(null)", true)]
    [InlineData("is_null(5)", false)]
    [InlineData("is_empty('')", true)]
    [InlineData("is_empty('a')", false)]
    public void Evaluate_TypeFunctions(string expr, bool expected)
    {
        var result = _evaluator.Evaluate(expr, new VariableScope());
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Evaluate_TypeOf()
    {
        Assert.Equal("null", _evaluator.Evaluate("type_of(null)", new VariableScope()));
        Assert.Equal("boolean", _evaluator.Evaluate("type_of(true)", new VariableScope()));
        Assert.Equal("number", _evaluator.Evaluate("type_of(42)", new VariableScope()));
        Assert.Equal("string", _evaluator.Evaluate("type_of('hello')", new VariableScope()));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Typed Evaluation Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_Generic_ReturnsTypedResult()
    {
        var result = _evaluator.Evaluate<double>("1 + 2", new VariableScope());
        Assert.Equal(3.0, result);
    }

    [Fact]
    public void Evaluate_Generic_ConvertibleType()
    {
        var result = _evaluator.Evaluate<int>("42", new VariableScope());
        Assert.Equal(42, result);
    }

    [Fact]
    public void Evaluate_Generic_NullReturnsDefault()
    {
        var result = _evaluator.Evaluate<int>("null", new VariableScope());
        Assert.Equal(default, result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // EvaluateCondition Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("'hello'", true)]
    [InlineData("''", false)]
    public void EvaluateCondition(string expr, bool expected)
    {
        var result = _evaluator.EvaluateCondition(expr, new VariableScope());
        Assert.Equal(expected, result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TryEvaluate Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TryEvaluate_Valid_ReturnsTrue()
    {
        var success = _evaluator.TryEvaluate("1 + 2", new VariableScope(), out var result);
        Assert.True(success);
        Assert.Equal(3.0, result);
    }

    [Fact]
    public void TryEvaluate_DivisionByZero_ReturnsFalse()
    {
        var success = _evaluator.TryEvaluate("1 / 0", new VariableScope(), out var result);
        Assert.False(success);
        Assert.Null(result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Cache Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetCacheStatistics_ReturnsStatistics()
    {
        _evaluator.ClearCache();
        _evaluator.Evaluate("1 + 2", new VariableScope());
        _evaluator.Evaluate("1 + 2", new VariableScope()); // Cache hit

        var stats = _evaluator.GetCacheStatistics();
        Assert.True(stats.HitCount > 0 || stats.MissCount > 0);
    }

    [Fact]
    public void ClearCache_ClearsStatistics()
    {
        _evaluator.Evaluate("1 + 2", new VariableScope());
        _evaluator.ClearCache();

        var stats = _evaluator.GetCacheStatistics();
        Assert.Equal(0, stats.CurrentSize);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Complex Expression Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Evaluate_ComplexExpression()
    {
        var scope = CreateScope(
            ("health", 0.25),
            ("maxHealth", 100),
            ("isAlive", true)
        );

        var result = _evaluator.Evaluate(
            "isAlive && health < 0.3 ? 'critical' : 'stable'",
            scope);

        Assert.Equal("critical", result);
    }

    [Fact]
    public void Evaluate_NullSafeChain()
    {
        var entity = new Dictionary<string, object?>
        {
            ["inventory"] = new Dictionary<string, object?>
            {
                ["items"] = new List<string> { "sword", "shield" }
            }
        };
        var scope = CreateScope(("entity", entity));

        var result = _evaluator.Evaluate("entity?.inventory?.items", scope);
        Assert.IsType<List<string>>(result);
    }

    [Fact]
    public void Evaluate_ExpressionWrapper()
    {
        var scope = CreateScope(("x", 10));
        var result = _evaluator.Evaluate("${x + 5}", scope);
        Assert.Equal(15.0, result);
    }
}
