using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Configuration class for Connect service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(ConnectService), envPrefix: "BANNOU_")]
public class ConnectServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Maximum number of concurrent WebSocket connections
    /// Environment variable: MAXCONCURRENTCONNECTIONS or BANNOU_MAXCONCURRENTCONNECTIONS
    /// </summary>
    public int MaxConcurrentConnections { get; set; } = 10000;

    /// <summary>
    /// WebSocket connection timeout in seconds
    /// Environment variable: CONNECTIONTIMEOUTSECONDS or BANNOU_CONNECTIONTIMEOUTSECONDS
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Interval between heartbeat messages
    /// Environment variable: HEARTBEATINTERVALSECONDS or BANNOU_HEARTBEATINTERVALSECONDS
    /// </summary>
    public int HeartbeatIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum number of queued messages per connection
    /// Environment variable: MESSAGEQUEUESIZE or BANNOU_MESSAGEQUEUESIZE
    /// </summary>
    public int MessageQueueSize { get; set; } = 1000;

    /// <summary>
    /// Binary protocol version identifier
    /// Environment variable: BINARYPROTOCOLVERSION or BANNOU_BINARYPROTOCOLVERSION
    /// </summary>
    public string BinaryProtocolVersion { get; set; } = "2.0";

    /// <summary>
    /// Size of message buffers in bytes
    /// Environment variable: BUFFERSIZE or BANNOU_BUFFERSIZE
    /// </summary>
    public int BufferSize { get; set; } = 65536;

    /// <summary>
    /// Services available to unauthenticated connections
    /// Environment variable: DEFAULTSERVICES or BANNOU_DEFAULTSERVICES
    /// </summary>
    public string[] DefaultServices { get; set; } = ["auth", "website"];

    /// <summary>
    /// Additional services available to authenticated connections
    /// Environment variable: AUTHENTICATEDSERVICES or BANNOU_AUTHENTICATEDSERVICES
    /// </summary>
    public string[] AuthenticatedServices { get; set; } = ["accounts", "behavior", "permissions", "gamesession"];

    /// <summary>
    /// Enable routing messages between WebSocket clients
    /// Environment variable: ENABLECLIENTTOCLIENTROUTING or BANNOU_ENABLECLIENTTOCLIENTROUTING
    /// </summary>
    public bool EnableClientToClientRouting { get; set; } = true;

    /// <summary>
    /// Rate limit for messages per minute per client
    /// Environment variable: MAXMESSAGESPERMINUTE or BANNOU_MAXMESSAGESPERMINUTE
    /// </summary>
    public int MaxMessagesPerMinute { get; set; } = 1000;

    /// <summary>
    /// Rate limit window in minutes
    /// Environment variable: RATELIMITWINDOWMINUTES or BANNOU_RATELIMITWINDOWMINUTES
    /// </summary>
    public int RateLimitWindowMinutes { get; set; } = 1;

    /// <summary>
    /// Redis connection string for session management
    /// Environment variable: REDISCONNECTIONSTRING or BANNOU_REDISCONNECTIONSTRING
    /// </summary>
    public string RedisConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// RSA public key for JWT validation (PEM format)
    /// Environment variable: JWTPUBLICKEY or BANNOU_JWTPUBLICKEY
    /// </summary>
    public string JwtPublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Interval for service heartbeat updates to Redis
    /// Environment variable: SERVICEHEARTBEATINTERVALSECONDS or BANNOU_SERVICEHEARTBEATINTERVALSECONDS
    /// </summary>
    public int ServiceHeartbeatIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Interval for refreshing service discovery from Redis
    /// Environment variable: SERVICEDISCOVERYREFRESHSECONDS or BANNOU_SERVICEDISCOVERYREFRESHSECONDS
    /// </summary>
    public int ServiceDiscoveryRefreshSeconds { get; set; } = 60;

}
