using Dapr.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.ServiceClients;

/// <summary>
/// Publisher for service mapping events via Dapr pub/sub to RabbitMQ.
/// Announces service registration, updates, and shutdowns for dynamic routing.
/// </summary>
public interface IServiceMappingEventPublisher
{
    /// <summary>
    /// Announces that a service is starting and should be mapped to this app-id.
    /// </summary>
    Task AnnounceServiceStartupAsync(string serviceName, string appId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Announces that a service is shutting down and should be unmapped.
    /// </summary>
    Task AnnounceServiceShutdownAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates service mapping (for configuration changes or health status updates).
    /// </summary>
    Task UpdateServiceMappingAsync(string serviceName, string appId, Dictionary<string, object>? metadata = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation of service mapping event publisher using Dapr.
/// </summary>
public class ServiceMappingEventPublisher : IServiceMappingEventPublisher
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<ServiceMappingEventPublisher> _logger;
    private const string PUB_SUB_NAME = "bannou-pubsub";
    private const string TOPIC_NAME = "bannou-service-mappings";

    public ServiceMappingEventPublisher(
        DaprClient daprClient,
        ILogger<ServiceMappingEventPublisher> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
    }

    public async Task AnnounceServiceStartupAsync(string serviceName, string appId, CancellationToken cancellationToken = default)
    {
        var eventData = new ServiceMappingEvent
        {
            ServiceName = serviceName,
            AppId = appId,
            Action = "register",
            Metadata = new Dictionary<string, object>
            {
                ["source"] = "startup",
                ["environment"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown"
            }
        };

        await PublishEventAsync(eventData, cancellationToken);
        _logger.LogInformation("Announced service startup: {ServiceName} -> {AppId}", serviceName, appId);
    }

    public async Task AnnounceServiceShutdownAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        var eventData = new ServiceMappingEvent
        {
            ServiceName = serviceName,
            AppId = "", // Empty when shutting down
            Action = "unregister",
            Metadata = new Dictionary<string, object>
            {
                ["source"] = "shutdown"
            }
        };

        await PublishEventAsync(eventData, cancellationToken);
        _logger.LogInformation("Announced service shutdown: {ServiceName}", serviceName);
    }

    public async Task UpdateServiceMappingAsync(string serviceName, string appId, Dictionary<string, object>? metadata = null, CancellationToken cancellationToken = default)
    {
        var eventData = new ServiceMappingEvent
        {
            ServiceName = serviceName,
            AppId = appId,
            Action = "update",
            Metadata = metadata ?? new Dictionary<string, object>()
        };

        eventData.Metadata["source"] = "update";

        await PublishEventAsync(eventData, cancellationToken);
        _logger.LogInformation("Updated service mapping: {ServiceName} -> {AppId}", serviceName, appId);
    }

    private async Task PublishEventAsync(ServiceMappingEvent eventData, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Publishing service mapping event {EventId}: {Action} {ServiceName} -> {AppId}",
                eventData.EventId, eventData.Action, eventData.ServiceName, eventData.AppId);

            await _daprClient.PublishEventAsync(PUB_SUB_NAME, TOPIC_NAME, eventData, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish service mapping event {EventId}: {Action} {ServiceName}",
                eventData.EventId, eventData.Action, eventData.ServiceName);
            throw;
        }
    }
}

/// <summary>
/// Background service that automatically announces service startup and shutdown events.
/// Integrates with ASP.NET Core hosting lifecycle to manage service mappings.
/// </summary>
public class ServiceMappingLifecycleService : BackgroundService
{
    private readonly IServiceMappingEventPublisher _eventPublisher;
    private readonly ILogger<ServiceMappingLifecycleService> _logger;
    private readonly string _serviceName;
    private readonly string _appId;

    public ServiceMappingLifecycleService(
        IServiceMappingEventPublisher eventPublisher,
        ILogger<ServiceMappingLifecycleService> logger,
        IConfiguration configuration)
    {
        _eventPublisher = eventPublisher;
        _logger = logger;

        // Get service configuration
        _serviceName = configuration.GetValue<string>("ServiceName") ?? "unknown-service";
        _appId = configuration.GetValue<string>("Dapr:AppId") ??
                Environment.GetEnvironmentVariable("DAPR_APP_ID") ??
                "bannou";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Wait a bit for Dapr to initialize
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            // Announce service startup
            await _eventPublisher.AnnounceServiceStartupAsync(_serviceName, _appId, stoppingToken);

            // Keep service alive and periodically send heartbeats
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Send periodic health updates (every 5 minutes)
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

                    if (!stoppingToken.IsCancellationRequested)
                    {
                        await _eventPublisher.UpdateServiceMappingAsync(_serviceName, _appId,
                            new Dictionary<string, object> { ["healthCheck"] = DateTime.UtcNow },
                            stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when shutting down
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send service mapping health update");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Service mapping lifecycle service encountered an error");
        }
        finally
        {
            // Announce service shutdown
            try
            {
                await _eventPublisher.AnnounceServiceShutdownAsync(_serviceName, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to announce service shutdown");
            }
        }
    }
}
