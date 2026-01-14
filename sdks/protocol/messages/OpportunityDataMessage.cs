using MessagePack;
using System.Collections.Generic;

namespace BeyondImmersion.Bannou.Protocol.Messages;

/// <summary>
/// Opportunity/QTE prompt (server -> client).
/// </summary>
[MessagePackObject]
public class OpportunityDataMessage
{
    /// <summary>Unique identifier for this opportunity.</summary>
    [Key(0)]
    public string OpportunityId { get; set; } = string.Empty;

    /// <summary>Text prompt displayed to the player.</summary>
    [Key(1)]
    public string Prompt { get; set; } = string.Empty;

    /// <summary>Available options the player can select.</summary>
    [Key(2)]
    public List<OpportunityOption> Options { get; set; } = new();

    /// <summary>Option to select if player doesn't respond in time.</summary>
    [Key(3)]
    public string? DefaultOptionId { get; set; }

    /// <summary>Time limit in milliseconds before default is selected.</summary>
    [Key(4)]
    public int DeadlineMs { get; set; }

    /// <summary>Optional exchange identifier for correlated transactions.</summary>
    [Key(5)]
    public string? ExchangeId { get; set; }

    /// <summary>Whether this opportunity requires immediate response (blocks gameplay).</summary>
    [Key(6)]
    public bool Forced { get; set; }
}

/// <summary>
/// A selectable option within an opportunity/QTE prompt.
/// </summary>
[MessagePackObject]
public class OpportunityOption
{
    /// <summary>Unique identifier for this option (sent back in response).</summary>
    [Key(0)]
    public string Id { get; set; } = string.Empty;

    /// <summary>Display label shown to the player.</summary>
    [Key(1)]
    public string Label { get; set; } = string.Empty;
}
