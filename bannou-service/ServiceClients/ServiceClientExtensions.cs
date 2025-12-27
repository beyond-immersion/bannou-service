using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using System.Net.Http;
using System.Reflection;

namespace BeyondImmersion.BannouService.ServiceClients;

/// <summary>
/// Dependency injection extensions for registering Bannou-aware service clients.
/// Provides dynamic app-id resolution with default "bannou" routing.
/// </summary>
public static class ServiceClientExtensions
{
    /// <summary>
    /// Registers the service app mapping resolver as a singleton.
    /// This resolver handles dynamic service-to-app-id mapping via RabbitMQ events.
    /// </summary>
    public static IServiceCollection AddServiceAppMappingResolver(this IServiceCollection services)
    {
        services.AddSingleton<IServiceAppMappingResolver, ServiceAppMappingResolver>();
        return services;
    }

    /// <summary>
    /// Registers a Bannou-aware service client with automatic HttpClient configuration.
    /// The client uses dynamic app-id resolution defaulting to "bannou".
    /// </summary>
    /// <typeparam name="TClient">The service client type (e.g., AccountsClient)</typeparam>
    /// <typeparam name="TInterface">The service interface type (e.g., IAccountsClient)</typeparam>
    /// <param name="services">The service collection to add the client to.</param>
    /// <param name="serviceName">The service name for app-id resolution (e.g., "accounts")</param>
    /// <param name="configureClient">Optional HttpClient configuration</param>
    public static IServiceCollection AddBannouServiceClient<TClient, TInterface>(
        this IServiceCollection services,
        string serviceName,
        Action<HttpClient>? configureClient = null)
        where TClient : class, TInterface
        where TInterface : class
    {
        services.AddHttpClient<TClient>(client =>
        {
            // mesh endpoint - will be routed via app-id resolution
            client.BaseAddress = new Uri("http://localhost:3500");
            client.Timeout = TimeSpan.FromSeconds(30);

            // Add standard routing headers
            client.DefaultRequestHeaders.Add("Accept", "application/json");

            configureClient?.Invoke(client);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler())
        .SetHandlerLifetime(TimeSpan.FromMinutes(5));

        // Register the client with its interface
        services.AddScoped<TInterface>(serviceProvider =>
        {
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var appMappingResolver = serviceProvider.GetRequiredService<IServiceAppMappingResolver>();
            var logger = serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TClient>>();

            var httpClient = httpClientFactory.CreateClient(typeof(TClient).Name);

            // Use reflection to create the client instance with proper constructor parameters
            var constructor = typeof(TClient).GetConstructors()
                .FirstOrDefault(c => c.GetParameters().Length >= 3) ?? throw new InvalidOperationException($"No suitable constructor found for {typeof(TClient).Name}");
            var parameters = constructor.GetParameters();
            var args = new object[parameters.Length];

            // Map known constructor parameters
            for (int i = 0; i < parameters.Length; i++)
            {
                var paramType = parameters[i].ParameterType;
                if (paramType == typeof(HttpClient))
                    args[i] = httpClient;
                else if (paramType == typeof(IServiceAppMappingResolver))
                    args[i] = appMappingResolver;
                else if (paramType.Name.StartsWith("ILogger"))
                    args[i] = logger;
                else if (paramType == typeof(string) && parameters[i].Name == "serviceName")
                    args[i] = serviceName;
                else
                    args[i] = serviceProvider.GetService(paramType) ??
                            throw new InvalidOperationException($"Cannot resolve parameter {parameters[i].Name} of type {paramType.Name}");
            }

            var instance = Activator.CreateInstance(typeof(TClient), args)
                ?? throw new InvalidOperationException($"Failed to create instance of {typeof(TClient).Name}");
            return (TInterface)instance;
        });

        return services;
    }

    /// <summary>
    /// Registers multiple Bannou service clients at once for convenience.
    /// Uses service name derived from client type name (e.g., AccountsClient -> "accounts").
    /// </summary>
    public static IServiceCollection AddBannouServiceClients(
        this IServiceCollection services,
        params (Type clientType, Type interfaceType, string serviceName)[] clientConfigurations)
    {
        foreach (var (clientType, interfaceType, serviceName) in clientConfigurations)
        {
            var method = typeof(ServiceClientExtensions)
                .GetMethod(nameof(AddBannouServiceClient))!
                .MakeGenericMethod(clientType, interfaceType);

            method.Invoke(null, new object?[] { services, serviceName, null });
        }

        return services;
    }

    /// <summary>
    /// Auto-registers all service clients found in the current assembly and plugin assemblies.
    /// Follows naming convention: {Service}Client implements I{Service}Client.
    /// Service name is derived by removing "Client" suffix and converting to lowercase.
    /// </summary>
    public static IServiceCollection AddAllBannouServiceClients(this IServiceCollection services)
    {
        var assemblies = new List<Assembly> { typeof(ServiceClientExtensions).Assembly };

        // Add plugin assemblies if PluginLoader is available
        // Use GetAllPluginAssemblies() to include client types from ALL plugins (enabled and disabled)
        // Client types from disabled plugins are needed for inter-service communication
        var pluginLoader = Program.PluginLoader;
        if (pluginLoader != null)
        {
            assemblies.AddRange(pluginLoader.GetAllPluginAssemblies());
        }

        foreach (var assembly in assemblies)
        {
            var clientTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Client"))
                .ToList();

            foreach (var clientType in clientTypes)
            {
                var interfaceName = $"I{clientType.Name}";
                var interfaceType = assembly.GetType($"{clientType.Namespace}.{interfaceName}");

                if (interfaceType == null)
                    continue;

                // Derive service name from client type name
                var serviceName = clientType.Name
                    .Replace("Client", "")
                    .ToLowerInvariant();

                var method = typeof(ServiceClientExtensions)
                    .GetMethod(nameof(AddBannouServiceClient))!
                    .MakeGenericMethod(clientType, interfaceType);

                method.Invoke(null, new object?[] { services, serviceName, null });
            }
        }

        return services;
    }
}
