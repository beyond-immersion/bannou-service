using BeyondImmersion.BannouService.Tools.Inspect.Commands;

var rootCommand = new RootCommand("Bannou Assembly Inspector - IntelliSense-like type/method inspection from the command line")
{
    TypeCommand.Create(),
    MethodCommand.Create(),
    ConstructorCommand.Create(),
    ListTypesCommand.Create(),
    SearchCommand.Create()
};

return await rootCommand.InvokeAsync(args);
