using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
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

        // Register named HttpClient for subscription callbacks (FOUNDATION TENETS: use IHttpClientFactory)
        services.AddHttpClient(MessagingService.HttpClientName);
        Logger?.LogDebug("Registered named HttpClient '{ClientName}' for subscription callbacks", MessagingService.HttpClientName);

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

            // Register in-memory message tap
            services.AddSingleton<IMessageTap, InMemoryMessageTap>();
            Logger?.LogDebug("Registered InMemoryMessageTap for in-memory messaging");

            Logger?.LogDebug("In-memory messaging configured");
            return;
        }

        // Configure direct RabbitMQ (no MassTransit)
        Logger?.LogInformation("Configuring direct RabbitMQ messaging (no MassTransit)");

        // Register shared connection manager
        services.AddSingleton<RabbitMQConnectionManager>();

        // Register retry buffer for handling transient publish failures
        services.AddSingleton<MessageRetryBuffer>();

        // Register messaging interfaces with direct RabbitMQ implementations
        services.AddSingleton<IMessageBus, RabbitMQMessageBus>();
        services.AddSingleton<IMessageSubscriber, RabbitMQMessageSubscriber>();

        // Register message tap for forwarding events between exchanges
        services.AddSingleton<IMessageTap, RabbitMQMessageTap>();
        Logger?.LogDebug("Registered RabbitMQMessageTap for event tapping");

        // Register NativeEventConsumerBackend as IHostedService
        // This bridges RabbitMQ subscriptions to existing IEventConsumer fan-out
        // MANDATORY: This is always registered as messaging is required infrastructure
        services.AddHostedService<NativeEventConsumerBackend>();
        Logger?.LogInformation("Registered NativeEventConsumerBackend - messaging is required infrastructure");

        Logger?.LogDebug("Messaging service dependencies configured");
    }
}
