using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Messaging;

/// <summary>
/// Plugin wrapper for Messaging service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class MessagingServicePlugin : StandardServicePlugin<IMessagingService>
{
    public override string PluginName => "messaging";
    public override string DisplayName => "Messaging Service";

    private MessagingServiceConfiguration? _cachedConfig;

    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring messaging service dependencies");

        // Get configuration to read RabbitMQ settings
        // Cache the provider to avoid multiple builds and ensure consistent config
        var tempProvider = services.BuildServiceProvider();
        _cachedConfig = tempProvider.GetService<MessagingServiceConfiguration>();
        var config = _cachedConfig;

        // Check for in-memory mode
        if (config?.UseInMemory == true)
        {
            Logger?.LogWarning(
                "Messaging using IN-MEMORY mode. Messages will NOT be persisted or delivered across processes!");

            // Register in-memory implementation (no RabbitMQ connection)
            services.AddSingleton<InMemoryMessageBus>();
            services.AddSingleton<IMessageBus>(sp => sp.GetRequiredService<InMemoryMessageBus>());
            services.AddSingleton<IMessageSubscriber>(sp => sp.GetRequiredService<InMemoryMessageBus>());

            Logger?.LogDebug("In-memory messaging configured");
            return;
        }

        // Configure MassTransit with RabbitMQ
        var connectionTimeoutSeconds = config?.ConnectionTimeoutSeconds ?? 60;
        var requestTimeoutSeconds = config?.RequestTimeoutSeconds ?? 30;

        Logger?.LogInformation(
            "Configuring MassTransit with connection timeout {ConnectionTimeout}s, request timeout {RequestTimeout}s",
            connectionTimeoutSeconds, requestTimeoutSeconds);

        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();

            x.UsingRabbitMq((context, cfg) =>
            {
                var host = config?.RabbitMQHost ?? "rabbitmq";
                var port = config?.RabbitMQPort ?? 5672;
                var username = config?.RabbitMQUsername ?? "guest";
                var password = config?.RabbitMQPassword ?? "guest";
                var vhost = config?.RabbitMQVirtualHost ?? "/";

                cfg.Host(host, (ushort)port, vhost, h =>
                {
                    h.Username(username);
                    h.Password(password);
                    h.RequestedConnectionTimeout(TimeSpan.FromSeconds(connectionTimeoutSeconds));
                });

                // Configure MassTransit to use BannouJson serializer options for consistency
                cfg.ConfigureJsonSerializerOptions(opts => BannouJson.ApplyBannouSettings(opts));

                // Set send/publish timeout
                cfg.SendTopology.ConfigureErrorSettings = settings =>
                    settings.SetQueueArgument("x-message-ttl", requestTimeoutSeconds * 1000);

                cfg.ConfigureEndpoints(context);
            });
        });

        // Register messaging interfaces
        services.AddSingleton<IMessageBus, MassTransitMessageBus>();
        services.AddSingleton<IMessageSubscriber, MassTransitMessageSubscriber>();

        // Register NativeEventConsumerBackend as IHostedService
        // This bridges RabbitMQ subscriptions to existing IEventConsumer fan-out
        // MANDATORY: This is always registered as messaging is required infrastructure
        services.AddHostedService<NativeEventConsumerBackend>();
        Logger?.LogInformation("Registered NativeEventConsumerBackend - messaging is required infrastructure");

        Logger?.LogDebug("Messaging service dependencies configured");
    }
}
