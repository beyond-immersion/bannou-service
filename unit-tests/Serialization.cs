using BeyondImmersion.BannouService.Configuration;
using System.Text.Json;
using Xunit.Abstractions;

namespace BeyondImmersion.BannouService.UnitTests;

/// <summary>
/// Unit tests for System.Text.Json serialization patterns used throughout Bannou.
/// These tests verify that BannouJson correctly handles our data models,
/// especially the critical Unix timestamp properties that have caused 401 failures.
/// </summary>
[Collection("unit tests")]
public class Serialization : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    public Serialization(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = output.BuildLoggerFor<Serialization>();
    }

    #region Test Models (mirroring AuthService.SessionDataModel)

    /// <summary>
    /// Test model mimicking SessionDataModel structure from AuthService.
    /// This allows us to test serialization without requiring access to the private class.
    /// </summary>
    private class SessionDataModel
    {
        public Guid AccountId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new List<string>();
        public string SessionId { get; set; } = string.Empty;

        // Store as Unix epoch timestamps (long) to avoid System.Text.Json DateTimeOffset serialization issues
        public long CreatedAtUnix { get; set; }
        public long ExpiresAtUnix { get; set; }

        // Expose as DateTimeOffset for code convenience (not serialized)
        [System.Text.Json.Serialization.JsonIgnore]
        public DateTimeOffset CreatedAt
        {
            get => DateTimeOffset.FromUnixTimeSeconds(CreatedAtUnix);
            set => CreatedAtUnix = value.ToUnixTimeSeconds();
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public DateTimeOffset ExpiresAt
        {
            get => DateTimeOffset.FromUnixTimeSeconds(ExpiresAtUnix);
            set => ExpiresAtUnix = value.ToUnixTimeSeconds();
        }
    }

    /// <summary>
    /// Model with various numeric types to test serialization behavior.
    /// </summary>
    private class NumericTypesModel
    {
        public int IntValue { get; set; }
        public long LongValue { get; set; }
        public double DoubleValue { get; set; }
        public decimal DecimalValue { get; set; }
        public float FloatValue { get; set; }
    }

    /// <summary>
    /// Model to test case-sensitivity behavior.
    /// </summary>
    private class CaseSensitiveModel
    {
        public string PascalCase { get; set; } = string.Empty;
        public string ALLCAPS { get; set; } = string.Empty;
        public string lowercase { get; set; } = string.Empty;
    }

    #endregion

    #region BannouSerializerConfig Tests

    [Fact]
    public void BannouSerializerConfig_Exists()
    {
        // Verify the shared serializer config exists
        Assert.NotNull(IServiceConfiguration.BannouSerializerConfig);
    }

    [Fact]
    public void BannouSerializerConfig_NumberHandling_IsStrict()
    {
        // Verify NumberHandling is Strict (numbers must be actual numbers, not strings)
        var config = IServiceConfiguration.BannouSerializerConfig;
        Assert.Equal(System.Text.Json.Serialization.JsonNumberHandling.Strict, config.NumberHandling);
    }

    [Fact]
    public void BannouSerializerConfig_PropertyNameCaseInsensitive_IsTrue()
    {
        // Verify PropertyNameCaseInsensitive is true (case-insensitive matching for flexibility)
        var config = IServiceConfiguration.BannouSerializerConfig;
        Assert.True(config.PropertyNameCaseInsensitive);
    }

    [Fact]
    public void BannouSerializerConfig_PropertyNamingPolicy_IsNull()
    {
        // Verify no naming policy (PascalCase preserved)
        var config = IServiceConfiguration.BannouSerializerConfig;
        Assert.Null(config.PropertyNamingPolicy);
    }

    #endregion

    #region SessionDataModel Serialization Tests

    [Fact]
    public void SessionDataModel_RoundTrip_PreservesAllProperties()
    {
        var original = new SessionDataModel
        {
            AccountId = Guid.NewGuid(),
            Email = "test@example.com",
            DisplayName = "Test User",
            Roles = new List<string> { "user", "admin" },
            SessionId = Guid.NewGuid().ToString("N"),
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ExpiresAtUnix = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        };

        var json = JsonSerializer.Serialize(original, IServiceConfiguration.BannouSerializerConfig);
        var deserialized = JsonSerializer.Deserialize<SessionDataModel>(json, IServiceConfiguration.BannouSerializerConfig);

        Assert.NotNull(deserialized);
        Assert.Equal(original.AccountId, deserialized.AccountId);
        Assert.Equal(original.Email, deserialized.Email);
        Assert.Equal(original.DisplayName, deserialized.DisplayName);
        Assert.Equal(original.Roles, deserialized.Roles);
        Assert.Equal(original.SessionId, deserialized.SessionId);
        Assert.Equal(original.CreatedAtUnix, deserialized.CreatedAtUnix);
        Assert.Equal(original.ExpiresAtUnix, deserialized.ExpiresAtUnix);
    }

    [Fact]
    public void SessionDataModel_ExpiresAtUnix_RoundTrip_PreservesValue()
    {
        // Specifically test the problematic ExpiresAtUnix property
        var expectedExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var original = new SessionDataModel { ExpiresAtUnix = expectedExpiresAt };

        var json = JsonSerializer.Serialize(original, IServiceConfiguration.BannouSerializerConfig);
        var deserialized = JsonSerializer.Deserialize<SessionDataModel>(json, IServiceConfiguration.BannouSerializerConfig);

        Assert.NotNull(deserialized);
        Assert.Equal(expectedExpiresAt, deserialized.ExpiresAtUnix);
        Assert.NotEqual(0, deserialized.ExpiresAtUnix);
    }

    [Fact]
    public void SessionDataModel_LongProperties_NotDefaultAfterDeserialization()
    {
        // Test that long properties don't default to 0 after deserialization
        var original = new SessionDataModel
        {
            CreatedAtUnix = 1733500000, // Specific non-zero value
            ExpiresAtUnix = 1733503600  // Specific non-zero value
        };

        var json = JsonSerializer.Serialize(original, IServiceConfiguration.BannouSerializerConfig);
        var deserialized = JsonSerializer.Deserialize<SessionDataModel>(json, IServiceConfiguration.BannouSerializerConfig);

        Assert.NotNull(deserialized);
        Assert.Equal(1733500000, deserialized.CreatedAtUnix);
        Assert.Equal(1733503600, deserialized.ExpiresAtUnix);
    }

    [Fact]
    public void SessionDataModel_Serializes_WithPascalCase()
    {
        var model = new SessionDataModel
        {
            ExpiresAtUnix = 1733500000,
            SessionId = "test-session"
        };

        var json = JsonSerializer.Serialize(model, IServiceConfiguration.BannouSerializerConfig);

        // Verify property names are PascalCase (not camelCase)
        Assert.Contains("\"ExpiresAtUnix\":", json);
        Assert.Contains("\"SessionId\":", json);
        Assert.DoesNotContain("\"expiresAtUnix\":", json);
        Assert.DoesNotContain("\"sessionId\":", json);
    }

    #endregion

    #region Case Sensitivity Tests - THE CRITICAL ISSUE

    [Fact]
    public void CaseSensitive_PascalCase_Deserializes_Correctly()
    {
        // This should work - property names match exactly
        var json = """{"PascalCase":"value1","ALLCAPS":"value2","lowercase":"value3"}""";
        var deserialized = JsonSerializer.Deserialize<CaseSensitiveModel>(json, IServiceConfiguration.BannouSerializerConfig);

        Assert.NotNull(deserialized);
        Assert.Equal("value1", deserialized.PascalCase);
        Assert.Equal("value2", deserialized.ALLCAPS);
        Assert.Equal("value3", deserialized.lowercase);
    }

    [Fact]
    public void CaseInsensitive_CamelCase_Matches_PascalCase()
    {
        // With PropertyNameCaseInsensitive = true, camelCase JSON matches PascalCase properties
        var camelCaseJson = """{"pascalCase":"value1","allcaps":"value2","lowercase":"value3"}""";
        var deserialized = JsonSerializer.Deserialize<CaseSensitiveModel>(camelCaseJson, IServiceConfiguration.BannouSerializerConfig);

        Assert.NotNull(deserialized);
        // With case-insensitive matching, camelCase matches PascalCase
        Assert.Equal("value1", deserialized.PascalCase);
        Assert.Equal("value2", deserialized.ALLCAPS);
        Assert.Equal("value3", deserialized.lowercase);
    }

    [Fact]
    public void SessionDataModel_CamelCase_Deserializes_ExpiresAtUnix()
    {
        // With PropertyNameCaseInsensitive = true, camelCase JSON properly deserializes
        var camelCaseJson = """
        {
            "accountId":"00000000-0000-0000-0000-000000000001",
            "email":"test@example.com",
            "sessionId":"test-session-123",
            "createdAtUnix":1733500000,
            "expiresAtUnix":1733503600
        }
        """;

        var deserialized = JsonSerializer.Deserialize<SessionDataModel>(camelCaseJson, IServiceConfiguration.BannouSerializerConfig);

        Assert.NotNull(deserialized);
        // With case-insensitive matching, camelCase properties match PascalCase
        Assert.Equal(new Guid("00000000-0000-0000-0000-000000000001"), deserialized.AccountId);
        Assert.Equal("test@example.com", deserialized.Email);
        Assert.Equal("test-session-123", deserialized.SessionId);
        Assert.Equal(1733500000, deserialized.CreatedAtUnix);
        Assert.Equal(1733503600, deserialized.ExpiresAtUnix);
    }

    [Fact]
    public void SessionDataModel_PascalCase_Does_Deserialize_ExpiresAtUnix()
    {
        // When data is saved AND read with our config, it should work
        var pascalCaseJson = """
        {
            "AccountId":"00000000-0000-0000-0000-000000000001",
            "Email":"test@example.com",
            "SessionId":"test-session-123",
            "CreatedAtUnix":1733500000,
            "ExpiresAtUnix":1733503600
        }
        """;

        var deserialized = JsonSerializer.Deserialize<SessionDataModel>(pascalCaseJson, IServiceConfiguration.BannouSerializerConfig);

        Assert.NotNull(deserialized);
        Assert.Equal(new Guid("00000000-0000-0000-0000-000000000001"), deserialized.AccountId);
        Assert.Equal("test@example.com", deserialized.Email);
        Assert.Equal("test-session-123", deserialized.SessionId);
        Assert.Equal(1733500000, deserialized.CreatedAtUnix);
        Assert.Equal(1733503600, deserialized.ExpiresAtUnix);  // Should be correct!
    }

    #endregion

    #region Numeric Type Tests

    [Fact]
    public void LongValue_RoundTrip_PreservesValue()
    {
        var original = new NumericTypesModel { LongValue = 1765031079 };
        var json = JsonSerializer.Serialize(original, IServiceConfiguration.BannouSerializerConfig);
        var deserialized = JsonSerializer.Deserialize<NumericTypesModel>(json, IServiceConfiguration.BannouSerializerConfig);

        Assert.NotNull(deserialized);
        Assert.Equal(1765031079, deserialized.LongValue);
    }

    [Fact]
    public void NumericString_FailsWithStrictHandling()
    {
        // With NumberHandling.Strict, numbers as strings should fail
        var jsonWithStringNumber = """{"LongValue":"1765031079"}""";

        // This should throw because Strict handling doesn't allow string numbers
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<NumericTypesModel>(jsonWithStringNumber, IServiceConfiguration.BannouSerializerConfig));
    }

    [Fact]
    public void LongValue_DefaultsToZero_WhenMissing()
    {
        var jsonWithoutLongValue = """{"IntValue":42}""";
        var deserialized = JsonSerializer.Deserialize<NumericTypesModel>(jsonWithoutLongValue, IServiceConfiguration.BannouSerializerConfig);

        Assert.NotNull(deserialized);
        Assert.Equal(42, deserialized.IntValue);
        Assert.Equal(0, deserialized.LongValue); // Default when property is missing
    }

    #endregion

    #region Web Default Serializer Comparison

    [Fact]
    public void BannouDefaults_Use_CamelCase_Naming()
    {
        // This demonstrates what web default JSON settings do
        var defaultOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var model = new SessionDataModel
        {
            ExpiresAtUnix = 1733500000,
            SessionId = "test"
        };

        var json = JsonSerializer.Serialize(model, defaultOptions);

        // Web defaults serialize with camelCase
        Assert.Contains("\"expiresAtUnix\":", json);
        Assert.Contains("\"sessionId\":", json);
        Assert.DoesNotContain("\"ExpiresAtUnix\":", json);
        Assert.DoesNotContain("\"SessionId\":", json);
    }

    [Fact]
    public void OurConfig_Uses_PascalCase_Naming()
    {
        var model = new SessionDataModel
        {
            ExpiresAtUnix = 1733500000,
            SessionId = "test"
        };

        var json = JsonSerializer.Serialize(model, IServiceConfiguration.BannouSerializerConfig);

        // Our config serializes with PascalCase
        Assert.Contains("\"ExpiresAtUnix\":", json);
        Assert.Contains("\"SessionId\":", json);
        Assert.DoesNotContain("\"expiresAtUnix\":", json);
        Assert.DoesNotContain("\"sessionId\":", json);
    }

    [Fact]
    public void CrossSerializer_BannouDefaultsWrite_OurConfigRead_Succeeds()
    {
        // With PropertyNameCaseInsensitive = true, data written with web defaults (camelCase)
        // can be read with our config

        var defaultOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var original = new SessionDataModel
        {
            ExpiresAtUnix = 1733503600,
            SessionId = "important-session-id"
        };

        // Simulate data saved with web default settings
        var jsonFromBannouDefaults = JsonSerializer.Serialize(original, defaultOptions);

        // Try to read with our config (what the service does)
        var deserialized = JsonSerializer.Deserialize<SessionDataModel>(jsonFromBannouDefaults, IServiceConfiguration.BannouSerializerConfig);

        Assert.NotNull(deserialized);
        // With case-insensitive matching, camelCase works correctly
        Assert.Equal(1733503600, deserialized.ExpiresAtUnix);
        Assert.Equal("important-session-id", deserialized.SessionId);
    }

    [Fact]
    public void CrossSerializer_OurConfigWrite_OurConfigRead_Succeeds()
    {
        // When both write and read use our config, everything works
        var original = new SessionDataModel
        {
            ExpiresAtUnix = 1733503600,
            SessionId = "important-session-id"
        };

        var json = JsonSerializer.Serialize(original, IServiceConfiguration.BannouSerializerConfig);
        var deserialized = JsonSerializer.Deserialize<SessionDataModel>(json, IServiceConfiguration.BannouSerializerConfig);

        Assert.NotNull(deserialized);
        Assert.Equal(1733503600, deserialized.ExpiresAtUnix);
        Assert.Equal("important-session-id", deserialized.SessionId);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void SessionDataModel_WithMaxLongValue_RoundTrips()
    {
        var original = new SessionDataModel { ExpiresAtUnix = long.MaxValue };
        var json = JsonSerializer.Serialize(original, IServiceConfiguration.BannouSerializerConfig);
        var deserialized = JsonSerializer.Deserialize<SessionDataModel>(json, IServiceConfiguration.BannouSerializerConfig);

        Assert.NotNull(deserialized);
        Assert.Equal(long.MaxValue, deserialized.ExpiresAtUnix);
    }

    [Fact]
    public void SessionDataModel_WithZeroUnixTime_RoundTrips()
    {
        // Zero is a valid Unix timestamp (Jan 1, 1970) - verify it serializes/deserializes
        var original = new SessionDataModel { ExpiresAtUnix = 0 };
        var json = JsonSerializer.Serialize(original, IServiceConfiguration.BannouSerializerConfig);
        var deserialized = JsonSerializer.Deserialize<SessionDataModel>(json, IServiceConfiguration.BannouSerializerConfig);

        Assert.NotNull(deserialized);
        Assert.Equal(0, deserialized.ExpiresAtUnix);
    }

    [Fact]
    public void SessionDataModel_WithNegativeUnixTime_RoundTrips()
    {
        // Negative Unix time (before Jan 1, 1970) should also work
        var original = new SessionDataModel { ExpiresAtUnix = -1000000 };
        var json = JsonSerializer.Serialize(original, IServiceConfiguration.BannouSerializerConfig);
        var deserialized = JsonSerializer.Deserialize<SessionDataModel>(json, IServiceConfiguration.BannouSerializerConfig);

        Assert.NotNull(deserialized);
        Assert.Equal(-1000000, deserialized.ExpiresAtUnix);
    }

    [Fact]
    public void SessionDataModel_WithEmptyRoles_RoundTrips()
    {
        var original = new SessionDataModel { Roles = new List<string>() };
        var json = JsonSerializer.Serialize(original, IServiceConfiguration.BannouSerializerConfig);
        var deserialized = JsonSerializer.Deserialize<SessionDataModel>(json, IServiceConfiguration.BannouSerializerConfig);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Roles);
        Assert.Empty(deserialized.Roles);
    }

    [Fact]
    public void SessionDataModel_WithNullRoles_HandledCorrectly()
    {
        var original = new SessionDataModel { Roles = null! };
        var json = JsonSerializer.Serialize(original, IServiceConfiguration.BannouSerializerConfig);

        // DefaultIgnoreCondition.WhenWritingNull should exclude null Roles
        Assert.DoesNotContain("\"Roles\":null", json);

        var deserialized = JsonSerializer.Deserialize<SessionDataModel>(json, IServiceConfiguration.BannouSerializerConfig);
        Assert.NotNull(deserialized);
        // Roles should be default (new List<string>()) since it wasn't in JSON
        Assert.NotNull(deserialized.Roles);
    }

    #endregion

    #region BannouJson.ApplyBannouSettings Tests

    [Fact]
    public void ApplyBannouSettings_CopiesAllSettings()
    {
        // Arrange
        var target = new JsonSerializerOptions();

        // Act
        BannouJson.ApplyBannouSettings(target);

        // Assert - verify key settings are copied
        Assert.Equal(BannouJson.Options.AllowTrailingCommas, target.AllowTrailingCommas);
        Assert.Equal(BannouJson.Options.DefaultIgnoreCondition, target.DefaultIgnoreCondition);
        Assert.Equal(BannouJson.Options.PropertyNameCaseInsensitive, target.PropertyNameCaseInsensitive);
        Assert.Equal(BannouJson.Options.MaxDepth, target.MaxDepth);
        Assert.Equal(BannouJson.Options.NumberHandling, target.NumberHandling);
        Assert.Equal(BannouJson.Options.WriteIndented, target.WriteIndented);
    }

    [Fact]
    public void ApplyBannouSettings_CopiesConverters()
    {
        // Arrange
        var target = new JsonSerializerOptions();
        var originalConverterCount = BannouJson.Options.Converters.Count;

        // Act
        BannouJson.ApplyBannouSettings(target);

        // Assert - converters should be copied
        Assert.Equal(originalConverterCount, target.Converters.Count);
    }

    [Fact]
    public void ApplyBannouSettings_DoesNotDuplicateConverters()
    {
        // Arrange
        var target = new JsonSerializerOptions();
        BannouJson.ApplyBannouSettings(target);
        var countAfterFirst = target.Converters.Count;

        // Act - apply settings again
        BannouJson.ApplyBannouSettings(target);

        // Assert - should not duplicate converters
        Assert.Equal(countAfterFirst, target.Converters.Count);
    }

    [Fact]
    public void ApplyBannouSettings_ReturnsTargetForChaining()
    {
        // Arrange
        var target = new JsonSerializerOptions();

        // Act
        var result = BannouJson.ApplyBannouSettings(target);

        // Assert - should return same instance for fluent chaining
        Assert.Same(target, result);
    }

    #endregion

    #region AdditionalProperties Serialization Tests (NSwag Generated Models)

    /// <summary>
    /// Model mimicking NSwag-generated models with AdditionalProperties.
    /// This tests the fix for invalid JSON when AdditionalProperties is null.
    ///
    /// CRITICAL BUG FIXED: NSwag generates lazy initialization that created empty {}
    /// in serialized JSON, resulting in invalid JSON like: {"prop":"value",{}}
    /// Fix: Changed getter to return null directly, made property nullable.
    /// </summary>
    private class ModelWithAdditionalProperties
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }

        // Mimics NSwag generated pattern AFTER fix:
        // Property is nullable, getter returns backing field directly (no lazy init)
        private System.Collections.Generic.IDictionary<string, object>? _additionalProperties;

        [System.Text.Json.Serialization.JsonExtensionData]
        public System.Collections.Generic.IDictionary<string, object>? AdditionalProperties
        {
            get => _additionalProperties;
            set { _additionalProperties = value; }
        }
    }

    [Fact]
    public void AdditionalProperties_WhenNull_DoesNotSerializeAsEmptyObject()
    {
        // Arrange - model with null AdditionalProperties
        var model = new ModelWithAdditionalProperties
        {
            Name = "Test",
            Value = 42
        };

        // Act
        var json = JsonSerializer.Serialize(model, IServiceConfiguration.BannouSerializerConfig);

        // Assert - should NOT contain empty {} which would make invalid JSON
        Assert.DoesNotContain("{}", json);
        Assert.DoesNotContain(",}", json); // No trailing comma before }
        Assert.Contains("\"Name\":", json);
        Assert.Contains("\"Value\":", json);
    }

    [Fact]
    public void AdditionalProperties_WhenPopulated_SerializesCorrectly()
    {
        // Arrange
        var model = new ModelWithAdditionalProperties
        {
            Name = "Test",
            Value = 42,
            AdditionalProperties = new Dictionary<string, object>
            {
                { "extra1", "value1" },
                { "extra2", 123 }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(model, IServiceConfiguration.BannouSerializerConfig);

        // Assert - additional properties should be included inline (not nested)
        Assert.Contains("\"extra1\":", json);
        Assert.Contains("\"extra2\":", json);
    }

    [Fact]
    public void AdditionalProperties_WhenEmptyDict_SerializesWithoutInvalidJson()
    {
        // Arrange - explicitly set to empty dictionary (not null)
        var model = new ModelWithAdditionalProperties
        {
            Name = "Test",
            Value = 42,
            AdditionalProperties = new Dictionary<string, object>()
        };

        // Act
        var json = JsonSerializer.Serialize(model, IServiceConfiguration.BannouSerializerConfig);

        // Assert - empty dict with JsonExtensionData should not add anything
        // The JSON should be valid (parseable)
        var parsed = JsonSerializer.Deserialize<ModelWithAdditionalProperties>(json, IServiceConfiguration.BannouSerializerConfig);
        Assert.NotNull(parsed);
        Assert.Equal("Test", parsed.Name);
        Assert.Equal(42, parsed.Value);
    }

    [Fact]
    public void AdditionalProperties_RoundTrip_PreservesData()
    {
        // Arrange
        var original = new ModelWithAdditionalProperties
        {
            Name = "Test",
            Value = 42,
            AdditionalProperties = new Dictionary<string, object>
            {
                { "customField", "customValue" }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(original, IServiceConfiguration.BannouSerializerConfig);
        var deserialized = JsonSerializer.Deserialize<ModelWithAdditionalProperties>(json, IServiceConfiguration.BannouSerializerConfig);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Value, deserialized.Value);
        Assert.NotNull(deserialized.AdditionalProperties);
        Assert.True(deserialized.AdditionalProperties.ContainsKey("customField"));
    }

    #endregion
}
