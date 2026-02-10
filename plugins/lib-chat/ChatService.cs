using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Chat;

/// <summary>
/// Implementation of the Chat service.
/// This class contains the business logic for all Chat operations.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
/// Generated code (event handlers, permissions) is placed in companion partial classes.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS CHECKLIST:</b>
/// <list type="bullet">
///   <item><b>Type Safety:</b> Internal POCOs MUST use proper C# types (enums, Guids, DateTimeOffset) - never string representations. No Enum.Parse in business logic.</item>
///   <item><b>Configuration:</b> ALL config properties in ChatServiceConfiguration MUST be wired up. No hardcoded magic numbers for tunables.</item>
///   <item><b>Events:</b> ALL meaningful state changes MUST publish typed events, even without current consumers.</item>
///   <item><b>Cache Stores:</b> If state-stores.yaml defines cache stores for this service, implement read-through/write-through caching.</item>
///   <item><b>Concurrency:</b> Use GetWithETagAsync + TrySaveAsync for list/index operations. No non-atomic read-modify-write.</item>
/// </list>
/// </para>
/// <para>
/// <b>RELATED FILES:</b>
/// <list type="bullet">
///   <item>Internal data models: ChatServiceModels.cs (storage models, cache entries, internal DTOs)</item>
///   <item>Event handlers: ChatServiceEvents.cs (event consumer registration and handlers)</item>
///   <item>Request/Response models: bannou-service/Generated/Models/ChatModels.cs</item>
///   <item>Event models: bannou-service/Generated/Events/ChatEventsModels.cs</item>
///   <item>Lifecycle events: bannou-service/Generated/Events/ChatLifecycleEvents.cs</item>
///   <item>Configuration: Generated/ChatServiceConfiguration.cs</item>
///   <item>State stores: bannou-service/Generated/StateStoreDefinitions.cs</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("chat", typeof(IChatService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.AppFoundation)]
public partial class ChatService : IChatService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<ChatService> _logger;
    private readonly ChatServiceConfiguration _configuration;

    /// <summary>
    /// Creates a new instance of the Chat service.
    /// </summary>
    public ChatService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<ChatService> logger,
        ChatServiceConfiguration configuration,
        IEventConsumer eventConsumer)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;

        // Register event handlers via partial class (ChatServiceEvents.cs)
        RegisterEventConsumers(eventConsumer);
    }

    /// <summary>
    /// Implementation of RegisterRoomType operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, RoomTypeResponse?)> RegisterRoomTypeAsync(RegisterRoomTypeRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing RegisterRoomType operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method RegisterRoomType not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing RegisterRoomType operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "RegisterRoomType",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/type/register",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of GetRoomType operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, RoomTypeResponse?)> GetRoomTypeAsync(GetRoomTypeRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing GetRoomType operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method GetRoomType not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing GetRoomType operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "GetRoomType",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/type/get",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of ListRoomTypes operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, ListRoomTypesResponse?)> ListRoomTypesAsync(ListRoomTypesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing ListRoomTypes operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method ListRoomTypes not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing ListRoomTypes operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "ListRoomTypes",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/type/list",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of UpdateRoomType operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, RoomTypeResponse?)> UpdateRoomTypeAsync(UpdateRoomTypeRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing UpdateRoomType operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method UpdateRoomType not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing UpdateRoomType operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "UpdateRoomType",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/type/update",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of DeprecateRoomType operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, RoomTypeResponse?)> DeprecateRoomTypeAsync(DeprecateRoomTypeRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing DeprecateRoomType operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method DeprecateRoomType not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing DeprecateRoomType operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "DeprecateRoomType",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/type/deprecate",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of CreateRoom operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, ChatRoomResponse?)> CreateRoomAsync(CreateRoomRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing CreateRoom operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method CreateRoom not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing CreateRoom operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "CreateRoom",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/room/create",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of GetRoom operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, ChatRoomResponse?)> GetRoomAsync(GetRoomRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing GetRoom operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method GetRoom not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing GetRoom operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "GetRoom",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/room/get",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of ListRooms operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, ListRoomsResponse?)> ListRoomsAsync(ListRoomsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing ListRooms operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method ListRooms not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing ListRooms operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "ListRooms",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/room/list",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of UpdateRoom operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, ChatRoomResponse?)> UpdateRoomAsync(UpdateRoomRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing UpdateRoom operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method UpdateRoom not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing UpdateRoom operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "UpdateRoom",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/room/update",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of DeleteRoom operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, ChatRoomResponse?)> DeleteRoomAsync(DeleteRoomRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing DeleteRoom operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method DeleteRoom not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing DeleteRoom operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "DeleteRoom",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/room/delete",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of ArchiveRoom operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, ChatRoomResponse?)> ArchiveRoomAsync(ArchiveRoomRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing ArchiveRoom operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method ArchiveRoom not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing ArchiveRoom operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "ArchiveRoom",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/room/archive",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of JoinRoom operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, ChatRoomResponse?)> JoinRoomAsync(JoinRoomRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing JoinRoom operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method JoinRoom not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing JoinRoom operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "JoinRoom",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/room/join",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of LeaveRoom operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, ChatRoomResponse?)> LeaveRoomAsync(LeaveRoomRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing LeaveRoom operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method LeaveRoom not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing LeaveRoom operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "LeaveRoom",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/room/leave",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of ListParticipants operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, ParticipantsResponse?)> ListParticipantsAsync(ListParticipantsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing ListParticipants operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method ListParticipants not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing ListParticipants operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "ListParticipants",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/room/participants",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of KickParticipant operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, ChatRoomResponse?)> KickParticipantAsync(KickParticipantRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing KickParticipant operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method KickParticipant not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing KickParticipant operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "KickParticipant",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/room/participant/kick",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of BanParticipant operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, ChatRoomResponse?)> BanParticipantAsync(BanParticipantRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing BanParticipant operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method BanParticipant not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing BanParticipant operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "BanParticipant",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/room/participant/ban",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of UnbanParticipant operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, ChatRoomResponse?)> UnbanParticipantAsync(UnbanParticipantRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing UnbanParticipant operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method UnbanParticipant not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing UnbanParticipant operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "UnbanParticipant",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/room/participant/unban",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of MuteParticipant operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, ChatRoomResponse?)> MuteParticipantAsync(MuteParticipantRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing MuteParticipant operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method MuteParticipant not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing MuteParticipant operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "MuteParticipant",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/room/participant/mute",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of SendMessage operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, ChatMessageResponse?)> SendMessageAsync(SendMessageRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing SendMessage operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method SendMessage not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing SendMessage operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "SendMessage",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/message/send",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of SendMessageBatch operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, SendMessageBatchResponse?)> SendMessageBatchAsync(SendMessageBatchRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing SendMessageBatch operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method SendMessageBatch not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing SendMessageBatch operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "SendMessageBatch",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/message/send-batch",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of GetMessageHistory operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, MessageHistoryResponse?)> GetMessageHistoryAsync(MessageHistoryRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing GetMessageHistory operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method GetMessageHistory not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing GetMessageHistory operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "GetMessageHistory",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/message/history",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of DeleteMessage operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, ChatMessageResponse?)> DeleteMessageAsync(DeleteMessageRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing DeleteMessage operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method DeleteMessage not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing DeleteMessage operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "DeleteMessage",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/message/delete",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of PinMessage operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, ChatMessageResponse?)> PinMessageAsync(PinMessageRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing PinMessage operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method PinMessage not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing PinMessage operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "PinMessage",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/message/pin",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of UnpinMessage operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, ChatMessageResponse?)> UnpinMessageAsync(UnpinMessageRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing UnpinMessage operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method UnpinMessage not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing UnpinMessage operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "UnpinMessage",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/message/unpin",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of SearchMessages operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, SearchMessagesResponse?)> SearchMessagesAsync(SearchMessagesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing SearchMessages operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method SearchMessages not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing SearchMessages operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "SearchMessages",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/message/search",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of AdminListRooms operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, ListRoomsResponse?)> AdminListRoomsAsync(AdminListRoomsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing AdminListRooms operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method AdminListRooms not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing AdminListRooms operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "AdminListRooms",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/admin/rooms",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of AdminGetStats operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, AdminStatsResponse?)> AdminGetStatsAsync(AdminGetStatsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing AdminGetStats operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method AdminGetStats not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing AdminGetStats operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "AdminGetStats",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/admin/stats",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

    /// <summary>
    /// Implementation of AdminForceCleanup operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public async Task<(StatusCodes, AdminCleanupResponse?)> AdminForceCleanupAsync(AdminForceCleanupRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing AdminForceCleanup operation");

        try
        {
            // TODO: Implement your business logic here
            throw new NotImplementedException("Method AdminForceCleanup not yet implemented");

            // Example patterns using infrastructure libs:
            //
            // For data retrieval (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var data = await stateStore.GetAsync(key, cancellationToken);
            // return data != null ? (StatusCodes.OK, data) : (StatusCodes.NotFound, default);
            //
            // For data creation (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.SaveAsync(key, newData, cancellationToken);
            // return (StatusCodes.Created, newData);
            //
            // For data updates (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // var existing = await stateStore.GetAsync(key, cancellationToken);
            // if (existing == null) return (StatusCodes.NotFound, default);
            // await stateStore.SaveAsync(key, updatedData, cancellationToken);
            // return (StatusCodes.OK, updatedData);
            //
            // For data deletion (lib-state):
            // var stateStore = _stateStoreFactory.Create<YourDataType>(STATE_STORE);
            // await stateStore.DeleteAsync(key, cancellationToken);
            // return (StatusCodes.NoContent, default);
            //
            // For event publishing (lib-messaging):
            // await _messageBus.TryPublishAsync("topic.name", eventModel, cancellationToken: cancellationToken);
            //
            // For calling other services (lib-mesh):
            // Inject the specific client you need, e.g.: IAccountClient _accountClient
            // var (status, result) = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = id }, cancellationToken);
            // if (status != StatusCodes.OK) return (status, default);
            //
            // For client event delivery (if request from WebSocket):
            // Inject IClientEventPublisher _clientEventPublisher
            // await _clientEventPublisher.PublishToSessionAsync(sessionId, new YourClientEvent { ... }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing AdminForceCleanup operation");
            await _messageBus.TryPublishErrorAsync(
                "chat",
                "AdminForceCleanup",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/chat/admin/cleanup",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, default);
        }
    }

}
