namespace BeyondImmersion.BannouService.Tools.Inspect.Services;

/// <summary>
/// Locates NuGet package assemblies and their XML documentation files.
/// </summary>
public static class NuGetPackageLocator
{
    private static readonly string NuGetPackagesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".nuget",
        "packages"
    );

    /// <summary>
    /// Finds the assembly path for a NuGet package.
    /// </summary>
    /// <param name="packageName">The NuGet package name (e.g., "RabbitMQ.Client").</param>
    /// <param name="version">Optional specific version. If null, uses the latest.</param>
    /// <returns>Path to the main assembly, or null if not found.</returns>
    public static string? FindAssemblyPath(string packageName, string? version = null)
    {
        var packagePath = Path.Combine(NuGetPackagesPath, packageName.ToLowerInvariant());
        if (!Directory.Exists(packagePath))
        {
            return null;
        }

        var versionDir = GetVersionDirectory(packagePath, version);
        if (versionDir is null)
        {
            return null;
        }

        // Try to find the assembly in lib folder
        var libPath = Path.Combine(versionDir, "lib");
        if (!Directory.Exists(libPath))
        {
            return null;
        }

        // Prefer net9.0, then net8.0, then netstandard2.1, then netstandard2.0
        string[] tfmPriority = ["net9.0", "net8.0", "net7.0", "net6.0", "netstandard2.1", "netstandard2.0"];

        foreach (var tfm in tfmPriority)
        {
            var tfmPath = Path.Combine(libPath, tfm);
            if (Directory.Exists(tfmPath))
            {
                var dll = Directory.GetFiles(tfmPath, "*.dll").FirstOrDefault();
                if (dll is not null)
                {
                    return dll;
                }
            }
        }

        // Fallback: find any .dll in lib subdirectories
        foreach (var subDir in Directory.GetDirectories(libPath))
        {
            var dll = Directory.GetFiles(subDir, "*.dll").FirstOrDefault();
            if (dll is not null)
            {
                return dll;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the XML documentation file for a NuGet package assembly.
    /// </summary>
    /// <param name="assemblyPath">Path to the assembly.</param>
    /// <returns>Path to the XML file, or null if not found.</returns>
    public static string? FindXmlDocPath(string assemblyPath)
    {
        var xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
        return File.Exists(xmlPath) ? xmlPath : null;
    }

    /// <summary>
    /// Gets all assemblies in a NuGet package (including dependencies in the same TFM folder).
    /// </summary>
    /// <param name="packageName">The NuGet package name.</param>
    /// <param name="version">Optional specific version.</param>
    /// <returns>List of assembly paths.</returns>
    public static IReadOnlyList<string> GetPackageAssemblies(string packageName, string? version = null)
    {
        var mainAssembly = FindAssemblyPath(packageName, version);
        if (mainAssembly is null)
        {
            return [];
        }

        var directory = Path.GetDirectoryName(mainAssembly);
        if (directory is null)
        {
            return [mainAssembly];
        }

        return Directory.GetFiles(directory, "*.dll");
    }

    /// <summary>
    /// Lists all installed NuGet packages.
    /// </summary>
    /// <returns>List of package names and their installed versions.</returns>
    public static IReadOnlyList<(string Package, string[] Versions)> ListInstalledPackages()
    {
        if (!Directory.Exists(NuGetPackagesPath))
        {
            return [];
        }

        var result = new List<(string, string[])>();

        foreach (var packageDir in Directory.GetDirectories(NuGetPackagesPath))
        {
            var packageName = Path.GetFileName(packageDir);
            var versions = Directory.GetDirectories(packageDir)
                .Select(Path.GetFileName)
                .Where(v => v is not null && !v.StartsWith('.'))
                .Cast<string>()
                .OrderByDescending(v => v)
                .ToArray();

            if (versions.Length > 0)
            {
                result.Add((packageName, versions));
            }
        }

        return result;
    }

    private static string? GetVersionDirectory(string packagePath, string? version)
    {
        if (version is not null)
        {
            var specificPath = Path.Combine(packagePath, version);
            return Directory.Exists(specificPath) ? specificPath : null;
        }

        // Get latest version (sort by semantic versioning)
        var versions = Directory.GetDirectories(packagePath)
            .Select(Path.GetFileName)
            .Where(v => v is not null && !v.StartsWith('.'))
            .Cast<string>()
            .OrderByDescending(ParseVersion)
            .ToList();

        if (versions.Count == 0)
        {
            return null;
        }

        return Path.Combine(packagePath, versions[0]);
    }

    private static Version ParseVersion(string versionString)
    {
        // Handle pre-release suffixes like "7.1.2-rc.1"
        var dashIndex = versionString.IndexOf('-');
        if (dashIndex > 0)
        {
            versionString = versionString[..dashIndex];
        }

        return Version.TryParse(versionString, out var v) ? v : new Version(0, 0);
    }
}
