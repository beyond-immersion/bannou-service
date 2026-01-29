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
    /// Environment variables are normalized from UPPER_SNAKE_CASE to PascalCase.
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

        // Use normalized env vars to support UPPER_SNAKE_CASE -> PascalCase mapping
        var normalizedEnvVars = GetNormalizedEnvVars();

        IConfigurationBuilder configurationBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(normalizedEnvVars)
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
            { "-c", "ConnectEndpoint" },
            { "--connect", "ConnectEndpoint" },
            { "--connect-endpoint", "ConnectEndpoint" },
            { "-r", "RegisterEndpoint" },
            { "--register", "RegisterEndpoint" },
            { "--register-endpoint", "RegisterEndpoint" },
            { "-l", "LoginCredentialsEndpoint" },
            { "--login", "LoginCredentialsEndpoint" },
            { "--login-credentials-endpoint", "LoginCredentialsEndpoint" },
            { "-t", "LoginTokenEndpoint" },
            { "--token", "LoginTokenEndpoint" },
            { "--login-token-endpoint", "LoginTokenEndpoint" },
            { "-u", "ClientUsername" },
            { "--username", "ClientUsername" },
            { "--client-username", "ClientUsername" },
            { "-p", "ClientPassword" },
            { "--password", "ClientPassword" },
            { "--client-password", "ClientPassword" },
            { "--openresty-host", "OpenRestyHost" },
            { "--openresty-port", "OpenRestyPort" },
            { "--admin-username", "AdminUsername" },
            { "--admin-password", "AdminPassword" },
            { "--exit-on-service-error", "ExitOnServiceError" }
        };
    }

    /// <summary>
    /// Normalizes an environment variable key from UPPER_SNAKE_CASE to PascalCase.
    /// Example: STORAGE_ACCESS_KEY -> StorageAccessKey
    /// </summary>
    private static string NormalizeEnvVarKey(string envVarKey)
    {
        if (string.IsNullOrEmpty(envVarKey))
            return envVarKey;

        // Split by underscore and convert each part to title case
        var parts = envVarKey.Split('_');
        var result = new System.Text.StringBuilder();

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part))
                continue;

            // First letter uppercase, rest lowercase
            result.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
                result.Append(part.Substring(1).ToLowerInvariant());
        }

        return result.ToString();
    }

    /// <summary>
    /// Reads all environment variables and returns them with normalized keys.
    /// Keys are converted from UPPER_SNAKE_CASE to PascalCase to match C# property naming.
    /// </summary>
    private static IDictionary<string, string?> GetNormalizedEnvVars()
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = entry.Key?.ToString();
            if (key == null)
                continue;

            var normalizedKey = NormalizeEnvVarKey(key);

            // Only add if we got a valid normalized key
            if (!string.IsNullOrEmpty(normalizedKey))
                result[normalizedKey] = entry.Value?.ToString();
        }

        return result;
    }
}
