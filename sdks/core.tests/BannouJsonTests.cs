using System.Text.Json;
using Xunit;

namespace BeyondImmersion.Bannou.Core.Tests;

/// <summary>
/// Unit tests for BannouJson serialization options.
/// Verifies that shared serialization settings work correctly across all Bannou services.
/// </summary>
public class BannouJsonTests
{
    #region Test Models

    /// <summary>
    /// Test enum for verifying string serialization.
    /// </summary>
    private enum TestStatus
    {
        Active,
        Inactive,
        PendingApproval
    }

    /// <summary>
    /// Test model for verifying property handling.
    /// </summary>
    private class TestModel
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public int AccountId { get; set; }
        public TestStatus Status { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    /// <summary>
    /// Test model with nullable properties.
    /// </summary>
    private class TestModelWithNullables
    {
        public string? OptionalField { get; set; }
        public int? OptionalNumber { get; set; }
        public TestStatus? OptionalStatus { get; set; }
    }

    #endregion

    #region Property Case Insensitive Tests

    /// <summary>
    /// Verifies that properties can be deserialized case-insensitively.
    /// </summary>
    [Fact]
    public void Deserialize_HandlesCaseInsensitiveProperties()
    {
        // Arrange - lowercase properties
        var json = """{"firstname":"Jane","lastname":"Smith","accountid":456}""";

        // Act
        var model = BannouJson.Deserialize<TestModel>(json);

        // Assert
        Assert.NotNull(model);
        Assert.Equal("Jane", model.FirstName);
        Assert.Equal("Smith", model.LastName);
        Assert.Equal(456, model.AccountId);
    }

    /// <summary>
    /// Verifies that camelCase properties are deserialized correctly.
    /// </summary>
    [Fact]
    public void Deserialize_HandlesCamelCaseProperties()
    {
        // Arrange
        var json = """{"firstName":"John","lastName":"Doe","accountId":123}""";

        // Act
        var model = BannouJson.Deserialize<TestModel>(json);

        // Assert
        Assert.NotNull(model);
        Assert.Equal("John", model.FirstName);
        Assert.Equal("Doe", model.LastName);
        Assert.Equal(123, model.AccountId);
    }

    /// <summary>
    /// Verifies that PascalCase properties are deserialized correctly.
    /// </summary>
    [Fact]
    public void Deserialize_HandlesPascalCaseProperties()
    {
        // Arrange
        var json = """{"FirstName":"Alice","LastName":"Wonder","AccountId":789}""";

        // Act
        var model = BannouJson.Deserialize<TestModel>(json);

        // Assert
        Assert.NotNull(model);
        Assert.Equal("Alice", model.FirstName);
        Assert.Equal("Wonder", model.LastName);
        Assert.Equal(789, model.AccountId);
    }

    #endregion

    #region Enum Serialization Tests

    /// <summary>
    /// Verifies that enums are serialized as strings, not integers.
    /// This is critical for client event handling where event_name is matched by string value.
    /// </summary>
    [Fact]
    public void Serialize_EnumsAsStrings()
    {
        // Arrange
        var model = new TestModel
        {
            Status = TestStatus.PendingApproval
        };

        // Act
        var json = BannouJson.Serialize(model);

        // Assert
        Assert.Contains("\"PendingApproval\"", json);
        Assert.DoesNotContain(":2", json); // Should not be serialized as integer
    }

    /// <summary>
    /// Verifies that string enum values are deserialized correctly.
    /// </summary>
    [Fact]
    public void Deserialize_EnumsFromStrings()
    {
        // Arrange
        var json = """{"Status":"Active","FirstName":"","LastName":"","AccountId":0}""";

        // Act
        var model = BannouJson.Deserialize<TestModel>(json);

        // Assert
        Assert.NotNull(model);
        Assert.Equal(TestStatus.Active, model.Status);
    }

    /// <summary>
    /// Verifies that nullable enum values are serialized when present.
    /// </summary>
    [Fact]
    public void Serialize_NullableEnum_WhenPresent()
    {
        // Arrange
        var model = new TestModelWithNullables
        {
            OptionalStatus = TestStatus.Inactive
        };

        // Act
        var json = BannouJson.Serialize(model);

        // Assert
        Assert.Contains("\"Inactive\"", json);
    }

    #endregion

    #region Null Handling Tests

    /// <summary>
    /// Verifies that null values are omitted from output.
    /// </summary>
    [Fact]
    public void Serialize_OmitsNullValues()
    {
        // Arrange
        var model = new TestModelWithNullables
        {
            OptionalField = null,
            OptionalNumber = null
        };

        // Act
        var json = BannouJson.Serialize(model);

        // Assert - WhenWritingNull should omit null values
        Assert.DoesNotContain("null", json.ToLower());
    }

    /// <summary>
    /// Verifies that present values are included.
    /// </summary>
    [Fact]
    public void Serialize_IncludesNonNullValues()
    {
        // Arrange
        var model = new TestModelWithNullables
        {
            OptionalField = "present",
            OptionalNumber = 42
        };

        // Act
        var json = BannouJson.Serialize(model);

        // Assert
        Assert.Contains("\"present\"", json);
        Assert.Contains("42", json);
    }

    #endregion

    #region Round-Trip Tests

    /// <summary>
    /// Verifies that a model can be serialized and deserialized without data loss.
    /// </summary>
    [Fact]
    public void RoundTrip_PreservesAllData()
    {
        // Arrange
        var original = new TestModel
        {
            FirstName = "Test",
            LastName = "User",
            AccountId = 999,
            Status = TestStatus.Active,
            CreatedAt = new DateTimeOffset(2025, 1, 15, 12, 0, 0, TimeSpan.Zero)
        };

        // Act
        var json = BannouJson.Serialize(original);
        var deserialized = BannouJson.Deserialize<TestModel>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.FirstName, deserialized.FirstName);
        Assert.Equal(original.LastName, deserialized.LastName);
        Assert.Equal(original.AccountId, deserialized.AccountId);
        Assert.Equal(original.Status, deserialized.Status);
        Assert.Equal(original.CreatedAt, deserialized.CreatedAt);
    }

    #endregion

    #region DeserializeRequired Tests

    /// <summary>
    /// Verifies that DeserializeRequired returns valid object.
    /// </summary>
    [Fact]
    public void DeserializeRequired_ReturnsValidObject()
    {
        // Arrange
        var json = """{"FirstName":"Test","LastName":"User","AccountId":1}""";

        // Act
        var result = BannouJson.DeserializeRequired<TestModel>(json);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test", result.FirstName);
    }

    #endregion

    #region Bytes Serialization Tests

    /// <summary>
    /// Verifies that SerializeToUtf8Bytes produces valid output.
    /// </summary>
    [Fact]
    public void SerializeToUtf8Bytes_ProducesValidOutput()
    {
        // Arrange
        var model = new TestModel
        {
            FirstName = "Test",
            AccountId = 42
        };

        // Act
        var bytes = BannouJson.SerializeToUtf8Bytes(model);

        // Assert
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);

        // Verify can round-trip
        var deserialized = BannouJson.Deserialize<TestModel>(bytes);
        Assert.NotNull(deserialized);
        Assert.Equal("Test", deserialized.FirstName);
    }

    #endregion

    #region Options Consistency Tests

    /// <summary>
    /// Verifies that Options is not null and configured.
    /// </summary>
    [Fact]
    public void Options_IsConfigured()
    {
        // Assert
        Assert.NotNull(BannouJson.Options);
        Assert.True(BannouJson.Options.PropertyNameCaseInsensitive);
    }

    /// <summary>
    /// Verifies that Options has enum converter.
    /// </summary>
    [Fact]
    public void Options_HasEnumConverter()
    {
        // Assert
        Assert.Contains(BannouJson.Options.Converters, c => c.GetType().Name.Contains("Enum"));
    }

    /// <summary>
    /// Verifies that Options ignores null values when writing.
    /// </summary>
    [Fact]
    public void Options_IgnoresNullWhenWriting()
    {
        // Assert
        Assert.Equal(
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            BannouJson.Options.DefaultIgnoreCondition);
    }

    #endregion

    #region Extension Methods Tests

    /// <summary>
    /// Verifies that ToJson extension method works.
    /// </summary>
    [Fact]
    public void ToJson_ExtensionMethod_Works()
    {
        // Arrange
        var model = new TestModel { FirstName = "Test" };

        // Act
        var json = model.ToJson();

        // Assert
        Assert.Contains("Test", json);
    }

    /// <summary>
    /// Verifies that FromJson extension method works.
    /// </summary>
    [Fact]
    public void FromJson_ExtensionMethod_Works()
    {
        // Arrange
        var json = """{"FirstName":"Extension"}""";

        // Act
        var model = json.FromJson<TestModel>();

        // Assert
        Assert.NotNull(model);
        Assert.Equal("Extension", model.FirstName);
    }

    #endregion

    #region ApplyBannouSettings Tests

    /// <summary>
    /// Verifies that ApplyBannouSettings configures target options correctly.
    /// </summary>
    [Fact]
    public void ApplyBannouSettings_ConfiguresOptions()
    {
        // Arrange
        var target = new JsonSerializerOptions();

        // Act
        BannouJson.ApplyBannouSettings(target);

        // Assert
        Assert.True(target.PropertyNameCaseInsensitive);
        Assert.Contains(target.Converters, c => c.GetType().Name.Contains("Enum"));
    }

    #endregion
}
