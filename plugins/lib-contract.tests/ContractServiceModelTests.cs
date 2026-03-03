using BeyondImmersion.BannouService.Contract;
using System.Text.Json;
using Xunit;

namespace BeyondImmersion.BannouService.Contract.Tests;

/// <summary>
/// Unit tests for ContractServiceModels internal types.
/// Tests ClauseDefinition JSON parsing methods.
/// </summary>
public class ContractServiceModelTests
{
    #region ClauseDefinition.GetProperty Tests

    [Fact]
    public void ClauseDefinition_GetProperty_StringValue_ReturnsString()
    {
        // Arrange
        var json = """{"id": "clause1", "type": "fee", "assetType": "currency"}""";
        var element = JsonDocument.Parse(json).RootElement;
        var clause = new ClauseDefinition("clause1", "fee", element);

        // Act
        var result = clause.GetProperty("assetType");

        // Assert
        Assert.Equal("currency", result);
    }

    [Fact]
    public void ClauseDefinition_GetProperty_NumericValue_ReturnsRawText()
    {
        // Arrange
        var json = """{"id": "clause1", "type": "fee", "amount": 42.5}""";
        var element = JsonDocument.Parse(json).RootElement;
        var clause = new ClauseDefinition("clause1", "fee", element);

        // Act
        var result = clause.GetProperty("amount");

        // Assert
        Assert.Equal("42.5", result);
    }

    [Fact]
    public void ClauseDefinition_GetProperty_MissingProperty_ReturnsNull()
    {
        // Arrange
        var json = """{"id": "clause1", "type": "fee"}""";
        var element = JsonDocument.Parse(json).RootElement;
        var clause = new ClauseDefinition("clause1", "fee", element);

        // Act
        var result = clause.GetProperty("nonExistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ClauseDefinition_GetProperty_BooleanValue_ReturnsNull()
    {
        // Arrange: boolean is neither string nor number, so GetProperty returns null
        var json = """{"id": "clause1", "type": "fee", "required": true}""";
        var element = JsonDocument.Parse(json).RootElement;
        var clause = new ClauseDefinition("clause1", "fee", element);

        // Act
        var result = clause.GetProperty("required");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ClauseDefinition_GetProperty_NullValue_ReturnsNull()
    {
        // Arrange
        var json = """{"id": "clause1", "type": "fee", "description": null}""";
        var element = JsonDocument.Parse(json).RootElement;
        var clause = new ClauseDefinition("clause1", "fee", element);

        // Act
        var result = clause.GetProperty("description");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ClauseDefinition_GetProperty_IntegerValue_ReturnsRawText()
    {
        // Arrange
        var json = """{"id": "clause1", "type": "fee", "count": 10}""";
        var element = JsonDocument.Parse(json).RootElement;
        var clause = new ClauseDefinition("clause1", "fee", element);

        // Act
        var result = clause.GetProperty("count");

        // Assert
        Assert.Equal("10", result);
    }

    #endregion

    #region ClauseDefinition.GetArray Tests

    [Fact]
    public void ClauseDefinition_GetArray_WithArrayProperty_ReturnsElements()
    {
        // Arrange
        var json = """{"id": "clause1", "type": "distribution", "items": [{"code": "gold"}, {"code": "silver"}]}""";
        var element = JsonDocument.Parse(json).RootElement;
        var clause = new ClauseDefinition("clause1", "distribution", element);

        // Act
        var result = clause.GetArray("items");

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("gold", result[0].GetProperty("code").GetString());
        Assert.Equal("silver", result[1].GetProperty("code").GetString());
    }

    [Fact]
    public void ClauseDefinition_GetArray_EmptyArray_ReturnsEmptyList()
    {
        // Arrange
        var json = """{"id": "clause1", "type": "distribution", "items": []}""";
        var element = JsonDocument.Parse(json).RootElement;
        var clause = new ClauseDefinition("clause1", "distribution", element);

        // Act
        var result = clause.GetArray("items");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ClauseDefinition_GetArray_MissingProperty_ReturnsEmptyList()
    {
        // Arrange
        var json = """{"id": "clause1", "type": "distribution"}""";
        var element = JsonDocument.Parse(json).RootElement;
        var clause = new ClauseDefinition("clause1", "distribution", element);

        // Act
        var result = clause.GetArray("items");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ClauseDefinition_GetArray_NonArrayProperty_ReturnsEmptyList()
    {
        // Arrange: "items" is a string, not an array
        var json = """{"id": "clause1", "type": "distribution", "items": "not_an_array"}""";
        var element = JsonDocument.Parse(json).RootElement;
        var clause = new ClauseDefinition("clause1", "distribution", element);

        // Act
        var result = clause.GetArray("items");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ClauseDefinition_GetArray_PrimitiveArrayValues_ReturnsElements()
    {
        // Arrange
        var json = """{"id": "clause1", "type": "distribution", "tags": ["a", "b", "c"]}""";
        var element = JsonDocument.Parse(json).RootElement;
        var clause = new ClauseDefinition("clause1", "distribution", element);

        // Act
        var result = clause.GetArray("tags");

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("a", result[0].GetString());
        Assert.Equal("b", result[1].GetString());
        Assert.Equal("c", result[2].GetString());
    }

    #endregion

    #region ClauseDefinition Properties

    [Fact]
    public void ClauseDefinition_Constructor_SetsIdAndType()
    {
        // Arrange
        var json = """{"id": "clause1", "type": "fee"}""";
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var clause = new ClauseDefinition("clause1", "fee", element);

        // Assert
        Assert.Equal("clause1", clause.Id);
        Assert.Equal("fee", clause.Type);
    }

    #endregion
}
