using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.TestUtilities;
using System.Reflection;
using Xunit;

namespace BeyondImmersion.BannouService.StructuralTests;

/// <summary>
/// Auto-discovered structural validation tests for all Bannou services.
/// Services are discovered via [BannouService] attribute reflection — adding
/// a new plugin automatically includes it with zero opt-in required.
/// </summary>
public class StructuralTests
{
    /// <summary>
    /// Discovers all types with [BannouService] attribute across all loaded assemblies.
    /// Each discovered type becomes a row in Theory-based test methods.
    /// </summary>
    public static IEnumerable<object[]> AllServiceTypes()
    {
        // Force all plugin assemblies to load by touching a type from each
        // (project references alone don't guarantee assembly loading)
        EnsureAssembliesLoaded();

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
        {
            var assemblyName = assembly.GetName().Name;
            if (assemblyName == null ||
                assemblyName.StartsWith("System", StringComparison.Ordinal) ||
                assemblyName.StartsWith("Microsoft", StringComparison.Ordinal) ||
                assemblyName.StartsWith("netstandard", StringComparison.Ordinal) ||
                assemblyName.StartsWith("mscorlib", StringComparison.Ordinal) ||
                assemblyName.StartsWith("xunit", StringComparison.Ordinal) ||
                assemblyName.StartsWith("Moq", StringComparison.Ordinal) ||
                assemblyName.StartsWith("Castle", StringComparison.Ordinal))
            {
                continue;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException)
            {
                continue;
            }

            foreach (var type in types)
            {
                var attr = type.GetCustomAttribute<BannouServiceAttribute>();
                if (attr != null)
                {
                    yield return [type];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_HasValidConstructor(Type serviceType)
    {
        ServiceConstructorValidator.ValidateServiceConstructor(serviceType);
    }

    [Theory]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_RespectsHierarchy(Type serviceType)
    {
        ServiceHierarchyValidator.ValidateServiceHierarchy(serviceType);
    }

    [Theory]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_HasValidKeyBuilders(Type serviceType)
    {
        StateStoreKeyValidator.ValidateKeyBuilders(serviceType);
    }

    [Theory]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_ReferencesItsStateStores(Type serviceType)
    {
        StateStoreKeyValidator.ValidateStoreReferences(serviceType);
    }

    /// <summary>
    /// Validates that every generated Publish*Async extension method on the service's
    /// *EventPublisher class is called from somewhere in the plugin assembly.
    /// An uncalled method means a declared event topic is never published.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllServiceTypes))]
    public void Service_CallsAllGeneratedEventPublishers(Type serviceType)
    {
        var publisherInfo = EventPublishingValidator.GetEventPublisherInfo(serviceType);
        if (publisherInfo == null)
            return; // No generated publisher — service has no published events

        var assemblyPath = serviceType.Assembly.Location;
        if (string.IsNullOrEmpty(assemblyPath))
            return;

        var referencedMethods = AssemblyMetadataScanner.GetReferencedMethods(
            assemblyPath,
            publisherInfo.PublisherType.Name,
            publisherInfo.MethodNames);

        var uncalledMethods = publisherInfo.MethodNames
            .Where(m => !referencedMethods.Contains(m))
            .ToArray();

        Assert.True(
            uncalledMethods.Length == 0,
            $"{serviceType.Name}: {uncalledMethods.Length} generated event publisher method(s) " +
            $"on {publisherInfo.PublisherType.Name} are never called " +
            $"(declared events not published):\n" +
            string.Join("\n", uncalledMethods.Select(m => $"  - {m}")));
    }

    /// <summary>
    /// Validates that every plugin using EnumMapping helper methods (MapByName,
    /// MapByNameOrDefault, TryMapByName) has corresponding EnumMappingValidator
    /// tests in its test project. This is the safety guarantee documented in
    /// EnumMapping's XML docs — catching enum value drift at test time rather
    /// than at runtime.
    /// </summary>
    [Fact]
    public void PluginsUsingEnumMapping_MustHaveEnumMappingValidatorTests()
    {
        EnsureAssembliesLoaded();

        string[] enumMappingMethods = ["MapByName", "MapByNameOrDefault", "TryMapByName"];
        string[] validatorMethods =
        [
            "AssertFullCoverage", "AssertSubset", "AssertSupersetToSubsetMapping",
            "AssertSwitchCoversAllValues", "AssertSourceCoveredByTarget"
        ];

        var failures = new List<string>();

        var pluginAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a =>
            {
                var name = a.GetName().Name;
                return name != null
                    && name.StartsWith("lib-", StringComparison.Ordinal)
                    && !name.EndsWith(".tests", StringComparison.Ordinal);
            });

        foreach (var pluginAssembly in pluginAssemblies)
        {
            var pluginPath = pluginAssembly.Location;
            if (string.IsNullOrEmpty(pluginPath))
                continue;

            if (!AssemblyMetadataScanner.ReferencesMethodOnType(pluginPath, "EnumMapping", enumMappingMethods))
                continue;

            var pluginName = pluginAssembly.GetName().Name!;
            var serviceName = pluginName["lib-".Length..];
            var testAssemblyPath = TestAssemblyDiscovery.GetTestAssemblyPath(serviceName);

            if (testAssemblyPath == null)
            {
                failures.Add($"{pluginName}: test assembly not found (not built or missing)");
            }
            else if (!AssemblyMetadataScanner.ReferencesMethodOnType(
                testAssemblyPath, "EnumMappingValidator", validatorMethods))
            {
                failures.Add($"{pluginName}: uses EnumMapping but test project has no EnumMappingValidator tests");
            }
        }

        Assert.True(
            failures.Count == 0,
            $"Plugins using EnumMapping helpers must have corresponding EnumMappingValidator " +
            $"tests in their test project (per ENUM-BOUNDARIES.md):\n" +
            string.Join("\n", failures.Select(f => $"  - {f}")));
    }

    /// <summary>
    /// Ensures all plugin assemblies are loaded into the AppDomain.
    /// Project references guarantee compilation but not loading — we need
    /// to touch at least one type per assembly to force the CLR to load it.
    /// </summary>
    private static void EnsureAssembliesLoaded()
    {
        // Reference one type from each plugin assembly to force loading.
        // This list is maintained manually but only needs the assembly, not specific types.
        // If a plugin is missing, its tests simply won't run (safe failure mode).
        var assemblyAnchors = new[]
        {
            typeof(BannouService.Account.AccountService),
            typeof(BannouService.Achievement.AchievementService),
            typeof(BannouService.Actor.ActorService),
            typeof(BannouService.Analytics.AnalyticsService),
            typeof(BannouService.Asset.AssetService),
            typeof(BannouService.Auth.AuthService),
            typeof(BannouService.Behavior.BehaviorService),
            typeof(BannouService.Character.CharacterService),
            typeof(BannouService.CharacterEncounter.CharacterEncounterService),
            typeof(BannouService.CharacterHistory.CharacterHistoryService),
            typeof(BannouService.CharacterPersonality.CharacterPersonalityService),
            typeof(BannouService.Chat.ChatService),
            typeof(BannouService.Collection.CollectionService),
            typeof(BannouService.Connect.ConnectService),
            typeof(BannouService.Contract.ContractService),
            typeof(BannouService.Currency.CurrencyService),
            typeof(BannouService.Divine.DivineService),
            typeof(BannouService.Documentation.DocumentationService),
            typeof(BannouService.Escrow.EscrowService),
            typeof(BannouService.Faction.FactionService),
            typeof(BannouService.GameService.GameServiceService),
            typeof(BannouService.GameSession.GameSessionService),
            typeof(BannouService.Gardener.GardenerService),
            typeof(BannouService.Inventory.InventoryService),
            typeof(BannouService.Item.ItemService),
            typeof(BannouService.Leaderboard.LeaderboardService),
            typeof(BannouService.License.LicenseService),
            typeof(BannouService.Location.LocationService),
            typeof(BannouService.Mapping.MappingService),
            typeof(BannouService.Matchmaking.MatchmakingService),
            typeof(BannouService.Mesh.MeshService),
            typeof(BannouService.Messaging.MessagingService),
            typeof(BannouService.Music.MusicService),
            typeof(BannouService.Obligation.ObligationService),
            typeof(BannouService.Orchestrator.OrchestratorService),
            typeof(BannouService.Permission.PermissionService),
            typeof(BannouService.Puppetmaster.PuppetmasterService),
            typeof(BannouService.Quest.QuestService),
            typeof(BannouService.Realm.RealmService),
            typeof(BannouService.RealmHistory.RealmHistoryService),
            typeof(BannouService.Relationship.RelationshipService),
            typeof(BannouService.Resource.ResourceService),
            typeof(BannouService.SaveLoad.SaveLoadService),
            typeof(BannouService.Scene.SceneService),
            typeof(BannouService.Seed.SeedService),
            typeof(BannouService.Species.SpeciesService),
            typeof(BannouService.State.StateService),
            typeof(BannouService.Status.StatusService),
            typeof(BannouService.Storyline.StorylineService),
            typeof(BannouService.Subscription.SubscriptionService),
            typeof(BannouService.Telemetry.TelemetryService),
            typeof(BannouService.Transit.TransitService),
            typeof(BannouService.Voice.VoiceService),
            typeof(BannouService.Website.WebsiteService),
            typeof(BannouService.Worldstate.WorldstateService),
        };

        // Prevent the compiler from optimizing away the array
        _ = assemblyAnchors.Length;
    }
}
