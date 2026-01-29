using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using Xunit.Abstractions;

namespace BeyondImmersion.BannouService.Tests;

/// <summary>
/// Tests for ConfigRangeAttribute and numeric range validation in configuration classes.
/// </summary>
[Collection("unit tests")]
public class ConfigRangeValidationTests : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    public ConfigRangeValidationTests(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigRangeValidationTests>.Instance;
    }

    #region ConfigRangeAttribute.IsValid Tests

    [Theory]
    [InlineData(5, 1, 10, true)]      // Within range
    [InlineData(1, 1, 10, true)]      // At minimum (inclusive)
    [InlineData(10, 1, 10, true)]     // At maximum (inclusive)
    [InlineData(0, 1, 10, false)]     // Below minimum
    [InlineData(11, 1, 10, false)]    // Above maximum
    [InlineData(-5, -10, -1, true)]   // Negative range, within
    [InlineData(-10, -10, -1, true)]  // Negative range, at min
    [InlineData(-1, -10, -1, true)]   // Negative range, at max
    [InlineData(0, -10, -1, false)]   // Negative range, above max
    public void IsValid_InclusiveBounds_ValidatesCorrectly(double value, double min, double max, bool expected)
    {
        var attr = new ConfigRangeAttribute(min, max);
        Assert.Equal(expected, attr.IsValid(value));
    }

    [Theory]
    [InlineData(5, 1, 10, true)]      // Within range
    [InlineData(1, 1, 10, false)]     // At minimum (exclusive - should fail)
    [InlineData(1.001, 1, 10, true)]  // Just above minimum
    [InlineData(10, 1, 10, true)]     // At maximum (only min is exclusive)
    public void IsValid_ExclusiveMinimum_ValidatesCorrectly(double value, double min, double max, bool expected)
    {
        var attr = new ConfigRangeAttribute(min, max) { ExclusiveMinimum = true };
        Assert.Equal(expected, attr.IsValid(value));
    }

    [Theory]
    [InlineData(5, 1, 10, true)]      // Within range
    [InlineData(10, 1, 10, false)]    // At maximum (exclusive - should fail)
    [InlineData(9.999, 1, 10, true)]  // Just below maximum
    [InlineData(1, 1, 10, true)]      // At minimum (only max is exclusive)
    public void IsValid_ExclusiveMaximum_ValidatesCorrectly(double value, double min, double max, bool expected)
    {
        var attr = new ConfigRangeAttribute(min, max) { ExclusiveMaximum = true };
        Assert.Equal(expected, attr.IsValid(value));
    }

    [Theory]
    [InlineData(5, 1, 10, true)]      // Within range
    [InlineData(1, 1, 10, false)]     // At minimum (exclusive - should fail)
    [InlineData(10, 1, 10, false)]    // At maximum (exclusive - should fail)
    [InlineData(1.001, 1, 9.999, true)] // Just inside both bounds
    public void IsValid_BothExclusive_ValidatesCorrectly(double value, double min, double max, bool expected)
    {
        var attr = new ConfigRangeAttribute(min, max) { ExclusiveMinimum = true, ExclusiveMaximum = true };
        Assert.Equal(expected, attr.IsValid(value));
    }

    [Theory]
    [InlineData(100, true)]    // Above minimum
    [InlineData(1, true)]      // At minimum (inclusive)
    [InlineData(0, false)]     // Below minimum
    [InlineData(-100, false)]  // Well below minimum
    public void IsValid_MinimumOnly_ValidatesCorrectly(double value, bool expected)
    {
        var attr = new ConfigRangeAttribute { Minimum = 1 };
        Assert.Equal(expected, attr.IsValid(value));
    }

    [Theory]
    [InlineData(50, true)]     // Below maximum
    [InlineData(100, true)]    // At maximum (inclusive)
    [InlineData(101, false)]   // Above maximum
    [InlineData(1000, false)]  // Well above maximum
    public void IsValid_MaximumOnly_ValidatesCorrectly(double value, bool expected)
    {
        var attr = new ConfigRangeAttribute { Maximum = 100 };
        Assert.Equal(expected, attr.IsValid(value));
    }

    [Fact]
    public void IsValid_NoBounds_AlwaysValid()
    {
        var attr = new ConfigRangeAttribute();
        Assert.True(attr.IsValid(double.MinValue));
        Assert.True(attr.IsValid(0));
        Assert.True(attr.IsValid(double.MaxValue));
    }

    [Fact]
    public void IsValid_PortRange_ValidatesCorrectly()
    {
        // Common use case: port numbers 1-65535
        var attr = new ConfigRangeAttribute(1, 65535);

        Assert.False(attr.IsValid(0));       // Invalid port
        Assert.True(attr.IsValid(1));        // Min valid port
        Assert.True(attr.IsValid(80));       // HTTP
        Assert.True(attr.IsValid(443));      // HTTPS
        Assert.True(attr.IsValid(22222));    // RTPEngine default
        Assert.True(attr.IsValid(65535));    // Max valid port
        Assert.False(attr.IsValid(65536));   // Invalid port
    }

    [Fact]
    public void IsValid_Positive_WithExclusiveMinimum()
    {
        // Value must be > 0 (positive)
        var attr = new ConfigRangeAttribute { Minimum = 0, ExclusiveMinimum = true };

        Assert.False(attr.IsValid(-1));
        Assert.False(attr.IsValid(0));       // Zero is NOT positive
        Assert.True(attr.IsValid(0.001));
        Assert.True(attr.IsValid(1));
        Assert.True(attr.IsValid(1000));
    }

    [Fact]
    public void IsValid_NonNegative_WithInclusiveMinimum()
    {
        // Value must be >= 0 (non-negative)
        var attr = new ConfigRangeAttribute { Minimum = 0 };

        Assert.False(attr.IsValid(-1));
        Assert.False(attr.IsValid(-0.001));
        Assert.True(attr.IsValid(0));        // Zero IS non-negative
        Assert.True(attr.IsValid(1));
        Assert.True(attr.IsValid(1000));
    }

    #endregion

    #region ConfigRangeAttribute.HasMinimum/HasMaximum Tests

    [Fact]
    public void HasMinimum_WhenSet_ReturnsTrue()
    {
        var attr = new ConfigRangeAttribute { Minimum = 5 };
        Assert.True(attr.HasMinimum);
        Assert.False(attr.HasMaximum);
    }

    [Fact]
    public void HasMaximum_WhenSet_ReturnsTrue()
    {
        var attr = new ConfigRangeAttribute { Maximum = 100 };
        Assert.False(attr.HasMinimum);
        Assert.True(attr.HasMaximum);
    }

    [Fact]
    public void HasBothBounds_WhenBothSet_ReturnsTrue()
    {
        var attr = new ConfigRangeAttribute(1, 100);
        Assert.True(attr.HasMinimum);
        Assert.True(attr.HasMaximum);
    }

    [Fact]
    public void HasNoBounds_WhenDefault_ReturnsFalse()
    {
        var attr = new ConfigRangeAttribute();
        Assert.False(attr.HasMinimum);
        Assert.False(attr.HasMaximum);
    }

    [Fact]
    public void SentinelValues_AreInfinity()
    {
        Assert.Equal(double.NegativeInfinity, ConfigRangeAttribute.NoMinimum);
        Assert.Equal(double.PositiveInfinity, ConfigRangeAttribute.NoMaximum);
    }

    #endregion

    #region ConfigRangeAttribute.GetRangeDescription Tests

    [Theory]
    [InlineData(1, 10, false, false, "[1, 10]")]
    [InlineData(1, 10, true, false, "(1, 10]")]
    [InlineData(1, 10, false, true, "[1, 10)")]
    [InlineData(1, 10, true, true, "(1, 10)")]
    public void GetRangeDescription_FormatsCorrectly(double min, double max, bool exclMin, bool exclMax, string expected)
    {
        var attr = new ConfigRangeAttribute(min, max)
        {
            ExclusiveMinimum = exclMin,
            ExclusiveMaximum = exclMax
        };
        Assert.Equal(expected, attr.GetRangeDescription());
    }

    [Fact]
    public void GetRangeDescription_MinimumOnly_ShowsInfinity()
    {
        var attr = new ConfigRangeAttribute { Minimum = 1 };
        var desc = attr.GetRangeDescription();
        Assert.Contains("1", desc);
        Assert.Contains("\u221E", desc); // Infinity symbol
    }

    [Fact]
    public void GetRangeDescription_MaximumOnly_ShowsNegativeInfinity()
    {
        var attr = new ConfigRangeAttribute { Maximum = 100 };
        var desc = attr.GetRangeDescription();
        Assert.Contains("100", desc);
        Assert.Contains("-\u221E", desc); // Negative infinity
    }

    #endregion

    #region ValidateNumericRanges Integration Tests

    // Test configuration classes for validation testing
    [ServiceConfiguration]
    private class ConfigWithValidRange : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigRange(Minimum = 1, Maximum = 65535)]
        public int Port { get; set; } = 8080;
    }

    [ServiceConfiguration]
    private class ConfigWithInvalidRange : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigRange(Minimum = 1, Maximum = 100)]
        public int Value { get; set; } = 150; // Default exceeds maximum!
    }

    [ServiceConfiguration]
    private class ConfigWithMultipleRanges : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigRange(Minimum = 1, Maximum = 65535)]
        public int Port { get; set; } = 8080;

        [ConfigRange(Minimum = 1, Maximum = 300)]
        public int TimeoutSeconds { get; set; } = 30;

        [ConfigRange(Minimum = 0)]
        public int MaxConnections { get; set; } = 100;

        public int UnconstrainedValue { get; set; } = 999;
    }

    [ServiceConfiguration]
    private class ConfigWithExclusiveBounds : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigRange(Minimum = 0, ExclusiveMinimum = true)]
        public double PositiveValue { get; set; } = 1.0;
    }

    [ServiceConfiguration]
    private class ConfigWithDoubleRange : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigRange(Minimum = 0.0, Maximum = 1.0)]
        public double Percentage { get; set; } = 0.5;
    }

    [Fact]
    public void ValidateNumericRanges_ValidConfig_NoException()
    {
        IServiceConfiguration config = new ConfigWithValidRange { Port = 8080 };
        var exception = Record.Exception(() => config.ValidateNumericRanges());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateNumericRanges_InvalidConfig_ThrowsException()
    {
        IServiceConfiguration config = new ConfigWithInvalidRange { Value = 150 };
        var exception = Assert.Throws<InvalidOperationException>(() => config.ValidateNumericRanges());
        Assert.Contains("Value=150", exception.Message);
        Assert.Contains("[1, 100]", exception.Message);
    }

    [Fact]
    public void ValidateNumericRanges_BelowMinimum_ThrowsException()
    {
        IServiceConfiguration config = new ConfigWithValidRange { Port = 0 };
        var exception = Assert.Throws<InvalidOperationException>(() => config.ValidateNumericRanges());
        Assert.Contains("Port=0", exception.Message);
    }

    [Fact]
    public void ValidateNumericRanges_AboveMaximum_ThrowsException()
    {
        IServiceConfiguration config = new ConfigWithValidRange { Port = 70000 };
        var exception = Assert.Throws<InvalidOperationException>(() => config.ValidateNumericRanges());
        Assert.Contains("Port=70000", exception.Message);
    }

    [Fact]
    public void ValidateNumericRanges_MultipleProperties_ValidatesAll()
    {
        IServiceConfiguration config = new ConfigWithMultipleRanges
        {
            Port = 8080,
            TimeoutSeconds = 30,
            MaxConnections = 50,
            UnconstrainedValue = -1000 // No constraint, any value OK
        };
        var exception = Record.Exception(() => config.ValidateNumericRanges());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateNumericRanges_MultipleInvalid_ReportsAll()
    {
        IServiceConfiguration config = new ConfigWithMultipleRanges
        {
            Port = 0,           // Invalid: below 1
            TimeoutSeconds = 500, // Invalid: above 300
            MaxConnections = 50,
            UnconstrainedValue = 999
        };
        var exception = Assert.Throws<InvalidOperationException>(() => config.ValidateNumericRanges());
        Assert.Contains("Port=0", exception.Message);
        Assert.Contains("TimeoutSeconds=500", exception.Message);
    }

    [Fact]
    public void ValidateNumericRanges_ExclusiveMinimum_ZeroFails()
    {
        // Zero should fail with exclusive minimum
        var configObj = new ConfigWithExclusiveBounds { PositiveValue = 0.0 };
        IServiceConfiguration config = configObj;
        var exception = Assert.Throws<InvalidOperationException>(() => config.ValidateNumericRanges());
        Assert.Contains("PositiveValue=0", exception.Message);
    }

    [Fact]
    public void ValidateNumericRanges_ExclusiveMinimum_PositivePasses()
    {
        // Positive value should pass
        var configObj = new ConfigWithExclusiveBounds { PositiveValue = 0.001 };
        IServiceConfiguration config = configObj;
        var exception = Record.Exception(() => config.ValidateNumericRanges());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateNumericRanges_DoubleRange_ValidValue()
    {
        var configObj = new ConfigWithDoubleRange { Percentage = 0.5 };
        IServiceConfiguration config = configObj;
        var exception = Record.Exception(() => config.ValidateNumericRanges());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateNumericRanges_DoubleRange_InvalidValue()
    {
        var configObj = new ConfigWithDoubleRange { Percentage = 1.5 }; // Above 1.0
        IServiceConfiguration config = configObj;
        var exception = Assert.Throws<InvalidOperationException>(() => config.ValidateNumericRanges());
        Assert.Contains("Percentage=1.5", exception.Message);
    }

    [Fact]
    public void ValidateNumericRanges_AtMinimumBoundary_InclusiveValid()
    {
        // At minimum boundary
        IServiceConfiguration config = new ConfigWithValidRange { Port = 1 };
        var exception = Record.Exception(() => config.ValidateNumericRanges());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateNumericRanges_AtMaximumBoundary_InclusiveValid()
    {
        // At maximum boundary
        IServiceConfiguration config = new ConfigWithValidRange { Port = 65535 };
        var exception = Record.Exception(() => config.ValidateNumericRanges());
        Assert.Null(exception);
    }

    #endregion

    #region Combined Validate() Method Tests

    [ServiceConfiguration]
    private class ConfigWithStringAndRange : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        // Non-nullable string with default
        public string Name { get; set; } = "default";

        [ConfigRange(Minimum = 1, Maximum = 100)]
        public int Value { get; set; } = 50;
    }

    [Fact]
    public void Validate_CallsBothValidationMethods()
    {
        IServiceConfiguration config = new ConfigWithStringAndRange
        {
            Name = "test",
            Value = 50
        };

        // Should not throw - both validations pass
        var exception = Record.Exception(() => config.Validate());
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_RangeViolation_ThrowsException()
    {
        IServiceConfiguration config = new ConfigWithStringAndRange
        {
            Name = "test",
            Value = 200 // Invalid
        };

        var exception = Assert.Throws<InvalidOperationException>(() => config.Validate());
        Assert.Contains("Value=200", exception.Message);
    }

    #endregion
}
