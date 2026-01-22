// =============================================================================
// Music Service Test Fixtures Loader
// Loads YAML test fixtures from external files to avoid editorconfig conflicts.
// =============================================================================

namespace BeyondImmersion.BannouService.Music.Tests;

/// <summary>
/// Loads YAML test fixtures from the fixtures directory.
/// External YAML files preserve their native 2-space indentation without
/// conflicting with C# editorconfig rules.
/// </summary>
public static class TestFixtures
{
    private static readonly string FixturesPath = Path.Combine(
        AppContext.BaseDirectory, "fixtures");

    /// <summary>
    /// Loads a YAML fixture file by name.
    /// </summary>
    /// <param name="name">Fixture name without extension (e.g., "style_loader_test").</param>
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
