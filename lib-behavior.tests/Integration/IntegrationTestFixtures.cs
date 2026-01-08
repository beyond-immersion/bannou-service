// =============================================================================
// Integration Test Fixtures Loader
// Loads YAML test fixtures for integration tests.
// =============================================================================

namespace BeyondImmersion.BannouService.Behavior.Tests.Integration;

/// <summary>
/// Loads YAML test fixtures from the Integration fixtures directory.
/// External YAML files are not affected by dotnet format, preserving indentation.
/// </summary>
public static class IntegrationTestFixtures
{
    private static readonly string FixturesPath = Path.Combine(
        AppContext.BaseDirectory, "Integration", "fixtures");

    /// <summary>
    /// Loads a YAML fixture file by name.
    /// </summary>
    /// <param name="name">Fixture name without extension (e.g., "dialogue_greet_localized").</param>
    /// <returns>The YAML content.</returns>
    public static string Load(string name)
    {
        var path = Path.Combine(FixturesPath, $"{name}.yml");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Test fixture not found: {path}");
        }
        return File.ReadAllText(path);
    }

    /// <summary>
    /// Checks if a fixture exists.
    /// </summary>
    /// <param name="name">Fixture name without extension.</param>
    /// <returns>True if the fixture file exists.</returns>
    public static bool Exists(string name)
    {
        var path = Path.Combine(FixturesPath, $"{name}.yml");
        return File.Exists(path);
    }

    /// <summary>
    /// Gets the full path to the fixtures directory.
    /// </summary>
    /// <returns>The fixtures directory path.</returns>
    public static string GetFixturesDirectory()
    {
        return FixturesPath;
    }
}
