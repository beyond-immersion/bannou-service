using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BeyondImmersion.BannouService.ServiceClients;

/// <summary>
/// Extension methods for registering all service client infrastructure.
/// This includes Dapr service clients, event handling, and service mapping.
/// </summary>
public static class ServiceClientsDependencyInjection
{
    /// <summary>
    /// Registers the complete Bannou service client infrastructure.
    /// Includes Dapr clients, service mapping, events, and lifecycle management.
    /// </summary>
    public static IServiceCollection AddBannouServiceClients(this IServiceCollection services)
    {
        // Core service mapping infrastructure
        services.AddServiceAppMappingResolver();

        // Service mapping event system
        services.AddScoped<IServiceMappingEventPublisher, ServiceMappingEventPublisher>();
        services.AddScoped<IServiceMappingEventDispatcher, ServiceMappingEventDispatcher>();
        services.AddScoped<ServiceMappingEventHandler>();
        services.AddScoped<ExampleServiceMappingHandlers>();

        // Lifecycle management for automatic service announcements
        services.AddHostedService<ServiceMappingLifecycleService>();

        return services;
    }

    /// <summary>
    /// Registers all discovered Dapr service clients automatically.
    /// Uses reflection to find all {Service}Client classes and registers them with DI.
    /// </summary>
    public static IServiceCollection AddAllBannouServiceClients(this IServiceCollection services)
    {
        services.AddBannouServiceClients();
        services.AddAllDaprServiceClients();

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
        services.AddDaprServiceClients(clientConfigurations);

        return services;
    }
}
