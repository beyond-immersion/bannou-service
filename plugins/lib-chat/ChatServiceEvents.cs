using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Chat;

/// <summary>
/// Partial class for ChatService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPLEMENTATION TENETS - Event Consumer Fan-Out:</b> Registers IEventConsumer handlers
/// for contract lifecycle events. When a governing contract changes state, finds all rooms
/// bound to that contract and applies the configured action (lock, archive, delete, continue).
/// </para>
/// </remarks>
public partial class ChatService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IChatService, ContractFulfilledEvent>(
            "contract.fulfilled",
            async (svc, evt) => await ((ChatService)svc).HandleContractFulfilledAsync(evt));

        eventConsumer.RegisterHandler<IChatService, ContractBreachDetectedEvent>(
            "contract.breach.detected",
            async (svc, evt) => await ((ChatService)svc).HandleContractBreachDetectedAsync(evt));

        eventConsumer.RegisterHandler<IChatService, ContractTerminatedEvent>(
            "contract.terminated",
            async (svc, evt) => await ((ChatService)svc).HandleContractTerminatedAsync(evt));

        eventConsumer.RegisterHandler<IChatService, ContractExpiredEvent>(
            "contract.expired",
            async (svc, evt) => await ((ChatService)svc).HandleContractExpiredAsync(evt));

    }

    /// <summary>
    /// Handles contract fulfillment by applying the configured action to affected rooms.
    /// </summary>
    /// <param name="evt">The contract fulfilled event data.</param>
    public async Task HandleContractFulfilledAsync(ContractFulfilledEvent evt)
    {
        _logger.LogInformation("Received contract fulfilled event for contract {ContractId}", evt.ContractId);

        try
        {
            var rooms = await FindRoomsByContractIdAsync(evt.ContractId, CancellationToken.None);
            foreach (var room in rooms)
            {
                var action = room.ContractFulfilledAction ?? _configuration.DefaultContractFulfilledAction;
                await ExecuteContractRoomActionAsync(room, action, ChatRoomLockReason.ContractFulfilled, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling contract fulfilled event for contract {ContractId}", evt.ContractId);
            await _messageBus.TryPublishErrorAsync(
                "chat", "HandleContractFulfilled", ex.GetType().Name, ex.Message,
                severity: ServiceErrorEventSeverity.Error);
        }
    }

    /// <summary>
    /// Handles contract breach by applying the configured action to affected rooms.
    /// </summary>
    /// <param name="evt">The contract breach detected event data.</param>
    public async Task HandleContractBreachDetectedAsync(ContractBreachDetectedEvent evt)
    {
        _logger.LogInformation("Received contract breach detected event for contract {ContractId}", evt.ContractId);

        try
        {
            var rooms = await FindRoomsByContractIdAsync(evt.ContractId, CancellationToken.None);
            foreach (var room in rooms)
            {
                var action = room.ContractBreachAction ?? _configuration.DefaultContractBreachAction;
                await ExecuteContractRoomActionAsync(room, action, ChatRoomLockReason.ContractBreachDetected, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling contract breach event for contract {ContractId}", evt.ContractId);
            await _messageBus.TryPublishErrorAsync(
                "chat", "HandleContractBreachDetected", ex.GetType().Name, ex.Message,
                severity: ServiceErrorEventSeverity.Error);
        }
    }

    /// <summary>
    /// Handles contract termination by applying the configured action to affected rooms.
    /// </summary>
    /// <param name="evt">The contract terminated event data.</param>
    public async Task HandleContractTerminatedAsync(ContractTerminatedEvent evt)
    {
        _logger.LogInformation("Received contract terminated event for contract {ContractId}", evt.ContractId);

        try
        {
            var rooms = await FindRoomsByContractIdAsync(evt.ContractId, CancellationToken.None);
            foreach (var room in rooms)
            {
                var action = room.ContractTerminatedAction ?? _configuration.DefaultContractTerminatedAction;
                await ExecuteContractRoomActionAsync(room, action, ChatRoomLockReason.ContractTerminated, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling contract terminated event for contract {ContractId}", evt.ContractId);
            await _messageBus.TryPublishErrorAsync(
                "chat", "HandleContractTerminated", ex.GetType().Name, ex.Message,
                severity: ServiceErrorEventSeverity.Error);
        }
    }

    /// <summary>
    /// Handles contract expiration by applying the configured action to affected rooms.
    /// </summary>
    /// <param name="evt">The contract expired event data.</param>
    public async Task HandleContractExpiredAsync(ContractExpiredEvent evt)
    {
        _logger.LogInformation("Received contract expired event for contract {ContractId}", evt.ContractId);

        try
        {
            var rooms = await FindRoomsByContractIdAsync(evt.ContractId, CancellationToken.None);
            foreach (var room in rooms)
            {
                var action = room.ContractExpiredAction ?? _configuration.DefaultContractExpiredAction;
                await ExecuteContractRoomActionAsync(room, action, ChatRoomLockReason.ContractExpired, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling contract expired event for contract {ContractId}", evt.ContractId);
            await _messageBus.TryPublishErrorAsync(
                "chat", "HandleContractExpired", ex.GetType().Name, ex.Message,
                severity: ServiceErrorEventSeverity.Error);
        }
    }

    /// <summary>
    /// Finds all rooms governed by a specific contract ID.
    /// </summary>
    /// <param name="contractId">The contract ID to search for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of rooms bound to the contract.</returns>
    private async Task<List<ChatRoomModel>> FindRoomsByContractIdAsync(Guid contractId, CancellationToken ct)
    {
        var conditions = new List<QueryCondition>
        {
            new() { Path = "$.ContractId", Operator = QueryOperator.Equals, Value = contractId.ToString() },
            new() { Path = "$.RoomId", Operator = QueryOperator.Exists, Value = true },
        };

        var result = await _roomStore.JsonQueryPagedAsync(conditions, 0, 100, cancellationToken: ct);
        return result.Items.Select(i => i.Value).ToList();
    }
}
