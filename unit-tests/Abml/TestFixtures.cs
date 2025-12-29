// ═══════════════════════════════════════════════════════════════════════════
// ABML Test Fixtures Loader
// Loads YAML test fixtures from external files to avoid dotnet format corruption.
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.BannouService.UnitTests.Abml;

/// <summary>
/// Loads YAML test fixtures from the fixtures directory.
/// External YAML files are not affected by dotnet format, preserving indentation.
/// </summary>
public static class TestFixtures
{
    private static readonly string FixturesPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Abml", "fixtures");

    /// <summary>
    /// Loads a YAML fixture file by name.
    /// </summary>
    /// <param name="name">Fixture name without extension (e.g., "two_channels").</param>
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
    public static bool Exists(string name)
    {
        var path = Path.Combine(FixturesPath, $"{name}.yml");
        return File.Exists(path);
    }
}
