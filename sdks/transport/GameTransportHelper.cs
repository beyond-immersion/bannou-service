using BeyondImmersion.Bannou.Protocol;
using MessagePack;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.Bannou.Transport;

/// <summary>
/// Helper methods for serializing and sending game transport messages.
/// </summary>
public static class GameTransportHelper
{
    /// <inheritdoc/>
    public static byte[] Serialize<T>(GameMessageType messageType, T message)
    {
        return GameProtocolEnvelope.Serialize(messageType, message, options: GameProtocolEnvelope.DefaultOptions);
    }

    /// <inheritdoc/>
    public static byte[] SerializePayloadOnly<T>(T message)
    {
        return MessagePackSerializer.Serialize(message, GameProtocolEnvelope.DefaultOptions);
    }

    /// <inheritdoc/>
    public static Task BroadcastAsync<T>(
        IGameServerTransport transport,
        GameMessageType messageType,
        T message,
        bool reliable = true,
        CancellationToken cancellationToken = default)
    {
        var payload = SerializePayloadOnly(message);
        return transport.BroadcastAsync(messageType, payload, reliable, cancellationToken);
    }

    /// <inheritdoc/>
    public static Task SendAsync<T>(
        IGameServerTransport transport,
        long clientId,
        GameMessageType messageType,
        T message,
        bool reliable = true,
        CancellationToken cancellationToken = default)
    {
        var payload = SerializePayloadOnly(message);
        return transport.SendAsync(clientId, messageType, payload, reliable, cancellationToken);
    }
}
