// ═══════════════════════════════════════════════════════════════════════════
// ABML Type Coercion Tests
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using Xunit;

namespace BeyondImmersion.BannouService.Tests.Abml;

/// <summary>
/// Tests for AbmlTypeCoercion helper methods.
/// </summary>
public class TypeCoercionTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // IsTrue Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void IsTrue_NullIsFalse()
    {
        Assert.False(AbmlTypeCoercion.IsTrue(null));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void IsTrue_Boolean(bool input, bool expected)
    {
        Assert.Equal(expected, AbmlTypeCoercion.IsTrue(input));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(-1, true)]
    [InlineData(42, true)]
    public void IsTrue_Integer(int input, bool expected)
    {
        Assert.Equal(expected, AbmlTypeCoercion.IsTrue(input));
    }

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(0.1, true)]
    [InlineData(-0.1, true)]
    [InlineData(double.NaN, true)] // NaN is truthy (non-zero)
    public void IsTrue_Double(double input, bool expected)
    {
        Assert.Equal(expected, AbmlTypeCoercion.IsTrue(input));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("hello", true)]
    [InlineData(" ", true)]
    public void IsTrue_String(string input, bool expected)
    {
        Assert.Equal(expected, AbmlTypeCoercion.IsTrue(input));
    }

    [Fact]
    public void IsTrue_EmptyCollectionIsFalse()
    {
        Assert.False(AbmlTypeCoercion.IsTrue(new List<int>()));
        Assert.False(AbmlTypeCoercion.IsTrue(Array.Empty<string>()));
    }

    [Fact]
    public void IsTrue_NonEmptyCollectionIsTrue()
    {
        Assert.True(AbmlTypeCoercion.IsTrue(new List<int> { 1 }));
        Assert.True(AbmlTypeCoercion.IsTrue(new[] { "a" }));
    }

    [Fact]
    public void IsTrue_ObjectIsTrue()
    {
        Assert.True(AbmlTypeCoercion.IsTrue(new object()));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Add Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Add_NullPlusNull_ReturnsZero()
    {
        Assert.Equal(0, AbmlTypeCoercion.Add(null, null));
    }

    [Fact]
    public void Add_NullPlusValue_ReturnsValue()
    {
        Assert.Equal(5, AbmlTypeCoercion.Add(null, 5));
        Assert.Equal(5, AbmlTypeCoercion.Add(5, null));
    }

    [Theory]
    [InlineData(1, 2, 3)]
    [InlineData(-5, 10, 5)]
    [InlineData(0, 0, 0)]
    public void Add_Integers(int a, int b, int expected)
    {
        Assert.Equal(expected, AbmlTypeCoercion.Add(a, b));
    }

    [Theory]
    [InlineData(1.5, 2.5, 4.0)]
    [InlineData(-1.0, 1.0, 0.0)]
    public void Add_Doubles(double a, double b, double expected)
    {
        Assert.Equal(expected, AbmlTypeCoercion.Add(a, b));
    }

    [Theory]
    [InlineData("hello", " world", "hello world")]
    [InlineData("", "test", "test")]
    [InlineData("a", "b", "ab")]
    public void Add_Strings(string a, string b, string expected)
    {
        Assert.Equal(expected, AbmlTypeCoercion.Add(a, b));
    }

    [Fact]
    public void Add_StringPlusNumber_Concatenates()
    {
        Assert.Equal("value: 42", AbmlTypeCoercion.Add("value: ", 42));
        Assert.Equal("42end", AbmlTypeCoercion.Add(42, "end"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Subtract Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(5, 3, 2)]
    [InlineData(0, 0, 0)]
    [InlineData(-5, -3, -2)]
    public void Subtract_Integers(int a, int b, int expected)
    {
        Assert.Equal(expected, AbmlTypeCoercion.Subtract(a, b));
    }

    [Theory]
    [InlineData(5.5, 2.5, 3.0)]
    [InlineData(1.0, 1.0, 0.0)]
    public void Subtract_Doubles(double a, double b, double expected)
    {
        Assert.Equal(expected, AbmlTypeCoercion.Subtract(a, b));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Multiply Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(3, 4, 12)]
    [InlineData(-2, 3, -6)]
    [InlineData(0, 100, 0)]
    public void Multiply_Integers(int a, int b, int expected)
    {
        Assert.Equal(expected, AbmlTypeCoercion.Multiply(a, b));
    }

    [Theory]
    [InlineData(2.5, 4.0, 10.0)]
    [InlineData(0.5, 0.5, 0.25)]
    public void Multiply_Doubles(double a, double b, double expected)
    {
        Assert.Equal(expected, AbmlTypeCoercion.Multiply(a, b));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Divide Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(10, 2, 5)]
    [InlineData(7, 2, 3)] // Integer division
    [InlineData(-6, 2, -3)]
    public void Divide_Integers(int a, int b, int expected)
    {
        Assert.Equal(expected, AbmlTypeCoercion.Divide(a, b));
    }

    [Theory]
    [InlineData(10.0, 4.0, 2.5)]
    [InlineData(1.0, 3.0, 0.333333333333333)]
    public void Divide_Doubles(double a, double b, double expected)
    {
        Assert.Equal(expected, (double)AbmlTypeCoercion.Divide(a, b), 10);
    }

    [Fact]
    public void Divide_ByZero_ThrowsDivideByZeroException()
    {
        Assert.Throws<DivideByZeroException>(() => AbmlTypeCoercion.Divide(10, 0));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Modulo Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(10, 3, 1)]
    [InlineData(15, 5, 0)]
    [InlineData(7, 4, 3)]
    public void Modulo_Integers(int a, int b, int expected)
    {
        Assert.Equal(expected, AbmlTypeCoercion.Modulo(a, b));
    }

    [Fact]
    public void Modulo_ByZero_ThrowsDivideByZeroException()
    {
        Assert.Throws<DivideByZeroException>(() => AbmlTypeCoercion.Modulo(10, 0));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Negate Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(5, -5)]
    [InlineData(-5, 5)]
    [InlineData(0, 0)]
    public void Negate_Integer(int input, int expected)
    {
        Assert.Equal(expected, AbmlTypeCoercion.Negate(input));
    }

    [Fact]
    public void Negate_Null_ReturnsZero()
    {
        Assert.Equal(0, AbmlTypeCoercion.Negate(null));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Compare Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(1, 2, -1)]
    [InlineData(2, 1, 1)]
    [InlineData(5, 5, 0)]
    public void Compare_Integers(int a, int b, int expectedSign)
    {
        var result = AbmlTypeCoercion.Compare(a, b);
        Assert.Equal(expectedSign, Math.Sign(result));
    }

    [Theory]
    [InlineData("a", "b", -1)]
    [InlineData("b", "a", 1)]
    [InlineData("abc", "abc", 0)]
    public void Compare_Strings(string a, string b, int expectedSign)
    {
        var result = AbmlTypeCoercion.Compare(a, b);
        Assert.Equal(expectedSign, Math.Sign(result));
    }

    [Fact]
    public void Compare_NullWithNull_ReturnsZero()
    {
        Assert.Equal(0, AbmlTypeCoercion.Compare(null, null));
    }

    [Fact]
    public void Compare_NullWithValue_ReturnsNegative()
    {
        Assert.True(AbmlTypeCoercion.Compare(null, 1) < 0);
    }

    [Fact]
    public void Compare_ValueWithNull_ReturnsPositive()
    {
        Assert.True(AbmlTypeCoercion.Compare(1, null) > 0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Equals Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Equals_SameReferences_ReturnsTrue()
    {
        var obj = new object();
        Assert.True(AbmlTypeCoercion.Equals(obj, obj));
    }

    [Fact]
    public void Equals_BothNull_ReturnsTrue()
    {
        Assert.True(AbmlTypeCoercion.Equals(null, null));
    }

    [Fact]
    public void Equals_OneNull_ReturnsFalse()
    {
        Assert.False(AbmlTypeCoercion.Equals(null, 1));
        Assert.False(AbmlTypeCoercion.Equals(1, null));
    }

    [Theory]
    [InlineData(5, 5, true)]
    [InlineData(5, 6, false)]
    [InlineData(5, 5.0, true)]
    [InlineData(5L, 5, true)]
    public void Equals_Numbers(object a, object b, bool expected)
    {
        Assert.Equal(expected, AbmlTypeCoercion.Equals(a, b));
    }

    [Theory]
    [InlineData("hello", "hello", true)]
    [InlineData("hello", "world", false)]
    public void Equals_Strings(string a, string b, bool expected)
    {
        Assert.Equal(expected, AbmlTypeCoercion.Equals(a, b));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetProperty Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetProperty_Null_ReturnsNull()
    {
        Assert.Null(AbmlTypeCoercion.GetProperty(null, "name"));
    }

    [Fact]
    public void GetProperty_Dictionary_ReturnsValue()
    {
        var dict = new Dictionary<string, object?> { ["name"] = "test" };
        Assert.Equal("test", AbmlTypeCoercion.GetProperty(dict, "name"));
    }

    [Fact]
    public void GetProperty_Dictionary_MissingKey_ReturnsNull()
    {
        var dict = new Dictionary<string, object?> { ["name"] = "test" };
        Assert.Null(AbmlTypeCoercion.GetProperty(dict, "missing"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetIndex Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetIndex_Null_ReturnsNull()
    {
        Assert.Null(AbmlTypeCoercion.GetIndex(null, 0));
    }

    [Fact]
    public void GetIndex_List_ReturnsElement()
    {
        var list = new List<string> { "a", "b", "c" };
        Assert.Equal("a", AbmlTypeCoercion.GetIndex(list, 0));
        Assert.Equal("b", AbmlTypeCoercion.GetIndex(list, 1));
        Assert.Equal("c", AbmlTypeCoercion.GetIndex(list, 2));
    }

    [Fact]
    public void GetIndex_List_OutOfRange_ReturnsNull()
    {
        var list = new List<string> { "a" };
        Assert.Null(AbmlTypeCoercion.GetIndex(list, 5));
        Assert.Null(AbmlTypeCoercion.GetIndex(list, -1));
    }

    [Fact]
    public void GetIndex_String_ReturnsCharacter()
    {
        Assert.Equal("h", AbmlTypeCoercion.GetIndex("hello", 0));
        Assert.Equal("o", AbmlTypeCoercion.GetIndex("hello", 4));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Contains Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Contains_NullCollection_ReturnsFalse()
    {
        Assert.False(AbmlTypeCoercion.Contains(null, "test"));
    }

    [Fact]
    public void Contains_StringSubstring_ReturnsTrue()
    {
        Assert.True(AbmlTypeCoercion.Contains("hello world", "world"));
    }

    [Fact]
    public void Contains_StringSubstring_ReturnsFalse()
    {
        Assert.False(AbmlTypeCoercion.Contains("hello world", "universe"));
    }

    [Fact]
    public void Contains_List_ReturnsTrue()
    {
        var list = new List<int> { 1, 2, 3 };
        Assert.True(AbmlTypeCoercion.Contains(list, 2));
    }

    [Fact]
    public void Contains_List_ReturnsFalse()
    {
        var list = new List<int> { 1, 2, 3 };
        Assert.False(AbmlTypeCoercion.Contains(list, 5));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ToDouble Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(null, 0.0)]
    [InlineData(5, 5.0)]
    [InlineData(5L, 5.0)]
    [InlineData(5.5f, 5.5)]
    [InlineData(5.5, 5.5)]
    [InlineData("3.14", 3.14)]
    public void ToDouble_VariousTypes(object? input, double expected)
    {
        Assert.Equal(expected, AbmlTypeCoercion.ToDouble(input), 10);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // IsNumeric Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(5, true)]
    [InlineData(5L, true)]
    [InlineData(5.0f, true)]
    [InlineData(5.0, true)]
    [InlineData("5", false)]
    [InlineData(null, false)]
    public void IsNumeric_VariousTypes(object? input, bool expected)
    {
        Assert.Equal(expected, AbmlTypeCoercion.IsNumeric(input));
    }
}
