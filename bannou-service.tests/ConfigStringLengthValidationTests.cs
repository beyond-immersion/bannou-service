using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using Xunit.Abstractions;

namespace BeyondImmersion.BannouService.Tests;

/// <summary>
/// Tests for ConfigStringLengthAttribute and string length validation in configuration classes.
/// </summary>
[Collection("unit tests")]
public class ConfigStringLengthValidationTests : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    public ConfigStringLengthValidationTests(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigStringLengthValidationTests>.Instance;
    }

    #region ConfigStringLengthAttribute.IsValid Tests

    [Theory]
    [InlineData("hello", 1, 10, true)]      // Within range
    [InlineData("a", 1, 10, true)]          // At minimum
    [InlineData("1234567890", 1, 10, true)] // At maximum
    [InlineData("", 1, 10, false)]          // Below minimum (empty)
    [InlineData("12345678901", 1, 10, false)] // Above maximum
    [InlineData("test", 0, 100, true)]      // Zero minimum allowed
    [InlineData("", 0, 100, true)]          // Empty with zero minimum
    public void IsValid_BothBounds_ValidatesCorrectly(string value, int minLength, int maxLength, bool expected)
    {
        var attr = new ConfigStringLengthAttribute(minLength, maxLength);
        Assert.Equal(expected, attr.IsValid(value));
    }

    [Theory]
    [InlineData("12345678901234567890123456789012", 32, true)]  // At minimum (32 chars)
    [InlineData("1234567890123456789012345678901", 32, false)]  // Below minimum (31 chars)
    [InlineData("123456789012345678901234567890123", 32, true)] // Above minimum (33 chars)
    [InlineData("a", 1, true)]              // Minimum 1
    [InlineData("", 1, false)]              // Empty with minimum 1
    [InlineData("", 0, true)]               // Empty with minimum 0
    public void IsValid_MinLengthOnly_ValidatesCorrectly(string value, int minLength, bool expected)
    {
        var attr = new ConfigStringLengthAttribute { MinLength = minLength };
        Assert.Equal(expected, attr.IsValid(value));
    }

    [Theory]
    [InlineData("abc", 10, true)]           // Below maximum
    [InlineData("1234567890", 10, true)]    // At maximum
    [InlineData("12345678901", 10, false)]  // Above maximum
    [InlineData("", 10, true)]              // Empty (no minimum)
    [InlineData("a", 1, true)]              // Maximum 1, length 1
    [InlineData("ab", 1, false)]            // Maximum 1, length 2
    public void IsValid_MaxLengthOnly_ValidatesCorrectly(string value, int maxLength, bool expected)
    {
        var attr = new ConfigStringLengthAttribute { MaxLength = maxLength };
        Assert.Equal(expected, attr.IsValid(value));
    }

    [Fact]
    public void IsValid_NoBounds_AlwaysValid()
    {
        var attr = new ConfigStringLengthAttribute();
        Assert.True(attr.IsValid(""));
        Assert.True(attr.IsValid("a"));
        Assert.True(attr.IsValid(new string('x', 10000)));
    }

    [Fact]
    public void IsValid_NullValue_WithNoMinLength_ReturnsTrue()
    {
        var attr = new ConfigStringLengthAttribute();
        Assert.True(attr.IsValid(null));
    }

    [Fact]
    public void IsValid_NullValue_WithMinLengthZero_ReturnsTrue()
    {
        var attr = new ConfigStringLengthAttribute { MinLength = 0 };
        Assert.True(attr.IsValid(null));
    }

    [Fact]
    public void IsValid_NullValue_WithMinLengthGreaterThanZero_ReturnsFalse()
    {
        var attr = new ConfigStringLengthAttribute { MinLength = 1 };
        Assert.False(attr.IsValid(null));
    }

    [Fact]
    public void IsValid_JwtSecret_MinimumLength()
    {
        // Common use case: JWT secrets should be at least 32 characters
        var attr = new ConfigStringLengthAttribute { MinLength = 32 };

        Assert.False(attr.IsValid("short-secret"));  // Too short
        Assert.False(attr.IsValid("1234567890123456789012345678901")); // 31 chars
        Assert.True(attr.IsValid("12345678901234567890123456789012"));  // Exactly 32
        Assert.True(attr.IsValid("this-is-a-very-long-secret-key-for-jwt-signing")); // > 32
    }

    [Fact]
    public void IsValid_ConnectionString_SanityMaxLength()
    {
        // Common use case: connection strings shouldn't exceed reasonable length
        var attr = new ConfigStringLengthAttribute { MaxLength = 2048 };

        Assert.True(attr.IsValid("Server=localhost;Database=test;"));
        Assert.True(attr.IsValid(new string('a', 2048)));
        Assert.False(attr.IsValid(new string('a', 2049)));
    }

    #endregion

    #region ConfigStringLengthAttribute.HasMinLength/HasMaxLength Tests

    [Fact]
    public void HasMinLength_WhenSet_ReturnsTrue()
    {
        var attr = new ConfigStringLengthAttribute { MinLength = 5 };
        Assert.True(attr.HasMinLength);
        Assert.False(attr.HasMaxLength);
    }

    [Fact]
    public void HasMaxLength_WhenSet_ReturnsTrue()
    {
        var attr = new ConfigStringLengthAttribute { MaxLength = 100 };
        Assert.False(attr.HasMinLength);
        Assert.True(attr.HasMaxLength);
    }

    [Fact]
    public void HasBothBounds_WhenBothSet_ReturnsTrue()
    {
        var attr = new ConfigStringLengthAttribute(1, 100);
        Assert.True(attr.HasMinLength);
        Assert.True(attr.HasMaxLength);
    }

    [Fact]
    public void HasNoBounds_WhenDefault_ReturnsFalse()
    {
        var attr = new ConfigStringLengthAttribute();
        Assert.False(attr.HasMinLength);
        Assert.False(attr.HasMaxLength);
    }

    [Fact]
    public void SentinelValues_AreNegativeOne()
    {
        Assert.Equal(-1, ConfigStringLengthAttribute.NoMinLength);
        Assert.Equal(-1, ConfigStringLengthAttribute.NoMaxLength);
    }

    #endregion

    #region ConfigStringLengthAttribute.GetLengthDescription Tests

    [Fact]
    public void GetLengthDescription_BothBounds_FormatsCorrectly()
    {
        var attr = new ConfigStringLengthAttribute(10, 100);
        Assert.Equal("[10, 100] characters", attr.GetLengthDescription());
    }

    [Fact]
    public void GetLengthDescription_MinLengthOnly_FormatsCorrectly()
    {
        var attr = new ConfigStringLengthAttribute { MinLength = 32 };
        Assert.Equal(">= 32 characters", attr.GetLengthDescription());
    }

    [Fact]
    public void GetLengthDescription_MaxLengthOnly_FormatsCorrectly()
    {
        var attr = new ConfigStringLengthAttribute { MaxLength = 256 };
        Assert.Equal("<= 256 characters", attr.GetLengthDescription());
    }

    [Fact]
    public void GetLengthDescription_NoBounds_ReturnsAnyLength()
    {
        var attr = new ConfigStringLengthAttribute();
        Assert.Equal("any length", attr.GetLengthDescription());
    }

    #endregion

    #region ValidateStringLengths Integration Tests

    // Test configuration classes for validation testing
    [ServiceConfiguration]
    private class ConfigWithValidStringLength : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigStringLength(MinLength = 8, MaxLength = 64)]
        public string Password { get; set; } = "default-password-here";
    }

    [ServiceConfiguration]
    private class ConfigWithInvalidStringLength : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigStringLength(MinLength = 32)]
        public string JwtSecret { get; set; } = "short"; // Default is too short!
    }

    [ServiceConfiguration]
    private class ConfigWithMultipleStringLengths : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigStringLength(MinLength = 32)]
        public string JwtSecret { get; set; } = "12345678901234567890123456789012"; // Exactly 32

        [ConfigStringLength(MinLength = 8, MaxLength = 64)]
        public string ApiKey { get; set; } = "api-key-value";

        [ConfigStringLength(MaxLength = 256)]
        public string Description { get; set; } = "A short description";

        public string UnconstrainedValue { get; set; } = "anything goes";
    }

    [ServiceConfiguration]
    private class ConfigWithNullableString : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigStringLength(MinLength = 10)]
        public string? OptionalField { get; set; }
    }

    [Fact]
    public void ValidateStringLengths_ValidConfig_NoException()
    {
        IServiceConfiguration config = new ConfigWithValidStringLength { Password = "valid-password-12345" };
        var exception = Record.Exception(() => config.ValidateStringLengths());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateStringLengths_InvalidConfig_ThrowsException()
    {
        IServiceConfiguration config = new ConfigWithInvalidStringLength { JwtSecret = "too-short" };
        var exception = Assert.Throws<InvalidOperationException>(() => config.ValidateStringLengths());
        Assert.Contains("JwtSecret", exception.Message);
        Assert.Contains(">= 32 characters", exception.Message);
    }

    [Fact]
    public void ValidateStringLengths_BelowMinLength_ThrowsException()
    {
        IServiceConfiguration config = new ConfigWithValidStringLength { Password = "short" }; // 5 chars, min is 8
        var exception = Assert.Throws<InvalidOperationException>(() => config.ValidateStringLengths());
        Assert.Contains("Password", exception.Message);
        Assert.Contains("length=5", exception.Message);
    }

    [Fact]
    public void ValidateStringLengths_AboveMaxLength_ThrowsException()
    {
        IServiceConfiguration config = new ConfigWithValidStringLength
        {
            Password = new string('x', 100) // 100 chars, max is 64
        };
        var exception = Assert.Throws<InvalidOperationException>(() => config.ValidateStringLengths());
        Assert.Contains("Password", exception.Message);
        Assert.Contains("length=100", exception.Message);
    }

    [Fact]
    public void ValidateStringLengths_MultipleProperties_ValidatesAll()
    {
        IServiceConfiguration config = new ConfigWithMultipleStringLengths
        {
            JwtSecret = "12345678901234567890123456789012", // Exactly 32
            ApiKey = "valid-api-key",
            Description = "Valid description",
            UnconstrainedValue = "" // No constraint, any value OK
        };
        var exception = Record.Exception(() => config.ValidateStringLengths());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateStringLengths_MultipleInvalid_ReportsAll()
    {
        IServiceConfiguration config = new ConfigWithMultipleStringLengths
        {
            JwtSecret = "short",          // Invalid: below 32
            ApiKey = "tiny",              // Invalid: below 8
            Description = "ok",
            UnconstrainedValue = "anything"
        };
        var exception = Assert.Throws<InvalidOperationException>(() => config.ValidateStringLengths());
        Assert.Contains("JwtSecret", exception.Message);
        Assert.Contains("ApiKey", exception.Message);
    }

    [Fact]
    public void ValidateStringLengths_AtMinimumBoundary_Valid()
    {
        IServiceConfiguration config = new ConfigWithValidStringLength
        {
            Password = "12345678" // Exactly 8 chars, minimum is 8
        };
        var exception = Record.Exception(() => config.ValidateStringLengths());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateStringLengths_AtMaximumBoundary_Valid()
    {
        IServiceConfiguration config = new ConfigWithValidStringLength
        {
            Password = new string('x', 64) // Exactly 64 chars, maximum is 64
        };
        var exception = Record.Exception(() => config.ValidateStringLengths());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateStringLengths_NullableStringWithNull_SkipsValidation()
    {
        // Nullable string that is null - should skip validation
        IServiceConfiguration config = new ConfigWithNullableString { OptionalField = null };
        // This tests the behavior with nullable strings
        // The attribute has MinLength = 10, but null should be treated as valid
        // because the property is nullable (optional)
        var exception = Record.Exception(() => config.ValidateStringLengths());
        Assert.Null(exception);
    }

    #endregion

    #region Combined Validate() Method Tests

    [ServiceConfiguration]
    private class ConfigWithStringLengthAndRange : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigStringLength(MinLength = 10)]
        public string Name { get; set; } = "default-name";

        [ConfigRange(Minimum = 1, Maximum = 100)]
        public int Value { get; set; } = 50;
    }

    [Fact]
    public void Validate_CallsAllValidationMethods()
    {
        IServiceConfiguration config = new ConfigWithStringLengthAndRange
        {
            Name = "valid-name-here",
            Value = 50
        };

        // Should not throw - all validations pass
        var exception = Record.Exception(() => config.Validate());
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_StringLengthViolation_ThrowsException()
    {
        IServiceConfiguration config = new ConfigWithStringLengthAndRange
        {
            Name = "short", // Invalid - less than 10 chars
            Value = 50
        };

        var exception = Assert.Throws<InvalidOperationException>(() => config.Validate());
        Assert.Contains("Name", exception.Message);
    }

    [Fact]
    public void Validate_RangeViolation_ThrowsException()
    {
        IServiceConfiguration config = new ConfigWithStringLengthAndRange
        {
            Name = "valid-name-here",
            Value = 200 // Invalid - greater than 100
        };

        var exception = Assert.Throws<InvalidOperationException>(() => config.Validate());
        Assert.Contains("Value=200", exception.Message);
    }

    #endregion
}
