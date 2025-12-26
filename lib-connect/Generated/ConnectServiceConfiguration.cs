using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Configuration class for Connect service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(ConnectService))]
[Obsolete]
public class ConnectServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Maximum number of concurrent WebSocket connections
    /// Environment variable: MAXCONCURRENTCONNECTIONS
    /// </summary>
    public int MaxConcurrentConnections { get; set; } = 10000;

    /// <summary>
    /// WebSocket connection timeout in seconds
    /// Environment variable: CONNECTIONTIMEOUTSECONDS
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Interval between heartbeat messages
    /// Environment variable: HEARTBEATINTERVALSECONDS
    /// </summary>
    public int HeartbeatIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum number of queued messages per connection
    /// Environment variable: MESSAGEQUEUESIZE
    /// </summary>
    public int MessageQueueSize { get; set; } = 1000;

    /// <summary>
    /// Binary protocol version identifier
    /// Environment variable: BINARYPROTOCOLVERSION
    /// </summary>
    public string BinaryProtocolVersion { get; set; } = "2.0";

    /// <summary>
    /// Size of message buffers in bytes
    /// Environment variable: BUFFERSIZE
    /// </summary>
    public int BufferSize { get; set; } = 65536;

    /// <summary>
    /// Services available to unauthenticated connections
    /// Environment variable: DEFAULTSERVICES
    /// </summary>
    public string[] DefaultServices { get; set; } = ["auth", "website"];

    /// <summary>
    /// Additional services available to authenticated connections
    /// Environment variable: AUTHENTICATEDSERVICES
    /// </summary>
    public string[] AuthenticatedServices { get; set; } = ["accounts", "behavior", "permissions", "gamesession"];

    /// <summary>
    /// Enable routing messages between WebSocket clients
    /// Environment variable: ENABLECLIENTTOCLIENTROUTING
    /// </summary>
    public bool EnableClientToClientRouting { get; set; } = true;

    /// <summary>
    /// Rate limit for messages per minute per client
    /// Environment variable: MAXMESSAGESPERMINUTE
    /// </summary>
    public int MaxMessagesPerMinute { get; set; } = 1000;

    /// <summary>
    /// Rate limit window in minutes
    /// Environment variable: RATELIMITWINDOWMINUTES
    /// </summary>
    public int RateLimitWindowMinutes { get; set; } = 1;

    /// <summary>
    /// RSA public key for JWT validation (PEM format)
    /// Environment variable: JWTPUBLICKEY
    /// </summary>
    public string JwtPublicKey { get; set; } = string.Empty;

    /// <summary>
    /// RabbitMQ connection string for client event subscriptions (Tenet 21 - no default, required)
    /// Environment variable: CONNECT_RABBITMQ_CONNECTION_STRING
    /// </summary>
    public string RabbitMqConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Server salt for client GUID generation. Must be shared across all Connect instances.
    /// Environment variable: CONNECT_SERVER_SALT
    /// </summary>
    public string ServerSalt { get; set; } = string.Empty;

    /// <summary>
    /// WebSocket URL returned to clients for reconnection
    /// Environment variable: CONNECT_URL
    /// </summary>
    public string ConnectUrl { get; set; } = string.Empty;

}
