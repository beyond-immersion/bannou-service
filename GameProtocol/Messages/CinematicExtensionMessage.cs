using MessagePack;
using System.Collections.Generic;

namespace BeyondImmersion.Bannou.GameProtocol.Messages;

/// <summary>
/// Cinematic extension offer (server -> client) carrying attach point metadata and payload reference.
/// </summary>
[MessagePackObject]
public class CinematicExtensionMessage
{
    [Key(0)]
    public string ExchangeId { get; set; } = string.Empty;

    [Key(1)]
    public string AttachPoint { get; set; } = string.Empty;

    [Key(2)]
    public string PayloadType { get; set; } = "abml-bytecode";

    [Key(3)]
    public string PayloadUrl { get; set; } = string.Empty;

    [Key(4)]
    public string? Hint { get; set; }

    /// <summary>
    /// Optional key-value state bag to seed the next behavior when applied.
    /// </summary>
    [Key(5)]
    public Dictionary<string, string>? InitiateState { get; set; }

    /// <summary>
    /// Unix epoch milliseconds when this extension expires.
    /// </summary>
    [Key(6)]
    public long? ValidUntilEpochMs { get; set; }
}
