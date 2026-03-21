using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.Worldstate.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Worldstate;

// =============================================================================
// WorldstateService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by WorldstateService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (WorldstateService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IWorldstateService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (WorldstateService.Helpers.cs):
//     Contains all private/internal helper methods, core logic extracted
//     from endpoints, event publishing helpers, query builders, mapping
//     functions, and any other non-public methods. Every async method in
//     this file MUST call ITelemetryProvider.StartActivity to ensure
//     sub-operations are properly instrumented.
//
// Structural tests enforce both rules:
//   - Services_PrimaryFile_DoesNotCallStartActivity
//   - Services_HelperFiles_HaveStartActivityWhenAsync
//
// WHAT GOES HERE:
//   - Private async helper methods (with StartActivity spans)
//   - Private sync helper methods (query builders, mappers, validators)
//   - Internal static key builders (already in primary file by convention,
//     but may be moved here if the primary file is large)
//   - Event publishing helper methods
//   - Any extracted "core" logic (e.g., CreateAccountCoreAsync)
//
// WHAT STAYS IN THE PRIMARY FILE:
//   - Public interface method implementations (/// <inheritdoc/> methods)
//   - Constructor and field declarations
//   - Constants and key prefix definitions
//
// See: docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md (T30)
// See: docs/reference/HELPERS-AND-COMMON-PATTERNS.md
// =============================================================================

/// <summary>
/// Private and internal helper methods for WorldstateService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class WorldstateService
{
    /// <summary>
    /// Publishes boundary events (hour, period, day, month, season, year) based on the
    /// list of boundary crossings detected during clock advancement. Delegates to the
    /// shared static helper to avoid duplication with the worker's boundary publishing.
    /// </summary>
    /// <param name="realmId">The realm whose clock crossed boundaries.</param>
    /// <param name="clock">The current clock state after advancement.</param>
    /// <param name="boundaries">The list of boundary crossings to publish events for.</param>
    /// <param name="isCatchUp">Whether these boundaries were crossed during catch-up processing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task PublishBoundaryEventsAsync(
        Guid realmId,
        RealmClockModel clock,
        List<BoundaryCrossing> boundaries,
        bool isCatchUp,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.worldstate", "WorldstateService.PublishBoundaryEvents");

        var snapshot = WorldstateBoundaryEventPublisher.MapClockToSnapshot(clock);
        await WorldstateBoundaryEventPublisher.PublishBoundaryEventsAsync(
            realmId, snapshot, clock, boundaries, isCatchUp, _messageBus, cancellationToken);
    }
}
