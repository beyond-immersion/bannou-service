using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.Reflection;
using Xunit;

namespace BeyondImmersion.BannouService.StructuralTests;

/// <summary>
/// Centralized configuration default regression tests. Discovers all service
/// configuration classes via [ServiceConfiguration] attribute and validates
/// that every property's default value matches a pinned expectation.
///
/// Three tests:
/// 1. Pin test — every property in the pin registry matches its actual default
/// 2. Completeness test — every config property across all plugins is in the registry
/// 3. Per-plugin duplicate detector (informational) — finds plugin test projects
///    that still have their own config default tests, candidates for cleanup
///
/// Pinned values live in <see cref="DefaultConfigurationPins"/> (separate file)
/// to keep this test logic clean and the 1000+ pin entries maintainable.
/// </summary>
public class ConfigurationDefaultTests
{
    // =========================================================================
    // Test 1: Pin all config defaults
    // =========================================================================

    /// <summary>
    /// Validates that every pinned configuration default matches the actual default
    /// value from the generated configuration class. Fails when a schema change
    /// modifies a default value without updating the pin registry.
    /// </summary>
    [Fact]
    public void ConfigurationDefaults_MatchPinnedValues()
    {
        var configs = DiscoverAllConfigurations();
        var pins = DefaultConfigurationPins.All;
        var failures = new List<string>();

        foreach (var (configType, instance) in configs)
        {
            var properties = configType == typeof(BaseServiceConfiguration)
                ? GetBaseProperties()
                : GetDeclaredProperties(configType);

            foreach (var prop in properties)
            {
                var key = $"{configType.Name}.{prop.Name}";
                if (!pins.TryGetValue(key, out var expectedString))
                    continue; // Completeness test catches missing pins

                var actualValue = prop.GetValue(instance);
                var actualString = FormatValue(actualValue);

                if (expectedString != actualString)
                {
                    failures.Add(
                        $"{key}: pinned [{expectedString}] but actual [{actualString}]");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            $"Configuration default mismatches ({failures.Count}):\n" +
            string.Join("\n", failures.Select(f => $"  - {f}")));
    }

    // =========================================================================
    // Test 2: Verify completeness — every config property has a pin
    // =========================================================================

    /// <summary>
    /// Validates that every configuration property across all plugins is represented
    /// in the pin registry. Fails when a new config property is added to a schema
    /// without adding its expected default to <see cref="DefaultConfigurationPins"/>.
    /// </summary>
    [Fact]
    public void ConfigurationDefaults_AllPropertiesArePinned()
    {
        var configs = DiscoverAllConfigurations();
        var pins = DefaultConfigurationPins.All;
        var unpinned = new List<string>();

        foreach (var (configType, instance) in configs)
        {
            var properties = configType == typeof(BaseServiceConfiguration)
                ? GetBaseProperties()
                : GetDeclaredProperties(configType);

            foreach (var prop in properties)
            {
                var key = $"{configType.Name}.{prop.Name}";
                if (!pins.ContainsKey(key))
                {
                    var actualValue = prop.GetValue(instance);
                    unpinned.Add($"[\"{key}\"] = \"{FormatValue(actualValue)}\",");
                }
            }
        }

        Assert.True(
            unpinned.Count == 0,
            $"Configuration properties missing from DefaultConfigurationPins ({unpinned.Count}).\n" +
            "Add these to DefaultConfigurationPins.cs:\n" +
            string.Join("\n", unpinned.Select(u => $"  {u}")));
    }

    // =========================================================================
    // Test 3: Detect per-plugin duplicate config tests (informational)
    // =========================================================================

    /// <summary>
    /// Scans plugin test project source files for per-plugin configuration default
    /// tests that are now redundant with this centralized test. Produces a cleanup
    /// checklist. Informational — does not fail the build.
    /// </summary>
    [Fact]
    public void ConfigurationDefaults_DetectPerPluginDuplicates()
    {
        SkipUnless.InformationalTest(
            "Produces a checklist of per-plugin config default tests that can be removed");

        var repoRoot = FindRepoRoot();
        if (repoRoot == null)
        {
            Assert.Fail("Could not locate repository root");
            return;
        }

        var pluginTestDirs = Directory.GetDirectories(
            Path.Combine(repoRoot, "plugins"), "lib-*.tests");

        var duplicates = new List<string>();

        foreach (var testDir in pluginTestDirs)
        {
            var csFiles = Directory.GetFiles(testDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/"));

            foreach (var file in csFiles)
            {
                var content = File.ReadAllText(file);

                var hasCanBeInstantiated = content.Contains("CanBeInstantiated", StringComparison.Ordinal)
                    && content.Contains("Configuration", StringComparison.Ordinal);

                var hasDefaultAssertions = content.Contains("ServiceConfiguration()", StringComparison.Ordinal)
                    && content.Contains("Assert.Equal", StringComparison.Ordinal);

                if (hasCanBeInstantiated || hasDefaultAssertions)
                {
                    var relPath = Path.GetRelativePath(repoRoot, file);
                    var reason = hasCanBeInstantiated && hasDefaultAssertions
                        ? "CanBeInstantiated + default assertions"
                        : hasCanBeInstantiated
                            ? "CanBeInstantiated (redundant with structural test)"
                            : "config default assertions";
                    duplicates.Add($"{relPath} ({reason})");
                }
            }
        }

        if (duplicates.Count > 0)
        {
            Assert.Fail(
                $"Found {duplicates.Count} plugin test file(s) with per-plugin config tests " +
                $"that may be redundant with centralized ConfigurationDefaultTests:\n" +
                string.Join("\n", duplicates.Select(d => $"  - {d}")));
        }
    }

    // =========================================================================
    // Infrastructure
    // =========================================================================

    /// <summary>
    /// Discovers all configuration classes via [ServiceConfiguration] attribute.
    /// </summary>
    private static List<(Type ConfigType, object Instance)> DiscoverAllConfigurations()
    {
        EnsureAssembliesLoaded();

        var results = new List<(Type, object)>();
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
        {
            var name = assembly.GetName().Name;
            if (name == null ||
                name.StartsWith("System", StringComparison.Ordinal) ||
                name.StartsWith("Microsoft", StringComparison.Ordinal) ||
                name.StartsWith("netstandard", StringComparison.Ordinal) ||
                name.StartsWith("mscorlib", StringComparison.Ordinal) ||
                name.StartsWith("xunit", StringComparison.Ordinal) ||
                name.StartsWith("Moq", StringComparison.Ordinal) ||
                name.StartsWith("Castle", StringComparison.Ordinal))
                continue;

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException) { continue; }

            foreach (var type in types)
            {
                if (type.GetCustomAttribute<ServiceConfigurationAttribute>() == null) continue;
                if (type.IsAbstract) continue;

                var ctor = type.GetConstructor(Type.EmptyTypes);
                if (ctor == null) continue;

                var instance = Activator.CreateInstance(type)!;
                results.Add((type, instance));
            }
        }

        // Include BaseServiceConfiguration (inherited properties)
        var baseInstance = new BaseServiceConfiguration();
        results.Insert(0, (typeof(BaseServiceConfiguration), baseInstance));

        return results;
    }

    private static IEnumerable<PropertyInfo> GetDeclaredProperties(Type configType) =>
        configType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.CanRead);

    private static IEnumerable<PropertyInfo> GetBaseProperties() =>
        typeof(BaseServiceConfiguration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead);

    /// <summary>
    /// Formats a value as a stable string for comparison. All values are compared
    /// as strings to avoid type mismatch issues with enums, floats, etc.
    /// </summary>
    internal static string FormatValue(object? value) => value switch
    {
        null => "null",
        bool b => b ? "True" : "False",
        float f => f.ToString("G", System.Globalization.CultureInfo.InvariantCulture),
        double d => d.ToString("G", System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null"
    };

    private static string? FindRepoRoot()
    {
        var dir = Path.GetDirectoryName(typeof(ConfigurationDefaultTests).Assembly.Location);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "plugins")) &&
                Directory.Exists(Path.Combine(dir, "schemas")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    private static void EnsureAssembliesLoaded()
    {
        var outputDir = Path.GetDirectoryName(typeof(ConfigurationDefaultTests).Assembly.Location);
        if (outputDir == null) return;

        foreach (var dllPath in Directory.GetFiles(outputDir, "lib-*.dll"))
        {
            var fileName = Path.GetFileNameWithoutExtension(dllPath);
            if (fileName.EndsWith(".tests", StringComparison.Ordinal)) continue;

            try
            {
                var assemblyName = AssemblyName.GetAssemblyName(dllPath);
                var existing = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == assemblyName.Name);
                if (existing == null)
                    Assembly.LoadFrom(dllPath);
            }
            catch { /* Skip unloadable assemblies */ }
        }
    }
}
