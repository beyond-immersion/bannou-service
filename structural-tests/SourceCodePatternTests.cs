using System.Text.RegularExpressions;
using BeyondImmersion.BannouService.Services;
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

    /// <summary>
    /// Validates that plugin files registering hosted services (AddHostedService calls)
    /// extend StandardServicePlugin&lt;T&gt; and override ConfigureServices. Hosted services
    /// registered in plugins that bypass StandardServicePlugin may lack proper lifecycle
    /// management (scoped service resolution, consistent start/running/shutdown phases).
    /// </summary>
    [Fact]
    public void Plugins_WithHostedServices_UseStandardServicePluginConfigureServices()
    {
        if (!Directory.Exists(PluginsDir))
            return;

        var violations = new List<string>();

        foreach (var pluginDir in Directory.GetDirectories(PluginsDir, "lib-*"))
        {
            var dirName = Path.GetFileName(pluginDir);
            if (dirName.EndsWith(".tests", StringComparison.Ordinal))
                continue;

            foreach (var pluginFile in Directory.GetFiles(pluginDir, "*Plugin.cs", SearchOption.TopDirectoryOnly))
            {
                var lines = File.ReadAllLines(pluginFile);
                var relativeToRepo = Path.GetRelativePath(TestAssemblyDiscovery.RepoRoot, pluginFile);

                bool hasHostedService = lines.Any(l =>
                    l.Contains("AddHostedService", StringComparison.Ordinal));

                if (!hasHostedService)
                    continue;

                bool extendsStandard = lines.Any(l =>
                    l.Contains(": StandardServicePlugin<", StringComparison.Ordinal));

                bool hasConfigureServicesOverride = lines.Any(l =>
                {
                    var trimmed = l.TrimStart();
                    return trimmed.Contains("override", StringComparison.Ordinal) &&
                            trimmed.Contains("ConfigureServices", StringComparison.Ordinal);
                });

                if (!extendsStandard)
                {
                    violations.Add(
                        $"{relativeToRepo}: registers hosted services but extends BaseBannouPlugin " +
                        $"directly — should extend StandardServicePlugin<T>");
                }
                else if (!hasConfigureServicesOverride)
                {
                    violations.Add(
                        $"{relativeToRepo}: registers hosted services but does not override " +
                        $"ConfigureServices on StandardServicePlugin<T>");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"{violations.Count} plugin(s) register hosted services without proper " +
            $"StandardServicePlugin<T> ConfigureServices override:\n" +
            string.Join("\n", violations.Select(v => $"  - {v}")));
    }

    // =========================================================================
    // Localization Category Registry Validation
    // =========================================================================

    /// <summary>
    /// Validates that every constant in <see cref="LocalizationCategoryDefinitions"/> has at least
    /// one declared consumer plugin in the schema (<c>schemas/localization-categories.yaml</c>).
    /// Categories without consumers are orphaned — no service validates keys against them.
    /// </summary>
    [Fact]
    public void LocalizationCategories_AreAllReferenced()
    {
        // Read the schema YAML and check that every category has non-empty consumers
        var schemaFile = Path.Combine(TestAssemblyDiscovery.RepoRoot, "schemas", "localization-categories.yaml");
        if (!File.Exists(schemaFile))
        {
            // Schema not yet created — skip gracefully
            return;
        }

        // Use the generated metadata which faithfully mirrors the schema
        var orphaned = new List<string>();
        foreach (var (code, metadata) in LocalizationCategoryDefinitions.Metadata)
        {
            if (metadata.Consumers.Length == 0)
            {
                orphaned.Add(code);
            }
        }

        Assert.True(
            orphaned.Count == 0,
            $"Found {orphaned.Count} localization category constant(s) with no declared consumers " +
            $"in schemas/localization-categories.yaml. Every category should declare at least one " +
            $"consumer plugin:\n" +
            string.Join("\n", orphaned.Select(c => $"  - LocalizationCategoryDefinitions.{ToPascalCase(c)} (\"{c}\")")));
    }

    /// <summary>
    /// Validates that every call to <c>ValidateLocalizationKeyAsync</c> in plugin source code
    /// passes a <see cref="LocalizationCategoryDefinitions"/> constant as the category argument,
    /// not a hardcoded string literal. Structural enforcement of the localization category registry.
    /// </summary>
    [Fact]
    public void LocalizationValidator_UsesGeneratedConstants()
    {
        // Pattern: ValidateLocalizationKeyAsync( followed by a string literal instead of a constant
        // Good: ValidateLocalizationKeyAsync(LocalizationCategoryDefinitions.Items, ...)
        // Bad:  ValidateLocalizationKeyAsync("items", ...)
        var callPattern = new Regex(
            @"ValidateLocalizationKeyAsync\s*\(",
            RegexOptions.Compiled);
        var stringLiteralArgPattern = new Regex(
            @"ValidateLocalizationKeyAsync\s*\(\s*""",
            RegexOptions.Compiled);

        var violations = new List<string>();

        foreach (var file in GetPluginSourceFiles())
        {
            var lines = File.ReadAllLines(file);
            var relativePath = Path.GetRelativePath(TestAssemblyDiscovery.RepoRoot, file);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();

                // Skip comments
                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                    continue;

                // Only inspect lines that call the validator
                if (!callPattern.IsMatch(line))
                    continue;

                // Flag if the first argument is a string literal
                if (stringLiteralArgPattern.IsMatch(line))
                {
                    violations.Add($"{relativePath}:{i + 1}: {trimmed.Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Found {violations.Count} call(s) to ValidateLocalizationKeyAsync with hardcoded " +
            $"string category argument. Use LocalizationCategoryDefinitions constants instead:\n" +
            string.Join("\n", violations.Select(v => $"  - {v}")));
    }

    /// <summary>
    /// Converts a kebab-case or snake_case string to PascalCase for display purposes.
    /// </summary>
    private static string ToPascalCase(string name) =>
        string.Concat(name.Split('-', '_').Select(p =>
            p.Length > 0 ? char.ToUpper(p[0]) + p[1..] : p));
}
