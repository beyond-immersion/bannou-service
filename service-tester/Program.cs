using BeyondImmersion.BannouService;

namespace BeyondImmersion.ServiceTester;

public class Program
{
    /// <summary>
    /// Configuration for test application.
    /// </summary>
    public static TestConfiguration Configuration { get; private set; }

    /// <summary>
    /// Lookup for all service tests.
    /// </summary>
    private static readonly Dictionary<string, Action<string[]>> sTestRegistry = new();

    internal static void Main()
    {
        Configuration = IServiceConfiguration.BuildConfiguration<TestConfiguration>();
        if (!ValidateConfiguration())
            return;

        LoadServiceTests();

        string? line;
        if (sTestRegistry.Count == 0)
        {
            Console.WriteLine("No tests to run- press any key to exit.");
            _ = Console.ReadKey();
            return;
        }

        string command;
        List<string> commandArgs;
        do
        {
            Console.WriteLine("Select a test to run from the following list (press CTRL+C to exit):");
            var i = 0;
            foreach (KeyValuePair<string, Action<string[]>> kvp in sTestRegistry)
                Console.WriteLine($"{++i}. {kvp.Key}");

            Console.WriteLine();
            line = Console.ReadLine();
            if (line != null && !string.IsNullOrWhiteSpace(line))
            {
                commandArgs = line.Split(' ').ToList();
                command = commandArgs[0];
                commandArgs.RemoveAt(0);

                if (int.TryParse(command, out var commandIndex) && commandIndex >= 1 && commandIndex <= sTestRegistry.Count)
                    command = sTestRegistry.Keys.ElementAt(commandIndex - 1);

                if (sTestRegistry.TryGetValue(command, out Action<string[]>? testTarget))
                {
                    Console.Clear();
                    testTarget?.Invoke(commandArgs.ToArray());
                }
                else
                {
                    Console.WriteLine($"Command '{command}' not found.");
                }

                Console.WriteLine();
                Console.WriteLine($"Press any key to continue...");
                _ = Console.ReadKey();
                Console.Clear();
            }

            // delay slightly to ensure detecting CTRL+C before looping
            Thread.Sleep(1);

        } while (true);
    }

    private static bool ValidateConfiguration()
    {
        if (Configuration == null)
        {
            Console.WriteLine("Error: missing configuration.");
            return false;
        }

        if (!(Configuration as IServiceConfiguration).HasRequired())
        {
            Console.WriteLine("Error: missing required configuration.");
            return false;
        }

        return true;
    }

    private static void LoadServiceTests()
    {
        sTestRegistry.Add("All", RunEntireTestSuite);

        // load login tests
        var loginTestHandler = new LoginTestHandler();
        foreach (ServiceTest serviceTest in loginTestHandler.GetServiceTests())
            sTestRegistry.Add(serviceTest.Name, serviceTest.Target);

        // load template tests
        var templateTestHandler = new TemplateTestHandler();
        foreach (ServiceTest serviceTest in templateTestHandler.GetServiceTests())
            sTestRegistry.Add(serviceTest.Name, serviceTest.Target);

    }

    private static void RunEntireTestSuite(string[] args)
    {
        foreach (KeyValuePair<string, Action<string[]>> kvp in sTestRegistry)
        {
            if (kvp.Key == "All")
                continue;

            kvp.Value?.Invoke(args);
            Console.WriteLine();
        }
    }
}
