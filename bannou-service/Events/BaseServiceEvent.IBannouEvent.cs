namespace BeyondImmersion.BannouService.Events;

/// <summary>
/// Partial class extension to implement IBannouEvent interface.
/// Bridges the generated BaseServiceEvent properties to the interface contract.
/// </summary>
public partial class BaseServiceEvent : IBannouEvent
{
    /// <inheritdoc />
    Guid EventId;

    /// <inheritdoc />
    DateTime Timestamp;
}
