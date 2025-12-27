using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BeyondImmersion.BannouService.ServiceClients;

/// <summary>
/// Extension methods for registering all service client infrastructure.
/// This includes Bannou service clients, event handling, and service mapping.
/// </summary>
public static class ServiceClientsDependencyInjection
{
    /// <summary>
    /// Registers the complete Bannou service client infrastructure.
    /// Includes Bannou clients, service mapping, events, and lifecycle management.
    /// </summary>
    public static IServiceCollection AddBannouServiceClients(this IServiceCollection services)
    {
        // Core service mapping infrastructure
        services.AddServiceAppMappingResolver();

        // Event consumer for cross-plugin event dispatch
        services.AddEventConsumer();

        return services;
    }

    /// <summary>
    /// Registers all discovered Bannou service clients automatically.
    /// Uses reflection to find all {Service}Client classes and registers them with DI.
    /// </summary>
    public static IServiceCollection AddAllBannouServiceClients(this IServiceCollection services)
    {
        services.AddBannouServiceClients();
        ServiceClientExtensions.AddAllBannouServiceClients(services);

        return services;
    }

    /// <summary>
    /// Registers specific service clients manually for more control.
    /// </summary>
    public static IServiceCollection AddSpecificServiceClients(
        this IServiceCollection services,
        params (Type clientType, Type interfaceType, string serviceName)[] clientConfigurations)
    {
        services.AddBannouServiceClients();
        services.AddBannouServiceClients(clientConfigurations);

        return services;
    }
}
