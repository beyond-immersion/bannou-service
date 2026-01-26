namespace BeyondImmersion.BannouService.Tools.Inspect.Services;

/// <summary>
/// Provides assembly resolution for MetadataLoadContext.
/// </summary>
public sealed class AssemblyResolver : MetadataAssemblyResolver
{
    private readonly string _primaryAssemblyPath;
    private readonly HashSet<string> _searchDirectories;
    private readonly Dictionary<string, string> _assemblyPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the AssemblyResolver class.
    /// </summary>
    /// <param name="primaryAssemblyPath">Path to the primary assembly being inspected.</param>
    /// <param name="additionalSearchPaths">Additional directories to search for dependencies.</param>
    public AssemblyResolver(string primaryAssemblyPath, IEnumerable<string>? additionalSearchPaths = null)
    {
        _primaryAssemblyPath = primaryAssemblyPath;
        _searchDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Add the primary assembly's directory
        var primaryDir = Path.GetDirectoryName(primaryAssemblyPath);
        if (primaryDir is not null)
        {
            _searchDirectories.Add(primaryDir);
        }

        // Add runtime directory for core assemblies
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (runtimeDir is not null)
        {
            _searchDirectories.Add(runtimeDir);
        }

        // Add .NET reference assemblies path
        var dotnetPath = Environment.GetEnvironmentVariable("DOTNET_ROOT")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");

        var refAssemblies = Path.Combine(dotnetPath, "packs", "Microsoft.NETCore.App.Ref");
        if (Directory.Exists(refAssemblies))
        {
            var latestVersion = Directory.GetDirectories(refAssemblies)
                .OrderByDescending(d => d)
                .FirstOrDefault();

            if (latestVersion is not null)
            {
                var net9Ref = Path.Combine(latestVersion, "ref", "net9.0");
                if (Directory.Exists(net9Ref))
                {
                    _searchDirectories.Add(net9Ref);
                }

                var net8Ref = Path.Combine(latestVersion, "ref", "net8.0");
                if (Directory.Exists(net8Ref))
                {
                    _searchDirectories.Add(net8Ref);
                }
            }
        }

        // Add additional search paths
        if (additionalSearchPaths is not null)
        {
            foreach (var path in additionalSearchPaths)
            {
                if (Directory.Exists(path))
                {
                    _searchDirectories.Add(path);
                }
            }
        }

        // Pre-populate assembly paths from search directories
        foreach (var dir in _searchDirectories)
        {
            PopulateAssemblyPaths(dir);
        }
    }

    /// <summary>
    /// Resolves an assembly reference by name.
    /// </summary>
    public override Assembly? Resolve(MetadataLoadContext context, AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (name is null)
        {
            return null;
        }

        // Check if we have a cached path
        if (_assemblyPaths.TryGetValue(name, out var path))
        {
            return context.LoadFromAssemblyPath(path);
        }

        // Try to find in NuGet packages
        var nugetPath = NuGetPackageLocator.FindAssemblyPath(name);
        if (nugetPath is not null)
        {
            _assemblyPaths[name] = nugetPath;
            PopulateAssemblyPaths(Path.GetDirectoryName(nugetPath) ?? "");
            return context.LoadFromAssemblyPath(nugetPath);
        }

        return null;
    }

    private void PopulateAssemblyPaths(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        try
        {
            foreach (var dll in Directory.GetFiles(directory, "*.dll"))
            {
                var name = Path.GetFileNameWithoutExtension(dll);
                _assemblyPaths.TryAdd(name, dll);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore directories we can't access
        }
    }
}
