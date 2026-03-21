using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.GameService;

// =============================================================================
// GameServiceService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by GameServiceService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (GameServiceService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IGameServiceService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (GameServiceService.Helpers.cs):
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
/// Private and internal helper methods for GameServiceService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class GameServiceService
{
    #region Private Helpers

    /// <summary>
    /// Add a service ID to the service list index.
    /// Uses ETag-based optimistic concurrency per IMPLEMENTATION TENETS (Multi-Instance Safety).
    /// </summary>
    /// <param name="serviceId">The service ID to add to the master list.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    private async Task AddToServiceListAsync(Guid serviceId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-service", "GameServiceService.AddToServiceList");

        for (var attempt = 0; attempt < _configuration.ServiceListRetryAttempts; attempt++)
        {
            var (serviceIds, etag) = await _listStore.GetWithETagAsync(SERVICE_LIST_KEY, cancellationToken);
            serviceIds ??= new List<Guid>();

            if (serviceIds.Contains(serviceId))
            {
                return; // Already in list
            }

            serviceIds.Add(serviceId);

            // etag is null when the list key doesn't exist yet; empty string signals
            // "new entry" to TrySaveAsync (will never conflict on new entries)
            var result = await _listStore.TrySaveAsync(SERVICE_LIST_KEY, serviceIds, etag ?? string.Empty, cancellationToken: cancellationToken);
            if (result != null)
            {
                return;
            }

            _logger.LogDebug("Concurrent modification on service list, retrying add (attempt {Attempt})", attempt + 1);
        }

        _logger.LogWarning("Failed to add service {ServiceId} to list after {MaxAttempts} attempts",
            serviceId, _configuration.ServiceListRetryAttempts);
    }

    /// <summary>
    /// Remove a service ID from the service list index.
    /// Uses ETag-based optimistic concurrency per IMPLEMENTATION TENETS (Multi-Instance Safety).
    /// </summary>
    /// <param name="serviceId">The service ID to remove from the master list.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    private async Task RemoveFromServiceListAsync(Guid serviceId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-service", "GameServiceService.RemoveFromServiceList");

        for (var attempt = 0; attempt < _configuration.ServiceListRetryAttempts; attempt++)
        {
            var (serviceIds, etag) = await _listStore.GetWithETagAsync(SERVICE_LIST_KEY, cancellationToken);
            if (serviceIds == null || !serviceIds.Remove(serviceId))
            {
                return; // Not in list or already removed
            }

            // etag is null when the list key doesn't exist yet; empty string signals
            // "new entry" to TrySaveAsync (will never conflict on new entries)
            var result = await _listStore.TrySaveAsync(SERVICE_LIST_KEY, serviceIds, etag ?? string.Empty, cancellationToken: cancellationToken);
            if (result != null)
            {
                return;
            }

            _logger.LogDebug("Concurrent modification on service list, retrying remove (attempt {Attempt})", attempt + 1);
        }

        _logger.LogWarning("Failed to remove service {ServiceId} from list after {MaxAttempts} attempts",
            serviceId, _configuration.ServiceListRetryAttempts);
    }

    /// <summary>
    /// Map internal storage model to API response model.
    /// </summary>
    /// <param name="model">The internal storage model to convert.</param>
    /// <returns>A ServiceInfo response model with all fields mapped and timestamps converted from Unix.</returns>
    private static ServiceInfo MapToServiceInfo(GameServiceRegistryModel model)
    {
        return new ServiceInfo
        {
            ServiceId = model.ServiceId,
            StubName = model.StubName,
            DisplayName = model.DisplayName,
            Description = model.Description,
            IsActive = model.IsActive,
            AutoLobbyEnabled = model.AutoLobbyEnabled,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(model.CreatedAtUnix),
            UpdatedAt = DateTimeOffset.FromUnixTimeSeconds(model.UpdatedAtUnix ?? model.CreatedAtUnix)
        };
    }

    #endregion

    #region Lifecycle Event Publishing

    /// <summary>
    /// Publishes a GameServiceCreatedEvent when a new game service is registered.
    /// </summary>
    /// <param name="model">The created service model to include in the event.</param>
    private async Task PublishServiceCreatedEventAsync(GameServiceRegistryModel model)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-service", "GameServiceService.PublishServiceCreatedEvent");

        var eventModel = new GameServiceCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            GameServiceId = model.ServiceId,
            StubName = model.StubName,
            DisplayName = model.DisplayName,
            Description = model.Description,
            IsActive = model.IsActive,
            AutoLobbyEnabled = model.AutoLobbyEnabled,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(model.CreatedAtUnix),
            UpdatedAt = DateTimeOffset.FromUnixTimeSeconds(model.UpdatedAtUnix ?? model.CreatedAtUnix)
        };
        await _messageBus.PublishGameServiceCreatedAsync(eventModel);
    }

    /// <summary>
    /// Publishes a GameServiceUpdatedEvent when a game service is modified.
    /// </summary>
    /// <param name="model">The updated service model to include in the event.</param>
    /// <param name="changedFields">List of field names that were modified.</param>
    private async Task PublishServiceUpdatedEventAsync(GameServiceRegistryModel model, List<string> changedFields)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-service", "GameServiceService.PublishServiceUpdatedEvent");

        var eventModel = new GameServiceUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            GameServiceId = model.ServiceId,
            StubName = model.StubName,
            DisplayName = model.DisplayName,
            Description = model.Description,
            IsActive = model.IsActive,
            AutoLobbyEnabled = model.AutoLobbyEnabled,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(model.CreatedAtUnix),
            UpdatedAt = DateTimeOffset.FromUnixTimeSeconds(model.UpdatedAtUnix ?? model.CreatedAtUnix),
            ChangedFields = changedFields
        };
        await _messageBus.PublishGameServiceUpdatedAsync(eventModel);
    }

    /// <summary>
    /// Publishes a GameServiceDeletedEvent when a game service is removed.
    /// </summary>
    /// <param name="model">The deleted service model to include in the event.</param>
    /// <param name="reason">Optional reason for deletion provided by the caller.</param>
    private async Task PublishServiceDeletedEventAsync(GameServiceRegistryModel model, string? reason)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-service", "GameServiceService.PublishServiceDeletedEvent");

        var eventModel = new GameServiceDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            GameServiceId = model.ServiceId,
            StubName = model.StubName,
            DisplayName = model.DisplayName,
            Description = model.Description,
            IsActive = model.IsActive,
            AutoLobbyEnabled = model.AutoLobbyEnabled,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(model.CreatedAtUnix),
            UpdatedAt = DateTimeOffset.FromUnixTimeSeconds(model.UpdatedAtUnix ?? model.CreatedAtUnix),
            DeletedReason = reason
        };
        await _messageBus.PublishGameServiceDeletedAsync(eventModel);
    }

    #endregion
}
