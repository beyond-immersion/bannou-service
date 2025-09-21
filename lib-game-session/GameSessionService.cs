using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Dapr.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.GameSession;

/// <summary>
/// Generated service implementation for GameSession API
/// </summary>
[DaprService("game-session", typeof(IGameSessionService), lifetime: ServiceLifetime.Scoped)]
public class GameSessionService : IGameSessionService
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<GameSessionService> _logger;
    private readonly GameSessionServiceConfiguration _configuration;

    public GameSessionService(
        DaprClient daprClient,
        ILogger<GameSessionService> logger,
        GameSessionServiceConfiguration configuration)
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// ListGameSessionsAsync implementation - TODO: Add business logic
    /// </summary>
    public async Task<(StatusCodes, GameSessionListResponse?)> ListGameSessionsAsync(GameType? gameType, Status? status, CancellationToken cancellationToken = default(CancellationToken))
    {
        _logger.LogWarning("Method ListGameSessionsAsync called but not implemented");
        await Task.Delay(1); // Avoid async warning
        throw new NotImplementedException("Method ListGameSessionsAsync is not implemented");
    }

    /// <summary>
    /// CreateGameSessionAsync implementation - TODO: Add business logic
    /// </summary>
    public async Task<(StatusCodes, GameSessionResponse?)> CreateGameSessionAsync(CreateGameSessionRequest body, CancellationToken cancellationToken = default(CancellationToken))
    {
        _logger.LogWarning("Method CreateGameSessionAsync called but not implemented");
        await Task.Delay(1); // Avoid async warning
        throw new NotImplementedException("Method CreateGameSessionAsync is not implemented");
    }

    /// <summary>
    /// GetGameSessionAsync implementation - TODO: Add business logic
    /// </summary>
    public async Task<(StatusCodes, GameSessionResponse?)> GetGameSessionAsync(Guid sessionId, CancellationToken cancellationToken = default(CancellationToken))
    {
        _logger.LogWarning("Method GetGameSessionAsync called but not implemented");
        await Task.Delay(1); // Avoid async warning
        throw new NotImplementedException("Method GetGameSessionAsync is not implemented");
    }

    /// <summary>
    /// JoinGameSessionAsync implementation - TODO: Add business logic
    /// </summary>
    public async Task<(StatusCodes, JoinGameSessionResponse?)> JoinGameSessionAsync(Guid sessionId, JoinGameSessionRequest body, CancellationToken cancellationToken = default(CancellationToken))
    {
        _logger.LogWarning("Method JoinGameSessionAsync called but not implemented");
        await Task.Delay(1); // Avoid async warning
        throw new NotImplementedException("Method JoinGameSessionAsync is not implemented");
    }

    /// <summary>
    /// PerformGameActionAsync implementation - TODO: Add business logic
    /// </summary>
    public async Task<(StatusCodes, GameActionResponse?)> PerformGameActionAsync(Guid sessionId, GameActionRequest body, CancellationToken cancellationToken = default(CancellationToken))
    {
        _logger.LogWarning("Method PerformGameActionAsync called but not implemented");
        await Task.Delay(1); // Avoid async warning
        throw new NotImplementedException("Method PerformGameActionAsync is not implemented");
    }

    /// <summary>
    /// LeaveGameSessionAsync implementation - TODO: Add business logic
    /// </summary>
    public async Task<(StatusCodes, object?)> LeaveGameSessionAsync(Guid sessionId, CancellationToken cancellationToken = default(CancellationToken))
    {
        _logger.LogWarning("Method LeaveGameSessionAsync called but not implemented");
        await Task.Delay(1); // Avoid async warning
        return (StatusCodes.OK, null);
    }

    /// <summary>
    /// KickPlayerAsync implementation - TODO: Add business logic
    /// </summary>
    public async Task<(StatusCodes, object?)> KickPlayerAsync(Guid sessionId, KickPlayerRequest body, CancellationToken cancellationToken = default(CancellationToken))
    {
        _logger.LogWarning("Method KickPlayerAsync called but not implemented");
        await Task.Delay(1); // Avoid async warning
        return (StatusCodes.OK, null);
    }

    /// <summary>
    /// SendChatMessageAsync implementation - TODO: Add business logic
    /// </summary>
    public async Task<(StatusCodes, object?)> SendChatMessageAsync(Guid sessionId, ChatMessageRequest body, CancellationToken cancellationToken = default(CancellationToken))
    {
        _logger.LogWarning("Method SendChatMessageAsync called but not implemented");
        await Task.Delay(1); // Avoid async warning
        return (StatusCodes.OK, null);
    }

    /// <summary>
    /// Registers service permissions extracted from x-permissions sections in the OpenAPI schema.
    /// This method is automatically generated and called during service startup.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {
        try
        {
            var serviceName = GetType().GetServiceName() ?? "game-session";
            _logger?.LogInformation("Registering permissions for {ServiceName} service", serviceName);

            // Build endpoints directly from x-permissions data
            var endpoints = CreateServiceEndpoints();

            // Publish service registration event to Permissions service
            await _daprClient.PublishEventAsync(
                "bannou-pubsub",
                "bannou-service-registered",
                new ServiceRegistrationEvent
                {
                    EventId = Guid.NewGuid().ToString(),
                    Timestamp = DateTime.UtcNow,
                    ServiceId = serviceName,
                    Version = "1.0.0", // TODO: Extract from schema info.version
                    AppId = "bannou", // Default routing
                    Endpoints = endpoints,
                    Metadata = new Dictionary<string, object>
                    {
                        { "generatedFrom", "x-permissions" },
                        { "extractedAt", DateTime.UtcNow },
                        { "endpointCount", endpoints.Count }
                    }
                });

            _logger?.LogInformation("Successfully registered {Count} permission rules for {ServiceName}",
                endpoints.Count, serviceName);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to register permissions for service {ServiceName}", GetType().GetServiceName() ?? "game-session");
            // Don't throw - permission registration failure shouldn't crash the service
        }
    }

    /// <summary>
    /// Create ServiceEndpoint objects from extracted x-permissions data.
    /// </summary>
    private List<ServiceEndpoint> CreateServiceEndpoints()
    {
        var endpoints = new List<ServiceEndpoint>();

        // Permission mapping extracted from x-permissions sections:
        // State -> Role -> Methods
        var permissionData = new Dictionary<string, Dictionary<string, List<string>>>
        {
            ["anonymous"] = new Dictionary<string, List<string>>
            {
                ["user"] = new List<string> { "GET:/sessions", "GET:/sessions/{sessionId}", "POST:/sessions/{sessionId}/actions", "POST:/sessions/{sessionId}/chat", "POST:/sessions/{sessionId}/leave" },
                ["admin"] = new List<string> { "POST:/sessions/{sessionId}/kick" }
            },
            ["authenticated"] = new Dictionary<string, List<string>>
            {
                ["user"] = new List<string> { "POST:/sessions", "POST:/sessions/{sessionId}/join" }
            }
        };

        foreach (var stateEntry in permissionData)
        {
            var stateName = stateEntry.Key;
            var statePermissions = stateEntry.Value;

            foreach (var roleEntry in statePermissions)
            {
                var roleName = roleEntry.Key;
                var methods = roleEntry.Value;

                foreach (var method in methods)
                {
                    var parts = method.Split(':', 2);
                    if (parts.Length == 2 && Enum.TryParse<ServiceEndpointMethod>(parts[0], out var httpMethod))
                    {
                        endpoints.Add(new ServiceEndpoint
                        {
                            Path = parts[1],
                            Method = httpMethod,
                            Permissions = new List<PermissionRequirement>
                            {
                                new PermissionRequirement
                                {
                                    Role = roleName,
                                    RequiredStates = new Dictionary<string, string>
                                    {
                                        { "game-session", stateName }
                                    }
                                }
                            },
                            Description = $"{httpMethod} {parts[1]} ({roleName} in {stateName} state)",
                            Category = "game-session"
                        });
                    }
                }
            }
        }

        return endpoints;
    }
}
