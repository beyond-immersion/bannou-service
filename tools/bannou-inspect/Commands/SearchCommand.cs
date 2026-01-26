namespace BeyondImmersion.BannouService.Tools.Inspect.Commands;

/// <summary>
/// Command to search for types by name pattern.
/// </summary>
public static class SearchCommand
{
    /// <summary>
    /// Creates the search command.
    /// </summary>
    public static Command Create()
    {
        var patternArg = new Argument<string>("pattern", "Search pattern (* wildcard supported)");
        var packageOption = new Option<string?>("--package", "NuGet package name");
        packageOption.AddAlias("-p");
        var assemblyOption = new Option<string?>("--assembly", "Path to the assembly file");
        assemblyOption.AddAlias("-a");
        var versionOption = new Option<string?>("--version", "Specific package version (default: latest)");
        versionOption.AddAlias("-v");

        var command = new Command("search", "Search for types by name pattern")
        {
            patternArg,
            packageOption,
            assemblyOption,
            versionOption
        };

        command.SetHandler(Execute, patternArg, packageOption, assemblyOption, versionOption);
        return command;
    }

    private static void Execute(string pattern, string? package, string? assembly, string? version)
    {
        string? assemblyPath;

        if (assembly is not null)
        {
            assemblyPath = assembly;
        }
        else if (package is not null)
        {
            assemblyPath = NuGetPackageLocator.FindAssemblyPath(package, version);
            if (assemblyPath is null)
            {
                Console.Error.WriteLine($"Package '{package}' not found in NuGet cache.");
                return;
            }
        }
        else
        {
            Console.Error.WriteLine("Either --package or --assembly must be specified.");
            return;
        }

        using var inspector = new TypeInspector(assemblyPath);
        var types = inspector.SearchTypes(pattern);

        var heading = $"Search results for '{pattern}'";
        ConsoleFormatter.WriteTypeList(types, heading);
    }
}
