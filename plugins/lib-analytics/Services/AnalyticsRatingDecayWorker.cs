using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Analytics;

/// <summary>
/// Background service that applies Glicko-2 rating period decay to inactive players.
/// Uses a Redis sorted set index (<c>rating-decay-tracker</c>) scored by <c>LastMatchAt</c>
/// to efficiently discover ratings eligible for decay. Self-limiting: removes entries
/// from the tracker when rating deviation reaches the configured maximum.
/// </summary>
/// <remarks>
/// <para>
/// The Glicko-2 algorithm specifies that rating deviation increases over time when a
/// player does not compete. This worker periodically finds inactive ratings, applies
/// the existing zero-games-played formula (which increases RD by volatility), and
/// publishes <c>analytics.rating.updated</c> with <c>MatchId = null</c> to distinguish
/// decay from match results (per FOUNDATION TENETS: background state changes publish
/// only lifecycle events, no action event).
/// </para>
/// </remarks>
[BannouHelperService("rating-decay", typeof(IAnalyticsService), lifetime: ServiceLifetime.Singleton, RegistrationMode = HelperRegistrationMode.HostedService)]
public class AnalyticsRatingDecayWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AnalyticsRatingDecayWorker> _logger;
    private readonly AnalyticsServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>Sorted set key for the decay tracker index on analytics-rating store.</summary>
    private const string RATING_DECAY_TRACKER_KEY = "rating-decay-tracker";

    /// <summary>Lock resource ID for preventing concurrent decay cycles.</summary>
    private const string DECAY_LOCK_RESOURCE = "rating-decay-cycle";

    // Glicko-2 scale conversion constant (same as in AnalyticsService)
    private const double GlickoScale = 173.7178;

    /// <summary>
    /// Initializes a new instance of the AnalyticsRatingDecayWorker.
    /// </summary>
    /// <param name="serviceProvider">Service provider for scope creation and error publishing.</param>
    /// <param name="logger">Logger for structured logging.</param>
    /// <param name="configuration">Analytics service configuration with decay tunables.</param>
    /// <param name="telemetryProvider">Telemetry provider for per-cycle span instrumentation.</param>
    public AnalyticsRatingDecayWorker(
        IServiceProvider serviceProvider,
        ILogger<AnalyticsRatingDecayWorker> logger,
        AnalyticsServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(
                TimeSpan.FromSeconds(_configuration.RatingDecayStartupDelaySeconds),
                stoppingToken);
        }
        catch (OperationCanceledException) { return; }

        _logger.LogInformation("{Worker} starting, interval: {Interval}s, inactivity threshold: {InactivityDays} days",
            nameof(AnalyticsRatingDecayWorker), _configuration.RatingDecayIntervalSeconds, _configuration.RatingDecayInactivityDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var activity = _telemetryProvider.StartActivity(
                    "bannou.analytics", "AnalyticsRatingDecayWorker.ProcessCycle");
                await ProcessCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Worker} cycle failed", nameof(AnalyticsRatingDecayWorker));
                await _serviceProvider.TryPublishWorkerErrorAsync(
                    "analytics", "RatingDecay", ex, _logger, stoppingToken);
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_configuration.RatingDecayIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("{Worker} stopped", nameof(AnalyticsRatingDecayWorker));
    }

    /// <summary>
    /// Executes a single decay cycle: finds inactive ratings via the sorted set index,
    /// applies the Glicko-2 no-games-played formula to increase RD, and publishes
    /// rating updated events.
    /// </summary>
    private async Task ProcessCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var lockProvider = scope.ServiceProvider.GetRequiredService<IDistributedLockProvider>();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var ratingStore = stateStoreFactory.GetStore<SkillRatingData>(StateStoreDefinitions.AnalyticsRating);
        var cacheableStore = stateStoreFactory.GetCacheableStore<SkillRatingData>(StateStoreDefinitions.AnalyticsRating);

        var cutoffMs = DateTimeOffset.UtcNow.AddDays(-_configuration.RatingDecayInactivityDays)
            .ToUnixTimeMilliseconds();

        // Query sorted set for entries scored below the cutoff (inactive longer than threshold)
        var entries = await cacheableStore.SortedSetRangeByScoreAsync(
            RATING_DECAY_TRACKER_KEY,
            0,
            cutoffMs,
            0,
            _configuration.RatingDecayBatchSize,
            false,
            cancellationToken);

        if (entries.Count == 0)
        {
            return;
        }

        // Acquire distributed lock to prevent concurrent decay from multiple nodes
        await using var lockResponse = await lockProvider.LockAsync(
            StateStoreDefinitions.AnalyticsRating,
            DECAY_LOCK_RESOURCE,
            Guid.NewGuid().ToString(),
            _configuration.RatingDecayLockExpirySeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogDebug("{Worker} skipping cycle — another node holds the decay lock",
                nameof(AnalyticsRatingDecayWorker));
            return;
        }

        var maxDeviation = _configuration.Glicko2DefaultDeviation;
        var baseRating = _configuration.Glicko2DefaultRating;
        var processedCount = 0;
        var skippedCount = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in entries)
        {
            try
            {
                var ratingKey = entry.member;
                var rating = await ratingStore.GetAsync(ratingKey, cancellationToken);

                if (rating == null)
                {
                    // Rating was deleted since the index was built — clean up orphaned entry
                    await cacheableStore.SortedSetRemoveAsync(
                        RATING_DECAY_TRACKER_KEY, ratingKey, cancellationToken);
                    skippedCount++;
                    continue;
                }

                // Already at max deviation — no further decay possible
                if (rating.RatingDeviation >= maxDeviation - 0.001)
                {
                    await cacheableStore.SortedSetRemoveAsync(
                        RATING_DECAY_TRACKER_KEY, ratingKey, cancellationToken);
                    skippedCount++;
                    continue;
                }

                // Apply Glicko-2 no-games-played formula: RD increases by volatility
                var mu = (rating.Rating - baseRating) / GlickoScale;
                var phi = rating.RatingDeviation / GlickoScale;
                var sigma = rating.Volatility;
                var newPhi = Math.Sqrt(phi * phi + sigma * sigma);
                var newRD = Math.Min(newPhi * GlickoScale, maxDeviation);

                var previousRD = rating.RatingDeviation;
                rating.RatingDeviation = newRD;

                await ratingStore.SaveAsync(ratingKey, rating, cancellationToken: cancellationToken);

                // Self-limiting: remove from tracker at max deviation
                if (newRD >= maxDeviation - 0.001)
                {
                    await cacheableStore.SortedSetRemoveAsync(
                        RATING_DECAY_TRACKER_KEY, ratingKey, cancellationToken);
                }
                else
                {
                    // Re-score to now — eligible for next cycle after another inactivity period
                    await cacheableStore.SortedSetAddAsync(
                        RATING_DECAY_TRACKER_KEY,
                        ratingKey,
                        now.ToUnixTimeMilliseconds(),
                        options: null,
                        cancellationToken: cancellationToken);
                }

                // Publish rating updated event with MatchId = null (background decay, not match)
                var ratingEvent = new AnalyticsRatingUpdatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    ServiceType = rating.ServiceType,
                    ServiceId = rating.ServiceId,
                    EntityId = rating.EntityId,
                    EntityType = rating.EntityType,
                    RatingType = rating.RatingType,
                    PreviousRating = rating.Rating,
                    NewRating = rating.Rating,
                    RatingChange = 0,
                    NewRatingDeviation = newRD,
                    MatchId = null
                };
                await messageBus.PublishAnalyticsRatingUpdatedAsync(ratingEvent, cancellationToken);

                processedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to apply decay to rating {RatingKey}, continuing", entry.member);
            }
        }

        if (processedCount > 0 || skippedCount > 0)
        {
            _logger.LogInformation("{Worker} decay cycle completed: {Processed} decayed, {Skipped} skipped",
                nameof(AnalyticsRatingDecayWorker), processedCount, skippedCount);
        }
    }
}
