#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.CharacterEncounter;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Worldstate;

namespace BeyondImmersion.BannouService.CharacterEncounter.Tests;

/// <summary>
/// Unit tests for MemoryDecaySchedulerService game-time integration.
/// Tests the background worker's branching on DecayTimeSource config.
/// Uses the Capture Pattern per TESTING-PATTERNS.md.
/// </summary>
public class MemoryDecaySchedulerServiceTests
{
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IServiceScope> _mockScope;
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
    private readonly Mock<IServiceProvider> _mockScopedProvider;
    private readonly Mock<ILogger<MemoryDecaySchedulerService>> _mockLogger;
    private readonly CharacterEncounterServiceConfiguration _configuration;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;

    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IWorldstateClient> _mockWorldstateClient;

    private readonly Mock<IStateStore<GlobalCharacterIndexData>> _mockGlobalIndexStore;
    private readonly Mock<IStateStore<CharacterIndexData>> _mockCharIndexStore;
    private readonly Mock<IStateStore<PerspectiveData>> _mockPerspectiveStore;
    private readonly Mock<IStateStore<EncounterData>> _mockEncounterStore;

    private readonly Guid _characterId = Guid.NewGuid();
    private readonly Guid _encounterId = Guid.NewGuid();
    private readonly Guid _perspectiveId = Guid.NewGuid();
    private readonly Guid _realmId = Guid.NewGuid();

    public MemoryDecaySchedulerServiceTests()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockScope = new Mock<IServiceScope>();
        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        _mockScopedProvider = new Mock<IServiceProvider>();
        _mockLogger = new Mock<ILogger<MemoryDecaySchedulerService>>();
        _configuration = new CharacterEncounterServiceConfiguration
        {
            MemoryDecayEnabled = true,
            MemoryDecayMode = MemoryDecayMode.Scheduled,
            ScheduledDecayStartupDelaySeconds = 0,
            ScheduledDecayCheckIntervalMinutes = 1,
            MemoryDecayIntervalHours = 24,
            MemoryDecayRate = 0.05,
            MemoryFadeThreshold = 0.1,
            DecayTimeSource = TimeSource.GameTime
        };
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();

        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockWorldstateClient = new Mock<IWorldstateClient>();

        _mockGlobalIndexStore = new Mock<IStateStore<GlobalCharacterIndexData>>();
        _mockCharIndexStore = new Mock<IStateStore<CharacterIndexData>>();
        _mockPerspectiveStore = new Mock<IStateStore<PerspectiveData>>();
        _mockEncounterStore = new Mock<IStateStore<EncounterData>>();

        // Wire up scope
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(IServiceScopeFactory)))
            .Returns(_mockScopeFactory.Object);
        _mockScopeFactory.Setup(f => f.CreateScope()).Returns(_mockScope.Object);
        _mockScope.Setup(s => s.ServiceProvider).Returns(_mockScopedProvider.Object);

        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IStateStoreFactory)))
            .Returns(_mockStateStoreFactory.Object);
        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IMessageBus)))
            .Returns(_mockMessageBus.Object);
        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IWorldstateClient)))
            .Returns(_mockWorldstateClient.Object);

        // Wire up stores — all use the same StateStoreDefinitions.CharacterEncounter
        _mockStateStoreFactory.Setup(f => f.GetStore<GlobalCharacterIndexData>(StateStoreDefinitions.CharacterEncounter))
            .Returns(_mockGlobalIndexStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<CharacterIndexData>(StateStoreDefinitions.CharacterEncounter))
            .Returns(_mockCharIndexStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter))
            .Returns(_mockPerspectiveStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<EncounterData>(StateStoreDefinitions.CharacterEncounter))
            .Returns(_mockEncounterStore.Object);

        // Default message bus succeeds
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private MemoryDecaySchedulerService CreateService()
    {
        return new MemoryDecaySchedulerService(
            _mockServiceProvider.Object,
            _mockLogger.Object,
            _configuration,
            _mockTelemetryProvider.Object);
    }

    private async Task RunOneCycleAsync()
    {
        using var service = CreateService();
        using var cts = new CancellationTokenSource();
        var task = service.StartAsync(cts.Token);
        await Task.Delay(500);
        cts.Cancel();
        try { await task; }
        catch (OperationCanceledException) { }
        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ScheduledDecay_GameTimeSource_CallsWorldstateForEncounterRealm()
    {
        // Arrange: one character, one perspective tied to an encounter with a realmId
        _mockGlobalIndexStore
            .Setup(s => s.GetAsync("global-char-idx", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalCharacterIndexData { CharacterIds = new List<Guid> { _characterId } });

        _mockCharIndexStore
            .Setup(s => s.GetAsync($"char-idx-{_characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterIndexData
            {
                CharacterId = _characterId,
                PerspectiveIds = new List<Guid> { _perspectiveId }
            });

        var createdAtUnix = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeSeconds();
        _mockPerspectiveStore
            .Setup(s => s.GetWithETagAsync($"pers-{_perspectiveId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new PerspectiveData
            {
                PerspectiveId = _perspectiveId,
                EncounterId = _encounterId,
                CharacterId = _characterId,
                EmotionalImpact = EmotionalImpact.Anger,
                MemoryStrength = 1.0f,
                LastDecayedAtUnix = null,
                CreatedAtUnix = createdAtUnix
            }, "etag-1"));

        _mockEncounterStore
            .Setup(s => s.GetAsync($"enc-{_encounterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EncounterData
            {
                EncounterId = _encounterId,
                RealmId = _realmId,
                EncounterTypeCode = "COMBAT",
                Outcome = EncounterOutcome.Negative,
                ParticipantIds = new List<Guid> { _characterId, Guid.NewGuid() },
                Timestamp = createdAtUnix,
                CreatedAtUnix = createdAtUnix
            });

        // Worldstate returns 5 game days elapsed (at 24:1 ratio, 5 hours real time = 5 game days)
        _mockWorldstateClient
            .Setup(c => c.GetElapsedGameTimeAsync(
                It.Is<GetElapsedGameTimeRequest>(r => r.RealmId == _realmId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetElapsedGameTimeResponse
            {
                TotalGameSeconds = 5 * 86400, // 5 game days
                GameDays = 5,
                GameHours = 0,
                GameMinutes = 0
            });

        PerspectiveData? savedPerspective = null;
        _mockPerspectiveStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<PerspectiveData>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, PerspectiveData, string, StateOptions?, CancellationToken>((_, p, _, _, _) => savedPerspective = p)
            .ReturnsAsync("new-etag");

        // Act
        await RunOneCycleAsync();

        // Assert: Worldstate was called with the encounter's realmId
        _mockWorldstateClient.Verify(c => c.GetElapsedGameTimeAsync(
            It.Is<GetElapsedGameTimeRequest>(r => r.RealmId == _realmId),
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert: perspective was decayed using game-time
        // 5 game days / 24h interval = 5 intervals, 5 * 0.05 rate = 0.25 decay
        // 1.0 - 0.25 = 0.75
        Assert.NotNull(savedPerspective);
        Assert.True(savedPerspective.MemoryStrength > 0.7f && savedPerspective.MemoryStrength < 0.8f,
            $"Expected memory strength ~0.75 after 5 game-day intervals, got {savedPerspective.MemoryStrength}");
    }

    [Fact]
    public async Task ScheduledDecay_RealTimeSource_DoesNotCallWorldstate()
    {
        // Arrange: RealTime mode
        _configuration.DecayTimeSource = TimeSource.RealTime;

        _mockGlobalIndexStore
            .Setup(s => s.GetAsync("global-char-idx", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalCharacterIndexData { CharacterIds = new List<Guid> { _characterId } });

        _mockCharIndexStore
            .Setup(s => s.GetAsync($"char-idx-{_characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterIndexData
            {
                CharacterId = _characterId,
                PerspectiveIds = new List<Guid> { _perspectiveId }
            });

        var createdAtUnix = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeSeconds();
        _mockPerspectiveStore
            .Setup(s => s.GetWithETagAsync($"pers-{_perspectiveId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new PerspectiveData
            {
                PerspectiveId = _perspectiveId,
                EncounterId = _encounterId,
                CharacterId = _characterId,
                EmotionalImpact = EmotionalImpact.Respect,
                MemoryStrength = 1.0f,
                LastDecayedAtUnix = null,
                CreatedAtUnix = createdAtUnix
            }, "etag-1"));

        _mockPerspectiveStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<PerspectiveData>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-etag");

        // Act
        await RunOneCycleAsync();

        // Assert: Worldstate was NOT called
        _mockWorldstateClient.Verify(c => c.GetElapsedGameTimeAsync(
            It.IsAny<GetElapsedGameTimeRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);

        // Assert: encounter store was NOT read (no need for realmId in RealTime mode)
        _mockEncounterStore.Verify(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ScheduledDecay_GameTimeSource_WorldstateUnavailable_SkipsPerspective()
    {
        // Arrange: GameTime mode, Worldstate throws
        _mockGlobalIndexStore
            .Setup(s => s.GetAsync("global-char-idx", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalCharacterIndexData { CharacterIds = new List<Guid> { _characterId } });

        _mockCharIndexStore
            .Setup(s => s.GetAsync($"char-idx-{_characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterIndexData
            {
                CharacterId = _characterId,
                PerspectiveIds = new List<Guid> { _perspectiveId }
            });

        var createdAtUnix = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeSeconds();
        _mockPerspectiveStore
            .Setup(s => s.GetWithETagAsync($"pers-{_perspectiveId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new PerspectiveData
            {
                PerspectiveId = _perspectiveId,
                EncounterId = _encounterId,
                CharacterId = _characterId,
                EmotionalImpact = EmotionalImpact.Fear,
                MemoryStrength = 0.5f,
                LastDecayedAtUnix = null,
                CreatedAtUnix = createdAtUnix
            }, "etag-1"));

        _mockEncounterStore
            .Setup(s => s.GetAsync($"enc-{_encounterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EncounterData
            {
                EncounterId = _encounterId,
                RealmId = _realmId,
                EncounterTypeCode = "COMBAT",
                Outcome = EncounterOutcome.Negative,
                ParticipantIds = new List<Guid> { _characterId },
                Timestamp = createdAtUnix,
                CreatedAtUnix = createdAtUnix
            });

        _mockWorldstateClient
            .Setup(c => c.GetElapsedGameTimeAsync(It.IsAny<GetElapsedGameTimeRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Worldstate unavailable", 503, null, null, null));

        // Act
        await RunOneCycleAsync();

        // Assert: perspective was NOT saved (Worldstate unavailable — skipped)
        _mockPerspectiveStore.Verify(s => s.TrySaveAsync(
            It.IsAny<string>(), It.IsAny<PerspectiveData>(), It.IsAny<string>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
