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
    /// - IClientEventPublisher as scoped service (uses DaprClient per request)
    /// </remarks>
    public static IServiceCollection AddClientEventPublisher(this IServiceCollection services)
    {
        services.AddScoped<IClientEventPublisher, DaprClientEventPublisher>();
        return services;
    }
}
