using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.CharacterEncounter;

/// <summary>
/// Background service that periodically processes memory decay when MemoryDecayMode is set to Scheduled.
/// Iterates through all characters with encounter perspectives and applies time-based memory strength decay.
/// When perspectives cross the fade threshold, publishes encounter.memory.faded events.
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPLEMENTATION TENETS - Background Service Pattern:</b>
/// Uses IServiceProvider.CreateScope() to access scoped services (IMessageBus, IStateStoreFactory).
/// Follows established patterns from SubscriptionExpirationService, EscrowExpirationService.
/// </para>
/// <para>
/// The scheduler only runs when MemoryDecayMode is Scheduled. When mode is Lazy, decay is applied
/// on-demand during read operations (QueryByCharacter, GetPerspective, etc.).
/// </para>
/// </remarks>
public class MemoryDecaySchedulerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MemoryDecaySchedulerService> _logger;
    private readonly CharacterEncounterServiceConfiguration _configuration;

    private const string ENCOUNTER_MEMORY_FADED_TOPIC = "encounter.memory.faded";
    private const string PERSPECTIVE_KEY_PREFIX = "pers-";
    private const string GLOBAL_CHAR_INDEX_KEY = "global-char-idx";
    private const string CHAR_INDEX_PREFIX = "char-idx-";

    /// <summary>
    /// Interval between decay checks, from configuration.
    /// </summary>
    private TimeSpan CheckInterval => TimeSpan.FromMinutes(_configuration.ScheduledDecayCheckIntervalMinutes);

    /// <summary>
    /// Startup delay before first check, from configuration.
    /// </summary>
    private TimeSpan StartupDelay => TimeSpan.FromSeconds(_configuration.ScheduledDecayStartupDelaySeconds);

    /// <summary>
    /// Initializes the memory decay scheduler with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for creating scopes to access scoped services.</param>
    /// <param name="logger">Logger for structured logging.</param>
    /// <param name="configuration">Service configuration with decay settings.</param>
    public MemoryDecaySchedulerService(
        IServiceProvider serviceProvider,
        ILogger<MemoryDecaySchedulerService> logger,
        CharacterEncounterServiceConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Main execution loop for the background service.
    /// Only processes decay when MemoryDecayEnabled is true and MemoryDecayMode is Scheduled.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token for graceful shutdown.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Skip startup entirely if not in scheduled mode
        if (!_configuration.MemoryDecayEnabled || _configuration.MemoryDecayMode != MemoryDecayMode.Scheduled)
        {
            _logger.LogInformation(
                "Memory decay scheduler not starting: Enabled={Enabled}, Mode={Mode}",
                _configuration.MemoryDecayEnabled,
                _configuration.MemoryDecayMode);
            return;
        }

        _logger.LogInformation(
            "Memory decay scheduler starting, check interval: {Interval}",
            CheckInterval);

        // Wait before first check to allow other services to start
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Memory decay scheduler cancelled during startup");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessScheduledDecayAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during scheduled memory decay processing");
                try
                {
                    using var errorScope = _serviceProvider.CreateScope();
                    var messageBus = errorScope.ServiceProvider.GetRequiredService<IMessageBus>();
                    await messageBus.TryPublishErrorAsync(
                        "character-encounter",
                        "ScheduledMemoryDecay",
                        ex.GetType().Name,
                        ex.Message,
                        severity: BeyondImmersion.BannouService.Events.ServiceErrorEventSeverity.Error);
                }
                catch (Exception pubEx)
                {
                    // Don't let error publishing failures affect the loop
                    _logger.LogDebug(pubEx, "Failed to publish error event - continuing decay loop");
                }
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Memory decay scheduler stopped");
    }

    /// <summary>
    /// Processes memory decay for all characters in batches.
    /// Uses global character index to iterate through all characters with encounter data.
    /// </summary>
    private async Task ProcessScheduledDecayAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting scheduled memory decay processing");

        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        // Get all character IDs from global index
        var globalIndexStore = stateStoreFactory.GetStore<GlobalCharacterIndexData>(StateStoreDefinitions.CharacterEncounter);
        var globalIndex = await globalIndexStore.GetAsync(GLOBAL_CHAR_INDEX_KEY, cancellationToken);

        if (globalIndex == null || globalIndex.CharacterIds.Count == 0)
        {
            _logger.LogDebug("No characters with encounter data to process");
            return;
        }

        var perspectiveStore = stateStoreFactory.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter);
        var indexStore = stateStoreFactory.GetStore<CharacterIndexData>(StateStoreDefinitions.CharacterEncounter);

        var totalProcessed = 0;
        var totalFaded = 0;
        var charactersProcessed = 0;

        foreach (var characterId in globalIndex.CharacterIds)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                var (processed, faded) = await ProcessCharacterDecayAsync(
                    characterId,
                    indexStore,
                    perspectiveStore,
                    messageBus,
                    cancellationToken);

                totalProcessed += processed;
                totalFaded += faded;
                charactersProcessed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process decay for character {CharacterId}", characterId);
            }
        }

        _logger.LogInformation(
            "Scheduled decay complete: characters={Characters}, perspectivesProcessed={Processed}, memoriesFaded={Faded}",
            charactersProcessed,
            totalProcessed,
            totalFaded);
    }

    /// <summary>
    /// Processes memory decay for a single character's perspectives.
    /// </summary>
    /// <returns>Tuple of (perspectives processed, memories faded).</returns>
    private async Task<(int Processed, int Faded)> ProcessCharacterDecayAsync(
        Guid characterId,
        IStateStore<CharacterIndexData> indexStore,
        IStateStore<PerspectiveData> perspectiveStore,
        IMessageBus messageBus,
        CancellationToken cancellationToken)
    {
        var characterIndex = await indexStore.GetAsync($"{CHAR_INDEX_PREFIX}{characterId}", cancellationToken);
        if (characterIndex == null || characterIndex.PerspectiveIds.Count == 0)
        {
            return (0, 0);
        }

        var processed = 0;
        var faded = 0;

        foreach (var perspectiveId in characterIndex.PerspectiveIds)
        {
            var perspectiveKey = $"{PERSPECTIVE_KEY_PREFIX}{perspectiveId}";
            var (perspective, etag) = await perspectiveStore.GetWithETagAsync(perspectiveKey, cancellationToken);

            if (perspective == null)
                continue;

            var (needsDecay, willFade) = CalculateDecay(perspective);
            if (!needsDecay)
                continue;

            processed++;
            var decayAmount = GetDecayAmount(perspective);
            var previousStrength = perspective.MemoryStrength;

            perspective.MemoryStrength = (float)Math.Max(0, previousStrength - decayAmount);
            perspective.LastDecayedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var saveResult = await perspectiveStore.TrySaveAsync(perspectiveKey, perspective, etag ?? string.Empty, cancellationToken);
            if (saveResult == null)
            {
                _logger.LogDebug(
                    "Concurrent modification during scheduled decay for perspective {PerspectiveId}, skipping",
                    perspectiveId);
                continue;
            }

            if (willFade)
            {
                faded++;
                await messageBus.TryPublishAsync(ENCOUNTER_MEMORY_FADED_TOPIC, new EncounterMemoryFadedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    EncounterId = perspective.EncounterId,
                    CharacterId = perspective.CharacterId,
                    PerspectiveId = perspective.PerspectiveId,
                    PreviousStrength = (float)previousStrength,
                    NewStrength = (float)perspective.MemoryStrength,
                    FadeThreshold = (float)_configuration.MemoryFadeThreshold
                }, cancellationToken: cancellationToken);
            }
        }

        return (processed, faded);
    }

    /// <summary>
    /// Calculates whether a perspective needs decay and if it will fade below threshold.
    /// </summary>
    /// <returns>Tuple of (needs decay, will fade).</returns>
    private (bool NeedsDecay, bool WillFade) CalculateDecay(PerspectiveData perspective)
    {
        var baseTime = perspective.LastDecayedAtUnix ?? perspective.CreatedAtUnix;
        var hoursSince = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - baseTime) / 3600.0;
        var intervals = hoursSince / _configuration.MemoryDecayIntervalHours;

        if (intervals < 1)
            return (false, false);

        var decayAmount = intervals * _configuration.MemoryDecayRate;
        var newStrength = perspective.MemoryStrength - decayAmount;
        var wasBelowThreshold = perspective.MemoryStrength <= _configuration.MemoryFadeThreshold;
        var willBeBelowThreshold = newStrength <= _configuration.MemoryFadeThreshold;
        var willFade = !wasBelowThreshold && willBeBelowThreshold;

        return (true, willFade);
    }

    /// <summary>
    /// Calculates the decay amount for a perspective based on time elapsed.
    /// </summary>
    private double GetDecayAmount(PerspectiveData perspective)
    {
        var baseTime = perspective.LastDecayedAtUnix ?? perspective.CreatedAtUnix;
        var hoursSince = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - baseTime) / 3600.0;
        var intervals = hoursSince / _configuration.MemoryDecayIntervalHours;
        return intervals * _configuration.MemoryDecayRate;
    }
}
