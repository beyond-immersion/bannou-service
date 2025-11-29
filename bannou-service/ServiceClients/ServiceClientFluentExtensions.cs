using Newtonsoft.Json.Linq;

namespace BeyondImmersion.BannouService.ServiceClients;

/// <summary>
/// Fluent API extensions for service client header management.
/// Provides type-safe method chaining without casting requirements.
/// </summary>
public static class ServiceClientFluentExtensions
{
    /// <summary>
    /// Sets a custom header on the service client.
    /// Uses extension method pattern to return the correct concrete type for method chaining.
    /// </summary>
    /// <typeparam name="T">The concrete service client type</typeparam>
    /// <param name="client">The service client instance</param>
    /// <param name="name">Header name</param>
    /// <param name="value">Header value</param>
    /// <returns>The same client instance for method chaining</returns>
    public static T WithHeader<T>(this T client, string name, string value)
        where T : DaprServiceClientBase
    {
        client.SetHeader(name, value);
        return client;
    }

    /// <summary>
    /// Sets the Authorization header on the service client.
    /// Automatically prefixes with "Bearer " if not already present.
    /// Uses extension method pattern to return the correct concrete type for method chaining.
    /// </summary>
    /// <typeparam name="T">The concrete service client type</typeparam>
    /// <param name="client">The service client instance</param>
    /// <param name="token">JWT token or other authorization value</param>
    /// <returns>The same client instance for method chaining</returns>
    public static T WithAuthorization<T>(this T client, string token)
        where T : DaprServiceClientBase
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            client.ClearAuthorization();
            return client;
        }

        var authValue = token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? token : $"Bearer {token}";

        client.SetHeader("Authorization", authValue);
        return client;
    }

    /// <summary>
    /// Sets the Authorization header using a JObject containing auth data.
    /// Serializes the JObject to JSON and uses it as the authorization value.
    /// Uses extension method pattern to return the correct concrete type for method chaining.
    /// </summary>
    /// <typeparam name="T">The concrete service client type</typeparam>
    /// <param name="client">The service client instance</param>
    /// <param name="authData">JObject containing authentication data</param>
    /// <returns>The same client instance for method chaining</returns>
    public static T WithAuthorization<T>(this T client, JObject authData)
        where T : DaprServiceClientBase
    {
        if (authData == null)
        {
            client.ClearAuthorization();
            return client;
        }

        var authValue = $"Bearer {authData.ToString(Newtonsoft.Json.Formatting.None)}";
        client.SetHeader("Authorization", authValue);
        return client;
    }

    /// <summary>
    /// Clears the Authorization header on the service client.
    /// Uses extension method pattern to return the correct concrete type for method chaining.
    /// </summary>
    /// <typeparam name="T">The concrete service client type</typeparam>
    /// <param name="client">The service client instance</param>
    /// <returns>The same client instance for method chaining</returns>
    public static T ClearAuthorization<T>(this T client)
        where T : DaprServiceClientBase
    {
        client.ClearAuthorization();
        return client;
    }
}
