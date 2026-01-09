namespace BeyondImmersion.BannouService.Testing;

/// <summary>
/// Configuration for test clients
/// </summary>
public sealed class TestConfiguration
{
    /// <summary>
    /// Base URL for HTTP direct service calls (e.g., "http://localhost:80")
    /// </summary>
    public string? HttpBaseUrl { get; set; }

    /// <summary>
    /// WebSocket endpoint for Connect service testing (e.g., "ws://localhost:8080/connect", "wss://connect.bannou.game")
    /// </summary>
    public string? WebSocketEndpoint { get; set; }

    /// <summary>
    /// Account registration endpoint (e.g., "localhost:80/api/account/create")
    /// </summary>
    public string? RegisterEndpoint { get; set; }

    /// <summary>
    /// Login with credentials endpoint (e.g., "localhost:80/api/auth/login")
    /// </summary>
    public string? LoginCredentialsEndpoint { get; set; }

    /// <summary>
    /// Login with refresh token endpoint (e.g., "localhost:80/api/auth/refresh")
    /// </summary>
    public string? LoginTokenEndpoint { get; set; }

    /// <summary>
    /// Test username for authentication
    /// </summary>
    public string? ClientUsername { get; set; }

    /// <summary>
    /// Test password for authentication
    /// </summary>
    public string? ClientPassword { get; set; }

    /// <summary>
    /// Check if required HTTP testing configuration is provided
    /// </summary>
    public bool HasHttpRequired()
        => !string.IsNullOrWhiteSpace(ClientUsername) &&
            !string.IsNullOrWhiteSpace(ClientPassword) &&
            !string.IsNullOrWhiteSpace(HttpBaseUrl) &&
            !string.IsNullOrWhiteSpace(RegisterEndpoint) &&
            !string.IsNullOrWhiteSpace(LoginCredentialsEndpoint);

    /// <summary>
    /// Check if required WebSocket testing configuration is provided
    /// </summary>
    public bool HasWebSocketRequired()
        => HasHttpRequired() && !string.IsNullOrWhiteSpace(WebSocketEndpoint);
}
