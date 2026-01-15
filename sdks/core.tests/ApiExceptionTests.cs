using Xunit;

namespace BeyondImmersion.Bannou.Core.Tests;

/// <summary>
/// Unit tests for ApiException classes.
/// Verifies exception structure, status codes, and result handling.
/// </summary>
public class ApiExceptionTests
{
    private static readonly IReadOnlyDictionary<string, IEnumerable<string>> EmptyHeaders =
        new Dictionary<string, IEnumerable<string>>();

    #region Basic ApiException Tests

    /// <summary>
    /// Verifies that ApiException stores message correctly.
    /// </summary>
    [Fact]
    public void ApiException_StoresMessage()
    {
        // Arrange & Act
        var exception = new ApiException("Test error message", 400, null, EmptyHeaders, null);

        // Assert
        Assert.Contains("Test error message", exception.Message);
    }

    /// <summary>
    /// Verifies that ApiException stores status code correctly.
    /// </summary>
    [Fact]
    public void ApiException_StoresStatusCode()
    {
        // Arrange & Act
        var exception = new ApiException("Error", 404, null, EmptyHeaders, null);

        // Assert
        Assert.Equal(404, exception.StatusCode);
    }

    /// <summary>
    /// Verifies that ApiException stores response correctly.
    /// </summary>
    [Fact]
    public void ApiException_StoresResponse()
    {
        // Arrange
        var responseBody = """{"error":"Not found"}""";

        // Act
        var exception = new ApiException("Error", 404, responseBody, EmptyHeaders, null);

        // Assert
        Assert.Equal(responseBody, exception.Response);
    }

    /// <summary>
    /// Verifies that ApiException stores headers correctly.
    /// </summary>
    [Fact]
    public void ApiException_StoresHeaders()
    {
        // Arrange
        var headers = new Dictionary<string, IEnumerable<string>>
        {
            { "Content-Type", new[] { "application/json" } },
            { "X-Request-Id", new[] { "abc123" } }
        };

        // Act
        var exception = new ApiException("Error", 500, null, headers, null);

        // Assert
        Assert.NotNull(exception.Headers);
        Assert.Equal(2, exception.Headers.Count);
        Assert.Contains("Content-Type", exception.Headers.Keys);
    }

    /// <summary>
    /// Verifies that ApiException stores inner exception correctly.
    /// </summary>
    [Fact]
    public void ApiException_StoresInnerException()
    {
        // Arrange
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new ApiException("Outer error", 500, null, EmptyHeaders, innerException);

        // Assert
        Assert.NotNull(exception.InnerException);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal("Inner error", exception.InnerException.Message);
    }

    #endregion

    #region Typed ApiException Tests

    /// <summary>
    /// Verifies that typed ApiException stores result correctly.
    /// </summary>
    [Fact]
    public void TypedApiException_StoresResult()
    {
        // Arrange
        var result = new TestErrorResult { Code = "ERR001", Message = "Test error" };

        // Act
        var exception = new ApiException<TestErrorResult>("Error", 400, null, EmptyHeaders, result, null);

        // Assert
        Assert.NotNull(exception.Result);
        Assert.Equal("ERR001", exception.Result.Code);
        Assert.Equal("Test error", exception.Result.Message);
    }

    /// <summary>
    /// Verifies that typed ApiException inherits from base ApiException.
    /// </summary>
    [Fact]
    public void TypedApiException_InheritsFromApiException()
    {
        // Arrange & Act
        var result = new TestErrorResult { Code = "ERR", Message = "Error" };
        var exception = new ApiException<TestErrorResult>("Error", 400, null, EmptyHeaders, result, null);

        // Assert
        Assert.IsAssignableFrom<ApiException>(exception);
    }

    /// <summary>
    /// Verifies that typed ApiException can be caught as base ApiException.
    /// </summary>
    [Fact]
    public void TypedApiException_CanBeCaughtAsBaseType()
    {
        // Arrange
        var result = new TestErrorResult { Code = "ERR", Message = "Error" };
        var typedException = new ApiException<TestErrorResult>("Error", 400, null, EmptyHeaders, result, null);

        // Act & Assert
        ApiException? caughtException = null;
        try
        {
            throw typedException;
        }
        catch (ApiException ex)
        {
            caughtException = ex;
        }

        Assert.NotNull(caughtException);
        Assert.Equal(400, caughtException.StatusCode);
    }

    #endregion

    #region Status Code Tests

    /// <summary>
    /// Verifies common HTTP status codes are stored correctly.
    /// </summary>
    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(503)]
    public void ApiException_HandlesCommonStatusCodes(int statusCode)
    {
        // Act
        var exception = new ApiException($"Status {statusCode}", statusCode, null, EmptyHeaders, null);

        // Assert
        Assert.Equal(statusCode, exception.StatusCode);
    }

    #endregion

    #region ToString Tests

    /// <summary>
    /// Verifies that ToString includes response content.
    /// </summary>
    [Fact]
    public void ApiException_ToStringIncludesResponse()
    {
        // Arrange
        var response = """{"error":"test"}""";
        var exception = new ApiException("Error", 500, response, EmptyHeaders, null);

        // Act
        var result = exception.ToString();

        // Assert
        Assert.Contains(response, result);
    }

    #endregion

    #region Test Models

    /// <summary>
    /// Test error result model.
    /// </summary>
    private class TestErrorResult
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    #endregion
}
