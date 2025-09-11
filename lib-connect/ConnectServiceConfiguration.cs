using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Configuration for the Connect service (WebSocket edge gateway).
/// </summary>
[ServiceConfiguration(typeof(ConnectService), envPrefix: "CONNECT_")]
public class ConnectServiceConfiguration : IServiceConfiguration
{
    /// <summary>
    /// Maximum number of concurrent WebSocket connections.
    /// </summary>
    public int MaxConnections { get; set; } = 10000;

    /// <summary>
    /// WebSocket keep-alive interval in seconds.
    /// </summary>
    public int KeepAliveIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum message size in bytes (default 1MB).
    /// </summary>
    public int MaxMessageSizeBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// Buffer size for WebSocket operations.
    /// </summary>
    public int BufferSize { get; set; } = 4096;

    /// <summary>
    /// Connection timeout in seconds.
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Whether to enable message compression.
    /// </summary>
    public bool EnableCompression { get; set; } = true;

    /// <summary>
    /// Whether to enable detailed connection logging.
    /// </summary>
    public bool EnableConnectionLogging { get; set; } = true;

    /// <summary>
    /// Whether to enable message routing metrics.
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Service discovery refresh interval in seconds.
    /// </summary>
    public int ServiceDiscoveryRefreshSeconds { get; set; } = 60;

    /// <summary>
    /// Default services available to unauthenticated clients.
    /// </summary>
    public string[] DefaultServices { get; set; } = ["auth.login", "auth.register", "auth.oauth"];

    /// <summary>
    /// Services available to authenticated clients.
    /// </summary>
    public string[] AuthenticatedServices { get; set; } = 
    [
        "accounts.get", "accounts.update", "accounts.profile",
        "behavior.create", "behavior.get", "behavior.update",
        "connect.status"
    ];

    /// <summary>
    /// Whether to enable client-to-client (P2P) routing.
    /// </summary>
    public bool EnableClientToClientRouting { get; set; } = true;

    /// <summary>
    /// Rate limiting: maximum messages per client per minute.
    /// </summary>
    public int MaxMessagesPerMinute { get; set; } = 1000;

    /// <summary>
    /// Rate limiting window in minutes.
    /// </summary>
    public int RateLimitWindowMinutes { get; set; } = 1;
}