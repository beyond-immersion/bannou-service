using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using Xunit.Abstractions;

namespace BeyondImmersion.BannouService.Tests;

/// <summary>
/// Tests for ConfigMultipleOfAttribute and multipleOf validation in configuration classes.
/// </summary>
[Collection("unit tests")]
public class ConfigMultipleOfValidationTests : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    public ConfigMultipleOfValidationTests(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigMultipleOfValidationTests>.Instance;
    }

    #region ConfigMultipleOfAttribute.IsValid Tests - Integers

    [Theory]
    [InlineData(0, 5, true)]      // Zero is multiple of anything
    [InlineData(5, 5, true)]      // Exactly the factor
    [InlineData(10, 5, true)]     // Multiple of factor
    [InlineData(100, 5, true)]    // Larger multiple
    [InlineData(3, 5, false)]     // Not a multiple
    [InlineData(7, 5, false)]     // Not a multiple
    [InlineData(11, 5, false)]    // Not a multiple
    public void IsValid_IntegerMultiples_ValidatesCorrectly(double value, double factor, bool expected)
    {
        var attr = new ConfigMultipleOfAttribute(factor);
        Assert.Equal(expected, attr.IsValid(value));
    }

    [Theory]
    [InlineData(0, 1000, true)]       // Zero
    [InlineData(1000, 1000, true)]    // Exactly 1 second
    [InlineData(5000, 1000, true)]    // 5 seconds
    [InlineData(60000, 1000, true)]   // 60 seconds
    [InlineData(500, 1000, false)]    // Half second - not allowed
    [InlineData(1500, 1000, false)]   // 1.5 seconds - not allowed
    [InlineData(999, 1000, false)]    // Just under
    [InlineData(1001, 1000, false)]   // Just over
    public void IsValid_TimeoutMilliseconds_ValidatesCorrectly(double value, double factor, bool expected)
    {
        var attr = new ConfigMultipleOfAttribute(factor);
        Assert.Equal(expected, attr.IsValid(value));
    }

    [Theory]
    [InlineData(0, 1024, true)]       // Zero
    [InlineData(1024, 1024, true)]    // 1 KB
    [InlineData(2048, 1024, true)]    // 2 KB
    [InlineData(1048576, 1024, true)] // 1 MB (1024 KB)
    [InlineData(1000, 1024, false)]   // Not on KB boundary
    [InlineData(1500, 1024, false)]   // Not on KB boundary
    public void IsValid_BufferSizeKB_ValidatesCorrectly(double value, double factor, bool expected)
    {
        var attr = new ConfigMultipleOfAttribute(factor);
        Assert.Equal(expected, attr.IsValid(value));
    }

    [Theory]
    [InlineData(-5, 5, true)]     // Negative multiple
    [InlineData(-10, 5, true)]    // Negative multiple
    [InlineData(-100, 5, true)]   // Negative multiple
    [InlineData(-3, 5, false)]    // Negative non-multiple
    [InlineData(-7, 5, false)]    // Negative non-multiple
    public void IsValid_NegativeValues_ValidatesCorrectly(double value, double factor, bool expected)
    {
        var attr = new ConfigMultipleOfAttribute(factor);
        Assert.Equal(expected, attr.IsValid(value));
    }

    #endregion

    #region ConfigMultipleOfAttribute.IsValid Tests - Floating Point

    [Theory]
    [InlineData(0.0, 0.01, true)]      // Zero
    [InlineData(0.01, 0.01, true)]     // Exactly the factor
    [InlineData(0.99, 0.01, true)]     // Multiple of 0.01
    [InlineData(1.00, 0.01, true)]     // Whole number
    [InlineData(9.99, 0.01, true)]     // Currency precision
    [InlineData(0.005, 0.01, false)]   // Half cent - not allowed
    [InlineData(0.015, 0.01, false)]   // 1.5 cents - not allowed
    public void IsValid_CurrencyPrecision_ValidatesCorrectly(double value, double factor, bool expected)
    {
        var attr = new ConfigMultipleOfAttribute(factor);
        Assert.Equal(expected, attr.IsValid(value));
    }

    [Theory]
    [InlineData(0.0, 0.5, true)]       // Zero
    [InlineData(0.5, 0.5, true)]       // Half
    [InlineData(1.0, 0.5, true)]       // Whole
    [InlineData(1.5, 0.5, true)]       // One and half
    [InlineData(10.0, 0.5, true)]      // Ten
    [InlineData(0.25, 0.5, false)]     // Quarter - not allowed
    [InlineData(0.75, 0.5, false)]     // Three quarters - not allowed
    [InlineData(1.25, 0.5, false)]     // Not on half boundary
    public void IsValid_HalfIncrements_ValidatesCorrectly(double value, double factor, bool expected)
    {
        var attr = new ConfigMultipleOfAttribute(factor);
        Assert.Equal(expected, attr.IsValid(value));
    }

    [Fact]
    public void IsValid_FloatingPointPrecision_HandlesToleranceCorrectly()
    {
        // Test that floating point precision issues are handled
        var attr = new ConfigMultipleOfAttribute(0.1);

        // 0.3 is famously imprecise in floating point
        // 0.1 + 0.1 + 0.1 != 0.3 exactly
        Assert.True(attr.IsValid(0.1 + 0.1 + 0.1));  // Should still pass with tolerance
        Assert.True(attr.IsValid(0.3));
        Assert.True(attr.IsValid(0.7));
        Assert.True(attr.IsValid(1.0));
    }

    [Fact]
    public void IsValid_SmallFactor_WorksCorrectly()
    {
        var attr = new ConfigMultipleOfAttribute(0.001);

        Assert.True(attr.IsValid(0.001));
        Assert.True(attr.IsValid(0.123));
        Assert.True(attr.IsValid(1.0));
        Assert.False(attr.IsValid(0.0001));  // Too precise
        Assert.False(attr.IsValid(0.0015));  // Not on boundary
    }

    #endregion

    #region ConfigMultipleOfAttribute Constructor Tests

    [Fact]
    public void Constructor_PositiveFactor_SetsProperty()
    {
        var attr = new ConfigMultipleOfAttribute(5);
        Assert.Equal(5, attr.Factor);
    }

    [Fact]
    public void Constructor_ZeroFactor_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConfigMultipleOfAttribute(0));
    }

    [Fact]
    public void Constructor_NegativeFactor_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConfigMultipleOfAttribute(-5));
    }

    [Fact]
    public void Tolerance_DefaultValue_IsSmall()
    {
        var attr = new ConfigMultipleOfAttribute(5);
        Assert.True(attr.Tolerance < 1e-6);
        Assert.True(attr.Tolerance > 0);
    }

    [Fact]
    public void Tolerance_CanBeCustomized()
    {
        var attr = new ConfigMultipleOfAttribute(5) { Tolerance = 0.001 };
        Assert.Equal(0.001, attr.Tolerance);
    }

    #endregion

    #region ConfigMultipleOfAttribute.GetMultipleOfDescription Tests

    [Fact]
    public void GetMultipleOfDescription_IntegerFactor_FormatsAsInteger()
    {
        var attr = new ConfigMultipleOfAttribute(1000);
        Assert.Equal("must be a multiple of 1000", attr.GetMultipleOfDescription());
    }

    [Fact]
    public void GetMultipleOfDescription_DecimalFactor_FormatsWithDecimal()
    {
        var attr = new ConfigMultipleOfAttribute(0.5);
        Assert.Equal("must be a multiple of 0.5", attr.GetMultipleOfDescription());
    }

    [Fact]
    public void GetMultipleOfDescription_SmallDecimalFactor_FormatsCorrectly()
    {
        var attr = new ConfigMultipleOfAttribute(0.01);
        Assert.Equal("must be a multiple of 0.01", attr.GetMultipleOfDescription());
    }

    #endregion

    #region ValidateMultipleOf Integration Tests

    [ServiceConfiguration]
    private class ConfigWithValidMultipleOf : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigMultipleOf(1000)]
        public int TimeoutMs { get; set; } = 5000;
    }

    [ServiceConfiguration]
    private class ConfigWithInvalidMultipleOf : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigMultipleOf(1000)]
        public int TimeoutMs { get; set; } = 1500; // Not a multiple of 1000
    }

    [ServiceConfiguration]
    private class ConfigWithMultipleConstraints : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigMultipleOf(1000)]
        public int TimeoutMs { get; set; } = 5000;

        [ConfigMultipleOf(5)]
        public int PercentageStep { get; set; } = 25;

        [ConfigMultipleOf(1024)]
        public int BufferSize { get; set; } = 4096;

        public int UnconstrainedValue { get; set; } = 123;
    }

    [ServiceConfiguration]
    private class ConfigWithDoubleMultipleOf : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigMultipleOf(0.01)]
        public double Price { get; set; } = 9.99;
    }

    [Fact]
    public void ValidateMultipleOf_ValidConfig_NoException()
    {
        IServiceConfiguration config = new ConfigWithValidMultipleOf { TimeoutMs = 5000 };
        var exception = Record.Exception(() => config.ValidateMultipleOf());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateMultipleOf_InvalidConfig_ThrowsException()
    {
        IServiceConfiguration config = new ConfigWithInvalidMultipleOf { TimeoutMs = 1500 };
        var exception = Assert.Throws<InvalidOperationException>(() => config.ValidateMultipleOf());
        Assert.Contains("TimeoutMs", exception.Message);
        Assert.Contains("1500", exception.Message);
        Assert.Contains("multiple of 1000", exception.Message);
    }

    [Fact]
    public void ValidateMultipleOf_MultipleProperties_ValidatesAll()
    {
        IServiceConfiguration config = new ConfigWithMultipleConstraints
        {
            TimeoutMs = 10000,
            PercentageStep = 50,
            BufferSize = 8192,
            UnconstrainedValue = 7  // No constraint
        };
        var exception = Record.Exception(() => config.ValidateMultipleOf());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateMultipleOf_MultipleInvalid_ReportsAll()
    {
        IServiceConfiguration config = new ConfigWithMultipleConstraints
        {
            TimeoutMs = 1500,      // Invalid
            PercentageStep = 7,     // Invalid
            BufferSize = 1000,      // Invalid
            UnconstrainedValue = 7
        };
        var exception = Assert.Throws<InvalidOperationException>(() => config.ValidateMultipleOf());
        Assert.Contains("TimeoutMs", exception.Message);
        Assert.Contains("PercentageStep", exception.Message);
        Assert.Contains("BufferSize", exception.Message);
    }

    [Fact]
    public void ValidateMultipleOf_DoubleValue_ValidatesCorrectly()
    {
        IServiceConfiguration config = new ConfigWithDoubleMultipleOf { Price = 19.99 };
        var exception = Record.Exception(() => config.ValidateMultipleOf());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateMultipleOf_DoubleValueInvalid_ThrowsException()
    {
        IServiceConfiguration config = new ConfigWithDoubleMultipleOf { Price = 9.999 }; // 3 decimal places
        var exception = Assert.Throws<InvalidOperationException>(() => config.ValidateMultipleOf());
        Assert.Contains("Price", exception.Message);
    }

    [Fact]
    public void ValidateMultipleOf_ZeroValue_AlwaysValid()
    {
        IServiceConfiguration config = new ConfigWithValidMultipleOf { TimeoutMs = 0 };
        var exception = Record.Exception(() => config.ValidateMultipleOf());
        Assert.Null(exception);
    }

    #endregion

    #region Combined Validate() Method Tests

    [ServiceConfiguration]
    private class ConfigWithAllConstraintTypes : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigMultipleOf(1000)]
        [ConfigRange(Minimum = 1000, Maximum = 60000)]
        public int TimeoutMs { get; set; } = 5000;

        [ConfigStringLength(MinLength = 8)]
        public string ApiKey { get; set; } = "secret-key";

        [ConfigPattern(@"^https?://")]
        public string Endpoint { get; set; } = "https://api.example.com";
    }

    [Fact]
    public void Validate_AllConstraintsPass_NoException()
    {
        IServiceConfiguration config = new ConfigWithAllConstraintTypes
        {
            TimeoutMs = 10000,
            ApiKey = "my-api-key-here",
            Endpoint = "https://example.com"
        };

        var exception = Record.Exception(() => config.Validate());
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_MultipleOfViolation_ThrowsException()
    {
        IServiceConfiguration config = new ConfigWithAllConstraintTypes
        {
            TimeoutMs = 1500,  // Not multiple of 1000
            ApiKey = "valid-key",
            Endpoint = "https://example.com"
        };

        var exception = Assert.Throws<InvalidOperationException>(() => config.Validate());
        Assert.Contains("TimeoutMs", exception.Message);
        Assert.Contains("multiple of", exception.Message);
    }

    [Fact]
    public void Validate_RangeViolation_ThrowsFirst()
    {
        IServiceConfiguration config = new ConfigWithAllConstraintTypes
        {
            TimeoutMs = 500,  // Below minimum (also not multiple of 1000)
            ApiKey = "valid-key",
            Endpoint = "https://example.com"
        };

        // Range validation runs before multipleOf, so range error is thrown
        var exception = Assert.Throws<InvalidOperationException>(() => config.Validate());
        Assert.Contains("TimeoutMs", exception.Message);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void IsValid_VeryLargeNumbers_WorksCorrectly()
    {
        var attr = new ConfigMultipleOfAttribute(1000000);

        Assert.True(attr.IsValid(1000000000));  // 1 billion
        Assert.True(attr.IsValid(5000000000));  // 5 billion
        Assert.False(attr.IsValid(1000000001)); // Off by one
    }

    [Fact]
    public void IsValid_FactorOfOne_AllIntegersValid()
    {
        var attr = new ConfigMultipleOfAttribute(1);

        Assert.True(attr.IsValid(1));
        Assert.True(attr.IsValid(100));
        Assert.True(attr.IsValid(999999));
        Assert.True(attr.IsValid(-42));
        Assert.False(attr.IsValid(1.5));  // Not an integer
    }

    [Fact]
    public void IsValid_VerySmallFactor_WorksCorrectly()
    {
        var attr = new ConfigMultipleOfAttribute(0.0001);

        Assert.True(attr.IsValid(0.0001));
        Assert.True(attr.IsValid(0.0005));
        Assert.True(attr.IsValid(1.2345));
    }

    #endregion
}
