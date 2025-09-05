namespace BeyondImmersion.BannouService.Testing;

/// <summary>
/// Configuration for test clients
/// </summary>
public sealed class TestConfiguration
{
    /// <summary>
    /// Base URL for HTTP direct service calls (e.g., "http://localhost:80")
    /// </summary>
    public string? Http_Base_Url { get; set; }

    /// <summary>
    /// WebSocket endpoint for Connect service testing (e.g., "ws://localhost:8080/connect", "wss://connect.bannou.game")
    /// </summary>
    public string? WebSocket_Endpoint { get; set; }

    /// <summary>
    /// Account registration endpoint (e.g., "localhost:80/api/accounts/create")
    /// </summary>
    public string? Register_Endpoint { get; set; }

    /// <summary>
    /// Login with credentials endpoint (e.g., "localhost:80/api/auth/login")
    /// </summary>
    public string? Login_Credentials_Endpoint { get; set; }

    /// <summary>
    /// Login with refresh token endpoint (e.g., "localhost:80/api/auth/refresh")
    /// </summary>
    public string? Login_Token_Endpoint { get; set; }

    /// <summary>
    /// Test username for authentication
    /// </summary>
    public string? Client_Username { get; set; }

    /// <summary>
    /// Test password for authentication
    /// </summary>
    public string? Client_Password { get; set; }

    /// <summary>
    /// Check if required HTTP testing configuration is provided
    /// </summary>
    public bool HasHttpRequired()
        => !string.IsNullOrWhiteSpace(Client_Username) &&
           !string.IsNullOrWhiteSpace(Client_Password) &&
           !string.IsNullOrWhiteSpace(Http_Base_Url) &&
           !string.IsNullOrWhiteSpace(Register_Endpoint) &&
           !string.IsNullOrWhiteSpace(Login_Credentials_Endpoint);

    /// <summary>
    /// Check if required WebSocket testing configuration is provided
    /// </summary>
    public bool HasWebSocketRequired()
        => HasHttpRequired() && !string.IsNullOrWhiteSpace(WebSocket_Endpoint);
}
