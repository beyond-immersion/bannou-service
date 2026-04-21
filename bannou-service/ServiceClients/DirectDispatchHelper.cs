#nullable enable

using Microsoft.Extensions.DependencyInjection;
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
/// <remarks>
/// KNOWN ISSUE (tracked in issue #724): this reflection-based implementation is NOT
/// AOT-compatible. Phase 4 (interface relocation) is complete — interfaces now live in
/// <c>bannou-service/Generated/Services/</c>. Phase 5 (typed template + helper) requires
/// coordinated changes to <c>scripts/generate-client.sh</c> (to strip x-controller-only
/// operations from the client schema so template-emitted typed dispatch calls always
/// resolve to existing interface methods) and the NSwag template itself. Until that
/// lands, this reflection path remains the interim surface.
/// </remarks>
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
    /// Invokes a service method directly via DI with fully-typed generic dispatch.
    /// TService, TRequest, and TResponse are closed at compile time; the caller supplies
    /// a static lambda capturing the concrete interface method. Zero reflection.
    /// Adapts the (StatusCodes, TResponse?) tuple return to the client's
    /// TResponse / ApiException pattern.
    /// </summary>
    /// <typeparam name="TService">The service interface type (e.g., IAccountService).</typeparam>
    /// <typeparam name="TRequest">The request body type.</typeparam>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="serviceProvider">The DI service provider for scope creation.</param>
    /// <param name="body">The request body to pass to the service method.</param>
    /// <param name="method">Delegate invoking the target method on the resolved service instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized response, or throws ApiException on non-success status.</returns>
    public static async Task<TResponse> InvokeDirectAsync<TService, TRequest, TResponse>(
        IServiceProvider serviceProvider,
        TRequest body,
        Func<TService, TRequest, CancellationToken, Task<(StatusCodes, TResponse?)>> method,
        CancellationToken cancellationToken)
        where TService : class
        where TResponse : class
    {
        using var scope = serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TService>();

        var (status, response) = await method(service, body, cancellationToken).ConfigureAwait(false);
        int statusInt = (int)status;
        if (statusInt >= 200 && statusInt < 300)
        {
            if (response == null)
                throw new InvalidOperationException(
                    $"Service {typeof(TService).Name} returned success status {statusInt} with null response.");
            return response;
        }
        throw new BeyondImmersion.Bannou.Core.ApiException(
            $"Service returned status {statusInt}", statusInt, null, null, null);
    }

    /// <summary>
    /// Invokes a void-returning service method directly via DI with fully-typed generic dispatch.
    /// Used for endpoints whose interface methods return Task&lt;StatusCodes&gt; (no payload).
    /// </summary>
    /// <typeparam name="TService">The service interface type.</typeparam>
    /// <typeparam name="TRequest">The request body type.</typeparam>
    /// <param name="serviceProvider">The DI service provider for scope creation.</param>
    /// <param name="body">The request body to pass to the service method.</param>
    /// <param name="method">Delegate invoking the target method on the resolved service instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task InvokeDirectVoidAsync<TService, TRequest>(
        IServiceProvider serviceProvider,
        TRequest body,
        Func<TService, TRequest, CancellationToken, Task<StatusCodes>> method,
        CancellationToken cancellationToken)
        where TService : class
    {
        using var scope = serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TService>();

        var status = await method(service, body, cancellationToken).ConfigureAwait(false);
        int statusInt = (int)status;
        if (statusInt < 200 || statusInt >= 300)
            throw new BeyondImmersion.Bannou.Core.ApiException(
                $"Service returned status {statusInt}", statusInt, null, null, null);
    }

    /// <summary>
    /// Direct dispatch overload for endpoints with an <c>x-from-authorization</c> parameter.
    /// The extracted token (already stripped of the "Bearer " prefix by the client template)
    /// is passed as the service method's typed first argument, matching the service-interface
    /// shape <c>MethodAsync(string jwt, TRequest body, CancellationToken ct)</c>.
    /// </summary>
    /// <typeparam name="TService">The service interface type (e.g., IAuthService).</typeparam>
    /// <typeparam name="TRequest">The request body type.</typeparam>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="serviceProvider">The DI service provider for scope creation.</param>
    /// <param name="token">The extracted authorization token (no "Bearer " prefix).</param>
    /// <param name="body">The request body to pass to the service method.</param>
    /// <param name="method">Delegate invoking the target method on the resolved service instance,
    /// accepting (service, token, body, cancellationToken).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response payload, or throws ApiException on non-success status.</returns>
    public static async Task<TResponse> InvokeDirectWithAuthAsync<TService, TRequest, TResponse>(
        IServiceProvider serviceProvider,
        string token,
        TRequest body,
        Func<TService, string, TRequest, CancellationToken, Task<(StatusCodes, TResponse?)>> method,
        CancellationToken cancellationToken)
        where TService : class
        where TResponse : class
    {
        using var scope = serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TService>();

        var (status, response) = await method(service, token, body, cancellationToken).ConfigureAwait(false);
        int statusInt = (int)status;
        if (statusInt >= 200 && statusInt < 300)
        {
            if (response == null)
                throw new InvalidOperationException(
                    $"Service {typeof(TService).Name} returned success status {statusInt} with null response.");
            return response;
        }
        throw new BeyondImmersion.Bannou.Core.ApiException(
            $"Service returned status {statusInt}", statusInt, null, null, null);
    }

    /// <summary>
    /// Void-returning variant of <see cref="InvokeDirectWithAuthAsync{TService,TRequest,TResponse}"/>
    /// for endpoints whose interface methods return <see cref="Task{TResult}"/> of <see cref="StatusCodes"/>
    /// (no payload).
    /// </summary>
    /// <typeparam name="TService">The service interface type.</typeparam>
    /// <typeparam name="TRequest">The request body type.</typeparam>
    /// <param name="serviceProvider">The DI service provider for scope creation.</param>
    /// <param name="token">The extracted authorization token (no "Bearer " prefix).</param>
    /// <param name="body">The request body to pass to the service method.</param>
    /// <param name="method">Delegate invoking the target method on the resolved service instance,
    /// accepting (service, token, body, cancellationToken).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task InvokeDirectWithAuthVoidAsync<TService, TRequest>(
        IServiceProvider serviceProvider,
        string token,
        TRequest body,
        Func<TService, string, TRequest, CancellationToken, Task<StatusCodes>> method,
        CancellationToken cancellationToken)
        where TService : class
    {
        using var scope = serviceProvider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TService>();

        var status = await method(service, token, body, cancellationToken).ConfigureAwait(false);
        int statusInt = (int)status;
        if (statusInt < 200 || statusInt >= 300)
            throw new BeyondImmersion.Bannou.Core.ApiException(
                $"Service returned status {statusInt}", statusInt, null, null, null);
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
