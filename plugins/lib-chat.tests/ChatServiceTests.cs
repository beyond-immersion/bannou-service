using BeyondImmersion.BannouService.Chat;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.TestUtilities;

namespace BeyondImmersion.BannouService.Chat.Tests;

/// <summary>
/// Unit tests for ChatService constructor, configuration, permissions, and model validation.
/// </summary>
public class ChatServiceTests
{
    #region Constructor Validation

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    /// </summary>
    [Fact]
    public void ChatService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<ChatService>();

    #endregion

    #region Configuration Tests

    [Fact]
    public void ChatServiceConfiguration_CanBeInstantiated()
    {
        var config = new ChatServiceConfiguration();
        Assert.NotNull(config);
    }

    [Fact]
    public void ChatServiceConfiguration_HasExpectedDefaults()
    {
        var config = new ChatServiceConfiguration();

        Assert.Equal(50, config.MaxRoomTypesPerGameService);
        Assert.Equal(100, config.DefaultMaxParticipantsPerRoom);
        Assert.Equal(60, config.DefaultRateLimitPerMinute);
        Assert.Equal(60, config.IdleRoomCleanupIntervalMinutes);
        Assert.Equal(1440, config.IdleRoomTimeoutMinutes);
        Assert.Equal(60, config.EphemeralMessageTtlMinutes);
        Assert.Equal(10, config.MaxPinnedMessagesPerRoom);
        Assert.Equal(ContractRoomAction.Archive, config.DefaultContractFulfilledAction);
        Assert.Equal(ContractRoomAction.Lock, config.DefaultContractBreachAction);
        Assert.Equal(ContractRoomAction.Delete, config.DefaultContractTerminatedAction);
        Assert.Equal(ContractRoomAction.Archive, config.DefaultContractExpiredAction);
        Assert.Equal(50, config.MessageHistoryPageSize);
        Assert.Equal(15, config.LockExpirySeconds);
        Assert.Equal(30, config.IdleRoomCleanupStartupDelaySeconds);
    }

    #endregion

    #region Permission Registration Tests

    [Fact]
    public void ChatPermissionRegistration_GetEndpoints_ReturnsAllEndpoints()
    {
        var endpoints = ChatPermissionRegistration.GetEndpoints();

        Assert.NotNull(endpoints);
        Assert.NotEmpty(endpoints);
        Assert.True(endpoints.Count >= 25, $"Expected at least 25 endpoints, got {endpoints.Count}");
    }

    [Fact]
    public void ChatPermissionRegistration_GetEndpoints_AllEndpointsArePOST()
    {
        var endpoints = ChatPermissionRegistration.GetEndpoints();

        foreach (var endpoint in endpoints)
        {
            Assert.Equal(ServiceEndpointMethod.POST, endpoint.Method);
        }
    }

    [Fact]
    public void ChatPermissionRegistration_GetEndpoints_AllHavePermissions()
    {
        var endpoints = ChatPermissionRegistration.GetEndpoints();

        foreach (var endpoint in endpoints)
        {
            Assert.NotNull(endpoint.Permissions);
            Assert.NotEmpty(endpoint.Permissions);
        }
    }

    [Fact]
    public void ChatPermissionRegistration_GetEndpoints_HasRoomTypeEndpoints()
    {
        var endpoints = ChatPermissionRegistration.GetEndpoints();
        var typeEndpoints = endpoints.Where(e => e.Path.Contains("/chat/type/")).ToList();

        Assert.True(typeEndpoints.Count >= 5, $"Expected at least 5 type endpoints, got {typeEndpoints.Count}");
    }

    [Fact]
    public void ChatPermissionRegistration_GetEndpoints_HasRoomEndpoints()
    {
        var endpoints = ChatPermissionRegistration.GetEndpoints();
        var roomEndpoints = endpoints.Where(e => e.Path.Contains("/chat/room/")).ToList();

        Assert.True(roomEndpoints.Count >= 8, $"Expected at least 8 room endpoints, got {roomEndpoints.Count}");
    }

    [Fact]
    public void ChatPermissionRegistration_GetEndpoints_HasMessageEndpoints()
    {
        var endpoints = ChatPermissionRegistration.GetEndpoints();
        var msgEndpoints = endpoints.Where(e => e.Path.Contains("/chat/message/")).ToList();

        Assert.True(msgEndpoints.Count >= 6, $"Expected at least 6 message endpoints, got {msgEndpoints.Count}");
    }

    [Fact]
    public void ChatPermissionRegistration_GetEndpoints_HasAdminEndpoints()
    {
        var endpoints = ChatPermissionRegistration.GetEndpoints();
        var adminEndpoints = endpoints.Where(e => e.Path.Contains("/chat/admin/")).ToList();

        Assert.True(adminEndpoints.Count >= 3, $"Expected at least 3 admin endpoints, got {adminEndpoints.Count}");
    }

    [Fact]
    public void ChatPermissionRegistration_BuildPermissionMatrix_ReturnsValidMatrix()
    {
        var matrix = ChatPermissionRegistration.BuildPermissionMatrix();

        Assert.NotNull(matrix);
        Assert.NotEmpty(matrix);
        // Should have "default" state (for developer-role endpoints) and "in_room" state
        Assert.True(matrix.ContainsKey("default"), "Matrix should contain 'default' state key");
        Assert.True(matrix.ContainsKey("in_room"), "Matrix should contain 'in_room' state key");
    }

    #endregion

    #region Enum Validation Tests

    [Fact]
    public void MessageFormat_HasExpectedValues()
    {
        Assert.True(Enum.IsDefined(typeof(MessageFormat), MessageFormat.Text));
        Assert.True(Enum.IsDefined(typeof(MessageFormat), MessageFormat.Sentiment));
        Assert.True(Enum.IsDefined(typeof(MessageFormat), MessageFormat.Emoji));
        Assert.True(Enum.IsDefined(typeof(MessageFormat), MessageFormat.Custom));
    }

    [Fact]
    public void PersistenceMode_HasExpectedValues()
    {
        Assert.True(Enum.IsDefined(typeof(PersistenceMode), PersistenceMode.Persistent));
        Assert.True(Enum.IsDefined(typeof(PersistenceMode), PersistenceMode.Ephemeral));
    }

    [Fact]
    public void ChatRoomStatus_HasExpectedValues()
    {
        Assert.True(Enum.IsDefined(typeof(ChatRoomStatus), ChatRoomStatus.Active));
        Assert.True(Enum.IsDefined(typeof(ChatRoomStatus), ChatRoomStatus.Locked));
        Assert.True(Enum.IsDefined(typeof(ChatRoomStatus), ChatRoomStatus.Archived));
    }

    [Fact]
    public void ChatParticipantRole_HasExpectedValues()
    {
        Assert.True(Enum.IsDefined(typeof(ChatParticipantRole), ChatParticipantRole.Owner));
        Assert.True(Enum.IsDefined(typeof(ChatParticipantRole), ChatParticipantRole.Moderator));
        Assert.True(Enum.IsDefined(typeof(ChatParticipantRole), ChatParticipantRole.Member));
        Assert.True(Enum.IsDefined(typeof(ChatParticipantRole), ChatParticipantRole.ReadOnly));
    }

    [Fact]
    public void ContractRoomAction_HasExpectedValues()
    {
        Assert.True(Enum.IsDefined(typeof(ContractRoomAction), ContractRoomAction.Lock));
        Assert.True(Enum.IsDefined(typeof(ContractRoomAction), ContractRoomAction.Archive));
        Assert.True(Enum.IsDefined(typeof(ContractRoomAction), ContractRoomAction.Delete));
        Assert.True(Enum.IsDefined(typeof(ContractRoomAction), ContractRoomAction.Continue));
    }

    [Fact]
    public void RoomTypeStatus_HasExpectedValues()
    {
        Assert.True(Enum.IsDefined(typeof(RoomTypeStatus), RoomTypeStatus.Active));
        Assert.True(Enum.IsDefined(typeof(RoomTypeStatus), RoomTypeStatus.Deprecated));
    }

    [Fact]
    public void SenderType_IsStringBased()
    {
        // SenderType is an opaque string, not an enum
        // Verify the model accepts standard string values
        var request = new JoinRoomRequest
        {
            RoomId = Guid.NewGuid(),
            SenderType = "player",
            DisplayName = "Test",
        };
        Assert.Equal("player", request.SenderType);
    }

    #endregion

    #region Request Model Tests

    [Fact]
    public void RegisterRoomTypeRequest_CanBeInstantiated()
    {
        var request = new RegisterRoomTypeRequest
        {
            Code = "text",
            DisplayName = "Text Chat",
            Description = "Standard text chat",
            MessageFormat = MessageFormat.Text,
            PersistenceMode = PersistenceMode.Persistent,
        };

        Assert.Equal("text", request.Code);
        Assert.Equal(MessageFormat.Text, request.MessageFormat);
        Assert.Equal(PersistenceMode.Persistent, request.PersistenceMode);
    }

    [Fact]
    public void CreateRoomRequest_CanBeInstantiated()
    {
        var roomId = Guid.NewGuid();
        var request = new CreateRoomRequest
        {
            RoomTypeCode = "text",
            DisplayName = "My Room",
        };

        Assert.Equal("text", request.RoomTypeCode);
        Assert.Equal("My Room", request.DisplayName);
    }

    [Fact]
    public void SendMessageRequest_CanBeInstantiated()
    {
        var roomId = Guid.NewGuid();
        var request = new SendMessageRequest
        {
            RoomId = roomId,
            Content = new SendMessageContent { Text = "Hello" },
        };

        Assert.Equal(roomId, request.RoomId);
        Assert.Equal("Hello", request.Content.Text);
    }

    #endregion
}
