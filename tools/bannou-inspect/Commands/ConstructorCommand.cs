namespace BeyondImmersion.BannouService.Tools.Inspect.Commands;

/// <summary>
/// Command to inspect a type's constructors.
/// </summary>
public static class ConstructorCommand
{
    /// <summary>
    /// Creates the constructor command.
    /// </summary>
    public static Command Create()
    {
        var typeArg = new Argument<string>("type", "The type name to inspect constructors for (e.g., 'ConnectionFactory')");
        var packageOption = new Option<string?>("--package", "NuGet package name containing the type");
        packageOption.AddAlias("-p");
        var assemblyOption = new Option<string?>("--assembly", "Path to the assembly file");
        assemblyOption.AddAlias("-a");
        var versionOption = new Option<string?>("--version", "Specific package version (default: latest)");
        versionOption.AddAlias("-v");

        var command = new Command("constructor", "Inspect a type's constructors and their parameters")
        {
            typeArg,
            packageOption,
            assemblyOption,
            versionOption
        };

        command.SetHandler(Execute, typeArg, packageOption, assemblyOption, versionOption);
        return command;
    }

    private static void Execute(string typeName, string? package, string? assembly, string? version)
    {
        string? assemblyPath;
        string? xmlDocPath = null;

        if (assembly is not null)
        {
            assemblyPath = assembly;
            xmlDocPath = NuGetPackageLocator.FindXmlDocPath(assembly);
        }
        else if (package is not null)
        {
            assemblyPath = NuGetPackageLocator.FindAssemblyPath(package, version);
            if (assemblyPath is null)
            {
                Console.Error.WriteLine($"Package '{package}' not found in NuGet cache.");
                Console.Error.WriteLine("Make sure the package is installed: dotnet add package " + package);
                return;
            }
            xmlDocPath = NuGetPackageLocator.FindXmlDocPath(assemblyPath);
        }
        else
        {
            Console.Error.WriteLine("Either --package or --assembly must be specified.");
            return;
        }

        using var inspector = new TypeInspector(assemblyPath, xmlDocPath);
        var constructors = inspector.InspectConstructors(typeName);

        if (constructors.Count == 0)
        {
            // Check if the type exists at all
            var typeInfo = inspector.InspectType(typeName);
            if (typeInfo is null)
            {
                Console.Error.WriteLine($"Type '{typeName}' not found in the assembly.");

                var similar = inspector.SearchTypes($"*{typeName}*");
                if (similar.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Did you mean one of these?");
                    foreach (var t in similar.Take(10))
                    {
                        Console.WriteLine($"  - {t}");
                    }
                }
                return;
            }
        }

        ConsoleFormatter.WriteConstructorInfo(constructors, typeName);
    }
}
