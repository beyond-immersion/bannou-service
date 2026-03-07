using BeyondImmersion.Bannou.Core;
using System.Reflection;
using Xunit;

namespace BeyondImmersion.BannouService.Tests;

/// <summary>
/// Validates structural invariants of generated code across all plugins.
/// These tests catch template/generation regressions that would silently
/// break runtime infrastructure (controller discovery, event routing).
/// </summary>
public class GeneratedCodeValidationTests
{
    /// <summary>
    /// All concrete event classes in BeyondImmersion.BannouService.Events namespace
    /// (the generated service events from bannou-service/Generated/Events/).
    /// Excludes BaseServiceEvent itself and abstract classes.
    /// </summary>
    private static readonly Type[] AllServiceEventTypes = typeof(Program).Assembly
        .GetTypes()
        .Where(t =>
            t.Namespace == "BeyondImmersion.BannouService.Events"
            && t.IsClass
            && !t.IsAbstract
            && t.Name.EndsWith("Event")
            && t != typeof(BaseServiceEvent))
        .ToArray();

    /// <summary>
    /// All generated controller files across all plugins (plugins/lib-*/Generated/*Controller.cs).
    /// Excludes Meta.cs files which are partial class extensions, not the main controller.
    /// </summary>
    private static readonly string[] AllGeneratedControllerFiles = Directory
        .GetFiles(
            Path.Combine(FindRepositoryRoot(), "plugins"),
            "*Controller.cs",
            SearchOption.AllDirectories)
        .Where(f =>
            f.Contains(Path.Combine("Generated", "")) // Backslash-safe path check
            && !f.EndsWith("Controller.Meta.cs")
            && !f.EndsWith("EventsController.cs"))
        .ToArray();

    #region Service Event Validation

    /// <summary>
    /// Verifies that at least 200 service event types exist (we have 275+).
    /// Guards against empty generation, broken assembly scanning, or namespace changes
    /// that would make the other event tests vacuously pass on an empty set.
    /// </summary>
    [Fact]
    public void ServiceEvents_AtLeastTwoHundredEventTypesExist()
    {
        Assert.True(
            AllServiceEventTypes.Length >= 200,
            $"Expected at least 200 service event types (we have 275+), " +
            $"but found only {AllServiceEventTypes.Length}. " +
            $"Check that assembly scanning and namespace filtering are working correctly.");
    }

    /// <summary>
    /// Every service event class MUST inherit from BaseServiceEvent.
    /// BaseServiceEvent provides: IBannouEvent implementation, EventName for message tap
    /// forwarding, EventId for deduplication, and Timestamp for ordering.
    /// Without this inheritance, events are invisible to the generic event processing
    /// infrastructure (IMessageTap, InMemoryMessageTap).
    ///
    /// If this test fails, the event's schema is missing allOf with BaseServiceEvent.
    /// Fix the schema (not the generated code), then regenerate.
    /// </summary>
    [Fact]
    public void ServiceEvents_AllInheritFromBaseServiceEvent()
    {
        var violations = AllServiceEventTypes
            .Where(t => !typeof(BaseServiceEvent).IsAssignableFrom(t))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();

        // Group by service prefix for tasklist readability
        var grouped = violations
            .GroupBy(name => ExtractServicePrefix(name))
            .OrderBy(g => g.Key);

        var report = string.Join("\n", grouped.Select(g =>
            $"\n  [{g.Key}-events.yaml] ({g.Count()} events):\n" +
            string.Join("\n", g.Select(v => $"    - {v}"))));

        Assert.True(
            violations.Count == 0,
            $"Found {violations.Count} service event(s) that do NOT inherit from BaseServiceEvent.\n" +
            $"Fix the corresponding *-events.yaml schema to use allOf with BaseServiceEvent, then regenerate.\n" +
            report);
    }

    /// <summary>
    /// Every service event that inherits from BaseServiceEvent should have an EventName override
    /// with a non-empty default value. This is the event type identifier used for routing.
    /// </summary>
    [Fact]
    public void ServiceEvents_AllHaveEventNameOverride()
    {
        var violations = new List<(string ServicePrefix, string TypeName, string Reason)>();

        foreach (var eventType in AllServiceEventTypes.Where(t => typeof(BaseServiceEvent).IsAssignableFrom(t)))
        {
            var eventNameProp = eventType.GetProperty("EventName",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            if (eventNameProp == null)
            {
                violations.Add((ExtractServicePrefix(eventType.Name), eventType.Name, "no EventName override"));
                continue;
            }

            // Check that the default value is non-empty by instantiating
            try
            {
                var instance = (BaseServiceEvent)Activator.CreateInstance(eventType)!;
                if (string.IsNullOrEmpty(instance.EventName))
                    violations.Add((ExtractServicePrefix(eventType.Name), eventType.Name, "EventName is empty"));
            }
            catch
            {
                // Can't instantiate — skip default value check, the override exists
            }
        }

        var grouped = violations
            .GroupBy(v => v.ServicePrefix)
            .OrderBy(g => g.Key);

        var report = string.Join("\n", grouped.Select(g =>
            $"\n  [{g.Key}-events.yaml] ({g.Count()} events):\n" +
            string.Join("\n", g.Select(v => $"    - {v.TypeName} ({v.Reason})"))));

        Assert.True(
            violations.Count == 0,
            $"Found {violations.Count} service event(s) missing EventName override or with empty EventName.\n" +
            report);
    }

    #endregion

    #region Controller Validation

    /// <summary>
    /// Verifies that at least 50 generated controller files are found (we have 55+ plugins).
    /// Guards against path issues, broken glob patterns, or structural changes that would
    /// make the other controller tests vacuously pass on an empty set.
    /// </summary>
    [Fact]
    public void Controllers_AtLeastFiftyGeneratedControllersExist()
    {
        Assert.True(
            AllGeneratedControllerFiles.Length >= 50,
            $"Expected at least 50 generated controller files (we have 55+ plugins), " +
            $"but found only {AllGeneratedControllerFiles.Length}. " +
            $"Check that the file glob and repository root detection are working correctly.");
    }

    /// <summary>
    /// Every generated controller class MUST implement I{Service}Controller (which extends
    /// IBannouController). Without this, the controller is invisible to the IBannouController
    /// discovery system used for runtime controller enumeration and service-to-controller mapping.
    ///
    /// If this test fails, the Controller.liquid NSwag template is not emitting the interface
    /// implementation on the controller class declaration.
    /// </summary>
    [Fact]
    public void Controllers_AllImplementIBannouController()
    {
        var violations = new List<string>();

        foreach (var file in AllGeneratedControllerFiles)
        {
            var content = File.ReadAllText(file);

            // Every generated controller must have the I{Service}Controller interface declaration
            if (!content.Contains(": IBannouController"))
            {
                // Check if it implements via I{Service}Controller (which extends IBannouController)
                // Pattern: "class FooController ... , IFooController" or "class FooControllerBase ... , IFooController"
                var hasControllerInterface = System.Text.RegularExpressions.Regex.IsMatch(
                    content,
                    @"class \w+Controller(?:Base)?\s*:.*,\s*I\w+Controller\b");

                if (!hasControllerInterface)
                    violations.Add(Path.GetFileName(file));
            }
        }

        var report = string.Join("\n", violations.Select(v => $"  - {v}"));

        Assert.True(
            violations.Count == 0,
            $"Found {violations.Count} generated controller(s) that do NOT implement I{{Service}}Controller " +
            $"(IBannouController). These controllers are invisible to runtime discovery.\n" +
            $"Fix Controller.liquid template, then regenerate.\n\n" +
            report);
    }

    /// <summary>
    /// Every generated controller class MUST have the [BannouController] attribute linking it
    /// to its service interface. Without this, the IBannouController discovery system cannot
    /// map controllers to their services.
    ///
    /// If this test fails, the Controller.liquid NSwag template is not emitting the
    /// [BannouController(typeof(I{Service}Service))] attribute.
    /// </summary>
    [Fact]
    public void Controllers_AllHaveBannouControllerAttribute()
    {
        var violations = new List<string>();

        foreach (var file in AllGeneratedControllerFiles)
        {
            var content = File.ReadAllText(file);

            if (!content.Contains("[BeyondImmersion.BannouService.Attributes.BannouController(typeof(I"))
                violations.Add(Path.GetFileName(file));
        }

        var report = string.Join("\n", violations.Select(v => $"  - {v}"));

        Assert.True(
            violations.Count == 0,
            $"Found {violations.Count} generated controller(s) missing [BannouController] attribute.\n" +
            $"Fix Controller.liquid template, then regenerate.\n\n" +
            report);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Extracts a service prefix from an event type name for grouping.
    /// AccountCreatedEvent → account, GameSessionPlayerJoinedEvent → game-session, etc.
    /// Uses known service prefixes to handle multi-word service names correctly.
    /// </summary>
    private static string ExtractServicePrefix(string eventTypeName)
    {
        // Known multi-word service prefixes (longest first for greedy matching)
        string[] knownPrefixes =
        [
            "CharacterEncounter", "CharacterHistory", "CharacterPersonality",
            "GameSession", "GameService", "RealmHistory", "SaveLoad",
            "Orchestrator", "Documentation", "Matchmaking", "Leaderboard",
            "Achievement", "Relationship", "Subscription", "Worldstate",
            "Puppetmaster", "Obligation", "Collection", "Storyline",
            "Inventory", "Character", "Currency", "Behavior", "Gardener",
            "Location", "Contract", "Resource", "Mapping", "Transit",
            "Account", "Connect", "Faction", "License", "Escrow", "Divine",
            "Voice", "Asset", "Actor", "Quest", "Scene", "Realm", "Auth",
            "Chat", "Item", "Mesh", "Seed", "Goap", "Map", "Status",
        ];

        foreach (var prefix in knownPrefixes)
        {
            if (eventTypeName.StartsWith(prefix, StringComparison.Ordinal))
            {
                // Convert PascalCase prefix to kebab-case for schema file naming
                return System.Text.RegularExpressions.Regex
                    .Replace(prefix, "(?<!^)([A-Z])", "-$1")
                    .ToLowerInvariant();
            }
        }

        // Fallback: take first word (up to second capital letter)
        for (var i = 1; i < eventTypeName.Length; i++)
        {
            if (char.IsUpper(eventTypeName[i]))
                return eventTypeName[..i].ToLowerInvariant();
        }

        return eventTypeName.ToLowerInvariant();
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        // Fallback: walk up from assembly location
        dir = Path.GetDirectoryName(typeof(GeneratedCodeValidationTests).Assembly.Location);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "plugins")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException(
            "Could not find repository root. Expected .git directory or plugins/ directory in parent path.");
    }

    #endregion
}
