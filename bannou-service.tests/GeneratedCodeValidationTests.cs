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

    #endregion
}
