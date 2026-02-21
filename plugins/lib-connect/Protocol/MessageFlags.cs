using System;

namespace BeyondImmersion.BannouService.Connect.Protocol;

/// <summary>
/// Bit flags controlling message behavior in the enhanced 31-byte binary protocol.
/// These flags provide zero-copy routing and performance optimizations.
/// </summary>
[Flags]
public enum MessageFlags : byte
{
    /// <summary>
    /// Default - JSON text, service request, unencrypted, standard priority, expects response
    /// </summary>
    None = 0x00,

    /// <summary>
    /// Message payload is binary data (not JSON)
    /// </summary>
    Binary = 0x01,

    /// <summary>
    /// Reserved for future use. Do not assign.
    /// </summary>
    Reserved0x02 = 0x02,

    /// <summary>
    /// Payload is Brotli-compressed. Client must decompress before parsing.
    /// Only set on server-to-client messages when compression is enabled and
    /// payload exceeds the configured size threshold.
    /// </summary>
    Compressed = 0x04,

    /// <summary>
    /// Reserved for future use. Do not assign.
    /// </summary>
    Reserved0x08 = 0x08,

    /// <summary>
    /// Fire-and-forget message, no response expected
    /// </summary>
    Event = 0x10,

    /// <summary>
    /// Route to another WebSocket client (not a Bannou service)
    /// </summary>
    Client = 0x20,

    /// <summary>
    /// Message is a response to an RPC (not a new request)
    /// </summary>
    Response = 0x40,

    /// <summary>
    /// Request metadata about endpoint instead of executing it.
    /// When set, the Channel field specifies the meta type:
    /// 0 = endpoint-info, 1 = request-schema, 2 = response-schema, 3 = full-schema.
    /// Connect service transforms the path and routes to companion endpoints.
    /// </summary>
    Meta = 0x80
}
