using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using Xunit.Abstractions;

namespace BeyondImmersion.BannouService.Tests;

/// <summary>
/// Tests for ConfigPatternAttribute and regex pattern validation in configuration classes.
/// </summary>
[Collection("unit tests")]
public class ConfigPatternValidationTests : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    public ConfigPatternValidationTests(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigPatternValidationTests>.Instance;
    }

    #region ConfigPatternAttribute.IsValid Tests

    [Theory]
    [InlineData("https://example.com", @"^https?://", true)]
    [InlineData("http://example.com", @"^https?://", true)]
    [InlineData("ftp://example.com", @"^https?://", false)]
    [InlineData("example.com", @"^https?://", false)]
    [InlineData("", @"^https?://", false)]
    public void IsValid_UrlPattern_ValidatesCorrectly(string value, string pattern, bool expected)
    {
        var attr = new ConfigPatternAttribute(pattern);
        Assert.Equal(expected, attr.IsValid(value));
    }

    [Theory]
    [InlineData("admin@example.com", @"^[^@]+@[^@]+\.[^@]+$", true)]
    [InlineData("user.name@domain.co.uk", @"^[^@]+@[^@]+\.[^@]+$", true)]
    [InlineData("invalid-email", @"^[^@]+@[^@]+\.[^@]+$", false)]
    [InlineData("@missing-local.com", @"^[^@]+@[^@]+\.[^@]+$", false)]
    [InlineData("missing-domain@", @"^[^@]+@[^@]+\.[^@]+$", false)]
    public void IsValid_EmailPattern_ValidatesCorrectly(string value, string pattern, bool expected)
    {
        var attr = new ConfigPatternAttribute(pattern);
        Assert.Equal(expected, attr.IsValid(value));
    }

    [Theory]
    [InlineData("abc123", @"^[a-z0-9]+$", true)]
    [InlineData("ABC123", @"^[a-z0-9]+$", false)]  // Case sensitive
    [InlineData("abc-123", @"^[a-z0-9]+$", false)] // Hyphen not allowed
    [InlineData("", @"^[a-z0-9]+$", false)]        // Empty doesn't match +
    public void IsValid_AlphanumericPattern_ValidatesCorrectly(string value, string pattern, bool expected)
    {
        var attr = new ConfigPatternAttribute(pattern);
        Assert.Equal(expected, attr.IsValid(value));
    }

    [Theory]
    [InlineData("v1.0.0", @"^v\d+\.\d+\.\d+$", true)]
    [InlineData("v10.20.30", @"^v\d+\.\d+\.\d+$", true)]
    [InlineData("1.0.0", @"^v\d+\.\d+\.\d+$", false)]    // Missing 'v' prefix
    [InlineData("v1.0", @"^v\d+\.\d+\.\d+$", false)]     // Missing patch version
    [InlineData("v1.0.0-beta", @"^v\d+\.\d+\.\d+$", false)] // Extra suffix
    public void IsValid_SemverPattern_ValidatesCorrectly(string value, string pattern, bool expected)
    {
        var attr = new ConfigPatternAttribute(pattern);
        Assert.Equal(expected, attr.IsValid(value));
    }

    [Fact]
    public void IsValid_NullValue_ReturnsTrue()
    {
        var attr = new ConfigPatternAttribute(@"^https?://");
        Assert.True(attr.IsValid(null));
    }

    [Fact]
    public void IsValid_EmptyPattern_MatchesEverything()
    {
        var attr = new ConfigPatternAttribute("");
        Assert.True(attr.IsValid("anything"));
        Assert.True(attr.IsValid(""));
    }

    [Fact]
    public void IsValid_DotStarPattern_MatchesEverything()
    {
        var attr = new ConfigPatternAttribute(".*");
        Assert.True(attr.IsValid("anything"));
        Assert.True(attr.IsValid(""));
        Assert.True(attr.IsValid("with spaces and symbols!@#$"));
    }

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("example.com", true)]
    [InlineData("sub.example.com", true)]
    [InlineData("a-b.example.com", true)]
    [InlineData("-invalid.com", false)]      // Can't start with hyphen
    [InlineData("invalid-.com", false)]      // Can't end segment with hyphen
    [InlineData(".invalid.com", false)]      // Can't start with dot
    public void IsValid_DomainNamePattern_ValidatesCorrectly(string value, bool expected)
    {
        // Simplified domain pattern
        var pattern = @"^[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9-]*[a-zA-Z0-9])?)*$";
        var attr = new ConfigPatternAttribute(pattern);
        Assert.Equal(expected, attr.IsValid(value));
    }

    [Fact]
    public void IsValid_CaseInsensitiveWithFlag_MatchesCorrectly()
    {
        // Pattern with inline case-insensitive flag
        var attr = new ConfigPatternAttribute(@"(?i)^https?://");
        Assert.True(attr.IsValid("HTTPS://EXAMPLE.COM"));
        Assert.True(attr.IsValid("Http://Example.Com"));
    }

    #endregion

    #region ConfigPatternAttribute Properties Tests

    [Fact]
    public void Constructor_SetsPattern()
    {
        var attr = new ConfigPatternAttribute(@"^test$");
        Assert.Equal(@"^test$", attr.Pattern);
    }

    [Fact]
    public void Constructor_NullPattern_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ConfigPatternAttribute(null!));
    }

    [Fact]
    public void Description_WhenSet_ReturnsDescription()
    {
        var attr = new ConfigPatternAttribute(@"^https?://")
        {
            Description = "Must be a valid HTTP or HTTPS URL"
        };
        Assert.Equal("Must be a valid HTTP or HTTPS URL", attr.Description);
    }

    [Fact]
    public void MatchTimeout_DefaultsToOneSecond()
    {
        var attr = new ConfigPatternAttribute(@"^test$");
        Assert.Equal(TimeSpan.FromSeconds(1), attr.MatchTimeout);
    }

    [Fact]
    public void MatchTimeout_CanBeCustomized()
    {
        var attr = new ConfigPatternAttribute(@"^test$")
        {
            MatchTimeout = TimeSpan.FromMilliseconds(100)
        };
        Assert.Equal(TimeSpan.FromMilliseconds(100), attr.MatchTimeout);
    }

    #endregion

    #region ConfigPatternAttribute.GetPatternDescription Tests

    [Fact]
    public void GetPatternDescription_WithDescription_ReturnsDescription()
    {
        var attr = new ConfigPatternAttribute(@"^https?://")
        {
            Description = "Must start with http:// or https://"
        };
        Assert.Equal("Must start with http:// or https://", attr.GetPatternDescription());
    }

    [Fact]
    public void GetPatternDescription_WithoutDescription_ReturnsPatternMessage()
    {
        var attr = new ConfigPatternAttribute(@"^https?://");
        Assert.Equal("must match pattern: ^https?://", attr.GetPatternDescription());
    }

    #endregion

    #region ValidatePatterns Integration Tests

    [ServiceConfiguration]
    private class ConfigWithValidPattern : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigPattern(@"^https?://")]
        public string ServiceUrl { get; set; } = "https://api.example.com";
    }

    [ServiceConfiguration]
    private class ConfigWithInvalidPattern : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigPattern(@"^https?://")]
        public string ServiceUrl { get; set; } = "ftp://invalid.com"; // Doesn't match pattern
    }

    [ServiceConfiguration]
    private class ConfigWithMultiplePatterns : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigPattern(@"^https?://")]
        public string ApiUrl { get; set; } = "https://api.example.com";

        [ConfigPattern(@"^[a-z][a-z0-9-]*$")]
        public string ServiceName { get; set; } = "my-service";

        [ConfigPattern(@"^v\d+\.\d+\.\d+$")]
        public string Version { get; set; } = "v1.0.0";

        public string UnconstrainedValue { get; set; } = "anything goes";
    }

    [ServiceConfiguration]
    private class ConfigWithPatternAndDescription : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigPattern(@"^https?://", Description = "Must be a valid HTTP/HTTPS URL")]
        public string Endpoint { get; set; } = "https://example.com";
    }

    [ServiceConfiguration]
    private class ConfigWithNullablePatternString : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigPattern(@"^https?://")]
        public string? OptionalUrl { get; set; }
    }

    [Fact]
    public void ValidatePatterns_ValidConfig_NoException()
    {
        IServiceConfiguration config = new ConfigWithValidPattern { ServiceUrl = "https://api.example.com" };
        var exception = Record.Exception(() => config.ValidatePatterns());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidatePatterns_InvalidConfig_ThrowsException()
    {
        IServiceConfiguration config = new ConfigWithInvalidPattern { ServiceUrl = "ftp://invalid.com" };
        var exception = Assert.Throws<InvalidOperationException>(() => config.ValidatePatterns());
        Assert.Contains("ServiceUrl", exception.Message);
        Assert.Contains("ftp://invalid.com", exception.Message);
    }

    [Fact]
    public void ValidatePatterns_MultipleProperties_ValidatesAll()
    {
        IServiceConfiguration config = new ConfigWithMultiplePatterns
        {
            ApiUrl = "https://api.example.com",
            ServiceName = "my-service",
            Version = "v1.0.0",
            UnconstrainedValue = "!@#$%^&*()" // No pattern constraint
        };
        var exception = Record.Exception(() => config.ValidatePatterns());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidatePatterns_MultipleInvalid_ReportsAll()
    {
        IServiceConfiguration config = new ConfigWithMultiplePatterns
        {
            ApiUrl = "not-a-url",           // Invalid
            ServiceName = "Invalid_Name",    // Invalid (underscore and uppercase)
            Version = "1.0.0",               // Invalid (missing 'v' prefix)
            UnconstrainedValue = "anything"
        };
        var exception = Assert.Throws<InvalidOperationException>(() => config.ValidatePatterns());
        Assert.Contains("ApiUrl", exception.Message);
        Assert.Contains("ServiceName", exception.Message);
        Assert.Contains("Version", exception.Message);
    }

    [Fact]
    public void ValidatePatterns_WithDescription_IncludesDescriptionInError()
    {
        var configObj = new ConfigWithPatternAndDescription { Endpoint = "invalid-url" };
        IServiceConfiguration config = configObj;
        var exception = Assert.Throws<InvalidOperationException>(() => config.ValidatePatterns());
        Assert.Contains("Must be a valid HTTP/HTTPS URL", exception.Message);
    }

    [Fact]
    public void ValidatePatterns_NullableStringWithNull_SkipsValidation()
    {
        IServiceConfiguration config = new ConfigWithNullablePatternString { OptionalUrl = null };
        var exception = Record.Exception(() => config.ValidatePatterns());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidatePatterns_NullableStringWithValue_ValidatesPattern()
    {
        IServiceConfiguration config = new ConfigWithNullablePatternString { OptionalUrl = "https://example.com" };
        var exception = Record.Exception(() => config.ValidatePatterns());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidatePatterns_NullableStringWithInvalidValue_ThrowsException()
    {
        IServiceConfiguration config = new ConfigWithNullablePatternString { OptionalUrl = "not-a-url" };
        var exception = Assert.Throws<InvalidOperationException>(() => config.ValidatePatterns());
        Assert.Contains("OptionalUrl", exception.Message);
    }

    #endregion

    #region Combined Validate() Method Tests

    [ServiceConfiguration]
    private class ConfigWithPatternAndOtherConstraints : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigPattern(@"^https?://")]
        public string ApiUrl { get; set; } = "https://example.com";

        [ConfigStringLength(MinLength = 8)]
        public string ApiKey { get; set; } = "secret-key-here";

        [ConfigRange(Minimum = 1, Maximum = 65535)]
        public int Port { get; set; } = 8080;
    }

    [Fact]
    public void Validate_AllConstraintsPass_NoException()
    {
        IServiceConfiguration config = new ConfigWithPatternAndOtherConstraints
        {
            ApiUrl = "https://api.example.com",
            ApiKey = "my-secret-api-key",
            Port = 443
        };

        var exception = Record.Exception(() => config.Validate());
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_PatternViolation_ThrowsException()
    {
        IServiceConfiguration config = new ConfigWithPatternAndOtherConstraints
        {
            ApiUrl = "invalid-url",  // Pattern violation
            ApiKey = "valid-key-here",
            Port = 443
        };

        var exception = Assert.Throws<InvalidOperationException>(() => config.Validate());
        Assert.Contains("ApiUrl", exception.Message);
    }

    [Fact]
    public void Validate_StringLengthViolation_ThrowsException()
    {
        IServiceConfiguration config = new ConfigWithPatternAndOtherConstraints
        {
            ApiUrl = "https://example.com",
            ApiKey = "short",  // Length violation (< 8)
            Port = 443
        };

        var exception = Assert.Throws<InvalidOperationException>(() => config.Validate());
        Assert.Contains("ApiKey", exception.Message);
    }

    [Fact]
    public void Validate_RangeViolation_ThrowsException()
    {
        IServiceConfiguration config = new ConfigWithPatternAndOtherConstraints
        {
            ApiUrl = "https://example.com",
            ApiKey = "valid-key-here",
            Port = 0  // Range violation (< 1)
        };

        var exception = Assert.Throws<InvalidOperationException>(() => config.Validate());
        Assert.Contains("Port", exception.Message);
    }

    #endregion

    #region Edge Cases and Special Patterns

    [Fact]
    public void IsValid_PatternWithSpecialCharacters_WorksCorrectly()
    {
        // Pattern matching literal special regex characters
        var attr = new ConfigPatternAttribute(@"^\$\d+\.\d{2}$");  // Dollar amount like $10.99
        Assert.True(attr.IsValid("$10.99"));
        Assert.True(attr.IsValid("$0.01"));
        Assert.False(attr.IsValid("10.99"));  // Missing $
        Assert.False(attr.IsValid("$10.9"));  // Only one decimal digit
    }

    [Fact]
    public void IsValid_UnicodePattern_WorksCorrectly()
    {
        var attr = new ConfigPatternAttribute(@"^[\p{L}]+$");  // Unicode letters only
        Assert.True(attr.IsValid("Hello"));
        Assert.True(attr.IsValid("Привет"));  // Russian
        Assert.True(attr.IsValid("你好"));     // Chinese
        Assert.False(attr.IsValid("Hello123"));
    }

    [Fact]
    public void IsValid_PatternWithAnchors_RequiresFullMatch()
    {
        var attrAnchored = new ConfigPatternAttribute(@"^test$");
        Assert.True(attrAnchored.IsValid("test"));
        Assert.False(attrAnchored.IsValid("test123"));
        Assert.False(attrAnchored.IsValid("pretest"));

        var attrUnanchored = new ConfigPatternAttribute("test");
        Assert.True(attrUnanchored.IsValid("test"));
        Assert.True(attrUnanchored.IsValid("test123"));
        Assert.True(attrUnanchored.IsValid("pretest"));
    }

    [Fact]
    public void IsValid_InvalidRegexPattern_ThrowsOnFirstUse()
    {
        var attr = new ConfigPatternAttribute(@"[invalid(regex");
        // RegexParseException is thrown for invalid regex patterns
        Assert.ThrowsAny<ArgumentException>(() => attr.IsValid("test"));
    }

    #endregion
}
