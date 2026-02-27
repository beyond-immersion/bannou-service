using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Achievement;

/// <summary>
/// Background service that periodically recalculates achievement rarity percentages.
/// Uses RarityCalculationIntervalMinutes from configuration to determine recalculation frequency.
/// </summary>
public class RarityCalculationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RarityCalculationService> _logger;
    private readonly AchievementServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    private const string DEFINITION_INDEX_PREFIX = "achievement-definitions";
    private const string GAME_SERVICE_INDEX_KEY = "achievement-game-services";

    /// <summary>
    /// Interval between rarity recalculations, from configuration.
    /// </summary>
    private TimeSpan CalculationInterval => TimeSpan.FromMinutes(_configuration.RarityCalculationIntervalMinutes);

    /// <summary>
    /// Initializes a new instance of the RarityCalculationService.
    /// </summary>
    /// <param name="serviceProvider">Service provider for creating scopes.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="configuration">Achievement service configuration.</param>
    public RarityCalculationService(
        IServiceProvider serviceProvider,
        ILogger<RarityCalculationService> logger,
        AchievementServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
    }

    /// <summary>
    /// Executes the periodic rarity recalculation loop.
    /// </summary>
    /// <param name="stoppingToken">Token to signal shutdown.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.achievement", "RarityCalculationService.ExecuteAsync");
        _logger.LogInformation(
            "Rarity calculation service starting, interval: {IntervalMinutes} minutes",
            _configuration.RarityCalculationIntervalMinutes);

        // Wait before first calculation to allow services to start
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_configuration.RarityCalculationStartupDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Rarity calculation service cancelled during startup");
            return;
        }

        using var timer = new PeriodicTimer(CalculationInterval);

        // Run first calculation immediately after startup delay
        await RecalculateRarityAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var timerFired = await timer.WaitForNextTickAsync(stoppingToken);
                if (!timerFired)
                {
                    break;
                }

                await RecalculateRarityAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during rarity recalculation cycle");
            }
        }

        _logger.LogInformation("Rarity calculation service stopped");
    }

    /// <summary>
    /// Recalculates rarity percentages for all achievement definitions across all game services.
    /// </summary>
    private async Task RecalculateRarityAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.achievement", "RarityCalculationService.RecalculateRarityAsync");
        _logger.LogDebug("Starting rarity recalculation");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
            var definitionStore = stateStoreFactory.GetCacheableStore<AchievementDefinitionData>(StateStoreDefinitions.AchievementDefinition);

            // Get all known game service IDs from the index set
            var gameServiceIds = await definitionStore.GetSetAsync<string>(GAME_SERVICE_INDEX_KEY, cancellationToken);
            var updatedCount = 0;

            foreach (var gameServiceIdStr in gameServiceIds)
            {
                if (!Guid.TryParse(gameServiceIdStr, out var gameServiceId))
                {
                    continue;
                }

                var indexKey = $"{DEFINITION_INDEX_PREFIX}:{gameServiceId}";
                var achievementIds = await definitionStore.GetSetAsync<string>(indexKey, cancellationToken);

                foreach (var achievementId in achievementIds)
                {
                    var defKey = $"{gameServiceId}:{achievementId}";
                    var (definition, etag) = await definitionStore.GetWithETagAsync(defKey, cancellationToken);
                    if (definition == null)
                    {
                        continue;
                    }

                    if (definition.TotalEligibleEntities > 0 && definition.EarnedCount >= 0)
                    {
                        var rarityPercent = (double)definition.EarnedCount / definition.TotalEligibleEntities * 100.0;
                        definition.RarityPercent = rarityPercent;
                        definition.RarityCalculatedAt = DateTimeOffset.UtcNow;

                        // GetWithETagAsync always returns a non-null etag for existing records;
                        // coalesce satisfies compiler's nullable analysis (will never execute)
                        var savedEtag = await definitionStore.TrySaveAsync(defKey, definition, etag ?? string.Empty, cancellationToken);
                        if (savedEtag != null)
                        {
                            updatedCount++;
                        }
                        else
                        {
                            _logger.LogDebug(
                                "Rarity update skipped for {DefKey} due to concurrent modification",
                                defKey);
                        }
                    }
                }
            }

            _logger.LogDebug(
                "Rarity recalculation complete, updated {UpdatedCount} definitions",
                updatedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recalculate achievement rarity percentages");
        }
    }
}
