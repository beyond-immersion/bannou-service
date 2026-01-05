using System.Threading;
using System.Threading.Tasks;
using BeyondImmersion.Bannou.GameProtocol;
using MessagePack;

namespace BeyondImmersion.Bannou.GameTransport;

/// <summary>
/// Helper methods for serializing and sending game transport messages.
/// </summary>
public static class GameTransportHelper
{
    public static byte[] Serialize<T>(GameMessageType messageType, T message)
    {
        return GameProtocolEnvelope.Serialize(messageType, message, options: GameProtocolEnvelope.DefaultOptions);
    }

    public static byte[] SerializePayloadOnly<T>(T message)
    {
        return MessagePackSerializer.Serialize(message, GameProtocolEnvelope.DefaultOptions);
    }

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
