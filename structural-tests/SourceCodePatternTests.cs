using System.Text.RegularExpressions;
using Xunit;

namespace BeyondImmersion.BannouService.StructuralTests;

/// <summary>
/// Validates source code patterns across all plugin assemblies by scanning .cs files
/// on disk. These tests catch prohibited patterns that compile successfully but violate
/// CLAUDE.md mandatory rules or development tenets.
/// </summary>
public class SourceCodePatternTests
{
    private static readonly string PluginsDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "plugins");

    /// <summary>
    /// Gets all non-generated, non-test .cs files across all plugin directories.
    /// Excludes Generated/ directories and .tests projects.
    /// </summary>
    private static IEnumerable<string> GetPluginSourceFiles()
    {
        if (!Directory.Exists(PluginsDir))
            yield break;

        foreach (var pluginDir in Directory.GetDirectories(PluginsDir, "lib-*"))
        {
            var dirName = Path.GetFileName(pluginDir);

            // Skip test projects
            if (dirName.EndsWith(".tests", StringComparison.Ordinal))
                continue;

            foreach (var file in Directory.EnumerateFiles(pluginDir, "*.cs", SearchOption.AllDirectories))
            {
                // Skip Generated/ directories
                var relativePath = Path.GetRelativePath(pluginDir, file);
                if (relativePath.StartsWith("Generated", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip bin/obj
                if (relativePath.StartsWith("bin", StringComparison.OrdinalIgnoreCase) ||
                    relativePath.StartsWith("obj", StringComparison.OrdinalIgnoreCase))
                    continue;

                yield return file;
            }
        }
    }

    /// <summary>
    /// Validates that no plugin source file uses null-forgiving operators (null!, default!)
    /// or unsafe null casts ((Type)null). These patterns cause segmentation faults and
    /// hide null reference exceptions per CLAUDE.md mandatory rules.
    /// </summary>
    [Fact]
    public void Plugins_DoNotUseNullForgivingOperator()
    {
        // Patterns that are always prohibited:
        // - null!  (null-forgiving on null literal)
        // - default!  (null-forgiving on default)
        // These are unambiguous — there's no valid use case for them.
        var nullForgivingPattern = new Regex(@"\bnull!\b|\bdefault!\b", RegexOptions.Compiled);

        // (SomeType)null cast pattern — matches (Type)null but not (Type?)null
        var nullCastPattern = new Regex(@"\(\s*[A-Z]\w+\s*\)\s*null\b", RegexOptions.Compiled);

        var violations = new List<string>();

        foreach (var file in GetPluginSourceFiles())
        {
            var lines = File.ReadAllLines(file);
            var relativePath = Path.GetRelativePath(TestAssemblyDiscovery.RepoRoot, file);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();

                // Skip single-line comments and XML doc comments
                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                    continue;

                if (nullForgivingPattern.IsMatch(line))
                {
                    violations.Add($"{relativePath}:{i + 1}: {trimmed.Trim()}");
                }
                else if (nullCastPattern.IsMatch(line))
                {
                    violations.Add($"{relativePath}:{i + 1}: {trimmed.Trim()}");
                }
            }
        }

        var grouped = violations
            .GroupBy(v => v.Split('/')[1]) // Group by plugin directory (plugins/lib-xxx/...)
            .OrderBy(g => g.Key);

        var report = string.Join("\n", grouped.Select(g =>
            $"\n  [{g.Key}] ({g.Count()} violation(s)):\n" +
            string.Join("\n", g.Select(v => $"    - {v}"))));

        Assert.True(
            violations.Count == 0,
            $"Found {violations.Count} null-forgiving operator / unsafe null cast violation(s) " +
            $"across {grouped.Count()} plugin(s):{report}");
    }
}
