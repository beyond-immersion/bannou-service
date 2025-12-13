using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.ClientEvents;

/// <summary>
/// Extension methods for registering client event services.
/// </summary>
public static class ClientEventsDependencyInjection
{
    /// <summary>
    /// Adds the client event publisher to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Registers:
    /// - IClientEventPublisher as singleton service (DaprClient is thread-safe)
    /// </remarks>
    public static IServiceCollection AddClientEventPublisher(this IServiceCollection services)
    {
        services.AddSingleton<IClientEventPublisher, DaprClientEventPublisher>();
        return services;
    }
}
