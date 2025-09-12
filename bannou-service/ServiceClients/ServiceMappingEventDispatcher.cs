using BeyondImmersion.BannouService.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace BeyondImmersion.BannouService.ServiceClients;

/// <summary>
/// Dispatches service mapping events to registered handlers using reflection and attributes.
/// Provides a clean, declarative way to handle service mapping events.
/// </summary>
public interface IServiceMappingEventDispatcher
{
    /// <summary>
    /// Dispatches a service mapping event to all matching handlers.
    /// </summary>
    Task DispatchEventAsync(ServiceMappingEvent eventData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about all registered handlers for debugging.
    /// </summary>
    IEnumerable<ServiceMappingHandlerInfo> GetRegisteredHandlers();
}

/// <summary>
/// Default implementation of service mapping event dispatcher.
/// </summary>
public class ServiceMappingEventDispatcher : IServiceMappingEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ServiceMappingEventDispatcher> _logger;
    private readonly List<ServiceMappingHandlerInfo> _handlers;

    /// <inheritdoc/>
    public ServiceMappingEventDispatcher(
        IServiceProvider serviceProvider,
        ILogger<ServiceMappingEventDispatcher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _handlers = DiscoverHandlers().ToList();

        _logger.LogInformation("Discovered {HandlerCount} service mapping event handlers", _handlers.Count);
    }

    /// <inheritdoc/>
    public async Task DispatchEventAsync(ServiceMappingEvent eventData, CancellationToken cancellationToken = default)
    {
        var matchingHandlers = GetMatchingHandlers(eventData)
            .OrderBy(h => h.Priority)
            .ToList();

        if (!matchingHandlers.Any())
        {
            _logger.LogDebug("No handlers found for service mapping event {EventId}: {Action} {ServiceName}",
                eventData.EventId, eventData.Action, eventData.ServiceName);
            return;
        }

        _logger.LogDebug("Found {HandlerCount} handlers for service mapping event {EventId}: {Action} {ServiceName}",
            matchingHandlers.Count, eventData.EventId, eventData.Action, eventData.ServiceName);

        var syncHandlers = matchingHandlers.Where(h => !h.RunAsync).ToList();
        var asyncHandlers = matchingHandlers.Where(h => h.RunAsync).ToList();

        // Execute synchronous handlers first (in priority order)
        foreach (var handler in syncHandlers)
        {
            await ExecuteHandlerAsync(handler, eventData, cancellationToken);
        }

        // Execute asynchronous handlers in parallel
        if (asyncHandlers.Any())
        {
            var asyncTasks = asyncHandlers.Select(h => ExecuteHandlerAsync(h, eventData, cancellationToken));
            await Task.WhenAll(asyncTasks);
        }
    }

    /// <inheritdoc/>
    public IEnumerable<ServiceMappingHandlerInfo> GetRegisteredHandlers()
    {
        return _handlers.AsReadOnly();
    }

    private async Task ExecuteHandlerAsync(ServiceMappingHandlerInfo handler, ServiceMappingEvent eventData, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogTrace("Executing service mapping handler {HandlerName} for event {EventId}",
                handler.Name, eventData.EventId);

            using var scope = _serviceProvider.CreateScope();
            var handlerInstance = scope.ServiceProvider.GetRequiredService(handler.HandlerType);

            var parameters = PrepareParameters(handler, eventData, cancellationToken);
            var result = handler.Method.Invoke(handlerInstance, parameters);

            // Handle async methods
            if (result is Task task)
            {
                await task;
            }

            _logger.LogDebug("Successfully executed service mapping handler {HandlerName} for event {EventId}",
                handler.Name, eventData.EventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute service mapping handler {HandlerName} for event {EventId}",
                handler.Name, eventData.EventId);
            // Continue processing other handlers even if one fails
        }
    }

    private object[] PrepareParameters(ServiceMappingHandlerInfo handler, ServiceMappingEvent eventData, CancellationToken cancellationToken)
    {
        var parameters = new List<object>();

        foreach (var param in handler.Method.GetParameters())
        {
            if (param.ParameterType == typeof(ServiceMappingEvent))
            {
                parameters.Add(eventData);
            }
            else if (param.ParameterType == typeof(CancellationToken))
            {
                parameters.Add(cancellationToken);
            }
            else if (param.ParameterType == typeof(string) && param.Name?.ToLowerInvariant() == "servicename")
            {
                parameters.Add(eventData.ServiceName);
            }
            else if (param.ParameterType == typeof(string) && param.Name?.ToLowerInvariant() == "appid")
            {
                parameters.Add(eventData.AppId);
            }
            else if (param.ParameterType == typeof(string) && param.Name?.ToLowerInvariant() == "action")
            {
                parameters.Add(eventData.Action);
            }
            else
            {
                // Try to resolve from DI container
                using var scope = _serviceProvider.CreateScope();
                var service = scope.ServiceProvider.GetService(param.ParameterType);
                if (service != null)
                {
                    parameters.Add(service);
                }
                else if (param.HasDefaultValue)
                {
                    parameters.Add(param.DefaultValue!);
                }
                else
                {
                    throw new InvalidOperationException($"Cannot resolve parameter {param.Name} of type {param.ParameterType.Name} for handler {handler.Name}");
                }
            }
        }

        return parameters.ToArray();
    }

    private IEnumerable<ServiceMappingHandlerInfo> GetMatchingHandlers(ServiceMappingEvent eventData)
    {
        return _handlers.Where(h =>
            (h.Action == "*" || string.Equals(h.Action, eventData.Action, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrEmpty(h.ServiceName) || h.ServiceName == "*" || string.Equals(h.ServiceName, eventData.ServiceName, StringComparison.OrdinalIgnoreCase))
        );
    }

    private IEnumerable<ServiceMappingHandlerInfo> DiscoverHandlers()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.GetName().Name?.StartsWith("BeyondImmersion") == true);

        foreach (var assembly in assemblies)
        {
            foreach (var type in GetTypesFromAssembly(assembly))
            {
                var classAttribute = type.GetCustomAttribute<ServiceMappingHandlerAttribute>();
                if (classAttribute?.AutoRegister == false)
                    continue;

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    var methodAttribute = method.GetCustomAttribute<ServiceMappingEventAttribute>();
                    if (methodAttribute == null)
                        continue;

                    yield return new ServiceMappingHandlerInfo
                    {
                        HandlerType = type,
                        Method = method,
                        Name = $"{type.Name}.{method.Name}",
                        Action = methodAttribute.Action,
                        ServiceName = methodAttribute.ServiceName,
                        Priority = methodAttribute.Priority,
                        RunAsync = methodAttribute.RunAsync,
                        Description = classAttribute?.Description ?? $"Handler in {type.Name}"
                    };
                }
            }
        }
    }

    private static IEnumerable<Type> GetTypesFromAssembly(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Return the types that loaded successfully
            return ex.Types.Where(t => t != null)!;
        }
    }
}

/// <summary>
/// Information about a registered service mapping event handler.
/// </summary>
public class ServiceMappingHandlerInfo
{
    /// <inheritdoc/>
    public Type HandlerType { get; set; } = null!;
    /// <inheritdoc/>
    public MethodInfo Method { get; set; } = null!;
    /// <inheritdoc/>
    public string Name { get; set; } = "";
    /// <inheritdoc/>
    public string Action { get; set; } = "*";
    /// <inheritdoc/>
    public string? ServiceName { get; set; }
    /// <inheritdoc/>
    public int Priority { get; set; } = 100;
    /// <inheritdoc/>
    public bool RunAsync { get; set; }
    /// <inheritdoc/>
    public string? Description { get; set; }
}
