using BeyondImmersion.BannouService.Chat;
using BeyondImmersion.BannouService.State;
using Moq;

namespace BeyondImmersion.BannouService.Chat.Tests;

/// <summary>
/// Tests for ChatService room type operations:
/// RegisterRoomType, GetRoomType, ListRoomTypes, UpdateRoomType, DeprecateRoomType.
/// </summary>
public class ChatServiceRoomTypeTests : ChatServiceTestBase
{
    #region RegisterRoomType

    [Fact]
    public async Task RegisterRoomType_ValidRequest_SavesAndReturnsOK()
    {
        var service = CreateService();
        var request = new RegisterRoomTypeRequest
        {
            Code = "text",
            DisplayName = "Text Chat",
            Description = "Standard text",
            MessageFormat = MessageFormat.Text,
            PersistenceMode = PersistenceMode.Persistent,
        };

        // No existing type
        MockRoomTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatRoomTypeModel?)null);

        var (status, response) = await service.RegisterRoomTypeAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("text", response.Code);
        Assert.Equal(MessageFormat.Text, response.MessageFormat);
        Assert.Equal(RoomTypeStatus.Active, response.Status);

        // Verify save was called
        MockRoomTypeStore.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ChatRoomTypeModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify event published
        MockMessageBus.Verify(
            m => m.TryPublishAsync("chat-room-type.created", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RegisterRoomType_DuplicateCode_ReturnsConflict()
    {
        var service = CreateService();
        var existing = CreateTestRoomType("text");
        SetupRoomType(existing);

        var request = new RegisterRoomTypeRequest
        {
            Code = "text",
            DisplayName = "Duplicate",
            MessageFormat = MessageFormat.Text,
            PersistenceMode = PersistenceMode.Persistent,
        };

        var (status, response) = await service.RegisterRoomTypeAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RegisterRoomType_ExceedsMaxTypesPerGameService_ReturnsConflict()
    {
        Configuration.MaxRoomTypesPerGameService = 2;
        var service = CreateService();

        // No existing type with this key
        MockRoomTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatRoomTypeModel?)null);

        // Count returns 2 (at max)
        MockRoomTypeStore
            .Setup(s => s.JsonCountAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2L);

        var request = new RegisterRoomTypeRequest
        {
            Code = "third",
            DisplayName = "Third Type",
            GameServiceId = TestGameServiceId,
            MessageFormat = MessageFormat.Text,
            PersistenceMode = PersistenceMode.Persistent,
        };

        var (status, response) = await service.RegisterRoomTypeAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RegisterRoomType_WithValidatorConfig_SavesConfig()
    {
        var service = CreateService();
        MockRoomTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatRoomTypeModel?)null);

        ChatRoomTypeModel? savedModel = null;
        MockRoomTypeStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ChatRoomTypeModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ChatRoomTypeModel, StateOptions?, CancellationToken>(
                (_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");

        var request = new RegisterRoomTypeRequest
        {
            Code = "validated",
            DisplayName = "Validated Chat",
            MessageFormat = MessageFormat.Text,
            PersistenceMode = PersistenceMode.Persistent,
            ValidatorConfig = new ValidatorConfig
            {
                MaxMessageLength = 500,
                AllowedPattern = "^[a-zA-Z ]+$",
            },
        };

        var (status, _) = await service.RegisterRoomTypeAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(savedModel);
        Assert.NotNull(savedModel.ValidatorConfig);
        Assert.Equal(500, savedModel.ValidatorConfig.MaxMessageLength);
        Assert.Equal("^[a-zA-Z ]+$", savedModel.ValidatorConfig.AllowedPattern);
    }

    #endregion

    #region GetRoomType

    [Fact]
    public async Task GetRoomType_Exists_ReturnsOK()
    {
        var service = CreateService();
        var roomType = CreateTestRoomType("text");
        SetupRoomType(roomType);

        var (status, response) = await service.GetRoomTypeAsync(
            new GetRoomTypeRequest { Code = "text" }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("text", response.Code);
    }

    [Fact]
    public async Task GetRoomType_NotFound_ReturnsNotFound()
    {
        var service = CreateService();
        MockRoomTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatRoomTypeModel?)null);

        var (status, response) = await service.GetRoomTypeAsync(
            new GetRoomTypeRequest { Code = "nonexistent" }, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region ListRoomTypes

    [Fact]
    public async Task ListRoomTypes_ReturnsPagedResults()
    {
        var service = CreateService();
        var types = new List<ChatRoomTypeModel>
        {
            CreateTestRoomType("text"),
            CreateTestRoomType("emoji", MessageFormat.Emoji),
        };
        SetupRoomTypeQuery(types, 2);

        var (status, response) = await service.ListRoomTypesAsync(
            new ListRoomTypesRequest { Page = 0, PageSize = 20 }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Items.Count);
        Assert.Equal(2, response.TotalCount);
    }

    [Fact]
    public async Task ListRoomTypes_WithFilters_PassesConditions()
    {
        var service = CreateService();
        SetupRoomTypeQuery(new List<ChatRoomTypeModel>(), 0);

        await service.ListRoomTypesAsync(new ListRoomTypesRequest
        {
            GameServiceId = TestGameServiceId,
            MessageFormat = MessageFormat.Emoji,
            Status = RoomTypeStatus.Active,
            Page = 0,
            PageSize = 20,
        }, CancellationToken.None);

        MockRoomTypeStore.Verify(s => s.JsonQueryPagedAsync(
            It.Is<IReadOnlyList<QueryCondition>>(c =>
                c.Any(q => q.Path == "$.GameServiceId") &&
                c.Any(q => q.Path == "$.MessageFormat") &&
                c.Any(q => q.Path == "$.Status")),
            It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region UpdateRoomType

    [Fact]
    public async Task UpdateRoomType_ValidRequest_UpdatesAndReturnsOK()
    {
        var service = CreateService();
        var existing = CreateTestRoomType("text");
        SetupRoomType(existing);

        ChatRoomTypeModel? savedModel = null;
        MockRoomTypeStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ChatRoomTypeModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ChatRoomTypeModel, StateOptions?, CancellationToken>(
                (_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");

        var (status, response) = await service.UpdateRoomTypeAsync(new UpdateRoomTypeRequest
        {
            Code = "text",
            DisplayName = "Updated Text Chat",
            DefaultMaxParticipants = 200,
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(savedModel);
        Assert.Equal("Updated Text Chat", savedModel.DisplayName);
        Assert.Equal(200, savedModel.DefaultMaxParticipants);
    }

    [Fact]
    public async Task UpdateRoomType_NotFound_ReturnsNotFound()
    {
        var service = CreateService();
        MockRoomTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatRoomTypeModel?)null);

        var (status, _) = await service.UpdateRoomTypeAsync(
            new UpdateRoomTypeRequest { Code = "nonexistent" }, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task UpdateRoomType_LockFails_ReturnsConflict()
    {
        var service = CreateService();
        SetupLockFailure();

        var (status, _) = await service.UpdateRoomTypeAsync(
            new UpdateRoomTypeRequest { Code = "text" }, CancellationToken.None);

        Assert.Equal(StatusCodes.Conflict, status);
    }

    #endregion

    #region DeprecateRoomType

    [Fact]
    public async Task DeprecateRoomType_ActiveType_SetsDeprecatedAndReturnsOK()
    {
        var service = CreateService();
        var existing = CreateTestRoomType("text", status: RoomTypeStatus.Active);
        SetupRoomType(existing);

        ChatRoomTypeModel? savedModel = null;
        MockRoomTypeStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ChatRoomTypeModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ChatRoomTypeModel, StateOptions?, CancellationToken>(
                (_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");

        var (status, response) = await service.DeprecateRoomTypeAsync(
            new DeprecateRoomTypeRequest { Code = "text" }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(savedModel);
        Assert.Equal(RoomTypeStatus.Deprecated, savedModel.Status);
    }

    [Fact]
    public async Task DeprecateRoomType_AlreadyDeprecated_ReturnsOKWithoutSave()
    {
        var service = CreateService();
        var existing = CreateTestRoomType("text", status: RoomTypeStatus.Deprecated);
        SetupRoomType(existing);

        var (status, response) = await service.DeprecateRoomTypeAsync(
            new DeprecateRoomTypeRequest { Code = "text" }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Should NOT save again since already deprecated
        MockRoomTypeStore.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ChatRoomTypeModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeprecateRoomType_NotFound_ReturnsNotFound()
    {
        var service = CreateService();
        MockRoomTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatRoomTypeModel?)null);

        var (status, _) = await service.DeprecateRoomTypeAsync(
            new DeprecateRoomTypeRequest { Code = "nonexistent" }, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion
}
