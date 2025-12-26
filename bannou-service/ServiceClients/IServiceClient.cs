namespace BeyondImmersion.BannouService.ServiceClients;

/// <summary>
/// Base marker interface for all Bannou service clients.
/// Provides explicit identification of client types for plugin discovery.
/// </summary>
public interface IServiceClient
{
    /// <summary>
    /// The name of the service this client communicates with.
    /// Should match the service name in the corresponding BannouServiceAttribute.
    /// </summary>
    string ServiceName { get; }
}

/// <summary>
/// Generic interface for Bannou service clients with fluent API support.
/// Provides type-safe method chaining for request configuration.
/// </summary>
/// <typeparam name="TSelf">The concrete client type for fluent method chaining</typeparam>
public interface IServiceClient<TSelf> : IServiceClient where TSelf : IServiceClient<TSelf>
{
    /// <summary>
    /// Sets the Authorization header for the next request.
    /// Automatically prefixes with "Bearer " if not already present.
    /// Returns this instance for method chaining.
    /// </summary>
    /// <param name="token">JWT token or authorization value</param>
    /// <returns>This client instance for method chaining</returns>
    TSelf WithAuthorization(string? token);
}
