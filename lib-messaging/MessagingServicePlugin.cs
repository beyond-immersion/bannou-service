using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Note: IMessageBus and IMessageSubscriber interfaces are in BeyondImmersion.BannouService.Services
// Implementations (MassTransitMessageBus, MassTransitMessageSubscriber) are in this plugin

namespace BeyondImmersion.BannouService.Messaging;

/// <summary>
/// Plugin wrapper for Messaging service enabling plugin-based discovery and lifecycle management.
/// Bridges existing IDaprService implementation with the new Plugin system.
/// </summary>
public class MessagingServicePlugin : BaseBannouPlugin
{
    public override string PluginName => "messaging";
    public override string DisplayName => "Messaging Service";

    private IMessagingService? _service;
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Configure services for dependency injection - mimics existing [DaprService] registration.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring messaging service dependencies");

        // Get configuration to read RabbitMQ settings
        var config = services.BuildServiceProvider().GetService<MessagingServiceConfiguration>();

        // Configure MassTransit with RabbitMQ
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
                });

                // Configure endpoints from context
                cfg.ConfigureEndpoints(context);
            });
        });

        // Register messaging interfaces
        services.AddSingleton<IMessageBus, MassTransitMessageBus>();
        services.AddSingleton<IMessageSubscriber, MassTransitMessageSubscriber>();

        // Register NativeEventConsumerBackend as IHostedService
        // This bridges RabbitMQ subscriptions to existing IEventConsumer fan-out
        // MANDATORY: This is always registered as messaging is required infrastructure
        // (lib-messaging cannot be disabled - see PluginLoader.RequiredInfrastructurePlugins)
        services.AddHostedService<NativeEventConsumerBackend>();
        Logger?.LogInformation("Registered NativeEventConsumerBackend - messaging is required infrastructure");

        Logger?.LogDebug("Messaging service dependencies configured");
    }

    /// <summary>
    /// Configure application pipeline - handles controller registration.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        Logger?.LogInformation("Configuring Messaging service application pipeline");

        // The generated MessagingController should already be discovered via standard ASP.NET Core controller discovery
        // since we're not excluding the assembly like we did with IDaprController approach

        // Store service provider for lifecycle management
        _serviceProvider = app.Services;

        Logger?.LogInformation("Messaging service application pipeline configured");
    }

    /// <summary>
    /// Start the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        Logger?.LogInformation("Starting Messaging service");

        try
        {
            // Get service instance from DI container with proper scope handling
            // Note: CreateScope() is required for Scoped services to avoid "Cannot resolve scoped service from root provider" error
            using var scope = _serviceProvider?.CreateScope();
            _service = scope?.ServiceProvider.GetService<IMessagingService>();

            if (_service == null)
            {
                Logger?.LogError("Failed to resolve IMessagingService from DI container");
                return false;
            }

            // Call existing IDaprService.OnStartAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnStartAsync for Messaging service");
                await daprService.OnStartAsync(CancellationToken.None);
            }

            Logger?.LogInformation("Messaging service started successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to start Messaging service");
            return false;
        }
    }

    /// <summary>
    /// Running phase - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (_service == null) return;

        Logger?.LogDebug("Messaging service running");

        try
        {
            // Call existing IDaprService.OnRunningAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnRunningAsync for Messaging service");
                await daprService.OnRunningAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during Messaging service running phase");
        }
    }

    /// <summary>
    /// Shutdown the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnShutdownAsync()
    {
        if (_service == null) return;

        Logger?.LogInformation("Shutting down Messaging service");

        try
        {
            // Call existing IDaprService.OnShutdownAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnShutdownAsync for Messaging service");
                await daprService.OnShutdownAsync();
            }

            Logger?.LogInformation("Messaging service shutdown complete");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during Messaging service shutdown");
        }
    }
}
