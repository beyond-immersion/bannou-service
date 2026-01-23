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
        try
        {
            var handlerKey = GetHandlerKey(body.AssetType);
            var existing = await HandlerStore.GetAsync(handlerKey, cancellationToken);

            if (existing != null)
            {
                _logger.LogWarning("Handler for asset type {AssetType} already registered by plugin {PluginId}",
                    body.AssetType, existing.PluginId);
                return (StatusCodes.Status400BadRequest, new RegisterHandlerResponse
                {
                    Registered = false
                });
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

            await HandlerStore.SaveAsync(handlerKey, handlerModel, cancellationToken);

            _logger.LogInformation("Registered handler for asset type {AssetType} from plugin {PluginId}",
                body.AssetType, body.PluginId);

            return (StatusCodes.Status200OK, new RegisterHandlerResponse
            {
                Registered = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register handler for asset type {AssetType}", body.AssetType);
            await EmitErrorAsync("RegisterHandler", ex.Message, new { body.AssetType, body.PluginId }, cancellationToken);
            return (StatusCodes.Status500InternalServerError, new RegisterHandlerResponse
            {
                Registered = false
            });
        }
    }

    /// <summary>
    /// Lists all registered asset handlers.
    /// </summary>
    public async Task<(StatusCodes, ListHandlersResponse?)> ListHandlersAsync(
        ListHandlersRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var allHandlers = await HandlerStore.QueryAsync(h => true, cancellationToken);

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

            return (StatusCodes.Status200OK, new ListHandlersResponse
            {
                Handlers = handlerInfos
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list asset handlers");
            await EmitErrorAsync("ListHandlers", ex.Message, new { }, cancellationToken);
            return (StatusCodes.Status500InternalServerError, new ListHandlersResponse
            {
                Handlers = new List<AssetHandlerInfo>()
            });
        }
    }

    /// <summary>
    /// Deregisters a custom asset type handler.
    /// </summary>
    public async Task<(StatusCodes, DeregisterHandlerResponse?)> DeregisterHandlerAsync(
        DeregisterHandlerRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var handlerKey = GetHandlerKey(body.AssetType);
            var existing = await HandlerStore.GetAsync(handlerKey, cancellationToken);

            if (existing == null)
            {
                return (StatusCodes.Status404NotFound, new DeregisterHandlerResponse
                {
                    Deregistered = false
                });
            }

            if (existing.BuiltIn)
            {
                _logger.LogWarning("Cannot deregister built-in handler for asset type {AssetType}", body.AssetType);
                return (StatusCodes.Status400BadRequest, new DeregisterHandlerResponse
                {
                    Deregistered = false
                });
            }

            await HandlerStore.DeleteAsync(handlerKey, cancellationToken);

            _logger.LogInformation("Deregistered handler for asset type {AssetType}", body.AssetType);

            return (StatusCodes.Status200OK, new DeregisterHandlerResponse
            {
                Deregistered = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deregister handler for asset type {AssetType}", body.AssetType);
            await EmitErrorAsync("DeregisterHandler", ex.Message, new { body.AssetType }, cancellationToken);
            return (StatusCodes.Status500InternalServerError, new DeregisterHandlerResponse
            {
                Deregistered = false
            });
        }
    }
}
