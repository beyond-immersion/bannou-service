using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Seed;

/// <summary>
/// Static dispatch utility for invoking <see cref="ISeedEvolutionListener"/> callbacks.
/// Shared by SeedService and SeedDecayWorkerService to avoid dispatch logic duplication.
/// </summary>
/// <remarks>
/// Each dispatch method: early-returns if no listeners, filters by InterestedSeedTypes,
/// wraps each listener call in try-catch with warning log, and never rethrows.
/// </remarks>
internal static class SeedEvolutionDispatcher
{
    /// <summary>
    /// Dispatches growth recorded notifications to interested listeners.
    /// </summary>
    /// <param name="listeners">Registered evolution listeners.</param>
    /// <param name="seedTypeCode">Seed type code for filtering.</param>
    /// <param name="notification">Growth notification to dispatch.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    /// <param name="logger">Logger for warning on listener failure.</param>
    /// <param name="ct">Cancellation token.</param>
    internal static async Task DispatchGrowthRecordedAsync(
        IReadOnlyList<ISeedEvolutionListener> listeners,
        string seedTypeCode,
        SeedGrowthNotification notification,
        ITelemetryProvider telemetryProvider,
        ILogger logger,
        CancellationToken ct)
    {
        if (listeners.Count == 0) return;

        using var activity = telemetryProvider.StartActivity(
            "bannou.seed", "SeedEvolutionDispatcher.DispatchGrowthRecorded");

        foreach (var listener in listeners)
        {
            if (listener.InterestedSeedTypes.Count > 0 && !listener.InterestedSeedTypes.Contains(seedTypeCode))
                continue;

            try
            {
                await listener.OnGrowthRecordedAsync(notification, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Evolution listener {ListenerType} failed during OnGrowthRecordedAsync for seed {SeedId}",
                    listener.GetType().Name, notification.SeedId);
            }
        }
    }

    /// <summary>
    /// Dispatches phase changed notifications to interested listeners.
    /// </summary>
    /// <param name="listeners">Registered evolution listeners.</param>
    /// <param name="seedTypeCode">Seed type code for filtering.</param>
    /// <param name="notification">Phase change notification to dispatch.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    /// <param name="logger">Logger for warning on listener failure.</param>
    /// <param name="ct">Cancellation token.</param>
    internal static async Task DispatchPhaseChangedAsync(
        IReadOnlyList<ISeedEvolutionListener> listeners,
        string seedTypeCode,
        SeedPhaseNotification notification,
        ITelemetryProvider telemetryProvider,
        ILogger logger,
        CancellationToken ct)
    {
        if (listeners.Count == 0) return;

        using var activity = telemetryProvider.StartActivity(
            "bannou.seed", "SeedEvolutionDispatcher.DispatchPhaseChanged");

        foreach (var listener in listeners)
        {
            if (listener.InterestedSeedTypes.Count > 0 && !listener.InterestedSeedTypes.Contains(seedTypeCode))
                continue;

            try
            {
                await listener.OnPhaseChangedAsync(notification, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Evolution listener {ListenerType} failed during OnPhaseChangedAsync for seed {SeedId}",
                    listener.GetType().Name, notification.SeedId);
            }
        }
    }

    /// <summary>
    /// Dispatches capability changed notifications to interested listeners.
    /// </summary>
    /// <param name="listeners">Registered evolution listeners.</param>
    /// <param name="seedTypeCode">Seed type code for filtering.</param>
    /// <param name="notification">Capability change notification to dispatch.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    /// <param name="logger">Logger for warning on listener failure.</param>
    /// <param name="ct">Cancellation token.</param>
    internal static async Task DispatchCapabilitiesChangedAsync(
        IReadOnlyList<ISeedEvolutionListener> listeners,
        string seedTypeCode,
        SeedCapabilityNotification notification,
        ITelemetryProvider telemetryProvider,
        ILogger logger,
        CancellationToken ct)
    {
        if (listeners.Count == 0) return;

        using var activity = telemetryProvider.StartActivity(
            "bannou.seed", "SeedEvolutionDispatcher.DispatchCapabilitiesChanged");

        foreach (var listener in listeners)
        {
            if (listener.InterestedSeedTypes.Count > 0 && !listener.InterestedSeedTypes.Contains(seedTypeCode))
                continue;

            try
            {
                await listener.OnCapabilitiesChangedAsync(notification, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Evolution listener {ListenerType} failed during OnCapabilitiesChangedAsync for seed {SeedId}",
                    listener.GetType().Name, notification.SeedId);
            }
        }
    }
}
