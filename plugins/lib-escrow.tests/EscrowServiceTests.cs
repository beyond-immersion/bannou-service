using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Escrow;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using BeyondImmersion.BannouService.TestUtilities;
using System.Linq.Expressions;

namespace BeyondImmersion.BannouService.Escrow.Tests;

/// <summary>
/// Unit tests for EscrowService.
/// Tests escrow lifecycle, deposit, consent, and completion operations.
/// </summary>
public class EscrowServiceTests : ServiceTestBase<EscrowServiceConfiguration>
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IQueryableStateStore<EscrowAgreementModel>> _mockAgreementStore;
    private readonly Mock<IQueryableStateStore<AssetHandlerModel>> _mockHandlerStore;
    private readonly Mock<IStateStore<TokenHashModel>> _mockTokenStore;
    private readonly Mock<IStateStore<IdempotencyRecord>> _mockIdempotencyStore;
    private readonly Mock<IStateStore<PartyPendingCount>> _mockPartyPendingStore;
    private readonly Mock<IStateStore<StatusIndexEntry>> _mockStatusIndexStore;
    private readonly Mock<IStateStore<ValidationTrackingEntry>> _mockValidationStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<EscrowService>> _mockLogger;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    public EscrowServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockAgreementStore = new Mock<IQueryableStateStore<EscrowAgreementModel>>();
        _mockHandlerStore = new Mock<IQueryableStateStore<AssetHandlerModel>>();
        _mockTokenStore = new Mock<IStateStore<TokenHashModel>>();
        _mockIdempotencyStore = new Mock<IStateStore<IdempotencyRecord>>();
        _mockPartyPendingStore = new Mock<IStateStore<PartyPendingCount>>();
        _mockStatusIndexStore = new Mock<IStateStore<StatusIndexEntry>>();
        _mockValidationStore = new Mock<IStateStore<ValidationTrackingEntry>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<EscrowService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();

        // Setup factory to return typed stores
        _mockStateStoreFactory
            .Setup(f => f.GetQueryableStore<EscrowAgreementModel>(StateStoreDefinitions.EscrowAgreements))
            .Returns(_mockAgreementStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetQueryableStore<AssetHandlerModel>(StateStoreDefinitions.EscrowHandlerRegistry))
            .Returns(_mockHandlerStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<TokenHashModel>(StateStoreDefinitions.EscrowTokens))
            .Returns(_mockTokenStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<IdempotencyRecord>(StateStoreDefinitions.EscrowIdempotency))
            .Returns(_mockIdempotencyStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<PartyPendingCount>(StateStoreDefinitions.EscrowPartyPending))
            .Returns(_mockPartyPendingStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<StatusIndexEntry>(StateStoreDefinitions.EscrowStatusIndex))
            .Returns(_mockStatusIndexStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<ValidationTrackingEntry>(StateStoreDefinitions.EscrowActiveValidation))
            .Returns(_mockValidationStore.Object);

        // Default message bus setup
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockMessageBus
            .Setup(m => m.TryPublishErrorAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<ServiceErrorEventSeverity>(), It.IsAny<object?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Default save returns etag
        _mockAgreementStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<EscrowAgreementModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockTokenStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<TokenHashModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockStatusIndexStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<StatusIndexEntry>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockPartyPendingStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<PartyPendingCount>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockIdempotencyStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<IdempotencyRecord>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
    }

    private EscrowService CreateService()
    {
        return new EscrowService(
            _mockMessageBus.Object,
            _mockStateStoreFactory.Object,
            _mockLogger.Object,
            Configuration,
            _mockEventConsumer.Object);
    }

    #region Constructor Tests

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    /// </summary>
    [Fact]
    public void EscrowService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<EscrowService>();

    #endregion

    #region Configuration Tests

    [Fact]
    public void EscrowServiceConfiguration_HasCorrectDefaults()
    {
        var config = new EscrowServiceConfiguration();

        Assert.Equal("P7D", config.DefaultTimeout);
        Assert.Equal("P30D", config.MaxTimeout);
        Assert.Equal("PT1H", config.ExpirationGracePeriod);
        Assert.Equal("hmac_sha256", config.TokenAlgorithm);
        Assert.Equal(32, config.TokenLength);
        Assert.Null(config.TokenSecret);
        Assert.Equal("PT1M", config.ExpirationCheckInterval);
        Assert.Equal(100, config.ExpirationBatchSize);
        Assert.Equal("PT5M", config.ValidationCheckInterval);
        Assert.Equal(10, config.MaxParties);
        Assert.Equal(50, config.MaxAssetsPerDeposit);
        Assert.Equal(100, config.MaxPendingPerParty);
    }

    #endregion

    #region State Machine Tests

    [Theory]
    [InlineData(EscrowStatus.Pending_deposits, EscrowStatus.Partially_funded, true)]
    [InlineData(EscrowStatus.Pending_deposits, EscrowStatus.Funded, true)]
    [InlineData(EscrowStatus.Pending_deposits, EscrowStatus.Cancelled, true)]
    [InlineData(EscrowStatus.Pending_deposits, EscrowStatus.Expired, true)]
    [InlineData(EscrowStatus.Pending_deposits, EscrowStatus.Released, false)]
    [InlineData(EscrowStatus.Funded, EscrowStatus.Pending_consent, true)]
    [InlineData(EscrowStatus.Funded, EscrowStatus.Finalizing, true)]
    [InlineData(EscrowStatus.Funded, EscrowStatus.Disputed, true)]
    [InlineData(EscrowStatus.Funded, EscrowStatus.Released, false)]
    [InlineData(EscrowStatus.Pending_consent, EscrowStatus.Finalizing, true)]
    [InlineData(EscrowStatus.Pending_consent, EscrowStatus.Disputed, true)]
    [InlineData(EscrowStatus.Finalizing, EscrowStatus.Releasing, true)]
    [InlineData(EscrowStatus.Finalizing, EscrowStatus.Refunding, true)]
    [InlineData(EscrowStatus.Releasing, EscrowStatus.Released, true)]
    [InlineData(EscrowStatus.Refunding, EscrowStatus.Refunded, true)]
    [InlineData(EscrowStatus.Disputed, EscrowStatus.Releasing, true)]
    [InlineData(EscrowStatus.Disputed, EscrowStatus.Refunding, true)]
    [InlineData(EscrowStatus.Released, EscrowStatus.Pending_deposits, false)]
    [InlineData(EscrowStatus.Refunded, EscrowStatus.Pending_deposits, false)]
    public void IsValidTransition_ReturnsExpectedResult(EscrowStatus from, EscrowStatus to, bool expected)
    {
        var result = EscrowService.IsValidTransition(from, to);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(EscrowStatus.Released, true)]
    [InlineData(EscrowStatus.Refunded, true)]
    [InlineData(EscrowStatus.Expired, true)]
    [InlineData(EscrowStatus.Cancelled, true)]
    [InlineData(EscrowStatus.Pending_deposits, false)]
    [InlineData(EscrowStatus.Funded, false)]
    [InlineData(EscrowStatus.Finalizing, false)]
    [InlineData(EscrowStatus.Disputed, false)]
    public void IsTerminalState_ReturnsExpectedResult(EscrowStatus status, bool expected)
    {
        var result = EscrowService.IsTerminalState(status);
        Assert.Equal(expected, result);
    }

    #endregion

    #region Helper Method Tests

    [Fact]
    public void HashToken_ProducesConsistentResults()
    {
        var token = "test-token-value";
        var hash1 = EscrowService.HashToken(token);
        var hash2 = EscrowService.HashToken(token);

        Assert.Equal(hash1, hash2);
        Assert.NotEqual(token, hash1);
    }

    [Fact]
    public void HashToken_DifferentInputsProduceDifferentHashes()
    {
        var hash1 = EscrowService.HashToken("token-a");
        var hash2 = EscrowService.HashToken("token-b");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void GenerateToken_ProducesNonEmptyResult()
    {
        var service = CreateService();
        var token = service.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), TokenType.Deposit);

        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public void GenerateToken_ProducesDifferentTokensForDifferentInputs()
    {
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var partyId = Guid.NewGuid();

        var token1 = service.GenerateToken(escrowId, partyId, TokenType.Deposit);
        var token2 = service.GenerateToken(escrowId, partyId, TokenType.Release);

        // Due to random component, tokens will differ even with same inputs
        Assert.NotEqual(token1, token2);
    }

    [Fact]
    public void GetAgreementKey_FormatsCorrectly()
    {
        var id = Guid.NewGuid();
        var key = EscrowService.GetAgreementKey(id);
        Assert.Equal($"agreement:{id}", key);
    }

    [Fact]
    public void GetTokenKey_FormatsCorrectly()
    {
        var hash = "abc123hash";
        var key = EscrowService.GetTokenKey(hash);
        Assert.Equal("token:abc123hash", key);
    }

    [Fact]
    public void GenerateAssetSummary_NullAssets_ReturnsNoAssets()
    {
        var result = EscrowService.GenerateAssetSummary(null);
        Assert.Equal("No assets", result);
    }

    [Fact]
    public void GenerateAssetSummary_EmptyList_ReturnsNoAssets()
    {
        var result = EscrowService.GenerateAssetSummary(new List<EscrowAssetModel>());
        Assert.Equal("No assets", result);
    }

    [Fact]
    public void GenerateAssetSummary_CurrencyAsset_IncludesAmountAndCode()
    {
        var assets = new List<EscrowAssetModel>
        {
            new EscrowAssetModel
            {
                AssetType = AssetType.Currency,
                CurrencyAmount = 100,
                CurrencyCode = "GOLD"
            }
        };

        var result = EscrowService.GenerateAssetSummary(assets);
        Assert.Contains("100", result);
        Assert.Contains("GOLD", result);
    }

    [Fact]
    public void GenerateAssetSummary_ItemAsset_IncludesName()
    {
        var assets = new List<EscrowAssetModel>
        {
            new EscrowAssetModel
            {
                AssetType = AssetType.Item,
                ItemName = "Magic Sword"
            }
        };

        var result = EscrowService.GenerateAssetSummary(assets);
        Assert.Contains("Magic Sword", result);
    }

    [Fact]
    public void GenerateAssetSummary_MultipleAssets_JoinsWithComma()
    {
        var assets = new List<EscrowAssetModel>
        {
            new EscrowAssetModel { AssetType = AssetType.Currency, CurrencyAmount = 50, CurrencyCode = "GOLD" },
            new EscrowAssetModel { AssetType = AssetType.Item, ItemName = "Shield" }
        };

        var result = EscrowService.GenerateAssetSummary(assets);
        Assert.Contains(", ", result);
    }

    #endregion

    #region CreateEscrow Tests

    [Fact]
    public async Task CreateEscrowAsync_ValidRequest_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidEscrowRequest();

        _mockPartyPendingStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PartyPendingCount?)null);

        EscrowAgreementModel? savedModel = null;
        _mockAgreementStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<EscrowAgreementModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, EscrowAgreementModel, StateOptions?, CancellationToken>((k, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");

        // Act
        var (status, response) = await service.CreateEscrowAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.Escrow);
        Assert.Equal(EscrowStatus.Pending_deposits, response.Escrow.Status);
        Assert.NotNull(savedModel);
        Assert.Equal(EscrowType.Two_party, savedModel.EscrowType);
        Assert.Equal(2, savedModel.Parties?.Count);
    }

    [Fact]
    public async Task CreateEscrowAsync_TooFewParties_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateEscrowRequest
        {
            Parties = new List<CreateEscrowPartyInput> { CreatePartyInput(Guid.NewGuid(), EscrowPartyRole.Depositor) },
            ExpectedDeposits = new List<ExpectedDepositInput> { CreateExpectedDepositInput(Guid.NewGuid()) },
            EscrowType = EscrowType.Two_party,
            TrustMode = EscrowTrustMode.Initiator_trusted
        };

        // Act
        var (status, response) = await service.CreateEscrowAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateEscrowAsync_NoExpectedDeposits_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var partyA = Guid.NewGuid();
        var partyB = Guid.NewGuid();
        var request = new CreateEscrowRequest
        {
            Parties = new List<CreateEscrowPartyInput>
            {
                CreatePartyInput(partyA, EscrowPartyRole.Depositor),
                CreatePartyInput(partyB, EscrowPartyRole.Recipient)
            },
            ExpectedDeposits = new List<ExpectedDepositInput>(),
            EscrowType = EscrowType.Two_party,
            TrustMode = EscrowTrustMode.Initiator_trusted
        };

        // Act
        var (status, response) = await service.CreateEscrowAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateEscrowAsync_SinglePartyTrustedWithoutTrustedPartyId_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var partyA = Guid.NewGuid();
        var partyB = Guid.NewGuid();
        var request = new CreateEscrowRequest
        {
            Parties = new List<CreateEscrowPartyInput>
            {
                CreatePartyInput(partyA, EscrowPartyRole.Depositor),
                CreatePartyInput(partyB, EscrowPartyRole.Recipient)
            },
            ExpectedDeposits = new List<ExpectedDepositInput> { CreateExpectedDepositInput(partyA) },
            EscrowType = EscrowType.Two_party,
            TrustMode = EscrowTrustMode.Single_party_trusted,
            TrustedPartyId = null
        };

        // Act
        var (status, response) = await service.CreateEscrowAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateEscrowAsync_FullConsentMode_GeneratesDepositTokens()
    {
        // Arrange
        var service = CreateService();
        var depositorId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        var request = new CreateEscrowRequest
        {
            Parties = new List<CreateEscrowPartyInput>
            {
                CreatePartyInput(depositorId, EscrowPartyRole.Depositor),
                CreatePartyInput(recipientId, EscrowPartyRole.Recipient)
            },
            ExpectedDeposits = new List<ExpectedDepositInput>
            {
                CreateExpectedDepositInput(depositorId)
            },
            EscrowType = EscrowType.Two_party,
            TrustMode = EscrowTrustMode.Full_consent
        };

        _mockPartyPendingStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PartyPendingCount?)null);

        // Act
        var (status, response) = await service.CreateEscrowAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.DepositTokens);
        Assert.Single(response.DepositTokens);
        var firstToken = response.DepositTokens.First();
        Assert.Equal(depositorId, firstToken.PartyId);
        Assert.False(string.IsNullOrEmpty(firstToken.Token));
    }

    [Fact]
    public async Task CreateEscrowAsync_PublishesCreatedEvent()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidEscrowRequest();

        _mockPartyPendingStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PartyPendingCount?)null);

        // Act
        await service.CreateEscrowAsync(request);

        // Assert
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            EscrowTopics.EscrowCreated,
            It.IsAny<EscrowCreatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateEscrowAsync_IncrementsPartyPendingCount()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidEscrowRequest();

        _mockPartyPendingStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyPendingCount { PendingCount = 3 });

        PartyPendingCount? savedCount = null;
        _mockPartyPendingStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<PartyPendingCount>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, PartyPendingCount, StateOptions?, CancellationToken>((_, c, _, _) => savedCount = c)
            .ReturnsAsync("etag");

        // Act
        await service.CreateEscrowAsync(request);

        // Assert
        Assert.NotNull(savedCount);
        Assert.Equal(4, savedCount.PendingCount);
    }

    #endregion

    #region GetEscrow Tests

    [Fact]
    public async Task GetEscrowAsync_ExistingEscrow_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var model = CreateTestAgreementModel(escrowId);

        _mockAgreementStore
            .Setup(s => s.GetAsync($"agreement:{escrowId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Act
        var (status, response) = await service.GetEscrowAsync(new GetEscrowRequest { EscrowId = escrowId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(escrowId, response.Escrow.Id);
    }

    [Fact]
    public async Task GetEscrowAsync_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();

        _mockAgreementStore
            .Setup(s => s.GetAsync($"agreement:{escrowId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((EscrowAgreementModel?)null);

        // Act
        var (status, response) = await service.GetEscrowAsync(new GetEscrowRequest { EscrowId = escrowId });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region ListEscrows Tests

    [Fact]
    public async Task ListEscrowsAsync_ByPartyId_ReturnsFiltered()
    {
        // Arrange
        var service = CreateService();
        var partyId = Guid.NewGuid();
        var model = CreateTestAgreementModel(Guid.NewGuid());
        model.Parties = new List<EscrowPartyModel>
        {
            new EscrowPartyModel { PartyId = partyId, PartyType = "player" }
        };

        _mockAgreementStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<EscrowAgreementModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EscrowAgreementModel> { model });

        // Act
        var (status, response) = await service.ListEscrowsAsync(new ListEscrowsRequest { PartyId = partyId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Escrows);
    }

    [Fact]
    public async Task ListEscrowsAsync_NoFilters_ReturnsAll()
    {
        // Arrange
        var service = CreateService();
        var models = new List<EscrowAgreementModel>
        {
            CreateTestAgreementModel(Guid.NewGuid()),
            CreateTestAgreementModel(Guid.NewGuid())
        };

        _mockAgreementStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<EscrowAgreementModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(models);

        // Act
        var (status, response) = await service.ListEscrowsAsync(new ListEscrowsRequest());

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Escrows.Count);
    }

    #endregion

    #region Deposit Tests

    [Fact]
    public async Task DepositAsync_ValidDeposit_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var model = CreateTestAgreementModel(escrowId);
        model.Status = EscrowStatus.Pending_deposits;
        model.TrustMode = EscrowTrustMode.Initiator_trusted;
        model.Parties = new List<EscrowPartyModel>
        {
            new EscrowPartyModel { PartyId = partyId, PartyType = "player", DepositTokenUsed = false }
        };
        model.ExpectedDeposits = new List<ExpectedDepositModel>
        {
            new ExpectedDepositModel { PartyId = partyId, PartyType = "player", Fulfilled = false, Optional = false }
        };

        SetupAgreementGet(escrowId, model);
        SetupIdempotencyNotFound();

        // Act
        var (status, response) = await service.DepositAsync(new DepositRequest
        {
            EscrowId = escrowId,
            PartyId = partyId,
            PartyType = "player",
            IdempotencyKey = "test-key-1",
            Assets = new EscrowAssetBundleInput
            {
                Assets = new List<EscrowAssetInput>
                {
                    new EscrowAssetInput { AssetType = AssetType.Currency, CurrencyAmount = 100, CurrencyCode = "GOLD" }
                }
            }
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.FullyFunded);
    }

    [Fact]
    public async Task DepositAsync_IdempotentDuplicate_ReturnsCachedResponse()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var cachedResponse = new DepositResponse { FullyFunded = true };

        _mockIdempotencyStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdempotencyRecord
            {
                Key = "dup-key",
                EscrowId = escrowId,
                PartyId = partyId,
                Operation = "Deposit",
                Result = cachedResponse
            });

        // Act
        var (status, response) = await service.DepositAsync(new DepositRequest
        {
            EscrowId = escrowId,
            PartyId = partyId,
            PartyType = "player",
            IdempotencyKey = "dup-key"
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.FullyFunded);
    }

    [Fact]
    public async Task DepositAsync_WrongStatus_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var model = CreateTestAgreementModel(escrowId);
        model.Status = EscrowStatus.Funded;

        SetupAgreementGet(escrowId, model);
        SetupIdempotencyNotFound();

        // Act
        var (status, response) = await service.DepositAsync(new DepositRequest
        {
            EscrowId = escrowId,
            PartyId = Guid.NewGuid(),
            PartyType = "player",
            IdempotencyKey = "key-1"
        });

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DepositAsync_ExpiredEscrow_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var model = CreateTestAgreementModel(escrowId);
        model.Status = EscrowStatus.Pending_deposits;
        model.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);

        SetupAgreementGet(escrowId, model);
        SetupIdempotencyNotFound();

        // Act
        var (status, response) = await service.DepositAsync(new DepositRequest
        {
            EscrowId = escrowId,
            PartyId = Guid.NewGuid(),
            PartyType = "player",
            IdempotencyKey = "key-2"
        });

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DepositAsync_PartyNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var model = CreateTestAgreementModel(escrowId);
        model.Status = EscrowStatus.Pending_deposits;
        model.Parties = new List<EscrowPartyModel>
        {
            new EscrowPartyModel { PartyId = Guid.NewGuid(), PartyType = "player" }
        };

        SetupAgreementGet(escrowId, model);
        SetupIdempotencyNotFound();

        // Act
        var (status, response) = await service.DepositAsync(new DepositRequest
        {
            EscrowId = escrowId,
            PartyId = Guid.NewGuid(), // Different from party in model
            PartyType = "player",
            IdempotencyKey = "key-3"
        });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DepositAsync_FullConsentMode_RequiresToken()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var model = CreateTestAgreementModel(escrowId);
        model.Status = EscrowStatus.Pending_deposits;
        model.TrustMode = EscrowTrustMode.Full_consent;
        model.Parties = new List<EscrowPartyModel>
        {
            new EscrowPartyModel { PartyId = partyId, PartyType = "player", DepositTokenUsed = false }
        };

        SetupAgreementGet(escrowId, model);
        SetupIdempotencyNotFound();

        // Act - no deposit token provided
        var (status, response) = await service.DepositAsync(new DepositRequest
        {
            EscrowId = escrowId,
            PartyId = partyId,
            PartyType = "player",
            IdempotencyKey = "key-4",
            DepositToken = null
        });

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DepositAsync_PublishesDepositReceivedEvent()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var model = CreateTestAgreementModel(escrowId);
        model.Status = EscrowStatus.Pending_deposits;
        model.TrustMode = EscrowTrustMode.Initiator_trusted;
        model.Parties = new List<EscrowPartyModel>
        {
            new EscrowPartyModel { PartyId = partyId, PartyType = "player", DepositTokenUsed = false }
        };
        model.ExpectedDeposits = new List<ExpectedDepositModel>
        {
            new ExpectedDepositModel { PartyId = partyId, PartyType = "player", Fulfilled = false, Optional = false }
        };

        SetupAgreementGet(escrowId, model);
        SetupIdempotencyNotFound();

        // Act
        await service.DepositAsync(new DepositRequest
        {
            EscrowId = escrowId,
            PartyId = partyId,
            PartyType = "player",
            IdempotencyKey = "key-5",
            Assets = new EscrowAssetBundleInput
            {
                Assets = new List<EscrowAssetInput>
                {
                    new EscrowAssetInput { AssetType = AssetType.Currency, CurrencyAmount = 50 }
                }
            }
        });

        // Assert
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            EscrowTopics.EscrowDepositReceived,
            It.IsAny<EscrowDepositReceivedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DepositAsync_AllRequiredFulfilled_PublishesFundedEvent()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var model = CreateTestAgreementModel(escrowId);
        model.Status = EscrowStatus.Pending_deposits;
        model.TrustMode = EscrowTrustMode.Initiator_trusted;
        model.Parties = new List<EscrowPartyModel>
        {
            new EscrowPartyModel { PartyId = partyId, PartyType = "player", DepositTokenUsed = false }
        };
        model.ExpectedDeposits = new List<ExpectedDepositModel>
        {
            new ExpectedDepositModel { PartyId = partyId, PartyType = "player", Fulfilled = false, Optional = false }
        };

        SetupAgreementGet(escrowId, model);
        SetupIdempotencyNotFound();

        // Act
        await service.DepositAsync(new DepositRequest
        {
            EscrowId = escrowId,
            PartyId = partyId,
            PartyType = "player",
            IdempotencyKey = "key-6",
            Assets = new EscrowAssetBundleInput { Assets = new List<EscrowAssetInput>() }
        });

        // Assert
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            EscrowTopics.EscrowFunded,
            It.IsAny<EscrowFundedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ValidateDeposit Tests

    [Fact]
    public async Task ValidateDepositAsync_ValidState_ReturnsValid()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var model = CreateTestAgreementModel(escrowId);
        model.Status = EscrowStatus.Pending_deposits;
        model.Parties = new List<EscrowPartyModel>
        {
            new EscrowPartyModel { PartyId = partyId, PartyType = "player", DepositTokenUsed = false }
        };

        SetupAgreementGet(escrowId, model);

        // Act
        var (status, response) = await service.ValidateDepositAsync(new ValidateDepositRequest
        {
            EscrowId = escrowId,
            PartyId = partyId,
            PartyType = "player"
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Valid);
    }

    [Fact]
    public async Task ValidateDepositAsync_WrongStatus_ReturnsInvalid()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var model = CreateTestAgreementModel(escrowId);
        model.Status = EscrowStatus.Released;
        model.Parties = new List<EscrowPartyModel>
        {
            new EscrowPartyModel { PartyId = partyId, PartyType = "player" }
        };

        SetupAgreementGet(escrowId, model);

        // Act
        var (status, response) = await service.ValidateDepositAsync(new ValidateDepositRequest
        {
            EscrowId = escrowId,
            PartyId = partyId,
            PartyType = "player"
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.Valid);
        Assert.NotEmpty(response.Errors);
    }

    #endregion

    #region Consent Tests

    [Fact]
    public async Task RecordConsentAsync_ValidReleaseConsent_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var model = CreateTestAgreementModel(escrowId);
        model.Status = EscrowStatus.Funded;
        model.TrustMode = EscrowTrustMode.Initiator_trusted;
        model.RequiredConsentsForRelease = 1;
        model.Parties = new List<EscrowPartyModel>
        {
            new EscrowPartyModel { PartyId = partyId, PartyType = "player", ConsentRequired = true }
        };
        model.Consents = new List<EscrowConsentModel>();

        SetupAgreementGet(escrowId, model);

        // Act
        var (status, response) = await service.RecordConsentAsync(new ConsentRequest
        {
            EscrowId = escrowId,
            PartyId = partyId,
            PartyType = "player",
            ConsentType = EscrowConsentType.Release
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.ConsentRecorded);
    }

    [Fact]
    public async Task RecordConsentAsync_WrongStatus_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var model = CreateTestAgreementModel(escrowId);
        model.Status = EscrowStatus.Pending_deposits;

        SetupAgreementGet(escrowId, model);

        // Act
        var (status, response) = await service.RecordConsentAsync(new ConsentRequest
        {
            EscrowId = escrowId,
            PartyId = Guid.NewGuid(),
            PartyType = "player",
            ConsentType = EscrowConsentType.Release
        });

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RecordConsentAsync_ExpiredEscrow_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var model = CreateTestAgreementModel(escrowId);
        model.Status = EscrowStatus.Funded;
        model.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);

        SetupAgreementGet(escrowId, model);

        // Act
        var (status, response) = await service.RecordConsentAsync(new ConsentRequest
        {
            EscrowId = escrowId,
            PartyId = Guid.NewGuid(),
            PartyType = "player",
            ConsentType = EscrowConsentType.Release
        });

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RecordConsentAsync_DuplicateConsent_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var model = CreateTestAgreementModel(escrowId);
        model.Status = EscrowStatus.Funded;
        model.TrustMode = EscrowTrustMode.Initiator_trusted;
        model.Parties = new List<EscrowPartyModel>
        {
            new EscrowPartyModel { PartyId = partyId, PartyType = "player", ConsentRequired = true }
        };
        model.Consents = new List<EscrowConsentModel>
        {
            new EscrowConsentModel { PartyId = partyId, PartyType = "player", ConsentType = EscrowConsentType.Release }
        };

        SetupAgreementGet(escrowId, model);

        // Act
        var (status, response) = await service.RecordConsentAsync(new ConsentRequest
        {
            EscrowId = escrowId,
            PartyId = partyId,
            PartyType = "player",
            ConsentType = EscrowConsentType.Release
        });

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RecordConsentAsync_AllConsentsReceived_TransitionsToFinalizing()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var model = CreateTestAgreementModel(escrowId);
        model.Status = EscrowStatus.Funded;
        model.TrustMode = EscrowTrustMode.Initiator_trusted;
        model.RequiredConsentsForRelease = 1;
        model.BoundContractId = null;
        model.Parties = new List<EscrowPartyModel>
        {
            new EscrowPartyModel { PartyId = partyId, PartyType = "player", ConsentRequired = true }
        };
        model.Consents = new List<EscrowConsentModel>();

        SetupAgreementGet(escrowId, model);

        // Act
        var (status, response) = await service.RecordConsentAsync(new ConsentRequest
        {
            EscrowId = escrowId,
            PartyId = partyId,
            PartyType = "player",
            ConsentType = EscrowConsentType.Release
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Triggered);
        Assert.Equal(EscrowStatus.Finalizing, response.NewStatus);
    }

    [Fact]
    public async Task RecordConsentAsync_DisputeConsent_TransitionsToDisputed()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var model = CreateTestAgreementModel(escrowId);
        model.Status = EscrowStatus.Funded;
        model.TrustMode = EscrowTrustMode.Initiator_trusted;
        model.Parties = new List<EscrowPartyModel>
        {
            new EscrowPartyModel { PartyId = partyId, PartyType = "player", ConsentRequired = true }
        };
        model.Consents = new List<EscrowConsentModel>();

        SetupAgreementGet(escrowId, model);

        // Act
        var (status, response) = await service.RecordConsentAsync(new ConsentRequest
        {
            EscrowId = escrowId,
            PartyId = partyId,
            PartyType = "player",
            ConsentType = EscrowConsentType.Dispute
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Triggered);
        Assert.Equal(EscrowStatus.Disputed, response.NewStatus);
    }

    [Fact]
    public async Task RecordConsentAsync_FullConsentMode_RequiresReleaseToken()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var model = CreateTestAgreementModel(escrowId);
        model.Status = EscrowStatus.Funded;
        model.TrustMode = EscrowTrustMode.Full_consent;
        model.Parties = new List<EscrowPartyModel>
        {
            new EscrowPartyModel { PartyId = partyId, PartyType = "player", ConsentRequired = true, ReleaseTokenUsed = false }
        };
        model.Consents = new List<EscrowConsentModel>();

        SetupAgreementGet(escrowId, model);

        // Act - no release token provided
        var (status, response) = await service.RecordConsentAsync(new ConsentRequest
        {
            EscrowId = escrowId,
            PartyId = partyId,
            PartyType = "player",
            ConsentType = EscrowConsentType.Release,
            ReleaseToken = null
        });

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    #endregion

    #region GetConsentStatus Tests

    [Fact]
    public async Task GetConsentStatusAsync_ExistingEscrow_ReturnsStatus()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var model = CreateTestAgreementModel(escrowId);
        model.RequiredConsentsForRelease = 2;
        model.Parties = new List<EscrowPartyModel>
        {
            new EscrowPartyModel { PartyId = partyId, PartyType = "player", ConsentRequired = true }
        };
        model.Consents = new List<EscrowConsentModel>
        {
            new EscrowConsentModel { PartyId = partyId, PartyType = "player", ConsentType = EscrowConsentType.Release }
        };

        SetupAgreementGet(escrowId, model);

        // Act
        var (status, response) = await service.GetConsentStatusAsync(new GetConsentStatusRequest { EscrowId = escrowId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.ConsentsReceived);
        Assert.Equal(2, response.ConsentsRequired);
        Assert.False(response.CanRelease);
    }

    #endregion

    #region GetMyToken Tests

    [Fact]
    public async Task GetMyTokenAsync_ExistingDepositToken_ReturnsToken()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var model = CreateTestAgreementModel(escrowId);
        model.Parties = new List<EscrowPartyModel>
        {
            new EscrowPartyModel
            {
                PartyId = partyId,
                PartyType = "player",
                DepositToken = "my-deposit-token",
                DepositTokenUsed = false
            }
        };

        SetupAgreementGet(escrowId, model);

        // Act
        var (status, response) = await service.GetMyTokenAsync(new GetMyTokenRequest
        {
            EscrowId = escrowId,
            OwnerId = partyId,
            OwnerType = "player",
            TokenType = TokenType.Deposit
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("my-deposit-token", response.Token);
        Assert.False(response.TokenUsed);
    }

    [Fact]
    public async Task GetMyTokenAsync_PartyNotInEscrow_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var model = CreateTestAgreementModel(escrowId);
        model.Parties = new List<EscrowPartyModel>
        {
            new EscrowPartyModel { PartyId = Guid.NewGuid(), PartyType = "player" }
        };

        SetupAgreementGet(escrowId, model);

        // Act
        var (status, response) = await service.GetMyTokenAsync(new GetMyTokenRequest
        {
            EscrowId = escrowId,
            OwnerId = Guid.NewGuid(), // Different party
            OwnerType = "player",
            TokenType = TokenType.Deposit
        });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region Completion Tests

    [Fact]
    public async Task DisputeAsync_FundedEscrow_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var model = CreateTestAgreementModel(escrowId);
        model.Status = EscrowStatus.Funded;
        model.Parties = new List<EscrowPartyModel>
        {
            new EscrowPartyModel { PartyId = partyId, PartyType = "player" }
        };

        SetupAgreementGet(escrowId, model);

        // Act
        var (status, response) = await service.DisputeAsync(new DisputeRequest
        {
            EscrowId = escrowId,
            PartyId = partyId,
            PartyType = "player",
            Reason = "Goods not as described"
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(EscrowStatus.Disputed, response.Escrow.Status);
    }

    [Fact]
    public async Task DisputeAsync_TerminalState_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var model = CreateTestAgreementModel(escrowId);
        model.Status = EscrowStatus.Released;

        SetupAgreementGet(escrowId, model);

        // Act
        var (status, response) = await service.DisputeAsync(new DisputeRequest
        {
            EscrowId = escrowId,
            PartyId = Guid.NewGuid(),
            PartyType = "player",
            Reason = "Too late"
        });

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DisputeAsync_NonExistentEscrow_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();

        _mockAgreementStore
            .Setup(s => s.GetAsync($"agreement:{escrowId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((EscrowAgreementModel?)null);

        // Act
        var (status, response) = await service.DisputeAsync(new DisputeRequest
        {
            EscrowId = escrowId,
            PartyId = Guid.NewGuid(),
            PartyType = "player",
            Reason = "Not found"
        });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DisputeAsync_PublishesDisputedEvent()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var model = CreateTestAgreementModel(escrowId);
        model.Status = EscrowStatus.Funded;
        model.Parties = new List<EscrowPartyModel>
        {
            new EscrowPartyModel { PartyId = partyId, PartyType = "player" }
        };

        SetupAgreementGet(escrowId, model);

        // Act
        await service.DisputeAsync(new DisputeRequest
        {
            EscrowId = escrowId,
            PartyId = partyId,
            PartyType = "player",
            Reason = "Test dispute"
        });

        // Assert
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            EscrowTopics.EscrowDisputed,
            It.IsAny<EscrowDisputedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelAsync_PendingDeposits_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var model = CreateTestAgreementModel(escrowId);
        model.Status = EscrowStatus.Pending_deposits;
        model.Deposits = new List<EscrowDepositModel>();

        SetupAgreementGet(escrowId, model);

        // Act
        var (status, response) = await service.CancelAsync(new CancelRequest
        {
            EscrowId = escrowId,
            Reason = "No longer needed"
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(EscrowStatus.Cancelled, response.Escrow.Status);
    }

    [Fact]
    public async Task CancelAsync_FundedEscrow_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var escrowId = Guid.NewGuid();
        var model = CreateTestAgreementModel(escrowId);
        model.Status = EscrowStatus.Funded;

        SetupAgreementGet(escrowId, model);

        // Act
        var (status, response) = await service.CancelAsync(new CancelRequest
        {
            EscrowId = escrowId,
            Reason = "Too late"
        });

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    #endregion

    #region Test Helpers

    private static CreateEscrowRequest CreateValidEscrowRequest()
    {
        var depositorId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();

        return new CreateEscrowRequest
        {
            Parties = new List<CreateEscrowPartyInput>
            {
                CreatePartyInput(depositorId, EscrowPartyRole.Depositor),
                CreatePartyInput(recipientId, EscrowPartyRole.Recipient)
            },
            ExpectedDeposits = new List<ExpectedDepositInput>
            {
                CreateExpectedDepositInput(depositorId)
            },
            EscrowType = EscrowType.Two_party,
            TrustMode = EscrowTrustMode.Initiator_trusted
        };
    }

    private static CreateEscrowPartyInput CreatePartyInput(Guid partyId, EscrowPartyRole role)
    {
        return new CreateEscrowPartyInput
        {
            PartyId = partyId,
            PartyType = "player",
            DisplayName = $"Party {partyId.ToString()[..8]}",
            Role = role
        };
    }

    private static ExpectedDepositInput CreateExpectedDepositInput(Guid partyId)
    {
        return new ExpectedDepositInput
        {
            PartyId = partyId,
            PartyType = "player",
            ExpectedAssets = new List<EscrowAssetInput>
            {
                new EscrowAssetInput
                {
                    AssetType = AssetType.Currency,
                    CurrencyCode = "GOLD",
                    CurrencyAmount = 100
                }
            }
        };
    }

    private static EscrowAgreementModel CreateTestAgreementModel(Guid escrowId)
    {
        return new EscrowAgreementModel
        {
            EscrowId = escrowId,
            EscrowType = EscrowType.Two_party,
            TrustMode = EscrowTrustMode.Initiator_trusted,
            Status = EscrowStatus.Pending_deposits,
            Parties = new List<EscrowPartyModel>(),
            ExpectedDeposits = new List<ExpectedDepositModel>(),
            Deposits = new List<EscrowDepositModel>(),
            Consents = new List<EscrowConsentModel>(),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            RequiredConsentsForRelease = -1
        };
    }

    private void SetupAgreementGet(Guid escrowId, EscrowAgreementModel model)
    {
        _mockAgreementStore
            .Setup(s => s.GetAsync($"agreement:{escrowId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);
    }

    private void SetupIdempotencyNotFound()
    {
        _mockIdempotencyStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyRecord?)null);
    }

    #endregion
}
