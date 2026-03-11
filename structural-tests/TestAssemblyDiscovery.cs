using System.Reflection;

namespace BeyondImmersion.BannouService.StructuralTests;

// ⛔ FROZEN FILE — DO NOT MODIFY WITHOUT EXPLICIT USER PERMISSION ⛔
// Structural test infrastructure. Changes affect assembly discovery across all services.

/// <summary>
/// Discovers and loads plugin test assemblies (lib-*.tests) from the repository
/// file system, using the same Assembly.LoadFrom() mechanism as PluginLoader.
/// Provides both path-based lookup (for metadata scanning) and AppDomain loading
/// (for standard reflection in future structural tests).
/// </summary>
internal static class TestAssemblyDiscovery
{
    private static readonly Lazy<string> LazyRepoRoot = new(FindRepoRoot);
    private static readonly Lazy<string> LazyBuildConfiguration = new(FindBuildConfiguration);
    private static readonly object LoadLock = new();
    private static IReadOnlyList<Assembly>? _loadedTestAssemblies;

    /// <summary>
    /// Repository root directory (containing the plugins/ directory).
    /// </summary>
    internal static string RepoRoot => LazyRepoRoot.Value;

    /// <summary>
    /// Discovers and loads all lib-*.tests assemblies into the AppDomain using
    /// Assembly.LoadFrom() — the same mechanism as PluginLoader. Loaded assemblies
    /// are available for standard reflection (GetTypes, GetMethods, GetCustomAttributes, etc.)
    /// in any structural test that needs type-level or attribute-level inspection of test projects.
    /// </summary>
    /// <returns>All successfully loaded test assemblies.</returns>
    internal static IReadOnlyList<Assembly> EnsureTestAssembliesLoaded()
    {
        if (_loadedTestAssemblies != null)
            return _loadedTestAssemblies;

        lock (LoadLock)
        {
            if (_loadedTestAssemblies != null)
                return _loadedTestAssemblies;

            _loadedTestAssemblies = DiscoverAndLoadTestAssemblies();
            return _loadedTestAssemblies;
        }
    }

    /// <summary>
    /// Gets the file path to a plugin test project's compiled DLL without loading it.
    /// Returns null if the DLL doesn't exist (test project not built or missing).
    /// </summary>
    /// <param name="serviceName">Service name without lib- prefix (e.g., "music", "storyline").</param>
    internal static string? GetTestAssemblyPath(string serviceName)
    {
        var config = LazyBuildConfiguration.Value;
        var path = Path.Combine(
            RepoRoot, "plugins", $"lib-{serviceName}.tests",
            "bin", config, "net9.0", $"lib-{serviceName}.tests.dll");
        return File.Exists(path) ? path : null;
    }

    private static IReadOnlyList<Assembly> DiscoverAndLoadTestAssemblies()
    {
        var loaded = new List<Assembly>();
        var pluginsDir = Path.Combine(RepoRoot, "plugins");

        if (!Directory.Exists(pluginsDir))
            return loaded.AsReadOnly();

        foreach (var testDir in Directory.GetDirectories(pluginsDir, "lib-*.tests"))
        {
            var dirName = Path.GetFileName(testDir);
            var serviceName = dirName["lib-".Length..^".tests".Length];
            var dllPath = GetTestAssemblyPath(serviceName);

            if (dllPath == null)
                continue;

            try
            {
                // Check if already loaded (same pattern as PluginLoader)
                var assemblyName = AssemblyName.GetAssemblyName(dllPath);
                var existing = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == assemblyName.Name);

                loaded.Add(existing ?? Assembly.LoadFrom(dllPath));
            }
            catch (Exception)
            {
                // Skip assemblies that fail to load (dependency resolution issues, etc.)
                // Safe failure mode: those test projects simply won't be validated.
            }
        }

        return loaded.AsReadOnly();
    }

    /// <summary>
    /// Finds the repository root by walking up from the executing assembly's directory
    /// until a directory containing "plugins/" is found.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = Path.GetDirectoryName(typeof(TestAssemblyDiscovery).Assembly.Location)
            ?? throw new InvalidOperationException("Cannot determine assembly location");

        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "plugins")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            "Cannot find repository root (directory containing 'plugins/'). " +
            "Ensure the test is running from within the bannou repository.");
    }

    /// <summary>
    /// Extracts the build configuration (Debug/Release/Development) from the executing
    /// assembly's output path (pattern: .../bin/{Configuration}/net9.0/structural-tests.dll).
    /// </summary>
    private static string FindBuildConfiguration()
    {
        var location = typeof(TestAssemblyDiscovery).Assembly.Location;
        var parts = location.Split(Path.DirectorySeparatorChar);
        for (int i = 0; i < parts.Length - 2; i++)
        {
            if (string.Equals(parts[i], "bin", StringComparison.Ordinal))
                return parts[i + 1];
        }
        return "Debug";
    }
}
