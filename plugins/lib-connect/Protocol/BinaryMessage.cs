using System;
using System.Text;

namespace BeyondImmersion.BannouService.Connect.Protocol;

/// <summary>
/// Represents a complete binary message in the Bannou WebSocket protocol.
/// Request messages use a 31-byte header, response messages use a 16-byte header.
/// Provides zero-copy parsing and serialization for optimal performance.
/// </summary>
public readonly struct BinaryMessage
{
    /// <summary>
    /// Size of the binary header in bytes for request messages.
    /// </summary>
    public const int HeaderSize = 31;

    /// <summary>
    /// Size of the binary header in bytes for response messages.
    /// Response headers are smaller because ServiceGuid is not needed (client correlates via MessageId).
    /// </summary>
    public const int ResponseHeaderSize = 16;

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
    /// Client-salted service GUID for routing (only used in request messages).
    /// For response messages, this is Guid.Empty since routing is not needed.
    /// </summary>
    public Guid ServiceGuid { get; }

    /// <summary>
    /// Unique message ID for request/response correlation
    /// </summary>
    public ulong MessageId { get; }

    /// <summary>
    /// Response code for response messages (0 = OK, non-zero = error).
    /// Only meaningful when Flags has Response set.
    /// </summary>
    public byte ResponseCode { get; }

    /// <summary>
    /// Message payload (JSON or binary data based on flags).
    /// For error responses (non-zero ResponseCode), this is empty.
    /// </summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>
    /// Initializes a new binary message with the specified parameters (for requests).
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
        ResponseCode = 0;
        Payload = payload;
    }

    /// <summary>
    /// Initializes a new binary message with the specified parameters (for responses).
    /// </summary>
    public BinaryMessage(
        MessageFlags flags,
        ushort channel,
        uint sequenceNumber,
        ulong messageId,
        byte responseCode,
        ReadOnlyMemory<byte> payload)
    {
        Flags = flags;
        Channel = channel;
        SequenceNumber = sequenceNumber;
        ServiceGuid = Guid.Empty; // Not used in responses
        MessageId = messageId;
        ResponseCode = responseCode;
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
    /// For error responses (non-zero responseCode), the payload should be empty.
    /// For success responses (responseCode = 0), the payload contains the response data.
    /// </summary>
    public static BinaryMessage CreateResponse(
        BinaryMessage request,
        ResponseCodes responseCode,
        ReadOnlyMemory<byte> responsePayload = default)
    {
        var flags = request.Flags | MessageFlags.Response;

        // For error responses, payload is empty - the response code tells the story
        // For success responses, payload contains the actual response data
        var payload = responseCode != ResponseCodes.OK ? ReadOnlyMemory<byte>.Empty : responsePayload;

        return new BinaryMessage(
            flags,
            request.Channel,
            request.SequenceNumber,
            request.MessageId,
            (byte)responseCode,
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
    /// Automatically detects whether this is a request (31-byte header) or response (16-byte header).
    /// </summary>
    /// <param name="buffer">Buffer containing the complete message</param>
    /// <param name="messageLength">Total length of the message</param>
    /// <returns>Parsed binary message</returns>
    public static BinaryMessage Parse(byte[] buffer, int messageLength)
    {
        if (messageLength < 1)
        {
            throw new ArgumentException("Message too short. Expected at least 1 byte for flags.");
        }

        var flags = (MessageFlags)buffer[0];

        // Check if this is a response message (16-byte header) or request message (31-byte header)
        if (flags.HasFlag(MessageFlags.Response))
        {
            return ParseResponse(buffer, messageLength, flags);
        }
        else
        {
            return ParseRequest(buffer, messageLength, flags);
        }
    }

    private static BinaryMessage ParseRequest(byte[] buffer, int messageLength, MessageFlags flags)
    {
        if (messageLength < HeaderSize)
        {
            throw new ArgumentException($"Request message too short. Expected at least {HeaderSize} bytes, got {messageLength}");
        }

        // Parse 31-byte request header using consistent network byte order
        var channel = NetworkByteOrder.ReadUInt16(buffer.AsSpan(1, 2));        // Bytes 1-2: Channel
        var sequence = NetworkByteOrder.ReadUInt32(buffer.AsSpan(3, 4));       // Bytes 3-6: Sequence number
        var serviceGuid = NetworkByteOrder.ReadGuid(buffer.AsSpan(7, 16));     // Bytes 7-22: Service GUID
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

    private static BinaryMessage ParseResponse(byte[] buffer, int messageLength, MessageFlags flags)
    {
        if (messageLength < ResponseHeaderSize)
        {
            throw new ArgumentException($"Response message too short. Expected at least {ResponseHeaderSize} bytes, got {messageLength}");
        }

        // Parse 16-byte response header using consistent network byte order
        var channel = NetworkByteOrder.ReadUInt16(buffer.AsSpan(1, 2));        // Bytes 1-2: Channel
        var sequence = NetworkByteOrder.ReadUInt32(buffer.AsSpan(3, 4));       // Bytes 3-6: Sequence number
        var messageId = NetworkByteOrder.ReadUInt64(buffer.AsSpan(7, 8));      // Bytes 7-14: Message ID
        var responseCode = buffer[15];                                          // Byte 15: Response code

        // Extract payload (remaining bytes after 16-byte header)
        var payloadLength = messageLength - ResponseHeaderSize;
        var payload = new byte[payloadLength];
        if (payloadLength > 0)
        {
            Array.Copy(buffer, ResponseHeaderSize, payload, 0, payloadLength);
        }

        return new BinaryMessage(flags, channel, sequence, messageId, responseCode, payload);
    }

    /// <summary>
    /// Serializes the binary message to a byte array.
    /// Uses 31-byte header for requests, 16-byte header for responses.
    /// </summary>
    /// <returns>Complete message as byte array</returns>
    public byte[] ToByteArray()
    {
        if (IsResponse)
        {
            return ToResponseByteArray();
        }
        else
        {
            return ToRequestByteArray();
        }
    }

    private byte[] ToRequestByteArray()
    {
        var totalLength = HeaderSize + Payload.Length;
        var result = new byte[totalLength];

        // Build 31-byte request header using consistent network byte order
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

    private byte[] ToResponseByteArray()
    {
        var totalLength = ResponseHeaderSize + Payload.Length;
        var result = new byte[totalLength];

        // Build 16-byte response header using consistent network byte order
        result[0] = (byte)Flags;                                              // Byte 0: Message flags
        NetworkByteOrder.WriteUInt16(result.AsSpan(1, 2), Channel);          // Bytes 1-2: Channel
        NetworkByteOrder.WriteUInt32(result.AsSpan(3, 4), SequenceNumber);   // Bytes 3-6: Sequence
        NetworkByteOrder.WriteUInt64(result.AsSpan(7, 8), MessageId);        // Bytes 7-14: Message ID
        result[15] = ResponseCode;                                            // Byte 15: Response code

        // Append payload (empty for error responses)
        if (!Payload.IsEmpty)
        {
            Payload.Span.CopyTo(result.AsSpan(ResponseHeaderSize));
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
    /// Returns true if this is a successful response (ResponseCode is 0).
    /// Only meaningful for response messages.
    /// </summary>
    public bool IsSuccess => IsResponse && ResponseCode == 0;

    /// <summary>
    /// Returns true if this is an error response (ResponseCode is non-zero).
    /// Only meaningful for response messages.
    /// </summary>
    public bool IsError => IsResponse && ResponseCode != 0;

    /// <summary>
    /// Returns true if this message requests endpoint metadata instead of executing the endpoint.
    /// When set, the Channel field specifies the meta type:
    /// 0 = endpoint-info, 1 = request-schema, 2 = response-schema, 3 = full-schema.
    /// </summary>
    public bool IsMeta => Flags.HasFlag(MessageFlags.Meta);

    /// <summary>
    /// Returns a string representation of the message for debugging.
    /// </summary>
    public override string ToString()
    {
        if (IsResponse)
        {
            return $"BinaryMessage(Response, Flags={Flags}, Channel={Channel}, Seq={SequenceNumber}, " +
                    $"MessageId={MessageId}, ResponseCode={ResponseCode}, PayloadSize={Payload.Length})";
        }
        else
        {
            return $"BinaryMessage(Request, Flags={Flags}, Channel={Channel}, Seq={SequenceNumber}, " +
                    $"ServiceGuid={ServiceGuid}, MessageId={MessageId}, PayloadSize={Payload.Length})";
        }
    }
}
