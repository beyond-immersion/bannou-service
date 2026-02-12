using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Faction;

/// <summary>
/// Implementation of the Faction service.
/// This class contains the business logic for all Faction operations.
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
///   <item><b>Configuration:</b> ALL config properties in FactionServiceConfiguration MUST be wired up. No hardcoded magic numbers for tunables.</item>
///   <item><b>Events:</b> ALL meaningful state changes MUST publish typed events, even without current consumers.</item>
///   <item><b>Cache Stores:</b> If state-stores.yaml defines cache stores for this service, implement read-through/write-through caching.</item>
///   <item><b>Concurrency:</b> Use GetWithETagAsync + TrySaveAsync for list/index operations. No non-atomic read-modify-write.</item>
/// </list>
/// </para>
/// <para>
/// <b>RELATED FILES:</b>
/// <list type="bullet">
///   <item>Internal data models: FactionServiceModels.cs (storage models, cache entries, internal DTOs)</item>
///   <item>Event handlers: FactionServiceEvents.cs (event consumer registration and handlers)</item>
///   <item>Request/Response models: bannou-service/Generated/Models/FactionModels.cs</item>
///   <item>Event models: bannou-service/Generated/Events/FactionEventsModels.cs</item>
///   <item>Lifecycle events: bannou-service/Generated/Events/FactionLifecycleEvents.cs</item>
///   <item>Configuration: Generated/FactionServiceConfiguration.cs</item>
///   <item>State stores: bannou-service/Generated/StateStoreDefinitions.cs</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("faction", typeof(IFactionService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFeatures)]
public partial class FactionService : IFactionService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<FactionService> _logger;
    private readonly FactionServiceConfiguration _configuration;

    private const string STATE_STORE = "faction-statestore";

    public FactionService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<FactionService> logger,
        FactionServiceConfiguration configuration)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Implementation of CreateFaction operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, FactionResponse?)> CreateFactionAsync(CreateFactionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing CreateFaction operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method CreateFaction not yet implemented");

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

    /// <summary>
    /// Implementation of GetFaction operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, FactionResponse?)> GetFactionAsync(GetFactionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing GetFaction operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method GetFaction not yet implemented");

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

    /// <summary>
    /// Implementation of GetFactionByCode operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, FactionResponse?)> GetFactionByCodeAsync(GetFactionByCodeRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing GetFactionByCode operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method GetFactionByCode not yet implemented");

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

    /// <summary>
    /// Implementation of ListFactions operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, ListFactionsResponse?)> ListFactionsAsync(ListFactionsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing ListFactions operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method ListFactions not yet implemented");

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

    /// <summary>
    /// Implementation of UpdateFaction operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, FactionResponse?)> UpdateFactionAsync(UpdateFactionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing UpdateFaction operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method UpdateFaction not yet implemented");

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

    /// <summary>
    /// Implementation of DeprecateFaction operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, FactionResponse?)> DeprecateFactionAsync(DeprecateFactionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing DeprecateFaction operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method DeprecateFaction not yet implemented");

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

    /// <summary>
    /// Implementation of UndeprecateFaction operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, FactionResponse?)> UndeprecateFactionAsync(UndeprecateFactionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing UndeprecateFaction operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method UndeprecateFaction not yet implemented");

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

    /// <summary>
    /// Implementation of DeleteFaction operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, object?)> DeleteFactionAsync(DeleteFactionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing DeleteFaction operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method DeleteFaction not yet implemented");

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

    /// <summary>
    /// Implementation of SeedFactions operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, SeedFactionsResponse?)> SeedFactionsAsync(SeedFactionsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing SeedFactions operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method SeedFactions not yet implemented");

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

    /// <summary>
    /// Implementation of DesignateRealmBaseline operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, FactionResponse?)> DesignateRealmBaselineAsync(DesignateRealmBaselineRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing DesignateRealmBaseline operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method DesignateRealmBaseline not yet implemented");

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

    /// <summary>
    /// Implementation of GetRealmBaseline operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, FactionResponse?)> GetRealmBaselineAsync(GetRealmBaselineRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing GetRealmBaseline operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method GetRealmBaseline not yet implemented");

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

    /// <summary>
    /// Implementation of AddMember operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, FactionMemberResponse?)> AddMemberAsync(AddMemberRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing AddMember operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method AddMember not yet implemented");

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

    /// <summary>
    /// Implementation of RemoveMember operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, object?)> RemoveMemberAsync(RemoveMemberRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing RemoveMember operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method RemoveMember not yet implemented");

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

    /// <summary>
    /// Implementation of ListMembers operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, ListMembersResponse?)> ListMembersAsync(ListMembersRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing ListMembers operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method ListMembers not yet implemented");

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

    /// <summary>
    /// Implementation of ListMembershipsByCharacter operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, ListMembershipsByCharacterResponse?)> ListMembershipsByCharacterAsync(ListMembershipsByCharacterRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing ListMembershipsByCharacter operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method ListMembershipsByCharacter not yet implemented");

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

    /// <summary>
    /// Implementation of UpdateMemberRole operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, FactionMemberResponse?)> UpdateMemberRoleAsync(UpdateMemberRoleRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing UpdateMemberRole operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method UpdateMemberRole not yet implemented");

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

    /// <summary>
    /// Implementation of CheckMembership operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, CheckMembershipResponse?)> CheckMembershipAsync(CheckMembershipRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing CheckMembership operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method CheckMembership not yet implemented");

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

    /// <summary>
    /// Implementation of ClaimTerritory operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, TerritoryClaimResponse?)> ClaimTerritoryAsync(ClaimTerritoryRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing ClaimTerritory operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method ClaimTerritory not yet implemented");

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

    /// <summary>
    /// Implementation of ReleaseTerritory operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, object?)> ReleaseTerritoryAsync(ReleaseTerritoryRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing ReleaseTerritory operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method ReleaseTerritory not yet implemented");

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

    /// <summary>
    /// Implementation of ListTerritoryClaims operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, ListTerritoryClaimsResponse?)> ListTerritoryClaimsAsync(ListTerritoryClaimsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing ListTerritoryClaims operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method ListTerritoryClaims not yet implemented");

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

    /// <summary>
    /// Implementation of GetControllingFaction operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, ControllingFactionResponse?)> GetControllingFactionAsync(GetControllingFactionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing GetControllingFaction operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method GetControllingFaction not yet implemented");

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

    /// <summary>
    /// Implementation of DefineNorm operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, NormDefinitionResponse?)> DefineNormAsync(DefineNormRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing DefineNorm operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method DefineNorm not yet implemented");

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

    /// <summary>
    /// Implementation of UpdateNorm operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, NormDefinitionResponse?)> UpdateNormAsync(UpdateNormRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing UpdateNorm operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method UpdateNorm not yet implemented");

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

    /// <summary>
    /// Implementation of DeleteNorm operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, object?)> DeleteNormAsync(DeleteNormRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing DeleteNorm operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method DeleteNorm not yet implemented");

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

    /// <summary>
    /// Implementation of ListNorms operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, ListNormsResponse?)> ListNormsAsync(ListNormsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing ListNorms operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method ListNorms not yet implemented");

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

    /// <summary>
    /// Implementation of QueryApplicableNorms operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, QueryApplicableNormsResponse?)> QueryApplicableNormsAsync(QueryApplicableNormsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing QueryApplicableNorms operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method QueryApplicableNorms not yet implemented");

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

    /// <summary>
    /// Implementation of CleanupByCharacter operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, CleanupByCharacterResponse?)> CleanupByCharacterAsync(CleanupByCharacterRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing CleanupByCharacter operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method CleanupByCharacter not yet implemented");

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

    /// <summary>
    /// Implementation of CleanupByRealm operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, CleanupByRealmResponse?)> CleanupByRealmAsync(CleanupByRealmRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing CleanupByRealm operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method CleanupByRealm not yet implemented");

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

    /// <summary>
    /// Implementation of CleanupByLocation operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, CleanupByLocationResponse?)> CleanupByLocationAsync(CleanupByLocationRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing CleanupByLocation operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method CleanupByLocation not yet implemented");

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

    /// <summary>
    /// Implementation of GetCompressData operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, FactionArchive?)> GetCompressDataAsync(GetCompressDataRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing GetCompressData operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method GetCompressData not yet implemented");

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

    /// <summary>
    /// Implementation of RestoreFromArchive operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, RestoreFromArchiveResponse?)> RestoreFromArchiveAsync(RestoreFromArchiveRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing RestoreFromArchive operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method RestoreFromArchive not yet implemented");

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

}
