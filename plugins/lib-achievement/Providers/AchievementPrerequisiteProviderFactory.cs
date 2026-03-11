// =============================================================================
// Achievement Prerequisite Provider Factory
// Enables Quest (L2) to validate "achievement unlocked" prerequisites via the
// IPrerequisiteProviderFactory DI inversion pattern (SERVICE HIERARCHY).
// Registered with DI for Quest to consume via IEnumerable<IPrerequisiteProviderFactory>.
// =============================================================================

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Achievement.Providers;

/// <summary>
/// Factory for checking achievement-based quest prerequisites.
/// Registered with DI as IPrerequisiteProviderFactory for Quest to discover.
/// </summary>
/// <remarks>
/// <para>
/// Quest (L2) discovers this factory via <c>IEnumerable&lt;IPrerequisiteProviderFactory&gt;</c>
/// DI collection injection. When a quest definition has an achievement prerequisite,
/// Quest calls <see cref="CheckAsync"/> with:
/// </para>
/// <list type="bullet">
///   <item><c>characterId</c>: The character attempting to accept the quest</item>
///   <item><c>prerequisiteCode</c>: The achievement ID to check</item>
///   <item><c>parameters</c>: Must include <c>gameServiceId</c> (Guid); optionally <c>entityType</c> (EntityType, defaults to Character)</item>
/// </list>
/// </remarks>
[BannouHelperService("achievement-prerequisite-provider", typeof(IAchievementService), typeof(IPrerequisiteProviderFactory), lifetime: ServiceLifetime.Singleton)]
public sealed class AchievementPrerequisiteProviderFactory : IPrerequisiteProviderFactory
{
    private readonly IStateStore<EntityProgressData> _progressStore;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<AchievementPrerequisiteProviderFactory> _logger;

    /// <summary>
    /// Creates a new achievement prerequisite provider factory.
    /// </summary>
    public AchievementPrerequisiteProviderFactory(
        IStateStoreFactory stateStoreFactory,
        ITelemetryProvider telemetryProvider,
        ILogger<AchievementPrerequisiteProviderFactory> logger)
    {
        _progressStore = stateStoreFactory.GetStore<EntityProgressData>(StateStoreDefinitions.AchievementProgress);
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string ProviderName => "achievement";

    /// <inheritdoc/>
    public async Task<PrerequisiteResult> CheckAsync(
        Guid characterId,
        string prerequisiteCode,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.achievement", "AchievementPrerequisiteProviderFactory.CheckAsync");

        // Extract gameServiceId from parameters (required)
        if (!TryGetGuidParameter(parameters, "gameServiceId", out var gameServiceId))
        {
            _logger.LogWarning("Achievement prerequisite check missing required gameServiceId parameter");
            return PrerequisiteResult.Failure(
                "Achievement prerequisite requires gameServiceId parameter");
        }

        // Extract entityType (optional, defaults to Character)
        var entityType = EntityType.Character;
        if (parameters.TryGetValue("entityType", out var entityTypeObj) && entityTypeObj != null)
        {
            if (entityTypeObj is EntityType et)
            {
                entityType = et;
            }
            else
            {
                try
                {
                    entityType = BannouJson.Deserialize<EntityType>($"\"{entityTypeObj}\"");
                }
                catch
                {
                    // Value doesn't map to a valid EntityType, keep default (Character)
                }
            }
        }

        var progressKey = $"{gameServiceId}:{entityType}:{characterId}";
        var progress = await _progressStore.GetAsync(progressKey, ct);

        if (progress == null || !progress.Achievements.TryGetValue(prerequisiteCode, out var achievementProgress))
        {
            _logger.LogDebug(
                "Character {CharacterId} has no progress for achievement {AchievementId} in game {GameServiceId}",
                characterId, prerequisiteCode, gameServiceId);

            return PrerequisiteResult.Failure(
                $"Achievement '{prerequisiteCode}' not yet started",
                "Not started", "Unlocked");
        }

        if (!achievementProgress.IsUnlocked)
        {
            _logger.LogDebug(
                "Character {CharacterId} has not unlocked achievement {AchievementId} ({Current}/{Target})",
                characterId, prerequisiteCode, achievementProgress.CurrentProgress, achievementProgress.TargetProgress);

            return PrerequisiteResult.Failure(
                $"Achievement '{prerequisiteCode}' not yet unlocked",
                $"{achievementProgress.CurrentProgress}/{achievementProgress.TargetProgress}",
                "Unlocked");
        }

        return PrerequisiteResult.Success();
    }

    /// <summary>
    /// Attempts to extract a Guid parameter from the parameters dictionary.
    /// Handles both Guid and string representations.
    /// </summary>
    private static bool TryGetGuidParameter(
        IReadOnlyDictionary<string, object?> parameters,
        string key,
        out Guid value)
    {
        value = Guid.Empty;

        if (!parameters.TryGetValue(key, out var obj) || obj == null)
            return false;

        if (obj is Guid guid)
        {
            value = guid;
            return true;
        }

        return Guid.TryParse(obj.ToString(), out value);
    }
}
