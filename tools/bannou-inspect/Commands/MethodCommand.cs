namespace BeyondImmersion.BannouService.Tools.Inspect.Commands;

/// <summary>
/// Command to inspect a method's signature, parameters, and exceptions.
/// </summary>
public static class MethodCommand
{
    /// <summary>
    /// Creates the method command.
    /// </summary>
    public static Command Create()
    {
        var methodArg = new Argument<string>("method", "The method to inspect (e.g., 'IChannel.BasicPublish' or 'BasicPublish')");
        var packageOption = new Option<string?>("--package", "NuGet package name containing the type");
        packageOption.AddAlias("-p");
        var assemblyOption = new Option<string?>("--assembly", "Path to the assembly file");
        assemblyOption.AddAlias("-a");
        var typeOption = new Option<string?>("--type", "Type containing the method (if method name alone is ambiguous)");
        typeOption.AddAlias("-t");
        var versionOption = new Option<string?>("--version", "Specific package version (default: latest)");
        versionOption.AddAlias("-v");

        var command = new Command("method", "Inspect a method's signature, parameters, and exceptions")
        {
            methodArg,
            packageOption,
            assemblyOption,
            typeOption,
            versionOption
        };

        command.SetHandler(Execute, methodArg, packageOption, assemblyOption, typeOption, versionOption);
        return command;
    }

    private static void Execute(string method, string? package, string? assembly, string? type, string? version)
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
                return;
            }
            xmlDocPath = NuGetPackageLocator.FindXmlDocPath(assemblyPath);
        }
        else
        {
            Console.Error.WriteLine("Either --package or --assembly must be specified.");
            return;
        }

        // Parse type and method from the argument
        string typeName;
        string methodName;

        var lastDot = method.LastIndexOf('.');
        if (lastDot > 0)
        {
            typeName = method[..lastDot];
            methodName = method[(lastDot + 1)..];
        }
        else if (type is not null)
        {
            typeName = type;
            methodName = method;
        }
        else
        {
            Console.Error.WriteLine("Method must include type name (e.g., 'IChannel.BasicPublish') or use --type option.");
            return;
        }

        using var inspector = new TypeInspector(assemblyPath, xmlDocPath);
        var methods = inspector.InspectMethod(typeName, methodName);

        ConsoleFormatter.WriteMethodInfo(methods, typeName);
    }
}
