using MessagePack;
using System.Collections.Generic;

namespace BeyondImmersion.Bannou.GameProtocol.Messages;

/// <summary>
/// Opportunity/QTE prompt (server -> client).
/// </summary>
[MessagePackObject]
public class OpportunityDataMessage
{
    [Key(0)]
    public string OpportunityId { get; set; } = string.Empty;

    [Key(1)]
    public string Prompt { get; set; } = string.Empty;

    [Key(2)]
    public List<OpportunityOption> Options { get; set; } = new();

    [Key(3)]
    public string? DefaultOptionId { get; set; }

    [Key(4)]
    public int DeadlineMs { get; set; }

    [Key(5)]
    public string? ExchangeId { get; set; }

    [Key(6)]
    public bool Forced { get; set; }
}

[MessagePackObject]
public class OpportunityOption
{
    [Key(0)]
    public string Id { get; set; } = string.Empty;

    [Key(1)]
    public string Label { get; set; } = string.Empty;
}
