using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Extension methods for conditional service registration in Plugin.ConfigureServices.
/// Using these methods signals to structural tests that the registration is intentionally
/// manual and conditional — the type cannot use [BannouHelperService] auto-registration
/// because it is only registered under specific runtime conditions (e.g., messaging mode
/// branches where different backends require different implementations).
/// </summary>
/// <remarks>
/// <para>
/// <b>Structural test integration:</b> The structural test
/// <c>HostedServices_ShouldUseBannouHelperServiceAttribute</c> recognizes
/// <c>AddConditionalHostedService</c> calls as approved self-registration patterns.
/// Types registered via these methods do not need to be in an exception list.
/// </para>
/// <para>
/// <b>When to use:</b> Only when the type cannot use attribute-based auto-registration
/// because its registration is conditional on runtime configuration (backend mode,
/// feature flags, deployment topology). If the type is always registered, use
/// <c>[BannouHelperService(RegistrationMode = HelperRegistrationMode.HostedService)]</c> instead.
/// </para>
/// </remarks>
public static class ConditionalServiceRegistration
{
    /// <summary>
    /// Conditionally registers a hosted service. The condition is evaluated immediately
    /// during ConfigureServices. If the condition returns false, registration is skipped.
    /// </summary>
    /// <typeparam name="T">The hosted service type (must implement IHostedService).</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="condition">
    /// Condition evaluated at registration time. Only registers if the delegate returns true.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddConditionalHostedService<T>(
        this IServiceCollection services,
        Func<bool> condition) where T : class, IHostedService
    {
        if (!condition())
            return services;

        services.AddSingleton<IHostedService, T>();
        return services;
    }

    /// <summary>
    /// Conditionally registers a service with interface → implementation mapping.
    /// The condition is evaluated immediately during ConfigureServices.
    /// </summary>
    /// <typeparam name="TInterface">The service interface type.</typeparam>
    /// <typeparam name="TImplementation">The implementation type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="lifetime">DI lifetime for the registration.</param>
    /// <param name="condition">
    /// Condition evaluated at registration time. Only registers if the delegate returns true.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddConditional<TInterface, TImplementation>(
        this IServiceCollection services,
        ServiceLifetime lifetime,
        Func<bool> condition)
        where TInterface : class
        where TImplementation : class, TInterface
    {
        if (!condition())
            return services;

        services.Add(new ServiceDescriptor(typeof(TInterface), typeof(TImplementation), lifetime));
        return services;
    }

    /// <summary>
    /// Conditionally registers a concrete type service (no interface mapping).
    /// The condition is evaluated immediately during ConfigureServices.
    /// </summary>
    /// <typeparam name="T">The concrete service type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="lifetime">DI lifetime for the registration.</param>
    /// <param name="condition">
    /// Condition evaluated at registration time. Only registers if the delegate returns true.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddConditional<T>(
        this IServiceCollection services,
        ServiceLifetime lifetime,
        Func<bool> condition) where T : class
    {
        if (!condition())
            return services;

        services.Add(new ServiceDescriptor(typeof(T), typeof(T), lifetime));
        return services;
    }
}
