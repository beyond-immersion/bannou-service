using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.State.Tests;

/// <summary>
/// Regression tests for StateStoreDefinitions constants.
///
/// ⚠️ IMPORTANT: These tests exist to prevent accidental changes to state store names.
///
/// State store names are NOT configuration - they are architectural identifiers that:
/// 1. Map to physical Redis key prefixes and MySQL table names
/// 2. Are used by production data that persists across deployments
/// 3. Cannot be changed without data migration planning
///
/// If a test here fails after a schema change:
/// - DO NOT simply update the expected value to make the test pass
/// - Instead, verify this is an intentional change with data migration implications
/// - Update the test ONLY after confirming the change is deliberate and migration-planned
///
/// The source of truth is schemas/state-stores.yaml - changes should be made there,
/// regenerated via `python3 scripts/generate-state-stores.py`, and then these tests
/// should be deliberately updated to match.
/// </summary>
public class StateStoreDefinitionsRegressionTests
{
    /// <summary>
    /// Validates that core service state store names haven't changed accidentally.
    /// These are the most critical stores - account, auth, character, etc.
    /// </summary>
    [Fact]
    public void CoreServiceStoreNames_ShouldMatchExpectedValues()
    {
        // Core identity/auth stores
        Assert.Equal("account-statestore", StateStoreDefinitions.Account);
        Assert.Equal("auth-statestore", StateStoreDefinitions.Auth);
        Assert.Equal("permission-statestore", StateStoreDefinitions.Permission);

        // Core connection/session stores
        Assert.Equal("connect-statestore", StateStoreDefinitions.Connect);

        // Core game entity stores
        Assert.Equal("character-statestore", StateStoreDefinitions.Character);
        Assert.Equal("realm-statestore", StateStoreDefinitions.Realm);
        Assert.Equal("location-statestore", StateStoreDefinitions.Location);
        Assert.Equal("species-statestore", StateStoreDefinitions.Species);
    }

    /// <summary>
    /// Validates that game session and service stores haven't changed.
    /// </summary>
    [Fact]
    public void GameServiceStoreNames_ShouldMatchExpectedValues()
    {
        Assert.Equal("game-service-statestore", StateStoreDefinitions.GameService);
        Assert.Equal("game-session-statestore", StateStoreDefinitions.GameSession);
        Assert.Equal("subscription-statestore", StateStoreDefinitions.Subscription);
        Assert.Equal("matchmaking-statestore", StateStoreDefinitions.Matchmaking);
    }

    /// <summary>
    /// Validates relationship and history stores haven't changed.
    /// </summary>
    [Fact]
    public void RelationshipAndHistoryStoreNames_ShouldMatchExpectedValues()
    {
        Assert.Equal("relationship-statestore", StateStoreDefinitions.Relationship);
        Assert.Equal("relationship-type-statestore", StateStoreDefinitions.RelationshipType);
        Assert.Equal("character-history-statestore", StateStoreDefinitions.CharacterHistory);
        Assert.Equal("character-personality-statestore", StateStoreDefinitions.CharacterPersonality);
        Assert.Equal("realm-history-statestore", StateStoreDefinitions.RealmHistory);
    }

    /// <summary>
    /// Validates infrastructure stores haven't changed.
    /// </summary>
    [Fact]
    public void InfrastructureStoreNames_ShouldMatchExpectedValues()
    {
        Assert.Equal("orchestrator-statestore", StateStoreDefinitions.Orchestrator);
        Assert.Equal("orchestrator-config", StateStoreDefinitions.OrchestratorConfig);
        Assert.Equal("orchestrator-heartbeats", StateStoreDefinitions.OrchestratorHeartbeats);
        Assert.Equal("orchestrator-routings", StateStoreDefinitions.OrchestratorRoutings);
        Assert.Equal("documentation-statestore", StateStoreDefinitions.Documentation);
        Assert.Equal("voice-statestore", StateStoreDefinitions.Voice);
        Assert.Equal("asset-statestore", StateStoreDefinitions.Asset);
    }

    /// <summary>
    /// Validates feature-specific stores haven't changed.
    /// </summary>
    [Fact]
    public void FeatureStoreNames_ShouldMatchExpectedValues()
    {
        // Leaderboards
        Assert.Equal("leaderboard-definition", StateStoreDefinitions.LeaderboardDefinition);
        Assert.Equal("leaderboard-ranking", StateStoreDefinitions.LeaderboardRanking);
        Assert.Equal("leaderboard-season", StateStoreDefinitions.LeaderboardSeason);

        // Achievements
        Assert.Equal("achievement-definition", StateStoreDefinitions.AchievementDefinition);
        Assert.Equal("achievement-progress", StateStoreDefinitions.AchievementProgress);

        // Analytics
        Assert.Equal("analytics-summary", StateStoreDefinitions.AnalyticsSummary);
        Assert.Equal("analytics-rating", StateStoreDefinitions.AnalyticsRating);
        Assert.Equal("analytics-history", StateStoreDefinitions.AnalyticsHistory);

        // Save/Load
        Assert.Equal("save-load-slots", StateStoreDefinitions.SaveLoadSlots);
        Assert.Equal("save-load-versions", StateStoreDefinitions.SaveLoadVersions);
        Assert.Equal("save-load-schemas", StateStoreDefinitions.SaveLoadSchemas);
        Assert.Equal("save-load-cache", StateStoreDefinitions.SaveLoadCache);
        Assert.Equal("save-load-pending", StateStoreDefinitions.SaveLoadPending);
    }

    /// <summary>
    /// Validates actor/behavior stores haven't changed.
    /// </summary>
    [Fact]
    public void ActorStoreNames_ShouldMatchExpectedValues()
    {
        Assert.Equal("actor-state", StateStoreDefinitions.ActorState);
        Assert.Equal("actor-templates", StateStoreDefinitions.ActorTemplates);
        Assert.Equal("actor-instances", StateStoreDefinitions.ActorInstances);
        Assert.Equal("actor-pool-nodes", StateStoreDefinitions.ActorPoolNodes);
        Assert.Equal("actor-assignments", StateStoreDefinitions.ActorAssignments);
        Assert.Equal("agent-memories", StateStoreDefinitions.AgentMemories);
    }

    /// <summary>
    /// Validates spatial and composition stores haven't changed.
    /// </summary>
    [Fact]
    public void SpatialStoreNames_ShouldMatchExpectedValues()
    {
        Assert.Equal("mapping-statestore", StateStoreDefinitions.Mapping);
        Assert.Equal("scene-statestore", StateStoreDefinitions.Scene);
    }

    /// <summary>
    /// Validates that the Configurations dictionary contains all defined stores.
    /// This ensures no store constant exists without a corresponding configuration.
    /// </summary>
    [Fact]
    public void Configurations_ShouldContainAllStores()
    {
        // Get all public const string fields from StateStoreDefinitions
        var storeConstants = typeof(StateStoreDefinitions)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string?)f.GetValue(null))
            .Where(v => v != null)
            .ToList();

        foreach (var storeName in storeConstants)
        {
            Assert.True(
                StateStoreDefinitions.Configurations.ContainsKey(storeName!),
                $"Store '{storeName}' has no configuration in StateStoreDefinitions.Configurations");
        }
    }

    /// <summary>
    /// Validates that all stores have metadata entries.
    /// </summary>
    [Fact]
    public void Metadata_ShouldContainAllStores()
    {
        var storeConstants = typeof(StateStoreDefinitions)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string?)f.GetValue(null))
            .Where(v => v != null)
            .ToList();

        foreach (var storeName in storeConstants)
        {
            Assert.True(
                StateStoreDefinitions.Metadata.ContainsKey(storeName!),
                $"Store '{storeName}' has no metadata in StateStoreDefinitions.Metadata");
        }
    }
}
