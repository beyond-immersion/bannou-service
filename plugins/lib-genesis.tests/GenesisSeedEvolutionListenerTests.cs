using System.Linq.Expressions;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Actor;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Genesis;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Species;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Genesis.Tests;

/// <summary>
/// Unit tests for <see cref="GenesisSeedEvolutionListener"/>. Focuses on phase transition logic,
/// owner-type filtering, actor template resolution, and publishing transition-failed events on errors.
/// </summary>
public class GenesisSeedEvolutionListenerTests : ServiceTestBase<GenesisServiceConfiguration>
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory = new();
    private readonly Mock<IStateStore<GenesisEntityModel>> _mockEntityStore = new();
    private readonly Mock<IQueryableStateStore<GenesisEntityModel>> _mockEntityQueryStore = new();
    private readonly Mock<IStateStore<GenesisTemplateModel>> _mockTemplateStore = new();
    private readonly Mock<IStateStore<CachedGenesisEntity>> _mockEntityCacheStore = new();
    private readonly Mock<IStateStore<CachedCapabilityManifest>> _mockCapsCacheStore = new();
    private readonly Mock<IDistributedLockProvider> _mockLockProvider = new();
    private readonly Mock<IMessageBus> _mockMessageBus = new();
    private readonly Mock<ILogger<GenesisSeedEvolutionListener>> _mockLogger = new();
    private readonly Mock<IActorClient> _mockActorClient = new();
    private readonly Mock<ICharacterClient> _mockCharacterClient = new();
    private readonly Mock<IRelationshipClient> _mockRelationshipClient = new();
    private readonly Mock<IRealmClient> _mockRealmClient = new();
    private readonly Mock<ISpeciesClient> _mockSpeciesClient = new();
    private readonly GenesisGrowthState _state = new();

    public GenesisSeedEvolutionListenerTests()
    {
        _mockStateStoreFactory
            .Setup(f => f.GetStore<GenesisEntityModel>(StateStoreDefinitions.GenesisEntities))
            .Returns(_mockEntityStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetQueryableStore<GenesisEntityModel>(StateStoreDefinitions.GenesisEntities))
            .Returns(_mockEntityQueryStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<GenesisTemplateModel>(StateStoreDefinitions.GenesisTemplates))
            .Returns(_mockTemplateStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<CachedGenesisEntity>(StateStoreDefinitions.GenesisEntityCache))
            .Returns(_mockEntityCacheStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<CachedCapabilityManifest>(StateStoreDefinitions.GenesisEntityCache))
            .Returns(_mockCapsCacheStore.Object);

        _mockEntityStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GenesisEntityModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var successLock = new Mock<ILockResponse>();
        successLock.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successLock.Object);
    }

    private GenesisSeedEvolutionListener CreateListener() =>
        new(
            _mockStateStoreFactory.Object,
            _mockLockProvider.Object,
            _mockMessageBus.Object,
            new NullTelemetryProvider(),
            _mockLogger.Object,
            Configuration,
            _state,
            _mockActorClient.Object,
            _mockCharacterClient.Object,
            _mockRelationshipClient.Object,
            _mockRealmClient.Object,
            _mockSpeciesClient.Object);

    private static GenesisEntityModel CreateEntity(
        Guid? entityId = null,
        Guid? seedId = null,
        CognitiveStage stage = CognitiveStage.Dormant,
        string currentPhase = "Dormant") =>
        new()
        {
            EntityId = entityId ?? Guid.NewGuid(),
            TemplateCode = "treasure_chest",
            GameServiceId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            SeedId = seedId ?? Guid.NewGuid(),
            WalletIds = new Dictionary<string, Guid>(),
            InventoryIds = new Dictionary<string, Guid>(),
            CurrentPhase = currentPhase,
            CognitiveStage = stage,
            PhysicalFormType = PhysicalFormType.Item,
            Status = GenesisEntityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private static GenesisTemplateModel CreateTemplate(IEnumerable<GenesisSeedPhase>? phases = null) =>
        new()
        {
            TemplateCode = "treasure_chest",
            GameServiceId = Guid.NewGuid(),
            DisplayName = "Treasure Chest",
            Description = "A growing chest",
            Seed = new GenesisSeedConfig
            {
                SeedTypeCode = "treasure_chest",
                Domains = new List<GenesisSeedDomain> { new() { DomainCode = "awareness", DisplayName = "Awareness" } },
                Phases = phases?.ToList() ?? new List<GenesisSeedPhase>
                {
                    new() { PhaseName = "Dormant", Threshold = 0, CognitiveStage = CognitiveStage.Dormant },
                    new() { PhaseName = "Stirring", Threshold = 100, CognitiveStage = CognitiveStage.EventBrain, BehaviorRef = "treasure-stirring" },
                    new() { PhaseName = "Awakened", Threshold = 500, CognitiveStage = CognitiveStage.CharacterBrain, BehaviorRef = "treasure-awakened" },
                }
            },
            Economy = new GenesisEconomyConfig
            {
                Wallets = new List<GenesisWalletConfig>(),
                GrowthMappings = new List<GenesisGrowthMapping>()
            },
            Storage = new GenesisStorageConfig { Inventories = new List<GenesisInventoryConfig>() },
            Awakening = new GenesisAwakeningConfig
            {
                SystemRealmCode = "SENTIENT_CONTAINERS",
                CharacterSpeciesCode = "treasure_spirit"
            },
            PhysicalFormType = PhysicalFormType.Item,
            Bond = new GenesisBondConfig { Enabled = false, Cardinality = BondCardinality.None },
        };

    [Fact]
    public void InterestedSeedTypes_IsWildcard()
    {
        var listener = CreateListener();

        // Wildcard set — filtering is done by owner type in-method instead
        Assert.Empty(listener.InterestedSeedTypes);
    }

    [Fact]
    public async Task OnPhaseChangedAsync_NonOtherOwner_Ignored()
    {
        var listener = CreateListener();
        var notification = new SeedPhaseNotification(
            SeedId: Guid.NewGuid(),
            SeedTypeCode: "treasure_chest",
            OwnerId: Guid.NewGuid(),
            OwnerType: EntityType.Character,
            PreviousPhase: "Dormant",
            NewPhase: "Stirring",
            TotalGrowth: 100f,
            Progressed: true);

        await listener.OnPhaseChangedAsync(notification, CancellationToken.None);

        // Non-genesis seed (owner type != Other) is filtered early; no queries, no publishes
        _mockEntityQueryStore.Verify(
            q => q.QueryAsync(It.IsAny<Expression<Func<GenesisEntityModel, bool>>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task OnPhaseChangedAsync_NoMatchingEntity_Ignored()
    {
        var listener = CreateListener();
        _mockEntityQueryStore
            .Setup(q => q.QueryAsync(It.IsAny<Expression<Func<GenesisEntityModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GenesisEntityModel>());

        var notification = new SeedPhaseNotification(
            SeedId: Guid.NewGuid(),
            SeedTypeCode: "treasure_chest",
            OwnerId: Guid.NewGuid(),
            OwnerType: EntityType.Other,
            PreviousPhase: "Dormant",
            NewPhase: "Stirring",
            TotalGrowth: 100f,
            Progressed: true);

        await listener.OnPhaseChangedAsync(notification, CancellationToken.None);

        _mockActorClient.Verify(
            a => a.SpawnActorAsync(It.IsAny<SpawnActorRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task OnPhaseChangedAsync_DormantToEventBrain_SpawnsActorAndPublishes()
    {
        var entityId = Guid.NewGuid();
        var seedId = Guid.NewGuid();
        var actorTemplateId = Guid.NewGuid();
        var entity = CreateEntity(entityId: entityId, seedId: seedId);
        var template = CreateTemplate();

        _mockEntityQueryStore
            .Setup(q => q.QueryAsync(It.IsAny<Expression<Func<GenesisEntityModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GenesisEntityModel> { entity });
        _mockEntityStore
            .Setup(s => s.GetAsync(GenesisService.BuildEntityKey(entityId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _mockTemplateStore
            .Setup(s => s.GetAsync(GenesisService.BuildTemplateKey("treasure_chest"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _state.ActorTemplateMap[GenesisSeedEvolutionListener.BuildActorTemplateKey("treasure_chest", "Stirring")] = actorTemplateId;

        _mockActorClient
            .Setup(a => a.SpawnActorAsync(It.IsAny<SpawnActorRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActorInstanceResponse { ActorId = entityId.ToString(), TemplateId = actorTemplateId });

        GenesisEntityModel? savedEntity = null;
        _mockEntityStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GenesisEntityModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, GenesisEntityModel, StateOptions?, CancellationToken>((_, e, _, _) => savedEntity = e)
            .ReturnsAsync("etag");

        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, e, _) => { capturedTopic = t; capturedEvent = e; })
            .ReturnsAsync(true);

        var notification = new SeedPhaseNotification(
            SeedId: seedId,
            SeedTypeCode: "treasure_chest",
            OwnerId: entityId,
            OwnerType: EntityType.Other,
            PreviousPhase: "Dormant",
            NewPhase: "Stirring",
            TotalGrowth: 100f,
            Progressed: true);

        var listener = CreateListener();
        await listener.OnPhaseChangedAsync(notification, CancellationToken.None);

        _mockActorClient.Verify(
            a => a.SpawnActorAsync(
                It.Is<SpawnActorRequest>(r => r.TemplateId == actorTemplateId && r.ActorId == entityId.ToString()),
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.NotNull(savedEntity);
        Assert.Equal(CognitiveStage.EventBrain, savedEntity.CognitiveStage);
        Assert.Equal(entityId, savedEntity.ActorId);
        Assert.Equal("Stirring", savedEntity.CurrentPhase);
        Assert.Equal(GenesisPublishedTopics.GenesisEntityPhaseChanged, capturedTopic);
        Assert.IsType<GenesisEntityPhaseChangedEvent>(capturedEvent);
    }

    [Fact]
    public async Task OnPhaseChangedAsync_MissingActorTemplateMapEntry_PublishesTransitionFailed()
    {
        var entityId = Guid.NewGuid();
        var seedId = Guid.NewGuid();
        var entity = CreateEntity(entityId: entityId, seedId: seedId);
        var template = CreateTemplate();

        _mockEntityQueryStore
            .Setup(q => q.QueryAsync(It.IsAny<Expression<Func<GenesisEntityModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GenesisEntityModel> { entity });
        _mockEntityStore
            .Setup(s => s.GetAsync(GenesisService.BuildEntityKey(entityId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _mockTemplateStore
            .Setup(s => s.GetAsync(GenesisService.BuildTemplateKey("treasure_chest"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Deliberately NOT populating the actor template map

        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, e, _) => { capturedTopic = t; capturedEvent = e; })
            .ReturnsAsync(true);

        var notification = new SeedPhaseNotification(
            SeedId: seedId,
            SeedTypeCode: "treasure_chest",
            OwnerId: entityId,
            OwnerType: EntityType.Other,
            PreviousPhase: "Dormant",
            NewPhase: "Stirring",
            TotalGrowth: 100f,
            Progressed: true);

        var listener = CreateListener();
        await listener.OnPhaseChangedAsync(notification, CancellationToken.None);

        // Actor was NOT spawned
        _mockActorClient.Verify(
            a => a.SpawnActorAsync(It.IsAny<SpawnActorRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        // Transition-failed event WAS published
        Assert.Equal(GenesisPublishedTopics.GenesisEntityTransitionFailed, capturedTopic);
        var failedEvent = Assert.IsType<GenesisEntityTransitionFailedEvent>(capturedEvent);
        Assert.Equal(entityId, failedEvent.EntityId);
        Assert.Equal("Stirring", failedEvent.TargetPhase);
        Assert.Equal(CognitiveStage.EventBrain, failedEvent.TargetCognitiveStage);
        Assert.Contains("Actor template", failedEvent.FailureReason);
    }

    [Fact]
    public async Task OnPhaseChangedAsync_ActorSpawnFails_PublishesTransitionFailed()
    {
        var entityId = Guid.NewGuid();
        var seedId = Guid.NewGuid();
        var actorTemplateId = Guid.NewGuid();
        var entity = CreateEntity(entityId: entityId, seedId: seedId);
        var template = CreateTemplate();

        _mockEntityQueryStore
            .Setup(q => q.QueryAsync(It.IsAny<Expression<Func<GenesisEntityModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GenesisEntityModel> { entity });
        _mockEntityStore
            .Setup(s => s.GetAsync(GenesisService.BuildEntityKey(entityId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _mockTemplateStore
            .Setup(s => s.GetAsync(GenesisService.BuildTemplateKey("treasure_chest"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _state.ActorTemplateMap[GenesisSeedEvolutionListener.BuildActorTemplateKey("treasure_chest", "Stirring")] = actorTemplateId;

        _mockActorClient
            .Setup(a => a.SpawnActorAsync(It.IsAny<SpawnActorRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Actor service failure", 500, null, null, null));

        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, e, _) => { capturedTopic = t; capturedEvent = e; })
            .ReturnsAsync(true);

        var notification = new SeedPhaseNotification(
            SeedId: seedId,
            SeedTypeCode: "treasure_chest",
            OwnerId: entityId,
            OwnerType: EntityType.Other,
            PreviousPhase: "Dormant",
            NewPhase: "Stirring",
            TotalGrowth: 100f,
            Progressed: true);

        var listener = CreateListener();
        await listener.OnPhaseChangedAsync(notification, CancellationToken.None);

        // Entity should NOT have been saved with partial state
        _mockEntityStore.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GenesisEntityModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Equal(GenesisPublishedTopics.GenesisEntityTransitionFailed, capturedTopic);
        Assert.IsType<GenesisEntityTransitionFailedEvent>(capturedEvent);
    }

    [Fact]
    public async Task OnPhaseChangedAsync_EventBrainToCharacterBrain_CreatesCharacterBindsActor()
    {
        var entityId = Guid.NewGuid();
        var seedId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var speciesId = Guid.NewGuid();

        var entity = CreateEntity(entityId: entityId, seedId: seedId, stage: CognitiveStage.EventBrain, currentPhase: "Stirring");
        entity.ActorId = entityId;
        var template = CreateTemplate();

        _mockEntityQueryStore
            .Setup(q => q.QueryAsync(It.IsAny<Expression<Func<GenesisEntityModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GenesisEntityModel> { entity });
        _mockEntityStore
            .Setup(s => s.GetAsync(GenesisService.BuildEntityKey(entityId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _mockTemplateStore
            .Setup(s => s.GetAsync(GenesisService.BuildTemplateKey("treasure_chest"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockRealmClient
            .Setup(r => r.GetRealmByCodeAsync(It.IsAny<GetRealmByCodeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmResponse { RealmId = realmId, IsSystemType = true });
        _mockSpeciesClient
            .Setup(s => s.GetSpeciesByCodeAsync(It.IsAny<GetSpeciesByCodeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpeciesResponse { SpeciesId = speciesId });
        _mockCharacterClient
            .Setup(c => c.CreateCharacterAsync(It.IsAny<CreateCharacterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterResponse
            {
                CharacterId = characterId,
                Name = "Treasure Spirit",
                RealmId = realmId,
                SpeciesId = speciesId,
                BirthDate = DateTimeOffset.UtcNow,
                Status = CharacterStatus.Alive,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        _mockActorClient
            .Setup(a => a.BindActorCharacterAsync(It.IsAny<BindActorCharacterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActorInstanceResponse { ActorId = entityId.ToString(), CharacterId = characterId });

        GenesisEntityModel? savedEntity = null;
        _mockEntityStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GenesisEntityModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, GenesisEntityModel, StateOptions?, CancellationToken>((_, e, _, _) => savedEntity = e)
            .ReturnsAsync("etag");

        var notification = new SeedPhaseNotification(
            SeedId: seedId,
            SeedTypeCode: "treasure_chest",
            OwnerId: entityId,
            OwnerType: EntityType.Other,
            PreviousPhase: "Stirring",
            NewPhase: "Awakened",
            TotalGrowth: 500f,
            Progressed: true);

        var listener = CreateListener();
        await listener.OnPhaseChangedAsync(notification, CancellationToken.None);

        _mockCharacterClient.Verify(
            c => c.CreateCharacterAsync(
                It.Is<CreateCharacterRequest>(r => r.RealmId == realmId && r.SpeciesId == speciesId),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockActorClient.Verify(
            a => a.BindActorCharacterAsync(
                It.Is<BindActorCharacterRequest>(r => r.ActorId == entityId.ToString() && r.CharacterId == characterId),
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.NotNull(savedEntity);
        Assert.Equal(CognitiveStage.CharacterBrain, savedEntity.CognitiveStage);
        Assert.Equal(characterId, savedEntity.CharacterId);
        Assert.Equal("Awakened", savedEntity.CurrentPhase);
    }

    [Fact]
    public async Task OnPhaseChangedAsync_AwakeningWithoutActorId_PublishesTransitionFailed()
    {
        var entityId = Guid.NewGuid();
        var seedId = Guid.NewGuid();
        var entity = CreateEntity(entityId: entityId, seedId: seedId, stage: CognitiveStage.EventBrain, currentPhase: "Stirring");
        entity.ActorId = null; // Defect: EventBrain with no actor
        var template = CreateTemplate();

        _mockEntityQueryStore
            .Setup(q => q.QueryAsync(It.IsAny<Expression<Func<GenesisEntityModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GenesisEntityModel> { entity });
        _mockEntityStore
            .Setup(s => s.GetAsync(GenesisService.BuildEntityKey(entityId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _mockTemplateStore
            .Setup(s => s.GetAsync(GenesisService.BuildTemplateKey("treasure_chest"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, e, _) => { capturedTopic = t; capturedEvent = e; })
            .ReturnsAsync(true);

        var notification = new SeedPhaseNotification(
            SeedId: seedId,
            SeedTypeCode: "treasure_chest",
            OwnerId: entityId,
            OwnerType: EntityType.Other,
            PreviousPhase: "Stirring",
            NewPhase: "Awakened",
            TotalGrowth: 500f,
            Progressed: true);

        var listener = CreateListener();
        await listener.OnPhaseChangedAsync(notification, CancellationToken.None);

        _mockCharacterClient.Verify(
            c => c.CreateCharacterAsync(It.IsAny<CreateCharacterRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Equal(GenesisPublishedTopics.GenesisEntityTransitionFailed, capturedTopic);
        var failedEvent = Assert.IsType<GenesisEntityTransitionFailedEvent>(capturedEvent);
        Assert.Contains("ActorId", failedEvent.FailureReason);
    }

    [Fact]
    public async Task OnGrowthRecordedAsync_NonOtherOwner_DoesNotQuery()
    {
        var listener = CreateListener();
        var notification = new SeedGrowthNotification(
            SeedId: Guid.NewGuid(),
            SeedTypeCode: "treasure_chest",
            OwnerId: Guid.NewGuid(),
            OwnerType: EntityType.Character,
            DomainChanges: new List<DomainChange>(),
            TotalGrowth: 10f,
            CrossPollinated: false,
            Source: "test");

        await listener.OnGrowthRecordedAsync(notification, CancellationToken.None);

        _mockEntityQueryStore.Verify(
            q => q.QueryAsync(It.IsAny<Expression<Func<GenesisEntityModel, bool>>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task OnGrowthRecordedAsync_GenesisSeed_InvalidatesCapabilityCache()
    {
        var entityId = Guid.NewGuid();
        var seedId = Guid.NewGuid();
        var entity = CreateEntity(entityId: entityId, seedId: seedId);

        _mockEntityQueryStore
            .Setup(q => q.QueryAsync(It.IsAny<Expression<Func<GenesisEntityModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GenesisEntityModel> { entity });

        var notification = new SeedGrowthNotification(
            SeedId: seedId,
            SeedTypeCode: "treasure_chest",
            OwnerId: entityId,
            OwnerType: EntityType.Other,
            DomainChanges: new List<DomainChange>(),
            TotalGrowth: 10f,
            CrossPollinated: false,
            Source: "test");

        var listener = CreateListener();
        await listener.OnGrowthRecordedAsync(notification, CancellationToken.None);

        _mockCapsCacheStore.Verify(
            s => s.DeleteAsync(GenesisService.BuildCapsCacheKey(entityId), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
