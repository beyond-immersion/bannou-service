using BeyondImmersion.BannouService.Events;

namespace BeyondImmersion.BannouService.ClientEvents;

/// <summary>
/// Partial class extension to implement IBannouEvent interface.
/// Bridges the generated BaseClientEvent properties to the interface contract.
/// </summary>
public partial class BaseClientEvent : IBannouEvent
{
    /// <inheritdoc />
    string IBannouEvent.BannouEventId => Event_id.ToString();

    /// <inheritdoc />
    DateTimeOffset IBannouEvent.BannouTimestamp => Timestamp;
}
