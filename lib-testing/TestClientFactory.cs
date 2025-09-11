namespace BeyondImmersion.BannouService.Testing;

/// <summary>
/// Factory for creating test clients that support both HTTP and WebSocket transports
/// </summary>
public static class TestClientFactory
{
    /// <summary>
    /// Create an HTTP test client for direct service communication
    /// </summary>
    public static ITestClient CreateHttpClient(TestConfiguration configuration)
    {
        if (!configuration.HasHttpRequired())
            throw new ArgumentException("HTTP test configuration is incomplete");

        // Note: HttpTestClient implementation would need to be moved to lib-testing-core
        // For now, return a placeholder
        throw new NotImplementedException("HttpTestClient needs to be moved from http-tester project");
    }

    /// <summary>
    /// Create a WebSocket test client for Connect service communication
    /// </summary>
    public static ITestClient CreateWebSocketClient(TestConfiguration configuration)
    {
        if (!configuration.HasWebSocketRequired())
            throw new ArgumentException("WebSocket test configuration is incomplete");

        return new WebSocketTestClient(configuration);
    }

    /// <summary>
    /// Create both HTTP and WebSocket clients for dual-transport testing
    /// </summary>
    public static (ITestClient Http, ITestClient WebSocket) CreateBothClients(TestConfiguration configuration)
    {
        return (CreateHttpClient(configuration), CreateWebSocketClient(configuration));
    }
}

/// <summary>
/// Enhanced test handler interface that supports schema-driven test generation
/// </summary>
public interface ISchemaTestHandler : IServiceTestHandler
{
    /// <summary>
    /// Get schema-based tests for this service
    /// </summary>
    Task<ServiceTest[]> GetSchemaBasedTests(SchemaTestGenerator generator);

    /// <summary>
    /// Get the OpenAPI schema file path for this service
    /// </summary>
    string GetSchemaFilePath();
}
