using MessagePack;

namespace BeyondImmersion.Bannou.Protocol.Messages;

/// <summary>
/// Client response to an opportunity/QTE.
/// </summary>
[MessagePackObject]
public class OpportunityResponseMessage
{
    /// <summary>Identifier of the opportunity being responded to.</summary>
    [Key(0)]
    public string OpportunityId { get; set; } = string.Empty;

    /// <summary>Identifier of the option the player selected.</summary>
    [Key(1)]
    public string SelectedOptionId { get; set; } = string.Empty;

    /// <summary>Optional exchange identifier for correlated transactions.</summary>
    [Key(2)]
    public string? ExchangeId { get; set; }

    /// <summary>Client-measured latency in milliseconds (for timing compensation).</summary>
    [Key(3)]
    public int ClientLatencyMs { get; set; }
}
