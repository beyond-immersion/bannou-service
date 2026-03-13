using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Mesh.Services;

/// <summary>
/// Default implementation of <see cref="IServiceMappingReceiver"/> provided by Mesh (L0).
/// Updates the local <see cref="IServiceAppMappingResolver"/> and publishes
/// <c>mesh.mappings.updated</c> (L0→L0) events for cross-node sync.
/// </summary>
/// <remarks>
/// <para>
/// This class resolves a T27 (Cross-Service Communication Discipline) violation where
/// Mesh previously subscribed to <c>bannou.full-service-mappings</c> events published
/// by Orchestrator (L3). Now Orchestrator discovers this receiver via DI and pushes
/// mapping updates directly. The receiver broadcasts an L0 event so other mesh nodes
/// stay in sync.
/// </para>
/// <para>
/// <strong>DISTRIBUTED DEPLOYMENT NOTE</strong>: Only one Orchestrator exists in the
/// network. The DI call is local-only — it updates the co-located resolver. The
/// <c>mesh.mappings.updated</c> event propagates the update to all other nodes.
/// </para>
/// </remarks>
public class MeshServiceMappingReceiver : IServiceMappingReceiver
{
    private readonly IServiceAppMappingResolver _mappingResolver;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<MeshServiceMappingReceiver> _logger;
    private readonly MeshServiceConfiguration _configuration;
    private readonly IMeshInstanceIdentifier _instanceIdentifier;

    /// <summary>
    /// Initializes a new instance of the <see cref="MeshServiceMappingReceiver"/> class.
    /// </summary>
    /// <param name="mappingResolver">The shared service-to-appId mapping resolver.</param>
    /// <param name="messageBus">The message bus for publishing L0 sync events.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="configuration">The mesh service configuration.</param>
    /// <param name="instanceIdentifier">The mesh instance identifier for source tagging.</param>
    public MeshServiceMappingReceiver(
        IServiceAppMappingResolver mappingResolver,
        IMessageBus messageBus,
        ILogger<MeshServiceMappingReceiver> logger,
        MeshServiceConfiguration configuration,
        IMeshInstanceIdentifier instanceIdentifier)
    {
        _mappingResolver = mappingResolver;
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        _instanceIdentifier = instanceIdentifier;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateMappingsAsync(
        IReadOnlyDictionary<string, string> mappings,
        string defaultAppId,
        long version,
        CancellationToken ct = default)
    {
        using var activity = System.Diagnostics.Activity.Current;

        var updated = _mappingResolver.ReplaceAllMappings(mappings, defaultAppId, version);

        if (updated)
        {
            if (mappings.Count == 0)
            {
                _logger.LogInformation(
                    "Reset ServiceAppMappingResolver to default routing v{Version} (all services -> {DefaultAppId})",
                    version, defaultAppId);
            }
            else
            {
                _logger.LogInformation(
                    "Updated ServiceAppMappingResolver to v{Version} with {Count} mappings",
                    version, mappings.Count);

                var displayLimit = _configuration.MaxServiceMappingsDisplayed;
                foreach (var mapping in mappings.Take(displayLimit))
                {
                    _logger.LogDebug("  {Service} -> {AppId}", mapping.Key, mapping.Value);
                }
                if (mappings.Count > displayLimit)
                {
                    _logger.LogDebug("  ... and {Count} more", mappings.Count - displayLimit);
                }
            }

            // Broadcast L0 event so other mesh nodes update their resolvers
            var evt = new MeshMappingsUpdatedEvent
            {
                EventName = "mesh.mappings.updated",
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Mappings = mappings.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                DefaultAppId = defaultAppId,
                Version = version,
                SourceInstanceId = _instanceIdentifier.InstanceId,
                TotalServices = mappings.Count
            };

            await _messageBus.PublishMeshMappingsUpdatedAsync(evt, ct);
        }
        else
        {
            _logger.LogDebug(
                "Skipped stale mappings update v{Version} (current: v{Current})",
                version, _mappingResolver.CurrentVersion);
        }

        return updated;
    }
}
