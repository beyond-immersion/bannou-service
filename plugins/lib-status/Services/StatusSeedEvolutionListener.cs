using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Status;

/// <summary>
/// DI Listener for seed evolution events to invalidate seed effects caches.
/// Registered as singleton in DI; discovered by Seed service via <c>IEnumerable&lt;ISeedEvolutionListener&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Distributed Safety:</b> This is a DI Listener (push pattern) with local-only fan-out.
/// Listener reactions write to distributed state (Redis cache deletion) so all nodes see
/// the invalidation on next read. The StatusServiceEvents.cs also subscribes to
/// <c>seed.capability.updated</c> via IMessageBus for guaranteed distributed delivery
/// across all nodes.
/// </para>
/// </remarks>
public class StatusSeedEvolutionListener : ISeedEvolutionListener
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly StatusServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<StatusSeedEvolutionListener> _logger;

    private IStateStore<SeedEffectsCacheModel>? _seedEffectsCacheStore;
    private IStateStore<SeedEffectsCacheModel> SeedEffectsCacheStore =>
        _seedEffectsCacheStore ??= _stateStoreFactory.GetStore<SeedEffectsCacheModel>(
            StateStoreDefinitions.StatusSeedEffectsCache);

    /// <summary>
    /// Empty set means interested in ALL seed types (wildcard).
    /// Status effects can come from any seed type.
    /// </summary>
    public IReadOnlySet<string> InterestedSeedTypes { get; } = new HashSet<string>();

    /// <summary>
    /// Initializes the seed evolution listener for status cache invalidation.
    /// </summary>
    /// <param name="stateStoreFactory">Factory for accessing state stores.</param>
    /// <param name="configuration">Status service configuration.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    /// <param name="logger">Logger instance.</param>
    public StatusSeedEvolutionListener(
        IStateStoreFactory stateStoreFactory,
        StatusServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider,
        ILogger<StatusSeedEvolutionListener> logger)
    {
        _stateStoreFactory = stateStoreFactory;
        _configuration = configuration;
        _telemetryProvider = telemetryProvider;
        _logger = logger;
    }

    /// <summary>
    /// No-op: growth recorded does not change capabilities, so no cache invalidation needed.
    /// </summary>
    public async Task OnGrowthRecordedAsync(SeedGrowthNotification notification, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.status", "StatusSeedEvolutionListener.OnGrowthRecordedAsync");
        await Task.CompletedTask;
    }

    /// <summary>
    /// Phase changes may unlock or change capabilities; invalidate seed effects cache.
    /// </summary>
    public async Task OnPhaseChangedAsync(SeedPhaseNotification notification, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.status", "StatusSeedEvolutionListener.OnPhaseChangedAsync");
        if (!_configuration.SeedEffectsEnabled)
        {
            return;
        }

        await InvalidateSeedEffectsCacheAsync(notification.OwnerId, notification.OwnerType);
    }

    /// <summary>
    /// Capability changes directly affect seed-derived effects; invalidate cache.
    /// </summary>
    public async Task OnCapabilitiesChangedAsync(SeedCapabilityNotification notification, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.status", "StatusSeedEvolutionListener.OnCapabilitiesChangedAsync");
        if (!_configuration.SeedEffectsEnabled)
        {
            return;
        }

        await InvalidateSeedEffectsCacheAsync(notification.OwnerId, notification.OwnerType);
    }

    private async Task InvalidateSeedEffectsCacheAsync(Guid ownerId, EntityType ownerType)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.status", "StatusSeedEvolutionListener.InvalidateSeedEffectsCacheAsync");
        // Use lowercase to match cache key format from StatusService.SeedEffectsCacheKey
        var ownerTypeStr = ownerType.ToString().ToLowerInvariant();
        var cacheKey = $"seed:{ownerId}:{ownerTypeStr}";
        try
        {
            await SeedEffectsCacheStore.DeleteAsync(cacheKey);
            _logger.LogDebug(
                "Invalidated seed effects cache for {OwnerType} {OwnerId} via DI listener",
                ownerType, ownerId);
        }
        catch (Exception ex)
        {
            // Cache invalidation failure is non-fatal; cache will expire via TTL
            _logger.LogWarning(ex,
                "Failed to invalidate seed effects cache for {OwnerType} {OwnerId}",
                ownerType, ownerId);
        }
    }
}
