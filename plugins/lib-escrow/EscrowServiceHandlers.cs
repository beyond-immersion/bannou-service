using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Escrow;

/// <summary>
/// Handler registration operations for escrow management.
/// Manages custom asset type handlers for extensibility.
/// </summary>
public partial class EscrowService
{
    /// <summary>
    /// Registers a custom asset type handler.
    /// </summary>
    public async Task<(StatusCodes, RegisterHandlerResponse?)> RegisterHandlerAsync(
        RegisterHandlerRequest body,
        CancellationToken cancellationToken = default)
    {
        var handlerKey = BuildHandlerKey(body.AssetType);
        var existing = await _handlerStore.GetAsync(handlerKey, cancellationToken);

        if (existing != null)
        {
            _logger.LogWarning("Handler for asset type {AssetType} already registered by plugin {PluginId}",
                body.AssetType, existing.PluginId);
            return (StatusCodes.BadRequest, null);
        }

        var now = DateTimeOffset.UtcNow;

        var handlerModel = new AssetHandlerModel
        {
            AssetType = body.AssetType,
            PluginId = body.PluginId,
            BuiltIn = false,
            DepositEndpoint = body.DepositEndpoint,
            ReleaseEndpoint = body.ReleaseEndpoint,
            RefundEndpoint = body.RefundEndpoint,
            ValidateEndpoint = body.ValidateEndpoint,
            RegisteredAt = now
        };

        await _handlerStore.SaveAsync(handlerKey, handlerModel, cancellationToken: cancellationToken);

        _logger.LogInformation("Registered handler for asset type {AssetType} from plugin {PluginId}",
            body.AssetType, body.PluginId);

        return (StatusCodes.OK, new RegisterHandlerResponse());
    }

    /// <summary>
    /// Lists all registered asset handlers.
    /// </summary>
    public async Task<(StatusCodes, ListHandlersResponse?)> ListHandlersAsync(
        ListHandlersRequest body,
        CancellationToken cancellationToken = default)
    {
        var allHandlers = await _handlerStore.QueryAsync(h => true, cancellationToken);

        var handlerInfos = new List<AssetHandlerInfo>();
        foreach (var handler in allHandlers)
        {
            handlerInfos.Add(new AssetHandlerInfo
            {
                AssetType = handler.AssetType,
                PluginId = handler.PluginId,
                BuiltIn = handler.BuiltIn,
                DepositEndpoint = handler.DepositEndpoint,
                ReleaseEndpoint = handler.ReleaseEndpoint,
                RefundEndpoint = handler.RefundEndpoint,
                ValidateEndpoint = handler.ValidateEndpoint
            });
        }

        return (StatusCodes.OK, new ListHandlersResponse
        {
            Handlers = handlerInfos
        });
    }

    /// <summary>
    /// Deregisters a custom asset type handler.
    /// </summary>
    public async Task<(StatusCodes, DeregisterHandlerResponse?)> DeregisterHandlerAsync(
        DeregisterHandlerRequest body,
        CancellationToken cancellationToken = default)
    {
        var handlerKey = BuildHandlerKey(body.AssetType);
        var existing = await _handlerStore.GetAsync(handlerKey, cancellationToken);

        if (existing == null)
        {
            return (StatusCodes.NotFound, null);
        }

        if (existing.BuiltIn)
        {
            _logger.LogWarning("Cannot deregister built-in handler for asset type {AssetType}", body.AssetType);
            return (StatusCodes.BadRequest, null);
        }

        await _handlerStore.DeleteAsync(handlerKey, cancellationToken);

        _logger.LogInformation("Deregistered handler for asset type {AssetType}", body.AssetType);

        return (StatusCodes.OK, new DeregisterHandlerResponse());
    }
}
