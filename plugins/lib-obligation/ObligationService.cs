using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Obligation;

/// <summary>
/// Implementation of the Obligation service.
/// This class contains the business logic for all Obligation operations.
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
///   <item><b>Configuration:</b> ALL config properties in ObligationServiceConfiguration MUST be wired up. No hardcoded magic numbers for tunables.</item>
///   <item><b>Events:</b> ALL meaningful state changes MUST publish typed events, even without current consumers.</item>
///   <item><b>Cache Stores:</b> If state-stores.yaml defines cache stores for this service, implement read-through/write-through caching.</item>
///   <item><b>Concurrency:</b> Use GetWithETagAsync + TrySaveAsync for list/index operations. No non-atomic read-modify-write.</item>
/// </list>
/// </para>
/// <para>
/// <b>RELATED FILES:</b>
/// <list type="bullet">
///   <item>Internal data models: ObligationServiceModels.cs (storage models, cache entries, internal DTOs)</item>
///   <item>Event handlers: ObligationServiceEvents.cs (event consumer registration and handlers)</item>
///   <item>Request/Response models: bannou-service/Generated/Models/ObligationModels.cs</item>
///   <item>Event models: bannou-service/Generated/Events/ObligationEventsModels.cs</item>
///   <item>Lifecycle events: bannou-service/Generated/Events/ObligationLifecycleEvents.cs</item>
///   <item>Configuration: Generated/ObligationServiceConfiguration.cs</item>
///   <item>State stores: bannou-service/Generated/StateStoreDefinitions.cs</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("obligation", typeof(IObligationService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFeatures)]
public partial class ObligationService : IObligationService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<ObligationService> _logger;
    private readonly ObligationServiceConfiguration _configuration;

    private const string STATE_STORE = "obligation-statestore";

    public ObligationService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<ObligationService> logger,
        ObligationServiceConfiguration configuration)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Implementation of SetActionMapping operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, ActionMappingResponse?)> SetActionMappingAsync(SetActionMappingRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing SetActionMapping operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method SetActionMapping not yet implemented");

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
    /// Implementation of ListActionMappings operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, ListActionMappingsResponse?)> ListActionMappingsAsync(ListActionMappingsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing ListActionMappings operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method ListActionMappings not yet implemented");

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
    /// Implementation of DeleteActionMapping operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, object?)> DeleteActionMappingAsync(DeleteActionMappingRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing DeleteActionMapping operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method DeleteActionMapping not yet implemented");

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
    /// Implementation of QueryObligations operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, QueryObligationsResponse?)> QueryObligationsAsync(QueryObligationsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing QueryObligations operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method QueryObligations not yet implemented");

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
    /// Implementation of EvaluateAction operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, EvaluateActionResponse?)> EvaluateActionAsync(EvaluateActionRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing EvaluateAction operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method EvaluateAction not yet implemented");

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
    /// Implementation of ReportViolation operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, ReportViolationResponse?)> ReportViolationAsync(ReportViolationRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing ReportViolation operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method ReportViolation not yet implemented");

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
    /// Implementation of QueryViolations operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, QueryViolationsResponse?)> QueryViolationsAsync(QueryViolationsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing QueryViolations operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method QueryViolations not yet implemented");

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
    /// Implementation of InvalidateCache operation.
    /// TODO: Implement business logic for this method.
    /// </summary>
    public Task<(StatusCodes, InvalidateCacheResponse?)> InvalidateCacheAsync(InvalidateCacheRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing InvalidateCache operation");

        // TODO: Implement your business logic here
        // Note: The generated controller wraps this method with try/catch for error handling.
        // Do NOT add an outer try/catch here -- exceptions will be caught, logged, and
        // published as error events by the controller automatically.
        throw new NotImplementedException("Method InvalidateCache not yet implemented");

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
    public Task<(StatusCodes, ObligationArchive?)> GetCompressDataAsync(GetCompressDataRequest body, CancellationToken cancellationToken)
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

}
