using BCrypt.Net;
using BeyondImmersion.Bannou.Auth.ClientEvents;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Account;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Auth.Services;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Auth;

// =============================================================================
// AuthService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by AuthService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (AuthService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IAuthService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (AuthService.Helpers.cs):
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
/// Private and internal helper methods for AuthService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class AuthService
{
    private async Task PublishClientEventToAccountSessionsAsync<TEvent>(
        Guid accountId, TEvent clientEvent, CancellationToken ct = default)
        where TEvent : BaseClientEvent
    {
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "AuthService.PublishClientEventToAccountSessions");
        try
        {
            var count = await _entitySessionRegistry.PublishToEntitySessionsAsync(
                "account", accountId, clientEvent, ct);
            _logger.LogDebug("Published {EventName} to {SessionCount} sessions for account {AccountId}",
                clientEvent.EventName, count, accountId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish {EventName} to account {AccountId} sessions",
                clientEvent.EventName, accountId);
        }
    }
}
