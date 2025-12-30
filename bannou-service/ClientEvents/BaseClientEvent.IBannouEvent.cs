using BeyondImmersion.BannouService.Events;

namespace BeyondImmersion.BannouService.ClientEvents;

/// <summary>
/// Partial class extension to implement IBannouEvent interface.
/// Bridges the generated BaseClientEvent properties to the interface contract.
/// </summary>
public partial class BaseClientEvent : IBannouEvent
{
    /// <inheritdoc />
    Guid EventId;

    /// <inheritdoc />
    DateTime Timestamp;
}
