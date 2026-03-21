using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Mesh.Services;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Mesh;

// =============================================================================
// MeshService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by MeshService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (MeshService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IMeshService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (MeshService.Helpers.cs):
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
/// Private and internal helper methods for MeshService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class MeshService
{
    #region Event Publishing

    private async Task PublishEndpointRegisteredEventAsync(
        MeshEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity(TelemetryComponents.Mesh, "mesh.publish_registered", ActivityKind.Internal);

            var evt = new MeshEndpointRegisteredEvent
            {
                EventName = "mesh.endpoint_registered",
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                InstanceId = endpoint.InstanceId,
                AppId = endpoint.AppId,
                Host = endpoint.Host,
                Port = endpoint.Port,
                Services = endpoint.Services
            };

            await _messageBus.PublishMeshEndpointRegisteredAsync(evt, cancellationToken);

            _logger.LogDebug("Published endpoint registered event for {InstanceId}", endpoint.InstanceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish endpoint registered event");
            await _messageBus.TryPublishErrorAsync(
                "mesh", "PublishEndpointRegistered", ex.GetType().Name, ex.Message,
                severity: ServiceErrorEventSeverity.Warning,
                cancellationToken: cancellationToken);
        }
    }

    private async Task PublishEndpointDeregisteredEventAsync(
        Guid instanceId,
        string appId,
        DeregistrationReason reason,
        CancellationToken cancellationToken)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity(TelemetryComponents.Mesh, "mesh.publish_deregistered", ActivityKind.Internal);

            var evt = new MeshEndpointDeregisteredEvent
            {
                EventName = "mesh.endpoint_deregistered",
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                InstanceId = instanceId,
                AppId = appId,
                Reason = reason
            };

            await _messageBus.PublishMeshEndpointDeregisteredAsync(evt, cancellationToken);

            _logger.LogDebug("Published endpoint deregistered event for {InstanceId}", instanceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish endpoint deregistered event");
            await _messageBus.TryPublishErrorAsync(
                "mesh", "PublishEndpointDeregistered", ex.GetType().Name, ex.Message,
                severity: ServiceErrorEventSeverity.Warning,
                cancellationToken: cancellationToken);
        }
    }

    #endregion
}
