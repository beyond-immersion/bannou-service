using MessagePack;
using System;

namespace BeyondImmersion.Bannou.GameProtocol;

/// <summary>
/// Helper for serializing/deserializing game transport messages with a 2-byte envelope:
/// [0] = protocol version, [1] = GameMessageType, [2..] = MessagePack-serialized payload (LZ4).
/// </summary>
public static class GameProtocolEnvelope
{
    /// <summary>
    /// Current protocol version for envelopes.
    /// </summary>
    public const byte CurrentVersion = 1;

    /// <summary>
    /// Default MessagePack options (LZ4 compression).
    /// </summary>
    public static readonly MessagePackSerializerOptions DefaultOptions =
        MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4Block);

    /// <summary>
    /// Serialize a message with an envelope (version + message type).
    /// </summary>
    public static byte[] Serialize<T>(
        GameMessageType messageType,
        T message,
        byte version = CurrentVersion,
        MessagePackSerializerOptions? options = null)
    {
        if (version == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Version must be non-zero.");
        }

        var opts = options ?? DefaultOptions;
        var payload = MessagePackSerializer.Serialize(message, opts);
        var buffer = new byte[2 + payload.Length];
        buffer[0] = version;
        buffer[1] = (byte)messageType;
        Buffer.BlockCopy(payload, 0, buffer, 2, payload.Length);
        return buffer;
    }

    /// <summary>
    /// Parse an envelope and return version, type, and payload slice.
    /// </summary>
    public static (byte version, GameMessageType messageType, ReadOnlyMemory<byte> payload) Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            throw new ArgumentException("Buffer too small to contain header.", nameof(data));
        }

        var version = data[0];
        var type = (GameMessageType)data[1];
        var payload = data.Length > 2 ? data.Slice(2).ToArray() : Array.Empty<byte>();
        return (version, type, payload);
    }

    /// <summary>
    /// Deserialize a payload after parsing the envelope.
    /// </summary>
    public static T DeserializePayload<T>(ReadOnlyMemory<byte> payload, MessagePackSerializerOptions? options = null)
    {
        var opts = options ?? DefaultOptions;
        return MessagePackSerializer.Deserialize<T>(payload, opts);
    }

    /// <summary>
    /// Convenience: parse envelope and deserialize payload to a type.
    /// </summary>
    public static (byte version, GameMessageType messageType, T payload) ParseAndDeserialize<T>(
        ReadOnlySpan<byte> data,
        MessagePackSerializerOptions? options = null)
    {
        var (version, messageType, payloadBytes) = Parse(data);
        var payload = DeserializePayload<T>(payloadBytes, options);
        return (version, messageType, payload);
    }
}
