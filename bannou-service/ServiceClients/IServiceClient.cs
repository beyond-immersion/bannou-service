namespace BeyondImmersion.BannouService.ServiceClients;

/// <summary>
/// Base interface for all Bannou service clients.
/// Provides fluent API methods for request configuration.
/// </summary>
/// <typeparam name="TSelf">The concrete client type for fluent method chaining</typeparam>
public interface IServiceClient<TSelf> where TSelf : IServiceClient<TSelf>
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
