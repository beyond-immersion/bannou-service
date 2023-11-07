using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using StackExchange.Redis;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Service component responsible for client connection handling.
/// </summary>
[DaprService("connect", typeof(IConnectService))]
public sealed class ConnectService : DaprService<ConnectServiceConfiguration>, IConnectService
{
    private IConnectionMultiplexer? _redisConnection;

    async Task IDaprService.OnStart(CancellationToken cancellationToken)
    {
        if (Configuration.Redis_Connection_String == null)
            throw new NullReferenceException();

        _redisConnection = await ConnectionMultiplexer.ConnectAsync(Configuration.Redis_Connection_String);
        if (!_redisConnection.IsConnected)
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Could not connect to Redis.");
    }

    public (StatusCodes, IConnectService.ConnectResult?) Connect()
    {
        return (StatusCodes.Ok, null);
    }


}
