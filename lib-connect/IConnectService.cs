using Microsoft.AspNetCore.Mvc;
using BeyondImmersion.BannouService.Controllers.Generated;
using System.Net.WebSockets;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Interface for WebSocket Connect service operations.
/// Implements the WebSocket-first edge gateway for zero-copy message routing.
/// </summary>
public interface IConnectService
{
    /// <summary>
    /// Gets available services for a client based on authentication state.
    /// </summary>
    Task<ActionResult<ServiceMappingsResponse>> GetServicesAsync(
        string? authorization = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets connection status and metrics.
    /// </summary>
    Task<ActionResult<ConnectionStatusResponse>> GetStatusAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles WebSocket upgrade and connection management.
    /// </summary>
    Task HandleWebSocketAsync(
        WebSocket webSocket,
        string? authorization = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a client connection with the service.
    /// </summary>
    Task<string> RegisterClientAsync(
        WebSocket webSocket,
        string? authorization = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unregisters a client connection.
    /// </summary>
    Task UnregisterClientAsync(
        string clientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Routes a message to the appropriate destination (service or client).
    /// </summary>
    Task<bool> RouteMessageAsync(
        ReadOnlyMemory<byte> message,
        string clientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message to a specific client.
    /// </summary>
    Task<bool> SendToClientAsync(
        string clientId,
        ReadOnlyMemory<byte> message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcasts a message to all connected clients.
    /// </summary>
    Task BroadcastAsync(
        ReadOnlyMemory<byte> message,
        CancellationToken cancellationToken = default);
}