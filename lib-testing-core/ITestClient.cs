namespace BeyondImmersion.BannouService.Testing;

/// <summary>
/// Interface for test clients that can execute API calls via different transport mechanisms
/// </summary>
public interface ITestClient : IDisposable
{
    /// <summary>
    /// Make an authenticated HTTP POST request to a service endpoint
    /// </summary>
    Task<TestResponse<T>> PostAsync<T>(string endpoint, object? requestBody = null) where T : class;

    /// <summary>
    /// Make an authenticated HTTP GET request to a service endpoint
    /// </summary>
    Task<TestResponse<T>> GetAsync<T>(string endpoint) where T : class;

    /// <summary>
    /// Register a new user account
    /// </summary>
    Task<bool> RegisterAsync(string username, string password);

    /// <summary>
    /// Login with username/password credentials
    /// </summary>
    Task<bool> LoginAsync(string username, string password);

    /// <summary>
    /// Check if client is currently authenticated
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Get the transport type being used (HTTP or WebSocket)
    /// </summary>
    string TransportType { get; }
}
