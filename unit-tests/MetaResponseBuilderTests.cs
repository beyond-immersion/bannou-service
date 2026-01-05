using BeyondImmersion.BannouService.Meta;
using System.Text.Json;
using Xunit;

namespace BeyondImmersion.BannouService.UnitTests;

/// <summary>
/// Unit tests for MetaResponseBuilder functionality.
/// Tests the meta endpoint response generation using pre-embedded JSON strings.
/// </summary>
public class MetaResponseBuilderTests
{
    private const string TestInfoJson = """
        {
            "summary": "Get an account by ID",
            "description": "Retrieves account details for the specified account ID.",
            "tags": ["account"],
            "deprecated": false,
            "operationId": "GetAccount"
        }
        """;

    private const string TestRequestSchemaJson = """
        {
            "$schema": "http://json-schema.org/draft-07/schema#",
            "type": "object",
            "required": ["id"],
            "properties": {
                "id": {
                    "type": "string",
                    "format": "uuid"
                }
            }
        }
        """;

    private const string TestResponseSchemaJson = """
        {
            "$schema": "http://json-schema.org/draft-07/schema#",
            "type": "object",
            "required": ["id", "email"],
            "properties": {
                "id": {
                    "type": "string",
                    "format": "uuid"
                },
                "email": {
                    "type": "string",
                    "format": "email"
                },
                "displayName": {
                    "type": "string"
                }
            }
        }
        """;

    [Fact]
    public void BuildInfoResponse_ShouldCreateValidMetaResponse()
    {
        // Arrange
        var serviceName = "Accounts";
        var method = "POST";
        var path = "/account/get";

        // Act
        var response = MetaResponseBuilder.BuildInfoResponse(serviceName, method, path, TestInfoJson);

        // Assert
        Assert.Equal("endpoint-info", response.MetaType);
        Assert.Equal($"{method}:{path}", response.EndpointKey);
        Assert.Equal(serviceName, response.ServiceName);
        Assert.Equal(method, response.Method);
        Assert.Equal(path, response.Path);
        Assert.NotEqual(default, response.GeneratedAt);
        Assert.False(string.IsNullOrEmpty(response.SchemaVersion));

        // Verify Data contains expected info
        Assert.Equal(JsonValueKind.Object, response.Data.ValueKind);
        Assert.True(response.Data.TryGetProperty("summary", out var summary));
        Assert.Equal("Get an account by ID", summary.GetString());
    }

    [Fact]
    public void BuildSchemaResponse_RequestSchema_ShouldCreateValidMetaResponse()
    {
        // Arrange
        var serviceName = "Accounts";
        var method = "POST";
        var path = "/account/get";
        var metaType = "request-schema";

        // Act
        var response = MetaResponseBuilder.BuildSchemaResponse(serviceName, method, path, metaType, TestRequestSchemaJson);

        // Assert
        Assert.Equal("request-schema", response.MetaType);
        Assert.Equal($"{method}:{path}", response.EndpointKey);

        // Verify Data contains JSON Schema
        Assert.Equal(JsonValueKind.Object, response.Data.ValueKind);
        Assert.True(response.Data.TryGetProperty("$schema", out var schema));
        Assert.Equal("http://json-schema.org/draft-07/schema#", schema.GetString());
        Assert.True(response.Data.TryGetProperty("required", out var required));
        Assert.Equal(JsonValueKind.Array, required.ValueKind);
    }

    [Fact]
    public void BuildSchemaResponse_ResponseSchema_ShouldCreateValidMetaResponse()
    {
        // Arrange
        var serviceName = "Accounts";
        var method = "POST";
        var path = "/account/get";
        var metaType = "response-schema";

        // Act
        var response = MetaResponseBuilder.BuildSchemaResponse(serviceName, method, path, metaType, TestResponseSchemaJson);

        // Assert
        Assert.Equal("response-schema", response.MetaType);

        // Verify response schema has more properties than request
        Assert.True(response.Data.TryGetProperty("properties", out var properties));
        Assert.True(properties.TryGetProperty("email", out _));
        Assert.True(properties.TryGetProperty("displayName", out _));
    }

    [Fact]
    public void BuildFullSchemaResponse_ShouldCombineAllSchemas()
    {
        // Arrange
        var serviceName = "Accounts";
        var method = "POST";
        var path = "/account/get";

        // Act
        var response = MetaResponseBuilder.BuildFullSchemaResponse(
            serviceName, method, path,
            TestInfoJson, TestRequestSchemaJson, TestResponseSchemaJson);

        // Assert
        Assert.Equal("full-schema", response.MetaType);
        Assert.Equal($"{method}:{path}", response.EndpointKey);

        // Verify Data contains all three sections
        Assert.Equal(JsonValueKind.Object, response.Data.ValueKind);
        Assert.True(response.Data.TryGetProperty("info", out var info));
        Assert.True(response.Data.TryGetProperty("request", out var request));
        Assert.True(response.Data.TryGetProperty("response", out var responseSchema));

        // Verify info section
        Assert.True(info.TryGetProperty("summary", out _));

        // Verify request section has $schema
        Assert.True(request.TryGetProperty("$schema", out _));

        // Verify response section has properties
        Assert.True(responseSchema.TryGetProperty("properties", out _));
    }

    [Fact]
    public void BuildInfoResponse_WithEmptyJson_ShouldReturnEmptyObject()
    {
        // Arrange
        var emptyJson = "{}";

        // Act
        var response = MetaResponseBuilder.BuildInfoResponse("Test", "GET", "/test", emptyJson);

        // Assert
        Assert.Equal(JsonValueKind.Object, response.Data.ValueKind);
    }

    [Fact]
    public void BuildInfoResponse_WithInvalidJson_ShouldReturnEmptyObject()
    {
        // Arrange
        var invalidJson = "not valid json";

        // Act
        var response = MetaResponseBuilder.BuildInfoResponse("Test", "GET", "/test", invalidJson);

        // Assert
        // Should not throw, returns empty object
        Assert.Equal(JsonValueKind.Object, response.Data.ValueKind);
    }

    [Fact]
    public void BuildSchemaResponse_WithWhitespaceOnly_ShouldReturnEmptyObject()
    {
        // Arrange
        var whitespaceJson = "   ";

        // Act
        var response = MetaResponseBuilder.BuildSchemaResponse("Test", "GET", "/test", "request-schema", whitespaceJson);

        // Assert
        Assert.Equal(JsonValueKind.Object, response.Data.ValueKind);
    }

    [Fact]
    public void MetaResponse_SchemaVersion_ShouldNotBeEmpty()
    {
        // Act
        var response = MetaResponseBuilder.BuildInfoResponse("Test", "GET", "/test", TestInfoJson);

        // Assert
        Assert.False(string.IsNullOrEmpty(response.SchemaVersion));
        // SchemaVersion should be a valid version format (like "1.0.0" or "1.0.0.0")
        Assert.Matches(@"^\d+\.\d+(\.\d+)?(\.\d+)?$", response.SchemaVersion);
    }

    [Fact]
    public void MetaResponse_GeneratedAt_ShouldBeRecentUtc()
    {
        // Arrange
        var beforeCall = DateTimeOffset.UtcNow.AddSeconds(-1);

        // Act
        var response = MetaResponseBuilder.BuildInfoResponse("Test", "GET", "/test", TestInfoJson);

        // Assert
        var afterCall = DateTimeOffset.UtcNow.AddSeconds(1);
        Assert.True(response.GeneratedAt >= beforeCall);
        Assert.True(response.GeneratedAt <= afterCall);
        Assert.Equal(TimeSpan.Zero, response.GeneratedAt.Offset); // Should be UTC
    }
}
