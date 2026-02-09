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

    // Bits 0x02, 0x04, 0x08 are reserved. These were originally defined as Encrypted,
    // Compressed, and HighPriority respectively, but all three were removed because they
    // conflict with Connect's zero-copy routing architecture. Connect forwards payloads
    // unchanged to backend services; gateway-level encryption, compression, or priority
    // queuing would require payload transformation, destroying that property.
    // Transport-level concerns (TLS, permessage-deflate) handle encryption and compression.
    // Endpoint-to-endpoint concerns are signaled by clients and handled by target services.
    // Values are preserved to avoid shifting existing flag assignments.

    /// <summary>
    /// Reserved for future use. Do not assign.
    /// </summary>
    Reserved0x02 = 0x02,

    /// <summary>
    /// Reserved for future use. Do not assign.
    /// </summary>
    Reserved0x04 = 0x04,

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
