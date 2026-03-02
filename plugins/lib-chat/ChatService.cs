using BeyondImmersion.Bannou.Chat.ClientEvents;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Permission;
using BeyondImmersion.BannouService.Protocol;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

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
///   <item><b>Concurrency:</b> Use distributed locks for room/type mutations. No non-atomic read-modify-write.</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("chat", typeof(IChatService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.AppFoundation)]
public partial class ChatService : IChatService
{
    private const string TypeKeyPrefix = "type:";
    private const string RoomKeyPrefix = "room:";
    private const string BanKeyPrefix = "ban:";

    private readonly IMessageBus _messageBus;
    private readonly IClientEventPublisher _clientEventPublisher;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ILogger<ChatService> _logger;
    private readonly ChatServiceConfiguration _configuration;
    private readonly IContractClient _contractClient;
    private readonly IResourceClient _resourceClient;
    private readonly IPermissionClient _permissionClient;
    private readonly IEntitySessionRegistry _entitySessionRegistry;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly string _serverSalt;

    private readonly IJsonQueryableStateStore<ChatRoomTypeModel> _roomTypeStore;
    private readonly IJsonQueryableStateStore<ChatRoomModel> _roomStore;
    private readonly IStateStore<ChatRoomModel> _roomCache;
    private readonly IJsonQueryableStateStore<ChatMessageModel> _messageStore;
    private readonly IStateStore<ChatMessageModel> _messageBuffer;
    private readonly ICacheableStateStore<ChatParticipantModel> _participantStore;
    private readonly IJsonQueryableStateStore<ChatBanModel> _banStore;

    /// <summary>
    /// Creates a new instance of the Chat service.
    /// </summary>
    public ChatService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        IClientEventPublisher clientEventPublisher,
        IDistributedLockProvider lockProvider,
        ILogger<ChatService> logger,
        ChatServiceConfiguration configuration,
        IEventConsumer eventConsumer,
        IContractClient contractClient,
        IResourceClient resourceClient,
        IPermissionClient permissionClient,
        IEntitySessionRegistry entitySessionRegistry,
        ITelemetryProvider telemetryProvider)
    {
        _messageBus = messageBus;
        _clientEventPublisher = clientEventPublisher;
        _lockProvider = lockProvider;
        _logger = logger;
        _configuration = configuration;
        _contractClient = contractClient;
        _resourceClient = resourceClient;
        _permissionClient = permissionClient;
        _entitySessionRegistry = entitySessionRegistry;
        _telemetryProvider = telemetryProvider;
        _serverSalt = configuration.ServerSalt;

        _roomTypeStore = stateStoreFactory.GetJsonQueryableStore<ChatRoomTypeModel>(StateStoreDefinitions.ChatRoomTypes);
        _roomStore = stateStoreFactory.GetJsonQueryableStore<ChatRoomModel>(StateStoreDefinitions.ChatRooms);
        _roomCache = stateStoreFactory.GetStore<ChatRoomModel>(StateStoreDefinitions.ChatRoomsCache);
        _messageStore = stateStoreFactory.GetJsonQueryableStore<ChatMessageModel>(StateStoreDefinitions.ChatMessages);
        _messageBuffer = stateStoreFactory.GetStore<ChatMessageModel>(StateStoreDefinitions.ChatMessagesEphemeral);
        _participantStore = stateStoreFactory.GetCacheableStore<ChatParticipantModel>(StateStoreDefinitions.ChatParticipants);
        _banStore = stateStoreFactory.GetJsonQueryableStore<ChatBanModel>(StateStoreDefinitions.ChatBans);

        RegisterEventConsumers(eventConsumer);
    }

    // ============================================================================
    // ROOM TYPE OPERATIONS
    // ============================================================================

    /// <inheritdoc />
    public async Task<(StatusCodes, RoomTypeResponse?)> RegisterRoomTypeAsync(
        RegisterRoomTypeRequest body, CancellationToken cancellationToken)
    {
        var typeKey = BuildRoomTypeKey(body.GameServiceId, body.Code);

        // Distributed lock prevents TOCTOU race on uniqueness check
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.ChatLock, "register-room-type", typeKey,
            _configuration.LockExpirySeconds, cancellationToken);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for room type registration: {Code}", body.Code);
            return (StatusCodes.Conflict, null);
        }

        // Check uniqueness
        var existing = await _roomTypeStore.GetAsync(typeKey, cancellationToken);
        if (existing != null)
        {
            _logger.LogWarning("Room type already exists: {Code} for game {GameServiceId}", body.Code, body.GameServiceId);
            return (StatusCodes.Conflict, null);
        }

        // Check MaxRoomTypesPerGameService for scoped types
        if (body.GameServiceId.HasValue)
        {
            var countConditions = new List<QueryCondition>
            {
                new() { Path = "$.GameServiceId", Operator = QueryOperator.Equals, Value = body.GameServiceId.Value.ToString() },
                new() { Path = "$.Code", Operator = QueryOperator.Exists, Value = true }
            };
            var count = await _roomTypeStore.JsonCountAsync(countConditions, cancellationToken);
            if (count >= _configuration.MaxRoomTypesPerGameService)
            {
                _logger.LogWarning("Game service {GameServiceId} has reached max room types ({Max})",
                    body.GameServiceId, _configuration.MaxRoomTypesPerGameService);
                return (StatusCodes.Conflict, null);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var model = new ChatRoomTypeModel
        {
            Code = body.Code,
            DisplayName = body.DisplayName,
            Description = body.Description,
            GameServiceId = body.GameServiceId,
            MessageFormat = body.MessageFormat,
            ValidatorConfig = body.ValidatorConfig != null ? new ValidatorConfigModel
            {
                MaxMessageLength = body.ValidatorConfig.MaxMessageLength,
                AllowedPattern = body.ValidatorConfig.AllowedPattern,
                AllowedValues = body.ValidatorConfig.AllowedValues?.ToList(),
                RequiredFields = body.ValidatorConfig.RequiredFields?.ToList(),
                JsonSchema = body.ValidatorConfig.JsonSchema,
            } : null,
            PersistenceMode = body.PersistenceMode,
            DefaultMaxParticipants = body.DefaultMaxParticipants,
            RetentionDays = body.RetentionDays,
            DefaultContractTemplateId = body.DefaultContractTemplateId,
            AllowAnonymousSenders = body.AllowAnonymousSenders,
            RateLimitPerMinute = body.RateLimitPerMinute,
            Metadata = body.Metadata,
            Status = RoomTypeStatus.Active,
            CreatedAt = now,
        };

        await _roomTypeStore.SaveAsync(typeKey, model, cancellationToken: cancellationToken);

        await _messageBus.TryPublishAsync("chat.room-type.created", new ChatRoomTypeCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            Code = model.Code,
            GameServiceId = model.GameServiceId,
            DisplayName = model.DisplayName,
            MessageFormat = model.MessageFormat,
            PersistenceMode = model.PersistenceMode,
            AllowAnonymousSenders = model.AllowAnonymousSenders,
            Status = model.Status,
            CreatedAt = model.CreatedAt,
        }, cancellationToken);

        _logger.LogInformation("Registered room type {Code} for game {GameServiceId}", body.Code, body.GameServiceId);
        return (StatusCodes.OK, MapToRoomTypeResponse(model));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, RoomTypeResponse?)> GetRoomTypeAsync(
        GetRoomTypeRequest body, CancellationToken cancellationToken)
    {
        var typeKey = BuildRoomTypeKey(body.GameServiceId, body.Code);
        var model = await _roomTypeStore.GetAsync(typeKey, cancellationToken);
        if (model == null)
        {
            return (StatusCodes.NotFound, null);
        }
        return (StatusCodes.OK, MapToRoomTypeResponse(model));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ListRoomTypesResponse?)> ListRoomTypesAsync(
        ListRoomTypesRequest body, CancellationToken cancellationToken)
    {
        var conditions = new List<QueryCondition>
        {
            // Type discriminator
            new() { Path = "$.Code", Operator = QueryOperator.Exists, Value = true }
        };

        if (body.GameServiceId.HasValue)
        {
            conditions.Add(new QueryCondition
            {
                Path = "$.GameServiceId",
                Operator = QueryOperator.Equals,
                Value = body.GameServiceId.Value.ToString()
            });
        }
        if (body.MessageFormat.HasValue)
        {
            conditions.Add(new QueryCondition
            {
                Path = "$.MessageFormat",
                Operator = QueryOperator.Equals,
                Value = body.MessageFormat.Value.ToString()
            });
        }
        if (body.Status.HasValue)
        {
            conditions.Add(new QueryCondition
            {
                Path = "$.Status",
                Operator = QueryOperator.Equals,
                Value = body.Status.Value.ToString()
            });
        }

        var offset = body.Page * body.PageSize;
        var result = await _roomTypeStore.JsonQueryPagedAsync(
            conditions, offset, body.PageSize,
            new JsonSortSpec { Path = "$.CreatedAt", Descending = true },
            cancellationToken);

        var items = result.Items.Select(r => MapToRoomTypeResponse(r.Value)).ToList();

        return (StatusCodes.OK, new ListRoomTypesResponse
        {
            Items = items,
            TotalCount = (int)result.TotalCount,
            Page = body.Page,
            PageSize = body.PageSize,
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, RoomTypeResponse?)> UpdateRoomTypeAsync(
        UpdateRoomTypeRequest body, CancellationToken cancellationToken)
    {
        var typeKey = BuildRoomTypeKey(body.GameServiceId, body.Code);

        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.ChatLock, $"type:{body.Code}",
            Guid.NewGuid().ToString(), _configuration.LockExpirySeconds, cancellationToken);
        if (!lockResponse.Success)
        {
            return (StatusCodes.Conflict, null);
        }

        var model = await _roomTypeStore.GetAsync(typeKey, cancellationToken);
        if (model == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var changedFields = new List<string>();
        if (body.DisplayName != null) { model.DisplayName = body.DisplayName; changedFields.Add("DisplayName"); }
        if (body.Description != null) { model.Description = body.Description; changedFields.Add("Description"); }
        if (body.ValidatorConfig != null)
        {
            model.ValidatorConfig = new ValidatorConfigModel
            {
                MaxMessageLength = body.ValidatorConfig.MaxMessageLength,
                AllowedPattern = body.ValidatorConfig.AllowedPattern,
                AllowedValues = body.ValidatorConfig.AllowedValues?.ToList(),
                RequiredFields = body.ValidatorConfig.RequiredFields?.ToList(),
                JsonSchema = body.ValidatorConfig.JsonSchema,
            };
            changedFields.Add("ValidatorConfig");
        }
        if (body.DefaultMaxParticipants.HasValue) { model.DefaultMaxParticipants = body.DefaultMaxParticipants; changedFields.Add("DefaultMaxParticipants"); }
        if (body.RetentionDays.HasValue) { model.RetentionDays = body.RetentionDays; changedFields.Add("RetentionDays"); }
        if (body.DefaultContractTemplateId.HasValue) { model.DefaultContractTemplateId = body.DefaultContractTemplateId; changedFields.Add("DefaultContractTemplateId"); }
        if (body.AllowAnonymousSenders.HasValue) { model.AllowAnonymousSenders = body.AllowAnonymousSenders.Value; changedFields.Add("AllowAnonymousSenders"); }
        if (body.RateLimitPerMinute.HasValue) { model.RateLimitPerMinute = body.RateLimitPerMinute; changedFields.Add("RateLimitPerMinute"); }
        if (body.Metadata != null) { model.Metadata = body.Metadata; changedFields.Add("Metadata"); }

        model.UpdatedAt = DateTimeOffset.UtcNow;
        await _roomTypeStore.SaveAsync(typeKey, model, cancellationToken: cancellationToken);

        await _messageBus.TryPublishAsync("chat.room-type.updated", new ChatRoomTypeUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = model.UpdatedAt.Value,
            Code = model.Code,
            GameServiceId = model.GameServiceId,
            DisplayName = model.DisplayName,
            MessageFormat = model.MessageFormat,
            PersistenceMode = model.PersistenceMode,
            AllowAnonymousSenders = model.AllowAnonymousSenders,
            Status = model.Status,
            CreatedAt = model.CreatedAt,
            ChangedFields = changedFields,
        }, cancellationToken);

        return (StatusCodes.OK, MapToRoomTypeResponse(model));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, RoomTypeResponse?)> DeprecateRoomTypeAsync(
        DeprecateRoomTypeRequest body, CancellationToken cancellationToken)
    {
        var typeKey = BuildRoomTypeKey(body.GameServiceId, body.Code);

        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.ChatLock, $"type:{body.Code}",
            Guid.NewGuid().ToString(), _configuration.LockExpirySeconds, cancellationToken);
        if (!lockResponse.Success)
        {
            return (StatusCodes.Conflict, null);
        }

        var model = await _roomTypeStore.GetAsync(typeKey, cancellationToken);
        if (model == null)
        {
            return (StatusCodes.NotFound, null);
        }

        if (model.Status == RoomTypeStatus.Deprecated)
        {
            return (StatusCodes.OK, MapToRoomTypeResponse(model));
        }

        model.Status = RoomTypeStatus.Deprecated;
        model.UpdatedAt = DateTimeOffset.UtcNow;
        await _roomTypeStore.SaveAsync(typeKey, model, cancellationToken: cancellationToken);

        await _messageBus.TryPublishAsync("chat.room-type.updated", new ChatRoomTypeUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = model.UpdatedAt.Value,
            Code = model.Code,
            GameServiceId = model.GameServiceId,
            DisplayName = model.DisplayName,
            MessageFormat = model.MessageFormat,
            PersistenceMode = model.PersistenceMode,
            AllowAnonymousSenders = model.AllowAnonymousSenders,
            Status = model.Status,
            CreatedAt = model.CreatedAt,
            ChangedFields = new List<string> { "Status" },
        }, cancellationToken);

        _logger.LogInformation("Deprecated room type {Code}", body.Code);
        return (StatusCodes.OK, MapToRoomTypeResponse(model));
    }

    // ============================================================================
    // ROOM OPERATIONS
    // ============================================================================

    /// <inheritdoc />
    public async Task<(StatusCodes, ChatRoomResponse?)> CreateRoomAsync(
        CreateRoomRequest body, CancellationToken cancellationToken)
    {
        // Validate room type exists and is active
        var roomType = await FindRoomTypeByCodeAsync(body.RoomTypeCode, cancellationToken);
        if (roomType == null)
        {
            _logger.LogWarning("Room type not found: {Code}", body.RoomTypeCode);
            return (StatusCodes.NotFound, null);
        }
        if (roomType.Status == RoomTypeStatus.Deprecated)
        {
            _logger.LogWarning("Cannot create room with deprecated type: {Code}", body.RoomTypeCode);
            return (StatusCodes.BadRequest, null);
        }

        // Validate contract if provided
        if (body.ContractId.HasValue)
        {
            try
            {
                await _contractClient.GetContractInstanceAsync(new GetContractInstanceRequest { ContractId = body.ContractId.Value }, cancellationToken);
            }
            catch (ApiException apiEx) when (apiEx.StatusCode == 404)
            {
                _logger.LogWarning("Contract {ContractId} not found", body.ContractId);
                return (StatusCodes.NotFound, null);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var roomId = Guid.NewGuid();
        var model = new ChatRoomModel
        {
            RoomId = roomId,
            RoomTypeCode = body.RoomTypeCode,
            SessionId = body.SessionId,
            ContractId = body.ContractId,
            DisplayName = body.DisplayName,
            Status = ChatRoomStatus.Active,
            MaxParticipants = body.MaxParticipants,
            ContractFulfilledAction = body.ContractFulfilledAction,
            ContractBreachAction = body.ContractBreachAction,
            ContractTerminatedAction = body.ContractTerminatedAction,
            ContractExpiredAction = body.ContractExpiredAction,
            IsArchived = false,
            Metadata = body.Metadata,
            CreatedAt = now,
            LastActivityAt = now,
        };

        var roomKey = $"{RoomKeyPrefix}{roomId}";
        await _roomStore.SaveAsync(roomKey, model, cancellationToken: cancellationToken);
        await _roomCache.SaveAsync(roomKey, model, cancellationToken: cancellationToken);

        var effectiveMax = GetEffectiveMaxParticipants(model, roomType);

        await _messageBus.TryPublishAsync("chat.room.created", new ChatRoomCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            RoomId = roomId,
            RoomTypeCode = model.RoomTypeCode,
            SessionId = model.SessionId,
            ContractId = model.ContractId,
            DisplayName = model.DisplayName,
            Status = model.Status,
            ParticipantCount = 0,
            MaxParticipants = effectiveMax,
            IsArchived = false,
            CreatedAt = now,
        }, cancellationToken);

        _logger.LogInformation("Created chat room {RoomId} of type {RoomTypeCode}", roomId, body.RoomTypeCode);
        return (StatusCodes.OK, MapToRoomResponse(model, 0));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ChatRoomResponse?)> GetRoomAsync(
        GetRoomRequest body, CancellationToken cancellationToken)
    {
        var model = await GetRoomWithCacheAsync(body.RoomId, cancellationToken);
        if (model == null)
        {
            return (StatusCodes.NotFound, null);
        }
        var count = await GetParticipantCountAsync(body.RoomId, cancellationToken);
        return (StatusCodes.OK, MapToRoomResponse(model, count));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ListRoomsResponse?)> ListRoomsAsync(
        ListRoomsRequest body, CancellationToken cancellationToken)
    {
        var conditions = new List<QueryCondition>
            {
                new() { Path = "$.RoomId", Operator = QueryOperator.Exists, Value = true }
            };

        if (!string.IsNullOrEmpty(body.RoomTypeCode))
        {
            conditions.Add(new QueryCondition { Path = "$.RoomTypeCode", Operator = QueryOperator.Equals, Value = body.RoomTypeCode });
        }
        if (body.SessionId.HasValue)
        {
            conditions.Add(new QueryCondition { Path = "$.SessionId", Operator = QueryOperator.Equals, Value = body.SessionId.Value.ToString() });
        }
        if (body.Status.HasValue)
        {
            conditions.Add(new QueryCondition { Path = "$.Status", Operator = QueryOperator.Equals, Value = body.Status.Value.ToString() });
        }

        var offset = body.Page * body.PageSize;
        var result = await _roomStore.JsonQueryPagedAsync(
            conditions, offset, body.PageSize,
            new JsonSortSpec { Path = "$.CreatedAt", Descending = true },
            cancellationToken);

        var items = new List<ChatRoomResponse>();
        foreach (var r in result.Items)
        {
            var count = await GetParticipantCountAsync(r.Value.RoomId, cancellationToken);
            items.Add(MapToRoomResponse(r.Value, count));
        }

        return (StatusCodes.OK, new ListRoomsResponse
        {
            Items = items,
            TotalCount = (int)result.TotalCount,
            Page = body.Page,
            PageSize = body.PageSize,
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ChatRoomResponse?)> UpdateRoomAsync(
        UpdateRoomRequest body, CancellationToken cancellationToken)
    {
        var roomKey = $"{RoomKeyPrefix}{body.RoomId}";

        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.ChatLock, body.RoomId.ToString(),
            Guid.NewGuid().ToString(), _configuration.LockExpirySeconds, cancellationToken);
        if (!lockResponse.Success)
        {
            return (StatusCodes.Conflict, null);
        }

        var model = await _roomStore.GetAsync(roomKey, cancellationToken);
        if (model == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var changedFields = new List<string>();
        if (body.DisplayName != null) { model.DisplayName = body.DisplayName; changedFields.Add("DisplayName"); }
        if (body.MaxParticipants.HasValue) { model.MaxParticipants = body.MaxParticipants; changedFields.Add("MaxParticipants"); }
        if (body.Metadata != null) { model.Metadata = body.Metadata; changedFields.Add("Metadata"); }

        await _roomStore.SaveAsync(roomKey, model, cancellationToken: cancellationToken);
        await _roomCache.SaveAsync(roomKey, model, cancellationToken: cancellationToken);

        var participantCount = await GetParticipantCountAsync(model.RoomId, cancellationToken);
        await _messageBus.TryPublishAsync("chat.room.updated", new ChatRoomUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RoomId = model.RoomId,
            RoomTypeCode = model.RoomTypeCode,
            SessionId = model.SessionId,
            ContractId = model.ContractId,
            DisplayName = model.DisplayName,
            Status = model.Status,
            ParticipantCount = (int)participantCount,
            MaxParticipants = GetEffectiveMaxParticipants(model, null),
            IsArchived = model.IsArchived,
            CreatedAt = model.CreatedAt,
            ChangedFields = changedFields,
        }, cancellationToken);

        var participants = await GetParticipantsAsync(model.RoomId, cancellationToken);
        var sessionIds = participants.Select(p => p.SessionId.ToString()).ToList();
        if (sessionIds.Count > 0)
        {
            await _clientEventPublisher.PublishToSessionsAsync(sessionIds, new ChatRoomUpdatedClientEvent
            {
                RoomId = model.RoomId,
                RoomTypeCode = model.RoomTypeCode,
                DisplayName = model.DisplayName,
                Status = model.Status,
                ParticipantCount = (int)participantCount,
                MaxParticipants = GetEffectiveMaxParticipants(model, null),
                IsArchived = model.IsArchived,
                CreatedAt = model.CreatedAt,
                ChangedFields = changedFields,
            }, cancellationToken);
        }

        return (StatusCodes.OK, MapToRoomResponse(model, participantCount));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ChatRoomResponse?)> DeleteRoomAsync(
        DeleteRoomRequest body, CancellationToken cancellationToken)
    {
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.ChatLock, body.RoomId.ToString(),
            Guid.NewGuid().ToString(), _configuration.LockExpirySeconds, cancellationToken);
        if (!lockResponse.Success)
        {
            return (StatusCodes.Conflict, null);
        }

        var roomKey = $"{RoomKeyPrefix}{body.RoomId}";
        var model = await _roomStore.GetAsync(roomKey, cancellationToken);
        if (model == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Notify all participants before deletion
        var participants = await GetParticipantsAsync(body.RoomId, cancellationToken);
        if (participants.Count > 0)
        {
            var sessionIds = participants.Select(p => p.SessionId.ToString()).ToList();
            await _clientEventPublisher.PublishToSessionsAsync(sessionIds, new ChatRoomDeletedClientEvent
            {
                RoomId = body.RoomId,
                Reason = "Room deleted",
            }, cancellationToken);

            // Clear permission state for all participants
            foreach (var participant in participants)
            {
                await ClearParticipantPermissionStateAsync(participant.SessionId, cancellationToken);
            }
        }

        // Clean up participant and message data
        await DeleteAllParticipantsAsync(body.RoomId, cancellationToken);
        await _roomStore.DeleteAsync(roomKey, cancellationToken);
        await _roomCache.DeleteAsync(roomKey, cancellationToken);

        var snapshot = MapToRoomResponse(model, 0);

        await _messageBus.TryPublishAsync("chat.room.deleted", new ChatRoomDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RoomId = model.RoomId,
            RoomTypeCode = model.RoomTypeCode,
            SessionId = model.SessionId,
            ContractId = model.ContractId,
            DisplayName = model.DisplayName,
            Status = model.Status,
            ParticipantCount = 0,
            MaxParticipants = GetEffectiveMaxParticipants(model, null),
            IsArchived = model.IsArchived,
            CreatedAt = model.CreatedAt,
            DeletedReason = "Explicitly deleted",
        }, cancellationToken);

        _logger.LogInformation("Deleted chat room {RoomId}", body.RoomId);
        return (StatusCodes.OK, snapshot);
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ChatRoomResponse?)> ArchiveRoomAsync(
        ArchiveRoomRequest body, CancellationToken cancellationToken)
    {
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.ChatLock, body.RoomId.ToString(),
            Guid.NewGuid().ToString(), _configuration.LockExpirySeconds, cancellationToken);
        if (!lockResponse.Success)
        {
            return (StatusCodes.Conflict, null);
        }

        var roomKey = $"{RoomKeyPrefix}{body.RoomId}";
        var model = await _roomStore.GetAsync(roomKey, cancellationToken);
        if (model == null)
        {
            return (StatusCodes.NotFound, null);
        }

        if (model.IsArchived)
        {
            var existingCount = await GetParticipantCountAsync(body.RoomId, cancellationToken);
            return (StatusCodes.OK, MapToRoomResponse(model, existingCount));
        }

        // Archive via Resource service
        Guid archiveId;
        try
        {
            var compressResponse = await _resourceClient.ExecuteCompressAsync(
                new ExecuteCompressRequest { ResourceType = "chat-room", ResourceId = body.RoomId },
                cancellationToken);
            archiveId = compressResponse.ArchiveId ?? throw new InvalidOperationException(
                $"Resource service returned null ArchiveId for room {body.RoomId}");
        }
        catch (ApiException apiEx)
        {
            _logger.LogWarning(apiEx, "Resource service error during archive of room {RoomId}", body.RoomId);
            return (StatusCodes.InternalServerError, null);
        }

        model.IsArchived = true;
        model.Status = ChatRoomStatus.Archived;
        await _roomStore.SaveAsync(roomKey, model, cancellationToken: cancellationToken);
        await _roomCache.SaveAsync(roomKey, model, cancellationToken: cancellationToken);

        await _messageBus.TryPublishAsync("chat.room.archived", new ChatRoomArchivedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RoomId = model.RoomId,
            ArchiveId = archiveId,
        }, cancellationToken);

        _logger.LogInformation("Archived chat room {RoomId} with archive {ArchiveId}", body.RoomId, archiveId);
        var archiveCount = await GetParticipantCountAsync(body.RoomId, cancellationToken);
        return (StatusCodes.OK, MapToRoomResponse(model, archiveCount));
    }

    // ============================================================================
    // PARTICIPANT OPERATIONS
    // ============================================================================

    /// <inheritdoc />
    public async Task<(StatusCodes, ChatRoomResponse?)> JoinRoomAsync(
        JoinRoomRequest body, CancellationToken cancellationToken)
    {
        var callerSessionId = GetCallerSessionId();
        if (callerSessionId == null)
        {
            return (StatusCodes.Unauthorized, null);
        }

        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.ChatLock, body.RoomId.ToString(),
            Guid.NewGuid().ToString(), _configuration.LockExpirySeconds, cancellationToken);
        if (!lockResponse.Success)
        {
            return (StatusCodes.Conflict, null);
        }

        var roomKey = $"{RoomKeyPrefix}{body.RoomId}";
        var model = await _roomStore.GetAsync(roomKey, cancellationToken);
        if (model == null)
        {
            return (StatusCodes.NotFound, null);
        }

        if (model.Status == ChatRoomStatus.Locked)
        {
            return (StatusCodes.Forbidden, null);
        }

        // Check ban
        var banKey = $"{BanKeyPrefix}{body.RoomId}:{callerSessionId}";
        var ban = await _banStore.GetAsync(banKey, cancellationToken);
        if (ban != null && (ban.ExpiresAt == null || ban.ExpiresAt > DateTimeOffset.UtcNow))
        {
            _logger.LogWarning("Banned session {SessionId} attempted to join room {RoomId}", callerSessionId, body.RoomId);
            return (StatusCodes.Forbidden, null);
        }

        var roomType = await FindRoomTypeByCodeAsync(model.RoomTypeCode, cancellationToken);
        var effectiveMax = GetEffectiveMaxParticipants(model, roomType);

        var participants = await GetParticipantsAsync(body.RoomId, cancellationToken);

        // Check if already a participant
        if (participants.Any(p => p.SessionId == callerSessionId.Value))
        {
            return (StatusCodes.OK, MapToRoomResponse(model, participants.Count));
        }

        // Check capacity
        if (participants.Count >= effectiveMax)
        {
            _logger.LogWarning("Room {RoomId} is full ({Count}/{Max})", body.RoomId, participants.Count, effectiveMax);
            return (StatusCodes.Conflict, null);
        }

        var now = DateTimeOffset.UtcNow;
        var role = body.Role ?? ChatParticipantRole.Member;
        var participant = new ChatParticipantModel
        {
            RoomId = body.RoomId,
            SessionId = callerSessionId.Value,
            SenderType = body.SenderType,
            SenderId = body.SenderId,
            DisplayName = body.DisplayName,
            Role = role,
            JoinedAt = now,
            LastActivityAt = now,
            IsMuted = false,
        };

        await SaveParticipantAsync(body.RoomId, participant, cancellationToken);

        model.LastActivityAt = now;
        await _roomStore.SaveAsync(roomKey, model, cancellationToken: cancellationToken);
        await _roomCache.SaveAsync(roomKey, model, cancellationToken: cancellationToken);

        // Set permission state
        await SetParticipantPermissionStateAsync(callerSessionId.Value, cancellationToken);

        // Publish service event
        await _messageBus.TryPublishAsync("chat.participant.joined", new ChatParticipantJoinedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            RoomId = body.RoomId,
            RoomTypeCode = model.RoomTypeCode,
            ParticipantSessionId = callerSessionId.Value,
            SenderType = body.SenderType,
            SenderId = body.SenderId,
            DisplayName = body.DisplayName,
            Role = role,
            CurrentCount = participants.Count,
        }, cancellationToken);

        // Broadcast client event to room participants
        var sessionIds = participants.Select(p => p.SessionId.ToString()).ToList();
        await _clientEventPublisher.PublishToSessionsAsync(sessionIds, new ChatParticipantJoinedClientEvent
        {
            RoomId = body.RoomId,
            ParticipantSessionId = callerSessionId.Value,
            SenderType = body.SenderType,
            SenderId = body.SenderId,
            DisplayName = body.DisplayName,
            Role = role,
            CurrentCount = participants.Count,
        }, cancellationToken);

        // Register session in entity session registry for room event routing
        await _entitySessionRegistry.RegisterAsync("chat-room", body.RoomId, callerSessionId.Value.ToString(), cancellationToken);
        await PublishTypingShortcutsAsync(callerSessionId.Value, body.RoomId, cancellationToken);

        _logger.LogInformation("Session {SessionId} joined room {RoomId}", callerSessionId, body.RoomId);
        var joinCount = await GetParticipantCountAsync(body.RoomId, cancellationToken);
        return (StatusCodes.OK, MapToRoomResponse(model, joinCount));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ChatRoomResponse?)> LeaveRoomAsync(
        LeaveRoomRequest body, CancellationToken cancellationToken)
    {
        var callerSessionId = GetCallerSessionId();
        if (callerSessionId == null)
        {
            return (StatusCodes.Unauthorized, null);
        }

        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.ChatLock, body.RoomId.ToString(),
            Guid.NewGuid().ToString(), _configuration.LockExpirySeconds, cancellationToken);
        if (!lockResponse.Success)
        {
            return (StatusCodes.Conflict, null);
        }

        var roomKey = $"{RoomKeyPrefix}{body.RoomId}";
        var model = await _roomStore.GetAsync(roomKey, cancellationToken);
        if (model == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var participants = await GetParticipantsAsync(body.RoomId, cancellationToken);
        var leaving = participants.FirstOrDefault(p => p.SessionId == callerSessionId.Value);
        if (leaving == null)
        {
            return (StatusCodes.NotFound, null);
        }

        await RemoveParticipantAsync(body.RoomId, callerSessionId.Value, cancellationToken);

        // If Owner leaves, promote next Moderator or oldest Member
        if (leaving.Role == ChatParticipantRole.Owner)
        {
            var remaining = await GetParticipantsAsync(body.RoomId, cancellationToken);
            if (remaining.Count > 0)
            {
                var newOwner = remaining.FirstOrDefault(p => p.Role == ChatParticipantRole.Moderator)
                    ?? remaining.OrderBy(p => p.JoinedAt).First();
                var oldRole = newOwner.Role;
                newOwner.Role = ChatParticipantRole.Owner;
                await SaveParticipantAsync(body.RoomId, newOwner, cancellationToken);

                var now = DateTimeOffset.UtcNow;
                await _messageBus.TryPublishAsync("chat.participant.role-changed", new ChatParticipantRoleChangedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    RoomId = body.RoomId,
                    ParticipantSessionId = newOwner.SessionId,
                    OldRole = oldRole,
                    NewRole = ChatParticipantRole.Owner,
                    ChangedBySessionId = null,
                }, cancellationToken);

                var promotionSessionIds = remaining.Select(p => p.SessionId.ToString()).ToList();
                if (promotionSessionIds.Count > 0)
                {
                    await _clientEventPublisher.PublishToSessionsAsync(promotionSessionIds, new ChatParticipantRoleChangedClientEvent
                    {
                        RoomId = body.RoomId,
                        ParticipantSessionId = newOwner.SessionId,
                        DisplayName = newOwner.DisplayName,
                        OldRole = oldRole,
                        NewRole = ChatParticipantRole.Owner,
                        ChangedBySessionId = null,
                        ChangedByDisplayName = null,
                    }, cancellationToken);
                }
            }
        }

        model.LastActivityAt = DateTimeOffset.UtcNow;
        await _roomStore.SaveAsync(roomKey, model, cancellationToken: cancellationToken);
        await _roomCache.SaveAsync(roomKey, model, cancellationToken: cancellationToken);

        await ClearParticipantPermissionStateAsync(callerSessionId.Value, cancellationToken);

        // Unregister from entity session registry, revoke typing shortcuts, clear typing state
        await _entitySessionRegistry.UnregisterAsync("chat-room", body.RoomId, callerSessionId.Value.ToString(), cancellationToken);
        await RevokeTypingShortcutsAsync(callerSessionId.Value, body.RoomId, cancellationToken);
        await ClearTypingStateAsync(callerSessionId.Value, body.RoomId, cancellationToken);

        var remainingParticipants = await GetParticipantsAsync(body.RoomId, cancellationToken);

        await _messageBus.TryPublishAsync("chat.participant.left", new ChatParticipantLeftEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RoomId = body.RoomId,
            ParticipantSessionId = callerSessionId.Value,
            RemainingCount = remainingParticipants.Count,
        }, cancellationToken);

        var sessionIds = remainingParticipants.Select(p => p.SessionId.ToString()).ToList();
        if (sessionIds.Count > 0)
        {
            await _clientEventPublisher.PublishToSessionsAsync(sessionIds, new ChatParticipantLeftClientEvent
            {
                RoomId = body.RoomId,
                ParticipantSessionId = callerSessionId.Value,
                DisplayName = leaving.DisplayName,
                RemainingCount = remainingParticipants.Count,
            }, cancellationToken);
        }

        var leaveCount = await GetParticipantCountAsync(body.RoomId, cancellationToken);
        return (StatusCodes.OK, MapToRoomResponse(model, leaveCount));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ParticipantsResponse?)> ListParticipantsAsync(
        ListParticipantsRequest body, CancellationToken cancellationToken)
    {
        var roomKey = $"{RoomKeyPrefix}{body.RoomId}";
        var room = await GetRoomWithCacheAsync(body.RoomId, cancellationToken);
        if (room == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var participants = await GetParticipantsAsync(body.RoomId, cancellationToken);
        var infos = participants.Select(p => new ParticipantInfo
        {
            SessionId = p.SessionId,
            SenderType = p.SenderType,
            SenderId = p.SenderId,
            DisplayName = p.DisplayName,
            Role = p.Role,
            JoinedAt = p.JoinedAt,
            IsMuted = p.IsMuted && (p.MutedUntil == null || p.MutedUntil > DateTimeOffset.UtcNow),
        }).ToList();

        return (StatusCodes.OK, new ParticipantsResponse
        {
            RoomId = body.RoomId,
            Participants = infos,
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ChatRoomResponse?)> KickParticipantAsync(
        KickParticipantRequest body, CancellationToken cancellationToken)
    {
        var callerSessionId = GetCallerSessionId();
        if (callerSessionId == null)
        {
            return (StatusCodes.Unauthorized, null);
        }

        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.ChatLock, body.RoomId.ToString(),
            Guid.NewGuid().ToString(), _configuration.LockExpirySeconds, cancellationToken);
        if (!lockResponse.Success)
        {
            return (StatusCodes.Conflict, null);
        }

        var roomKey = $"{RoomKeyPrefix}{body.RoomId}";
        var model = await _roomStore.GetAsync(roomKey, cancellationToken);
        if (model == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var participants = await GetParticipantsAsync(body.RoomId, cancellationToken);
        var caller = participants.FirstOrDefault(p => p.SessionId == callerSessionId.Value);
        if (caller == null || (caller.Role != ChatParticipantRole.Owner && caller.Role != ChatParticipantRole.Moderator))
        {
            return (StatusCodes.Forbidden, null);
        }

        var target = participants.FirstOrDefault(p => p.SessionId == body.TargetSessionId);
        if (target == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Cannot kick someone with equal or higher role
        if (target.Role <= caller.Role && caller.Role != ChatParticipantRole.Owner)
        {
            return (StatusCodes.Forbidden, null);
        }

        await RemoveParticipantAsync(body.RoomId, body.TargetSessionId, cancellationToken);

        model.LastActivityAt = DateTimeOffset.UtcNow;
        await _roomStore.SaveAsync(roomKey, model, cancellationToken: cancellationToken);
        await _roomCache.SaveAsync(roomKey, model, cancellationToken: cancellationToken);

        await ClearParticipantPermissionStateAsync(body.TargetSessionId, cancellationToken);

        // Unregister from entity session registry, revoke typing shortcuts, clear typing state
        await _entitySessionRegistry.UnregisterAsync("chat-room", body.RoomId, body.TargetSessionId.ToString(), cancellationToken);
        await RevokeTypingShortcutsAsync(body.TargetSessionId, body.RoomId, cancellationToken);
        await ClearTypingStateAsync(body.TargetSessionId, body.RoomId, cancellationToken);

        await _messageBus.TryPublishAsync("chat.participant.kicked", new ChatParticipantKickedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RoomId = body.RoomId,
            TargetSessionId = body.TargetSessionId,
            KickedBySessionId = callerSessionId.Value,
            Reason = body.Reason,
        }, cancellationToken);

        var sessionIds = participants.Select(p => p.SessionId.ToString()).Append(body.TargetSessionId.ToString()).ToList();
        await _clientEventPublisher.PublishToSessionsAsync(sessionIds, new ChatParticipantKickedClientEvent
        {
            RoomId = body.RoomId,
            TargetSessionId = body.TargetSessionId,
            TargetDisplayName = target.DisplayName,
            Reason = body.Reason,
        }, cancellationToken);

        var kickCount = await GetParticipantCountAsync(body.RoomId, cancellationToken);
        return (StatusCodes.OK, MapToRoomResponse(model, kickCount));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ChatRoomResponse?)> BanParticipantAsync(
        BanParticipantRequest body, CancellationToken cancellationToken)
    {
        var callerSessionId = GetCallerSessionId();
        if (callerSessionId == null)
        {
            return (StatusCodes.Unauthorized, null);
        }

        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.ChatLock, body.RoomId.ToString(),
            Guid.NewGuid().ToString(), _configuration.LockExpirySeconds, cancellationToken);
        if (!lockResponse.Success)
        {
            return (StatusCodes.Conflict, null);
        }

        var roomKey = $"{RoomKeyPrefix}{body.RoomId}";
        var model = await _roomStore.GetAsync(roomKey, cancellationToken);
        if (model == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var participants = await GetParticipantsAsync(body.RoomId, cancellationToken);
        var caller = participants.FirstOrDefault(p => p.SessionId == callerSessionId.Value);
        if (caller == null || (caller.Role != ChatParticipantRole.Owner && caller.Role != ChatParticipantRole.Moderator))
        {
            return (StatusCodes.Forbidden, null);
        }

        // Save ban record
        var now = DateTimeOffset.UtcNow;
        var ban = new ChatBanModel
        {
            BanId = Guid.NewGuid(),
            RoomId = body.RoomId,
            TargetSessionId = body.TargetSessionId,
            BannedBySessionId = callerSessionId.Value,
            Reason = body.Reason,
            BannedAt = now,
            ExpiresAt = body.DurationMinutes.HasValue ? now.AddMinutes(body.DurationMinutes.Value) : null,
        };
        var banKey = $"{BanKeyPrefix}{body.RoomId}:{body.TargetSessionId}";
        await _banStore.SaveAsync(banKey, ban, cancellationToken: cancellationToken);

        // Kick if currently present
        var target = await GetParticipantAsync(body.RoomId, body.TargetSessionId, cancellationToken);
        if (target != null)
        {
            await RemoveParticipantAsync(body.RoomId, body.TargetSessionId, cancellationToken);
            await ClearParticipantPermissionStateAsync(body.TargetSessionId, cancellationToken);
        }

        // Unregister from entity session registry, revoke typing shortcuts, clear typing state
        await _entitySessionRegistry.UnregisterAsync("chat-room", body.RoomId, body.TargetSessionId.ToString(), cancellationToken);
        await RevokeTypingShortcutsAsync(body.TargetSessionId, body.RoomId, cancellationToken);
        await ClearTypingStateAsync(body.TargetSessionId, body.RoomId, cancellationToken);

        model.LastActivityAt = now;
        await _roomStore.SaveAsync(roomKey, model, cancellationToken: cancellationToken);
        await _roomCache.SaveAsync(roomKey, model, cancellationToken: cancellationToken);

        await _messageBus.TryPublishAsync("chat.participant.banned", new ChatParticipantBannedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            RoomId = body.RoomId,
            TargetSessionId = body.TargetSessionId,
            BannedBySessionId = callerSessionId.Value,
            Reason = body.Reason,
            DurationMinutes = body.DurationMinutes,
        }, cancellationToken);

        var sessionIds = participants.Select(p => p.SessionId.ToString()).Append(body.TargetSessionId.ToString()).ToList();
        await _clientEventPublisher.PublishToSessionsAsync(sessionIds, new ChatParticipantBannedClientEvent
        {
            RoomId = body.RoomId,
            TargetSessionId = body.TargetSessionId,
            TargetDisplayName = target?.DisplayName,
            Reason = body.Reason,
        }, cancellationToken);

        var banCount = await GetParticipantCountAsync(body.RoomId, cancellationToken);
        return (StatusCodes.OK, MapToRoomResponse(model, banCount));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ChatRoomResponse?)> UnbanParticipantAsync(
        UnbanParticipantRequest body, CancellationToken cancellationToken)
    {
        var callerSessionId = GetCallerSessionId();
        if (callerSessionId == null)
        {
            return (StatusCodes.Unauthorized, null);
        }

        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.ChatLock, body.RoomId.ToString(),
            Guid.NewGuid().ToString(), _configuration.LockExpirySeconds, cancellationToken);
        if (!lockResponse.Success)
        {
            return (StatusCodes.Conflict, null);
        }

        var roomKey = $"{RoomKeyPrefix}{body.RoomId}";
        var model = await _roomStore.GetAsync(roomKey, cancellationToken);
        if (model == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var participants = await GetParticipantsAsync(body.RoomId, cancellationToken);
        var caller = participants.FirstOrDefault(p => p.SessionId == callerSessionId.Value);
        if (caller == null || (caller.Role != ChatParticipantRole.Owner && caller.Role != ChatParticipantRole.Moderator))
        {
            return (StatusCodes.Forbidden, null);
        }

        var banKey = $"{BanKeyPrefix}{body.RoomId}:{body.TargetSessionId}";
        var ban = await _banStore.GetAsync(banKey, cancellationToken);
        if (ban == null)
        {
            return (StatusCodes.NotFound, null);
        }

        await _banStore.DeleteAsync(banKey, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        await _messageBus.TryPublishAsync("chat.participant.unbanned", new ChatParticipantUnbannedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            RoomId = body.RoomId,
            TargetSessionId = body.TargetSessionId,
            UnbannedBySessionId = callerSessionId.Value,
        }, cancellationToken);

        var sessionIds = participants.Select(p => p.SessionId.ToString()).ToList();
        if (sessionIds.Count > 0)
        {
            await _clientEventPublisher.PublishToSessionsAsync(sessionIds, new ChatParticipantUnbannedClientEvent
            {
                RoomId = body.RoomId,
                TargetSessionId = body.TargetSessionId,
                UnbannedByDisplayName = caller.DisplayName,
            }, cancellationToken);
        }

        var unbanCount = await GetParticipantCountAsync(body.RoomId, cancellationToken);
        return (StatusCodes.OK, MapToRoomResponse(model, unbanCount));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ChatRoomResponse?)> MuteParticipantAsync(
        MuteParticipantRequest body, CancellationToken cancellationToken)
    {
        var callerSessionId = GetCallerSessionId();
        if (callerSessionId == null)
        {
            return (StatusCodes.Unauthorized, null);
        }

        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.ChatLock, body.RoomId.ToString(),
            Guid.NewGuid().ToString(), _configuration.LockExpirySeconds, cancellationToken);
        if (!lockResponse.Success)
        {
            return (StatusCodes.Conflict, null);
        }

        var roomKey = $"{RoomKeyPrefix}{body.RoomId}";
        var model = await _roomStore.GetAsync(roomKey, cancellationToken);
        if (model == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var participants = await GetParticipantsAsync(body.RoomId, cancellationToken);
        var caller = participants.FirstOrDefault(p => p.SessionId == callerSessionId.Value);
        if (caller == null || (caller.Role != ChatParticipantRole.Owner && caller.Role != ChatParticipantRole.Moderator))
        {
            return (StatusCodes.Forbidden, null);
        }

        var target = await GetParticipantAsync(body.RoomId, body.TargetSessionId, cancellationToken);
        if (target == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var now = DateTimeOffset.UtcNow;
        target.IsMuted = true;
        target.MutedUntil = body.DurationMinutes.HasValue ? now.AddMinutes(body.DurationMinutes.Value) : null;
        await SaveParticipantAsync(body.RoomId, target, cancellationToken);

        await _messageBus.TryPublishAsync("chat.participant.muted", new ChatParticipantMutedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            RoomId = body.RoomId,
            TargetSessionId = body.TargetSessionId,
            MutedBySessionId = callerSessionId.Value,
            DurationMinutes = body.DurationMinutes,
        }, cancellationToken);

        var sessionIds = participants.Select(p => p.SessionId.ToString()).ToList();
        await _clientEventPublisher.PublishToSessionsAsync(sessionIds, new ChatParticipantMutedClientEvent
        {
            RoomId = body.RoomId,
            TargetSessionId = body.TargetSessionId,
            TargetDisplayName = target.DisplayName,
            DurationMinutes = body.DurationMinutes,
        }, cancellationToken);

        var muteCount = await GetParticipantCountAsync(body.RoomId, cancellationToken);
        return (StatusCodes.OK, MapToRoomResponse(model, muteCount));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ChatRoomResponse?)> UnmuteParticipantAsync(
        UnmuteParticipantRequest body, CancellationToken cancellationToken)
    {
        var callerSessionId = GetCallerSessionId();
        if (callerSessionId == null)
        {
            return (StatusCodes.Unauthorized, null);
        }

        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.ChatLock, body.RoomId.ToString(),
            Guid.NewGuid().ToString(), _configuration.LockExpirySeconds, cancellationToken);
        if (!lockResponse.Success)
        {
            return (StatusCodes.Conflict, null);
        }

        var roomKey = $"{RoomKeyPrefix}{body.RoomId}";
        var model = await _roomStore.GetAsync(roomKey, cancellationToken);
        if (model == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var participants = await GetParticipantsAsync(body.RoomId, cancellationToken);
        var caller = participants.FirstOrDefault(p => p.SessionId == callerSessionId.Value);
        if (caller == null || (caller.Role != ChatParticipantRole.Owner && caller.Role != ChatParticipantRole.Moderator))
        {
            return (StatusCodes.Forbidden, null);
        }

        var target = await GetParticipantAsync(body.RoomId, body.TargetSessionId, cancellationToken);
        if (target == null)
        {
            return (StatusCodes.NotFound, null);
        }

        if (!target.IsMuted)
        {
            return (StatusCodes.BadRequest, null);
        }

        target.IsMuted = false;
        target.MutedUntil = null;
        await SaveParticipantAsync(body.RoomId, target, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        await _messageBus.TryPublishAsync("chat.participant.unmuted", new ChatParticipantUnmutedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            RoomId = body.RoomId,
            TargetSessionId = body.TargetSessionId,
            UnmutedBySessionId = callerSessionId.Value,
        }, cancellationToken);

        var sessionIds = participants.Select(p => p.SessionId.ToString()).ToList();
        await _clientEventPublisher.PublishToSessionsAsync(sessionIds, new ChatParticipantUnmutedClientEvent
        {
            RoomId = body.RoomId,
            TargetSessionId = body.TargetSessionId,
            TargetDisplayName = target.DisplayName,
        }, cancellationToken);

        var unmuteCount = await GetParticipantCountAsync(body.RoomId, cancellationToken);
        return (StatusCodes.OK, MapToRoomResponse(model, unmuteCount));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ChatRoomResponse?)> ChangeParticipantRoleAsync(
        ChangeParticipantRoleRequest body, CancellationToken cancellationToken)
    {
        var callerSessionId = GetCallerSessionId();
        if (callerSessionId == null)
        {
            return (StatusCodes.Unauthorized, null);
        }

        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.ChatLock, body.RoomId.ToString(),
            Guid.NewGuid().ToString(), _configuration.LockExpirySeconds, cancellationToken);
        if (!lockResponse.Success)
        {
            return (StatusCodes.Conflict, null);
        }

        var roomKey = $"{RoomKeyPrefix}{body.RoomId}";
        var model = await _roomStore.GetAsync(roomKey, cancellationToken);
        if (model == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var participants = await GetParticipantsAsync(body.RoomId, cancellationToken);
        var caller = participants.FirstOrDefault(p => p.SessionId == callerSessionId.Value);
        if (caller == null || caller.Role != ChatParticipantRole.Owner)
        {
            return (StatusCodes.Forbidden, null);
        }

        // Cannot change own role
        if (body.TargetSessionId == callerSessionId.Value)
        {
            return (StatusCodes.BadRequest, null);
        }

        // Cannot promote to Owner (ownership transfer happens on owner leave)
        if (body.NewRole == ChatParticipantRole.Owner)
        {
            return (StatusCodes.BadRequest, null);
        }

        var target = participants.FirstOrDefault(p => p.SessionId == body.TargetSessionId);
        if (target == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var oldRole = target.Role;
        target.Role = body.NewRole;
        await SaveParticipantAsync(body.RoomId, target, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        await _messageBus.TryPublishAsync("chat.participant.role-changed", new ChatParticipantRoleChangedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            RoomId = body.RoomId,
            ParticipantSessionId = body.TargetSessionId,
            OldRole = oldRole,
            NewRole = body.NewRole,
            ChangedBySessionId = callerSessionId.Value,
        }, cancellationToken);

        var sessionIds = participants.Select(p => p.SessionId.ToString()).ToList();
        await _clientEventPublisher.PublishToSessionsAsync(sessionIds, new ChatParticipantRoleChangedClientEvent
        {
            RoomId = body.RoomId,
            ParticipantSessionId = body.TargetSessionId,
            DisplayName = target.DisplayName,
            OldRole = oldRole,
            NewRole = body.NewRole,
            ChangedBySessionId = callerSessionId.Value,
            ChangedByDisplayName = caller.DisplayName,
        }, cancellationToken);

        var roleCount = await GetParticipantCountAsync(body.RoomId, cancellationToken);
        return (StatusCodes.OK, MapToRoomResponse(model, roleCount));
    }

    // ============================================================================
    // MESSAGE OPERATIONS
    // ============================================================================

    /// <inheritdoc />
    public async Task<(StatusCodes, ChatMessageResponse?)> SendMessageAsync(
        SendMessageRequest body, CancellationToken cancellationToken)
    {
        var callerSessionId = GetCallerSessionId();
        if (callerSessionId == null)
        {
            return (StatusCodes.Unauthorized, null);
        }

        var model = await GetRoomWithCacheAsync(body.RoomId, cancellationToken);
        if (model == null)
        {
            return (StatusCodes.NotFound, null);
        }

        if (model.Status == ChatRoomStatus.Locked || model.Status == ChatRoomStatus.Archived)
        {
            return (StatusCodes.Forbidden, null);
        }

        var sender = await GetParticipantAsync(body.RoomId, callerSessionId.Value, cancellationToken);
        if (sender == null)
        {
            return (StatusCodes.Forbidden, null);
        }

        if (sender.Role == ChatParticipantRole.ReadOnly)
        {
            return (StatusCodes.Forbidden, null);
        }

        // Check mute (auto-unmute if expired)
        if (sender.IsMuted)
        {
            if (sender.MutedUntil.HasValue && sender.MutedUntil.Value <= DateTimeOffset.UtcNow)
            {
                sender.IsMuted = false;
                sender.MutedUntil = null;
                await SaveParticipantAsync(body.RoomId, sender, cancellationToken);

                await _messageBus.TryPublishAsync("chat.participant.unmuted", new ChatParticipantUnmutedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    RoomId = body.RoomId,
                    TargetSessionId = callerSessionId.Value,
                    UnmutedBySessionId = callerSessionId.Value,
                }, cancellationToken);

                var unmuteParticipants = await GetParticipantsAsync(body.RoomId, cancellationToken);
                var unmuteSessionIds = unmuteParticipants.Select(p => p.SessionId.ToString()).ToList();
                if (unmuteSessionIds.Count > 0)
                {
                    await _clientEventPublisher.PublishToSessionsAsync(unmuteSessionIds, new ChatParticipantUnmutedClientEvent
                    {
                        RoomId = body.RoomId,
                        TargetSessionId = callerSessionId.Value,
                        TargetDisplayName = sender.DisplayName,
                    }, cancellationToken);
                }
            }
            else
            {
                return (StatusCodes.Forbidden, null);
            }
        }

        // Validate message content against room type
        var roomType = await FindRoomTypeByCodeAsync(model.RoomTypeCode, cancellationToken);
        if (roomType == null)
        {
            _logger.LogError("Room type {RoomTypeCode} not found for room {RoomId}", model.RoomTypeCode, body.RoomId);
            return (StatusCodes.InternalServerError, null);
        }

        var validationError = ValidateMessageContent(body.Content, roomType);
        if (validationError != null)
        {
            _logger.LogWarning("Message validation failed for room {RoomId}: {Error}", body.RoomId, validationError);
            return (StatusCodes.BadRequest, null);
        }

        // Check rate limit using atomic Redis counter with 60s TTL
        var effectiveRateLimit = GetEffectiveRateLimit(roomType);
        var now = DateTimeOffset.UtcNow;
        var rateLimitKey = $"rate:{body.RoomId}:{callerSessionId.Value}";
        var currentCount = await _participantStore.IncrementAsync(
            rateLimitKey, 1, new StateOptions { Ttl = 60 }, cancellationToken);
        if (currentCount > effectiveRateLimit)
        {
            return (StatusCodes.BadRequest, null);
        }

        sender.LastActivityAt = now;
        await SaveParticipantAsync(body.RoomId, sender, cancellationToken);

        // Create message
        var messageId = Guid.NewGuid();
        var message = new ChatMessageModel
        {
            MessageId = messageId,
            RoomId = body.RoomId,
            SenderType = body.SenderType ?? sender.SenderType,
            SenderId = body.SenderId ?? sender.SenderId,
            DisplayName = body.DisplayName ?? sender.DisplayName,
            Timestamp = now,
            MessageFormat = roomType.MessageFormat,
            TextContent = body.Content.Text,
            SentimentCategory = body.Content.SentimentCategory,
            SentimentIntensity = body.Content.SentimentIntensity,
            EmojiCode = body.Content.EmojiCode,
            EmojiSetId = body.Content.EmojiSetId,
            CustomPayload = body.Content.CustomPayload,
            IsPinned = false,
        };

        // Store based on persistence mode
        if (roomType.PersistenceMode == PersistenceMode.Persistent)
        {
            var msgKey = $"{body.RoomId}:{messageId}";
            await _messageStore.SaveAsync(msgKey, message, cancellationToken: cancellationToken);
        }
        else
        {
            var msgKey = $"{body.RoomId}:{messageId}";
            var ttlSeconds = _configuration.EphemeralMessageTtlMinutes * 60;
            await _messageBuffer.SaveAsync(msgKey, message,
                new StateOptions { Ttl = ttlSeconds }, cancellationToken);
        }

        // Update room last activity
        var roomKey = $"{RoomKeyPrefix}{body.RoomId}";
        model.LastActivityAt = now;
        await _roomStore.SaveAsync(roomKey, model, cancellationToken: cancellationToken);
        await _roomCache.SaveAsync(roomKey, model, cancellationToken: cancellationToken);

        // Publish service event (no text/custom content for privacy)
        await _messageBus.TryPublishAsync("chat.message.sent", new ChatMessageSentEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            RoomId = body.RoomId,
            RoomTypeCode = model.RoomTypeCode,
            MessageId = messageId,
            SenderType = message.SenderType,
            SenderId = message.SenderId,
            MessageFormat = roomType.MessageFormat,
            SentimentCategory = message.SentimentCategory,
            SentimentIntensity = message.SentimentIntensity,
            EmojiCode = message.EmojiCode,
        }, cancellationToken);

        // Clear typing state on message send
        await ClearTypingStateAsync(callerSessionId.Value, body.RoomId, cancellationToken);

        // Broadcast to participants (includes full content for rendering)
        var participants = await GetParticipantsAsync(body.RoomId, cancellationToken);
        var sessionIds = participants.Select(p => p.SessionId.ToString()).ToList();
        await _clientEventPublisher.PublishToSessionsAsync(sessionIds, new ChatMessageReceivedClientEvent
        {
            RoomId = body.RoomId,
            MessageId = messageId,
            SenderType = message.SenderType,
            SenderId = message.SenderId,
            DisplayName = message.DisplayName,
            MessageFormat = roomType.MessageFormat,
            TextContent = message.TextContent,
            SentimentCategory = message.SentimentCategory,
            SentimentIntensity = message.SentimentIntensity,
            EmojiCode = message.EmojiCode,
            EmojiSetId = message.EmojiSetId,
            CustomPayload = message.CustomPayload,
        }, cancellationToken);

        return (StatusCodes.OK, MapToMessageResponse(message, model.RoomTypeCode));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, SendMessageBatchResponse?)> SendMessageBatchAsync(
        SendMessageBatchRequest body, CancellationToken cancellationToken)
    {
        var model = await GetRoomWithCacheAsync(body.RoomId, cancellationToken);
        if (model == null)
        {
            return (StatusCodes.NotFound, null);
        }

        if (model.Status != ChatRoomStatus.Active)
        {
            return (StatusCodes.Forbidden, null);
        }

        var roomType = await FindRoomTypeByCodeAsync(model.RoomTypeCode, cancellationToken);
        if (roomType == null)
        {
            return (StatusCodes.InternalServerError, null);
        }

        var participants = await GetParticipantsAsync(body.RoomId, cancellationToken);
        var sessionIds = participants.Select(p => p.SessionId.ToString()).ToList();
        var now = DateTimeOffset.UtcNow;
        var messageCount = 0;
        var failed = new List<BatchMessageFailure>();
        var messages = body.Messages.ToList();

        for (var i = 0; i < messages.Count; i++)
        {
            var entry = messages[i];

            var validationError = ValidateMessageContent(entry.Content, roomType);
            if (validationError != null)
            {
                failed.Add(new BatchMessageFailure { Index = i, Error = validationError });
                continue;
            }

            try
            {
                var messageId = Guid.NewGuid();
                var message = new ChatMessageModel
                {
                    MessageId = messageId,
                    RoomId = body.RoomId,
                    SenderType = entry.SenderType,
                    SenderId = entry.SenderId,
                    DisplayName = entry.DisplayName,
                    Timestamp = now,
                    MessageFormat = roomType.MessageFormat,
                    TextContent = entry.Content.Text,
                    SentimentCategory = entry.Content.SentimentCategory,
                    SentimentIntensity = entry.Content.SentimentIntensity,
                    EmojiCode = entry.Content.EmojiCode,
                    EmojiSetId = entry.Content.EmojiSetId,
                    CustomPayload = entry.Content.CustomPayload,
                };

                if (roomType.PersistenceMode == PersistenceMode.Persistent)
                {
                    await _messageStore.SaveAsync($"{body.RoomId}:{messageId}", message,
                        cancellationToken: cancellationToken);
                }
                else
                {
                    var ttlSeconds = _configuration.EphemeralMessageTtlMinutes * 60;
                    await _messageBuffer.SaveAsync($"{body.RoomId}:{messageId}", message,
                        new StateOptions { Ttl = ttlSeconds }, cancellationToken);
                }

                await _clientEventPublisher.PublishToSessionsAsync(sessionIds, new ChatMessageReceivedClientEvent
                {
                    RoomId = body.RoomId,
                    MessageId = messageId,
                    SenderType = message.SenderType,
                    SenderId = message.SenderId,
                    DisplayName = message.DisplayName,
                    MessageFormat = roomType.MessageFormat,
                    TextContent = message.TextContent,
                    SentimentCategory = message.SentimentCategory,
                    SentimentIntensity = message.SentimentIntensity,
                    EmojiCode = message.EmojiCode,
                    EmojiSetId = message.EmojiSetId,
                    CustomPayload = message.CustomPayload,
                }, cancellationToken);

                messageCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send batch message at index {Index} in room {RoomId}",
                    i, body.RoomId);
                failed.Add(new BatchMessageFailure { Index = i, Error = ex.Message });
            }
        }

        // Update room activity
        var roomKey = $"{RoomKeyPrefix}{body.RoomId}";
        model.LastActivityAt = now;
        await _roomStore.SaveAsync(roomKey, model, cancellationToken: cancellationToken);
        await _roomCache.SaveAsync(roomKey, model, cancellationToken: cancellationToken);

        return (StatusCodes.OK, new SendMessageBatchResponse
        {
            MessageCount = messageCount,
            Failed = failed,
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, MessageHistoryResponse?)> GetMessageHistoryAsync(
        MessageHistoryRequest body, CancellationToken cancellationToken)
    {
        var model = await GetRoomWithCacheAsync(body.RoomId, cancellationToken);
        if (model == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var roomType = await FindRoomTypeByCodeAsync(model.RoomTypeCode, cancellationToken);
        if (roomType == null)
        {
            return (StatusCodes.InternalServerError, null);
        }

        // Ephemeral rooms have no persistent history
        if (roomType.PersistenceMode == PersistenceMode.Ephemeral)
        {
            return (StatusCodes.OK, new MessageHistoryResponse
            {
                Messages = new List<ChatMessageResponse>(),
                HasMore = false,
            });
        }

        var limit = Math.Min(body.Limit, _configuration.MessageHistoryPageSize);
        var conditions = new List<QueryCondition>
            {
                new() { Path = "$.RoomId", Operator = QueryOperator.Equals, Value = body.RoomId.ToString() },
                new() { Path = "$.MessageId", Operator = QueryOperator.Exists, Value = true },
            };

        if (!string.IsNullOrEmpty(body.Before))
        {
            conditions.Add(new QueryCondition
            {
                Path = "$.Timestamp",
                Operator = QueryOperator.LessThan,
                Value = body.Before,
            });
        }

        var result = await _messageStore.JsonQueryPagedAsync(
            conditions, 0, limit + 1,
            new JsonSortSpec { Path = "$.Timestamp", Descending = true },
            cancellationToken);

        var messages = result.Items.Take(limit)
            .Select(r => MapToMessageResponse(r.Value, model.RoomTypeCode))
            .ToList();

        var hasMore = result.Items.Count > limit;
        var nextCursor = hasMore && messages.Count > 0
            ? messages.Last().Timestamp.ToString("O")
            : null;

        return (StatusCodes.OK, new MessageHistoryResponse
        {
            Messages = messages,
            HasMore = hasMore,
            NextCursor = nextCursor,
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ChatMessageResponse?)> DeleteMessageAsync(
        DeleteMessageRequest body, CancellationToken cancellationToken)
    {
        var callerSessionId = GetCallerSessionId();
        if (callerSessionId == null)
        {
            return (StatusCodes.Unauthorized, null);
        }

        var msgKey = $"{body.RoomId}:{body.MessageId}";
        var message = await _messageStore.GetAsync(msgKey, cancellationToken);
        if (message == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var model = await GetRoomWithCacheAsync(body.RoomId, cancellationToken);
        if (model == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var snapshot = MapToMessageResponse(message, model.RoomTypeCode);
        await _messageStore.DeleteAsync(msgKey, cancellationToken);

        await _messageBus.TryPublishAsync("chat.message.deleted", new ChatMessageDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RoomId = body.RoomId,
            MessageId = body.MessageId,
            DeletedBySessionId = callerSessionId.Value,
        }, cancellationToken);

        var participants = await GetParticipantsAsync(body.RoomId, cancellationToken);
        var sessionIds = participants.Select(p => p.SessionId.ToString()).ToList();
        await _clientEventPublisher.PublishToSessionsAsync(sessionIds, new ChatMessageDeletedClientEvent
        {
            RoomId = body.RoomId,
            MessageId = body.MessageId,
        }, cancellationToken);

        return (StatusCodes.OK, snapshot);
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ChatMessageResponse?)> PinMessageAsync(
        PinMessageRequest body, CancellationToken cancellationToken)
    {
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.ChatLock, body.RoomId.ToString(),
            Guid.NewGuid().ToString(), _configuration.LockExpirySeconds, cancellationToken);
        if (!lockResponse.Success)
        {
            return (StatusCodes.Conflict, null);
        }

        var msgKey = $"{body.RoomId}:{body.MessageId}";
        var message = await _messageStore.GetAsync(msgKey, cancellationToken);
        if (message == null)
        {
            return (StatusCodes.NotFound, null);
        }

        if (message.IsPinned)
        {
            var model2 = await GetRoomWithCacheAsync(body.RoomId, cancellationToken);
            if (model2 == null)
            {
                return (StatusCodes.NotFound, null);
            }
            return (StatusCodes.OK, MapToMessageResponse(message, model2.RoomTypeCode));
        }

        // Check max pinned messages
        var pinnedConditions = new List<QueryCondition>
            {
                new() { Path = "$.RoomId", Operator = QueryOperator.Equals, Value = body.RoomId.ToString() },
                new() { Path = "$.IsPinned", Operator = QueryOperator.Equals, Value = true },
            };
        var pinnedCount = await _messageStore.JsonCountAsync(pinnedConditions, cancellationToken);
        if (pinnedCount >= _configuration.MaxPinnedMessagesPerRoom)
        {
            return (StatusCodes.Conflict, null);
        }

        message.IsPinned = true;
        await _messageStore.SaveAsync(msgKey, message, cancellationToken: cancellationToken);

        var model = await GetRoomWithCacheAsync(body.RoomId, cancellationToken);
        if (model == null)
        {
            return (StatusCodes.NotFound, null);
        }
        var participants = await GetParticipantsAsync(body.RoomId, cancellationToken);
        var sessionIds = participants.Select(p => p.SessionId.ToString()).ToList();
        await _clientEventPublisher.PublishToSessionsAsync(sessionIds, new ChatMessagePinnedClientEvent
        {
            RoomId = body.RoomId,
            MessageId = body.MessageId,
            IsPinned = true,
        }, cancellationToken);

        return (StatusCodes.OK, MapToMessageResponse(message, model.RoomTypeCode));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, ChatMessageResponse?)> UnpinMessageAsync(
        UnpinMessageRequest body, CancellationToken cancellationToken)
    {
        var msgKey = $"{body.RoomId}:{body.MessageId}";
        var message = await _messageStore.GetAsync(msgKey, cancellationToken);
        if (message == null)
        {
            return (StatusCodes.NotFound, null);
        }

        message.IsPinned = false;
        await _messageStore.SaveAsync(msgKey, message, cancellationToken: cancellationToken);

        var model = await GetRoomWithCacheAsync(body.RoomId, cancellationToken);
        if (model == null)
        {
            return (StatusCodes.NotFound, null);
        }
        var participants = await GetParticipantsAsync(body.RoomId, cancellationToken);
        var sessionIds = participants.Select(p => p.SessionId.ToString()).ToList();
        await _clientEventPublisher.PublishToSessionsAsync(sessionIds, new ChatMessagePinnedClientEvent
        {
            RoomId = body.RoomId,
            MessageId = body.MessageId,
            IsPinned = false,
        }, cancellationToken);

        return (StatusCodes.OK, MapToMessageResponse(message, model.RoomTypeCode));
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, SearchMessagesResponse?)> SearchMessagesAsync(
        SearchMessagesRequest body, CancellationToken cancellationToken)
    {
        var model = await GetRoomWithCacheAsync(body.RoomId, cancellationToken);
        if (model == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var roomType = await FindRoomTypeByCodeAsync(model.RoomTypeCode, cancellationToken);
        if (roomType != null && roomType.PersistenceMode == PersistenceMode.Ephemeral)
        {
            return (StatusCodes.BadRequest, null);
        }

        var conditions = new List<QueryCondition>
            {
                new() { Path = "$.RoomId", Operator = QueryOperator.Equals, Value = body.RoomId.ToString() },
                new() { Path = "$.TextContent", Operator = QueryOperator.Contains, Value = body.Query },
            };

        var result = await _messageStore.JsonQueryPagedAsync(
            conditions, 0, body.Limit,
            new JsonSortSpec { Path = "$.Timestamp", Descending = true },
            cancellationToken);

        var messages = result.Items.Select(r => MapToMessageResponse(r.Value, model.RoomTypeCode)).ToList();

        return (StatusCodes.OK, new SearchMessagesResponse
        {
            Messages = messages,
            TotalMatches = (int)result.TotalCount,
        });
    }

    // ============================================================================
    // ADMIN OPERATIONS
    // ============================================================================

    /// <inheritdoc />
    public async Task<(StatusCodes, ListRoomsResponse?)> AdminListRoomsAsync(
        AdminListRoomsRequest body, CancellationToken cancellationToken)
    {
        var conditions = new List<QueryCondition>
            {
                new() { Path = "$.RoomId", Operator = QueryOperator.Exists, Value = true }
            };

        if (!string.IsNullOrEmpty(body.RoomTypeCode))
        {
            conditions.Add(new QueryCondition { Path = "$.RoomTypeCode", Operator = QueryOperator.Equals, Value = body.RoomTypeCode });
        }
        if (body.Status.HasValue)
        {
            conditions.Add(new QueryCondition { Path = "$.Status", Operator = QueryOperator.Equals, Value = body.Status.Value.ToString() });
        }

        var offset = body.Page * body.PageSize;
        var result = await _roomStore.JsonQueryPagedAsync(
            conditions, offset, body.PageSize,
            new JsonSortSpec { Path = "$.CreatedAt", Descending = true },
            cancellationToken);

        var items = new List<ChatRoomResponse>();
        foreach (var r in result.Items)
        {
            var count = await GetParticipantCountAsync(r.Value.RoomId, cancellationToken);
            items.Add(MapToRoomResponse(r.Value, count));
        }

        return (StatusCodes.OK, new ListRoomsResponse
        {
            Items = items,
            TotalCount = (int)result.TotalCount,
            Page = body.Page,
            PageSize = body.PageSize,
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, AdminStatsResponse?)> AdminGetStatsAsync(
        AdminGetStatsRequest body, CancellationToken cancellationToken)
    {
        var allRoomsConditions = new List<QueryCondition>
            {
                new() { Path = "$.RoomId", Operator = QueryOperator.Exists, Value = true }
            };
        var totalRooms = await _roomStore.JsonCountAsync(allRoomsConditions, cancellationToken);

        var activeConditions = new List<QueryCondition>
            {
                new() { Path = "$.Status", Operator = QueryOperator.Equals, Value = ChatRoomStatus.Active.ToString() }
            };
        var activeRooms = await _roomStore.JsonCountAsync(activeConditions, cancellationToken);

        var lockedConditions = new List<QueryCondition>
            {
                new() { Path = "$.Status", Operator = QueryOperator.Equals, Value = ChatRoomStatus.Locked.ToString() }
            };
        var lockedRooms = await _roomStore.JsonCountAsync(lockedConditions, cancellationToken);

        var archivedConditions = new List<QueryCondition>
            {
                new() { Path = "$.Status", Operator = QueryOperator.Equals, Value = ChatRoomStatus.Archived.ToString() }
            };
        var archivedRooms = await _roomStore.JsonCountAsync(archivedConditions, cancellationToken);

        var typeConditions = new List<QueryCondition>
            {
                new() { Path = "$.Code", Operator = QueryOperator.Exists, Value = true }
            };
        var totalRoomTypes = await _roomTypeStore.JsonCountAsync(typeConditions, cancellationToken);

        // Sum participant counts across all rooms via hash counts
        var allRooms = await _roomStore.JsonQueryPagedAsync(allRoomsConditions, 0, 1000, cancellationToken: cancellationToken);
        var totalParticipants = 0L;
        foreach (var r in allRooms.Items)
        {
            totalParticipants += await GetParticipantCountAsync(r.Value.RoomId, cancellationToken);
        }

        return (StatusCodes.OK, new AdminStatsResponse
        {
            TotalRooms = (int)totalRooms,
            ActiveRooms = (int)activeRooms,
            LockedRooms = (int)lockedRooms,
            ArchivedRooms = (int)archivedRooms,
            TotalParticipants = (int)totalParticipants,
            TotalRoomTypes = (int)totalRoomTypes,
        });
    }

    /// <inheritdoc />
    public async Task<(StatusCodes, AdminCleanupResponse?)> AdminForceCleanupAsync(
        AdminForceCleanupRequest body, CancellationToken cancellationToken)
    {
        var result = await CleanupIdleRoomsAsync(cancellationToken);
        return (StatusCodes.OK, result);
    }

    // ============================================================================
    // INTERNAL: Cleanup logic (shared with IdleRoomCleanupWorker)
    // ============================================================================

    /// <summary>
    /// Scans for idle rooms and cleans them up. Used by both AdminForceCleanup and the background worker.
    /// </summary>
    internal async Task<AdminCleanupResponse> CleanupIdleRoomsAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-_configuration.IdleRoomTimeoutMinutes);
        var conditions = new List<QueryCondition>
        {
            new() { Path = "$.RoomId", Operator = QueryOperator.Exists, Value = true },
            new() { Path = "$.LastActivityAt", Operator = QueryOperator.LessThan, Value = cutoff.ToString("O") },
        };

        var result = await _roomStore.JsonQueryPagedAsync(conditions, 0, 1000, cancellationToken: cancellationToken);

        var archivedCount = 0;
        var deletedCount = 0;

        foreach (var item in result.Items)
        {
            var room = item.Value;

            // Skip contract-governed rooms
            if (room.ContractId.HasValue)
            {
                continue;
            }

            var roomType = await FindRoomTypeByCodeAsync(room.RoomTypeCode, cancellationToken);
            var roomKey = $"{RoomKeyPrefix}{room.RoomId}";

            if (roomType?.PersistenceMode == PersistenceMode.Persistent && !room.IsArchived)
            {
                // Archive persistent rooms
                try
                {
                    await _resourceClient.ExecuteCompressAsync(
                        new ExecuteCompressRequest { ResourceType = "chat-room", ResourceId = room.RoomId },
                        cancellationToken);
                    room.IsArchived = true;
                    room.Status = ChatRoomStatus.Archived;
                    await _roomStore.SaveAsync(roomKey, room, cancellationToken: cancellationToken);
                    await _roomCache.DeleteAsync(roomKey, cancellationToken);
                    archivedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to archive idle room {RoomId}", room.RoomId);
                }
            }
            else
            {
                // Delete ephemeral or already-archived rooms
                var participants = await GetParticipantsAsync(room.RoomId, cancellationToken);
                foreach (var p in participants)
                {
                    await ClearParticipantPermissionStateAsync(p.SessionId, cancellationToken);
                }
                await DeleteAllParticipantsAsync(room.RoomId, cancellationToken);
                await _roomStore.DeleteAsync(roomKey, cancellationToken);
                await _roomCache.DeleteAsync(roomKey, cancellationToken);

                await _messageBus.TryPublishAsync("chat.room.deleted", new ChatRoomDeletedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    RoomId = room.RoomId,
                    RoomTypeCode = room.RoomTypeCode,
                    DisplayName = room.DisplayName,
                    Status = room.Status,
                    ParticipantCount = 0,
                    IsArchived = room.IsArchived,
                    CreatedAt = room.CreatedAt,
                    DeletedReason = "Idle room cleanup",
                }, cancellationToken);

                deletedCount++;
            }
        }

        _logger.LogInformation("Idle room cleanup: {Archived} archived, {Deleted} deleted", archivedCount, deletedCount);
        return new AdminCleanupResponse
        {
            CleanedRooms = archivedCount + deletedCount,
            ArchivedRooms = archivedCount,
            DeletedRooms = deletedCount,
        };
    }

    // ============================================================================
    // INTERNAL: Contract room action execution (used by ChatServiceEvents.cs)
    // ============================================================================

    /// <summary>
    /// Executes a contract room action (Lock, Archive, Delete, Continue) on a room.
    /// Used by contract event handlers in ChatServiceEvents.cs.
    /// </summary>
    internal async Task ExecuteContractRoomActionAsync(
        ChatRoomModel room, ContractRoomAction action, ChatRoomLockReason lockReason, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.chat", "ChatService.ExecuteContractRoomAction");

        var roomKey = $"{RoomKeyPrefix}{room.RoomId}";

        switch (action)
        {
            case ContractRoomAction.Lock:
                room.Status = ChatRoomStatus.Locked;
                await _roomStore.SaveAsync(roomKey, room, cancellationToken: ct);
                await _roomCache.SaveAsync(roomKey, room, cancellationToken: ct);

                await _messageBus.TryPublishAsync("chat.room.locked", new ChatRoomLockedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    RoomId = room.RoomId,
                    Reason = lockReason,
                }, ct);

                var lockParticipants = await GetParticipantsAsync(room.RoomId, ct);
                var lockSessionIds = lockParticipants.Select(p => p.SessionId.ToString()).ToList();
                await _clientEventPublisher.PublishToSessionsAsync(lockSessionIds, new ChatRoomLockedClientEvent
                {
                    RoomId = room.RoomId,
                    Reason = lockReason,
                }, ct);
                break;

            case ContractRoomAction.Archive:
                try
                {
                    var compressResponse = await _resourceClient.ExecuteCompressAsync(
                        new ExecuteCompressRequest { ResourceType = "chat-room", ResourceId = room.RoomId }, ct);

                    room.IsArchived = true;
                    room.Status = ChatRoomStatus.Archived;
                    await _roomStore.SaveAsync(roomKey, room, cancellationToken: ct);
                    await _roomCache.SaveAsync(roomKey, room, cancellationToken: ct);

                    var contractArchiveId = compressResponse.ArchiveId ?? throw new InvalidOperationException(
                        $"Resource service returned null ArchiveId for room {room.RoomId}");

                    await _messageBus.TryPublishAsync("chat.room.archived", new ChatRoomArchivedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        RoomId = room.RoomId,
                        ArchiveId = contractArchiveId,
                    }, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to archive room {RoomId} via contract action", room.RoomId);
                }
                break;

            case ContractRoomAction.Delete:
                var deleteParticipants = await GetParticipantsAsync(room.RoomId, ct);
                if (deleteParticipants.Count > 0)
                {
                    var deleteSessionIds = deleteParticipants.Select(p => p.SessionId.ToString()).ToList();
                    await _clientEventPublisher.PublishToSessionsAsync(deleteSessionIds, new ChatRoomDeletedClientEvent
                    {
                        RoomId = room.RoomId,
                        Reason = "Contract action",
                    }, ct);

                    foreach (var p in deleteParticipants)
                    {
                        await ClearParticipantPermissionStateAsync(p.SessionId, ct);
                    }
                }

                await DeleteAllParticipantsAsync(room.RoomId, ct);
                await _roomStore.DeleteAsync(roomKey, ct);
                await _roomCache.DeleteAsync(roomKey, ct);

                await _messageBus.TryPublishAsync("chat.room.deleted", new ChatRoomDeletedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    RoomId = room.RoomId,
                    RoomTypeCode = room.RoomTypeCode,
                    DisplayName = room.DisplayName,
                    Status = room.Status,
                    ParticipantCount = 0,
                    IsArchived = room.IsArchived,
                    CreatedAt = room.CreatedAt,
                    DeletedReason = "Contract action",
                }, ct);
                break;

            case ContractRoomAction.Continue:
                // No-op
                break;
        }
    }

    // ============================================================================
    // PRIVATE HELPERS
    // ============================================================================

    private static string BuildRoomTypeKey(Guid? gameServiceId, string code)
    {
        var scope = gameServiceId.HasValue ? gameServiceId.Value.ToString() : "global";
        return $"{TypeKeyPrefix}{scope}:{code}";
    }

    private async Task<ChatRoomTypeModel?> FindRoomTypeByCodeAsync(string code, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.chat", "ChatService.FindRoomTypeByCode");

        // Try to find by querying for the code (searches across all scopes)
        var conditions = new List<QueryCondition>
        {
            new() { Path = "$.Code", Operator = QueryOperator.Equals, Value = code }
        };
        var result = await _roomTypeStore.JsonQueryPagedAsync(conditions, 0, 1, cancellationToken: ct);
        return result.Items.FirstOrDefault()?.Value;
    }

    private async Task<ChatRoomModel?> GetRoomWithCacheAsync(Guid roomId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.chat", "ChatService.GetRoomWithCache");

        var roomKey = $"{RoomKeyPrefix}{roomId}";

        // Try cache first
        var cached = await _roomCache.GetAsync(roomKey, ct);
        if (cached != null)
        {
            return cached;
        }

        // Fall back to MySQL
        var model = await _roomStore.GetAsync(roomKey, ct);
        if (model != null)
        {
            await _roomCache.SaveAsync(roomKey, model, cancellationToken: ct);
        }
        return model;
    }

    private async Task<List<ChatParticipantModel>> GetParticipantsAsync(Guid roomId, CancellationToken ct)
    {
        var hash = await _participantStore.HashGetAllAsync<ChatParticipantModel>(roomId.ToString(), ct);
        return hash.Values.ToList();
    }

    private async Task<ChatParticipantModel?> GetParticipantAsync(Guid roomId, Guid sessionId, CancellationToken ct)
    {
        return await _participantStore.HashGetAsync<ChatParticipantModel>(
            roomId.ToString(), sessionId.ToString(), ct);
    }

    private async Task SaveParticipantAsync(Guid roomId, ChatParticipantModel participant, CancellationToken ct)
    {
        await _participantStore.HashSetAsync(
            roomId.ToString(), participant.SessionId.ToString(), participant, cancellationToken: ct);
    }

    private async Task RemoveParticipantAsync(Guid roomId, Guid sessionId, CancellationToken ct)
    {
        await _participantStore.HashDeleteAsync(roomId.ToString(), sessionId.ToString(), ct);
    }

    private async Task<long> GetParticipantCountAsync(Guid roomId, CancellationToken ct)
    {
        return await _participantStore.HashCountAsync(roomId.ToString(), ct);
    }

    private async Task DeleteAllParticipantsAsync(Guid roomId, CancellationToken ct)
    {
        await _participantStore.DeleteAsync(roomId.ToString(), ct);
    }

    private static string? ValidateMessageContent(SendMessageContent content, ChatRoomTypeModel roomType)
    {
        switch (roomType.MessageFormat)
        {
            case MessageFormat.Text:
                if (string.IsNullOrEmpty(content.Text))
                {
                    return "Text content required for text rooms";
                }
                if (roomType.ValidatorConfig?.MaxMessageLength.HasValue == true &&
                    content.Text.Length > roomType.ValidatorConfig.MaxMessageLength.Value)
                {
                    return $"Text exceeds maximum length of {roomType.ValidatorConfig.MaxMessageLength}";
                }
                if (!string.IsNullOrEmpty(roomType.ValidatorConfig?.AllowedPattern))
                {
                    try
                    {
                        if (!Regex.IsMatch(content.Text, roomType.ValidatorConfig.AllowedPattern,
                            RegexOptions.None, TimeSpan.FromSeconds(1)))
                        {
                            return "Text does not match allowed pattern";
                        }
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        return $"Content validation failed (pattern timeout for room type {roomType.Code})";
                    }
                }
                break;

            case MessageFormat.Sentiment:
                if (content.SentimentCategory == null)
                {
                    return "SentimentCategory required for sentiment rooms";
                }
                if (content.SentimentIntensity == null)
                {
                    return "SentimentIntensity required for sentiment rooms";
                }
                if (content.SentimentIntensity < 0.0f || content.SentimentIntensity > 1.0f)
                {
                    return "SentimentIntensity must be between 0.0 and 1.0";
                }
                break;

            case MessageFormat.Emoji:
                if (string.IsNullOrEmpty(content.EmojiCode))
                {
                    return "EmojiCode required for emoji rooms";
                }
                if (roomType.ValidatorConfig?.AllowedValues != null &&
                    !roomType.ValidatorConfig.AllowedValues.Contains(content.EmojiCode))
                {
                    return "Emoji code not in allowed values";
                }
                break;

            case MessageFormat.Custom:
                if (string.IsNullOrEmpty(content.CustomPayload))
                {
                    return "CustomPayload required for custom rooms";
                }
                if (roomType.ValidatorConfig?.MaxMessageLength.HasValue == true &&
                    content.CustomPayload.Length > roomType.ValidatorConfig.MaxMessageLength.Value)
                {
                    return $"Custom payload exceeds maximum length of {roomType.ValidatorConfig.MaxMessageLength}";
                }
                break;
        }
        return null;
    }

    private int GetEffectiveMaxParticipants(ChatRoomModel room, ChatRoomTypeModel? roomType)
    {
        return room.MaxParticipants
            ?? roomType?.DefaultMaxParticipants
            ?? _configuration.DefaultMaxParticipantsPerRoom;
    }

    private int GetEffectiveRateLimit(ChatRoomTypeModel roomType)
    {
        return roomType.RateLimitPerMinute ?? _configuration.DefaultRateLimitPerMinute;
    }

    private Guid? GetCallerSessionId()
    {
        if (string.IsNullOrEmpty(ServiceRequestContext.SessionId) ||
            !Guid.TryParse(ServiceRequestContext.SessionId, out var sessionId))
        {
            return null;
        }
        return sessionId;
    }

    private async Task SetParticipantPermissionStateAsync(Guid sessionId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.chat", "ChatService.SetParticipantPermissionState");

        try
        {
            await _permissionClient.UpdateSessionStateAsync(new SessionStateUpdate
            {
                SessionId = sessionId,
                ServiceId = "chat",
                NewState = "in_room",
            }, ct);
        }
        catch (ApiException apiEx)
        {
            _logger.LogWarning(apiEx, "Permission service error when setting state for session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set permission state for session {SessionId}", sessionId);
        }
    }

    private async Task ClearParticipantPermissionStateAsync(Guid sessionId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.chat", "ChatService.ClearParticipantPermissionState");

        try
        {
            await _permissionClient.ClearSessionStateAsync(new ClearSessionStateRequest
            {
                SessionId = sessionId,
                ServiceId = "chat",
            }, ct);
        }
        catch (ApiException apiEx)
        {
            _logger.LogWarning(apiEx, "Permission service error when clearing state for session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear permission state for session {SessionId}", sessionId);
        }
    }

    private static RoomTypeResponse MapToRoomTypeResponse(ChatRoomTypeModel model) => new()
    {
        Code = model.Code,
        DisplayName = model.DisplayName,
        Description = model.Description,
        GameServiceId = model.GameServiceId,
        MessageFormat = model.MessageFormat,
        ValidatorConfig = model.ValidatorConfig != null ? new ValidatorConfig
        {
            MaxMessageLength = model.ValidatorConfig.MaxMessageLength,
            AllowedPattern = model.ValidatorConfig.AllowedPattern,
            AllowedValues = model.ValidatorConfig.AllowedValues,
            RequiredFields = model.ValidatorConfig.RequiredFields,
            JsonSchema = model.ValidatorConfig.JsonSchema,
        } : null,
        PersistenceMode = model.PersistenceMode,
        DefaultMaxParticipants = model.DefaultMaxParticipants,
        RetentionDays = model.RetentionDays,
        DefaultContractTemplateId = model.DefaultContractTemplateId,
        AllowAnonymousSenders = model.AllowAnonymousSenders,
        RateLimitPerMinute = model.RateLimitPerMinute,
        Metadata = model.Metadata,
        Status = model.Status,
        CreatedAt = model.CreatedAt,
        UpdatedAt = model.UpdatedAt,
    };

    private static ChatRoomResponse MapToRoomResponse(ChatRoomModel model, long participantCount) => new()
    {
        RoomId = model.RoomId,
        RoomTypeCode = model.RoomTypeCode,
        SessionId = model.SessionId,
        ContractId = model.ContractId,
        DisplayName = model.DisplayName,
        Status = model.Status,
        ParticipantCount = (int)participantCount,
        MaxParticipants = model.MaxParticipants,
        IsArchived = model.IsArchived,
        CreatedAt = model.CreatedAt,
        Metadata = model.Metadata,
    };

    private static ChatMessageResponse MapToMessageResponse(ChatMessageModel model, string roomTypeCode) => new()
    {
        MessageId = model.MessageId,
        RoomId = model.RoomId,
        SenderType = model.SenderType,
        SenderId = model.SenderId,
        DisplayName = model.DisplayName,
        Timestamp = model.Timestamp,
        RoomTypeCode = roomTypeCode,
        Content = new SendMessageContent
        {
            Text = model.TextContent,
            SentimentCategory = model.SentimentCategory,
            SentimentIntensity = model.SentimentIntensity,
            EmojiCode = model.EmojiCode,
            EmojiSetId = model.EmojiSetId,
            CustomPayload = model.CustomPayload,
        },
        IsPinned = model.IsPinned,
    };

    // ============================================================================
    // TYPING INDICATOR OPERATIONS
    // ============================================================================

    /// <inheritdoc />
    public async Task<StatusCodes> TypingAsync(
        TypingRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.chat", "ChatService.Typing");

        _logger.LogDebug("Typing signal for room {RoomId} from session {SessionId}",
            body.RoomId, body.SessionId);

        var member = $"{body.RoomId:N}:{body.SessionId:N}";
        var nowMs = (double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Check if already typing (heartbeat refresh vs new start)
        var existingScore = await _participantStore.SortedSetScoreAsync(
            "typing:active", member, cancellationToken);

        // Upsert timestamp
        await _participantStore.SortedSetAddAsync(
            "typing:active", member, nowMs, cancellationToken: cancellationToken);

        // Only publish start event on NEW typing (not heartbeat refresh)
        if (existingScore == null)
        {
            _logger.LogDebug("New typing started in room {RoomId} by session {SessionId}",
                body.RoomId, body.SessionId);

            // Resolve display name from participant data
            var typingParticipant = await GetParticipantAsync(
                body.RoomId, body.SessionId, cancellationToken);

            await _entitySessionRegistry.PublishToEntitySessionsAsync(
                "chat-room", body.RoomId,
                new ChatTypingStartedClientEvent
                {
                    RoomId = body.RoomId,
                    ParticipantSessionId = body.SessionId,
                    DisplayName = typingParticipant?.DisplayName,
                }, cancellationToken);
        }

        return StatusCodes.OK;
    }

    /// <inheritdoc />
    public async Task<StatusCodes> EndTypingAsync(
        EndTypingRequest body, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.chat", "ChatService.EndTyping");

        _logger.LogDebug("End typing signal for room {RoomId} from session {SessionId}",
            body.RoomId, body.SessionId);

        await ClearTypingStateAsync(body.SessionId, body.RoomId, cancellationToken);

        return StatusCodes.OK;
    }

    // ============================================================================
    // TYPING INDICATOR HELPERS
    // ============================================================================

    /// <summary>
    /// Publish typing and end-typing session shortcuts to a participant.
    /// </summary>
    private async Task PublishTypingShortcutsAsync(Guid sessionId, Guid roomId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.chat", "ChatService.PublishTypingShortcuts");

        var sessionIdStr = sessionId.ToString();
        var roomIdStr = roomId.ToString();

        // --- Typing shortcut ---
        var typingRouteGuid = GuidGenerator.GenerateSessionShortcutGuid(
            sessionIdStr, $"chat_typing_{roomIdStr}", "chat", _serverSalt);
        var typingTargetGuid = GuidGenerator.GenerateServiceGuid(
            sessionIdStr, "chat/typing", _serverSalt);

        var typingPayload = new TypingRequest { RoomId = roomId, SessionId = sessionId };

        await _clientEventPublisher.PublishToSessionAsync(sessionIdStr, new ShortcutPublishedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = sessionId,
            Shortcut = new SessionShortcut
            {
                RouteGuid = typingRouteGuid,
                TargetGuid = typingTargetGuid,
                BoundPayload = BannouJson.Serialize(typingPayload),
                Metadata = new SessionShortcutMetadata
                {
                    Name = $"chat_typing_{roomIdStr}",
                    SourceService = "chat",
                    TargetService = "chat",
                    TargetMethod = "POST",
                    TargetEndpoint = "/chat/typing",
                    CreatedAt = DateTimeOffset.UtcNow,
                }
            },
            ReplaceExisting = false
        }, ct);

        // --- End-typing shortcut ---
        var endTypingRouteGuid = GuidGenerator.GenerateSessionShortcutGuid(
            sessionIdStr, $"chat_end_typing_{roomIdStr}", "chat", _serverSalt);
        var endTypingTargetGuid = GuidGenerator.GenerateServiceGuid(
            sessionIdStr, "chat/end-typing", _serverSalt);

        var endTypingPayload = new EndTypingRequest { RoomId = roomId, SessionId = sessionId };

        await _clientEventPublisher.PublishToSessionAsync(sessionIdStr, new ShortcutPublishedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = sessionId,
            Shortcut = new SessionShortcut
            {
                RouteGuid = endTypingRouteGuid,
                TargetGuid = endTypingTargetGuid,
                BoundPayload = BannouJson.Serialize(endTypingPayload),
                Metadata = new SessionShortcutMetadata
                {
                    Name = $"chat_end_typing_{roomIdStr}",
                    SourceService = "chat",
                    TargetService = "chat",
                    TargetMethod = "POST",
                    TargetEndpoint = "/chat/end-typing",
                    CreatedAt = DateTimeOffset.UtcNow,
                }
            },
            ReplaceExisting = false
        }, ct);

        _logger.LogDebug("Published typing shortcuts for session {SessionId} in room {RoomId}",
            sessionId, roomId);
    }

    /// <summary>
    /// Revoke typing and end-typing shortcuts for a departing participant.
    /// Uses per-GUID revocation (not RevokeByService) because a user can be in multiple rooms.
    /// </summary>
    private async Task RevokeTypingShortcutsAsync(Guid sessionId, Guid roomId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.chat", "ChatService.RevokeTypingShortcuts");

        var sessionIdStr = sessionId.ToString();
        var roomIdStr = roomId.ToString();

        var typingRouteGuid = GuidGenerator.GenerateSessionShortcutGuid(
            sessionIdStr, $"chat_typing_{roomIdStr}", "chat", _serverSalt);
        var endTypingRouteGuid = GuidGenerator.GenerateSessionShortcutGuid(
            sessionIdStr, $"chat_end_typing_{roomIdStr}", "chat", _serverSalt);

        await _clientEventPublisher.PublishToSessionAsync(sessionIdStr, new ShortcutRevokedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = sessionId,
            RouteGuid = typingRouteGuid,
            Reason = $"Left room {roomId}"
        }, ct);

        await _clientEventPublisher.PublishToSessionAsync(sessionIdStr, new ShortcutRevokedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = sessionId,
            RouteGuid = endTypingRouteGuid,
            Reason = $"Left room {roomId}"
        }, ct);

        _logger.LogDebug("Revoked typing shortcuts for session {SessionId} in room {RoomId}",
            sessionId, roomId);
    }

    /// <summary>
    /// Remove typing state from sorted set and publish stop event if was active.
    /// </summary>
    private async Task ClearTypingStateAsync(Guid sessionId, Guid roomId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.chat", "ChatService.ClearTypingState");

        var member = $"{roomId:N}:{sessionId:N}";
        var removed = await _participantStore.SortedSetRemoveAsync("typing:active", member, ct);

        if (removed)
        {
            _logger.LogDebug("Cleared typing state for session {SessionId} in room {RoomId}",
                sessionId, roomId);

            await _entitySessionRegistry.PublishToEntitySessionsAsync(
                "chat-room", roomId,
                new ChatTypingStoppedClientEvent
                {
                    RoomId = roomId,
                    ParticipantSessionId = sessionId,
                }, ct);
        }
    }
}
