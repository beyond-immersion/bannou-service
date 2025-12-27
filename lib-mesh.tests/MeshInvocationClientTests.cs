using BeyondImmersion.BannouService.Mesh;
using BeyondImmersion.BannouService.Mesh.Services;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Mesh.Tests;

/// <summary>
/// Unit tests for MeshInvocationClient.
/// Tests service-to-service invocation functionality.
/// </summary>
public class MeshInvocationClientTests
{
    private readonly Mock<IMeshRedisManager> _mockRedisManager;
    private readonly Mock<ILogger<MeshInvocationClient>> _mockLogger;

    public MeshInvocationClientTests()
    {
        _mockRedisManager = new Mock<IMeshRedisManager>();
        _mockLogger = new Mock<ILogger<MeshInvocationClient>>();
    }

    private MeshInvocationClient CreateClient()
    {
        return new MeshInvocationClient(
            _mockRedisManager.Object,
            _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        using var client = CreateClient();

        // Assert
        Assert.NotNull(client);
    }

    [Fact]
    public void Constructor_WithNullRedisManager_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new MeshInvocationClient(
            null!,
            _mockLogger.Object));
        Assert.Equal("redisManager", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new MeshInvocationClient(
            _mockRedisManager.Object,
            null!));
        Assert.Equal("logger", ex.ParamName);
    }

    #endregion

    #region CreateInvokeMethodRequest Tests

    [Fact]
    public void CreateInvokeMethodRequest_WithValidParameters_ShouldReturnConfiguredRequest()
    {
        // Arrange
        using var client = CreateClient();
        var httpMethod = HttpMethod.Get;
        var appId = "test-app";
        var methodName = "test/method";

        // Act
        var request = client.CreateInvokeMethodRequest(httpMethod, appId, methodName);

        // Assert
        Assert.NotNull(request);
        Assert.Equal(httpMethod, request.Method);
        Assert.Contains("application/json", request.Headers.Accept.ToString());

        // Verify options are set
        Assert.True(request.Options.TryGetValue(new HttpRequestOptionsKey<string>("mesh-app-id"), out var storedAppId));
        Assert.Equal(appId, storedAppId);
        Assert.True(request.Options.TryGetValue(new HttpRequestOptionsKey<string>("mesh-method"), out var storedMethod));
        Assert.Equal(methodName, storedMethod);
    }

    [Fact]
    public void CreateInvokeMethodRequest_WithNullHttpMethod_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var client = CreateClient();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => client.CreateInvokeMethodRequest(
            null!,
            "test-app",
            "test/method"));
    }

    [Fact]
    public void CreateInvokeMethodRequest_WithNullAppId_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var client = CreateClient();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => client.CreateInvokeMethodRequest(
            HttpMethod.Get,
            null!,
            "test/method"));
    }

    [Fact]
    public void CreateInvokeMethodRequest_WithNullMethodName_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var client = CreateClient();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => client.CreateInvokeMethodRequest(
            HttpMethod.Get,
            "test-app",
            null!));
    }

    [Fact]
    public void CreateInvokeMethodRequest_WithBody_ShouldSerializeRequestBody()
    {
        // Arrange
        using var client = CreateClient();
        var requestBody = new TestRequest { Name = "test", Value = 42 };

        // Act
        var request = client.CreateInvokeMethodRequest(HttpMethod.Post, "test-app", "test/method", requestBody);

        // Assert
        Assert.NotNull(request);
        Assert.NotNull(request.Content);
        Assert.Equal("application/json", request.Content.Headers.ContentType?.MediaType);
    }

    #endregion

    #region InvokeMethodWithResponseAsync Tests

    [Fact]
    public async Task InvokeMethodWithResponseAsync_WithNullRequest_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var client = CreateClient();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.InvokeMethodWithResponseAsync(null!));
    }

    [Fact]
    public async Task InvokeMethodWithResponseAsync_WithoutMeshAppIdOption_ShouldThrowArgumentException()
    {
        // Arrange
        using var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.InvokeMethodWithResponseAsync(request));
    }

    [Fact]
    public async Task InvokeMethodWithResponseAsync_WhenNoEndpointsAvailable_ShouldThrowMeshInvocationException()
    {
        // Arrange
        using var client = CreateClient();
        var appId = "test-app";
        var methodName = "test/method";

        _mockRedisManager.Setup(x => x.GetEndpointsForAppIdAsync(appId, false))
            .ReturnsAsync(new List<MeshEndpoint>());

        var request = client.CreateInvokeMethodRequest(HttpMethod.Get, appId, methodName);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<MeshInvocationException>(() =>
            client.InvokeMethodWithResponseAsync(request));
        Assert.Equal(appId, ex.AppId);
        Assert.Equal(503, ex.StatusCode);
    }

    #endregion

    #region InvokeMethodAsync Tests (POST with request, no response)

    [Fact]
    public async Task InvokeMethodAsync_WithRequest_NullAppId_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var client = CreateClient();
        var request = new TestRequest { Name = "test" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.InvokeMethodAsync(null!, "method", request));
    }

    [Fact]
    public async Task InvokeMethodAsync_WithRequest_NullMethodName_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var client = CreateClient();
        var request = new TestRequest { Name = "test" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.InvokeMethodAsync("app", null!, request));
    }

    [Fact]
    public async Task InvokeMethodAsync_WithRequest_NullRequest_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var client = CreateClient();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.InvokeMethodAsync<TestRequest>("app", "method", null!));
    }

    #endregion

    #region InvokeMethodAsync Tests (POST with request and response)

    [Fact]
    public async Task InvokeMethodAsync_WithRequestAndResponse_NullAppId_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var client = CreateClient();
        var request = new TestRequest { Name = "test" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.InvokeMethodAsync<TestRequest, TestResponse>(null!, "method", request));
    }

    [Fact]
    public async Task InvokeMethodAsync_WithRequestAndResponse_NullMethodName_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var client = CreateClient();
        var request = new TestRequest { Name = "test" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.InvokeMethodAsync<TestRequest, TestResponse>("app", null!, request));
    }

    [Fact]
    public async Task InvokeMethodAsync_WithRequestAndResponse_NullRequest_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var client = CreateClient();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.InvokeMethodAsync<TestRequest, TestResponse>("app", "method", null!));
    }

    #endregion

    #region InvokeMethodAsync Tests (GET with response only)

    [Fact]
    public async Task InvokeMethodAsync_Get_NullAppId_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var client = CreateClient();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.InvokeMethodAsync<TestResponse>(null!, "method"));
    }

    [Fact]
    public async Task InvokeMethodAsync_Get_NullMethodName_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var client = CreateClient();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.InvokeMethodAsync<TestResponse>("app", null!));
    }

    #endregion

    #region IsServiceAvailableAsync Tests

    [Fact]
    public async Task IsServiceAvailableAsync_WithNullAppId_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var client = CreateClient();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.IsServiceAvailableAsync(null!));
    }

    [Fact]
    public async Task IsServiceAvailableAsync_WhenEndpointsExist_ShouldReturnTrue()
    {
        // Arrange
        using var client = CreateClient();
        var appId = "test-app";

        _mockRedisManager.Setup(x => x.GetEndpointsForAppIdAsync(appId, false))
            .ReturnsAsync(new List<MeshEndpoint>
            {
                new MeshEndpoint
                {
                    InstanceId = Guid.NewGuid(),
                    AppId = appId,
                    Host = "localhost",
                    Port = 5012,
                    Status = EndpointStatus.Healthy
                }
            });

        // Act
        var result = await client.IsServiceAvailableAsync(appId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task IsServiceAvailableAsync_WhenNoEndpoints_ShouldReturnFalse()
    {
        // Arrange
        using var client = CreateClient();
        var appId = "test-app";

        _mockRedisManager.Setup(x => x.GetEndpointsForAppIdAsync(appId, false))
            .ReturnsAsync(new List<MeshEndpoint>());

        // Act
        var result = await client.IsServiceAvailableAsync(appId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsServiceAvailableAsync_WhenRedisThrows_ShouldReturnFalse()
    {
        // Arrange
        using var client = CreateClient();
        var appId = "test-app";

        _mockRedisManager.Setup(x => x.GetEndpointsForAppIdAsync(appId, false))
            .ThrowsAsync(new Exception("Redis connection failed"));

        // Act
        var result = await client.IsServiceAvailableAsync(appId);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var client = CreateClient();

        // Act & Assert - Should not throw
        var exception = Record.Exception(() => client.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var client = CreateClient();

        // Act & Assert - Multiple dispose calls should be safe
        var exception = Record.Exception(() =>
        {
            client.Dispose();
            client.Dispose();
            client.Dispose();
        });
        Assert.Null(exception);
    }

    #endregion

    #region Helper Classes

    private class TestRequest
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    private class TestResponse
    {
        public string Result { get; set; } = string.Empty;
    }

    #endregion
}
