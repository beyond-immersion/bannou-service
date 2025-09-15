using System;
using System.Text;

namespace BeyondImmersion.BannouService.Connect.Protocol;

/// <summary>
/// Represents a complete binary message in the enhanced 31-byte protocol.
/// Provides zero-copy parsing and serialization for optimal performance.
/// </summary>
public readonly struct BinaryMessage
{
    /// <summary>
    /// Size of the binary header in bytes
    /// </summary>
    public const int HeaderSize = 31;

    /// <summary>
    /// Message behavior flags
    /// </summary>
    public MessageFlags Flags { get; }

    /// <summary>
    /// Channel for sequential message processing (0-65535, 0 = default)
    /// </summary>
    public ushort Channel { get; }

    /// <summary>
    /// Per-channel sequence number for message ordering
    /// </summary>
    public uint SequenceNumber { get; }

    /// <summary>
    /// Client-salted service GUID for routing
    /// </summary>
    public Guid ServiceGuid { get; }

    /// <summary>
    /// Unique message ID for request/response correlation
    /// </summary>
    public ulong MessageId { get; }

    /// <summary>
    /// Message payload (JSON or binary data based on flags)
    /// </summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>
    /// Initializes a new binary message with the specified parameters.
    /// </summary>
    public BinaryMessage(
        MessageFlags flags,
        ushort channel,
        uint sequenceNumber,
        Guid serviceGuid,
        ulong messageId,
        ReadOnlyMemory<byte> payload)
    {
        Flags = flags;
        Channel = channel;
        SequenceNumber = sequenceNumber;
        ServiceGuid = serviceGuid;
        MessageId = messageId;
        Payload = payload;
    }

    /// <summary>
    /// Creates a binary message from a JSON string payload.
    /// </summary>
    public static BinaryMessage FromJson(
        ushort channel,
        uint sequenceNumber,
        Guid serviceGuid,
        ulong messageId,
        string jsonPayload,
        MessageFlags additionalFlags = MessageFlags.None)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(jsonPayload);
        return new BinaryMessage(
            additionalFlags, // JSON is default, no Binary flag needed
            channel,
            sequenceNumber,
            serviceGuid,
            messageId,
            payloadBytes);
    }

    /// <summary>
    /// Creates a binary message from raw binary payload.
    /// </summary>
    public static BinaryMessage FromBinary(
        ushort channel,
        uint sequenceNumber,
        Guid serviceGuid,
        ulong messageId,
        ReadOnlyMemory<byte> binaryPayload,
        MessageFlags additionalFlags = MessageFlags.None)
    {
        return new BinaryMessage(
            additionalFlags | MessageFlags.Binary, // Mark as binary
            channel,
            sequenceNumber,
            serviceGuid,
            messageId,
            binaryPayload);
    }

    /// <summary>
    /// Creates a response message for the given request.
    /// </summary>
    public static BinaryMessage CreateResponse(
        BinaryMessage request,
        ResponseCodes responseCode,
        ReadOnlyMemory<byte> responsePayload = default)
    {
        var flags = request.Flags | MessageFlags.Response;

        // If no payload provided, create a simple JSON response with the response code
        var payload = responsePayload.IsEmpty
            ? Encoding.UTF8.GetBytes($"{{\"responseCode\":{(int)responseCode}}}")
            : responsePayload;

        return new BinaryMessage(
            flags,
            request.Channel,
            request.SequenceNumber,
            request.ServiceGuid,
            request.MessageId, // Same message ID for correlation
            payload);
    }

    /// <summary>
    /// Gets the JSON payload as a string (if the message is JSON).
    /// </summary>
    public string GetJsonPayload()
    {
        if (Flags.HasFlag(MessageFlags.Binary))
        {
            throw new InvalidOperationException("Cannot get JSON payload from binary message");
        }

        return Encoding.UTF8.GetString(Payload.Span);
    }

    /// <summary>
    /// Parses a binary message from a byte array.
    /// </summary>
    /// <param name="buffer">Buffer containing the complete message</param>
    /// <param name="messageLength">Total length of the message</param>
    /// <returns>Parsed binary message</returns>
    public static BinaryMessage Parse(byte[] buffer, int messageLength)
    {
        if (messageLength < HeaderSize)
        {
            throw new ArgumentException($"Message too short. Expected at least {HeaderSize} bytes, got {messageLength}");
        }

        // Parse 31-byte header using consistent network byte order
        var flags = (MessageFlags)buffer[0];                                    // Byte 0: Message flags
        var channel = NetworkByteOrder.ReadUInt16(buffer.AsSpan(1, 2));        // Bytes 1-2: Channel
        var sequence = NetworkByteOrder.ReadUInt32(buffer.AsSpan(3, 4));       // Bytes 3-6: Sequence number

        // Bytes 7-22: Service GUID (16 bytes) in network byte order
        var serviceGuid = NetworkByteOrder.ReadGuid(buffer.AsSpan(7, 16));

        var messageId = NetworkByteOrder.ReadUInt64(buffer.AsSpan(23, 8));     // Bytes 23-30: Message ID

        // Extract payload (remaining bytes after 31-byte header)
        var payloadLength = messageLength - HeaderSize;
        var payload = new byte[payloadLength];
        if (payloadLength > 0)
        {
            Array.Copy(buffer, HeaderSize, payload, 0, payloadLength);
        }

        return new BinaryMessage(flags, channel, sequence, serviceGuid, messageId, payload);
    }

    /// <summary>
    /// Serializes the binary message to a byte array.
    /// </summary>
    /// <returns>Complete message as byte array</returns>
    public byte[] ToByteArray()
    {
        var totalLength = HeaderSize + Payload.Length;
        var result = new byte[totalLength];

        // Build 31-byte header using consistent network byte order
        result[0] = (byte)Flags;                                              // Byte 0: Message flags
        NetworkByteOrder.WriteUInt16(result.AsSpan(1, 2), Channel);          // Bytes 1-2: Channel
        NetworkByteOrder.WriteUInt32(result.AsSpan(3, 4), SequenceNumber);   // Bytes 3-6: Sequence
        NetworkByteOrder.WriteGuid(result.AsSpan(7, 16), ServiceGuid);       // Bytes 7-22: Service GUID
        NetworkByteOrder.WriteUInt64(result.AsSpan(23, 8), MessageId);       // Bytes 23-30: Message ID

        // Append payload
        if (!Payload.IsEmpty)
        {
            Payload.Span.CopyTo(result.AsSpan(HeaderSize));
        }

        return result;
    }

    /// <summary>
    /// Returns true if this message expects a response.
    /// </summary>
    public bool ExpectsResponse => !Flags.HasFlag(MessageFlags.Event) && !Flags.HasFlag(MessageFlags.Response);

    /// <summary>
    /// Returns true if this message is a response to another message.
    /// </summary>
    public bool IsResponse => Flags.HasFlag(MessageFlags.Response);

    /// <summary>
    /// Returns true if this message should be routed to a client (not a service).
    /// </summary>
    public bool IsClientRouted => Flags.HasFlag(MessageFlags.Client);

    /// <summary>
    /// Returns true if this message has high priority.
    /// </summary>
    public bool IsHighPriority => Flags.HasFlag(MessageFlags.HighPriority);

    /// <summary>
    /// Returns a string representation of the message for debugging.
    /// </summary>
    public override string ToString()
    {
        return $"BinaryMessage(Flags={Flags}, Channel={Channel}, Seq={SequenceNumber}, " +
                $"ServiceGuid={ServiceGuid}, MessageId={MessageId}, PayloadSize={Payload.Length})";
    }
}
