using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Events;

/// <summary>
/// Extension methods for registering <see cref="IEventConsumer"/> with DI.
/// </summary>
public static class EventConsumerExtensions
{
    /// <summary>
    /// Adds the <see cref="IEventConsumer"/> singleton service to the DI container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddEventConsumer(this IServiceCollection services)
    {
        services.AddSingleton<IEventConsumer, EventConsumer>();
        return services;
    }

    /// <summary>
    /// Helper method to register an event handler with less boilerplate.
    /// </summary>
    /// <typeparam name="TService">The service type that handles the event.</typeparam>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="eventConsumer">The event consumer.</param>
    /// <param name="topicName">The Dapr topic name.</param>
    /// <param name="handler">The handler method on the service.</param>
    public static void RegisterHandler<TService, TEvent>(
        this IEventConsumer eventConsumer,
        string topicName,
        Func<TService, TEvent, Task> handler)
        where TService : class
        where TEvent : class
    {
        var handlerKey = $"{typeof(TService).Name}:{topicName}";

        eventConsumer.Register<TEvent>(
            topicName,
            handlerKey,
            async (sp, evt) =>
            {
                var service = sp.GetRequiredService<TService>();
                await handler(service, evt);
            });
    }
}
