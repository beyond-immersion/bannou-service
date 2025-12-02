using DotNetEnv;
using Microsoft.Extensions.Configuration;

namespace BeyondImmersion.EdgeTester.Application;

/// <summary>
/// Simple configuration helper for edge-tester that doesn't depend on server-side code.
/// Loads configuration from .env files, environment variables, and command line args.
/// </summary>
public static class ConfigurationHelper
{
    /// <summary>
    /// Builds the configuration root from available .env files, environment variables, and command line args.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>The configuration root.</returns>
    public static IConfigurationRoot BuildConfigurationRoot(string[]? args = null)
    {
        // Load .env file first for local development support
        try
        {
            if (File.Exists("../.env"))
            {
                Env.Load("../.env");
            }
            else if (File.Exists(".env"))
            {
                Env.Load();
            }
        }
        catch (Exception)
        {
            // .env file is optional, ignore if not present
        }

        IConfigurationBuilder configurationBuilder = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddCommandLine(args ?? Environment.GetCommandLineArgs(), CreateSwitchMappings());

        return configurationBuilder.Build();
    }

    /// <summary>
    /// Builds and binds the ClientConfiguration from available sources.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>The bound ClientConfiguration.</returns>
    public static ClientConfiguration BuildClientConfiguration(string[]? args = null)
    {
        var configRoot = BuildConfigurationRoot(args);
        return configRoot.Get<ClientConfiguration>() ?? new ClientConfiguration();
    }

    /// <summary>
    /// Creates command line switch mappings for common configuration options.
    /// </summary>
    private static Dictionary<string, string> CreateSwitchMappings()
    {
        return new Dictionary<string, string>
        {
            { "-c", "Connect_Endpoint" },
            { "--connect", "Connect_Endpoint" },
            { "-r", "Register_Endpoint" },
            { "--register", "Register_Endpoint" },
            { "-l", "Login_Credentials_Endpoint" },
            { "--login", "Login_Credentials_Endpoint" },
            { "-t", "Login_Token_Endpoint" },
            { "--token", "Login_Token_Endpoint" },
            { "-u", "Client_Username" },
            { "--username", "Client_Username" },
            { "-p", "Client_Password" },
            { "--password", "Client_Password" },
            { "--openresty-host", "OpenResty_Host" },
            { "--openresty-port", "OpenResty_Port" },
            { "--admin-username", "Admin_Username" },
            { "--admin-password", "Admin_Password" }
        };
    }
}
