// =============================================================================
// Runtime Test Fixtures Loader
// Loads YAML test fixtures for runtime interpreter tests.
// =============================================================================

namespace BeyondImmersion.BannouService.Behavior.Tests.Runtime;

/// <summary>
/// Loads YAML test fixtures from the Runtime fixtures directory.
/// </summary>
public static class RuntimeTestFixtures
{
    private static readonly string FixturesPath = Path.Combine(
        AppContext.BaseDirectory, "Runtime", "fixtures");

    /// <summary>
    /// Loads a YAML fixture file by name.
    /// </summary>
    /// <param name="name">Fixture name without extension (e.g., "cinematic_base").</param>
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
}
