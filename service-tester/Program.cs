using System;
using System.Collections.Generic;
using System.Text.Json;
using BeyondImmersion.BannouService;

namespace BeyondImmersion.ServiceTester
{
    using Tests;

    public class Program
    {
        /// <summary>
        /// Configuration for test application.
        /// </summary>
        public static TestConfiguration Configuration { get; private set; }

        /// <summary>
        /// Lookup for all service tests.
        /// </summary>
        private static readonly Dictionary<string, Action<string[]>> sTestRegistry = new Dictionary<string, Action<string[]>>();

        internal static void Main(string[] args)
        {
            Configuration = ServiceConfiguration.BuildConfiguration<TestConfiguration>(args, null) ?? new TestConfiguration();
            if (!ValidateConfiguration(Configuration))
                return;

            LoadServiceTests();

            string? line;
            if (sTestRegistry.Count == 0)
            {
                Console.WriteLine("No tests to run- press any key to exit.");
                Console.ReadKey();
                return;
            }

            string command;
            List<string> commandArgs;
            do
            {
                Console.WriteLine("Select a test to run from the following list (press CTRL+C to exit):");
                int i = 0;
                foreach (var kvp in sTestRegistry)
                    Console.WriteLine($"{++i}. {kvp.Key}");

                Console.WriteLine();
                line = Console.ReadLine();
                if (line != null && !string.IsNullOrWhiteSpace(line))
                {
                    commandArgs = line.Split(' ').ToList();
                    command = commandArgs[0];
                    commandArgs.RemoveAt(0);

                    if (int.TryParse(command, out int commandIndex) && commandIndex >= 1 && commandIndex <= sTestRegistry.Count)
                        command = sTestRegistry.Keys.ElementAt(commandIndex-1);

                    if (sTestRegistry.TryGetValue(command, out var testTarget))
                    {
                        Console.Clear();
                        testTarget?.Invoke(commandArgs.ToArray());
                    }
                    else
                        Console.WriteLine($"Command '{command}' not found.");

                    Console.WriteLine();
                    Console.WriteLine($"Press any key to continue...");
                    Console.ReadKey();
                    Console.Clear();
                }

                // delay slightly to ensure detecting CTRL+C before looping
                Thread.Sleep(1);

            } while (true);
        }

        private static bool ValidateConfiguration(TestConfiguration configuration)
        {
            return true;
        }

        private static void LoadServiceTests()
        {
            sTestRegistry.Add("All", RunEntireTestSuite);

            // load login tests
            var loginTestHandler = new LoginTestHandler();
            foreach (var serviceTest in loginTestHandler.GetServiceTests())
                sTestRegistry.Add(serviceTest.Name, serviceTest.Target);

        }

        private static void RunEntireTestSuite(string[] args)
        {
            foreach (var kvp in sTestRegistry)
            {
                if (kvp.Key == "All")
                    continue;

                kvp.Value?.Invoke(args);
                Console.WriteLine();
            }
        }
    }
}
