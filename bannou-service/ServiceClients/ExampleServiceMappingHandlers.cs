using BeyondImmersion.BannouService.Attributes;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.ServiceClients;

/// <summary>
/// Example service mapping event handlers showing how to use the attribute system.
/// These handlers demonstrate various patterns for responding to service mapping changes.
/// </summary>
[ServiceMappingHandler("Example Handlers", Description = "Demonstration of service mapping event handling patterns")]
public class ExampleServiceMappingHandlers
{
    private readonly ILogger<ExampleServiceMappingHandlers> _logger;

    /// <inheritdoc/>
    public ExampleServiceMappingHandlers(ILogger<ExampleServiceMappingHandlers> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Handles all service registrations with high priority logging.
    /// </summary>
    [ServiceMappingEvent("register", Priority = 1)]
    public async Task OnServiceRegistrationAsync(ServiceMappingEvent eventData, CancellationToken cancellationToken)
    {
        _logger.LogInformation("🟢 Service {ServiceName} registered on app-id {AppId}",
            eventData.ServiceName, eventData.AppId);

        // Example: Could trigger health checks, update load balancers, etc.
        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles service unregistrations.
    /// </summary>
    [ServiceMappingEvent("unregister", Priority = 1)]
    public async Task OnServiceUnregistrationAsync(ServiceMappingEvent eventData, CancellationToken cancellationToken)
    {
        _logger.LogWarning("🔴 Service {ServiceName} unregistered from app-id {AppId}",
            eventData.ServiceName, eventData.AppId);

        // Example: Clean up resources, drain connections, etc.
        await Task.CompletedTask;
    }

    /// <summary>
    /// Specifically handles accounts service events with custom logic.
    /// </summary>
    [ServiceMappingEvent("*", ServiceName = "accounts", Priority = 50)]
    public async Task OnAccountsServiceEventAsync(string serviceName, string appId, string action, CancellationToken cancellationToken)
    {
        _logger.LogDebug("📊 Accounts service event: {Action} on {AppId}", action, appId);

        // Example: Could invalidate caches, notify other services, etc.
        switch (action?.ToLowerInvariant())
        {
            case "register":
                _logger.LogInformation("Accounts service is now available on {AppId}", appId);
                break;
            case "unregister":
                _logger.LogWarning("Accounts service is no longer available");
                break;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Asynchronous handler for service updates (runs in parallel with other async handlers).
    /// </summary>
    [ServiceMappingEvent("update", RunAsync = true, Priority = 100)]
    public async Task OnServiceUpdateAsync(ServiceMappingEvent eventData)
    {
        _logger.LogDebug("🔄 Service {ServiceName} updated on {AppId} (async handler)",
            eventData.ServiceName, eventData.AppId);

        // Example: Long-running operations that don't block other handlers
        await Task.Delay(100); // Simulate work
    }

    /// <summary>
    /// Catch-all handler for any service mapping events (low priority).
    /// </summary>
    [ServiceMappingEvent("*", Priority = 999)]
    public void OnAnyServiceEventAsync(ServiceMappingEvent eventData)
    {
        var metadata = eventData.Metadata?.Count > 0
            ? $" (metadata: {string.Join(", ", eventData.Metadata.Select(kv => $"{kv.Key}={kv.Value}"))})"
            : "";

        _logger.LogTrace("📋 Service mapping event: {Action} {ServiceName} -> {AppId}{Metadata}",
            eventData.Action, eventData.ServiceName, eventData.AppId, metadata);
    }
}
