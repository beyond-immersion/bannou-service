using System.Text;

namespace BeyondImmersion.BannouService.Connect.Models;

/// <summary>
/// Represents a binary message in the Bannou WebSocket protocol.
/// Format: [Service GUID: 16 bytes][Message ID: 8 bytes][Payload: Variable]
/// </summary>
public readonly struct BinaryMessage
{
    private const int ServiceGuidOffset = 0;
    private const int MessageIdOffset = 16;
    private const int PayloadOffset = 24;
    private const int HeaderSize = 24;

    private readonly ReadOnlyMemory<byte> _data;

    public BinaryMessage(ReadOnlyMemory<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new ArgumentException($"Message must be at least {HeaderSize} bytes", nameof(data));

        _data = data;
    }

    /// <summary>
    /// Service GUID for routing (16 bytes).
    /// </summary>
    public Guid ServiceGuid
    {
        get
        {
            var guidBytes = _data.Span.Slice(ServiceGuidOffset, 16);
            return new Guid(guidBytes);
        }
    }

    /// <summary>
    /// Message ID for correlation (8 bytes).
    /// </summary>
    public long MessageId
    {
        get
        {
            var messageIdBytes = _data.Span.Slice(MessageIdOffset, 8);
            return BitConverter.ToInt64(messageIdBytes);
        }
    }

    /// <summary>
    /// Message payload (variable length).
    /// </summary>
    public ReadOnlyMemory<byte> Payload => _data.Slice(PayloadOffset);

    /// <summary>
    /// Full message data including header.
    /// </summary>
    public ReadOnlyMemory<byte> Data => _data;

    /// <summary>
    /// Message payload as UTF-8 string (for JSON payloads).
    /// </summary>
    public string PayloadAsString => Encoding.UTF8.GetString(Payload.Span);

    /// <summary>
    /// Creates a new binary message.
    /// </summary>
    public static BinaryMessage Create(Guid serviceGuid, long messageId, ReadOnlySpan<byte> payload)
    {
        var message = new byte[HeaderSize + payload.Length];
        var messageSpan = message.AsSpan();

        // Write service GUID (16 bytes)
        serviceGuid.TryWriteBytes(messageSpan.Slice(ServiceGuidOffset, 16));

        // Write message ID (8 bytes)
        BitConverter.TryWriteBytes(messageSpan.Slice(MessageIdOffset, 8), messageId);

        // Write payload
        payload.CopyTo(messageSpan.Slice(PayloadOffset));

        return new BinaryMessage(message);
    }

    /// <summary>
    /// Creates a new binary message with JSON payload.
    /// </summary>
    public static BinaryMessage Create(Guid serviceGuid, long messageId, string jsonPayload)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(jsonPayload);
        return Create(serviceGuid, messageId, payloadBytes);
    }

    /// <summary>
    /// Creates a response message with the same message ID.
    /// </summary>
    public BinaryMessage CreateResponse(ReadOnlySpan<byte> responsePayload)
    {
        return Create(ServiceGuid, MessageId, responsePayload);
    }

    /// <summary>
    /// Creates a response message with JSON payload.
    /// </summary>
    public BinaryMessage CreateResponse(string jsonResponse)
    {
        return Create(ServiceGuid, MessageId, jsonResponse);
    }

    /// <summary>
    /// Validates the message format.
    /// </summary>
    public bool IsValid => _data.Length >= HeaderSize;

    /// <summary>
    /// Gets the total message size in bytes.
    /// </summary>
    public int Size => _data.Length;

    public override string ToString()
    {
        return $"BinaryMessage {{ ServiceGuid: {ServiceGuid}, MessageId: {MessageId}, PayloadSize: {Payload.Length} }}";
    }
}
