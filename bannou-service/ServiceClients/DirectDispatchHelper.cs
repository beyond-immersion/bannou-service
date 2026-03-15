#nullable enable

using System.Collections.Concurrent;
using System.Reflection;

namespace BeyondImmersion.BannouService.ServiceClients;

/// <summary>
/// Provides zero-serialization direct dispatch from generated mesh clients to service implementations.
/// Used in embedded and sidecar deployment modes where services run in-process.
/// Resolves service interfaces by naming convention (AccountClient → IAccountService)
/// and invokes methods via cached reflection, adapting the (StatusCodes, TResponse?) tuple
/// return to the client's TResponse / ApiException pattern.
/// </summary>
public static class DirectDispatchHelper
{
    private static readonly ConcurrentDictionary<string, Type?> _serviceTypeCache = new();
    private static readonly ConcurrentDictionary<(Type serviceType, string methodName), MethodInfo?> _methodCache = new();

    /// <summary>
    /// Invokes a service method directly via DI, bypassing HTTP mesh transport.
    /// Resolves the service interface by convention from the service name,
    /// calls the method, and adapts the (StatusCodes, TResponse?) return pattern.
    /// </summary>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="serviceProvider">The DI service provider for scope creation.</param>
    /// <param name="serviceName">The service name (e.g., "account", "character").</param>
    /// <param name="methodName">The async method name (e.g., "CreateAccountAsync").</param>
    /// <param name="body">The request body to pass to the service method.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized response, or throws ApiException on non-success status.</returns>
    public static async Task<TResponse> InvokeAsync<TResponse>(
        IServiceProvider serviceProvider,
        string serviceName,
        string methodName,
        object body,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        var serviceType = ResolveServiceType(serviceName);

        using var scope = serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetService(serviceType)
            ?? throw new InvalidOperationException(
                $"Service interface {serviceType.FullName} not registered in DI. " +
                $"Ensure the plugin for '{serviceName}' is loaded.");

        var method = ResolveMethod(serviceType, methodName);

        var task = method.Invoke(service, new[] { body, cancellationToken })
            ?? throw new InvalidOperationException(
                $"Method {methodName} on {serviceType.Name} returned null.");

        await (Task)task;

        // Extract (StatusCodes, TResponse?) tuple from Task<ValueTuple<StatusCodes, TResponse?>>
        var resultProp = task.GetType().GetProperty("Result")!;
        var tuple = resultProp.GetValue(task)!;
        var item1Field = tuple.GetType().GetField("Item1")!;
        var item2Field = tuple.GetType().GetField("Item2")!;
        var status = (int)item1Field.GetValue(tuple)!;
        var response = item2Field.GetValue(tuple);

        if (status >= 200 && status < 300)
            return (TResponse)response!;

        throw new BeyondImmersion.Bannou.Core.ApiException(
            $"Service returned status {status}", status, null, null, null);
    }

    /// <summary>
    /// Invokes a service method that returns no meaningful response body.
    /// Used for void-equivalent endpoints where only the status code matters.
    /// </summary>
    public static async Task InvokeVoidAsync(
        IServiceProvider serviceProvider,
        string serviceName,
        string methodName,
        object body,
        CancellationToken cancellationToken)
    {
        var serviceType = ResolveServiceType(serviceName);

        using var scope = serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetService(serviceType)
            ?? throw new InvalidOperationException(
                $"Service interface {serviceType.FullName} not registered in DI.");

        var method = ResolveMethod(serviceType, methodName);

        var task = method.Invoke(service, new[] { body, cancellationToken })
            ?? throw new InvalidOperationException(
                $"Method {methodName} on {serviceType.Name} returned null.");

        await (Task)task;

        var resultProp = task.GetType().GetProperty("Result")!;
        var tuple = resultProp.GetValue(task)!;
        var item1Field = tuple.GetType().GetField("Item1")!;
        var status = (int)item1Field.GetValue(tuple)!;

        if (status < 200 || status >= 300)
            throw new BeyondImmersion.Bannou.Core.ApiException(
                $"Service returned status {status}", status, null, null, null);
    }

    /// <summary>
    /// Resolves the service interface type by naming convention.
    /// AccountClient (serviceName="account") → IAccountService.
    /// </summary>
    private static Type ResolveServiceType(string serviceName)
    {
        return _serviceTypeCache.GetOrAdd(serviceName, name =>
        {
            // Convert "account" → "Account", "game-session" → "GameSession"
            var pascalName = string.Join("", name.Split('-')
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
            var interfaceName = $"I{pascalName}Service";

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.IsInterface && type.Name == interfaceName)
                            return type;
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    // Skip assemblies that can't be loaded
                }
            }

            return null;
        }) ?? throw new InvalidOperationException(
            $"Could not find service interface for '{serviceName}'. " +
            $"Expected I{char.ToUpperInvariant(serviceName[0])}{serviceName[1..]}Service.");
    }

    /// <summary>
    /// Resolves and caches the MethodInfo for a service method.
    /// </summary>
    private static MethodInfo ResolveMethod(Type serviceType, string methodName)
    {
        return _methodCache.GetOrAdd((serviceType, methodName), key =>
            key.serviceType.GetMethod(key.methodName))
            ?? throw new InvalidOperationException(
                $"Method {methodName} not found on {serviceType.Name}.");
    }
}
