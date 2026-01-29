namespace BeyondImmersion.BannouService.Tools.Inspect.Commands;

/// <summary>
/// Command to list all public types in an assembly.
/// </summary>
public static class ListTypesCommand
{
    /// <summary>
    /// Creates the list-types command.
    /// </summary>
    public static Command Create()
    {
        var packageOption = new Option<string?>("--package", "NuGet package name");
        packageOption.AddAlias("-p");
        var assemblyOption = new Option<string?>("--assembly", "Path to the assembly file");
        assemblyOption.AddAlias("-a");
        var versionOption = new Option<string?>("--version", "Specific package version (default: latest)");
        versionOption.AddAlias("-v");

        var command = new Command("list-types", "List all public types in an assembly")
        {
            packageOption,
            assemblyOption,
            versionOption
        };

        command.SetHandler(Execute, packageOption, assemblyOption, versionOption);
        return command;
    }

    private static void Execute(string? package, string? assembly, string? version)
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
        var types = inspector.GetPublicTypes();

        var heading = package is not null ? $"Types in {package}" : $"Types in {Path.GetFileName(assemblyPath)}";
        ConsoleFormatter.WriteTypeList(types, heading);
    }
}
