using MessagePack;

namespace BeyondImmersion.Bannou.GameProtocol.Messages;

/// <summary>
/// Client response to an opportunity/QTE.
/// </summary>
[MessagePackObject]
public class OpportunityResponseMessage
{
    [Key(0)]
    public string OpportunityId { get; set; } = string.Empty;

    [Key(1)]
    public string SelectedOptionId { get; set; } = string.Empty;

    [Key(2)]
    public string? ExchangeId { get; set; }

    [Key(3)]
    public int ClientLatencyMs { get; set; }
}
